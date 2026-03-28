using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Directory.Ldap.Handlers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Directory.Ldap.Server;

/// <summary>
/// LDAP server hosted service. Listens on port 389 (and optionally 636 for LDAPS).
/// For each accepted connection, spawns an LdapConnectionHandler.
/// Supports connection backpressure via a semaphore and tracks active connections.
/// </summary>
public class LdapServer : IHostedService, IDisposable
{
    private readonly ILdapOperationDispatcher _dispatcher;
    private readonly LdapServerOptions _options;
    private readonly LdapConnectionStats _stats;
    private readonly ILogger<LdapServer> _logger;
    private Socket _listener;
    private CancellationTokenSource _cts;
    private Task _acceptTask;
    private SemaphoreSlim _connectionSemaphore;

    private readonly ConcurrentDictionary<int, LdapConnectionHandler> _activeConnections = new();
    private int _connectionCount;

    public LdapServer(
        ILdapOperationDispatcher dispatcher,
        IOptions<LdapServerOptions> options,
        LdapConnectionStats stats,
        ILogger<LdapServer> logger)
    {
        _dispatcher = dispatcher;
        _options = options.Value;
        _stats = stats;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _connectionSemaphore = new SemaphoreSlim(_options.MaxConnections, _options.MaxConnections);

        _listener = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp);
        _listener.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, false);
        _listener.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _listener.Bind(new IPEndPoint(IPAddress.IPv6Any, _options.Port));
        _listener.Listen(_options.MaxConnections);

        _logger.LogInformation("LDAP server listening on port {Port}", _options.Port);

        _acceptTask = AcceptConnectionsAsync(_cts.Token);

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("LDAP server stopping (active={Active}, total={Total})",
            _stats.ActiveConnections, _stats.TotalConnections);

        _cts?.Cancel();

        _listener?.Close();

        if (_acceptTask is not null)
        {
            try
            {
                await _acceptTask;
            }
            catch (OperationCanceledException) { }
        }
    }

    private async Task AcceptConnectionsAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var socket = await _listener.AcceptAsync(ct);
                socket.NoDelay = true;

                // Backpressure: reject connections when at capacity
                if (!_connectionSemaphore.Wait(0))
                {
                    _logger.LogWarning("Max LDAP connections ({Max}) reached, rejecting from {Remote}",
                        _options.MaxConnections, socket.RemoteEndPoint);
                    _stats.OnReject();
                    socket.Dispose();
                    continue;
                }

                // Apply idle timeout at the socket level
                if (_options.IdleTimeoutSeconds > 0)
                {
                    socket.ReceiveTimeout = _options.IdleTimeoutSeconds * 1000;
                }

                var connId = Interlocked.Increment(ref _connectionCount);
                var handler = new LdapConnectionHandler(socket, _dispatcher, _options, _logger);
                _activeConnections[connId] = handler;
                _stats.OnConnect();

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await handler.RunAsync(ct);
                    }
                    finally
                    {
                        _activeConnections.TryRemove(connId, out _);
                        _stats.OnDisconnect();
                        _connectionSemaphore.Release();
                    }
                }, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (SocketException) when (ct.IsCancellationRequested) { break; }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error accepting LDAP connection");
            }
        }
    }

    public void Dispose()
    {
        _listener?.Dispose();
        _cts?.Dispose();
        _connectionSemaphore?.Dispose();
    }
}
