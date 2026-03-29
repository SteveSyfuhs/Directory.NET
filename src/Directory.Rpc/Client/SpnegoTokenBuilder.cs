using System.Formats.Asn1;

namespace Directory.Rpc.Client;

/// <summary>
/// Builds SPNEGO tokens for RPC authentication per RFC 4178.
/// NTLM wrapping has been removed; only Kerberos SPNEGO support remains.
/// </summary>
public static class SpnegoTokenBuilder
{
    private enum NegState
    {
        AcceptCompleted = 0,
        AcceptIncomplete = 1,
        Reject = 2,
        RequestMic = 3,
    }

    // SPNEGO OID: 1.3.6.1.5.5.2
    private const string SpnegoOid = "1.3.6.1.5.5.2";

    // Kerberos mechanism OID: 1.2.840.113554.1.2.2
    private const string KerberosMechOid = "1.2.840.113554.1.2.2";

    /// <summary>
    /// Wraps a Kerberos AP-REQ token in a SPNEGO negTokenInit for the BIND auth verifier.
    /// Produces a GSS-API InitialContextToken [APPLICATION 0] containing:
    ///   thisMech = SPNEGO OID (1.3.6.1.5.5.2)
    ///   innerToken = NegTokenInit { mechTypes = [Kerberos OID], mechToken = kerberosToken }
    /// </summary>
    public static byte[] WrapKerberosNegotiate(byte[] kerberosToken)
    {
        // Build the NegTokenInit SEQUENCE
        var negTokenInitWriter = new AsnWriter(AsnEncodingRules.DER);
        negTokenInitWriter.PushSequence();
        {
            // mechTypes [0] SEQUENCE OF OID
            negTokenInitWriter.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0));
            negTokenInitWriter.PushSequence();
            negTokenInitWriter.WriteObjectIdentifier(KerberosMechOid);
            negTokenInitWriter.PopSequence();
            negTokenInitWriter.PopSequence(new Asn1Tag(TagClass.ContextSpecific, 0));

            // mechToken [2] OCTET STRING
            negTokenInitWriter.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 2));
            negTokenInitWriter.WriteOctetString(kerberosToken);
            negTokenInitWriter.PopSequence(new Asn1Tag(TagClass.ContextSpecific, 2));
        }
        negTokenInitWriter.PopSequence();
        byte[] negTokenInitBytes = negTokenInitWriter.Encode();

        // Wrap in [CONTEXT 0] (NegotiationToken choice for negTokenInit)
        var negTokenWrapper = new AsnWriter(AsnEncodingRules.DER);
        negTokenWrapper.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true));
        negTokenWrapper.WriteEncodedValue(negTokenInitBytes);
        negTokenWrapper.PopSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true));
        byte[] wrappedNegToken = negTokenWrapper.Encode();

        // Wrap in GSS-API InitialContextToken [APPLICATION 0] IMPLICIT SEQUENCE
        var gssWriter = new AsnWriter(AsnEncodingRules.DER);
        gssWriter.PushSequence(new Asn1Tag(TagClass.Application, 0, isConstructed: true));
        gssWriter.WriteObjectIdentifier(SpnegoOid);
        gssWriter.WriteEncodedValue(wrappedNegToken);
        gssWriter.PopSequence(new Asn1Tag(TagClass.Application, 0, isConstructed: true));

        return gssWriter.Encode();
    }

    /// <summary>
    /// Wraps a Kerberos AP-REP or continuation token in a SPNEGO negTokenResp.
    /// Produces a NegTokenResp { negState = accept-incomplete(1), responseToken = kerberosToken }
    /// </summary>
    public static byte[] WrapKerberosAuthenticate(byte[] kerberosToken)
    {
        var respWriter = new AsnWriter(AsnEncodingRules.DER);
        respWriter.PushSequence();
        {
            // negState [0] ENUMERATED
            respWriter.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0));
            respWriter.WriteEnumeratedValue(NegState.AcceptIncomplete);
            respWriter.PopSequence(new Asn1Tag(TagClass.ContextSpecific, 0));

            // responseToken [2] OCTET STRING
            respWriter.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 2));
            respWriter.WriteOctetString(kerberosToken);
            respWriter.PopSequence(new Asn1Tag(TagClass.ContextSpecific, 2));
        }
        respWriter.PopSequence();
        byte[] negTokenRespBytes = respWriter.Encode();

        // Wrap in [CONTEXT 1] (NegotiationToken choice for negTokenResp)
        var wrapper = new AsnWriter(AsnEncodingRules.DER);
        wrapper.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 1, isConstructed: true));
        wrapper.WriteEncodedValue(negTokenRespBytes);
        wrapper.PopSequence(new Asn1Tag(TagClass.ContextSpecific, 1, isConstructed: true));

        return wrapper.Encode();
    }

    /// <summary>
    /// Unwraps a SPNEGO negTokenResp to extract the inner mechanism token (e.g., Kerberos AP-REP).
    /// Parses NegTokenResp { negState, supportedMech, responseToken }
    /// </summary>
    public static byte[] UnwrapResponseToken(byte[] spnegoToken)
    {
        var reader = new AsnReader(spnegoToken, AsnEncodingRules.DER);

        // The token may be wrapped in [CONTEXT 1] for negTokenResp
        var outerTag = reader.PeekTag();

        AsnReader seqReader;
        if (outerTag.TagClass == TagClass.ContextSpecific && outerTag.TagValue == 1)
        {
            // [CONTEXT 1] wrapper
            var ctxReader = reader.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 1, isConstructed: true));
            seqReader = ctxReader.ReadSequence();
        }
        else if (outerTag.TagClass == TagClass.Universal && outerTag == Asn1Tag.Sequence)
        {
            // Direct SEQUENCE (some implementations omit the context wrapper)
            seqReader = reader.ReadSequence();
        }
        else
        {
            throw new InvalidOperationException(
                $"Unexpected SPNEGO token tag: class={outerTag.TagClass}, value={outerTag.TagValue}");
        }

        // Parse NegTokenResp fields
        byte[] responseToken = null;

        while (seqReader.HasData)
        {
            var fieldTag = seqReader.PeekTag();

            if (fieldTag.TagClass == TagClass.ContextSpecific)
            {
                switch (fieldTag.TagValue)
                {
                    case 0: // negState [0] ENUMERATED
                    {
                        var stateReader = seqReader.ReadSequence(
                            new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true));
                        stateReader.ReadEnumeratedBytes(); // consume but we don't need the value
                        break;
                    }
                    case 1: // supportedMech [1] OID
                    {
                        var mechReader = seqReader.ReadSequence(
                            new Asn1Tag(TagClass.ContextSpecific, 1, isConstructed: true));
                        mechReader.ReadObjectIdentifier(); // consume
                        break;
                    }
                    case 2: // responseToken [2] OCTET STRING — the mechanism token
                    {
                        var tokenReader = seqReader.ReadSequence(
                            new Asn1Tag(TagClass.ContextSpecific, 2, isConstructed: true));
                        responseToken = tokenReader.ReadOctetString();
                        break;
                    }
                    case 3: // mechListMIC [3] OCTET STRING
                    {
                        var micReader = seqReader.ReadSequence(
                            new Asn1Tag(TagClass.ContextSpecific, 3, isConstructed: true));
                        micReader.ReadOctetString(); // consume
                        break;
                    }
                    default:
                        // Unknown field, skip
                        seqReader.ReadEncodedValue();
                        break;
                }
            }
            else
            {
                // Skip unknown elements
                seqReader.ReadEncodedValue();
            }
        }

        if (responseToken is null)
        {
            throw new InvalidOperationException(
                "SPNEGO negTokenResp did not contain a responseToken.");
        }

        return responseToken;
    }
}
