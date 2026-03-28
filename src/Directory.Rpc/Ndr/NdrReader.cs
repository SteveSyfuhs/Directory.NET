using System.Buffers.Binary;
using System.Text;

namespace Directory.Rpc.Ndr;

/// <summary>
/// Reads NDR (Network Data Representation) encoded data from RPC request stub data.
/// Uses little-endian byte order and natural alignment for all types.
/// </summary>
public class NdrReader
{
    private readonly ReadOnlyMemory<byte> _data;
    private int _offset;

    public NdrReader(ReadOnlyMemory<byte> data)
    {
        _data = data;
        _offset = 0;
    }

    public int Offset => _offset;

    public int Remaining => _data.Length - _offset;

    /// <summary>
    /// Advances the offset to the next alignment boundary.
    /// </summary>
    public void Align(int boundary)
    {
        int remainder = _offset % boundary;
        if (remainder != 0)
        {
            _offset += boundary - remainder;
        }
    }

    public byte ReadByte()
    {
        EnsureAvailable(1);
        return _data.Span[_offset++];
    }

    public short ReadInt16()
    {
        Align(2);
        EnsureAvailable(2);
        short value = BinaryPrimitives.ReadInt16LittleEndian(_data.Span.Slice(_offset));
        _offset += 2;
        return value;
    }

    public ushort ReadUInt16()
    {
        Align(2);
        EnsureAvailable(2);
        ushort value = BinaryPrimitives.ReadUInt16LittleEndian(_data.Span.Slice(_offset));
        _offset += 2;
        return value;
    }

    public int ReadInt32()
    {
        Align(4);
        EnsureAvailable(4);
        int value = BinaryPrimitives.ReadInt32LittleEndian(_data.Span.Slice(_offset));
        _offset += 4;
        return value;
    }

    public uint ReadUInt32()
    {
        Align(4);
        EnsureAvailable(4);
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(_data.Span.Slice(_offset));
        _offset += 4;
        return value;
    }

    public long ReadInt64()
    {
        Align(8);
        EnsureAvailable(8);
        long value = BinaryPrimitives.ReadInt64LittleEndian(_data.Span.Slice(_offset));
        _offset += 8;
        return value;
    }

    public ulong ReadUInt64()
    {
        Align(8);
        EnsureAvailable(8);
        ulong value = BinaryPrimitives.ReadUInt64LittleEndian(_data.Span.Slice(_offset));
        _offset += 8;
        return value;
    }

    /// <summary>
    /// Reads a 4-byte aligned boolean (uint32: 0 = false, non-zero = true).
    /// </summary>
    public bool ReadBoolean()
    {
        return ReadUInt32() != 0;
    }

    public ReadOnlyMemory<byte> ReadBytes(int count)
    {
        EnsureAvailable(count);
        var result = _data.Slice(_offset, count);
        _offset += count;
        return result;
    }

    /// <summary>
    /// Reads a conformant array: uint32 max_count followed by max_count * elementSize bytes.
    /// </summary>
    public ReadOnlyMemory<byte> ReadConformantArray(int elementSize)
    {
        uint maxCount = ReadUInt32();
        int totalBytes = (int)maxCount * elementSize;
        return ReadBytes(totalBytes);
    }

    /// <summary>
    /// Reads an NDR RPC_UNICODE_STRING structure:
    /// uint16 Length, uint16 MaxLength, align(4), uint32 referentId.
    /// The actual string data is deferred (read separately from the referent).
    /// </summary>
    public (ushort Length, ushort MaxLength, uint ReferentId) ReadRpcUnicodeString()
    {
        ushort length = ReadUInt16();
        ushort maxLength = ReadUInt16();
        Align(4);
        uint referentId = ReadUInt32();
        return (length, maxLength, referentId);
    }

    /// <summary>
    /// Reads a conformant varying string:
    /// uint32 maxCount, uint32 offset, uint32 actualCount, then actualCount * 2 bytes of UTF-16LE.
    /// </summary>
    public string ReadConformantVaryingString()
    {
        uint maxCount = ReadUInt32();
        uint offset = ReadUInt32();
        uint actualCount = ReadUInt32();

        if (actualCount == 0)
            return string.Empty;

        int byteCount = (int)actualCount * 2;
        EnsureAvailable(byteCount);

        var chars = _data.Span.Slice(_offset, byteCount);
        _offset += byteCount;

        // Strip null terminator if present
        string result = Encoding.Unicode.GetString(chars);
        return result.TrimEnd('\0');
    }

    /// <summary>
    /// Reads a 20-byte context handle: 4 bytes attributes + 16 bytes UUID.
    /// </summary>
    public (uint Attributes, Guid Uuid) ReadContextHandle()
    {
        Align(4);
        EnsureAvailable(20);

        uint attributes = BinaryPrimitives.ReadUInt32LittleEndian(_data.Span.Slice(_offset));
        _offset += 4;

        var guidBytes = _data.Span.Slice(_offset, 16);
        var uuid = new Guid(guidBytes);
        _offset += 16;

        return (attributes, uuid);
    }

    /// <summary>
    /// Reads an RPC_SID: revision(1), subAuthorityCount(1), identifierAuthority(6),
    /// then subAuthorityCount uint32s. Returns the SID as a string "S-1-5-...".
    /// </summary>
    public string ReadRpcSid()
    {
        byte revision = ReadByte();
        byte subAuthorityCount = ReadByte();

        // Identifier authority is 6 bytes big-endian
        EnsureAvailable(6);
        var authorityBytes = _data.Span.Slice(_offset, 6);
        _offset += 6;

        // Convert 6-byte big-endian authority to a 64-bit value
        long authority = 0;
        for (int i = 0; i < 6; i++)
        {
            authority = (authority << 8) | authorityBytes[i];
        }

        var sb = new StringBuilder();
        sb.Append($"S-{revision}-{authority}");

        // Read sub-authorities (each is a uint32, naturally aligned)
        Align(4);
        for (int i = 0; i < subAuthorityCount; i++)
        {
            uint subAuth = ReadUInt32();
            sb.Append($"-{subAuth}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Reads a uint32 referent ID. A value of 0 indicates a null pointer.
    /// </summary>
    public uint ReadPointer()
    {
        return ReadUInt32();
    }

    private void EnsureAvailable(int count)
    {
        if (_offset + count > _data.Length)
        {
            throw new InvalidOperationException(
                $"NDR buffer underflow: need {count} bytes at offset {_offset}, but only {_data.Length - _offset} remain.");
        }
    }
}
