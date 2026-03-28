using Directory.Core.Interfaces;
using Directory.Core.Models;
using Microsoft.Extensions.Logging;

namespace Directory.Security;

/// <summary>
/// Evaluates ACLs per MS-ADTS section 5.1.
/// Supports group membership expansion, object-type scoped ACEs,
/// per-attribute property set checks, and protected attribute enforcement.
/// </summary>
public class AccessControlService : IAccessControlService
{
    private readonly ISchemaService _schema;
    private readonly ILogger<AccessControlService> _logger;

    /// <summary>
    /// Attributes that require special access rights beyond WRITE_PROPERTY.
    /// </summary>
    private static readonly Dictionary<string, int> ProtectedAttributeRights = new(StringComparer.OrdinalIgnoreCase)
    {
        ["nTSecurityDescriptor"] = AccessMask.WriteDacl,
        ["owner"] = AccessMask.WriteOwner,
    };

    /// <summary>
    /// Freshness window for materialized TokenGroupsSids.
    /// </summary>
    private static readonly TimeSpan TokenGroupsFreshnessWindow = TimeSpan.FromMinutes(30);

    public AccessControlService(ISchemaService schema, ILogger<AccessControlService> logger)
    {
        _schema = schema;
        _logger = logger;
    }

    #region Group Resolution

    public async Task<IReadOnlySet<string>> ResolveCallerGroupsAsync(
        string callerSid, IDirectoryStore store, string tenantId, CancellationToken ct = default)
    {
        var groups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Always include the caller's own SID
        if (!string.IsNullOrEmpty(callerSid))
            groups.Add(callerSid);

        // Well-known SIDs for any authenticated caller
        if (!string.Equals(callerSid, "S-1-5-7", StringComparison.OrdinalIgnoreCase) // not Anonymous
            && !string.IsNullOrEmpty(callerSid))
        {
            groups.Add(WellKnownSids.Everyone);
            groups.Add(WellKnownSids.AuthenticatedUsers);
        }

        if (string.IsNullOrEmpty(callerSid))
            return groups;

        // Look up the caller's DirectoryObject to get materialized groups
        // We search by objectSid since that's what we have
        var callerObj = await FindByObjectSidAsync(store, tenantId, callerSid, ct);
        if (callerObj is null)
            return groups;

        // Use materialized TokenGroupsSids if fresh
        if (callerObj.TokenGroupsSids.Count > 0 && callerObj.TokenGroupsLastComputed.HasValue)
        {
            var age = DateTimeOffset.UtcNow - callerObj.TokenGroupsLastComputed.Value;
            if (age < TokenGroupsFreshnessWindow)
            {
                foreach (var sid in callerObj.TokenGroupsSids)
                    groups.Add(sid);

                // Add primary group SID
                AddPrimaryGroupSid(callerObj, groups);
                return groups;
            }
        }

        // Fall back to computing on-the-fly via BFS of memberOf
        await ExpandGroupSidsAsync(store, tenantId, callerObj, groups, ct);
        AddPrimaryGroupSid(callerObj, groups);

        return groups;
    }

    private static void AddPrimaryGroupSid(DirectoryObject obj, HashSet<string> groups)
    {
        if (obj.PrimaryGroupId > 0 && !string.IsNullOrEmpty(obj.ObjectSid))
        {
            var lastDash = obj.ObjectSid.LastIndexOf('-');
            if (lastDash > 0)
            {
                var domainSid = obj.ObjectSid[..lastDash];
                groups.Add($"{domainSid}-{obj.PrimaryGroupId}");
            }
        }
    }

    private static async Task ExpandGroupSidsAsync(
        IDirectoryStore store, string tenantId, DirectoryObject obj,
        HashSet<string> sids, CancellationToken ct)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>(obj.MemberOf ?? []);

        while (queue.Count > 0)
        {
            var batch = new List<string>();
            while (queue.Count > 0)
            {
                var dn = queue.Dequeue();
                if (visited.Add(dn))
                    batch.Add(dn);
            }

            if (batch.Count == 0) break;

            var groups = await store.GetByDnsAsync(tenantId, batch, ct);
            foreach (var group in groups)
            {
                if (!string.IsNullOrEmpty(group.ObjectSid))
                    sids.Add(group.ObjectSid);

                foreach (var parentDn in group.MemberOf ?? [])
                {
                    if (!visited.Contains(parentDn))
                        queue.Enqueue(parentDn);
                }
            }
        }
    }

    private static async Task<DirectoryObject> FindByObjectSidAsync(
        IDirectoryStore store, string tenantId, string objectSid, CancellationToken ct)
    {
        // Search for the object by SID - use a filter-based search
        var result = await store.SearchAsync(
            tenantId,
            baseDn: string.Empty,
            SearchScope.WholeSubtree,
            filter: new EqualityFilterNode("objectSid", objectSid),
            attributes: null,
            sizeLimit: 1,
            ct: ct);

        return result.Entries.Count > 0 ? result.Entries[0] : null;
    }

    #endregion

    #region Admin/System Check

    public bool IsAdminOrSystem(string callerSid, IReadOnlySet<string> callerGroupSids)
    {
        if (string.Equals(callerSid, WellKnownSids.System, StringComparison.OrdinalIgnoreCase))
            return true;

        if (string.Equals(callerSid, WellKnownSids.Administrators, StringComparison.OrdinalIgnoreCase))
            return true;

        // Check if caller is a member of Administrators or Domain Admins
        if (callerGroupSids.Contains(WellKnownSids.Administrators))
            return true;

        // Domain Admins check: any SID ending in -512
        foreach (var sid in callerGroupSids)
        {
            if (sid.EndsWith("-512", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    #endregion

    #region CheckAccess

    public bool CheckAccess(string callerSid, IReadOnlySet<string> callerGroupSids, DirectoryObject target, int desiredAccess)
    {
        var sd = GetSecurityDescriptor(target);
        if (sd?.Dacl is null)
        {
            _logger.LogDebug("No DACL present on {Dn}, granting access", target.DistinguishedName);
            return true;
        }

        bool isOwner = string.Equals(sd.OwnerSid, callerSid, StringComparison.OrdinalIgnoreCase);

        // Owner always has ReadControl and WriteDacl
        if (isOwner)
        {
            desiredAccess &= ~(AccessMask.ReadControl | AccessMask.WriteDacl);
            if (desiredAccess == 0)
                return true;
        }

        // Phase 1: Check AccessDenied ACEs
        foreach (var ace in sd.Dacl.Aces)
        {
            if (ace.Type is not (AceType.AccessDenied or AceType.AccessDeniedObject))
                continue;

            if (!SidMatches(ace.TrusteeSid, callerSid, callerGroupSids, isOwner))
                continue;

            if ((ace.Flags & AceFlags.InheritOnly) != 0)
                continue;

            if ((ace.Mask & desiredAccess) != 0)
            {
                _logger.LogDebug("Access denied for {Sid} on {Dn} by deny ACE for {Trustee}",
                    callerSid, target.DistinguishedName, ace.TrusteeSid);
                return false;
            }
        }

        // Phase 2: Check AccessAllowed ACEs, accumulate granted bits
        int granted = 0;

        foreach (var ace in sd.Dacl.Aces)
        {
            if (ace.Type is not (AceType.AccessAllowed or AceType.AccessAllowedObject))
                continue;

            if (!SidMatches(ace.TrusteeSid, callerSid, callerGroupSids, isOwner))
                continue;

            if ((ace.Flags & AceFlags.InheritOnly) != 0)
                continue;

            if ((ace.Mask & AccessMask.GenericAll) != 0)
                return true;

            granted |= ace.Mask;
        }

        bool result = (granted & desiredAccess) == desiredAccess;

        if (!result)
        {
            _logger.LogDebug("Insufficient access for {Sid} on {Dn}: desired=0x{Desired:X}, granted=0x{Granted:X}",
                callerSid, target.DistinguishedName, desiredAccess, granted);
        }

        return result;
    }

    /// <summary>
    /// Legacy overload that delegates to group-aware version with empty group set.
    /// </summary>
    public bool CheckAccess(string callerSid, DirectoryObject target, int desiredAccess)
    {
        return CheckAccess(callerSid, EmptyGroupSids, target, desiredAccess);
    }

    #endregion

    #region Attribute Access

    public bool CheckAttributeAccess(
        string callerSid, IReadOnlySet<string> callerGroupSids,
        DirectoryObject target, string attributeName, bool isWrite,
        ISchemaService schema = null)
    {
        // Check protected attribute rights first
        if (isWrite && ProtectedAttributeRights.TryGetValue(attributeName, out var requiredRight))
        {
            return CheckAccess(callerSid, callerGroupSids, target, requiredRight);
        }

        int requiredAccess = isWrite ? AccessMask.WriteProperty : AccessMask.ReadProperty;

        var sd = GetSecurityDescriptor(target);
        if (sd?.Dacl is null)
            return true; // No DACL = full access

        bool isOwner = string.Equals(sd.OwnerSid, callerSid, StringComparison.OrdinalIgnoreCase);

        // Look up attribute schema for object-specific ACE matching
        Guid? attrSchemaIdGuid = null;
        Guid? attrSecurityGuid = null;
        if (schema != null)
        {
            var attrSchema = schema.GetAttribute(attributeName);
            if (attrSchema != null)
            {
                attrSchemaIdGuid = attrSchema.SchemaIDGUID;
                attrSecurityGuid = attrSchema.AttributeSecurityGUID;
            }
        }

        // Phase 1: Check deny ACEs
        foreach (var ace in sd.Dacl.Aces)
        {
            if (ace.Type is not (AceType.AccessDenied or AceType.AccessDeniedObject))
                continue;

            if (!SidMatches(ace.TrusteeSid, callerSid, callerGroupSids, isOwner))
                continue;

            if ((ace.Flags & AceFlags.InheritOnly) != 0)
                continue;

            if ((ace.Mask & requiredAccess) == 0 && (ace.Mask & AccessMask.GenericAll) == 0)
                continue;

            // For object-specific deny ACEs, check if the ObjectType matches
            if (ace.Type == AceType.AccessDeniedObject && ace.ObjectType.HasValue)
            {
                if (!ObjectTypeMatchesAttribute(ace.ObjectType.Value, attrSchemaIdGuid, attrSecurityGuid))
                    continue;
            }

            _logger.LogTrace("Attribute {Attr} access denied for {Sid} on {Dn}",
                attributeName, callerSid, target.DistinguishedName);
            return false;
        }

        // Phase 2: Check allow ACEs - accumulate
        bool genericGranted = false;
        bool specificGranted = false;

        foreach (var ace in sd.Dacl.Aces)
        {
            if (ace.Type is not (AceType.AccessAllowed or AceType.AccessAllowedObject))
                continue;

            if (!SidMatches(ace.TrusteeSid, callerSid, callerGroupSids, isOwner))
                continue;

            if ((ace.Flags & AceFlags.InheritOnly) != 0)
                continue;

            // GenericAll grants everything
            if ((ace.Mask & AccessMask.GenericAll) != 0)
                return true;

            if ((ace.Mask & requiredAccess) == 0)
                continue;

            // Non-object ACE: grants the right for ALL attributes
            if (ace.Type == AceType.AccessAllowed)
            {
                genericGranted = true;
                continue;
            }

            // Object-specific ACE: check if ObjectType matches this attribute
            if (ace.Type == AceType.AccessAllowedObject)
            {
                if (!ace.ObjectType.HasValue)
                {
                    // No ObjectType means it applies to all properties
                    genericGranted = true;
                }
                else if (ObjectTypeMatchesAttribute(ace.ObjectType.Value, attrSchemaIdGuid, attrSecurityGuid))
                {
                    specificGranted = true;
                }
            }
        }

        return genericGranted || specificGranted;
    }

    public bool CheckAttributeAccess(string callerSid, DirectoryObject target, string attributeName, bool isWrite)
    {
        return CheckAttributeAccess(callerSid, EmptyGroupSids, target, attributeName, isWrite, _schema);
    }

    /// <summary>
    /// Checks if an ACE ObjectType GUID matches a specific attribute by its schemaIDGUID or property set GUID.
    /// </summary>
    private static bool ObjectTypeMatchesAttribute(Guid aceObjectType, Guid? attrSchemaIdGuid, Guid? attrSecurityGuid)
    {
        if (attrSchemaIdGuid.HasValue && aceObjectType == attrSchemaIdGuid.Value)
            return true;
        if (attrSecurityGuid.HasValue && aceObjectType == attrSecurityGuid.Value)
            return true;
        return false;
    }

    #endregion

    #region Effective Access

    public int GetEffectiveAccess(string callerSid, IReadOnlySet<string> callerGroupSids, DirectoryObject target)
    {
        var sd = GetSecurityDescriptor(target);
        if (sd?.Dacl is null)
            return ~0; // No DACL = full access

        int allowed = 0;
        int denied = 0;

        bool isOwner = string.Equals(sd.OwnerSid, callerSid, StringComparison.OrdinalIgnoreCase);

        // Owner implicit rights
        if (isOwner)
            allowed |= AccessMask.ReadControl | AccessMask.WriteDacl;

        foreach (var ace in sd.Dacl.Aces)
        {
            if (!SidMatches(ace.TrusteeSid, callerSid, callerGroupSids, isOwner))
                continue;

            if ((ace.Flags & AceFlags.InheritOnly) != 0)
                continue;

            switch (ace.Type)
            {
                case AceType.AccessDenied:
                case AceType.AccessDeniedObject:
                    denied |= ace.Mask;
                    break;
                case AceType.AccessAllowed:
                case AceType.AccessAllowedObject:
                    allowed |= ace.Mask;
                    break;
            }
        }

        return allowed & ~denied;
    }

    public int GetEffectiveAccess(string callerSid, DirectoryObject target)
    {
        return GetEffectiveAccess(callerSid, EmptyGroupSids, target);
    }

    #endregion

    #region Default SD & Inheritance

    public SecurityDescriptor GetDefaultSecurityDescriptor(string objectClassName, string ownerSid, string domainSid)
    {
        _logger.LogDebug("Creating default SD for class {Class}, owner {Owner}", objectClassName, ownerSid);

        // Walk the class hierarchy to find the most specific DefaultSecurityDescriptor SDDL
        string sddl = null;
        var current = _schema.GetObjectClass(objectClassName);
        while (current is not null)
        {
            if (!string.IsNullOrEmpty(current.DefaultSecurityDescriptor))
            {
                sddl = current.DefaultSecurityDescriptor;
                break;
            }
            current = current.SuperiorClass is not null ? _schema.GetObjectClass(current.SuperiorClass) : null;
        }

        if (!string.IsNullOrEmpty(sddl))
        {
            var sd = SddlParser.Parse(sddl, domainSid);
            sd.OwnerSid ??= ownerSid;
            sd.GroupSid ??= domainSid.Length > 0 ? WellKnownSids.DomainAdmins(domainSid) : WellKnownSids.Administrators;
            return sd;
        }

        // Fallback: generic default when no class-specific SDDL is defined
        var dacl = new AccessControlList
        {
            Aces =
            [
                new AccessControlEntry
                {
                    Type = AceType.AccessAllowed,
                    Flags = AceFlags.ContainerInherit,
                    Mask = AccessMask.GenericAll,
                    TrusteeSid = WellKnownSids.Administrators
                },
                new AccessControlEntry
                {
                    Type = AceType.AccessAllowed,
                    Flags = AceFlags.ContainerInherit,
                    Mask = AccessMask.GenericAll,
                    TrusteeSid = WellKnownSids.System
                },
                new AccessControlEntry
                {
                    Type = AceType.AccessAllowed,
                    Flags = AceFlags.ContainerInherit,
                    Mask = AccessMask.ReadProperty | AccessMask.ListContents | AccessMask.ListObject | AccessMask.ReadControl,
                    TrusteeSid = WellKnownSids.AuthenticatedUsers
                }
            ]
        };

        return new SecurityDescriptor
        {
            OwnerSid = ownerSid,
            GroupSid = domainSid.Length > 0 ? WellKnownSids.DomainAdmins(domainSid) : WellKnownSids.Administrators,
            Dacl = dacl,
            Control = SdControlFlags.DaclPresent
        };
    }

    /// <summary>
    /// Inherit ACEs from parent to child, with InheritedObjectType filtering (Part 3).
    /// </summary>
    public SecurityDescriptor InheritAces(
        SecurityDescriptor parentSd, SecurityDescriptor childSd,
        string childObjectClassName = null, ISchemaService schema = null)
    {
        if (parentSd.Dacl is null)
            return childSd;

        childSd.Dacl ??= new AccessControlList();

        if (childSd.Control.HasFlag(SdControlFlags.DaclProtected))
        {
            _logger.LogDebug("Child DACL is protected, skipping ACE inheritance");
            return childSd;
        }

        // Resolve child class SchemaIDGUID for InheritedObjectType filtering
        Guid? childClassGuid = null;
        if (childObjectClassName != null && schema != null)
        {
            var classDef = schema.GetObjectClass(childObjectClassName);
            childClassGuid = classDef?.SchemaIDGUID;

            // Also check the class hierarchy — an ACE targeting "person" should also apply to "user"
            // We collect all class GUIDs in the hierarchy
            var classGuids = new HashSet<Guid>();
            var current = classDef;
            while (current != null)
            {
                if (current.SchemaIDGUID.HasValue)
                    classGuids.Add(current.SchemaIDGUID.Value);
                current = current.SuperiorClass != null ? schema.GetObjectClass(current.SuperiorClass) : null;
            }

            // Remove previously inherited ACEs from child
            childSd.Dacl.Aces.RemoveAll(a => (a.Flags & AceFlags.Inherited) != 0);

            foreach (var parentAce in parentSd.Dacl.Aces)
            {
                bool inheritable = (parentAce.Flags & AceFlags.ContainerInherit) != 0
                                || (parentAce.Flags & AceFlags.ObjectInherit) != 0;

                if (!inheritable)
                    continue;

                // InheritedObjectType filtering: if set, only inherit to matching child classes
                if (parentAce.InheritedObjectType.HasValue)
                {
                    if (!classGuids.Contains(parentAce.InheritedObjectType.Value))
                        continue; // Skip — this ACE is not for this child class
                }

                var inherited = new AccessControlEntry
                {
                    Type = parentAce.Type,
                    Flags = (parentAce.Flags | AceFlags.Inherited) & ~AceFlags.InheritOnly,
                    Mask = parentAce.Mask,
                    TrusteeSid = parentAce.TrusteeSid,
                    ObjectType = parentAce.ObjectType,
                    InheritedObjectType = parentAce.InheritedObjectType
                };

                if ((parentAce.Flags & AceFlags.NoPropagateInherit) != 0)
                {
                    inherited.Flags &= ~(AceFlags.ContainerInherit | AceFlags.ObjectInherit | AceFlags.NoPropagateInherit);
                }

                childSd.Dacl.Aces.Add(inherited);
            }

            childSd.Control |= SdControlFlags.DaclAutoInherited;
            return childSd;
        }

        // Fallback: no schema info, inherit all (original behavior)
        childSd.Dacl.Aces.RemoveAll(a => (a.Flags & AceFlags.Inherited) != 0);

        foreach (var parentAce in parentSd.Dacl.Aces)
        {
            bool inheritable = (parentAce.Flags & AceFlags.ContainerInherit) != 0
                            || (parentAce.Flags & AceFlags.ObjectInherit) != 0;

            if (!inheritable)
                continue;

            var inherited = new AccessControlEntry
            {
                Type = parentAce.Type,
                Flags = (parentAce.Flags | AceFlags.Inherited) & ~AceFlags.InheritOnly,
                Mask = parentAce.Mask,
                TrusteeSid = parentAce.TrusteeSid,
                ObjectType = parentAce.ObjectType,
                InheritedObjectType = parentAce.InheritedObjectType
            };

            if ((parentAce.Flags & AceFlags.NoPropagateInherit) != 0)
            {
                inherited.Flags &= ~(AceFlags.ContainerInherit | AceFlags.ObjectInherit | AceFlags.NoPropagateInherit);
            }

            childSd.Dacl.Aces.Add(inherited);
        }

        childSd.Control |= SdControlFlags.DaclAutoInherited;
        return childSd;
    }

    #endregion

    #region SID Matching

    private static bool SidMatches(string aceSid, string callerSid, IReadOnlySet<string> callerGroupSids, bool isOwner)
    {
        // Direct match
        if (string.Equals(callerSid, aceSid, StringComparison.OrdinalIgnoreCase))
            return true;

        // CreatorOwner matches the object owner
        if (string.Equals(aceSid, WellKnownSids.CreatorOwner, StringComparison.OrdinalIgnoreCase) && isOwner)
            return true;

        // Self matches when the caller IS the target object
        if (string.Equals(aceSid, WellKnownSids.Self, StringComparison.OrdinalIgnoreCase)
            && string.Equals(callerSid, callerSid, StringComparison.OrdinalIgnoreCase))
        {
            // Self is handled by the caller passing their own SID — it matches via group membership
        }

        // Everyone matches all
        if (string.Equals(aceSid, WellKnownSids.Everyone, StringComparison.OrdinalIgnoreCase))
            return true;

        // Authenticated Users matches any non-anonymous SID
        if (string.Equals(aceSid, WellKnownSids.AuthenticatedUsers, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(callerSid, "S-1-5-7", StringComparison.OrdinalIgnoreCase)) // Anonymous
            return true;

        // Group membership expansion: check if the ACE trustee SID is in caller's group set
        if (callerGroupSids.Contains(aceSid))
            return true;

        return false;
    }

    #endregion

    #region Helpers

    private static SecurityDescriptor GetSecurityDescriptor(DirectoryObject obj)
    {
        var attr = obj.GetAttribute("nTSecurityDescriptor");
        if (attr?.GetFirstBytes() is { } bytes)
            return SecurityDescriptor.Deserialize(bytes);

        return null;
    }

    private static readonly IReadOnlySet<string> EmptyGroupSids = new HashSet<string>();

    #endregion
}
