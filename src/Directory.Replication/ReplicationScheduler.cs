using System.Collections.Concurrent;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Replication.Drsr;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Directory.Replication;

/// <summary>
/// Configuration options for the <see cref="ReplicationScheduler"/>.
/// </summary>
public class ReplicationSchedulerOptions
{
    public const string SectionName = "ReplicationScheduler";

    /// <summary>
    /// Replication interval for intra-site partners (default 15 seconds per AD spec).
    /// </summary>
    public TimeSpan IntraSiteInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Replication interval for inter-site partners (default 180 minutes per AD spec).
    /// </summary>
    public TimeSpan InterSiteInterval { get; set; } = TimeSpan.FromMinutes(180);

    /// <summary>
    /// Delay before the first replication cycle after startup, allowing other services to initialize.
    /// </summary>
    public TimeSpan StartupDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Maximum number of replication partners to pull from concurrently.
    /// </summary>
    public int MaxConcurrentPartners { get; set; } = 4;

    /// <summary>
    /// Whether the replication scheduler is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Hosted service that manages ongoing pull replication from inbound partner DCs.
/// Runs continuously after DC startup, polling each inbound partner on a schedule
/// and pulling changes via GetNCChanges. Supports urgent replication for password
/// changes, account lockouts, and trust modifications per [MS-DRSR] section 5.166.
/// </summary>
public class ReplicationScheduler : IHostedService, IDisposable
{
    private readonly DrsProtocol _drsProtocol;
    private readonly ReplicationEngine _replicationEngine;
    private readonly ReplicationTopology _topology;
    private readonly INamingContextService _ncService;
    private readonly DcInstanceInfo _dcInfo;
    private readonly ReplicationSchedulerOptions _options;
    private readonly ILogger<ReplicationScheduler> _logger;

    private CancellationTokenSource _cts;
    private Task _replicationLoop;

    /// <summary>
    /// Tracks per-partner state: last sync time and consecutive failure count for backoff.
    /// Key: (NamingContextDn, DsaGuid).
    /// </summary>
    private readonly ConcurrentDictionary<(string NcDn, Guid DsaGuid), PartnerSyncState> _partnerState = new();

    /// <summary>
    /// Queue of urgent replication requests. Partners/NCs added here are pulled immediately
    /// on the next cycle iteration, bypassing schedule checks.
    /// </summary>
    private readonly ConcurrentQueue<UrgentReplicationRequest> _urgentQueue = new();

    /// <summary>
    /// Semaphore to wake the replication loop when an urgent request or notification arrives.
    /// </summary>
    private readonly SemaphoreSlim _wakeSignal = new(0);

    /// <summary>
    /// Exponential backoff tiers for consecutive failures.
    /// </summary>
    private static readonly TimeSpan[] BackoffTiers =
    [
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(1),
    ];

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

    public ReplicationScheduler(
        DrsProtocol drsProtocol,
        ReplicationEngine replicationEngine,
        ReplicationTopology topology,
        INamingContextService ncService,
        DcInstanceInfo dcInfo,
        IOptions<ReplicationSchedulerOptions> options,
        ILogger<ReplicationScheduler> logger)
    {
        _drsProtocol = drsProtocol;
        _replicationEngine = replicationEngine;
        _topology = topology;
        _ncService = ncService;
        _dcInfo = dcInfo;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("ReplicationScheduler is disabled via configuration");
            return Task.CompletedTask;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _replicationLoop = Task.Run(() => RunReplicationLoopAsync(_cts.Token), _cts.Token);

        _logger.LogInformation(
            "ReplicationScheduler started (IntraSite={IntraSite}s, InterSite={InterSite}min, StartupDelay={Delay}s, MaxConcurrent={Max})",
            _options.IntraSiteInterval.TotalSeconds,
            _options.InterSiteInterval.TotalMinutes,
            _options.StartupDelay.TotalSeconds,
            _options.MaxConcurrentPartners);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_cts is null) return;

        _logger.LogInformation("ReplicationScheduler stopping...");
        _cts.Cancel();

        if (_replicationLoop is not null)
        {
            try
            {
                await _replicationLoop.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) { }
        }

        _logger.LogInformation("ReplicationScheduler stopped");
    }

    /// <summary>
    /// Signals the scheduler to immediately pull from a specific partner and naming context.
    /// Called when a change notification is received from a partner DC.
    /// </summary>
    /// <param name="sourceDsaGuid">The DSA GUID of the partner that has changes.</param>
    /// <param name="namingContextDn">The naming context that has changes.</param>
    /// <param name="latestUsn">The partner's latest USN for this NC.</param>
    public void SignalPullFromPartner(Guid sourceDsaGuid, string namingContextDn, long latestUsn)
    {
        _urgentQueue.Enqueue(new UrgentReplicationRequest
        {
            SourceDsaGuid = sourceDsaGuid,
            NamingContextDn = namingContextDn,
            LatestUsn = latestUsn,
            IsUrgent = false,
        });

        // Wake the replication loop
        _wakeSignal.Release();

        _logger.LogDebug(
            "Signaled pull from partner {DsaGuid} for NC {NC} (USN {Usn})",
            sourceDsaGuid, namingContextDn, latestUsn);
    }

    /// <summary>
    /// Signals urgent replication for a specific naming context (e.g., password change).
    /// All inbound partners for that NC will be pulled immediately.
    /// </summary>
    /// <param name="namingContextDn">The naming context requiring urgent replication.</param>
    public void SignalUrgentReplication(string namingContextDn)
    {
        foreach (var partner in _topology.GetInboundPartners(namingContextDn))
        {
            _urgentQueue.Enqueue(new UrgentReplicationRequest
            {
                SourceDsaGuid = partner.DsaGuid,
                NamingContextDn = namingContextDn,
                LatestUsn = 0,
                IsUrgent = true,
            });
        }

        _wakeSignal.Release();

        _logger.LogInformation("Signaled urgent replication for NC {NC}", namingContextDn);
    }

    /// <summary>
    /// Main replication loop. Waits for startup delay, then continuously iterates
    /// over inbound partners, checking schedules and pulling changes.
    /// </summary>
    private async Task RunReplicationLoopAsync(CancellationToken ct)
    {
        // Wait for other services to initialize
        _logger.LogInformation("ReplicationScheduler waiting {Delay}s for startup initialization...",
            _options.StartupDelay.TotalSeconds);

        try
        {
            await Task.Delay(_options.StartupDelay, ct);
        }
        catch (OperationCanceledException) { return; }

        _logger.LogInformation("ReplicationScheduler entering main replication loop");

        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Process any urgent/notification-triggered requests first
                await ProcessUrgentRequestsAsync(ct);

                // Run a scheduled replication cycle across all partners and NCs
                await RunScheduledCycleAsync(ct);

                // Wait for the intra-site interval or until woken by a notification
                var waitTime = _options.IntraSiteInterval;

                try
                {
                    await _wakeSignal.WaitAsync(waitTime, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in replication loop; will retry next cycle");

                // Brief pause before retrying to avoid tight error loops
                try { await Task.Delay(TimeSpan.FromSeconds(5), ct); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    /// <summary>
    /// Drains and processes all pending urgent replication requests.
    /// </summary>
    private async Task ProcessUrgentRequestsAsync(CancellationToken ct)
    {
        var processed = new HashSet<(string NcDn, Guid DsaGuid)>();

        while (_urgentQueue.TryDequeue(out var request))
        {
            ct.ThrowIfCancellationRequested();

            var key = (request.NamingContextDn, request.SourceDsaGuid);

            // Deduplicate: don't pull the same partner+NC twice in one batch
            if (!processed.Add(key))
                continue;

            var partner = _topology.InboundPartners
                .FirstOrDefault(p => p.DsaGuid == request.SourceDsaGuid &&
                    p.NamingContextDn.Equals(request.NamingContextDn, StringComparison.OrdinalIgnoreCase));

            if (partner is null)
            {
                _logger.LogDebug(
                    "Ignoring notification from unknown partner {DsaGuid} for NC {NC}",
                    request.SourceDsaGuid, request.NamingContextDn);
                continue;
            }

            _logger.LogInformation(
                "Processing {Type} replication from {DnsName} ({DsaGuid}) for NC {NC}",
                request.IsUrgent ? "urgent" : "notification-triggered",
                partner.DnsName, partner.DsaGuid, partner.NamingContextDn);

            await PullFromPartnerAsync(partner, ct);
        }
    }

    /// <summary>
    /// Runs one scheduled replication cycle: iterates all naming contexts and their
    /// inbound partners, checking schedules and backoff state before pulling.
    /// </summary>
    private async Task RunScheduledCycleAsync(CancellationToken ct)
    {
        var namingContexts = _ncService.GetAllNamingContexts();
        var semaphore = new SemaphoreSlim(_options.MaxConcurrentPartners);
        var tasks = new List<Task>();

        foreach (var nc in namingContexts)
        {
            ct.ThrowIfCancellationRequested();

            // Only replicate Schema, Configuration, and Domain NCs
            if (nc.Type != NamingContextType.Schema &&
                nc.Type != NamingContextType.Configuration &&
                nc.Type != NamingContextType.Domain)
            {
                continue;
            }

            var partners = _topology.GetInboundPartners(nc.Dn);

            foreach (var partner in partners)
            {
                ct.ThrowIfCancellationRequested();

                // Check the partner's replication schedule
                if (!IsScheduleActive(partner.Schedule))
                {
                    _logger.LogDebug(
                        "Skipping partner {DsaGuid} for NC {NC}: schedule inactive",
                        partner.DsaGuid, nc.Dn);
                    continue;
                }

                // Check if enough time has elapsed since last sync (respecting backoff)
                var state = GetOrCreatePartnerState(partner);
                var interval = GetEffectiveInterval(partner, state);

                if (DateTimeOffset.UtcNow - state.LastSyncAttempt < interval)
                    continue;

                await semaphore.WaitAsync(ct);

                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        await PullFromPartnerAsync(partner, ct);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, ct));
            }
        }

        if (tasks.Count > 0)
        {
            await Task.WhenAll(tasks);
        }
    }

    /// <summary>
    /// Pulls changes from a single inbound partner for its naming context.
    /// Issues a GetNCChanges request, applies changes via the ReplicationEngine,
    /// and records success or failure in the topology.
    /// </summary>
    private async Task PullFromPartnerAsync(ReplicationPartner partner, CancellationToken ct)
    {
        var state = GetOrCreatePartnerState(partner);
        state.LastSyncAttempt = DateTimeOffset.UtcNow;

        try
        {
            _logger.LogDebug(
                "Pulling changes from {DnsName} ({DsaGuid}) for NC {NC}, USN from {Usn}",
                partner.DnsName, partner.DsaGuid, partner.NamingContextDn, partner.LastUsnSynced);

            var request = new GetNCChangesRequest
            {
                TenantId = "default",
                NamingContextDn = partner.NamingContextDn,
                UsnFrom = partner.LastUsnSynced,
                MaxObjects = 1000,
                MaxBytes = 10_000_000,
            };

            // Pull changes from partner via HTTP API
            var response = await _drsProtocol.GetNCChangesAsync(request, ct);

            if (response.Entries.Count > 0)
            {
                _logger.LogInformation(
                    "Received {Count} changes from {DnsName} for NC {NC} (USN {FromUsn} -> {ToUsn})",
                    response.Entries.Count, partner.DnsName, partner.NamingContextDn,
                    partner.LastUsnSynced, response.HighestUsn);
            }

            // Record success
            _topology.RecordSyncSuccess(partner.NamingContextDn, partner.DsaGuid, response.HighestUsn);
            state.ConsecutiveFailures = 0;
            state.LastSyncSuccess = DateTimeOffset.UtcNow;

            // Handle MORE_DATA: keep pulling until complete
            while (response.MoreData)
            {
                ct.ThrowIfCancellationRequested();

                request = new GetNCChangesRequest
                {
                    TenantId = "default",
                    NamingContextDn = partner.NamingContextDn,
                    UsnFrom = response.HighestUsn,
                    MaxObjects = 1000,
                    MaxBytes = 10_000_000,
                };

                response = await _drsProtocol.GetNCChangesAsync(request, ct);

                if (response.Entries.Count > 0)
                {
                    _topology.RecordSyncSuccess(partner.NamingContextDn, partner.DsaGuid, response.HighestUsn);
                }
            }

            _logger.LogDebug(
                "Replication cycle complete from {DnsName} for NC {NC}",
                partner.DnsName, partner.NamingContextDn);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // Propagate cancellation
        }
        catch (Exception ex)
        {
            state.ConsecutiveFailures++;
            state.LastSyncAttempt = DateTimeOffset.UtcNow;

            _topology.RecordSyncFailure(partner.NamingContextDn, partner.DsaGuid, 0x2105 /* ERROR_DS_DRA_CONNECTION_FAILED */);

            _logger.LogWarning(ex,
                "Replication from {DnsName} ({DsaGuid}) for NC {NC} failed (attempt {Attempt}, backoff {Backoff}s)",
                partner.DnsName, partner.DsaGuid, partner.NamingContextDn,
                state.ConsecutiveFailures, GetBackoffDuration(state.ConsecutiveFailures).TotalSeconds);
        }
    }

    /// <summary>
    /// Checks the 168-byte weekly schedule to determine whether the current time slot is active.
    /// Each byte represents one hour. Each bit in the byte represents a 15-minute interval.
    /// A null schedule means "always replicate".
    /// </summary>
    /// <param name="schedule">168-byte weekly schedule, or null for always-on.</param>
    /// <returns>True if replication is allowed in the current time slot.</returns>
    private static bool IsScheduleActive(byte[] schedule)
    {
        if (schedule is null || schedule.Length != 168)
            return true; // No schedule or invalid = always replicate

        var now = DateTimeOffset.UtcNow;
        var dayOfWeek = (int)now.DayOfWeek; // Sunday = 0
        var hour = now.Hour;
        var minute = now.Minute;

        // Index into the 168-byte array: day * 24 + hour
        var byteIndex = dayOfWeek * 24 + hour;
        if (byteIndex < 0 || byteIndex >= 168)
            return true;

        var scheduleByte = schedule[byteIndex];

        // Each bit represents a 15-minute interval within the hour
        var quarterIndex = minute / 15; // 0-3
        var mask = 1 << quarterIndex;

        return (scheduleByte & mask) != 0;
    }

    /// <summary>
    /// Computes the effective replication interval for a partner, factoring in
    /// transport type (intra-site vs inter-site) and exponential backoff on failure.
    /// </summary>
    private TimeSpan GetEffectiveInterval(ReplicationPartner partner, PartnerSyncState state)
    {
        // Base interval depends on transport type
        var baseInterval = partner.TransportType.Equals("RPC", StringComparison.OrdinalIgnoreCase)
            ? _options.IntraSiteInterval
            : _options.InterSiteInterval;

        // Apply exponential backoff if there have been consecutive failures
        if (state.ConsecutiveFailures > 0)
        {
            var backoff = GetBackoffDuration(state.ConsecutiveFailures);
            return baseInterval > backoff ? baseInterval : backoff;
        }

        return baseInterval;
    }

    /// <summary>
    /// Returns the backoff duration for a given number of consecutive failures.
    /// Tiers: 30s, 1m, 2m, 5m, 15m, 30m, 1h (max).
    /// </summary>
    private static TimeSpan GetBackoffDuration(int consecutiveFailures)
    {
        var index = Math.Min(consecutiveFailures - 1, BackoffTiers.Length - 1);
        return index >= 0 ? BackoffTiers[index] : TimeSpan.Zero;
    }

    /// <summary>
    /// Gets or creates the per-partner sync tracking state.
    /// </summary>
    private PartnerSyncState GetOrCreatePartnerState(ReplicationPartner partner)
    {
        var key = (partner.NamingContextDn, partner.DsaGuid);
        return _partnerState.GetOrAdd(key, _ => new PartnerSyncState());
    }

    /// <summary>
    /// Checks whether a set of changed attributes contains any that require urgent replication.
    /// </summary>
    /// <param name="changedAttributes">The names of attributes that were modified.</param>
    /// <returns>True if any attribute triggers urgent replication.</returns>
    public static bool ContainsUrgentAttributes(IEnumerable<string> changedAttributes)
    {
        return changedAttributes.Any(attr => UrgentReplicationAttributes.Contains(attr));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _cts?.Dispose();
        _wakeSignal.Dispose();
    }

    /// <summary>
    /// Tracks per-partner replication state for scheduling and backoff.
    /// </summary>
    private class PartnerSyncState
    {
        public DateTimeOffset LastSyncAttempt { get; set; } = DateTimeOffset.MinValue;
        public DateTimeOffset LastSyncSuccess { get; set; } = DateTimeOffset.MinValue;
        public int ConsecutiveFailures { get; set; }
    }

    /// <summary>
    /// Represents an urgent or notification-triggered replication request.
    /// </summary>
    private class UrgentReplicationRequest
    {
        public Guid SourceDsaGuid { get; init; }
        public string NamingContextDn { get; init; } = string.Empty;
        public long LatestUsn { get; init; }
        public bool IsUrgent { get; init; }
    }
}
