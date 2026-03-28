using System.Formats.Asn1;
using System.Text;
using Directory.Ldap.Protocol;
using Directory.Ldap.Protocol.Messages;
using Xunit;

namespace Directory.Tests;

/// <summary>
/// BER TLV round-trip tests for LDAP protocol messages.
/// Verifies that encoding and decoding are inverse operations across all request types
/// and that BER framing details (long-form lengths, APPLICATION/CONTEXT tags) work correctly.
/// </summary>
public class BerCodecTests
{
    // ─── TLV framing helpers ────────────────────────────────────────────────

    [Fact]
    public void TryReadTlvLength_ShortFormLength_ReturnsCorrectTotal()
    {
        // SEQUENCE (tag 0x30), length 5, followed by 5 bytes of content
        var buffer = new byte[] { 0x30, 0x05, 0x01, 0x02, 0x03, 0x04, 0x05 };
        var total = BerHelper.TryReadTlvLength(buffer);
        // 2 bytes overhead (tag + length) + 5 bytes content = 7
        Assert.Equal(7, total);
    }

    [Fact]
    public void TryReadTlvLength_LongFormLength_ReturnsCorrectTotal()
    {
        // SEQUENCE, long-form 2-byte length = 256
        var payload = new byte[256];
        var buffer = new byte[260];
        buffer[0] = 0x30;        // SEQUENCE tag
        buffer[1] = 0x82;        // long-form, 2 subsequent bytes
        buffer[2] = 0x01;        // high byte of 256
        buffer[3] = 0x00;        // low byte of 256
        Array.Copy(payload, 0, buffer, 4, 256);

        var total = BerHelper.TryReadTlvLength(buffer);
        // 4 bytes overhead (tag + 0x82 + 2 length bytes) + 256 content = 260
        Assert.Equal(260, total);
    }

    [Fact]
    public void TryReadTlvLength_InsufficientData_ReturnsMinusOne()
    {
        // Only 1 byte — not enough to determine length
        var buffer = new byte[] { 0x30 };
        var result = BerHelper.TryReadTlvLength(buffer);
        Assert.Equal(-1, result);
    }

    [Fact]
    public void TryReadTlvLength_LongFormLengthBytesNotFullyPresent_ReturnsMinusOne()
    {
        // Tag + 0x82 (says 2 length bytes follow) but only 1 length byte present
        var buffer = new byte[] { 0x30, 0x82, 0x01 };
        var result = BerHelper.TryReadTlvLength(buffer);
        Assert.Equal(-1, result);
    }

    [Fact]
    public void TryReadTlvLength_PayloadOver127Bytes_UsesLongForm()
    {
        // Build a 200-byte OCTET STRING the .NET AsnWriter produces
        var writer = new AsnWriter(AsnEncodingRules.BER);
        writer.WriteOctetString(new byte[200]);
        var encoded = writer.Encode();

        // Length field at byte 1 should be 0x81 (long form, 1 subsequent byte)
        Assert.Equal(0x81, encoded[1]);
        Assert.Equal(200, encoded[2]);

        var total = BerHelper.TryReadTlvLength(encoded);
        Assert.Equal(encoded.Length, total);
    }

    // ─── ApplicationTag / ContextTag helpers ───────────────────────────────

    [Fact]
    public void ApplicationTag_Constructed_HasCorrectClass()
    {
        var tag = BerHelper.ApplicationTag(3); // SearchRequest
        Assert.Equal(TagClass.Application, tag.TagClass);
        Assert.Equal(3, tag.TagValue);
        Assert.True(tag.IsConstructed);
    }

    [Fact]
    public void ContextTag_Primitive_HasCorrectClass()
    {
        var tag = BerHelper.ContextTag(0);
        Assert.Equal(TagClass.ContextSpecific, tag.TagClass);
        Assert.Equal(0, tag.TagValue);
        Assert.False(tag.IsConstructed);
    }

    [Fact]
    public void ContextTag_Constructed_HasCorrectClass()
    {
        var tag = BerHelper.ContextTag(3, isConstructed: true);
        Assert.Equal(TagClass.ContextSpecific, tag.TagClass);
        Assert.Equal(3, tag.TagValue);
        Assert.True(tag.IsConstructed);
    }

    // ─── LdapString read/write round-trips ────────────────────────────────

    [Fact]
    public void WriteLdapString_ThenReadLdapString_RoundTrips()
    {
        var writer = new AsnWriter(AsnEncodingRules.BER);
        BerHelper.WriteLdapString(writer, "cn=John,dc=example,dc=com");
        var encoded = writer.Encode();

        var reader = new AsnReader(encoded, AsnEncodingRules.BER);
        var decoded = BerHelper.ReadLdapString(reader);
        Assert.Equal("cn=John,dc=example,dc=com", decoded);
    }

    [Fact]
    public void WriteLdapString_WithContextTag_RoundTrips()
    {
        var tag = BerHelper.ContextTag(0);
        var writer = new AsnWriter(AsnEncodingRules.BER);
        BerHelper.WriteLdapString(writer, tag, "secret");
        var encoded = writer.Encode();

        var reader = new AsnReader(encoded, AsnEncodingRules.BER);
        var decoded = BerHelper.ReadLdapString(reader, tag);
        Assert.Equal("secret", decoded);
    }

    [Fact]
    public void WriteLdapString_LongPayloadOver127_EncodesWithLongFormLength()
    {
        // String of 200 characters triggers long-form length encoding
        var longValue = new string('A', 200);
        var writer = new AsnWriter(AsnEncodingRules.BER);
        BerHelper.WriteLdapString(writer, longValue);
        var encoded = writer.Encode();

        // Verify long-form marker is present
        Assert.Equal(0x81, encoded[1]); // 0x81 = long-form, 1 subsequent length byte
        Assert.Equal(200, encoded[2]);

        var reader = new AsnReader(encoded, AsnEncodingRules.BER);
        Assert.Equal(longValue, BerHelper.ReadLdapString(reader));
    }

    // ─── BindRequest round-trip ────────────────────────────────────────────

    [Fact]
    public void BindRequest_SimpleAuth_RoundTrips()
    {
        // Manually encode a simple BindRequest
        var writer = new AsnWriter(AsnEncodingRules.BER);
        using (writer.PushSequence(BerHelper.ApplicationTag(0)))
        {
            writer.WriteInteger(3);
            BerHelper.WriteLdapString(writer, "cn=admin,dc=example,dc=com");
            // [0] simple credentials
            writer.WriteOctetString(Encoding.UTF8.GetBytes("P@ssw0rd"), BerHelper.ContextTag(0));
        }
        var encoded = writer.Encode();

        var decoded = BindRequest.Decode(encoded);

        Assert.Equal(3, decoded.Version);
        Assert.Equal("cn=admin,dc=example,dc=com", decoded.Name);
        Assert.True(decoded.IsSimple);
        Assert.Equal("P@ssw0rd", Encoding.UTF8.GetString(decoded.SimpleCredentials));
    }

    [Fact]
    public void BindRequest_SaslAuth_RoundTrips()
    {
        var saslToken = new byte[] { 0x01, 0x02, 0x03 };
        var writer = new AsnWriter(AsnEncodingRules.BER);
        using (writer.PushSequence(BerHelper.ApplicationTag(0)))
        {
            writer.WriteInteger(3);
            BerHelper.WriteLdapString(writer, string.Empty);
            // [3] SASL constructed
            using (writer.PushSequence(BerHelper.ContextTag(3, isConstructed: true)))
            {
                BerHelper.WriteLdapString(writer, "GSSAPI");
                writer.WriteOctetString(saslToken);
            }
        }
        var encoded = writer.Encode();

        var decoded = BindRequest.Decode(encoded);

        Assert.False(decoded.IsSimple);
        Assert.Equal("GSSAPI", decoded.SaslMechanism);
        Assert.Equal(saslToken, decoded.SaslCredentials);
    }

    // ─── SearchRequest round-trip ──────────────────────────────────────────

    [Fact]
    public void SearchRequest_BasicSearch_RoundTrips()
    {
        // Encode a SearchRequest with an equality filter
        var writer = new AsnWriter(AsnEncodingRules.BER);
        using (writer.PushSequence(BerHelper.ApplicationTag(3)))
        {
            BerHelper.WriteLdapString(writer, "dc=example,dc=com");
            writer.WriteEnumeratedValue(Directory.Core.Models.SearchScope.WholeSubtree);
            writer.WriteEnumeratedValue(Directory.Core.Models.DerefAliases.NeverDerefAliases);
            writer.WriteInteger(0); // sizeLimit
            writer.WriteInteger(30); // timeLimit
            writer.WriteBoolean(false); // typesOnly

            // Equality filter: (cn=John) — [3] CONSTRUCTED
            using (writer.PushSequence(BerHelper.ContextTag(3, isConstructed: true)))
            {
                BerHelper.WriteLdapString(writer, "cn");
                BerHelper.WriteLdapString(writer, "John");
            }

            // AttributeSelection SEQUENCE OF
            using (writer.PushSequence())
            {
                BerHelper.WriteLdapString(writer, "cn");
                BerHelper.WriteLdapString(writer, "mail");
            }
        }
        var encoded = writer.Encode();

        var decoded = SearchRequest.Decode(encoded);

        Assert.Equal("dc=example,dc=com", decoded.BaseObject);
        Assert.Equal(Directory.Core.Models.SearchScope.WholeSubtree, decoded.Scope);
        Assert.Equal(0, decoded.SizeLimit);
        Assert.Equal(30, decoded.TimeLimit);
        Assert.False(decoded.TypesOnly);
        Assert.NotNull(decoded.Filter);
        Assert.Equal(2, decoded.Attributes.Count);
    }

    // ─── AddRequest round-trip ─────────────────────────────────────────────

    [Fact]
    public void AddRequest_WithAttributes_RoundTrips()
    {
        var writer = new AsnWriter(AsnEncodingRules.BER);
        using (writer.PushSequence(BerHelper.ApplicationTag(8)))
        {
            BerHelper.WriteLdapString(writer, "cn=Alice,dc=example,dc=com");

            using (writer.PushSequence()) // AttributeList
            {
                using (writer.PushSequence()) // Attribute
                {
                    BerHelper.WriteLdapString(writer, "cn");
                    using (writer.PushSetOf())
                    {
                        BerHelper.WriteLdapString(writer, "Alice");
                    }
                }
                using (writer.PushSequence())
                {
                    BerHelper.WriteLdapString(writer, "objectClass");
                    using (writer.PushSetOf())
                    {
                        BerHelper.WriteLdapString(writer, "user");
                        BerHelper.WriteLdapString(writer, "top");
                    }
                }
            }
        }
        var encoded = writer.Encode();

        var decoded = AddRequest.Decode(encoded);

        Assert.Equal("cn=Alice,dc=example,dc=com", decoded.Entry);
        Assert.Equal(2, decoded.Attributes.Count);

        var (name, values) = decoded.Attributes[0];
        Assert.Equal("cn", name);
        Assert.Single(values);
        Assert.Equal("Alice", values[0]);

        var (name2, values2) = decoded.Attributes[1];
        Assert.Equal("objectClass", name2);
        Assert.Equal(2, values2.Count);
    }

    // ─── ModifyRequest round-trip ──────────────────────────────────────────

    [Fact]
    public void ModifyRequest_ReplaceOperation_RoundTrips()
    {
        var writer = new AsnWriter(AsnEncodingRules.BER);
        using (writer.PushSequence(BerHelper.ApplicationTag(6)))
        {
            BerHelper.WriteLdapString(writer, "cn=Bob,dc=example,dc=com");

            using (writer.PushSequence()) // changes
            {
                using (writer.PushSequence()) // change
                {
                    writer.WriteEnumeratedValue(ModifyOperation.Replace);
                    using (writer.PushSequence()) // PartialAttribute
                    {
                        BerHelper.WriteLdapString(writer, "mail");
                        using (writer.PushSetOf())
                        {
                            BerHelper.WriteLdapString(writer, "bob@example.com");
                        }
                    }
                }
            }
        }
        var encoded = writer.Encode();

        var decoded = ModifyRequest.Decode(encoded);

        Assert.Equal("cn=Bob,dc=example,dc=com", decoded.Object);
        Assert.Single(decoded.Changes);
        Assert.Equal(ModifyOperation.Replace, decoded.Changes[0].Operation);
        Assert.Equal("mail", decoded.Changes[0].AttributeName);
        Assert.Equal("bob@example.com", decoded.Changes[0].Values[0]);
    }

    // ─── DeleteRequest round-trip ──────────────────────────────────────────

    [Fact]
    public void DeleteRequest_RoundTrips()
    {
        var dn = "cn=Charlie,dc=example,dc=com";
        var writer = new AsnWriter(AsnEncodingRules.BER);
        // DelRequest is APPLICATION 10 PRIMITIVE OCTET STRING
        writer.WriteOctetString(Encoding.UTF8.GetBytes(dn), BerHelper.ApplicationTag(10, isConstructed: false));
        var encoded = writer.Encode();

        var decoded = DeleteRequest.Decode(encoded);

        Assert.Equal(dn, decoded.Entry);
    }

    // ─── CompareRequest round-trip ─────────────────────────────────────────

    [Fact]
    public void CompareRequest_RoundTrips()
    {
        var writer = new AsnWriter(AsnEncodingRules.BER);
        using (writer.PushSequence(BerHelper.ApplicationTag(14)))
        {
            BerHelper.WriteLdapString(writer, "cn=Dave,dc=example,dc=com");
            using (writer.PushSequence()) // AVA
            {
                BerHelper.WriteLdapString(writer, "memberOf");
                BerHelper.WriteLdapString(writer, "cn=Admins,dc=example,dc=com");
            }
        }
        var encoded = writer.Encode();

        var decoded = CompareRequest.Decode(encoded);

        Assert.Equal("cn=Dave,dc=example,dc=com", decoded.Entry);
        Assert.Equal("memberOf", decoded.AttributeDesc);
        Assert.Equal("cn=Admins,dc=example,dc=com", decoded.AssertionValue);
    }

    // ─── LdapMessage envelope ─────────────────────────────────────────────

    [Fact]
    public void LdapMessage_EncodeDecodeWithMessageId_RoundTrips()
    {
        // Build a minimal BindResponse payload to use as protocolOp
        var bindResp = new BindResponse
        {
            ResultCode = Directory.Ldap.Protocol.LdapResultCode.Success,
            MatchedDN = string.Empty,
            DiagnosticMessage = string.Empty,
        };
        var protocolOpBytes = bindResp.Encode();

        var message = new LdapMessage
        {
            MessageId = 42,
            Operation = LdapOperation.BindResponse,
            ProtocolOpData = protocolOpBytes,
        };

        var encoded = message.Encode();
        var decoded = LdapMessage.Decode(encoded);

        Assert.Equal(42, decoded.MessageId);
        Assert.Equal(LdapOperation.BindResponse, decoded.Operation);
    }

    [Fact]
    public void LdapMessage_WithControls_PreservesControlOid()
    {
        var bindResp = new BindResponse
        {
            ResultCode = Directory.Ldap.Protocol.LdapResultCode.Success,
            MatchedDN = string.Empty,
            DiagnosticMessage = string.Empty,
        };

        var message = new LdapMessage
        {
            MessageId = 1,
            Operation = LdapOperation.BindResponse,
            ProtocolOpData = bindResp.Encode(),
            Controls =
            [
                new LdapControl
                {
                    Oid = "1.2.840.113556.1.4.319",
                    Criticality = false,
                    Value = new byte[] { 0x01, 0x02 },
                }
            ],
        };

        var encoded = message.Encode();
        var decoded = LdapMessage.Decode(encoded);

        Assert.NotNull(decoded.Controls);
        Assert.Single(decoded.Controls);
        Assert.Equal("1.2.840.113556.1.4.319", decoded.Controls[0].Oid);
    }
}
