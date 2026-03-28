using System.Buffers.Binary;
using System.Net.Sockets;
using Directory.Rpc.Protocol;
using Microsoft.Extensions.Logging;

namespace Directory.Rpc.Client;

/// <summary>
/// A DCE/RPC client over TCP that handles the full connection lifecycle:
/// connect, bind with NTLM authentication, and request/response exchange.
/// </summary>
public class RpcTcpClient : IAsyncDisposable
{
    private readonly ILogger _logger;
    private TcpClient _tcpClient;
    private NetworkStream _stream;
    private uint _callId;
    private ushort _maxXmitFrag = 4280;
    private ushort _maxRecvFrag = 4280;
    private NtlmSessionSecurity _sessionSecurity;

    public RpcTcpClient(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Whether the TCP connection is established.
    /// </summary>
    public bool IsConnected => _tcpClient?.Connected == true;

    /// <summary>
    /// Whether NTLM authentication has completed successfully.
    /// </summary>
    public bool IsAuthenticated { get; private set; }

    /// <summary>
    /// The NTLM session key derived during authentication, used for signing/sealing.
    /// </summary>
    public byte[] SessionKey { get; private set; } = [];

    /// <summary>
    /// The negotiated presentation context ID from the bind.
    /// </summary>
    public ushort ContextId { get; private set; }

    /// <summary>
    /// The authentication level to use for request/response PDUs.
    /// Default is PKT_PRIVACY (6) which is required for DRSUAPI.
    /// Set to PKT_INTEGRITY (5) for signing without encryption.
    /// </summary>
    public byte AuthLevel { get; set; } = RpcConstants.AuthLevelPrivacy;

    /// <summary>
    /// Connects to the RPC server over TCP.
    /// </summary>
    public async Task ConnectAsync(string hostname, int port, CancellationToken ct = default)
    {
        _logger.LogDebug("Connecting to {Hostname}:{Port}", hostname, port);

        _tcpClient = new TcpClient();
        await _tcpClient.ConnectAsync(hostname, port, ct);
        _stream = _tcpClient.GetStream();

        _logger.LogDebug("Connected to {Hostname}:{Port}", hostname, port);
    }

    /// <summary>
    /// Binds to an RPC interface with NTLM authentication.
    /// Sends BIND (with Type 1) -> receives BIND_ACK (with Type 2) -> sends AUTH3 (with Type 3).
    /// </summary>
    public async Task BindAsync(
        Guid interfaceId, ushort majorVersion, ushort minorVersion,
        string username, string domain, byte[] ntHash,
        CancellationToken ct = default)
    {
        EnsureConnected();

        var ntlmBuilder = new NtlmClientMessageBuilder();

        // Step 1: Build NTLM Type 1 (Negotiate) message and wrap in SPNEGO
        byte[] type1Message = ntlmBuilder.BuildNegotiateMessage();
        byte[] spnegoInit = SpnegoTokenBuilder.WrapNtlmNegotiate(type1Message);
        _logger.LogDebug("Built NTLM Type 1 negotiate message ({Length} bytes), SPNEGO wrapped ({SpnegoLen} bytes)",
            type1Message.Length, spnegoInit.Length);

        // Step 2: Build and send BIND PDU with SPNEGO-wrapped Type 1 in auth verifier
        uint callId = ++_callId;
        byte[] bindPdu = BuildBindPdu(callId, interfaceId, majorVersion, minorVersion, spnegoInit);
        await SendPduAsync(bindPdu, ct);
        _logger.LogDebug("Sent BIND PDU (callId={CallId}, {Length} bytes)", callId, bindPdu.Length);

        // Step 3: Receive BIND_ACK
        byte[] bindAckPdu = await ReceivePduAsync(ct);
        var bindAckHeader = RpcPduHeader.Read(bindAckPdu);

        if (bindAckHeader.PacketType != RpcConstants.PTypeBindAck)
        {
            throw new InvalidOperationException(
                $"Expected BIND_ACK (type {RpcConstants.PTypeBindAck}), got type {bindAckHeader.PacketType}.");
        }

        _logger.LogDebug("Received BIND_ACK (callId={CallId}, fragLen={FragLen}, authLen={AuthLen})",
            bindAckHeader.CallId, bindAckHeader.FragLength, bindAckHeader.AuthLength);

        // Parse BIND_ACK to get max fragment sizes and context results
        ParseBindAck(bindAckPdu, bindAckHeader);

        // Extract SPNEGO token from auth verifier and unwrap to get NTLM Type 2 (Challenge)
        var authVerifier = PduParser.ParseAuthVerifier(bindAckPdu, bindAckHeader.AuthLength);
        if (authVerifier is null)
        {
            throw new InvalidOperationException("BIND_ACK did not contain an auth verifier with SPNEGO token.");
        }

        byte[] spnegoChallenge = authVerifier.Credentials.ToArray();
        byte[] type2Message = SpnegoTokenBuilder.UnwrapNtlmChallenge(spnegoChallenge);
        _logger.LogDebug("Unwrapped SPNEGO to get NTLM Type 2 challenge ({Length} bytes)", type2Message.Length);

        // Step 4: Parse the Type 2 to get server challenge
        var (serverChallenge, negotiateFlags, targetName) = ntlmBuilder.ParseChallengeMessage(type2Message);
        _logger.LogDebug("Server challenge received, target={TargetName}, flags=0x{Flags:X8}",
            targetName ?? "(none)", negotiateFlags);

        // Step 5: Build NTLM Type 3 (Authenticate) message
        var (type3Message, sessionKey) = ntlmBuilder.BuildAuthenticateMessage(
            username, domain, ntHash, serverChallenge, negotiateFlags);
        _logger.LogDebug("Built NTLM Type 3 authenticate message ({Length} bytes)", type3Message.Length);

        // Step 6: Wrap Type 3 in SPNEGO and send AUTH3 PDU
        byte[] spnegoAuth = SpnegoTokenBuilder.WrapNtlmAuthenticate(type3Message);
        byte[] auth3Pdu = BuildAuth3Pdu(callId, spnegoAuth);
        await SendPduAsync(auth3Pdu, ct);
        _logger.LogDebug("Sent AUTH3 PDU with SPNEGO-wrapped Type 3 ({Length} bytes)", auth3Pdu.Length);

        // AUTH3 has no server response. Authentication is complete.
        SessionKey = sessionKey;
        IsAuthenticated = true;

        // Create session security for signing/sealing subsequent PDUs
        _sessionSecurity = new NtlmSessionSecurity(ntlmBuilder.ExportedSessionKey, ntlmBuilder.NegotiateFlags);
        _logger.LogDebug("NTLM authentication complete. Session security initialized (authLevel={AuthLevel})",
            AuthLevel);
    }

    /// <summary>
    /// Binds to an RPC interface without authentication (e.g., for the Endpoint Mapper).
    /// </summary>
    public async Task BindAsync(
        Guid interfaceId, ushort majorVersion, ushort minorVersion,
        CancellationToken ct = default)
    {
        EnsureConnected();

        uint callId = ++_callId;
        byte[] bindPdu = BuildBindPdu(callId, interfaceId, majorVersion, minorVersion, authData: null);
        await SendPduAsync(bindPdu, ct);
        _logger.LogDebug("Sent unauthenticated BIND PDU (callId={CallId}, {Length} bytes)", callId, bindPdu.Length);

        byte[] bindAckPdu = await ReceivePduAsync(ct);
        var bindAckHeader = RpcPduHeader.Read(bindAckPdu);

        if (bindAckHeader.PacketType != RpcConstants.PTypeBindAck)
        {
            throw new InvalidOperationException(
                $"Expected BIND_ACK (type {RpcConstants.PTypeBindAck}), got type {bindAckHeader.PacketType}.");
        }

        ParseBindAck(bindAckPdu, bindAckHeader);
        _logger.LogDebug("Unauthenticated bind complete. MaxXmitFrag={MaxXmit}, MaxRecvFrag={MaxRecv}",
            _maxXmitFrag, _maxRecvFrag);
    }

    /// <summary>
    /// Sends an RPC request and receives the response.
    /// Handles fragmentation on both the send and receive sides.
    /// </summary>
    public async Task<byte[]> RequestAsync(ushort opnum, byte[] stubData, CancellationToken ct = default)
    {
        EnsureConnected();

        uint callId = ++_callId;
        bool useAuth = _sessionSecurity is not null && IsAuthenticated;

        // Calculate maximum stub data per request fragment
        // Request PDU overhead: 16 (header) + 8 (allocHint + contextId + opnum) = 24 bytes
        // If authenticated: + auth padding (up to 15) + auth verifier header (8) + signature (16) = up to 39 extra
        const int requestOverhead = RpcPduHeader.HeaderSize + 8;
        const int authOverhead = 16 + 8 + 16; // max padding + auth header + NTLM signature
        int maxStubPerFragment = _maxXmitFrag - requestOverhead - (useAuth ? authOverhead : 0);

        if (stubData.Length <= maxStubPerFragment)
        {
            // Single fragment
            byte[] requestPdu = BuildAuthenticatedRequestPdu(callId, ContextId, opnum, stubData,
                RpcConstants.PfcFirstFrag | RpcConstants.PfcLastFrag);
            await SendPduAsync(requestPdu, ct);
            _logger.LogDebug("Sent REQUEST PDU (callId={CallId}, opnum={Opnum}, stubLen={StubLen}, auth={Auth})",
                callId, opnum, stubData.Length, useAuth);
        }
        else
        {
            // Multi-fragment send
            int offset = 0;
            int fragmentIndex = 0;
            while (offset < stubData.Length)
            {
                int chunkSize = Math.Min(maxStubPerFragment, stubData.Length - offset);
                bool isFirst = offset == 0;
                bool isLast = offset + chunkSize >= stubData.Length;

                byte flags = 0;
                if (isFirst) flags |= RpcConstants.PfcFirstFrag;
                if (isLast) flags |= RpcConstants.PfcLastFrag;

                byte[] chunk = new byte[chunkSize];
                Array.Copy(stubData, offset, chunk, 0, chunkSize);

                byte[] fragmentPdu = BuildAuthenticatedRequestPdu(callId, ContextId, opnum, chunk, flags);
                await SendPduAsync(fragmentPdu, ct);

                _logger.LogDebug(
                    "Sent REQUEST fragment {Index} (callId={CallId}, flags=0x{Flags:X2}, chunkLen={ChunkLen})",
                    fragmentIndex, callId, flags, chunkSize);

                offset += chunkSize;
                fragmentIndex++;
            }
        }

        // Receive response, handling multi-fragment reassembly
        using var reassembly = new MemoryStream();
        bool receivedLast = false;

        while (!receivedLast)
        {
            byte[] responsePdu = await ReceivePduAsync(ct);
            var header = RpcPduHeader.Read(responsePdu);

            if (header.PacketType == RpcConstants.PTypeFault)
            {
                // Parse fault status
                uint status = 0;
                if (responsePdu.Length >= RpcPduHeader.HeaderSize + 16)
                {
                    status = BinaryPrimitives.ReadUInt32LittleEndian(
                        responsePdu.AsSpan(RpcPduHeader.HeaderSize + 8));
                }
                throw new InvalidOperationException(
                    $"RPC fault received: status=0x{status:X8} (callId={header.CallId})");
            }

            if (header.PacketType != RpcConstants.PTypeResponse)
            {
                throw new InvalidOperationException(
                    $"Expected RESPONSE (type {RpcConstants.PTypeResponse}), got type {header.PacketType}.");
            }

            // Extract stub data from response PDU
            // Response body: allocHint(4) + contextId(2) + cancelCount(1) + reserved(1) = 8 bytes after header
            int stubStart = RpcPduHeader.HeaderSize + 8;
            int stubEnd = header.FragLength;
            byte[] responseStubData;

            if (header.AuthLength > 0 && _sessionSecurity is not null)
            {
                // Parse auth verifier to get signature and auth pad length
                var respAuthVerifier = PduParser.ParseAuthVerifier(responsePdu, header.AuthLength);
                if (respAuthVerifier is null)
                {
                    throw new InvalidOperationException("Response PDU missing expected auth verifier.");
                }

                // The authenticated region is stub_data + auth_pad (before the auth verifier header)
                int authVerifierStart = header.FragLength - header.AuthLength - 8;
                int authPadLen = respAuthVerifier.AuthPadLength;
                int dataEnd = authVerifierStart - authPadLen;

                if (dataEnd < stubStart)
                    dataEnd = stubStart;

                // Extract the stub + padding that was signed/sealed
                int sealedLen = authVerifierStart - stubStart;
                byte[] sealedData = new byte[sealedLen];
                Array.Copy(responsePdu, stubStart, sealedData, 0, sealedLen);

                byte[] signature = respAuthVerifier.Credentials.ToArray();

                if (AuthLevel == RpcConstants.AuthLevelPrivacy)
                {
                    // Unseal (decrypt + verify)
                    byte[] decrypted = _sessionSecurity.Unseal(sealedData, signature);
                    // Strip auth padding from decrypted data
                    int actualStubLen = decrypted.Length - authPadLen;
                    responseStubData = new byte[actualStubLen];
                    Array.Copy(decrypted, 0, responseStubData, 0, actualStubLen);
                }
                else
                {
                    // Verify signature only
                    if (!_sessionSecurity.Verify(sealedData, signature))
                    {
                        throw new InvalidOperationException("Response PDU signature verification failed.");
                    }
                    int actualStubLen = sealedLen - authPadLen;
                    responseStubData = new byte[actualStubLen];
                    Array.Copy(sealedData, 0, responseStubData, 0, actualStubLen);
                }
            }
            else
            {
                if (header.AuthLength > 0)
                {
                    stubEnd -= (header.AuthLength + 8);
                }

                int len = stubEnd - stubStart;
                responseStubData = len > 0 ? new byte[len] : [];
                if (len > 0)
                {
                    Array.Copy(responsePdu, stubStart, responseStubData, 0, len);
                }
            }

            reassembly.Write(responseStubData, 0, responseStubData.Length);

            bool isLastFrag = (header.PacketFlags & RpcConstants.PfcLastFrag) != 0;
            receivedLast = isLastFrag;

            _logger.LogDebug(
                "Received RESPONSE fragment (callId={CallId}, flags=0x{Flags:X2}, fragLen={FragLen}, stubBytes={StubBytes})",
                header.CallId, header.PacketFlags, header.FragLength, responseStubData.Length);
        }

        byte[] result = reassembly.ToArray();
        _logger.LogDebug("Request complete (callId={CallId}, opnum={Opnum}, responseStubLen={Len})",
            callId, opnum, result.Length);

        return result;
    }

    public async ValueTask DisposeAsync()
    {
        if (_stream is not null)
        {
            await _stream.DisposeAsync();
            _stream = null;
        }

        _tcpClient?.Dispose();
        _tcpClient = null;

        GC.SuppressFinalize(this);
    }

    private void EnsureConnected()
    {
        if (_stream is null || _tcpClient?.Connected != true)
        {
            throw new InvalidOperationException("Not connected. Call ConnectAsync first.");
        }
    }

    private async Task SendPduAsync(byte[] pdu, CancellationToken ct)
    {
        await _stream.WriteAsync(pdu, ct);
        await _stream.FlushAsync(ct);
    }

    /// <summary>
    /// Reads a complete PDU from the stream: first the 16-byte header to get FragLength,
    /// then the remaining bytes.
    /// </summary>
    private async Task<byte[]> ReceivePduAsync(CancellationToken ct)
    {
        // Read the 16-byte header
        byte[] headerBuf = new byte[RpcPduHeader.HeaderSize];
        await ReadExactAsync(_stream, headerBuf, ct);

        var header = RpcPduHeader.Read(headerBuf);

        if (header.VersionMajor != RpcConstants.VersionMajor ||
            header.VersionMinor != RpcConstants.VersionMinor)
        {
            throw new InvalidOperationException(
                $"Unsupported RPC version {header.VersionMajor}.{header.VersionMinor}.");
        }

        // Read the remaining bytes
        int remaining = header.FragLength - RpcPduHeader.HeaderSize;
        if (remaining < 0)
        {
            throw new InvalidOperationException($"Invalid fragment length {header.FragLength}.");
        }

        byte[] pdu = new byte[header.FragLength];
        headerBuf.CopyTo(pdu, 0);

        if (remaining > 0)
        {
            await ReadExactAsync(_stream, pdu.AsMemory(RpcPduHeader.HeaderSize, remaining), ct);
        }

        return pdu;
    }

    /// <summary>
    /// Reads exactly the requested number of bytes, handling partial reads.
    /// </summary>
    private static async Task ReadExactAsync(NetworkStream stream, Memory<byte> buffer, CancellationToken ct)
    {
        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.Slice(totalRead), ct);
            if (read == 0)
                throw new IOException("Connection closed while reading PDU data.");
            totalRead += read;
        }
    }

    /// <summary>
    /// Parses the BIND_ACK PDU to extract negotiated parameters and context results.
    /// </summary>
    private void ParseBindAck(byte[] pdu, RpcPduHeader header)
    {
        var span = pdu.AsSpan();
        int offset = RpcPduHeader.HeaderSize;

        // MaxXmitFrag (2), MaxRecvFrag (2), AssocGroupId (4)
        _maxXmitFrag = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(offset)); offset += 2;
        _maxRecvFrag = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(offset)); offset += 2;
        uint assocGroupId = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset)); offset += 4;

        _logger.LogDebug("BIND_ACK: maxXmitFrag={MaxXmit}, maxRecvFrag={MaxRecv}, assocGroupId={AssocGroup}",
            _maxXmitFrag, _maxRecvFrag, assocGroupId);

        // Secondary address: ushort length, then string, then padding to 4-byte alignment
        ushort secAddrLen = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(offset)); offset += 2;
        offset += secAddrLen; // skip string + null terminator

        // Pad to 4-byte alignment
        int remainder = offset % 4;
        if (remainder != 0)
            offset += 4 - remainder;

        // Presentation result list: numResults (1 byte) + 3 padding bytes
        byte numResults = span[offset]; offset += 4; // 1 byte + 3 padding

        for (int i = 0; i < numResults; i++)
        {
            ushort result = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(offset)); offset += 2;
            ushort reason = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(offset)); offset += 2;

            // Transfer syntax UUID (16) + version (4)
            var transferUuid = new Guid(span.Slice(offset, 16)); offset += 16;
            uint transferVersion = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset)); offset += 4;

            if (result == RpcConstants.BindResultAcceptance)
            {
                ContextId = (ushort)i;
                _logger.LogDebug("Context {Index} accepted, transfer syntax={Uuid} v{Version}",
                    i, transferUuid, transferVersion);
            }
            else
            {
                _logger.LogDebug("Context {Index} rejected: result={Result}, reason={Reason}",
                    i, result, reason);
            }
        }
    }

    /// <summary>
    /// Builds a BIND PDU with an optional NTLM auth verifier.
    /// </summary>
    private static byte[] BuildBindPdu(
        uint callId, Guid interfaceId, ushort majorVersion, ushort minorVersion,
        byte[] authData)
    {
        using var ms = new MemoryStream();

        // Reserve header (16 bytes)
        ms.Write(new byte[RpcPduHeader.HeaderSize]);

        // MaxXmitFrag (2)
        WriteUInt16(ms, 4280);
        // MaxRecvFrag (2)
        WriteUInt16(ms, 4280);
        // AssocGroupId (4)
        WriteUInt32(ms, 0);

        // Presentation context list
        // NumContexts (1) + Reserved (3)
        ms.WriteByte(1);
        ms.WriteByte(0);
        ms.WriteByte(0);
        ms.WriteByte(0);

        // Context[0]:
        //   ContextId (2)
        WriteUInt16(ms, 0);
        //   NumTransferSyntaxes (1)
        ms.WriteByte(1);
        //   Reserved (1)
        ms.WriteByte(0);

        //   AbstractSyntax: UUID (16) + Version (4)
        //   Version encodes major in low 16 bits, minor in high 16 bits
        WriteGuid(ms, interfaceId);
        uint abstractVersion = (uint)majorVersion | ((uint)minorVersion << 16);
        WriteUInt32(ms, abstractVersion);

        //   TransferSyntax[0]: NDR 2.0
        WriteGuid(ms, RpcConstants.NdrSyntaxId);
        WriteUInt32(ms, RpcConstants.NdrSyntaxVersion);

        // Auth verifier (if NTLM)
        ushort authLength = 0;
        if (authData is { Length: > 0 })
        {
            // Align to 4-byte boundary before auth verifier
            AlignStream(ms, 4);

            authLength = (ushort)authData.Length;

            // Auth verifier header (8 bytes) — use SPNEGO auth type
            ms.WriteByte(RpcConstants.AuthTypeSpnego);    // auth_type (SPNEGO = 0x09)
            ms.WriteByte(RpcConstants.AuthLevelConnect);  // auth_level
            ms.WriteByte(0);                              // auth_pad_length
            ms.WriteByte(0);                              // reserved
            WriteUInt32(ms, 0);                           // auth_context_id
            ms.Write(authData);
        }

        // Patch header
        byte[] pdu = ms.ToArray();
        var header = new RpcPduHeader
        {
            VersionMajor = RpcConstants.VersionMajor,
            VersionMinor = RpcConstants.VersionMinor,
            PacketType = RpcConstants.PTypeBind,
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
    /// Builds an AUTH3 PDU with the NTLM Type 3 message in the auth verifier.
    /// </summary>
    private static byte[] BuildAuth3Pdu(uint callId, byte[] authData)
    {
        using var ms = new MemoryStream();

        // Reserve header (16 bytes)
        ms.Write(new byte[RpcPduHeader.HeaderSize]);

        // Pad (4 bytes) — AUTH3 body is just padding before the auth verifier
        WriteUInt32(ms, 0);

        // Auth verifier header (8 bytes) + credentials — use SPNEGO auth type
        ushort authLength = (ushort)authData.Length;
        ms.WriteByte(RpcConstants.AuthTypeSpnego);    // auth_type (SPNEGO = 0x09)
        ms.WriteByte(RpcConstants.AuthLevelConnect);  // auth_level
        ms.WriteByte(0);                              // auth_pad_length
        ms.WriteByte(0);                              // reserved
        WriteUInt32(ms, 0);                           // auth_context_id
        ms.Write(authData);

        // Patch header
        byte[] pdu = ms.ToArray();
        var header = new RpcPduHeader
        {
            VersionMajor = RpcConstants.VersionMajor,
            VersionMinor = RpcConstants.VersionMinor,
            PacketType = RpcConstants.PTypeAuth3,
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
    /// Builds a REQUEST PDU wrapping stub data with the given PFC flags.
    /// </summary>
    private static byte[] BuildRequestPdu(
        uint callId, ushort contextId, ushort opnum, byte[] stubData, byte flags)
    {
        using var ms = new MemoryStream();

        // Reserve header (16 bytes)
        ms.Write(new byte[RpcPduHeader.HeaderSize]);

        // Request body: allocHint (4), contextId (2), opnum (2)
        WriteUInt32(ms, (uint)stubData.Length); // alloc_hint
        WriteUInt16(ms, contextId);
        WriteUInt16(ms, opnum);

        // Stub data
        ms.Write(stubData);

        // Patch header
        byte[] pdu = ms.ToArray();
        var header = new RpcPduHeader
        {
            VersionMajor = RpcConstants.VersionMajor,
            VersionMinor = RpcConstants.VersionMinor,
            PacketType = RpcConstants.PTypeRequest,
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
    /// Builds a REQUEST PDU with signing/sealing if session security is available.
    /// For authenticated requests, the stub data is padded, signed/sealed, and an auth verifier is appended.
    /// </summary>
    private byte[] BuildAuthenticatedRequestPdu(
        uint callId, ushort contextId, ushort opnum, byte[] stubData, byte flags)
    {
        if (_sessionSecurity is null || !IsAuthenticated)
        {
            // No authentication — use plain request
            return BuildRequestPdu(callId, contextId, opnum, stubData, flags);
        }

        using var ms = new MemoryStream();

        // Reserve header (16 bytes)
        ms.Write(new byte[RpcPduHeader.HeaderSize]);

        // Request body: allocHint (4), contextId (2), opnum (2)
        WriteUInt32(ms, (uint)stubData.Length); // alloc_hint (original stub size)
        WriteUInt16(ms, contextId);
        WriteUInt16(ms, opnum);

        // Calculate auth padding to align stub data to 16-byte boundary
        int stubDataStart = (int)ms.Position;
        int authPadLength = (16 - (stubData.Length % 16)) % 16;

        // Build the data that will be signed/sealed: stub_data + auth_pad
        byte[] dataToProtect = new byte[stubData.Length + authPadLength];
        stubData.CopyTo(dataToProtect, 0);
        // auth padding bytes are zeros (already default)

        byte[] protectedData;
        byte[] signature;

        if (AuthLevel == RpcConstants.AuthLevelPrivacy)
        {
            // Seal: encrypt the stub data + padding, and produce signature
            (protectedData, signature) = _sessionSecurity.Seal(dataToProtect);
        }
        else
        {
            // Sign only: stub data is plaintext, signature is computed
            protectedData = dataToProtect;
            signature = _sessionSecurity.Sign(dataToProtect);
        }

        // Write the protected (possibly encrypted) stub data + padding
        ms.Write(protectedData);

        // Auth verifier header (8 bytes) + NTLM signature (16 bytes)
        ushort authLength = (ushort)signature.Length;
        ms.WriteByte(RpcConstants.AuthTypeSpnego);   // auth_type
        ms.WriteByte(AuthLevel);                      // auth_level
        ms.WriteByte((byte)authPadLength);            // auth_pad_length
        ms.WriteByte(0);                              // reserved
        WriteUInt32(ms, 0);                           // auth_context_id
        ms.Write(signature);

        // Patch header
        byte[] pdu = ms.ToArray();
        var header = new RpcPduHeader
        {
            VersionMajor = RpcConstants.VersionMajor,
            VersionMinor = RpcConstants.VersionMinor,
            PacketType = RpcConstants.PTypeRequest,
            PacketFlags = flags,
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
