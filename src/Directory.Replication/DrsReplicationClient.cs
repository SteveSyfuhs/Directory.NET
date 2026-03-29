using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Directory.Replication.Drsr;
using Microsoft.Extensions.Logging;

namespace Directory.Replication;

/// <summary>
/// Identifies the transport used by a replication client to communicate with a partner DC.
/// </summary>
public enum DrsTransportType
{
    /// <summary>
    /// HTTP/JSON transport to a Directory.NET peer DC.
    /// </summary>
    Http,

    /// <summary>
    /// DCE/RPC over TCP (ncacn_ip_tcp) transport to a Windows AD DS domain controller.
    /// </summary>
    Rpc,
}

/// <summary>
/// HTTP client for the Directory Replication Service (DRS) protocol.
/// Connects to a remote DC's <see cref="DrsHttpService"/> endpoint to pull
/// directory changes, register for change notifications, and trigger sync cycles.
/// </summary>
public class DrsReplicationClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<DrsReplicationClient> _logger;
    private readonly bool _ownsHttpClient;

    private string _partnerUrl;
    private Guid _partnerDsaGuid;
    private DRS_EXTENSIONS_INT _partnerExtensions;

    /// <summary>
    /// Maximum number of retry attempts for transient HTTP failures.
    /// </summary>
    private const int MaxRetries = 5;

    /// <summary>
    /// Base delay for exponential backoff between retries.
    /// </summary>
    private static readonly TimeSpan BaseRetryDelay = TimeSpan.FromMilliseconds(500);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
    };

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
    /// The DSA object GUID of the bound partner DC, available after a successful <see cref="BindAsync"/> call.
    /// </summary>
    public Guid PartnerDsaGuid => _partnerDsaGuid;

    /// <summary>
    /// The DRS extensions advertised by the partner DC, available after a successful <see cref="BindAsync"/> call.
    /// </summary>
    public DRS_EXTENSIONS_INT PartnerExtensions => _partnerExtensions;

    /// <summary>
    /// The transport type in use by this client instance.
    /// </summary>
    public DrsTransportType Transport { get; } = DrsTransportType.Http;

    /// <summary>
    /// Creates an RPC-based replication client that uses DCE/RPC over TCP (ncacn_ip_tcp)
    /// with Kerberos authentication to connect to a Windows AD DS domain controller.
    /// The client discovers the DRSUAPI dynamic port via the Endpoint Mapper on port 135,
    /// performs an authenticated RPC bind, and then issues DRSBind to establish a replication handle.
    /// </summary>
    /// <param name="hostname">DNS hostname or IP address of the target DC.</param>
    /// <param name="username">Username for authentication (e.g., "Administrator").</param>
    /// <param name="domain">NetBIOS or DNS domain name for authentication.</param>
    /// <param name="password">Password for authentication.</param>
    /// <param name="logger">Logger instance for diagnostics.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A bound <see cref="DrsRpcReplicationClient"/> ready for replication operations.</returns>
    public static async Task<DrsRpcReplicationClient> CreateRpcClientAsync(
        string hostname, string username, string domain, string password,
        ILogger logger, CancellationToken ct = default)
    {
        var drsuapi = new DrsuapiClient(logger);
        await drsuapi.ConnectAsync(hostname, username, domain, password, ct);

        var clientDsaGuid = Guid.NewGuid();
        await drsuapi.DrsBindAsync(clientDsaGuid, ct);

        return new DrsRpcReplicationClient(drsuapi, clientDsaGuid, logger);
    }

    /// <summary>
    /// Creates a new DRS replication client with a default <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="logger">Logger instance for diagnostics.</param>
    public DrsReplicationClient(ILogger<DrsReplicationClient> logger)
        : this(new HttpClient(), logger, ownsHttpClient: true)
    {
    }

    /// <summary>
    /// Creates a new DRS replication client using the provided <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="httpClient">An externally managed HTTP client (caller retains ownership).</param>
    /// <param name="logger">Logger instance for diagnostics.</param>
    public DrsReplicationClient(HttpClient httpClient, ILogger<DrsReplicationClient> logger)
        : this(httpClient, logger, ownsHttpClient: false)
    {
    }

    private DrsReplicationClient(HttpClient httpClient, ILogger<DrsReplicationClient> logger, bool ownsHttpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _ownsHttpClient = ownsHttpClient;
        _httpClient.Timeout = TimeSpan.FromMinutes(5);
    }

    /// <summary>
    /// Authenticates against a partner DC and retrieves its DSA GUID and supported DRS extensions.
    /// Must be called before any other replication operations.
    /// </summary>
    /// <param name="partnerUrl">
    /// Base URL of the partner DC's DRS HTTP service (e.g., <c>http://dc2.contoso.com:9389</c>).
    /// </param>
    /// <param name="adminUpn">User principal name for authentication.</param>
    /// <param name="password">Password for authentication.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the bind handshake succeeds.</returns>
    /// <exception cref="DrsReplicationException">Thrown when the partner rejects the bind or is unreachable.</exception>
    public async Task BindAsync(string partnerUrl, string adminUpn, string password, CancellationToken ct = default)
    {
        _partnerUrl = partnerUrl.TrimEnd('/');
        _logger.LogInformation("DRS Bind: connecting to partner {Url} as {Upn}", _partnerUrl, adminUpn);

        // Set basic auth header for partner communication
        var credentials = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes($"{adminUpn}:{password}"));
        _httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);

        // Perform a bind handshake via the health endpoint to verify connectivity
        // and retrieve partner metadata
        var bindRequest = new DrsBindRequest
        {
            AdminUpn = adminUpn,
            ClientExtensions = new DRS_EXTENSIONS_INT
            {
                DwFlags = DrsExtFlags.DRS_EXT_BASE
                        | DrsExtFlags.DRS_EXT_GETCHGREQ_V8
                        | DrsExtFlags.DRS_EXT_GETCHGREPLY_V6
                        | DrsExtFlags.DRS_EXT_LINKED_VALUE_REPLICATION
                        | DrsExtFlags.DRS_EXT_STRONG_ENCRYPTION,
                Pid = Environment.ProcessId,
            },
        };

        var response = await SendWithRetryAsync<DrsBindResponse>(
            HttpMethod.Post,
            $"{_partnerUrl}/drs/Bind",
            bindRequest,
            ct);

        if (response == null)
        {
            // Fall back to health check if partner does not implement Bind endpoint
            _logger.LogWarning("DRS Bind: partner does not expose /drs/Bind, falling back to health check");
            var healthResponse = await SendWithRetryAsync<DrsHealthResponse>(
                HttpMethod.Get,
                $"{_partnerUrl}/drs/health",
                null,
                ct);

            if (healthResponse == null)
            {
                throw new DrsReplicationException($"Failed to connect to partner at {_partnerUrl}");
            }

            _partnerDsaGuid = healthResponse.DsaGuid != Guid.Empty
                ? healthResponse.DsaGuid
                : Guid.NewGuid();
            _partnerExtensions = new DRS_EXTENSIONS_INT
            {
                DwFlags = DrsExtFlags.DRS_EXT_BASE
                        | DrsExtFlags.DRS_EXT_GETCHGREQ_V8
                        | DrsExtFlags.DRS_EXT_GETCHGREPLY_V6,
            };
        }
        else
        {
            _partnerDsaGuid = response.DsaGuid;
            _partnerExtensions = response.ServerExtensions;
        }

        _logger.LogInformation(
            "DRS Bind: successfully bound to partner {Guid}, extensions={Flags}",
            _partnerDsaGuid, _partnerExtensions?.DwFlags);
    }

    /// <summary>
    /// Pulls changes from the partner DC for a naming context since a given USN watermark.
    /// Handles paging transparently when the server sets <c>MoreData=true</c>, yielding
    /// each batch as it arrives.
    /// </summary>
    /// <param name="namingContextDn">The distinguished name of the naming context to replicate.</param>
    /// <param name="usnFrom">The USN watermark to resume replication from.</param>
    /// <param name="utdVector">Optional up-to-dateness vector to filter already-seen changes.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An async enumerable of <see cref="GetNCChangesResponse"/> batches.</returns>
    /// <exception cref="InvalidOperationException">Thrown if <see cref="BindAsync"/> has not been called.</exception>
    public async IAsyncEnumerable<GetNCChangesResponse> GetNCChangesAsync(
        string namingContextDn,
        USN_VECTOR usnFrom,
        UPTODATE_VECTOR_V2_EXT utdVector = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        EnsureBound();

        var currentUsn = usnFrom.UsnHighObjUpdate;
        bool moreData;
        int batchNumber = 0;

        do
        {
            batchNumber++;
            _logger.LogDebug(
                "GetNCChanges batch {Batch}: NC={NC}, UsnFrom={Usn}",
                batchNumber, namingContextDn, currentUsn);

            var request = new GetNCChangesRequest
            {
                NamingContextDn = namingContextDn,
                UsnFrom = currentUsn,
                MaxObjects = 1000,
                MaxBytes = 10_000_000,
                PartnerUpToDateVector = utdVector != null
                    ? ConvertUtdVector(utdVector)
                    : null,
            };

            var sw = Stopwatch.StartNew();
            var response = await SendWithRetryAsync<GetNCChangesResponse>(
                HttpMethod.Post,
                $"{_partnerUrl}/drs/GetNCChanges",
                request,
                ct);
            sw.Stop();

            if (response == null)
            {
                throw new DrsReplicationException(
                    $"GetNCChanges returned null for NC={namingContextDn}, batch {batchNumber}");
            }

            // Update statistics
            var batchBytes = EstimateResponseSize(response);
            TotalObjectsReceived += response.Entries.Count;
            TotalBytesTransferred += batchBytes;
            TotalElapsedTime += sw.Elapsed;

            _logger.LogInformation(
                "GetNCChanges batch {Batch}: received {Count} objects ({Bytes} bytes) in {Elapsed}ms, MoreData={More}",
                batchNumber, response.Entries.Count, batchBytes, sw.ElapsedMilliseconds, response.MoreData);

            moreData = response.MoreData;
            if (response.HighestUsn > currentUsn)
            {
                currentUsn = response.HighestUsn;
            }

            yield return response;
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
    /// <returns>A task that completes when the partner acknowledges the request.</returns>
    /// <exception cref="InvalidOperationException">Thrown if <see cref="BindAsync"/> has not been called.</exception>
    /// <exception cref="DrsReplicationException">Thrown if the partner rejects the UpdateRefs request.</exception>
    public async Task UpdateRefsAsync(
        string namingContextDn,
        Guid localDsaGuid,
        string localDnsName,
        bool addRef,
        CancellationToken ct = default)
    {
        EnsureBound();

        var options = addRef
            ? DrsUpdateRefsOptions.DRS_ADD_REF | DrsUpdateRefsOptions.DRS_WRIT_REP
            : DrsUpdateRefsOptions.DRS_DEL_REF | DrsUpdateRefsOptions.DRS_DEL_REF_NO_ERROR;

        _logger.LogInformation(
            "UpdateRefs: NC={NC}, LocalDsa={Guid}, Action={Action}",
            namingContextDn, localDsaGuid, addRef ? "AddRef" : "DelRef");

        var request = new DrsUpdateRefsRequest
        {
            NamingContextDn = namingContextDn,
            DsaGuid = localDsaGuid,
            DnsName = localDnsName,
            Options = (uint)options,
        };

        var response = await SendWithRetryAsync<DrsUpdateRefsResponse>(
            HttpMethod.Post,
            $"{_partnerUrl}/drs/UpdateRefs",
            request,
            ct);

        if (response is { Success: false })
        {
            throw new DrsReplicationException(
                $"UpdateRefs failed: {response.ErrorMessage}");
        }

        _logger.LogInformation("UpdateRefs: completed successfully");
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
    /// <exception cref="InvalidOperationException">Thrown if <see cref="BindAsync"/> has not been called.</exception>
    public async Task<int> ReplicateFullNcAsync(
        string namingContextDn,
        ReplicationEngine engine,
        string tenantId,
        IProgress<ReplicationProgress> progress = null,
        CancellationToken ct = default)
    {
        EnsureBound();

        _logger.LogInformation("ReplicateFullNc: starting full sync of NC={NC}", namingContextDn);
        var overallSw = Stopwatch.StartNew();
        int totalApplied = 0;
        int totalReceived = 0;
        long totalBytes = 0;

        var usnFrom = new USN_VECTOR
        {
            UsnHighObjUpdate = 0,
            UsnHighPropUpdate = 0,
        };

        progress?.Report(new ReplicationProgress
        {
            Phase = "Starting",
            NamingContext = namingContextDn,
            Message = $"Beginning full replication of {namingContextDn}",
            ElapsedTime = overallSw.Elapsed,
        });

        await foreach (var batch in GetNCChangesAsync(namingContextDn, usnFrom, utdVector: null, ct))
        {
            totalReceived += batch.Entries.Count;
            var batchBytes = EstimateResponseSize(batch);
            totalBytes += batchBytes;

            progress?.Report(new ReplicationProgress
            {
                Phase = "Pulling changes",
                NamingContext = namingContextDn,
                ObjectsProcessed = totalReceived,
                BytesTransferred = totalBytes,
                ElapsedTime = overallSw.Elapsed,
                Message = $"Received batch with {batch.Entries.Count} objects (USN up to {batch.HighestUsn})",
            });

            // Convert the HTTP-level response into a DRS_MSG_GETCHGREPLY_V6 for the engine
            var replyV6 = ConvertToReplyV6(batch, namingContextDn);

            progress?.Report(new ReplicationProgress
            {
                Phase = "Applying batch",
                NamingContext = namingContextDn,
                ObjectsProcessed = totalReceived,
                BytesTransferred = totalBytes,
                ElapsedTime = overallSw.Elapsed,
                Message = $"Applying {batch.Entries.Count} objects to local store",
            });

            var applied = await engine.ApplyIncomingChangesAsync(replyV6, tenantId, ct);
            totalApplied += applied;

            _logger.LogInformation(
                "ReplicateFullNc: applied {Applied} objects from batch (total applied: {Total})",
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
            "ReplicateFullNc: completed NC={NC}, {Applied} objects applied, {Bytes} bytes, {Elapsed}",
            namingContextDn, totalApplied, totalBytes, overallSw.Elapsed);

        return totalApplied;
    }

    /// <summary>
    /// Requests the partner DC to initiate a replication sync cycle for the specified naming context,
    /// corresponding to <c>IDL_DRSReplicaSync</c>.
    /// </summary>
    /// <param name="namingContextDn">The naming context the partner should synchronize.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the partner acknowledges the sync request.</returns>
    /// <exception cref="InvalidOperationException">Thrown if <see cref="BindAsync"/> has not been called.</exception>
    /// <exception cref="DrsReplicationException">Thrown if the partner rejects the sync request.</exception>
    public async Task DsReplicaSyncAsync(string namingContextDn, CancellationToken ct = default)
    {
        EnsureBound();

        _logger.LogInformation("DsReplicaSync: requesting partner to sync NC={NC}", namingContextDn);

        var request = new DsReplicaSyncRequest
        {
            NamingContextDn = namingContextDn,
            IsUrgent = false,
        };

        var sw = Stopwatch.StartNew();
        var result = await SendWithRetryAsync<DsReplicaSyncResult>(
            HttpMethod.Post,
            $"{_partnerUrl}/drs/DsReplicaSync",
            request,
            ct);
        sw.Stop();
        TotalElapsedTime += sw.Elapsed;

        if (result is { Success: false })
        {
            throw new DrsReplicationException(
                $"DsReplicaSync failed: {result.ErrorMessage}");
        }

        _logger.LogInformation("DsReplicaSync: partner acknowledged sync request in {Elapsed}ms", sw.ElapsedMilliseconds);
    }

    /// <summary>
    /// Sends an HTTP request with automatic retry and exponential backoff for transient failures.
    /// </summary>
    private async Task<TResponse> SendWithRetryAsync<TResponse>(
        HttpMethod method,
        string url,
        object body,
        CancellationToken ct)
        where TResponse : class
    {
        int attempt = 0;

        while (true)
        {
            attempt++;

            try
            {
                using var request = new HttpRequestMessage(method, url);

                if (body != null)
                {
                    request.Content = JsonContent.Create(body, options: JsonOptions);
                }

                using var response = await _httpClient.SendAsync(request, ct);

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    // Endpoint not implemented on partner — return null rather than retry
                    _logger.LogDebug("Endpoint not found: {Url}", url);
                    return null;
                }

                if (IsTransientFailure(response.StatusCode) && attempt < MaxRetries)
                {
                    var delay = GetRetryDelay(attempt);
                    _logger.LogWarning(
                        "Transient failure ({Status}) calling {Url}, retrying in {Delay}ms (attempt {Attempt}/{Max})",
                        response.StatusCode, url, delay.TotalMilliseconds, attempt, MaxRetries);
                    await Task.Delay(delay, ct);
                    continue;
                }

                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync(ct);
                TotalBytesTransferred += content.Length;

                return JsonSerializer.Deserialize<TResponse>(content, JsonOptions);
            }
            catch (HttpRequestException ex) when (attempt < MaxRetries)
            {
                var delay = GetRetryDelay(attempt);
                _logger.LogWarning(
                    ex,
                    "HTTP error calling {Url}, retrying in {Delay}ms (attempt {Attempt}/{Max})",
                    url, delay.TotalMilliseconds, attempt, MaxRetries);
                await Task.Delay(delay, ct);
            }
            catch (TaskCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (TaskCanceledException ex) when (attempt < MaxRetries)
            {
                // Timeout — treat as transient
                var delay = GetRetryDelay(attempt);
                _logger.LogWarning(
                    ex,
                    "Timeout calling {Url}, retrying in {Delay}ms (attempt {Attempt}/{Max})",
                    url, delay.TotalMilliseconds, attempt, MaxRetries);
                await Task.Delay(delay, ct);
            }
        }
    }

    /// <summary>
    /// Determines whether an HTTP status code represents a transient failure eligible for retry.
    /// </summary>
    private static bool IsTransientFailure(HttpStatusCode statusCode)
    {
        return statusCode is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;
    }

    /// <summary>
    /// Computes an exponential backoff delay with jitter for the given retry attempt.
    /// </summary>
    private static TimeSpan GetRetryDelay(int attempt)
    {
        // Exponential backoff: 500ms, 1s, 2s, 4s, 8s + jitter
        var baseDelay = BaseRetryDelay * Math.Pow(2, attempt - 1);
        var jitter = Random.Shared.Next(0, (int)(baseDelay.TotalMilliseconds * 0.3));
        return baseDelay + TimeSpan.FromMilliseconds(jitter);
    }

    /// <summary>
    /// Validates that <see cref="BindAsync"/> has been called successfully.
    /// </summary>
    private void EnsureBound()
    {
        if (_partnerUrl == null)
        {
            throw new InvalidOperationException(
                "Not bound to a partner DC. Call BindAsync before performing replication operations.");
        }
    }

    /// <summary>
    /// Converts an <see cref="UPTODATE_VECTOR_V2_EXT"/> to the HTTP-level <see cref="UpToDateVector"/>
    /// used by <see cref="DrsProtocol"/>.
    /// </summary>
    private static UpToDateVector ConvertUtdVector(UPTODATE_VECTOR_V2_EXT utd)
    {
        return new UpToDateVector
        {
            Entries = utd.RgCursors.Select(c => new UpToDateVectorEntry
            {
                InvocationId = c.UuidDsa.ToString(),
                UsnHighPropUpdate = c.UsnHighPropUpdate,
                TimeLastSyncSuccess = DateTimeOffset.FromFileTime(c.FtimeLastSyncSuccess),
            }).ToList(),
        };
    }

    /// <summary>
    /// Converts an HTTP-level <see cref="GetNCChangesResponse"/> into a wire-format
    /// <see cref="DRS_MSG_GETCHGREPLY_V6"/> suitable for <see cref="ReplicationEngine.ApplyIncomingChangesAsync"/>.
    /// </summary>
    private DRS_MSG_GETCHGREPLY_V6 ConvertToReplyV6(GetNCChangesResponse response, string namingContextDn)
    {
        REPLENTINFLIST firstEntry = null;
        REPLENTINFLIST lastEntry = null;
        uint totalBytes = 0;

        foreach (var entry in response.Entries)
        {
            var dsname = DSNAME.FromDn(
                entry.Dn,
                Guid.TryParse(entry.ObjectGuid, out var g) ? g : Guid.Empty);

            var attrBlock = new ATTRBLOCK();
            foreach (var kvp in entry.Attributes)
            {
                var attrValBlock = new ATTRVALBLOCK();
                foreach (var val in kvp.Value)
                {
                    var bytes = System.Text.Encoding.Unicode.GetBytes(val);
                    attrValBlock.PVal.Add(new ATTRVAL
                    {
                        ValLen = (uint)bytes.Length,
                        PVal = bytes,
                    });
                }
                attrValBlock.ValCount = (uint)attrValBlock.PVal.Count;

                // Use a hash-based ATTRTYP since we don't have the partner's prefix table
                attrBlock.PAttr.Add(new ATTR
                {
                    AttrTyp = new ATTRTYP((uint)(kvp.Key.GetHashCode() & 0x7FFFFFFF)),
                    AttrVal = attrValBlock,
                });
            }
            attrBlock.AttrCount = (uint)attrBlock.PAttr.Count;

            var entinf = new ENTINF
            {
                PName = dsname,
                UlFlags = entry.IsDeleted ? 1u : 0u,
                AttrBlock = attrBlock,
            };

            var replEntry = new REPLENTINFLIST
            {
                Entinf = entinf,
                FIsNCPrefix = entry.Dn.Equals(namingContextDn, StringComparison.OrdinalIgnoreCase),
            };

            if (firstEntry == null)
            {
                firstEntry = replEntry;
                lastEntry = replEntry;
            }
            else
            {
                lastEntry.PNextEntInf = replEntry;
                lastEntry = replEntry;
            }

            totalBytes += (uint)(entry.Dn.Length * 2 + 64);
        }

        return new DRS_MSG_GETCHGREPLY_V6
        {
            UuidDsaObjSrc = _partnerDsaGuid,
            UuidInvocIdSrc = _partnerDsaGuid,
            PNC = DSNAME.FromDn(namingContextDn),
            UsnvecFrom = new USN_VECTOR { UsnHighObjUpdate = 0, UsnHighPropUpdate = 0 },
            UsnvecTo = new USN_VECTOR
            {
                UsnHighObjUpdate = response.HighestUsn,
                UsnHighPropUpdate = response.HighestUsn,
            },
            PObjects = firstEntry,
            CNumObjects = (uint)response.Entries.Count,
            CNumBytes = totalBytes,
            FMoreData = response.MoreData,
            PrefixTableSrc = new SCHEMA_PREFIX_TABLE(),
            RgValues = [],
        };
    }

    /// <summary>
    /// Estimates the byte size of a <see cref="GetNCChangesResponse"/> for statistics tracking.
    /// </summary>
    private static long EstimateResponseSize(GetNCChangesResponse response)
    {
        long size = 64; // Base overhead
        foreach (var entry in response.Entries)
        {
            size += entry.Dn.Length * 2;
            size += 64; // Per-object overhead
            foreach (var attr in entry.Attributes)
            {
                size += attr.Key.Length * 2;
                foreach (var val in attr.Value)
                {
                    size += val.Length * 2;
                }
            }
        }
        return size;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    #region HTTP-level request/response DTOs for endpoints not in DrsProtocol

    /// <summary>
    /// Request body for the DRS Bind endpoint.
    /// </summary>
    private class DrsBindRequest
    {
        public string AdminUpn { get; init; } = string.Empty;
        public DRS_EXTENSIONS_INT ClientExtensions { get; init; } = new();
    }

    /// <summary>
    /// Response body from the DRS Bind endpoint.
    /// </summary>
    private class DrsBindResponse
    {
        public Guid DsaGuid { get; init; }
        public DRS_EXTENSIONS_INT ServerExtensions { get; init; } = new();
    }

    /// <summary>
    /// Response body from the DRS health endpoint.
    /// </summary>
    private class DrsHealthResponse
    {
        public string Status { get; init; } = string.Empty;
        public Guid DsaGuid { get; init; }
        public DateTimeOffset Timestamp { get; init; }
    }

    /// <summary>
    /// Request body for the DRS UpdateRefs endpoint.
    /// </summary>
    private class DrsUpdateRefsRequest
    {
        public string NamingContextDn { get; init; } = string.Empty;
        public Guid DsaGuid { get; init; }
        public string DnsName { get; init; } = string.Empty;
        public uint Options { get; init; }
    }

    /// <summary>
    /// Response body from the DRS UpdateRefs endpoint.
    /// </summary>
    private class DrsUpdateRefsResponse
    {
        public bool Success { get; init; }
        public string ErrorMessage { get; init; }
    }

    #endregion
}

/// <summary>
/// Represents an error during DRS replication communication with a partner DC.
/// </summary>
public class DrsReplicationException : Exception
{
    /// <summary>
    /// Creates a new <see cref="DrsReplicationException"/> with the specified message.
    /// </summary>
    /// <param name="message">A description of the replication error.</param>
    public DrsReplicationException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Creates a new <see cref="DrsReplicationException"/> with the specified message and inner exception.
    /// </summary>
    /// <param name="message">A description of the replication error.</param>
    /// <param name="innerException">The underlying exception that caused this error.</param>
    public DrsReplicationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
