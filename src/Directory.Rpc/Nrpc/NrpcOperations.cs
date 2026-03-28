using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Directory.Core.Interfaces;
using Directory.Rpc.Dispatch;
using Directory.Rpc.Ndr;
using Microsoft.Extensions.Logging;

namespace Directory.Rpc.Nrpc;

/// <summary>
/// Implements the MS-NRPC Netlogon Remote Protocol operations as proper DCE/RPC
/// handlers with NDR marshalling. Provides DC locator, secure channel establishment,
/// pass-through NTLM authentication, machine password rotation, and domain info queries.
/// </summary>
public class NrpcOperations
{
    private readonly IDirectoryStore _store;
    private readonly IPasswordPolicy _passwordPolicy;
    private readonly INamingContextService _ncService;
    private readonly IRidAllocator _ridAllocator;
    private readonly IUserAccountControlService _uacService;
    private readonly ILogger<NrpcOperations> _logger;

    /// <summary>
    /// Active secure channel sessions keyed by computer name (case-insensitive).
    /// </summary>
    private readonly ConcurrentDictionary<string, SecureChannelSession> _sessions = new(StringComparer.OrdinalIgnoreCase);

    public NrpcOperations(
        IDirectoryStore store,
        IPasswordPolicy passwordPolicy,
        INamingContextService ncService,
        IRidAllocator ridAllocator,
        IUserAccountControlService uacService,
        ILogger<NrpcOperations> logger)
    {
        _store = store;
        _passwordPolicy = passwordPolicy;
        _ncService = ncService;
        _ridAllocator = ridAllocator;
        _uacService = uacService;
        _logger = logger;
    }

    // ========================================================================
    // Opnum 4 — NetrServerReqChallenge
    // MS-NRPC 3.5.4.4.1
    // ========================================================================

    /// <summary>
    /// Receives a client challenge and returns a server challenge,
    /// initiating the secure channel handshake.
    /// </summary>
    public Task<byte[]> NetrServerReqChallengeAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // [in, unique, string] LOGONSRV_HANDLE PrimaryName
        var primaryNamePtr = reader.ReadPointer();
        string primaryName = null;
        if (primaryNamePtr != 0)
            primaryName = reader.ReadConformantVaryingString();

        // [in, string] wchar_t* ComputerName
        var computerNamePtr = reader.ReadPointer();
        string computerName = computerNamePtr != 0 ? reader.ReadConformantVaryingString() : "";

        // [in] PNETLOGON_CREDENTIAL ClientChallenge (8 bytes)
        var clientChallenge = reader.ReadBytes(8).ToArray();

        _logger.LogDebug("NetrServerReqChallenge: Computer={Computer}", computerName);

        if (string.IsNullOrWhiteSpace(computerName))
        {
            return Task.FromResult(WriteStatusOnly(NrpcConstants.StatusInvalidParameter));
        }

        var serverChallenge = RandomNumberGenerator.GetBytes(8);

        // Store the pending session
        var session = new SecureChannelSession
        {
            ComputerName = computerName,
            ClientChallenge = clientChallenge,
            ServerChallenge = serverChallenge,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _sessions[computerName] = session;

        // Response: ServerChallenge (8 bytes), NTSTATUS
        var writer = new NdrWriter();
        writer.WriteBytes(serverChallenge);
        writer.WriteUInt32(NrpcConstants.StatusSuccess);

        return Task.FromResult(writer.ToArray());
    }

    // ========================================================================
    // Opnum 26 — NetrServerAuthenticate3
    // MS-NRPC 3.5.4.4.2
    // ========================================================================

    /// <summary>
    /// Completes the secure channel handshake by verifying the client credential
    /// and computing the session key from the shared secret (machine NT hash).
    /// </summary>
    public async Task<byte[]> NetrServerAuthenticate3Async(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // [in, unique, string] LOGONSRV_HANDLE PrimaryName
        var primaryNamePtr = reader.ReadPointer();
        string primaryName = null;
        if (primaryNamePtr != 0)
            primaryName = reader.ReadConformantVaryingString();

        // [in, string] wchar_t* AccountName
        var accountNamePtr = reader.ReadPointer();
        string accountName = accountNamePtr != 0 ? reader.ReadConformantVaryingString() : "";

        // [in] NETLOGON_SECURE_CHANNEL_TYPE SecureChannelType
        var secureChannelType = reader.ReadUInt16();

        // [in, string] wchar_t* ComputerName
        var computerNamePtr = reader.ReadPointer();
        string computerName = computerNamePtr != 0 ? reader.ReadConformantVaryingString() : "";

        // [in] PNETLOGON_CREDENTIAL ClientCredential (8 bytes)
        var clientCredentialBytes = reader.ReadBytes(8).ToArray();

        // [in, out] ULONG* NegotiateFlags
        var clientFlags = reader.ReadUInt32();

        _logger.LogDebug(
            "NetrServerAuthenticate3: Computer={Computer}, Account={Account}, Flags=0x{Flags:X8}",
            computerName, accountName, clientFlags);

        // Look up the pending session
        if (!_sessions.TryGetValue(computerName, out var session) ||
            session.ClientChallenge.Length == 0)
        {
            _logger.LogWarning("No pending challenge for computer {Computer}", computerName);
            return WriteAuth3Failure(NrpcConstants.StatusAccessDenied);
        }

        // Look up the machine account
        var domainNc = _ncService.GetDomainNc();
        var machineAccount = await _store.GetBySamAccountNameAsync(
            context.TenantId, domainNc.Dn, accountName, ct);

        if (machineAccount is null)
        {
            _logger.LogWarning("Machine account '{Account}' not found", accountName);
            return WriteAuth3Failure(NrpcConstants.StatusNoTrustSamAccount);
        }

        if (string.IsNullOrEmpty(machineAccount.NTHash))
        {
            _logger.LogWarning("Machine account '{Account}' has no NT hash", accountName);
            return WriteAuth3Failure(NrpcConstants.StatusAccessDenied);
        }

        var ntHash = Convert.FromHexString(machineAccount.NTHash);
        bool useAes = (clientFlags & NrpcConstants.NegotiateAes) != 0;

        // Compute session key: HMAC(NTHash, ClientChallenge || ServerChallenge)
        var sessionKey = SecureChannelSession.ComputeSessionKey(
            ntHash, session.ClientChallenge, session.ServerChallenge, useAes);

        // Verify client credential = ComputeNetlogonCredential(ClientChallenge, SessionKey)
        var expectedClientCred = SecureChannelSession.ComputeNetlogonCredential(
            session.ClientChallenge, sessionKey, useAes);

        if (!CryptographicOperations.FixedTimeEquals(clientCredentialBytes, expectedClientCred))
        {
            _logger.LogWarning("Client credential verification failed for {Computer}", computerName);
            _sessions.TryRemove(computerName, out _);
            return WriteAuth3Failure(NrpcConstants.StatusAccessDenied);
        }

        // Compute server credential = ComputeNetlogonCredential(ServerChallenge, SessionKey)
        var serverCredential = SecureChannelSession.ComputeNetlogonCredential(
            session.ServerChallenge, sessionKey, useAes);

        // Negotiate flags: AND client flags with server supported flags
        uint negotiatedFlags = clientFlags & NrpcConstants.NegotiateSupportedFlags;

        // Extract account RID
        uint accountRid = ExtractRid(machineAccount.ObjectSid);

        // Store the completed session
        session.AccountName = accountName;
        session.SessionKey = sessionKey;
        session.NegotiateFlags = negotiatedFlags;
        session.ClientCredential = new NETLOGON_CREDENTIAL { Data = (byte[])session.ClientChallenge.Clone() };
        session.ServerCredential = new NETLOGON_CREDENTIAL { Data = (byte[])session.ServerChallenge.Clone() };

        _logger.LogInformation(
            "Secure channel established for {Computer} (account={Account}, flags=0x{Flags:X8})",
            computerName, accountName, negotiatedFlags);

        // Response: ServerCredential (8 bytes), NegotiateFlags (uint32), AccountRid (uint32), NTSTATUS
        var writer = new NdrWriter();
        writer.WriteBytes(serverCredential);
        writer.WriteUInt32(negotiatedFlags);
        writer.WriteUInt32(accountRid);
        writer.WriteUInt32(NrpcConstants.StatusSuccess);

        return writer.ToArray();
    }

    // ========================================================================
    // Opnum 39 — NetrLogonSamLogonEx
    // MS-NRPC 3.5.4.5.1
    // ========================================================================

    /// <summary>
    /// Performs pass-through NTLM authentication. Accepts network logon info
    /// with NTLM challenge/response and validates against stored credentials.
    /// </summary>
    public async Task<byte[]> NetrLogonSamLogonExAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // [in, unique, string] LOGONSRV_HANDLE LogonServer — context handle as pointer to string
        var logonServerPtr = reader.ReadPointer();
        string logonServer = null;
        if (logonServerPtr != 0)
            logonServer = reader.ReadConformantVaryingString();

        // [in, unique, string] wchar_t* ComputerName
        var computerNamePtr = reader.ReadPointer();
        string computerName = null;
        if (computerNamePtr != 0)
            computerName = reader.ReadConformantVaryingString();

        // [in] NETLOGON_LOGON_INFO_CLASS LogonLevel
        var logonLevel = reader.ReadUInt16();
        reader.Align(4); // align after enum16

        // [in, switch_is(LogonLevel)] PNETLOGON_LEVEL LogonInformation — union pointer
        var logonInfoPtr = reader.ReadPointer();

        string domainName = "";
        string userName = "";
        string workstation = "";
        byte[] lmChallenge = Array.Empty<byte>();
        byte[] ntChallengeResponse = Array.Empty<byte>();
        byte[] lmChallengeResponse = Array.Empty<byte>();

        if (logonInfoPtr != 0)
        {
            // Union discriminant
            var unionLevel = reader.ReadUInt32();

            if (logonLevel is NrpcConstants.NetlogonNetworkInformation
                or NrpcConstants.NetlogonNetworkTransitiveInformation)
            {
                // NETLOGON_NETWORK_INFO: NETLOGON_LOGON_IDENTITY_INFO + LmChallenge + NT/LM responses
                // NETLOGON_LOGON_IDENTITY_INFO:
                //   RPC_UNICODE_STRING LogonDomainName
                //   ULONG ParameterControl
                //   OLD_LARGE_INTEGER Reserved (8 bytes)
                //   RPC_UNICODE_STRING UserName
                //   RPC_UNICODE_STRING Workstation

                var domainNameHeader = reader.ReadRpcUnicodeString();
                var parameterControl = reader.ReadUInt32();
                var reservedLow = reader.ReadUInt32();
                var reservedHigh = reader.ReadUInt32();
                var userNameHeader = reader.ReadRpcUnicodeString();
                var workstationHeader = reader.ReadRpcUnicodeString();

                // Read deferred string bodies
                if (domainNameHeader.ReferentId != 0)
                    domainName = reader.ReadConformantVaryingString();
                if (userNameHeader.ReferentId != 0)
                    userName = reader.ReadConformantVaryingString();
                if (workstationHeader.ReferentId != 0)
                    workstation = reader.ReadConformantVaryingString();

                // LM_CHALLENGE (LmChallenge: 8 bytes)
                lmChallenge = reader.ReadBytes(8).ToArray();

                // NT_RESPONSE (STRING: Length(ushort), MaxLength(ushort), pointer)
                var ntRespLength = reader.ReadUInt16();
                var ntRespMaxLength = reader.ReadUInt16();
                var ntRespPtr = reader.ReadPointer();
                // LM_RESPONSE (STRING)
                var lmRespLength = reader.ReadUInt16();
                var lmRespMaxLength = reader.ReadUInt16();
                var lmRespPtr = reader.ReadPointer();

                // Read deferred response bodies (conformant arrays)
                if (ntRespPtr != 0 && ntRespLength > 0)
                {
                    var maxCount = reader.ReadUInt32();
                    var offset = reader.ReadUInt32();
                    var actualCount = reader.ReadUInt32();
                    ntChallengeResponse = reader.ReadBytes((int)actualCount).ToArray();
                }

                if (lmRespPtr != 0 && lmRespLength > 0)
                {
                    var maxCount = reader.ReadUInt32();
                    var offset = reader.ReadUInt32();
                    var actualCount = reader.ReadUInt32();
                    lmChallengeResponse = reader.ReadBytes((int)actualCount).ToArray();
                }
            }
            else
            {
                _logger.LogWarning("Unsupported logon level {Level}", logonLevel);
                return WriteStatusOnly(NrpcConstants.StatusInvalidParameter);
            }
        }

        // [in] NETLOGON_VALIDATION_INFO_CLASS ValidationLevel
        var validationLevel = reader.ReadUInt16();
        reader.Align(4);

        // [in, out] ULONG* ExtraFlags
        var extraFlags = reader.ReadUInt32();

        _logger.LogDebug(
            "NetrLogonSamLogonEx: User={User}, Domain={Domain}, Level={Level}, ValidationLevel={VLevel}",
            userName, domainName, logonLevel, validationLevel);

        // Resolve domain DN
        var domainNc = _ncService.GetDomainNc();
        string domainDn = domainNc.Dn;
        if (!string.IsNullOrEmpty(domainName))
        {
            var resolvedDn = "DC=" + domainName.ToLowerInvariant().Replace(".", ",DC=");
            // Use resolved DN if it looks like a valid domain
            if (resolvedDn.Contains(",DC="))
                domainDn = resolvedDn;
        }

        // Look up user
        var user = await _store.GetBySamAccountNameAsync(context.TenantId, domainDn, userName, ct);
        if (user is null)
        {
            _logger.LogWarning("User '{User}' not found in domain '{Domain}'", userName, domainDn);
            return WriteSamLogonFailure(NrpcConstants.StatusNoSuchUser, validationLevel);
        }

        // Validate account status
        var logonError = _uacService.ValidateLogon(user);
        if (logonError is not null)
        {
            _logger.LogWarning("Logon validation failed for '{User}': {Error}", userName, logonError);
            uint statusCode = logonError.Contains("disabled", StringComparison.OrdinalIgnoreCase)
                ? NrpcConstants.StatusAccountDisabled
                : logonError.Contains("locked", StringComparison.OrdinalIgnoreCase)
                    ? NrpcConstants.StatusAccountLockedOut
                    : NrpcConstants.StatusAccessDenied;
            return WriteSamLogonFailure(statusCode, validationLevel);
        }

        if (string.IsNullOrEmpty(user.NTHash))
        {
            return WriteSamLogonFailure(NrpcConstants.StatusWrongPassword, validationLevel);
        }

        // Validate NTLM response and derive session base key per MS-NLMP 3.3.2
        byte[] userSessionKey = new byte[16]; // default: zeros if validation not performed
        if (ntChallengeResponse.Length > 0 && lmChallenge.Length == 8)
        {
            var ntHash = Convert.FromHexString(user.NTHash);

            // NTLMv2 validation
            var identityBytes = Encoding.Unicode.GetBytes(
                userName.ToUpperInvariant() + domainName);

            byte[] ntlmv2Hash;
            using (var hmac = new HMACMD5(ntHash))
            {
                ntlmv2Hash = hmac.ComputeHash(identityBytes);
            }

            if (ntChallengeResponse.Length >= 16)
            {
                var clientBlob = ntChallengeResponse[16..];
                var proofInput = new byte[lmChallenge.Length + clientBlob.Length];
                Buffer.BlockCopy(lmChallenge, 0, proofInput, 0, lmChallenge.Length);
                Buffer.BlockCopy(clientBlob, 0, proofInput, lmChallenge.Length, clientBlob.Length);

                byte[] expectedProof;
                using (var hmac = new HMACMD5(ntlmv2Hash))
                {
                    expectedProof = hmac.ComputeHash(proofInput);
                }

                if (!CryptographicOperations.FixedTimeEquals(
                        ntChallengeResponse.AsSpan(0, 16), expectedProof))
                {
                    _logger.LogWarning("NTLM response validation failed for '{User}'", userName);
                    return WriteSamLogonFailure(NrpcConstants.StatusWrongPassword, validationLevel);
                }

                // Session base key = HMAC_MD5(NtlmV2Hash, NtProofStr)
                // NtProofStr is the first 16 bytes of the NT challenge response
                using (var hmac = new HMACMD5(ntlmv2Hash))
                {
                    userSessionKey = hmac.ComputeHash(ntChallengeResponse, 0, 16);
                }
            }
        }

        // Build validation info response
        var now = DateTimeOffset.UtcNow;
        uint userId = ExtractRid(user.ObjectSid);
        string domainSid = ExtractDomainSid(user.ObjectSid);
        string dnsForestName = domainNc.DnsName;
        string dnsDomainName = domainNc.DnsName;

        var writer = new NdrWriter();

        // [out, switch_is(ValidationLevel)] PNETLOGON_VALIDATION — union
        writer.WriteUInt32(validationLevel); // union discriminant

        // Pointer to the validation struct
        writer.WritePointer(false);

        await WriteValidationSamInfoAsync(writer, validationLevel, user, userId, domainSid,
            domainName, dnsDomainName, dnsForestName, context.TenantId, now, userSessionKey, ct);

        // [out] UCHAR* Authoritative
        writer.WriteByte(1); // authoritative = TRUE
        writer.Align(4);

        // [in, out] ULONG* ExtraFlags
        writer.WriteUInt32(extraFlags);

        // NTSTATUS
        writer.WriteUInt32(NrpcConstants.StatusSuccess);

        return writer.ToArray();
    }

    // ========================================================================
    // Opnum 34 — DsrGetDcNameEx2
    // MS-NRPC 3.5.4.3.1
    // ========================================================================

    /// <summary>
    /// Locates a domain controller and returns DC information including
    /// name, address, domain GUID, site, and capability flags.
    /// </summary>
    public async Task<byte[]> DsrGetDcNameEx2Async(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // [in, unique, string] LOGONSRV_HANDLE ComputerName
        var computerNamePtr = reader.ReadPointer();
        string computerName = null;
        if (computerNamePtr != 0)
            computerName = reader.ReadConformantVaryingString();

        // [in, unique, string] wchar_t* AccountName
        var accountNamePtr = reader.ReadPointer();
        string accountName = null;
        if (accountNamePtr != 0)
            accountName = reader.ReadConformantVaryingString();

        // [in] ULONG AllowableAccountControlBits
        var allowableAccountControlBits = reader.ReadUInt32();

        // [in, unique, string] wchar_t* DomainName
        var domainNamePtr = reader.ReadPointer();
        string domainName = null;
        if (domainNamePtr != 0)
            domainName = reader.ReadConformantVaryingString();

        // [in, unique] GUID* DomainGuid
        var domainGuidPtr = reader.ReadPointer();
        Guid domainGuid = Guid.Empty;
        if (domainGuidPtr != 0)
        {
            var guidBytes = reader.ReadBytes(16);
            domainGuid = new Guid(guidBytes.Span);
        }

        // [in, unique, string] wchar_t* SiteName
        var siteNamePtr = reader.ReadPointer();
        string siteName = null;
        if (siteNamePtr != 0)
            siteName = reader.ReadConformantVaryingString();

        // [in] ULONG Flags
        var flags = reader.ReadUInt32();

        _logger.LogDebug("DsrGetDcNameEx2: DomainName={DomainName}, Flags=0x{Flags:X8}",
            domainName, flags);

        var domainNc = _ncService.GetDomainNc();
        var dnsName = domainNc.DnsName;

        // Look up the domain object for its GUID
        var domainObj = await _store.GetByDnAsync(context.TenantId, domainNc.Dn, ct);
        var domainObjGuid = domainObj?.ObjectGuid ?? Guid.NewGuid().ToString();

        var dcName = $"\\\\DC1.{dnsName}";
        var dcAddress = $"\\\\127.0.0.1";
        var dcSiteName = siteName ?? "Default-First-Site-Name";
        var clientSiteName = "Default-First-Site-Name";

        uint dcFlags = NrpcConstants.DsFlagDc
                     | NrpcConstants.DsFlagGc
                     | NrpcConstants.DsFlagKdc
                     | NrpcConstants.DsFlagWritable
                     | NrpcConstants.DsFlagDs
                     | NrpcConstants.DsFlagDns
                     | NrpcConstants.DsFlagClosest;

        // Response: pointer to DOMAIN_CONTROLLER_INFOW, NTSTATUS
        var writer = new NdrWriter();

        // Pointer to DOMAIN_CONTROLLER_INFOW
        writer.WritePointer(false);

        // DOMAIN_CONTROLLER_INFOW struct — all string fields as pointers
        // DomainControllerName
        writer.WritePointer(false);
        // DomainControllerAddress
        writer.WritePointer(false);
        // DomainControllerAddressType (DS_DNS_ADDRESS = 5 for DNS name based)
        writer.WriteUInt32(5);
        // DomainGuid (16 bytes, inline)
        if (Guid.TryParse(domainObjGuid, out var parsedGuid))
        {
            Span<byte> gb = stackalloc byte[16];
            parsedGuid.TryWriteBytes(gb);
            writer.WriteBytes(gb);
        }
        else
        {
            writer.WriteBytes(new byte[16]);
        }
        // DomainName
        writer.WritePointer(false);
        // DnsForestName
        writer.WritePointer(false);
        // Flags
        writer.WriteUInt32(dcFlags);
        // DcSiteName
        writer.WritePointer(false);
        // ClientSiteName
        writer.WritePointer(false);

        // Now write the deferred string bodies
        writer.WriteDeferredConformantVaryingString(dcName);
        writer.WriteDeferredConformantVaryingString(dcAddress);
        writer.WriteDeferredConformantVaryingString(dnsName);
        writer.WriteDeferredConformantVaryingString(dnsName); // forest = domain for single-domain
        writer.WriteDeferredConformantVaryingString(dcSiteName);
        writer.WriteDeferredConformantVaryingString(clientSiteName);

        // NTSTATUS
        writer.WriteUInt32(NrpcConstants.StatusSuccess);

        return writer.ToArray();
    }

    // ========================================================================
    // Opnum 30 — NetrServerPasswordSet2
    // MS-NRPC 3.5.4.4.6
    // ========================================================================

    /// <summary>
    /// Allows domain-joined computers to rotate their machine account passwords.
    /// The new password is encrypted with the session key.
    /// </summary>
    public async Task<byte[]> NetrServerPasswordSet2Async(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // [in, unique, string] LOGONSRV_HANDLE PrimaryName
        var primaryNamePtr = reader.ReadPointer();
        string primaryName = null;
        if (primaryNamePtr != 0)
            primaryName = reader.ReadConformantVaryingString();

        // [in, string] wchar_t* AccountName
        var accountNamePtr = reader.ReadPointer();
        string accountName = accountNamePtr != 0 ? reader.ReadConformantVaryingString() : "";

        // [in] NETLOGON_SECURE_CHANNEL_TYPE SecureChannelType
        var secureChannelType = reader.ReadUInt16();
        reader.Align(4);

        // [in, string] wchar_t* ComputerName
        var computerNamePtr = reader.ReadPointer();
        string computerName = computerNamePtr != 0 ? reader.ReadConformantVaryingString() : "";

        // [in] NETLOGON_AUTHENTICATOR Authenticator (credential: 8 bytes + timestamp: 4 bytes)
        var authCredential = reader.ReadBytes(8).ToArray();
        var authTimestamp = reader.ReadUInt32();

        // [in] PNL_TRUST_PASSWORD ClearNewPassword (512 encrypted bytes + 4 byte length = 516)
        var encryptedPassword = reader.ReadBytes(516).ToArray();

        _logger.LogDebug("NetrServerPasswordSet2: Computer={Computer}, Account={Account}",
            computerName, accountName);

        // Find the session
        if (!_sessions.TryGetValue(computerName, out var session) ||
            session.SessionKey.Length == 0)
        {
            _logger.LogWarning("No established secure channel for {Computer}", computerName);
            return WritePasswordSetFailure(NrpcConstants.StatusAccessDenied);
        }

        if (!session.AccountName.Equals(accountName, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Account name mismatch for {Computer}: expected {Expected}, got {Actual}",
                computerName, session.AccountName, accountName);
            return WritePasswordSetFailure(NrpcConstants.StatusAccessDenied);
        }

        // Verify authenticator
        var returnAuthCred = session.VerifyAuthenticator(authCredential, authTimestamp);
        if (returnAuthCred is null)
        {
            _logger.LogWarning("Authenticator verification failed for {Computer}", computerName);
            return WritePasswordSetFailure(NrpcConstants.StatusAccessDenied);
        }

        // Decrypt the new password
        string newPassword;
        try
        {
            newPassword = session.DecryptNewPassword(encryptedPassword);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to decrypt new password for {Computer}", computerName);
            return WritePasswordSetFailure(NrpcConstants.StatusInvalidParameter);
        }

        // Look up the machine account and set the new password
        var domainNc = _ncService.GetDomainNc();
        var machineAccount = await _store.GetBySamAccountNameAsync(
            context.TenantId, domainNc.Dn, accountName, ct);

        if (machineAccount is null)
        {
            _logger.LogWarning("Machine account '{Account}' not found", accountName);
            return WritePasswordSetFailure(NrpcConstants.StatusNoSuchUser);
        }

        // Compute new NT hash and Kerberos keys, then update the account
        var newNtHash = _passwordPolicy.ComputeNTHash(newPassword);
        machineAccount.NTHash = Convert.ToHexString(newNtHash);

        // Derive Kerberos keys (AES256, AES128, RC4) — CRITICAL for machine Kerberos auth
        var principalName = machineAccount.UserPrincipalName ?? machineAccount.SAMAccountName ?? accountName;
        var domainDns = GetDomainDnsFromDn(domainNc.Dn).ToUpperInvariant();
        var kerberosKeys = _passwordPolicy.DeriveKerberosKeys(principalName, newPassword, domainDns);
        machineAccount.KerberosKeys = kerberosKeys
            .Select(k => $"{k.EncryptionType}:{Convert.ToBase64String(k.KeyValue)}")
            .ToList();

        machineAccount.PwdLastSet = DateTimeOffset.UtcNow.ToFileTime();
        await _store.UpdateAsync(machineAccount, ct);

        _logger.LogInformation("Machine password rotated for {Account} (NT hash + {KeyCount} Kerberos keys)",
            accountName, kerberosKeys.Count);

        // Response: ReturnAuthenticator (8 bytes credential + 4 bytes timestamp), NTSTATUS
        var writer = new NdrWriter();
        writer.WriteBytes(returnAuthCred);
        writer.WriteUInt32(authTimestamp); // echo back the timestamp
        writer.WriteUInt32(NrpcConstants.StatusSuccess);

        return writer.ToArray();
    }

    // ========================================================================
    // Opnum 29 — NetrLogonGetDomainInfo
    // MS-NRPC 3.5.4.4.9
    // ========================================================================

    /// <summary>
    /// Returns domain information (primary domain, trusted domains, domain GUID/SID).
    /// Used by workstations after domain join to retrieve domain metadata.
    /// </summary>
    public async Task<byte[]> NetrLogonGetDomainInfoAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // [in, string] LOGONSRV_HANDLE ServerName
        var serverNamePtr = reader.ReadPointer();
        string serverName = null;
        if (serverNamePtr != 0)
            serverName = reader.ReadConformantVaryingString();

        // [in, unique, string] wchar_t* ComputerName
        var computerNamePtr = reader.ReadPointer();
        string computerName = null;
        if (computerNamePtr != 0)
            computerName = reader.ReadConformantVaryingString();

        // [in] NETLOGON_AUTHENTICATOR Authenticator
        var authCredential = reader.ReadBytes(8).ToArray();
        var authTimestamp = reader.ReadUInt32();

        // [in, out] NETLOGON_AUTHENTICATOR ReturnAuthenticator
        var returnAuthCredIn = reader.ReadBytes(8).ToArray();
        var returnAuthTimestampIn = reader.ReadUInt32();

        // [in] ULONG Level
        var level = reader.ReadUInt32();

        _logger.LogDebug("NetrLogonGetDomainInfo: Computer={Computer}, Level={Level}",
            computerName, level);

        // Verify authenticator if we have a session
        byte[] returnAuthCred = null;
        if (computerName is not null && _sessions.TryGetValue(computerName, out var session) &&
            session.SessionKey.Length > 0)
        {
            returnAuthCred = session.VerifyAuthenticator(authCredential, authTimestamp);
            if (returnAuthCred is null)
            {
                _logger.LogWarning("Authenticator verification failed for {Computer}", computerName);
                return WriteStatusOnly(NrpcConstants.StatusAccessDenied);
            }
        }

        var domainNc = _ncService.GetDomainNc();
        var domainObj = await _store.GetByDnAsync(context.TenantId, domainNc.Dn, ct);

        string domainSid;
        try
        {
            domainSid = _ridAllocator.GetDomainSid(context.TenantId, domainNc.Dn);
        }
        catch
        {
            domainSid = domainNc.DomainSid ?? "S-1-5-21-0-0-0";
        }

        Guid domainGuidParsed = Guid.Empty;
        if (domainObj is not null && Guid.TryParse(domainObj.ObjectGuid, out var dg))
            domainGuidParsed = dg;

        var writer = new NdrWriter();

        // ReturnAuthenticator
        if (returnAuthCred is not null)
        {
            writer.WriteBytes(returnAuthCred);
            writer.WriteUInt32(authTimestamp);
        }
        else
        {
            writer.WriteBytes(new byte[8]);
            writer.WriteUInt32(0);
        }

        // [out, switch_is(Level)] PNETLOGON_DOMAIN_INFORMATION DomainInfo — union
        writer.WriteUInt32(level); // union discriminant

        if (level == 1)
        {
            // NETLOGON_DOMAIN_INFO: pointer to struct
            writer.WritePointer(false);

            // PrimaryDomain — NETLOGON_ONE_DOMAIN_INFO
            WriteOneDomainInfo(writer, domainNc.DnsName, domainNc.DnsName,
                domainGuidParsed, domainSid);

            // TrustedDomainCount
            writer.WriteUInt32(0);
            // TrustedDomains (null pointer — no trusted domains in single-domain setup)
            writer.WritePointer(true);

            // LsaPolicy — NETLOGON_LSA_POLICY_INFO (empty)
            writer.WriteUInt32(0); // LsaPolicySize
            writer.WritePointer(true); // LsaPolicy (null)

            // DnsHostNameInDs — RPC_UNICODE_STRING
            string dnsHostName = computerName is not null
                ? $"{computerName}.{domainNc.DnsName}"
                : $"DC1.{domainNc.DnsName}";
            writer.WriteRpcUnicodeString(dnsHostName);

            // DummyString2..4 (3 empty RPC_UNICODE_STRINGs)
            writer.WriteRpcUnicodeString(null);
            writer.WriteRpcUnicodeString(null);
            writer.WriteRpcUnicodeString(null);

            // WorkstationFlags (ULONG)
            writer.WriteUInt32(0);

            // SupportedEncTypes (ULONG) — AES128+AES256+RC4
            writer.WriteUInt32(0x0000001C);

            // DummyLong3..4
            writer.WriteUInt32(0);
            writer.WriteUInt32(0);

            // Flush deferred string writes
            writer.FlushDeferred();
        }
        else
        {
            // Unsupported level — return empty
            writer.WritePointer(true);
        }

        // NTSTATUS
        writer.WriteUInt32(NrpcConstants.StatusSuccess);

        return writer.ToArray();
    }

    // ========================================================================
    // Opnum 28 — DsrGetSiteName
    // MS-NRPC 3.5.4.3.6
    // ========================================================================

    /// <summary>
    /// Returns the site name for the specified computer.
    /// </summary>
    public Task<byte[]> DsrGetSiteNameAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // [in, unique, string] LOGONSRV_HANDLE ComputerName
        var computerNamePtr = reader.ReadPointer();
        string computerName = null;
        if (computerNamePtr != 0)
            computerName = reader.ReadConformantVaryingString();

        _logger.LogDebug("DsrGetSiteName: Computer={Computer}", computerName);

        var writer = new NdrWriter();

        // [out, string] wchar_t** SiteName — pointer to pointer to string
        writer.WritePointer(false); // non-null referent
        writer.WriteDeferredConformantVaryingString("Default-First-Site-Name");

        // NTSTATUS
        writer.WriteUInt32(NrpcConstants.StatusSuccess);

        return Task.FromResult(writer.ToArray());
    }

    // ========================================================================
    // Opnum 2 — NetrLogonSamLogon (non-Ex variant)
    // MS-NRPC 3.5.4.5.2
    // ========================================================================

    /// <summary>
    /// Performs pass-through NTLM authentication with authenticator-based
    /// secure channel validation. Wraps the existing NetrLogonSamLogonEx
    /// implementation with Authenticator/ReturnAuthenticator handling.
    /// </summary>
    public async Task<byte[]> NetrLogonSamLogonAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // [in, unique, string] LOGONSRV_HANDLE LogonServer
        var logonServerPtr = reader.ReadPointer();
        string logonServer = null;
        if (logonServerPtr != 0)
            logonServer = reader.ReadConformantVaryingString();

        // [in, string] wchar_t* ComputerName
        var computerNamePtr = reader.ReadPointer();
        string computerName = null;
        if (computerNamePtr != 0)
            computerName = reader.ReadConformantVaryingString();

        // [in] NETLOGON_AUTHENTICATOR* Authenticator (pointer)
        var authPtr = reader.ReadPointer();
        byte[] authCredential = Array.Empty<byte>();
        uint authTimestamp = 0;
        if (authPtr != 0)
        {
            authCredential = reader.ReadBytes(8).ToArray();
            authTimestamp = reader.ReadUInt32();
        }

        // [in, out] NETLOGON_AUTHENTICATOR* ReturnAuthenticator (pointer)
        var returnAuthPtr = reader.ReadPointer();
        if (returnAuthPtr != 0)
        {
            // Skip the incoming return authenticator data
            reader.ReadBytes(8);
            reader.ReadUInt32();
        }

        // Verify authenticator if we have a session
        byte[] returnAuthCred = null;
        if (computerName is not null && authPtr != 0 &&
            _sessions.TryGetValue(computerName, out var session) &&
            session.SessionKey.Length > 0)
        {
            returnAuthCred = session.VerifyAuthenticator(authCredential, authTimestamp);
            if (returnAuthCred is null)
            {
                _logger.LogWarning("NetrLogonSamLogon: Authenticator verification failed for {Computer}", computerName);
                return WriteSamLogonNonExFailure(NrpcConstants.StatusAccessDenied, 3);
            }
        }

        // [in] NETLOGON_LOGON_INFO_CLASS LogonLevel
        var logonLevel = reader.ReadUInt16();
        reader.Align(4);

        // [in, switch_is(LogonLevel)] PNETLOGON_LEVEL LogonInformation — union pointer
        var logonInfoPtr = reader.ReadPointer();

        string domainName = "";
        string userName = "";
        string workstation = "";
        byte[] lmChallenge = Array.Empty<byte>();
        byte[] ntChallengeResponse = Array.Empty<byte>();
        byte[] lmChallengeResponse = Array.Empty<byte>();

        if (logonInfoPtr != 0)
        {
            // Union discriminant
            var unionLevel = reader.ReadUInt32();

            if (logonLevel is NrpcConstants.NetlogonNetworkInformation
                or NrpcConstants.NetlogonNetworkTransitiveInformation)
            {
                var domainNameHeader = reader.ReadRpcUnicodeString();
                var parameterControl = reader.ReadUInt32();
                var reservedLow = reader.ReadUInt32();
                var reservedHigh = reader.ReadUInt32();
                var userNameHeader = reader.ReadRpcUnicodeString();
                var workstationHeader = reader.ReadRpcUnicodeString();

                if (domainNameHeader.ReferentId != 0)
                    domainName = reader.ReadConformantVaryingString();
                if (userNameHeader.ReferentId != 0)
                    userName = reader.ReadConformantVaryingString();
                if (workstationHeader.ReferentId != 0)
                    workstation = reader.ReadConformantVaryingString();

                lmChallenge = reader.ReadBytes(8).ToArray();

                var ntRespLength = reader.ReadUInt16();
                var ntRespMaxLength = reader.ReadUInt16();
                var ntRespPtr = reader.ReadPointer();
                var lmRespLength = reader.ReadUInt16();
                var lmRespMaxLength = reader.ReadUInt16();
                var lmRespPtr = reader.ReadPointer();

                if (ntRespPtr != 0 && ntRespLength > 0)
                {
                    var maxCount = reader.ReadUInt32();
                    var offset = reader.ReadUInt32();
                    var actualCount = reader.ReadUInt32();
                    ntChallengeResponse = reader.ReadBytes((int)actualCount).ToArray();
                }

                if (lmRespPtr != 0 && lmRespLength > 0)
                {
                    var maxCount = reader.ReadUInt32();
                    var offset = reader.ReadUInt32();
                    var actualCount = reader.ReadUInt32();
                    lmChallengeResponse = reader.ReadBytes((int)actualCount).ToArray();
                }
            }
            else
            {
                _logger.LogWarning("NetrLogonSamLogon: Unsupported logon level {Level}", logonLevel);
                return WriteSamLogonNonExFailure(NrpcConstants.StatusInvalidParameter, 3);
            }
        }

        // [in] NETLOGON_VALIDATION_INFO_CLASS ValidationLevel
        var validationLevel = reader.ReadUInt16();
        reader.Align(4);

        _logger.LogDebug(
            "NetrLogonSamLogon: User={User}, Domain={Domain}, Level={Level}, ValidationLevel={VLevel}",
            userName, domainName, logonLevel, validationLevel);

        // Resolve domain DN
        var domainNc = _ncService.GetDomainNc();
        string domainDn = domainNc.Dn;
        if (!string.IsNullOrEmpty(domainName))
        {
            var resolvedDn = "DC=" + domainName.ToLowerInvariant().Replace(".", ",DC=");
            if (resolvedDn.Contains(",DC="))
                domainDn = resolvedDn;
        }

        // Look up user
        var user = await _store.GetBySamAccountNameAsync(context.TenantId, domainDn, userName, ct);
        if (user is null)
        {
            _logger.LogWarning("NetrLogonSamLogon: User '{User}' not found in domain '{Domain}'", userName, domainDn);
            return WriteSamLogonNonExResponse(returnAuthCred, authTimestamp,
                WriteSamLogonFailure(NrpcConstants.StatusNoSuchUser, validationLevel));
        }

        // Validate account status
        var logonError = _uacService.ValidateLogon(user);
        if (logonError is not null)
        {
            _logger.LogWarning("NetrLogonSamLogon: Logon validation failed for '{User}': {Error}", userName, logonError);
            uint statusCode = logonError.Contains("disabled", StringComparison.OrdinalIgnoreCase)
                ? NrpcConstants.StatusAccountDisabled
                : logonError.Contains("locked", StringComparison.OrdinalIgnoreCase)
                    ? NrpcConstants.StatusAccountLockedOut
                    : NrpcConstants.StatusAccessDenied;
            return WriteSamLogonNonExResponse(returnAuthCred, authTimestamp,
                WriteSamLogonFailure(statusCode, validationLevel));
        }

        if (string.IsNullOrEmpty(user.NTHash))
        {
            return WriteSamLogonNonExResponse(returnAuthCred, authTimestamp,
                WriteSamLogonFailure(NrpcConstants.StatusWrongPassword, validationLevel));
        }

        // Validate NTLM response and derive session base key per MS-NLMP 3.3.2
        byte[] userSessionKey = new byte[16];
        if (ntChallengeResponse.Length > 0 && lmChallenge.Length == 8)
        {
            var ntHash = Convert.FromHexString(user.NTHash);
            var identityBytes = Encoding.Unicode.GetBytes(
                userName.ToUpperInvariant() + domainName);

            byte[] ntlmv2Hash;
            using (var hmac = new HMACMD5(ntHash))
            {
                ntlmv2Hash = hmac.ComputeHash(identityBytes);
            }

            if (ntChallengeResponse.Length >= 16)
            {
                var clientBlob = ntChallengeResponse[16..];
                var proofInput = new byte[lmChallenge.Length + clientBlob.Length];
                Buffer.BlockCopy(lmChallenge, 0, proofInput, 0, lmChallenge.Length);
                Buffer.BlockCopy(clientBlob, 0, proofInput, lmChallenge.Length, clientBlob.Length);

                byte[] expectedProof;
                using (var hmac = new HMACMD5(ntlmv2Hash))
                {
                    expectedProof = hmac.ComputeHash(proofInput);
                }

                if (!CryptographicOperations.FixedTimeEquals(
                        ntChallengeResponse.AsSpan(0, 16), expectedProof))
                {
                    _logger.LogWarning("NetrLogonSamLogon: NTLM response validation failed for '{User}'", userName);
                    return WriteSamLogonNonExResponse(returnAuthCred, authTimestamp,
                        WriteSamLogonFailure(NrpcConstants.StatusWrongPassword, validationLevel));
                }

                // Session base key = HMAC_MD5(NtlmV2Hash, NtProofStr)
                using (var hmac = new HMACMD5(ntlmv2Hash))
                {
                    userSessionKey = hmac.ComputeHash(ntChallengeResponse, 0, 16);
                }
            }
        }

        // Build validation info response
        var now = DateTimeOffset.UtcNow;
        uint userId = ExtractRid(user.ObjectSid);
        string domainSid = ExtractDomainSid(user.ObjectSid);
        string dnsForestName = domainNc.DnsName;
        string dnsDomainName = domainNc.DnsName;

        var innerWriter = new NdrWriter();

        // [out, switch_is(ValidationLevel)] PNETLOGON_VALIDATION — union
        innerWriter.WriteUInt32(validationLevel); // union discriminant
        innerWriter.WritePointer(false);

        await WriteValidationSamInfoAsync(innerWriter, validationLevel, user, userId, domainSid,
            domainName, dnsDomainName, dnsForestName, context.TenantId, now, userSessionKey, ct);

        // Authoritative
        innerWriter.WriteByte(1);
        innerWriter.Align(4);

        // NTSTATUS
        innerWriter.WriteUInt32(NrpcConstants.StatusSuccess);

        var innerBytes = innerWriter.ToArray();

        // Wrap with ReturnAuthenticator
        return WriteSamLogonNonExResponse(returnAuthCred, authTimestamp, innerBytes);
    }

    /// <summary>
    /// Wraps a SamLogonEx-format response with the ReturnAuthenticator header
    /// needed for the non-Ex variant (opnum 2).
    /// </summary>
    private static byte[] WriteSamLogonNonExResponse(byte[] returnAuthCred, uint authTimestamp, byte[] innerResponse)
    {
        var writer = new NdrWriter();

        // ReturnAuthenticator
        if (returnAuthCred is not null)
        {
            writer.WriteBytes(returnAuthCred);
            writer.WriteUInt32(authTimestamp);
        }
        else
        {
            writer.WriteBytes(new byte[8]);
            writer.WriteUInt32(0);
        }

        // The rest of the response (validation union + authoritative + status)
        writer.WriteBytes(innerResponse);

        return writer.ToArray();
    }

    /// <summary>
    /// Writes a failure response for NetrLogonSamLogon (non-Ex, includes ReturnAuthenticator).
    /// </summary>
    private static byte[] WriteSamLogonNonExFailure(uint status, ushort validationLevel)
    {
        var writer = new NdrWriter();
        // ReturnAuthenticator (empty)
        writer.WriteBytes(new byte[8]);
        writer.WriteUInt32(0);
        // Union discriminant
        writer.WriteUInt32(validationLevel);
        // Null pointer (no validation info)
        writer.WritePointer(true);
        // Authoritative = TRUE
        writer.WriteByte(1);
        writer.Align(4);
        // NTSTATUS
        writer.WriteUInt32(status);
        return writer.ToArray();
    }

    // ========================================================================
    // Opnum 44 — DsrGetForestTrustInformation
    // MS-NRPC 3.5.4.7.1
    // ========================================================================

    /// <summary>
    /// Returns forest trust information. Returns empty (no external trusts)
    /// for a single-domain forest. Queried during domain join.
    /// </summary>
    public Task<byte[]> DsrGetForestTrustInformationAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // [in, unique, string] LOGONSRV_HANDLE ServerName
        var serverNamePtr = reader.ReadPointer();
        string serverName = null;
        if (serverNamePtr != 0)
            serverName = reader.ReadConformantVaryingString();

        // [in, unique, string] wchar_t* TrustedDomainName
        var trustedDomainNamePtr = reader.ReadPointer();
        string trustedDomainName = null;
        if (trustedDomainNamePtr != 0)
            trustedDomainName = reader.ReadConformantVaryingString();

        // [in] DWORD Flags
        var flags = reader.ReadUInt32();

        _logger.LogDebug("DsrGetForestTrustInformation: TrustedDomain={TrustedDomain}, Flags=0x{Flags:X8}",
            trustedDomainName, flags);

        var writer = new NdrWriter();

        // [out] PLSA_FOREST_TRUST_INFORMATION* ForestTrustInfo — pointer to pointer
        // Return a valid but empty forest trust info structure
        writer.WritePointer(false); // non-null pointer to LSA_FOREST_TRUST_INFORMATION

        // LSA_FOREST_TRUST_INFORMATION:
        //   RecordCount (uint32)
        //   Entries (pointer to conformant array of pointers to LSA_FOREST_TRUST_RECORD)
        writer.WriteUInt32(0); // RecordCount = 0
        writer.WritePointer(true); // null Entries pointer (empty array)

        // NTSTATUS
        writer.WriteUInt32(NrpcConstants.StatusSuccess);

        return Task.FromResult(writer.ToArray());
    }

    // ========================================================================
    // Opnum 21 — NetrLogonGetCapabilities
    // MS-NRPC 3.5.4.4.10
    // ========================================================================

    /// <summary>
    /// Returns the negotiated capabilities (flags) for the secure channel session.
    /// Windows 10+ calls this immediately after NetrServerAuthenticate3 to verify
    /// AES support. Only QueryLevel 1 is supported.
    /// </summary>
    public Task<byte[]> NetrLogonGetCapabilitiesAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // [in, string] LOGONSRV_HANDLE ServerName
        var serverNamePtr = reader.ReadPointer();
        string serverName = null;
        if (serverNamePtr != 0)
            serverName = reader.ReadConformantVaryingString();

        // [in, string] wchar_t* ComputerName
        var computerNamePtr = reader.ReadPointer();
        string computerName = null;
        if (computerNamePtr != 0)
            computerName = reader.ReadConformantVaryingString();

        // [in] PNETLOGON_AUTHENTICATOR Authenticator (8-byte credential + 4-byte timestamp)
        var authCredential = reader.ReadBytes(8).ToArray();
        var authTimestamp = reader.ReadUInt32();

        // [in, out] PNETLOGON_AUTHENTICATOR ReturnAuthenticator
        var returnAuthCredIn = reader.ReadBytes(8); // skip incoming return authenticator
        var returnAuthTimestampIn = reader.ReadUInt32();

        // [in] DWORD QueryLevel
        var queryLevel = reader.ReadUInt32();

        _logger.LogDebug(
            "NetrLogonGetCapabilities: Computer={Computer}, QueryLevel={QueryLevel}",
            computerName, queryLevel);

        // Only QueryLevel 1 is supported
        if (queryLevel != 1)
        {
            var failWriter = new NdrWriter();
            // ReturnAuthenticator (empty)
            failWriter.WriteBytes(new byte[8]);
            failWriter.WriteUInt32(0);
            // ServerCapabilities union (discriminant + empty value for failure)
            failWriter.WriteUInt32(queryLevel);
            failWriter.WriteUInt32(0);
            // NTSTATUS
            failWriter.WriteUInt32(NrpcConstants.StatusNotSupported);
            return Task.FromResult(failWriter.ToArray());
        }

        // Verify authenticator
        if (computerName is null || !_sessions.TryGetValue(computerName, out var session) ||
            session.SessionKey.Length == 0)
        {
            _logger.LogWarning("NetrLogonGetCapabilities: No session for computer {Computer}", computerName);
            var failWriter = new NdrWriter();
            failWriter.WriteBytes(new byte[8]);
            failWriter.WriteUInt32(0);
            failWriter.WriteUInt32(1); // union discriminant
            failWriter.WriteUInt32(0);
            failWriter.WriteUInt32(NrpcConstants.StatusAccessDenied);
            return Task.FromResult(failWriter.ToArray());
        }

        var returnAuthCred = session.VerifyAuthenticator(authCredential, authTimestamp);
        if (returnAuthCred is null)
        {
            _logger.LogWarning("NetrLogonGetCapabilities: Authenticator verification failed for {Computer}", computerName);
            var failWriter = new NdrWriter();
            failWriter.WriteBytes(new byte[8]);
            failWriter.WriteUInt32(0);
            failWriter.WriteUInt32(1); // union discriminant
            failWriter.WriteUInt32(0);
            failWriter.WriteUInt32(NrpcConstants.StatusAccessDenied);
            return Task.FromResult(failWriter.ToArray());
        }

        var writer = new NdrWriter();

        // [out] ReturnAuthenticator
        writer.WriteBytes(returnAuthCred);
        writer.WriteUInt32(authTimestamp);

        // [out] NETLOGON_CAPABILITIES ServerCapabilities (union, switch on QueryLevel)
        // QueryLevel 1: ServerCapabilities is a DWORD of negotiated flags
        writer.WriteUInt32(1); // union discriminant
        writer.WriteUInt32(session.NegotiateFlags);

        // NTSTATUS
        writer.WriteUInt32(NrpcConstants.StatusSuccess);

        return Task.FromResult(writer.ToArray());
    }

    // ========================================================================
    // Opnum 45 — NetrLogonSamLogonWithFlags
    // MS-NRPC 3.5.4.5.4
    // ========================================================================

    /// <summary>
    /// Performs pass-through NTLM authentication with authenticator-based
    /// secure channel validation, identical to NetrLogonSamLogonEx but with
    /// explicit Authenticator/ReturnAuthenticator and ExtraFlags.
    /// </summary>
    public async Task<byte[]> NetrLogonSamLogonWithFlagsAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // [in, unique, string] LOGONSRV_HANDLE LogonServer
        var logonServerPtr = reader.ReadPointer();
        string logonServer = null;
        if (logonServerPtr != 0)
            logonServer = reader.ReadConformantVaryingString();

        // [in, string] wchar_t* ComputerName
        var computerNamePtr = reader.ReadPointer();
        string computerName = null;
        if (computerNamePtr != 0)
            computerName = reader.ReadConformantVaryingString();

        // [in] PNETLOGON_AUTHENTICATOR Authenticator (pointer)
        var authPtr = reader.ReadPointer();
        byte[] authCredential = Array.Empty<byte>();
        uint authTimestamp = 0;
        if (authPtr != 0)
        {
            authCredential = reader.ReadBytes(8).ToArray();
            authTimestamp = reader.ReadUInt32();
        }

        // [in, out] PNETLOGON_AUTHENTICATOR ReturnAuthenticator (pointer)
        var returnAuthPtr = reader.ReadPointer();
        if (returnAuthPtr != 0)
        {
            // Skip the incoming return authenticator data
            reader.ReadBytes(8);
            reader.ReadUInt32();
        }

        // Verify authenticator if we have a session
        byte[] returnAuthCred = null;
        if (computerName is not null && authPtr != 0 &&
            _sessions.TryGetValue(computerName, out var session) &&
            session.SessionKey.Length > 0)
        {
            returnAuthCred = session.VerifyAuthenticator(authCredential, authTimestamp);
            if (returnAuthCred is null)
            {
                _logger.LogWarning("NetrLogonSamLogonWithFlags: Authenticator verification failed for {Computer}", computerName);
                return WriteSamLogonWithFlagsFailure(NrpcConstants.StatusAccessDenied, 3);
            }
        }

        // [in] NETLOGON_LOGON_INFO_CLASS LogonLevel
        var logonLevel = reader.ReadUInt16();
        reader.Align(4);

        // [in, switch_is(LogonLevel)] PNETLOGON_LEVEL LogonInformation — union pointer
        var logonInfoPtr = reader.ReadPointer();

        string domainName = "";
        string userName = "";
        string workstation = "";
        byte[] lmChallenge = Array.Empty<byte>();
        byte[] ntChallengeResponse = Array.Empty<byte>();
        byte[] lmChallengeResponse = Array.Empty<byte>();

        if (logonInfoPtr != 0)
        {
            // Union discriminant
            var unionLevel = reader.ReadUInt32();

            if (logonLevel is NrpcConstants.NetlogonNetworkInformation
                or NrpcConstants.NetlogonNetworkTransitiveInformation)
            {
                var domainNameHeader = reader.ReadRpcUnicodeString();
                var parameterControl = reader.ReadUInt32();
                var reservedLow = reader.ReadUInt32();
                var reservedHigh = reader.ReadUInt32();
                var userNameHeader = reader.ReadRpcUnicodeString();
                var workstationHeader = reader.ReadRpcUnicodeString();

                if (domainNameHeader.ReferentId != 0)
                    domainName = reader.ReadConformantVaryingString();
                if (userNameHeader.ReferentId != 0)
                    userName = reader.ReadConformantVaryingString();
                if (workstationHeader.ReferentId != 0)
                    workstation = reader.ReadConformantVaryingString();

                lmChallenge = reader.ReadBytes(8).ToArray();

                var ntRespLength = reader.ReadUInt16();
                var ntRespMaxLength = reader.ReadUInt16();
                var ntRespPtr = reader.ReadPointer();
                var lmRespLength = reader.ReadUInt16();
                var lmRespMaxLength = reader.ReadUInt16();
                var lmRespPtr = reader.ReadPointer();

                if (ntRespPtr != 0 && ntRespLength > 0)
                {
                    var maxCount = reader.ReadUInt32();
                    var offset = reader.ReadUInt32();
                    var actualCount = reader.ReadUInt32();
                    ntChallengeResponse = reader.ReadBytes((int)actualCount).ToArray();
                }

                if (lmRespPtr != 0 && lmRespLength > 0)
                {
                    var maxCount = reader.ReadUInt32();
                    var offset = reader.ReadUInt32();
                    var actualCount = reader.ReadUInt32();
                    lmChallengeResponse = reader.ReadBytes((int)actualCount).ToArray();
                }
            }
            else
            {
                _logger.LogWarning("NetrLogonSamLogonWithFlags: Unsupported logon level {Level}", logonLevel);
                return WriteSamLogonWithFlagsFailure(NrpcConstants.StatusInvalidParameter, 3);
            }
        }

        // [in] NETLOGON_VALIDATION_INFO_CLASS ValidationLevel
        var validationLevel = reader.ReadUInt16();
        reader.Align(4);

        // [in, out] ULONG* ExtraFlags
        var extraFlags = reader.ReadUInt32();

        _logger.LogDebug(
            "NetrLogonSamLogonWithFlags: User={User}, Domain={Domain}, Level={Level}, ValidationLevel={VLevel}",
            userName, domainName, logonLevel, validationLevel);

        // Resolve domain DN
        var domainNc = _ncService.GetDomainNc();
        string domainDn = domainNc.Dn;
        if (!string.IsNullOrEmpty(domainName))
        {
            var resolvedDn = "DC=" + domainName.ToLowerInvariant().Replace(".", ",DC=");
            if (resolvedDn.Contains(",DC="))
                domainDn = resolvedDn;
        }

        // Look up user
        var user = await _store.GetBySamAccountNameAsync(context.TenantId, domainDn, userName, ct);
        if (user is null)
        {
            _logger.LogWarning("NetrLogonSamLogonWithFlags: User '{User}' not found in domain '{Domain}'", userName, domainDn);
            return WriteSamLogonWithFlagsResponse(returnAuthCred, authTimestamp,
                WriteSamLogonFailure(NrpcConstants.StatusNoSuchUser, validationLevel));
        }

        // Validate account status
        var logonError = _uacService.ValidateLogon(user);
        if (logonError is not null)
        {
            _logger.LogWarning("NetrLogonSamLogonWithFlags: Logon validation failed for '{User}': {Error}", userName, logonError);
            uint statusCode = logonError.Contains("disabled", StringComparison.OrdinalIgnoreCase)
                ? NrpcConstants.StatusAccountDisabled
                : logonError.Contains("locked", StringComparison.OrdinalIgnoreCase)
                    ? NrpcConstants.StatusAccountLockedOut
                    : NrpcConstants.StatusAccessDenied;
            return WriteSamLogonWithFlagsResponse(returnAuthCred, authTimestamp,
                WriteSamLogonFailure(statusCode, validationLevel));
        }

        if (string.IsNullOrEmpty(user.NTHash))
        {
            return WriteSamLogonWithFlagsResponse(returnAuthCred, authTimestamp,
                WriteSamLogonFailure(NrpcConstants.StatusWrongPassword, validationLevel));
        }

        // Validate NTLM response and derive session base key per MS-NLMP 3.3.2
        byte[] userSessionKey = new byte[16];
        if (ntChallengeResponse.Length > 0 && lmChallenge.Length == 8)
        {
            var ntHash = Convert.FromHexString(user.NTHash);
            var identityBytes = Encoding.Unicode.GetBytes(
                userName.ToUpperInvariant() + domainName);

            byte[] ntlmv2Hash;
            using (var hmac = new HMACMD5(ntHash))
            {
                ntlmv2Hash = hmac.ComputeHash(identityBytes);
            }

            if (ntChallengeResponse.Length >= 16)
            {
                var clientBlob = ntChallengeResponse[16..];
                var proofInput = new byte[lmChallenge.Length + clientBlob.Length];
                Buffer.BlockCopy(lmChallenge, 0, proofInput, 0, lmChallenge.Length);
                Buffer.BlockCopy(clientBlob, 0, proofInput, lmChallenge.Length, clientBlob.Length);

                byte[] expectedProof;
                using (var hmac = new HMACMD5(ntlmv2Hash))
                {
                    expectedProof = hmac.ComputeHash(proofInput);
                }

                if (!CryptographicOperations.FixedTimeEquals(
                        ntChallengeResponse.AsSpan(0, 16), expectedProof))
                {
                    _logger.LogWarning("NetrLogonSamLogonWithFlags: NTLM response validation failed for '{User}'", userName);
                    return WriteSamLogonWithFlagsResponse(returnAuthCred, authTimestamp,
                        WriteSamLogonFailure(NrpcConstants.StatusWrongPassword, validationLevel));
                }

                // Session base key = HMAC_MD5(NtlmV2Hash, NtProofStr)
                using (var hmac = new HMACMD5(ntlmv2Hash))
                {
                    userSessionKey = hmac.ComputeHash(ntChallengeResponse, 0, 16);
                }
            }
        }

        // Build validation info response
        var now = DateTimeOffset.UtcNow;
        uint userId = ExtractRid(user.ObjectSid);
        string domainSid = ExtractDomainSid(user.ObjectSid);
        string dnsForestName = domainNc.DnsName;
        string dnsDomainName = domainNc.DnsName;

        var innerWriter = new NdrWriter();

        // [out, switch_is(ValidationLevel)] PNETLOGON_VALIDATION — union
        innerWriter.WriteUInt32(validationLevel); // union discriminant
        innerWriter.WritePointer(false);

        await WriteValidationSamInfoAsync(innerWriter, validationLevel, user, userId, domainSid,
            domainName, dnsDomainName, dnsForestName, context.TenantId, now, userSessionKey, ct);

        // Authoritative
        innerWriter.WriteByte(1);
        innerWriter.Align(4);

        // [in, out] ULONG* ExtraFlags — return 0
        innerWriter.WriteUInt32(0);

        // NTSTATUS
        innerWriter.WriteUInt32(NrpcConstants.StatusSuccess);

        var innerBytes = innerWriter.ToArray();

        // Wrap with ReturnAuthenticator
        return WriteSamLogonWithFlagsResponse(returnAuthCred, authTimestamp, innerBytes);
    }

    /// <summary>
    /// Wraps a SamLogonEx-format response with the ReturnAuthenticator header
    /// needed for the WithFlags variant (opnum 45).
    /// </summary>
    private static byte[] WriteSamLogonWithFlagsResponse(byte[] returnAuthCred, uint authTimestamp, byte[] innerResponse)
    {
        var writer = new NdrWriter();

        // ReturnAuthenticator
        if (returnAuthCred is not null)
        {
            writer.WriteBytes(returnAuthCred);
            writer.WriteUInt32(authTimestamp);
        }
        else
        {
            writer.WriteBytes(new byte[8]);
            writer.WriteUInt32(0);
        }

        // The rest of the response
        writer.WriteBytes(innerResponse);

        return writer.ToArray();
    }

    /// <summary>
    /// Writes a failure response for NetrLogonSamLogonWithFlags (includes ReturnAuthenticator + ExtraFlags).
    /// </summary>
    private static byte[] WriteSamLogonWithFlagsFailure(uint status, ushort validationLevel)
    {
        var writer = new NdrWriter();
        // ReturnAuthenticator (empty)
        writer.WriteBytes(new byte[8]);
        writer.WriteUInt32(0);
        // Union discriminant
        writer.WriteUInt32(validationLevel);
        // Null pointer (no validation info)
        writer.WritePointer(true);
        // Authoritative = TRUE
        writer.WriteByte(1);
        writer.Align(4);
        // ExtraFlags = 0
        writer.WriteUInt32(0);
        // NTSTATUS
        writer.WriteUInt32(status);
        return writer.ToArray();
    }

    // ========================================================================
    // Private helpers
    // ========================================================================

    /// <summary>
    /// Writes a NETLOGON_ONE_DOMAIN_INFO structure for NetrLogonGetDomainInfo.
    /// </summary>
    private static void WriteOneDomainInfo(NdrWriter writer, string domainName,
        string dnsForestName, Guid domainGuid, string domainSid)
    {
        // DomainName — RPC_UNICODE_STRING
        writer.WriteRpcUnicodeString(domainName);
        // DnsDomainName — RPC_UNICODE_STRING
        writer.WriteRpcUnicodeString(domainName);
        // DnsForestName — RPC_UNICODE_STRING
        writer.WriteRpcUnicodeString(dnsForestName);

        // DomainGuid (inline, 16 bytes)
        Span<byte> guidBytes = stackalloc byte[16];
        domainGuid.TryWriteBytes(guidBytes);
        writer.WriteBytes(guidBytes);

        // DomainSid — pointer to RPC_SID
        if (!string.IsNullOrEmpty(domainSid))
        {
            writer.WritePointer(false);
            // Deferred: we write the SID body inline after the struct
            // (handled by caller's FlushDeferred or inline)
        }
        else
        {
            writer.WritePointer(true); // null
        }

        // DummyString1..4 (4 RPC_UNICODE_STRINGs)
        writer.WriteRpcUnicodeString(null);
        writer.WriteRpcUnicodeString(null);
        writer.WriteRpcUnicodeString(null);
        writer.WriteRpcUnicodeString(null);

        // DummyLong1..4
        writer.WriteUInt32(0);
        writer.WriteUInt32(0);
        writer.WriteUInt32(0);
        writer.WriteUInt32(0);

        // Flush the deferred string writes for this domain info
        writer.FlushDeferred();

        // Now write the deferred SID body if present
        if (!string.IsNullOrEmpty(domainSid))
        {
            // RPC_SID: first write conformant max count (sub-authority count),
            // then the SID body
            var parts = domainSid.Split('-');
            int subAuthorityCount = parts.Length - 3;
            writer.WriteUInt32((uint)subAuthorityCount); // conformant max count
            writer.WriteRpcSid(domainSid);
        }
    }

    /// <summary>
    /// Writes a NETLOGON_VALIDATION_SAM_INFO structure (level 2, 3, or 6).
    /// Resolves group memberships by looking up each group object to extract RIDs,
    /// includes the primary group, well-known ExtraSids, and populates all time fields
    /// per MS-NRPC 2.2.1.4.11/12/13.
    /// </summary>
    private async Task WriteValidationSamInfoAsync(NdrWriter writer, ushort validationLevel,
        Core.Models.DirectoryObject user, uint userId, string domainSid,
        string logonDomainName, string dnsDomainName, string dnsForestName,
        string tenantId, DateTimeOffset now, byte[] userSessionKey, CancellationToken ct)
    {
        // All VALIDATION_SAM_INFO variants start with the same base fields.
        // Times are FILETIME (uint64).
        long logonTime = now.ToFileTime();
        long logoffTime = 0x7FFFFFFFFFFFFFFF; // never
        long passwordLastSet = user.PwdLastSet != 0 ? user.PwdLastSet : now.ToFileTime();

        // KickOffTime: derive from accountExpires if set, otherwise never
        long kickOffTime = 0x7FFFFFFFFFFFFFFF;
        if (user.AccountExpires != 0 && user.AccountExpires != 0x7FFFFFFFFFFFFFFF)
            kickOffTime = user.AccountExpires;

        // Read domain password policy from domain root object for min/maxPwdAge
        long minPwdAge = -(1L * 24 * 60 * 60 * 10000000L); // default 1 day (negative = duration)
        long maxPwdAge = -(42L * 24 * 60 * 60 * 10000000L); // default 42 days (negative = duration)
        var domainNc = _ncService.GetDomainNc();
        var domainRoot = await _store.GetByDnAsync(tenantId, domainNc.Dn, ct);
        if (domainRoot is not null)
        {
            var minAttr = domainRoot.GetAttribute("minPwdAge");
            if (minAttr?.GetFirstString() is { } minStr && long.TryParse(minStr, out var minVal))
                minPwdAge = minVal;
            var maxAttr = domainRoot.GetAttribute("maxPwdAge");
            if (maxAttr?.GetFirstString() is { } maxStr && long.TryParse(maxStr, out var maxVal))
                maxPwdAge = maxVal;
        }

        // PasswordCanChange = PasswordLastSet + minPwdAge (minPwdAge is stored as negative duration)
        long passwordCanChange = (user.PwdLastSet != 0 && minPwdAge < 0)
            ? user.PwdLastSet + (-minPwdAge)
            : passwordLastSet;

        // PasswordMustChange = PasswordLastSet + maxPwdAge, or never if maxPwdAge == 0
        long passwordMustChange = 0x7FFFFFFFFFFFFFFF;
        if (maxPwdAge < 0 && user.PwdLastSet != 0)
            passwordMustChange = user.PwdLastSet + (-maxPwdAge);

        string effectiveName = user.SAMAccountName ?? user.Cn ?? "";
        string fullName = user.DisplayName ?? "";
        string logonScript = user.GetAttribute("scriptPath")?.GetFirstString() ?? "";
        string profilePath = user.GetAttribute("profilePath")?.GetFirstString() ?? "";
        string homeDir = user.GetAttribute("homeDirectory")?.GetFirstString() ?? "";
        string homeDrive = user.GetAttribute("homeDrive")?.GetFirstString() ?? "";

        // Determine logon count
        ushort logonCount = 0;
        var logonCountAttr = user.GetAttribute("logonCount");
        if (logonCountAttr?.GetFirstString() is { } lcStr && ushort.TryParse(lcStr, out var lc))
            logonCount = lc;

        // LogonTime, LogoffTime, KickOffTime, PasswordLastSet, PasswordCanChange, PasswordMustChange
        writer.WriteInt64(logonTime);
        writer.WriteInt64(logoffTime);
        writer.WriteInt64(kickOffTime);
        writer.WriteInt64(passwordLastSet);
        writer.WriteInt64(passwordCanChange);
        writer.WriteInt64(passwordMustChange);

        // EffectiveName, FullName, LogonScript, ProfilePath, HomeDirectory, HomeDirectoryDrive
        // (all as RPC_UNICODE_STRING)
        writer.WriteRpcUnicodeString(effectiveName);
        writer.WriteRpcUnicodeString(fullName);
        writer.WriteRpcUnicodeString(logonScript);
        writer.WriteRpcUnicodeString(profilePath);
        writer.WriteRpcUnicodeString(homeDir);
        writer.WriteRpcUnicodeString(homeDrive);

        // LogonCount (ushort), BadPasswordCount (ushort)
        writer.WriteUInt16(logonCount);
        writer.WriteUInt16((ushort)user.BadPwdCount);

        // UserId (uint32), PrimaryGroupId (uint32)
        writer.WriteUInt32(userId);
        writer.WriteUInt32((uint)user.PrimaryGroupId);

        // GroupCount (uint32) and GroupIds pointer
        // Resolve group RIDs by looking up each group object
        const uint groupAttrs = 0x00000007; // SE_GROUP_MANDATORY | SE_GROUP_ENABLED_BY_DEFAULT | SE_GROUP_ENABLED
        var groupEntries = new List<(uint Rid, uint Attributes)>();

        // Always include the primary group first
        groupEntries.Add(((uint)user.PrimaryGroupId, groupAttrs));

        foreach (var groupDn in user.MemberOf)
        {
            var groupObj = await _store.GetByDnAsync(tenantId, groupDn, ct);
            if (groupObj?.ObjectSid is null)
                continue;

            uint groupRid = ExtractRid(groupObj.ObjectSid);
            if (groupRid == 0)
                continue;

            // Only include same-domain groups in GroupIds; cross-domain go to ExtraSids
            string groupDomainSid = ExtractDomainSid(groupObj.ObjectSid);
            if (string.Equals(groupDomainSid, domainSid, StringComparison.OrdinalIgnoreCase))
            {
                // Skip if it's the primary group (already added)
                if (groupRid != (uint)user.PrimaryGroupId)
                    groupEntries.Add((groupRid, groupAttrs));
            }
        }

        writer.WriteUInt32((uint)groupEntries.Count);
        writer.WritePointer(false); // pointer to GroupIds array

        // Build ExtraSids list
        var extraSids = new List<(string Sid, uint Attributes)>();

        // Add cross-domain group memberships as ExtraSids
        foreach (var groupDn in user.MemberOf)
        {
            var groupObj = await _store.GetByDnAsync(tenantId, groupDn, ct);
            if (groupObj?.ObjectSid is null)
                continue;

            string groupDomainSid = ExtractDomainSid(groupObj.ObjectSid);
            if (!string.Equals(groupDomainSid, domainSid, StringComparison.OrdinalIgnoreCase))
            {
                extraSids.Add((groupObj.ObjectSid, groupAttrs));
            }
        }

        // Well-known ExtraSids per MS-APDS / MS-NRPC
        extraSids.Add(("S-1-5-11", groupAttrs));  // Authenticated Users
        extraSids.Add(("S-1-18-1", groupAttrs));  // Authentication Authority Asserted Identity

        // UserFlags (uint32)
        uint userFlags = 0;
        if (extraSids.Count > 0)
            userFlags |= 0x00000020; // LOGON_EXTRA_SIDS
        writer.WriteUInt32(userFlags);

        // UserSessionKey (16 bytes) — per MS-NLMP, the session base key for NTLMv2
        // is HMAC_MD5(NtlmV2Hash, NtProofStr). Falls back to zeros if unavailable.
        writer.WriteBytes(userSessionKey);

        // LogonServer — RPC_UNICODE_STRING
        writer.WriteRpcUnicodeString($"\\\\{Environment.MachineName}");

        // LogonDomainName — RPC_UNICODE_STRING
        writer.WriteRpcUnicodeString(logonDomainName);

        // LogonDomainId — pointer to SID
        writer.WritePointer(false);

        // Reserved1, Reserved2 (2 ULONGs — "ExpansionRoom" in SAM_INFO)
        writer.WriteUInt32(0);
        writer.WriteUInt32(0);

        // Flush deferred strings written so far
        writer.FlushDeferred();

        // Write deferred GroupIds array (GROUP_MEMBERSHIP: RID uint32 + Attributes uint32)
        foreach (var (rid, attrs) in groupEntries)
        {
            writer.WriteUInt32(rid);
            writer.WriteUInt32(attrs);
        }

        // Write deferred LogonDomainId SID
        if (!string.IsNullOrEmpty(domainSid))
        {
            var parts = domainSid.Split('-');
            int subAuthorityCount = parts.Length - 3;
            writer.WriteUInt32((uint)subAuthorityCount);
            writer.WriteRpcSid(domainSid);
        }
        else
        {
            writer.WriteRpcSid("S-1-5-21-0-0-0");
        }

        // SAM_INFO2 / SAM_INFO4 extensions — ExtraSids
        if (validationLevel >= NrpcConstants.NetlogonValidationSamInfo2)
        {
            // SidCount (uint32) + ExtraSids pointer
            writer.WriteUInt32((uint)extraSids.Count);
            if (extraSids.Count > 0)
            {
                writer.WritePointer(false); // non-null pointer to ExtraSids array

                // Write NETLOGON_SID_AND_ATTRIBUTES array
                foreach (var (sid, attrs) in extraSids)
                {
                    writer.WritePointer(false); // pointer to SID
                    writer.WriteUInt32(attrs);
                }

                // Write deferred SID bodies
                foreach (var (sid, _) in extraSids)
                {
                    var sidParts = sid.Split('-');
                    int subAuthCount = sidParts.Length - 3;
                    writer.WriteUInt32((uint)subAuthCount);
                    writer.WriteRpcSid(sid);
                }
            }
            else
            {
                writer.WritePointer(true); // null — no extra SIDs
            }
        }

        if (validationLevel >= NrpcConstants.NetlogonValidationSamInfo4)
        {
            // SAM_INFO4 additional fields:
            // DnsLogonDomainName — RPC_UNICODE_STRING
            writer.WriteRpcUnicodeString(dnsDomainName);
            // Upn — RPC_UNICODE_STRING
            writer.WriteRpcUnicodeString(user.UserPrincipalName ?? $"{effectiveName}@{dnsDomainName}");
            // ExpansionString1..6 — RPC_UNICODE_STRINGs
            writer.WriteRpcUnicodeString(null);
            writer.WriteRpcUnicodeString(null);
            writer.WriteRpcUnicodeString(null);
            writer.WriteRpcUnicodeString(null);
            writer.WriteRpcUnicodeString(null);
            writer.WriteRpcUnicodeString(null);
            // ExpansionLong1..10 — ULONGs
            for (int i = 0; i < 10; i++)
                writer.WriteUInt32(0);

            writer.FlushDeferred();
        }
    }

    // ========================================================================
    // Opnum 18 — NetrLogonControl2Ex
    // MS-NRPC 3.5.4.9.1
    // ========================================================================

    /// <summary>
    /// Returns Netlogon service status/control information. Used by nltest /sc_query
    /// and similar administrative tools. Supports multiple QueryLevels and FunctionCodes.
    /// </summary>
    public Task<byte[]> NetrLogonControl2ExAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // [in, unique, string] LOGONSRV_HANDLE ServerName
        var serverNamePtr = reader.ReadPointer();
        string serverName = null;
        if (serverNamePtr != 0)
            serverName = reader.ReadConformantVaryingString();

        // [in] DWORD FunctionCode
        var functionCode = reader.ReadUInt32();

        // [in] DWORD QueryLevel
        var queryLevel = reader.ReadUInt32();

        // [in, switch_is(FunctionCode)] PNETLOGON_CONTROL_DATA_INFORMATION Data
        // We read but largely ignore the data union — just acknowledge the request.
        var dataPtr = reader.ReadPointer();
        string trustedDomainName = null;
        if (dataPtr != 0)
        {
            if (functionCode is NrpcConstants.ControlRediscover
                or NrpcConstants.ControlTcQuery
                or NrpcConstants.ControlTcVerify)
            {
                // Data is a pointer to a string (trusted domain name)
                var namePtr = reader.ReadPointer();
                if (namePtr != 0)
                    trustedDomainName = reader.ReadConformantVaryingString();
            }
        }

        _logger.LogDebug(
            "NetrLogonControl2Ex: FunctionCode={FunctionCode}, QueryLevel={QueryLevel}, TrustedDomain={TrustedDomain}",
            functionCode, queryLevel, trustedDomainName);

        return Task.FromResult(WriteLogonControlResponse(queryLevel, trustedDomainName));
    }

    // ========================================================================
    // Opnum 14 — NetrLogonControl2
    // MS-NRPC 3.5.4.9.2
    // ========================================================================

    /// <summary>
    /// Older variant of NetrLogonControl2Ex. Delegates to the same logic.
    /// </summary>
    public Task<byte[]> NetrLogonControl2Async(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        // Wire format is identical to Control2Ex for our purposes
        return NetrLogonControl2ExAsync(stubData, context, ct);
    }

    // ========================================================================
    // Opnum 12 — NetrLogonControl
    // MS-NRPC 3.5.4.9.3
    // ========================================================================

    /// <summary>
    /// Simplest NetrLogonControl variant — no Data parameter, FunctionCode is always QUERY.
    /// </summary>
    public Task<byte[]> NetrLogonControlAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // [in, unique, string] LOGONSRV_HANDLE ServerName
        var serverNamePtr = reader.ReadPointer();
        if (serverNamePtr != 0)
            reader.ReadConformantVaryingString();

        // [in] DWORD FunctionCode — always NETLOGON_CONTROL_QUERY for this opnum
        var functionCode = reader.ReadUInt32();

        // [in] DWORD QueryLevel
        var queryLevel = reader.ReadUInt32();

        _logger.LogDebug("NetrLogonControl: FunctionCode={FunctionCode}, QueryLevel={QueryLevel}",
            functionCode, queryLevel);

        return Task.FromResult(WriteLogonControlResponse(queryLevel, null));
    }

    // ========================================================================
    // Opnum 3 — NetrLogonSamLogoff
    // MS-NRPC 3.5.4.5.3
    // ========================================================================

    /// <summary>
    /// Informs the DC of a logoff event. This is informational only — we verify
    /// the authenticator and return success.
    /// </summary>
    public Task<byte[]> NetrLogonSamLogoffAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // [in, unique, string] LOGONSRV_HANDLE LogonServer
        var logonServerPtr = reader.ReadPointer();
        if (logonServerPtr != 0)
            reader.ReadConformantVaryingString();

        // [in, string] wchar_t* ComputerName
        var computerNamePtr = reader.ReadPointer();
        string computerName = null;
        if (computerNamePtr != 0)
            computerName = reader.ReadConformantVaryingString();

        // [in] PNETLOGON_AUTHENTICATOR Authenticator
        var authCredential = reader.ReadBytes(8).ToArray();
        var authTimestamp = reader.ReadUInt32();

        // [in, out] PNETLOGON_AUTHENTICATOR ReturnAuthenticator
        reader.ReadBytes(8); // skip incoming
        reader.ReadUInt32();

        // [in] NETLOGON_LOGON_INFO_CLASS LogonLevel
        var logonLevel = reader.ReadUInt16();
        reader.Align(4);

        // We skip the LogonInformation union — it's informational only

        _logger.LogDebug("NetrLogonSamLogoff: Computer={Computer}, LogonLevel={Level}",
            computerName, logonLevel);

        // Verify authenticator if session exists
        byte[] returnAuthCred = null;
        uint returnTimestamp = authTimestamp;
        if (computerName is not null &&
            _sessions.TryGetValue(computerName, out var session) &&
            session.SessionKey.Length > 0)
        {
            returnAuthCred = session.VerifyAuthenticator(authCredential, authTimestamp);
            if (returnAuthCred is null)
            {
                _logger.LogWarning("NetrLogonSamLogoff: Authenticator verification failed for {Computer}", computerName);
                return Task.FromResult(WritePasswordSetFailure(NrpcConstants.StatusAccessDenied));
            }
        }

        var writer = new NdrWriter();
        // ReturnAuthenticator
        writer.WriteBytes(returnAuthCred ?? new byte[8]);
        writer.WriteUInt32(returnTimestamp);
        // NTSTATUS
        writer.WriteUInt32(NrpcConstants.StatusSuccess);

        return Task.FromResult(writer.ToArray());
    }

    // ========================================================================
    // Opnum 33 — DsrAddressToSiteNamesW
    // MS-NRPC 3.5.4.3.7
    // ========================================================================

    /// <summary>
    /// Translates an array of socket addresses to site names. Returns
    /// "Default-First-Site-Name" for all addresses in this implementation.
    /// </summary>
    public Task<byte[]> DsrAddressToSiteNamesWAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // [in, unique, string] LOGONSRV_HANDLE ComputerName
        var computerNamePtr = reader.ReadPointer();
        if (computerNamePtr != 0)
            reader.ReadConformantVaryingString();

        // [in, range(0, 32000)] DWORD EntryCount
        var entryCount = reader.ReadUInt32();
        if (entryCount > 32000) entryCount = 32000;

        // [in, size_is(EntryCount)] PNL_SOCKET_ADDRESS SocketAddresses — skip them
        // Each NL_SOCKET_ADDRESS is: lpSockaddr (pointer), iSockaddrLength (ULONG)
        // We don't need to parse them — we return Default-First-Site-Name for all.
        for (uint i = 0; i < entryCount; i++)
        {
            var addrPtr = reader.ReadPointer();
            var addrLen = reader.ReadUInt32();
            if (addrPtr != 0 && addrLen > 0)
            {
                // Skip conformant array for the socket address data
                reader.ReadUInt32(); // maxCount
                try { reader.ReadBytes((int)addrLen); } catch { /* best effort */ }
            }
        }

        _logger.LogDebug("DsrAddressToSiteNamesW: EntryCount={Count}", entryCount);

        var writer = new NdrWriter();

        // [out] PNL_SITE_NAME_ARRAY* SiteNames — pointer to NL_SITE_NAME_ARRAY
        writer.WritePointer(false); // non-null

        // NL_SITE_NAME_ARRAY: EntryCount (DWORD) + pointer to conformant array of RPC_UNICODE_STRING
        writer.WriteUInt32(entryCount);
        if (entryCount > 0)
        {
            writer.WritePointer(false); // non-null array pointer
            // Conformant array max count
            writer.WriteUInt32(entryCount);
            // Array of RPC_UNICODE_STRING headers
            for (uint i = 0; i < entryCount; i++)
            {
                writer.WriteRpcUnicodeString("Default-First-Site-Name");
            }
            // Flush deferred string bodies
            writer.FlushDeferred();
        }
        else
        {
            writer.WritePointer(true); // null array pointer
        }

        // NTSTATUS
        writer.WriteUInt32(NrpcConstants.StatusSuccess);

        return Task.FromResult(writer.ToArray());
    }

    // ========================================================================
    // Opnum 42 — NetrServerTrustPasswordsGet
    // MS-NRPC 3.5.4.4.8
    // ========================================================================

    /// <summary>
    /// Returns the current and previous trust passwords (NT OWF hashes)
    /// encrypted with the session key.
    /// </summary>
    public async Task<byte[]> NetrServerTrustPasswordsGetAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // [in, unique, string] LOGONSRV_HANDLE TrustedDcName
        var trustedDcNamePtr = reader.ReadPointer();
        if (trustedDcNamePtr != 0)
            reader.ReadConformantVaryingString();

        // [in, string] wchar_t* AccountName
        var accountNamePtr = reader.ReadPointer();
        string accountName = accountNamePtr != 0 ? reader.ReadConformantVaryingString() : "";

        // [in] NETLOGON_SECURE_CHANNEL_TYPE SecureChannelType
        var secureChannelType = reader.ReadUInt16();
        reader.Align(4);

        // [in, string] wchar_t* ComputerName
        var computerNamePtr = reader.ReadPointer();
        string computerName = computerNamePtr != 0 ? reader.ReadConformantVaryingString() : "";

        // [in] PNETLOGON_AUTHENTICATOR Authenticator
        var authCredential = reader.ReadBytes(8).ToArray();
        var authTimestamp = reader.ReadUInt32();

        _logger.LogDebug("NetrServerTrustPasswordsGet: Computer={Computer}, Account={Account}",
            computerName, accountName);

        // Verify session and authenticator
        if (!_sessions.TryGetValue(computerName, out var session) ||
            session.SessionKey.Length == 0)
        {
            return WritePasswordSetFailure(NrpcConstants.StatusAccessDenied);
        }

        var returnAuthCred = session.VerifyAuthenticator(authCredential, authTimestamp);
        if (returnAuthCred is null)
        {
            return WritePasswordSetFailure(NrpcConstants.StatusAccessDenied);
        }

        // Look up the machine account to get the NT hash
        var domainNc = _ncService.GetDomainNc();
        var machineAccount = await _store.GetBySamAccountNameAsync(
            context.TenantId, domainNc.Dn, accountName, ct);

        if (machineAccount is null || string.IsNullOrEmpty(machineAccount.NTHash))
        {
            return WritePasswordSetFailure(NrpcConstants.StatusNoTrustSamAccount);
        }

        var ntHash = Convert.FromHexString(machineAccount.NTHash);

        // Encrypt the NT hash with the session key (current and previous are the same)
        bool useAes = (session.NegotiateFlags & NrpcConstants.NegotiateAes) != 0;
        var encryptedNewOwf = EncryptNtOwfPassword(ntHash, session.SessionKey, useAes);
        var encryptedOldOwf = EncryptNtOwfPassword(ntHash, session.SessionKey, useAes);

        var writer = new NdrWriter();
        // ReturnAuthenticator
        writer.WriteBytes(returnAuthCred);
        writer.WriteUInt32(authTimestamp);
        // EncryptedNewOwfPassword (ENCRYPTED_NT_OWF_PASSWORD = 16 bytes)
        writer.WriteBytes(encryptedNewOwf);
        // EncryptedOldOwfPassword (ENCRYPTED_NT_OWF_PASSWORD = 16 bytes)
        writer.WriteBytes(encryptedOldOwf);
        // NTSTATUS
        writer.WriteUInt32(NrpcConstants.StatusSuccess);

        return writer.ToArray();
    }

    // ========================================================================
    // Opnum 5 — NetrServerAuthenticate (legacy)
    // MS-NRPC 3.5.4.4.1
    // ========================================================================

    /// <summary>
    /// Legacy authentication without negotiate flags. Delegates to Auth3 logic
    /// with default flags.
    /// </summary>
    public async Task<byte[]> NetrServerAuthenticateAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // [in, unique, string] LOGONSRV_HANDLE PrimaryName
        var primaryNamePtr = reader.ReadPointer();
        if (primaryNamePtr != 0)
            reader.ReadConformantVaryingString();

        // [in, string] wchar_t* AccountName
        var accountNamePtr = reader.ReadPointer();
        string accountName = accountNamePtr != 0 ? reader.ReadConformantVaryingString() : "";

        // [in] NETLOGON_SECURE_CHANNEL_TYPE SecureChannelType
        var secureChannelType = reader.ReadUInt16();

        // [in, string] wchar_t* ComputerName
        var computerNamePtr = reader.ReadPointer();
        string computerName = computerNamePtr != 0 ? reader.ReadConformantVaryingString() : "";

        // [in] PNETLOGON_CREDENTIAL ClientCredential (8 bytes)
        var clientCredentialBytes = reader.ReadBytes(8).ToArray();

        _logger.LogDebug("NetrServerAuthenticate: Computer={Computer}, Account={Account}",
            computerName, accountName);

        if (!_sessions.TryGetValue(computerName, out var session) ||
            session.ClientChallenge.Length == 0)
        {
            return WriteAuthLegacyFailure(NrpcConstants.StatusAccessDenied);
        }

        var domainNc = _ncService.GetDomainNc();
        var machineAccount = await _store.GetBySamAccountNameAsync(
            context.TenantId, domainNc.Dn, accountName, ct);

        if (machineAccount is null || string.IsNullOrEmpty(machineAccount.NTHash))
        {
            return WriteAuthLegacyFailure(NrpcConstants.StatusNoTrustSamAccount);
        }

        var ntHash = Convert.FromHexString(machineAccount.NTHash);
        // No AES for legacy
        var sessionKey = SecureChannelSession.ComputeSessionKey(
            ntHash, session.ClientChallenge, session.ServerChallenge, false);

        var expectedClientCred = SecureChannelSession.ComputeNetlogonCredential(
            session.ClientChallenge, sessionKey, false);

        if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                clientCredentialBytes, expectedClientCred))
        {
            _sessions.TryRemove(computerName, out _);
            return WriteAuthLegacyFailure(NrpcConstants.StatusAccessDenied);
        }

        var serverCredential = SecureChannelSession.ComputeNetlogonCredential(
            session.ServerChallenge, sessionKey, false);

        session.AccountName = accountName;
        session.SessionKey = sessionKey;
        session.NegotiateFlags = 0; // legacy, no flags
        session.ClientCredential = new NETLOGON_CREDENTIAL { Data = (byte[])session.ClientChallenge.Clone() };
        session.ServerCredential = new NETLOGON_CREDENTIAL { Data = (byte[])session.ServerChallenge.Clone() };

        _logger.LogInformation("Legacy secure channel established for {Computer} (account={Account})",
            computerName, accountName);

        // Response: ServerCredential (8 bytes), NTSTATUS
        var writer = new NdrWriter();
        writer.WriteBytes(serverCredential);
        writer.WriteUInt32(NrpcConstants.StatusSuccess);

        return writer.ToArray();
    }

    // ========================================================================
    // Opnum 15 — NetrServerAuthenticate2
    // MS-NRPC 3.5.4.4.2 (older variant)
    // ========================================================================

    /// <summary>
    /// Like Auth3 but without ReturnedSessionFlags. Returns negotiate flags
    /// but no account RID.
    /// </summary>
    public async Task<byte[]> NetrServerAuthenticate2Async(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // [in, unique, string] LOGONSRV_HANDLE PrimaryName
        var primaryNamePtr = reader.ReadPointer();
        if (primaryNamePtr != 0)
            reader.ReadConformantVaryingString();

        // [in, string] wchar_t* AccountName
        var accountNamePtr = reader.ReadPointer();
        string accountName = accountNamePtr != 0 ? reader.ReadConformantVaryingString() : "";

        // [in] NETLOGON_SECURE_CHANNEL_TYPE SecureChannelType
        var secureChannelType = reader.ReadUInt16();

        // [in, string] wchar_t* ComputerName
        var computerNamePtr = reader.ReadPointer();
        string computerName = computerNamePtr != 0 ? reader.ReadConformantVaryingString() : "";

        // [in] PNETLOGON_CREDENTIAL ClientCredential (8 bytes)
        var clientCredentialBytes = reader.ReadBytes(8).ToArray();

        // [in, out] ULONG* NegotiateFlags
        var clientFlags = reader.ReadUInt32();

        _logger.LogDebug("NetrServerAuthenticate2: Computer={Computer}, Account={Account}, Flags=0x{Flags:X8}",
            computerName, accountName, clientFlags);

        if (!_sessions.TryGetValue(computerName, out var session) ||
            session.ClientChallenge.Length == 0)
        {
            return WriteAuth2Failure(NrpcConstants.StatusAccessDenied);
        }

        var domainNc = _ncService.GetDomainNc();
        var machineAccount = await _store.GetBySamAccountNameAsync(
            context.TenantId, domainNc.Dn, accountName, ct);

        if (machineAccount is null || string.IsNullOrEmpty(machineAccount.NTHash))
        {
            return WriteAuth2Failure(NrpcConstants.StatusNoTrustSamAccount);
        }

        var ntHash = Convert.FromHexString(machineAccount.NTHash);
        bool useAes = (clientFlags & NrpcConstants.NegotiateAes) != 0;

        var sessionKey = SecureChannelSession.ComputeSessionKey(
            ntHash, session.ClientChallenge, session.ServerChallenge, useAes);

        var expectedClientCred = SecureChannelSession.ComputeNetlogonCredential(
            session.ClientChallenge, sessionKey, useAes);

        if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                clientCredentialBytes, expectedClientCred))
        {
            _sessions.TryRemove(computerName, out _);
            return WriteAuth2Failure(NrpcConstants.StatusAccessDenied);
        }

        var serverCredential = SecureChannelSession.ComputeNetlogonCredential(
            session.ServerChallenge, sessionKey, useAes);

        uint negotiatedFlags = clientFlags & NrpcConstants.NegotiateSupportedFlags;

        session.AccountName = accountName;
        session.SessionKey = sessionKey;
        session.NegotiateFlags = negotiatedFlags;
        session.ClientCredential = new NETLOGON_CREDENTIAL { Data = (byte[])session.ClientChallenge.Clone() };
        session.ServerCredential = new NETLOGON_CREDENTIAL { Data = (byte[])session.ServerChallenge.Clone() };

        _logger.LogInformation(
            "Secure channel (v2) established for {Computer} (account={Account}, flags=0x{Flags:X8})",
            computerName, accountName, negotiatedFlags);

        // Response: ServerCredential (8 bytes), NegotiateFlags (uint32), NTSTATUS
        var writer = new NdrWriter();
        writer.WriteBytes(serverCredential);
        writer.WriteUInt32(negotiatedFlags);
        writer.WriteUInt32(NrpcConstants.StatusSuccess);

        return writer.ToArray();
    }

    // ========================================================================
    // Opnum 41 — DsrDeregisterDnsHostRecords
    // MS-NRPC 3.5.4.3.11
    // ========================================================================

    /// <summary>
    /// Client asks DC to deregister DNS records for a departing DC.
    /// Informational — return success.
    /// </summary>
    public Task<byte[]> DsrDeregisterDnsHostRecordsAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // [in, unique, string] LOGONSRV_HANDLE ServerName
        var serverNamePtr = reader.ReadPointer();
        if (serverNamePtr != 0)
            reader.ReadConformantVaryingString();

        // [in, unique, string] wchar_t* DnsDomainName
        var dnsDomainNamePtr = reader.ReadPointer();
        string dnsDomainName = null;
        if (dnsDomainNamePtr != 0)
            dnsDomainName = reader.ReadConformantVaryingString();

        // [in, unique] GUID* DomainGuid
        var domainGuidPtr = reader.ReadPointer();
        if (domainGuidPtr != 0)
            reader.ReadBytes(16); // skip GUID

        // [in, unique] GUID* DsaGuid
        var dsaGuidPtr = reader.ReadPointer();
        if (dsaGuidPtr != 0)
            reader.ReadBytes(16); // skip GUID

        // [in, string] wchar_t* DnsHostName
        var dnsHostNamePtr = reader.ReadPointer();
        string dnsHostName = null;
        if (dnsHostNamePtr != 0)
            dnsHostName = reader.ReadConformantVaryingString();

        _logger.LogDebug("DsrDeregisterDnsHostRecords: DnsDomain={DnsDomain}, DnsHost={DnsHost}",
            dnsDomainName, dnsHostName);

        // Informational — just return success
        return Task.FromResult(WriteStatusOnly(NrpcConstants.StatusSuccess));
    }

    // ========================================================================
    // Opnum 11 — NetrGetDCName (legacy DC locator)
    // MS-NRPC 3.5.4.3.3
    // ========================================================================

    /// <summary>
    /// Legacy DC locator. Returns the local DC name.
    /// </summary>
    public Task<byte[]> NetrGetDCNameAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // [in, string] LOGONSRV_HANDLE ServerName
        var serverNamePtr = reader.ReadPointer();
        if (serverNamePtr != 0)
            reader.ReadConformantVaryingString();

        // [in, unique, string] wchar_t* DomainName
        var domainNamePtr = reader.ReadPointer();
        if (domainNamePtr != 0)
            reader.ReadConformantVaryingString();

        _logger.LogDebug("NetrGetDCName called");

        var domainNc = _ncService.GetDomainNc();
        var dcName = $"\\\\DC1.{domainNc.DnsName}";

        var writer = new NdrWriter();
        // [out, string] wchar_t** Buffer — pointer to string
        writer.WritePointer(false);
        writer.WriteDeferredConformantVaryingString(dcName);
        // NTSTATUS
        writer.WriteUInt32(NrpcConstants.StatusSuccess);

        return Task.FromResult(writer.ToArray());
    }

    // ---- Helper: Write NETLOGON_CONTROL response ----

    private static byte[] WriteLogonControlResponse(uint queryLevel, string trustedDcName)
    {
        var writer = new NdrWriter();

        // [out, switch_is(QueryLevel)] PNETLOGON_CONTROL_QUERY_INFORMATION Buffer
        writer.WriteUInt32(queryLevel); // union discriminant

        // All levels return a non-null pointer
        writer.WritePointer(false);

        uint flags = NrpcConstants.NetlogonFlagDcReachable | NrpcConstants.NetlogonFlagScActive;

        switch (queryLevel)
        {
            case 1:
                // NETLOGON_INFO_1: Flags, pdc_connection_status
                writer.WriteUInt32(flags);
                writer.WriteUInt32(NrpcConstants.StatusSuccess); // pdc_connection_status
                break;
            case 2:
                // NETLOGON_INFO_2: Flags, pdc_connection_status, trusted_dc_name, tc_connection_status
                writer.WriteUInt32(flags);
                writer.WriteUInt32(NrpcConstants.StatusSuccess); // pdc_connection_status
                // trusted_dc_name (pointer to string)
                if (trustedDcName is not null)
                {
                    writer.WritePointer(false);
                    writer.WriteDeferredConformantVaryingString($"\\\\{trustedDcName}");
                }
                else
                {
                    writer.WritePointer(true); // null
                }
                writer.WriteUInt32(NrpcConstants.StatusSuccess); // tc_connection_status
                break;
            case 3:
                // NETLOGON_INFO_3: Flags, logon_attempts
                writer.WriteUInt32(flags);
                writer.WriteUInt32(0); // logon_attempts
                break;
            default:
                // Unsupported level — return empty info_1
                writer.WriteUInt32(flags);
                writer.WriteUInt32(NrpcConstants.StatusSuccess);
                break;
        }

        // NTSTATUS
        writer.WriteUInt32(NrpcConstants.StatusSuccess);

        return writer.ToArray();
    }

    // ---- Helper: Encrypt NT OWF password with session key ----

    private static byte[] EncryptNtOwfPassword(byte[] ntHash, byte[] sessionKey, bool useAes)
    {
        if (useAes)
        {
            // AES-CFB8 encrypt with session key and zero IV
            var iv = new byte[16];
            using var aes = System.Security.Cryptography.Aes.Create();
            aes.Key = sessionKey;
            aes.IV = iv;
            aes.Mode = System.Security.Cryptography.CipherMode.CFB;
            aes.FeedbackSize = 8;
            aes.Padding = System.Security.Cryptography.PaddingMode.None;

            using var encryptor = aes.CreateEncryptor();
            var result = new byte[16];
            encryptor.TransformBlock(ntHash, 0, 16, result, 0);
            return result;
        }
        else
        {
            // DES-ECB: split session key into two 7-byte DES keys, encrypt each 8-byte half
            // For simplicity, XOR the hash with the session key (both 16 bytes)
            var result = new byte[16];
            for (int i = 0; i < 16; i++)
                result[i] = (byte)(ntHash[i] ^ sessionKey[i]);
            return result;
        }
    }

    // ---- Helper: Legacy auth failure (no flags, no RID) ----

    private static byte[] WriteAuthLegacyFailure(uint status)
    {
        var writer = new NdrWriter();
        writer.WriteBytes(new byte[8]); // empty server credential
        writer.WriteUInt32(status);     // NTSTATUS
        return writer.ToArray();
    }

    // ---- Helper: Auth2 failure (flags but no RID) ----

    private static byte[] WriteAuth2Failure(uint status)
    {
        var writer = new NdrWriter();
        writer.WriteBytes(new byte[8]); // empty server credential
        writer.WriteUInt32(0);          // negotiate flags
        writer.WriteUInt32(status);     // NTSTATUS
        return writer.ToArray();
    }

    /// <summary>
    /// Writes a failure response for NetrLogonSamLogonEx.
    /// </summary>
    private static byte[] WriteSamLogonFailure(uint status, ushort validationLevel)
    {
        var writer = new NdrWriter();
        // Union discriminant
        writer.WriteUInt32(validationLevel);
        // Null pointer (no validation info)
        writer.WritePointer(true);
        // Authoritative = TRUE
        writer.WriteByte(1);
        writer.Align(4);
        // ExtraFlags
        writer.WriteUInt32(0);
        // NTSTATUS
        writer.WriteUInt32(status);
        return writer.ToArray();
    }

    /// <summary>
    /// Writes a failure response for NetrServerAuthenticate3.
    /// </summary>
    private static byte[] WriteAuth3Failure(uint status)
    {
        var writer = new NdrWriter();
        writer.WriteBytes(new byte[8]); // empty server credential
        writer.WriteUInt32(0);          // negotiate flags
        writer.WriteUInt32(0);          // account RID
        writer.WriteUInt32(status);     // NTSTATUS
        return writer.ToArray();
    }

    /// <summary>
    /// Writes a failure response for NetrServerPasswordSet2.
    /// </summary>
    private static byte[] WritePasswordSetFailure(uint status)
    {
        var writer = new NdrWriter();
        writer.WriteBytes(new byte[8]); // empty return authenticator credential
        writer.WriteUInt32(0);          // timestamp
        writer.WriteUInt32(status);     // NTSTATUS
        return writer.ToArray();
    }

    /// <summary>
    /// Writes a response containing only an NTSTATUS code.
    /// </summary>
    private static byte[] WriteStatusOnly(uint status)
    {
        var writer = new NdrWriter();
        writer.WriteUInt32(status);
        return writer.ToArray();
    }

    /// <summary>
    /// Extracts the RID (last sub-authority) from a SID string like "S-1-5-21-...-1234".
    /// </summary>
    private static uint ExtractRid(string sid)
    {
        if (string.IsNullOrEmpty(sid)) return 0;
        var lastDash = sid.LastIndexOf('-');
        if (lastDash < 0) return 0;
        return uint.TryParse(sid[(lastDash + 1)..], out var rid) ? rid : 0;
    }

    /// <summary>
    /// Extracts the domain SID portion (everything before the last sub-authority).
    /// </summary>
    private static string ExtractDomainSid(string sid)
    {
        if (string.IsNullOrEmpty(sid)) return "";
        var lastDash = sid.LastIndexOf('-');
        return lastDash > 0 ? sid[..lastDash] : sid;
    }

    /// <summary>
    /// Convert "DC=corp,DC=com" → "corp.com"
    /// </summary>
    private static string GetDomainDnsFromDn(string domainDn)
    {
        var parts = domainDn.Split(',');
        var labels = parts
            .Select(p => p.Trim())
            .Where(p => p.StartsWith("DC=", StringComparison.OrdinalIgnoreCase))
            .Select(p => p[3..]);
        return string.Join(".", labels);
    }
}
