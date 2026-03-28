using System.Buffers.Binary;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Directory.Security.Claims;

/// <summary>
/// Serializes and deserializes <see cref="ClaimsSet"/> to/from NDR-encoded byte arrays
/// per MS-CTA section 2.1.3 for inclusion in the Kerberos PAC (CLAIMS_SET structure).
///
/// Wire format (simplified NDR encoding):
///   CLAIMS_SET ::= {
///     ClaimsArrayCount  : UINT32
///     ClaimsArrays      : CLAIMS_ARRAY[ClaimsArrayCount]
///     ReservedType      : UINT16
///     ReservedFieldSize : UINT32
///     ReservedField     : BYTE[ReservedFieldSize]
///   }
///
///   CLAIMS_ARRAY ::= {
///     ClaimSourceType   : UINT16
///     ClaimCount        : UINT32
///     ClaimEntries      : CLAIM_ENTRY[ClaimCount]
///   }
///
///   CLAIM_ENTRY ::= {
///     Id                : NDR_STRING
///     Type              : UINT16
///     ValueCount        : UINT32
///     Values            : (typed array based on Type)
///   }
/// </summary>
public class ClaimsSerialization
{
    private readonly ILogger<ClaimsSerialization> _logger;

    public ClaimsSerialization(ILogger<ClaimsSerialization> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Serializes a <see cref="ClaimsSet"/> to NDR-encoded bytes for PAC inclusion.
    /// </summary>
    public byte[] Serialize(ClaimsSet claimsSet)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms, Encoding.Unicode, leaveOpen: true);

        // ClaimsArrayCount
        writer.Write((uint)claimsSet.ClaimsArrays.Count);

        // Each ClaimsArray
        foreach (var array in claimsSet.ClaimsArrays)
        {
            SerializeClaimsArray(writer, array);
        }

        // ReservedType
        writer.Write((ushort)0);
        // ReservedFieldSize
        writer.Write((uint)0);

        writer.Flush();
        return ms.ToArray();
    }

    /// <summary>
    /// Deserializes NDR-encoded bytes back into a <see cref="ClaimsSet"/>.
    /// </summary>
    public ClaimsSet Deserialize(ReadOnlySpan<byte> data)
    {
        var claimsSet = new ClaimsSet();

        if (data.Length < 4)
        {
            _logger.LogWarning("Claims data too short ({Length} bytes), returning empty ClaimsSet", data.Length);
            return claimsSet;
        }

        int offset = 0;

        // ClaimsArrayCount
        uint arrayCount = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
        offset += 4;

        for (uint i = 0; i < arrayCount && offset < data.Length; i++)
        {
            var array = DeserializeClaimsArray(data, ref offset);
            if (array is not null)
                claimsSet.ClaimsArrays.Add(array);
        }

        return claimsSet;
    }

    /// <summary>
    /// Wraps a serialized <see cref="ClaimsSet"/> in a <see cref="ClaimsBlob"/>
    /// with CLAIMS_SET_METADATA envelope per MS-CTA section 2.1.3.
    /// </summary>
    public ClaimsBlob WrapInBlob(byte[] serializedClaimsSet)
    {
        return new ClaimsBlob
        {
            Data = serializedClaimsSet,
            UncompressedSize = (uint)serializedClaimsSet.Length,
            CompressionFormat = 0, // No compression
            ReservedType = 0,
            ReservedFieldSize = 0
        };
    }

    /// <summary>
    /// Serializes a <see cref="ClaimsBlob"/> to its wire format (CLAIMS_SET_METADATA).
    /// </summary>
    public byte[] SerializeBlob(ClaimsBlob blob)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms, Encoding.Unicode, leaveOpen: true);

        // ClaimsSetSize
        writer.Write((uint)blob.Data.Length);
        // ClaimsSetData
        writer.Write(blob.Data);
        // CompressionFormat
        writer.Write(blob.CompressionFormat);
        // UncompressedClaimsSetSize
        writer.Write(blob.UncompressedSize);
        // ReservedType
        writer.Write(blob.ReservedType);
        // ReservedFieldSize
        writer.Write(blob.ReservedFieldSize);

        writer.Flush();
        return ms.ToArray();
    }

    /// <summary>
    /// Deserializes a <see cref="ClaimsBlob"/> from its wire format (CLAIMS_SET_METADATA).
    /// </summary>
    public ClaimsBlob DeserializeBlob(ReadOnlySpan<byte> data)
    {
        var blob = new ClaimsBlob();

        if (data.Length < 12)
        {
            _logger.LogWarning("ClaimsBlob data too short ({Length} bytes)", data.Length);
            return blob;
        }

        int offset = 0;

        // ClaimsSetSize
        uint claimsSetSize = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
        offset += 4;

        // ClaimsSetData
        if (offset + claimsSetSize <= (uint)data.Length)
        {
            blob.Data = data.Slice(offset, (int)claimsSetSize).ToArray();
            offset += (int)claimsSetSize;
        }
        else
        {
            _logger.LogWarning("ClaimsBlob claims data extends beyond buffer");
            blob.Data = data[offset..].ToArray();
            return blob;
        }

        // CompressionFormat
        if (offset + 2 <= data.Length)
        {
            blob.CompressionFormat = BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
            offset += 2;
        }

        // UncompressedClaimsSetSize
        if (offset + 4 <= data.Length)
        {
            blob.UncompressedSize = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
            offset += 4;
        }

        // ReservedType
        if (offset + 2 <= data.Length)
        {
            blob.ReservedType = BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
            offset += 2;
        }

        // ReservedFieldSize
        if (offset + 4 <= data.Length)
        {
            blob.ReservedFieldSize = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
        }

        return blob;
    }

    private static void SerializeClaimsArray(BinaryWriter writer, ClaimsArray array)
    {
        // ClaimSourceType
        writer.Write((ushort)array.ClaimSourceType);

        // ClaimCount
        writer.Write((uint)array.ClaimEntries.Count);

        // Each ClaimEntry
        foreach (var entry in array.ClaimEntries)
        {
            SerializeClaimEntry(writer, entry);
        }
    }

    private static void SerializeClaimEntry(BinaryWriter writer, ClaimEntry entry)
    {
        // Id as NDR conformant string: length prefix + UTF-16LE chars + null terminator
        WriteNdrString(writer, entry.Id);

        // Type
        writer.Write((ushort)entry.Type);

        // Values - written as count + typed array
        switch (entry.Type)
        {
            case ClaimValueType.STRING:
                var strings = entry.StringValues ?? [];
                writer.Write((uint)strings.Count);
                foreach (var s in strings)
                    WriteNdrString(writer, s);
                break;

            case ClaimValueType.INT64:
                var longs = entry.Int64Values ?? [];
                writer.Write((uint)longs.Count);
                foreach (var v in longs)
                    writer.Write(v);
                break;

            case ClaimValueType.UINT64:
                var ulongs = entry.UInt64Values ?? [];
                writer.Write((uint)ulongs.Count);
                foreach (var v in ulongs)
                    writer.Write(v);
                break;

            case ClaimValueType.BOOLEAN:
                var bools = entry.BooleanValues ?? [];
                writer.Write((uint)bools.Count);
                foreach (var v in bools)
                    writer.Write(v ? (ulong)1 : (ulong)0);
                break;

            case ClaimValueType.SID:
                var sids = entry.SidValues ?? [];
                writer.Write((uint)sids.Count);
                foreach (var sid in sids)
                {
                    writer.Write((uint)sid.Length);
                    writer.Write(sid);
                }
                break;
        }
    }

    private ClaimsArray DeserializeClaimsArray(ReadOnlySpan<byte> data, ref int offset)
    {
        if (offset + 6 > data.Length)
            return null;

        var array = new ClaimsArray();

        // ClaimSourceType
        array.ClaimSourceType = (ClaimSourceType)BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
        offset += 2;

        // ClaimCount
        uint claimCount = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
        offset += 4;

        for (uint i = 0; i < claimCount && offset < data.Length; i++)
        {
            var entry = DeserializeClaimEntry(data, ref offset);
            if (entry is not null)
                array.ClaimEntries.Add(entry);
        }

        return array;
    }

    private ClaimEntry DeserializeClaimEntry(ReadOnlySpan<byte> data, ref int offset)
    {
        var entry = new ClaimEntry();

        // Id
        var id = ReadNdrString(data, ref offset);
        if (id is null)
            return null;
        entry.Id = id;

        // Type
        if (offset + 2 > data.Length)
            return null;
        entry.Type = (ClaimValueType)BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
        offset += 2;

        // ValueCount
        if (offset + 4 > data.Length)
            return null;
        uint valueCount = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
        offset += 4;

        switch (entry.Type)
        {
            case ClaimValueType.STRING:
                entry.StringValues = [];
                for (uint i = 0; i < valueCount && offset < data.Length; i++)
                {
                    var s = ReadNdrString(data, ref offset);
                    if (s is not null)
                        entry.StringValues.Add(s);
                }
                break;

            case ClaimValueType.INT64:
                entry.Int64Values = [];
                for (uint i = 0; i < valueCount && offset + 8 <= data.Length; i++)
                {
                    entry.Int64Values.Add(BinaryPrimitives.ReadInt64LittleEndian(data[offset..]));
                    offset += 8;
                }
                break;

            case ClaimValueType.UINT64:
                entry.UInt64Values = [];
                for (uint i = 0; i < valueCount && offset + 8 <= data.Length; i++)
                {
                    entry.UInt64Values.Add(BinaryPrimitives.ReadUInt64LittleEndian(data[offset..]));
                    offset += 8;
                }
                break;

            case ClaimValueType.BOOLEAN:
                entry.BooleanValues = [];
                for (uint i = 0; i < valueCount && offset + 8 <= data.Length; i++)
                {
                    entry.BooleanValues.Add(BinaryPrimitives.ReadUInt64LittleEndian(data[offset..]) != 0);
                    offset += 8;
                }
                break;

            case ClaimValueType.SID:
                entry.SidValues = [];
                for (uint i = 0; i < valueCount && offset + 4 <= data.Length; i++)
                {
                    uint sidLen = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
                    offset += 4;
                    if (offset + (int)sidLen <= data.Length)
                    {
                        entry.SidValues.Add(data.Slice(offset, (int)sidLen).ToArray());
                        offset += (int)sidLen;
                    }
                }
                break;

            default:
                _logger.LogWarning("Unknown claim value type {Type} for claim {Id}", entry.Type, entry.Id);
                return null;
        }

        return entry;
    }

    private static void WriteNdrString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.Unicode.GetBytes(value);
        // Length in chars (including null terminator conceptually)
        writer.Write((uint)(value.Length + 1));
        writer.Write(bytes);
        // Null terminator (2 bytes for Unicode)
        writer.Write((ushort)0);
    }

    private static string ReadNdrString(ReadOnlySpan<byte> data, ref int offset)
    {
        if (offset + 4 > data.Length)
            return null;

        uint charCount = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
        offset += 4;

        if (charCount == 0)
            return string.Empty;

        int byteCount = (int)(charCount * 2); // UTF-16LE
        if (offset + byteCount > data.Length)
            return null;

        // Read the string bytes (includes null terminator)
        var str = Encoding.Unicode.GetString(data.Slice(offset, byteCount - 2)); // Exclude null terminator
        offset += byteCount;

        return str;
    }
}
