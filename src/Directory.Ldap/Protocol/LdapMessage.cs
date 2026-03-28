using System.Formats.Asn1;

namespace Directory.Ldap.Protocol;

/// <summary>
/// Top-level LDAP message: SEQUENCE { messageID INTEGER, protocolOp CHOICE, controls [0] OPTIONAL }.
/// </summary>
public class LdapMessage
{
    public int MessageId { get; set; }
    public LdapOperation Operation { get; set; }
    public ReadOnlyMemory<byte> ProtocolOpData { get; set; }
    public List<LdapControl> Controls { get; set; }

    /// <summary>
    /// Decode an LDAP message from raw BER bytes.
    /// </summary>
    public static LdapMessage Decode(ReadOnlyMemory<byte> data)
    {
        var reader = new AsnReader(data, BerHelper.Rules);
        var sequence = reader.ReadSequence();

        var messageId = (int)sequence.ReadInteger();

        // Read the protocolOp — it's a CHOICE encoded with APPLICATION tags
        var peekTag = sequence.PeekTag();
        var operation = (LdapOperation)peekTag.TagValue;

        // Read the full protocolOp element as raw bytes
        var protocolOpData = sequence.PeekEncodedValue();
        // Advance past it
        sequence.ReadEncodedValue();

        var message = new LdapMessage
        {
            MessageId = messageId,
            Operation = operation,
            ProtocolOpData = protocolOpData,
        };

        // Read optional controls [0]
        if (sequence.HasData)
        {
            var controlsTag = BerHelper.ContextTag(0, isConstructed: true);
            if (sequence.PeekTag() == controlsTag)
            {
                message.Controls = [];
                var controlsReader = sequence.ReadSequence(controlsTag);
                while (controlsReader.HasData)
                {
                    message.Controls.Add(LdapControl.Decode(controlsReader));
                }
            }
        }

        return message;
    }

    /// <summary>
    /// Encode this LDAP message to BER bytes.
    /// </summary>
    public byte[] Encode()
    {
        var writer = new AsnWriter(BerHelper.Rules);

        using (writer.PushSequence())
        {
            writer.WriteInteger(MessageId);

            // Write protocolOp as raw pre-encoded data
            writer.WriteEncodedValue(ProtocolOpData.Span);

            // Write controls if present
            if (Controls is { Count: > 0 })
            {
                var controlsTag = BerHelper.ContextTag(0, isConstructed: true);
                using (writer.PushSequence(controlsTag))
                {
                    foreach (var control in Controls)
                    {
                        control.Encode(writer);
                    }
                }
            }
        }

        return writer.Encode();
    }
}

/// <summary>
/// LDAP Control: SEQUENCE { controlType LDAPOID, criticality BOOLEAN DEFAULT FALSE, controlValue OCTET STRING OPTIONAL }.
/// </summary>
public class LdapControl
{
    public string Oid { get; set; } = string.Empty;
    public bool Criticality { get; set; }
    public byte[] Value { get; set; }

    public static LdapControl Decode(AsnReader parentReader)
    {
        var sequence = parentReader.ReadSequence();
        var oid = sequence.ReadObjectIdentifier();
        var criticality = false;
        byte[] value = null;

        if (sequence.HasData)
        {
            var nextTag = sequence.PeekTag();
            if (nextTag == Asn1Tag.Boolean)
            {
                criticality = sequence.ReadBoolean();
            }
        }

        if (sequence.HasData)
        {
            value = sequence.ReadOctetString();
        }

        return new LdapControl
        {
            Oid = oid,
            Criticality = criticality,
            Value = value,
        };
    }

    public void Encode(AsnWriter writer)
    {
        using (writer.PushSequence())
        {
            writer.WriteObjectIdentifier(Oid);

            if (Criticality)
                writer.WriteBoolean(true);

            if (Value is not null)
                writer.WriteOctetString(Value);
        }
    }
}
