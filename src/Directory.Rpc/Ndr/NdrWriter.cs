using System.Buffers.Binary;
using System.Text;

namespace Directory.Rpc.Ndr;

/// <summary>
/// Writes NDR (Network Data Representation) encoded data for RPC response stub data.
/// Uses little-endian byte order and natural alignment for all types.
/// </summary>
public class NdrWriter
{
    private readonly MemoryStream _stream = new();
    private uint _nextReferentId = 0x00020000; // Standard starting referent ID
    private readonly List<Action> _deferredWrites = new();

    public long Position => _stream.Position;

    /// <summary>
    /// Writes zero-padding bytes to align to the given boundary.
    /// </summary>
    public void Align(int boundary)
    {
        int remainder = (int)(_stream.Position % boundary);
        if (remainder != 0)
        {
            int padding = boundary - remainder;
            for (int i = 0; i < padding; i++)
            {
                _stream.WriteByte(0);
            }
        }
    }

    public void WriteByte(byte value)
    {
        _stream.WriteByte(value);
    }

    public void WriteInt16(short value)
    {
        Align(2);
        Span<byte> buf = stackalloc byte[2];
        BinaryPrimitives.WriteInt16LittleEndian(buf, value);
        _stream.Write(buf);
    }

    public void WriteUInt16(ushort value)
    {
        Align(2);
        Span<byte> buf = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(buf, value);
        _stream.Write(buf);
    }

    public void WriteInt32(int value)
    {
        Align(4);
        Span<byte> buf = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(buf, value);
        _stream.Write(buf);
    }

    public void WriteUInt32(uint value)
    {
        Align(4);
        Span<byte> buf = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buf, value);
        _stream.Write(buf);
    }

    public void WriteInt64(long value)
    {
        Align(8);
        Span<byte> buf = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(buf, value);
        _stream.Write(buf);
    }

    public void WriteUInt64(ulong value)
    {
        Align(8);
        Span<byte> buf = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(buf, value);
        _stream.Write(buf);
    }

    /// <summary>
    /// Writes a 4-byte aligned boolean (uint32: 0 = false, 1 = true).
    /// </summary>
    public void WriteBoolean(bool value)
    {
        WriteUInt32(value ? 1u : 0u);
    }

    public void WriteBytes(ReadOnlySpan<byte> data)
    {
        _stream.Write(data);
    }

    /// <summary>
    /// Writes an NDR RPC_UNICODE_STRING header:
    /// uint16 Length (byte count of chars, no null terminator),
    /// uint16 MaxLength (byte count including null terminator),
    /// align(4), referent ID (non-zero if not null, 0 if null).
    /// Defers the actual string data write.
    /// </summary>
    public void WriteRpcUnicodeString(string value)
    {
        if (value is null)
        {
            WriteUInt16(0); // Length
            WriteUInt16(0); // MaxLength
            Align(4);
            WriteUInt32(0); // Null pointer
        }
        else
        {
            ushort byteLength = (ushort)(value.Length * 2);
            ushort maxByteLength = (ushort)(byteLength + 2); // include null terminator space

            WriteUInt16(byteLength);    // Length
            WriteUInt16(maxByteLength); // MaxLength
            Align(4);

            uint referentId = _nextReferentId++;
            WriteUInt32(referentId);

            // Defer the actual string body
            string captured = value;
            _deferredWrites.Add(() => WriteDeferredConformantVaryingString(captured));
        }
    }

    /// <summary>
    /// Writes a conformant varying string body:
    /// uint32 maxCount, uint32 offset (0), uint32 actualCount, then UTF-16LE chars with null terminator.
    /// </summary>
    public void WriteDeferredConformantVaryingString(string value)
    {
        uint charCountWithNull = (uint)(value.Length + 1); // include null terminator

        WriteUInt32(charCountWithNull); // MaxCount (conformant size)
        WriteUInt32(0);                 // Offset (always 0)
        WriteUInt32(charCountWithNull); // ActualCount

        // Write UTF-16LE string
        byte[] stringBytes = Encoding.Unicode.GetBytes(value);
        _stream.Write(stringBytes);

        // Write null terminator (2 bytes for UTF-16)
        _stream.WriteByte(0);
        _stream.WriteByte(0);
    }

    /// <summary>
    /// Writes a 20-byte context handle: 4 bytes attributes + 16 bytes UUID.
    /// </summary>
    public void WriteContextHandle(uint attributes, Guid uuid)
    {
        Align(4);
        Span<byte> buf = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buf, attributes);
        _stream.Write(buf);

        Span<byte> guidBytes = stackalloc byte[16];
        uuid.TryWriteBytes(guidBytes);
        _stream.Write(guidBytes);
    }

    /// <summary>
    /// Writes an RPC_SID from a SID string like "S-1-5-21-...".
    /// </summary>
    public void WriteRpcSid(string sidString)
    {
        var parts = sidString.Split('-');
        // parts[0] = "S", parts[1] = revision, parts[2] = authority, parts[3..] = sub-authorities

        byte revision = byte.Parse(parts[1]);
        long authority = long.Parse(parts[2]);
        int subAuthorityCount = parts.Length - 3;

        WriteByte(revision);
        WriteByte((byte)subAuthorityCount);

        // Write 6-byte big-endian identifier authority
        Span<byte> authBytes = stackalloc byte[6];
        for (int i = 5; i >= 0; i--)
        {
            authBytes[i] = (byte)(authority & 0xFF);
            authority >>= 8;
        }
        _stream.Write(authBytes);

        // Write sub-authorities (uint32 each, naturally aligned)
        Align(4);
        for (int i = 0; i < subAuthorityCount; i++)
        {
            WriteUInt32(uint.Parse(parts[i + 3]));
        }
    }

    /// <summary>
    /// Writes a pointer referent ID. Returns 0 for null, or an incrementing non-zero referent ID.
    /// </summary>
    public uint WritePointer(bool isNull)
    {
        if (isNull)
        {
            WriteUInt32(0);
            return 0;
        }
        else
        {
            uint referentId = _nextReferentId++;
            WriteUInt32(referentId);
            return referentId;
        }
    }

    /// <summary>
    /// Writes all deferred data (e.g., string bodies for RPC_UNICODE_STRING pointers).
    /// </summary>
    public void FlushDeferred()
    {
        var writes = _deferredWrites.ToList();
        _deferredWrites.Clear();

        foreach (var write in writes)
        {
            write();
        }
    }

    /// <summary>
    /// Returns all bytes written, including any remaining deferred data.
    /// </summary>
    public byte[] ToArray()
    {
        if (_deferredWrites.Count > 0)
        {
            FlushDeferred();
        }

        return _stream.ToArray();
    }
}
