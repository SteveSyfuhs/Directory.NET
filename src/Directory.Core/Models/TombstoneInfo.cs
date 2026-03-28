namespace Directory.Core.Models;

public class TombstoneInfo
{
    public bool IsDeleted { get; set; }
    public bool IsRecycled { get; set; }
    public DateTimeOffset? DeletedTime { get; set; }
    public string LastKnownParent { get; set; }
    public string LastKnownDn { get; set; }
    public TimeSpan TombstoneLifetime { get; set; } = TimeSpan.FromDays(180);
    public bool RecycleBinEnabled { get; set; }

    public bool HasExpired()
        => HasExpiredAsOf(DateTimeOffset.UtcNow);

    public bool HasExpiredAsOf(DateTimeOffset asOf)
    {
        if (!IsDeleted || DeletedTime is null)
            return false;

        if (RecycleBinEnabled && !IsRecycled)
            return false;

        return asOf - DeletedTime.Value >= TombstoneLifetime;
    }

    public DateTimeOffset? ExpirationTime
        => DeletedTime?.Add(TombstoneLifetime);

    public void MarkDeleted(string parentDn, string originalDn)
    {
        IsDeleted = true;
        DeletedTime = DateTimeOffset.UtcNow;
        LastKnownParent = parentDn;
        LastKnownDn = originalDn;
    }

    public void MarkRecycled()
    {
        if (!IsDeleted)
            throw new InvalidOperationException("Object must be deleted before it can be recycled.");

        IsRecycled = true;
    }
}
