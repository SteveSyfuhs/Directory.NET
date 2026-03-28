using Directory.Core.Caching;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Core.Telemetry;
using Directory.CosmosDb;
using Directory.CosmosDb.Configuration;
using Directory.Dns;
using Directory.Kerberos;
using Directory.Kerberos.S4U;
using Directory.Ldap.Auditing;
using Directory.Ldap.Handlers;
using Directory.Ldap.Server;
using Directory.Replication;
using Directory.Rpc;
using Directory.Rpc.Dispatch;
using Directory.Rpc.Lsa;
using Directory.Rpc.Nrpc;
using Directory.Rpc.Samr;
using Directory.Schema;
using Directory.Replication.Drsr;
using Directory.Security;
using Directory.Security.Apds;
using Directory.Security.CertificateAuthority;
using Directory.Security.Claims;
using Directory.Server;
using Directory.Server.Diagnostics;
using Directory.Server.Setup;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Caching.Memory;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;

// Check for diagnostics mode
if (args.Length > 0 && args[0].Equals("diag", StringComparison.OrdinalIgnoreCase))
{
    return await DiagnosticRunner.RunAsync(args);
}

// Check for setup mode
if (args.Length > 0 && args[0].Equals("setup", StringComparison.OrdinalIgnoreCase))
{
    var setupOptions = SetupWizard.Run(args.Skip(1).ToArray());

    // Build a minimal service provider for setup
    var setupBuilder = Host.CreateApplicationBuilder();

    // Configure Cosmos DB — use overrides from setup options or fall back to appsettings
    if (!string.IsNullOrEmpty(setupOptions.CosmosConnectionString))
    {
        setupBuilder.Configuration["CosmosDb:ConnectionString"] = setupOptions.CosmosConnectionString;
    }
    if (!string.IsNullOrEmpty(setupOptions.CosmosDatabaseName))
    {
        setupBuilder.Configuration["CosmosDb:DatabaseName"] = setupOptions.CosmosDatabaseName;
    }

    // Configure NamingContexts for the new domain
    setupBuilder.Configuration["NamingContexts:DomainDn"] = setupOptions.DomainDn;
    setupBuilder.Configuration["NamingContexts:ForestDnsName"] = setupOptions.DomainName;

    setupBuilder.Services.Configure<CosmosDbOptions>(setupBuilder.Configuration.GetSection(CosmosDbOptions.SectionName));
    setupBuilder.Services.Configure<NamingContextOptions>(setupBuilder.Configuration.GetSection(NamingContextOptions.SectionName));

    // Core services
    setupBuilder.Services.AddSingleton<ISchemaService, SchemaService>();
    setupBuilder.Services.AddSingleton<SchemaModificationService>();
    setupBuilder.Services.AddSingleton<IPasswordPolicy, PasswordService>();
    setupBuilder.Services.AddSingleton<IRidAllocator, RidAllocator>();
    setupBuilder.Services.AddSingleton<INamingContextService, NamingContextService>();

    // Cosmos DB
    setupBuilder.Services.AddSingleton<CosmosClient>(sp =>
    {
        var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<CosmosDbOptions>>().Value;
        return new CosmosClient(options.ConnectionString, new CosmosClientOptions
        {
            SerializerOptions = new CosmosSerializationOptions
            {
                PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase,
            },
            ConnectionMode = ConnectionMode.Direct,
            MaxRetryAttemptsOnRateLimitedRequests = 9,
            MaxRetryWaitTimeOnRateLimitedRequests = TimeSpan.FromSeconds(30),
        });
    });
    setupBuilder.Services.AddSingleton<Func<CosmosClient>>(sp => () => sp.GetRequiredService<CosmosClient>());
    setupBuilder.Services.AddSingleton<IDirectoryStore, CosmosDirectoryStore>();
    setupBuilder.Services.AddSingleton<CosmosDbInitializer>();
    setupBuilder.Services.AddSingleton<DnsZoneStore>();
    setupBuilder.Services.AddSingleton<DomainProvisioner>();

    // DC promotion services (needed for replica mode with source DC replication)
    setupBuilder.Services.AddSingleton<DcInstanceInfo>();
    setupBuilder.Services.AddSingleton<ReplicationTopology>();
    setupBuilder.Services.AddSingleton<DcPromotionService>();

    var setupHost = setupBuilder.Build();

    if (setupOptions.IsReplica && !string.IsNullOrEmpty(setupOptions.SourceDcUrl))
    {
        // ── DC Promotion: replicate from source DC and join domain ──
        Console.WriteLine();
        Console.WriteLine("  Starting DC promotion (replicating from source DC)...");
        Console.WriteLine();

        try
        {
            var promotionService = setupHost.Services.GetRequiredService<DcPromotionService>();
            await promotionService.PromoteAsync(setupOptions);
        }
        catch (DcPromotionException ex)
        {
            Console.WriteLine();
            Console.WriteLine($"  DC promotion failed: {ex.Message}");
            Console.WriteLine();
            if (ex.InnerException != null)
                Console.WriteLine($"  Details: {ex.InnerException.Message}");
            Environment.Exit(1);
        }
    }
    else
    {
        // ── Standard provisioning: create new domain or local-only replica ──
        Console.WriteLine();
        Console.WriteLine("  Initializing database...");
        var initializer = setupHost.Services.GetRequiredService<CosmosDbInitializer>();
        await initializer.StartAsync(CancellationToken.None);
        Console.WriteLine("  Database ready.");
        Console.WriteLine();

        var provisioner = setupHost.Services.GetRequiredService<DomainProvisioner>();
        if (setupOptions.IsReplica)
        {
            await provisioner.ProvisionReplicaAsync(setupOptions);
        }
        else
        {
            await provisioner.ProvisionAsync(setupOptions);
        }
    }

    return 0; // Exit after setup
}

var builder = Host.CreateApplicationBuilder(args);

if (OperatingSystem.IsWindows())
{
    builder.Logging.AddEventLog(new Microsoft.Extensions.Logging.EventLog.EventLogSettings
    {
        SourceName = "DirectoryNET-Server",
        LogName = "DirectoryNET"
    });
}

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "DirectoryServer";
});

// Add Cosmos DB as a configuration source (loads after appsettings.json, so it takes precedence)
var cosmosConn = builder.Configuration["CosmosDb:ConnectionString"];
var cosmosDb = builder.Configuration["CosmosDb:DatabaseName"] ?? "DirectoryService";
var hostname = builder.Configuration["DcNode:Hostname"] ?? Environment.MachineName;
if (!string.IsNullOrEmpty(cosmosConn))
{
    builder.Configuration.AddCosmosConfiguration(cosmosConn, cosmosDb, "default", hostname);
}

// Configuration sections
builder.Services.Configure<CosmosDbOptions>(builder.Configuration.GetSection(CosmosDbOptions.SectionName));
builder.Services.Configure<LdapServerOptions>(builder.Configuration.GetSection(LdapServerOptions.SectionName));
builder.Services.Configure<KerberosOptions>(builder.Configuration.GetSection(KerberosOptions.SectionName));
builder.Services.Configure<DnsOptions>(builder.Configuration.GetSection(DnsOptions.SectionName));
builder.Services.Configure<ReplicationOptions>(builder.Configuration.GetSection(ReplicationOptions.SectionName));
builder.Services.Configure<NamingContextOptions>(builder.Configuration.GetSection(NamingContextOptions.SectionName));
builder.Services.Configure<DcNodeOptions>(builder.Configuration.GetSection(DcNodeOptions.SectionName));

// Core services
builder.Services.AddSingleton<ISchemaService, SchemaService>();
builder.Services.AddSingleton<SchemaModificationService>();
builder.Services.AddSingleton<IPasswordPolicy, PasswordService>();
builder.Services.AddSingleton<NtlmAuthenticator>();

// Security & ACL services
builder.Services.AddSingleton<IUserAccountControlService, UserAccountControlService>();
builder.Services.AddSingleton<IAccessControlService, AccessControlService>();
builder.Services.AddSingleton<GroupMembershipMaterializer>();
builder.Services.AddSingleton<ILinkedAttributeService, LinkedAttributeService>();
builder.Services.AddSingleton<IConstructedAttributeService, ConstructedAttributeService>();
builder.Services.AddSingleton<IRidAllocator, RidAllocator>();
builder.Services.AddSingleton<INamingContextService, NamingContextService>();

// MS-APDS: Authentication Protocol Domain Support
builder.Services.AddSingleton<AccountRestrictions>();
builder.Services.AddSingleton<ApdsLogonProcessor>();
builder.Services.AddSingleton<NtlmPassThrough>();
builder.Services.AddSingleton<PacValidation>();
builder.Services.AddSingleton<DigestValidation>();

// MS-CTA: Claims Transformation Algorithm
builder.Services.AddSingleton<IClaimTypeStore, ClaimTypeStore>();
builder.Services.AddSingleton<IClaimsProvider, ClaimsProvider>();
builder.Services.AddSingleton<ClaimTransformationEngine>();
builder.Services.AddSingleton<ITrustClaimsPolicy, TrustClaimsPolicy>();
builder.Services.AddSingleton<DynamicAccessControlEvaluator>();
builder.Services.AddSingleton<ClaimsSerialization>();

// MS-ADTS 3.1.1.5.2: Fine-Grained Password Policy (PSO)
builder.Services.AddSingleton<FineGrainedPasswordPolicyService>();

// Pre-flight port availability check (runs before all protocol servers)
builder.Services.AddHostedService<PreflightCheckService>();

// Certificate Authority services
builder.Services.AddSingleton<CertificateStore>();
builder.Services.AddSingleton<CertificateTemplateService>();
builder.Services.AddSingleton<CertificateAuthorityService>();
builder.Services.AddHostedService<CertificateAutoEnrollmentService>();

// DC instance identity
builder.Services.AddSingleton<DcInstanceInfo>(sp =>
{
    var nodeOptions = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<DcNodeOptions>>().Value;
    return new DcInstanceInfo
    {
        Hostname = nodeOptions.Hostname,
        SiteName = nodeOptions.SiteName,
    };
});

// Domain configuration (populated from directory at startup)
builder.Services.AddSingleton<DomainConfiguration>();

// Cosmos DB
builder.Services.AddSingleton<CosmosClient>(sp =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<CosmosDbOptions>>().Value;
    return new CosmosClient(options.ConnectionString, new CosmosClientOptions
    {
        SerializerOptions = new CosmosSerializationOptions
        {
            PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase,
        },
        ConnectionMode = ConnectionMode.Direct,
        MaxRetryAttemptsOnRateLimitedRequests = 9,
        MaxRetryWaitTimeOnRateLimitedRequests = TimeSpan.FromSeconds(30),
    });
});
builder.Services.AddSingleton<Func<CosmosClient>>(sp => () => sp.GetRequiredService<CosmosClient>());

// Caching layer
builder.Services.AddMemoryCache();
builder.Services.Configure<CacheOptions>(builder.Configuration.GetSection(CacheOptions.SectionName));

var cacheOptions = builder.Configuration.GetSection(CacheOptions.SectionName).Get<CacheOptions>() ?? new CacheOptions();
if (!string.IsNullOrEmpty(cacheOptions.RedisConnectionString))
{
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = cacheOptions.RedisConnectionString;
        options.InstanceName = cacheOptions.RedisInstanceName;
    });
}
else
{
    builder.Services.AddDistributedMemoryCache(); // In-memory fallback
}

builder.Services.AddSingleton<DistributedCacheService>();
builder.Services.AddSingleton<CacheInvalidationBus>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<CacheInvalidationBus>());
builder.Services.AddSingleton<GroupExpansionCache>();
builder.Services.AddSingleton<CosmosDirectoryStore>();
builder.Services.AddSingleton<IDirectoryStore>(sp =>
{
    var inner = sp.GetRequiredService<CosmosDirectoryStore>();
    var distributedCache = sp.GetRequiredService<DistributedCacheService>();
    var invalidationBus = sp.GetRequiredService<CacheInvalidationBus>();
    var optionsMonitor = sp.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<CacheOptions>>();
    var logger = sp.GetRequiredService<ILogger<CachedDirectoryStore>>();
    return new CachedDirectoryStore(inner, distributedCache, invalidationBus, optionsMonitor, logger);
});
builder.Services.AddHostedService<CosmosDbInitializer>();

// Configuration management (Cosmos DB-backed)
builder.Services.AddSingleton<CosmosConfigurationStore>();
builder.Services.AddHostedService<ConfigurationMigrator>();
builder.Services.AddHostedService<ConfigurationChangeFeedService>();

// DC registration and domain config (must start after CosmosDbInitializer)
builder.Services.AddHostedService<DomainConfigurationService>();
builder.Services.AddHostedService<DcRegistrationService>();

// LDAP operation handlers
builder.Services.AddSingleton<RootDseProvider>();
builder.Services.AddSingleton<SaslHandler>();
builder.Services.AddSingleton<LdapControlHandler>();
builder.Services.AddSingleton<PasswordModifyHandler>();
builder.Services.AddSingleton<GroupPolicyHandler>();
builder.Services.AddSingleton<ILdapOperationHandler, BindHandler>();
builder.Services.AddSingleton<ILdapOperationHandler, SearchHandler>();
builder.Services.AddSingleton<ILdapOperationHandler, AddHandler>();
builder.Services.AddSingleton<ILdapOperationHandler, ModifyHandler>();
builder.Services.AddSingleton<ILdapOperationHandler, DeleteHandler>();
builder.Services.AddSingleton<ILdapOperationHandler, ModifyDNHandler>();
builder.Services.AddSingleton<ILdapOperationHandler, CompareHandler>();
builder.Services.AddSingleton<ILdapOperationHandler, AbandonHandler>();
builder.Services.AddSingleton<ILdapOperationHandler, ExtendedHandler>();
builder.Services.AddSingleton<ILdapOperationHandler, UnbindHandler>();
// LDAP protocol audit service (in-memory ring buffer for protocol-level auditing)
builder.Services.AddSingleton<LdapAuditService>();
builder.Services.AddSingleton<LdapOperationDispatcher>();
builder.Services.AddSingleton<ILdapOperationDispatcher>(sp =>
{
    var inner = sp.GetRequiredService<LdapOperationDispatcher>();
    var auditService = sp.GetRequiredService<LdapAuditService>();
    var logger = sp.GetRequiredService<ILogger<AuditingOperationDispatcher>>();
    return new AuditingOperationDispatcher(inner, auditService, logger);
});

// LDAP connection stats
builder.Services.AddSingleton<LdapConnectionStats>();

// Protocol servers
builder.Services.AddHostedService<LdapServer>();
builder.Services.AddHostedService<GlobalCatalogServer>();
builder.Services.AddHostedService<CldapServer>();
builder.Services.AddHostedService<KerberosHostService>();
builder.Services.AddHostedService<KpasswdService>();
builder.Services.AddHostedService<DnsServer>();
// RPC server (DCE/RPC on ports 135 + 49664)
builder.Services.Configure<RpcServerOptions>(builder.Configuration.GetSection(RpcServerOptions.SectionName));
builder.Services.AddSingleton<SamrOperations>();
builder.Services.AddSingleton<LsaOperations>();
builder.Services.AddSingleton<LsaSecretOperations>();
builder.Services.AddSingleton<LsaTrustedDomainOperations>();
builder.Services.AddSingleton<LsaAccountRightsOperations>();
builder.Services.AddSingleton<NrpcOperations>();
builder.Services.AddSingleton<IRpcInterfaceHandler, SamrInterfaceHandler>();
builder.Services.AddSingleton<IRpcInterfaceHandler, LsaInterfaceHandler>();
builder.Services.AddSingleton<IRpcInterfaceHandler, NrpcInterfaceHandler>();

// RODC (Read-Only Domain Controller) service
builder.Services.AddSingleton<RodcService>();

// DRSR (Directory Replication Service) RPC interface — MS-DRSR
builder.Services.AddSingleton<ReplicationEngine>();
builder.Services.AddSingleton<ReplicationTopology>();
builder.Services.AddSingleton<Directory.Rpc.Drsr.DsCrackNamesService>();
builder.Services.AddSingleton<DrsNameResolution>();
builder.Services.AddSingleton<FsmoRoleManager>();
builder.Services.AddSingleton<SchemaPrefixTable>();
builder.Services.AddSingleton<ConflictResolver>();
builder.Services.AddSingleton<LinkedValueReplication>();
builder.Services.AddSingleton<DrsInterfaceHandler>();
builder.Services.AddSingleton<IRpcInterfaceHandler>(sp => sp.GetRequiredService<DrsInterfaceHandler>());

builder.Services.AddRpcTransport();

// Kerberos services
builder.Services.AddSingleton<PacGenerator>();
builder.Services.AddSingleton<SpnService>();
builder.Services.AddSingleton<S4UService>();
builder.Services.AddSingleton<TrustedRealmService>();

// S4U constrained delegation handlers (MS-SFU)
builder.Services.AddSingleton<S4USelfHandler>();
builder.Services.AddSingleton<S4UProxyHandler>();
builder.Services.AddSingleton<RbcdHandler>();
builder.Services.AddSingleton<S4UDelegationProcessor>();

// DNS services
builder.Services.AddSingleton<DnsZoneStore>();
builder.Services.AddSingleton<CachedDnsZoneStore>();
builder.Services.AddSingleton<DnsDynamicUpdateHandler>();
builder.Services.AddSingleton<DnsSiteService>();

// Replication
builder.Services.AddSingleton<DrsProtocol>();
builder.Services.Configure<ReplicationSchedulerOptions>(builder.Configuration.GetSection(ReplicationSchedulerOptions.SectionName));
builder.Services.AddHostedService<ChangeFeedProcessorService>();
builder.Services.AddSingleton<DrsHttpService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DrsHttpService>());
builder.Services.AddSingleton<ReplicationScheduler>();
builder.Services.AddHostedService(sp =>
{
    var scheduler = sp.GetRequiredService<ReplicationScheduler>();
    // Wire up the scheduler reference in DrsHttpService for notify endpoint
    sp.GetRequiredService<DrsHttpService>().SetReplicationScheduler(scheduler);
    return scheduler;
});
builder.Services.AddHostedService<ChangeNotificationDispatcher>();

// OpenTelemetry tracing and metrics
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("Directory.Server"))
    .WithTracing(tracing =>
    {
        tracing
            .AddHttpClientInstrumentation()
            .AddSource(DirectoryMetrics.ServiceName)
            .AddOtlpExporter();

        if (builder.Environment.IsDevelopment())
        {
            tracing.AddConsoleExporter();
        }
    })
    .WithMetrics(metrics =>
    {
        metrics
            .AddHttpClientInstrumentation()
            .AddMeter(DirectoryMetrics.ServiceName)
            .AddOtlpExporter();

        if (builder.Environment.IsDevelopment())
        {
            metrics.AddConsoleExporter();
        }
    });

var host = builder.Build();

// Log startup banner
var logger = host.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("=== Modern Active Directory Service ===");
logger.LogInformation("Starting LDAP (389/636), CLDAP (389/UDP), GC (3268/3269), Kerberos (88), kpasswd (464), DNS (53), RPC (135/49664), DRS (9389)...");

host.Run();
return 0;
