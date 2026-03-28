using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Directory.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace Directory.Security;

public class RidAllocator : IRidAllocator
{
    private const int DefaultPoolSize = 500;

    private readonly IDirectoryStore _store;
    private readonly ILogger<RidAllocator> _logger;
    private readonly ConcurrentDictionary<(string TenantId, string DomainDn), RidPool> _pools = new();
    private readonly SemaphoreSlim _allocLock = new(1, 1);

    public RidAllocator(IDirectoryStore store, ILogger<RidAllocator> logger)
    {
        _store = store;
        _logger = logger;
    }

    public async Task<long> AllocateRidAsync(string tenantId, string domainDn, CancellationToken ct = default)
    {
        var key = (tenantId, domainDn.ToLowerInvariant());

        if (_pools.TryGetValue(key, out var pool))
        {
            long rid = pool.TryAllocate();
            if (rid > 0)
                return rid;
        }

        // Pool exhausted or not yet created — claim a new block from Cosmos DB
        await _allocLock.WaitAsync(ct);
        try
        {
            // Double-check after acquiring lock
            if (_pools.TryGetValue(key, out pool))
            {
                long rid = pool.TryAllocate();
                if (rid > 0)
                    return rid;
            }

            // Atomically claim a non-overlapping pool block via Cosmos DB
            long poolStart = await _store.ClaimRidPoolAsync(tenantId, domainDn, DefaultPoolSize, ct);

            var newPool = new RidPool(poolStart, poolStart + DefaultPoolSize - 1);
            _pools[key] = newPool;

            _logger.LogInformation("Claimed RID pool [{Start}-{End}] for {Tenant}/{Domain} from Cosmos DB",
                poolStart, poolStart + DefaultPoolSize - 1, tenantId, domainDn);

            return newPool.TryAllocate();
        }
        finally
        {
            _allocLock.Release();
        }
    }

    public async Task<string> GenerateObjectSidAsync(string tenantId, string domainDn, CancellationToken ct = default)
    {
        string domainSid = GetDomainSid(tenantId, domainDn);
        long rid = await AllocateRidAsync(tenantId, domainDn, ct);
        return $"{domainSid}-{rid}";
    }

    public string GetDomainSid(string tenantId, string domainDn)
    {
        // Compute deterministic sub-authorities from hash of "{tenantId}:{domainDn}"
        string input = $"{tenantId}:{domainDn}".ToLowerInvariant();
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));

        uint sub1 = BitConverter.ToUInt32(hash, 0);
        uint sub2 = BitConverter.ToUInt32(hash, 4);
        uint sub3 = BitConverter.ToUInt32(hash, 8);

        return $"S-1-5-21-{sub1}-{sub2}-{sub3}";
    }

    private sealed class RidPool
    {
        private long _next;
        private readonly long _end;

        public RidPool(long start, long end)
        {
            _next = start;
            _end = end;
        }

        public long TryAllocate()
        {
            long value = Interlocked.Increment(ref _next) - 1;
            return value <= _end ? value : -1;
        }
    }
}
