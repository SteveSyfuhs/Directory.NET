using Directory.Core.Interfaces;
using Directory.Core.Models;
using Microsoft.Extensions.Logging;

namespace Directory.Schema;

/// <summary>
/// Provides AD schema lookup and validation using built-in schema definitions
/// combined with custom schema extensions loaded from the directory store.
/// Supports runtime registration of new attributes and classes.
/// </summary>
public class SchemaService : ISchemaService
{
    private readonly IDirectoryStore _store;
    private readonly ILogger<SchemaService> _logger;
    private readonly object _lock = new();

    private Dictionary<string, AttributeSchemaEntry> _attributes = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, ObjectClassSchemaEntry> _objectClasses = new(StringComparer.OrdinalIgnoreCase);
    private List<string> _gcAttributes = [];

    // OID lookup indexes for uniqueness checks
    private Dictionary<string, string> _attributeOidIndex = new(StringComparer.Ordinal);
    private Dictionary<string, string> _classOidIndex = new(StringComparer.Ordinal);

    private long _schemaVersion;
    private long _schemaUsn;

    /// <summary>
    /// The tenant ID used to load/store custom schema extensions.
    /// </summary>
    private const string SchemaTenantId = "default";

    /// <summary>
    /// The DN path under which custom attributeSchema objects are stored.
    /// </summary>
    private const string SchemaContainerSuffix = "CN=Schema,CN=Configuration";

    public long SchemaVersion => Interlocked.Read(ref _schemaVersion);
    public long SchemaUsn => Interlocked.Read(ref _schemaUsn);

    public SchemaService(IDirectoryStore store, ILogger<SchemaService> logger)
    {
        _store = store;
        _logger = logger;

        // Load built-in schema synchronously at construction time
        LoadBuiltInSchema();
    }

    /// <summary>
    /// Parameterless constructor for backward compatibility (no store = built-in only).
    /// </summary>
    public SchemaService()
        : this(NullDirectoryStore.Instance, NullLogger<SchemaService>.Instance)
    {
    }

    private void LoadBuiltInSchema()
    {
        lock (_lock)
        {
            _attributes = new Dictionary<string, AttributeSchemaEntry>(StringComparer.OrdinalIgnoreCase);
            _objectClasses = new Dictionary<string, ObjectClassSchemaEntry>(StringComparer.OrdinalIgnoreCase);
            _attributeOidIndex = new Dictionary<string, string>(StringComparer.Ordinal);
            _classOidIndex = new Dictionary<string, string>(StringComparer.Ordinal);

            // Load from BuiltInSchema (highest priority)
            foreach (var attr in BuiltInSchema.GetAttributes())
            {
                attr.IsBuiltIn = true;
                _attributes[attr.LdapDisplayName] = attr;
                if (!string.IsNullOrEmpty(attr.Oid))
                    _attributeOidIndex[attr.Oid] = attr.LdapDisplayName;
            }

            // Aggregate additional schema attribute files
            foreach (var attr in SchemaAttributes_A_L.GetAttributes())
            {
                attr.IsBuiltIn = true;
                if (_attributes.TryAdd(attr.LdapDisplayName, attr) && !string.IsNullOrEmpty(attr.Oid))
                    _attributeOidIndex.TryAdd(attr.Oid, attr.LdapDisplayName);
            }
            foreach (var attr in SchemaAttributes_M.GetAttributes())
            {
                attr.IsBuiltIn = true;
                if (_attributes.TryAdd(attr.LdapDisplayName, attr) && !string.IsNullOrEmpty(attr.Oid))
                    _attributeOidIndex.TryAdd(attr.Oid, attr.LdapDisplayName);
            }
            foreach (var attr in SchemaAttributes_N_Z.GetAttributes())
            {
                attr.IsBuiltIn = true;
                if (_attributes.TryAdd(attr.LdapDisplayName, attr) && !string.IsNullOrEmpty(attr.Oid))
                    _attributeOidIndex.TryAdd(attr.Oid, attr.LdapDisplayName);
            }

            // Load built-in classes
            foreach (var cls in BuiltInSchema.GetObjectClasses())
            {
                cls.IsBuiltIn = true;
                _objectClasses[cls.LdapDisplayName] = cls;
                if (!string.IsNullOrEmpty(cls.Oid))
                    _classOidIndex[cls.Oid] = cls.LdapDisplayName;
            }
            foreach (var cls in SchemaClasses_Full.GetObjectClasses())
            {
                cls.IsBuiltIn = true;
                if (_objectClasses.TryAdd(cls.LdapDisplayName, cls) && !string.IsNullOrEmpty(cls.Oid))
                    _classOidIndex.TryAdd(cls.Oid, cls.LdapDisplayName);
            }

            RebuildGcAttributesList();
        }
    }

    /// <summary>
    /// Reload schema from both built-in definitions and the persistent directory store.
    /// Called after schema modifications or on explicit schemaUpdateNow trigger.
    /// </summary>
    public async Task ReloadSchemaAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Reloading schema from built-in definitions and directory store");

        // Start from built-in
        LoadBuiltInSchema();

        // Load custom extensions from the store
        await LoadCustomSchemaExtensionsAsync(ct);

        Interlocked.Increment(ref _schemaVersion);
        _logger.LogInformation("Schema reload complete. Version={Version}, Attributes={AttrCount}, Classes={ClassCount}",
            SchemaVersion, _attributes.Count, _objectClasses.Count);
    }

    private async Task LoadCustomSchemaExtensionsAsync(CancellationToken ct)
    {
        try
        {
            // Search for attributeSchema objects under the Schema container
            var attrResult = await _store.SearchAsync(
                SchemaTenantId,
                baseDn: string.Empty, // Will match any DN ending with Schema,Configuration
                SearchScope.WholeSubtree,
                filter: new EqualityFilterNode("objectClass", "attributeSchema"),
                attributes: null,
                sizeLimit: 10000,
                ct: ct);

            foreach (var entry in attrResult.Entries)
            {
                var attr = DirectoryObjectToAttributeSchema(entry);
                if (attr is not null)
                {
                    lock (_lock)
                    {
                        // Custom extensions don't override built-in definitions
                        if (!_attributes.ContainsKey(attr.LdapDisplayName))
                        {
                            _attributes[attr.LdapDisplayName] = attr;
                            if (!string.IsNullOrEmpty(attr.Oid))
                                _attributeOidIndex.TryAdd(attr.Oid, attr.LdapDisplayName);
                        }
                    }
                }
            }

            // Search for classSchema objects
            var classResult = await _store.SearchAsync(
                SchemaTenantId,
                baseDn: string.Empty,
                SearchScope.WholeSubtree,
                filter: new EqualityFilterNode("objectClass", "classSchema"),
                attributes: null,
                sizeLimit: 10000,
                ct: ct);

            foreach (var entry in classResult.Entries)
            {
                var cls = DirectoryObjectToClassSchema(entry);
                if (cls is not null)
                {
                    lock (_lock)
                    {
                        if (!_objectClasses.ContainsKey(cls.LdapDisplayName))
                        {
                            _objectClasses[cls.LdapDisplayName] = cls;
                            if (!string.IsNullOrEmpty(cls.Oid))
                                _classOidIndex.TryAdd(cls.Oid, cls.LdapDisplayName);
                        }
                    }
                }
            }

            lock (_lock)
            {
                RebuildGcAttributesList();
            }

            // Track the highest USN from custom schema objects
            var maxUsn = attrResult.Entries
                .Concat(classResult.Entries)
                .Select(e => e.USNChanged)
                .DefaultIfEmpty(0)
                .Max();

            if (maxUsn > Interlocked.Read(ref _schemaUsn))
                Interlocked.Exchange(ref _schemaUsn, maxUsn);

            _logger.LogDebug("Loaded {AttrCount} custom attributes and {ClassCount} custom classes from store",
                attrResult.Entries.Count, classResult.Entries.Count);
        }
        catch (Exception ex)
        {
            // Schema extension loading is best-effort — the service works with built-in schema alone
            _logger.LogWarning(ex, "Failed to load custom schema extensions from directory store. Using built-in schema only.");
        }
    }

    public AttributeSchemaEntry GetAttribute(string name)
    {
        lock (_lock)
        {
            _attributes.TryGetValue(name, out var entry);
            return entry;
        }
    }

    public ObjectClassSchemaEntry GetObjectClass(string name)
    {
        lock (_lock)
        {
            _objectClasses.TryGetValue(name, out var entry);
            return entry;
        }
    }

    public bool ValidateObject(DirectoryObject obj, out List<string> errors)
    {
        errors = [];

        if (obj.ObjectClass.Count == 0)
        {
            errors.Add("objectClass is required");
            return false;
        }

        var structuralClassName = obj.ObjectClass[^1];
        var structuralClass = GetObjectClass(structuralClassName);

        if (structuralClass is null)
        {
            errors.Add($"Unknown object class: {structuralClassName}");
            return false;
        }

        var mustHave = GetAllMustHaveAttributes(structuralClass);

        foreach (var required in mustHave)
        {
            if (required.Equals("objectClass", StringComparison.OrdinalIgnoreCase))
                continue;

            var attr = obj.GetAttribute(required);
            if (attr is null || attr.Values.Count == 0)
            {
                errors.Add($"Missing required attribute: {required}");
            }
        }

        // Validate single-valued constraints
        foreach (var (attrName, attr) in obj.Attributes)
        {
            var schema = GetAttribute(attrName);
            if (schema is not null && schema.IsSingleValued && attr.Values.Count > 1)
            {
                errors.Add($"Attribute {attrName} is single-valued but has {attr.Values.Count} values");
            }
        }

        return errors.Count == 0;
    }

    public IReadOnlyList<AttributeSchemaEntry> GetAllAttributes()
    {
        lock (_lock)
        {
            return [.. _attributes.Values];
        }
    }

    public IReadOnlyList<ObjectClassSchemaEntry> GetAllObjectClasses()
    {
        lock (_lock)
        {
            return [.. _objectClasses.Values];
        }
    }

    public IReadOnlyList<string> GetGlobalCatalogAttributes()
    {
        lock (_lock)
        {
            return _gcAttributes;
        }
    }

    public IReadOnlyList<ObjectClassSchemaEntry> GetAuxiliaryClasses(string structuralClassName)
    {
        lock (_lock)
        {
            return _objectClasses.Values
                .Where(c => c.ClassType == ObjectClassType.Auxiliary)
                .ToList();
        }
    }

    public HashSet<string> GetAllRequiredAttributes(string objectClassName)
    {
        var objClass = GetObjectClass(objectClassName);
        if (objClass is null) return [];
        return GetAllMustHaveAttributes(objClass);
    }

    public HashSet<string> GetAllAllowedAttributes(string objectClassName)
    {
        var objClass = GetObjectClass(objectClassName);
        if (objClass is null) return [];

        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var current = objClass;
        while (current is not null)
        {
            foreach (var attr in current.MustHaveAttributes)
                allowed.Add(attr);
            foreach (var attr in current.MayHaveAttributes)
                allowed.Add(attr);

            foreach (var auxName in current.AuxiliaryClasses)
            {
                var aux = GetObjectClass(auxName);
                if (aux is not null)
                {
                    foreach (var attr in aux.MustHaveAttributes)
                        allowed.Add(attr);
                    foreach (var attr in aux.MayHaveAttributes)
                        allowed.Add(attr);
                }
            }

            if (current.SuperiorClass is not null)
                current = GetObjectClass(current.SuperiorClass);
            else
                current = null;
        }

        foreach (var aux in GetAuxiliaryClasses(objectClassName))
        {
            foreach (var attr in aux.MustHaveAttributes)
                allowed.Add(attr);
            foreach (var attr in aux.MayHaveAttributes)
                allowed.Add(attr);
        }

        return allowed;
    }

    public bool RegisterAttribute(AttributeSchemaEntry attribute)
    {
        lock (_lock)
        {
            if (_attributes.ContainsKey(attribute.LdapDisplayName))
                return false;

            if (!string.IsNullOrEmpty(attribute.Oid) && _attributeOidIndex.ContainsKey(attribute.Oid))
                return false;

            attribute.IsBuiltIn = false;
            _attributes[attribute.LdapDisplayName] = attribute;

            if (!string.IsNullOrEmpty(attribute.Oid))
                _attributeOidIndex[attribute.Oid] = attribute.LdapDisplayName;

            RebuildGcAttributesList();
            Interlocked.Increment(ref _schemaVersion);

            _logger.LogInformation("Registered custom attribute: {Name} (OID: {Oid})", attribute.LdapDisplayName, attribute.Oid);
            return true;
        }
    }

    public bool RegisterObjectClass(ObjectClassSchemaEntry objectClass)
    {
        lock (_lock)
        {
            if (_objectClasses.ContainsKey(objectClass.LdapDisplayName))
                return false;

            if (!string.IsNullOrEmpty(objectClass.Oid) && _classOidIndex.ContainsKey(objectClass.Oid))
                return false;

            objectClass.IsBuiltIn = false;
            _objectClasses[objectClass.LdapDisplayName] = objectClass;

            if (!string.IsNullOrEmpty(objectClass.Oid))
                _classOidIndex[objectClass.Oid] = objectClass.LdapDisplayName;

            Interlocked.Increment(ref _schemaVersion);

            _logger.LogInformation("Registered custom object class: {Name} (OID: {Oid})", objectClass.LdapDisplayName, objectClass.Oid);
            return true;
        }
    }

    public bool UpdateObjectClass(string className, List<string> additionalMayHave = null, List<string> additionalAuxiliaryClasses = null)
    {
        lock (_lock)
        {
            if (!_objectClasses.TryGetValue(className, out var existing))
                return false;

            if (additionalMayHave is not null)
            {
                foreach (var attr in additionalMayHave)
                {
                    if (!existing.MayHaveAttributes.Contains(attr, StringComparer.OrdinalIgnoreCase))
                        existing.MayHaveAttributes.Add(attr);
                }
            }

            if (additionalAuxiliaryClasses is not null)
            {
                foreach (var aux in additionalAuxiliaryClasses)
                {
                    if (!existing.AuxiliaryClasses.Contains(aux, StringComparer.OrdinalIgnoreCase))
                        existing.AuxiliaryClasses.Add(aux);
                }
            }

            Interlocked.Increment(ref _schemaVersion);
            _logger.LogInformation("Updated object class: {Name}", className);
            return true;
        }
    }

    public bool UpdateAttribute(string attributeName, bool? isInGlobalCatalog = null, bool? isIndexed = null, string description = null)
    {
        lock (_lock)
        {
            if (!_attributes.TryGetValue(attributeName, out var existing))
                return false;

            if (isInGlobalCatalog.HasValue)
                existing.IsInGlobalCatalog = isInGlobalCatalog.Value;

            if (isIndexed.HasValue)
                existing.IsIndexed = isIndexed.Value;

            if (description is not null)
                existing.Description = description;

            RebuildGcAttributesList();
            Interlocked.Increment(ref _schemaVersion);
            _logger.LogInformation("Updated attribute: {Name}", attributeName);
            return true;
        }
    }

    /// <summary>
    /// Check whether an OID is already in use by an attribute or class.
    /// </summary>
    public bool IsOidInUse(string oid)
    {
        lock (_lock)
        {
            return _attributeOidIndex.ContainsKey(oid) || _classOidIndex.ContainsKey(oid);
        }
    }

    private HashSet<string> GetAllMustHaveAttributes(ObjectClassSchemaEntry objectClass)
    {
        var mustHave = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var current = objectClass;
        while (current is not null)
        {
            foreach (var attr in current.MustHaveAttributes)
                mustHave.Add(attr);

            if (current.SuperiorClass is not null)
                current = GetObjectClass(current.SuperiorClass);
            else
                current = null;
        }

        return mustHave;
    }

    private void RebuildGcAttributesList()
    {
        _gcAttributes = _attributes.Values
            .Where(a => a.IsInGlobalCatalog)
            .Select(a => a.LdapDisplayName)
            .ToList();
    }

    // --- Conversion helpers: DirectoryObject <-> Schema entries ---

    private static AttributeSchemaEntry DirectoryObjectToAttributeSchema(DirectoryObject obj)
    {
        var ldapDisplayName = obj.GetAttribute("lDAPDisplayName")?.GetFirstString()
                              ?? obj.GetAttribute("cn")?.GetFirstString()
                              ?? obj.Cn;
        if (string.IsNullOrEmpty(ldapDisplayName))
            return null;

        var oid = obj.GetAttribute("attributeID")?.GetFirstString();
        var syntax = obj.GetAttribute("attributeSyntax")?.GetFirstString() ?? "2.5.5.12";
        var isSingleValued = string.Equals(obj.GetAttribute("isSingleValued")?.GetFirstString(), "TRUE", StringComparison.OrdinalIgnoreCase);
        var isGc = string.Equals(obj.GetAttribute("isMemberOfPartialAttributeSet")?.GetFirstString(), "TRUE", StringComparison.OrdinalIgnoreCase);
        var isIndexed = string.Equals(obj.GetAttribute("searchFlags")?.GetFirstString(), "1", StringComparison.Ordinal);
        var isSystemOnly = string.Equals(obj.GetAttribute("systemOnly")?.GetFirstString(), "TRUE", StringComparison.OrdinalIgnoreCase);
        var description = obj.GetAttribute("adminDescription")?.GetFirstString() ?? obj.Description;

        int? rangeLower = null, rangeUpper = null;
        if (int.TryParse(obj.GetAttribute("rangeLower")?.GetFirstString(), out var rl))
            rangeLower = rl;
        if (int.TryParse(obj.GetAttribute("rangeUpper")?.GetFirstString(), out var ru))
            rangeUpper = ru;

        return new AttributeSchemaEntry
        {
            Name = ldapDisplayName,
            LdapDisplayName = ldapDisplayName,
            Oid = oid,
            Syntax = syntax,
            IsSingleValued = isSingleValued,
            IsInGlobalCatalog = isGc,
            IsIndexed = isIndexed,
            IsSystemOnly = isSystemOnly,
            Description = description,
            RangeLower = rangeLower,
            RangeUpper = rangeUpper,
            IsBuiltIn = false,
        };
    }

    private static ObjectClassSchemaEntry DirectoryObjectToClassSchema(DirectoryObject obj)
    {
        var ldapDisplayName = obj.GetAttribute("lDAPDisplayName")?.GetFirstString()
                              ?? obj.GetAttribute("cn")?.GetFirstString()
                              ?? obj.Cn;
        if (string.IsNullOrEmpty(ldapDisplayName))
            return null;

        var oid = obj.GetAttribute("governsID")?.GetFirstString();
        var superiorClass = obj.GetAttribute("subClassOf")?.GetFirstString();
        var classTypeStr = obj.GetAttribute("objectClassCategory")?.GetFirstString() ?? "1";
        var classType = classTypeStr switch
        {
            "0" => ObjectClassType.Abstract,
            "2" => ObjectClassType.Auxiliary,
            _ => ObjectClassType.Structural,
        };

        var mustContain = obj.GetAttribute("mustContain")?.GetStrings().ToList() ?? [];
        var systemMust = obj.GetAttribute("systemMustContain")?.GetStrings().ToList() ?? [];
        mustContain.AddRange(systemMust);

        var mayContain = obj.GetAttribute("mayContain")?.GetStrings().ToList() ?? [];
        var systemMay = obj.GetAttribute("systemMayContain")?.GetStrings().ToList() ?? [];
        mayContain.AddRange(systemMay);

        var auxiliaryClasses = obj.GetAttribute("auxiliaryClass")?.GetStrings().ToList() ?? [];
        var systemAux = obj.GetAttribute("systemAuxiliaryClass")?.GetStrings().ToList() ?? [];
        auxiliaryClasses.AddRange(systemAux);

        var possSuperiors = obj.GetAttribute("possSuperiors")?.GetStrings().ToList() ?? [];
        var description = obj.GetAttribute("adminDescription")?.GetFirstString() ?? obj.Description;

        return new ObjectClassSchemaEntry
        {
            Name = ldapDisplayName,
            LdapDisplayName = ldapDisplayName,
            Oid = oid,
            SuperiorClass = superiorClass,
            ClassType = classType,
            MustHaveAttributes = mustContain,
            MayHaveAttributes = mayContain,
            AuxiliaryClasses = auxiliaryClasses,
            PossibleSuperiors = possSuperiors,
            Description = description,
            IsBuiltIn = false,
        };
    }
}

/// <summary>
/// No-op directory store for backward compatibility when no store is configured.
/// </summary>
internal sealed class NullDirectoryStore : IDirectoryStore
{
    public static readonly NullDirectoryStore Instance = new();

    public Task<DirectoryObject> GetByDnAsync(string tenantId, string dn, CancellationToken ct = default) => Task.FromResult<DirectoryObject>(null);
    public Task<DirectoryObject> GetByGuidAsync(string tenantId, string guid, CancellationToken ct = default) => Task.FromResult<DirectoryObject>(null);
    public Task<DirectoryObject> GetBySamAccountNameAsync(string tenantId, string domainDn, string samAccountName, CancellationToken ct = default) => Task.FromResult<DirectoryObject>(null);
    public Task<DirectoryObject> GetByUpnAsync(string tenantId, string upn, CancellationToken ct = default) => Task.FromResult<DirectoryObject>(null);
    public Task<SearchResult> SearchAsync(string tenantId, string baseDn, SearchScope scope, FilterNode filter, string[] attributes, int sizeLimit = 0, int timeLimitSeconds = 0, string continuationToken = null, int pageSize = 1000, bool includeDeleted = false, CancellationToken ct = default) => Task.FromResult(new SearchResult());
    public Task CreateAsync(DirectoryObject obj, CancellationToken ct = default) => Task.CompletedTask;
    public Task UpdateAsync(DirectoryObject obj, CancellationToken ct = default) => Task.CompletedTask;
    public Task DeleteAsync(string tenantId, string dn, bool hardDelete = false, CancellationToken ct = default) => Task.CompletedTask;
    public Task MoveAsync(string tenantId, string oldDn, string newDn, CancellationToken ct = default) => Task.CompletedTask;
    public Task<long> GetNextUsnAsync(string tenantId, string domainDn, CancellationToken ct = default) => Task.FromResult(0L);
    public Task<long> ClaimRidPoolAsync(string tenantId, string domainDn, int poolSize, CancellationToken ct = default) => Task.FromResult(0L);
    public Task<IReadOnlyList<DirectoryObject>> GetChildrenAsync(string tenantId, string parentDn, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<DirectoryObject>>([]);
    public Task<IReadOnlyList<DirectoryObject>> GetByServicePrincipalNameAsync(string tenantId, string spn, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<DirectoryObject>>([]);
    public Task<IReadOnlyList<DirectoryObject>> GetByDnsAsync(string tenantId, IEnumerable<string> dns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<DirectoryObject>>([]);
}

/// <summary>
/// Null logger implementation for the parameterless constructor path.
/// </summary>
internal sealed class NullLogger<T> : ILogger<T>
{
    public static readonly NullLogger<T> Instance = new();
    public IDisposable BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => false;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter) { }
}
