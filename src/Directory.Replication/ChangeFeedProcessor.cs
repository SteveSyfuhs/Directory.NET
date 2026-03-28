using System.Collections.Concurrent;
using Directory.Core.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DcInstanceInfo = Directory.Core.Models.DcInstanceInfo;

namespace Directory.Replication;

/// <summary>
/// Monitors the Cosmos DB change feed for the DirectoryObjects container.
/// Writes change log entries and enables DirSync-style replication.
/// Supports urgent replication detection, originating/replicated USN tracking,
/// and up-to-dateness vector management per [MS-DRSR].
/// </summary>
public class ChangeFeedProcessorService : IHostedService
{
    private readonly CosmosClient _client;
    private readonly ReplicationOptions _options;
    private readonly ILogger<ChangeFeedProcessorService> _logger;
    private Microsoft.Azure.Cosmos.ChangeFeedProcessor _processor;

    /// <summary>
    /// Up-to-dateness vector: tracks the highest USN processed from each replication partner.
    /// Key is the invocation ID, value is the highest USN seen from that partner.
    /// </summary>
    private readonly ConcurrentDictionary<string, UpToDateVectorEntry> _upToDateVector = new();

    /// <summary>
    /// The invocation ID of this DC instance.
    /// </summary>
    private readonly string _localInvocationId;

    /// <summary>
    /// Attributes that trigger urgent replication per [MS-DRSR] section 5.166.
    /// Password changes, account lockouts, and trust changes require immediate replication.
    /// </summary>
    private static readonly HashSet<string> UrgentReplicationAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "unicodePwd",
        "dBCSPwd",
        "lockoutTime",
        "pwdLastSet",
        "userAccountControl",
        "trustAuthIncoming",
        "trustAuthOutgoing",
        "msDS-TrustForestTrustInfo",
    };

    /// <summary>
    /// Object classes that trigger urgent replication when created or modified.
    /// </summary>
    private static readonly HashSet<string> UrgentReplicationObjectClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "secret",
        "trustedDomain",
    };

    private readonly DcInstanceInfo _dcInfo;

    public ChangeFeedProcessorService(
        CosmosClient client,
        IOptions<ReplicationOptions> options,
        DcInstanceInfo dcInfo,
        ILogger<ChangeFeedProcessorService> logger)
    {
        _client = client;
        _options = options.Value;
        _dcInfo = dcInfo;
        _localInvocationId = dcInfo.InvocationId;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var database = _client.GetDatabase(_options.DatabaseName);
        var sourceContainer = database.GetContainer("DirectoryObjects");
        var leaseContainer = database.GetContainer("ChangeLog");

        // Ensure lease container exists
        try
        {
            await database.CreateContainerIfNotExistsAsync(
                new ContainerProperties("ChangeFeedLeases", "/id"),
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not create lease container; it may already exist");
        }

        var leasesContainer = database.GetContainer("ChangeFeedLeases");

        _processor = sourceContainer
            .GetChangeFeedProcessorBuilder<DirectoryObject>(
                "DirectoryReplication",
                HandleChangesAsync)
            .WithInstanceName($"{_dcInfo.Hostname}-{_dcInfo.InstanceId}")
            .WithLeaseContainer(leasesContainer)
            .WithStartTime(DateTime.UtcNow.AddMinutes(-5))
            .Build();

        await _processor.StartAsync();

        _logger.LogInformation("Change feed processor started for DirectoryObjects");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_processor is not null)
        {
            await _processor.StopAsync();
            _logger.LogInformation("Change feed processor stopped");
        }
    }

    /// <summary>
    /// Get the current up-to-dateness vector for this DC.
    /// </summary>
    public UpToDateVector GetUpToDateVector()
    {
        return new UpToDateVector
        {
            Entries = _upToDateVector.Values.ToList(),
        };
    }

    /// <summary>
    /// Check whether a change should be filtered based on the partner's up-to-dateness vector.
    /// Returns true if the change has already been seen by the partner.
    /// </summary>
    public bool IsChangeAlreadySeen(UpToDateVector partnerVector, string invocationId, long usn)
    {
        if (partnerVector == null) return false;

        var entry = partnerVector.Entries.FirstOrDefault(
            e => e.InvocationId.Equals(invocationId, StringComparison.OrdinalIgnoreCase));

        return entry != null && usn <= entry.UsnHighPropUpdate;
    }

    private async Task HandleChangesAsync(
        ChangeFeedProcessorContext context,
        IReadOnlyCollection<DirectoryObject> changes,
        CancellationToken ct)
    {
        _logger.LogDebug("Processing {Count} changes from partition {Partition}",
            changes.Count, context.LeaseToken);

        var database = _client.GetDatabase(_options.DatabaseName);
        var changeLogContainer = database.GetContainer("ChangeLog");

        foreach (var change in changes)
        {
            var isUrgent = DetectUrgentReplication(change);
            var originatingInvocationId = GetOriginatingInvocationId(change);
            var originatingUsn = GetOriginatingUsn(change);

            var entry = new ChangeLogEntry
            {
                TenantId = change.TenantId,
                DomainDn = change.DomainDn,
                Usn = change.USNChanged,
                ObjectDn = change.DistinguishedName,
                ChangeType = change.IsDeleted ? "delete" : "modify",
                Timestamp = change.WhenChanged,
                OriginatingUsn = originatingUsn,
                ReplicatedUsn = change.USNChanged,
                InvocationId = originatingInvocationId,
            };

            if (isUrgent)
            {
                _logger.LogWarning("Urgent replication triggered for {DN} (type={ChangeType})",
                    change.DistinguishedName, entry.ChangeType);
            }

            try
            {
                var pk = new PartitionKeyBuilder()
                    .Add(entry.TenantId)
                    .Add(entry.DomainDn)
                    .Build();

                await changeLogContainer.CreateItemAsync(entry, pk, cancellationToken: ct);

                // Update up-to-dateness vector
                UpdateUpToDateVector(originatingInvocationId, originatingUsn);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error writing change log entry for {DN}", change.DistinguishedName);
            }
        }
    }

    /// <summary>
    /// Detect whether a change requires urgent replication.
    /// Per [MS-DRSR], password changes, account lockouts, and trust modifications
    /// trigger immediate replication rather than waiting for the normal schedule.
    /// </summary>
    private bool DetectUrgentReplication(DirectoryObject change)
    {
        // Check if any urgent object class is present
        if (change.ObjectClass.Any(oc => UrgentReplicationObjectClasses.Contains(oc)))
        {
            _logger.LogDebug("Urgent replication: object class match for {DN}", change.DistinguishedName);
            return true;
        }

        // Check if any urgent attributes were changed
        foreach (var attr in change.Attributes)
        {
            if (UrgentReplicationAttributes.Contains(attr.Key))
            {
                _logger.LogDebug("Urgent replication: attribute {Attr} changed on {DN}", attr.Key, change.DistinguishedName);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Get the originating invocation ID from the change metadata.
    /// If the change originated locally, use this DC's invocation ID.
    /// </summary>
    private string GetOriginatingInvocationId(DirectoryObject change)
    {
        var attr = change.GetAttribute("replPropertyMetaData");
        if (attr != null)
        {
            var metaData = attr.GetFirstString();
            if (!string.IsNullOrEmpty(metaData) && metaData.Contains('|'))
            {
                // Format: "invocationId|usn"
                var parts = metaData.Split('|');
                if (parts.Length >= 1 && !string.IsNullOrEmpty(parts[0]))
                    return parts[0];
            }
        }
        return _localInvocationId;
    }

    /// <summary>
    /// Get the originating USN from the change metadata.
    /// For locally-originated changes, this is the same as USNChanged.
    /// </summary>
    private static long GetOriginatingUsn(DirectoryObject change)
    {
        var attr = change.GetAttribute("replPropertyMetaData");
        if (attr != null)
        {
            var metaData = attr.GetFirstString();
            if (!string.IsNullOrEmpty(metaData) && metaData.Contains('|'))
            {
                var parts = metaData.Split('|');
                if (parts.Length >= 2 && long.TryParse(parts[1], out var usn))
                    return usn;
            }
        }
        return change.USNChanged;
    }

    /// <summary>
    /// Update the up-to-dateness vector with the latest USN from a replication partner.
    /// </summary>
    private void UpdateUpToDateVector(string invocationId, long usn)
    {
        _upToDateVector.AddOrUpdate(
            invocationId,
            _ => new UpToDateVectorEntry
            {
                InvocationId = invocationId,
                UsnHighPropUpdate = usn,
                TimeLastSyncSuccess = DateTimeOffset.UtcNow,
            },
            (_, existing) =>
            {
                if (usn > existing.UsnHighPropUpdate)
                {
                    return new UpToDateVectorEntry
                    {
                        InvocationId = invocationId,
                        UsnHighPropUpdate = usn,
                        TimeLastSyncSuccess = DateTimeOffset.UtcNow,
                    };
                }
                return existing;
            });
    }
}

public class ReplicationOptions
{
    public const string SectionName = "Replication";

    public string DatabaseName { get; set; } = "DirectoryService";
    public int HttpPort { get; set; } = 9389;
}
