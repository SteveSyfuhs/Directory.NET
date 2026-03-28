using System.Globalization;
using System.Text;
using Directory.Core.Interfaces;
using Directory.Rpc.Ndr;

namespace Directory.Replication.Drsr;

/// <summary>
/// Resolves ATTRTYPs from DRSUAPI replication to OIDs and attribute names.
/// Windows sends a SCHEMA_PREFIX_TABLE in each GetNCChanges response that maps
/// prefix indices to OID prefixes. An ATTRTYP = (prefixIndex &lt;&lt; 16) | lastOidComponent.
/// </summary>
public class PrefixTableResolver
{
    /// <summary>
    /// Well-known ATTRTYP mappings (these are standard across all AD forests).
    /// See MS-DRSR section 5.16.4 and MS-ADTS section 3.1.1.2.6.
    /// Key = ATTRTYP value, Value = ldapDisplayName.
    /// </summary>
    private static readonly Dictionary<uint, string> WellKnownAttrs = new()
    {
        // Prefix 0x0000: 2.5.4 (X.500 standard attributes)
        { 0x00000000, "objectClass" },         // 2.5.4.0
        { 0x00000003, "cn" },                  // 2.5.4.3
        { 0x00000004, "sn" },                  // 2.5.4.4
        { 0x00000006, "countryName" },         // 2.5.4.6
        { 0x00000007, "l" },                   // 2.5.4.7
        { 0x00000008, "st" },                  // 2.5.4.8
        { 0x0000000A, "organizationName" },    // 2.5.4.10
        { 0x0000000B, "ou" },                  // 2.5.4.11
        { 0x0000000D, "description" },         // 2.5.4.13
        { 0x00000023, "userPassword" },        // 2.5.4.35
        { 0x00000029, "givenName" },           // 2.5.4.41 (givenName is 2.5.4.42 actually)
        { 0x0000002A, "givenName" },           // 2.5.4.42
        { 0x00000031, "distinguishedName" },   // 2.5.4.49
        { 0x00000032, "member" },              // 2.5.4.50

        // Prefix 0x0002: 1.2.840.113556.1.2 (MS legacy attributes)
        { 0x00020066, "objectClass" },         // 1.2.840.113556.1.2.102 (alternate objectClass OID used by AD)
        { 0x00020002, "whenCreated" },         // 1.2.840.113556.1.2.2
        { 0x00020003, "whenChanged" },         // 1.2.840.113556.1.2.3
        { 0x00020013, "name" },                // 1.2.840.113556.1.2.13 (RDN)
        { 0x0002001E, "nTSecurityDescriptor" }, // 1.2.840.113556.1.2.281 — encoded differently
        { 0x00020024, "displayName" },         // 1.2.840.113556.1.2.13 (displayName is elsewhere)

        // Prefix 0x0009: 1.2.840.113556.1.4 (MS AD attributes)
        { 0x00090001, "instanceType" },        // 1.2.840.113556.1.4.1
        { 0x00090002, "whenCreated" },         // 1.2.840.113556.1.4.2
        { 0x00090008, "userAccountControl" },  // 1.2.840.113556.1.4.8
        { 0x0009005A, "unicodePwd" },          // 1.2.840.113556.1.4.90
        { 0x0009005B, "dBCSPwd" },             // 1.2.840.113556.1.4.91
        { 0x00090037, "ntPwdHistory" },        // 1.2.840.113556.1.4.94 — approximate
        { 0x00090092, "objectSid" },           // 1.2.840.113556.1.4.146
        { 0x00090094, "objectGUID" },          // 1.2.840.113556.1.4.148
        { 0x000900DC, "displayName" },         // 1.2.840.113556.1.4.220
        { 0x000900DD, "sAMAccountName" },      // 1.2.840.113556.1.4.221
        { 0x000900DE, "memberOf" },            // 1.2.840.113556.1.4.222
        { 0x000900DF, "member" },              // 1.2.840.113556.1.4.223
        { 0x00090290, "userPrincipalName" },   // 1.2.840.113556.1.4.656
        { 0x00090303, "servicePrincipalName" }, // 1.2.840.113556.1.4.771
        { 0x00090119, "nTSecurityDescriptor" }, // 1.2.840.113556.1.4.281
        { 0x000900E5, "supplementalCredentials" }, // 1.2.840.113556.1.4.125
        { 0x00090177, "objectCategory" },      // 1.2.840.113556.1.4.782 — approximate
        { 0x000900A9, "pwdLastSet" },          // 1.2.840.113556.1.4.96 — approximate
        { 0x00090051, "lastLogon" },           // 1.2.840.113556.1.4.52 — approximate
        { 0x00090060, "accountExpires" },      // 1.2.840.113556.1.4.159
        { 0x0009009C, "groupType" },           // 1.2.840.113556.1.4.750
        { 0x00090029, "mail" },                // 1.2.840.113556.1.4.41 — approximate
        { 0x0009003E, "dNSHostName" },         // 1.2.840.113556.1.4.619 — approximate
        { 0x00090266, "options" },             // 1.2.840.113556.1.4.1
        { 0x000900FB, "invocationId" },        // 1.2.840.113556.1.4.336 — approximate
        { 0x0009030F, "msDS-Behavior-Version" }, // 1.2.840.113556.1.4.1459

        // Prefix 0x000A: 1.2.840.113556.1.5 (MS AD classes)
        { 0x000A0009, "nTDSDSA" },            // 1.2.840.113556.1.5.7000.9 (approx)
    };

    /// <summary>
    /// Reverse mapping: attribute name (case-insensitive) to OID string.
    /// </summary>
    private static readonly Dictionary<string, string> NameToOid = new(StringComparer.OrdinalIgnoreCase)
    {
        { "objectClass", "2.5.4.0" },
        { "cn", "2.5.4.3" },
        { "sn", "2.5.4.4" },
        { "countryName", "2.5.4.6" },
        { "l", "2.5.4.7" },
        { "st", "2.5.4.8" },
        { "organizationName", "2.5.4.10" },
        { "ou", "2.5.4.11" },
        { "description", "2.5.4.13" },
        { "userPassword", "2.5.4.35" },
        { "givenName", "2.5.4.42" },
        { "distinguishedName", "2.5.4.49" },
        { "member", "2.5.4.50" },
        { "name", "1.2.840.113556.1.2.13" },
        { "whenCreated", "1.2.840.113556.1.2.2" },
        { "whenChanged", "1.2.840.113556.1.2.3" },
        { "instanceType", "1.2.840.113556.1.4.1" },
        { "userAccountControl", "1.2.840.113556.1.4.8" },
        { "unicodePwd", "1.2.840.113556.1.4.90" },
        { "dBCSPwd", "1.2.840.113556.1.4.91" },
        { "objectSid", "1.2.840.113556.1.4.146" },
        { "objectGUID", "1.2.840.113556.1.4.148" },
        { "displayName", "1.2.840.113556.1.4.220" },
        { "sAMAccountName", "1.2.840.113556.1.4.221" },
        { "memberOf", "1.2.840.113556.1.4.222" },
        { "userPrincipalName", "1.2.840.113556.1.4.656" },
        { "servicePrincipalName", "1.2.840.113556.1.4.771" },
        { "nTSecurityDescriptor", "1.2.840.113556.1.4.281" },
        { "supplementalCredentials", "1.2.840.113556.1.4.125" },
        { "objectCategory", "1.2.840.113556.1.4.782" },
        { "pwdLastSet", "1.2.840.113556.1.4.96" },
        { "lastLogon", "1.2.840.113556.1.4.52" },
        { "accountExpires", "1.2.840.113556.1.4.159" },
        { "groupType", "1.2.840.113556.1.4.750" },
        { "mail", "1.2.840.113556.1.4.41" },
        { "dNSHostName", "1.2.840.113556.1.4.619" },
        { "invocationId", "1.2.840.113556.1.4.336" },
        { "hasMasterNCs", "1.2.840.113556.1.2.14" },
        { "msDS-HasMasterNCs", "1.2.840.113556.1.4.1836" },
        { "msDS-Behavior-Version", "1.2.840.113556.1.4.1459" },
        { "options", "1.2.840.113556.1.4.307" },
    };

    /// <summary>
    /// Reverse mapping: OID string to attribute name.
    /// </summary>
    private static readonly Dictionary<string, string> OidToName;

    static PrefixTableResolver()
    {
        OidToName = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, oid) in NameToOid)
        {
            OidToName.TryAdd(oid, name);
        }
    }

    private readonly Dictionary<uint, byte[]> _prefixEntries; // ndx -> OID prefix bytes
    private readonly Dictionary<uint, string> _resolvedCache;  // ATTRTYP -> attributeName
    private readonly ISchemaService _schema;

    /// <summary>
    /// Build from the prefix entries in a GetNCChanges response.
    /// </summary>
    /// <param name="prefixEntries">
    /// Map of prefix index (upper 16 bits of ATTRTYP) to BER-encoded OID prefix bytes.
    /// </param>
    /// <param name="schema">Optional schema service for resolving OIDs to attribute names.</param>
    public PrefixTableResolver(Dictionary<uint, byte[]> prefixEntries, ISchemaService schema = null)
    {
        _prefixEntries = prefixEntries ?? throw new ArgumentNullException(nameof(prefixEntries));
        _resolvedCache = new Dictionary<uint, string>();
        _schema = schema;
    }

    /// <summary>
    /// Build a resolver from a <see cref="SCHEMA_PREFIX_TABLE"/> wire structure
    /// (as received in GetNCChanges responses).
    /// </summary>
    public static PrefixTableResolver FromWireTable(SCHEMA_PREFIX_TABLE wireTable, ISchemaService schema = null)
    {
        var entries = new Dictionary<uint, byte[]>();
        foreach (var entry in wireTable.PPrefixEntry)
        {
            entries[entry.NdxValue] = entry.Prefix;
        }

        return new PrefixTableResolver(entries, schema);
    }

    /// <summary>
    /// Parse a SCHEMA_PREFIX_TABLE from NDR-encoded bytes.
    /// </summary>
    public static PrefixTableResolver FromNdr(byte[] ndrData, ISchemaService schema = null)
    {
        var reader = new NdrReader(ndrData);

        var prefixCount = reader.ReadUInt32();
        var entryPointer = reader.ReadUInt32(); // pointer to entry array

        var entries = new Dictionary<uint, byte[]>();

        if (entryPointer != 0)
        {
            var count = reader.ReadUInt32(); // conformant array max count

            // Read entry headers
            var headers = new List<(uint Ndx, uint PrefixLen, uint PrefixPtr)>();
            for (uint i = 0; i < count && i < prefixCount; i++)
            {
                var ndx = reader.ReadUInt32();
                var prefixLen = reader.ReadUInt32();
                var prefixPtr = reader.ReadUInt32();
                headers.Add((ndx, prefixLen, prefixPtr));
            }

            // Read deferred prefix data
            foreach (var (ndx, prefixLen, prefixPtr) in headers)
            {
                if (prefixPtr != 0)
                {
                    var dataLen = reader.ReadUInt32();
                    var data = reader.ReadBytes((int)dataLen).ToArray();
                    entries[ndx] = data;
                }
                else
                {
                    entries[ndx] = [];
                }
            }
        }

        return new PrefixTableResolver(entries, schema);
    }

    /// <summary>
    /// Resolve an ATTRTYP to an OID string (e.g., "2.5.4.3").
    /// The ATTRTYP is decomposed as: prefixIndex = attrTyp >> 16, suffix = attrTyp &amp; 0xFFFF.
    /// The full OID is the prefix table entry's OID with the suffix appended as the last arc.
    /// </summary>
    public string ResolveToOid(uint attrTyp)
    {
        uint prefixIndex = attrTyp >> 16;
        uint suffix = attrTyp & 0xFFFF;

        if (!_prefixEntries.TryGetValue(prefixIndex, out var prefixBytes) || prefixBytes.Length == 0)
            return null;

        // Build the full OID by appending the suffix to the BER-encoded prefix
        byte[] fullOidBytes;

        if (suffix < 128)
        {
            // Single byte suffix
            fullOidBytes = new byte[prefixBytes.Length + 1];
            Buffer.BlockCopy(prefixBytes, 0, fullOidBytes, 0, prefixBytes.Length);
            fullOidBytes[prefixBytes.Length] = (byte)suffix;
        }
        else
        {
            // Multi-byte BER encoding for suffix >= 128
            var suffixBytes = EncodeBerComponent(suffix);
            fullOidBytes = new byte[prefixBytes.Length + suffixBytes.Length];
            Buffer.BlockCopy(prefixBytes, 0, fullOidBytes, 0, prefixBytes.Length);
            Buffer.BlockCopy(suffixBytes, 0, fullOidBytes, prefixBytes.Length, suffixBytes.Length);
        }

        return OidBytesToString(fullOidBytes);
    }

    /// <summary>
    /// Resolve an ATTRTYP to an attribute name (e.g., "cn").
    /// Uses the schema service if available, falls back to well-known mappings,
    /// then falls back to OID resolution.
    /// </summary>
    public string ResolveToName(uint attrTyp)
    {
        // Check cache first
        if (_resolvedCache.TryGetValue(attrTyp, out var cached))
            return cached;

        // Check well-known ATTRTYPs
        if (WellKnownAttrs.TryGetValue(attrTyp, out var wellKnown))
        {
            _resolvedCache[attrTyp] = wellKnown;
            return wellKnown;
        }

        // Resolve to OID first
        var oid = ResolveToOid(attrTyp);
        if (oid == null)
            return null;

        // Check OID-to-name mapping
        if (OidToName.TryGetValue(oid, out var name))
        {
            _resolvedCache[attrTyp] = name;
            return name;
        }

        // Try schema service
        if (_schema != null)
        {
            // Search all attributes for a matching OID
            foreach (var attr in _schema.GetAllAttributes())
            {
                if (string.Equals(attr.Oid, oid, StringComparison.Ordinal))
                {
                    _resolvedCache[attrTyp] = attr.LdapDisplayName;
                    return attr.LdapDisplayName;
                }
            }
        }

        // Fall back to returning the OID itself as the name
        _resolvedCache[attrTyp] = oid;
        return oid;
    }

    /// <summary>
    /// Convert BER-encoded OID bytes to a dotted-string OID representation.
    /// The first byte encodes the first two arcs as (arc0 * 40 + arc1).
    /// Subsequent arcs are base-128 encoded with high-bit continuation.
    /// </summary>
    private static string OidBytesToString(byte[] oidBytes)
    {
        if (oidBytes.Length == 0)
            return string.Empty;

        var sb = new StringBuilder();

        // First byte encodes two arcs: arc0 * 40 + arc1
        int firstByte = oidBytes[0];
        int arc0 = firstByte / 40;
        int arc1 = firstByte % 40;
        sb.Append(arc0).Append('.').Append(arc1);

        // Decode remaining base-128 arcs
        int offset = 1;
        while (offset < oidBytes.Length)
        {
            long arc = 0;
            while (offset < oidBytes.Length)
            {
                byte b = oidBytes[offset++];
                arc = (arc << 7) | (long)(b & 0x7F);
                if ((b & 0x80) == 0)
                    break;
            }

            sb.Append('.').Append(arc);
        }

        return sb.ToString();
    }

    /// <summary>
    /// BER-encode a single OID component (arc value) into one or more bytes.
    /// Values less than 128 are a single byte; larger values use base-128 with high-bit continuation.
    /// </summary>
    private static byte[] EncodeBerComponent(uint value)
    {
        if (value < 128)
            return [(byte)value];

        // Count how many 7-bit groups we need
        var temp = new List<byte>();
        temp.Add((byte)(value & 0x7F));
        value >>= 7;

        while (value > 0)
        {
            temp.Add((byte)(0x80 | (value & 0x7F)));
            value >>= 7;
        }

        // Reverse to get correct order (most significant group first)
        temp.Reverse();
        return temp.ToArray();
    }
}
