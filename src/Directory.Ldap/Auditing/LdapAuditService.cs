using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Directory.Ldap.Auditing;

/// <summary>
/// Represents a single LDAP protocol-level audit entry.
/// </summary>
public class LdapAuditEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public string Operation { get; set; } = string.Empty; // Bind, Search, Add, Modify, Delete, ModifyDN, Compare, Extended
    public string ClientIp { get; set; } = string.Empty;
    public int ClientPort { get; set; }
    public string BoundDn { get; set; } = string.Empty; // Who is performing the operation
    public string TargetDn { get; set; } = string.Empty; // What object is being operated on
    public string ResultCode { get; set; } = string.Empty; // LDAP result code
    public long DurationMs { get; set; }
    public Dictionary<string, string> Details { get; set; } = new(); // Operation-specific details
}

/// <summary>
/// Tracks an active LDAP connection for monitoring.
/// </summary>
public class LdapActiveConnection
{
    public string ClientIp { get; set; } = string.Empty;
    public int ClientPort { get; set; }
    public string BoundDn { get; set; } = string.Empty;
    public DateTimeOffset ConnectedSince { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastActivity { get; set; } = DateTimeOffset.UtcNow;
    public long RequestCount { get; set; }
}

/// <summary>
/// Audit statistics summary.
/// </summary>
public class LdapAuditStatistics
{
    public double OperationsPerSecond { get; set; }
    public int TotalEntries { get; set; }
    public Dictionary<string, int> TopOperations { get; set; } = new();
    public Dictionary<string, int> TopClients { get; set; } = new();
    public Dictionary<string, int> TopResultCodes { get; set; } = new();
    public double AverageDurationMs { get; set; }
}

/// <summary>
/// In-memory ring-buffer audit service for LDAP protocol operations.
/// Stores the last N entries (configurable) and tracks active connections.
/// Thread-safe via ConcurrentQueue and ConcurrentDictionary.
/// </summary>
public class LdapAuditService
{
    private readonly ConcurrentQueue<LdapAuditEntry> _entries = new();
    private readonly ConcurrentDictionary<string, LdapActiveConnection> _activeConnections = new();
    private readonly int _maxEntries;
    private int _entryCount;
    private long _totalOperations;
    private readonly DateTimeOffset _startTime = DateTimeOffset.UtcNow;
    private readonly ILogger<LdapAuditService> _logger;

    public LdapAuditService(ILogger<LdapAuditService> logger, int maxEntries = 10000)
    {
        _logger = logger;
        _maxEntries = maxEntries;
    }

    /// <summary>
    /// Record an LDAP audit entry into the ring buffer.
    /// </summary>
    public void Record(LdapAuditEntry entry)
    {
        _entries.Enqueue(entry);
        Interlocked.Increment(ref _totalOperations);

        // Ring buffer: evict oldest entries when over capacity
        while (Interlocked.Increment(ref _entryCount) > _maxEntries)
        {
            if (_entries.TryDequeue(out _))
            {
                Interlocked.Decrement(ref _entryCount);
            }
            else
            {
                Interlocked.Decrement(ref _entryCount);
                break;
            }
        }

        // Update active connection's last activity
        if (!string.IsNullOrEmpty(entry.ClientIp))
        {
            var key = $"{entry.ClientIp}:{entry.ClientPort}";
            _activeConnections.AddOrUpdate(key,
                _ => new LdapActiveConnection
                {
                    ClientIp = entry.ClientIp,
                    ClientPort = entry.ClientPort,
                    BoundDn = entry.BoundDn,
                    LastActivity = DateTimeOffset.UtcNow,
                    RequestCount = 1,
                },
                (_, conn) =>
                {
                    conn.LastActivity = DateTimeOffset.UtcNow;
                    conn.RequestCount++;
                    if (!string.IsNullOrEmpty(entry.BoundDn))
                        conn.BoundDn = entry.BoundDn;
                    return conn;
                });
        }
    }

    /// <summary>
    /// Register a new active connection.
    /// </summary>
    public void OnConnect(string clientIp, int clientPort)
    {
        var key = $"{clientIp}:{clientPort}";
        _activeConnections[key] = new LdapActiveConnection
        {
            ClientIp = clientIp,
            ClientPort = clientPort,
            ConnectedSince = DateTimeOffset.UtcNow,
            LastActivity = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>
    /// Remove an active connection.
    /// </summary>
    public void OnDisconnect(string clientIp, int clientPort)
    {
        var key = $"{clientIp}:{clientPort}";
        _activeConnections.TryRemove(key, out _);
    }

    /// <summary>
    /// Query audit entries with optional filters.
    /// </summary>
    public List<LdapAuditEntry> Query(
        string operation = null,
        string clientIp = null,
        string boundDn = null,
        string targetDn = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        int limit = 200)
    {
        IEnumerable<LdapAuditEntry> query = _entries.Reverse(); // newest first

        if (!string.IsNullOrEmpty(operation))
            query = query.Where(e => e.Operation.Equals(operation, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrEmpty(clientIp))
            query = query.Where(e => e.ClientIp.Contains(clientIp, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrEmpty(boundDn))
            query = query.Where(e => e.BoundDn.Contains(boundDn, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrEmpty(targetDn))
            query = query.Where(e => e.TargetDn.Contains(targetDn, StringComparison.OrdinalIgnoreCase));

        if (from.HasValue)
            query = query.Where(e => e.Timestamp >= from.Value);

        if (to.HasValue)
            query = query.Where(e => e.Timestamp <= to.Value);

        return query.Take(Math.Clamp(limit, 1, 1000)).ToList();
    }

    /// <summary>
    /// Get aggregate statistics from the ring buffer.
    /// </summary>
    public LdapAuditStatistics GetStatistics()
    {
        var entries = _entries.ToArray();
        var elapsed = (DateTimeOffset.UtcNow - _startTime).TotalSeconds;

        var stats = new LdapAuditStatistics
        {
            TotalEntries = entries.Length,
            OperationsPerSecond = elapsed > 0 ? Volatile.Read(ref _totalOperations) / elapsed : 0,
            AverageDurationMs = entries.Length > 0 ? entries.Average(e => e.DurationMs) : 0,
        };

        stats.TopOperations = entries
            .GroupBy(e => e.Operation)
            .OrderByDescending(g => g.Count())
            .Take(10)
            .ToDictionary(g => g.Key, g => g.Count());

        stats.TopClients = entries
            .GroupBy(e => e.ClientIp)
            .OrderByDescending(g => g.Count())
            .Take(10)
            .ToDictionary(g => g.Key, g => g.Count());

        stats.TopResultCodes = entries
            .GroupBy(e => e.ResultCode)
            .OrderByDescending(g => g.Count())
            .Take(10)
            .ToDictionary(g => g.Key, g => g.Count());

        return stats;
    }

    /// <summary>
    /// Get all currently active LDAP connections.
    /// </summary>
    public List<LdapActiveConnection> GetActiveConnections()
    {
        return _activeConnections.Values
            .OrderByDescending(c => c.LastActivity)
            .ToList();
    }
}
