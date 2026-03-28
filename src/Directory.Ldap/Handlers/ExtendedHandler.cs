using System.Formats.Asn1;
using System.Text;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Ldap.Protocol;
using Directory.Ldap.Protocol.Messages;
using Directory.Ldap.Server;
using Microsoft.Extensions.Logging;

namespace Directory.Ldap.Handlers;

public class ExtendedHandler : ILdapOperationHandler
{
    private readonly IDirectoryStore _store;
    private readonly IPasswordPolicy _passwordPolicy;
    private readonly ILogger<ExtendedHandler> _logger;

    public LdapOperation Operation => LdapOperation.ExtendedRequest;

    public ExtendedHandler(
        IDirectoryStore store,
        IPasswordPolicy passwordPolicy,
        ILogger<ExtendedHandler> logger)
    {
        _store = store;
        _passwordPolicy = passwordPolicy;
        _logger = logger;
    }

    public async Task HandleAsync(LdapMessage request, ILdapResponseWriter writer, LdapConnectionState state, CancellationToken ct)
    {
        var extReq = ExtendedRequest.Decode(request.ProtocolOpData);

        _logger.LogDebug("Extended request: {OID}", extReq.RequestName);

        switch (extReq.RequestName)
        {
            case LdapConstants.OidWhoAmI:
                await HandleWhoAmIAsync(request.MessageId, state, writer, ct);
                break;

            case LdapConstants.OidStartTls:
                // StartTLS requires TLS certificate configuration — respond with success but note
                // that the actual TLS upgrade happens in the connection handler
                await HandleStartTlsAsync(request.MessageId, state, writer, ct);
                break;

            case LdapConstants.OidFastBind:
                await HandleFastBindAsync(request.MessageId, state, writer, ct);
                break;

            case LdapConstants.OidPasswordModify:
                await HandlePasswordModifyAsync(request.MessageId, extReq.RequestValue, state, writer, ct);
                break;

            case LdapConstants.OidTtlRefresh:
                await HandleTtlRefreshAsync(request.MessageId, extReq.RequestValue, state, writer, ct);
                break;

            default:
                var response = new ExtendedResponse
                {
                    ResultCode = LdapResultCode.ProtocolError,
                    DiagnosticMessage = $"Unsupported extended operation: {extReq.RequestName}",
                };
                await writer.WriteMessageAsync(request.MessageId, response.Encode(), ct: ct);
                break;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // WhoAmI (1.3.6.1.4.1.4203.1.11.3) — Enhanced with group memberships
    // ─────────────────────────────────────────────────────────────────────

    private async Task HandleWhoAmIAsync(int messageId, LdapConnectionState state, ILdapResponseWriter writer, CancellationToken ct)
    {
        if (state.BindStatus == BindState.Anonymous)
        {
            var anonResponse = new ExtendedResponse
            {
                ResultCode = LdapResultCode.Success,
                ResponseValue = Encoding.UTF8.GetBytes(""),
            };
            await writer.WriteMessageAsync(messageId, anonResponse.Encode(), ct: ct);
            return;
        }

        // Build the full authorization identity including group memberships
        var sb = new StringBuilder();
        sb.Append($"dn:{state.BoundDn}");

        // Look up the user's group memberships to include in the response
        if (!string.IsNullOrEmpty(state.BoundDn) && !string.IsNullOrEmpty(state.DomainDn))
        {
            try
            {
                var user = await _store.GetByDnAsync(state.TenantId, state.BoundDn, ct);
                if (user != null)
                {
                    // Include SID if available
                    if (!string.IsNullOrEmpty(user.ObjectSid))
                    {
                        sb.Append($"\nsid:{user.ObjectSid}");
                    }

                    // Include direct group memberships from the memberOf attribute
                    var memberOfAttr = user.GetAttribute("memberOf");
                    if (memberOfAttr != null)
                    {
                        var groups = memberOfAttr.GetStrings().ToList();
                        if (groups.Count > 0)
                        {
                            sb.Append("\ngroups:");
                            sb.Append(string.Join(";", groups));
                        }
                    }

                    // Include sAMAccountName
                    if (!string.IsNullOrEmpty(user.SAMAccountName))
                    {
                        sb.Append($"\nu:{user.SAMAccountName}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to retrieve group memberships for WhoAmI response");
                // Fall through with basic identity only
            }
        }

        var response = new ExtendedResponse
        {
            ResultCode = LdapResultCode.Success,
            ResponseValue = Encoding.UTF8.GetBytes(sb.ToString()),
        };

        await writer.WriteMessageAsync(messageId, response.Encode(), ct: ct);
    }

    // ─────────────────────────────────────────────────────────────────────
    // StartTLS (1.3.6.1.4.1.1466.20037)
    // ─────────────────────────────────────────────────────────────────────

    private async Task HandleStartTlsAsync(int messageId, LdapConnectionState state, ILdapResponseWriter writer, CancellationToken ct)
    {
        if (state.IsTls)
        {
            var response = new ExtendedResponse
            {
                ResultCode = LdapResultCode.OperationsError,
                DiagnosticMessage = "TLS already active",
                ResponseName = LdapConstants.OidStartTls,
            };
            await writer.WriteMessageAsync(messageId, response.Encode(), ct: ct);
            return;
        }

        // Check whether the connection handler supports TLS upgrade
        var upgradeable = writer as IStartTlsUpgradeable;
        if (upgradeable == null)
        {
            var notSupported = new ExtendedResponse
            {
                ResultCode = LdapResultCode.UnwillingToPerform,
                DiagnosticMessage = "StartTLS not supported by this connection handler",
                ResponseName = LdapConstants.OidStartTls,
            };
            await writer.WriteMessageAsync(messageId, notSupported.Encode(), ct: ct);
            return;
        }

        // Send success before upgrading — the client begins TLS handshake after receiving this
        var successResponse = new ExtendedResponse
        {
            ResultCode = LdapResultCode.Success,
            ResponseName = LdapConstants.OidStartTls,
        };
        await writer.WriteMessageAsync(messageId, successResponse.Encode(), ct: ct);

        // Now perform the actual TLS upgrade on the underlying stream
        var upgraded = await upgradeable.UpgradeToTlsAsync(ct);
        if (!upgraded)
        {
            _logger.LogWarning("StartTLS: TLS upgrade failed for connection (no certificate or handshake error). Connection will remain plain-text.");
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // FastBind (1.2.840.113556.1.4.1781) — Credential-only validation
    // ─────────────────────────────────────────────────────────────────────

    private async Task HandleFastBindAsync(int messageId, LdapConnectionState state, ILdapResponseWriter writer, CancellationToken ct)
    {
        _logger.LogDebug("FastBind: enabling credential-only bind mode for connection from {Endpoint}", state.RemoteEndPoint);

        // FastBind mode sets a flag on the connection. Subsequent simple binds
        // will only validate credentials without full session setup (no group
        // membership resolution, no token generation). This is used by applications
        // that just need to verify passwords (e.g., RADIUS servers, web apps).
        state.FastBindMode = true;

        var response = new ExtendedResponse
        {
            ResultCode = LdapResultCode.Success,
            ResponseName = LdapConstants.OidFastBind,
            DiagnosticMessage = "Fast bind mode enabled",
        };

        await writer.WriteMessageAsync(messageId, response.Encode(), ct: ct);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Password Modify (RFC 3062 - 1.3.6.1.4.1.4203.1.11.1)
    // ─────────────────────────────────────────────────────────────────────

    private async Task HandlePasswordModifyAsync(int messageId, byte[] requestValue, LdapConnectionState state, ILdapResponseWriter writer, CancellationToken ct)
    {
        // Require encrypted connection per MS-ADTS 3.1.1.3.1.5
        if (!state.IsTls)
        {
            var errResponse = new ExtendedResponse
            {
                ResultCode = LdapResultCode.ConfidentialityRequired,
                DiagnosticMessage = "Password modifications require an encrypted connection (TLS/SSL or SASL)",
            };
            await writer.WriteMessageAsync(messageId, errResponse.Encode(), ct: ct);
            return;
        }

        if (requestValue == null || requestValue.Length == 0)
        {
            var errResponse = new ExtendedResponse
            {
                ResultCode = LdapResultCode.ProtocolError,
                DiagnosticMessage = "Missing request value for password modify operation",
            };
            await writer.WriteMessageAsync(messageId, errResponse.Encode(), ct: ct);
            return;
        }

        // Decode the PasswdModifyRequestValue per RFC 3062:
        // PasswdModifyRequestValue ::= SEQUENCE {
        //     userIdentity [0] OCTET STRING OPTIONAL,
        //     oldPasswd    [1] OCTET STRING OPTIONAL,
        //     newPasswd    [2] OCTET STRING OPTIONAL
        // }
        string userIdentity = null;
        string oldPassword = null;
        string newPassword = null;

        try
        {
            var asnReader = new AsnReader(requestValue, AsnEncodingRules.BER);
            var sequence = asnReader.ReadSequence();

            while (sequence.HasData)
            {
                var tag = sequence.PeekTag();

                if (tag == BerHelper.ContextTag(0))
                {
                    userIdentity = Encoding.UTF8.GetString(sequence.ReadOctetString(BerHelper.ContextTag(0)));
                }
                else if (tag == BerHelper.ContextTag(1))
                {
                    oldPassword = Encoding.UTF8.GetString(sequence.ReadOctetString(BerHelper.ContextTag(1)));
                }
                else if (tag == BerHelper.ContextTag(2))
                {
                    newPassword = Encoding.UTF8.GetString(sequence.ReadOctetString(BerHelper.ContextTag(2)));
                }
                else
                {
                    // Skip unknown tags
                    sequence.ReadEncodedValue();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to decode password modify request");
            var errResponse = new ExtendedResponse
            {
                ResultCode = LdapResultCode.ProtocolError,
                DiagnosticMessage = "Invalid BER encoding in password modify request",
            };
            await writer.WriteMessageAsync(messageId, errResponse.Encode(), ct: ct);
            return;
        }

        // Determine the target user DN
        var targetDn = userIdentity ?? state.BoundDn;
        if (string.IsNullOrEmpty(targetDn))
        {
            var errResponse = new ExtendedResponse
            {
                ResultCode = LdapResultCode.UnwillingToPerform,
                DiagnosticMessage = "No target user specified and no authenticated user",
            };
            await writer.WriteMessageAsync(messageId, errResponse.Encode(), ct: ct);
            return;
        }

        if (string.IsNullOrEmpty(newPassword))
        {
            var errResponse = new ExtendedResponse
            {
                ResultCode = LdapResultCode.UnwillingToPerform,
                DiagnosticMessage = "New password is required",
            };
            await writer.WriteMessageAsync(messageId, errResponse.Encode(), ct: ct);
            return;
        }

        _logger.LogDebug("Password modify via RFC 3062 for {Target}", targetDn);

        // If oldPassword is provided, this is a user-initiated change — validate it
        if (oldPassword != null)
        {
            var isValid = await _passwordPolicy.ValidatePasswordAsync(state.TenantId, targetDn, oldPassword, ct);
            if (!isValid)
            {
                var errResponse = new ExtendedResponse
                {
                    ResultCode = LdapResultCode.InvalidCredentials,
                    DiagnosticMessage = "Current password is incorrect",
                };
                await writer.WriteMessageAsync(messageId, errResponse.Encode(), ct: ct);
                return;
            }
        }

        // Validate the user exists
        var user = await _store.GetByDnAsync(state.TenantId, targetDn, ct);
        if (user == null)
        {
            var errResponse = new ExtendedResponse
            {
                ResultCode = LdapResultCode.NoSuchObject,
                DiagnosticMessage = $"User not found: {targetDn}",
            };
            await writer.WriteMessageAsync(messageId, errResponse.Encode(), ct: ct);
            return;
        }

        // Check complexity requirements
        if (!_passwordPolicy.MeetsComplexityRequirements(newPassword, user.SAMAccountName))
        {
            var errResponse = new ExtendedResponse
            {
                ResultCode = LdapResultCode.ConstraintViolation,
                DiagnosticMessage = "New password does not meet complexity requirements",
            };
            await writer.WriteMessageAsync(messageId, errResponse.Encode(), ct: ct);
            return;
        }

        // Set the new password
        try
        {
            await _passwordPolicy.SetPasswordAsync(state.TenantId, targetDn, newPassword, ct);
            _logger.LogInformation("Password modified via RFC 3062 for {Target}", targetDn);

            var successResponse = new ExtendedResponse
            {
                ResultCode = LdapResultCode.Success,
            };
            await writer.WriteMessageAsync(messageId, successResponse.Encode(), ct: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set password for {Target}", targetDn);
            var errResponse = new ExtendedResponse
            {
                ResultCode = LdapResultCode.OperationsError,
                DiagnosticMessage = "Failed to set password",
            };
            await writer.WriteMessageAsync(messageId, errResponse.Encode(), ct: ct);
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // TTL Refresh / Dynamic Entries (RFC 2589 - 1.3.6.1.4.1.1466.101.119.1)
    // ─────────────────────────────────────────────────────────────────────

    private async Task HandleTtlRefreshAsync(int messageId, byte[] requestValue, LdapConnectionState state, ILdapResponseWriter writer, CancellationToken ct)
    {
        if (requestValue == null || requestValue.Length == 0)
        {
            var errResponse = new ExtendedResponse
            {
                ResultCode = LdapResultCode.ProtocolError,
                DiagnosticMessage = "Missing request value for TTL refresh",
            };
            await writer.WriteMessageAsync(messageId, errResponse.Encode(), ct: ct);
            return;
        }

        // Decode the request per RFC 2589:
        // SEQUENCE {
        //     entryName  LDAPDN,
        //     requestTtl INTEGER
        // }
        string entryDn = null;
        int requestTtl = 0;

        try
        {
            var asnReader = new AsnReader(requestValue, AsnEncodingRules.BER);
            var sequence = asnReader.ReadSequence();

            entryDn = Encoding.UTF8.GetString(sequence.ReadOctetString());
            requestTtl = (int)sequence.ReadInteger();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to decode TTL refresh request");
            var errResponse = new ExtendedResponse
            {
                ResultCode = LdapResultCode.ProtocolError,
                DiagnosticMessage = "Invalid BER encoding in TTL refresh request",
            };
            await writer.WriteMessageAsync(messageId, errResponse.Encode(), ct: ct);
            return;
        }

        _logger.LogDebug("TTL refresh: dn={Dn}, requestedTtl={Ttl}", entryDn, requestTtl);

        if (string.IsNullOrEmpty(entryDn))
        {
            var errResponse = new ExtendedResponse
            {
                ResultCode = LdapResultCode.ProtocolError,
                DiagnosticMessage = "Entry DN is required for TTL refresh",
            };
            await writer.WriteMessageAsync(messageId, errResponse.Encode(), ct: ct);
            return;
        }

        // Look up the entry
        var entry = await _store.GetByDnAsync(state.TenantId, entryDn, ct);
        if (entry == null)
        {
            var errResponse = new ExtendedResponse
            {
                ResultCode = LdapResultCode.NoSuchObject,
                DiagnosticMessage = $"Entry not found: {entryDn}",
            };
            await writer.WriteMessageAsync(messageId, errResponse.Encode(), ct: ct);
            return;
        }

        // Check if this is a dynamic entry (has entryTTL or dynamicObject objectClass)
        var isDynamic = entry.ObjectClass.Contains("dynamicObject", StringComparer.OrdinalIgnoreCase);

        if (!isDynamic)
        {
            var errResponse = new ExtendedResponse
            {
                ResultCode = LdapResultCode.UnwillingToPerform,
                DiagnosticMessage = "Entry is not a dynamic object (missing dynamicObject objectClass)",
            };
            await writer.WriteMessageAsync(messageId, errResponse.Encode(), ct: ct);
            return;
        }

        // Apply the TTL. Clamp to a reasonable range (minimum 1 second, maximum 31557600 = ~1 year)
        var grantedTtl = Math.Clamp(requestTtl, 1, 31557600);

        // Update the entryTTL attribute with the new expiration time
        var expirationTime = DateTimeOffset.UtcNow.AddSeconds(grantedTtl).ToFileTime();
        entry.SetAttribute("entryTTL", new DirectoryAttribute("entryTTL", grantedTtl.ToString()));
        entry.SetAttribute("ms-DS-Entry-Time-To-Die", new DirectoryAttribute("ms-DS-Entry-Time-To-Die", expirationTime.ToString()));

        await _store.UpdateAsync(entry, ct);

        _logger.LogInformation("TTL refresh: {Dn} granted TTL={Ttl}s", entryDn, grantedTtl);

        // Build the response per RFC 2589:
        // SEQUENCE {
        //     responseTtl INTEGER
        // }
        var responseWriter = new AsnWriter(AsnEncodingRules.BER);
        using (responseWriter.PushSequence())
        {
            responseWriter.WriteInteger(grantedTtl);
        }

        var response = new ExtendedResponse
        {
            ResultCode = LdapResultCode.Success,
            ResponseName = LdapConstants.OidTtlRefresh,
            ResponseValue = responseWriter.Encode(),
        };

        await writer.WriteMessageAsync(messageId, response.Encode(), ct: ct);
    }
}
