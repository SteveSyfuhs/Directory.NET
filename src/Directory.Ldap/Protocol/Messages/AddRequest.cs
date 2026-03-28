using System.Formats.Asn1;

namespace Directory.Ldap.Protocol.Messages;

/// <summary>
/// AddRequest ::= [APPLICATION 8] SEQUENCE {
///     entry LDAPDN,
///     attributes AttributeList
/// }
/// AttributeList ::= SEQUENCE OF attribute SEQUENCE {
///     type AttributeDescription,
///     vals SET OF value AttributeValue
/// }
/// </summary>
public class AddRequest
{
    public string Entry { get; set; } = string.Empty;
    public List<(string Name, List<string> Values)> Attributes { get; set; } = [];

    public static AddRequest Decode(ReadOnlyMemory<byte> data)
    {
        var reader = new AsnReader(data, BerHelper.Rules);
        var sequence = reader.ReadSequence(BerHelper.ApplicationTag(8));

        var request = new AddRequest
        {
            Entry = BerHelper.ReadLdapString(sequence),
        };

        var attrList = sequence.ReadSequence();
        while (attrList.HasData)
        {
            var attrSeq = attrList.ReadSequence();
            var name = BerHelper.ReadLdapString(attrSeq);
            var values = BerHelper.ReadStringSet(attrSeq);
            request.Attributes.Add((name, values));
        }

        return request;
    }
}

/// <summary>
/// AddResponse ::= [APPLICATION 9] LDAPResult
/// </summary>
public class AddResponse
{
    public LdapResultCode ResultCode { get; set; }
    public string MatchedDN { get; set; } = string.Empty;
    public string DiagnosticMessage { get; set; } = string.Empty;

    public byte[] Encode()
    {
        var writer = new AsnWriter(BerHelper.Rules);

        using (writer.PushSequence(BerHelper.ApplicationTag(9)))
        {
            writer.WriteEnumeratedValue(ResultCode);
            BerHelper.WriteLdapString(writer, MatchedDN);
            BerHelper.WriteLdapString(writer, DiagnosticMessage);
        }

        return writer.Encode();
    }
}
