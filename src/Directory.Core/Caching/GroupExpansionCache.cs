using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Directory.Core.Caching;

/// <summary>
/// Caches group membership expansion results. Group expansion is the #1 bottleneck:
/// a user in 50 nested groups requires 50+ sequential DB queries without caching.
/// This cache stores the flattened group SID list keyed by user DN.
/// </summary>
public class GroupExpansionCache
{
    private readonly IMemoryCache _cache;
    private readonly IOptionsMonitor<CacheOptions> _optionsMonitor;
    private CancellationTokenSource _cts = new();

    public GroupExpansionCache(IMemoryCache cache, IOptionsMonitor<CacheOptions> optionsMonitor)
    {
        _cache = cache;
        _optionsMonitor = optionsMonitor;
    }

    private TimeSpan Ttl => TimeSpan.FromSeconds(_optionsMonitor.CurrentValue.GroupExpansionTtlSeconds);

    public bool TryGetExpansion(string userDn, out List<(string Sid, uint Rid, int Attributes)> groups)
    {
        return _cache.TryGetValue($"grpexp:{userDn}", out groups);
    }

    public void SetExpansion(string userDn, List<(string Sid, uint Rid, int Attributes)> groups)
    {
        var options = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(Ttl)
            .AddExpirationToken(new CancellationChangeToken(_cts.Token));
        _cache.Set($"grpexp:{userDn}", groups, options);
    }

    /// <summary>
    /// Invalidate all group expansions. Called when group membership changes.
    /// Uses CancellationTokenSource swap so all entries with the old token expire immediately.
    /// </summary>
    public void InvalidateAll()
    {
        var old = Interlocked.Exchange(ref _cts, new CancellationTokenSource());
        old.Cancel();
        old.Dispose();
    }
}
