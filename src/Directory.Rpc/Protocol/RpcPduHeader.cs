using System.Buffers.Binary;

namespace Directory.Rpc.Protocol;

/// <summary>
/// 16-byte common header for all DCE/RPC PDUs (connection-oriented).
/// </summary>
public struct RpcPduHeader
{
    public byte VersionMajor;       // always 5
    public byte VersionMinor;       // always 0
    public byte PacketType;
    public byte PacketFlags;
    public uint DataRepresentation; // 0x00000010 = little-endian, ASCII, IEEE float
    public ushort FragLength;       // total PDU length including header
    public ushort AuthLength;       // length of auth_verifier credentials field
    public uint CallId;

    public const int HeaderSize = 16;
    public const uint LittleEndianDrep = 0x00000010;

    public static RpcPduHeader Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize)
            throw new ArgumentException($"PDU header requires {HeaderSize} bytes, got {data.Length}.");

        return new RpcPduHeader
        {
            VersionMajor = data[0],
            VersionMinor = data[1],
            PacketType = data[2],
            PacketFlags = data[3],
            DataRepresentation = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(4)),
            FragLength = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(8)),
            AuthLength = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(10)),
            CallId = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(12)),
        };
    }

    public readonly void Write(Span<byte> data)
    {
        if (data.Length < HeaderSize)
            throw new ArgumentException($"PDU header requires {HeaderSize} bytes, got {data.Length}.");

        data[0] = VersionMajor;
        data[1] = VersionMinor;
        data[2] = PacketType;
        data[3] = PacketFlags;
        BinaryPrimitives.WriteUInt32LittleEndian(data.Slice(4), DataRepresentation);
        BinaryPrimitives.WriteUInt16LittleEndian(data.Slice(8), FragLength);
        BinaryPrimitives.WriteUInt16LittleEndian(data.Slice(10), AuthLength);
        BinaryPrimitives.WriteUInt32LittleEndian(data.Slice(12), CallId);
    }
}
