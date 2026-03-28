namespace Directory.Core.Models;

/// <summary>
/// LDAP search scope per RFC 4511.
/// </summary>
public enum SearchScope
{
    BaseObject = 0,
    SingleLevel = 1,
    WholeSubtree = 2
}
