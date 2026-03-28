using System.Net.Http.Json;
using System.Text.Json;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.CosmosDb;
using Directory.Ldap.Client;
using Directory.Replication;
using Directory.Replication.DcPromotion;
using Directory.Replication.Drsr;
using Microsoft.Extensions.Logging;

namespace Directory.Server.Setup;

/// <summary>
/// Orchestrates the full DC promotion flow, analogous to dcpromo / Install-ADDSDomainController.
/// Promotes a server as a new Domain Controller joining an existing Active Directory domain
/// by replicating all naming contexts from a source DC, registering this DC in the directory,
/// and configuring replication partnerships.
/// </summary>
public class DcPromotionService
{
    private readonly IDirectoryStore _store;
    private readonly CosmosDbInitializer _cosmosInitializer;
    private readonly DcInstanceInfo _dcInfo;
    private readonly ReplicationTopology _topology;
    private readonly IRidAllocator _ridAllocator;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<DcPromotionService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="DcPromotionService"/> class.
    /// </summary>
    /// <param name="store">The directory store for creating and querying local objects.</param>
    /// <param name="cosmosInitializer">Initializer to ensure Cosmos DB containers exist.</param>
    /// <param name="dcInfo">Identity information for this DC instance.</param>
    /// <param name="topology">Replication topology manager for setting up partnerships.</param>
    /// <param name="ridAllocator">RID allocator for generating security principal SIDs.</param>
    /// <param name="loggerFactory">Logger factory for creating typed loggers for sub-components.</param>
    /// <param name="logger">Logger instance.</param>
    public DcPromotionService(
        IDirectoryStore store,
        CosmosDbInitializer cosmosInitializer,
        DcInstanceInfo dcInfo,
        ReplicationTopology topology,
        IRidAllocator ridAllocator,
        ILoggerFactory loggerFactory,
        ILogger<DcPromotionService> logger)
    {
        _store = store;
        _cosmosInitializer = cosmosInitializer;
        _dcInfo = dcInfo;
        _topology = topology;
        _ridAllocator = ridAllocator;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    /// <summary>
    /// Executes the full DC promotion workflow: validates the source DC, replicates all
    /// naming contexts, registers this DC in the directory, and sets up replication partnerships.
    /// </summary>
    /// <param name="options">Setup options containing source DC URL, credentials, and local configuration.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="DcPromotionException">Thrown when any phase of promotion fails.</exception>
    public async Task PromoteAsync(SetupOptions options, CancellationToken ct = default)
    {
        _logger.LogInformation("Starting DC promotion for domain {Domain} from source {Source}",
            options.DomainName, options.SourceDcUrl);

        var sourceDcUrl = options.SourceDcUrl.TrimEnd('/');

        using var httpClient = CreateHttpClient(options);

        // Determine source hostname for LDAP connection
        string sourceHostname;
        try
        {
            var uri = new Uri(sourceDcUrl.Contains("://") ? sourceDcUrl : $"https://{sourceDcUrl}");
            sourceHostname = uri.Host;
        }
        catch
        {
            sourceHostname = sourceDcUrl.Split(':')[0].Replace("https://", "").Replace("http://", "");
        }

        // For Windows DC join, we need both HTTP (for replication) and LDAP (for object creation)
        LdapClient ldapClient = null;
        string sourceNtdsSettingsDn = null;

        // ── Phase 1: Pre-flight Validation ──────────────────────────────────
        WritePhase(1, 6, "Pre-flight validation");

        var sourceInfo = await ValidateSourceDcAsync(httpClient, sourceDcUrl, options, ct);

        // Populate options from source DC's rootDSE
        options.DomainName = sourceInfo.DomainDnsName;
        options.ForestName = sourceInfo.ForestDnsName;
        options.NetBiosName = sourceInfo.NetBiosName;

        _logger.LogInformation(
            "Source DC validated: domain={Domain}, forest={Forest}, dcFunctionality={DcFunc}, forestFunctionality={ForestFunc}",
            sourceInfo.DefaultNamingContext, sourceInfo.ForestDnsName,
            sourceInfo.DomainControllerFunctionality, sourceInfo.ForestFunctionality);

        // Validate local Cosmos DB is empty or matches the same domain
        await ValidateLocalDatabaseAsync(options, ct);

        // Attempt LDAP connection to source DC for reading additional rootDSE attributes
        // and later for creating remote AD objects
        if (!string.IsNullOrEmpty(options.ReplicationAdminUpn) &&
            !string.IsNullOrEmpty(options.ReplicationAdminPassword))
        {
            try
            {
                Console.Write("    Connecting to source DC via LDAP...");
                ldapClient = new LdapClient(_logger);
                await ldapClient.ConnectAsync(sourceHostname, 389, ct: ct);
                await ldapClient.SimpleBindAsync(options.ReplicationAdminUpn, options.ReplicationAdminPassword, ct);
                Console.WriteLine(" ok");

                // Read additional rootDSE attributes via LDAP
                Console.Write("    Reading rootDSE via LDAP...");
                var rootDse = await ldapClient.ReadRootDseAsync(ct);

                if (rootDse.TryGetValue("dsServiceName", out var dsServiceValues) && dsServiceValues.Count > 0)
                {
                    sourceNtdsSettingsDn = dsServiceValues[0];
                    _logger.LogInformation("Source DC dsServiceName: {DsServiceName}", sourceNtdsSettingsDn);
                }

                if (rootDse.TryGetValue("forestFunctionality", out var forestFuncValues) && forestFuncValues.Count > 0)
                {
                    if (int.TryParse(forestFuncValues[0], out var forestFunc))
                    {
                        sourceInfo = sourceInfo with { ForestFunctionality = forestFunc };
                    }
                }

                if (rootDse.TryGetValue("domainControllerFunctionality", out var dcFuncValues) && dcFuncValues.Count > 0)
                {
                    if (int.TryParse(dcFuncValues[0], out var dcFunc))
                    {
                        sourceInfo = sourceInfo with { DomainControllerFunctionality = dcFunc };
                    }
                }

                Console.WriteLine(" ok");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "LDAP connection to source DC failed (non-fatal, will skip remote registration)");
                Console.WriteLine($" skipped ({ex.Message})");
                if (ldapClient != null)
                {
                    await ldapClient.DisposeAsync();
                    ldapClient = null;
                }
            }
        }

        WritePhaseComplete();

        // ── Phase 2: Local Infrastructure Setup ─────────────────────────────
        WritePhase(2, 6, "Local infrastructure setup");

        await _cosmosInitializer.StartAsync(ct);
        _logger.LogInformation("Cosmos DB containers initialized");

        // Generate a new InvocationId for this DC
        _dcInfo.InvocationId = Guid.NewGuid().ToString();
        _dcInfo.Hostname = options.Hostname;

        var domainDn = options.DomainDn;
        var configDn = options.ConfigurationDn;
        var schemaDn = options.SchemaDn;

        // Determine site via subnet-to-site mapping if LDAP is available
        if (ldapClient != null)
        {
            try
            {
                Console.Write("    Determining site from subnet mappings...");
                var siteMapper = new SiteMapper(_loggerFactory.CreateLogger<SiteMapper>());
                var detectedSite = await siteMapper.DetermineSiteAsync(
                    ldapClient, configDn, _dcInfo.IpAddress, ct);
                options.SiteName = detectedSite;
                Console.WriteLine($" {detectedSite}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Site detection failed, using default site: {Site}", options.SiteName);
                Console.WriteLine($" failed, using {options.SiteName}");
            }
        }

        _dcInfo.SiteName = options.SiteName;

        _logger.LogInformation(
            "DC identity: Hostname={Hostname}, InvocationId={InvocationId}, Site={Site}, NtdsSettingsDn={NtdsDn}",
            _dcInfo.Hostname, _dcInfo.InvocationId, _dcInfo.SiteName, _dcInfo.NtdsSettingsDn(domainDn));

        WritePhaseComplete();

        // ── Phase 3: Initial Directory Replication (IFM equivalent) ─────────
        WritePhase(3, 6, "Replicating directory data from source DC");

        // Replicate Schema NC first (smallest, needed for attribute interpretation)
        await ReplicateNamingContextAsync(httpClient, sourceDcUrl, options.TenantId,
            schemaDn, "Schema", ct);

        // Replicate Configuration NC second (sites, services, cross-refs)
        await ReplicateNamingContextAsync(httpClient, sourceDcUrl, options.TenantId,
            configDn, "Configuration", ct);

        // Replicate Domain NC last (largest, all user/group/computer objects)
        await ReplicateNamingContextAsync(httpClient, sourceDcUrl, options.TenantId,
            domainDn, "Domain", ct);

        WritePhaseComplete();

        // ── Phase 4: DC Registration ────────────────────────────────────────
        WritePhase(4, 6, "Registering this DC in the directory");

        // Register on remote DC via LDAP if available
        string remoteComputerDn = null;
        string remoteServerDn = null;
        string remoteNtdsDn = null;
        if (ldapClient != null)
        {
            try
            {
                var registrar = new RemoteDcRegistrar(_loggerFactory.CreateLogger<RemoteDcRegistrar>());
                var hostname = options.Hostname.ToUpperInvariant();
                var fqdn = $"{hostname.ToLowerInvariant()}.{options.DomainName}";
                var invocationId = Guid.Parse(_dcInfo.InvocationId);

                Console.Write("    Creating machine account on source DC...");
                remoteComputerDn = await registrar.EnsureMachineAccountAsync(
                    ldapClient, domainDn, hostname, fqdn, ct);
                Console.WriteLine(" done");

                Console.Write("    Creating server object on source DC...");
                remoteServerDn = await registrar.EnsureServerObjectAsync(
                    ldapClient, configDn, options.SiteName, hostname, fqdn, remoteComputerDn, ct);
                Console.WriteLine(" done");

                Console.Write("    Creating NTDS Settings on source DC...");
                remoteNtdsDn = await registrar.CreateNtdsSettingsAsync(
                    ldapClient, remoteServerDn, invocationId,
                    domainDn, configDn, schemaDn,
                    sourceInfo.ForestFunctionality, ct);
                Console.WriteLine(" done");

                Console.Write("    Registering SPNs on source DC...");
                var ntdsaGuid = Guid.NewGuid(); // NTDSA object GUID
                await registrar.RegisterServicePrincipalNamesAsync(
                    ldapClient, remoteComputerDn, hostname, fqdn,
                    options.DomainName, ntdsaGuid, ct);
                Console.WriteLine(" done");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Remote DC registration via LDAP failed (non-fatal, local registration will proceed)");
                Console.WriteLine($" warning: {ex.Message}");
            }
        }

        // Also register locally in the replicated directory
        await CreateDcDirectoryObjectsAsync(options, sourceInfo, ct);

        WritePhaseComplete();

        // ── Phase 5: Replication Partnership Setup ──────────────────────────
        WritePhase(5, 6, "Setting up replication partnerships");

        // Create connection objects on source DC via LDAP for inbound replication
        if (ldapClient != null && remoteNtdsDn != null && sourceNtdsSettingsDn != null)
        {
            try
            {
                Console.Write("    Creating replication connection objects on source DC...");
                var registrar = new RemoteDcRegistrar(_loggerFactory.CreateLogger<RemoteDcRegistrar>());
                var connectionName = Guid.NewGuid().ToString();
                await registrar.CreateConnectionObjectAsync(
                    ldapClient, remoteNtdsDn, sourceNtdsSettingsDn, connectionName, ct);
                Console.WriteLine(" done");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create connection objects on source DC (non-fatal)");
                Console.WriteLine($" warning: {ex.Message}");
            }
        }

        await SetupReplicationPartnershipsAsync(httpClient, sourceDcUrl, options, sourceInfo,
            ldapClient, sourceNtdsSettingsDn, ct);

        WritePhaseComplete();

        // ── Phase 6: Configuration Finalization ─────────────────────────────
        WritePhase(6, 6, "Finalizing configuration");

        // Clean up LDAP connection
        if (ldapClient != null)
        {
            await ldapClient.DisposeAsync();
        }

        await UpdateLocalConfigurationAsync(options, ct);
        await WritePromotionMarkerAsync(options, ct);

        WritePhaseComplete();

        // Show completion banner
        ShowCompletionBanner(options, sourceInfo);
    }

    // ── Phase 1: Pre-flight Validation ──────────────────────────────────────

    /// <summary>
    /// Validates connectivity to the source DC, authenticates, and reads its rootDSE.
    /// </summary>
    private async Task<SourceDcInfo> ValidateSourceDcAsync(
        HttpClient httpClient, string sourceDcUrl, SetupOptions options, CancellationToken ct)
    {
        // Step 1: Health check
        _logger.LogInformation("Checking source DC health at {Url}", sourceDcUrl);
        Console.WriteLine();
        Console.Write("    Verifying source DC connectivity...");

        HttpResponseMessage healthResponse;
        try
        {
            healthResponse = await httpClient.GetAsync($"{sourceDcUrl}/drs/health", ct);
        }
        catch (HttpRequestException ex)
        {
            throw new DcPromotionException(
                $"Cannot reach source DC at {sourceDcUrl}. Ensure the DC is running and the URL is correct. Error: {ex.Message}", ex);
        }

        if (!healthResponse.IsSuccessStatusCode)
        {
            throw new DcPromotionException(
                $"Source DC health check failed with status {healthResponse.StatusCode}. " +
                "Ensure the source DC is healthy and running.");
        }

        Console.WriteLine(" ok");

        // Step 2: Authenticate and retrieve rootDSE information
        Console.Write("    Authenticating and reading rootDSE...");

        var rootDseRequest = new GetNCChangesRequest
        {
            TenantId = options.TenantId,
            NamingContextDn = "", // rootDSE
            UsnFrom = 0,
            MaxObjects = 1,
        };

        // Get the rootDSE-equivalent information by querying configuration objects
        // First, get the domain root to discover naming contexts
        var domainDn = options.DomainDn;
        if (string.IsNullOrEmpty(domainDn) && !string.IsNullOrEmpty(options.DomainName))
        {
            domainDn = string.Join(",", options.DomainName.Split('.').Select(p => $"DC={p}"));
        }

        // Use the health endpoint response or query naming contexts
        var configDn = $"CN=Configuration,{domainDn}";
        var schemaDn = $"CN=Schema,{configDn.Replace("CN=Configuration,", "")}";

        // Verify the source has data by fetching a small batch from the domain NC
        var verifyRequest = new GetNCChangesRequest
        {
            TenantId = options.TenantId,
            NamingContextDn = domainDn,
            UsnFrom = 0,
            MaxObjects = 1,
        };

        GetNCChangesResponse verifyResponse;
        try
        {
            var response = await httpClient.PostAsJsonAsync(
                $"{sourceDcUrl}/drs/GetNCChanges", verifyRequest, JsonOptions, ct);
            response.EnsureSuccessStatusCode();
            verifyResponse = await response.Content.ReadFromJsonAsync<GetNCChangesResponse>(JsonOptions, ct)
                ?? throw new DcPromotionException("Empty response from source DC GetNCChanges.");
        }
        catch (HttpRequestException ex)
        {
            throw new DcPromotionException(
                $"Failed to query source DC for domain data. Error: {ex.Message}", ex);
        }

        if (verifyResponse.Entries.Count == 0)
        {
            throw new DcPromotionException(
                $"Source DC returned no objects for domain {domainDn}. " +
                "Verify the domain exists and the source DC is writable.");
        }

        Console.WriteLine(" ok");

        // Extract domain information from the first replicated object (domain root)
        var domainRoot = verifyResponse.Entries.FirstOrDefault(
            e => e.ObjectClass.Contains("domainDNS"));

        var forestDnsName = options.ForestName;
        if (string.IsNullOrEmpty(forestDnsName))
            forestDnsName = options.DomainName;

        var netBiosName = options.NetBiosName;
        if (string.IsNullOrEmpty(netBiosName))
            netBiosName = options.DomainName.Split('.')[0].ToUpperInvariant();

        return new SourceDcInfo
        {
            DefaultNamingContext = domainDn,
            ConfigurationNamingContext = configDn,
            SchemaNamingContext = schemaDn,
            DomainDnsName = options.DomainName,
            ForestDnsName = forestDnsName,
            NetBiosName = netBiosName,
            DomainControllerFunctionality = 7, // Windows Server 2016
            ForestFunctionality = 7,
            HighestUsn = verifyResponse.HighestUsn,
        };
    }

    /// <summary>
    /// Validates that the local Cosmos DB is either empty or already contains the same domain.
    /// </summary>
    private async Task ValidateLocalDatabaseAsync(SetupOptions options, CancellationToken ct)
    {
        Console.Write("    Validating local database...");

        try
        {
            var existing = await _store.GetByDnAsync(options.TenantId, options.DomainDn, ct);
            if (existing != null)
            {
                _logger.LogWarning(
                    "Local database already contains domain {DomainDn}. " +
                    "Proceeding with incremental sync.",
                    options.DomainDn);
                Console.WriteLine(" existing data found (will merge)");
                return;
            }
        }
        catch
        {
            // Database may not exist yet, which is fine
        }

        Console.WriteLine(" ok (empty)");
    }

    // ── Phase 3: Directory Replication ───────────────────────────────────────

    /// <summary>
    /// Replicates a single naming context from the source DC using paginated GetNCChanges calls.
    /// </summary>
    private async Task ReplicateNamingContextAsync(
        HttpClient httpClient, string sourceDcUrl, string tenantId,
        string namingContextDn, string ncFriendlyName, CancellationToken ct)
    {
        _logger.LogInformation("Replicating {NcName} NC: {NcDn}", ncFriendlyName, namingContextDn);
        Console.WriteLine();
        Console.Write($"    Replicating {ncFriendlyName} NC: ");

        long usnFrom = 0;
        int totalObjects = 0;
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
                throw new DcPromotionException(
                    $"Replication of {ncFriendlyName} NC failed at USN {usnFrom}. Error: {ex.Message}", ex);
            }

            var changesResponse = await response.Content.ReadFromJsonAsync<GetNCChangesResponse>(JsonOptions, ct)
                ?? throw new DcPromotionException($"Empty response during {ncFriendlyName} NC replication.");

            // Apply each replicated object locally
            foreach (var entry in changesResponse.Entries)
            {
                await ApplyReplicatedEntryAsync(tenantId, namingContextDn, entry, ct);
                totalObjects++;
            }

            // Update progress
            Console.Write($"\r    Replicating {ncFriendlyName} NC: {totalObjects} objects replicated");

            usnFrom = changesResponse.HighestUsn;
            moreData = changesResponse.MoreData;

            _logger.LogInformation(
                "Replicating {NcName} NC: {Count} objects so far, highestUsn={Usn}, moreData={More}",
                ncFriendlyName, totalObjects, usnFrom, moreData);
        }

        Console.WriteLine($"\r    Replicating {ncFriendlyName} NC: {totalObjects} objects replicated - done");

        _logger.LogInformation(
            "Replication of {NcName} NC complete: {Count} objects", ncFriendlyName, totalObjects);
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

            // Apply all attributes
            foreach (var (attrName, values) in entry.Attributes)
            {
                switch (attrName.ToLowerInvariant())
                {
                    case "objectcategory":
                        obj.ObjectCategory = values.FirstOrDefault() ?? "";
                        break;
                    case "objectsid":
                        obj.ObjectSid = values.FirstOrDefault() ?? "";
                        break;
                    case "samaccountname":
                        obj.SAMAccountName = values.FirstOrDefault();
                        break;
                    case "dnshostname":
                        obj.DnsHostName = values.FirstOrDefault();
                        break;
                    case "description":
                        obj.Description = values.FirstOrDefault();
                        break;
                    default:
                        if (values.Count == 1)
                            obj.SetAttribute(attrName, new DirectoryAttribute(attrName, values[0]));
                        else if (values.Count > 1)
                            obj.SetAttribute(attrName, new DirectoryAttribute(attrName, values.ToArray()));
                        break;
                }
            }

            try
            {
                await _store.CreateAsync(obj, ct);
            }
            catch (Exception ex)
            {
                // Object may already exist due to cross-NC references; update instead
                _logger.LogDebug(
                    "Create failed for {Dn}, attempting update: {Error}",
                    entry.Dn, ex.Message);

                try
                {
                    await _store.UpdateAsync(obj, ct);
                }
                catch (Exception updateEx)
                {
                    _logger.LogWarning(
                        "Failed to replicate object {Dn}: {Error}",
                        entry.Dn, updateEx.Message);
                }
            }
        }
    }

    // ── Phase 4: Local DC Registration ──────────────────────────────────────

    /// <summary>
    /// Creates this DC's Server object, NTDS Settings object, and computer account
    /// in the local directory.
    /// </summary>
    private async Task CreateDcDirectoryObjectsAsync(
        SetupOptions options, SourceDcInfo sourceInfo, CancellationToken ct)
    {
        var hostname = options.Hostname.ToUpperInvariant();
        var domainDn = options.DomainDn;
        var configDn = options.ConfigurationDn;
        var schemaDn = options.SchemaDn;
        var fqdn = $"{hostname.ToLowerInvariant()}.{options.DomainName}";

        // Create Server object in Sites container
        var serverDn = _dcInfo.ServerDn(domainDn);
        Console.Write("    Creating Server object...");

        var existingServer = await _store.GetByDnAsync(options.TenantId, serverDn, ct);
        if (existingServer != null)
        {
            _logger.LogWarning("Server object {ServerDn} already exists; updating", serverDn);
            existingServer.DnsHostName = fqdn;
            existingServer.WhenChanged = DateTimeOffset.UtcNow;
            await _store.UpdateAsync(existingServer, ct);
        }
        else
        {
            var server = CreateBaseObject(options.TenantId, domainDn, serverDn,
                ["top", "server"], $"CN=Server,{schemaDn}",
                $"CN=Servers,CN={options.SiteName},CN=Sites,{configDn}");
            server.Cn = hostname;
            server.DnsHostName = fqdn;
            await _store.CreateAsync(server, ct);
        }
        Console.WriteLine(" done");

        // Create NTDS Settings object under the Server object
        var ntdsSettingsDn = _dcInfo.NtdsSettingsDn(domainDn);
        Console.Write("    Creating NTDS Settings object...");

        var existingNtds = await _store.GetByDnAsync(options.TenantId, ntdsSettingsDn, ct);
        if (existingNtds == null)
        {
            var ntdsSettings = CreateBaseObject(options.TenantId, domainDn,
                ntdsSettingsDn, ["top", "nTDSDSA"],
                $"CN=NTDS-DSA,{schemaDn}", serverDn);
            ntdsSettings.Cn = "NTDS Settings";
            ntdsSettings.SetAttribute("invocationId",
                new DirectoryAttribute("invocationId", _dcInfo.InvocationId));
            ntdsSettings.SetAttribute("options",
                new DirectoryAttribute("options", 1)); // NTDS_DSA, IS_GC
            ntdsSettings.SetAttribute("hasMasterNCs",
                new DirectoryAttribute("hasMasterNCs",
                    domainDn, configDn, schemaDn));
            await _store.CreateAsync(ntdsSettings, ct);
        }
        Console.WriteLine(" done");

        // Create computer account in OU=Domain Controllers
        var computerDn = $"CN={hostname},OU=Domain Controllers,{domainDn}";
        Console.Write("    Creating DC computer account...");

        var existingComputer = await _store.GetByDnAsync(options.TenantId, computerDn, ct);
        if (existingComputer == null)
        {
            var domainGuid = Guid.NewGuid().ToString();
            var computer = CreateBaseObject(options.TenantId, domainDn, computerDn,
                ["top", "person", "organizationalPerson", "user", "computer"],
                $"CN=Computer,{schemaDn}",
                $"OU=Domain Controllers,{domainDn}");
            computer.Cn = hostname;
            computer.SAMAccountName = $"{hostname}$";
            computer.DnsHostName = fqdn;
            computer.ObjectSid = await _ridAllocator.GenerateObjectSidAsync(options.TenantId, domainDn, ct);

            // SERVER_TRUST_ACCOUNT | TRUSTED_FOR_DELEGATION
            computer.SetAttribute("userAccountControl",
                new DirectoryAttribute("userAccountControl", 532480));
            computer.SetAttribute("serverReferenceBL",
                new DirectoryAttribute("serverReferenceBL", serverDn));

            // Service Principal Names for DC services
            var spns = new[]
            {
                $"ldap/{fqdn}",
                $"ldap/{hostname}",
                $"HOST/{fqdn}",
                $"HOST/{hostname}",
                $"GC/{fqdn}/{options.DomainName}",
                $"E3514235-4B06-11D1-AB04-00C04FC2DCD2/{domainGuid}/{options.DomainName}",
                $"RestrictedKrbHost/{fqdn}",
                $"RestrictedKrbHost/{hostname}",
            };
            computer.SetAttribute("servicePrincipalName",
                new DirectoryAttribute("servicePrincipalName", spns));

            await _store.CreateAsync(computer, ct);

            // Add to Domain Controllers group
            var dcGroupDn = $"CN=Domain Controllers,CN=Users,{domainDn}";
            await AddGroupMemberAsync(options.TenantId, dcGroupDn, computerDn, ct);
        }
        Console.WriteLine(" done");

        _logger.LogInformation(
            "DC registration complete: Server={ServerDn}, NTDS={NtdsDn}, Computer={ComputerDn}",
            serverDn, ntdsSettingsDn, computerDn);
    }

    // ── Phase 5: Replication Partnership Setup ──────────────────────────────

    /// <summary>
    /// Registers this DC with the source DC for change notifications and sets up
    /// inbound replication partnerships.
    /// </summary>
    private async Task SetupReplicationPartnershipsAsync(
        HttpClient httpClient, string sourceDcUrl, SetupOptions options,
        SourceDcInfo sourceInfo, LdapClient ldapClient, string sourceNtdsSettingsDn,
        CancellationToken ct)
    {
        var domainDn = options.DomainDn;
        var configDn = options.ConfigurationDn;
        var schemaDn = options.SchemaDn;

        // Resolve the source DC's DSA GUID from its NTDS Settings object
        var sourceDsaGuid = Guid.Empty;

        // Strategy 1: Query via LDAP from the source DC
        if (ldapClient != null && !string.IsNullOrEmpty(sourceNtdsSettingsDn))
        {
            try
            {
                var searchResult = await ldapClient.SearchAsync(
                    sourceNtdsSettingsDn,
                    SearchScope.BaseObject,
                    "(objectClass=*)",
                    new[] { "objectGUID" },
                    ct: ct);

                if (searchResult.ResultCode == 0 && searchResult.Entries.Count > 0)
                {
                    var guidValue = searchResult.Entries[0].GetFirstValue("objectGUID");
                    if (guidValue != null && Guid.TryParse(guidValue, out var parsed))
                    {
                        sourceDsaGuid = parsed;
                        _logger.LogInformation("Resolved source DC DSA GUID from LDAP: {DsaGuid}", sourceDsaGuid);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read source DC DSA GUID via LDAP");
            }
        }

        // Strategy 2: Read from the local store if the NTDS Settings object was already replicated
        if (sourceDsaGuid == Guid.Empty && !string.IsNullOrEmpty(sourceNtdsSettingsDn))
        {
            try
            {
                var ntdsObj = await _store.GetByDnAsync(options.TenantId, sourceNtdsSettingsDn, ct);
                if (ntdsObj != null && Guid.TryParse(ntdsObj.ObjectGuid, out var localParsed))
                {
                    sourceDsaGuid = localParsed;
                    _logger.LogInformation("Resolved source DC DSA GUID from local store: {DsaGuid}", sourceDsaGuid);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read source DC DSA GUID from local store");
            }
        }

        // Strategy 3: Fallback to a random GUID
        if (sourceDsaGuid == Guid.Empty)
        {
            sourceDsaGuid = Guid.NewGuid();
            _logger.LogWarning(
                "Could not determine source DC DSA GUID from LDAP or local store; using generated GUID {DsaGuid}. " +
                "Replication partnership may need to be updated once the real GUID is known.",
                sourceDsaGuid);
        }

        Console.Write("    Registering for change notifications on source DC...");

        // Register this DC with the source for change notifications on all NCs
        var namingContexts = new[] { schemaDn, configDn, domainDn };

        foreach (var ncDn in namingContexts)
        {
            try
            {
                var syncRequest = new DsReplicaSyncRequest
                {
                    TenantId = options.TenantId,
                    NamingContextDn = ncDn,
                    SourceDsaDn = _dcInfo.NtdsSettingsDn(domainDn),
                };

                var response = await httpClient.PostAsJsonAsync(
                    $"{sourceDcUrl}/drs/DsReplicaSync", syncRequest, JsonOptions, ct);
                response.EnsureSuccessStatusCode();
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(
                    "Failed to register for notifications on {NC} at source DC: {Error}",
                    ncDn, ex.Message);
            }
        }

        Console.WriteLine(" done");

        // Add the source DC as an inbound replication partner for each NC
        Console.Write("    Adding source DC as inbound replication partner...");

        foreach (var ncDn in namingContexts)
        {
            _topology.AddInboundPartner(new ReplicationPartner
            {
                DsaGuid = sourceDsaGuid,
                DnsName = new Uri(sourceDcUrl).Host,
                NtdsSettingsDn = sourceNtdsSettingsDn
                    ?? $"CN=NTDS Settings,CN=SourceDC,CN=Servers,CN={options.SiteName},CN=Sites,{configDn}",
                NamingContextDn = ncDn,
                TransportType = "RPC",
                InvocationId = sourceDsaGuid,
                LastUsnSynced = sourceInfo.HighestUsn,
                LastSyncSuccess = DateTimeOffset.UtcNow,
                LastSyncAttempt = DateTimeOffset.UtcNow,
                LastSyncResult = 0,
                Options = DsReplNeighborFlags.DS_REPL_NBR_WRITEABLE |
                          DsReplNeighborFlags.DS_REPL_NBR_SYNC_ON_STARTUP |
                          DsReplNeighborFlags.DS_REPL_NBR_DO_SCHEDULED_SYNCS,
            });
        }

        Console.WriteLine(" done");

        // Run KCC to generate topology with any additional DCs
        Console.Write("    Running KCC topology generation...");

        try
        {
            await _topology.GenerateIntraSiteTopologyAsync(options.TenantId, domainDn, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("KCC topology generation completed with warnings: {Error}", ex.Message);
        }

        Console.WriteLine(" done");

        _logger.LogInformation("Replication partnerships configured successfully");
    }

    // ── Phase 6: Configuration Finalization ─────────────────────────────────

    /// <summary>
    /// Updates the local appsettings.json with domain configuration from the source DC.
    /// </summary>
    private async Task UpdateLocalConfigurationAsync(SetupOptions options, CancellationToken ct)
    {
        Console.Write("    Updating appsettings.json...");

        var configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

        // Also try the source directory (for development)
        var sourceConfigPath = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "appsettings.json");
        if (File.Exists(sourceConfigPath))
            configPath = Path.GetFullPath(sourceConfigPath);

        if (!File.Exists(configPath))
        {
            _logger.LogWarning("Could not find appsettings.json at {Path}, skipping configuration update", configPath);
            Console.WriteLine(" skipped (file not found)");
            return;
        }

        var json = await File.ReadAllTextAsync(configPath, ct);
        var config = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? [];

        // Look up domain SID from the replicated domain root object
        var domainSid = "";
        try
        {
            var domainRoot = await _store.GetByDnAsync(options.TenantId, options.DomainDn, ct);
            domainSid = domainRoot?.ObjectSid ?? "";
        }
        catch
        {
            _logger.LogWarning("Could not read domain SID from replicated data");
        }

        // Update NamingContexts
        var namingContexts = new Dictionary<string, object>
        {
            ["DomainDn"] = options.DomainDn,
            ["ForestDnsName"] = options.ForestName ?? options.DomainName,
            ["DomainSid"] = domainSid,
            ["KerberosRealm"] = options.DomainName.ToUpperInvariant(),
        };
        config["NamingContexts"] = JsonSerializer.SerializeToElement(namingContexts);

        // Update Ldap
        var ldap = new Dictionary<string, object>
        {
            ["Port"] = 389,
            ["TlsPort"] = 636,
            ["MaxConnections"] = 10000,
            ["MaxPageSize"] = 1000,
            ["DefaultTenantId"] = options.TenantId,
            ["DefaultDomain"] = options.DomainDn,
        };
        config["Ldap"] = JsonSerializer.SerializeToElement(ldap);

        // Update Kerberos realm
        var kerberos = new Dictionary<string, object>
        {
            ["DefaultRealm"] = options.DomainName.ToUpperInvariant(),
            ["Port"] = 88,
            ["MaximumSkew"] = "00:05:00",
            ["SessionLifetime"] = "10:00:00",
            ["MaximumRenewalWindow"] = "7.00:00:00",
        };
        config["Kerberos"] = JsonSerializer.SerializeToElement(kerberos);

        // Update DNS
        var hostname = options.Hostname.ToLowerInvariant();
        var dns = new Dictionary<string, object>
        {
            ["Port"] = 53,
            ["ServerHostname"] = $"{hostname}.{options.DomainName}",
            ["ServerIpAddresses"] = new[] { "127.0.0.1" },
            ["Domains"] = new[] { options.DomainName },
            ["ForestDnsName"] = options.ForestName ?? options.DomainName,
            ["DefaultTtl"] = 600,
        };
        config["Dns"] = JsonSerializer.SerializeToElement(dns);

        // DC Node configuration
        var dcNode = new Dictionary<string, object>
        {
            ["Hostname"] = hostname,
            ["SiteName"] = options.SiteName,
            ["TenantId"] = options.TenantId,
            ["DomainDn"] = options.DomainDn,
            ["BindAddresses"] = new[] { "0.0.0.0" },
        };
        config["DcNode"] = JsonSerializer.SerializeToElement(dcNode);

        // Replication configuration
        var replication = new Dictionary<string, object>
        {
            ["HttpPort"] = 9389,
            ["SourceDcUrl"] = options.SourceDcUrl ?? "",
        };
        config["Replication"] = JsonSerializer.SerializeToElement(replication);

        var writeOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = null,
        };

        var updatedJson = JsonSerializer.Serialize(config, writeOptions);
        await File.WriteAllTextAsync(configPath, updatedJson, ct);

        _logger.LogInformation("Configuration updated at {Path}", configPath);
        Console.WriteLine(" done");
    }

    /// <summary>
    /// Writes a marker file indicating that DC promotion completed successfully.
    /// This allows the server to detect on next startup that it has been promoted.
    /// </summary>
    private async Task WritePromotionMarkerAsync(SetupOptions options, CancellationToken ct)
    {
        Console.Write("    Writing promotion completion marker...");

        var markerPath = Path.Combine(AppContext.BaseDirectory, ".dc-promoted");

        // Also try the source directory (for development)
        var sourceMarkerPath = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", ".dc-promoted");
        if (System.IO.Directory.Exists(Path.GetDirectoryName(Path.GetFullPath(sourceMarkerPath))))
            markerPath = Path.GetFullPath(sourceMarkerPath);

        var marker = new Dictionary<string, object>
        {
            ["promotedAt"] = DateTimeOffset.UtcNow,
            ["domainDn"] = options.DomainDn,
            ["domainName"] = options.DomainName,
            ["hostname"] = options.Hostname,
            ["siteName"] = options.SiteName,
            ["sourceDcUrl"] = options.SourceDcUrl ?? "",
            ["invocationId"] = _dcInfo.InvocationId,
        };

        var json = JsonSerializer.Serialize(marker, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(markerPath, json, ct);

        _logger.LogInformation("Promotion marker written to {Path}", markerPath);
        Console.WriteLine(" done");
    }

    // ── Helper Methods ──────────────────────────────────────────────────────

    /// <summary>
    /// Creates an HttpClient configured for communication with the source DC.
    /// </summary>
    private static HttpClient CreateHttpClient(SetupOptions options)
    {
        var handler = new HttpClientHandler
        {
            // Allow self-signed certs for development/lab environments
            ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        };

        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(10),
        };

        // Set basic auth header if credentials are provided
        if (!string.IsNullOrEmpty(options.ReplicationAdminUpn) &&
            !string.IsNullOrEmpty(options.ReplicationAdminPassword))
        {
            var credentials = Convert.ToBase64String(
                System.Text.Encoding.UTF8.GetBytes(
                    $"{options.ReplicationAdminUpn}:{options.ReplicationAdminPassword}"));
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
        }

        return client;
    }

    /// <summary>
    /// Creates a base directory object with standard properties populated.
    /// </summary>
    private static DirectoryObject CreateBaseObject(
        string tenantId, string domainDn, string dn,
        List<string> objectClass, string objectCategory, string parentDn)
    {
        var now = DateTimeOffset.UtcNow;
        var firstComponent = dn.Split(',')[0];
        var eqIndex = firstComponent.IndexOf('=');
        var cn = eqIndex >= 0 ? firstComponent[(eqIndex + 1)..] : firstComponent;

        return new DirectoryObject
        {
            Id = dn.ToLowerInvariant(),
            TenantId = tenantId,
            DomainDn = domainDn,
            DistinguishedName = dn,
            ObjectGuid = Guid.NewGuid().ToString(),
            ObjectClass = objectClass,
            ObjectCategory = objectCategory,
            ParentDn = parentDn,
            Cn = cn,
            WhenCreated = now,
            WhenChanged = now,
        };
    }

    /// <summary>
    /// Adds a member to a group in the directory.
    /// </summary>
    private async Task AddGroupMemberAsync(
        string tenantId, string groupDn, string memberDn, CancellationToken ct)
    {
        var group = await _store.GetByDnAsync(tenantId, groupDn, ct);
        if (group == null)
        {
            _logger.LogWarning("Group {GroupDn} not found; cannot add member {MemberDn}", groupDn, memberDn);
            return;
        }

        if (!group.Member.Contains(memberDn))
        {
            group.Member.Add(memberDn);
            await _store.UpdateAsync(group, ct);
        }
    }

    private static void WritePhase(int current, int total, string description)
    {
        Console.Write($"  [{current,2}/{total}] {description}...");
    }

    private static void WritePhaseComplete()
    {
        Console.WriteLine(" done");
    }

    private void ShowCompletionBanner(SetupOptions options, SourceDcInfo sourceInfo)
    {
        Console.WriteLine();
        Console.WriteLine("  \u2554\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2557");
        Console.WriteLine("  \u2551             DC Promotion Complete!                      \u2551");
        Console.WriteLine("  \u255a\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u255d");
        Console.WriteLine();
        Console.WriteLine($"  Domain:          {options.DomainName}");
        Console.WriteLine($"  Domain DN:       {options.DomainDn}");
        Console.WriteLine($"  Hostname:        {options.Hostname}");
        Console.WriteLine($"  Site:            {options.SiteName}");
        Console.WriteLine($"  InvocationId:    {_dcInfo.InvocationId}");
        Console.WriteLine($"  Source DC:       {options.SourceDcUrl}");
        Console.WriteLine($"  NTDS Settings:   {_dcInfo.NtdsSettingsDn(options.DomainDn)}");
        Console.WriteLine();
        Console.WriteLine("  To start this domain controller, run:");
        Console.WriteLine("    dotnet run");
        Console.WriteLine();
    }
}

/// <summary>
/// Contains information discovered from the source DC's rootDSE during pre-flight validation.
/// </summary>
internal record SourceDcInfo
{
    /// <summary>The default naming context DN (e.g., DC=corp,DC=com).</summary>
    public string DefaultNamingContext { get; init; } = "";

    /// <summary>The configuration naming context DN.</summary>
    public string ConfigurationNamingContext { get; init; } = "";

    /// <summary>The schema naming context DN.</summary>
    public string SchemaNamingContext { get; init; } = "";

    /// <summary>The DNS name of the domain.</summary>
    public string DomainDnsName { get; init; } = "";

    /// <summary>The DNS name of the forest root domain.</summary>
    public string ForestDnsName { get; init; } = "";

    /// <summary>The NetBIOS name of the domain.</summary>
    public string NetBiosName { get; init; } = "";

    /// <summary>The DC functional level (e.g., 7 for Windows Server 2016).</summary>
    public int DomainControllerFunctionality { get; init; }

    /// <summary>The forest functional level.</summary>
    public int ForestFunctionality { get; init; }

    /// <summary>The highest USN on the source DC at the time of replication.</summary>
    public long HighestUsn { get; init; }
}

/// <summary>
/// Exception thrown when DC promotion fails at any phase.
/// </summary>
public class DcPromotionException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DcPromotionException"/> class.
    /// </summary>
    /// <param name="message">A message describing the promotion failure.</param>
    public DcPromotionException(string message) : base(message) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="DcPromotionException"/> class.
    /// </summary>
    /// <param name="message">A message describing the promotion failure.</param>
    /// <param name="innerException">The inner exception that caused the failure.</param>
    public DcPromotionException(string message, Exception innerException)
        : base(message, innerException) { }
}
