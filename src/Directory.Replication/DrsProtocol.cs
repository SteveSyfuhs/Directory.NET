using Directory.Core.Interfaces;
using Directory.Core.Models;
using Microsoft.Extensions.Logging;

namespace Directory.Replication;

/// <summary>
/// Directory Replication Service protocol per [MS-DRSR].
/// Handles GetNCChanges and DsReplicaSync operations between DCs.
/// </summary>
public class DrsProtocol
{
    private readonly IDirectoryStore _store;
    private readonly DcInstanceInfo _dcInfo;
    private readonly ILogger<DrsProtocol> _logger;

    /// <summary>
    /// The persisted invocation ID for this DC instance. Loaded on first access from the
    /// configuration store, or generated and persisted if not yet present.
    /// </summary>
    private string _invocationId;
    private readonly SemaphoreSlim _invocationIdLock = new(1, 1);

    /// <summary>
    /// Well-known DN used to persist the invocation ID in the directory store.
    /// Stored as a configuration object under CN=Configuration.
    /// </summary>
    private const string InvocationIdConfigDn = "CN=InvocationId,CN=DcConfig,CN=Configuration";

    public DrsProtocol(IDirectoryStore store, DcInstanceInfo dcInfo, ILogger<DrsProtocol> logger)
    {
        _store = store;
        _dcInfo = dcInfo;
        _logger = logger;
    }

    /// <summary>
    /// Process a GetNCChanges request — return all changes since a given USN
    /// for a specific naming context.
    /// </summary>
    public async Task<GetNCChangesResponse> GetNCChangesAsync(
        GetNCChangesRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("GetNCChanges: NC={NC}, USNFrom={USN}", request.NamingContextDn, request.UsnFrom);

        var results = await _store.SearchAsync(
            request.TenantId,
            request.NamingContextDn,
            SearchScope.WholeSubtree,
            new GreaterOrEqualFilterNode("uSNChanged", request.UsnFrom.ToString()),
            null, request.MaxObjects, 0, null, request.MaxObjects > 0 ? request.MaxObjects : 10000,
            true, ct);

        var entries = new List<ReplicationEntry>();
        long maxUsn = request.UsnFrom;

        foreach (var obj in results.Entries)
        {
            entries.Add(new ReplicationEntry
            {
                Dn = obj.DistinguishedName,
                ObjectGuid = obj.ObjectGuid,
                ObjectClass = obj.ObjectClass,
                UsnOriginating = obj.USNChanged,
                IsDeleted = obj.IsDeleted,
                Attributes = obj.Attributes.ToDictionary(
                    a => a.Key,
                    a => a.Value.Values.Select(v => v?.ToString() ?? "").ToList()),
            });

            if (obj.USNChanged > maxUsn)
                maxUsn = obj.USNChanged;
        }

        return new GetNCChangesResponse
        {
            Entries = entries,
            HighestUsn = maxUsn,
            MoreData = results.ContinuationToken != null,
            UpToDateVector = new UpToDateVector
            {
                Entries =
                [
                    new UpToDateVectorEntry
                    {
                        InvocationId = await GetInvocationIdAsync(ct),
                        UsnHighPropUpdate = maxUsn,
                        TimeLastSyncSuccess = DateTimeOffset.UtcNow,
                    }
                ],
            },
        };
    }

    /// <summary>
    /// Process a DsReplicaSync request — trigger replication from a source DC.
    /// In our architecture, this triggers change feed processing.
    /// </summary>
    public Task<DsReplicaSyncResult> DsReplicaSyncAsync(
        DsReplicaSyncRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("DsReplicaSync: NC={NC}, Source={Source}, Urgent={Urgent}",
            request.NamingContextDn, request.SourceDsaDn, request.IsUrgent);

        // In a Cosmos DB-backed system, replication is handled by the change feed processor.
        // This endpoint acknowledges the request and the change feed will pick up changes.
        return Task.FromResult(new DsReplicaSyncResult { Success = true });
    }

    /// <summary>
    /// Returns the persisted invocation ID for this DC instance.
    /// On first run, generates a new GUID and persists it to the directory store.
    /// On subsequent starts, loads the persisted value.
    /// If a USN mismatch is detected (backup restore scenario), regenerates the invocation ID.
    /// </summary>
    private async Task<string> GetInvocationIdAsync(CancellationToken ct = default)
    {
        if (_invocationId != null)
            return _invocationId;

        await _invocationIdLock.WaitAsync(ct);
        try
        {
            // Double-check after acquiring lock
            if (_invocationId != null)
                return _invocationId;

            // Try to load persisted invocation ID from the directory store
            var configDn = $"{InvocationIdConfigDn},{_dcInfo.Hostname}";
            var configObj = await _store.GetByDnAsync("default", configDn, ct);

            if (configObj != null)
            {
                // Check for backup-restore scenario: if the stored USN high-water mark
                // is higher than our current DC's USN, the DC was restored from backup
                // and we must regenerate the invocation ID to avoid replication conflicts.
                var storedInvocationId = configObj.GetAttribute("invocationId")?.GetFirstString();
                var storedUsnStr = configObj.GetAttribute("highestCommittedUsn")?.GetFirstString();
                long storedUsn = 0;
                if (storedUsnStr != null)
                    long.TryParse(storedUsnStr, out storedUsn);

                var currentUsn = await _store.GetNextUsnAsync("default", "", ct);

                if (storedUsn > 0 && currentUsn < storedUsn)
                {
                    // USN mismatch detected — DC was likely restored from backup.
                    // Regenerate invocation ID to signal replication partners that
                    // all changes must be re-evaluated.
                    _logger.LogWarning(
                        "USN mismatch detected (stored={StoredUsn}, current={CurrentUsn}). " +
                        "DC may have been restored from backup. Regenerating invocation ID.",
                        storedUsn, currentUsn);

                    _invocationId = Guid.NewGuid().ToString();
                    await PersistInvocationIdAsync(configDn, _invocationId, currentUsn, ct);
                }
                else if (!string.IsNullOrEmpty(storedInvocationId))
                {
                    _invocationId = storedInvocationId;
                    _logger.LogInformation("Loaded persisted invocation ID: {InvocationId}", _invocationId);
                }
                else
                {
                    // Object exists but no invocation ID stored — generate one
                    _invocationId = Guid.NewGuid().ToString();
                    await PersistInvocationIdAsync(configDn, _invocationId, currentUsn, ct);
                }
            }
            else
            {
                // First run — generate and persist a new invocation ID
                _invocationId = Guid.NewGuid().ToString();
                var currentUsn = await _store.GetNextUsnAsync("default", "", ct);

                var newConfig = new DirectoryObject
                {
                    Id = configDn.ToLowerInvariant(),
                    TenantId = "default",
                    DistinguishedName = configDn,
                    ObjectClass = new List<string> { "top", "configuration" },
                    ObjectCategory = "configuration",
                    Cn = "InvocationId",
                };
                newConfig.SetAttribute("invocationId", new DirectoryAttribute("invocationId", _invocationId));
                newConfig.SetAttribute("highestCommittedUsn", new DirectoryAttribute("highestCommittedUsn", currentUsn.ToString()));
                newConfig.SetAttribute("hostname", new DirectoryAttribute("hostname", _dcInfo.Hostname));

                try
                {
                    await _store.CreateAsync(newConfig, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to persist invocation ID; using in-memory value");
                }

                _logger.LogInformation("Generated and persisted new invocation ID: {InvocationId}", _invocationId);
            }

            // Also update DcInstanceInfo so other components see the persisted value
            _dcInfo.InvocationId = _invocationId;
            return _invocationId;
        }
        finally
        {
            _invocationIdLock.Release();
        }
    }

    /// <summary>
    /// Persists the invocation ID and current USN high-water mark to the directory store.
    /// </summary>
    private async Task PersistInvocationIdAsync(string configDn, string invocationId, long currentUsn, CancellationToken ct)
    {
        try
        {
            var configObj = await _store.GetByDnAsync("default", configDn, ct);
            if (configObj != null)
            {
                configObj.SetAttribute("invocationId", new DirectoryAttribute("invocationId", invocationId));
                configObj.SetAttribute("highestCommittedUsn", new DirectoryAttribute("highestCommittedUsn", currentUsn.ToString()));
                await _store.UpdateAsync(configObj, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist invocation ID update");
        }
    }
}

public class GetNCChangesRequest
{
    public string TenantId { get; init; } = "default";
    public string NamingContextDn { get; init; } = "";
    public long UsnFrom { get; init; }
    public int MaxObjects { get; init; } = 1000;
    public int MaxBytes { get; init; } = 10_000_000;
    public UpToDateVector PartnerUpToDateVector { get; init; }
}

public class GetNCChangesResponse
{
    public IReadOnlyList<ReplicationEntry> Entries { get; init; } = [];
    public long HighestUsn { get; init; }
    public bool MoreData { get; init; }
    public UpToDateVector UpToDateVector { get; init; } = new();
}

public class ReplicationEntry
{
    public string Dn { get; init; } = "";
    public string ObjectGuid { get; init; } = "";
    public List<string> ObjectClass { get; init; } = [];
    public long UsnOriginating { get; init; }
    public bool IsDeleted { get; init; }
    public Dictionary<string, List<string>> Attributes { get; init; } = [];
}

public class DsReplicaSyncRequest
{
    public string TenantId { get; init; } = "default";
    public string NamingContextDn { get; init; } = "";
    public string SourceDsaDn { get; init; } = "";
    public bool IsUrgent { get; init; }
}

public class DsReplicaSyncResult
{
    public bool Success { get; init; }
    public string ErrorMessage { get; init; }
}

/// <summary>
/// Up-to-dateness vector per [MS-DRSR] section 5.165.
/// Tracks the highest USN processed from each replication partner.
/// </summary>
public class UpToDateVector
{
    public List<UpToDateVectorEntry> Entries { get; init; } = [];
}

public class UpToDateVectorEntry
{
    public string InvocationId { get; init; } = "";
    public long UsnHighPropUpdate { get; init; }
    public DateTimeOffset TimeLastSyncSuccess { get; init; }
}
