namespace Directory.Core.Models;

/// <summary>
/// LDAP alias dereferencing policy per RFC 4511.
/// </summary>
public enum DerefAliases
{
    NeverDerefAliases = 0,
    DerefInSearching = 1,
    DerefFindingBaseObj = 2,
    DerefAlways = 3
}
