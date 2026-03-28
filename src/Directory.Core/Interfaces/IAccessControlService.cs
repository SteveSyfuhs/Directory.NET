using Directory.Core.Models;

namespace Directory.Core.Interfaces;

public interface IAccessControlService
{
    /// <summary>
    /// Check access using caller's transitive group SIDs for proper group membership expansion.
    /// </summary>
    bool CheckAccess(string callerSid, IReadOnlySet<string> callerGroupSids, DirectoryObject target, int desiredAccess);

    /// <summary>
    /// Legacy overload — delegates to the group-aware version with an empty group set.
    /// </summary>
    bool CheckAccess(string callerSid, DirectoryObject target, int desiredAccess);

    /// <summary>
    /// Check attribute-level access with group membership expansion and object-specific ACE support.
    /// </summary>
    bool CheckAttributeAccess(string callerSid, IReadOnlySet<string> callerGroupSids, DirectoryObject target, string attributeName, bool isWrite, ISchemaService schema = null);

    /// <summary>
    /// Legacy overload — delegates to the group-aware version with an empty group set.
    /// </summary>
    bool CheckAttributeAccess(string callerSid, DirectoryObject target, string attributeName, bool isWrite);

    int GetEffectiveAccess(string callerSid, IReadOnlySet<string> callerGroupSids, DirectoryObject target);

    int GetEffectiveAccess(string callerSid, DirectoryObject target);

    SecurityDescriptor GetDefaultSecurityDescriptor(string objectClassName, string ownerSid, string domainSid);

    SecurityDescriptor InheritAces(SecurityDescriptor parentSd, SecurityDescriptor childSd, string childObjectClassName = null, ISchemaService schema = null);

    /// <summary>
    /// Resolve a caller's full set of group SIDs (transitive memberships + well-known SIDs).
    /// </summary>
    Task<IReadOnlySet<string>> ResolveCallerGroupsAsync(string callerSid, IDirectoryStore store, string tenantId, CancellationToken ct = default);

    /// <summary>
    /// Returns true if the caller is SYSTEM or a Domain Admin (fast-path for admin bypass).
    /// </summary>
    bool IsAdminOrSystem(string callerSid, IReadOnlySet<string> callerGroupSids);
}
