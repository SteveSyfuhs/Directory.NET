using System.Net;
using System.Net.Sockets;
using Directory.Core.Telemetry;
using Directory.Dns;
using Directory.Kerberos;
using Directory.Ldap.Server;
using Directory.Replication;
using Directory.Rpc;
using Microsoft.Extensions.Options;

namespace Directory.Server;

/// <summary>
/// Pre-flight health check that validates port availability before protocol servers start.
/// Logs warnings for any ports already in use but does not prevent startup.
/// </summary>
public class PreflightCheckService : IHostedService
{
    private readonly ILogger<PreflightCheckService> _logger;
    private readonly LdapServerOptions _ldapOptions;
    private readonly KerberosOptions _kerberosOptions;
    private readonly DnsOptions _dnsOptions;
    private readonly RpcServerOptions _rpcOptions;
    private readonly ReplicationOptions _replicationOptions;

    public PreflightCheckService(
        ILogger<PreflightCheckService> logger,
        IOptions<LdapServerOptions> ldapOptions,
        IOptions<KerberosOptions> kerberosOptions,
        IOptions<DnsOptions> dnsOptions,
        IOptions<RpcServerOptions> rpcOptions,
        IOptions<ReplicationOptions> replicationOptions)
    {
        _logger = logger;
        _ldapOptions = ldapOptions.Value;
        _kerberosOptions = kerberosOptions.Value;
        _dnsOptions = dnsOptions.Value;
        _rpcOptions = rpcOptions.Value;
        _replicationOptions = replicationOptions.Value;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("[PreFlight] Checking port availability...");

        var conflicts = new List<string>();

        // TCP port checks
        var tcpChecks = new (int Port, string Service)[]
        {
            (_ldapOptions.Port, "LDAP"),
            (_ldapOptions.TlsPort, "LDAPS"),
            (_kerberosOptions.Port, "Kerberos"),
            (_dnsOptions.Port, "DNS"),
            (_rpcOptions.EndpointMapperPort, "RPC Endpoint Mapper"),
            (_rpcOptions.ServicePort, "RPC Service"),
            (3268, "Global Catalog"),
            (3269, "Global Catalog TLS"),
            (464, "Kpasswd"),
            (_replicationOptions.HttpPort, "DRS HTTP Replication"),
        };

        foreach (var (port, service) in tcpChecks)
        {
            if (IsTcpPortAvailable(port))
            {
                _logger.LogInformation(EventIds.ProtocolServerStarted, "[PreFlight] Port {Port}/TCP ({Service}) is available \u2713", port, service);
            }
            else
            {
                var message = $"Port {port}/TCP ({service}) is already in use by another process";
                _logger.LogWarning(EventIds.PortConflict, "[PreFlight] WARNING: {Message}", message);
                conflicts.Add(message);

                if (port == 135)
                {
                    _logger.LogWarning(EventIds.PortConflict,
                        "[PreFlight] This port is typically occupied by the Windows RPC Endpoint Mapper (RpcSs). " +
                        "The Directory.NET RPC service defaults to port 1135 instead.");
                }
            }
        }

        // UDP port checks
        var udpChecks = new (int Port, string Service)[]
        {
            (_dnsOptions.Port, "DNS"),
            (_ldapOptions.Port, "CLDAP"),
            (_kerberosOptions.Port, "Kerberos"),
        };

        foreach (var (port, service) in udpChecks)
        {
            if (IsUdpPortAvailable(port))
            {
                _logger.LogInformation(EventIds.ProtocolServerStarted, "[PreFlight] Port {Port}/UDP ({Service}) is available \u2713", port, service);
            }
            else
            {
                var message = $"Port {port}/UDP ({service}) is already in use by another process";
                _logger.LogWarning(EventIds.PortConflict, "[PreFlight] WARNING: {Message}", message);
                conflicts.Add(message);
            }
        }

        if (conflicts.Count > 0)
        {
            _logger.LogError(EventIds.PortConflict,
                "[PreFlight] Port conflict summary: {ConflictCount} port(s) unavailable. " +
                "The affected services may fail to start. Conflicts:{NewLine}{Conflicts}{NewLine}" +
                "Consider changing the port numbers in appsettings.json or stopping the conflicting processes.",
                conflicts.Count,
                Environment.NewLine,
                string.Join(Environment.NewLine, conflicts.Select(c => $"  - {c}")),
                Environment.NewLine);
        }
        else
        {
            _logger.LogInformation(EventIds.ProtocolServerStarted, "[PreFlight] All ports are available. Ready to start protocol servers.");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static bool IsTcpPortAvailable(int port)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static bool IsUdpPortAvailable(int port)
    {
        try
        {
            using var client = new UdpClient(port);
            client.Close();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }
}
