using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Ldap.Server;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Directory.Ldap.Handlers;

/// <summary>
/// Provides virtual (computed/operational) attributes that are generated on-the-fly
/// rather than stored in the directory. These attributes are returned in search results
/// when explicitly requested in the attribute list, or when "+" (all operational attributes)
/// is requested.
///
/// Implements the <see cref="IConstructedAttributeService"/> interface so it integrates
/// with the existing SearchHandler attribute computation pipeline.
///
/// Supported virtual attributes:
///   - hasSubordinates       : TRUE if the object has child objects
///   - numSubordinates       : count of immediate children
///   - subordinateCount      : alias for numSubordinates
///   - subschemaSubentry     : DN of the subschema entry
///   - structuralObjectClass : the structural (most-derived) object class
///   - entryDN               : the entry's own DN
///   - entryUUID             : alias for objectGUID
///   - createTimestamp       : object creation timestamp (generalized time)
///   - modifyTimestamp        : object last modified timestamp (generalized time)
///   - creatorsName          : DN of the principal that created the object
///   - modifiersName         : DN of the principal that last modified the object
/// </summary>
public class VirtualAttributeProvider : IConstructedAttributeService
{
    private readonly IDirectoryStore _store;
    private readonly IOptionsMonitor<LdapServerOptions> _optionsMonitor;
    private readonly ILogger<VirtualAttributeProvider> _logger;

    /// <summary>
    /// The set of virtual attribute names this provider handles (case-insensitive lookup).
    /// </summary>
    private static readonly Dictionary<string, bool> VirtualAttributes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["hasSubordinates"] = true,
            ["numSubordinates"] = true,
            ["subordinateCount"] = true,
            ["subschemaSubentry"] = true,
            ["structuralObjectClass"] = true,
            ["entryDN"] = true,
            ["entryUUID"] = true,
            ["createTimestamp"] = true,
            ["modifyTimestamp"] = true,
            ["creatorsName"] = true,
            ["modifiersName"] = true,
        };

    private static readonly IReadOnlyList<string> AttributeNames = [.. VirtualAttributes.Keys];

    public VirtualAttributeProvider(
        IDirectoryStore store,
        IOptionsMonitor<LdapServerOptions> optionsMonitor,
        ILogger<VirtualAttributeProvider> logger)
    {
        _store = store;
        _optionsMonitor = optionsMonitor;
        _logger = logger;
    }

    private LdapServerOptions Options => _optionsMonitor.CurrentValue;

    /// <inheritdoc />
    public bool IsConstructedAttribute(string attributeName)
    {
        return VirtualAttributes.ContainsKey(attributeName);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetConstructedAttributeNames()
    {
        return AttributeNames;
    }

    /// <inheritdoc />
    public async Task<DirectoryAttribute> ComputeAttributeAsync(
        string attributeName,
        DirectoryObject obj,
        string tenantId,
        CancellationToken ct = default)
    {
        return attributeName.ToLowerInvariant() switch
        {
            "hassubordinates" => await ComputeHasSubordinatesAsync(obj, tenantId, ct),
            "numsubordinates" or "subordinatecount" => await ComputeNumSubordinatesAsync(attributeName, obj, tenantId, ct),
            "subschemasubentry" => ComputeSubschemaSubentry(),
            "structuralobjectclass" => ComputeStructuralObjectClass(obj),
            "entrydn" => ComputeEntryDn(obj),
            "entryuuid" => ComputeEntryUuid(obj),
            "createtimestamp" => ComputeCreateTimestamp(obj),
            "modifytimestamp" => ComputeModifyTimestamp(obj),
            "creatorsname" => ComputeCreatorsName(obj),
            "modifiersname" => ComputeModifiersName(obj),
            _ => null,
        };
    }

    /// <summary>
    /// hasSubordinates: TRUE if the object has any child objects.
    /// Per RFC 3045 / X.501 2.6.
    /// </summary>
    private async Task<DirectoryAttribute> ComputeHasSubordinatesAsync(
        DirectoryObject obj, string tenantId, CancellationToken ct)
    {
        try
        {
            var children = await _store.GetChildrenAsync(tenantId, obj.DistinguishedName, ct);
            var hasChildren = children.Count > 0;
            return new DirectoryAttribute("hasSubordinates", hasChildren ? "TRUE" : "FALSE");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error computing hasSubordinates for {DN}", obj.DistinguishedName);
            return new DirectoryAttribute("hasSubordinates", "FALSE");
        }
    }

    /// <summary>
    /// numSubordinates / subordinateCount: count of immediate child objects.
    /// </summary>
    private async Task<DirectoryAttribute> ComputeNumSubordinatesAsync(
        string attributeName, DirectoryObject obj, string tenantId, CancellationToken ct)
    {
        try
        {
            var children = await _store.GetChildrenAsync(tenantId, obj.DistinguishedName, ct);
            return new DirectoryAttribute(attributeName, children.Count.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error computing {Attr} for {DN}", attributeName, obj.DistinguishedName);
            return new DirectoryAttribute(attributeName, "0");
        }
    }

    /// <summary>
    /// subschemaSubentry: DN of the subschema entry.
    /// Always points to cn=Aggregate,cn=Schema,cn=Configuration,{domainDN}.
    /// Per RFC 4512 section 4.2.
    /// </summary>
    private DirectoryAttribute ComputeSubschemaSubentry()
    {
        var domainDn = Options.DefaultDomain;
        var configDn = $"CN=Configuration,{domainDn}";
        var schemaDn = $"CN=Schema,{configDn}";
        var subschemaEntry = $"CN=Aggregate,{schemaDn}";
        return new DirectoryAttribute("subschemaSubentry", subschemaEntry);
    }

    /// <summary>
    /// structuralObjectClass: the most-derived (structural) object class.
    /// This is the last entry in the objectClass attribute list.
    /// Per RFC 4512 section 2.4.
    /// </summary>
    private static DirectoryAttribute ComputeStructuralObjectClass(DirectoryObject obj)
    {
        // The structural class is the last (most specific) in the objectClass hierarchy
        var structuralClass = obj.ObjectClass.Count > 0
            ? obj.ObjectClass[^1]
            : "top";
        return new DirectoryAttribute("structuralObjectClass", structuralClass);
    }

    /// <summary>
    /// entryDN: the entry's own distinguished name.
    /// Useful when the client needs the DN as an attribute value (e.g., in search result processing).
    /// Per draft-zeilenga-ldap-entrydn.
    /// </summary>
    private static DirectoryAttribute ComputeEntryDn(DirectoryObject obj)
    {
        return new DirectoryAttribute("entryDN", obj.DistinguishedName);
    }

    /// <summary>
    /// entryUUID: globally unique identifier for the entry.
    /// Alias for objectGUID to provide LDAP standard compatibility.
    /// Per RFC 4530.
    /// </summary>
    private static DirectoryAttribute ComputeEntryUuid(DirectoryObject obj)
    {
        return new DirectoryAttribute("entryUUID", obj.ObjectGuid);
    }

    /// <summary>
    /// createTimestamp: creation time of the object in generalized time format.
    /// Per RFC 4512 section 3.4.
    /// </summary>
    private static DirectoryAttribute ComputeCreateTimestamp(DirectoryObject obj)
    {
        var timestamp = obj.WhenCreated.UtcDateTime.ToString("yyyyMMddHHmmss") + "Z";
        return new DirectoryAttribute("createTimestamp", timestamp);
    }

    /// <summary>
    /// modifyTimestamp: last modification time of the object in generalized time format.
    /// Per RFC 4512 section 3.4.
    /// </summary>
    private static DirectoryAttribute ComputeModifyTimestamp(DirectoryObject obj)
    {
        var timestamp = obj.WhenChanged.UtcDateTime.ToString("yyyyMMddHHmmss") + "Z";
        return new DirectoryAttribute("modifyTimestamp", timestamp);
    }

    /// <summary>
    /// creatorsName: DN of the principal that created the object.
    /// If the directory stores this information in the attributes dictionary, use it.
    /// Otherwise, fall back to a synthetic value.
    /// </summary>
    private static DirectoryAttribute ComputeCreatorsName(DirectoryObject obj)
    {
        // Check if the object has a stored creatorsName/createdBy attribute
        var stored = obj.Attributes.GetValueOrDefault("creatorsName")
                    ?? obj.Attributes.GetValueOrDefault("createdBy");
        if (stored != null && stored.Values.Count > 0)
        {
            return new DirectoryAttribute("creatorsName", stored.Values[0]);
        }

        // Default to empty (unknown creator)
        return new DirectoryAttribute("creatorsName", "");
    }

    /// <summary>
    /// modifiersName: DN of the principal that last modified the object.
    /// If the directory stores this information in the attributes dictionary, use it.
    /// Otherwise, fall back to a synthetic value.
    /// </summary>
    private static DirectoryAttribute ComputeModifiersName(DirectoryObject obj)
    {
        // Check if the object has a stored modifiersName/modifiedBy attribute
        var stored = obj.Attributes.GetValueOrDefault("modifiersName")
                    ?? obj.Attributes.GetValueOrDefault("modifiedBy");
        if (stored != null && stored.Values.Count > 0)
        {
            return new DirectoryAttribute("modifiersName", stored.Values[0]);
        }

        // Default to empty (unknown modifier)
        return new DirectoryAttribute("modifiersName", "");
    }
}
