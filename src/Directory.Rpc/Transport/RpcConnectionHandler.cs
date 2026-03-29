using System.Buffers.Binary;
using System.Net.Sockets;
using Directory.Rpc.Dispatch;
using Directory.Rpc.Protocol;
using Microsoft.Extensions.Logging;

namespace Directory.Rpc.Transport;

/// <summary>
/// Handles a single RPC client connection, processing PDUs in a loop until
/// the connection is closed. Manages bind negotiation and request dispatch.
/// NTLM authentication is rejected; Kerberos authentication is required.
/// </summary>
public class RpcConnectionHandler
{
    private readonly Socket _socket;
    private readonly RpcInterfaceDispatcher _dispatcher;
    private readonly RpcServerOptions _options;
    private readonly ILogger _logger;
    private readonly RpcConnectionState _state;

    public RpcConnectionHandler(
        Socket socket,
        RpcInterfaceDispatcher dispatcher,
        RpcServerOptions options,
        ILogger logger)
    {
        _socket = socket;
        _dispatcher = dispatcher;
        _options = options;
        _logger = logger;

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
                    RpcConstants.PTypeAuth3 => WrapSingle(HandleAuth3Rejected(pdu, header)),
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

        // Check for auth verifier — reject NTLM, Kerberos is required
        var authVerifier = PduParser.ParseAuthVerifier(pdu, header.AuthLength);
        if (authVerifier != null)
        {
            RejectNtlmAuth(authVerifier);
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
            authData: null);
    }

    private async Task<byte[]> HandleAlterContextAsync(byte[] pdu, RpcPduHeader header)
    {
        var bindData = PduParser.ParseAlterContext(pdu);
        var results = NegotiateContexts(bindData.Contexts);

        var authVerifier = PduParser.ParseAuthVerifier(pdu, header.AuthLength);
        if (authVerifier != null)
        {
            RejectNtlmAuth(authVerifier);
        }

        _logger.LogDebug("AlterContext from {Remote}: {ContextCount} contexts", _state.RemoteEndPoint, bindData.Contexts.Length);

        return PduBuilder.BuildAlterContextResp(header.CallId, results, authData: null);
    }

    private byte[] HandleAuth3Rejected(byte[] pdu, RpcPduHeader header)
    {
        _logger.LogWarning(
            "NTLM Auth3 rejected from {Remote} — Kerberos authentication is required. NTLM is not supported.",
            _state.RemoteEndPoint);

        // Auth3 has no response PDU
        return null;
    }

    /// <summary>
    /// Rejects NTLM authentication attempts and logs that Kerberos is required.
    /// </summary>
    private void RejectNtlmAuth(AuthVerifier auth)
    {
        if (auth.AuthType == RpcConstants.AuthTypeNtlm)
        {
            _logger.LogWarning(
                "NTLM authentication rejected from {Remote} — Kerberos authentication is required. NTLM is not supported.",
                _state.RemoteEndPoint);
            _state.AuthState = RpcAuthState.Anonymous;
        }
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
