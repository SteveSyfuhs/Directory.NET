using System.Formats.Asn1;

namespace Directory.Ldap.Protocol.Messages;

/// <summary>
/// ModifyRequest ::= [APPLICATION 6] SEQUENCE {
///     object LDAPDN,
///     changes SEQUENCE OF change SEQUENCE {
///         operation ENUMERATED { add(0), delete(1), replace(2) },
///         modification PartialAttribute
///     }
/// }
/// </summary>
public class ModifyRequest
{
    public string Object { get; set; } = string.Empty;
    public List<ModifyChange> Changes { get; set; } = [];

    public static ModifyRequest Decode(ReadOnlyMemory<byte> data)
    {
        var reader = new AsnReader(data, BerHelper.Rules);
        var sequence = reader.ReadSequence(BerHelper.ApplicationTag(6));

        var request = new ModifyRequest
        {
            Object = BerHelper.ReadLdapString(sequence),
        };

        var changesSeq = sequence.ReadSequence();
        while (changesSeq.HasData)
        {
            var changeSeq = changesSeq.ReadSequence();
            var operation = changeSeq.ReadEnumeratedValue<ModifyOperation>();

            var partialAttr = changeSeq.ReadSequence();
            var attrName = BerHelper.ReadLdapString(partialAttr);
            var values = BerHelper.ReadStringSet(partialAttr);

            request.Changes.Add(new ModifyChange
            {
                Operation = operation,
                AttributeName = attrName,
                Values = values,
            });
        }

        return request;
    }
}

public class ModifyChange
{
    public ModifyOperation Operation { get; set; }
    public string AttributeName { get; set; } = string.Empty;
    public List<string> Values { get; set; } = [];
}

public enum ModifyOperation
{
    Add = 0,
    Delete = 1,
    Replace = 2,
}

/// <summary>
/// ModifyResponse ::= [APPLICATION 7] LDAPResult
/// </summary>
public class ModifyResponse
{
    public LdapResultCode ResultCode { get; set; }
    public string MatchedDN { get; set; } = string.Empty;
    public string DiagnosticMessage { get; set; } = string.Empty;

    public byte[] Encode()
    {
        var writer = new AsnWriter(BerHelper.Rules);

        using (writer.PushSequence(BerHelper.ApplicationTag(7)))
        {
            writer.WriteEnumeratedValue(ResultCode);
            BerHelper.WriteLdapString(writer, MatchedDN);
            BerHelper.WriteLdapString(writer, DiagnosticMessage);
        }

        return writer.Encode();
    }
}
