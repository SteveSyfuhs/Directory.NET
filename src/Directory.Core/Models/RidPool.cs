namespace Directory.Core.Models;

public class RidPool
{
    private readonly object _lock = new();

    public required string TenantId { get; set; }
    public required string DomainDn { get; set; }
    public long NextRid { get; set; } = 1000;
    public long RidPoolEnd { get; set; } = 1_073_741_823; // 2^30 - 1

    public long AllocateRid()
    {
        lock (_lock)
        {
            if (NextRid > RidPoolEnd)
                throw new InvalidOperationException("RID pool exhausted. No more RIDs available for allocation.");

            return NextRid++;
        }
    }

    public long[] AllocateRidBlock(int count)
    {
        if (count <= 0)
            throw new ArgumentOutOfRangeException(nameof(count), "Count must be positive.");

        lock (_lock)
        {
            if (NextRid + count - 1 > RidPoolEnd)
                throw new InvalidOperationException($"RID pool cannot satisfy allocation of {count} RIDs. Remaining: {RidPoolEnd - NextRid + 1}.");

            var rids = new long[count];
            for (int i = 0; i < count; i++)
                rids[i] = NextRid++;

            return rids;
        }
    }

    public long RemainingRids => Math.Max(0, RidPoolEnd - NextRid + 1);

    public string BuildObjectSid(string domainSid, long rid) => $"{domainSid}-{rid}";

    public string AllocateObjectSid(string domainSid) => BuildObjectSid(domainSid, AllocateRid());
}
