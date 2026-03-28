using System.Formats.Asn1;

namespace Directory.Ldap.Protocol.Messages;

/// <summary>
/// ModifyDNRequest ::= [APPLICATION 12] SEQUENCE {
///     entry LDAPDN,
///     newrdn RelativeLDAPDN,
///     deleteoldrdn BOOLEAN,
///     newSuperior [0] LDAPDN OPTIONAL
/// }
/// </summary>
public class ModifyDNRequest
{
    public string Entry { get; set; } = string.Empty;
    public string NewRdn { get; set; } = string.Empty;
    public bool DeleteOldRdn { get; set; }
    public string NewSuperior { get; set; }

    public static ModifyDNRequest Decode(ReadOnlyMemory<byte> data)
    {
        var reader = new AsnReader(data, BerHelper.Rules);
        var sequence = reader.ReadSequence(BerHelper.ApplicationTag(12));

        var request = new ModifyDNRequest
        {
            Entry = BerHelper.ReadLdapString(sequence),
            NewRdn = BerHelper.ReadLdapString(sequence),
            DeleteOldRdn = sequence.ReadBoolean(),
        };

        if (sequence.HasData)
        {
            request.NewSuperior = BerHelper.ReadLdapString(sequence, BerHelper.ContextTag(0));
        }

        return request;
    }
}

/// <summary>
/// ModifyDNResponse ::= [APPLICATION 13] LDAPResult
/// </summary>
public class ModifyDNResponse
{
    public LdapResultCode ResultCode { get; set; }
    public string MatchedDN { get; set; } = string.Empty;
    public string DiagnosticMessage { get; set; } = string.Empty;

    public byte[] Encode()
    {
        var writer = new AsnWriter(BerHelper.Rules);

        using (writer.PushSequence(BerHelper.ApplicationTag(13)))
        {
            writer.WriteEnumeratedValue(ResultCode);
            BerHelper.WriteLdapString(writer, MatchedDN);
            BerHelper.WriteLdapString(writer, DiagnosticMessage);
        }

        return writer.Encode();
    }
}
