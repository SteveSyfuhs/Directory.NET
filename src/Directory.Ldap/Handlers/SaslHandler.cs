using System.Formats.Asn1;
using Directory.Core.Interfaces;
using Directory.Ldap.Protocol;
using Directory.Ldap.Server;
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
            var mechToken = ExtractMechToken(token);

            if (mechToken == null || mechToken.Length == 0)
            {
                return SaslBindResult.Failure("Empty SPNEGO mechanism token");
            }

            // Check if this is an NTLM token (no longer supported)
            if (IsNtlmToken(mechToken))
            {
                _logger.LogWarning("NTLM authentication is not supported; use Kerberos GSSAPI instead");
                return SaslBindResult.Failure("NTLM authentication is not supported. Use Kerberos.");
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
