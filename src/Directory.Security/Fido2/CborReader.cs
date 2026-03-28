namespace Directory.Security.Fido2;

/// <summary>
/// Minimal CBOR reader supporting the subset needed for WebAuthn attestation objects.
/// Handles maps, byte strings, text strings, integers, arrays, and simple values (booleans, null).
/// See RFC 7049 for the CBOR specification.
/// </summary>
internal class CborReader
{
    private readonly byte[] _data;
    private int _pos;

    public CborReader(byte[] data)
    {
        _data = data;
        _pos = 0;
    }

    public int Position => _pos;
    public int Length => _data.Length;
    public bool HasData => _pos < _data.Length;

    public object ReadObject()
    {
        if (_pos >= _data.Length)
            throw new InvalidOperationException("CBOR: unexpected end of data");

        var initialByte = _data[_pos];
        var majorType = (initialByte >> 5) & 0x07;
        var additionalInfo = initialByte & 0x1F;

        return majorType switch
        {
            0 => ReadUnsignedInteger(),      // unsigned integer
            1 => ReadNegativeInteger(),       // negative integer
            2 => (object)ReadByteString(),    // byte string
            3 => ReadTextString(),            // text string
            4 => ReadArray(),                 // array
            5 => ReadMap(),                   // map
            6 => ReadTaggedValue(),           // tagged value
            7 => ReadSimpleOrFloat(additionalInfo),
            _ => throw new InvalidOperationException($"CBOR: unsupported major type {majorType}")
        };
    }

    private long ReadUnsignedInteger()
    {
        var initialByte = _data[_pos++];
        var additionalInfo = initialByte & 0x1F;
        return ReadLength(additionalInfo);
    }

    private long ReadNegativeInteger()
    {
        var initialByte = _data[_pos++];
        var additionalInfo = initialByte & 0x1F;
        var value = ReadLength(additionalInfo);
        return -1 - value;
    }

    private byte[] ReadByteString()
    {
        var initialByte = _data[_pos++];
        var additionalInfo = initialByte & 0x1F;
        var length = (int)ReadLength(additionalInfo);
        var result = new byte[length];
        Array.Copy(_data, _pos, result, 0, length);
        _pos += length;
        return result;
    }

    private string ReadTextString()
    {
        var initialByte = _data[_pos++];
        var additionalInfo = initialByte & 0x1F;
        var length = (int)ReadLength(additionalInfo);
        var result = System.Text.Encoding.UTF8.GetString(_data, _pos, length);
        _pos += length;
        return result;
    }

    private List<object> ReadArray()
    {
        var initialByte = _data[_pos++];
        var additionalInfo = initialByte & 0x1F;
        var count = (int)ReadLength(additionalInfo);
        var result = new List<object>(count);
        for (var i = 0; i < count; i++)
        {
            result.Add(ReadObject());
        }
        return result;
    }

    public Dictionary<object, object> ReadMap()
    {
        var initialByte = _data[_pos++];
        var additionalInfo = initialByte & 0x1F;
        var count = (int)ReadLength(additionalInfo);
        var result = new Dictionary<object, object>(count);
        for (var i = 0; i < count; i++)
        {
            var key = ReadObject()!;
            var value = ReadObject();
            result[key] = value;
        }
        return result;
    }

    private object ReadTaggedValue()
    {
        var initialByte = _data[_pos++];
        var additionalInfo = initialByte & 0x1F;
        ReadLength(additionalInfo); // tag number, ignored
        return ReadObject(); // read the tagged content
    }

    private object ReadSimpleOrFloat(int additionalInfo)
    {
        _pos++; // consume initial byte
        return additionalInfo switch
        {
            20 => (object)false,
            21 => (object)true,
            22 => null,
            23 => null, // undefined
            25 => ReadFloat16(),
            26 => ReadFloat32(),
            27 => ReadFloat64(),
            _ => (long)additionalInfo
        };
    }

    private object ReadFloat16()
    {
        var half = (_data[_pos] << 8) | _data[_pos + 1];
        _pos += 2;
        // Simple half-precision to double conversion
        var sign = (half >> 15) & 1;
        var exp = (half >> 10) & 0x1F;
        var mant = half & 0x3FF;
        double value;
        if (exp == 0)
            value = Math.Pow(-1, sign) * Math.Pow(2, -14) * (mant / 1024.0);
        else if (exp == 31)
            value = mant == 0 ? (sign == 0 ? double.PositiveInfinity : double.NegativeInfinity) : double.NaN;
        else
            value = Math.Pow(-1, sign) * Math.Pow(2, exp - 15) * (1 + mant / 1024.0);
        return value;
    }

    private object ReadFloat32()
    {
        var bytes = new byte[4];
        Array.Copy(_data, _pos, bytes, 0, 4);
        _pos += 4;
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);
        return (double)BitConverter.ToSingle(bytes, 0);
    }

    private object ReadFloat64()
    {
        var bytes = new byte[8];
        Array.Copy(_data, _pos, bytes, 0, 8);
        _pos += 8;
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);
        return BitConverter.ToDouble(bytes, 0);
    }

    private long ReadLength(int additionalInfo)
    {
        if (additionalInfo < 24) return additionalInfo;
        if (additionalInfo == 24)
        {
            return _data[_pos++];
        }
        if (additionalInfo == 25)
        {
            var val = (_data[_pos] << 8) | _data[_pos + 1];
            _pos += 2;
            return val;
        }
        if (additionalInfo == 26)
        {
            long val = ((long)_data[_pos] << 24) | ((long)_data[_pos + 1] << 16) |
                       ((long)_data[_pos + 2] << 8) | _data[_pos + 3];
            _pos += 4;
            return val;
        }
        if (additionalInfo == 27)
        {
            long val = 0;
            for (var i = 0; i < 8; i++)
                val = (val << 8) | _data[_pos++];
            return val;
        }
        throw new InvalidOperationException($"CBOR: unsupported additional info {additionalInfo}");
    }

    /// <summary>
    /// Reads raw bytes from the current position without CBOR decoding.
    /// </summary>
    public byte[] ReadRawBytes(int count)
    {
        var result = new byte[count];
        Array.Copy(_data, _pos, result, 0, count);
        _pos += count;
        return result;
    }

    public void Skip(int count)
    {
        _pos += count;
    }
}
