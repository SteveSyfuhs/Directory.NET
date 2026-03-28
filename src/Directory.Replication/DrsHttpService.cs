using System.Net;
using System.Text;
using System.Text.Json;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Replication.Drsr;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Directory.Replication;

/// <summary>
/// Exposes the DRS replication protocol as an HTTP API.
/// Real AD uses DCE/RPC for MS-DRSR, but since we're built on Cosmos DB,
/// we expose an HTTP endpoint that allows other DC instances to call
/// GetNCChanges, DsReplicaSync, and receive change notifications.
///
/// Endpoints:
/// - POST /drs/GetNCChanges — Pull replication changes for a naming context
/// - POST /drs/DsReplicaSync — Trigger replication from a source DC
/// - POST /drs/notify — Receive change notification from a partner DC
/// - GET  /drs/rootdse — Return key rootDSE attributes for this DC
/// - GET  /drs/health — Detailed health status with replication partner info
///
/// This runs on a configurable port (default 9389, matching AD Web Services port).
/// </summary>
public class DrsHttpService : IHostedService, IDisposable
{
    private readonly DrsProtocol _drsProtocol;
    private readonly ReplicationTopology _topology;
    private readonly DcInstanceInfo _dcInfo;
    private readonly INamingContextService _ncService;
    private readonly RodcService _rodcService;
    private readonly ReplicationOptions _options;
    private readonly ILogger<DrsHttpService> _logger;

    /// <summary>
    /// Reference to the ReplicationScheduler, set after construction via <see cref="SetReplicationScheduler"/>.
    /// Nullable because of circular dependency: DrsHttpService starts before ReplicationScheduler.
    /// </summary>
    private ReplicationScheduler _replicationScheduler;

    private HttpListener _listener;
    private CancellationTokenSource _cts;
    private Task _listenTask;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public DrsHttpService(
        DrsProtocol drsProtocol,
        ReplicationTopology topology,
        DcInstanceInfo dcInfo,
        INamingContextService ncService,
        RodcService rodcService,
        IOptions<ReplicationOptions> options,
        ILogger<DrsHttpService> logger)
    {
        _drsProtocol = drsProtocol;
        _topology = topology;
        _dcInfo = dcInfo;
        _ncService = ncService;
        _rodcService = rodcService;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Injects the ReplicationScheduler reference after construction to avoid circular DI.
    /// Called during service registration.
    /// </summary>
    /// <param name="scheduler">The replication scheduler instance.</param>
    public void SetReplicationScheduler(ReplicationScheduler scheduler)
    {
        _replicationScheduler = scheduler;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var port = _options.HttpPort > 0 ? _options.HttpPort : 9389;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://+:{port}/drs/");
        _listener.Start();
        _listenTask = ListenAsync(_cts.Token);
        _logger.LogInformation("DRS HTTP API listening on port {Port}", port);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        _listener?.Stop();
        if (_listenTask != null)
            await _listenTask;
    }

    private async Task ListenAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = ProcessRequestAsync(context, ct);
            }
            catch (HttpListenerException) when (ct.IsCancellationRequested) { break; }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DRS HTTP listener error");
            }
        }
    }

    private async Task ProcessRequestAsync(HttpListenerContext context, CancellationToken ct)
    {
        try
        {
            var path = context.Request.Url?.AbsolutePath ?? "";
            var method = context.Request.HttpMethod;

            if (method == "POST" && path.EndsWith("/GetNCChanges", StringComparison.OrdinalIgnoreCase))
            {
                await HandleGetNCChangesAsync(context, ct);
            }
            else if (method == "POST" && path.EndsWith("/DsReplicaSync", StringComparison.OrdinalIgnoreCase))
            {
                await HandleDsReplicaSyncAsync(context, ct);
            }
            else if (method == "POST" && path.EndsWith("/notify", StringComparison.OrdinalIgnoreCase))
            {
                await HandleNotifyAsync(context, ct);
            }
            else if (method == "GET" && path.EndsWith("/rootdse", StringComparison.OrdinalIgnoreCase))
            {
                await HandleRootDseAsync(context);
            }
            else if (method == "GET" && path.EndsWith("/health", StringComparison.OrdinalIgnoreCase))
            {
                await HandleHealthAsync(context);
            }
            else
            {
                context.Response.StatusCode = 404;
                await WriteJsonResponse(context, new { error = "Not found" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DRS request processing error");
            context.Response.StatusCode = 500;
            await WriteJsonResponse(context, new { error = ex.Message });
        }
        finally
        {
            context.Response.Close();
        }
    }

    private async Task HandleGetNCChangesAsync(HttpListenerContext context, CancellationToken ct)
    {
        using var reader = new StreamReader(context.Request.InputStream);
        var body = await reader.ReadToEndAsync(ct);
        var request = JsonSerializer.Deserialize<GetNCChangesRequest>(body, JsonOptions);

        if (request is null)
        {
            context.Response.StatusCode = 400;
            await WriteJsonResponse(context, new { error = "Invalid request" });
            return;
        }

        var result = await _drsProtocol.GetNCChangesAsync(request, ct);
        await WriteJsonResponse(context, result);
    }

    private async Task HandleDsReplicaSyncAsync(HttpListenerContext context, CancellationToken ct)
    {
        using var reader = new StreamReader(context.Request.InputStream);
        var body = await reader.ReadToEndAsync(ct);
        var request = JsonSerializer.Deserialize<DsReplicaSyncRequest>(body, JsonOptions);

        if (request is null)
        {
            context.Response.StatusCode = 400;
            await WriteJsonResponse(context, new { error = "Invalid request" });
            return;
        }

        var result = await _drsProtocol.DsReplicaSyncAsync(request, ct);
        await WriteJsonResponse(context, result);
    }

    /// <summary>
    /// Handles POST /drs/notify — receives a change notification from a partner DC.
    /// Signals the ReplicationScheduler to immediately pull from the notifying partner.
    /// </summary>
    private async Task HandleNotifyAsync(HttpListenerContext context, CancellationToken ct)
    {
        using var reader = new StreamReader(context.Request.InputStream);
        var body = await reader.ReadToEndAsync(ct);
        var notification = JsonSerializer.Deserialize<ChangeNotification>(body, JsonOptions);

        if (notification is null || string.IsNullOrEmpty(notification.NamingContextDn))
        {
            context.Response.StatusCode = 400;
            await WriteJsonResponse(context, new { error = "Invalid notification" });
            return;
        }

        _logger.LogDebug(
            "Received change notification from {SourceDsa} for NC {NC} (USN {Usn})",
            notification.SourceDsaGuid, notification.NamingContextDn, notification.LatestUsn);

        // Signal the replication scheduler to pull from this partner
        if (_replicationScheduler is not null &&
            Guid.TryParse(notification.SourceDsaGuid, out var sourceDsaGuid))
        {
            _replicationScheduler.SignalPullFromPartner(
                sourceDsaGuid, notification.NamingContextDn, notification.LatestUsn);
        }

        // Respond with 200 OK — the partner only needs acknowledgment
        await WriteJsonResponse(context, new { status = "ok" });
    }

    /// <summary>
    /// Handles GET /drs/rootdse — returns key rootDSE attributes for this DC.
    /// Used during DC promotion and by tools that need to discover DC capabilities.
    /// </summary>
    private async Task HandleRootDseAsync(HttpListenerContext context)
    {
        var domainNc = _ncService.GetDomainNc();
        var configNc = _ncService.GetConfigurationNc();
        var schemaNc = _ncService.GetSchemaNc();

        var domainDn = domainNc.Dn;
        var configDn = configNc.Dn;
        var schemaDn = schemaNc.Dn;

        var rootDse = new
        {
            defaultNamingContext = domainDn,
            configurationNamingContext = configDn,
            schemaNamingContext = schemaDn,
            dsServiceName = _dcInfo.NtdsSettingsDn(domainDn),
            serverName = _dcInfo.ServerDn(domainDn),
            domainControllerFunctionality = 7, // Windows Server 2016
            forestFunctionality = 7,
            invocationId = _dcInfo.InvocationId,
            dsaGuid = _dcInfo.InstanceId,
            domainDnsName = domainNc.DnsName,
            forestDnsName = domainNc.DnsName,
            domainSid = domainNc.DomainSid ?? "",
            isWritable = !_rodcService.IsRodc, // false when this instance is configured as an RODC
        };

        await WriteJsonResponse(context, rootDse);
    }

    /// <summary>
    /// Handles GET /drs/health — returns detailed health status including
    /// DC identity, replication state, and partner information.
    /// </summary>
    private async Task HandleHealthAsync(HttpListenerContext context)
    {
        var inboundPartners = _topology.InboundPartners.Select(p => new
        {
            dsaGuid = p.DsaGuid,
            dnsName = p.DnsName,
            namingContextDn = p.NamingContextDn,
            transportType = p.TransportType,
            lastSyncSuccess = p.LastSyncSuccess,
            lastSyncAttempt = p.LastSyncAttempt,
            lastSyncResult = p.LastSyncResult,
            consecutiveFailures = p.ConsecutiveFailures,
            lastUsnSynced = p.LastUsnSynced,
        }).ToList();

        var outboundPartners = _topology.OutboundPartners.Select(p => new
        {
            dsaGuid = p.DsaGuid,
            dnsName = p.DnsName,
            namingContextDn = p.NamingContextDn,
        }).ToList();

        var health = new
        {
            status = "healthy",
            dcGuid = _dcInfo.InstanceId,
            invocationId = _dcInfo.InvocationId,
            hostname = _dcInfo.Hostname,
            siteName = _dcInfo.SiteName,
            isWritable = !_dcInfo.IsRodc,
            startedAt = _dcInfo.StartedAt,
            timestamp = DateTimeOffset.UtcNow,
            replicationPartners = new
            {
                inbound = inboundPartners,
                outbound = outboundPartners,
            },
        };

        await WriteJsonResponse(context, health);
    }

    private static async Task WriteJsonResponse(HttpListenerContext context, object data)
    {
        context.Response.ContentType = "application/json";
        var json = JsonSerializer.Serialize(data, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes);
    }

    public void Dispose()
    {
        _cts?.Dispose();
        (_listener as IDisposable)?.Dispose();
    }
}
