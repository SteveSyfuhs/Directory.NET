using System.Formats.Asn1;
using System.Text;

namespace Directory.Ldap.Protocol;

/// <summary>
/// Utilities wrapping AsnReader/AsnWriter for LDAP-specific BER patterns.
/// LDAP uses BER with IMPLICIT TAGS, definite-length only, primitive OCTET STRING only.
/// </summary>
public static class BerHelper
{
    public static readonly AsnEncodingRules Rules = AsnEncodingRules.BER;

    /// <summary>
    /// Read an LDAP string (OCTET STRING encoded as UTF-8).
    /// </summary>
    public static string ReadLdapString(AsnReader reader)
    {
        return reader.ReadOctetString().ToUtf8String();
    }

    /// <summary>
    /// Read an LDAP string with a context-specific tag.
    /// </summary>
    public static string ReadLdapString(AsnReader reader, Asn1Tag tag)
    {
        return reader.ReadOctetString(tag).ToUtf8String();
    }

    /// <summary>
    /// Write an LDAP string (OCTET STRING encoded as UTF-8).
    /// </summary>
    public static void WriteLdapString(AsnWriter writer, string value)
    {
        writer.WriteOctetString(Encoding.UTF8.GetBytes(value));
    }

    /// <summary>
    /// Write an LDAP string with a specific tag.
    /// </summary>
    public static void WriteLdapString(AsnWriter writer, Asn1Tag tag, string value)
    {
        writer.WriteOctetString(Encoding.UTF8.GetBytes(value), tag);
    }

    /// <summary>
    /// Read a SEQUENCE OF LDAP strings (for attribute value lists, etc.).
    /// </summary>
    public static List<string> ReadStringSequence(AsnReader reader)
    {
        var result = new List<string>();
        var sequence = reader.ReadSequence();
        while (sequence.HasData)
        {
            result.Add(ReadLdapString(sequence));
        }
        return result;
    }

    /// <summary>
    /// Read a SET OF LDAP strings.
    /// </summary>
    public static List<string> ReadStringSet(AsnReader reader)
    {
        var result = new List<string>();
        var set = reader.ReadSetOf();
        while (set.HasData)
        {
            result.Add(ReadLdapString(set));
        }
        return result;
    }

    /// <summary>
    /// Read a SET OF OCTET STRINGs as byte arrays.
    /// </summary>
    public static List<byte[]> ReadByteStringSet(AsnReader reader)
    {
        var result = new List<byte[]>();
        var set = reader.ReadSetOf();
        while (set.HasData)
        {
            result.Add(set.ReadOctetString());
        }
        return result;
    }

    /// <summary>
    /// Create an APPLICATION tag (constructed).
    /// </summary>
    public static Asn1Tag ApplicationTag(int tagValue, bool isConstructed = true)
    {
        return new Asn1Tag(TagClass.Application, tagValue, isConstructed);
    }

    /// <summary>
    /// Create a CONTEXT-SPECIFIC tag.
    /// </summary>
    public static Asn1Tag ContextTag(int tagValue, bool isConstructed = false)
    {
        return new Asn1Tag(TagClass.ContextSpecific, tagValue, isConstructed);
    }

    /// <summary>
    /// Try to determine the total length of a BER TLV from a buffer.
    /// Returns -1 if not enough data is available yet.
    /// </summary>
    public static int TryReadTlvLength(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < 2)
            return -1;

        var offset = 0;

        // Read tag byte(s)
        var firstByte = buffer[offset++];
        var tagNumber = firstByte & 0x1F;
        if (tagNumber == 0x1F)
        {
            // Long form tag
            while (offset < buffer.Length)
            {
                var b = buffer[offset++];
                if ((b & 0x80) == 0) break;
            }
        }

        if (offset >= buffer.Length)
            return -1;

        // Read length byte(s)
        var lengthByte = buffer[offset++];
        int contentLength;

        if ((lengthByte & 0x80) == 0)
        {
            // Short form: length is in the byte itself
            contentLength = lengthByte;
        }
        else
        {
            // Long form: lower 7 bits = number of subsequent length bytes
            var numLengthBytes = lengthByte & 0x7F;
            if (numLengthBytes == 0)
                return -1; // Indefinite length not supported in LDAP

            if (offset + numLengthBytes > buffer.Length)
                return -1;

            contentLength = 0;
            for (var i = 0; i < numLengthBytes; i++)
            {
                contentLength = (contentLength << 8) | buffer[offset++];
            }
        }

        var totalLength = offset + contentLength;
        return totalLength;
    }

    /// <summary>
    /// Convert byte array to UTF-8 string.
    /// </summary>
    public static string ToUtf8String(this byte[] bytes)
    {
        return Encoding.UTF8.GetString(bytes);
    }

    public static string ToUtf8String(this ReadOnlyMemory<byte> bytes)
    {
        return Encoding.UTF8.GetString(bytes.Span);
    }
}
