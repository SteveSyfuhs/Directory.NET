using Directory.Core.Interfaces;
using Directory.Core.Models;
using Microsoft.Extensions.Logging;

namespace Directory.Security;

public class ConstructedAttributeService : IConstructedAttributeService
{
    private readonly IDirectoryStore _store;
    private readonly ISchemaService _schema;
    private readonly ILogger<ConstructedAttributeService> _logger;

    private static readonly HashSet<string> ConstructedAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "tokenGroups",
        "tokenGroupsGlobalAndUniversal",
        "tokenGroupsNoGCAcceptable",
        "canonicalName",
        "allowedAttributes",
        "allowedChildClasses",
        "msDS-UserPasswordExpiryTimeComputed",
        "msDS-User-Account-Control-Computed",
        "createTimeStamp",
        "modifyTimeStamp",
        "subSchemaSubEntry",
        "structuralObjectClass"
    };

    public ConstructedAttributeService(
        IDirectoryStore store,
        ISchemaService schema,
        ILogger<ConstructedAttributeService> logger)
    {
        _store = store;
        _schema = schema;
        _logger = logger;
    }

    public bool IsConstructedAttribute(string attributeName) =>
        ConstructedAttributes.Contains(attributeName);

    public IReadOnlyList<string> GetConstructedAttributeNames() =>
        [.. ConstructedAttributes];

    public async Task<DirectoryAttribute> ComputeAttributeAsync(
        string attributeName,
        DirectoryObject obj,
        string tenantId,
        CancellationToken ct = default)
    {
        return attributeName.ToLowerInvariant() switch
        {
            "tokengroups" => await ComputeTokenGroupsAsync("tokenGroups", obj, tenantId, false, ct),
            "tokengroupsglobalanduniversal" => await ComputeTokenGroupsAsync("tokenGroupsGlobalAndUniversal", obj, tenantId, true, ct),
            "tokengroupsnogcacceptable" => await ComputeTokenGroupsAsync("tokenGroupsNoGCAcceptable", obj, tenantId, true, ct),
            "canonicalname" => ComputeCanonicalName(obj),
            "allowedattributes" => ComputeAllowedAttributes(obj),
            "allowedchildclasses" => ComputeAllowedChildClasses(obj),
            "msds-userpasswordexpirytimecomputed" => await ComputePasswordExpiryAsync(obj, tenantId, ct),
            "msds-user-account-control-computed" => ComputeDynamicUac(obj),
            "createtimestamp" => ComputeAlias(obj, "whenCreated"),
            "modifytimestamp" => ComputeAlias(obj, "whenChanged"),
            "subschemasubentry" => new DirectoryAttribute(attributeName,
                $"CN=Aggregate,CN=Schema,CN=Configuration,{obj.DomainDn}"),
            "structuralobjectclass" => ComputeStructuralObjectClass(obj),
            _ => null
        };
    }

    private async Task<DirectoryAttribute> ComputeTokenGroupsAsync(
        string tokenGroupAttributeName,
        DirectoryObject obj,
        string tenantId,
        bool filterScope,
        CancellationToken ct)
    {
        // Fast path: use materialized TokenGroupsSids if available and fresh (non-filtered only)
        if (!filterScope && obj.TokenGroupsSids.Count > 0 && obj.TokenGroupsLastComputed.HasValue)
        {
            var age = DateTimeOffset.UtcNow - obj.TokenGroupsLastComputed.Value;
            if (age < TimeSpan.FromMinutes(30))
            {
                var materializedSids = new List<object>();

                // Include primary group SID
                if (obj.PrimaryGroupId != 0 && obj.ObjectSid is not null)
                {
                    string domainSid = GetDomainSidFromObjectSid(obj.ObjectSid);
                    string primaryGroupSid = $"{domainSid}-{obj.PrimaryGroupId}";
                    try
                    {
                        materializedSids.Add(AccessControlEntry.SerializeSid(primaryGroupSid));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to serialize primary group SID {Sid}", primaryGroupSid);
                    }
                }

                foreach (var sidString in obj.TokenGroupsSids)
                {
                    try
                    {
                        materializedSids.Add(AccessControlEntry.SerializeSid(sidString));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to serialize materialized SID {Sid}", sidString);
                    }
                }

                return materializedSids.Count > 0
                    ? new DirectoryAttribute(tokenGroupAttributeName, [.. materializedSids])
                    : null;
            }
        }

        // Slow path: compute on the fly (existing BFS code)
        var visitedDns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visitedSids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var binarySids = new List<object>();

        // Include primary group
        if (obj.PrimaryGroupId != 0 && obj.ObjectSid is not null)
        {
            string domainSid = GetDomainSidFromObjectSid(obj.ObjectSid);
            string primaryGroupSid = $"{domainSid}-{obj.PrimaryGroupId}";

            if (visitedSids.Add(primaryGroupSid))
            {
                try
                {
                    binarySids.Add(AccessControlEntry.SerializeSid(primaryGroupSid));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to serialize primary group SID {Sid}", primaryGroupSid);
                }
            }
        }

        // Walk memberOf transitively using BFS with batch-parallel loading per level
        var toProcess = new Queue<string>(obj.MemberOf);

        while (toProcess.Count > 0)
        {
            ct.ThrowIfCancellationRequested();

            // Collect all unvisited DNs for this BFS level
            var batch = new List<string>();
            while (toProcess.Count > 0)
            {
                var dn = toProcess.Dequeue();
                if (visitedDns.Add(dn))
                    batch.Add(dn);
            }

            if (batch.Count == 0) break;

            // Batch load all groups at this level
            var groups = await _store.GetByDnsAsync(tenantId, batch, ct);

            foreach (var group in groups)
            {
                if (group is null)
                    continue;

                if (filterScope && !IsGlobalOrUniversal(group.GroupType))
                    continue;

                if (group.ObjectSid is not null && visitedSids.Add(group.ObjectSid))
                {
                    try
                    {
                        binarySids.Add(AccessControlEntry.SerializeSid(group.ObjectSid));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to serialize group SID {Sid}", group.ObjectSid);
                    }
                }

                foreach (string parentDn in group.MemberOf)
                {
                    if (!visitedDns.Contains(parentDn))
                        toProcess.Enqueue(parentDn);
                }
            }
        }

        if (binarySids.Count == 0)
            return null;

        return new DirectoryAttribute(tokenGroupAttributeName, [.. binarySids]);
    }

    private static bool IsGlobalOrUniversal(int groupType)
    {
        // Global group: 0x00000002, Universal group: 0x00000008
        // Security bit: 0x80000000
        const int globalGroup = 0x00000002;
        const int universalGroup = 0x00000008;
        int scopeBits = groupType & 0x0000000F;
        return scopeBits == globalGroup || scopeBits == universalGroup;
    }

    private static string GetDomainSidFromObjectSid(string objectSid)
    {
        int lastDash = objectSid.LastIndexOf('-');
        return lastDash > 0 ? objectSid[..lastDash] : objectSid;
    }

    private static DirectoryAttribute ComputeCanonicalName(DirectoryObject obj)
    {
        var dn = DistinguishedName.Parse(obj.DistinguishedName);
        var components = dn.Components;

        // Extract DC components for domain
        var dcParts = components
            .Where(c => c.Type.Equals("DC", StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Value);
        string domain = string.Join(".", dcParts);

        // Non-DC components in reverse order (bottom-up to top-down)
        var pathParts = components
            .Where(c => !c.Type.Equals("DC", StringComparison.OrdinalIgnoreCase))
            .Reverse()
            .Select(c => c.Value);

        string path = string.Join("/", pathParts);

        string canonical = string.IsNullOrEmpty(path) ? domain : $"{domain}/{path}";
        return new DirectoryAttribute("canonicalName", canonical);
    }

    private DirectoryAttribute ComputeAllowedAttributes(DirectoryObject obj)
    {
        var attrNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string className in obj.ObjectClass)
        {
            var classDef = _schema.GetObjectClass(className);
            if (classDef is null) continue;

            foreach (string attr in classDef.MustHaveAttributes)
                attrNames.Add(attr);
            foreach (string attr in classDef.MayHaveAttributes)
                attrNames.Add(attr);

            // Walk superclass chain
            string superName = classDef.SuperiorClass;
            while (superName is not null)
            {
                var superDef = _schema.GetObjectClass(superName);
                if (superDef is null) break;

                foreach (string attr in superDef.MustHaveAttributes)
                    attrNames.Add(attr);
                foreach (string attr in superDef.MayHaveAttributes)
                    attrNames.Add(attr);

                superName = superDef.SuperiorClass;
            }
        }

        return attrNames.Count > 0
            ? new DirectoryAttribute("allowedAttributes", [.. attrNames.Order().Cast<object>()])
            : null;
    }

    private DirectoryAttribute ComputeAllowedChildClasses(DirectoryObject obj)
    {
        // Find all object classes whose possibleSuperiors includes any of this object's classes
        var objClasses = new HashSet<string>(obj.ObjectClass, StringComparer.OrdinalIgnoreCase);
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var classDef in _schema.GetAllObjectClasses())
        {
            if (classDef.ClassType != ObjectClassType.Structural)
                continue;

            // Check if any of the class's superior chain includes our object's classes
            // Convention: MayHaveAttributes may list possibleSuperiors via schema;
            // for simplicity, check if the class's superiorClass is in our object's classes
            // or if the class itself can be a child of container-type objects
            if (objClasses.Contains("container") || objClasses.Contains("organizationalUnit")
                || objClasses.Contains("domainDNS") || objClasses.Contains("top"))
            {
                allowed.Add(classDef.LdapDisplayName);
            }
        }

        return allowed.Count > 0
            ? new DirectoryAttribute("allowedChildClasses", [.. allowed.Order().Cast<object>()])
            : null;
    }

    private async Task<DirectoryAttribute> ComputePasswordExpiryAsync(
        DirectoryObject obj,
        string tenantId,
        CancellationToken ct)
    {
        if (obj.PwdLastSet == 0)
            return new DirectoryAttribute("msDS-UserPasswordExpiryTimeComputed", 0L);

        // Password never expires flag (UF_DONT_EXPIRE_PASSWD = 0x10000)
        if ((obj.UserAccountControl & 0x10000) != 0)
            return new DirectoryAttribute("msDS-UserPasswordExpiryTimeComputed", long.MaxValue);

        // Look up domain policy for maxPwdAge
        var domain = await _store.GetByDnAsync(tenantId, obj.DomainDn, ct);
        long maxPwdAge = 0;

        if (domain is not null)
        {
            var maxPwdAgeAttr = domain.GetAttribute("maxPwdAge");
            if (maxPwdAgeAttr is not null && long.TryParse(maxPwdAgeAttr.GetFirstString(), out var parsed))
                maxPwdAge = parsed;
        }

        if (maxPwdAge == 0)
            return new DirectoryAttribute("msDS-UserPasswordExpiryTimeComputed", long.MaxValue);

        // maxPwdAge is stored as negative 100ns intervals
        long expiryTime = obj.PwdLastSet + Math.Abs(maxPwdAge);
        return new DirectoryAttribute("msDS-UserPasswordExpiryTimeComputed", expiryTime);
    }

    private static DirectoryAttribute ComputeDynamicUac(DirectoryObject obj)
    {
        int computed = 0;

        // UF_LOCKOUT (0x0010) — based on badPwdCount and lockout threshold
        // Simplified: if badPwdCount > 0 and badPasswordTime is recent
        if (obj.BadPwdCount > 0 && obj.BadPasswordTime > 0)
        {
            // A more accurate implementation would check lockoutDuration domain policy.
            // For now, set lockout bit if badPwdCount is non-zero.
            computed |= 0x0010;
        }

        // UF_PASSWORD_EXPIRED (0x800000)
        if (obj.PwdLastSet == 0 && (obj.UserAccountControl & 0x10000) == 0)
            computed |= 0x800000;

        return new DirectoryAttribute("msDS-User-Account-Control-Computed", computed);
    }

    private static DirectoryAttribute ComputeAlias(DirectoryObject obj, string sourceAttr)
    {
        return sourceAttr.ToLowerInvariant() switch
        {
            "whencreated" => new DirectoryAttribute("createTimeStamp",
                obj.WhenCreated.ToString("yyyyMMddHHmmss.0Z")),
            "whenchanged" => new DirectoryAttribute("modifyTimeStamp",
                obj.WhenChanged.ToString("yyyyMMddHHmmss.0Z")),
            _ => null
        };
    }

    private DirectoryAttribute ComputeStructuralObjectClass(DirectoryObject obj)
    {
        // The most-derived structural class is the last in the objectClass list
        for (int i = obj.ObjectClass.Count - 1; i >= 0; i--)
        {
            var classDef = _schema.GetObjectClass(obj.ObjectClass[i]);
            if (classDef is not null && classDef.ClassType == ObjectClassType.Structural)
                return new DirectoryAttribute("structuralObjectClass", obj.ObjectClass[i]);
        }

        // Fallback to last class
        return obj.ObjectClass.Count > 0
            ? new DirectoryAttribute("structuralObjectClass", obj.ObjectClass[^1])
            : null;
    }
}
