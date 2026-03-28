using System.Security.Cryptography.X509Certificates;
using System.Text;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Core.Telemetry;
using Directory.Ldap.Protocol;
using Directory.Ldap.Protocol.Messages;
using Directory.Ldap.Server;
using Directory.Security.Apds;
using Microsoft.Extensions.Logging;

namespace Directory.Ldap.Handlers;

/// <summary>
/// Handles LDAP Bind operations (simple and SASL).
/// Delegates GSSAPI/SPNEGO/NTLM to SaslHandler for multi-step authentication.
/// Uses ApdsLogonProcessor for full MS-APDS account restriction checking.
/// </summary>
public class BindHandler : ILdapOperationHandler
{
    private readonly IDirectoryStore _store;
    private readonly IPasswordPolicy _passwordPolicy;
    private readonly ApdsLogonProcessor _apdsProcessor;
    private readonly SaslHandler _saslHandler;
    private readonly SaslGssapiHandler _gssapiHandler;
    private readonly IAccessControlService _acl;
    private readonly ILogger<BindHandler> _logger;

    public LdapOperation Operation => LdapOperation.BindRequest;

    public BindHandler(
        IDirectoryStore store,
        IPasswordPolicy passwordPolicy,
        ApdsLogonProcessor apdsProcessor,
        SaslHandler saslHandler,
        SaslGssapiHandler gssapiHandler,
        IAccessControlService acl,
        ILogger<BindHandler> logger)
    {
        _store = store;
        _passwordPolicy = passwordPolicy;
        _apdsProcessor = apdsProcessor;
        _saslHandler = saslHandler;
        _gssapiHandler = gssapiHandler;
        _acl = acl;
        _logger = logger;
    }

    public async Task HandleAsync(LdapMessage request, ILdapResponseWriter writer, LdapConnectionState state, CancellationToken ct)
    {
        var bindReq = BindRequest.Decode(request.ProtocolOpData);

        _logger.LogDebug("Bind request from {Endpoint}: name={Name}, simple={Simple}",
            state.RemoteEndPoint, bindReq.Name, bindReq.IsSimple);

        DirectoryMetrics.AuthAttempts.Add(1, new KeyValuePair<string, object>("mechanism", bindReq.IsSimple ? "simple" : bindReq.SaslMechanism ?? "unknown"));

        BindResponse response;

        if (bindReq.IsSimple)
        {
            response = await HandleSimpleBindAsync(bindReq, state, ct);
        }
        else if (bindReq.SaslMechanism is not null)
        {
            response = await HandleSaslBindAsync(bindReq, state, ct);
        }
        else
        {
            response = new BindResponse
            {
                ResultCode = LdapResultCode.AuthMethodNotSupported,
                DiagnosticMessage = "Unsupported authentication method",
            };
        }

        await writer.WriteMessageAsync(request.MessageId, response.Encode(), ct: ct);
    }

    private async Task<BindResponse> HandleSimpleBindAsync(BindRequest bindReq, LdapConnectionState state, CancellationToken ct)
    {
        var password = Encoding.UTF8.GetString(bindReq.SimpleCredentials);

        // Anonymous bind (empty DN and empty password)
        if (string.IsNullOrEmpty(bindReq.Name) && string.IsNullOrEmpty(password))
        {
            state.BindStatus = BindState.Anonymous;
            state.BoundDn = null;

            return new BindResponse { ResultCode = LdapResultCode.Success };
        }

        // Unauthenticated bind (DN with empty password) — treat as anonymous
        if (string.IsNullOrEmpty(password))
        {
            state.BindStatus = BindState.Anonymous;
            state.BoundDn = null;

            return new BindResponse { ResultCode = LdapResultCode.Success };
        }

        // Authenticated simple bind — route through APDS for full account restriction checking
        try
        {
            // Extract domain from the bind DN or use the connection's domain
            var domain = ExtractDomainFromDn(bindReq.Name) ?? state.DomainDn;
            var username = ExtractUsernameFromDn(bindReq.Name) ?? bindReq.Name;

            var logonResult = await _apdsProcessor.ProcessInteractiveLogonAsync(
                state.TenantId, domain, username, password, ct);

            if (logonResult.Status == NtStatus.StatusSuccess && logonResult.User is not null)
            {
                state.BindStatus = BindState.SimpleBound;
                state.BoundDn = logonResult.User.DistinguishedName;
                state.BoundSid = logonResult.User.ObjectSid;

                // Resolve and cache the caller's transitive group SIDs for ACL checks
                await ResolveAndCacheGroupSidsAsync(state, ct);

                _logger.LogInformation("Simple bind success for {DN} from {Endpoint}", bindReq.Name, state.RemoteEndPoint);

                return new BindResponse { ResultCode = LdapResultCode.Success };
            }

            _logger.LogWarning("Simple bind failed for {DN} from {Endpoint}: {Status}",
                bindReq.Name, state.RemoteEndPoint, logonResult.Status);

            return new BindResponse
            {
                ResultCode = MapNtStatusToLdap(logonResult.Status),
                DiagnosticMessage = MapNtStatusToMessage(logonResult.Status),
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during simple bind for {DN}", bindReq.Name);

            return new BindResponse
            {
                ResultCode = LdapResultCode.OperationsError,
                DiagnosticMessage = "Internal error during authentication",
            };
        }
    }

    private async Task<BindResponse> HandleSaslBindAsync(BindRequest bindReq, LdapConnectionState state, CancellationToken ct)
    {
        _logger.LogDebug("SASL bind attempt with mechanism {Mechanism}", bindReq.SaslMechanism);

        // SASL PLAIN support (handled inline — simple credential extraction)
        if (bindReq.SaslMechanism == LdapConstants.SaslPlain && bindReq.SaslCredentials is not null)
        {
            return await HandleSaslPlainAsync(bindReq, state, ct);
        }

        // GSSAPI (Kerberos) — delegate to SaslGssapiHandler for multi-step exchange
        if (bindReq.SaslMechanism == LdapConstants.SaslGssApi)
        {
            var token = bindReq.SaslCredentials ?? [];
            var gssResult = await _gssapiHandler.ProcessStepAsync(token, state, ct);
            var response = MapSaslResult(gssResult.ToSaslBindResult());
            if (gssResult.IsSuccess)
                await ResolveAndCacheGroupSidsAsync(state, ct);
            return response;
        }

        // GSS-SPNEGO (Kerberos or NTLM negotiation) — delegate to SaslHandler
        if (bindReq.SaslMechanism == LdapConstants.SaslNtlmSsp && bindReq.SaslCredentials is not null)
        {
            var result = await _saslHandler.HandleSpnegoAsync(bindReq.SaslCredentials, state, ct);
            var response = MapSaslResult(result);
            if (result.IsSuccess)
                await ResolveAndCacheGroupSidsAsync(state, ct);
            return response;
        }

        // EXTERNAL — use TLS client certificate identity
        if (bindReq.SaslMechanism == LdapConstants.SaslExternal)
        {
            if (!state.IsTls)
            {
                return new BindResponse
                {
                    ResultCode = LdapResultCode.InappropriateAuthentication,
                    DiagnosticMessage = "EXTERNAL mechanism requires TLS",
                };
            }

            return await HandleSaslExternalAsync(state, ct);
        }

        // Unknown SASL mechanism
        return new BindResponse
        {
            ResultCode = LdapResultCode.AuthMethodNotSupported,
            DiagnosticMessage = $"SASL mechanism not supported: {bindReq.SaslMechanism}",
        };
    }

    /// <summary>
    /// Maps a SaslBindResult from SaslHandler into an LDAP BindResponse,
    /// handling success, failure, and SASL continuation (SaslBindInProgress).
    /// </summary>
    private static BindResponse MapSaslResult(SaslBindResult result)
    {
        if (result.IsSuccess)
        {
            return new BindResponse
            {
                ResultCode = LdapResultCode.Success,
                ServerSaslCreds = result.ResponseToken,
            };
        }

        if (result.NeedsContinuation)
        {
            return new BindResponse
            {
                ResultCode = LdapResultCode.SaslBindInProgress,
                ServerSaslCreds = result.ResponseToken,
            };
        }

        return new BindResponse
        {
            ResultCode = LdapResultCode.InvalidCredentials,
            DiagnosticMessage = result.ErrorMessage ?? "SASL authentication failed",
        };
    }

    private async Task<BindResponse> HandleSaslPlainAsync(BindRequest bindReq, LdapConnectionState state, CancellationToken ct)
    {
        // SASL PLAIN format: [authzid] NUL authcid NUL passwd
        var creds = bindReq.SaslCredentials;
        var parts = SplitSaslPlain(creds);

        if (parts is null)
        {
            return new BindResponse
            {
                ResultCode = LdapResultCode.InvalidCredentials,
                DiagnosticMessage = "Malformed SASL PLAIN credentials",
            };
        }

        var (_, authcId, password) = parts.Value;

        // Route through APDS for full account restriction checking
        var domain = state.DomainDn;
        var logonResult = await _apdsProcessor.ProcessInteractiveLogonAsync(
            state.TenantId, domain, authcId, password, ct);

        if (logonResult.Status == NtStatus.StatusSuccess && logonResult.User is not null)
        {
            state.BindStatus = BindState.SaslBound;
            state.BoundDn = logonResult.User.DistinguishedName;
            state.BoundSid = logonResult.User.ObjectSid;

            // Resolve and cache the caller's transitive group SIDs for ACL checks
            await ResolveAndCacheGroupSidsAsync(state, ct);

            return new BindResponse { ResultCode = LdapResultCode.Success };
        }

        return new BindResponse
        {
            ResultCode = MapNtStatusToLdap(logonResult.Status),
            DiagnosticMessage = MapNtStatusToMessage(logonResult.Status),
        };
    }

    /// <summary>
    /// Handles SASL EXTERNAL authentication by mapping the client certificate to a directory user.
    /// The client certificate is obtained during the TLS handshake (mutual TLS) and stored
    /// in LdapConnectionState.ClientCertificate. The mapping strategy follows MS-ADTS:
    ///   1. Check the SAN (Subject Alternative Name) for a UPN — look up the user by userPrincipalName.
    ///   2. If no SAN/UPN, extract CN from the certificate Subject and search by sAMAccountName or CN.
    ///   3. If a match is found, bind the connection to that principal.
    /// </summary>
    private async Task<BindResponse> HandleSaslExternalAsync(LdapConnectionState state, CancellationToken ct)
    {
        var cert = state.ClientCertificate;
        if (cert == null)
        {
            _logger.LogWarning("EXTERNAL bind from {Endpoint}: no client certificate presented", state.RemoteEndPoint);
            return new BindResponse
            {
                ResultCode = LdapResultCode.InappropriateAuthentication,
                DiagnosticMessage = "EXTERNAL mechanism requires a client certificate (mutual TLS)",
            };
        }

        _logger.LogDebug("EXTERNAL bind from {Endpoint}: Subject={Subject}, Thumbprint={Thumbprint}",
            state.RemoteEndPoint, cert.Subject, cert.Thumbprint);

        // Strategy 1: Extract UPN from Subject Alternative Name (SAN) — this is the preferred mapping.
        // .NET's GetNameInfo(UpnName) reads the SAN otherName with OID 1.3.6.1.4.1.311.20.2.3 (MS UPN).
        // Falls back to email name if UPN is not present.
        string upn = cert.GetNameInfo(X509NameType.UpnName, false);
        if (string.IsNullOrEmpty(upn))
        {
            upn = cert.GetNameInfo(X509NameType.EmailName, false);
        }

        DirectoryObject user = null;

        // Try UPN lookup first
        if (!string.IsNullOrEmpty(upn))
        {
            _logger.LogDebug("EXTERNAL bind: looking up user by UPN {UPN}", upn);
            var result = await _store.SearchAsync(
                state.TenantId, state.DomainDn, SearchScope.WholeSubtree,
                new EqualityFilterNode("userPrincipalName", upn),
                new[] { "distinguishedName", "objectSid", "userAccountControl", "userPrincipalName" },
                sizeLimit: 1, ct: ct);

            if (result.Entries.Count > 0)
                user = result.Entries[0];
        }

        // Strategy 2: Extract CN from the certificate Subject and search by sAMAccountName
        if (user == null)
        {
            var cn = cert.GetNameInfo(X509NameType.SimpleName, false);
            if (!string.IsNullOrEmpty(cn))
            {
                _logger.LogDebug("EXTERNAL bind: looking up user by CN/sAMAccountName {CN}", cn);
                var result = await _store.SearchAsync(
                    state.TenantId, state.DomainDn, SearchScope.WholeSubtree,
                    new OrFilterNode(new List<FilterNode>
                    {
                        new EqualityFilterNode("sAMAccountName", cn),
                        new EqualityFilterNode("cn", cn),
                    }),
                    new[] { "distinguishedName", "objectSid", "userAccountControl", "userPrincipalName" },
                    sizeLimit: 1, ct: ct);

                if (result.Entries.Count > 0)
                    user = result.Entries[0];
            }
        }

        if (user == null)
        {
            _logger.LogWarning("EXTERNAL bind from {Endpoint}: no matching user found for certificate Subject={Subject}",
                state.RemoteEndPoint, cert.Subject);
            return new BindResponse
            {
                ResultCode = LdapResultCode.InvalidCredentials,
                DiagnosticMessage = "No directory user matches the client certificate",
            };
        }

        // Check if the account is disabled (UF_ACCOUNTDISABLE = 0x0002)
        if ((user.UserAccountControl & 0x0002) != 0)
        {
            _logger.LogWarning("EXTERNAL bind from {Endpoint}: matched user {DN} is disabled",
                state.RemoteEndPoint, user.DistinguishedName);
            return new BindResponse
            {
                ResultCode = LdapResultCode.InvalidCredentials,
                DiagnosticMessage = "Account is disabled",
            };
        }

        // Bind successful
        state.BindStatus = BindState.SaslBound;
        state.BoundDn = user.DistinguishedName;
        state.BoundSid = user.ObjectSid;

        await ResolveAndCacheGroupSidsAsync(state, ct);

        _logger.LogInformation("EXTERNAL bind success for {DN} via certificate {Thumbprint} from {Endpoint}",
            user.DistinguishedName, cert.Thumbprint, state.RemoteEndPoint);

        return new BindResponse { ResultCode = LdapResultCode.Success };
    }

    /// <summary>
    /// Resolve the bound principal's transitive group SIDs and cache on the connection state.
    /// </summary>
    private async Task ResolveAndCacheGroupSidsAsync(LdapConnectionState state, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(state.BoundSid))
            return;

        try
        {
            state.GroupSids = await _acl.ResolveCallerGroupsAsync(state.BoundSid, _store, state.TenantId, ct);
            _logger.LogDebug("Resolved {Count} group SIDs for {Sid}", state.GroupSids.Count, state.BoundSid);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve group SIDs for {Sid}, using empty set", state.BoundSid);
            state.GroupSids = new HashSet<string>();
        }
    }

    /// <summary>
    /// Maps MS-APDS NtStatus codes to appropriate LDAP result codes.
    /// </summary>
    private static LdapResultCode MapNtStatusToLdap(NtStatus status) => status switch
    {
        NtStatus.StatusAccountDisabled => LdapResultCode.InvalidCredentials,
        NtStatus.StatusAccountLockedOut => LdapResultCode.InvalidCredentials,
        NtStatus.StatusAccountExpired => LdapResultCode.InvalidCredentials,
        NtStatus.StatusPasswordExpired => LdapResultCode.InvalidCredentials,
        NtStatus.StatusPasswordMustChange => LdapResultCode.InvalidCredentials,
        NtStatus.StatusInvalidLogonHours => LdapResultCode.InvalidCredentials,
        NtStatus.StatusInvalidWorkstation => LdapResultCode.InvalidCredentials,
        NtStatus.StatusNoSuchUser => LdapResultCode.InvalidCredentials,
        NtStatus.StatusWrongPassword => LdapResultCode.InvalidCredentials,
        NtStatus.StatusLogonFailure => LdapResultCode.InvalidCredentials,
        _ => LdapResultCode.OperationsError,
    };

    /// <summary>
    /// Maps NtStatus to a diagnostic message for LDAP error data.
    /// AD includes detailed error info in the diagnosticMessage per MS-ADTS 3.1.1.3.3.5.
    /// </summary>
    private static string MapNtStatusToMessage(NtStatus status) => status switch
    {
        NtStatus.StatusAccountDisabled => "80090346: LdapErr: DSID-0C0906DC, comment: AcceptSecurityContext error, data 533, v4563",
        NtStatus.StatusAccountLockedOut => "80090346: LdapErr: DSID-0C0906DC, comment: AcceptSecurityContext error, data 775, v4563",
        NtStatus.StatusAccountExpired => "80090346: LdapErr: DSID-0C0906DC, comment: AcceptSecurityContext error, data 701, v4563",
        NtStatus.StatusPasswordExpired => "80090346: LdapErr: DSID-0C0906DC, comment: AcceptSecurityContext error, data 532, v4563",
        NtStatus.StatusPasswordMustChange => "80090346: LdapErr: DSID-0C0906DC, comment: AcceptSecurityContext error, data 773, v4563",
        NtStatus.StatusInvalidLogonHours => "80090346: LdapErr: DSID-0C0906DC, comment: AcceptSecurityContext error, data 530, v4563",
        NtStatus.StatusInvalidWorkstation => "80090346: LdapErr: DSID-0C0906DC, comment: AcceptSecurityContext error, data 531, v4563",
        NtStatus.StatusNoSuchUser => "80090346: LdapErr: DSID-0C0906DC, comment: AcceptSecurityContext error, data 525, v4563",
        NtStatus.StatusWrongPassword or NtStatus.StatusLogonFailure => "80090346: LdapErr: DSID-0C0906DC, comment: AcceptSecurityContext error, data 52e, v4563",
        _ => "Internal authentication error",
    };

    /// <summary>
    /// Extracts the domain DN from a user's distinguished name.
    /// For "CN=admin,CN=Users,DC=contoso,DC=com" returns "DC=contoso,DC=com".
    /// </summary>
    private static string ExtractDomainFromDn(string dn)
    {
        if (string.IsNullOrEmpty(dn))
            return null;

        // If it's a UPN format (user@domain), extract domain part
        if (dn.Contains('@'))
        {
            var domain = dn.Split('@')[1];
            return "DC=" + domain.ToLowerInvariant().Replace(".", ",DC=");
        }

        // If it's a DN format, extract DC components
        var components = dn.Split(',', StringSplitOptions.TrimEntries);
        var dcParts = components.Where(c => c.StartsWith("DC=", StringComparison.OrdinalIgnoreCase)).ToArray();
        return dcParts.Length > 0 ? string.Join(",", dcParts) : null;
    }

    /// <summary>
    /// Extracts the username from a DN or UPN.
    /// For "CN=admin,CN=Users,DC=contoso,DC=com" returns "admin".
    /// For "admin@contoso.com" returns "admin".
    /// </summary>
    private static string ExtractUsernameFromDn(string dn)
    {
        if (string.IsNullOrEmpty(dn))
            return null;

        // UPN format
        if (dn.Contains('@'))
            return dn.Split('@')[0];

        // DN format - extract CN value
        if (dn.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
        {
            var firstComma = dn.IndexOf(',');
            return firstComma > 3 ? dn[3..firstComma] : dn[3..];
        }

        // Could be just a sAMAccountName
        return dn;
    }

    private static (string AuthzId, string AuthcId, string Password)? SplitSaslPlain(byte[] data)
    {
        // Format: [authzid] NUL authcid NUL passwd
        var firstNull = Array.IndexOf(data, (byte)0);
        if (firstNull < 0) return null;

        var secondNull = Array.IndexOf(data, (byte)0, firstNull + 1);
        if (secondNull < 0) return null;

        var authzId = firstNull > 0 ? Encoding.UTF8.GetString(data, 0, firstNull) : null;
        var authcId = Encoding.UTF8.GetString(data, firstNull + 1, secondNull - firstNull - 1);
        var password = Encoding.UTF8.GetString(data, secondNull + 1, data.Length - secondNull - 1);

        return (authzId, authcId, password);
    }
}
