using Directory.Core.Interfaces;
using Directory.Core.Models;
using Microsoft.Extensions.Logging;

namespace Directory.Kerberos;

/// <summary>
/// SPN (Service Principal Name) validation and canonicalization per MS-ADTS section 3.1.1.3.1.
/// Handles HOST alias expansion, SPN format parsing, and uniqueness validation.
/// </summary>
public class SpnService
{
    private readonly IDirectoryStore _store;
    private readonly ILogger<SpnService> _logger;

    /// <summary>
    /// HOST SPN aliases — these service types are automatically implied when HOST is registered.
    /// Per MS-ADTS section 3.1.1.3.1.2, the HOST SPN maps to these service class aliases.
    /// </summary>
    private static readonly HashSet<string> HostSpnAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        "alerter", "appmgmt", "cisvc", "clipsrv", "browser", "dhcp", "dnscache",
        "replicator", "eventlog", "eventsystem", "policyagent", "oakley", "dmserver",
        "dns", "mcsvc", "fax", "msiserver", "ias", "messenger", "netdde", "netddedsm",
        "netlogon", "netman", "nmagent", "plugplay", "protectedstorage", "rasman",
        "rpclocator", "rpc", "rpcss", "remoteaccess", "rsvp", "samss", "scardsvr",
        "scesrv", "seclogon", "scm", "dcom", "cifs", "spooler", "snmp", "schedule",
        "tapisrv", "trksvr", "trkwks", "ups", "time", "wins", "www", "http",
        "w3svc", "iisadmin", "msdtc", "termsrv", "ldap", "gc"
    };

    public SpnService(IDirectoryStore store, ILogger<SpnService> logger)
    {
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// Validate that an SPN is unique within the tenant.
    /// Optionally excludes a specific DN (for updates to the object that already owns the SPN).
    /// </summary>
    public async Task<bool> ValidateSpnUniquenessAsync(
        string tenantId, string spn, string excludeDn = null, CancellationToken ct = default)
    {
        var results = await _store.GetByServicePrincipalNameAsync(tenantId, spn, ct);
        return results.All(r =>
            excludeDn != null && r.DistinguishedName.Equals(excludeDn, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Canonicalize an SPN by lowercasing the hostname portion.
    /// SPN format: serviceclass/hostname[:port][/servicename]
    /// </summary>
    public static string Canonicalize(string spn)
    {
        var parts = spn.Split('/');
        if (parts.Length < 2) return spn;

        // Lowercase hostname portion (including any port suffix)
        parts[1] = parts[1].ToLowerInvariant();
        return string.Join("/", parts);
    }

    /// <summary>
    /// When HOST/hostname is registered, generate the implied SPNs for all host aliases.
    /// </summary>
    public static IEnumerable<string> ExpandHostSpn(string hostname)
    {
        foreach (var alias in HostSpnAliases)
        {
            yield return $"{alias}/{hostname}";
        }
    }

    /// <summary>
    /// Check whether a given service class is one of the HOST SPN aliases.
    /// </summary>
    public static bool IsHostAlias(string serviceClass)
    {
        return HostSpnAliases.Contains(serviceClass);
    }

    /// <summary>
    /// Parse an SPN into its component parts.
    /// SPN format: serviceclass/hostname[:port][/servicename]
    /// </summary>
    public static (string ServiceClass, string Hostname, int? Port, string ServiceName) ParseSpn(string spn)
    {
        var parts = spn.Split('/');
        if (parts.Length < 2) return (spn, "", null, null);

        var serviceClass = parts[0];
        var hostAndPort = parts[1];
        string serviceName = parts.Length > 2 ? parts[2] : null;

        int? port = null;
        var colonIdx = hostAndPort.IndexOf(':');
        string hostname;
        if (colonIdx >= 0)
        {
            hostname = hostAndPort[..colonIdx];
            if (int.TryParse(hostAndPort[(colonIdx + 1)..], out var p))
                port = p;
        }
        else
        {
            hostname = hostAndPort;
        }

        return (serviceClass, hostname, port, serviceName);
    }

    /// <summary>
    /// Resolve an SPN to the directory object that owns it, considering HOST alias expansion.
    /// If the SPN is not found directly and the service class is a HOST alias,
    /// also searches for the corresponding HOST/hostname SPN.
    /// </summary>
    public async Task<DirectoryObject> ResolveSpnOwnerAsync(
        string tenantId, string spn, CancellationToken ct = default)
    {
        // Try exact match first
        var results = await _store.GetByServicePrincipalNameAsync(tenantId, spn, ct);
        if (results.Count > 0)
            return results[0];

        // If the service class is a HOST alias, try HOST/hostname
        var (serviceClass, hostname, port, _) = ParseSpn(spn);
        if (IsHostAlias(serviceClass))
        {
            var hostSpn = port.HasValue
                ? $"HOST/{hostname}:{port}"
                : $"HOST/{hostname}";

            results = await _store.GetByServicePrincipalNameAsync(tenantId, hostSpn, ct);
            if (results.Count > 0)
            {
                _logger.LogDebug("Resolved SPN {Spn} via HOST alias to {Owner}",
                    spn, results[0].DistinguishedName);
                return results[0];
            }
        }

        _logger.LogDebug("SPN not found: {Spn}", spn);
        return null;
    }
}
