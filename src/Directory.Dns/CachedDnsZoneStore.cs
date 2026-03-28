using Directory.Core.Caching;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Directory.Dns;

/// <summary>
/// Caching decorator for DnsZoneStore. DNS zone data is relatively static
/// but queried thousands of times per second. This caches all record lookups
/// with a configurable TTL (default 5 minutes).
/// </summary>
public class CachedDnsZoneStore
{
    private readonly DnsZoneStore _inner;
    private readonly IMemoryCache _cache;
    private readonly TimeSpan _ttl;

    public CachedDnsZoneStore(DnsZoneStore inner, IMemoryCache cache, IOptions<CacheOptions> options)
    {
        _inner = inner;
        _cache = cache;
        _ttl = TimeSpan.FromSeconds(options.Value.DnsZoneTtlSeconds);
    }

    public async Task<DnsRecord> GetRecordAsync(
        string tenantId, string zoneName, string name, DnsRecordType type, CancellationToken ct = default)
    {
        var key = $"dns:{zoneName}:{name}:{type}";

        if (_cache.TryGetValue(key, out DnsRecord cached))
            return cached;

        var record = await _inner.GetRecordAsync(tenantId, zoneName, name, type, ct);

        if (record is not null)
            _cache.Set(key, record, _ttl);

        return record;
    }

    /// <summary>
    /// Returns all records for a given name and type from cache or backing store.
    /// Supports multi-valued records (multiple SRV records per name from different DCs).
    /// </summary>
    public async Task<IReadOnlyList<DnsRecord>> GetRecordsByNameAsync(
        string tenantId, string zoneName, string name, DnsRecordType type, CancellationToken ct = default)
    {
        var key = $"dnsbyname:{zoneName}:{name}:{type}";

        if (_cache.TryGetValue(key, out IReadOnlyList<DnsRecord> cached))
            return cached;

        var records = await _inner.GetRecordsByNameAsync(tenantId, zoneName, name, type, ct);

        _cache.Set(key, records, _ttl);

        return records;
    }

    public async Task<IReadOnlyList<DnsRecord>> GetAllRecordsAsync(
        string tenantId, string zoneName, DnsRecordType type, CancellationToken ct = default)
    {
        var key = $"dnsall:{zoneName}:{type}";

        if (_cache.TryGetValue(key, out IReadOnlyList<DnsRecord> cached))
            return cached;

        var records = await _inner.GetAllRecordsAsync(tenantId, zoneName, type, ct);

        _cache.Set(key, records, _ttl);

        return records;
    }

    public async Task UpsertRecordAsync(
        string tenantId, string zoneName, DnsRecord record, CancellationToken ct = default)
    {
        await _inner.UpsertRecordAsync(tenantId, zoneName, record, ct);

        // Invalidate caches for this record
        _cache.Remove($"dns:{zoneName}:{record.Name}:{record.Type}");
        _cache.Remove($"dnsbyname:{zoneName}:{record.Name}:{record.Type}");
        _cache.Remove($"dnsall:{zoneName}:{record.Type}");
    }

    public async Task DeleteRecordByDataAsync(
        string tenantId, string zoneName, string name, DnsRecordType type, string data, CancellationToken ct = default)
    {
        await _inner.DeleteRecordByDataAsync(tenantId, zoneName, name, type, data, ct);

        // Invalidate caches
        _cache.Remove($"dns:{zoneName}:{name}:{type}");
        _cache.Remove($"dnsbyname:{zoneName}:{name}:{type}");
        _cache.Remove($"dnsall:{zoneName}:{type}");
    }

    public async Task DeleteRecordAsync(
        string tenantId, string zoneName, string name, CancellationToken ct = default)
    {
        await _inner.DeleteRecordAsync(tenantId, zoneName, name, ct);

        // Invalidate all type variants for this record name
        foreach (DnsRecordType type in Enum.GetValues<DnsRecordType>())
        {
            _cache.Remove($"dns:{zoneName}:{name}:{type}");
            _cache.Remove($"dnsbyname:{zoneName}:{name}:{type}");
            _cache.Remove($"dnsall:{zoneName}:{type}");
        }
    }
}
