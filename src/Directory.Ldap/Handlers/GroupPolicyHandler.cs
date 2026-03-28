using Directory.Core.Interfaces;
using Directory.Core.Models;
using Microsoft.Extensions.Logging;

namespace Directory.Ldap.Handlers;

/// <summary>
/// Group Policy Object (GPO) processing per [MS-GPOL].
/// Handles gPLink attribute parsing, GPO ordering, and SYSVOL path resolution.
/// </summary>
public class GroupPolicyHandler
{
    private readonly IDirectoryStore _store;
    private readonly ILogger<GroupPolicyHandler> _logger;

    public GroupPolicyHandler(IDirectoryStore store, ILogger<GroupPolicyHandler> logger)
    {
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// Get the ordered list of GPOs that apply to a given object (user or computer).
    /// Processing order: Site -> Domain -> OU hierarchy (closest to object last = highest priority).
    /// </summary>
    public async Task<IReadOnlyList<AppliedGpo>> GetAppliedGposAsync(
        string tenantId, DirectoryObject target, CancellationToken ct = default)
    {
        var result = new List<AppliedGpo>();
        var dn = DistinguishedName.Parse(target.DistinguishedName);

        // Walk up the DN hierarchy from domain root to the target's parent
        var containerDns = new List<string>();
        var current = dn.Parent();
        while (current != null && current.Components.Count > 0)
        {
            containerDns.Add(current.ToString());
            current = current.Parent();
        }

        // Reverse so we process from root (domain) down to nearest parent
        containerDns.Reverse();

        foreach (var containerDn in containerDns)
        {
            var container = await _store.GetByDnAsync(tenantId, containerDn, ct);
            if (container == null) continue;

            var gpLink = container.GPLink;
            if (string.IsNullOrEmpty(gpLink)) continue;

            var links = ParseGpLink(gpLink);
            foreach (var link in links)
            {
                if (link.IsDisabled) continue;

                var gpo = await _store.GetByDnAsync(tenantId, link.GpoDn, ct);
                if (gpo == null) continue;

                result.Add(new AppliedGpo
                {
                    GpoDn = link.GpoDn,
                    DisplayName = gpo.DisplayName ?? gpo.Cn ?? "",
                    FileSysPath = gpo.GPCFileSysPath ?? "",
                    IsEnforced = link.IsEnforced,
                    LinkOrder = result.Count,
                    SourceContainerDn = containerDn,
                });
            }
        }

        return result;
    }

    /// <summary>
    /// Parse the gPLink attribute value.
    /// Format: [LDAP://cn=...,cn=policies,cn=system,...;options][LDAP://...;options]
    /// Options: 0 = enabled, 1 = disabled, 2 = enforced
    /// </summary>
    public static IReadOnlyList<GpLinkEntry> ParseGpLink(string gpLink)
    {
        var result = new List<GpLinkEntry>();
        if (string.IsNullOrEmpty(gpLink)) return result;

        var entries = gpLink.Split(']', StringSplitOptions.RemoveEmptyEntries);
        foreach (var entry in entries)
        {
            var trimmed = entry.TrimStart('[');
            var semiColon = trimmed.LastIndexOf(';');
            if (semiColon < 0) continue;

            var path = trimmed[..semiColon];
            var optionsStr = trimmed[(semiColon + 1)..];
            int.TryParse(optionsStr, out var options);

            // Strip LDAP:// prefix
            if (path.StartsWith("LDAP://", StringComparison.OrdinalIgnoreCase))
                path = path[7..];
            else if (path.StartsWith("ldap://", StringComparison.OrdinalIgnoreCase))
                path = path[7..];

            result.Add(new GpLinkEntry
            {
                GpoDn = path,
                Options = options,
                IsDisabled = (options & 1) != 0,
                IsEnforced = (options & 2) != 0,
            });
        }

        return result;
    }

    /// <summary>
    /// Get the SYSVOL path for a GPO.
    /// Standard format: \\domain\SYSVOL\domain\Policies\{GPO-GUID}
    /// </summary>
    public static string GetSysvolPath(string domainDnsName, string gpoGuid)
    {
        return $@"\\{domainDnsName}\SYSVOL\{domainDnsName}\Policies\{{{gpoGuid}}}";
    }
}

public class GpLinkEntry
{
    public string GpoDn { get; init; } = "";
    public int Options { get; init; }
    public bool IsDisabled { get; init; }
    public bool IsEnforced { get; init; }
}

public class AppliedGpo
{
    public string GpoDn { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string FileSysPath { get; init; } = "";
    public bool IsEnforced { get; init; }
    public int LinkOrder { get; init; }
    public string SourceContainerDn { get; init; } = "";
}
