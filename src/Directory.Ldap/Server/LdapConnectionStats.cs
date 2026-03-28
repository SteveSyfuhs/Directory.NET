namespace Directory.Ldap.Server;

/// <summary>
/// Tracks LDAP connection statistics for monitoring and diagnostics.
/// Thread-safe via Interlocked operations.
/// </summary>
public class LdapConnectionStats
{
    private int _activeCount;
    private long _totalCount;
    private long _totalRequests;
    private long _rejectedCount;

    /// <summary>Current number of active connections.</summary>
    public int ActiveConnections => Volatile.Read(ref _activeCount);

    /// <summary>Total connections accepted since server start.</summary>
    public long TotalConnections => Volatile.Read(ref _totalCount);

    /// <summary>Total LDAP requests processed since server start.</summary>
    public long TotalRequests => Volatile.Read(ref _totalRequests);

    /// <summary>Total connections rejected due to max connection limit.</summary>
    public long RejectedConnections => Volatile.Read(ref _rejectedCount);

    public void OnConnect()
    {
        Interlocked.Increment(ref _activeCount);
        Interlocked.Increment(ref _totalCount);
    }

    public void OnDisconnect()
    {
        Interlocked.Decrement(ref _activeCount);
    }

    public void OnRequest()
    {
        Interlocked.Increment(ref _totalRequests);
    }

    public void OnReject()
    {
        Interlocked.Increment(ref _rejectedCount);
    }
}
