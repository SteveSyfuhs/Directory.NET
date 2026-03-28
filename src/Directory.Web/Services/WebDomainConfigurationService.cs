using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Web.Models;

namespace Directory.Web.Services;

/// <summary>
/// Reads domain configuration from the directory at startup.
/// Minimal version of Directory.Server's DomainConfigurationService,
/// avoiding a dependency on the Server project.
/// Tolerates missing database connection (first-run scenario).
/// </summary>
public class WebDomainConfigurationService : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly INamingContextService _ncService;
    private readonly ILogger<WebDomainConfigurationService> _logger;
    private readonly DomainConfiguration _config;
    private readonly SetupStateService _setupState;

    public WebDomainConfigurationService(
        IServiceProvider services,
        INamingContextService ncService,
        DomainConfiguration config,
        SetupStateService setupState,
        ILogger<WebDomainConfigurationService> logger)
    {
        _services = services;
        _ncService = ncService;
        _config = config;
        _setupState = setupState;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // If the database isn't configured, skip — the setup wizard will call ReloadAsync later
        var holder = _services.GetRequiredService<CosmosClientHolder>();
        if (!holder.IsConfigured)
        {
            _logger.LogInformation("Database not configured — skipping domain configuration load. Setup wizard will handle this.");
            _setupState.IsProvisioned = false;
            return;
        }

        await LoadConfigurationAsync(cancellationToken);
    }

    private async Task LoadConfigurationAsync(CancellationToken cancellationToken)
    {
        var store = _services.GetRequiredService<IDirectoryStore>();
        var domainNc = _ncService.GetDomainNc();
        var domainDn = domainNc.Dn;

        _logger.LogInformation("Loading domain configuration from directory for {DomainDn}", domainDn);

        try
        {
            var domainRoot = await store.GetByDnAsync("default", domainDn, cancellationToken);
            if (domainRoot == null)
            {
                _logger.LogWarning("Domain root object not found at {DomainDn}; domain not yet provisioned", domainDn);
                _setupState.IsProvisioned = false;
                return;
            }

            // Read domain identity
            _config.DomainDn = domainDn;
            _config.DomainDnsName = domainNc.DnsName ?? "";
            _config.DomainSid = domainRoot.ObjectSid ?? "";
            _config.ForestDnsName = domainNc.DnsName ?? "";

            // Read NetBIOS name from crossRef
            var configDn = _ncService.GetConfigurationDn();
            var partitionsDn = $"CN=Partitions,{configDn}";
            var children = await store.GetChildrenAsync("default", partitionsDn, cancellationToken);
            foreach (var crossRef in children)
            {
                var ncName = crossRef.GetAttribute("nCName")?.GetFirstString();
                if (string.Equals(ncName, domainDn, StringComparison.OrdinalIgnoreCase))
                {
                    _config.NetBiosName = crossRef.GetAttribute("netBIOSName")?.GetFirstString() ?? "";
                    break;
                }
            }

            // Read password policy from domain root
            _config.MinPwdLength = GetIntAttribute(domainRoot, "minPwdLength", 7);
            _config.PwdHistoryLength = GetIntAttribute(domainRoot, "pwdHistoryLength", 24);
            _config.LockoutThreshold = GetIntAttribute(domainRoot, "lockoutThreshold", 0);
            _config.MaxPwdAge = GetLongAttribute(domainRoot, "maxPwdAge", -36288000000000L); // 42 days
            _config.MinPwdAge = GetLongAttribute(domainRoot, "minPwdAge", -864000000000L); // 1 day
            _config.PwdProperties = GetIntAttribute(domainRoot, "pwdProperties", 1);
            _config.LockoutDuration = GetLongAttribute(domainRoot, "lockoutDuration", -18000000000L); // 30 min
            _config.LockoutObservationWindow = GetLongAttribute(domainRoot, "lockoutObservationWindow", -18000000000L); // 30 min

            // Read functional level
            _config.DomainFunctionalLevel = GetIntAttribute(domainRoot, "msDS-Behavior-Version", 7);

            // Kerberos realm is the uppercase domain DNS name
            _config.KerberosRealm = _config.DomainDnsName.ToUpperInvariant();

            _setupState.IsProvisioned = true;

            _logger.LogInformation(
                "Domain config loaded: {DomainDns} ({NetBios}), SID={Sid}, FunctionalLevel={FL}",
                _config.DomainDnsName, _config.NetBiosName, _config.DomainSid, _config.DomainFunctionalLevel);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load domain configuration from directory; using defaults");
            _setupState.IsProvisioned = false;
        }
    }

    /// <summary>
    /// Reloads domain configuration from the directory store.
    /// Called after provisioning completes to pick up the new domain objects.
    /// </summary>
    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        await LoadConfigurationAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static int GetIntAttribute(DirectoryObject obj, string name, int defaultValue)
    {
        var attr = obj.GetAttribute(name);
        if (attr == null) return defaultValue;
        var str = attr.GetFirstString();
        return int.TryParse(str, out var val) ? val : defaultValue;
    }

    private static long GetLongAttribute(DirectoryObject obj, string name, long defaultValue)
    {
        var attr = obj.GetAttribute(name);
        if (attr == null) return defaultValue;
        var str = attr.GetFirstString();
        return long.TryParse(str, out var val) ? val : defaultValue;
    }
}
