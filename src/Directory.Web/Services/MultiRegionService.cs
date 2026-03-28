using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json.Serialization;
using Directory.CosmosDb.Configuration;

namespace Directory.Web.Services;

public class RegionConfiguration
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string CosmosDbEndpoint { get; set; } = "";
    public string PreferredRegion { get; set; } = "";
    public List<string> DcEndpoints { get; set; } = new();
    public bool IsPrimary { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public RegionHealthStatus Health { get; set; } = RegionHealthStatus.Unknown;
    public DateTimeOffset? LastHealthCheck { get; set; }
    /// <summary>Optional override for the health-check URL. Falls back to CosmosDbEndpoint.</summary>
    public string HealthCheckUrl { get; set; }
    /// <summary>Last measured round-trip latency in milliseconds.</summary>
    public double? LastLatencyMs { get; set; }
}

public enum RegionHealthStatus { Unknown, Healthy, Degraded, Offline }

public class MultiRegionService
{
    private readonly ILogger<MultiRegionService> _logger;
    private readonly CosmosConfigurationStore _configStore;
    private readonly IHttpClientFactory _httpClientFactory;

    private readonly ConcurrentDictionary<string, RegionConfiguration> _regions = new();
    private bool _loaded;

    private const string ConfigScope = "cluster";
    private const string ConfigSection = "MultiRegion";
    private static readonly TimeSpan HealthCheckTimeout = TimeSpan.FromSeconds(5);
    private const double DegradedLatencyMs = 2000;

    public MultiRegionService(
        ILogger<MultiRegionService> logger,
        CosmosConfigurationStore configStore,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _configStore = configStore;
        _httpClientFactory = httpClientFactory;
    }

    private async Task EnsureLoadedAsync()
    {
        if (_loaded) return;
        try
        {
            var regions = await _configStore.GetAsync<List<RegionConfiguration>>(ConfigScope, ConfigSection, "Regions");
            if (regions != null)
            {
                foreach (var r in regions)
                    _regions[r.Id] = r;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load multi-region config from Cosmos DB");
        }
        _loaded = true;
    }

    private async Task SaveRegionsAsync()
    {
        await _configStore.SetAsync(ConfigScope, ConfigSection, "Regions", _regions.Values.ToList());
    }

    public async Task<List<RegionConfiguration>> GetRegions()
    {
        await EnsureLoadedAsync();
        return _regions.Values.OrderByDescending(r => r.IsPrimary).ThenBy(r => r.Name).ToList();
    }

    public async Task<RegionConfiguration> GetRegion(string id)
    {
        await EnsureLoadedAsync();
        _regions.TryGetValue(id, out var region);
        return region;
    }

    public async Task<RegionConfiguration> CreateRegion(RegionConfiguration region)
    {
        await EnsureLoadedAsync();
        region.Id = Guid.NewGuid().ToString();
        region.CreatedAt = DateTimeOffset.UtcNow;

        // If this is the first region or marked primary, ensure only one primary
        if (region.IsPrimary)
        {
            foreach (var existing in _regions.Values)
                existing.IsPrimary = false;
        }

        // If no regions exist, make this one primary
        if (_regions.IsEmpty)
            region.IsPrimary = true;

        _regions[region.Id] = region;
        await SaveRegionsAsync();
        _logger.LogInformation("Created region {Name} (Primary: {IsPrimary})", region.Name, region.IsPrimary);
        return region;
    }

    public async Task<RegionConfiguration> UpdateRegion(string id, RegionConfiguration region)
    {
        await EnsureLoadedAsync();
        if (!_regions.ContainsKey(id)) return null;

        region.Id = id;

        // If setting as primary, demote existing primary
        if (region.IsPrimary)
        {
            foreach (var existing in _regions.Values.Where(r => r.Id != id))
                existing.IsPrimary = false;
        }

        _regions[id] = region;
        await SaveRegionsAsync();
        return region;
    }

    public async Task<bool> DeleteRegion(string id)
    {
        await EnsureLoadedAsync();
        if (!_regions.TryRemove(id, out var removed)) return false;

        // If we deleted the primary, promote the next enabled region
        if (removed.IsPrimary)
        {
            var next = _regions.Values.FirstOrDefault(r => r.IsEnabled);
            if (next != null)
                next.IsPrimary = true;
        }

        await SaveRegionsAsync();
        return true;
    }

    public async Task<RegionConfiguration> SetPrimary(string id)
    {
        await EnsureLoadedAsync();
        if (!_regions.TryGetValue(id, out var region)) return null;

        foreach (var r in _regions.Values)
            r.IsPrimary = r.Id == id;

        await SaveRegionsAsync();
        _logger.LogInformation("Set region {Name} as primary", region.Name);
        return region;
    }

    public async Task<RegionConfiguration> ToggleEnabled(string id, bool enabled)
    {
        await EnsureLoadedAsync();
        if (!_regions.TryGetValue(id, out var region)) return null;

        region.IsEnabled = enabled;
        await SaveRegionsAsync();
        return region;
    }

    public async Task<Dictionary<string, RegionHealthStatus>> CheckHealth()
    {
        await EnsureLoadedAsync();
        var results = new Dictionary<string, RegionHealthStatus>();

        var checkTasks = _regions.Values
            .Where(r => r.IsEnabled)
            .Select(r => CheckRegionHealthAsync(r))
            .ToList();

        await Task.WhenAll(checkTasks);

        foreach (var region in _regions.Values.Where(r => r.IsEnabled))
            results[region.Id] = region.Health;

        await SaveRegionsAsync();
        return results;
    }

    private async Task CheckRegionHealthAsync(RegionConfiguration region)
    {
        var checkUrl = !string.IsNullOrWhiteSpace(region.HealthCheckUrl)
            ? region.HealthCheckUrl
            : region.CosmosDbEndpoint;

        if (string.IsNullOrWhiteSpace(checkUrl))
        {
            region.Health = RegionHealthStatus.Unknown;
            region.LastHealthCheck = DateTimeOffset.UtcNow;
            return;
        }

        var sw = Stopwatch.StartNew();
        try
        {
            using var cts = new CancellationTokenSource(HealthCheckTimeout);
            var client = _httpClientFactory.CreateClient("RegionHealthCheck");
            client.Timeout = HealthCheckTimeout;

            // Use HEAD first; fall back to GET if the server doesn't support HEAD
            HttpResponseMessage response = null;
            try
            {
                response = await client.SendAsync(
                    new HttpRequestMessage(HttpMethod.Head, checkUrl),
                    cts.Token);
            }
            catch (HttpRequestException)
            {
                response = await client.GetAsync(checkUrl, cts.Token);
            }

            sw.Stop();
            region.LastLatencyMs = sw.Elapsed.TotalMilliseconds;

            if (response.IsSuccessStatusCode || (int)response.StatusCode < 500)
            {
                region.Health = sw.Elapsed.TotalMilliseconds > DegradedLatencyMs
                    ? RegionHealthStatus.Degraded
                    : RegionHealthStatus.Healthy;
            }
            else
            {
                region.Health = RegionHealthStatus.Degraded;
            }

            _logger.LogDebug("Region {Name} health: {Status} ({Latency:F0}ms)", region.Name, region.Health, region.LastLatencyMs);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            region.LastLatencyMs = sw.Elapsed.TotalMilliseconds;
            region.Health = RegionHealthStatus.Offline;
            _logger.LogWarning("Region {Name} health check timed out after {Timeout}s", region.Name, HealthCheckTimeout.TotalSeconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            region.LastLatencyMs = sw.Elapsed.TotalMilliseconds;
            region.Health = RegionHealthStatus.Offline;
            _logger.LogWarning(ex, "Region {Name} health check failed", region.Name);
        }
        finally
        {
            region.LastHealthCheck = DateTimeOffset.UtcNow;
        }
    }
}
