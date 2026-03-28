using Directory.Core.Interfaces;
using Directory.Core.Models;
using Microsoft.Extensions.Logging;

namespace Directory.Security.Claims;

/// <summary>
/// Defines a claim type loaded from msDS-ClaimType schema objects per MS-ADTS section 2.7.7.
/// </summary>
public class ClaimTypeDefinition
{
    /// <summary>
    /// The claim type name (e.g., "department"). This is the LDAP display name of the msDS-ClaimType object.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// User-friendly display name.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// The source AD attribute that feeds this claim (msDS-ClaimAttributeSource).
    /// </summary>
    public string SourceAttribute { get; set; } = string.Empty;

    /// <summary>
    /// The claim value type (msDS-ClaimValueType).
    /// </summary>
    public ClaimValueType ValueType { get; set; } = ClaimValueType.STRING;

    /// <summary>
    /// Whether this claim type is disabled (msDS-ClaimTypeAppliesToClass / Enabled attribute).
    /// </summary>
    public bool IsDisabled { get; set; }

    /// <summary>
    /// The claim ID used in tokens (e.g., "ad://ext/department").
    /// </summary>
    public string ClaimId => $"ad://ext/{Name}";

    /// <summary>
    /// Object classes this claim type applies to (msDS-ClaimTypeAppliesToClass DNs).
    /// Empty means it applies to all principals.
    /// </summary>
    public List<string> AppliesToClasses { get; set; } = [];
}

/// <summary>
/// Retrieves claim type definitions from the directory.
/// </summary>
public interface IClaimTypeStore
{
    /// <summary>
    /// Gets a claim type definition by name.
    /// </summary>
    Task<ClaimTypeDefinition> GetClaimTypeAsync(string tenantId, string name, CancellationToken ct = default);

    /// <summary>
    /// Gets all enabled claim type definitions for a domain.
    /// </summary>
    Task<IReadOnlyList<ClaimTypeDefinition>> GetAllClaimTypesAsync(string tenantId, string domainDn, CancellationToken ct = default);
}

/// <summary>
/// Loads claim type definitions from msDS-ClaimType objects stored in the directory,
/// supplemented by a set of built-in claim types for common AD attributes.
/// </summary>
public class ClaimTypeStore : IClaimTypeStore
{
    private readonly IDirectoryStore _store;
    private readonly ILogger<ClaimTypeStore> _logger;

    /// <summary>
    /// Built-in claim types for common AD attributes per MS-ADTS section 2.7.7.
    /// These are always available even if no msDS-ClaimType objects are defined.
    /// </summary>
    private static readonly Dictionary<string, ClaimTypeDefinition> BuiltInClaimTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["department"] = new ClaimTypeDefinition
        {
            Name = "department",
            DisplayName = "Department",
            SourceAttribute = "department",
            ValueType = ClaimValueType.STRING
        },
        ["company"] = new ClaimTypeDefinition
        {
            Name = "company",
            DisplayName = "Company",
            SourceAttribute = "company",
            ValueType = ClaimValueType.STRING
        },
        ["title"] = new ClaimTypeDefinition
        {
            Name = "title",
            DisplayName = "Title",
            SourceAttribute = "title",
            ValueType = ClaimValueType.STRING
        },
        ["givenName"] = new ClaimTypeDefinition
        {
            Name = "givenName",
            DisplayName = "First Name",
            SourceAttribute = "givenName",
            ValueType = ClaimValueType.STRING
        },
        ["sn"] = new ClaimTypeDefinition
        {
            Name = "sn",
            DisplayName = "Last Name",
            SourceAttribute = "sn",
            ValueType = ClaimValueType.STRING
        },
        ["mail"] = new ClaimTypeDefinition
        {
            Name = "mail",
            DisplayName = "Email",
            SourceAttribute = "mail",
            ValueType = ClaimValueType.STRING
        },
        ["displayName"] = new ClaimTypeDefinition
        {
            Name = "displayName",
            DisplayName = "Display Name",
            SourceAttribute = "displayName",
            ValueType = ClaimValueType.STRING
        },
        ["manager"] = new ClaimTypeDefinition
        {
            Name = "manager",
            DisplayName = "Manager",
            SourceAttribute = "manager",
            ValueType = ClaimValueType.STRING
        },
        ["operatingSystem"] = new ClaimTypeDefinition
        {
            Name = "operatingSystem",
            DisplayName = "Operating System",
            SourceAttribute = "operatingSystem",
            ValueType = ClaimValueType.STRING,
            AppliesToClasses = ["computer"]
        },
        ["operatingSystemVersion"] = new ClaimTypeDefinition
        {
            Name = "operatingSystemVersion",
            DisplayName = "OS Version",
            SourceAttribute = "operatingSystemVersion",
            ValueType = ClaimValueType.STRING,
            AppliesToClasses = ["computer"]
        }
    };

    public ClaimTypeStore(IDirectoryStore store, ILogger<ClaimTypeStore> logger)
    {
        _store = store;
        _logger = logger;
    }

    public async Task<ClaimTypeDefinition> GetClaimTypeAsync(string tenantId, string name, CancellationToken ct = default)
    {
        if (BuiltInClaimTypes.TryGetValue(name, out var builtIn))
            return builtIn;

        // Search for msDS-ClaimType object in the schema/claims configuration container
        var filter = new AndFilterNode(
        [
            new EqualityFilterNode("objectClass", "msDS-ClaimType"),
            new EqualityFilterNode("cn", name)
        ]);

        var result = await _store.SearchAsync(
            tenantId,
            $"CN=Claim Types,CN=Claims Configuration,CN=Services,CN=Configuration",
            SearchScope.SingleLevel,
            filter,
            null,
            sizeLimit: 1,
            ct: ct);

        if (result.Entries.Count == 0)
            return null;

        return ParseClaimTypeObject(result.Entries[0]);
    }

    public async Task<IReadOnlyList<ClaimTypeDefinition>> GetAllClaimTypesAsync(
        string tenantId, string domainDn, CancellationToken ct = default)
    {
        var claimTypes = new List<ClaimTypeDefinition>(BuiltInClaimTypes.Values);

        try
        {
            var filter = new EqualityFilterNode("objectClass", "msDS-ClaimType");

            var result = await _store.SearchAsync(
                tenantId,
                $"CN=Claim Types,CN=Claims Configuration,CN=Services,CN=Configuration,{domainDn}",
                SearchScope.SingleLevel,
                filter,
                null,
                ct: ct);

            foreach (var entry in result.Entries)
            {
                var definition = ParseClaimTypeObject(entry);
                if (definition is not null && !BuiltInClaimTypes.ContainsKey(definition.Name))
                {
                    claimTypes.Add(definition);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load msDS-ClaimType objects for {TenantId}, using built-in types only", tenantId);
        }

        return claimTypes.Where(ct => !ct.IsDisabled).ToList();
    }

    private ClaimTypeDefinition ParseClaimTypeObject(DirectoryObject obj)
    {
        var name = obj.Cn;
        if (string.IsNullOrEmpty(name))
            return null;

        var definition = new ClaimTypeDefinition
        {
            Name = name,
            DisplayName = obj.DisplayName ?? name,
            SourceAttribute = obj.GetAttribute("msDS-ClaimAttributeSource")?.GetFirstString() ?? name,
            IsDisabled = string.Equals(
                obj.GetAttribute("Enabled")?.GetFirstString(), "FALSE", StringComparison.OrdinalIgnoreCase)
        };

        // Parse msDS-ClaimValueType
        var valueTypeStr = obj.GetAttribute("msDS-ClaimValueType")?.GetFirstString();
        if (valueTypeStr is not null && ushort.TryParse(valueTypeStr, out var valueType))
        {
            definition.ValueType = (ClaimValueType)valueType;
        }

        // Parse msDS-ClaimTypeAppliesToClass
        var appliesToAttr = obj.GetAttribute("msDS-ClaimTypeAppliesToClass");
        if (appliesToAttr is not null)
        {
            definition.AppliesToClasses = appliesToAttr.GetStrings().ToList();
        }

        return definition;
    }
}
