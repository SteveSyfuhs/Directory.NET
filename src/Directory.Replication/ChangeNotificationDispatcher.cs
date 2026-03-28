using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Replication.Drsr;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Directory.Replication;

/// <summary>
/// Hosted service that monitors local changes (via the Cosmos DB change feed) and
/// sends lightweight HTTP notifications to outbound replication partners so they
/// can schedule their own pull cycles.
///
/// Per [MS-DRSR], when a DC makes a local change it notifies all partners that
/// have registered via UpdateRefs. The partner then calls GetNCChanges to pull.
///
/// Urgent changes (password, lockout, trust) trigger immediate notification.
/// Notifications are deduplicated: the same NC notification is suppressed if
/// sent within the last second to avoid spamming partners.
/// </summary>
public class ChangeNotificationDispatcher : IHostedService, IDisposable
{
    private readonly ReplicationTopology _topology;
    private readonly DcInstanceInfo _dcInfo;
    private readonly INamingContextService _ncService;
    private readonly ReplicationOptions _replicationOptions;
    private readonly ILogger<ChangeNotificationDispatcher> _logger;

    private readonly CosmosClient _cosmosClient;
    private Microsoft.Azure.Cosmos.ChangeFeedProcessor _processor;
    private CancellationTokenSource _cts;

    /// <summary>
    /// HTTP client for sending notification POSTs to partner DCs.
    /// </summary>
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Tracks last notification time per (partnerDsaGuid, namingContextDn) to deduplicate.
    /// Notifications for the same NC to the same partner are suppressed if within the dedup window.
    /// </summary>
    private readonly ConcurrentDictionary<(Guid DsaGuid, string NcDn), DateTimeOffset> _lastNotificationTime = new();

    /// <summary>
    /// Minimum interval between notifications for the same partner+NC combination.
    /// </summary>
    private static readonly TimeSpan DeduplicationWindow = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Attributes that trigger urgent replication per [MS-DRSR] section 5.166.
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
    /// Object classes that trigger urgent replication.
    /// </summary>
    private static readonly HashSet<string> UrgentReplicationObjectClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "secret",
        "trustedDomain",
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public ChangeNotificationDispatcher(
        CosmosClient cosmosClient,
        ReplicationTopology topology,
        DcInstanceInfo dcInfo,
        INamingContextService ncService,
        IOptions<ReplicationOptions> replicationOptions,
        ILogger<ChangeNotificationDispatcher> logger)
    {
        _cosmosClient = cosmosClient;
        _topology = topology;
        _dcInfo = dcInfo;
        _ncService = ncService;
        _replicationOptions = replicationOptions.Value;
        _logger = logger;

        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10),
        };
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            var database = _cosmosClient.GetDatabase(_replicationOptions.DatabaseName);
            var sourceContainer = database.GetContainer("DirectoryObjects");

            // Create a dedicated lease container for change notifications
            try
            {
                await database.CreateContainerIfNotExistsAsync(
                    new ContainerProperties("ChangeNotificationLeases", "/id"),
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not create change notification lease container; it may already exist");
            }

            var leasesContainer = database.GetContainer("ChangeNotificationLeases");

            _processor = sourceContainer
                .GetChangeFeedProcessorBuilder<DirectoryObject>(
                    "ChangeNotificationDispatcher",
                    HandleChangesAsync)
                .WithInstanceName($"{_dcInfo.Hostname}-notify-{_dcInfo.InstanceId}")
                .WithLeaseContainer(leasesContainer)
                .WithStartTime(DateTime.UtcNow)
                .Build();

            await _processor.StartAsync();

            _logger.LogInformation("ChangeNotificationDispatcher started — monitoring local changes for outbound notification");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start ChangeNotificationDispatcher; outbound notifications disabled");
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();

        if (_processor is not null)
        {
            try
            {
                await _processor.StopAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error stopping change notification processor");
            }
        }

        _logger.LogInformation("ChangeNotificationDispatcher stopped");
    }

    /// <summary>
    /// Handles a batch of changes from the Cosmos DB change feed.
    /// For each changed object, determines the naming context and notifies outbound partners.
    /// </summary>
    private async Task HandleChangesAsync(
        ChangeFeedProcessorContext context,
        IReadOnlyCollection<DirectoryObject> changes,
        CancellationToken ct)
    {
        if (changes.Count == 0) return;

        _logger.LogDebug(
            "ChangeNotificationDispatcher processing {Count} changes from partition {Partition}",
            changes.Count, context.LeaseToken);

        // Group changes by naming context to minimize notification count
        var ncChanges = new Dictionary<string, NcChangeInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var change in changes)
        {
            var ncDn = DetermineNamingContext(change);
            if (string.IsNullOrEmpty(ncDn)) continue;

            if (!ncChanges.TryGetValue(ncDn, out var info))
            {
                info = new NcChangeInfo { NamingContextDn = ncDn };
                ncChanges[ncDn] = info;
            }

            // Track highest USN per NC
            if (change.USNChanged > info.HighestUsn)
                info.HighestUsn = change.USNChanged;

            // Detect urgent changes
            if (!info.IsUrgent)
                info.IsUrgent = IsUrgentChange(change);
        }

        // Send notifications per NC
        foreach (var (ncDn, info) in ncChanges)
        {
            await NotifyOutboundPartnersAsync(ncDn, info.HighestUsn, info.IsUrgent, ct);
        }
    }

    /// <summary>
    /// Sends a notification to all outbound partners for a naming context.
    /// </summary>
    private async Task NotifyOutboundPartnersAsync(
        string namingContextDn, long latestUsn, bool isUrgent, CancellationToken ct)
    {
        var partners = _topology.GetOutboundPartners(namingContextDn);
        if (partners.Count == 0) return;

        // Prepare the notification payload
        var sourceDsaGuid = Guid.TryParse(_dcInfo.InvocationId, out var g) ? g : Guid.NewGuid();

        var notification = new ChangeNotification
        {
            SourceDsaGuid = sourceDsaGuid.ToString(),
            NamingContextDn = namingContextDn,
            LatestUsn = latestUsn,
        };

        foreach (var partner in partners)
        {
            ct.ThrowIfCancellationRequested();

            // Skip partners with NO_CHANGE_NOTIFICATIONS flag (unless urgent)
            if (!isUrgent && partner.Options.HasFlag(DsReplNeighborFlags.DS_REPL_NBR_NO_CHANGE_NOTIFICATIONS))
                continue;

            // Deduplication: skip if we notified this partner+NC too recently
            var dedupKey = (partner.DsaGuid, namingContextDn);
            if (!isUrgent && _lastNotificationTime.TryGetValue(dedupKey, out var lastTime) &&
                DateTimeOffset.UtcNow - lastTime < DeduplicationWindow)
            {
                _logger.LogDebug(
                    "Suppressing duplicate notification to {DnsName} for NC {NC} (within dedup window)",
                    partner.DnsName, namingContextDn);
                continue;
            }

            // Send the notification asynchronously, but don't await failure
            _ = SendNotificationAsync(partner, notification, dedupKey, ct);
        }
    }

    /// <summary>
    /// Sends an HTTP POST notification to a single partner DC.
    /// Failures are logged and swallowed — the partner will eventually pull on its own schedule.
    /// </summary>
    private async Task SendNotificationAsync(
        ReplicationPartner partner,
        ChangeNotification notification,
        (Guid DsaGuid, string NcDn) dedupKey,
        CancellationToken ct)
    {
        var port = _replicationOptions.HttpPort > 0 ? _replicationOptions.HttpPort : 9389;
        var url = $"http://{partner.DnsName}:{port}/drs/notify";

        try
        {
            using var response = await _httpClient.PostAsJsonAsync(url, notification, JsonOptions, ct);

            if (response.IsSuccessStatusCode)
            {
                _lastNotificationTime[dedupKey] = DateTimeOffset.UtcNow;

                _logger.LogDebug(
                    "Notified partner {DnsName} ({DsaGuid}) of changes to NC {NC} (USN {Usn})",
                    partner.DnsName, partner.DsaGuid, notification.NamingContextDn, notification.LatestUsn);
            }
            else
            {
                _logger.LogWarning(
                    "Notification to {DnsName} for NC {NC} returned {StatusCode}",
                    partner.DnsName, notification.NamingContextDn, (int)response.StatusCode);
            }
        }
        catch (TaskCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutting down — don't log
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Failed to notify partner {DnsName} ({DsaGuid}) for NC {NC}: {Error}",
                partner.DnsName, partner.DsaGuid, notification.NamingContextDn, ex.Message);
        }
    }

    /// <summary>
    /// Determines which naming context a changed object belongs to.
    /// </summary>
    private string DetermineNamingContext(DirectoryObject change)
    {
        var dn = change.DistinguishedName ?? "";

        // Check each known naming context
        foreach (var nc in _ncService.GetAllNamingContexts())
        {
            if (dn.EndsWith(nc.Dn, StringComparison.OrdinalIgnoreCase))
                return nc.Dn;
        }

        // Fall back to the domain DN from the object
        return change.DomainDn ?? "";
    }

    /// <summary>
    /// Detects whether a change requires urgent replication.
    /// </summary>
    private static bool IsUrgentChange(DirectoryObject change)
    {
        // Check object class
        if (change.ObjectClass.Any(oc => UrgentReplicationObjectClasses.Contains(oc)))
            return true;

        // Check for urgent attributes
        foreach (var attr in change.Attributes)
        {
            if (UrgentReplicationAttributes.Contains(attr.Key))
                return true;
        }

        return false;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _httpClient.Dispose();
        _cts?.Dispose();
    }

    /// <summary>
    /// Aggregated change info per naming context within a change feed batch.
    /// </summary>
    private class NcChangeInfo
    {
        public string NamingContextDn { get; set; } = string.Empty;
        public long HighestUsn { get; set; }
        public bool IsUrgent { get; set; }
    }
}

/// <summary>
/// Payload sent to partner DCs via POST /drs/notify to inform them of local changes.
/// </summary>
public class ChangeNotification
{
    /// <summary>
    /// The DSA GUID of the DC that originated the change.
    /// </summary>
    public string SourceDsaGuid { get; set; } = string.Empty;

    /// <summary>
    /// The naming context DN that has changes available.
    /// </summary>
    public string NamingContextDn { get; set; } = string.Empty;

    /// <summary>
    /// The latest USN available from the source DC for this naming context.
    /// </summary>
    public long LatestUsn { get; set; }
}
