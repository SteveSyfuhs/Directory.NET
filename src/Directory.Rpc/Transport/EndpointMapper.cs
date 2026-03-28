using System.Buffers.Binary;
using Directory.Rpc.Dispatch;
using Directory.Rpc.Ndr;
using Directory.Rpc.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Directory.Rpc.Transport;

/// <summary>
/// Implements the DCE/RPC Endpoint Mapper (EPM) interface.
/// Handles ept_map (opnum 3) requests from clients looking up which TCP port
/// hosts a given RPC interface. This allows Windows clients to connect to port 135,
/// ask "where is SAMR?", and get back the dynamic port number.
/// </summary>
public class EndpointMapper : IRpcInterfaceHandler
{
    private readonly RpcInterfaceDispatcher _dispatcher;
    private readonly RpcServerOptions _options;
    private readonly ILogger<EndpointMapper> _logger;

    public Guid InterfaceId => RpcConstants.EpmInterfaceId;
    public ushort MajorVersion => RpcConstants.EpmMajorVersion;
    public ushort MinorVersion => 0;

    public EndpointMapper(
        RpcInterfaceDispatcher dispatcher,
        IOptions<RpcServerOptions> options,
        ILogger<EndpointMapper> logger)
    {
        _dispatcher = dispatcher;
        _options = options.Value;
        _logger = logger;
    }

    public Task<byte[]> HandleRequestAsync(
        ushort opnum,
        ReadOnlyMemory<byte> stubData,
        RpcCallContext context,
        CancellationToken ct)
    {
        return opnum switch
        {
            3 => Task.FromResult(HandleEptMap(stubData)),
            _ => throw new RpcFaultException(RpcConstants.NcaOpRangeError, $"EPM opnum {opnum} not supported"),
        };
    }

    /// <summary>
    /// Handles ept_map (opnum 3): the client sends a tower specifying the interface UUID
    /// it wants to connect to, and we respond with a tower containing our TCP port.
    ///
    /// Request stub (simplified):
    ///   - object UUID pointer (4 bytes pointer + optional 16 bytes)
    ///   - map_tower pointer (4 bytes pointer + tower data)
    ///   - entry_handle (20 bytes context handle)
    ///   - max_towers (uint32)
    ///
    /// Response stub (simplified):
    ///   - entry_handle (20 bytes, zeroed for terminal)
    ///   - num_towers (uint32)
    ///   - tower array (if any)
    ///   - status (uint32, 0 = success)
    /// </summary>
    private byte[] HandleEptMap(ReadOnlyMemory<byte> stubData)
    {
        var reader = new NdrReader(stubData);

        // Read object UUID pointer
        uint objectPtr = reader.ReadPointer();
        Guid objectUuid = Guid.Empty;
        if (objectPtr != 0)
        {
            reader.Align(4);
            var guidBytes = reader.ReadBytes(16);
            objectUuid = new Guid(guidBytes.Span);
        }

        // Read map_tower pointer
        uint towerPtr = reader.ReadPointer();
        Guid requestedInterface = Guid.Empty;
        ushort requestedMajorVersion = 0;

        if (towerPtr != 0)
        {
            // Tower is: uint32 towerLength, then tower octets
            uint towerLength = reader.ReadUInt32();
            if (towerLength > 0)
            {
                var towerData = reader.ReadBytes((int)towerLength);
                (requestedInterface, requestedMajorVersion) = ParseTowerInterfaceId(towerData.Span);
            }
        }

        // Read entry_handle (20 bytes context handle)
        reader.ReadContextHandle();

        // Read max_towers
        uint maxTowers = reader.ReadUInt32();

        _logger.LogDebug("ept_map request for interface {Interface} v{Version}", requestedInterface, requestedMajorVersion);

        // Build response
        var writer = new NdrWriter();

        // entry_handle (zeroed = no more entries)
        writer.WriteContextHandle(0, Guid.Empty);

        // Check if we have this interface registered
        bool hasInterface = requestedInterface != Guid.Empty &&
                           _dispatcher.SupportsInterface(requestedInterface, requestedMajorVersion);

        if (hasInterface && maxTowers > 0)
        {
            // Return one tower
            writer.WriteUInt32(1); // num_towers

            // Conformant array max_count
            writer.WriteUInt32(1);

            // Tower pointer (referent)
            writer.WritePointer(false);

            // Tower data: build a protocol tower pointing to our service port
            byte[] tower = BuildResponseTower(requestedInterface, requestedMajorVersion);

            // Tower: uint32 length + octets
            writer.WriteUInt32((uint)tower.Length);
            writer.WriteBytes(tower);
        }
        else
        {
            // No towers to return
            writer.WriteUInt32(0); // num_towers
            writer.WriteUInt32(0); // conformant array max_count
        }

        // Status: 0 = success (or 0x16c9a0d6 = EPT_S_NOT_REGISTERED if not found)
        writer.WriteUInt32(hasInterface ? 0u : 0x16c9a0d6u);

        return writer.ToArray();
    }

    /// <summary>
    /// Parses the interface UUID and version from an EPM tower's floor entries.
    /// A tower has: uint16 numFloors, then for each floor:
    ///   uint16 lhsLength, lhs bytes, uint16 rhsLength, rhs bytes.
    /// Floor 1 is the interface UUID (protocol 0x0D), floor 2 is the transfer syntax.
    /// </summary>
    private static (Guid interfaceId, ushort majorVersion) ParseTowerInterfaceId(ReadOnlySpan<byte> towerData)
    {
        if (towerData.Length < 2)
            return (Guid.Empty, 0);

        ushort numFloors = BinaryPrimitives.ReadUInt16LittleEndian(towerData);
        int offset = 2;

        if (numFloors < 1)
            return (Guid.Empty, 0);

        // Floor 1: Interface UUID
        if (offset + 2 > towerData.Length) return (Guid.Empty, 0);
        ushort lhsLen = BinaryPrimitives.ReadUInt16LittleEndian(towerData.Slice(offset));
        offset += 2;

        if (lhsLen >= 19 && offset + lhsLen <= towerData.Length)
        {
            // LHS: protocol_id(1) + UUID(16) + version(2)
            // byte protocolId = towerData[offset]; // should be 0x0D for UUID
            var uuid = new Guid(towerData.Slice(offset + 1, 16));
            ushort majorVersion = BinaryPrimitives.ReadUInt16LittleEndian(towerData.Slice(offset + 17));
            return (uuid, majorVersion);
        }

        return (Guid.Empty, 0);
    }

    /// <summary>
    /// Builds a protocol tower for the ept_map response, encoding:
    /// Floor 1: Interface UUID
    /// Floor 2: Transfer syntax (NDR)
    /// Floor 3: RPC protocol (ncacn_ip_tcp, protocol 0x07)
    /// Floor 4: TCP port
    /// Floor 5: IP address (0.0.0.0)
    /// </summary>
    private byte[] BuildResponseTower(Guid interfaceId, ushort majorVersion)
    {
        using var ms = new MemoryStream();

        // Number of floors
        WriteUInt16LE(ms, 5);

        // Floor 1: Interface UUID
        // LHS: protocol_id(0x0D) + UUID(16) + version(2) = 19 bytes
        WriteUInt16LE(ms, 19);
        ms.WriteByte(0x0D); // UUID protocol
        WriteGuidLE(ms, interfaceId);
        WriteUInt16LE(ms, majorVersion);
        // RHS: minor version (2 bytes)
        WriteUInt16LE(ms, 2);
        WriteUInt16LE(ms, 0); // minor version

        // Floor 2: Transfer syntax (NDR 2.0)
        WriteUInt16LE(ms, 19);
        ms.WriteByte(0x0D);
        WriteGuidLE(ms, RpcConstants.NdrSyntaxId);
        WriteUInt16LE(ms, RpcConstants.NdrSyntaxVersion);
        WriteUInt16LE(ms, 2);
        WriteUInt16LE(ms, 0);

        // Floor 3: RPC connection-oriented protocol
        WriteUInt16LE(ms, 1);
        ms.WriteByte(0x0B); // ncacn_ip_tcp RPC protocol
        WriteUInt16LE(ms, 2);
        WriteUInt16BE(ms, 0); // minor version

        // Floor 4: TCP port (big-endian as per DCE spec)
        WriteUInt16LE(ms, 1);
        ms.WriteByte(0x07); // TCP protocol
        WriteUInt16LE(ms, 2);
        WriteUInt16BE(ms, (ushort)_options.ServicePort);

        // Floor 5: IP address (0.0.0.0 = any)
        WriteUInt16LE(ms, 1);
        ms.WriteByte(0x09); // IP protocol
        WriteUInt16LE(ms, 4);
        ms.Write(new byte[] { 0, 0, 0, 0 }); // 0.0.0.0

        return ms.ToArray();
    }

    private static void WriteUInt16LE(MemoryStream ms, ushort value)
    {
        Span<byte> buf = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(buf, value);
        ms.Write(buf);
    }

    private static void WriteUInt16BE(MemoryStream ms, ushort value)
    {
        Span<byte> buf = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(buf, value);
        ms.Write(buf);
    }

    private static void WriteGuidLE(MemoryStream ms, Guid guid)
    {
        Span<byte> buf = stackalloc byte[16];
        guid.TryWriteBytes(buf);
        ms.Write(buf);
    }
}
