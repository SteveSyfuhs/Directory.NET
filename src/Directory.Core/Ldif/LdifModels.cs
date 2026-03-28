using Directory.Core.Models;

namespace Directory.Core.Ldif;

/// <summary>
/// Represents a single record parsed from an LDIF file.
/// Can be either a content record (dump) or a change record (add/modify/delete/moddn).
/// </summary>
public class LdifRecord
{
    /// <summary>
    /// Distinguished Name of the entry.
    /// </summary>
    public string Dn { get; set; } = string.Empty;

    /// <summary>
    /// The type of change this record represents. Content means a plain dump entry.
    /// </summary>
    public LdifChangeType ChangeType { get; set; } = LdifChangeType.Content;

    /// <summary>
    /// Attributes and their values for content or add records.
    /// </summary>
    public Dictionary<string, List<string>> Attributes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Modifications for changetype: modify records.
    /// </summary>
    public List<LdifModification> Modifications { get; set; }

    /// <summary>
    /// New RDN for changetype: moddn/modrdn records.
    /// </summary>
    public string NewRdn { get; set; }

    /// <summary>
    /// Whether to delete the old RDN value (moddn).
    /// </summary>
    public bool DeleteOldRdn { get; set; }

    /// <summary>
    /// New superior DN for moddn records.
    /// </summary>
    public string NewSuperior { get; set; }
}

/// <summary>
/// LDIF change types per RFC 2849.
/// </summary>
public enum LdifChangeType
{
    /// <summary>Content record (no changetype line — plain dump).</summary>
    Content,
    /// <summary>Add a new entry.</summary>
    Add,
    /// <summary>Modify an existing entry.</summary>
    Modify,
    /// <summary>Delete an entry.</summary>
    Delete,
    /// <summary>Rename/move an entry.</summary>
    ModDn
}

/// <summary>
/// A single modification within a changetype: modify record.
/// </summary>
public class LdifModification
{
    /// <summary>
    /// The modification operation: add, delete, or replace.
    /// </summary>
    public string Operation { get; set; } = string.Empty;

    /// <summary>
    /// The attribute being modified.
    /// </summary>
    public string AttributeName { get; set; } = string.Empty;

    /// <summary>
    /// Values for the modification.
    /// </summary>
    public List<string> Values { get; set; } = [];
}

/// <summary>
/// Result of an LDIF import operation.
/// </summary>
public class LdifImportResult
{
    public int TotalRecords { get; set; }
    public int Imported { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
    public List<string> Errors { get; set; } = [];
}

/// <summary>
/// Options for exporting directory entries as LDIF.
/// </summary>
public class LdifExportOptions
{
    /// <summary>
    /// Base DN for the search.
    /// </summary>
    public string BaseDn { get; set; }

    /// <summary>
    /// LDAP filter expression (e.g., "(objectClass=user)").
    /// </summary>
    public string Filter { get; set; }

    /// <summary>
    /// Search scope.
    /// </summary>
    public SearchScope Scope { get; set; } = SearchScope.WholeSubtree;

    /// <summary>
    /// Specific attributes to include. Null means all attributes.
    /// </summary>
    public List<string> Attributes { get; set; }

    /// <summary>
    /// Whether to include operational attributes in the export.
    /// </summary>
    public bool IncludeOperationalAttributes { get; set; }
}
