using System.Formats.Asn1;
using System.Security.Cryptography;
using Directory.Core.Interfaces;
using Directory.Ldap.Protocol;
using Directory.Ldap.Server;
using Directory.Security.Apds;
using ApdsNtStatus = Directory.Security.Apds.NtStatus;
using Kerberos.NET;
using Kerberos.NET.Crypto;
using Kerberos.NET.Entities;
using Microsoft.Extensions.Logging;

namespace Directory.Ldap.Handlers;

public class SaslHandler
{
    private readonly IDirectoryStore _store;
    private readonly IPasswordPolicy _passwordPolicy;
    private readonly ILogger<SaslHandler> _logger;
    private readonly KerberosValidator _kerberosValidator;
    private readonly ApdsLogonProcessor _apdsProcessor;

    public SaslHandler(IDirectoryStore store, IPasswordPolicy passwordPolicy, ILogger<SaslHandler> logger)
    {
        _store = store;
        _passwordPolicy = passwordPolicy;
        _logger = logger;
    }

    public SaslHandler(IDirectoryStore store, IPasswordPolicy passwordPolicy, ILogger<SaslHandler> logger, KerberosValidator kerberosValidator)
        : this(store, passwordPolicy, logger)
    {
        _kerberosValidator = kerberosValidator;
    }

    public SaslHandler(IDirectoryStore store, IPasswordPolicy passwordPolicy, ILogger<SaslHandler> logger, KerberosValidator kerberosValidator, ApdsLogonProcessor apdsProcessor)
        : this(store, passwordPolicy, logger, kerberosValidator)
    {
        _apdsProcessor = apdsProcessor;
    }

    // GSSAPI (Kerberos) SASL bind
    // In a real implementation, this would use Kerberos.NET to validate the AP-REQ token
    // For now, extract the Kerberos ticket and validate it
    public async Task<SaslBindResult> HandleGssApiAsync(byte[] token, LdapConnectionState state, CancellationToken ct)
    {
        _logger.LogDebug("GSSAPI SASL bind attempt");

        // Parse the SPNEGO/GSSAPI token
        // The token contains a Kerberos AP-REQ wrapped in GSSAPI
        // We need to unwrap and validate using the KDC's service key

        try
        {
            // Check for SPNEGO wrapping (OID 1.2.840.113554.1.2.2 for Kerberos)
            // or (OID 1.3.6.1.5.5.2 for SPNEGO)
            if (IsSpnegoToken(token))
            {
                return await HandleSpnegoAsync(token, state, ct);
            }

            // Direct GSSAPI/Kerberos token
            return await ValidateKerberosTokenAsync(token, state, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GSSAPI authentication failed");
            return SaslBindResult.Failure("GSSAPI authentication failed: " + ex.Message);
        }
    }

    // GSS-SPNEGO negotiation
    public async Task<SaslBindResult> HandleSpnegoAsync(byte[] token, LdapConnectionState state, CancellationToken ct)
    {
        _logger.LogDebug("SPNEGO SASL bind attempt");

        try
        {
            // SPNEGO tokens start with APPLICATION[0] containing an OID
            // We support two mechanisms: Kerberos and NTLM
            var mechToken = ExtractMechToken(token);

            if (mechToken == null || mechToken.Length == 0)
            {
                return SaslBindResult.Failure("Empty SPNEGO mechanism token");
            }

            // Check if this is an NTLM token (starts with "NTLMSSP\0")
            if (IsNtlmToken(mechToken))
            {
                return await HandleNtlmSpnegoAsync(mechToken, state, ct);
            }

            // Otherwise, try Kerberos
            return await ValidateKerberosTokenAsync(mechToken, state, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SPNEGO authentication failed");
            return SaslBindResult.Failure("SPNEGO authentication failed: " + ex.Message);
        }
    }

    // NTLM authentication within SPNEGO
    private async Task<SaslBindResult> HandleNtlmSpnegoAsync(byte[] ntlmToken, LdapConnectionState state, CancellationToken ct)
    {
        if (ntlmToken.Length < 12)
            return SaslBindResult.Failure("Invalid NTLM token");

        // Read NTLM message type (offset 8, 4 bytes LE)
        var messageType = BitConverter.ToInt32(ntlmToken, 8);

        switch (messageType)
        {
            case 1: // NEGOTIATE_MESSAGE
                return HandleNtlmNegotiate(ntlmToken, state);
            case 3: // AUTHENTICATE_MESSAGE
                return await HandleNtlmAuthenticateAsync(ntlmToken, state, ct);
            default:
                return SaslBindResult.Failure($"Unsupported NTLM message type: {messageType}");
        }
    }

    private SaslBindResult HandleNtlmNegotiate(byte[] token, LdapConnectionState state)
    {
        // Generate CHALLENGE_MESSAGE (type 2)
        var serverChallenge = RandomNumberGenerator.GetBytes(8);
        state.NtlmChallenge = serverChallenge;

        var challengeMsg = BuildNtlmChallenge(serverChallenge);

        // Wrap in SPNEGO response token
        var spnegoResponse = WrapSpnegoResponse(challengeMsg, isAcceptIncomplete: true);

        return SaslBindResult.ContinueNeeded(spnegoResponse);
    }

    private async Task<SaslBindResult> HandleNtlmAuthenticateAsync(byte[] token, LdapConnectionState state, CancellationToken ct)
    {
        if (state.NtlmChallenge == null)
            return SaslBindResult.Failure("No NTLM challenge in progress");

        // Parse AUTHENTICATE_MESSAGE fields
        var (domain, username, ntResponse, lmResponse) = ParseNtlmAuthenticate(token);

        if (string.IsNullOrEmpty(username))
            return SaslBindResult.Failure("Empty username in NTLM authenticate");

        _logger.LogDebug("NTLM authenticate: {Domain}\\{User}", domain, username);

        // Route through APDS for full account restriction checking when available
        if (_apdsProcessor is not null)
        {
            var logonResult = await _apdsProcessor.ProcessNetworkLogonAsync(
                state.TenantId, domain, username, ntResponse, lmResponse, state.NtlmChallenge, ct);

            state.NtlmChallenge = null; // Clear challenge

            if (logonResult.Status == ApdsNtStatus.StatusSuccess && logonResult.User is not null)
            {
                state.BindStatus = BindState.SaslBound;
                state.BoundDn = logonResult.User.DistinguishedName;
                state.BoundSid = logonResult.User.ObjectSid;

                var spnegoResponse = WrapSpnegoResponse([], isAcceptIncomplete: false);
                return SaslBindResult.Success(logonResult.User.DistinguishedName, spnegoResponse);
            }

            return SaslBindResult.Failure($"NTLM authentication failed: {logonResult.Status}");
        }

        // Fallback: inline NTLMv2 validation when APDS is not injected
        var user = await _store.GetBySamAccountNameAsync(state.TenantId, state.DomainDn, username, ct);
        if (user == null)
        {
            // Try UPN
            var upn = $"{username}@{domain}";
            user = await _store.GetByUpnAsync(state.TenantId, upn, ct);
        }

        if (user == null || string.IsNullOrEmpty(user.NTHash))
            return SaslBindResult.Failure("User not found or no credentials");

        // Validate NTLMv2 response
        var ntHash = Convert.FromHexString(user.NTHash);
        var isValid = ValidateNtlmv2Response(ntHash, username, domain, state.NtlmChallenge, ntResponse);

        state.NtlmChallenge = null; // Clear challenge

        if (isValid)
        {
            state.BindStatus = BindState.SaslBound;
            state.BoundDn = user.DistinguishedName;
            state.BoundSid = user.ObjectSid;

            var spnegoResponse = WrapSpnegoResponse([], isAcceptIncomplete: false);
            return SaslBindResult.Success(user.DistinguishedName, spnegoResponse);
        }

        return SaslBindResult.Failure("NTLM authentication failed");
    }

    private async Task<SaslBindResult> ValidateKerberosTokenAsync(byte[] token, LdapConnectionState state, CancellationToken ct)
    {
        _logger.LogDebug("Validating Kerberos AP-REQ token ({Length} bytes)", token.Length);

        if (_kerberosValidator == null)
        {
            _logger.LogWarning("Kerberos validator not configured; cannot validate AP-REQ");
            return SaslBindResult.Failure("Kerberos SASL not configured");
        }

        try
        {
            // Validate the AP-REQ token using the Kerberos.NET validator
            // The validator uses the service keytab/key to decrypt and verify the ticket
            var decryptedApReq = await _kerberosValidator.Validate(token);

            // Extract the authenticated principal name from the ticket's CName field
            var principalName = decryptedApReq.Ticket.CName.FullyQualifiedName;

            _logger.LogInformation("Kerberos SASL authentication succeeded for {Principal}", principalName);

            // Look up the user in the directory to get the DN for LDAP session binding
            var samAccountName = principalName.Contains('@') ? principalName.Split('@')[0] : principalName;

            var user = await _store.GetBySamAccountNameAsync(state.TenantId, state.DomainDn, samAccountName, ct);

            if (user == null)
            {
                // Try by UPN
                user = await _store.GetByUpnAsync(state.TenantId, principalName, ct);
            }

            if (user == null)
            {
                _logger.LogWarning("Kerberos principal {Principal} authenticated but no matching directory object found", principalName);
                return SaslBindResult.Failure($"No directory entry for principal: {principalName}");
            }

            // Bind the LDAP session to the authenticated user
            state.BindStatus = BindState.SaslBound;
            state.BoundDn = user.DistinguishedName;
            state.BoundSid = user.ObjectSid;

            // Build SPNEGO accept-complete response token
            var spnegoResponse = WrapSpnegoResponse([], isAcceptIncomplete: false);

            return SaslBindResult.Success(user.DistinguishedName, spnegoResponse);
        }
        catch (KerberosValidationException ex)
        {
            _logger.LogWarning(ex, "Kerberos ticket validation failed");
            return SaslBindResult.Failure("Kerberos authentication failed: " + ex.Message);
        }
    }

    private static bool IsSpnegoToken(byte[] token)
    {
        // SPNEGO tokens start with 0x60 (APPLICATION[0] CONSTRUCTED)
        return token.Length > 2 && token[0] == 0x60;
    }

    private static bool IsNtlmToken(byte[] token)
    {
        // NTLM tokens start with "NTLMSSP\0"
        return token.Length >= 8 &&
               token[0] == 'N' && token[1] == 'T' && token[2] == 'L' && token[3] == 'M' &&
               token[4] == 'S' && token[5] == 'S' && token[6] == 'P' && token[7] == 0;
    }

    // SPNEGO OID: 1.3.6.1.5.5.2
    private const string SpnegoOid = "1.3.6.1.5.5.2";

    private static byte[] ExtractMechToken(byte[] spnegoToken)
    {
        // Parse SPNEGO token using proper ASN.1 decoding per RFC 4178.
        // Handles both NegTokenInit (initial) and NegTokenResp (continuation) token types.
        try
        {
            var reader = new AsnReader(spnegoToken, AsnEncodingRules.BER);
            var outerTag = reader.PeekTag();

            // GSS-API InitialContextToken: APPLICATION [0] { OID, NegotiationToken }
            if (outerTag.TagClass == TagClass.Application && outerTag.TagValue == 0)
            {
                return ExtractFromNegTokenInit(reader);
            }

            // NegTokenResp: CONTEXT [1] { SEQUENCE { ... responseToken [2] ... } }
            if (outerTag.TagClass == TagClass.ContextSpecific && outerTag.TagValue == 1)
            {
                return ExtractFromNegTokenResp(reader);
            }

            // Some implementations send a bare NegTokenInit wrapped in CONTEXT [0]
            if (outerTag.TagClass == TagClass.ContextSpecific && outerTag.TagValue == 0)
            {
                var ctxReader = reader.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true));
                return ExtractMechTokenFromNegTokenInitSequence(ctxReader);
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Extract mechToken from a GSS-API InitialContextToken containing a SPNEGO NegTokenInit.
    /// Structure: APPLICATION [0] { thisMech OID, NegotiationToken CONTEXT [0] { NegTokenInit SEQUENCE { ... } } }
    /// </summary>
    private static byte[] ExtractFromNegTokenInit(AsnReader reader)
    {
        var appReader = reader.ReadSequence(new Asn1Tag(TagClass.Application, 0, isConstructed: true));

        // Read and verify the SPNEGO OID
        var oid = appReader.ReadObjectIdentifier();
        if (oid != SpnegoOid)
            return null;

        // NegotiationToken is a CHOICE; NegTokenInit is CONTEXT [0]
        if (!appReader.HasData)
            return null;

        var tokenTag = appReader.PeekTag();

        if (tokenTag.TagClass == TagClass.ContextSpecific && tokenTag.TagValue == 0)
        {
            // NegTokenInit wrapped in CONTEXT [0]
            var ctxReader = appReader.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true));
            return ExtractMechTokenFromNegTokenInitSequence(ctxReader);
        }

        if (tokenTag.TagClass == TagClass.ContextSpecific && tokenTag.TagValue == 1)
        {
            // NegTokenResp wrapped in CONTEXT [1] (unusual but possible in continuation)
            return ExtractFromNegTokenResp(appReader);
        }

        return null;
    }

    /// <summary>
    /// Extract mechToken from a NegTokenInit SEQUENCE.
    /// NegTokenInit ::= SEQUENCE { mechTypes [0], reqFlags [1]?, mechToken [2]?, mechListMIC [3]? }
    /// </summary>
    private static byte[] ExtractMechTokenFromNegTokenInitSequence(AsnReader seqParent)
    {
        var seqReader = seqParent.ReadSequence();

        while (seqReader.HasData)
        {
            var fieldTag = seqReader.PeekTag();

            if (fieldTag.TagClass == TagClass.ContextSpecific)
            {
                switch (fieldTag.TagValue)
                {
                    case 0: // mechTypes [0] SEQUENCE OF OID — skip
                        seqReader.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true));
                        break;
                    case 1: // reqFlags [1] BIT STRING — skip
                        seqReader.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 1, isConstructed: true));
                        break;
                    case 2: // mechToken [2] OCTET STRING — this is what we want
                    {
                        var tokenReader = seqReader.ReadSequence(
                            new Asn1Tag(TagClass.ContextSpecific, 2, isConstructed: true));
                        return tokenReader.ReadOctetString();
                    }
                    case 3: // mechListMIC [3] OCTET STRING — skip
                        seqReader.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 3, isConstructed: true));
                        break;
                    default:
                        seqReader.ReadEncodedValue();
                        break;
                }
            }
            else
            {
                seqReader.ReadEncodedValue();
            }
        }

        return null;
    }

    /// <summary>
    /// Extract responseToken from a NegTokenResp (continuation token).
    /// NegTokenResp ::= SEQUENCE { negState [0]?, supportedMech [1]?, responseToken [2]?, mechListMIC [3]? }
    /// The token is wrapped in CONTEXT [1].
    /// </summary>
    private static byte[] ExtractFromNegTokenResp(AsnReader reader)
    {
        var ctxReader = reader.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 1, isConstructed: true));
        var seqReader = ctxReader.ReadSequence();

        while (seqReader.HasData)
        {
            var fieldTag = seqReader.PeekTag();

            if (fieldTag.TagClass == TagClass.ContextSpecific)
            {
                switch (fieldTag.TagValue)
                {
                    case 0: // negState [0] ENUMERATED — skip
                    {
                        var stateReader = seqReader.ReadSequence(
                            new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true));
                        stateReader.ReadEnumeratedBytes();
                        break;
                    }
                    case 1: // supportedMech [1] OID — skip
                    {
                        var mechReader = seqReader.ReadSequence(
                            new Asn1Tag(TagClass.ContextSpecific, 1, isConstructed: true));
                        mechReader.ReadObjectIdentifier();
                        break;
                    }
                    case 2: // responseToken [2] OCTET STRING — this is what we want
                    {
                        var tokenReader = seqReader.ReadSequence(
                            new Asn1Tag(TagClass.ContextSpecific, 2, isConstructed: true));
                        return tokenReader.ReadOctetString();
                    }
                    case 3: // mechListMIC [3] OCTET STRING — skip
                    {
                        var micReader = seqReader.ReadSequence(
                            new Asn1Tag(TagClass.ContextSpecific, 3, isConstructed: true));
                        micReader.ReadOctetString();
                        break;
                    }
                    default:
                        seqReader.ReadEncodedValue();
                        break;
                }
            }
            else
            {
                seqReader.ReadEncodedValue();
            }
        }

        return null;
    }

    private static byte[] BuildNtlmChallenge(byte[] serverChallenge)
    {
        // Build minimal NTLM CHALLENGE_MESSAGE (type 2)
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        bw.Write("NTLMSSP\0"u8); // Signature
        bw.Write(2); // Message type

        // Target name (empty)
        bw.Write((short)0); // TargetNameLen
        bw.Write((short)0); // TargetNameMaxLen
        bw.Write(32); // TargetNameOffset

        // Negotiate flags
        bw.Write(0x00028233); // NTLMSSP_NEGOTIATE_UNICODE | NTLMSSP_NEGOTIATE_NTLM | NTLMSSP_NEGOTIATE_TARGET_INFO | NTLMSSP_NEGOTIATE_56 | NTLMSSP_NEGOTIATE_128

        // Server challenge
        bw.Write(serverChallenge);

        // Reserved
        bw.Write(0L);

        return ms.ToArray();
    }

    private enum NegState
    {
        AcceptCompleted = 0,
        AcceptIncomplete = 1,
    }

    private static byte[] WrapSpnegoResponse(byte[] mechToken, bool isAcceptIncomplete)
    {
        // Build SPNEGO NegTokenResp using proper ASN.1 encoding per RFC 4178.
        // NegTokenResp ::= SEQUENCE {
        //   negState      [0] ENUMERATED,
        //   supportedMech [1] OID           OPTIONAL,
        //   responseToken [2] OCTET STRING  OPTIONAL,
        //   mechListMIC   [3] OCTET STRING  OPTIONAL
        // }
        // The NegTokenResp is wrapped in CONTEXT [1] (NegotiationToken CHOICE).

        var negState = isAcceptIncomplete ? NegState.AcceptIncomplete : NegState.AcceptCompleted;

        // For accept-completed with no mechToken, return a minimal response
        if (mechToken.Length == 0 && !isAcceptIncomplete)
        {
            // Build just the negState field
            var minWriter = new AsnWriter(AsnEncodingRules.BER);
            minWriter.PushSequence();
            {
                minWriter.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true));
                minWriter.WriteEnumeratedValue(negState);
                minWriter.PopSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true));
            }
            minWriter.PopSequence();
            byte[] minRespBytes = minWriter.Encode();

            var minWrapper = new AsnWriter(AsnEncodingRules.BER);
            minWrapper.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 1, isConstructed: true));
            minWrapper.WriteEncodedValue(minRespBytes);
            minWrapper.PopSequence(new Asn1Tag(TagClass.ContextSpecific, 1, isConstructed: true));
            return minWrapper.Encode();
        }

        // Build the NegTokenResp SEQUENCE
        var respWriter = new AsnWriter(AsnEncodingRules.BER);
        respWriter.PushSequence();
        {
            // negState [0] ENUMERATED
            respWriter.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true));
            respWriter.WriteEnumeratedValue(negState);
            respWriter.PopSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true));

            // responseToken [2] OCTET STRING (if present)
            if (mechToken.Length > 0)
            {
                respWriter.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 2, isConstructed: true));
                respWriter.WriteOctetString(mechToken);
                respWriter.PopSequence(new Asn1Tag(TagClass.ContextSpecific, 2, isConstructed: true));
            }
        }
        respWriter.PopSequence();
        byte[] negTokenRespBytes = respWriter.Encode();

        // Wrap in CONTEXT [1] (NegotiationToken choice for negTokenResp)
        var wrapper = new AsnWriter(AsnEncodingRules.BER);
        wrapper.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 1, isConstructed: true));
        wrapper.WriteEncodedValue(negTokenRespBytes);
        wrapper.PopSequence(new Asn1Tag(TagClass.ContextSpecific, 1, isConstructed: true));

        return wrapper.Encode();
    }

    private static bool ValidateNtlmv2Response(byte[] ntHash, string username, string domain, byte[] serverChallenge, byte[] ntResponse)
    {
        if (ntResponse.Length < 24) return false;

        // NTLMv2: responseKey = HMAC-MD5(ntHash, UPPERCASE(username) + domain)
        var identity = System.Text.Encoding.Unicode.GetBytes(username.ToUpperInvariant() + domain);
        byte[] ntlmv2Hash;
        using (var hmac = new System.Security.Cryptography.HMACMD5(ntHash))
            ntlmv2Hash = hmac.ComputeHash(identity);

        // Expected: HMAC-MD5(ntlmv2Hash, serverChallenge + clientBlob)
        var clientBlob = ntResponse[16..];
        var challengePlusBlob = new byte[serverChallenge.Length + clientBlob.Length];
        serverChallenge.CopyTo(challengePlusBlob, 0);
        clientBlob.CopyTo(challengePlusBlob, serverChallenge.Length);

        byte[] expectedNtProof;
        using (var hmac = new System.Security.Cryptography.HMACMD5(ntlmv2Hash))
            expectedNtProof = hmac.ComputeHash(challengePlusBlob);

        return CryptographicOperations.FixedTimeEquals(expectedNtProof, ntResponse[..16]);
    }

    private static (string domain, string username, byte[] ntResponse, byte[] lmResponse) ParseNtlmAuthenticate(byte[] token)
    {
        // Parse NTLM AUTHENTICATE_MESSAGE (type 3)
        if (token.Length < 88) return ("", "", [], []);

        // LM Response: offset at 12 (len, maxlen, offset)
        var lmLen = BitConverter.ToUInt16(token, 12);
        var lmOffset = BitConverter.ToInt32(token, 16);
        var lmResponse = lmLen > 0 && lmOffset + lmLen <= token.Length ? token[lmOffset..(lmOffset + lmLen)] : [];

        // NT Response: offset at 20
        var ntLen = BitConverter.ToUInt16(token, 20);
        var ntOffset = BitConverter.ToInt32(token, 24);
        var ntResponse = ntLen > 0 && ntOffset + ntLen <= token.Length ? token[ntOffset..(ntOffset + ntLen)] : [];

        // Domain: offset at 28
        var domainLen = BitConverter.ToUInt16(token, 28);
        var domainOffset = BitConverter.ToInt32(token, 32);
        var domain = domainLen > 0 && domainOffset + domainLen <= token.Length
            ? System.Text.Encoding.Unicode.GetString(token, domainOffset, domainLen) : "";

        // User: offset at 36
        var userLen = BitConverter.ToUInt16(token, 36);
        var userOffset = BitConverter.ToInt32(token, 40);
        var username = userLen > 0 && userOffset + userLen <= token.Length
            ? System.Text.Encoding.Unicode.GetString(token, userOffset, userLen) : "";

        return (domain, username, ntResponse, lmResponse);
    }
}

public class SaslBindResult
{
    public bool IsSuccess { get; init; }
    public bool NeedsContinuation { get; init; }
    public string BoundDn { get; init; }
    public string ErrorMessage { get; init; }
    public byte[] ResponseToken { get; init; }

    public static SaslBindResult Success(string boundDn, byte[] responseToken = null)
        => new() { IsSuccess = true, BoundDn = boundDn, ResponseToken = responseToken };

    public static SaslBindResult Failure(string message)
        => new() { IsSuccess = false, ErrorMessage = message };

    public static SaslBindResult ContinueNeeded(byte[] responseToken)
        => new() { NeedsContinuation = true, ResponseToken = responseToken };
}
