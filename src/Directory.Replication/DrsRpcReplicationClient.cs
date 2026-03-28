using System.Diagnostics;
using Directory.Replication.Drsr;
using Microsoft.Extensions.Logging;

namespace Directory.Replication;

/// <summary>
/// RPC-based replication client that uses DCE/RPC over TCP (ncacn_ip_tcp) with NTLM
/// authentication to replicate from a Windows AD DS domain controller via the DRSUAPI
/// interface. Provides the same high-level replication operations as
/// <see cref="DrsReplicationClient"/> but over the native MS-DRSR RPC transport.
///
/// Create instances via <see cref="DrsReplicationClient.CreateRpcClientAsync"/>.
/// </summary>
public class DrsRpcReplicationClient : IAsyncDisposable
{
    private readonly DrsuapiClient _drsuapi;
    private readonly Guid _clientDsaGuid;
    private readonly ILogger _logger;

    /// <summary>
    /// The transport type used by this client.
    /// </summary>
    public DrsTransportType Transport => DrsTransportType.Rpc;

    /// <summary>
    /// Cumulative count of objects received from the partner during this client's lifetime.
    /// </summary>
    public long TotalObjectsReceived { get; private set; }

    /// <summary>
    /// Cumulative bytes transferred from the partner during this client's lifetime.
    /// </summary>
    public long TotalBytesTransferred { get; private set; }

    /// <summary>
    /// Total wall-clock time spent in replication calls during this client's lifetime.
    /// </summary>
    public TimeSpan TotalElapsedTime { get; private set; }

    /// <summary>
    /// The DSA object GUID of the bound partner DC.
    /// </summary>
    public Guid PartnerDsaGuid { get; private set; }

    /// <summary>
    /// The DRS extensions advertised by the partner DC.
    /// </summary>
    public DRS_EXTENSIONS_INT PartnerExtensions => _drsuapi.ServerExtensions;

    /// <summary>
    /// Creates a new RPC replication client wrapping an already-connected and bound
    /// <see cref="DrsuapiClient"/>. Use <see cref="DrsReplicationClient.CreateRpcClientAsync"/>
    /// to create instances.
    /// </summary>
    /// <param name="drsuapi">A connected and DRSBind-completed DRSUAPI client.</param>
    /// <param name="clientDsaGuid">The DSA object GUID identifying this (client) DC.</param>
    /// <param name="logger">Logger instance for diagnostics.</param>
    internal DrsRpcReplicationClient(DrsuapiClient drsuapi, Guid clientDsaGuid, ILogger logger)
    {
        _drsuapi = drsuapi ?? throw new ArgumentNullException(nameof(drsuapi));
        _clientDsaGuid = clientDsaGuid;
        _logger = logger;
    }

    /// <summary>
    /// Pulls changes from the partner DC for a naming context since a given USN watermark.
    /// Handles paging transparently when the server sets <c>FMoreData=true</c>, yielding
    /// each batch as it arrives.
    /// </summary>
    /// <param name="namingContextDn">The distinguished name of the naming context to replicate.</param>
    /// <param name="usnFrom">The USN watermark to resume replication from.</param>
    /// <param name="utdVector">Optional up-to-dateness vector to filter already-seen changes.</param>
    /// <param name="prefixTable">Optional schema prefix table for the destination.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An async enumerable of <see cref="DRS_MSG_GETCHGREPLY_V6"/> batches.</returns>
    public async IAsyncEnumerable<DRS_MSG_GETCHGREPLY_V6> GetNCChangesAsync(
        string namingContextDn,
        USN_VECTOR usnFrom,
        UPTODATE_VECTOR_V2_EXT utdVector = null,
        SCHEMA_PREFIX_TABLE prefixTable = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var currentUsn = usnFrom;
        bool moreData;
        int batchNumber = 0;

        do
        {
            batchNumber++;
            _logger.LogDebug(
                "RPC GetNCChanges batch {Batch}: NC={NC}, UsnFrom={Usn}",
                batchNumber, namingContextDn, currentUsn.UsnHighObjUpdate);

            var request = new DRS_MSG_GETCHGREQ_V8
            {
                UuidDsaObjDest = _clientDsaGuid,
                UuidInvocIdSrc = Guid.Empty,
                PNC = DSNAME.FromDn(namingContextDn),
                UsnvecFrom = currentUsn,
                UlFlags = DrsGetNcChangesFlags.DRS_INIT_SYNC
                        | DrsGetNcChangesFlags.DRS_WRIT_REP
                        | DrsGetNcChangesFlags.DRS_GET_ANC,
                CMaxObjects = 1000,
                CMaxBytes = 10_000_000,
                PUpToDateVecDest = utdVector,
                PrefixTableDest = prefixTable ?? new SCHEMA_PREFIX_TABLE(),
            };

            var sw = Stopwatch.StartNew();
            var reply = await _drsuapi.DrsGetNCChangesAsync(request, ct);
            sw.Stop();

            TotalObjectsReceived += reply.CNumObjects;
            TotalBytesTransferred += reply.CNumBytes;
            TotalElapsedTime += sw.Elapsed;

            _logger.LogInformation(
                "RPC GetNCChanges batch {Batch}: received {Count} objects ({Bytes} bytes) in {Elapsed}ms, MoreData={More}",
                batchNumber, reply.CNumObjects, reply.CNumBytes, sw.ElapsedMilliseconds, reply.FMoreData);

            moreData = reply.FMoreData;
            currentUsn = reply.UsnvecTo;

            yield return reply;
        }
        while (moreData);
    }

    /// <summary>
    /// Registers or unregisters this DC for change notifications on a naming context
    /// at the partner DC, corresponding to <c>IDL_DRSUpdateRefs</c>.
    /// </summary>
    /// <param name="namingContextDn">The naming context to register for notifications on.</param>
    /// <param name="localDsaGuid">The DSA object GUID of this (local) DC.</param>
    /// <param name="localDnsName">The DNS hostname of this (local) DC.</param>
    /// <param name="addRef">
    /// <c>true</c> to register (add a notification reference); <c>false</c> to unregister (remove it).
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    public async Task UpdateRefsAsync(
        string namingContextDn,
        Guid localDsaGuid,
        string localDnsName,
        bool addRef,
        CancellationToken ct = default)
    {
        var options = addRef
            ? DrsUpdateRefsOptions.DRS_ADD_REF | DrsUpdateRefsOptions.DRS_WRIT_REP
            : DrsUpdateRefsOptions.DRS_DEL_REF | DrsUpdateRefsOptions.DRS_DEL_REF_NO_ERROR;

        _logger.LogInformation(
            "RPC UpdateRefs: NC={NC}, LocalDsa={Guid}, Action={Action}",
            namingContextDn, localDsaGuid, addRef ? "AddRef" : "DelRef");

        var request = new DRS_MSG_UPDREFS_V1
        {
            PNC = DSNAME.FromDn(namingContextDn),
            PszDsaDest = localDnsName,
            UuidDsaObjDest = localDsaGuid,
            UlOptions = options,
        };

        await _drsuapi.DrsUpdateRefsAsync(request, ct);

        _logger.LogInformation("RPC UpdateRefs: completed successfully");
    }

    /// <summary>
    /// Translates object names between formats using <c>IDL_DRSCrackNames</c>.
    /// </summary>
    /// <param name="names">The names to translate.</param>
    /// <param name="formatOffered">The format of the input names.</param>
    /// <param name="formatDesired">The desired output format.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The crack names reply containing resolved name items.</returns>
    public async Task<DRS_MSG_CRACKREPLY_V1> CrackNamesAsync(
        IReadOnlyList<string> names,
        DsNameFormat formatOffered,
        DsNameFormat formatDesired,
        CancellationToken ct = default)
    {
        var request = new DRS_MSG_CRACKREQ_V1
        {
            CodePage = 0,
            LocaleId = 0,
            DwFlags = DsNameFlags.DS_NAME_NO_FLAGS,
            FormatOffered = formatOffered,
            FormatDesired = formatDesired,
            CNames = (uint)names.Count,
            RpNames = names.ToList(),
        };

        return await _drsuapi.DrsCrackNamesAsync(request, ct);
    }

    /// <summary>
    /// Performs a full synchronization of an entire naming context by repeatedly calling
    /// <see cref="GetNCChangesAsync"/> until no more data is available, applying each batch
    /// to the local store via <see cref="ReplicationEngine.ApplyIncomingChangesAsync"/>.
    /// </summary>
    /// <param name="namingContextDn">The distinguished name of the naming context to replicate.</param>
    /// <param name="engine">The replication engine used to apply incoming changes locally.</param>
    /// <param name="tenantId">The tenant identifier for the local directory store.</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The total number of objects applied to the local store.</returns>
    public async Task<int> ReplicateFullNcAsync(
        string namingContextDn,
        ReplicationEngine engine,
        string tenantId,
        IProgress<ReplicationProgress> progress = null,
        CancellationToken ct = default)
    {
        _logger.LogInformation("RPC ReplicateFullNc: starting full sync of NC={NC}", namingContextDn);
        var overallSw = Stopwatch.StartNew();
        int totalApplied = 0;
        int totalReceived = 0;
        long totalBytes = 0;

        var usnFrom = new USN_VECTOR();

        progress?.Report(new ReplicationProgress
        {
            Phase = "Starting",
            NamingContext = namingContextDn,
            Message = $"Beginning full replication of {namingContextDn}",
            ElapsedTime = overallSw.Elapsed,
        });

        await foreach (var reply in GetNCChangesAsync(namingContextDn, usnFrom, ct: ct))
        {
            totalReceived += (int)reply.CNumObjects;
            totalBytes += reply.CNumBytes;

            progress?.Report(new ReplicationProgress
            {
                Phase = "Pulling changes",
                NamingContext = namingContextDn,
                ObjectsProcessed = totalReceived,
                BytesTransferred = totalBytes,
                ElapsedTime = overallSw.Elapsed,
                Message = $"Received batch with {reply.CNumObjects} objects",
            });

            var applied = await engine.ApplyIncomingChangesAsync(reply, tenantId, ct);
            totalApplied += applied;

            _logger.LogInformation(
                "RPC ReplicateFullNc: applied {Applied} objects from batch (total: {Total})",
                applied, totalApplied);
        }

        overallSw.Stop();

        progress?.Report(new ReplicationProgress
        {
            Phase = "Complete",
            NamingContext = namingContextDn,
            ObjectsProcessed = totalReceived,
            BytesTransferred = totalBytes,
            ElapsedTime = overallSw.Elapsed,
            Message = $"Full replication complete: {totalApplied} objects applied in {overallSw.Elapsed.TotalSeconds:F1}s",
        });

        _logger.LogInformation(
            "RPC ReplicateFullNc: completed NC={NC}, {Applied} objects applied, {Bytes} bytes, {Elapsed}",
            namingContextDn, totalApplied, totalBytes, overallSw.Elapsed);

        return totalApplied;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _drsuapi.DisposeAsync();
    }
}
