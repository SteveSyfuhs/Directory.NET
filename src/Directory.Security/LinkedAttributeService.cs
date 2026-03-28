using Directory.Core.Interfaces;
using Directory.Core.Models;
using Microsoft.Extensions.Logging;

namespace Directory.Security;

public class LinkedAttributeService : ILinkedAttributeService
{
    private readonly IDirectoryStore _store;
    private readonly ILogger<LinkedAttributeService> _logger;
    private readonly GroupMembershipMaterializer _materializer;
    private readonly Dictionary<string, LinkedAttributeDefinition> _byForward;
    private readonly Dictionary<string, LinkedAttributeDefinition> _byBack;
    private readonly List<LinkedAttributeDefinition> _allLinks;

    public LinkedAttributeService(IDirectoryStore store, ILogger<LinkedAttributeService> logger, GroupMembershipMaterializer materializer)
    {
        _store = store;
        _logger = logger;
        _materializer = materializer;

        _allLinks =
        [
            WellKnownLinks.MemberOf,
            WellKnownLinks.ManagerDirectReports,
            WellKnownLinks.MsDsMembersOfResourcePropertyList,
            WellKnownLinks.MsDsAllowedToActOnBehalf
        ];

        _byForward = new Dictionary<string, LinkedAttributeDefinition>(StringComparer.OrdinalIgnoreCase);
        _byBack = new Dictionary<string, LinkedAttributeDefinition>(StringComparer.OrdinalIgnoreCase);

        foreach (var link in _allLinks)
        {
            _byForward[link.ForwardLinkName] = link;
            if (link.BackLinkName is not null)
                _byBack[link.BackLinkName] = link;
        }
    }

    public LinkedAttributeDefinition GetLinkDefinition(string attributeName)
    {
        if (_byForward.TryGetValue(attributeName, out var def))
            return def;

        _byBack.TryGetValue(attributeName, out def);
        return def;
    }

    public bool IsForwardLink(string attributeName) => _byForward.ContainsKey(attributeName);

    public bool IsBackLink(string attributeName) => _byBack.ContainsKey(attributeName);

    public IReadOnlyList<LinkedAttributeDefinition> GetAllLinks() => _allLinks;

    public async Task UpdateForwardLinkAsync(
        string tenantId,
        DirectoryObject source,
        string forwardLinkAttr,
        string targetDn,
        bool add,
        CancellationToken ct = default)
    {
        if (!_byForward.TryGetValue(forwardLinkAttr, out var linkDef) || linkDef.BackLinkName is null)
        {
            _logger.LogDebug("No back link defined for forward link {Attr}", forwardLinkAttr);
            return;
        }

        var target = await _store.GetByDnAsync(tenantId, targetDn, ct);
        if (target is null)
        {
            _logger.LogWarning("Target object {Dn} not found for back link update", targetDn);
            return;
        }

        string backLinkName = linkDef.BackLinkName;
        string sourceDn = source.DistinguishedName;

        // Update the back link attribute on the target object
        if (string.Equals(backLinkName, "memberOf", StringComparison.OrdinalIgnoreCase))
        {
            if (add)
            {
                if (!target.MemberOf.Contains(sourceDn, StringComparer.OrdinalIgnoreCase))
                    target.MemberOf.Add(sourceDn);
            }
            else
            {
                target.MemberOf.RemoveAll(m => string.Equals(m, sourceDn, StringComparison.OrdinalIgnoreCase));
            }
        }
        else
        {
            var attr = target.GetAttribute(backLinkName);
            var values = attr?.GetStrings().ToList() ?? [];

            if (add)
            {
                if (!values.Contains(sourceDn, StringComparer.OrdinalIgnoreCase))
                    values.Add(sourceDn);
            }
            else
            {
                values.RemoveAll(v => string.Equals(v, sourceDn, StringComparison.OrdinalIgnoreCase));
            }

            target.SetAttribute(backLinkName, new DirectoryAttribute(backLinkName, [.. values.Cast<object>()]));
        }

        target.WhenChanged = DateTimeOffset.UtcNow;

        await _store.UpdateAsync(target, ct);

        _logger.LogDebug("Updated back link {BackLink} on {Target}: {Op} {Source}",
            backLinkName, targetDn, add ? "add" : "remove", sourceDn);

        // Trigger async materialized view update when group membership changes
        if (string.Equals(forwardLinkAttr, "member", StringComparison.OrdinalIgnoreCase))
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _materializer.OnGroupMembershipChangedAsync(
                        tenantId,
                        source.DistinguishedName,
                        add ? targetDn : null,
                        add ? null : targetDn,
                        CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to update materialized group memberships for {Group}", source.DistinguishedName);
                }
            });
        }
    }
}
