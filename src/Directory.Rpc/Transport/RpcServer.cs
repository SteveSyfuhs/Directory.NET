using System.Net;
using System.Net.Sockets;
using Directory.Rpc.Dispatch;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Directory.Rpc.Transport;

/// <summary>
/// Hosted service that runs the DCE/RPC TCP transport.
/// Listens on two ports:
///   - EndpointMapperPort (default 1135): handles EPM ept_map queries
///   - ServicePort (default 49664): handles actual RPC interface traffic (SAMR, LSA, NRPC, etc.)
/// </summary>
public class RpcServer : IHostedService, IDisposable
{
    private readonly RpcServerOptions _options;
    private readonly RpcInterfaceDispatcher _dispatcher;
    private readonly IServiceProvider _services;
    private readonly ILogger<RpcServer> _logger;

    private TcpListener _epmListener;
    private TcpListener _serviceListener;
    private CancellationTokenSource _cts;
    private Task _epmAcceptTask;
    private Task _serviceAcceptTask;
    private readonly SemaphoreSlim _connectionSemaphore;
    private int _activeConnections;

    public RpcServer(
        IOptions<RpcServerOptions> options,
        RpcInterfaceDispatcher dispatcher,
        IServiceProvider services,
        ILogger<RpcServer> logger)
    {
        _options = options.Value;
        _dispatcher = dispatcher;
        _services = services;
        _logger = logger;
        _connectionSemaphore = new SemaphoreSlim(_options.MaxConnections, _options.MaxConnections);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Start the endpoint mapper listener (port 135)
        _epmListener = new TcpListener(IPAddress.Any, _options.EndpointMapperPort);
        _epmListener.Start();
        _epmAcceptTask = AcceptConnectionsAsync(_epmListener, "EPM", _cts.Token);

        // Start the service port listener
        _serviceListener = new TcpListener(IPAddress.Any, _options.ServicePort);
        _serviceListener.Start();
        _serviceAcceptTask = AcceptConnectionsAsync(_serviceListener, "Service", _cts.Token);

        _logger.LogInformation(
            "RPC server started: EPM on port {EpmPort}, Service on port {ServicePort}",
            _options.EndpointMapperPort, _options.ServicePort);

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("RPC server stopping...");

        _cts?.Cancel();

        _epmListener?.Stop();
        _serviceListener?.Stop();

        var tasks = new List<Task>();
        if (_epmAcceptTask != null) tasks.Add(_epmAcceptTask);
        if (_serviceAcceptTask != null) tasks.Add(_serviceAcceptTask);

        if (tasks.Count > 0)
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        _logger.LogInformation("RPC server stopped. Active connections: {Count}", _activeConnections);
    }

    private async Task AcceptConnectionsAsync(TcpListener listener, string listenerName, CancellationToken ct)
    {
        _logger.LogDebug("RPC {Listener} accept loop started on {Endpoint}", listenerName, listener.LocalEndpoint);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                Socket socket;
                try
                {
                    socket = await listener.AcceptSocketAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (SocketException ex) when (ct.IsCancellationRequested)
                {
                    _logger.LogDebug("Accept cancelled on {Listener}: {Message}", listenerName, ex.Message);
                    break;
                }

                // Enforce max connections
                if (!await _connectionSemaphore.WaitAsync(0, ct))
                {
                    _logger.LogWarning("Max connections ({Max}) reached, rejecting connection from {Remote}",
                        _options.MaxConnections, socket.RemoteEndPoint);
                    socket.Dispose();
                    continue;
                }

                Interlocked.Increment(ref _activeConnections);

                // Fire and forget the connection handler
                _ = HandleConnectionAsync(socket, ct).ContinueWith(_ =>
                {
                    Interlocked.Decrement(ref _activeConnections);
                    _connectionSemaphore.Release();
                }, TaskScheduler.Default);
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "Fatal error in {Listener} accept loop", listenerName);
        }

        _logger.LogDebug("RPC {Listener} accept loop ended", listenerName);
    }

    private async Task HandleConnectionAsync(Socket socket, CancellationToken ct)
    {
        try
        {
            socket.NoDelay = true;

            var handler = new RpcConnectionHandler(
                socket,
                _dispatcher,
                _options,
                _logger);

            await handler.ProcessAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error in connection handler for {Remote}", socket.RemoteEndPoint);
            try { socket.Dispose(); } catch { }
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _epmListener?.Stop();
        _serviceListener?.Stop();
        _connectionSemaphore.Dispose();
        GC.SuppressFinalize(this);
    }
}
