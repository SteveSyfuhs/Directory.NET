using System.Text.Json.Serialization;

namespace Directory.Core.Models;

/// <summary>
/// The central domain entity representing any AD object (user, group, computer, OU, etc.).
/// Maps directly to a Cosmos DB document.
/// </summary>
public class DirectoryObject
{
    /// <summary>
    /// Document ID — set to the lowercased DN.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Partition key level 1 — tenant isolation.
    /// </summary>
    [JsonPropertyName("tenantId")]
    public string TenantId { get; set; } = "default";

    /// <summary>
    /// Partition key level 2 — domain DN (e.g., "DC=corp,DC=example,DC=com").
    /// </summary>
    [JsonPropertyName("domainDn")]
    public string DomainDn { get; set; } = string.Empty;

    /// <summary>
    /// Partition key level 3 — object category (e.g., "person", "group", "organizationalUnit").
    /// </summary>
    [JsonPropertyName("objectCategory")]
    public string ObjectCategory { get; set; } = string.Empty;

    /// <summary>
    /// Full Distinguished Name.
    /// </summary>
    [JsonPropertyName("distinguishedName")]
    public string DistinguishedName { get; set; } = string.Empty;

    /// <summary>
    /// Globally unique identifier.
    /// </summary>
    [JsonPropertyName("objectGuid")]
    public string ObjectGuid { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Security identifier (S-1-5-21-...).
    /// </summary>
    [JsonPropertyName("objectSid")]
    public string ObjectSid { get; set; }

    /// <summary>
    /// Pre-Windows 2000 logon name.
    /// </summary>
    [JsonPropertyName("sAMAccountName")]
    public string SAMAccountName { get; set; }

    /// <summary>
    /// User Principal Name (user@domain.com).
    /// </summary>
    [JsonPropertyName("userPrincipalName")]
    public string UserPrincipalName { get; set; }

    /// <summary>
    /// Common Name.
    /// </summary>
    [JsonPropertyName("cn")]
    public string Cn { get; set; }

    /// <summary>
    /// Display name.
    /// </summary>
    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; }

    /// <summary>
    /// Ordered list of object classes (e.g., ["top", "person", "organizationalPerson", "user"]).
    /// </summary>
    [JsonPropertyName("objectClass")]
    public List<string> ObjectClass { get; set; } = [];

    /// <summary>
    /// All AD attributes stored as a flexible dictionary for extensibility.
    /// </summary>
    [JsonPropertyName("attributes")]
    public Dictionary<string, DirectoryAttribute> Attributes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Update Sequence Number — incremented on each modification.
    /// </summary>
    [JsonPropertyName("usnChanged")]
    public long USNChanged { get; set; }

    /// <summary>
    /// USN at creation time.
    /// </summary>
    [JsonPropertyName("usnCreated")]
    public long USNCreated { get; set; }

    [JsonPropertyName("whenCreated")]
    public DateTimeOffset WhenCreated { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("whenChanged")]
    public DateTimeOffset WhenChanged { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// DN of the parent container/OU.
    /// </summary>
    [JsonPropertyName("parentDn")]
    public string ParentDn { get; set; } = string.Empty;

    /// <summary>
    /// Tombstone flag — marks object as deleted.
    /// </summary>
    [JsonPropertyName("isDeleted")]
    public bool IsDeleted { get; set; }

    /// <summary>
    /// Legacy NT hash field. Retained for schema compatibility but no longer populated or used for authentication.
    /// </summary>
    [JsonPropertyName("ntHash")]
    public string NTHash { get; set; }

    /// <summary>
    /// Serialized Kerberos long-term credential keys (AES256-CTS, AES128-CTS).
    /// </summary>
    [JsonPropertyName("kerberosKeys")]
    public List<string> KerberosKeys { get; set; } = [];

    /// <summary>
    /// Group memberships — DNs of groups this object belongs to.
    /// </summary>
    [JsonPropertyName("memberOf")]
    public List<string> MemberOf { get; set; } = [];

    /// <summary>
    /// Members of this group — DNs of member objects.
    /// </summary>
    [JsonPropertyName("member")]
    public List<string> Member { get; set; } = [];

    /// <summary>
    /// Service Principal Names for Kerberos.
    /// </summary>
    [JsonPropertyName("servicePrincipalName")]
    public List<string> ServicePrincipalName { get; set; } = [];

    /// <summary>
    /// User Account Control flags (bitmask).
    /// </summary>
    [JsonPropertyName("userAccountControl")]
    public int UserAccountControl { get; set; }

    /// <summary>
    /// Email address.
    /// </summary>
    [JsonPropertyName("mail")]
    public string Mail { get; set; }

    /// <summary>
    /// Description.
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; set; }

    /// <summary>
    /// Cosmos DB ETag for optimistic concurrency.
    /// </summary>
    [JsonPropertyName("_etag")]
    public string ETag { get; set; }

    /// <summary>
    /// Password last set timestamp.
    /// </summary>
    [JsonPropertyName("pwdLastSet")]
    public long PwdLastSet { get; set; }

    /// <summary>
    /// Account expiration date.
    /// </summary>
    [JsonPropertyName("accountExpires")]
    public long AccountExpires { get; set; }

    /// <summary>
    /// Last logon timestamp.
    /// </summary>
    [JsonPropertyName("lastLogon")]
    public long LastLogon { get; set; }

    /// <summary>
    /// Bad password count.
    /// </summary>
    [JsonPropertyName("badPwdCount")]
    public int BadPwdCount { get; set; }

    /// <summary>
    /// Last bad password attempt timestamp.
    /// </summary>
    [JsonPropertyName("badPasswordTime")]
    public long BadPasswordTime { get; set; }

    /// <summary>
    /// Group type flags.
    /// </summary>
    [JsonPropertyName("groupType")]
    public int GroupType { get; set; }

    /// <summary>
    /// Primary group ID (RID).
    /// </summary>
    [JsonPropertyName("primaryGroupId")]
    public int PrimaryGroupId { get; set; } = 513; // Domain Users

    /// <summary>
    /// GPO file system path.
    /// </summary>
    [JsonPropertyName("gPCFileSysPath")]
    public string GPCFileSysPath { get; set; }

    [JsonPropertyName("nTSecurityDescriptor")]
    public byte[] NTSecurityDescriptor { get; set; }

    [JsonPropertyName("isRecycled")]
    public bool IsRecycled { get; set; }

    [JsonPropertyName("lastKnownParent")]
    public string LastKnownParent { get; set; }

    [JsonPropertyName("deletedTime")]
    public DateTimeOffset? DeletedTime { get; set; }

    [JsonPropertyName("systemFlags")]
    public int SystemFlags { get; set; }

    [JsonPropertyName("searchFlags")]
    public int SearchFlags { get; set; }

    [JsonPropertyName("msDS-AllowedToActOnBehalfOfOtherIdentity")]
    public byte[] MsDsAllowedToActOnBehalf { get; set; }

    [JsonPropertyName("msDS-AllowedToDelegateTo")]
    public List<string> MsDsAllowedToDelegateTo { get; set; } = [];

    [JsonPropertyName("dNSHostName")]
    public string DnsHostName { get; set; }

    [JsonPropertyName("operatingSystem")]
    public string OperatingSystem { get; set; }

    [JsonPropertyName("operatingSystemVersion")]
    public string OperatingSystemVersion { get; set; }

    [JsonPropertyName("givenName")]
    public string GivenName { get; set; }

    [JsonPropertyName("sn")]
    public string Sn { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; }

    [JsonPropertyName("department")]
    public string Department { get; set; }

    [JsonPropertyName("company")]
    public string Company { get; set; }

    [JsonPropertyName("manager")]
    public string Manager { get; set; }

    [JsonPropertyName("wkGuid")]
    public string WellKnownGuid { get; set; }

    [JsonPropertyName("gPLink")]
    public string GPLink { get; set; }

    [JsonPropertyName("gPOptions")]
    public int GPOptions { get; set; }

    [JsonPropertyName("sAMAccountType")]
    public int SAMAccountType { get; set; }

    /// <summary>
    /// Pre-computed transitive group SIDs this principal belongs to.
    /// Materialized on group membership change. Format: ["S-1-5-21-...-512", "S-1-5-21-...-513"]
    /// Used by PAC generation and tokenGroups computation to avoid recursive queries.
    /// </summary>
    [JsonPropertyName("tokenGroupsSids")]
    public List<string> TokenGroupsSids { get; set; } = [];

    /// <summary>
    /// Timestamp when TokenGroupsSids was last computed. Used to detect staleness.
    /// </summary>
    [JsonPropertyName("tokenGroupsLastComputed")]
    public DateTimeOffset? TokenGroupsLastComputed { get; set; }

    /// <summary>
    /// Get a well-known attribute value by name, checking both top-level properties and the Attributes dictionary.
    /// </summary>
    public DirectoryAttribute GetAttribute(string name)
    {
        // Check the Attributes dictionary first
        if (Attributes.TryGetValue(name, out var attr))
            return attr;

        // Map well-known properties
        return name.ToLowerInvariant() switch
        {
            "distinguishedname" => new DirectoryAttribute(name, DistinguishedName),
            "objectguid" => new DirectoryAttribute(name, ObjectGuid),
            "objectsid" => ObjectSid != null ? new DirectoryAttribute(name, ObjectSid) : null,
            "samaccountname" => SAMAccountName != null ? new DirectoryAttribute(name, SAMAccountName) : null,
            "userprincipalname" => UserPrincipalName != null ? new DirectoryAttribute(name, UserPrincipalName) : null,
            "cn" => Cn != null ? new DirectoryAttribute(name, Cn) : null,
            "displayname" => DisplayName != null ? new DirectoryAttribute(name, DisplayName) : null,
            "objectclass" => new DirectoryAttribute(name, [.. ObjectClass.Cast<object>()]),
            "objectcategory" => new DirectoryAttribute(name, ObjectCategory),
            "memberof" => MemberOf.Count > 0 ? new DirectoryAttribute(name, [.. MemberOf.Cast<object>()]) : null,
            "member" => Member.Count > 0 ? new DirectoryAttribute(name, [.. Member.Cast<object>()]) : null,
            "serviceprincipalname" => ServicePrincipalName.Count > 0 ? new DirectoryAttribute(name, [.. ServicePrincipalName.Cast<object>()]) : null,
            "useraccountcontrol" => new DirectoryAttribute(name, UserAccountControl),
            "mail" => Mail != null ? new DirectoryAttribute(name, Mail) : null,
            "description" => Description != null ? new DirectoryAttribute(name, Description) : null,
            "whencreated" => new DirectoryAttribute(name, WhenCreated.ToString("yyyyMMddHHmmss.0Z")),
            "whenchanged" => new DirectoryAttribute(name, WhenChanged.ToString("yyyyMMddHHmmss.0Z")),
            "parentdn" or "parentguid" => new DirectoryAttribute(name, ParentDn),
            "usnchanged" => new DirectoryAttribute(name, USNChanged),
            "usncreated" => new DirectoryAttribute(name, USNCreated),
            "isdeleted" => IsDeleted ? new DirectoryAttribute(name, true) : null,
            "pwdlastset" => new DirectoryAttribute(name, PwdLastSet),
            "lastlogon" => new DirectoryAttribute(name, LastLogon),
            "badpwdcount" => new DirectoryAttribute(name, BadPwdCount),
            "primarygroupid" => new DirectoryAttribute(name, PrimaryGroupId),
            "grouptype" => GroupType != 0 ? new DirectoryAttribute(name, GroupType) : null,
            "ntsecuritydescriptor" => NTSecurityDescriptor != null ? new DirectoryAttribute(name, NTSecurityDescriptor) : null,
            "isrecycled" => IsRecycled ? new DirectoryAttribute(name, true) : null,
            "lastknownparent" => LastKnownParent != null ? new DirectoryAttribute(name, LastKnownParent) : null,
            "deletedtime" => DeletedTime.HasValue ? new DirectoryAttribute(name, DeletedTime.Value.ToString("yyyyMMddHHmmss.0Z")) : null,
            "systemflags" => new DirectoryAttribute(name, SystemFlags),
            "searchflags" => new DirectoryAttribute(name, SearchFlags),
            "msds-allowedtoactonbehalfofotheridentity" => MsDsAllowedToActOnBehalf != null ? new DirectoryAttribute(name, MsDsAllowedToActOnBehalf) : null,
            "msds-allowedtodelegateto" => MsDsAllowedToDelegateTo.Count > 0 ? new DirectoryAttribute(name, [.. MsDsAllowedToDelegateTo.Cast<object>()]) : null,
            "dnshostname" => DnsHostName != null ? new DirectoryAttribute(name, DnsHostName) : null,
            "operatingsystem" => OperatingSystem != null ? new DirectoryAttribute(name, OperatingSystem) : null,
            "operatingsystemversion" => OperatingSystemVersion != null ? new DirectoryAttribute(name, OperatingSystemVersion) : null,
            "givenname" => GivenName != null ? new DirectoryAttribute(name, GivenName) : null,
            "sn" => Sn != null ? new DirectoryAttribute(name, Sn) : null,
            "title" => Title != null ? new DirectoryAttribute(name, Title) : null,
            "department" => Department != null ? new DirectoryAttribute(name, Department) : null,
            "company" => Company != null ? new DirectoryAttribute(name, Company) : null,
            "manager" => Manager != null ? new DirectoryAttribute(name, Manager) : null,
            "wkguid" or "wellknownguid" => WellKnownGuid != null ? new DirectoryAttribute(name, WellKnownGuid) : null,
            "gplink" => GPLink != null ? new DirectoryAttribute(name, GPLink) : null,
            "gpoptions" => new DirectoryAttribute(name, GPOptions),
            "samaccounttype" => new DirectoryAttribute(name, SAMAccountType),
            _ => null,
        };
    }

    /// <summary>
    /// Set an attribute value, storing in the appropriate top-level property or Attributes dict.
    /// </summary>
    public void SetAttribute(string name, DirectoryAttribute value)
    {
        switch (name.ToLowerInvariant())
        {
            case "cn": Cn = value.GetFirstString(); break;
            case "displayname": DisplayName = value.GetFirstString(); break;
            case "samaccountname": SAMAccountName = value.GetFirstString(); break;
            case "userprincipalname": UserPrincipalName = value.GetFirstString(); break;
            case "mail": Mail = value.GetFirstString(); break;
            case "description": Description = value.GetFirstString(); break;
            case "objectclass": ObjectClass = value.GetStrings().ToList(); break;
            case "objectcategory": ObjectCategory = value.GetFirstString() ?? string.Empty; break;
            case "memberof": MemberOf = value.GetStrings().ToList(); break;
            case "member": Member = value.GetStrings().ToList(); break;
            case "serviceprincipalname": ServicePrincipalName = value.GetStrings().ToList(); break;
            case "useraccountcontrol":
                if (value.Values.Count > 0 && int.TryParse(value.Values[0]?.ToString(), out var uac))
                    UserAccountControl = uac;
                break;
            case "ntsecuritydescriptor": NTSecurityDescriptor = value.GetFirstBytes(); break;
            case "isrecycled":
                if (value.Values.Count > 0 && bool.TryParse(value.Values[0]?.ToString(), out var recycled))
                    IsRecycled = recycled;
                break;
            case "lastknownparent": LastKnownParent = value.GetFirstString(); break;
            case "deletedtime":
                if (value.Values.Count > 0 && DateTimeOffset.TryParse(value.Values[0]?.ToString(), out var dt))
                    DeletedTime = dt;
                break;
            case "systemflags":
                if (value.Values.Count > 0 && int.TryParse(value.Values[0]?.ToString(), out var sf))
                    SystemFlags = sf;
                break;
            case "searchflags":
                if (value.Values.Count > 0 && int.TryParse(value.Values[0]?.ToString(), out var srf))
                    SearchFlags = srf;
                break;
            case "msds-allowedtoactonbehalfofotheridentity": MsDsAllowedToActOnBehalf = value.GetFirstBytes(); break;
            case "msds-allowedtodelegateto": MsDsAllowedToDelegateTo = value.GetStrings().ToList(); break;
            case "dnshostname": DnsHostName = value.GetFirstString(); break;
            case "operatingsystem": OperatingSystem = value.GetFirstString(); break;
            case "operatingsystemversion": OperatingSystemVersion = value.GetFirstString(); break;
            case "givenname": GivenName = value.GetFirstString(); break;
            case "sn": Sn = value.GetFirstString(); break;
            case "title": Title = value.GetFirstString(); break;
            case "department": Department = value.GetFirstString(); break;
            case "company": Company = value.GetFirstString(); break;
            case "manager": Manager = value.GetFirstString(); break;
            case "gplink": GPLink = value.GetFirstString(); break;
            case "gpoptions":
                if (value.Values.Count > 0 && int.TryParse(value.Values[0]?.ToString(), out var gpo))
                    GPOptions = gpo;
                break;
            case "samaccounttype":
                if (value.Values.Count > 0 && int.TryParse(value.Values[0]?.ToString(), out var sat))
                    SAMAccountType = sat;
                break;
            default:
                Attributes[name] = value;
                break;
        }
    }
}
