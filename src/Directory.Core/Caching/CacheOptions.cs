namespace Directory.Core.Caching;

/// <summary>
/// Configuration options for the in-memory caching layer.
/// Bind to the "Cache" configuration section.
/// </summary>
public class CacheOptions
{
    public const string SectionName = "Cache";

    /// <summary>Default TTL for cached objects (seconds). Default: 60.</summary>
    public int DefaultTtlSeconds { get; set; } = 60;

    /// <summary>TTL for well-known objects like krbtgt (seconds). Default: 600 (10 min).</summary>
    public int WellKnownTtlSeconds { get; set; } = 600;

    /// <summary>TTL for SPN lookups (seconds). Default: 30.</summary>
    public int SpnTtlSeconds { get; set; } = 30;

    /// <summary>TTL for group expansion results (seconds). Default: 300 (5 min).</summary>
    public int GroupExpansionTtlSeconds { get; set; } = 300;

    /// <summary>TTL for DNS zone data (seconds). Default: 300 (5 min).</summary>
    public int DnsZoneTtlSeconds { get; set; } = 300;

    /// <summary>Maximum cache size in entries. Default: 50000.</summary>
    public int MaxCacheSize { get; set; } = 50_000;

    /// <summary>Whether caching is enabled. Default: true.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Redis connection string. If null/empty, falls back to in-memory cache only.</summary>
    public string RedisConnectionString { get; set; }

    /// <summary>Redis instance name prefix for key isolation. Default: "ad:".</summary>
    public string RedisInstanceName { get; set; } = "ad:";

    /// <summary>Whether to use Redis pub/sub for cross-instance cache invalidation. Default: true.</summary>
    public bool EnableDistributedInvalidation { get; set; } = true;
}
