using System.Net;
using System.Net.Sockets;
using Directory.Core.Interfaces;
using Directory.Ldap.Handlers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Directory.Ldap.Server;

public class GlobalCatalogServer : IHostedService, IDisposable
{
    private readonly LdapServerOptions _options;
    private readonly ILdapOperationDispatcher _dispatcher;
    private readonly ISchemaService _schemaService;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<GlobalCatalogServer> _logger;
    private Socket _listener;
    private Socket _tlsListener;
    private CancellationTokenSource _cts;

    public GlobalCatalogServer(
        IOptions<LdapServerOptions> options,
        ILdapOperationDispatcher dispatcher,
        ISchemaService schemaService,
        ILoggerFactory loggerFactory)
    {
        _options = options.Value;
        _dispatcher = dispatcher;
        _schemaService = schemaService;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<GlobalCatalogServer>();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var gcPort = _options.GcPort;
        var gcTlsPort = _options.GcTlsPort;

        _listener = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp);
        _listener.DualMode = true;
        _listener.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _listener.Bind(new IPEndPoint(IPAddress.IPv6Any, gcPort));
        _listener.Listen(512);

        _logger.LogInformation("Global Catalog server started on port {Port}", gcPort);

        _ = AcceptConnectionsAsync(_listener, false, _cts.Token);

        // TLS listener
        _tlsListener = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp);
        _tlsListener.DualMode = true;
        _tlsListener.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _tlsListener.Bind(new IPEndPoint(IPAddress.IPv6Any, gcTlsPort));
        _tlsListener.Listen(512);

        _logger.LogInformation("Global Catalog TLS server started on port {Port}", gcTlsPort);

        _ = AcceptConnectionsAsync(_tlsListener, true, _cts.Token);

        return Task.CompletedTask;
    }

    private async Task AcceptConnectionsAsync(Socket listener, bool isTls, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var clientSocket = await listener.AcceptAsync(ct);
                var handler = new LdapConnectionHandler(
                    clientSocket, _dispatcher, _options,
                    _loggerFactory.CreateLogger<LdapConnectionHandler>());
                handler.IsGlobalCatalog = true;
                handler.GcAttributes = new HashSet<string>(_schemaService.GetGlobalCatalogAttributes(), StringComparer.OrdinalIgnoreCase);
                _ = handler.ProcessAsync(isTls, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error accepting GC connection");
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        _listener?.Close();
        _tlsListener?.Close();
        _logger.LogInformation("Global Catalog server stopped");
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _listener?.Dispose();
        _tlsListener?.Dispose();
        _cts?.Dispose();
    }
}
