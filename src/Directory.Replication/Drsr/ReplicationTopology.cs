using System.Collections.Concurrent;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Microsoft.Extensions.Logging;

namespace Directory.Replication.Drsr;

/// <summary>
/// Describes a replication partner (inbound or outbound) for a naming context.
/// </summary>
public class ReplicationPartner
{
    /// <summary>
    /// The objectGUID of the partner's NTDS Settings DSA object.
    /// </summary>
    public Guid DsaGuid { get; set; }

    /// <summary>
    /// The DNS hostname of the partner DC.
    /// </summary>
    public string DnsName { get; set; } = string.Empty;

    /// <summary>
    /// The DN of the partner's NTDS Settings object.
    /// </summary>
    public string NtdsSettingsDn { get; set; } = string.Empty;

    /// <summary>
    /// The naming context DN this partnership covers.
    /// </summary>
    public string NamingContextDn { get; set; } = string.Empty;

    /// <summary>
    /// Transport type: "RPC" for intra-site, "SMTP" for inter-site (legacy).
    /// </summary>
    public string TransportType { get; set; } = "RPC";

    /// <summary>
    /// Replication schedule — 168 bytes (7 days * 24 hours), each byte is a bitmask
    /// of 15-minute intervals. null means "always replicate".
    /// </summary>
    public byte[] Schedule { get; set; }

    /// <summary>
    /// Replication options flags.
    /// </summary>
    public DsReplNeighborFlags Options { get; set; } =
        DsReplNeighborFlags.DS_REPL_NBR_WRITEABLE |
        DsReplNeighborFlags.DS_REPL_NBR_SYNC_ON_STARTUP |
        DsReplNeighborFlags.DS_REPL_NBR_DO_SCHEDULED_SYNCS;

    /// <summary>
    /// The highest USN we have successfully replicated from this partner.
    /// </summary>
    public long LastUsnSynced { get; set; }

    /// <summary>
    /// Timestamp of last successful sync.
    /// </summary>
    public DateTimeOffset LastSyncSuccess { get; set; }

    /// <summary>
    /// Timestamp of last sync attempt.
    /// </summary>
    public DateTimeOffset LastSyncAttempt { get; set; }

    /// <summary>
    /// Last sync result (0 = success, otherwise Win32 error code).
    /// </summary>
    public uint LastSyncResult { get; set; }

    /// <summary>
    /// Number of consecutive sync failures.
    /// </summary>
    public uint ConsecutiveFailures { get; set; }

    /// <summary>
    /// Invocation ID of the partner (may change if the partner is restored from backup).
    /// </summary>
    public Guid InvocationId { get; set; }

    /// <summary>
    /// Converts to a DS_REPL_NEIGHBORW wire structure for DRSGetReplInfo.
    /// </summary>
    public DS_REPL_NEIGHBORW ToNeighborW()
    {
        return new DS_REPL_NEIGHBORW
        {
            PszNamingContext = NamingContextDn,
            PszSourceDsaDN = NtdsSettingsDn,
            PszSourceDsaAddress = DnsName,
            DwReplicaFlags = Options,
            UuidSourceDsaObjGuid = DsaGuid,
            UuidSourceDsaInvocationID = InvocationId,
            UsnLastObjChangeSynced = LastUsnSynced,
            FtimeLastSyncSuccess = LastSyncSuccess.ToFileTime(),
            FtimeLastSyncAttempt = LastSyncAttempt.ToFileTime(),
            DwLastSyncResult = LastSyncResult,
            CNumConsecutiveSyncFailures = ConsecutiveFailures,
        };
    }
}

/// <summary>
/// Manages the replication topology — which DCs replicate which naming contexts
/// to/from each other. Provides the KCC (Knowledge Consistency Checker) basics
/// for automatic topology generation within a site.
///
/// Per [MS-DRSR] section 4.1.10.2 and [MS-ADTS] section 6.2.2.
/// </summary>
public class ReplicationTopology
{
    private readonly IDirectoryStore _store;
    private readonly DcInstanceInfo _dcInfo;
    private readonly ILogger<ReplicationTopology> _logger;

    /// <summary>
    /// Inbound partners: DCs we pull changes FROM.
    /// Key: (namingContextDn, partnerDsaGuid).
    /// </summary>
    private readonly ConcurrentDictionary<(string NcDn, Guid DsaGuid), ReplicationPartner>
        _inboundPartners = new();

    /// <summary>
    /// Outbound notification references: DCs we should NOTIFY when changes occur.
    /// Key: (namingContextDn, partnerDsaGuid).
    /// </summary>
    private readonly ConcurrentDictionary<(string NcDn, Guid DsaGuid), ReplicationPartner>
        _outboundPartners = new();

    public ReplicationTopology(
        IDirectoryStore store,
        DcInstanceInfo dcInfo,
        ILogger<ReplicationTopology> logger)
    {
        _store = store;
        _dcInfo = dcInfo;
        _logger = logger;
    }

    /// <summary>
    /// All inbound replication partners (DCs we pull from).
    /// </summary>
    public IReadOnlyCollection<ReplicationPartner> InboundPartners => _inboundPartners.Values.ToList();

    /// <summary>
    /// All outbound notification targets (DCs we push notifications to).
    /// </summary>
    public IReadOnlyCollection<ReplicationPartner> OutboundPartners => _outboundPartners.Values.ToList();

    /// <summary>
    /// Gets inbound partners for a specific naming context.
    /// </summary>
    public IReadOnlyList<ReplicationPartner> GetInboundPartners(string namingContextDn)
    {
        return _inboundPartners.Values
            .Where(p => p.NamingContextDn.Equals(namingContextDn, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Gets outbound partners for a specific naming context.
    /// </summary>
    public IReadOnlyList<ReplicationPartner> GetOutboundPartners(string namingContextDn)
    {
        return _outboundPartners.Values
            .Where(p => p.NamingContextDn.Equals(namingContextDn, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Adds an inbound replication partner.
    /// </summary>
    public void AddInboundPartner(ReplicationPartner partner)
    {
        var key = (partner.NamingContextDn, partner.DsaGuid);
        _inboundPartners[key] = partner;

        _logger.LogInformation(
            "Added inbound partner {DsaGuid} ({DnsName}) for NC {NC}",
            partner.DsaGuid, partner.DnsName, partner.NamingContextDn);
    }

    /// <summary>
    /// Removes an inbound replication partner.
    /// </summary>
    public bool RemoveInboundPartner(string namingContextDn, Guid dsaGuid)
    {
        var removed = _inboundPartners.TryRemove((namingContextDn, dsaGuid), out _);
        if (removed)
        {
            _logger.LogInformation(
                "Removed inbound partner {DsaGuid} for NC {NC}", dsaGuid, namingContextDn);
        }

        return removed;
    }

    /// <summary>
    /// Adds a notification reference (outbound partner) per IDL_DRSUpdateRefs.
    /// The remote DC calls UpdateRefs to ask us to notify it when changes occur.
    /// </summary>
    public void AddNotificationRef(ReplicationPartner partner)
    {
        var key = (partner.NamingContextDn, partner.DsaGuid);
        _outboundPartners[key] = partner;

        _logger.LogInformation(
            "Added notification ref {DsaGuid} ({DnsName}) for NC {NC}",
            partner.DsaGuid, partner.DnsName, partner.NamingContextDn);
    }

    /// <summary>
    /// Removes a notification reference.
    /// </summary>
    public bool RemoveNotificationRef(string namingContextDn, Guid dsaGuid)
    {
        var removed = _outboundPartners.TryRemove((namingContextDn, dsaGuid), out _);
        if (removed)
        {
            _logger.LogInformation(
                "Removed notification ref {DsaGuid} for NC {NC}", dsaGuid, namingContextDn);
        }

        return removed;
    }

    /// <summary>
    /// Processes an IDL_DRSUpdateRefs request to add or remove outbound notification references.
    /// </summary>
    public void ProcessUpdateRefs(DRS_MSG_UPDREFS_V1 request)
    {
        if (request.PNC == null)
            return;

        var ncDn = request.PNC.StringName;

        if (request.UlOptions.HasFlag(DrsUpdateRefsOptions.DRS_ADD_REF))
        {
            AddNotificationRef(new ReplicationPartner
            {
                DsaGuid = request.UuidDsaObjDest,
                DnsName = request.PszDsaDest ?? string.Empty,
                NamingContextDn = ncDn,
                Options = request.UlOptions.HasFlag(DrsUpdateRefsOptions.DRS_WRIT_REP)
                    ? DsReplNeighborFlags.DS_REPL_NBR_WRITEABLE
                    : DsReplNeighborFlags.None,
            });
        }

        if (request.UlOptions.HasFlag(DrsUpdateRefsOptions.DRS_DEL_REF))
        {
            if (!RemoveNotificationRef(ncDn, request.UuidDsaObjDest) &&
                !request.UlOptions.HasFlag(DrsUpdateRefsOptions.DRS_DEL_REF_NO_ERROR))
            {
                _logger.LogWarning(
                    "DRS_DEL_REF: notification ref not found for {Guid} on NC {NC}",
                    request.UuidDsaObjDest, ncDn);
            }
        }
    }

    /// <summary>
    /// Sends change notifications to all outbound partners for a naming context.
    /// In real AD, this triggers the partner to call GetNCChanges to pull changes.
    ///
    /// Returns the list of partners that were notified.
    /// </summary>
    public IReadOnlyList<ReplicationPartner> NotifyPartners(string namingContextDn)
    {
        var partners = GetOutboundPartners(namingContextDn);

        foreach (var partner in partners)
        {
            if (partner.Options.HasFlag(DsReplNeighborFlags.DS_REPL_NBR_NO_CHANGE_NOTIFICATIONS))
                continue;

            _logger.LogInformation(
                "Notifying partner {DsaGuid} ({DnsName}) of changes to NC {NC}",
                partner.DsaGuid, partner.DnsName, namingContextDn);

            // In a production system, this would make an async RPC call to the partner's
            // IDL_DRSReplicaSync to trigger it to pull changes. In this implementation,
            // the Cosmos DB change feed handles propagation.
        }

        return partners;
    }

    /// <summary>
    /// KCC (Knowledge Consistency Checker) — generates a default intra-site replication
    /// topology by creating bidirectional replication agreements between all DCs in the site.
    ///
    /// Per [MS-ADTS] section 6.2.2, the KCC ensures:
    /// - Every DC has at least one inbound partner for each NC it hosts.
    /// - A ring topology is formed within each site for redundancy.
    /// - At most 3 inbound partners per DC per NC (for sites with &lt;= 7 DCs).
    /// </summary>
    public async Task GenerateIntraSiteTopologyAsync(
        string tenantId,
        string domainDn,
        CancellationToken ct = default)
    {
        var siteDn = $"CN={_dcInfo.SiteName},CN=Sites,CN=Configuration,{domainDn}";
        var serversDn = $"CN=Servers,{siteDn}";

        // Find all server objects in our site
        var serversResult = await _store.SearchAsync(
            tenantId,
            serversDn,
            SearchScope.SingleLevel,
            new EqualityFilterNode("objectClass", "server"),
            null, 0, 0, null, 1000, false, ct);

        var servers = serversResult.Entries
            .Where(s => !s.IsDeleted)
            .ToList();

        if (servers.Count <= 1)
        {
            _logger.LogInformation("KCC: Only {Count} DC(s) in site {Site}, no topology needed",
                servers.Count, _dcInfo.SiteName);
            return;
        }

        _logger.LogInformation("KCC: Generating intra-site topology for {Count} DCs in site {Site}",
            servers.Count, _dcInfo.SiteName);

        var myServerDn = _dcInfo.ServerDn(domainDn);

        // Create a ring topology: each DC replicates from the next DC in the ring
        var sortedServers = servers
            .OrderBy(s => s.DistinguishedName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var myIndex = sortedServers.FindIndex(
            s => s.DistinguishedName.Equals(myServerDn, StringComparison.OrdinalIgnoreCase));

        if (myIndex < 0)
        {
            _logger.LogWarning("KCC: This DC's server object not found in site {Site}", _dcInfo.SiteName);
            return;
        }

        // Add the previous and next DCs in the ring as inbound partners
        var partnersToAdd = new List<int>();

        // Previous DC in ring
        partnersToAdd.Add((myIndex - 1 + sortedServers.Count) % sortedServers.Count);

        // Next DC in ring (if different from previous)
        var nextIdx = (myIndex + 1) % sortedServers.Count;
        if (nextIdx != partnersToAdd[0])
            partnersToAdd.Add(nextIdx);

        // For larger sites, add one more partner for redundancy (skip one)
        if (sortedServers.Count > 4)
        {
            var skipIdx = (myIndex + 2) % sortedServers.Count;
            if (skipIdx != myIndex && !partnersToAdd.Contains(skipIdx))
                partnersToAdd.Add(skipIdx);
        }

        foreach (var partnerIdx in partnersToAdd)
        {
            var partnerServer = sortedServers[partnerIdx];
            var partnerGuid = Guid.TryParse(partnerServer.ObjectGuid, out var g) ? g : Guid.NewGuid();

            AddInboundPartner(new ReplicationPartner
            {
                DsaGuid = partnerGuid,
                DnsName = partnerServer.DnsHostName ?? partnerServer.Cn ?? "unknown",
                NtdsSettingsDn = $"CN=NTDS Settings,{partnerServer.DistinguishedName}",
                NamingContextDn = domainDn,
                TransportType = "RPC",
                InvocationId = partnerGuid, // Simplified — in production, read from NTDS Settings
            });
        }

        _logger.LogInformation("KCC: Topology generated with {Count} inbound partners",
            partnersToAdd.Count);
    }

    /// <summary>
    /// Records a successful sync with a partner, updating last-sync metadata.
    /// </summary>
    public void RecordSyncSuccess(string namingContextDn, Guid dsaGuid, long usnSynced)
    {
        if (_inboundPartners.TryGetValue((namingContextDn, dsaGuid), out var partner))
        {
            partner.LastUsnSynced = usnSynced;
            partner.LastSyncSuccess = DateTimeOffset.UtcNow;
            partner.LastSyncAttempt = DateTimeOffset.UtcNow;
            partner.LastSyncResult = 0;
            partner.ConsecutiveFailures = 0;
        }
    }

    /// <summary>
    /// Records a failed sync attempt with a partner.
    /// </summary>
    public void RecordSyncFailure(string namingContextDn, Guid dsaGuid, uint errorCode)
    {
        if (_inboundPartners.TryGetValue((namingContextDn, dsaGuid), out var partner))
        {
            partner.LastSyncAttempt = DateTimeOffset.UtcNow;
            partner.LastSyncResult = errorCode;
            partner.ConsecutiveFailures++;
        }
    }
}
