using Directory.Core.Interfaces;
using Directory.Core.Models;
using Microsoft.Extensions.Logging;

namespace Directory.Schema;

/// <summary>
/// Validates and applies schema modifications (new attributes, classes, and updates).
/// Persists custom schema extensions as directory objects under CN=Schema,CN=Configuration
/// and registers them in the in-memory SchemaService.
/// </summary>
public class SchemaModificationService
{
    private readonly ISchemaService _schema;
    private readonly IDirectoryStore _store;
    private readonly ILogger<SchemaModificationService> _logger;

    /// <summary>
    /// The tenant ID for schema storage.
    /// </summary>
    private const string SchemaTenantId = "default";

    /// <summary>
    /// Well-known AD syntax OIDs that are valid for attribute definitions.
    /// </summary>
    private static readonly HashSet<string> ValidSyntaxOids =
    [
        "2.5.5.1",  // Object(DS-DN) — Distinguished Name
        "2.5.5.2",  // String(Object-Identifier) — OID
        "2.5.5.3",  // Case-Sensitive String
        "2.5.5.4",  // Case-Insensitive String (IA5)
        "2.5.5.5",  // String(Printable) / String(IA5)
        "2.5.5.6",  // String(Numeric)
        "2.5.5.7",  // Object(DN-Binary)
        "2.5.5.8",  // Boolean
        "2.5.5.9",  // Integer
        "2.5.5.10", // String(Octet)
        "2.5.5.11", // String(Generalized-Time) / String(UTC-Time)
        "2.5.5.12", // String(Unicode)
        "2.5.5.13", // Object(Presentation-Address)
        "2.5.5.14", // Object(DN-String)
        "2.5.5.15", // String(NT-Sec-Desc) — Security Descriptor
        "2.5.5.16", // LargeInteger
        "2.5.5.17", // String(Sid) — Security Identifier
    ];

    public SchemaModificationService(ISchemaService schema, IDirectoryStore store, ILogger<SchemaModificationService> logger)
    {
        _schema = schema;
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// Validate a proposed attributeSchema definition.
    /// Returns a list of validation errors (empty if valid).
    /// </summary>
    public List<string> ValidateAttributeSchema(AttributeSchemaEntry entry)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(entry.LdapDisplayName))
            errors.Add("lDAPDisplayName is required");

        if (string.IsNullOrWhiteSpace(entry.Oid))
            errors.Add("attributeID (OID) is required");
        else if (!IsValidOidFormat(entry.Oid))
            errors.Add($"attributeID '{entry.Oid}' is not a valid OID (dotted-decimal format required)");

        if (string.IsNullOrWhiteSpace(entry.Syntax))
            errors.Add("attributeSyntax is required");
        else if (!ValidSyntaxOids.Contains(entry.Syntax))
            errors.Add($"attributeSyntax '{entry.Syntax}' is not a recognized AD syntax OID");

        // Check OID uniqueness
        if (!string.IsNullOrEmpty(entry.Oid))
        {
            var existingAttr = _schema.GetAllAttributes()
                .FirstOrDefault(a => string.Equals(a.Oid, entry.Oid, StringComparison.Ordinal));
            if (existingAttr is not null)
                errors.Add($"OID '{entry.Oid}' is already used by attribute '{existingAttr.LdapDisplayName}'");

            var existingClass = _schema.GetAllObjectClasses()
                .FirstOrDefault(c => string.Equals(c.Oid, entry.Oid, StringComparison.Ordinal));
            if (existingClass is not null)
                errors.Add($"OID '{entry.Oid}' is already used by object class '{existingClass.LdapDisplayName}'");
        }

        // Check name uniqueness
        if (!string.IsNullOrEmpty(entry.LdapDisplayName) && _schema.GetAttribute(entry.LdapDisplayName) is not null)
            errors.Add($"Attribute '{entry.LdapDisplayName}' already exists in the schema");

        // Validate range constraints
        if (entry.RangeLower.HasValue && entry.RangeUpper.HasValue && entry.RangeLower.Value > entry.RangeUpper.Value)
            errors.Add($"rangeLower ({entry.RangeLower}) cannot be greater than rangeUpper ({entry.RangeUpper})");

        return errors;
    }

    /// <summary>
    /// Validate a proposed classSchema definition.
    /// Returns a list of validation errors (empty if valid).
    /// </summary>
    public List<string> ValidateClassSchema(ObjectClassSchemaEntry entry)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(entry.LdapDisplayName))
            errors.Add("lDAPDisplayName is required");

        if (string.IsNullOrWhiteSpace(entry.Oid))
            errors.Add("governsID (OID) is required");
        else if (!IsValidOidFormat(entry.Oid))
            errors.Add($"governsID '{entry.Oid}' is not a valid OID (dotted-decimal format required)");

        // Check OID uniqueness
        if (!string.IsNullOrEmpty(entry.Oid))
        {
            var existingClass = _schema.GetAllObjectClasses()
                .FirstOrDefault(c => string.Equals(c.Oid, entry.Oid, StringComparison.Ordinal));
            if (existingClass is not null)
                errors.Add($"OID '{entry.Oid}' is already used by object class '{existingClass.LdapDisplayName}'");

            var existingAttr = _schema.GetAllAttributes()
                .FirstOrDefault(a => string.Equals(a.Oid, entry.Oid, StringComparison.Ordinal));
            if (existingAttr is not null)
                errors.Add($"OID '{entry.Oid}' is already used by attribute '{existingAttr.LdapDisplayName}'");
        }

        // Check name uniqueness
        if (!string.IsNullOrEmpty(entry.LdapDisplayName) && _schema.GetObjectClass(entry.LdapDisplayName) is not null)
            errors.Add($"Object class '{entry.LdapDisplayName}' already exists in the schema");

        // Validate superclass chain
        if (!string.IsNullOrEmpty(entry.SuperiorClass))
        {
            var superClass = _schema.GetObjectClass(entry.SuperiorClass);
            if (superClass is null)
                errors.Add($"Superior class '{entry.SuperiorClass}' does not exist in the schema");
        }
        else if (entry.ClassType != ObjectClassType.Auxiliary)
        {
            // Structural and abstract classes should typically have a superclass (except 'top')
            if (!string.Equals(entry.LdapDisplayName, "top", StringComparison.OrdinalIgnoreCase))
                errors.Add("Structural and abstract classes require a subClassOf (superior class)");
        }

        // Validate that mustContain and mayContain attributes exist
        foreach (var attrName in entry.MustHaveAttributes)
        {
            if (_schema.GetAttribute(attrName) is null)
                errors.Add($"mustContain references unknown attribute: '{attrName}'");
        }

        foreach (var attrName in entry.MayHaveAttributes)
        {
            if (_schema.GetAttribute(attrName) is null)
                errors.Add($"mayContain references unknown attribute: '{attrName}'");
        }

        // Validate auxiliary class references
        foreach (var auxName in entry.AuxiliaryClasses)
        {
            var auxClass = _schema.GetObjectClass(auxName);
            if (auxClass is null)
                errors.Add($"auxiliaryClass references unknown class: '{auxName}'");
            else if (auxClass.ClassType != ObjectClassType.Auxiliary)
                errors.Add($"auxiliaryClass '{auxName}' must be of type Auxiliary, but is {auxClass.ClassType}");
        }

        return errors;
    }

    /// <summary>
    /// Register a new attribute extension: validate, persist to CosmosDB, and add to runtime schema.
    /// </summary>
    public async Task<SchemaModificationResult> RegisterAttributeExtensionAsync(
        AttributeSchemaEntry entry, string schemaDn, CancellationToken ct = default)
    {
        var errors = ValidateAttributeSchema(entry);
        if (errors.Count > 0)
            return SchemaModificationResult.Failed(errors);

        // Persist as a directory object under the Schema container
        var dn = $"CN={entry.LdapDisplayName},{schemaDn}";
        var obj = new DirectoryObject
        {
            DistinguishedName = dn,
            TenantId = SchemaTenantId,
            Cn = entry.LdapDisplayName,
            ObjectCategory = "attributeSchema",
            ObjectClass = ["top", "attributeSchema"],
        };

        obj.SetAttribute("lDAPDisplayName", new DirectoryAttribute("lDAPDisplayName", entry.LdapDisplayName));
        obj.SetAttribute("attributeID", new DirectoryAttribute("attributeID", entry.Oid ?? string.Empty));
        obj.SetAttribute("attributeSyntax", new DirectoryAttribute("attributeSyntax", entry.Syntax));
        obj.SetAttribute("isSingleValued", new DirectoryAttribute("isSingleValued", entry.IsSingleValued ? "TRUE" : "FALSE"));
        obj.SetAttribute("isMemberOfPartialAttributeSet", new DirectoryAttribute("isMemberOfPartialAttributeSet", entry.IsInGlobalCatalog ? "TRUE" : "FALSE"));
        obj.SetAttribute("searchFlags", new DirectoryAttribute("searchFlags", entry.IsIndexed ? "1" : "0"));
        obj.SetAttribute("systemOnly", new DirectoryAttribute("systemOnly", entry.IsSystemOnly ? "TRUE" : "FALSE"));

        if (entry.RangeLower.HasValue)
            obj.SetAttribute("rangeLower", new DirectoryAttribute("rangeLower", entry.RangeLower.Value.ToString()));
        if (entry.RangeUpper.HasValue)
            obj.SetAttribute("rangeUpper", new DirectoryAttribute("rangeUpper", entry.RangeUpper.Value.ToString()));
        if (!string.IsNullOrEmpty(entry.Description))
            obj.SetAttribute("adminDescription", new DirectoryAttribute("adminDescription", entry.Description));

        try
        {
            await _store.CreateAsync(obj, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist attributeSchema object for '{Name}'", entry.LdapDisplayName);
            return SchemaModificationResult.Failed([$"Failed to persist schema object: {ex.Message}"]);
        }

        // Register in runtime schema
        if (!_schema.RegisterAttribute(entry))
        {
            _logger.LogWarning("Attribute '{Name}' was persisted but could not be registered in runtime schema (possible race condition)", entry.LdapDisplayName);
            return SchemaModificationResult.Failed([$"Attribute '{entry.LdapDisplayName}' could not be registered (may already exist)"]);
        }

        _logger.LogInformation("Successfully registered attribute extension: {Name} (OID: {Oid})", entry.LdapDisplayName, entry.Oid);
        return SchemaModificationResult.Success();
    }

    /// <summary>
    /// Register a new class extension: validate, persist to CosmosDB, and add to runtime schema.
    /// </summary>
    public async Task<SchemaModificationResult> RegisterClassExtensionAsync(
        ObjectClassSchemaEntry entry, string schemaDn, CancellationToken ct = default)
    {
        var errors = ValidateClassSchema(entry);
        if (errors.Count > 0)
            return SchemaModificationResult.Failed(errors);

        // Persist as a directory object
        var dn = $"CN={entry.LdapDisplayName},{schemaDn}";
        var obj = new DirectoryObject
        {
            DistinguishedName = dn,
            TenantId = SchemaTenantId,
            Cn = entry.LdapDisplayName,
            ObjectCategory = "classSchema",
            ObjectClass = ["top", "classSchema"],
        };

        obj.SetAttribute("lDAPDisplayName", new DirectoryAttribute("lDAPDisplayName", entry.LdapDisplayName));
        obj.SetAttribute("governsID", new DirectoryAttribute("governsID", entry.Oid ?? string.Empty));
        obj.SetAttribute("subClassOf", new DirectoryAttribute("subClassOf", entry.SuperiorClass ?? "top"));
        obj.SetAttribute("objectClassCategory", new DirectoryAttribute("objectClassCategory", ((int)entry.ClassType).ToString()));

        if (entry.MustHaveAttributes.Count > 0)
            obj.SetAttribute("mustContain", new DirectoryAttribute("mustContain", [.. entry.MustHaveAttributes.Cast<object>()]));
        if (entry.MayHaveAttributes.Count > 0)
            obj.SetAttribute("mayContain", new DirectoryAttribute("mayContain", [.. entry.MayHaveAttributes.Cast<object>()]));
        if (entry.AuxiliaryClasses.Count > 0)
            obj.SetAttribute("auxiliaryClass", new DirectoryAttribute("auxiliaryClass", [.. entry.AuxiliaryClasses.Cast<object>()]));
        if (entry.PossibleSuperiors.Count > 0)
            obj.SetAttribute("possSuperiors", new DirectoryAttribute("possSuperiors", [.. entry.PossibleSuperiors.Cast<object>()]));
        if (!string.IsNullOrEmpty(entry.Description))
            obj.SetAttribute("adminDescription", new DirectoryAttribute("adminDescription", entry.Description));

        try
        {
            await _store.CreateAsync(obj, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist classSchema object for '{Name}'", entry.LdapDisplayName);
            return SchemaModificationResult.Failed([$"Failed to persist schema object: {ex.Message}"]);
        }

        if (!_schema.RegisterObjectClass(entry))
        {
            _logger.LogWarning("Class '{Name}' was persisted but could not be registered in runtime schema", entry.LdapDisplayName);
            return SchemaModificationResult.Failed([$"Object class '{entry.LdapDisplayName}' could not be registered (may already exist)"]);
        }

        _logger.LogInformation("Successfully registered class extension: {Name} (OID: {Oid})", entry.LdapDisplayName, entry.Oid);
        return SchemaModificationResult.Success();
    }

    /// <summary>
    /// Full schema reload from directory store + built-in definitions.
    /// </summary>
    public async Task ReloadSchemaAsync(CancellationToken ct = default)
    {
        await _schema.ReloadSchemaAsync(ct);
    }

    /// <summary>
    /// Parse a DirectoryObject (from an LDAP Add of attributeSchema) into an AttributeSchemaEntry
    /// suitable for validation and registration.
    /// </summary>
    public static AttributeSchemaEntry ParseAttributeSchemaFromDirectoryObject(DirectoryObject obj)
    {
        var ldapDisplayName = obj.GetAttribute("lDAPDisplayName")?.GetFirstString()
                              ?? obj.Cn
                              ?? string.Empty;

        var oid = obj.GetAttribute("attributeID")?.GetFirstString() ?? string.Empty;
        var syntax = obj.GetAttribute("attributeSyntax")?.GetFirstString() ?? "2.5.5.12";
        var isSingleValued = string.Equals(obj.GetAttribute("isSingleValued")?.GetFirstString(), "TRUE", StringComparison.OrdinalIgnoreCase);
        var isGc = string.Equals(obj.GetAttribute("isMemberOfPartialAttributeSet")?.GetFirstString(), "TRUE", StringComparison.OrdinalIgnoreCase);
        var isSystemOnly = string.Equals(obj.GetAttribute("systemOnly")?.GetFirstString(), "TRUE", StringComparison.OrdinalIgnoreCase);
        var description = obj.GetAttribute("adminDescription")?.GetFirstString();

        int? rangeLower = null, rangeUpper = null;
        if (int.TryParse(obj.GetAttribute("rangeLower")?.GetFirstString(), out var rl))
            rangeLower = rl;
        if (int.TryParse(obj.GetAttribute("rangeUpper")?.GetFirstString(), out var ru))
            rangeUpper = ru;

        var searchFlagsStr = obj.GetAttribute("searchFlags")?.GetFirstString();
        var isIndexed = searchFlagsStr == "1" || string.Equals(searchFlagsStr, "TRUE", StringComparison.OrdinalIgnoreCase);

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

    /// <summary>
    /// Parse a DirectoryObject (from an LDAP Add of classSchema) into an ObjectClassSchemaEntry
    /// suitable for validation and registration.
    /// </summary>
    public static ObjectClassSchemaEntry ParseClassSchemaFromDirectoryObject(DirectoryObject obj)
    {
        var ldapDisplayName = obj.GetAttribute("lDAPDisplayName")?.GetFirstString()
                              ?? obj.Cn
                              ?? string.Empty;

        var oid = obj.GetAttribute("governsID")?.GetFirstString() ?? string.Empty;
        var superiorClass = obj.GetAttribute("subClassOf")?.GetFirstString();
        var classTypeStr = obj.GetAttribute("objectClassCategory")?.GetFirstString() ?? "1";
        var classType = classTypeStr switch
        {
            "0" => ObjectClassType.Abstract,
            "2" => ObjectClassType.Auxiliary,
            _ => ObjectClassType.Structural,
        };

        var mustContain = obj.GetAttribute("mustContain")?.GetStrings().ToList() ?? [];
        var mayContain = obj.GetAttribute("mayContain")?.GetStrings().ToList() ?? [];
        var auxiliaryClasses = obj.GetAttribute("auxiliaryClass")?.GetStrings().ToList() ?? [];
        var possSuperiors = obj.GetAttribute("possSuperiors")?.GetStrings().ToList() ?? [];
        var description = obj.GetAttribute("adminDescription")?.GetFirstString();

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

    private static bool IsValidOidFormat(string oid)
    {
        if (string.IsNullOrEmpty(oid))
            return false;

        var parts = oid.Split('.');
        if (parts.Length < 2)
            return false;

        foreach (var part in parts)
        {
            if (string.IsNullOrEmpty(part) || !part.All(char.IsDigit))
                return false;
        }

        return true;
    }
}

/// <summary>
/// Result of a schema modification operation.
/// </summary>
public class SchemaModificationResult
{
    public bool IsSuccess { get; init; }
    public List<string> Errors { get; init; } = [];

    public static SchemaModificationResult Success() => new() { IsSuccess = true };
    public static SchemaModificationResult Failed(List<string> errors) => new() { IsSuccess = false, Errors = errors };
}
