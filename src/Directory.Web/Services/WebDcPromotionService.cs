using System.Net.Http.Json;
using System.Text.Json;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Replication;
using Directory.Replication.Drsr;
using Directory.Web.Models;

namespace Directory.Web.Services;

/// <summary>
/// Web-compatible DC promotion service for the replica (join existing domain) flow.
/// Analogous to <see cref="WebDomainProvisioner"/> but replicates from an existing DC
/// instead of creating a new forest. Reports progress through <see cref="SetupStateService"/>
/// so the Vue frontend can poll for detailed replication status.
/// </summary>
public class WebDcPromotionService
{
    private readonly IDirectoryStore _store;
    private readonly INamingContextService _namingContextService;
    private readonly SetupStateService _setupState;
    private readonly WebDomainConfigurationService _domainConfigService;
    private readonly IPasswordPolicy _passwordPolicy;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<WebDcPromotionService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="WebDcPromotionService"/> class.
    /// </summary>
    /// <param name="store">The directory store for creating replicated objects locally.</param>
    /// <param name="namingContextService">Service for reconfiguring naming contexts after promotion.</param>
    /// <param name="setupState">Shared state service for reporting progress to the frontend.</param>
    /// <param name="domainConfigService">Service for reloading domain configuration after promotion.</param>
    /// <param name="passwordPolicy">Password policy for computing NT hashes needed by RPC/NTLM authentication.</param>
    /// <param name="loggerFactory">Logger factory for creating typed loggers.</param>
    /// <param name="logger">Logger instance.</param>
    public WebDcPromotionService(
        IDirectoryStore store,
        INamingContextService namingContextService,
        SetupStateService setupState,
        WebDomainConfigurationService domainConfigService,
        IPasswordPolicy passwordPolicy,
        ILoggerFactory loggerFactory,
        ILogger<WebDcPromotionService> logger)
    {
        _store = store;
        _namingContextService = namingContextService;
        _setupState = setupState;
        _domainConfigService = domainConfigService;
        _passwordPolicy = passwordPolicy;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    /// <summary>
    /// Promotes this server as a replica DC by replicating all naming contexts from the source DC,
    /// registering this DC in the directory, and updating local configuration.
    /// Progress is reported through <see cref="SetupStateService"/> for the frontend to poll.
    /// </summary>
    /// <param name="request">The replica provisioning request containing source DC URL and credentials.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task PromoteAsync(ProvisionReplicaRequest request, CancellationToken ct = default)
    {
        try
        {
            // Initialize provisioning state
            _setupState.IsProvisioning = true;
            _setupState.IsReplicaMode = true;
            _setupState.ProvisioningProgress = 0;
            _setupState.ProvisioningError = null;
            _setupState.ProvisioningPhase = "Connecting to source DC...";
            _setupState.ReplicationCurrentNc = null;
            _setupState.ReplicationObjectsProcessed = 0;
            _setupState.ReplicationObjectsTotal = null;
            _setupState.ReplicationBytesTransferred = 0;

            var sourceDcUrl = request.SourceDcUrl?.TrimEnd('/') ?? "";
            var tenantId = "default";
            var useRpc = string.Equals(request.Transport, "rpc", StringComparison.OrdinalIgnoreCase);

            _logger.LogInformation(
                "Starting replica promotion from source DC {SourceUrl} via {Transport} (DomainName={DomainName})",
                sourceDcUrl, useRpc ? "RPC" : "HTTP", request.DomainName);

            if (useRpc)
            {
                await PromoteViaRpcAsync(request, sourceDcUrl, tenantId, ct);
            }
            else
            {
                await PromoteViaHttpAsync(request, sourceDcUrl, tenantId, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Replica DC promotion failed");
            _setupState.ProvisioningError = ex.Message;
            _setupState.IsProvisioning = false;
            _setupState.ProvisioningPhase = "Failed";
        }
    }

    /// <summary>
    /// Promotes via the HTTP/JSON transport to a Directory.NET peer DC.
    /// </summary>
    private async Task PromoteViaHttpAsync(
        ProvisionReplicaRequest request, string sourceDcUrl, string tenantId, CancellationToken ct)
    {
        // ── Step 1: Bind to source DC via DRS ──────────────────────────────
        ReportProgress(2, "Binding to source DC...");

        var drsLogger = _loggerFactory.CreateLogger<DrsReplicationClient>();
        using var replicationHttpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        using var drsClientForReplication = new DrsReplicationClient(replicationHttpClient, drsLogger);

        await drsClientForReplication.BindAsync(
            sourceDcUrl,
            request.AdminUpn,
            request.AdminPassword,
            ct);

        _logger.LogInformation("Bound to source DC, partner DSA GUID: {DsaGuid}",
            drsClientForReplication.PartnerDsaGuid);

        // ── Step 2: Read rootDSE to discover naming contexts ───────────────
        ReportProgress(5, "Reading source DC rootDSE...");

        var rootDse = await ReadRootDseAsync(replicationHttpClient, sourceDcUrl, ct);
        if (rootDse == null)
        {
            throw new InvalidOperationException(
                "Failed to read rootDSE from source DC. Ensure the DC is running and accessible.");
        }

        var domainDn = rootDse.DefaultNamingContext;
        var configDn = rootDse.ConfigurationNamingContext;
        var schemaDn = rootDse.SchemaNamingContext;
        var domainDnsName = rootDse.DomainDnsName;

        _logger.LogInformation(
            "Source DC rootDSE: domain={DomainDn}, config={ConfigDn}, schema={SchemaDn}",
            domainDn, configDn, schemaDn);

        // ── Step 3: Replicate Schema NC (0-30%) ────────────────────────────
        await ReplicateNamingContextAsync(
            replicationHttpClient, sourceDcUrl, tenantId,
            schemaDn, "Schema", 0, 30, ct);

        // ── Step 4: Replicate Configuration NC (30-60%) ────────────────────
        await ReplicateNamingContextAsync(
            replicationHttpClient, sourceDcUrl, tenantId,
            configDn, "Configuration", 30, 60, ct);

        // ── Step 5: Replicate Domain NC (60-90%) ───────────────────────────
        await ReplicateNamingContextAsync(
            replicationHttpClient, sourceDcUrl, tenantId,
            domainDn, "Domain", 60, 90, ct);

        // ── Step 6: Register this DC in the directory (90-95%) ─────────────
        ReportProgress(90, "Registering this DC in the directory...");
        _setupState.ReplicationCurrentNc = null;

        var hostname = request.Hostname ?? Environment.MachineName;
        var siteName = request.SiteName ?? "Default-First-Site-Name";

        await RegisterDcObjectsAsync(tenantId, domainDn, hostname, siteName, ct);

        // ── Step 7: Register for replication notifications (95-98%) ────────
        ReportProgress(95, "Registering for replication notifications...");

        var localDsaGuid = Guid.NewGuid();
        try
        {
            await drsClientForReplication.UpdateRefsAsync(
                domainDn, localDsaGuid, hostname, addRef: true, ct);
            await drsClientForReplication.UpdateRefsAsync(
                configDn, localDsaGuid, hostname, addRef: true, ct);
            await drsClientForReplication.UpdateRefsAsync(
                schemaDn, localDsaGuid, hostname, addRef: true, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to register for replication notifications (non-fatal)");
        }

        // ── Step 8: Update local configuration (98-100%) ───────────────────
        ReportProgress(98, "Updating local configuration...");

        await UpdateLocalConfigAsync(domainDn, domainDnsName, hostname, siteName, tenantId, ct);
        _namingContextService.Reconfigure(domainDn, domainDnsName, domainSid: null);
        await _domainConfigService.ReloadAsync(ct);

        ReportProgress(100, "Complete!");
        _setupState.IsProvisioned = true;
        _setupState.IsProvisioning = false;

        _logger.LogInformation(
            "Replica DC promotion complete (HTTP). Domain={Domain}, Objects={Objects}, Bytes={Bytes}",
            domainDnsName,
            _setupState.ReplicationObjectsProcessed,
            _setupState.ReplicationBytesTransferred);
    }

    /// <summary>
    /// Promotes via the DCE/RPC over TCP transport to a Windows AD DS domain controller.
    /// Uses the Endpoint Mapper (port 135) to discover the DRSUAPI dynamic port,
    /// NTLM authentication, and the native MS-DRSR replication protocol.
    /// </summary>
    private async Task PromoteViaRpcAsync(
        ProvisionReplicaRequest request, string sourceDcUrl, string tenantId, CancellationToken ct)
    {
        // Determine the RPC hostname — either from the URL or by discovering it via DNS
        string rpcHostname;

        if (!string.IsNullOrWhiteSpace(request.DomainName) && string.IsNullOrWhiteSpace(request.SourceDcUrl))
        {
            // Domain-name-only mode: discover the DC via DNS
            ReportProgress(1, $"Discovering domain controller for {request.DomainName} via DNS...");
            _logger.LogInformation("RPC mode with domain name only — resolving {DomainName} via DNS", request.DomainName);

            try
            {
                var addresses = await System.Net.Dns.GetHostAddressesAsync(request.DomainName, ct);
                if (addresses.Length == 0)
                {
                    throw new InvalidOperationException(
                        $"DNS resolved '{request.DomainName}' but returned no addresses.");
                }

                // Try reverse lookup to get the DC FQDN
                try
                {
                    var hostEntry = await System.Net.Dns.GetHostEntryAsync(addresses[0]);
                    rpcHostname = hostEntry.HostName;
                }
                catch
                {
                    rpcHostname = request.DomainName;
                }

                _logger.LogInformation("DNS resolved {DomainName} to {Hostname} ({IpAddress})",
                    request.DomainName, rpcHostname, addresses[0]);
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                throw new InvalidOperationException(
                    $"DNS resolution failed for '{request.DomainName}'. Ensure the domain name is correct " +
                    $"and that this machine can resolve it. Error: {ex.Message}", ex);
            }
        }
        else
        {
            // Extract hostname from URL for RPC connection
            try
            {
                var uri = new Uri(sourceDcUrl.Contains("://") ? sourceDcUrl : $"https://{sourceDcUrl}");
                rpcHostname = uri.Host;
            }
            catch
            {
                rpcHostname = sourceDcUrl.Split(':')[0].Replace("https://", "").Replace("http://", "");
            }
        }

        // Parse username and domain from AdminUpn (user@domain or DOMAIN\user)
        string username, domain;
        if (request.AdminUpn.Contains('@'))
        {
            var parts = request.AdminUpn.Split('@', 2);
            username = parts[0];
            domain = parts[1];
        }
        else if (request.AdminUpn.Contains('\\'))
        {
            var parts = request.AdminUpn.Split('\\', 2);
            domain = parts[0];
            username = parts[1];
        }
        else
        {
            username = request.AdminUpn;
            domain = rpcHostname;
        }

        // ── Step 1: Connect and bind via RPC ────────────────────────────────
        ReportProgress(2, "Connecting to Windows DC via RPC...");

        var rpcLogger = _loggerFactory.CreateLogger("DrsRpcReplicationClient");

        await using var rpcClient = await DrsReplicationClient.CreateRpcClientAsync(
            rpcHostname, username, domain, request.AdminPassword, rpcLogger, ct);

        _logger.LogInformation("RPC bind succeeded on {Hostname}, extensions={Flags}",
            rpcHostname, rpcClient.PartnerExtensions?.DwFlags);

        // ── Step 2: Discover naming contexts via DRSCrackNames ──────────────
        ReportProgress(5, "Discovering domain metadata via DRSCrackNames...");

        // Crack the DNS domain name to get the domain DN
        var crackResult = await rpcClient.CrackNamesAsync(
            [rpcHostname],
            DsNameFormat.DS_CANONICAL_NAME,
            DsNameFormat.DS_FQDN_1779_NAME,
            ct);

        string domainDn = null;
        string domainDnsName = null;

        if (crackResult.PResult?.RItems.Count > 0)
        {
            var item = crackResult.PResult.RItems[0];
            if (item.Status == DsNameStatus.DS_NAME_NO_ERROR)
            {
                domainDn = item.PName;
                domainDnsName = item.PDomain;
            }
        }

        // If CrackNames didn't give us the DN, try a common pattern
        if (string.IsNullOrEmpty(domainDn))
        {
            // Fall back: convert DNS name to DN (e.g., corp.contoso.com -> DC=corp,DC=contoso,DC=com)
            var dnsName = domain.Contains('.') ? domain : rpcHostname;
            domainDn = "DC=" + string.Join(",DC=", dnsName.Split('.'));
            domainDnsName = dnsName;
        }

        var configDn = $"CN=Configuration,{domainDn}";
        var schemaDn = $"CN=Schema,{configDn}";

        _logger.LogInformation(
            "RPC domain metadata: domain={DomainDn}, config={ConfigDn}, schema={SchemaDn}",
            domainDn, configDn, schemaDn);

        // ── Step 3: Replicate Schema NC via RPC (10-35%) ────────────────────
        ReportProgress(10, "Replicating Schema NC via RPC...");
        _setupState.ReplicationCurrentNc = "Schema";

        await ReplicateNcViaRpcAsync(rpcClient, schemaDn, "Schema", tenantId, 10, 35, ct);

        // ── Step 4: Replicate Configuration NC via RPC (35-60%) ─────────────
        ReportProgress(35, "Replicating Configuration NC via RPC...");
        _setupState.ReplicationCurrentNc = "Configuration";

        await ReplicateNcViaRpcAsync(rpcClient, configDn, "Configuration", tenantId, 35, 60, ct);

        // ── Step 5: Replicate Domain NC via RPC (60-88%) ────────────────────
        ReportProgress(60, "Replicating Domain NC via RPC...");
        _setupState.ReplicationCurrentNc = "Domain";

        await ReplicateNcViaRpcAsync(rpcClient, domainDn, "Domain", tenantId, 60, 88, ct);

        // ── Step 6: Register this DC in the directory (88-93%) ──────────────
        ReportProgress(88, "Registering this DC in the directory...");
        _setupState.ReplicationCurrentNc = null;

        var hostname = request.Hostname ?? Environment.MachineName;
        var siteName = request.SiteName ?? "Default-First-Site-Name";

        await RegisterDcObjectsAsync(tenantId, domainDn, hostname, siteName, ct);

        // ── Step 7: Register for replication notifications via RPC (93-96%) ─
        ReportProgress(93, "Registering for replication notifications via RPC...");

        var localDsaGuid = Guid.NewGuid();
        try
        {
            await rpcClient.UpdateRefsAsync(domainDn, localDsaGuid, hostname, addRef: true, ct);
            await rpcClient.UpdateRefsAsync(configDn, localDsaGuid, hostname, addRef: true, ct);
            await rpcClient.UpdateRefsAsync(schemaDn, localDsaGuid, hostname, addRef: true, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to register for replication notifications via RPC (non-fatal)");
        }

        // ── Step 8: Update local configuration (96-100%) ────────────────────
        ReportProgress(96, "Updating local configuration...");

        await UpdateLocalConfigAsync(domainDn, domainDnsName ?? domain, hostname, siteName, tenantId, ct);
        _namingContextService.Reconfigure(domainDn, domainDnsName ?? domain, domainSid: null);
        await _domainConfigService.ReloadAsync(ct);

        ReportProgress(100, "Complete!");
        _setupState.IsProvisioned = true;
        _setupState.IsProvisioning = false;

        _logger.LogInformation(
            "Replica DC promotion complete (RPC). Domain={Domain}, Objects={Objects}, Bytes={Bytes}",
            domainDnsName ?? domain,
            _setupState.ReplicationObjectsProcessed,
            _setupState.ReplicationBytesTransferred);
    }

    /// <summary>
    /// Replicates a naming context via the RPC transport, tracking progress within
    /// the specified percentage range. Applies each batch of replicated objects
    /// directly to the local store by walking the <see cref="REPLENTINFLIST"/> chain.
    /// </summary>
    private async Task ReplicateNcViaRpcAsync(
        DrsRpcReplicationClient rpcClient,
        string namingContextDn,
        string ncFriendlyName,
        string tenantId,
        int progressStart,
        int progressEnd,
        CancellationToken ct)
    {
        _logger.LogInformation("RPC replicating {NcName} NC: {NcDn}", ncFriendlyName, namingContextDn);

        var usnFrom = new USN_VECTOR();
        int totalObjects = 0;
        long totalBytes = 0;

        await foreach (var reply in rpcClient.GetNCChangesAsync(namingContextDn, usnFrom, ct: ct))
        {
            // Walk the REPLENTINFLIST linked list and apply each object to the store
            int batchApplied = 0;
            var current = reply.PObjects;
            while (current != null)
            {
                ct.ThrowIfCancellationRequested();
                await ApplyRpcReplicatedEntryAsync(tenantId, namingContextDn, current.Entinf, ct);
                batchApplied++;
                current = current.PNextEntInf;
            }

            totalObjects += batchApplied;
            totalBytes += reply.CNumBytes;

            _setupState.ReplicationObjectsProcessed = totalObjects;
            _setupState.ReplicationBytesTransferred += reply.CNumBytes;

            var estimatedProgress = progressStart +
                (int)((progressEnd - progressStart) * 0.8 * totalObjects / Math.Max(totalObjects + 1000, 1));
            ReportProgress(
                Math.Min(estimatedProgress, progressEnd - 1),
                $"Replicating {ncFriendlyName} via RPC: {totalObjects} objects...");

            _logger.LogInformation(
                "RPC replicating {NcName} NC: {Count} objects so far, applied={Applied}, moreData={More}",
                ncFriendlyName, totalObjects, batchApplied, reply.FMoreData);
        }

        ReportProgress(progressEnd, $"{ncFriendlyName} replication complete ({totalObjects} objects)");

        _logger.LogInformation(
            "RPC replication of {NcName} NC complete: {Count} objects, {Bytes} bytes",
            ncFriendlyName, totalObjects, totalBytes);
    }

    /// <summary>
    /// Applies a single replicated ENTINF (from an RPC V6 reply) to the local directory store.
    /// Extracts the DN, GUID, and attributes from the NDR-decoded structure.
    /// </summary>
    private async Task ApplyRpcReplicatedEntryAsync(
        string tenantId, string namingContextDn, ENTINF entinf, CancellationToken ct)
    {
        var dn = entinf.PName.StringName;
        if (string.IsNullOrEmpty(dn))
            return;

        var guid = entinf.PName.Guid;
        var isDeleted = entinf.UlFlags != 0;

        var existing = await _store.GetByDnAsync(tenantId, dn, ct);

        if (existing != null)
        {
            existing.IsDeleted = isDeleted;
            existing.WhenChanged = DateTimeOffset.UtcNow;

            // Apply raw attribute values from the ATTRBLOCK
            foreach (var attr in entinf.AttrBlock.PAttr)
            {
                // Store as raw bytes keyed by ATTRTYP since we don't have prefix table mapping here
                var attrName = $"attr_{attr.AttrTyp.Value}";
                if (attr.AttrVal.PVal.Count == 1)
                {
                    existing.SetAttribute(attrName,
                        new DirectoryAttribute(attrName, attr.AttrVal.PVal[0].PVal));
                }
                else if (attr.AttrVal.PVal.Count > 1)
                {
                    existing.SetAttribute(attrName,
                        new DirectoryAttribute(attrName,
                            attr.AttrVal.PVal.Select(v => (object)v.PVal).ToArray()));
                }
            }

            await _store.UpdateAsync(existing, ct);
        }
        else
        {
            var obj = new DirectoryObject
            {
                Id = dn.ToLowerInvariant(),
                TenantId = tenantId,
                DomainDn = namingContextDn,
                DistinguishedName = dn,
                ObjectGuid = guid != Guid.Empty ? guid.ToString() : Guid.NewGuid().ToString(),
                IsDeleted = isDeleted,
                WhenCreated = DateTimeOffset.UtcNow,
                WhenChanged = DateTimeOffset.UtcNow,
            };

            // Set ParentDn and Cn from the DN
            var commaIndex = dn.IndexOf(',');
            obj.ParentDn = commaIndex >= 0 ? dn[(commaIndex + 1)..] : "";

            var firstComponent = dn.Split(',')[0];
            var eqIndex = firstComponent.IndexOf('=');
            obj.Cn = eqIndex >= 0 ? firstComponent[(eqIndex + 1)..] : firstComponent;

            // Apply raw attribute values
            foreach (var attr in entinf.AttrBlock.PAttr)
            {
                var attrName = $"attr_{attr.AttrTyp.Value}";
                if (attr.AttrVal.PVal.Count == 1)
                {
                    obj.SetAttribute(attrName,
                        new DirectoryAttribute(attrName, attr.AttrVal.PVal[0].PVal));
                }
                else if (attr.AttrVal.PVal.Count > 1)
                {
                    obj.SetAttribute(attrName,
                        new DirectoryAttribute(attrName,
                            attr.AttrVal.PVal.Select(v => (object)v.PVal).ToArray()));
                }
            }

            await _store.CreateAsync(obj, ct);
        }
    }

    /// <summary>
    /// Reads the rootDSE from the source DC to discover naming contexts and domain metadata.
    /// </summary>
    private async Task<RootDseInfo> ReadRootDseAsync(
        HttpClient httpClient, string sourceDcUrl, CancellationToken ct)
    {
        try
        {
            var response = await httpClient.GetAsync($"{sourceDcUrl}/drs/rootdse", ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, ct);

            return new RootDseInfo
            {
                DefaultNamingContext = json.GetProperty("defaultNamingContext").GetString() ?? "",
                ConfigurationNamingContext = json.GetProperty("configurationNamingContext").GetString() ?? "",
                SchemaNamingContext = json.GetProperty("schemaNamingContext").GetString() ?? "",
                DomainDnsName = json.GetProperty("domainDnsName").GetString() ?? "",
                ForestDnsName = json.GetProperty("forestDnsName").GetString() ?? "",
                DsaGuid = json.TryGetProperty("dsaGuid", out var dsaGuidProp) ? dsaGuidProp.GetString() : null,
                ServerName = json.TryGetProperty("serverName", out var serverProp) ? serverProp.GetString() : null,
                DomainControllerFunctionality = json.TryGetProperty("domainControllerFunctionality", out var funcProp)
                    ? funcProp.GetInt32() : 7,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read rootDSE from {Url}", sourceDcUrl);
            return null;
        }
    }

    /// <summary>
    /// Replicates a single naming context from the source DC using paginated GetNCChanges calls.
    /// Updates <see cref="SetupStateService"/> progress within the specified percentage range.
    /// </summary>
    private async Task ReplicateNamingContextAsync(
        HttpClient httpClient,
        string sourceDcUrl,
        string tenantId,
        string namingContextDn,
        string ncFriendlyName,
        int progressStart,
        int progressEnd,
        CancellationToken ct)
    {
        _logger.LogInformation("Replicating {NcName} NC: {NcDn}", ncFriendlyName, namingContextDn);

        _setupState.ReplicationCurrentNc = ncFriendlyName;
        _setupState.ReplicationObjectsProcessed = 0;
        _setupState.ReplicationObjectsTotal = null;
        ReportProgress(progressStart, $"Replicating {ncFriendlyName} naming context...");

        long usnFrom = 0;
        int totalObjects = 0;
        long totalBytes = 0;
        bool moreData = true;

        while (moreData)
        {
            var request = new GetNCChangesRequest
            {
                TenantId = tenantId,
                NamingContextDn = namingContextDn,
                UsnFrom = usnFrom,
                MaxObjects = 1000,
                MaxBytes = 10_000_000,
            };

            HttpResponseMessage response;
            try
            {
                response = await httpClient.PostAsJsonAsync(
                    $"{sourceDcUrl}/drs/GetNCChanges", request, JsonOptions, ct);
                response.EnsureSuccessStatusCode();
            }
            catch (HttpRequestException ex)
            {
                throw new InvalidOperationException(
                    $"Replication of {ncFriendlyName} NC failed at USN {usnFrom}: {ex.Message}", ex);
            }

            var changesResponse = await response.Content.ReadFromJsonAsync<GetNCChangesResponse>(JsonOptions, ct)
                ?? throw new InvalidOperationException(
                    $"Empty response during {ncFriendlyName} NC replication.");

            // Apply each replicated object locally
            foreach (var entry in changesResponse.Entries)
            {
                await ApplyReplicatedEntryAsync(tenantId, namingContextDn, entry, ct);
                totalObjects++;
            }

            // Estimate bytes for this batch
            long batchBytes = EstimateBatchSize(changesResponse);
            totalBytes += batchBytes;

            // Update replication-specific progress
            _setupState.ReplicationObjectsProcessed = totalObjects;
            _setupState.ReplicationBytesTransferred += batchBytes;

            // Calculate progress within our range
            // If we know there's more data, estimate progress within the range
            if (changesResponse.MoreData)
            {
                // Estimate ~80% done within this NC's range when there's more data
                var estimatedProgress = progressStart +
                    (int)((progressEnd - progressStart) * 0.8 * totalObjects / Math.Max(totalObjects + 1000, 1));
                ReportProgress(
                    Math.Min(estimatedProgress, progressEnd - 1),
                    $"Replicating {ncFriendlyName}: {totalObjects} objects...");
            }

            usnFrom = changesResponse.HighestUsn;
            moreData = changesResponse.MoreData;

            _logger.LogInformation(
                "Replicating {NcName} NC: {Count} objects so far, highestUsn={Usn}, moreData={More}",
                ncFriendlyName, totalObjects, usnFrom, moreData);
        }

        ReportProgress(progressEnd, $"{ncFriendlyName} replication complete ({totalObjects} objects)");

        _logger.LogInformation(
            "Replication of {NcName} NC complete: {Count} objects, {Bytes} bytes",
            ncFriendlyName, totalObjects, totalBytes);
    }

    /// <summary>
    /// Applies a single replicated entry to the local directory store,
    /// creating or updating the object as needed.
    /// </summary>
    private async Task ApplyReplicatedEntryAsync(
        string tenantId, string namingContextDn, ReplicationEntry entry, CancellationToken ct)
    {
        var existing = await _store.GetByDnAsync(tenantId, entry.Dn, ct);

        if (existing != null)
        {
            // Update existing object with replicated data
            existing.ObjectClass = entry.ObjectClass;
            existing.IsDeleted = entry.IsDeleted;
            existing.USNChanged = entry.UsnOriginating;
            existing.WhenChanged = DateTimeOffset.UtcNow;

            foreach (var (attrName, values) in entry.Attributes)
            {
                if (values.Count == 1)
                    existing.SetAttribute(attrName, new DirectoryAttribute(attrName, values[0]));
                else if (values.Count > 1)
                    existing.SetAttribute(attrName, new DirectoryAttribute(attrName, values.ToArray()));
            }

            await _store.UpdateAsync(existing, ct);
        }
        else
        {
            // Create new object from replicated data
            var obj = new DirectoryObject
            {
                Id = entry.Dn.ToLowerInvariant(),
                TenantId = tenantId,
                DomainDn = namingContextDn,
                DistinguishedName = entry.Dn,
                ObjectGuid = entry.ObjectGuid ?? Guid.NewGuid().ToString(),
                ObjectClass = entry.ObjectClass,
                IsDeleted = entry.IsDeleted,
                USNCreated = entry.UsnOriginating,
                USNChanged = entry.UsnOriginating,
                WhenCreated = DateTimeOffset.UtcNow,
                WhenChanged = DateTimeOffset.UtcNow,
            };

            // Set ParentDn from the DN
            var commaIndex = entry.Dn.IndexOf(',');
            obj.ParentDn = commaIndex >= 0 ? entry.Dn[(commaIndex + 1)..] : "";

            // Set Cn from first RDN component
            var firstComponent = entry.Dn.Split(',')[0];
            var eqIndex = firstComponent.IndexOf('=');
            obj.Cn = eqIndex >= 0 ? firstComponent[(eqIndex + 1)..] : firstComponent;

            // Apply attributes
            foreach (var (attrName, values) in entry.Attributes)
            {
                if (values.Count == 1)
                    obj.SetAttribute(attrName, new DirectoryAttribute(attrName, values[0]));
                else if (values.Count > 1)
                    obj.SetAttribute(attrName, new DirectoryAttribute(attrName, values.ToArray()));
            }

            await _store.CreateAsync(obj, ct);
        }
    }

    /// <summary>
    /// Registers this DC in the directory by creating the Server, NTDS Settings,
    /// and computer account objects.
    /// </summary>
    private async Task RegisterDcObjectsAsync(
        string tenantId, string domainDn, string hostname, string siteName, CancellationToken ct)
    {
        var configDn = $"CN=Configuration,{domainDn}";
        var sitesDn = $"CN=Sites,{configDn}";
        var siteDn = $"CN={siteName},{sitesDn}";
        var serversDn = $"CN=Servers,{siteDn}";
        var serverDn = $"CN={hostname},{serversDn}";
        var ntdsSettingsDn = $"CN=NTDS Settings,{serverDn}";

        var now = DateTimeOffset.UtcNow;
        var dsaGuid = Guid.NewGuid().ToString();

        // Create Server object if it doesn't already exist
        var existingServer = await _store.GetByDnAsync(tenantId, serverDn, ct);
        if (existingServer == null)
        {
            var serverObj = new DirectoryObject
            {
                Id = serverDn.ToLowerInvariant(),
                TenantId = tenantId,
                DomainDn = configDn,
                DistinguishedName = serverDn,
                ObjectGuid = Guid.NewGuid().ToString(),
                ObjectClass = ["top", "server"],
                Cn = hostname,
                ParentDn = serversDn,
                WhenCreated = now,
                WhenChanged = now,
            };
            serverObj.SetAttribute("dNSHostName", new DirectoryAttribute("dNSHostName", hostname));
            serverObj.SetAttribute("serverReference",
                new DirectoryAttribute("serverReference", $"CN={hostname},OU=Domain Controllers,{domainDn}"));

            await _store.CreateAsync(serverObj, ct);
            _logger.LogInformation("Created Server object: {Dn}", serverDn);
        }

        // Create NTDS Settings object
        var existingNtds = await _store.GetByDnAsync(tenantId, ntdsSettingsDn, ct);
        if (existingNtds == null)
        {
            var ntdsObj = new DirectoryObject
            {
                Id = ntdsSettingsDn.ToLowerInvariant(),
                TenantId = tenantId,
                DomainDn = configDn,
                DistinguishedName = ntdsSettingsDn,
                ObjectGuid = dsaGuid,
                ObjectClass = ["top", "applicationSettings", "nTDSDSA"],
                Cn = "NTDS Settings",
                ParentDn = serverDn,
                WhenCreated = now,
                WhenChanged = now,
            };
            ntdsObj.SetAttribute("invocationId",
                new DirectoryAttribute("invocationId", Guid.NewGuid().ToString()));
            ntdsObj.SetAttribute("dMDLocation",
                new DirectoryAttribute("dMDLocation", $"CN=Schema,{configDn}"));
            ntdsObj.SetAttribute("msDS-HasDomainNCs",
                new DirectoryAttribute("msDS-HasDomainNCs", domainDn));
            ntdsObj.SetAttribute("msDS-Behavior-Version",
                new DirectoryAttribute("msDS-Behavior-Version", 7));

            await _store.CreateAsync(ntdsObj, ct);
            _logger.LogInformation("Created NTDS Settings object: {Dn}", ntdsSettingsDn);
        }

        // Create computer account in Domain Controllers OU
        var computerDn = $"CN={hostname},OU=Domain Controllers,{domainDn}";
        var existingComputer = await _store.GetByDnAsync(tenantId, computerDn, ct);
        if (existingComputer == null)
        {
            var computerObj = new DirectoryObject
            {
                Id = computerDn.ToLowerInvariant(),
                TenantId = tenantId,
                DomainDn = domainDn,
                DistinguishedName = computerDn,
                ObjectGuid = Guid.NewGuid().ToString(),
                ObjectClass = ["top", "person", "organizationalPerson", "user", "computer"],
                Cn = hostname,
                ParentDn = $"OU=Domain Controllers,{domainDn}",
                WhenCreated = now,
                WhenChanged = now,
            };
            computerObj.SetAttribute("sAMAccountName",
                new DirectoryAttribute("sAMAccountName", $"{hostname}$"));
            computerObj.SetAttribute("userAccountControl",
                new DirectoryAttribute("userAccountControl", 532480)); // SERVER_TRUST_ACCOUNT | TRUSTED_FOR_DELEGATION
            computerObj.SetAttribute("dNSHostName",
                new DirectoryAttribute("dNSHostName", hostname));
            computerObj.SetAttribute("servicePrincipalName",
                new DirectoryAttribute("servicePrincipalName", $"HOST/{hostname}", $"HOST/{hostname}."));
            computerObj.SetAttribute("serverReferenceBL",
                new DirectoryAttribute("serverReferenceBL", serverDn));

            await _store.CreateAsync(computerObj, ct);
            _logger.LogInformation("Created computer account: {Dn}", computerDn);
        }
    }

    /// <summary>
    /// Updates appsettings.json with the domain configuration from the source DC.
    /// </summary>
    private async Task UpdateLocalConfigAsync(
        string domainDn, string domainDnsName, string hostname,
        string siteName, string tenantId, CancellationToken ct)
    {
        var configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

        // Also try the source directory (for development)
        var sourceConfigPath = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "appsettings.json");
        if (File.Exists(sourceConfigPath))
            configPath = Path.GetFullPath(sourceConfigPath);

        if (!File.Exists(configPath))
        {
            _logger.LogWarning("Could not find appsettings.json at {Path}, skipping config update", configPath);
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(configPath, ct);
            var config = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? [];

            // Update NamingContexts
            var namingContexts = new Dictionary<string, object>
            {
                ["DomainDn"] = domainDn,
                ["ForestDnsName"] = domainDnsName,
                ["DomainSid"] = ""
            };
            config["NamingContexts"] = JsonSerializer.SerializeToElement(namingContexts);

            // DC Node configuration
            var dcNode = new Dictionary<string, object>
            {
                ["Hostname"] = hostname.ToLowerInvariant(),
                ["SiteName"] = siteName,
                ["TenantId"] = tenantId,
                ["DomainDn"] = domainDn,
                ["BindAddresses"] = new[] { "0.0.0.0" }
            };
            config["DcNode"] = JsonSerializer.SerializeToElement(dcNode);

            var options = new JsonSerializerOptions { WriteIndented = true };
            await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(config, options), ct);

            _logger.LogInformation("Local configuration updated at {Path}", configPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update appsettings.json");
        }
    }

    /// <summary>
    /// Reports progress to <see cref="SetupStateService"/>.
    /// </summary>
    private void ReportProgress(int percent, string phase)
    {
        _setupState.ProvisioningProgress = percent;
        _setupState.ProvisioningPhase = phase;
        _logger.LogInformation("Replica promotion: {Percent}% - {Phase}", percent, phase);
    }

    /// <summary>
    /// Estimates the byte size of a replication response batch.
    /// </summary>
    private static long EstimateBatchSize(GetNCChangesResponse response)
    {
        long size = 64;
        foreach (var entry in response.Entries)
        {
            size += entry.Dn.Length * 2 + 64;
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

    /// <summary>
    /// Internal DTO for rootDSE information read from the source DC.
    /// </summary>
    private sealed class RootDseInfo
    {
        public string DefaultNamingContext { get; init; } = "";
        public string ConfigurationNamingContext { get; init; } = "";
        public string SchemaNamingContext { get; init; } = "";
        public string DomainDnsName { get; init; } = "";
        public string ForestDnsName { get; init; } = "";
        public string DsaGuid { get; init; }
        public string ServerName { get; init; }
        public int DomainControllerFunctionality { get; init; } = 7;
    }
}
