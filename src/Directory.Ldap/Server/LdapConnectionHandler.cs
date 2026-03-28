using System.Buffers;
using System.IO.Pipelines;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Directory.Ldap.Handlers;
using Directory.Ldap.Protocol;
using Microsoft.Extensions.Logging;

namespace Directory.Ldap.Server;

/// <summary>
/// Manages a single LDAP connection using System.IO.Pipelines for high-performance
/// BER message framing and dispatch to operation handlers.
/// </summary>
public class LdapConnectionHandler : ILdapResponseWriter, IStartTlsUpgradeable
{
    private readonly Socket _socket;
    private readonly NetworkStream _stream;
    private readonly ILdapOperationDispatcher _dispatcher;
    private readonly LdapServerOptions _options;
    private readonly ILogger _logger;
    private readonly LdapConnectionState _state;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private Stream _activeStream;

    // Signals that a StartTLS upgrade has been completed and the pipe loop should restart.
    private TaskCompletionSource<bool> _tlsUpgradeTcs;
    // Cancels the current FillPipeAsync read when a StartTLS upgrade is requested
    private CancellationTokenSource _fillPipeCts;

    public LdapConnectionState State => _state;

    /// <summary>
    /// When true, this connection serves Global Catalog requests.
    /// Search results will be filtered to only include GC-replicated attributes.
    /// </summary>
    public bool IsGlobalCatalog { get; set; }

    /// <summary>
    /// Set of attribute names replicated to the Global Catalog.
    /// Only populated when <see cref="IsGlobalCatalog"/> is true.
    /// </summary>
    public HashSet<string> GcAttributes { get; set; }

    public LdapConnectionHandler(
        Socket socket,
        ILdapOperationDispatcher dispatcher,
        LdapServerOptions options,
        ILogger logger,
        bool isTls = false)
    {
        _socket = socket;
        _stream = new NetworkStream(socket, ownsSocket: true);
        _activeStream = _stream;
        _dispatcher = dispatcher;
        _options = options;
        _logger = logger;
        _state = new LdapConnectionState
        {
            RemoteEndPoint = socket.RemoteEndPoint?.ToString() ?? "unknown",
            IsTls = isTls,
            TenantId = options.DefaultTenantId,
        };
    }

    /// <summary>
    /// Entry point used by GlobalCatalogServer: sets TLS state and runs the connection loop.
    /// </summary>
    public Task ProcessAsync(bool isTls, CancellationToken ct)
    {
        _state.IsTls = isTls;
        _state.IsGlobalCatalog = IsGlobalCatalog;
        return RunAsync(ct);
    }

    /// <summary>
    /// Run the connection read loop, framing BER messages and dispatching them.
    /// Supports in-place restart after a StartTLS upgrade.
    /// </summary>
    public async Task RunAsync(CancellationToken ct)
    {
        _logger.LogDebug("LDAP connection from {Endpoint}", _state.RemoteEndPoint);

        bool continueLoop = true;
        while (continueLoop && !ct.IsCancellationRequested)
        {
            continueLoop = false; // will be set to true only if a TLS upgrade occurs

            var pipe = new Pipe(new PipeOptions(
                pauseWriterThreshold: 256 * 1024,
                resumeWriterThreshold: 128 * 1024,
                minimumSegmentSize: 4096,
                useSynchronizationContext: false));

            // Create a new TCS and cancellation source for this pipe iteration
            _tlsUpgradeTcs = null;
            _fillPipeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            var fillTask = FillPipeAsync(pipe.Writer, _fillPipeCts.Token, ct);
            var readTask = ReadPipeAsync(pipe.Reader, ct);

            await Task.WhenAny(fillTask, readTask);

            // If a TLS upgrade is in progress, wait for it to complete before deciding whether to restart
            if (_tlsUpgradeTcs != null)
            {
                var upgraded = await _tlsUpgradeTcs.Task;
                if (upgraded)
                {
                    // Also wait for ReadPipeAsync to drain its current message processing
                    await readTask;
                    _logger.LogDebug("Restarting pipe loop after StartTLS upgrade for {Endpoint}", _state.RemoteEndPoint);
                    continueLoop = true;
                }
            }
        }

        _logger.LogDebug("LDAP connection closed: {Endpoint}", _state.RemoteEndPoint);
    }

    /// <param name="writer">The pipe writer to write incoming data to.</param>
    /// <param name="fillCt">Cancellation token that is cancelled to interrupt an in-progress read (e.g. for StartTLS).</param>
    /// <param name="connectionCt">The connection-level cancellation token (used to detect connection close).</param>
    private async Task FillPipeAsync(PipeWriter writer, CancellationToken fillCt, CancellationToken connectionCt)
    {
        try
        {
            while (!connectionCt.IsCancellationRequested)
            {
                var memory = writer.GetMemory(4096);
                int bytesRead;
                try
                {
                    bytesRead = await _activeStream.ReadAsync(memory, fillCt);
                }
                catch (OperationCanceledException) when (fillCt.IsCancellationRequested && !connectionCt.IsCancellationRequested)
                {
                    // StartTLS upgrade requested — exit this fill loop so the pipe can restart
                    break;
                }

                if (bytesRead == 0)
                    break; // Connection closed

                writer.Advance(bytesRead);
                var result = await writer.FlushAsync(connectionCt);

                if (result.IsCompleted)
                    break;
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        catch (SocketException) { }
        finally
        {
            await writer.CompleteAsync();
        }
    }

    private async Task ReadPipeAsync(PipeReader reader, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var result = await reader.ReadAsync(ct);
                var buffer = result.Buffer;

                while (TryReadMessage(ref buffer, out var messageBytes))
                {
                    await ProcessMessageAsync(messageBytes, ct);
                }

                reader.AdvanceTo(buffer.Start, buffer.End);

                if (result.IsCompleted)
                    break;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading LDAP messages from {Endpoint}", _state.RemoteEndPoint);
        }
        finally
        {
            await reader.CompleteAsync();
        }
    }

    /// <summary>
    /// Try to read a complete BER TLV message from the buffer.
    /// </summary>
    private bool TryReadMessage(ref ReadOnlySequence<byte> buffer, out ReadOnlyMemory<byte> message)
    {
        message = default;

        if (buffer.Length < 2)
            return false;

        // Get the first segment as a contiguous span for TLV length reading
        Span<byte> header = stackalloc byte[Math.Min((int)buffer.Length, 16)];
        buffer.Slice(0, header.Length).CopyTo(header);

        var totalLength = BerHelper.TryReadTlvLength(header);

        if (totalLength < 0 || buffer.Length < totalLength)
            return false;

        if (totalLength > _options.MaxMessageSize)
        {
            _logger.LogWarning("Message too large ({Size} bytes) from {Endpoint}", totalLength, _state.RemoteEndPoint);
            buffer = buffer.Slice(buffer.GetPosition(totalLength));
            return false;
        }

        // Extract the complete message
        var messageSlice = buffer.Slice(0, totalLength);
        message = messageSlice.ToArray();
        buffer = buffer.Slice(buffer.GetPosition(totalLength));
        return true;
    }

    private async Task ProcessMessageAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        try
        {
            var message = LdapMessage.Decode(data);
            _logger.LogTrace("Received {Op} messageId={Id} from {Endpoint}",
                message.Operation, message.MessageId, _state.RemoteEndPoint);

            await _dispatcher.DispatchAsync(message, this, _state, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing LDAP message from {Endpoint}", _state.RemoteEndPoint);
        }
    }

    #region ILdapResponseWriter

    public async Task WriteMessageAsync(int messageId, byte[] protocolOpData, List<LdapControl> controls = null, CancellationToken ct = default)
    {
        var response = new LdapMessage
        {
            MessageId = messageId,
            ProtocolOpData = protocolOpData,
            Controls = controls,
        };

        await WriteBytesAsync(response.Encode(), ct);
    }

    public async Task WriteBytesAsync(byte[] data, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            await _activeStream.WriteAsync(data, ct);
            await _activeStream.FlushAsync(ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    #endregion

    /// <summary>
    /// Upgrade the connection to TLS in response to a StartTLS extended request.
    /// Signals FillPipeAsync to stop, performs the TLS handshake, then signals RunAsync
    /// to restart the pipe loop reading from the new SslStream.
    /// Returns true if the upgrade succeeded, false if no certificate is configured.
    /// </summary>
    public async Task<bool> UpgradeToTlsAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_options.CertificatePath))
        {
            _logger.LogWarning("StartTLS: no server certificate configured (Ldap:CertificatePath). Upgrade refused.");
            return false;
        }

        // Load the server certificate from configuration
        X509Certificate2 certificate = null;
        try
        {
            certificate = string.IsNullOrEmpty(_options.CertificatePassword)
                ? X509CertificateLoader.LoadPkcs12FromFile(_options.CertificatePath, password: null)
                : X509CertificateLoader.LoadPkcs12FromFile(_options.CertificatePath, _options.CertificatePassword);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load TLS certificate from {Path}", _options.CertificatePath);
            return false;
        }

        // Signal FillPipeAsync to stop reading so we can safely take over the underlying stream.
        // We cancel the fill-pipe token to interrupt any in-progress ReadAsync.
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _tlsUpgradeTcs = tcs;
        _fillPipeCts?.Cancel();

        // Yield to allow the cancellation to propagate and FillPipeAsync to exit
        await Task.Yield();

        try
        {
            // Wrap the underlying NetworkStream (not _activeStream which may already be something else)
            var sslStream = new SslStream(_stream, leaveInnerStreamOpen: true);

            await sslStream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificate = certificate,
                ClientCertificateRequired = false,
                EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13,
                RemoteCertificateValidationCallback = (_, cert, _, _) => true, // Accept any client cert; mapping is validated at EXTERNAL bind time
            }, ct);

            _activeStream = sslStream;
            _state.IsTls = true;

            // Capture the client certificate if one was presented during the TLS handshake
            if (sslStream.RemoteCertificate is X509Certificate remoteCert)
            {
                _state.ClientCertificate = new X509Certificate2(remoteCert);
            }

            _logger.LogDebug("StartTLS upgrade complete for {Endpoint}", _state.RemoteEndPoint);
            tcs.TrySetResult(true);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "StartTLS handshake failed for {Endpoint}", _state.RemoteEndPoint);
            tcs.TrySetResult(false);
            return false;
        }
    }
}
