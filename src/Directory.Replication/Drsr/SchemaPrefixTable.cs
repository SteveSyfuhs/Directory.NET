using System.Globalization;
using System.Text;

namespace Directory.Replication.Drsr;

/// <summary>
/// Manages the schema prefix table that maps compact ATTRTYP identifiers to
/// full OID strings, as specified in [MS-DRSR] section 5.141.
///
/// The prefix table works by splitting an OID into a prefix (all but the last arc)
/// and a suffix (the last arc). The prefix is BER-encoded and stored in the table.
/// The ATTRTYP is constructed as: (prefixIndex &lt;&lt; 16) | suffix.
///
/// Standard well-known prefixes are pre-loaded (e.g., 2.5.4 for common attributes,
/// 2.5.6 for object classes, 1.2.840.113556.1.4 for Microsoft attributes).
/// </summary>
public class SchemaPrefixTable
{
    private readonly Dictionary<uint, byte[]> _indexToPrefix = new();
    private readonly Dictionary<string, uint> _prefixToIndex = new(StringComparer.Ordinal);
    private uint _nextIndex;

    /// <summary>
    /// Well-known OID prefixes used by Active Directory.
    /// Index values 0x0000-0x001F are reserved for standard prefixes per [MS-DRSR] section 5.16.1.
    /// </summary>
    private static readonly (uint Index, string OidPrefix)[] WellKnownPrefixes =
    [
        (0x0000, "2.5.4"),            // X.500 attribute types (CN, OU, etc.)
        (0x0001, "2.5.6"),            // X.500 object classes
        (0x0002, "1.2.840.113556.1.2"), // MS legacy attributes
        (0x0003, "1.2.840.113556.1.3"), // MS legacy classes
        (0x0004, "2.16.840.1.101.2.2.1"), // DSAIT
        (0x0005, "2.16.840.1.101.2.2.3"), // DSAIT
        (0x0006, "2.16.840.1.101.2.1.5"), // DSAIT
        (0x0007, "2.16.840.1.101.2.1.4"), // DSAIT
        (0x0008, "2.5.5"),            // X.500 syntaxes
        (0x0009, "1.2.840.113556.1.4"), // MS attribute types
        (0x000A, "1.2.840.113556.1.5"), // MS object classes
        (0x0013, "0.9.2342.19200300.100.1"), // inetOrgPerson attributes
        (0x0014, "2.16.840.1.113730.3"),     // Netscape attributes
        (0x0015, "0.9.2342.19200300.100.4"), // inetOrgPerson classes
        (0x0016, "1.2.840.113556.1.6"),      // MS extended attributes
    ];

    public SchemaPrefixTable()
    {
        // Load well-known prefixes
        foreach (var (index, oidPrefix) in WellKnownPrefixes)
        {
            var encoded = EncodeOidPrefix(oidPrefix);
            _indexToPrefix[index] = encoded;
            _prefixToIndex[oidPrefix] = index;

            if (index >= _nextIndex)
                _nextIndex = index + 1;
        }
    }

    /// <summary>
    /// Gets the number of entries in the prefix table.
    /// </summary>
    public int Count => _indexToPrefix.Count;

    /// <summary>
    /// Maps an OID string to an ATTRTYP by splitting into prefix + last arc,
    /// looking up (or registering) the prefix, and combining with the last arc.
    /// </summary>
    public ATTRTYP OidToAttrTyp(string oid)
    {
        var (prefix, lastArc) = SplitOid(oid);
        var index = GetOrAddPrefix(prefix);

        // ATTRTYP = (index << 16) | lastArc
        // If lastArc >= 0x8000, a special encoding is used
        if (lastArc < 0x8000)
        {
            return new ATTRTYP((index << 16) | (uint)lastArc);
        }
        else
        {
            // For large last-arc values, set bit 15 and use the lower 15 bits + overflow
            return new ATTRTYP((index << 16) | (uint)lastArc);
        }
    }

    /// <summary>
    /// Maps an ATTRTYP back to a full OID string by looking up the prefix index
    /// and appending the last arc.
    /// </summary>
    public string AttrTypToOid(ATTRTYP attrTyp)
    {
        uint index = attrTyp.Value >> 16;
        uint lastArc = attrTyp.Value & 0xFFFF;

        if (!_indexToPrefix.TryGetValue(index, out var encodedPrefix))
            return null;

        var prefix = DecodeOidPrefix(encodedPrefix);
        return $"{prefix}.{lastArc}";
    }

    /// <summary>
    /// Builds a <see cref="SCHEMA_PREFIX_TABLE"/> structure for wire transmission.
    /// </summary>
    public SCHEMA_PREFIX_TABLE ToWireFormat()
    {
        var table = new SCHEMA_PREFIX_TABLE
        {
            PrefixCount = (uint)_indexToPrefix.Count,
            PPrefixEntry = [],
        };

        foreach (var (index, encoded) in _indexToPrefix)
        {
            table.PPrefixEntry.Add(new OID_PREFIX_ENTRY
            {
                NdxValue = index,
                Prefix = encoded,
            });
        }

        return table;
    }

    /// <summary>
    /// Loads a <see cref="SCHEMA_PREFIX_TABLE"/> received from a remote DC.
    /// </summary>
    public static SchemaPrefixTable FromWireFormat(SCHEMA_PREFIX_TABLE wireTable)
    {
        var table = new SchemaPrefixTable();

        foreach (var entry in wireTable.PPrefixEntry)
        {
            table._indexToPrefix[entry.NdxValue] = entry.Prefix;

            var oid = DecodeOidPrefix(entry.Prefix);
            table._prefixToIndex[oid] = entry.NdxValue;

            if (entry.NdxValue >= table._nextIndex)
                table._nextIndex = entry.NdxValue + 1;
        }

        return table;
    }

    /// <summary>
    /// Translates an ATTRTYP from a remote prefix table into the local prefix table.
    /// Returns null if the remote prefix is unknown.
    /// </summary>
    public ATTRTYP? TranslateFromRemote(ATTRTYP remoteAttrTyp, SchemaPrefixTable remoteTable)
    {
        var oid = remoteTable.AttrTypToOid(remoteAttrTyp);
        if (oid == null)
            return null;

        return OidToAttrTyp(oid);
    }

    /// <summary>
    /// Registers a new prefix (or returns the existing index) for an OID prefix string.
    /// </summary>
    private uint GetOrAddPrefix(string oidPrefix)
    {
        if (_prefixToIndex.TryGetValue(oidPrefix, out var existing))
            return existing;

        var index = _nextIndex++;
        var encoded = EncodeOidPrefix(oidPrefix);
        _indexToPrefix[index] = encoded;
        _prefixToIndex[oidPrefix] = index;
        return index;
    }

    /// <summary>
    /// Splits an OID like "2.5.4.3" into prefix "2.5.4" and last arc 3.
    /// </summary>
    private static (string Prefix, int LastArc) SplitOid(string oid)
    {
        int lastDot = oid.LastIndexOf('.');
        if (lastDot < 0)
            throw new ArgumentException($"Invalid OID: {oid}");

        var prefix = oid[..lastDot];
        var lastArc = int.Parse(oid[(lastDot + 1)..], CultureInfo.InvariantCulture);
        return (prefix, lastArc);
    }

    /// <summary>
    /// BER-encodes an OID prefix string into a byte array.
    /// The first two arcs are combined as (arc0 * 40 + arc1), then remaining
    /// arcs are each encoded in base-128 with high-bit continuation.
    /// </summary>
    internal static byte[] EncodeOidPrefix(string oidPrefix)
    {
        var arcs = oidPrefix.Split('.');
        if (arcs.Length < 2)
            throw new ArgumentException($"OID prefix must have at least 2 arcs: {oidPrefix}");

        var result = new List<byte>();

        // First two arcs are encoded as a single byte: arc0 * 40 + arc1
        int first = int.Parse(arcs[0], CultureInfo.InvariantCulture);
        int second = int.Parse(arcs[1], CultureInfo.InvariantCulture);
        result.Add((byte)(first * 40 + second));

        // Remaining arcs are base-128 encoded
        for (int i = 2; i < arcs.Length; i++)
        {
            long arc = long.Parse(arcs[i], CultureInfo.InvariantCulture);
            EncodeBase128(result, arc);
        }

        return result.ToArray();
    }

    /// <summary>
    /// Decodes a BER-encoded OID prefix back to dotted string notation.
    /// </summary>
    internal static string DecodeOidPrefix(byte[] encoded)
    {
        if (encoded.Length == 0)
            return string.Empty;

        var sb = new StringBuilder();

        // Decode first byte as two arcs
        int firstByte = encoded[0];
        int arc0 = firstByte / 40;
        int arc1 = firstByte % 40;
        sb.Append(arc0).Append('.').Append(arc1);

        // Decode remaining base-128 arcs
        int offset = 1;
        while (offset < encoded.Length)
        {
            long arc = DecodeBase128(encoded, ref offset);
            sb.Append('.').Append(arc);
        }

        return sb.ToString();
    }

    private static void EncodeBase128(List<byte> result, long value)
    {
        if (value < 0x80)
        {
            result.Add((byte)value);
            return;
        }

        // Build bytes in reverse
        var temp = new List<byte>();
        temp.Add((byte)(value & 0x7F));
        value >>= 7;

        while (value > 0)
        {
            temp.Add((byte)(0x80 | (value & 0x7F)));
            value >>= 7;
        }

        // Write in correct order (reversed)
        for (int i = temp.Count - 1; i >= 0; i--)
        {
            result.Add(temp[i]);
        }
    }

    private static long DecodeBase128(byte[] data, ref int offset)
    {
        long result = 0;
        while (offset < data.Length)
        {
            byte b = data[offset++];
            result = (result << 7) | (long)(b & 0x7F);

            if ((b & 0x80) == 0)
                break;
        }

        return result;
    }
}
