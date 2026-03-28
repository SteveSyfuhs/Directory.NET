using Directory.Core.Interfaces;
using Directory.Core.Models;
using Microsoft.Extensions.Logging;

namespace Directory.Security;

/// <summary>
/// Materializes transitive group memberships into DirectoryObject.TokenGroupsSids.
/// When a group's 'member' attribute changes, this service:
/// 1. Identifies all affected users/groups (direct and transitive members)
/// 2. Recomputes their flattened group SIDs
/// 3. Updates the TokenGroupsSids field on each affected object
///
/// This trades write amplification for read performance:
/// - Write: O(affected_members) updates when a group changes
/// - Read: O(1) to get any user's complete group membership
/// </summary>
public class GroupMembershipMaterializer
{
    private readonly IDirectoryStore _store;
    private readonly ILogger<GroupMembershipMaterializer> _logger;

    public GroupMembershipMaterializer(IDirectoryStore store, ILogger<GroupMembershipMaterializer> logger)
    {
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// Called when a group's membership changes (member added or removed).
    /// Recomputes TokenGroupsSids for all affected members.
    /// </summary>
    public async Task OnGroupMembershipChangedAsync(
        string tenantId,
        string groupDn,
        string addedMemberDn,
        string removedMemberDn,
        CancellationToken ct = default)
    {
        // Determine which members need recomputation
        var affectedDns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (addedMemberDn != null)
            await CollectAffectedMembersAsync(tenantId, addedMemberDn, affectedDns, ct);
        if (removedMemberDn != null)
            await CollectAffectedMembersAsync(tenantId, removedMemberDn, affectedDns, ct);

        _logger.LogDebug("Group {Group} membership changed, recomputing {Count} affected members",
            groupDn, affectedDns.Count);

        // Recompute each affected member's TokenGroupsSids
        // Process in parallel batches for performance
        foreach (var batch in affectedDns.Chunk(20))
        {
            var tasks = batch.Select(dn => RecomputeTokenGroupsAsync(tenantId, dn, ct));
            await Task.WhenAll(tasks);
        }
    }

    /// <summary>
    /// Recompute TokenGroupsSids for a single object by doing a full BFS expansion.
    /// This is the expensive operation, but it only happens on writes, not reads.
    /// </summary>
    public async Task RecomputeTokenGroupsAsync(string tenantId, string objectDn, CancellationToken ct = default)
    {
        var obj = await _store.GetByDnAsync(tenantId, objectDn, ct);
        if (obj == null) return;

        // Only compute for security principals (users, computers, groups)
        if (!obj.ObjectClass.Contains("user") && !obj.ObjectClass.Contains("computer") && !obj.ObjectClass.Contains("group"))
            return;

        var groupSids = await ExpandGroupSidsAsync(tenantId, obj, ct);

        // Add primary group SID
        if (obj.PrimaryGroupId > 0 && !string.IsNullOrEmpty(obj.ObjectSid))
        {
            var domainSidParts = obj.ObjectSid.Split('-');
            if (domainSidParts.Length > 4)
            {
                var domainSid = string.Join("-", domainSidParts[..^1]);
                groupSids.Add($"{domainSid}-{obj.PrimaryGroupId}");
            }
        }

        obj.TokenGroupsSids = groupSids.Distinct().OrderBy(s => s).ToList();
        obj.TokenGroupsLastComputed = DateTimeOffset.UtcNow;

        await _store.UpdateAsync(obj, ct);
    }

    /// <summary>
    /// BFS expansion of all transitive group SIDs for an object.
    /// </summary>
    private async Task<List<string>> ExpandGroupSidsAsync(
        string tenantId, DirectoryObject obj, CancellationToken ct)
    {
        var sids = new List<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>(obj.MemberOf ?? []);

        while (queue.Count > 0)
        {
            // Batch this level
            var batch = new List<string>();
            while (queue.Count > 0)
            {
                var dn = queue.Dequeue();
                if (visited.Add(dn))
                    batch.Add(dn);
            }

            if (batch.Count == 0) break;

            var groups = await _store.GetByDnsAsync(tenantId, batch, ct);

            foreach (var group in groups)
            {
                if (!string.IsNullOrEmpty(group.ObjectSid))
                    sids.Add(group.ObjectSid);

                foreach (var nestedDn in group.MemberOf ?? [])
                {
                    if (!visited.Contains(nestedDn))
                        queue.Enqueue(nestedDn);
                }
            }
        }

        return sids;
    }

    /// <summary>
    /// Collect all members that need recomputation (the member itself, plus if it's a group, all its transitive members).
    /// </summary>
    private async Task CollectAffectedMembersAsync(
        string tenantId, string memberDn, HashSet<string> affected, CancellationToken ct)
    {
        affected.Add(memberDn);

        var member = await _store.GetByDnAsync(tenantId, memberDn, ct);
        if (member == null) return;

        // If the affected member is itself a group, all its members are also affected
        if (member.ObjectClass.Contains("group"))
        {
            var queue = new Queue<string>(member.Member ?? []);
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { memberDn };

            while (queue.Count > 0)
            {
                var dn = queue.Dequeue();
                if (!visited.Add(dn)) continue;
                affected.Add(dn);

                var nested = await _store.GetByDnAsync(tenantId, dn, ct);
                if (nested?.ObjectClass.Contains("group") == true)
                {
                    foreach (var m in nested.Member ?? [])
                        queue.Enqueue(m);
                }
            }
        }
    }

    /// <summary>
    /// Bulk recompute all objects in a domain. Used during initial setup or migration.
    /// </summary>
    public async Task RecomputeAllAsync(string tenantId, string domainDn, CancellationToken ct = default)
    {
        _logger.LogInformation("Starting bulk TokenGroupsSids recomputation for {Domain}", domainDn);

        var result = await _store.SearchAsync(tenantId, domainDn, SearchScope.WholeSubtree,
            null, ["distinguishedName", "objectClass"], ct: ct);

        int count = 0;
        foreach (var batch in result.Entries.Where(e =>
            e.ObjectClass.Contains("user") || e.ObjectClass.Contains("computer") || e.ObjectClass.Contains("group"))
            .Chunk(20))
        {
            var tasks = batch.Select(obj => RecomputeTokenGroupsAsync(tenantId, obj.DistinguishedName, ct));
            await Task.WhenAll(tasks);
            count += batch.Length;
        }

        _logger.LogInformation("Completed bulk recomputation: {Count} objects updated", count);
    }
}
