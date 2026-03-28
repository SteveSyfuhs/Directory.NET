using Directory.Core.Interfaces;
using Directory.Core.Models;

namespace Directory.Server;

/// <summary>
/// Reads domain-level configuration from the directory on startup.
/// Domain-level settings (domain name, password policy, functional level, etc.)
/// are stored on directory objects rather than appsettings.json, so all DCs
/// share the same configuration. Falls back to appsettings.json only for
/// node-specific settings (Cosmos connection, ports, IP addresses).
/// </summary>
public class DomainConfigurationService : IHostedService
{
    private readonly IDirectoryStore _store;
    private readonly INamingContextService _ncService;
    private readonly ILogger<DomainConfigurationService> _logger;
    private readonly DomainConfiguration _config;

    public DomainConfigurationService(
        IDirectoryStore store,
        INamingContextService ncService,
        DomainConfiguration config,
        ILogger<DomainConfigurationService> logger)
    {
        _store = store;
        _ncService = ncService;
        _config = config;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var domainNc = _ncService.GetDomainNc();
        var domainDn = domainNc.Dn;

        _logger.LogInformation("Loading domain configuration from directory for {DomainDn}", domainDn);

        try
        {
            var domainRoot = await _store.GetByDnAsync("default", domainDn, cancellationToken);
            if (domainRoot == null)
            {
                _logger.LogWarning("Domain root object not found at {DomainDn}; using default configuration", domainDn);
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
            var children = await _store.GetChildrenAsync("default", partitionsDn, cancellationToken);
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

            // Read functional level
            _config.DomainFunctionalLevel = GetIntAttribute(domainRoot, "msDS-Behavior-Version", 7);

            // Kerberos realm is the uppercase domain DNS name
            _config.KerberosRealm = _config.DomainDnsName.ToUpperInvariant();

            _logger.LogInformation(
                "Domain config loaded: {DomainDns} ({NetBios}), SID={Sid}, FunctionalLevel={FL}",
                _config.DomainDnsName, _config.NetBiosName, _config.DomainSid, _config.DomainFunctionalLevel);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load domain configuration from directory; using defaults");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static int GetIntAttribute(DirectoryObject obj, string name, int defaultValue)
    {
        var attr = obj.GetAttribute(name);
        if (attr == null) return defaultValue;
        var str = attr.GetFirstString();
        return int.TryParse(str, out var val) ? val : defaultValue;
    }
}

/// <summary>
/// Domain-level configuration populated from the directory at startup.
/// Shared across all DCs since the values are read from Cosmos DB.
/// </summary>
public class DomainConfiguration
{
    // Domain identity
    public string DomainDn { get; set; } = "";
    public string DomainDnsName { get; set; } = "";
    public string NetBiosName { get; set; } = "";
    public string DomainSid { get; set; } = "";
    public string ForestDnsName { get; set; } = "";
    public string KerberosRealm { get; set; } = "";

    // Password policy
    public int MinPwdLength { get; set; } = 7;
    public int PwdHistoryLength { get; set; } = 24;
    public int LockoutThreshold { get; set; } = 0;

    // Functional level
    public int DomainFunctionalLevel { get; set; } = 7; // Windows Server 2016
}
