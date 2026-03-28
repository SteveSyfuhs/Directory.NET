using System.Buffers.Binary;

namespace Directory.Core.Models;

/// <summary>
/// Utilities for converting SID and GUID values between their string representations
/// (used in Cosmos DB storage) and binary representations (used in LDAP wire protocol).
///
/// SID binary format (self-relative):
///   [0]     revision (1 byte)
///   [1]     sub-authority count (1 byte)
///   [2..7]  identifier authority (6 bytes, big-endian)
///   [8..]   sub-authorities (N × 4 bytes, little-endian)
///
/// GUID binary format: standard 16-byte Windows GUID layout (mixed-endian),
/// produced by System.Guid.ToByteArray().
/// </summary>
public static class SidUtils
{
    /// <summary>
    /// Convert a string SID (e.g., "S-1-5-21-3623811015-3361044348-30300820-1013")
    /// to its binary representation as used in LDAP OCTET STRING values.
    /// Delegates to the existing serialization in AccessControlEntry.
    /// </summary>
    public static byte[] StringSidToBytes(string sid)
    {
        return AccessControlEntry.SerializeSid(sid);
    }

    /// <summary>
    /// Convert a binary SID to its string representation (e.g., "S-1-5-21-...").
    /// Delegates to the existing deserialization in AccessControlEntry.
    /// </summary>
    public static string BytesToStringSid(byte[] bytes)
    {
        return AccessControlEntry.DeserializeSid(bytes);
    }

    /// <summary>
    /// Convert a GUID string to its 16-byte binary representation using Windows byte order
    /// (mixed-endian, as produced by System.Guid.ToByteArray()).
    /// This matches what Windows AD clients expect for objectGUID.
    /// </summary>
    public static byte[] GuidToLdapBytes(string guidString)
    {
        return Guid.Parse(guidString).ToByteArray();
    }

    /// <summary>
    /// Convert a 16-byte binary GUID (Windows byte order) to its string representation.
    /// </summary>
    public static string LdapBytesToGuid(byte[] bytes)
    {
        return new Guid(bytes).ToString();
    }

    /// <summary>
    /// Check if a string looks like a SID (starts with "S-").
    /// </summary>
    public static bool IsStringSid(string value)
    {
        return value.StartsWith("S-", StringComparison.Ordinal) && value.Length > 4;
    }

    /// <summary>
    /// Check if a string looks like a GUID (parseable by Guid.TryParse).
    /// </summary>
    public static bool IsStringGuid(string value)
    {
        return Guid.TryParse(value, out _);
    }

    /// <summary>
    /// Try to convert an LDAP filter value for objectSid to the stored string format.
    /// The filter value may be a string SID ("S-1-5-...") or a binary-escaped SID
    /// (e.g., "\01\05\00\00..."). Returns the string SID for Cosmos DB comparison.
    /// </summary>
    public static string TryDecodeFilterSid(string filterValue)
    {
        if (IsStringSid(filterValue))
            return filterValue;

        // Try decoding as LDAP binary-escaped value (\xx\xx...)
        var bytes = TryDecodeLdapBinaryEscape(filterValue);
        if (bytes != null && bytes.Length >= 8)
        {
            try
            {
                return BytesToStringSid(bytes);
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// Try to convert an LDAP filter value for objectGUID to the stored string format.
    /// The filter value may be a GUID string or a binary-escaped GUID.
    /// Returns the string GUID for Cosmos DB comparison.
    /// </summary>
    public static string TryDecodeFilterGuid(string filterValue)
    {
        if (IsStringGuid(filterValue))
            return filterValue;

        // Try decoding as LDAP binary-escaped value (\xx\xx...)
        var bytes = TryDecodeLdapBinaryEscape(filterValue);
        if (bytes != null && bytes.Length == 16)
        {
            try
            {
                return LdapBytesToGuid(bytes);
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// Decode an LDAP binary-escaped string (e.g., "\01\05\00\00\00\00\00\05")
    /// into raw bytes. Returns null if the value does not appear to be binary-escaped.
    /// </summary>
    private static byte[] TryDecodeLdapBinaryEscape(string value)
    {
        if (string.IsNullOrEmpty(value) || !value.Contains('\\'))
            return null;

        try
        {
            var bytes = new List<byte>();
            int i = 0;
            while (i < value.Length)
            {
                if (value[i] == '\\' && i + 2 < value.Length)
                {
                    var hex = value.Substring(i + 1, 2);
                    bytes.Add(Convert.ToByte(hex, 16));
                    i += 3;
                }
                else
                {
                    // Not a pure binary-escaped string
                    return null;
                }
            }
            return bytes.Count > 0 ? bytes.ToArray() : null;
        }
        catch
        {
            return null;
        }
    }
}
