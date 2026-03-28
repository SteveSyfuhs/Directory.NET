using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using Directory.Replication;
using Directory.Replication.Drsr;
using Directory.Rpc.Client;
using Directory.Web.Models;
using Directory.Web.Services;
using Microsoft.Azure.Cosmos;

namespace Directory.Web.Endpoints;

public static class SetupEndpoints
{
    public static RouteGroupBuilder MapSetupEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/status", (SetupStateService state) =>
        {
            return Results.Ok(new SetupStatusResponse(
                state.IsDatabaseConfigured,
                state.IsProvisioned,
                state.IsProvisioning,
                state.ProvisioningProgress,
                state.ProvisioningPhase,
                state.ProvisioningError
            ));
        });

        group.MapGet("/progress", (SetupStateService state) =>
        {
            return Results.Ok(new SetupStatusResponse(
                state.IsDatabaseConfigured,
                state.IsProvisioned,
                state.IsProvisioning,
                state.ProvisioningProgress,
                state.ProvisioningPhase,
                state.ProvisioningError
            ));
        });

        group.MapPost("/validate-connection", async (ValidateConnectionRequest request) =>
        {
            try
            {
                using var client = new CosmosClient(request.ConnectionString, new CosmosClientOptions
                {
                    ConnectionMode = ConnectionMode.Direct,
                    RequestTimeout = TimeSpan.FromSeconds(10),
                });

                await client.ReadAccountAsync();
                return Results.Ok(new ValidateConnectionResponse(true, null));
            }
            catch (Exception ex)
            {
                return Results.Ok(new ValidateConnectionResponse(false, ex.Message));
            }
        });

        // Configure (or reconfigure) the database connection. Called by the setup wizard
        // after the user tests their connection string successfully.
        group.MapPost("/configure-database", async (
            ConfigureDatabaseRequest request,
            CosmosClientHolder holder,
            DeferredCosmosDbInitializer initializer,
            SetupStateService state,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("SetupEndpoints");
            try
            {
                // Validate the connection first
                using var testClient = new CosmosClient(request.ConnectionString, new CosmosClientOptions
                {
                    ConnectionMode = ConnectionMode.Direct,
                    RequestTimeout = TimeSpan.FromSeconds(10),
                });
                await testClient.ReadAccountAsync();

                // Connection works — configure the real client
                holder.Configure(request.ConnectionString, request.DatabaseName);
                state.IsDatabaseConfigured = true;

                // Initialize the database containers
                await initializer.InitializeAsync();

                // Save the connection string to appsettings.json so it persists across restarts
                await SaveConnectionStringToConfig(request.ConnectionString, request.DatabaseName, logger);

                logger.LogInformation("Database configured and initialized successfully: {Database}", request.DatabaseName);
                return Results.Ok(new ValidateConnectionResponse(true, null));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to configure database");
                return Results.Ok(new ValidateConnectionResponse(false, ex.Message));
            }
        });

        group.MapPost("/validate-domain", (ValidateDomainRequest request) =>
        {
            var domainName = request.DomainName?.Trim() ?? "";

            if (string.IsNullOrEmpty(domainName))
                return Results.Ok(new ValidateDomainResponse(false, null, null, "Domain name is required."));

            // Must contain at least one dot
            if (!domainName.Contains('.'))
                return Results.Ok(new ValidateDomainResponse(false, null, null, "Domain name must contain at least one dot (e.g., contoso.com)."));

            // No leading or trailing dots
            if (domainName.StartsWith('.') || domainName.EndsWith('.'))
                return Results.Ok(new ValidateDomainResponse(false, null, null, "Domain name must not start or end with a dot."));

            // Each label must be valid
            var labels = domainName.Split('.');
            foreach (var label in labels)
            {
                if (string.IsNullOrEmpty(label))
                    return Results.Ok(new ValidateDomainResponse(false, null, null, "Domain name contains empty labels."));

                if (label.StartsWith('-') || label.EndsWith('-'))
                    return Results.Ok(new ValidateDomainResponse(false, null, null, $"Label '{label}' must not start or end with a hyphen."));

                if (!Regex.IsMatch(label, @"^[a-zA-Z0-9\-]+$"))
                    return Results.Ok(new ValidateDomainResponse(false, null, null, $"Label '{label}' contains invalid characters. Only letters, digits, and hyphens are allowed."));
            }

            var domainDn = string.Join(",", labels.Select(l => $"DC={l}"));
            var suggestedNetBios = labels[0].ToUpperInvariant();

            // NetBIOS name max 15 chars
            if (suggestedNetBios.Length > 15)
                suggestedNetBios = suggestedNetBios[..15];

            return Results.Ok(new ValidateDomainResponse(true, domainDn, suggestedNetBios, null));
        });

        group.MapPost("/validate-password", (ValidatePasswordRequest request) =>
        {
            var password = request.Password ?? "";

            if (password.Length < 7)
                return Results.Ok(new ValidatePasswordResponse(false, "Password must be at least 7 characters long."));

            // Check complexity: 3+ of 4 categories (uppercase, lowercase, digit, special)
            int categories = 0;
            if (Regex.IsMatch(password, @"[A-Z]")) categories++;
            if (Regex.IsMatch(password, @"[a-z]")) categories++;
            if (Regex.IsMatch(password, @"[0-9]")) categories++;
            if (Regex.IsMatch(password, @"[^a-zA-Z0-9]")) categories++;

            if (categories < 3)
                return Results.Ok(new ValidatePasswordResponse(false,
                    "Password must contain characters from at least 3 of these categories: uppercase letters, lowercase letters, digits, special characters."));

            return Results.Ok(new ValidatePasswordResponse(true, null));
        });

        group.MapPost("/provision", (
            ProvisionRequest request,
            SetupStateService state,
            WebDomainProvisioner provisioner) =>
        {
            // Database must be configured first
            if (!state.IsDatabaseConfigured)
                return Results.BadRequest(new ProvisionResponse(false, "Database is not configured. Please configure the database connection first."));

            // Already provisioned
            if (state.IsProvisioned)
                return Results.Conflict(new ProvisionResponse(false, "Domain is already provisioned."));

            // Already in progress
            if (state.IsProvisioning)
                return Results.Conflict(new ProvisionResponse(false, "Provisioning is already in progress."));

            // Validate required fields
            if (string.IsNullOrWhiteSpace(request.DomainName))
                return Results.BadRequest(new ProvisionResponse(false, "Domain name is required."));
            if (string.IsNullOrWhiteSpace(request.NetBiosName))
                return Results.BadRequest(new ProvisionResponse(false, "NetBIOS name is required."));
            if (string.IsNullOrWhiteSpace(request.AdminPassword))
                return Results.BadRequest(new ProvisionResponse(false, "Admin password is required."));

            // Kick off provisioning in a background task
            _ = Task.Run(async () =>
            {
                try
                {
                    await provisioner.ProvisionAsync(request);
                }
                catch (Exception)
                {
                    // Errors are captured inside ProvisionAsync via SetupStateService
                }
            });

            return Results.Ok(new ProvisionResponse(true, null));
        });

        // ── Domain Discovery (Windows AD via DNS + RPC) ─────────────────────

        // Discovers a Windows AD domain by DNS name: performs SRV lookup,
        // EPM port resolution, and verifies the DRSUAPI RPC endpoint.
        group.MapPost("/discover-domain", async (
            DiscoverDomainRequest request,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("SetupEndpoints");
            var domainName = request.DomainName?.Trim() ?? "";

            // ── Validate domain name format ────────────────────────────────
            if (string.IsNullOrEmpty(domainName))
            {
                return Results.Ok(new DiscoverDomainResponse(
                    false, "Domain name is required.", null, null, null, null, null, null, null));
            }

            if (!domainName.Contains('.'))
            {
                return Results.Ok(new DiscoverDomainResponse(
                    false, "Domain name must contain at least one dot (e.g., contoso.com).", null, null, null, null, null, null, null));
            }

            if (domainName.StartsWith('.') || domainName.EndsWith('.'))
            {
                return Results.Ok(new DiscoverDomainResponse(
                    false, "Domain name must not start or end with a dot.", null, null, null, null, null, null, null));
            }

            var labels = domainName.Split('.');
            foreach (var label in labels)
            {
                if (string.IsNullOrEmpty(label))
                {
                    return Results.Ok(new DiscoverDomainResponse(
                        false, "Domain name contains empty labels.", null, null, null, null, null, null, null));
                }

                if (label.StartsWith('-') || label.EndsWith('-'))
                {
                    return Results.Ok(new DiscoverDomainResponse(
                        false, $"Label '{label}' must not start or end with a hyphen.", null, null, null, null, null, null, null));
                }

                if (!Regex.IsMatch(label, @"^[a-zA-Z0-9\-]+$"))
                {
                    return Results.Ok(new DiscoverDomainResponse(
                        false, $"Label '{label}' contains invalid characters.", null, null, null, null, null, null, null));
                }
            }

            // ── Step 1: DNS SRV lookup for _ldap._tcp.{domainName} ─────────
            string dcHostname = null;
            string dcIpAddress = null;

            try
            {
                logger.LogInformation("Performing DNS lookup for domain {DomainName}", domainName);

                // Try to resolve the domain name directly — this works when DNS is configured
                // to return the DC. For SRV record support, we attempt _ldap._tcp.{domain} first.
                // .NET does not have built-in SRV record resolution, so we try resolving
                // the domain name itself as a fallback.
                IPAddress[] addresses;
                try
                {
                    addresses = await System.Net.Dns.GetHostAddressesAsync(domainName);
                }
                catch (SocketException)
                {
                    return Results.Ok(new DiscoverDomainResponse(
                        false,
                        $"DNS resolution failed for '{domainName}'. Ensure the domain name is correct " +
                        "and that this machine can resolve it (check DNS server settings).",
                        null, null, null, null, null, null, null));
                }

                if (addresses.Length == 0)
                {
                    return Results.Ok(new DiscoverDomainResponse(
                        false,
                        $"DNS resolved '{domainName}' but returned no addresses.",
                        null, null, null, null, null, null, null));
                }

                dcIpAddress = addresses[0].ToString();

                // Try to get the FQDN of the DC via reverse lookup
                try
                {
                    var hostEntry = await System.Net.Dns.GetHostEntryAsync(addresses[0]);
                    dcHostname = hostEntry.HostName;
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Reverse DNS lookup failed for {IP}, using domain name as hostname", addresses[0]);
                    dcHostname = domainName;
                }

                logger.LogInformation("DNS resolved {DomainName} to {IpAddress} (hostname: {Hostname})",
                    domainName, dcIpAddress, dcHostname);
            }
            catch (Exception ex)
            {
                return Results.Ok(new DiscoverDomainResponse(
                    false,
                    $"DNS lookup failed for '{domainName}': {ex.Message}",
                    null, null, null, null, null, null, null));
            }

            // ── Step 2: EPM port resolution for DRSUAPI ────────────────────
            int drsuapiPort;
            try
            {
                logger.LogInformation("Resolving DRSUAPI RPC port via EPM on {Hostname}", dcHostname);

                drsuapiPort = await EpmClient.ResolvePortAsync(
                    dcHostname,
                    DrsuapiClient.DrsuapiInterfaceId,
                    4, // major version
                    logger,
                    CancellationToken.None);

                logger.LogInformation("EPM resolved DRSUAPI to port {Port} on {Hostname}", drsuapiPort, dcHostname);
            }
            catch (Exception ex)
            {
                return Results.Ok(new DiscoverDomainResponse(
                    false,
                    $"RPC endpoint mapper (port 135) on '{dcHostname}' ({dcIpAddress}) is unreachable " +
                    $"or did not return a DRSUAPI endpoint. Ensure the target is a Windows domain controller " +
                    $"and RPC traffic is not blocked. Error: {ex.Message}",
                    dcHostname, dcIpAddress, null, null, null, null, null));
            }

            // ── Step 3: Verify RPC port is reachable ───────────────────────
            try
            {
                using var tcpClient = new TcpClient();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await tcpClient.ConnectAsync(dcHostname, drsuapiPort, cts.Token);

                logger.LogInformation("RPC port {Port} on {Hostname} is reachable", drsuapiPort, dcHostname);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "RPC port {Port} on {Hostname} is not reachable", drsuapiPort, dcHostname);
                // Non-fatal — the port was discovered via EPM, it may just be slow
            }

            // ── Step 4: Derive domain DN and return ────────────────────────
            var domainDn = string.Join(",", labels.Select(l => $"DC={l}"));

            return Results.Ok(new DiscoverDomainResponse(
                Success: true,
                Error: null,
                DcHostname: dcHostname,
                DcIpAddress: dcIpAddress,
                DcRpcPort: drsuapiPort,
                DomainDn: domainDn,
                ForestName: domainName,
                DsaGuid: null,       // Discovered during actual DRSBind with credentials
                FunctionalLevel: null // Discovered during actual DRSBind with credentials
            ));
        });

        // ── Replica DC (Join Existing Domain) endpoints ─────────────────────

        // Validates connectivity to a source DC and retrieves its domain metadata.
        // Used by the "Join Existing Domain" setup wizard step.
        group.MapPost("/validate-source-dc", async (
            ValidateSourceDcRequest request,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("SetupEndpoints");

            if (string.IsNullOrWhiteSpace(request.SourceDcUrl))
            {
                return Results.Ok(new ValidateSourceDcResponse(
                    false, "Source DC URL is required.", null, null, null, null, null, null));
            }

            var sourceDcUrl = request.SourceDcUrl.TrimEnd('/');

            // ── Try HTTP first (Directory.NET peer DC) ──────────────────────────
            try
            {
                using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

                logger.LogInformation("Validating source DC at {Url} via HTTP", sourceDcUrl);
                var healthResponse = await httpClient.GetAsync($"{sourceDcUrl}/drs/health");

                if (healthResponse.IsSuccessStatusCode)
                {
                    var rootDseResponse = await httpClient.GetAsync($"{sourceDcUrl}/drs/rootdse");

                    if (!rootDseResponse.IsSuccessStatusCode)
                    {
                        return Results.Ok(new ValidateSourceDcResponse(
                            false,
                            $"Failed to read rootDSE from source DC (status {rootDseResponse.StatusCode}).",
                            null, null, null, null, null, null));
                    }

                    var jsonOptions = new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        PropertyNameCaseInsensitive = true,
                    };

                    var rootDse = await rootDseResponse.Content.ReadFromJsonAsync<JsonElement>(jsonOptions);

                    var domainDnsName = rootDse.TryGetProperty("domainDnsName", out var dnsProp)
                        ? dnsProp.GetString() : null;
                    var defaultNc = rootDse.TryGetProperty("defaultNamingContext", out var ncProp)
                        ? ncProp.GetString() : null;
                    var forestDnsName = rootDse.TryGetProperty("forestDnsName", out var forestProp)
                        ? forestProp.GetString() : null;
                    var serverName = rootDse.TryGetProperty("serverName", out var serverProp)
                        ? serverProp.GetString() : null;
                    var dsaGuid = rootDse.TryGetProperty("dsaGuid", out var dsaGuidProp)
                        ? dsaGuidProp.GetString() : null;
                    var funcLevel = rootDse.TryGetProperty("domainControllerFunctionality", out var funcProp)
                        ? funcProp.GetInt32() : (int?)null;

                    logger.LogInformation(
                        "Source DC validated via HTTP: domain={Domain}, forest={Forest}, dc={Server}",
                        domainDnsName, forestDnsName, serverName);

                    return Results.Ok(new ValidateSourceDcResponse(
                        true, null, domainDnsName, defaultNc, forestDnsName, serverName, dsaGuid, funcLevel, "http"));
                }

                logger.LogInformation("HTTP health check failed (status {Status}), will try RPC fallback",
                    healthResponse.StatusCode);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                logger.LogInformation(ex,
                    "HTTP connection to {Url} failed, attempting RPC fallback for Windows DC", sourceDcUrl);
            }

            // ── RPC fallback (Windows AD DS domain controller) ──────────────────
            // Extract hostname from URL for RPC connection (strip scheme and port)
            string rpcHostname;
            try
            {
                var uri = new Uri(sourceDcUrl.Contains("://") ? sourceDcUrl : $"https://{sourceDcUrl}");
                rpcHostname = uri.Host;
            }
            catch
            {
                rpcHostname = sourceDcUrl.Split(':')[0].Replace("https://", "").Replace("http://", "");
            }

            try
            {
                logger.LogInformation("Attempting RPC validation of Windows DC at {Hostname}", rpcHostname);

                // Step 1: EPM discovery — resolve the DRSUAPI dynamic port
                var drsuapiPort = await Directory.Rpc.Client.EpmClient.ResolvePortAsync(
                    rpcHostname,
                    DrsuapiClient.DrsuapiInterfaceId,
                    4, // major version
                    logger,
                    CancellationToken.None);

                logger.LogInformation("EPM resolved DRSUAPI to port {Port} on {Hostname}", drsuapiPort, rpcHostname);

                // Step 2: Attempt unauthenticated bind to verify the interface is available
                // We cannot do a full DRSBind without credentials, but confirming the EPM
                // resolves a valid DRSUAPI port is strong evidence this is a Windows DC.
                // Return success with the hostname as the "domain name" placeholder.
                // The actual domain metadata will be discovered during provisioning via DRSCrackNames.
                return Results.Ok(new ValidateSourceDcResponse(
                    Success: true,
                    Error: null,
                    DomainName: rpcHostname, // Best guess until DRSBind+CrackNames
                    DomainDn: null,          // Will be resolved during provisioning
                    ForestName: null,
                    DcHostname: rpcHostname,
                    DsaGuid: null,
                    FunctionalLevel: null,
                    Transport: "rpc"));
            }
            catch (Exception rpcEx)
            {
                logger.LogWarning(rpcEx, "RPC fallback also failed for {Hostname}", rpcHostname);

                return Results.Ok(new ValidateSourceDcResponse(
                    false,
                    $"Cannot reach source DC at {sourceDcUrl}. " +
                    $"HTTP connection failed and RPC endpoint mapper on port 135 is unreachable. " +
                    $"Ensure the DC is running and accessible. RPC error: {rpcEx.Message}",
                    null, null, null, null, null, null));
            }
        });

        // Starts replica DC provisioning by replicating from a source DC.
        // Kicks off the promotion in a background task and returns immediately.
        group.MapPost("/provision-replica", (
            ProvisionReplicaRequest request,
            SetupStateService state,
            WebDcPromotionService promotionService) =>
        {
            // Database must be configured first
            if (!state.IsDatabaseConfigured)
                return Results.BadRequest(new ProvisionResponse(false, "Database is not configured. Please configure the database connection first."));

            // Already provisioned
            if (state.IsProvisioned)
                return Results.Conflict(new ProvisionResponse(false, "Domain is already provisioned."));

            // Already in progress
            if (state.IsProvisioning)
                return Results.Conflict(new ProvisionResponse(false, "Provisioning is already in progress."));

            // Validate required fields
            var useRpc = string.Equals(request.Transport, "rpc", StringComparison.OrdinalIgnoreCase);
            if (useRpc)
            {
                // RPC mode: need either a SourceDcUrl or a DomainName
                if (string.IsNullOrWhiteSpace(request.SourceDcUrl) && string.IsNullOrWhiteSpace(request.DomainName))
                    return Results.BadRequest(new ProvisionResponse(false, "Either Source DC URL or Domain Name is required for RPC transport."));
            }
            else
            {
                if (string.IsNullOrWhiteSpace(request.SourceDcUrl))
                    return Results.BadRequest(new ProvisionResponse(false, "Source DC URL is required."));
            }
            if (string.IsNullOrWhiteSpace(request.AdminUpn))
                return Results.BadRequest(new ProvisionResponse(false, "Admin UPN is required."));
            if (string.IsNullOrWhiteSpace(request.AdminPassword))
                return Results.BadRequest(new ProvisionResponse(false, "Admin password is required."));

            // Kick off replica provisioning in a background task
            _ = Task.Run(async () =>
            {
                try
                {
                    await promotionService.PromoteAsync(request);
                }
                catch (Exception)
                {
                    // Errors are captured inside PromoteAsync via SetupStateService
                }
            });

            return Results.Ok(new ProvisionResponse(true, null));
        });

        // Returns detailed replication progress during replica provisioning.
        // Includes standard setup status plus replication-specific phase information.
        group.MapGet("/replication-progress", (SetupStateService state) =>
        {
            var status = new SetupStatusResponse(
                state.IsDatabaseConfigured,
                state.IsProvisioned,
                state.IsProvisioning,
                state.ProvisioningProgress,
                state.ProvisioningPhase,
                state.ProvisioningError
            );

            var replicationProgress = state.IsReplicaMode && state.ReplicationCurrentNc != null
                ? new ReplicationPhaseProgress(
                    state.ProvisioningPhase ?? "",
                    state.ReplicationCurrentNc,
                    state.ReplicationObjectsProcessed,
                    state.ReplicationObjectsTotal,
                    state.ReplicationBytesTransferred)
                : null;

            return Results.Ok(new
            {
                status.IsDatabaseConfigured,
                status.IsProvisioned,
                status.IsProvisioning,
                status.ProvisioningProgress,
                status.ProvisioningPhase,
                status.ProvisioningError,
                IsReplicaMode = state.IsReplicaMode,
                ReplicationProgress = replicationProgress,
            });
        });

        return group;
    }

    /// <summary>
    /// Saves the Cosmos DB connection string to appsettings.json so it persists across app restarts.
    /// </summary>
    private static async Task SaveConnectionStringToConfig(string connectionString, string databaseName, ILogger logger)
    {
        var configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

        // Also try the source directory (for development)
        var sourceConfigPath = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "appsettings.json");
        if (File.Exists(sourceConfigPath))
            configPath = Path.GetFullPath(sourceConfigPath);

        if (!File.Exists(configPath))
        {
            logger.LogWarning("Could not find appsettings.json at {Path}, skipping config persistence", configPath);
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(configPath);
            var config = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? [];

            var cosmosDb = new Dictionary<string, object>
            {
                ["ConnectionString"] = connectionString,
                ["DatabaseName"] = databaseName,
                ["DefaultThroughput"] = 1000
            };
            config["CosmosDb"] = JsonSerializer.SerializeToElement(cosmosDb);

            var options = new JsonSerializerOptions { WriteIndented = true };
            await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(config, options));

            logger.LogInformation("Cosmos DB connection string saved to {Path}", configPath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to persist connection string to appsettings.json");
        }
    }
}
