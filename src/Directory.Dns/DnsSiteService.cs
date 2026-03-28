using Directory.Core.Interfaces;
using Directory.Core.Models;
using Microsoft.Extensions.Logging;

namespace Directory.Dns;

/// <summary>
/// Provides site-aware DNS SRV record resolution per [MS-ADTS] section 6.3.
/// Maps client IPs to sites via subnet definitions, then returns
/// site-specific _ldap._tcp.{site}._sites.{domain} SRV records.
/// </summary>
public class DnsSiteService
{
    private readonly IDirectoryStore _store;
    private readonly ILogger<DnsSiteService> _logger;
    private readonly List<SiteInfo> _sites = [];
    private readonly List<SubnetMapping> _subnets = [];

    public DnsSiteService(IDirectoryStore store, ILogger<DnsSiteService> logger)
    {
        _store = store;
        _logger = logger;
    }

    public async Task LoadSitesAsync(string tenantId, CancellationToken ct = default)
    {
        // Load sites from CN=Sites,CN=Configuration,...
        var sitesResult = await _store.SearchAsync(
            tenantId, "CN=Sites,CN=Configuration", SearchScope.SingleLevel,
            new EqualityFilterNode("objectClass", "site"),
            null, 0, 0, null, 100, false, ct);

        _sites.Clear();
        foreach (var siteObj in sitesResult.Entries)
        {
            _sites.Add(new SiteInfo
            {
                Name = siteObj.Cn ?? "",
                Dn = siteObj.DistinguishedName,
            });
        }

        // Load subnets from CN=Subnets,CN=Sites,CN=Configuration,...
        var subnetsResult = await _store.SearchAsync(
            tenantId, "CN=Subnets,CN=Sites,CN=Configuration", SearchScope.SingleLevel,
            new EqualityFilterNode("objectClass", "subnet"),
            null, 0, 0, null, 1000, false, ct);

        _subnets.Clear();
        foreach (var subnetObj in subnetsResult.Entries)
        {
            var siteBl = subnetObj.GetAttribute("siteObjectBL");
            if (siteBl == null) continue;

            var subnetName = subnetObj.Cn ?? "";
            if (TryParseSubnet(subnetName, out var network, out var prefixLen))
            {
                _subnets.Add(new SubnetMapping
                {
                    Network = network,
                    PrefixLength = prefixLen,
                    SiteName = ExtractSiteName(siteBl.GetFirstString() ?? ""),
                });
            }
        }

        _logger.LogInformation("Loaded {SiteCount} sites and {SubnetCount} subnets", _sites.Count, _subnets.Count);
    }

    public string GetSiteForAddress(System.Net.IPAddress clientAddress)
    {
        var addressBytes = clientAddress.GetAddressBytes();
        foreach (var subnet in _subnets)
        {
            if (IsInSubnet(addressBytes, subnet.Network, subnet.PrefixLength))
                return subnet.SiteName;
        }
        return null; // Default site
    }

    public IReadOnlyList<string> GetSiteSpecificSrvRecords(string site, string domain)
    {
        return
        [
            $"_ldap._tcp.{site}._sites.{domain}",
            $"_kerberos._tcp.{site}._sites.{domain}",
            $"_ldap._tcp.{site}._sites.dc._msdcs.{domain}",
            $"_kerberos._tcp.{site}._sites.dc._msdcs.{domain}",
            $"_gc._tcp.{site}._sites.{domain}",
        ];
    }

    public IReadOnlyList<SiteInfo> GetAllSites() => _sites;

    private static bool TryParseSubnet(string subnet, out byte[] network, out int prefixLen)
    {
        network = [];
        prefixLen = 0;
        var parts = subnet.Split('/');
        if (parts.Length != 2) return false;
        if (!System.Net.IPAddress.TryParse(parts[0], out var address)) return false;
        if (!int.TryParse(parts[1], out prefixLen)) return false;
        network = address.GetAddressBytes();
        return true;
    }

    private static bool IsInSubnet(byte[] address, byte[] network, int prefixLength)
    {
        if (address.Length != network.Length) return false;
        var fullBytes = prefixLength / 8;
        var remainingBits = prefixLength % 8;
        for (int i = 0; i < fullBytes && i < address.Length; i++)
        {
            if (address[i] != network[i]) return false;
        }
        if (remainingBits > 0 && fullBytes < address.Length)
        {
            var mask = (byte)(0xFF << (8 - remainingBits));
            if ((address[fullBytes] & mask) != (network[fullBytes] & mask)) return false;
        }
        return true;
    }

    private static string ExtractSiteName(string siteDn)
    {
        // Extract CN=SiteName from the DN
        if (siteDn.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
        {
            var comma = siteDn.IndexOf(',');
            return comma > 3 ? siteDn[3..comma] : siteDn[3..];
        }
        return siteDn;
    }
}

public class SiteInfo
{
    public string Name { get; set; } = "";
    public string Dn { get; set; } = "";
}

public class SubnetMapping
{
    public byte[] Network { get; set; } = [];
    public int PrefixLength { get; set; }
    public string SiteName { get; set; } = "";
}
