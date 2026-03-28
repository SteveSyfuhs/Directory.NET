using System.Runtime.CompilerServices;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Directory.Core.Caching;

/// <summary>
/// Caching decorator for IDirectoryStore. Uses a two-tier cache (L1 local + L2 Redis)
/// via DistributedCacheService. Write operations invalidate both tiers and publish
/// cross-instance invalidation via CacheInvalidationBus.
/// </summary>
public class CachedDirectoryStore : IDirectoryStore, IStreamingDirectoryStore
{
    private readonly IDirectoryStore _inner;
    private readonly DistributedCacheService _distributedCache;
    private readonly CacheInvalidationBus _invalidationBus;
    private readonly ILogger<CachedDirectoryStore> _logger;
    private readonly IOptionsMonitor<CacheOptions> _optionsMonitor;

    // Cache key prefixes
    private const string DnPrefix = "dn:";
    private const string SamPrefix = "sam:";
    private const string UpnPrefix = "upn:";
    private const string GuidPrefix = "guid:";
    private const string SpnPrefix = "spn:";

    // Well-known objects that rarely change — use longer TTL
    private static readonly HashSet<string> WellKnownSamNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "krbtgt", "Administrator", "Guest"
    };

    public CachedDirectoryStore(
        IDirectoryStore inner,
        DistributedCacheService distributedCache,
        CacheInvalidationBus invalidationBus,
        IOptionsMonitor<CacheOptions> optionsMonitor,
        ILogger<CachedDirectoryStore> logger)
    {
        _inner = inner;
        _distributedCache = distributedCache;
        _invalidationBus = invalidationBus;
        _logger = logger;
        _optionsMonitor = optionsMonitor;
    }

    private CacheOptions Options => _optionsMonitor.CurrentValue;
    private TimeSpan DefaultTtl => TimeSpan.FromSeconds(Options.DefaultTtlSeconds);
    private TimeSpan WellKnownTtl => TimeSpan.FromSeconds(Options.WellKnownTtlSeconds);
    private TimeSpan SpnTtl => TimeSpan.FromSeconds(Options.SpnTtlSeconds);

    public async Task<DirectoryObject> GetByDnAsync(string tenantId, string dn, CancellationToken ct = default)
    {
        var key = DnPrefix + dn;
        return await _distributedCache.GetOrAddAsync(key,
            () => _inner.GetByDnAsync(tenantId, dn, ct), DefaultTtl);
    }

    public async Task<DirectoryObject> GetBySamAccountNameAsync(string tenantId, string domainDn, string samAccountName, CancellationToken ct = default)
    {
        var key = SamPrefix + domainDn + "|" + samAccountName;
        var ttl = WellKnownSamNames.Contains(samAccountName) ? WellKnownTtl : DefaultTtl;
        return await _distributedCache.GetOrAddAsync(key,
            () => _inner.GetBySamAccountNameAsync(tenantId, domainDn, samAccountName, ct), ttl);
    }

    public async Task<DirectoryObject> GetByUpnAsync(string tenantId, string upn, CancellationToken ct = default)
    {
        var key = UpnPrefix + upn;
        return await _distributedCache.GetOrAddAsync(key,
            () => _inner.GetByUpnAsync(tenantId, upn, ct), DefaultTtl);
    }

    public async Task<DirectoryObject> GetByGuidAsync(string tenantId, string guid, CancellationToken ct = default)
    {
        var key = GuidPrefix + guid;
        return await _distributedCache.GetOrAddAsync(key,
            () => _inner.GetByGuidAsync(tenantId, guid, ct), DefaultTtl);
    }

    public async Task<IReadOnlyList<DirectoryObject>> GetByServicePrincipalNameAsync(string tenantId, string spn, CancellationToken ct = default)
    {
        var key = SpnPrefix + spn;
        var result = await _distributedCache.GetOrAddAsync<List<DirectoryObject>>(key,
            async () =>
            {
                var items = await _inner.GetByServicePrincipalNameAsync(tenantId, spn, ct);
                return items.Count > 0 ? items.ToList() : null;
            }, SpnTtl);
        return result ?? (IReadOnlyList<DirectoryObject>)[];
    }

    public async Task<IReadOnlyList<DirectoryObject>> GetByDnsAsync(string tenantId, IEnumerable<string> dns, CancellationToken ct = default)
    {
        // Leverage per-DN cache: resolve each DN through GetByDnAsync (which checks cache first)
        var tasks = dns.Select(dn => GetByDnAsync(tenantId, dn, ct));
        var results = await Task.WhenAll(tasks);
        return results.Where(r => r is not null).ToList();
    }

    // Search results are too varied to cache — pass through.
    public Task<SearchResult> SearchAsync(
        string tenantId, string baseDn, SearchScope scope, FilterNode filter,
        string[] attributes, int sizeLimit = 0, int timeLimitSeconds = 0,
        string continuationToken = null, int pageSize = 1000,
        bool includeDeleted = false, CancellationToken ct = default)
    {
        return _inner.SearchAsync(tenantId, baseDn, scope, filter, attributes,
            sizeLimit, timeLimitSeconds, continuationToken, pageSize, includeDeleted, ct);
    }

    // Streaming search bypasses cache (search results aren't cached anyway).
    // Delegates to the inner store if it supports streaming, otherwise falls back.
    public IAsyncEnumerable<DirectoryObject> SearchStreamAsync(
        string tenantId,
        string baseDn,
        SearchScope scope,
        FilterNode filter,
        string[] attributes,
        int sizeLimit = 0,
        int timeLimitSeconds = 0,
        bool includeDeleted = false,
        CancellationToken ct = default)
    {
        if (_inner is IStreamingDirectoryStore streaming)
            return streaming.SearchStreamAsync(tenantId, baseDn, scope, filter, attributes, sizeLimit, timeLimitSeconds, includeDeleted, ct);

        // Fallback: load all and yield
        return FallbackSearchStreamAsync(tenantId, baseDn, scope, filter, attributes, sizeLimit, timeLimitSeconds, includeDeleted, ct);
    }

    private async IAsyncEnumerable<DirectoryObject> FallbackSearchStreamAsync(
        string tenantId,
        string baseDn,
        SearchScope scope,
        FilterNode filter,
        string[] attributes,
        int sizeLimit,
        int timeLimitSeconds,
        bool includeDeleted,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var result = await _inner.SearchAsync(tenantId, baseDn, scope, filter, attributes,
            sizeLimit, timeLimitSeconds, includeDeleted: includeDeleted, ct: ct);
        foreach (var entry in result.Entries)
            yield return entry;
    }

    public async Task CreateAsync(DirectoryObject obj, CancellationToken ct = default)
    {
        await _inner.CreateAsync(obj, ct);

        // Proactively cache the new object (L1+L2) is not straightforward here
        // since we'd need to serialize to all keys. Instead, just let next read populate.
        // But we do want cross-instance awareness, so no-op for create.
    }

    public async Task UpdateAsync(DirectoryObject obj, CancellationToken ct = default)
    {
        await _inner.UpdateAsync(obj, ct);

        // Invalidate all cached representations of this object
        await InvalidateObjectAsync(obj);
    }

    public async Task DeleteAsync(string tenantId, string dn, bool hardDelete = false, CancellationToken ct = default)
    {
        // Try to get the object first so we can invalidate all its cache keys
        var obj = await _inner.GetByDnAsync(tenantId, dn, ct);

        await _inner.DeleteAsync(tenantId, dn, hardDelete, ct);

        // Invalidate by DN at minimum
        await InvalidateKeyAsync(DnPrefix + dn);

        // If we got the full object, invalidate all keys
        if (obj is not null)
        {
            await InvalidateObjectAsync(obj);
        }
    }

    public async Task MoveAsync(string tenantId, string oldDn, string newDn, CancellationToken ct = default)
    {
        // Invalidate old DN before move
        await InvalidateKeyAsync(DnPrefix + oldDn);

        await _inner.MoveAsync(tenantId, oldDn, newDn, ct);

        // Invalidate new DN as well
        await InvalidateKeyAsync(DnPrefix + newDn);
    }

    // Children results are not cached — pass through.
    public Task<IReadOnlyList<DirectoryObject>> GetChildrenAsync(string tenantId, string parentDn, CancellationToken ct = default)
    {
        return _inner.GetChildrenAsync(tenantId, parentDn, ct);
    }

    // USN counter — never cached.
    public Task<long> GetNextUsnAsync(string tenantId, string domainDn, CancellationToken ct = default)
    {
        return _inner.GetNextUsnAsync(tenantId, domainDn, ct);
    }

    // RID pool — never cached.
    public Task<long> ClaimRidPoolAsync(string tenantId, string domainDn, int poolSize, CancellationToken ct = default)
    {
        return _inner.ClaimRidPoolAsync(tenantId, domainDn, poolSize, ct);
    }

    /// <summary>
    /// Invalidate a single cache key in both tiers and publish to peers.
    /// </summary>
    private async Task InvalidateKeyAsync(string key)
    {
        await _distributedCache.InvalidateAsync(key);
        if (_invalidationBus != null)
            await _invalidationBus.PublishInvalidationAsync(key);
    }

    /// <summary>
    /// Invalidate all cached entries for a directory object.
    /// </summary>
    private async Task InvalidateObjectAsync(DirectoryObject obj)
    {
        await InvalidateKeyAsync(DnPrefix + obj.DistinguishedName);

        if (!string.IsNullOrEmpty(obj.SAMAccountName))
            await InvalidateKeyAsync(SamPrefix + obj.DomainDn + "|" + obj.SAMAccountName);

        if (!string.IsNullOrEmpty(obj.UserPrincipalName))
            await InvalidateKeyAsync(UpnPrefix + obj.UserPrincipalName);

        if (!string.IsNullOrEmpty(obj.ObjectGuid))
            await InvalidateKeyAsync(GuidPrefix + obj.ObjectGuid);

        foreach (var spn in obj.ServicePrincipalName)
            await InvalidateKeyAsync(SpnPrefix + spn);
    }
}
