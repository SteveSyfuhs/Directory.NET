using System.Buffers.Binary;
using System.Text;

namespace Directory.Rpc.Protocol;

/// <summary>
/// Builds outgoing DCE/RPC response PDUs as byte arrays.
/// </summary>
public static class PduBuilder
{
    /// <summary>
    /// Builds a Bind Ack PDU (type 12) in response to a Bind request.
    /// </summary>
    public static byte[] BuildBindAck(
        uint callId,
        ushort maxXmitFrag,
        ushort maxRecvFrag,
        uint assocGroupId,
        BindContextResult[] results,
        ushort secondaryPort,
        byte[] authData)
    {
        using var ms = new MemoryStream();

        // Reserve space for header (16 bytes)
        ms.Write(new byte[RpcPduHeader.HeaderSize]);

        // Bind Ack body: maxXmitFrag, maxRecvFrag, assocGroupId
        WriteUInt16(ms, maxXmitFrag);
        WriteUInt16(ms, maxRecvFrag);
        WriteUInt32(ms, assocGroupId);

        // Secondary address (port as string with null terminator)
        string portStr = secondaryPort.ToString();
        ushort secAddrLen = (ushort)(portStr.Length + 1); // include null terminator
        WriteUInt16(ms, secAddrLen);
        ms.Write(Encoding.ASCII.GetBytes(portStr));
        ms.WriteByte(0); // null terminator

        // Pad to 4-byte alignment after secondary address
        AlignStream(ms, 4);

        // Presentation result list
        byte numResults = (byte)results.Length;
        ms.WriteByte(numResults);
        ms.WriteByte(0); // padding
        ms.WriteByte(0);
        ms.WriteByte(0);

        foreach (var result in results)
        {
            WriteUInt16(ms, result.Result);
            WriteUInt16(ms, result.Reason);

            // Transfer syntax: 16-byte UUID + uint32 version
            WriteGuid(ms, result.TransferSyntax.Uuid);
            WriteUInt32(ms, result.TransferSyntax.Version);
        }

        // Auth verifier
        ushort authLength = 0;
        if (authData is { Length: > 0 })
        {
            // Pad stub data to 4-byte alignment before auth verifier
            AlignStream(ms, 4);

            authLength = (ushort)authData.Length;

            // Auth verifier header (8 bytes)
            ms.WriteByte(RpcConstants.AuthTypeNtlm); // auth_type
            ms.WriteByte(RpcConstants.AuthLevelConnect); // auth_level
            ms.WriteByte(0); // auth_pad_length
            ms.WriteByte(0); // reserved
            WriteUInt32(ms, 0); // auth_context_id
            ms.Write(authData);
        }

        // Patch header
        var pdu = ms.ToArray();
        var header = new RpcPduHeader
        {
            VersionMajor = RpcConstants.VersionMajor,
            VersionMinor = RpcConstants.VersionMinor,
            PacketType = RpcConstants.PTypeBindAck,
            PacketFlags = RpcConstants.PfcFirstFrag | RpcConstants.PfcLastFrag,
            DataRepresentation = RpcPduHeader.LittleEndianDrep,
            FragLength = (ushort)pdu.Length,
            AuthLength = authLength,
            CallId = callId,
        };
        header.Write(pdu.AsSpan());

        return pdu;
    }

    /// <summary>
    /// Builds a Response PDU (type 2) wrapping RPC stub data.
    /// </summary>
    public static byte[] BuildResponse(uint callId, ushort contextId, byte[] stubData, byte[] authData)
    {
        using var ms = new MemoryStream();

        // Reserve space for header (16 bytes)
        ms.Write(new byte[RpcPduHeader.HeaderSize]);

        // Response body: allocHint, contextId, cancelCount, reserved
        WriteUInt32(ms, (uint)stubData.Length); // alloc_hint
        WriteUInt16(ms, contextId);
        ms.WriteByte(0); // cancel_count
        ms.WriteByte(0); // reserved

        // Stub data
        ms.Write(stubData);

        // Auth verifier
        ushort authLength = 0;
        if (authData is { Length: > 0 })
        {
            AlignStream(ms, 4);
            authLength = (ushort)authData.Length;

            ms.WriteByte(RpcConstants.AuthTypeNtlm);
            ms.WriteByte(RpcConstants.AuthLevelConnect);
            ms.WriteByte(0); // auth_pad_length
            ms.WriteByte(0); // reserved
            WriteUInt32(ms, 0); // auth_context_id
            ms.Write(authData);
        }

        // Patch header
        var pdu = ms.ToArray();
        var header = new RpcPduHeader
        {
            VersionMajor = RpcConstants.VersionMajor,
            VersionMinor = RpcConstants.VersionMinor,
            PacketType = RpcConstants.PTypeResponse,
            PacketFlags = RpcConstants.PfcFirstFrag | RpcConstants.PfcLastFrag,
            DataRepresentation = RpcPduHeader.LittleEndianDrep,
            FragLength = (ushort)pdu.Length,
            AuthLength = authLength,
            CallId = callId,
        };
        header.Write(pdu.AsSpan());

        return pdu;
    }

    /// <summary>
    /// Builds a Response PDU fragment with explicit flags and alloc_hint.
    /// Used when a response must be split across multiple fragments.
    /// </summary>
    public static byte[] BuildResponseFragment(uint callId, ushort contextId, byte[] stubData, byte flags, uint allocHint)
    {
        using var ms = new MemoryStream();

        // Reserve space for header (16 bytes)
        ms.Write(new byte[RpcPduHeader.HeaderSize]);

        // Response body: allocHint, contextId, cancelCount, reserved
        WriteUInt32(ms, allocHint);
        WriteUInt16(ms, contextId);
        ms.WriteByte(0); // cancel_count
        ms.WriteByte(0); // reserved

        // Stub data
        ms.Write(stubData);

        // Patch header
        var pdu = ms.ToArray();
        var header = new RpcPduHeader
        {
            VersionMajor = RpcConstants.VersionMajor,
            VersionMinor = RpcConstants.VersionMinor,
            PacketType = RpcConstants.PTypeResponse,
            PacketFlags = flags,
            DataRepresentation = RpcPduHeader.LittleEndianDrep,
            FragLength = (ushort)pdu.Length,
            AuthLength = 0,
            CallId = callId,
        };
        header.Write(pdu.AsSpan());

        return pdu;
    }

    /// <summary>
    /// Builds a Fault PDU (type 3) with the given NTSTATUS or NCA status code.
    /// </summary>
    public static byte[] BuildFault(uint callId, uint statusCode)
    {
        using var ms = new MemoryStream();

        // Reserve space for header (16 bytes)
        ms.Write(new byte[RpcPduHeader.HeaderSize]);

        // Fault body: allocHint(4), contextId(2), cancelCount(1), reserved(1), status(4), reserved2(4)
        WriteUInt32(ms, 0); // alloc_hint
        WriteUInt16(ms, 0); // context_id
        ms.WriteByte(0); // cancel_count
        ms.WriteByte(0); // reserved
        WriteUInt32(ms, statusCode); // status
        WriteUInt32(ms, 0); // reserved

        // Patch header
        var pdu = ms.ToArray();
        var header = new RpcPduHeader
        {
            VersionMajor = RpcConstants.VersionMajor,
            VersionMinor = RpcConstants.VersionMinor,
            PacketType = RpcConstants.PTypeFault,
            PacketFlags = RpcConstants.PfcFirstFrag | RpcConstants.PfcLastFrag | RpcConstants.PfcDidNotExecute,
            DataRepresentation = RpcPduHeader.LittleEndianDrep,
            FragLength = (ushort)pdu.Length,
            AuthLength = 0,
            CallId = callId,
        };
        header.Write(pdu.AsSpan());

        return pdu;
    }

    /// <summary>
    /// Builds an AlterContextResp PDU (type 15) — same structure as BindAck but different type.
    /// </summary>
    public static byte[] BuildAlterContextResp(uint callId, BindContextResult[] results, byte[] authData)
    {
        using var ms = new MemoryStream();

        // Reserve space for header (16 bytes)
        ms.Write(new byte[RpcPduHeader.HeaderSize]);

        // Alter Context Resp body: maxXmitFrag, maxRecvFrag, assocGroupId
        WriteUInt16(ms, 4280); // maxXmitFrag
        WriteUInt16(ms, 4280); // maxRecvFrag
        WriteUInt32(ms, 0);    // assocGroupId (echoed from state)

        // Secondary address (empty for alter context resp)
        WriteUInt16(ms, 0); // sec_addr_length = 0

        // Pad to 4-byte alignment
        AlignStream(ms, 4);

        // Presentation result list
        byte numResults = (byte)results.Length;
        ms.WriteByte(numResults);
        ms.WriteByte(0);
        ms.WriteByte(0);
        ms.WriteByte(0);

        foreach (var result in results)
        {
            WriteUInt16(ms, result.Result);
            WriteUInt16(ms, result.Reason);
            WriteGuid(ms, result.TransferSyntax.Uuid);
            WriteUInt32(ms, result.TransferSyntax.Version);
        }

        // Auth verifier
        ushort authLength = 0;
        if (authData is { Length: > 0 })
        {
            AlignStream(ms, 4);
            authLength = (ushort)authData.Length;

            ms.WriteByte(RpcConstants.AuthTypeNtlm);
            ms.WriteByte(RpcConstants.AuthLevelConnect);
            ms.WriteByte(0);
            ms.WriteByte(0);
            WriteUInt32(ms, 0);
            ms.Write(authData);
        }

        // Patch header
        var pdu = ms.ToArray();
        var header = new RpcPduHeader
        {
            VersionMajor = RpcConstants.VersionMajor,
            VersionMinor = RpcConstants.VersionMinor,
            PacketType = RpcConstants.PTypeAlterContextResp,
            PacketFlags = RpcConstants.PfcFirstFrag | RpcConstants.PfcLastFrag,
            DataRepresentation = RpcPduHeader.LittleEndianDrep,
            FragLength = (ushort)pdu.Length,
            AuthLength = authLength,
            CallId = callId,
        };
        header.Write(pdu.AsSpan());

        return pdu;
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

    private static void WriteGuid(MemoryStream ms, Guid guid)
    {
        Span<byte> buf = stackalloc byte[16];
        guid.TryWriteBytes(buf);
        ms.Write(buf);
    }

    private static void AlignStream(MemoryStream ms, int boundary)
    {
        int remainder = (int)(ms.Position % boundary);
        if (remainder != 0)
        {
            int padding = boundary - remainder;
            for (int i = 0; i < padding; i++)
            {
                ms.WriteByte(0);
            }
        }
    }
}
