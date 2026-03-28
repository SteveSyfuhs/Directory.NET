using System.Formats.Asn1;

namespace Directory.Ldap.Protocol.Messages;

/// <summary>
/// AbandonRequest ::= [APPLICATION 16] MessageID (INTEGER, primitive)
/// </summary>
public class AbandonRequest
{
    public int MessageIdToAbandon { get; set; }

    public static AbandonRequest Decode(ReadOnlyMemory<byte> data)
    {
        var reader = new AsnReader(data, BerHelper.Rules);
        // AbandonRequest is [APPLICATION 16] INTEGER (primitive)
        var messageId = (int)reader.ReadInteger(BerHelper.ApplicationTag(16, isConstructed: false));
        return new AbandonRequest { MessageIdToAbandon = messageId };
    }
}
