using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Directory.Ldap.Server;

/// <summary>
/// Manages and tracks active LDAP connections with metadata, enforces connection limits,
/// and handles idle connection cleanup.
///
/// Features:
///   - Track active connections with metadata (bound DN, remote IP, connect time, last activity)
///   - Configurable max connections per IP and total max connections
///   - Idle connection timeout with periodic cleanup
///   - Connection statistics (total, active, idle, rejected)
///   - Integration with LdapServer / LdapConnectionHandler
/// </summary>
public class LdapConnectionPool : IDisposable
{
    private readonly LdapConnectionPoolOptions _poolOptions;
    private readonly LdapConnectionStats _stats;
    private readonly ILogger<LdapConnectionPool> _logger;

    private readonly ConcurrentDictionary<int, ConnectionEntry> _connections = new();
    private readonly ConcurrentDictionary<string, int> _connectionsPerIp = new();

    private readonly Timer _cleanupTimer;
    private int _nextConnectionId;

    public LdapConnectionPool(
        IOptions<LdapConnectionPoolOptions> poolOptions,
        LdapConnectionStats stats,
        ILogger<LdapConnectionPool> logger)
    {
        _poolOptions = poolOptions.Value;
        _stats = stats;
        _logger = logger;

        // Start periodic idle connection cleanup
        var cleanupInterval = TimeSpan.FromSeconds(Math.Max(30, _poolOptions.IdleTimeoutSeconds / 2));
        _cleanupTimer = new Timer(CleanupIdleConnections, null, cleanupInterval, cleanupInterval);
    }

    /// <summary>
    /// Try to register a new connection. Returns a connection ID if accepted, or -1 if rejected.
    /// </summary>
    public int TryRegister(EndPoint remoteEndPoint, LdapConnectionHandler handler)
    {
        var ipAddress = ExtractIpAddress(remoteEndPoint);

        // Check total connection limit
        if (_connections.Count >= _poolOptions.MaxTotalConnections)
        {
            _logger.LogWarning("Connection rejected: total limit {Max} reached (from {IP})",
                _poolOptions.MaxTotalConnections, ipAddress);
            _stats.OnReject();
            return -1;
        }

        // Check per-IP connection limit
        if (!string.IsNullOrEmpty(ipAddress))
        {
            var currentPerIp = _connectionsPerIp.GetOrAdd(ipAddress, 0);
            if (currentPerIp >= _poolOptions.MaxConnectionsPerIp)
            {
                _logger.LogWarning("Connection rejected: per-IP limit {Max} reached for {IP}",
                    _poolOptions.MaxConnectionsPerIp, ipAddress);
                _stats.OnReject();
                return -1;
            }
        }

        var connectionId = Interlocked.Increment(ref _nextConnectionId);
        var entry = new ConnectionEntry
        {
            ConnectionId = connectionId,
            Handler = handler,
            RemoteIp = ipAddress,
            RemoteEndPoint = remoteEndPoint?.ToString() ?? "unknown",
            ConnectedAt = DateTimeOffset.UtcNow,
            LastActivity = DateTimeOffset.UtcNow,
        };

        _connections[connectionId] = entry;

        // Increment per-IP counter
        if (!string.IsNullOrEmpty(ipAddress))
        {
            _connectionsPerIp.AddOrUpdate(ipAddress, 1, (_, count) => count + 1);
        }

        _stats.OnConnect();

        _logger.LogDebug("Connection registered: id={Id}, remote={Endpoint}, total={Total}",
            connectionId, entry.RemoteEndPoint, _connections.Count);

        return connectionId;
    }

    /// <summary>
    /// Remove a connection from the pool (called on disconnect).
    /// </summary>
    public void Unregister(int connectionId)
    {
        if (_connections.TryRemove(connectionId, out var entry))
        {
            // Decrement per-IP counter
            if (!string.IsNullOrEmpty(entry.RemoteIp))
            {
                _connectionsPerIp.AddOrUpdate(entry.RemoteIp, 0, (_, count) => Math.Max(0, count - 1));

                // Clean up zero-count entries to prevent memory leak
                if (_connectionsPerIp.TryGetValue(entry.RemoteIp, out var remaining) && remaining <= 0)
                {
                    _connectionsPerIp.TryRemove(entry.RemoteIp, out _);
                }
            }

            _stats.OnDisconnect();

            _logger.LogDebug("Connection unregistered: id={Id}, remote={Endpoint}, duration={Duration}s, total={Total}",
                connectionId, entry.RemoteEndPoint,
                (DateTimeOffset.UtcNow - entry.ConnectedAt).TotalSeconds,
                _connections.Count);
        }
    }

    /// <summary>
    /// Record activity on a connection (resets the idle timeout).
    /// </summary>
    public void RecordActivity(int connectionId)
    {
        if (_connections.TryGetValue(connectionId, out var entry))
        {
            entry.LastActivity = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>
    /// Update the bound DN for a connection (called after successful bind).
    /// </summary>
    public void UpdateBoundDn(int connectionId, string boundDn)
    {
        if (_connections.TryGetValue(connectionId, out var entry))
        {
            entry.BoundDn = boundDn;
        }
    }

    /// <summary>
    /// Get a snapshot of all active connections.
    /// </summary>
    public IReadOnlyList<ConnectionInfo> GetActiveConnections()
    {
        return _connections.Values
            .Select(e => new ConnectionInfo
            {
                ConnectionId = e.ConnectionId,
                RemoteEndPoint = e.RemoteEndPoint,
                BoundDn = e.BoundDn,
                ConnectedAt = e.ConnectedAt,
                LastActivity = e.LastActivity,
                IsIdle = IsIdle(e),
            })
            .ToList();
    }

    /// <summary>
    /// Get pool statistics.
    /// </summary>
    public ConnectionPoolStatistics GetStatistics()
    {
        var now = DateTimeOffset.UtcNow;
        var connections = _connections.Values.ToList();
        var idleCount = connections.Count(e => IsIdle(e));

        return new ConnectionPoolStatistics
        {
            TotalConnections = _stats.TotalConnections,
            ActiveConnections = connections.Count - idleCount,
            IdleConnections = idleCount,
            RejectedConnections = _stats.RejectedConnections,
            TotalRequests = _stats.TotalRequests,
            ConnectionsPerIp = _connectionsPerIp
                .Where(kv => kv.Value > 0)
                .ToDictionary(kv => kv.Key, kv => kv.Value),
        };
    }

    /// <summary>
    /// Periodic cleanup of idle connections.
    /// </summary>
    private void CleanupIdleConnections(object state)
    {
        var now = DateTimeOffset.UtcNow;
        var idleTimeout = TimeSpan.FromSeconds(_poolOptions.IdleTimeoutSeconds);
        var idleConnections = new List<(int Id, ConnectionEntry Entry)>();

        foreach (var (id, entry) in _connections)
        {
            if (now - entry.LastActivity > idleTimeout)
            {
                idleConnections.Add((id, entry));
            }
        }

        if (idleConnections.Count == 0)
            return;

        _logger.LogInformation("Cleaning up {Count} idle connections (timeout={Timeout}s)",
            idleConnections.Count, _poolOptions.IdleTimeoutSeconds);

        foreach (var (id, entry) in idleConnections)
        {
            try
            {
                // Notify the idle callback so the server can close the socket
                entry.OnIdleTimeout?.Invoke(id);
                Unregister(id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error closing idle connection {Id}", id);
            }
        }
    }

    private bool IsIdle(ConnectionEntry entry)
    {
        return (DateTimeOffset.UtcNow - entry.LastActivity).TotalSeconds > _poolOptions.IdleTimeoutSeconds;
    }

    private static string ExtractIpAddress(EndPoint endPoint)
    {
        if (endPoint is IPEndPoint ipEndPoint)
            return ipEndPoint.Address.ToString();

        var str = endPoint?.ToString() ?? "";
        var colonIndex = str.LastIndexOf(':');
        return colonIndex > 0 ? str[..colonIndex] : str;
    }

    public void Dispose()
    {
        _cleanupTimer.Dispose();
    }
}

/// <summary>
/// Internal tracking entry for an active connection.
/// </summary>
internal class ConnectionEntry
{
    public int ConnectionId { get; init; }
    public LdapConnectionHandler Handler { get; init; } = null;
    public string RemoteIp { get; init; } = string.Empty;
    public string RemoteEndPoint { get; init; } = string.Empty;
    public string BoundDn { get; set; }
    public DateTimeOffset ConnectedAt { get; init; }
    public DateTimeOffset LastActivity { get; set; }

    /// <summary>
    /// Callback invoked when the connection is detected as idle and should be closed.
    /// </summary>
    public Action<int> OnIdleTimeout { get; set; }
}

/// <summary>
/// Public-facing connection information snapshot.
/// </summary>
public class ConnectionInfo
{
    public int ConnectionId { get; init; }
    public string RemoteEndPoint { get; init; } = string.Empty;
    public string BoundDn { get; init; }
    public DateTimeOffset ConnectedAt { get; init; }
    public DateTimeOffset LastActivity { get; init; }
    public bool IsIdle { get; init; }
}

/// <summary>
/// Connection pool statistics snapshot.
/// </summary>
public class ConnectionPoolStatistics
{
    public long TotalConnections { get; init; }
    public int ActiveConnections { get; init; }
    public int IdleConnections { get; init; }
    public long RejectedConnections { get; init; }
    public long TotalRequests { get; init; }
    public Dictionary<string, int> ConnectionsPerIp { get; init; } = [];
}

/// <summary>
/// Configuration options for the LDAP connection pool.
/// </summary>
public class LdapConnectionPoolOptions
{
    public const string SectionName = "LdapConnectionPool";

    /// <summary>Maximum total connections across all clients. Default: 2000.</summary>
    public int MaxTotalConnections { get; set; } = 2000;

    /// <summary>Maximum connections from a single IP address. Default: 100.</summary>
    public int MaxConnectionsPerIp { get; set; } = 100;

    /// <summary>Idle connection timeout in seconds. Default: 300 (5 minutes).</summary>
    public int IdleTimeoutSeconds { get; set; } = 300;
}
