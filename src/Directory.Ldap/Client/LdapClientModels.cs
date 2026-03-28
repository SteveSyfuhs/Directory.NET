namespace Directory.Ldap.Client;

/// <summary>
/// Result of an LDAP bind operation.
/// </summary>
public class LdapBindResult
{
    public bool Success { get; init; }
    public int ResultCode { get; init; }
    public string DiagnosticMessage { get; init; } = "";
}

/// <summary>
/// Generic LDAP operation result (add, modify, delete).
/// </summary>
public class LdapResult
{
    public bool Success { get; init; }
    public int ResultCode { get; init; }
    public string MatchedDn { get; init; } = "";
    public string DiagnosticMessage { get; init; } = "";
}

/// <summary>
/// Result of an LDAP search operation containing matched entries.
/// </summary>
public class LdapSearchResult
{
    public List<LdapSearchEntry> Entries { get; init; } = new();
    public int ResultCode { get; init; }
    public string DiagnosticMessage { get; init; } = "";
}

/// <summary>
/// A single entry returned from an LDAP search.
/// </summary>
public class LdapSearchEntry
{
    public string DistinguishedName { get; init; } = "";
    public Dictionary<string, List<string>> Attributes { get; init; } = new();

    public string GetFirstValue(string attributeName) =>
        Attributes.TryGetValue(attributeName, out var vals) && vals.Count > 0 ? vals[0] : null;
}

/// <summary>
/// Describes a single modification to apply to an LDAP entry.
/// </summary>
public class LdapModification
{
    public LdapModOperation Operation { get; init; }
    public string AttributeName { get; init; } = "";
    public List<string> Values { get; init; } = new();
}

/// <summary>
/// LDAP modify operation types per RFC 4511.
/// </summary>
public enum LdapModOperation
{
    Add = 0,
    Delete = 1,
    Replace = 2
}
