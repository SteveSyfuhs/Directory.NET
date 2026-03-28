using System.Buffers.Binary;
using Directory.Rpc.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Directory.Rpc.Client;

/// <summary>
/// Endpoint Mapper (EPM) client that queries the RPC endpoint mapper service on port 135
/// to discover the dynamic TCP port for a given RPC interface UUID.
/// Uses the ept_map operation (opnum 3) on the EPM interface.
/// </summary>
public static class EpmClient
{
    private const int EpmPort = 135;

    /// <summary>
    /// Queries the endpoint mapper on port 135 to find the dynamic TCP port
    /// for a given interface UUID.
    /// </summary>
    public static async Task<int> ResolvePortAsync(
        string hostname, Guid interfaceId, ushort majorVersion,
        CancellationToken ct = default)
    {
        return await ResolvePortAsync(hostname, interfaceId, majorVersion, NullLogger.Instance, ct);
    }

    /// <summary>
    /// Queries the endpoint mapper on port 135 to find the dynamic TCP port
    /// for a given interface UUID, with logging support.
    /// </summary>
    public static async Task<int> ResolvePortAsync(
        string hostname, Guid interfaceId, ushort majorVersion,
        ILogger logger, CancellationToken ct = default)
    {
        logger.LogDebug("Resolving EPM endpoint for interface {InterfaceId} v{Version} on {Hostname}",
            interfaceId, majorVersion, hostname);

        await using var client = new RpcTcpClient(logger);

        // Connect to the endpoint mapper on port 135
        await client.ConnectAsync(hostname, EpmPort, ct);

        // Bind to the EPM interface (no auth needed)
        await client.BindAsync(
            RpcConstants.EpmInterfaceId,
            RpcConstants.EpmMajorVersion,
            0, // minor version
            ct);

        // Build the ept_map request stub data
        byte[] requestStub = BuildEptMapRequest(interfaceId, majorVersion);

        logger.LogDebug("Sending ept_map request for interface {InterfaceId}", interfaceId);

        // Send ept_map (opnum 3)
        byte[] responseStub = await client.RequestAsync(3, requestStub, ct);

        // Parse the response to extract the TCP port
        int port = ParseEptMapResponse(responseStub, logger);

        logger.LogDebug("Resolved interface {InterfaceId} to TCP port {Port} on {Hostname}",
            interfaceId, port, hostname);

        return port;
    }

    /// <summary>
    /// Builds the NDR-encoded ept_map request stub data.
    /// </summary>
    private static byte[] BuildEptMapRequest(Guid interfaceId, ushort majorVersion)
    {
        using var ms = new MemoryStream();

        // object UUID (16 bytes, null UUID)
        ms.Write(new byte[16]);

        // tower pointer (referent ID, non-null)
        WriteUInt32(ms, 0x00020000);

        // Tower: we encode it as a conformant byte array
        // First build the tower bytes
        byte[] towerBytes = BuildTower(interfaceId, majorVersion);

        // Conformant array: max_count (uint32) = tower length
        WriteUInt32(ms, (uint)towerBytes.Length);
        // Tower length (uint32) — actual_count in the tower structure
        WriteUInt32(ms, (uint)towerBytes.Length);
        // Tower data
        ms.Write(towerBytes);

        // entry_handle: 20 bytes context handle (all zeros for initial call)
        ms.Write(new byte[20]);

        // max_ents (uint32)
        WriteUInt32(ms, 4);

        return ms.ToArray();
    }

    /// <summary>
    /// Builds a protocol tower for the ept_map request.
    /// The tower describes the interface we're looking for and the transport we want (TCP/IP).
    /// </summary>
    private static byte[] BuildTower(Guid interfaceId, ushort majorVersion)
    {
        using var ms = new MemoryStream();

        // Floor count (ushort) = 5
        WriteUInt16(ms, 5);

        // Floor 1: RPC interface UUID + version
        // LHS: protocol ID (0x0D) + UUID (16) + major version (2)
        // RHS: minor version (2)
        {
            byte[] uuid = new byte[16];
            interfaceId.TryWriteBytes(uuid);

            byte[] lhs = new byte[1 + 16 + 2]; // protocol_id + uuid + version
            lhs[0] = 0x0D; // RPC interface identifier
            uuid.CopyTo(lhs, 1);
            BinaryPrimitives.WriteUInt16LittleEndian(lhs.AsSpan(17), majorVersion);

            WriteUInt16(ms, (ushort)lhs.Length);
            ms.Write(lhs);

            // RHS: minor version
            byte[] rhs = [0x00, 0x00]; // minor version = 0
            WriteUInt16(ms, (ushort)rhs.Length);
            ms.Write(rhs);
        }

        // Floor 2: NDR transfer syntax
        {
            byte[] uuid = new byte[16];
            RpcConstants.NdrSyntaxId.TryWriteBytes(uuid);

            byte[] lhs = new byte[1 + 16 + 2];
            lhs[0] = 0x0D;
            uuid.CopyTo(lhs, 1);
            BinaryPrimitives.WriteUInt16LittleEndian(lhs.AsSpan(17), RpcConstants.NdrSyntaxVersion);

            WriteUInt16(ms, (ushort)lhs.Length);
            ms.Write(lhs);

            byte[] rhs = [0x00, 0x00]; // minor version = 0
            WriteUInt16(ms, (ushort)rhs.Length);
            ms.Write(rhs);
        }

        // Floor 3: RPC connection-oriented protocol
        {
            byte[] lhs = [0x0B]; // ncacn (connection-oriented RPC)
            WriteUInt16(ms, (ushort)lhs.Length);
            ms.Write(lhs);

            byte[] rhs = [0x00, 0x00]; // minor version
            WriteUInt16(ms, (ushort)rhs.Length);
            ms.Write(rhs);
        }

        // Floor 4: TCP port (we're requesting, so port = 0)
        {
            byte[] lhs = [0x07]; // TCP protocol
            WriteUInt16(ms, (ushort)lhs.Length);
            ms.Write(lhs);

            byte[] rhs = [0x00, 0x00]; // port 0 (any)
            WriteUInt16(ms, (ushort)rhs.Length);
            ms.Write(rhs);
        }

        // Floor 5: IP address (0.0.0.0 = any)
        {
            byte[] lhs = [0x09]; // IP protocol
            WriteUInt16(ms, (ushort)lhs.Length);
            ms.Write(lhs);

            byte[] rhs = [0x00, 0x00, 0x00, 0x00]; // 0.0.0.0
            WriteUInt16(ms, (ushort)rhs.Length);
            ms.Write(rhs);
        }

        return ms.ToArray();
    }

    /// <summary>
    /// Parses the ept_map response to extract the TCP port from the returned tower(s).
    /// </summary>
    private static int ParseEptMapResponse(byte[] responseStub, ILogger logger)
    {
        if (responseStub.Length < 28)
        {
            throw new InvalidOperationException(
                $"EPM response too short: {responseStub.Length} bytes.");
        }

        var span = responseStub.AsSpan();
        int offset = 0;

        // entry_handle: 20 bytes context handle
        offset += 20;

        // num_ents (uint32)
        uint numEnts = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset)); offset += 4;

        logger.LogDebug("EPM response: {NumEntries} entries returned", numEnts);

        if (numEnts == 0)
        {
            throw new InvalidOperationException("EPM returned no entries for the requested interface.");
        }

        // max_count of the conformant array of entries
        uint maxCount = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset)); offset += 4;

        // Parse entries to find a TCP port
        for (uint i = 0; i < numEnts && offset < span.Length; i++)
        {
            try
            {
                int port = ParseEptMapEntry(span, ref offset, logger);
                if (port > 0)
                {
                    return port;
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug("Failed to parse EPM entry {Index}: {Error}", i, ex.Message);
            }
        }

        throw new InvalidOperationException("Could not find a TCP port in the EPM response.");
    }

    /// <summary>
    /// Parses a single ept_map entry (tower) and returns the TCP port if found.
    /// </summary>
    private static int ParseEptMapEntry(ReadOnlySpan<byte> span, ref int offset, ILogger logger)
    {
        // Each entry has:
        //   object UUID (16 bytes)
        //   tower pointer (4 bytes referent)
        //   annotation (conformant varying string)

        // object UUID
        offset += 16;

        // tower pointer referent
        uint towerRef = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset)); offset += 4;

        // annotation: max_count (4) + offset (4) + actual_count (4) + chars
        uint annMaxCount = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset)); offset += 4;
        uint annOffset = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset)); offset += 4;
        uint annActualCount = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset)); offset += 4;
        offset += (int)annActualCount; // skip annotation string bytes

        // Align to 4 bytes after annotation
        int remainder = offset % 4;
        if (remainder != 0)
            offset += 4 - remainder;

        if (towerRef == 0)
            return 0;

        // Tower data: conformant array
        // max_count (4) + actual_length (4) + tower bytes
        uint towerMaxCount = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset)); offset += 4;
        uint towerLength = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset)); offset += 4;

        int towerStart = offset;
        int towerEnd = offset + (int)towerLength;

        if (towerEnd > span.Length)
        {
            offset = span.Length;
            return 0;
        }

        // Parse the tower to extract TCP port
        int port = ParseTowerForPort(span.Slice(towerStart, (int)towerLength), logger);

        offset = towerEnd;

        // Align to 4 bytes after tower data
        remainder = offset % 4;
        if (remainder != 0)
            offset += 4 - remainder;

        return port;
    }

    /// <summary>
    /// Parses a protocol tower and extracts the TCP port from floor 4.
    /// </summary>
    private static int ParseTowerForPort(ReadOnlySpan<byte> tower, ILogger logger)
    {
        if (tower.Length < 2)
            return 0;

        ushort floorCount = BinaryPrimitives.ReadUInt16LittleEndian(tower);
        int offset = 2;

        for (int i = 0; i < floorCount && offset < tower.Length; i++)
        {
            if (offset + 2 > tower.Length) break;
            ushort lhsLen = BinaryPrimitives.ReadUInt16LittleEndian(tower.Slice(offset)); offset += 2;

            if (offset + lhsLen > tower.Length) break;
            var lhs = tower.Slice(offset, lhsLen);
            offset += lhsLen;

            if (offset + 2 > tower.Length) break;
            ushort rhsLen = BinaryPrimitives.ReadUInt16LittleEndian(tower.Slice(offset)); offset += 2;

            if (offset + rhsLen > tower.Length) break;
            var rhs = tower.Slice(offset, rhsLen);
            offset += rhsLen;

            // Floor 4 (index 3): TCP port, protocol ID = 0x07
            if (lhs.Length >= 1 && lhs[0] == 0x07 && rhsLen == 2)
            {
                // TCP port is big-endian in the tower
                int port = (rhs[0] << 8) | rhs[1];
                logger.LogDebug("Found TCP port {Port} in tower floor {Floor}", port, i);
                return port;
            }
        }

        return 0;
    }

    private static void WriteUInt16(MemoryStream ms, ushort value)
    {
        Span<byte> buf = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(buf, value);
        ms.Write(buf);
    }

    private static void WriteUInt32(MemoryStream ms, uint value)
    {
        Span<byte> buf = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buf, value);
        ms.Write(buf);
    }
}
