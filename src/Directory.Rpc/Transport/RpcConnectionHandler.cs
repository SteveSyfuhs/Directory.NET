using System.Buffers.Binary;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Directory.Rpc.Dispatch;
using Directory.Rpc.Protocol;
using Directory.Security;
using Microsoft.Extensions.Logging;

namespace Directory.Rpc.Transport;

/// <summary>
/// Handles a single RPC client connection, processing PDUs in a loop until
/// the connection is closed. Manages bind negotiation, NTLM authentication,
/// and request dispatch.
/// </summary>
public class RpcConnectionHandler
{
    private readonly Socket _socket;
    private readonly RpcInterfaceDispatcher _dispatcher;
    private readonly RpcServerOptions _options;
    private readonly ILogger _logger;
    private readonly NtlmAuthenticator _ntlmAuth;
    private readonly RpcConnectionState _state;

    public RpcConnectionHandler(
        Socket socket,
        RpcInterfaceDispatcher dispatcher,
        RpcServerOptions options,
        ILogger logger,
        NtlmAuthenticator ntlmAuth)
    {
        _socket = socket;
        _dispatcher = dispatcher;
        _options = options;
        _logger = logger;
        _ntlmAuth = ntlmAuth;

        _state = new RpcConnectionState
        {
            RemoteEndPoint = socket.RemoteEndPoint?.ToString() ?? "unknown",
            AssocGroupId = (uint)Random.Shared.Next(1, int.MaxValue),
        };
    }

    public async Task ProcessAsync(CancellationToken ct)
    {
        _logger.LogDebug("RPC connection from {RemoteEndPoint}", _state.RemoteEndPoint);

        await using var stream = new NetworkStream(_socket, ownsSocket: true);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                // Read the 16-byte PDU header
                var headerBuf = new byte[RpcPduHeader.HeaderSize];
                if (!await ReadExactAsync(stream, headerBuf, ct))
                {
                    break; // Connection closed
                }

                var header = RpcPduHeader.Read(headerBuf);

                if (header.VersionMajor != RpcConstants.VersionMajor ||
                    header.VersionMinor != RpcConstants.VersionMinor)
                {
                    _logger.LogWarning("Unsupported RPC version {Major}.{Minor}", header.VersionMajor, header.VersionMinor);
                    break;
                }

                // Read the rest of the PDU
                int remainingBytes = header.FragLength - RpcPduHeader.HeaderSize;
                if (remainingBytes < 0)
                {
                    _logger.LogWarning("Invalid fragment length {FragLength}", header.FragLength);
                    break;
                }

                var pdu = new byte[header.FragLength];
                headerBuf.CopyTo(pdu.AsSpan());

                if (remainingBytes > 0 && !await ReadExactAsync(stream, pdu.AsMemory(RpcPduHeader.HeaderSize, remainingBytes), ct))
                {
                    break; // Connection closed mid-PDU
                }

                // Dispatch based on PDU type
                byte[][] responseFragments = header.PacketType switch
                {
                    RpcConstants.PTypeBind => WrapSingle(await HandleBindAsync(pdu, header)),
                    RpcConstants.PTypeAlterContext => WrapSingle(await HandleAlterContextAsync(pdu, header)),
                    RpcConstants.PTypeAuth3 => WrapSingle(await HandleAuth3Async(pdu, header)),
                    RpcConstants.PTypeRequest => await HandleRequestAsync(pdu, header, ct),
                    _ => WrapSingle(HandleUnknown(header)),
                };

                if (responseFragments != null)
                {
                    foreach (var fragment in responseFragments)
                    {
                        await stream.WriteAsync(fragment, ct);
                    }

                    await stream.FlushAsync(ct);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { } // Client disconnected
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing RPC connection from {RemoteEndPoint}", _state.RemoteEndPoint);
        }

        _logger.LogDebug("RPC connection closed: {RemoteEndPoint}", _state.RemoteEndPoint);
    }

    private async Task<byte[]> HandleBindAsync(byte[] pdu, RpcPduHeader header)
    {
        var bindData = PduParser.ParseBind(pdu);

        _state.MaxXmitFrag = Math.Min(bindData.MaxXmitFrag, (ushort)4280);
        _state.MaxRecvFrag = Math.Min(bindData.MaxRecvFrag, (ushort)4280);

        var results = NegotiateContexts(bindData.Contexts);

        // Check for auth verifier (NTLM negotiate)
        byte[] authResponseData = null;
        var authVerifier = PduParser.ParseAuthVerifier(pdu, header.AuthLength);
        if (authVerifier != null)
        {
            authResponseData = await ProcessAuthBindAsync(authVerifier);
        }

        _logger.LogDebug(
            "Bind from {Remote}: {ContextCount} contexts, {Accepted} accepted",
            _state.RemoteEndPoint,
            bindData.Contexts.Length,
            results.Count(r => r.Result == RpcConstants.BindResultAcceptance));

        return PduBuilder.BuildBindAck(
            header.CallId,
            _state.MaxXmitFrag,
            _state.MaxRecvFrag,
            _state.AssocGroupId,
            results,
            (ushort)_options.ServicePort,
            authResponseData);
    }

    private async Task<byte[]> HandleAlterContextAsync(byte[] pdu, RpcPduHeader header)
    {
        var bindData = PduParser.ParseAlterContext(pdu);
        var results = NegotiateContexts(bindData.Contexts);

        byte[] authResponseData = null;
        var authVerifier = PduParser.ParseAuthVerifier(pdu, header.AuthLength);
        if (authVerifier != null)
        {
            authResponseData = await ProcessAuthBindAsync(authVerifier);
        }

        _logger.LogDebug("AlterContext from {Remote}: {ContextCount} contexts", _state.RemoteEndPoint, bindData.Contexts.Length);

        return PduBuilder.BuildAlterContextResp(header.CallId, results, authResponseData);
    }

    private async Task<byte[]> HandleAuth3Async(byte[] pdu, RpcPduHeader header)
    {
        var authVerifier = PduParser.ParseAuth3(pdu, header.AuthLength);
        if (authVerifier != null)
        {
            await ProcessAuthType3Async(authVerifier);
        }

        // Auth3 has no response PDU
        return null;
    }

    private async Task<byte[][]> HandleRequestAsync(byte[] pdu, RpcPduHeader header, CancellationToken ct)
    {
        var requestData = PduParser.ParseRequest(pdu, header.AuthLength);
        byte flags = header.PacketFlags;
        bool isFirst = (flags & RpcConstants.PfcFirstFrag) != 0;
        bool isLast = (flags & RpcConstants.PfcLastFrag) != 0;

        if (isFirst && isLast)
        {
            // Single-fragment request — process immediately
            return await DispatchRequestAsync(
                header.CallId, requestData.ContextId, requestData.Opnum,
                requestData.StubData.ToArray(), ct);
        }

        if (isFirst)
        {
            // First fragment of a multi-fragment request — start accumulating
            var buffer = new FragmentReassemblyBuffer
            {
                Opnum = requestData.Opnum,
                ContextId = requestData.ContextId,
            };
            buffer.StubData.Write(requestData.StubData.Span);
            _state.FragmentBuffers[header.CallId] = buffer;

            _logger.LogDebug(
                "Request fragment (first): callId={CallId} opnum={Opnum} stubLen={StubLen}",
                header.CallId, requestData.Opnum, requestData.StubData.Length);

            return null; // No response yet
        }

        // Middle or last fragment — must have a buffer already
        if (!_state.FragmentBuffers.TryGetValue(header.CallId, out var reassembly))
        {
            _logger.LogWarning("Received continuation fragment for unknown callId {CallId}", header.CallId);
            return new[] { PduBuilder.BuildFault(header.CallId, RpcConstants.NcaProtocolError) };
        }

        reassembly.StubData.Write(requestData.StubData.Span);

        if (!isLast)
        {
            _logger.LogDebug(
                "Request fragment (middle): callId={CallId} accumulated={AccLen}",
                header.CallId, reassembly.StubData.Length);

            return null; // Still accumulating
        }

        // Last fragment — reassemble and dispatch
        _state.FragmentBuffers.Remove(header.CallId);
        var completeStub = reassembly.StubData.ToArray();

        _logger.LogDebug(
            "Request fragment (last): callId={CallId} opnum={Opnum} totalStubLen={StubLen}",
            header.CallId, reassembly.Opnum, completeStub.Length);

        return await DispatchRequestAsync(
            header.CallId, reassembly.ContextId, reassembly.Opnum, completeStub, ct);
    }

    private async Task<byte[][]> DispatchRequestAsync(
        uint callId, ushort contextId, ushort opnum, byte[] stubData, CancellationToken ct)
    {
        // Look up the bound interface for this context ID
        if (!_state.BoundContexts.TryGetValue(contextId, out var boundInterface))
        {
            _logger.LogWarning("Request for unbound context {ContextId}", contextId);
            return new[] { PduBuilder.BuildFault(callId, RpcConstants.NcaProtocolError) };
        }

        // Get the handler for this interface
        var handler = _dispatcher.GetHandler(boundInterface.InterfaceId);
        if (handler == null)
        {
            _logger.LogWarning("No handler for interface {InterfaceId}", boundInterface.InterfaceId);
            return new[] { PduBuilder.BuildFault(callId, RpcConstants.NcaUnspecifiedReject) };
        }

        // Build per-call context
        var callContext = new RpcCallContext
        {
            AuthenticatedSid = _state.AuthenticatedSid,
            AuthenticatedUser = _state.AuthenticatedUser,
            TenantId = _options.TenantId,
            DomainDn = _options.DomainDn,
            ContextHandles = _state.ContextHandles,
            SessionKey = _state.SessionKey,
        };

        try
        {
            _logger.LogDebug(
                "Request: interface={InterfaceId} opnum={Opnum} stubLen={StubLen}",
                boundInterface.InterfaceId, opnum, stubData.Length);

            var responseStub = await handler.HandleRequestAsync(
                opnum,
                stubData,
                callContext,
                ct);

            return BuildFragmentedResponse(callId, contextId, responseStub);
        }
        catch (RpcFaultException ex)
        {
            _logger.LogWarning("RPC fault on opnum {Opnum}: 0x{StatusCode:X8}", opnum, ex.StatusCode);
            return new[] { PduBuilder.BuildFault(callId, ex.StatusCode) };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception in opnum {Opnum} for interface {InterfaceId}",
                opnum, boundInterface.InterfaceId);
            return new[] { PduBuilder.BuildFault(callId, RpcConstants.NcaUnspecifiedReject) };
        }
    }

    /// <summary>
    /// Builds one or more Response PDU fragments. If the response stub data fits
    /// within a single MaxXmitFrag PDU, returns a single fragment. Otherwise, splits
    /// the stub data across multiple fragments with correct PFC_FIRST_FRAG/PFC_LAST_FRAG flags.
    /// </summary>
    private byte[][] BuildFragmentedResponse(uint callId, ushort contextId, byte[] responseStub)
    {
        // Response PDU overhead: 16 (header) + 8 (response fields: allocHint + contextId + cancelCount + reserved)
        const int responseHeaderOverhead = RpcPduHeader.HeaderSize + 8;
        int maxStubPerFragment = _state.MaxXmitFrag - responseHeaderOverhead;

        if (maxStubPerFragment <= 0)
        {
            maxStubPerFragment = 1; // Degenerate case safety
        }

        if (responseStub.Length <= maxStubPerFragment)
        {
            // Fits in a single fragment
            return new[] { PduBuilder.BuildResponse(callId, contextId, responseStub, authData: null) };
        }

        // Need to split into multiple fragments
        var fragments = new List<byte[]>();
        int offset = 0;

        while (offset < responseStub.Length)
        {
            int chunkSize = Math.Min(maxStubPerFragment, responseStub.Length - offset);
            bool isFirst = (offset == 0);
            bool isLast = (offset + chunkSize >= responseStub.Length);

            byte flags = 0;
            if (isFirst) flags |= RpcConstants.PfcFirstFrag;
            if (isLast) flags |= RpcConstants.PfcLastFrag;

            var chunk = new byte[chunkSize];
            Array.Copy(responseStub, offset, chunk, 0, chunkSize);

            fragments.Add(PduBuilder.BuildResponseFragment(callId, contextId, chunk, flags, (uint)responseStub.Length));
            offset += chunkSize;
        }

        return fragments.ToArray();
    }

    private static byte[][] WrapSingle(byte[] response)
    {
        return response != null ? new[] { response } : null;
    }

    private byte[] HandleUnknown(RpcPduHeader header)
    {
        _logger.LogWarning("Unknown PDU type {Type} from {Remote}", header.PacketType, _state.RemoteEndPoint);
        return PduBuilder.BuildFault(header.CallId, RpcConstants.NcaProtocolError);
    }

    private BindContextResult[] NegotiateContexts(PresentationContext[] contexts)
    {
        var results = new BindContextResult[contexts.Length];

        for (int i = 0; i < contexts.Length; i++)
        {
            var ctx = contexts[i];
            var abstractId = ctx.AbstractSyntax.Uuid;
            ushort majorVersion = (ushort)(ctx.AbstractSyntax.Version & 0xFFFF);

            // Check if we support this interface
            if (_dispatcher.SupportsInterface(abstractId, majorVersion))
            {
                // Find a supported transfer syntax (we only support NDR 2.0)
                RpcSyntaxId matchedTransfer = null;
                foreach (var ts in ctx.TransferSyntaxes)
                {
                    if (ts.Uuid == RpcConstants.NdrSyntaxId && (ts.Version & 0xFFFF) == RpcConstants.NdrSyntaxVersion)
                    {
                        matchedTransfer = ts;
                        break;
                    }
                }

                if (matchedTransfer != null)
                {
                    // Accepted
                    _state.BoundContexts[ctx.ContextId] = new BoundInterface(abstractId, majorVersion);
                    results[i] = new BindContextResult(
                        RpcConstants.BindResultAcceptance,
                        RpcConstants.BindReasonNotSpecified,
                        matchedTransfer);
                }
                else
                {
                    // Transfer syntax not supported
                    results[i] = new BindContextResult(
                        RpcConstants.BindResultProviderRejection,
                        RpcConstants.BindReasonTransferSyntaxNotSupported,
                        new RpcSyntaxId(Guid.Empty, 0));
                }
            }
            else
            {
                // Abstract syntax (interface) not supported
                results[i] = new BindContextResult(
                    RpcConstants.BindResultProviderRejection,
                    RpcConstants.BindReasonAbstractSyntaxNotSupported,
                    new RpcSyntaxId(Guid.Empty, 0));
            }
        }

        return results;
    }

    /// <summary>
    /// Processes auth data from a Bind or AlterContext PDU.
    /// For NTLM Type 1 (Negotiate), generates a Type 2 (Challenge) response.
    /// </summary>
    private async Task<byte[]> ProcessAuthBindAsync(AuthVerifier auth)
    {
        if (auth.AuthType == RpcConstants.AuthTypeNtlm)
        {
            var credentials = auth.Credentials.Span;

            // Check if this is an NTLM Type 1 (Negotiate) message
            // NTLMSSP signature followed by type indicator
            if (credentials.Length >= 12 && IsNtlmNegotiate(credentials))
            {
                // Generate NTLM Type 2 (Challenge) message
                var challenge = _ntlmAuth.GenerateChallenge();
                _state.NtlmChallenge = challenge;
                _state.AuthState = RpcAuthState.NtlmNegotiating;

                return BuildNtlmChallengeMessage(challenge);
            }

            // Check if this is an NTLM Type 3 (Authenticate) message
            if (credentials.Length >= 12 && IsNtlmAuthenticate(credentials))
            {
                await ProcessNtlmType3Async(credentials.ToArray());
                return null; // No auth data in response for Type 3 in alter context
            }
        }

        return null;
    }

    /// <summary>
    /// Processes an NTLM Type 3 (Authenticate) message from an Auth3 PDU.
    /// </summary>
    private async Task ProcessAuthType3Async(AuthVerifier auth)
    {
        if (auth.AuthType == RpcConstants.AuthTypeNtlm)
        {
            var credentials = auth.Credentials.Span;
            if (credentials.Length >= 12 && IsNtlmAuthenticate(credentials))
            {
                await ProcessNtlmType3Async(credentials.ToArray());
            }
        }
    }

    private async Task ProcessNtlmType3Async(ReadOnlyMemory<byte> ntlmMessageMem)
    {
        // Parse NTLM Type 3 message to extract username, domain, and NTLMv2 response
        // Layout: "NTLMSSP\0" (8) + type (4) + field descriptors
        try
        {
            var ntlmMessage = ntlmMessageMem.Span;

            if (ntlmMessage.Length < 72)
            {
                _logger.LogWarning("NTLM Type 3 message too short");
                return;
            }

            // LM response: length at bytes 12-13, offset at bytes 16-19
            ushort lmLen = BinaryPrimitives.ReadUInt16LittleEndian(ntlmMessage.Slice(12));
            uint lmOffset = BinaryPrimitives.ReadUInt32LittleEndian(ntlmMessage.Slice(16));

            // NT response: length at bytes 20-21, offset at bytes 24-27
            ushort ntLen = BinaryPrimitives.ReadUInt16LittleEndian(ntlmMessage.Slice(20));
            uint ntOffset = BinaryPrimitives.ReadUInt32LittleEndian(ntlmMessage.Slice(24));

            // Domain name: length at bytes 28-29, offset at bytes 32-35
            ushort domainLen = BinaryPrimitives.ReadUInt16LittleEndian(ntlmMessage.Slice(28));
            uint domainOffset = BinaryPrimitives.ReadUInt32LittleEndian(ntlmMessage.Slice(32));

            // User name: length at bytes 36-37, offset at bytes 40-43
            ushort userLen = BinaryPrimitives.ReadUInt16LittleEndian(ntlmMessage.Slice(36));
            uint userOffset = BinaryPrimitives.ReadUInt32LittleEndian(ntlmMessage.Slice(40));

            string username = "";
            if (userLen > 0 && userOffset + userLen <= ntlmMessage.Length)
            {
                username = Encoding.Unicode.GetString(ntlmMessage.Slice((int)userOffset, userLen));
            }

            string domain = "";
            if (domainLen > 0 && domainOffset + domainLen <= ntlmMessage.Length)
            {
                domain = Encoding.Unicode.GetString(ntlmMessage.Slice((int)domainOffset, domainLen));
            }

            // Extract NT response
            byte[] ntResponse = Array.Empty<byte>();
            if (ntLen > 0 && ntOffset + ntLen <= ntlmMessage.Length)
            {
                ntResponse = ntlmMessage.Slice((int)ntOffset, ntLen).ToArray();
            }

            // Validate the NTLMv2 response
            if (ntResponse.Length < 16 || _state.NtlmChallenge is null)
            {
                _logger.LogWarning(
                    "NTLM Type 3 validation failed for {Domain}\\{User}: missing NT response or server challenge",
                    domain, username);
                _state.AuthState = RpcAuthState.Anonymous;
                return;
            }

            // NTLMv2 response structure: first 16 bytes = NTProofStr (HMAC), rest = client blob
            var clientBlob = ntResponse[16..];

            bool valid = await _ntlmAuth.ValidateNtlmv2ResponseAsync(
                _options.TenantId,
                _options.DomainDn,
                username,
                _state.NtlmChallenge,
                ntResponse,
                clientBlob);

            if (!valid)
            {
                _logger.LogWarning(
                    "NTLM authentication failed for {Domain}\\{User} from {Remote}: NTLMv2 validation rejected",
                    domain, username, _state.RemoteEndPoint);
                _state.AuthState = RpcAuthState.Anonymous;
                return;
            }

            // Compute session key for signing/sealing:
            // SessionBaseKey = HMAC_MD5(NTLMv2Hash, NTProofStr)
            // We recompute NTLMv2Hash here for the session key derivation.
            var ntProofStr = ntResponse[..16];
            var userNtHash = await GetUserNtHashAsync(username);
            if (userNtHash is not null)
            {
                var identityBytes = Encoding.Unicode.GetBytes(username.ToUpperInvariant() + domain);
                byte[] ntlmv2Hash;
                using (var hmac = new System.Security.Cryptography.HMACMD5(userNtHash))
                {
                    ntlmv2Hash = hmac.ComputeHash(identityBytes);
                }
                using (var hmac = new System.Security.Cryptography.HMACMD5(ntlmv2Hash))
                {
                    _state.SessionKey = hmac.ComputeHash(ntProofStr);
                }
            }

            _state.AuthenticatedUser = username;
            _state.AuthState = RpcAuthState.Authenticated;

            _logger.LogInformation(
                "NTLM authentication succeeded for {Domain}\\{User} from {Remote}",
                domain, username, _state.RemoteEndPoint);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse or validate NTLM Type 3 message");
            _state.AuthState = RpcAuthState.Anonymous;
        }
    }

    /// <summary>
    /// Retrieves the NT hash for a user from the directory store via the authenticator.
    /// Returns null if the user is not found.
    /// </summary>
    private async Task<byte[]> GetUserNtHashAsync(string username)
    {
        try
        {
            var user = await _ntlmAuth.GetUserNtHashAsync(_options.TenantId, _options.DomainDn, username);
            return user;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Builds an NTLM Type 2 (Challenge) message with the given 8-byte server challenge.
    /// Includes proper TargetInfo AV_PAIR blocks per MS-NLMP section 2.2.1.2.
    /// </summary>
    private byte[] BuildNtlmChallengeMessage(byte[] challenge)
    {
        // Build TargetInfo AV_PAIR structure first so we know its length
        var targetInfo = BuildTargetInfo();

        using var ms = new MemoryStream();

        // Signature: "NTLMSSP\0"
        ms.Write("NTLMSSP\0"u8);

        // Type: 2 (Challenge)
        Span<byte> buf4 = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buf4, 2);
        ms.Write(buf4);

        // Target name = NetBIOS domain name
        string targetName = _options.NetBiosDomainName;
        byte[] targetNameBytes = Encoding.Unicode.GetBytes(targetName);

        // TargetNameLen + TargetNameMaxLen
        Span<byte> buf2 = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(buf2, (ushort)targetNameBytes.Length);
        ms.Write(buf2); // Len
        ms.Write(buf2); // MaxLen

        // TargetNameOffset — the payload area starts after the fixed 56-byte header
        uint payloadOffset = 56;
        BinaryPrimitives.WriteUInt32LittleEndian(buf4, payloadOffset);
        ms.Write(buf4);

        // Negotiate flags per MS-NLMP:
        // NTLMSSP_NEGOTIATE_UNICODE (0x01) | NTLMSSP_NEGOTIATE_NTLM (0x200) |
        // NTLMSSP_TARGET_TYPE_DOMAIN (0x10000) | NTLMSSP_NEGOTIATE_TARGET_INFO (0x800000) |
        // NTLMSSP_NEGOTIATE_EXTENDED_SESSIONSECURITY (0x80000) | NTLMSSP_NEGOTIATE_ALWAYS_SIGN (0x8000)
        uint flags = 0x00898233;
        BinaryPrimitives.WriteUInt32LittleEndian(buf4, flags);
        ms.Write(buf4);

        // Server challenge (8 bytes)
        ms.Write(challenge);

        // Reserved (8 bytes)
        ms.Write(new byte[8]);

        // TargetInfoLen + TargetInfoMaxLen
        BinaryPrimitives.WriteUInt16LittleEndian(buf2, (ushort)targetInfo.Length);
        ms.Write(buf2); // Len
        ms.Write(buf2); // MaxLen

        // TargetInfoOffset
        uint targetInfoOffset = payloadOffset + (uint)targetNameBytes.Length;
        BinaryPrimitives.WriteUInt32LittleEndian(buf4, targetInfoOffset);
        ms.Write(buf4);

        // Payload: Target name
        ms.Write(targetNameBytes);

        // Payload: TargetInfo AV_PAIRs
        ms.Write(targetInfo);

        return ms.ToArray();
    }

    /// <summary>
    /// Builds the TargetInfo AV_PAIR sequence per MS-NLMP section 2.2.2.1.
    /// Contains domain name, server name, DNS domain, DNS server FQDN, and timestamp.
    /// </summary>
    private byte[] BuildTargetInfo()
    {
        using var ms = new MemoryStream();

        // MsvAvNbDomainName (type 2) — NetBIOS domain name
        WriteAvPair(ms, 0x0002, Encoding.Unicode.GetBytes(_options.NetBiosDomainName));

        // MsvAvNbComputerName (type 1) — NetBIOS computer name
        WriteAvPair(ms, 0x0001, Encoding.Unicode.GetBytes(_options.ServerName));

        // MsvAvDnsDomainName (type 4) — DNS domain name
        WriteAvPair(ms, 0x0004, Encoding.Unicode.GetBytes(_options.DnsDomainName));

        // MsvAvDnsComputerName (type 3) — DNS server FQDN
        WriteAvPair(ms, 0x0003, Encoding.Unicode.GetBytes(_options.ServerFqdn));

        // MsvAvTimestamp (type 7) — FILETIME (8 bytes, 100-nanosecond intervals since Jan 1 1601)
        var timestamp = new byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(timestamp, DateTime.UtcNow.ToFileTimeUtc());
        WriteAvPair(ms, 0x0007, timestamp);

        // MsvAvEOL (type 0) — terminator
        WriteAvPair(ms, 0x0000, Array.Empty<byte>());

        return ms.ToArray();
    }

    private static void WriteAvPair(MemoryStream ms, ushort avId, byte[] avValue)
    {
        Span<byte> buf2 = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(buf2, avId);
        ms.Write(buf2);
        BinaryPrimitives.WriteUInt16LittleEndian(buf2, (ushort)avValue.Length);
        ms.Write(buf2);
        if (avValue.Length > 0)
            ms.Write(avValue);
    }

    private static bool IsNtlmNegotiate(ReadOnlySpan<byte> data)
    {
        // "NTLMSSP\0" followed by type 1
        return data.Length >= 12 &&
               data[0] == 'N' && data[1] == 'T' && data[2] == 'L' && data[3] == 'M' &&
               data[4] == 'S' && data[5] == 'S' && data[6] == 'P' && data[7] == 0 &&
               BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(8)) == 1;
    }

    private static bool IsNtlmAuthenticate(ReadOnlySpan<byte> data)
    {
        // "NTLMSSP\0" followed by type 3
        return data.Length >= 12 &&
               data[0] == 'N' && data[1] == 'T' && data[2] == 'L' && data[3] == 'M' &&
               data[4] == 'S' && data[5] == 'S' && data[6] == 'P' && data[7] == 0 &&
               BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(8)) == 3;
    }

    /// <summary>
    /// Reads exactly the requested number of bytes from the stream, handling partial reads.
    /// Returns false if the connection was closed before all bytes were read.
    /// </summary>
    private static async Task<bool> ReadExactAsync(NetworkStream stream, Memory<byte> buffer, CancellationToken ct)
    {
        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.Slice(totalRead), ct);
            if (read == 0)
                return false; // Connection closed
            totalRead += read;
        }

        return true;
    }
}
