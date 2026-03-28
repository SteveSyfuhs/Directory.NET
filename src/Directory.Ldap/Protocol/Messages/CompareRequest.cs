using System.Formats.Asn1;

namespace Directory.Ldap.Protocol.Messages;

/// <summary>
/// CompareRequest ::= [APPLICATION 14] SEQUENCE {
///     entry LDAPDN,
///     ava AttributeValueAssertion
/// }
/// </summary>
public class CompareRequest
{
    public string Entry { get; set; } = string.Empty;
    public string AttributeDesc { get; set; } = string.Empty;
    public string AssertionValue { get; set; } = string.Empty;

    public static CompareRequest Decode(ReadOnlyMemory<byte> data)
    {
        var reader = new AsnReader(data, BerHelper.Rules);
        var sequence = reader.ReadSequence(BerHelper.ApplicationTag(14));

        var entry = BerHelper.ReadLdapString(sequence);
        var ava = sequence.ReadSequence();
        var desc = BerHelper.ReadLdapString(ava);
        var value = BerHelper.ReadLdapString(ava);

        return new CompareRequest
        {
            Entry = entry,
            AttributeDesc = desc,
            AssertionValue = value,
        };
    }
}

/// <summary>
/// CompareResponse ::= [APPLICATION 15] LDAPResult
/// </summary>
public class CompareResponse
{
    public LdapResultCode ResultCode { get; set; }
    public string MatchedDN { get; set; } = string.Empty;
    public string DiagnosticMessage { get; set; } = string.Empty;

    public byte[] Encode()
    {
        var writer = new AsnWriter(BerHelper.Rules);

        using (writer.PushSequence(BerHelper.ApplicationTag(15)))
        {
            writer.WriteEnumeratedValue(ResultCode);
            BerHelper.WriteLdapString(writer, MatchedDN);
            BerHelper.WriteLdapString(writer, DiagnosticMessage);
        }

        return writer.Encode();
    }
}
