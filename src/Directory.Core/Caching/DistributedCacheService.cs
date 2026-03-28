using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Directory.Core.Caching;

/// <summary>
/// Two-tier cache: L1 (IMemoryCache, local, sub-ms) + L2 (IDistributedCache/Redis, shared, ~1ms).
/// Read path: L1 -> L2 -> DB -> populate both.
/// Write path: DB -> invalidate L1 + L2 -> publish invalidation to peers.
/// </summary>
public class DistributedCacheService
{
    private readonly IMemoryCache _l1;
    private readonly IDistributedCache _l2;
    private readonly CacheOptions _options;
    private readonly ILogger<DistributedCacheService> _logger;

    public DistributedCacheService(
        IMemoryCache l1,
        IDistributedCache l2,
        IOptions<CacheOptions> options,
        ILogger<DistributedCacheService> logger)
    {
        _l1 = l1;
        _l2 = l2;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<T> GetOrAddAsync<T>(string key, Func<Task<T>> factory, TimeSpan ttl) where T : class
    {
        // L1 check (local memory, ~0ms)
        if (_l1.TryGetValue(key, out T cached))
            return cached;

        // L2 check (Redis, ~1ms)
        try
        {
            var bytes = await _l2.GetAsync(key);
            if (bytes != null)
            {
                var deserialized = JsonSerializer.Deserialize<T>(bytes);
                if (deserialized != null)
                {
                    // Populate L1 with shorter TTL (L1 expires before L2)
                    _l1.Set(key, deserialized, TimeSpan.FromSeconds(Math.Min(ttl.TotalSeconds, 30)));
                    return deserialized;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis L2 cache read failed for key {Key}, falling through to DB", key);
        }

        // DB query (the factory)
        var value = await factory();

        if (value != null)
        {
            // Populate L1
            _l1.Set(key, value, TimeSpan.FromSeconds(Math.Min(ttl.TotalSeconds, 30)));

            // Populate L2
            try
            {
                var serialized = JsonSerializer.SerializeToUtf8Bytes(value);
                await _l2.SetAsync(key, serialized, new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = ttl,
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis L2 cache write failed for key {Key}", key);
            }
        }

        return value;
    }

    public async Task InvalidateAsync(string key)
    {
        _l1.Remove(key);
        try
        {
            await _l2.RemoveAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis L2 cache invalidation failed for key {Key}", key);
        }
    }

    public void InvalidateLocal(string key)
    {
        _l1.Remove(key);
    }
}
