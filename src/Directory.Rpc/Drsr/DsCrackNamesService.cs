using System.Text.RegularExpressions;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Microsoft.Extensions.Logging;

namespace Directory.Rpc.Drsr;

/// <summary>
/// Implements DsCrackNames (MS-DRSR 4.1.4.2) — translates Active Directory object names
/// between different name formats (DN, NT4, UPN, canonical, GUID, SID, SPN, etc.).
///
/// This is a standalone service that can be consumed by the DRS RPC wire handler,
/// REST APIs, or any other component needing name translation.
/// </summary>
public class DsCrackNamesService
{
    private readonly IDirectoryStore _store;
    private readonly INamingContextService _ncService;
    private readonly ILogger<DsCrackNamesService> _logger;

    public DsCrackNamesService(
        IDirectoryStore store,
        INamingContextService ncService,
        ILogger<DsCrackNamesService> logger)
    {
        _store = store;
        _ncService = ncService;
        _logger = logger;
    }

    /// <summary>
    /// Translates an array of names from one format to another.
    /// </summary>
    /// <param name="tenantId">Tenant identifier for multi-tenant isolation.</param>
    /// <param name="formatOffered">The format of the input names (0 = auto-detect).</param>
    /// <param name="formatDesired">The desired output format.</param>
    /// <param name="flags">Flags controlling behavior (1 = syntactical only).</param>
    /// <param name="names">The names to translate.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An array of results, one per input name.</returns>
    public async Task<CrackNameResult[]> CrackNamesAsync(
        string tenantId,
        uint formatOffered,
        uint formatDesired,
        uint flags,
        string[] names,
        CancellationToken ct = default)
    {
        var domainNc = _ncService.GetDomainNc();
        var domainDn = domainNc.Dn;
        var syntacticalOnly = (flags & (uint)CrackNameFlags.DS_NAME_FLAG_SYNTACTICAL_ONLY) != 0;

        var results = new CrackNameResult[names.Length];

        for (int i = 0; i < names.Length; i++)
        {
            var name = names[i];
            var offered = (CrackNameFormat)formatOffered;
            var desired = (CrackNameFormat)formatDesired;

            try
            {
                if (offered == CrackNameFormat.DS_UNKNOWN_NAME)
                {
                    offered = DetectNameFormat(name);

                    if (offered == CrackNameFormat.DS_UNKNOWN_NAME)
                    {
                        results[i] = new CrackNameResult
                        {
                            Status = CrackNameStatus.DS_NAME_ERROR_NOT_FOUND,
                        };
                        continue;
                    }
                }

                if (syntacticalOnly)
                {
                    results[i] = ConvertSyntactical(name, offered, desired, domainDn);
                }
                else
                {
                    results[i] = await ResolveAndConvertAsync(
                        name, offered, desired, tenantId, domainDn, ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error cracking name '{Name}' from {Offered} to {Desired}",
                    name, offered, desired);

                results[i] = new CrackNameResult
                {
                    Status = CrackNameStatus.DS_NAME_ERROR_RESOLVING,
                };
            }
        }

        return results;
    }

    #region Auto-detection

    /// <summary>
    /// Auto-detects the format of a name string per DS_UNKNOWN_NAME (format 0).
    /// </summary>
    internal static CrackNameFormat DetectNameFormat(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return CrackNameFormat.DS_UNKNOWN_NAME;

        // GUID: {xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx}
        if (name.StartsWith('{') && name.EndsWith('}') && Guid.TryParse(name.Trim('{', '}'), out _))
            return CrackNameFormat.DS_UNIQUE_ID_NAME;

        // SID: S-1-5-21-...
        if (name.StartsWith("S-1-", StringComparison.OrdinalIgnoreCase) && Regex.IsMatch(name, @"^S-\d+-\d+(-\d+)+$"))
            return CrackNameFormat.DS_SID_OR_SID_HISTORY_NAME;

        // DN: starts with CN=, OU=, DC= (case-insensitive)
        if (Regex.IsMatch(name, @"^(CN|OU|DC)\s*=", RegexOptions.IgnoreCase))
            return CrackNameFormat.DS_FQDN_1779_NAME;

        // NT4: DOMAIN\user
        if (name.Contains('\\') && !name.Contains('/') && !name.Contains('\n'))
            return CrackNameFormat.DS_NT4_ACCOUNT_NAME;

        // UPN: user@domain
        if (name.Contains('@') && !name.Contains('/') && !name.Contains('\\'))
            return CrackNameFormat.DS_USER_PRINCIPAL_NAME;

        // Canonical-Ex: domain.com/path\nleaf
        if (name.Contains('\n') && name.Contains('/'))
            return CrackNameFormat.DS_CANONICAL_NAME_EX;

        // Canonical: domain.com/path/leaf
        if (name.Contains('/') && name.Contains('.'))
            return CrackNameFormat.DS_CANONICAL_NAME;

        // SPN: service/host or service/host:port
        if (name.Contains('/') && !name.Contains('.') is false)
        {
            var slashIdx = name.IndexOf('/');
            if (slashIdx > 0 && slashIdx < name.Length - 1)
                return CrackNameFormat.DS_SERVICE_PRINCIPAL_NAME;
        }

        // DNS domain: contains dots but no slashes or special chars
        if (name.Contains('.') && !name.Contains('/') && !name.Contains('\\') && !name.Contains('@'))
            return CrackNameFormat.DS_DNS_DOMAIN_NAME;

        return CrackNameFormat.DS_UNKNOWN_NAME;
    }

    #endregion

    #region Resolution

    /// <summary>
    /// Resolves a name to a directory object, then converts to the desired format.
    /// </summary>
    private async Task<CrackNameResult> ResolveAndConvertAsync(
        string name,
        CrackNameFormat formatOffered,
        CrackNameFormat formatDesired,
        string tenantId,
        string domainDn,
        CancellationToken ct)
    {
        DirectoryObject obj = null;

        try
        {
            obj = formatOffered switch
            {
                CrackNameFormat.DS_FQDN_1779_NAME =>
                    await _store.GetByDnAsync(tenantId, name, ct),

                CrackNameFormat.DS_UNIQUE_ID_NAME =>
                    await _store.GetByGuidAsync(tenantId, NormalizeGuid(name), ct),

                CrackNameFormat.DS_NT4_ACCOUNT_NAME =>
                    await ResolveNt4NameAsync(tenantId, domainDn, name, ct),

                CrackNameFormat.DS_USER_PRINCIPAL_NAME =>
                    await _store.GetByUpnAsync(tenantId, name, ct),

                CrackNameFormat.DS_DISPLAY_NAME =>
                    await ResolveByAttributeAsync(tenantId, domainDn, "displayName", name, ct),

                CrackNameFormat.DS_SERVICE_PRINCIPAL_NAME =>
                    await ResolveSpnAsync(tenantId, name, ct),

                CrackNameFormat.DS_CANONICAL_NAME or CrackNameFormat.DS_CANONICAL_NAME_EX =>
                    await ResolveCanonicalNameAsync(tenantId, name, ct),

                CrackNameFormat.DS_SID_OR_SID_HISTORY_NAME =>
                    await ResolveByAttributeAsync(tenantId, domainDn, "objectSid", name, ct),

                CrackNameFormat.DS_DNS_DOMAIN_NAME =>
                    await ResolveDnsDomainAsync(tenantId, name, ct),

                _ => null,
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error resolving name '{Name}' in format {Format}", name, formatOffered);
        }

        if (obj == null)
        {
            return new CrackNameResult
            {
                Status = CrackNameStatus.DS_NAME_ERROR_NOT_FOUND,
            };
        }

        return FormatObject(obj, formatDesired, domainDn);
    }

    #endregion

    #region Format output

    /// <summary>
    /// Converts a resolved directory object to the desired output format.
    /// </summary>
    private static CrackNameResult FormatObject(DirectoryObject obj, CrackNameFormat format, string domainDn)
    {
        var domainDns = DnToDnsName(domainDn);

        string outputName = format switch
        {
            CrackNameFormat.DS_FQDN_1779_NAME => obj.DistinguishedName,

            CrackNameFormat.DS_NT4_ACCOUNT_NAME =>
                obj.SAMAccountName != null
                    ? $"{DnToNetbiosName(domainDn)}\\{obj.SAMAccountName}"
                    : null,

            CrackNameFormat.DS_DISPLAY_NAME => obj.DisplayName ?? obj.Cn,

            CrackNameFormat.DS_UNIQUE_ID_NAME => $"{{{obj.ObjectGuid}}}",

            CrackNameFormat.DS_CANONICAL_NAME => DnToCanonical(obj.DistinguishedName, domainDn),

            CrackNameFormat.DS_CANONICAL_NAME_EX => DnToCanonicalEx(obj.DistinguishedName, domainDn),

            CrackNameFormat.DS_USER_PRINCIPAL_NAME =>
                obj.UserPrincipalName ?? (obj.SAMAccountName != null
                    ? $"{obj.SAMAccountName}@{domainDns}"
                    : null),

            CrackNameFormat.DS_SERVICE_PRINCIPAL_NAME =>
                obj.ServicePrincipalName.Count > 0 ? obj.ServicePrincipalName[0] : null,

            CrackNameFormat.DS_SID_OR_SID_HISTORY_NAME => obj.ObjectSid,

            CrackNameFormat.DS_DNS_DOMAIN_NAME => domainDns,

            _ => null,
        };

        if (outputName == null)
        {
            return new CrackNameResult
            {
                Status = CrackNameStatus.DS_NAME_ERROR_NO_MAPPING,
                DnsDomainName = domainDns,
            };
        }

        return new CrackNameResult
        {
            Status = CrackNameStatus.DS_NAME_NO_ERROR,
            Name = outputName,
            DnsDomainName = domainDns,
        };
    }

    #endregion

    #region Syntactical conversion

    /// <summary>
    /// Performs name conversion without directory lookups (DS_NAME_FLAG_SYNTACTICAL_ONLY).
    /// Only certain format pairs are supported syntactically.
    /// </summary>
    private static CrackNameResult ConvertSyntactical(
        string name, CrackNameFormat offered, CrackNameFormat desired, string domainDn)
    {
        var domainDns = DnToDnsName(domainDn);

        // DN -> Canonical
        if (offered == CrackNameFormat.DS_FQDN_1779_NAME && desired == CrackNameFormat.DS_CANONICAL_NAME)
        {
            var canonical = DnToCanonical(name, domainDn);
            if (canonical != null)
                return Success(canonical, domainDns);
        }

        // DN -> Canonical-Ex
        if (offered == CrackNameFormat.DS_FQDN_1779_NAME && desired == CrackNameFormat.DS_CANONICAL_NAME_EX)
        {
            var canonical = DnToCanonicalEx(name, domainDn);
            if (canonical != null)
                return Success(canonical, domainDns);
        }

        // Canonical -> DN
        if (offered == CrackNameFormat.DS_CANONICAL_NAME && desired == CrackNameFormat.DS_FQDN_1779_NAME)
        {
            var dn = CanonicalToDn(name);
            if (dn != null)
                return Success(dn, domainDns);
        }

        // Canonical-Ex -> DN
        if (offered == CrackNameFormat.DS_CANONICAL_NAME_EX && desired == CrackNameFormat.DS_FQDN_1779_NAME)
        {
            var dn = CanonicalToDn(name);
            if (dn != null)
                return Success(dn, domainDns);
        }

        // DN -> DNS Domain Name
        if (offered == CrackNameFormat.DS_FQDN_1779_NAME && desired == CrackNameFormat.DS_DNS_DOMAIN_NAME)
        {
            var dns = DnToDnsName(name);
            if (!string.IsNullOrEmpty(dns))
                return Success(dns, domainDns);
        }

        // DN -> NT4 (domain part only — we can extract the domain but not the sAMAccountName)
        if (offered == CrackNameFormat.DS_FQDN_1779_NAME && desired == CrackNameFormat.DS_NT4_ACCOUNT_NAME)
        {
            // Extract CN from DN as approximate sAMAccountName for syntactical conversion
            var components = ParseDnComponents(name);
            var cn = components.FirstOrDefault(c => c.Type.Equals("CN", StringComparison.OrdinalIgnoreCase));
            if (cn != null)
            {
                var netbios = DnToNetbiosName(name);
                return Success($"{netbios}\\{cn.Value}", domainDns);
            }
        }

        return new CrackNameResult
        {
            Status = CrackNameStatus.DS_NAME_ERROR_NO_SYNTACTICAL_MAPPING,
        };
    }

    private static CrackNameResult Success(string name, string dnsDomain) => new()
    {
        Status = CrackNameStatus.DS_NAME_NO_ERROR,
        Name = name,
        DnsDomainName = dnsDomain,
    };

    #endregion

    #region Lookup helpers

    private async Task<DirectoryObject> ResolveNt4NameAsync(
        string tenantId, string domainDn, string nt4Name, CancellationToken ct)
    {
        var parts = nt4Name.Split('\\', 2);
        if (parts.Length != 2)
            return null;

        var samAccountName = parts[1];
        return await _store.GetBySamAccountNameAsync(tenantId, domainDn, samAccountName, ct);
    }

    private async Task<DirectoryObject> ResolveByAttributeAsync(
        string tenantId, string domainDn, string attribute, string value, CancellationToken ct)
    {
        var result = await _store.SearchAsync(
            tenantId, domainDn, SearchScope.WholeSubtree,
            new EqualityFilterNode(attribute, value),
            null, 1, 0, null, 1, false, ct);

        return result.Entries.Count > 0 ? result.Entries[0] : null;
    }

    private async Task<DirectoryObject> ResolveSpnAsync(
        string tenantId, string spn, CancellationToken ct)
    {
        var results = await _store.GetByServicePrincipalNameAsync(tenantId, spn, ct);
        return results.Count > 0 ? results[0] : null;
    }

    private async Task<DirectoryObject> ResolveCanonicalNameAsync(
        string tenantId, string canonical, CancellationToken ct)
    {
        var dn = CanonicalToDn(canonical);
        if (dn == null)
            return null;

        return await _store.GetByDnAsync(tenantId, dn, ct);
    }

    private async Task<DirectoryObject> ResolveDnsDomainAsync(
        string tenantId, string dnsName, CancellationToken ct)
    {
        var dn = DnsNameToDn(dnsName);
        return await _store.GetByDnAsync(tenantId, dn, ct);
    }

    #endregion

    #region Format conversion helpers

    /// <summary>
    /// Converts a DN to DNS domain name.
    /// "DC=corp,DC=example,DC=com" -> "corp.example.com"
    /// </summary>
    internal static string DnToDnsName(string dn)
    {
        var parts = dn.Split(',')
            .Where(p => p.TrimStart().StartsWith("DC=", StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Split('=', 2)[1].Trim())
            .ToList();

        return string.Join('.', parts);
    }

    /// <summary>
    /// Converts a DNS name to DN format.
    /// "corp.example.com" -> "DC=corp,DC=example,DC=com"
    /// </summary>
    internal static string DnsNameToDn(string dnsName)
    {
        var parts = dnsName.Split('.');
        return string.Join(',', parts.Select(p => $"DC={p}"));
    }

    /// <summary>
    /// Extracts a NetBIOS domain name from a DN.
    /// "DC=corp,DC=example,DC=com" -> "CORP"
    /// Also handles DNs like "CN=User,CN=Users,DC=corp,DC=com" by finding the first DC component.
    /// </summary>
    internal static string DnToNetbiosName(string dn)
    {
        var firstDc = dn.Split(',')
            .FirstOrDefault(p => p.TrimStart().StartsWith("DC=", StringComparison.OrdinalIgnoreCase));

        if (firstDc == null)
            return "UNKNOWN";

        return firstDc.Split('=', 2)[1].Trim().ToUpperInvariant();
    }

    /// <summary>
    /// Converts a DN to canonical name format.
    /// "CN=John Doe,CN=Users,DC=corp,DC=com" -> "corp.com/Users/John Doe"
    /// </summary>
    internal static string DnToCanonical(string dn, string domainDn)
    {
        var dnsName = DnToDnsName(domainDn);
        var components = ParseDnComponents(dn);

        var nonDcComponents = components
            .Where(c => !c.Type.Equals("DC", StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Value)
            .Reverse()
            .ToList();

        if (nonDcComponents.Count == 0)
            return dnsName;

        return $"{dnsName}/{string.Join('/', nonDcComponents)}";
    }

    /// <summary>
    /// Converts a DN to canonical-ex name format (newline before last component).
    /// "CN=John Doe,CN=Users,DC=corp,DC=com" -> "corp.com/Users\nJohn Doe"
    /// </summary>
    internal static string DnToCanonicalEx(string dn, string domainDn)
    {
        var canonical = DnToCanonical(dn, domainDn);
        if (canonical == null)
            return null;

        var lastSlash = canonical.LastIndexOf('/');
        if (lastSlash >= 0)
        {
            return string.Concat(canonical.AsSpan(0, lastSlash), "\n", canonical.AsSpan(lastSlash + 1));
        }

        return canonical;
    }

    /// <summary>
    /// Converts a canonical name back to DN format.
    /// "corp.com/Users/John Doe" -> "CN=John Doe,CN=Users,DC=corp,DC=com"
    /// </summary>
    internal static string CanonicalToDn(string canonical)
    {
        // Normalize canonical-ex (newline) to canonical (slash)
        canonical = canonical.Replace('\n', '/');

        var slashPos = canonical.IndexOf('/');
        string dnsName;
        string[] pathParts;

        if (slashPos >= 0)
        {
            dnsName = canonical[..slashPos];
            pathParts = canonical[(slashPos + 1)..].Split('/', StringSplitOptions.RemoveEmptyEntries);
        }
        else
        {
            dnsName = canonical;
            pathParts = [];
        }

        var dnParts = new List<string>();

        // Add non-DC components in reverse order
        for (int i = pathParts.Length - 1; i >= 0; i--)
        {
            dnParts.Add($"CN={pathParts[i]}");
        }

        // Add DC components
        foreach (var part in dnsName.Split('.'))
        {
            dnParts.Add($"DC={part}");
        }

        return string.Join(',', dnParts);
    }

    private static string NormalizeGuid(string guidStr)
    {
        return guidStr.Trim('{', '}', ' ');
    }

    private record DnComponent(string Type, string Value);

    private static List<DnComponent> ParseDnComponents(string dn)
    {
        var result = new List<DnComponent>();

        foreach (var part in dn.Split(','))
        {
            var trimmed = part.Trim();
            var eq = trimmed.IndexOf('=');
            if (eq > 0)
            {
                result.Add(new DnComponent(trimmed[..eq], trimmed[(eq + 1)..]));
            }
        }

        return result;
    }

    #endregion
}

/// <summary>
/// Result of a single name translation operation.
/// </summary>
public class CrackNameResult
{
    /// <summary>
    /// Status code indicating success or the type of failure.
    /// </summary>
    public CrackNameStatus Status { get; set; }

    /// <summary>
    /// The translated name in the desired format, or null on error.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// The DNS domain name where the object was found.
    /// </summary>
    public string DnsDomainName { get; set; }
}

/// <summary>
/// Status codes for DsCrackNames results per [MS-DRSR] 4.1.4.2.
/// </summary>
public enum CrackNameStatus : uint
{
    DS_NAME_NO_ERROR = 0,
    DS_NAME_ERROR_RESOLVING = 1,
    DS_NAME_ERROR_NOT_FOUND = 2,
    DS_NAME_ERROR_NOT_UNIQUE = 3,
    DS_NAME_ERROR_NO_MAPPING = 4,
    DS_NAME_ERROR_DOMAIN_ONLY = 5,
    DS_NAME_ERROR_NO_SYNTACTICAL_MAPPING = 6,
}

/// <summary>
/// Name format identifiers per [MS-DRSR] 4.1.4.2.
/// </summary>
public enum CrackNameFormat : uint
{
    DS_UNKNOWN_NAME = 0,
    DS_FQDN_1779_NAME = 1,
    DS_NT4_ACCOUNT_NAME = 2,
    DS_DISPLAY_NAME = 3,
    DS_UNIQUE_ID_NAME = 6,
    DS_CANONICAL_NAME = 7,
    DS_USER_PRINCIPAL_NAME = 8,
    DS_CANONICAL_NAME_EX = 9,
    DS_SERVICE_PRINCIPAL_NAME = 10,
    DS_SID_OR_SID_HISTORY_NAME = 11,
    DS_DNS_DOMAIN_NAME = 12,
}

/// <summary>
/// Flags for DsCrackNames per [MS-DRSR] 4.1.4.2.
/// </summary>
[Flags]
public enum CrackNameFlags : uint
{
    DS_NAME_NO_FLAGS = 0x0,
    DS_NAME_FLAG_SYNTACTICAL_ONLY = 0x1,
    DS_NAME_FLAG_EVAL_AT_DC = 0x2,
    DS_NAME_FLAG_GCVERIFY = 0x4,
    DS_NAME_FLAG_TRUST_REFERRAL = 0x8,
}
