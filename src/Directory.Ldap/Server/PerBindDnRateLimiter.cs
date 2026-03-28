using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Directory.Ldap.Server;

/// <summary>
/// Per-bind-DN rate limiter for LDAP operations. Tracks operations per bound DN
/// within configurable time windows and returns LDAP busy (result code 51) when
/// limits are exceeded. Service accounts can be exempted via admin overrides.
/// </summary>
public class PerBindDnRateLimiter
{
    private readonly ILogger<PerBindDnRateLimiter> _logger;
    private readonly ConcurrentDictionary<string, DnRateState> _dnStates = new();
    private readonly ConcurrentDictionary<string, DnRateLimit> _customLimits = new();
    private readonly ConcurrentDictionary<string, bool> _exemptDns = new();

    // Statistics
    private long _totalChecks;
    private long _totalRejections;
    private readonly ConcurrentDictionary<string, long> _rejectionsByDn = new();

    /// <summary>
    /// Default operations permitted per window for any bound DN.
    /// </summary>
    public int DefaultPermitLimit { get; set; } = 1000;

    /// <summary>
    /// Default time window for rate limiting.
    /// </summary>
    public TimeSpan DefaultWindow { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// LDAP result code returned when rate limit is exceeded (51 = busy).
    /// </summary>
    public const int LdapBusyResultCode = 51;

    public PerBindDnRateLimiter(ILogger<PerBindDnRateLimiter> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Check whether the given bound DN is allowed to perform an operation.
    /// Returns true if allowed, false if the rate limit has been exceeded.
    /// </summary>
    public bool TryAcquire(string boundDn)
    {
        Interlocked.Increment(ref _totalChecks);

        if (string.IsNullOrEmpty(boundDn))
            return true; // Anonymous binds use a global pool, not per-DN limiting

        var normalizedDn = boundDn.ToLowerInvariant();

        // Check exemptions
        if (_exemptDns.ContainsKey(normalizedDn))
            return true;

        // Get or create state for this DN
        var state = _dnStates.GetOrAdd(normalizedDn, _ => new DnRateState());

        // Get limit configuration (custom or default)
        var limit = _customLimits.TryGetValue(normalizedDn, out var custom)
            ? custom.PermitLimit
            : DefaultPermitLimit;

        var window = _customLimits.TryGetValue(normalizedDn, out var customWindow)
            ? customWindow.Window
            : DefaultWindow;

        lock (state)
        {
            var now = DateTimeOffset.UtcNow;

            // If the window has elapsed, reset the counter
            if (now - state.WindowStart >= window)
            {
                state.WindowStart = now;
                state.OperationCount = 0;
            }

            if (state.OperationCount >= limit)
            {
                Interlocked.Increment(ref _totalRejections);
                _rejectionsByDn.AddOrUpdate(normalizedDn, 1, (_, count) => count + 1);
                _logger.LogWarning("Rate limit exceeded for DN {BoundDn}: {Count}/{Limit} in {Window}",
                    boundDn, state.OperationCount, limit, window);
                return false;
            }

            state.OperationCount++;
            state.LastOperation = now;
            return true;
        }
    }

    // Configuration management

    /// <summary>
    /// Set a custom rate limit for a specific DN or DN pattern.
    /// </summary>
    public void SetDnLimit(string dn, int permitLimit, TimeSpan? window = null)
    {
        var normalizedDn = dn.ToLowerInvariant();
        _customLimits[normalizedDn] = new DnRateLimit
        {
            Dn = dn,
            PermitLimit = permitLimit,
            Window = window ?? DefaultWindow
        };
        _logger.LogInformation("Set rate limit for {Dn}: {Limit} ops/{Window}", dn, permitLimit, window ?? DefaultWindow);
    }

    /// <summary>
    /// Remove a custom rate limit, reverting to the default.
    /// </summary>
    public bool RemoveDnLimit(string dn)
    {
        return _customLimits.TryRemove(dn.ToLowerInvariant(), out _);
    }

    /// <summary>
    /// Exempt a DN from rate limiting (e.g., service accounts).
    /// </summary>
    public void AddExemption(string dn)
    {
        _exemptDns[dn.ToLowerInvariant()] = true;
        _logger.LogInformation("Added rate limit exemption for {Dn}", dn);
    }

    /// <summary>
    /// Remove an exemption.
    /// </summary>
    public bool RemoveExemption(string dn)
    {
        return _exemptDns.TryRemove(dn.ToLowerInvariant(), out _);
    }

    /// <summary>
    /// Get all custom rate limit configurations.
    /// </summary>
    public List<DnRateLimit> GetCustomLimits()
    {
        return _customLimits.Values.ToList();
    }

    /// <summary>
    /// Get all exempted DNs.
    /// </summary>
    public List<string> GetExemptions()
    {
        return _exemptDns.Keys.ToList();
    }

    /// <summary>
    /// Get rate limiting statistics.
    /// </summary>
    public RateLimitStats GetStats()
    {
        var topConsumers = _dnStates
            .Select(kvp => new DnConsumption
            {
                Dn = kvp.Key,
                CurrentWindowOps = kvp.Value.OperationCount,
                LastOperation = kvp.Value.LastOperation
            })
            .OrderByDescending(c => c.CurrentWindowOps)
            .Take(20)
            .ToList();

        var topRejected = _rejectionsByDn
            .Select(kvp => new DnRejection { Dn = kvp.Key, Rejections = kvp.Value })
            .OrderByDescending(r => r.Rejections)
            .Take(20)
            .ToList();

        return new RateLimitStats
        {
            TotalChecks = Interlocked.Read(ref _totalChecks),
            TotalRejections = Interlocked.Read(ref _totalRejections),
            ActiveDns = _dnStates.Count,
            DefaultPermitLimit = DefaultPermitLimit,
            DefaultWindowSeconds = (int)DefaultWindow.TotalSeconds,
            CustomLimitCount = _customLimits.Count,
            ExemptionCount = _exemptDns.Count,
            TopConsumers = topConsumers,
            TopRejected = topRejected
        };
    }

    /// <summary>
    /// Reset statistics counters.
    /// </summary>
    public void ResetStats()
    {
        Interlocked.Exchange(ref _totalChecks, 0);
        Interlocked.Exchange(ref _totalRejections, 0);
        _rejectionsByDn.Clear();
    }

    /// <summary>
    /// Clean up stale entries (DNs that haven't had operations recently).
    /// </summary>
    public int CleanupStaleEntries(TimeSpan olderThan)
    {
        var cutoff = DateTimeOffset.UtcNow - olderThan;
        int removed = 0;

        foreach (var kvp in _dnStates)
        {
            if (kvp.Value.LastOperation < cutoff)
            {
                if (_dnStates.TryRemove(kvp.Key, out _))
                    removed++;
            }
        }

        return removed;
    }

    private class DnRateState
    {
        public DateTimeOffset WindowStart { get; set; } = DateTimeOffset.UtcNow;
        public int OperationCount { get; set; }
        public DateTimeOffset LastOperation { get; set; } = DateTimeOffset.UtcNow;
    }
}

public class DnRateLimit
{
    public string Dn { get; set; } = "";
    public int PermitLimit { get; set; }
    public TimeSpan Window { get; set; }
    public double WindowSeconds => Window.TotalSeconds;
}

public class RateLimitStats
{
    public long TotalChecks { get; set; }
    public long TotalRejections { get; set; }
    public int ActiveDns { get; set; }
    public int DefaultPermitLimit { get; set; }
    public int DefaultWindowSeconds { get; set; }
    public int CustomLimitCount { get; set; }
    public int ExemptionCount { get; set; }
    public List<DnConsumption> TopConsumers { get; set; } = new();
    public List<DnRejection> TopRejected { get; set; } = new();
}

public class DnConsumption
{
    public string Dn { get; set; } = "";
    public int CurrentWindowOps { get; set; }
    public DateTimeOffset LastOperation { get; set; }
}

public class DnRejection
{
    public string Dn { get; set; } = "";
    public long Rejections { get; set; }
}
