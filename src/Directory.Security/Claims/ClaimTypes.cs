namespace Directory.Security.Claims;

/// <summary>
/// Claim value types per MS-CTA section 2.1.3 CLAIM_TYPE.
/// </summary>
public enum ClaimValueType : ushort
{
    /// <summary>CLAIM_TYPE_INT64</summary>
    INT64 = 1,

    /// <summary>CLAIM_TYPE_UINT64</summary>
    UINT64 = 2,

    /// <summary>CLAIM_TYPE_STRING</summary>
    STRING = 3,

    /// <summary>CLAIM_TYPE_SID</summary>
    SID = 5,

    /// <summary>CLAIM_TYPE_BOOLEAN</summary>
    BOOLEAN = 6
}

/// <summary>
/// Claim source types per MS-CTA section 2.1.3 CLAIMS_SOURCE_TYPE.
/// </summary>
public enum ClaimSourceType : ushort
{
    /// <summary>CLAIMS_SOURCE_TYPE_AD</summary>
    AD = 1,

    /// <summary>CLAIMS_SOURCE_TYPE_CERTIFICATE</summary>
    Certificate = 2,

    /// <summary>CLAIMS_SOURCE_TYPE_TRANSFORM_POLICY</summary>
    TransformPolicy = 4
}

/// <summary>
/// A single claim entry per MS-CTA section 2.1.3 CLAIM_ENTRY.
/// Exactly one of the typed value arrays is populated, matching <see cref="Type"/>.
/// </summary>
public class ClaimEntry
{
    /// <summary>
    /// Claim identifier (e.g., "ad://ext/department").
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The value type discriminator for this claim.
    /// </summary>
    public ClaimValueType Type { get; set; } = ClaimValueType.STRING;

    /// <summary>
    /// String values when <see cref="Type"/> == STRING.
    /// </summary>
    public List<string> StringValues { get; set; }

    /// <summary>
    /// Int64 values when <see cref="Type"/> == INT64.
    /// </summary>
    public List<long> Int64Values { get; set; }

    /// <summary>
    /// UInt64 values when <see cref="Type"/> == UINT64.
    /// </summary>
    public List<ulong> UInt64Values { get; set; }

    /// <summary>
    /// Boolean values when <see cref="Type"/> == BOOLEAN.
    /// </summary>
    public List<bool> BooleanValues { get; set; }

    /// <summary>
    /// SID values (as binary) when <see cref="Type"/> == SID.
    /// </summary>
    public List<byte[]> SidValues { get; set; }

    /// <summary>
    /// Returns the number of values stored in this claim entry.
    /// </summary>
    public int ValueCount => Type switch
    {
        ClaimValueType.STRING => StringValues?.Count ?? 0,
        ClaimValueType.INT64 => Int64Values?.Count ?? 0,
        ClaimValueType.UINT64 => UInt64Values?.Count ?? 0,
        ClaimValueType.BOOLEAN => BooleanValues?.Count ?? 0,
        ClaimValueType.SID => SidValues?.Count ?? 0,
        _ => 0
    };

    /// <summary>
    /// Creates a deep copy of this claim entry.
    /// </summary>
    public ClaimEntry Clone()
    {
        var clone = new ClaimEntry
        {
            Id = Id,
            Type = Type,
            StringValues = StringValues != null ? new List<string>(StringValues) : null,
            Int64Values = Int64Values != null ? new List<long>(Int64Values) : null,
            UInt64Values = UInt64Values != null ? new List<ulong>(UInt64Values) : null,
            BooleanValues = BooleanValues != null ? new List<bool>(BooleanValues) : null,
            SidValues = SidValues != null ? new List<byte[]>(SidValues.Select(s => (byte[])s.Clone())) : null
        };
        return clone;
    }
}

/// <summary>
/// An array of claims from a single source per MS-CTA section 2.1.3 CLAIMS_ARRAY.
/// </summary>
public class ClaimsArray
{
    /// <summary>
    /// The source of claims in this array.
    /// </summary>
    public ClaimSourceType ClaimSourceType { get; set; } = ClaimSourceType.AD;

    /// <summary>
    /// The claim entries from this source.
    /// </summary>
    public List<ClaimEntry> ClaimEntries { get; set; } = [];
}

/// <summary>
/// A complete set of claims per MS-CTA section 2.1.3 CLAIMS_SET.
/// Contains one or more <see cref="ClaimsArray"/> entries from different sources.
/// </summary>
public class ClaimsSet
{
    /// <summary>
    /// The claims arrays grouped by source type.
    /// </summary>
    public List<ClaimsArray> ClaimsArrays { get; set; } = [];

    /// <summary>
    /// Gets or creates a <see cref="ClaimsArray"/> for the specified source type.
    /// </summary>
    public ClaimsArray GetOrCreateArray(ClaimSourceType sourceType)
    {
        var existing = ClaimsArrays.FirstOrDefault(a => a.ClaimSourceType == sourceType);
        if (existing is not null)
            return existing;

        var array = new ClaimsArray { ClaimSourceType = sourceType };
        ClaimsArrays.Add(array);
        return array;
    }

    /// <summary>
    /// Finds a claim entry by ID across all arrays.
    /// </summary>
    public ClaimEntry FindClaim(string claimId)
    {
        foreach (var array in ClaimsArrays)
        {
            var entry = array.ClaimEntries.FirstOrDefault(
                e => string.Equals(e.Id, claimId, StringComparison.OrdinalIgnoreCase));

            if (entry is not null)
                return entry;
        }

        return null;
    }

    /// <summary>
    /// Returns all claim entries across all arrays.
    /// </summary>
    public IEnumerable<ClaimEntry> GetAllClaims()
    {
        return ClaimsArrays.SelectMany(a => a.ClaimEntries);
    }
}

/// <summary>
/// Transport container for NDR-serialized claims per MS-CTA section 2.1.3 CLAIMS_SET_METADATA.
/// Used for embedding in Kerberos PAC or other transport mechanisms.
/// </summary>
public class ClaimsBlob
{
    /// <summary>
    /// The raw NDR-encoded claims data.
    /// </summary>
    public byte[] Data { get; set; } = [];

    /// <summary>
    /// The uncompressed size of the claims data before compression (0 if not compressed).
    /// </summary>
    public uint UncompressedSize { get; set; }

    /// <summary>
    /// Compression format used. 0 = none, 0xFFFF = LZNT1 (per MS-CTA).
    /// </summary>
    public ushort CompressionFormat { get; set; }

    /// <summary>
    /// Reserved, must be zero.
    /// </summary>
    public ushort ReservedType { get; set; }

    /// <summary>
    /// Reserved, must be zero.
    /// </summary>
    public uint ReservedFieldSize { get; set; }
}
