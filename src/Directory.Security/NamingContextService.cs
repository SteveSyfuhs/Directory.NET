using Directory.Core.Interfaces;
using Directory.Core.Models;
using Microsoft.Extensions.Options;

namespace Directory.Security;

public class NamingContextOptions
{
    public const string SectionName = "NamingContexts";
    public string DomainDn { get; set; } = "DC=directory,DC=local";
    public string ForestDnsName { get; set; } = "directory.local";
    public string DomainSid { get; set; } = "";
}

public class NamingContextService : INamingContextService
{
    private readonly object _lock = new();
    private NamingContext _domainNc = null;
    private NamingContext _configNc = null;
    private NamingContext _schemaNc = null;
    private List<NamingContext> _allNcs = null;

    private DistinguishedName _domainDnParsed = null;
    private DistinguishedName _configDnParsed = null;
    private DistinguishedName _schemaDnParsed = null;

    public NamingContextService(IOptions<NamingContextOptions> options)
    {
        var opts = options.Value;
        Initialize(opts.DomainDn, opts.ForestDnsName, string.IsNullOrEmpty(opts.DomainSid) ? null : opts.DomainSid);
    }

    /// <summary>
    /// Reconfigures naming contexts at runtime. Called after domain provisioning
    /// to update the singleton with the actual domain DN chosen by the user.
    /// </summary>
    public void Reconfigure(string domainDn, string forestDnsName, string domainSid = null)
    {
        lock (_lock)
        {
            Initialize(domainDn, forestDnsName, domainSid);
        }
    }

    private void Initialize(string domainDn, string forestDnsName, string domainSid)
    {
        string configDn = $"CN=Configuration,{domainDn}";
        string schemaDn = $"CN=Schema,CN=Configuration,{domainDn}";

        _domainDnParsed = DistinguishedName.Parse(domainDn);
        _configDnParsed = DistinguishedName.Parse(configDn);
        _schemaDnParsed = DistinguishedName.Parse(schemaDn);

        _domainNc = new NamingContext
        {
            Type = NamingContextType.Domain,
            Dn = domainDn,
            DnsName = forestDnsName,
            DomainSid = domainSid
        };

        _configNc = new NamingContext
        {
            Type = NamingContextType.Configuration,
            Dn = configDn,
            DnsName = forestDnsName
        };

        _schemaNc = new NamingContext
        {
            Type = NamingContextType.Schema,
            Dn = schemaDn,
            DnsName = forestDnsName
        };

        _allNcs = [_schemaNc, _configNc, _domainNc];
    }

    public NamingContext GetNamingContext(string dn)
    {
        var parsed = DistinguishedName.Parse(dn);

        // Check most specific first: Schema > Configuration > Domain
        if (parsed.Equals(_schemaDnParsed) || parsed.IsDescendantOf(_schemaDnParsed))
            return _schemaNc;

        if (parsed.Equals(_configDnParsed) || parsed.IsDescendantOf(_configDnParsed))
            return _configNc;

        if (parsed.Equals(_domainDnParsed) || parsed.IsDescendantOf(_domainDnParsed))
            return _domainNc;

        return null;
    }

    public NamingContext GetDomainNc() => _domainNc;
    public NamingContext GetConfigurationNc() => _configNc;
    public NamingContext GetSchemaNc() => _schemaNc;
    public IReadOnlyList<NamingContext> GetAllNamingContexts() => _allNcs;

    public bool IsDnInNamingContext(string dn, NamingContextType type)
    {
        var nc = GetNamingContext(dn);
        return nc is not null && nc.Type == type;
    }

    public string GetConfigurationDn() => _configNc.Dn;
    public string GetSchemaDn() => _schemaNc.Dn;
}
