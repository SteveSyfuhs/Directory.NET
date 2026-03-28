using Directory.Core.Models;

namespace Directory.Core.Interfaces;

public interface IAuditService
{
    Task LogAsync(AuditEntry entry, CancellationToken ct = default);
    Task<(IReadOnlyList<AuditEntry> Items, string ContinuationToken)> QueryAsync(
        string tenantId, string action = null, string targetDn = null,
        DateTimeOffset? from = null, DateTimeOffset? to = null,
        int pageSize = 50, string continuationToken = null, CancellationToken ct = default);
    Task<AuditEntry> GetByIdAsync(string tenantId, string id, CancellationToken ct = default);
}

/// <summary>
/// Optional extension for audit service implementations that support explicit entry deletion.
/// Implementations that rely on TTL-based expiry need not implement this interface.
/// </summary>
public interface IAuditServiceWithDelete
{
    Task DeleteAsync(string id, string tenantId, CancellationToken ct = default);
}
