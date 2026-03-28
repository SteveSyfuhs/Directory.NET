using System.Formats.Asn1;

namespace Directory.Ldap.Protocol.Messages;

/// <summary>
/// DelRequest ::= [APPLICATION 10] LDAPDN (primitive OCTET STRING)
/// </summary>
public class DeleteRequest
{
    public string Entry { get; set; } = string.Empty;

    public static DeleteRequest Decode(ReadOnlyMemory<byte> data)
    {
        var reader = new AsnReader(data, BerHelper.Rules);
        // DelRequest is [APPLICATION 10] OCTET STRING (primitive)
        var dn = reader.ReadOctetString(BerHelper.ApplicationTag(10, isConstructed: false));
        return new DeleteRequest { Entry = dn.ToUtf8String() };
    }
}

/// <summary>
/// DelResponse ::= [APPLICATION 11] LDAPResult
/// </summary>
public class DeleteResponse
{
    public LdapResultCode ResultCode { get; set; }
    public string MatchedDN { get; set; } = string.Empty;
    public string DiagnosticMessage { get; set; } = string.Empty;

    public byte[] Encode()
    {
        var writer = new AsnWriter(BerHelper.Rules);

        using (writer.PushSequence(BerHelper.ApplicationTag(11)))
        {
            writer.WriteEnumeratedValue(ResultCode);
            BerHelper.WriteLdapString(writer, MatchedDN);
            BerHelper.WriteLdapString(writer, DiagnosticMessage);
        }

        return writer.Encode();
    }
}
