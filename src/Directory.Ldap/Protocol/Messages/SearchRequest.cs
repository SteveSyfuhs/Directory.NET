using System.Formats.Asn1;
using Directory.Core.Interfaces;
using Directory.Core.Models;

namespace Directory.Ldap.Protocol.Messages;

/// <summary>
/// SearchRequest ::= [APPLICATION 3] SEQUENCE {
///     baseObject LDAPDN,
///     scope ENUMERATED { baseObject(0), singleLevel(1), wholeSubtree(2) },
///     derefAliases ENUMERATED { neverDerefAliases(0), ... },
///     sizeLimit INTEGER (0..maxInt),
///     timeLimit INTEGER (0..maxInt),
///     typesOnly BOOLEAN,
///     filter Filter,
///     attributes AttributeSelection
/// }
/// </summary>
public class SearchRequest
{
    public string BaseObject { get; set; } = string.Empty;
    public SearchScope Scope { get; set; }
    public DerefAliases DerefAliases { get; set; }
    public int SizeLimit { get; set; }
    public int TimeLimit { get; set; }
    public bool TypesOnly { get; set; }
    public FilterNode Filter { get; set; }
    public List<string> Attributes { get; set; } = [];

    public static SearchRequest Decode(ReadOnlyMemory<byte> data)
    {
        var reader = new AsnReader(data, BerHelper.Rules);
        var sequence = reader.ReadSequence(BerHelper.ApplicationTag(3));

        var request = new SearchRequest
        {
            BaseObject = BerHelper.ReadLdapString(sequence),
            Scope = sequence.ReadEnumeratedValue<SearchScope>(),
            DerefAliases = sequence.ReadEnumeratedValue<DerefAliases>(),
            SizeLimit = (int)sequence.ReadInteger(),
            TimeLimit = (int)sequence.ReadInteger(),
            TypesOnly = sequence.ReadBoolean(),
        };

        // Parse filter
        request.Filter = LdapFilterParser.DecodeBer(sequence);

        // Parse attribute selection
        var attrSeq = sequence.ReadSequence();
        while (attrSeq.HasData)
        {
            request.Attributes.Add(BerHelper.ReadLdapString(attrSeq));
        }

        return request;
    }
}

/// <summary>
/// SearchResultEntry ::= [APPLICATION 4] SEQUENCE {
///     objectName LDAPDN,
///     attributes PartialAttributeList
/// }
/// </summary>
public class SearchResultEntry
{
    public string ObjectName { get; set; } = string.Empty;

    /// <summary>
    /// Attribute list. Each value in the list can be a string (written as UTF-8 OCTET STRING)
    /// or a byte[] (written as raw OCTET STRING for binary attributes like objectSid, objectGUID).
    /// </summary>
    public List<(string Name, List<object> Values)> Attributes { get; set; } = [];

    public byte[] Encode()
    {
        var writer = new AsnWriter(BerHelper.Rules);

        using (writer.PushSequence(BerHelper.ApplicationTag(4)))
        {
            BerHelper.WriteLdapString(writer, ObjectName);

            using (writer.PushSequence()) // PartialAttributeList
            {
                foreach (var (name, values) in Attributes)
                {
                    using (writer.PushSequence()) // PartialAttribute
                    {
                        BerHelper.WriteLdapString(writer, name);

                        using (writer.PushSetOf()) // SET OF AttributeValue
                        {
                            foreach (var value in values)
                            {
                                if (value is byte[] bytes)
                                {
                                    // Binary value — write raw OCTET STRING (for objectSid, objectGUID, etc.)
                                    writer.WriteOctetString(bytes);
                                }
                                else
                                {
                                    // String value — write as UTF-8 encoded OCTET STRING
                                    BerHelper.WriteLdapString(writer, value?.ToString() ?? string.Empty);
                                }
                            }
                        }
                    }
                }
            }
        }

        return writer.Encode();
    }
}

/// <summary>
/// SearchResultDone ::= [APPLICATION 5] LDAPResult
/// </summary>
public class SearchResultDone
{
    public LdapResultCode ResultCode { get; set; }
    public string MatchedDN { get; set; } = string.Empty;
    public string DiagnosticMessage { get; set; } = string.Empty;

    public byte[] Encode()
    {
        var writer = new AsnWriter(BerHelper.Rules);

        using (writer.PushSequence(BerHelper.ApplicationTag(5)))
        {
            writer.WriteEnumeratedValue(ResultCode);
            BerHelper.WriteLdapString(writer, MatchedDN);
            BerHelper.WriteLdapString(writer, DiagnosticMessage);
        }

        return writer.Encode();
    }
}

/// <summary>
/// SearchResultReference ::= [APPLICATION 19] SEQUENCE SIZE (1..MAX) OF uri URI
/// </summary>
public class SearchResultReference
{
    public List<string> Uris { get; set; } = [];

    public byte[] Encode()
    {
        var writer = new AsnWriter(BerHelper.Rules);

        using (writer.PushSequence(BerHelper.ApplicationTag(19)))
        {
            foreach (var uri in Uris)
            {
                BerHelper.WriteLdapString(writer, uri);
            }
        }

        return writer.Encode();
    }
}
