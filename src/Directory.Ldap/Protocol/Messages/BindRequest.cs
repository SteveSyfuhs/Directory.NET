using System.Formats.Asn1;

namespace Directory.Ldap.Protocol.Messages;

/// <summary>
/// BindRequest ::= [APPLICATION 0] SEQUENCE {
///     version INTEGER (1..127),
///     name LDAPDN,
///     authentication AuthenticationChoice
/// }
/// AuthenticationChoice ::= CHOICE {
///     simple [0] OCTET STRING,
///     sasl [3] SaslCredentials
/// }
/// </summary>
public class BindRequest
{
    public int Version { get; set; } = 3;
    public string Name { get; set; } = string.Empty;
    public bool IsSimple { get; set; }
    public byte[] SimpleCredentials { get; set; } = [];
    public string SaslMechanism { get; set; }
    public byte[] SaslCredentials { get; set; }

    public static BindRequest Decode(ReadOnlyMemory<byte> data)
    {
        var reader = new AsnReader(data, BerHelper.Rules);
        var sequence = reader.ReadSequence(BerHelper.ApplicationTag(0));

        var version = (int)sequence.ReadInteger();
        var name = BerHelper.ReadLdapString(sequence);

        var request = new BindRequest
        {
            Version = version,
            Name = name,
        };

        var authTag = sequence.PeekTag();
        if (authTag == BerHelper.ContextTag(0)) // simple
        {
            request.IsSimple = true;
            request.SimpleCredentials = sequence.ReadOctetString(BerHelper.ContextTag(0));
        }
        else if (authTag == BerHelper.ContextTag(3, isConstructed: true)) // sasl
        {
            var sasl = sequence.ReadSequence(BerHelper.ContextTag(3, isConstructed: true));
            request.SaslMechanism = BerHelper.ReadLdapString(sasl);
            if (sasl.HasData)
                request.SaslCredentials = sasl.ReadOctetString();
            request.IsSimple = false;
        }

        return request;
    }
}

/// <summary>
/// BindResponse ::= [APPLICATION 1] SEQUENCE {
///     COMPONENTS OF LDAPResult,
///     serverSaslCreds [7] OCTET STRING OPTIONAL
/// }
/// </summary>
public class BindResponse
{
    public LdapResultCode ResultCode { get; set; }
    public string MatchedDN { get; set; } = string.Empty;
    public string DiagnosticMessage { get; set; } = string.Empty;
    public byte[] ServerSaslCreds { get; set; }

    public byte[] Encode()
    {
        var writer = new AsnWriter(BerHelper.Rules);

        using (writer.PushSequence(BerHelper.ApplicationTag(1)))
        {
            writer.WriteEnumeratedValue(ResultCode);
            BerHelper.WriteLdapString(writer, MatchedDN);
            BerHelper.WriteLdapString(writer, DiagnosticMessage);

            if (ServerSaslCreds is not null)
            {
                writer.WriteOctetString(ServerSaslCreds, BerHelper.ContextTag(7));
            }
        }

        return writer.Encode();
    }
}
