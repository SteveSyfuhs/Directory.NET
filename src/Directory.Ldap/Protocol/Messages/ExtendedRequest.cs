using System.Formats.Asn1;

namespace Directory.Ldap.Protocol.Messages;

/// <summary>
/// ExtendedRequest ::= [APPLICATION 23] SEQUENCE {
///     requestName [0] LDAPOID,
///     requestValue [1] OCTET STRING OPTIONAL
/// }
/// </summary>
public class ExtendedRequest
{
    public string RequestName { get; set; } = string.Empty;
    public byte[] RequestValue { get; set; }

    public static ExtendedRequest Decode(ReadOnlyMemory<byte> data)
    {
        var reader = new AsnReader(data, BerHelper.Rules);
        var sequence = reader.ReadSequence(BerHelper.ApplicationTag(23));

        var request = new ExtendedRequest
        {
            RequestName = BerHelper.ReadLdapString(sequence, BerHelper.ContextTag(0)),
        };

        if (sequence.HasData)
        {
            request.RequestValue = sequence.ReadOctetString(BerHelper.ContextTag(1));
        }

        return request;
    }
}

/// <summary>
/// ExtendedResponse ::= [APPLICATION 24] SEQUENCE {
///     COMPONENTS OF LDAPResult,
///     responseName [10] LDAPOID OPTIONAL,
///     responseValue [11] OCTET STRING OPTIONAL
/// }
/// </summary>
public class ExtendedResponse
{
    public LdapResultCode ResultCode { get; set; }
    public string MatchedDN { get; set; } = string.Empty;
    public string DiagnosticMessage { get; set; } = string.Empty;
    public string ResponseName { get; set; }
    public byte[] ResponseValue { get; set; }

    public byte[] Encode()
    {
        var writer = new AsnWriter(BerHelper.Rules);

        using (writer.PushSequence(BerHelper.ApplicationTag(24)))
        {
            writer.WriteEnumeratedValue(ResultCode);
            BerHelper.WriteLdapString(writer, MatchedDN);
            BerHelper.WriteLdapString(writer, DiagnosticMessage);

            if (ResponseName is not null)
            {
                BerHelper.WriteLdapString(writer, BerHelper.ContextTag(10), ResponseName);
            }

            if (ResponseValue is not null)
            {
                writer.WriteOctetString(ResponseValue, BerHelper.ContextTag(11));
            }
        }

        return writer.Encode();
    }
}
