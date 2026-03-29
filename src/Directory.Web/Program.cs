using System.Threading.RateLimiting;
using Directory.Core;
using Directory.Core.Caching;
using Directory.Core.Interfaces;
using Directory.CosmosDb;
using Directory.CosmosDb.Configuration;
using Directory.Ldap.Auditing;
using Directory.Ldap.Handlers;
using Directory.Schema;
using Directory.Security;
using Directory.Security.Apds;
using Directory.Security.CertificateAuthority;
using Directory.Security.Claims;
using Directory.Security.Delegation;
using Directory.Security.Fido2;
using Directory.Security.Mfa;
using Directory.Security.Pam;
using Directory.Security.Radius;
using Directory.Security.SshKeys;
using Directory.Security.OAuth;
using Directory.Security.Saml;
using Directory.Security.PasswordFilters;
using Directory.Security.SmartCard;
using Directory.Replication;
using Directory.Rpc.Drsr;
using Directory.Web.Endpoints;
using Directory.Web.Middleware;
using Directory.Web.Models;
using Directory.Ldap.Proxy;
using Directory.Ldap.Server;
using Directory.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Caching.Memory;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Directory.Core.Telemetry;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

if (OperatingSystem.IsWindows())
{
    builder.Logging.AddEventLog(new Microsoft.Extensions.Logging.EventLog.EventLogSettings
    {
        SourceName = "DirectoryNET-Web",
        LogName = "DirectoryNET"
    });
}

builder.Host.UseWindowsService(options =>
{
    options.ServiceName = "DirectoryWeb";
});

// Configuration sections
builder.Services.Configure<CosmosDbOptions>(builder.Configuration.GetSection(CosmosDbOptions.SectionName));
builder.Services.Configure<NamingContextOptions>(builder.Configuration.GetSection(NamingContextOptions.SectionName));
builder.Services.Configure<Directory.Web.Models.DcNodeOptions>(builder.Configuration.GetSection(Directory.Web.Models.DcNodeOptions.SectionName));

// Core services (same registrations as Directory.Server)
builder.Services.AddSingleton<ISchemaService, SchemaService>();
builder.Services.AddSingleton<SchemaModificationService>();
builder.Services.AddSingleton<IPasswordPolicy, PasswordService>();
builder.Services.AddSingleton<IUserAccountControlService, UserAccountControlService>();
builder.Services.AddSingleton<IAccessControlService, AccessControlService>();
builder.Services.AddSingleton<GroupMembershipMaterializer>();
builder.Services.AddSingleton<ILinkedAttributeService, LinkedAttributeService>();
builder.Services.AddSingleton<IConstructedAttributeService, ConstructedAttributeService>();
builder.Services.AddSingleton<AttributeFormatter>();
builder.Services.AddSingleton<IRidAllocator, RidAllocator>();
builder.Services.AddSingleton<INamingContextService, NamingContextService>();

// MS-DRSR 4.1.4.2: DsCrackNames — name format translation
builder.Services.AddSingleton<DsCrackNamesService>();

// MS-APDS: Authentication Protocol Domain Support
builder.Services.AddSingleton<AccountRestrictions>();
builder.Services.AddSingleton<ApdsLogonProcessor>();
builder.Services.AddSingleton<PacValidation>();
builder.Services.AddSingleton<DigestValidation>();

// MS-CTA: Claims Transformation
builder.Services.AddSingleton<IClaimTypeStore, ClaimTypeStore>();
builder.Services.AddSingleton<IClaimsProvider, ClaimsProvider>();
builder.Services.AddSingleton<ClaimTransformationEngine>();
builder.Services.AddSingleton<ITrustClaimsPolicy, TrustClaimsPolicy>();
builder.Services.AddSingleton<DynamicAccessControlEvaluator>();
builder.Services.AddSingleton<ClaimsSerialization>();

// MS-ADTS 3.1.1.5.2: Fine-Grained Password Policy (PSO)
builder.Services.AddSingleton<FineGrainedPasswordPolicyService>();

// Account lockout tracking and enforcement
builder.Services.AddSingleton<AccountLockoutService>();

// MFA / TOTP services
builder.Services.AddSingleton<TotpService>();
builder.Services.AddSingleton<MfaService>();

// FIDO2 / WebAuthn service
builder.Services.AddSingleton<Fido2Service>();

// Conditional Access / Risk-Based MFA
builder.Services.AddSingleton<ConditionalMfaService>();

// Smart Card / PIV authentication
builder.Services.AddSingleton<SmartCardService>();

// Privileged Access Management (PAM) — JIT elevation and break-glass
builder.Services.AddSingleton<PamService>();

// RADIUS authentication server (RFC 2865)
builder.Services.AddSingleton<RadiusServer>();
builder.Services.AddHostedService<RadiusServer>(sp => sp.GetRequiredService<RadiusServer>());

// SSH public key management
builder.Services.AddSingleton<SshKeyService>();

// Delegated Administration (RBAC for admin portal)
builder.Services.AddSingleton<DelegationService>();

// Group Managed Service Accounts (gMSA) per MS-GMSAD
builder.Services.AddSingleton<GmsaService>();

// Certificate Authority services
builder.Services.AddSingleton<CertificateStore>();
builder.Services.AddSingleton<CertificateTemplateService>();
builder.Services.AddSingleton<CertificateAuthorityService>();
builder.Services.AddHostedService<CertificateAutoEnrollmentService>();

// MS-GPOL: Group Policy Object processing
builder.Services.AddSingleton<GroupPolicyHandler>();
builder.Services.AddSingleton<GroupPolicyService>();

// LDIF import/export service
builder.Services.AddSingleton<LdifService>();

// Scheduled tasks (background scheduler with Cosmos DB persistence)
builder.Services.AddSingleton<ScheduledTaskService>();
builder.Services.AddHostedService<ScheduledTaskService>(sp => sp.GetRequiredService<ScheduledTaskService>());

// Webhook / event notification service
builder.Services.AddHttpClient("Webhooks");
builder.Services.AddSingleton<WebhookService>();

// SCIM 2.0 provisioning service (RFC 7644)
builder.Services.AddSingleton<ScimService>();

// HR System inbound sync service
builder.Services.AddHttpClient("HrSync");
builder.Services.AddSingleton<HrSyncService>();

// User lifecycle workflow engine
builder.Services.AddHttpClient("Workflows");
builder.Services.AddSingleton<WorkflowService>();

// OAuth2/OIDC Identity Provider
builder.Services.AddSingleton<OAuthService>();

// SAML 2.0 Identity Provider
builder.Services.AddSingleton<SamlService>();

// Schema replication (Cosmos DB change feed based)
builder.Services.Configure<ReplicationOptions>(builder.Configuration.GetSection(ReplicationOptions.SectionName));
builder.Services.AddSingleton<SchemaReplicationService>();

// SYSVOL service (cloud-native DFS-R replacement using Cosmos DB)
builder.Services.AddSingleton<SysvolService>();

// Password filter plugins
builder.Services.AddSingleton<IPasswordFilter, CommonPasswordFilter>();
builder.Services.AddSingleton<IPasswordFilter, RepetitionFilter>();
builder.Services.AddSingleton<IPasswordFilter, UsernameFilter>();
builder.Services.AddSingleton<IPasswordFilter, DictionaryWordFilter>();
builder.Services.AddSingleton<PasswordFilterService>();

// LDAP protocol audit service (in-memory ring buffer for protocol-level auditing)
// In the combined server (Directory.Server), AuditingOperationDispatcher fills this.
// In the web-only project, it exposes the query API for any shared audit data.
builder.Services.AddSingleton<LdapAuditService>();

// DNS zone store for AD-integrated DNS management
builder.Services.Configure<Directory.Dns.DnsOptions>(builder.Configuration.GetSection(Directory.Dns.DnsOptions.SectionName));
builder.Services.AddSingleton<Directory.Dns.DnsZoneStore>();

// DNSSEC signing and validation service
builder.Services.AddSingleton<Directory.Dns.Dnssec.DnssecService>();

// Read-Only Domain Controller (RODC) mode service
builder.Services.AddSingleton<Directory.Web.Services.RodcService>();

// DC instance info (needed by DC management services)
builder.Services.AddSingleton(new Directory.Core.Models.DcInstanceInfo());

// DC Management: FSMO roles, demotion, functional levels
builder.Services.AddSingleton<Directory.Web.Services.FsmoRoleService>();
builder.Services.AddSingleton<Directory.Web.Services.DcDemotionService>();
builder.Services.AddSingleton<Directory.Web.Services.FunctionalLevelService>();

// Domain Join service (orchestrates workstation/member-server join flow)
builder.Services.AddSingleton<Directory.Web.Services.DomainJoinService>();

// Post-domain-join verification and diagnostics
builder.Services.AddSingleton<Directory.Web.Services.JoinVerificationService>();
builder.Services.AddSingleton<Directory.Web.Services.JoinDiagnosticsService>();

// Self-Service Password Reset (SSPR)
builder.Services.AddSingleton<Directory.Web.Services.SelfServicePasswordService>();

// Computer pre-staging and offline domain join (djoin) services
builder.Services.AddSingleton<ComputerPrestagingService>();
builder.Services.AddSingleton<OfflineDomainJoinService>();

// Migration wizard (from AD DS / LDAP / LDIF sources)
builder.Services.AddSingleton<MigrationService>();

// LDAP Proxy / Virtual Directory service
builder.Services.AddSingleton<LdapProxyService>();

// MDM integration service (Intune, Jamf, WorkspaceOne, etc.)
builder.Services.AddHttpClient("MdmIntegration");
builder.Services.AddSingleton<MdmIntegrationService>();

// Compliance reporting service
builder.Services.AddSingleton<ComplianceReportService>();

// Access review / certification campaigns
builder.Services.AddSingleton<AccessReviewService>();

// Data retention policy enforcement
builder.Services.AddSingleton<DataRetentionService>();

// Multi-region configuration
builder.Services.AddHttpClient("RegionHealthCheck");
builder.Services.AddSingleton<MultiRegionService>();

// Per-bind-DN rate limiter for LDAP operations
builder.Services.AddSingleton<PerBindDnRateLimiter>();

// Cosmos DB — lazy initialization via holder so the app boots even without a valid connection string.
// The CosmosClientHolder creates the client if the config is valid, otherwise waits for the setup wizard.
// We register a Func<CosmosClient> factory so that CosmosDirectoryStore always resolves the *current*
// client — even after the setup wizard reconfigures the holder with a new connection string.
builder.Services.AddSingleton<CosmosClientHolder>();
builder.Services.AddSingleton<Func<CosmosClient>>(sp =>
    () => sp.GetRequiredService<CosmosClientHolder>().Client);

// Caching layer
builder.Services.AddMemoryCache();
builder.Services.AddDistributedMemoryCache();
builder.Services.Configure<CacheOptions>(builder.Configuration.GetSection(CacheOptions.SectionName));
builder.Services.AddSingleton<GroupExpansionCache>();
builder.Services.AddSingleton<CosmosDirectoryStore>();
builder.Services.AddSingleton<CosmosAuditStore>();
builder.Services.AddSingleton<IAuditService>(sp => sp.GetRequiredService<CosmosAuditStore>());
builder.Services.AddSingleton<DistributedCacheService>();
builder.Services.AddSingleton<IDirectoryStore>(sp =>
{
    var inner = sp.GetRequiredService<CosmosDirectoryStore>();
    var distributedCache = sp.GetRequiredService<DistributedCacheService>();
    var invalidationBus = sp.GetService<CacheInvalidationBus>();
    var optionsMonitor = sp.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<CacheOptions>>();
    var logger = sp.GetRequiredService<ILogger<CachedDirectoryStore>>();
    return new CachedDirectoryStore(inner, distributedCache, invalidationBus, optionsMonitor, logger);
});

// Configuration store for centralized config management
builder.Services.AddSingleton<CosmosConfigurationStore>(sp =>
{
    var client = sp.GetRequiredService<CosmosClientHolder>().Client;
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<CosmosDbOptions>>();
    var logger = sp.GetRequiredService<ILogger<CosmosConfigurationStore>>();
    return new CosmosConfigurationStore(client, options, logger);
});

// CosmosDbInitializer is registered but aware of the holder — it skips init if not configured.
// Registered as singleton so both the hosted service startup and setup endpoints can access it.
builder.Services.AddSingleton<DeferredCosmosDbInitializer>();
builder.Services.AddHostedService<DeferredCosmosDbInitializer>(sp => sp.GetRequiredService<DeferredCosmosDbInitializer>());

// Setup state tracking
builder.Services.AddSingleton<SetupStateService>();
builder.Services.AddSingleton<WebDomainProvisioner>();
builder.Services.AddSingleton<WebDcPromotionService>();

// Domain configuration (reads from directory at startup — tolerates missing database)
builder.Services.AddSingleton<DomainConfiguration>();
builder.Services.AddSingleton<WebDomainConfigurationService>();
builder.Services.AddHostedService<WebDomainConfigurationService>(sp => sp.GetRequiredService<WebDomainConfigurationService>());

// RFC 7807 ProblemDetails
builder.Services.AddProblemDetails();

// OpenAPI / Scalar API reference
builder.Services.AddOpenApi();

// ── Cookie-based authentication ──────────────────────────────────────────────
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "DirectoryAdmin.Session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.LoginPath = "/login";
        options.LogoutPath = "/api/v1/auth/logout";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        // Return 401 JSON instead of redirecting API calls
        options.Events.OnRedirectToLogin = ctx =>
        {
            if (ctx.Request.Path.StartsWithSegments("/api"))
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }
            ctx.Response.Redirect(ctx.RedirectUri);
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = ctx =>
        {
            if (ctx.Request.Path.StartsWithSegments("/api"))
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            }
            ctx.Response.Redirect(ctx.RedirectUri);
            return Task.CompletedTask;
        };
    });

// ── Authorization — default policy requires authenticated users ──────────────
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = null; // individual endpoints opt in via RequireAuthorization()
});

// ── Forwarded headers for reverse proxy support ──────────────────────────────
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // Restrict to known proxies/networks in production; clear defaults to trust all for now.
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// CORS — only expose Vite dev origin in Development
var allowedCorsOrigins = builder.Environment.IsDevelopment()
    ? new[] { "http://localhost:6173", "https://localhost:6001" }
    : new[] { "https://localhost:6001" };

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(allowedCorsOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials());
});

// Rate limiting
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Global per-IP policy: 2000 requests per minute (SPA fires many parallel reads per page)
    options.AddFixedWindowLimiter("per-ip", opt =>
    {
        opt.PermitLimit = 2000;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        opt.QueueLimit = 50;
    });

    // Stricter policy for mutations: 300 requests per minute (bulk ops can be bursty)
    options.AddSlidingWindowLimiter("mutation", opt =>
    {
        opt.PermitLimit = 300;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.SegmentsPerWindow = 6;
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        opt.QueueLimit = 25;
    });

    options.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.ContentType = "application/problem+json";
        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry)
            ? retry.TotalSeconds : 60;
        context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter).ToString();

        var problem = new
        {
            type = "https://tools.ietf.org/html/rfc6585#section-4",
            title = "Too Many Requests",
            status = 429,
            detail = $"Rate limit exceeded. Try again in {(int)retryAfter} seconds."
        };
        await context.HttpContext.Response.WriteAsJsonAsync(problem, ct);
    };
});

// OpenTelemetry tracing and metrics
var otelBuilder = builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService(DirectoryMetrics.ServiceName))
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation()
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
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddMeter(DirectoryMetrics.ServiceName)
            .AddOtlpExporter();

        if (builder.Environment.IsDevelopment())
        {
            metrics.AddConsoleExporter();
        }
    });

var app = builder.Build();

// Sync the IsDatabaseConfigured flag at startup
var holder = app.Services.GetRequiredService<CosmosClientHolder>();
var setupState = app.Services.GetRequiredService<SetupStateService>();
setupState.IsDatabaseConfigured = holder.IsConfigured;

// Wire SYSVOL hostname provider into GroupPolicyService so GPO gPCFileSysPath
// attributes use the configured SMB server hostname instead of the domain DNS name.
{
    var gpService = app.Services.GetRequiredService<GroupPolicyService>();
    var configStore = app.Services.GetRequiredService<CosmosConfigurationStore>();
    gpService.SysvolHostnameProvider = () =>
    {
        try
        {
            var doc = configStore.GetSectionAsync(DirectoryConstants.DefaultTenantId, "domain", "sysvol").GetAwaiter().GetResult();
            var sysvolConfig = SysvolEndpoints.MapToSysvolConfig(doc);
            return string.IsNullOrEmpty(sysvolConfig.SmbServerHostname) ? null : sysvolConfig.SmbServerHostname;
        }
        catch
        {
            return null;
        }
    };
}

// ── Security middleware ───────────────────────────────────────────────────────

// Forwarded headers must come first so HTTPS redirect and HSTS see the correct scheme
app.UseForwardedHeaders();

app.UseHttpsRedirection();

if (!app.Environment.IsDevelopment())
{
    // HTTP Strict Transport Security (HSTS) — production only
    app.UseHsts();
}

app.UseCors();
app.UseRateLimiter();

// RFC 7807 ProblemDetails middleware — catches unhandled exceptions
app.UseMiddleware<ProblemDetailsMiddleware>();

// RODC middleware — blocks write operations when in read-only mode
app.UseMiddleware<RodcMiddleware>();

// Authentication & Authorization — must come after CORS and before endpoint mapping
app.UseAuthentication();
app.UseAuthorization();

// In development, launch Vite and proxy non-API requests to it.
// This middleware skips /api/* paths — those always hit ASP.NET Core endpoints.
if (app.Environment.IsDevelopment())
{
    app.UseViteDevelopmentServer(port: 6173);
}

app.UseStaticFiles();

// OpenAPI spec endpoint — available at /openapi/v1.json
app.MapOpenApi();

// Scalar API reference UI — available at /scalar/v1 (development only)
if (app.Environment.IsDevelopment())
{
    app.MapScalarApiReference();
}

// ── Auth endpoints — AllowAnonymous (login/logout/me handle their own auth) ───
app.MapGroup("/api/v1/auth").MapAuthEndpoints().RequireRateLimiting("mutation");

// ── Setup endpoints — AllowAnonymous when not yet provisioned ─────────────────
app.MapGroup("/api/v1/setup").MapSetupEndpoints().RequireRateLimiting("mutation");

// ── Protected API endpoints (all require an authenticated admin session) ──────
app.MapGroup("/api/v1/tree").MapTreeEndpoints().RequireRateLimiting("per-ip").RequireAuthorization();
app.MapGroup("/api/v1/dashboard").MapDashboardEndpoints().RequireRateLimiting("per-ip").RequireAuthorization();
app.MapGroup("/api/v1/objects").MapObjectEndpoints().RequireRateLimiting("per-ip").RequireAuthorization();
app.MapGroup("/api/v1/users").MapUserEndpoints().RequireRateLimiting("mutation").RequireAuthorization();
app.MapGroup("/api/v1/groups").MapGroupEndpoints().RequireRateLimiting("mutation").RequireAuthorization();
app.MapGroup("/api/v1/computers").MapComputerEndpoints().RequireRateLimiting("mutation").RequireAuthorization();
app.MapGroup("/api/v1/schema").MapSchemaEndpoints().RequireRateLimiting("per-ip").RequireAuthorization();
app.MapGroup("/api/v1/domain").MapDomainConfigEndpoints().RequireRateLimiting("mutation").RequireAuthorization();
app.MapGroup("/api/v1/ous").MapOuEndpoints().RequireRateLimiting("mutation").RequireAuthorization();
app.MapGroup("/api/v1/recyclebin").MapRecycleBinEndpoints().RequireRateLimiting("mutation").RequireAuthorization();
app.MapGroup("/api/v1/sites").MapSiteEndpoints().RequireRateLimiting("mutation").RequireAuthorization();
app.MapGroup("/api/v1/replication").MapReplicationEndpoints().RequireRateLimiting("per-ip").RequireAuthorization();
// Health: /ping is public liveness probe; full health report requires auth
app.MapGroup("/api/v1/health").MapHealthEndpoints().RequireRateLimiting("per-ip");
app.MapGroup("/api/v1/gpos").MapGpoEndpoints().RequireRateLimiting("mutation").RequireAuthorization();
app.MapGroup("/api/v1/trusts").MapTrustEndpoints().RequireRateLimiting("mutation").RequireAuthorization();
app.MapGroup("/api/v1/cracknames").MapCrackNamesEndpoints().RequireRateLimiting("per-ip").RequireAuthorization();
app.MapGroup("/api/v1/configuration").MapConfigurationEndpoints().RequireRateLimiting("mutation").RequireAuthorization();
app.MapGroup("/api/v1/search").MapSearchEndpoints().RequireRateLimiting("per-ip").RequireAuthorization();
app.MapGroup("/api/v1/bulk").MapBulkEndpoints().RequireRateLimiting("mutation").RequireAuthorization();
app.MapGroup("/api/v1/dns").MapDnsEndpoints().RequireRateLimiting("mutation").RequireAuthorization();
app.MapGroup("/api/v1/service-accounts").MapServiceAccountEndpoints().RequireRateLimiting("mutation").RequireAuthorization();
app.MapGroup("/api/v1/certificates").MapCertificateEndpoints().RequireRateLimiting("mutation").RequireAuthorization();
app.MapGroup("/api/v1/password-policies").MapPasswordPolicyEndpoints().RequireRateLimiting("mutation").RequireAuthorization();
app.MapGroup("/api/v1/lockout").MapAccountLockoutEndpoints().RequireRateLimiting("mutation").RequireAuthorization();
app.MapGroup("/api/v1/audit").MapAuditEndpoints().RequireRateLimiting("per-ip").RequireAuthorization();
app.MapGroup("/api/v1/ldap-audit").MapLdapAuditEndpoints().RequireRateLimiting("per-ip").RequireAuthorization();
app.MapGroup("/api/v1/mfa").MapMfaEndpoints().RequireRateLimiting("mutation").RequireAuthorization();
app.MapGroup("/api/v1/fido2").MapFido2Endpoints().RequireRateLimiting("mutation").RequireAuthorization();
app.MapGroup("/api/v1/conditional-access").MapConditionalAccessEndpoints().RequireRateLimiting("mutation").RequireAuthorization();
app.MapGroup("/api/v1/smartcard").MapSmartCardEndpoints().RequireRateLimiting("mutation").RequireAuthorization();
app.MapGroup("/api/v1/service-settings").MapServiceSettingsEndpoints().RequireRateLimiting("per-ip").RequireAuthorization();
app.MapGroup("/api/v1/backup").MapBackupEndpoints().WithTags("Backup").RequireRateLimiting("mutation").RequireAuthorization();
app.MapGroup("/api/v1/delegation").MapDelegationEndpoints().RequireRateLimiting("mutation").RequireAuthorization();
app.MapGroup("/api/v1/gmsa").MapGmsaEndpoints().RequireRateLimiting("mutation").RequireAuthorization();
app.MapGroup("/api/v1/certificates/tls").MapTlsCertificateEndpoints().RequireRateLimiting("mutation").RequireAuthorization();
app.MapGroup("/api/v1/ldif").MapLdifEndpoints().RequireRateLimiting("mutation").RequireAuthorization();
app.MapGroup("/api/v1/scheduled-tasks").MapScheduledTaskEndpoints().RequireRateLimiting("mutation").RequireAuthorization();
app.MapGroup("/api/v1/webhooks").MapWebhookEndpoints().RequireRateLimiting("mutation").RequireAuthorization();
app.MapGroup("/api/v1/schema/replication").MapSchemaReplicationEndpoints().RequireRateLimiting("per-ip").RequireAuthorization();
app.MapGroup("/api/v1/sysvol").MapSysvolEndpoints().RequireRateLimiting("mutation").RequireAuthorization();
app.MapGroup("/api/v1/password-filters").MapPasswordFilterEndpoints().RequireRateLimiting("mutation").RequireAuthorization();
app.MapGroup("/api/v1/dns/dnssec").MapDnssecEndpoints().RequireRateLimiting("mutation").RequireAuthorization();
app.MapGroup("/api/v1/rodc").MapRodcEndpoints().RequireRateLimiting("mutation").RequireAuthorization();
app.MapGroup("/api/v1/dc-management").MapDcManagementEndpoints().RequireRateLimiting("mutation").RequireAuthorization();
app.MapGroup("/api/v1/domain-join").MapDomainJoinEndpoints().RequireRateLimiting("mutation").RequireAuthorization();
app.MapGroup("/api/v1/domain-join").MapJoinVerificationEndpoints().RequireRateLimiting("mutation").RequireAuthorization();
app.MapGroup("/api/v1/migration").MapMigrationEndpoints().RequireRateLimiting("mutation").RequireAuthorization();
app.MapGroup("/api/v1/ldap-proxy").MapLdapProxyEndpoints().RequireRateLimiting("mutation").RequireAuthorization();
app.MapGroup("/api/v1/mdm").MapMdmEndpoints().RequireRateLimiting("mutation").RequireAuthorization();
app.MapGroup("/api/v1/regions").MapMultiRegionEndpoints().RequireRateLimiting("mutation").RequireAuthorization();
app.MapGroup("/api/v1/rate-limits").MapRateLimitEndpoints().RequireRateLimiting("per-ip").RequireAuthorization();
app.MapGroup("/api/v1/compliance").MapComplianceEndpoints().RequireRateLimiting("per-ip").RequireAuthorization();
app.MapGroup("/api/v1/access-reviews").MapAccessReviewEndpoints().RequireRateLimiting("mutation").RequireAuthorization();
app.MapGroup("/api/v1/retention").MapDataRetentionEndpoints().RequireRateLimiting("mutation").RequireAuthorization();

// SSPR — self-service user-facing flows; individual endpoints handle their own validation
app.MapGroup("/api/v1/sspr").MapSsprEndpoints().RequireRateLimiting("mutation");

// OAuth2/OIDC Identity Provider endpoints — handle their own token-based auth
app.MapGroup("/api/v1/oauth").MapOAuthProtocolEndpoints().RequireRateLimiting("mutation");
app.MapGroup("/api/v1/oauth/clients").MapOAuthClientEndpoints().RequireRateLimiting("mutation").RequireAuthorization();
app.MapOidcDiscoveryEndpoints(); // /.well-known/* — public discovery documents

// SAML 2.0 Identity Provider endpoints — handle their own auth
app.MapGroup("/api/v1/saml").MapSamlProtocolEndpoints().RequireRateLimiting("mutation");
app.MapGroup("/api/v1/saml/service-providers").MapSamlSpEndpoints().RequireRateLimiting("mutation").RequireAuthorization();

// Privileged Access Management (PAM) endpoints
app.MapGroup("/api/v1/pam").MapPamEndpoints().RequireRateLimiting("mutation").RequireAuthorization();

// RADIUS server management endpoints
app.MapGroup("/api/v1/radius").MapRadiusEndpoints().RequireRateLimiting("mutation").RequireAuthorization();

// SSH key management endpoints
app.MapGroup("/api/v1/ssh-keys").MapSshKeyEndpoints().RequireRateLimiting("mutation").RequireAuthorization();

// SCIM 2.0 provisioning endpoints (RFC 7644) — bearer token auth handled inside ScimEndpoints
app.MapGroup("/scim/v2").MapScimEndpoints().RequireRateLimiting("mutation");
app.MapGroup("/api/v1/scim-integrations").MapScimManagementEndpoints().RequireRateLimiting("mutation").RequireAuthorization();

// HR System inbound sync endpoints
app.MapGroup("/api/v1/hr-sync").MapHrSyncEndpoints().RequireRateLimiting("mutation").RequireAuthorization();

// User lifecycle workflow endpoints
app.MapGroup("/api/v1/workflows").MapWorkflowEndpoints().RequireRateLimiting("mutation").RequireAuthorization();

app.MapGet("/api/v1/metrics", () =>
{
    return Results.Ok(new
    {
        service = DirectoryMetrics.ServiceName,
        timestamp = DateTimeOffset.UtcNow,
        note = "Use OTLP exporter for full metrics. This endpoint provides basic health info."
    });
}).WithTags("Metrics").ExcludeFromDescription().RequireAuthorization();

// In production, serve the pre-built SPA from wwwroot
if (!app.Environment.IsDevelopment())
{
    app.MapFallbackToFile("index.html");
}

app.Run();

// Expose Program as a public partial class so WebApplicationFactory<Program> can reference it from tests.
public partial class Program { }
