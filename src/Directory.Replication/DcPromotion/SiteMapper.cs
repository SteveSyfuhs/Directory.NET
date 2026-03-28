using System.Net;
using Directory.Ldap.Client;
using Microsoft.Extensions.Logging;

namespace Directory.Replication.DcPromotion;

/// <summary>
/// Maps an IP address to an Active Directory site using subnet-to-site mappings
/// from CN=Subnets,CN=Sites,CN=Configuration.
/// </summary>
public class SiteMapper
{
    private const string DefaultSiteName = "Default-First-Site-Name";

    private readonly ILogger _logger;

    public SiteMapper(ILogger<SiteMapper> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Query the source DC for subnet-to-site mappings and determine our site.
    /// Falls back to "Default-First-Site-Name" if no matching subnet.
    /// </summary>
    public async Task<string> DetermineSiteAsync(
        LdapClient ldap,
        string configurationDn,
        string localIpAddress,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Determining site for IP {IpAddress}", localIpAddress);

        if (!IPAddress.TryParse(localIpAddress, out var localIp))
        {
            _logger.LogWarning("Cannot parse local IP address '{IpAddress}', falling back to {DefaultSite}",
                localIpAddress, DefaultSiteName);
            return DefaultSiteName;
        }

        // Search for all subnet objects
        var subnetsBaseDn = $"CN=Subnets,CN=Sites,{configurationDn}";
        var searchResult = await ldap.SearchAsync(
            subnetsBaseDn,
            Directory.Core.Models.SearchScope.SingleLevel,
            "(objectClass=subnet)",
            ["cn", "siteObject"],
            ct: ct);

        if (searchResult.ResultCode != 0 && searchResult.Entries.Count == 0)
        {
            _logger.LogWarning(
                "Failed to query subnets (resultCode={Code}), falling back to {DefaultSite}",
                searchResult.ResultCode, DefaultSiteName);
            return DefaultSiteName;
        }

        _logger.LogInformation("Found {Count} subnet definitions", searchResult.Entries.Count);

        // Find the best (longest prefix) matching subnet
        string bestSiteDn = null;
        int bestPrefixLength = -1;

        foreach (var entry in searchResult.Entries)
        {
            var subnetCn = entry.GetFirstValue("cn");
            var siteObjectDn = entry.GetFirstValue("siteObject");

            if (subnetCn is null || siteObjectDn is null)
                continue;

            if (IsInSubnet(localIp, subnetCn, out var prefixLength) && prefixLength > bestPrefixLength)
            {
                bestPrefixLength = prefixLength;
                bestSiteDn = siteObjectDn;

                _logger.LogDebug("IP {IpAddress} matches subnet {Subnet} (prefix /{Prefix})",
                    localIpAddress, subnetCn, prefixLength);
            }
        }

        if (bestSiteDn is not null)
        {
            // Extract site name from the DN: CN=MySite,CN=Sites,CN=Configuration,...
            var siteName = ExtractCnFromDn(bestSiteDn);
            _logger.LogInformation("Mapped IP {IpAddress} to site {SiteName}", localIpAddress, siteName);
            return siteName;
        }

        _logger.LogInformation("No matching subnet for IP {IpAddress}, using {DefaultSite}",
            localIpAddress, DefaultSiteName);
        return DefaultSiteName;
    }

    /// <summary>
    /// Parse a CIDR notation subnet and check if an IP belongs to it.
    /// Returns true if the address is in the subnet, and outputs the prefix length.
    /// </summary>
    private static bool IsInSubnet(IPAddress address, string cidrSubnet, out int prefixLength)
    {
        prefixLength = 0;

        var parts = cidrSubnet.Split('/');
        if (parts.Length != 2)
            return false;

        if (!IPAddress.TryParse(parts[0], out var networkAddress))
            return false;

        if (!int.TryParse(parts[1], out prefixLength))
            return false;

        // Ensure both addresses are the same family
        if (address.AddressFamily != networkAddress.AddressFamily)
            return false;

        var addressBytes = address.GetAddressBytes();
        var networkBytes = networkAddress.GetAddressBytes();

        // Compare the prefix bits
        var fullBytes = prefixLength / 8;
        var remainingBits = prefixLength % 8;

        for (var i = 0; i < fullBytes; i++)
        {
            if (addressBytes[i] != networkBytes[i])
                return false;
        }

        if (remainingBits > 0 && fullBytes < addressBytes.Length)
        {
            var mask = (byte)(0xFF << (8 - remainingBits));
            if ((addressBytes[fullBytes] & mask) != (networkBytes[fullBytes] & mask))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Extracts the first CN value from a DN string.
    /// e.g., "CN=MySite,CN=Sites,CN=Configuration,DC=corp,DC=com" returns "MySite".
    /// </summary>
    private static string ExtractCnFromDn(string dn)
    {
        if (dn.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
        {
            var commaIndex = dn.IndexOf(',');
            return commaIndex >= 0 ? dn[3..commaIndex] : dn[3..];
        }

        return dn;
    }
}
