using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Directory.Core.Caching;

/// <summary>
/// Cross-instance cache invalidation via Redis pub/sub.
/// When a DC writes to the directory, it publishes an invalidation message.
/// All other DC instances subscribe and evict the stale entry from their L1 cache.
/// </summary>
public class CacheInvalidationBus : IHostedService, IDisposable
{
    private readonly DistributedCacheService _cacheService;
    private readonly CacheOptions _options;
    private readonly ILogger<CacheInvalidationBus> _logger;
    private ISubscriber _subscriber;
    private IConnectionMultiplexer _redis;
    private readonly string _instanceId = Guid.NewGuid().ToString("N")[..8];

    private const string InvalidationChannel = "ad:cache:invalidate";

    public CacheInvalidationBus(
        DistributedCacheService cacheService,
        IOptions<CacheOptions> options,
        ILogger<CacheInvalidationBus> logger)
    {
        _cacheService = cacheService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_options.RedisConnectionString) || !_options.EnableDistributedInvalidation)
            return;

        try
        {
            _redis = await ConnectionMultiplexer.ConnectAsync(_options.RedisConnectionString);
            _subscriber = _redis.GetSubscriber();

            await _subscriber.SubscribeAsync(RedisChannel.Literal(InvalidationChannel), (channel, message) =>
            {
                var msg = message.ToString();
                // Message format: "instanceId|key"
                var parts = msg.Split('|', 2);
                if (parts.Length == 2 && parts[0] != _instanceId)
                {
                    // Invalidation from another instance -- evict from local L1
                    _cacheService.InvalidateLocal(parts[1]);
                    _logger.LogDebug("Cross-instance invalidation: {Key} from {Instance}", parts[1], parts[0]);
                }
            });

            _logger.LogInformation("Cache invalidation bus connected to Redis, instance {Id}", _instanceId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to connect cache invalidation bus to Redis -- running in local-only mode");
        }
    }

    /// <summary>
    /// Publish an invalidation message so all other instances evict this key.
    /// </summary>
    public async Task PublishInvalidationAsync(string key)
    {
        if (_subscriber == null) return;

        try
        {
            await _subscriber.PublishAsync(RedisChannel.Literal(InvalidationChannel), $"{_instanceId}|{key}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish cache invalidation for {Key}", key);
        }
    }

    public async Task StopAsync(CancellationToken ct)
    {
        if (_subscriber != null)
            await _subscriber.UnsubscribeAllAsync();
        _redis?.Dispose();
    }

    public void Dispose()
    {
        _redis?.Dispose();
    }
}
