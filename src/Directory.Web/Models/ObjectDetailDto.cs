using Directory.Core.Models;

namespace Directory.Web.Models;

public record ObjectDetailDto
{
    public string Dn { get; init; } = "";
    public string ObjectGuid { get; init; }
    public string ObjectSid { get; init; }
    public List<string> ObjectClass { get; init; } = [];
    public string Cn { get; init; }
    public string DisplayName { get; init; }
    public string Description { get; init; }
    public string SAMAccountName { get; init; }
    public string UserPrincipalName { get; init; }
    public int UserAccountControl { get; init; }
    public string GivenName { get; init; }
    public string Sn { get; init; }
    public string Mail { get; init; }
    public string Title { get; init; }
    public string Department { get; init; }
    public string Company { get; init; }
    public string Manager { get; init; }
    public string DnsHostName { get; init; }
    public string OperatingSystem { get; init; }
    public string OperatingSystemVersion { get; init; }
    public string OperatingSystemServicePack { get; init; }
    public List<string> MemberOf { get; init; } = [];
    public List<string> Member { get; init; } = [];
    public List<string> ServicePrincipalNames { get; init; } = [];
    public List<string> MsDsAllowedToDelegateTo { get; init; } = [];
    public string ThumbnailPhoto { get; init; }
    public int PrimaryGroupId { get; init; }
    public long PwdLastSet { get; init; }
    public long LastLogon { get; init; }
    public long? AccountExpires { get; init; }
    public int BadPwdCount { get; init; }
    public int GroupType { get; init; }
    public DateTimeOffset? WhenCreated { get; init; }
    public DateTimeOffset? WhenChanged { get; init; }
    public Dictionary<string, List<string>> Attributes { get; init; } = [];
    public string ETag { get; init; }

    public static ObjectDetailDto FromDirectoryObject(DirectoryObject obj)
    {
        // Build safe attributes dictionary, excluding sensitive fields
        var safeAttributes = new Dictionary<string, List<string>>();
        foreach (var kvp in obj.Attributes)
        {
            var key = kvp.Key.ToLowerInvariant();
            // Never expose sensitive credential data
            if (key is "nthash" or "kerberoskeys" or "unicodepwd" or "dbcspwd" or "supplementalcredentials")
                continue;

            safeAttributes[kvp.Key] = kvp.Value.GetStrings().ToList();
        }

        return new ObjectDetailDto
        {
            Dn = obj.DistinguishedName,
            ObjectGuid = obj.ObjectGuid,
            ObjectSid = obj.ObjectSid,
            ObjectClass = obj.ObjectClass,
            Cn = obj.Cn,
            DisplayName = obj.DisplayName,
            Description = obj.Description,
            SAMAccountName = obj.SAMAccountName,
            UserPrincipalName = obj.UserPrincipalName,
            UserAccountControl = obj.UserAccountControl,
            GivenName = obj.GivenName,
            Sn = obj.Sn,
            Mail = obj.Mail,
            Title = obj.Title,
            Department = obj.Department,
            Company = obj.Company,
            Manager = obj.Manager,
            DnsHostName = obj.DnsHostName,
            OperatingSystem = obj.OperatingSystem,
            OperatingSystemVersion = obj.OperatingSystemVersion,
            OperatingSystemServicePack = obj.Attributes.TryGetValue("operatingSystemServicePack", out var spAttr) ? spAttr.GetFirstString() : null,
            MemberOf = obj.MemberOf,
            Member = obj.Member,
            ServicePrincipalNames = obj.ServicePrincipalName,
            MsDsAllowedToDelegateTo = obj.MsDsAllowedToDelegateTo,
            ThumbnailPhoto = GetThumbnailPhoto(obj),
            PrimaryGroupId = obj.PrimaryGroupId,
            PwdLastSet = obj.PwdLastSet,
            LastLogon = obj.LastLogon,
            AccountExpires = obj.AccountExpires,
            BadPwdCount = obj.BadPwdCount,
            GroupType = obj.GroupType,
            WhenCreated = obj.WhenCreated,
            WhenChanged = obj.WhenChanged,
            Attributes = safeAttributes,
            ETag = obj.ETag,
        };
    }

    private static string GetThumbnailPhoto(DirectoryObject obj)
    {
        if (obj.Attributes.TryGetValue("thumbnailPhoto", out var photoAttr))
        {
            var bytes = photoAttr.GetFirstBytes();
            if (bytes != null) return Convert.ToBase64String(bytes);
        }
        if (obj.Attributes.TryGetValue("jpegPhoto", out var jpegAttr))
        {
            var bytes = jpegAttr.GetFirstBytes();
            if (bytes != null) return Convert.ToBase64String(bytes);
        }
        return null;
    }
}
