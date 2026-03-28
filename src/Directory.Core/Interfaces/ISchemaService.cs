using Directory.Core.Models;

namespace Directory.Core.Interfaces;

/// <summary>
/// Provides AD schema lookup and validation.
/// </summary>
public interface ISchemaService
{
    AttributeSchemaEntry GetAttribute(string name);
    ObjectClassSchemaEntry GetObjectClass(string name);
    bool ValidateObject(DirectoryObject obj, out List<string> errors);
    IReadOnlyList<AttributeSchemaEntry> GetAllAttributes();
    IReadOnlyList<ObjectClassSchemaEntry> GetAllObjectClasses();
    IReadOnlyList<string> GetGlobalCatalogAttributes();
    IReadOnlyList<ObjectClassSchemaEntry> GetAuxiliaryClasses(string structuralClassName);

    /// <summary>
    /// Collect all allowed attribute names (mustContain + mayContain) for an object class,
    /// walking the class hierarchy and including auxiliary classes.
    /// </summary>
    HashSet<string> GetAllAllowedAttributes(string objectClassName);

    /// <summary>
    /// Collect all required (mustContain) attribute names for an object class,
    /// walking the class hierarchy.
    /// </summary>
    HashSet<string> GetAllRequiredAttributes(string objectClassName);

    /// <summary>
    /// Register a custom attribute schema extension at runtime.
    /// Returns false if the attribute already exists.
    /// </summary>
    bool RegisterAttribute(AttributeSchemaEntry attribute);

    /// <summary>
    /// Register a custom object class schema extension at runtime.
    /// Returns false if the class already exists.
    /// </summary>
    bool RegisterObjectClass(ObjectClassSchemaEntry objectClass);

    /// <summary>
    /// Update an existing object class definition (e.g., to add auxiliary classes or optional attributes).
    /// Only non-built-in properties can be extended. Returns false if the class does not exist.
    /// </summary>
    bool UpdateObjectClass(string className, List<string> additionalMayHave = null, List<string> additionalAuxiliaryClasses = null);

    /// <summary>
    /// Update limited properties of an existing attribute definition (e.g., add to GC, change indexing).
    /// Returns false if the attribute does not exist.
    /// </summary>
    bool UpdateAttribute(string attributeName, bool? isInGlobalCatalog = null, bool? isIndexed = null, string description = null);

    /// <summary>
    /// Reload all schema definitions from built-in sources and the persistent store.
    /// </summary>
    Task ReloadSchemaAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets the current schema version identifier (incremented on each schema modification).
    /// </summary>
    long SchemaVersion { get; }

    /// <summary>
    /// Gets the schema USN — the highest USN of any schema modification.
    /// Used for replication tracking.
    /// </summary>
    long SchemaUsn { get; }
}

/// <summary>
/// Defines an AD attribute's schema metadata.
/// </summary>
public class AttributeSchemaEntry
{
    public string Name { get; set; } = string.Empty;
    public string LdapDisplayName { get; set; } = string.Empty;
    public string Oid { get; set; }
    public string Syntax { get; set; } = "2.5.5.12"; // Unicode String
    public bool IsSingleValued { get; set; }
    public bool IsIndexed { get; set; }
    public bool IsInGlobalCatalog { get; set; }
    public bool IsSystemOnly { get; set; }
    public int? RangeLower { get; set; }
    public int? RangeUpper { get; set; }
    public string Description { get; set; }
    public int? LinkId { get; set; }
    public SystemFlags SystemFlags { get; set; }
    public SearchFlags SearchFlags { get; set; }

    /// <summary>
    /// True if this attribute was loaded from the built-in schema (immutable).
    /// False if it was added as a custom extension.
    /// </summary>
    public bool IsBuiltIn { get; set; }

    /// <summary>
    /// The schemaIDGUID for this attribute (well-known AD GUID).
    /// Used for object-specific ACE matching on individual attributes.
    /// </summary>
    public Guid? SchemaIDGUID { get; set; }

    /// <summary>
    /// The attributeSecurityGUID (property set GUID) that this attribute belongs to.
    /// Used for property-set-level ACL checks (e.g., Personal Information, Web Information).
    /// </summary>
    public Guid? AttributeSecurityGUID { get; set; }
}

/// <summary>
/// Defines an AD object class's schema metadata.
/// </summary>
public class ObjectClassSchemaEntry
{
    public string Name { get; set; } = string.Empty;
    public string LdapDisplayName { get; set; } = string.Empty;
    public string Oid { get; set; }
    public string SuperiorClass { get; set; }
    public ObjectClassType ClassType { get; set; } = ObjectClassType.Structural;
    public List<string> MustHaveAttributes { get; set; } = [];
    public List<string> MayHaveAttributes { get; set; } = [];
    public string Description { get; set; }
    public List<string> AuxiliaryClasses { get; set; } = [];
    public List<string> PossibleSuperiors { get; set; } = [];

    /// <summary>
    /// Default security descriptor in SDDL format, applied to new instances of this class.
    /// Domain-relative SID aliases (DA, DU, etc.) are resolved at runtime using the domain SID.
    /// </summary>
    public string DefaultSecurityDescriptor { get; set; }

    /// <summary>
    /// True if this class was loaded from the built-in schema (immutable).
    /// False if it was added as a custom extension.
    /// </summary>
    public bool IsBuiltIn { get; set; }

    /// <summary>
    /// The schemaIDGUID for this object class (well-known AD GUID).
    /// Used for InheritedObjectType filtering in ACE inheritance.
    /// </summary>
    public Guid? SchemaIDGUID { get; set; }
}

public enum ObjectClassType
{
    Abstract = 0,
    Structural = 1,
    Auxiliary = 2
}
