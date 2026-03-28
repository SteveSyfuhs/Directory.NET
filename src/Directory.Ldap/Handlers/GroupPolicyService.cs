using System.Text.Json;
using System.Text.Json.Serialization;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Microsoft.Extensions.Logging;

namespace Directory.Ldap.Handlers;

/// <summary>
/// Group Policy processing service implementing RSoP (Resultant Set of Policy)
/// computation per [MS-GPOL] sections 3.2.5.1 through 3.2.5.4.
/// Replaces SYSVOL file-based policy storage with CosmosDB-backed JSON policy data.
/// </summary>
public class GroupPolicyService
{
    private readonly IDirectoryStore _store;
    private readonly INamingContextService _ncService;
    private readonly GroupPolicyHandler _gpHandler;
    private readonly ILogger<GroupPolicyService> _logger;

    /// <summary>
    /// Optional delegate that resolves the configured SMB server hostname for SYSVOL.
    /// When set, GPO gPCFileSysPath attributes use this hostname instead of the domain DNS name.
    /// Format: just the hostname, e.g. "dc01.contoso.com".
    /// Set by the web layer after reading SYSVOL configuration from the config store.
    /// </summary>
    public Func<string> SysvolHostnameProvider { get; set; }

    public GroupPolicyService(
        IDirectoryStore store,
        INamingContextService ncService,
        GroupPolicyHandler gpHandler,
        ILogger<GroupPolicyService> logger)
    {
        _store = store;
        _ncService = ncService;
        _gpHandler = gpHandler;
        _logger = logger;
    }

    /// <summary>
    /// Get all GPOs in the domain.
    /// </summary>
    public async Task<IReadOnlyList<GpoSummary>> GetAllGposAsync(CancellationToken ct = default)
    {
        var domainDn = _ncService.GetDomainNc().Dn;
        var policiesDn = $"CN=Policies,CN=System,{domainDn}";

        var filter = new EqualityFilterNode("objectClass", "groupPolicyContainer");
        var result = await _store.SearchAsync("default", policiesDn, SearchScope.SingleLevel, filter,
            null, pageSize: 1000, ct: ct);

        var gpos = new List<GpoSummary>();
        foreach (var entry in result.Entries.Where(e => !e.IsDeleted))
        {
            var settings = ExtractPolicySettings(entry);
            var flags = GetGpoFlags(entry);
            var links = await GetGpoLinksAsync(entry.DistinguishedName, ct);

            gpos.Add(new GpoSummary
            {
                Dn = entry.DistinguishedName,
                ObjectGuid = entry.ObjectGuid,
                DisplayName = entry.DisplayName ?? entry.Cn ?? "",
                Cn = entry.Cn ?? "",
                Flags = flags,
                IsUserEnabled = (flags & 1) == 0,
                IsComputerEnabled = (flags & 2) == 0,
                VersionNumber = GetVersionNumber(entry),
                LinkCount = links.Count,
                WhenCreated = entry.WhenCreated,
                WhenChanged = entry.WhenChanged,
            });
        }

        return gpos;
    }

    /// <summary>
    /// Get detailed GPO info including policy settings.
    /// </summary>
    public async Task<GpoDetail> GetGpoAsync(string guidOrDn, CancellationToken ct = default)
    {
        var gpo = await ResolveGpoAsync(guidOrDn, ct);
        if (gpo == null) return null;

        var settings = ExtractPolicySettings(gpo);
        var flags = GetGpoFlags(gpo);
        var links = await GetGpoLinksAsync(gpo.DistinguishedName, ct);
        var securityFiltering = ExtractSecurityFiltering(gpo);

        return new GpoDetail
        {
            Dn = gpo.DistinguishedName,
            ObjectGuid = gpo.ObjectGuid,
            DisplayName = gpo.DisplayName ?? gpo.Cn ?? "",
            Cn = gpo.Cn ?? "",
            Flags = flags,
            IsUserEnabled = (flags & 1) == 0,
            IsComputerEnabled = (flags & 2) == 0,
            VersionNumber = GetVersionNumber(gpo),
            UserVersion = GetVersionNumber(gpo) >> 16,
            ComputerVersion = GetVersionNumber(gpo) & 0xFFFF,
            GPCFileSysPath = gpo.GPCFileSysPath ?? "",
            GPCMachineExtensionNames = gpo.GetAttribute("gPCMachineExtensionNames")?.GetFirstString() ?? "",
            GPCUserExtensionNames = gpo.GetAttribute("gPCUserExtensionNames")?.GetFirstString() ?? "",
            WhenCreated = gpo.WhenCreated,
            WhenChanged = gpo.WhenChanged,
            Links = links,
            SecurityFiltering = securityFiltering,
            PolicySettings = settings,
        };
    }

    /// <summary>
    /// Create a new GPO.
    /// </summary>
    public async Task<GpoDetail> CreateGpoAsync(string displayName, GpoPolicySettings initialSettings = null, CancellationToken ct = default)
    {
        var domainDn = _ncService.GetDomainNc().Dn;
        var gpoGuid = Guid.NewGuid().ToString("B").ToUpperInvariant(); // {GUID} format
        var dn = $"CN={gpoGuid},CN=Policies,CN=System,{domainDn}";

        var now = DateTimeOffset.UtcNow;
        var usn = await _store.GetNextUsnAsync("default", domainDn, ct);

        var gpo = new DirectoryObject
        {
            Id = dn.ToLowerInvariant(),
            TenantId = "default",
            DistinguishedName = dn,
            DomainDn = domainDn,
            ObjectCategory = "groupPolicyContainer",
            ObjectClass = ["top", "container", "groupPolicyContainer"],
            Cn = gpoGuid,
            DisplayName = displayName,
            ParentDn = $"CN=Policies,CN=System,{domainDn}",
            WhenCreated = now,
            WhenChanged = now,
            USNCreated = usn,
            USNChanged = usn,
        };

        // Set GPO-specific attributes
        gpo.SetAttribute("flags", new DirectoryAttribute("flags", 0)); // 0 = all enabled
        gpo.SetAttribute("versionNumber", new DirectoryAttribute("versionNumber", 0));
        gpo.GPCFileSysPath = BuildGpcFileSysPath(gpoGuid);
        gpo.SetAttribute("gPCMachineExtensionNames", new DirectoryAttribute("gPCMachineExtensionNames", ""));
        gpo.SetAttribute("gPCUserExtensionNames", new DirectoryAttribute("gPCUserExtensionNames", ""));

        // Store policy settings as JSON in an attribute
        var settings = initialSettings ?? new GpoPolicySettings();
        StorePolicySettings(gpo, settings);

        await _store.CreateAsync(gpo, ct);

        return (await GetGpoAsync(gpo.ObjectGuid, ct));
    }

    /// <summary>
    /// Update GPO settings.
    /// </summary>
    public async Task<GpoDetail> UpdateGpoAsync(
        string guidOrDn, string displayName, int? flags,
        GpoPolicySettings settings, CancellationToken ct = default)
    {
        var gpo = await ResolveGpoAsync(guidOrDn, ct);
        if (gpo == null) return null;

        if (displayName != null)
            gpo.DisplayName = displayName;

        if (flags.HasValue)
            gpo.SetAttribute("flags", new DirectoryAttribute("flags", flags.Value));

        if (settings != null)
        {
            StorePolicySettings(gpo, settings);
            // Increment version number
            var version = GetVersionNumber(gpo);
            version++; // Simple increment; in real AD the high/low words are user/computer versions
            gpo.SetAttribute("versionNumber", new DirectoryAttribute("versionNumber", version));
        }

        gpo.WhenChanged = DateTimeOffset.UtcNow;
        await _store.UpdateAsync(gpo, ct);

        return await GetGpoAsync(gpo.ObjectGuid, ct);
    }

    /// <summary>
    /// Delete a GPO and remove all links to it.
    /// </summary>
    public async Task<bool> DeleteGpoAsync(string guidOrDn, CancellationToken ct = default)
    {
        var gpo = await ResolveGpoAsync(guidOrDn, ct);
        if (gpo == null) return false;

        // Remove all links pointing to this GPO
        var links = await GetGpoLinksAsync(gpo.DistinguishedName, ct);
        foreach (var link in links)
        {
            await UnlinkGpoAsync(gpo.ObjectGuid, link.TargetDn, ct);
        }

        await _store.DeleteAsync("default", gpo.DistinguishedName, hardDelete: true, ct);
        return true;
    }

    /// <summary>
    /// Link a GPO to a target container (OU, domain, or site).
    /// </summary>
    public async Task<bool> LinkGpoAsync(string guidOrDn, string targetDn, bool enforced = false, CancellationToken ct = default)
    {
        var gpo = await ResolveGpoAsync(guidOrDn, ct);
        if (gpo == null) return false;

        var target = await _store.GetByDnAsync("default", targetDn, ct);
        if (target == null) return false;

        var gpLink = target.GPLink ?? "";
        var gpoDn = gpo.DistinguishedName;

        // Check if already linked
        var existingLinks = GroupPolicyHandler.ParseGpLink(gpLink);
        if (existingLinks.Any(l => string.Equals(l.GpoDn, gpoDn, StringComparison.OrdinalIgnoreCase)))
            return true; // Already linked

        // Append new link: options = 0 (enabled, not enforced) or 2 (enforced)
        var options = enforced ? 2 : 0;
        gpLink += $"[LDAP://{gpoDn};{options}]";

        target.GPLink = gpLink;
        target.WhenChanged = DateTimeOffset.UtcNow;
        await _store.UpdateAsync(target, ct);

        return true;
    }

    /// <summary>
    /// Unlink a GPO from a target container.
    /// </summary>
    public async Task<bool> UnlinkGpoAsync(string guidOrDn, string targetDn, CancellationToken ct = default)
    {
        var gpo = await ResolveGpoAsync(guidOrDn, ct);
        if (gpo == null) return false;

        var target = await _store.GetByDnAsync("default", targetDn, ct);
        if (target == null) return false;

        var gpLink = target.GPLink;
        if (string.IsNullOrEmpty(gpLink)) return false;

        var links = GroupPolicyHandler.ParseGpLink(gpLink);
        var gpoDn = gpo.DistinguishedName;
        var filtered = links.Where(l => !string.Equals(l.GpoDn, gpoDn, StringComparison.OrdinalIgnoreCase)).ToList();

        if (filtered.Count == links.Count)
            return false; // Was not linked

        // Rebuild gPLink string
        target.GPLink = BuildGpLinkString(filtered);
        target.WhenChanged = DateTimeOffset.UtcNow;
        await _store.UpdateAsync(target, ct);

        return true;
    }

    /// <summary>
    /// Get policy settings from a GPO.
    /// </summary>
    public async Task<GpoPolicySettings> GetPolicySettingsAsync(string guidOrDn, CancellationToken ct = default)
    {
        var gpo = await ResolveGpoAsync(guidOrDn, ct);
        if (gpo == null) return null;

        return ExtractPolicySettings(gpo);
    }

    /// <summary>
    /// Compute the Resultant Set of Policy (RSoP) for a user/computer pair.
    /// Follows [MS-GPOL] processing order: Site -> Domain -> OU chain to target.
    /// Applies enforcement, blocking inheritance, and security filtering.
    /// </summary>
    public async Task<RsopResult> GetResultantSetOfPolicyAsync(
        string userDn, string computerDn, CancellationToken ct = default)
    {
        var result = new RsopResult();

        // Collect GPOs for user and computer separately
        if (!string.IsNullOrEmpty(userDn))
        {
            var userObj = await _store.GetByDnAsync("default", userDn, ct);
            if (userObj != null)
            {
                var appliedGpos = await _gpHandler.GetAppliedGposAsync("default", userObj, ct);
                var userGpos = await FilterAndResolveGposAsync(appliedGpos, userObj, isComputer: false, ct);
                result.UserGpos = userGpos;
                result.UserPolicy = MergeGpos(userGpos, isComputer: false);
            }
        }

        if (!string.IsNullOrEmpty(computerDn))
        {
            var computerObj = await _store.GetByDnAsync("default", computerDn, ct);
            if (computerObj != null)
            {
                var appliedGpos = await _gpHandler.GetAppliedGposAsync("default", computerObj, ct);
                var computerGpos = await FilterAndResolveGposAsync(appliedGpos, computerObj, isComputer: true, ct);
                result.ComputerGpos = computerGpos;
                result.ComputerPolicy = MergeGpos(computerGpos, isComputer: true);
            }
        }

        // Merge user + computer policy (computer settings take precedence where overlapping)
        result.MergedPolicy = MergeFinalPolicy(result.UserPolicy, result.ComputerPolicy);

        return result;
    }

    /// <summary>
    /// Merge multiple GPOs into a resultant policy. Last writer wins for conflicts.
    /// GPOs are processed in order: first in list = lowest priority, last = highest.
    /// Enforced GPOs override all non-enforced GPOs.
    /// </summary>
    public GpoPolicySettings MergeGpos(IReadOnlyList<RsopGpoEntry> gpoEntries, bool isComputer)
    {
        var result = new GpoPolicySettings();

        // First pass: apply non-enforced GPOs in order (last wins)
        foreach (var entry in gpoEntries.Where(e => !e.IsEnforced))
        {
            MergePolicyInto(result, entry.Settings);
        }

        // Second pass: enforced GPOs override everything (last enforced wins)
        foreach (var entry in gpoEntries.Where(e => e.IsEnforced))
        {
            MergePolicyInto(result, entry.Settings);
        }

        return result;
    }

    /// <summary>
    /// Get security filtering principals for a GPO.
    /// </summary>
    public async Task<IReadOnlyList<SecurityFilterEntry>> GetSecurityFilteringAsync(string guidOrDn, CancellationToken ct = default)
    {
        var gpo = await ResolveGpoAsync(guidOrDn, ct);
        if (gpo == null) return [];

        var dns = ExtractSecurityFiltering(gpo);
        var entries = new List<SecurityFilterEntry>();
        foreach (var dn in dns)
        {
            var obj = await _store.GetByDnAsync("default", dn, ct);
            entries.Add(new SecurityFilterEntry
            {
                Dn = dn,
                Name = obj?.DisplayName ?? obj?.Cn ?? dn,
                ObjectSid = obj?.ObjectSid ?? "",
                ObjectClass = obj?.ObjectClass.LastOrDefault() ?? "unknown",
            });
        }
        return entries;
    }

    /// <summary>
    /// Add a principal to GPO security filtering.
    /// </summary>
    public async Task<bool> AddSecurityFilterAsync(string guidOrDn, string principalDn, CancellationToken ct = default)
    {
        var gpo = await ResolveGpoAsync(guidOrDn, ct);
        if (gpo == null) return false;

        var existing = ExtractSecurityFiltering(gpo).ToList();
        if (existing.Any(e => string.Equals(e, principalDn, StringComparison.OrdinalIgnoreCase)))
            return true;

        existing.Add(principalDn);
        gpo.SetAttribute("gpoSecurityFiltering", new DirectoryAttribute("gpoSecurityFiltering", [.. existing.Cast<object>()]));
        gpo.WhenChanged = DateTimeOffset.UtcNow;
        await _store.UpdateAsync(gpo, ct);
        return true;
    }

    /// <summary>
    /// Remove a principal from GPO security filtering by SID or DN.
    /// </summary>
    public async Task<bool> RemoveSecurityFilterAsync(string guidOrDn, string sidOrDn, CancellationToken ct = default)
    {
        var gpo = await ResolveGpoAsync(guidOrDn, ct);
        if (gpo == null) return false;

        var existing = ExtractSecurityFiltering(gpo).ToList();
        var toRemove = existing.FirstOrDefault(e =>
            string.Equals(e, sidOrDn, StringComparison.OrdinalIgnoreCase));

        if (toRemove == null)
        {
            // Try matching by SID - resolve each DN to see if SID matches
            foreach (var dn in existing)
            {
                var obj = await _store.GetByDnAsync("default", dn, ct);
                if (obj?.ObjectSid != null && string.Equals(obj.ObjectSid, sidOrDn, StringComparison.OrdinalIgnoreCase))
                {
                    toRemove = dn;
                    break;
                }
            }
        }

        if (toRemove == null) return false;

        existing.Remove(toRemove);
        if (existing.Count > 0)
            gpo.SetAttribute("gpoSecurityFiltering", new DirectoryAttribute("gpoSecurityFiltering", [.. existing.Cast<object>()]));
        else
            gpo.Attributes.Remove("gpoSecurityFiltering");

        gpo.WhenChanged = DateTimeOffset.UtcNow;
        await _store.UpdateAsync(gpo, ct);
        return true;
    }

    /// <summary>
    /// Create a backup of a GPO.
    /// </summary>
    public async Task<GpoBackup> CreateBackupAsync(string guidOrDn, string description, CancellationToken ct = default)
    {
        var gpo = await ResolveGpoAsync(guidOrDn, ct);
        if (gpo == null) throw new InvalidOperationException("GPO not found");

        var detail = (await GetGpoAsync(gpo.ObjectGuid, ct));
        var backupId = Guid.NewGuid().ToString();
        var domainDn = _ncService.GetDomainNc().Dn;

        var backupObj = new DirectoryObject
        {
            Id = $"gpo-backup-{backupId}".ToLowerInvariant(),
            TenantId = "default",
            DistinguishedName = $"CN={backupId},CN=GpoBackups,CN=System,{domainDn}",
            DomainDn = domainDn,
            ObjectCategory = "gpoBackup",
            ObjectClass = ["top", "gpoBackup"],
            Cn = backupId,
            DisplayName = $"Backup of {detail.DisplayName}",
            Description = description ?? $"Backup created {DateTimeOffset.UtcNow:u}",
            ParentDn = $"CN=GpoBackups,CN=System,{domainDn}",
            WhenCreated = DateTimeOffset.UtcNow,
            WhenChanged = DateTimeOffset.UtcNow,
        };

        // Store the GPO data as JSON
        var backupData = new GpoBackupData
        {
            GpoGuid = gpo.ObjectGuid,
            GpoDn = gpo.DistinguishedName,
            DisplayName = detail.DisplayName,
            Flags = detail.Flags,
            PolicySettings = detail.PolicySettings,
            SecurityFiltering = detail.SecurityFiltering.ToList(),
        };

        var json = JsonSerializer.Serialize(backupData, JsonOptions);
        backupObj.SetAttribute("backupData", new DirectoryAttribute("backupData", json));
        backupObj.SetAttribute("sourceGpoGuid", new DirectoryAttribute("sourceGpoGuid", gpo.ObjectGuid));

        await _store.CreateAsync(backupObj, ct);

        return new GpoBackup
        {
            BackupId = backupId,
            GpoGuid = gpo.ObjectGuid,
            GpoDisplayName = detail.DisplayName,
            Description = backupObj.Description ?? "",
            CreatedAt = backupObj.WhenCreated,
        };
    }

    /// <summary>
    /// List all GPO backups.
    /// </summary>
    public async Task<IReadOnlyList<GpoBackup>> ListBackupsAsync(CancellationToken ct = default)
    {
        var domainDn = _ncService.GetDomainNc().Dn;
        var backupsDn = $"CN=GpoBackups,CN=System,{domainDn}";

        var filter = new EqualityFilterNode("objectClass", "gpoBackup");

        try
        {
            var result = await _store.SearchAsync("default", backupsDn, SearchScope.SingleLevel, filter,
                null, pageSize: 1000, ct: ct);

            return result.Entries.Where(e => !e.IsDeleted).Select(e =>
            {
                var dataJson = e.GetAttribute("backupData")?.GetFirstString();
                GpoBackupData data = null;
                if (!string.IsNullOrEmpty(dataJson))
                {
                    try { data = JsonSerializer.Deserialize<GpoBackupData>(dataJson, JsonOptions); } catch { }
                }

                return new GpoBackup
                {
                    BackupId = e.Cn ?? e.ObjectGuid,
                    GpoGuid = e.GetAttribute("sourceGpoGuid")?.GetFirstString() ?? "",
                    GpoDisplayName = data?.DisplayName ?? e.DisplayName ?? "",
                    Description = e.Description ?? "",
                    CreatedAt = e.WhenCreated,
                };
            }).OrderByDescending(b => b.CreatedAt).ToList();
        }
        catch
        {
            // Container may not exist yet
            return [];
        }
    }

    /// <summary>
    /// Restore a GPO from backup.
    /// </summary>
    public async Task<GpoDetail> RestoreBackupAsync(string backupId, CancellationToken ct = default)
    {
        var domainDn = _ncService.GetDomainNc().Dn;
        var backupDn = $"CN={backupId},CN=GpoBackups,CN=System,{domainDn}";

        var backupObj = await _store.GetByDnAsync("default", backupDn, ct);
        if (backupObj == null) return null;

        var dataJson = backupObj.GetAttribute("backupData")?.GetFirstString();
        if (string.IsNullOrEmpty(dataJson)) return null;

        var data = JsonSerializer.Deserialize<GpoBackupData>(dataJson, JsonOptions);
        if (data == null) return null;

        // Find the original GPO
        var gpo = await ResolveGpoAsync(data.GpoGuid, ct);
        if (gpo == null)
        {
            // GPO was deleted, recreate it
            var created = await CreateGpoAsync(data.DisplayName, data.PolicySettings, ct);
            return created;
        }

        // Restore settings
        return await UpdateGpoAsync(gpo.ObjectGuid, data.DisplayName, data.Flags, data.PolicySettings, ct);
    }

    /// <summary>
    /// List all WMI filters.
    /// </summary>
    public async Task<IReadOnlyList<WmiFilter>> ListWmiFiltersAsync(CancellationToken ct = default)
    {
        var domainDn = _ncService.GetDomainNc().Dn;
        var wmiDn = $"CN=SOM,CN=WMIPolicy,CN=System,{domainDn}";

        var filter = new EqualityFilterNode("objectClass", "msWMI-Som");

        try
        {
            var result = await _store.SearchAsync("default", wmiDn, SearchScope.SingleLevel, filter,
                null, pageSize: 1000, ct: ct);

            return result.Entries.Where(e => !e.IsDeleted).Select(e => new WmiFilter
            {
                Id = e.ObjectGuid,
                Name = e.GetAttribute("msWMI-Name")?.GetFirstString() ?? e.DisplayName ?? e.Cn ?? "",
                Description = e.GetAttribute("msWMI-Parm1")?.GetFirstString() ?? e.Description ?? "",
                Query = e.GetAttribute("msWMI-Parm2")?.GetFirstString() ?? "",
                CreatedAt = e.WhenCreated,
                ModifiedAt = e.WhenChanged,
            }).ToList();
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Create a WMI filter.
    /// </summary>
    public async Task<WmiFilter> CreateWmiFilterAsync(string name, string description, string query, CancellationToken ct = default)
    {
        var domainDn = _ncService.GetDomainNc().Dn;
        var filterId = Guid.NewGuid().ToString("B").ToUpperInvariant();
        var dn = $"CN={filterId},CN=SOM,CN=WMIPolicy,CN=System,{domainDn}";

        var now = DateTimeOffset.UtcNow;
        var usn = await _store.GetNextUsnAsync("default", domainDn, ct);

        var obj = new DirectoryObject
        {
            Id = dn.ToLowerInvariant(),
            TenantId = "default",
            DistinguishedName = dn,
            DomainDn = domainDn,
            ObjectCategory = "msWMI-Som",
            ObjectClass = ["top", "msWMI-Som"],
            Cn = filterId,
            DisplayName = name,
            Description = description,
            ParentDn = $"CN=SOM,CN=WMIPolicy,CN=System,{domainDn}",
            WhenCreated = now,
            WhenChanged = now,
            USNCreated = usn,
            USNChanged = usn,
        };

        obj.SetAttribute("msWMI-Name", new DirectoryAttribute("msWMI-Name", name));
        obj.SetAttribute("msWMI-Parm1", new DirectoryAttribute("msWMI-Parm1", description));
        obj.SetAttribute("msWMI-Parm2", new DirectoryAttribute("msWMI-Parm2", query));
        obj.SetAttribute("msWMI-ID", new DirectoryAttribute("msWMI-ID", filterId));

        await _store.CreateAsync(obj, ct);

        return new WmiFilter
        {
            Id = obj.ObjectGuid,
            Name = name,
            Description = description,
            Query = query,
            CreatedAt = now,
            ModifiedAt = now,
        };
    }

    /// <summary>
    /// Update a WMI filter.
    /// </summary>
    public async Task<WmiFilter> UpdateWmiFilterAsync(string id, string name, string description, string query, CancellationToken ct = default)
    {
        var obj = await _store.GetByGuidAsync("default", id, ct);
        if (obj == null || obj.IsDeleted) return null;

        if (name != null)
        {
            obj.DisplayName = name;
            obj.SetAttribute("msWMI-Name", new DirectoryAttribute("msWMI-Name", name));
        }
        if (description != null)
        {
            obj.Description = description;
            obj.SetAttribute("msWMI-Parm1", new DirectoryAttribute("msWMI-Parm1", description));
        }
        if (query != null)
            obj.SetAttribute("msWMI-Parm2", new DirectoryAttribute("msWMI-Parm2", query));

        obj.WhenChanged = DateTimeOffset.UtcNow;
        await _store.UpdateAsync(obj, ct);

        return new WmiFilter
        {
            Id = obj.ObjectGuid,
            Name = obj.GetAttribute("msWMI-Name")?.GetFirstString() ?? obj.DisplayName ?? "",
            Description = obj.GetAttribute("msWMI-Parm1")?.GetFirstString() ?? "",
            Query = obj.GetAttribute("msWMI-Parm2")?.GetFirstString() ?? "",
            CreatedAt = obj.WhenCreated,
            ModifiedAt = obj.WhenChanged,
        };
    }

    /// <summary>
    /// Delete a WMI filter.
    /// </summary>
    public async Task<bool> DeleteWmiFilterAsync(string id, CancellationToken ct = default)
    {
        var obj = await _store.GetByGuidAsync("default", id, ct);
        if (obj == null || obj.IsDeleted) return false;

        await _store.DeleteAsync("default", obj.DistinguishedName, hardDelete: true, ct);
        return true;
    }

    /// <summary>
    /// Link a WMI filter to a GPO.
    /// </summary>
    public async Task<bool> SetGpoWmiFilterAsync(string gpoGuidOrDn, string wmiFilterId, CancellationToken ct = default)
    {
        var gpo = await ResolveGpoAsync(gpoGuidOrDn, ct);
        if (gpo == null) return false;

        if (string.IsNullOrEmpty(wmiFilterId))
        {
            gpo.Attributes.Remove("gPCWMIFilter");
        }
        else
        {
            var wmiObj = await _store.GetByGuidAsync("default", wmiFilterId, ct);
            if (wmiObj == null) return false;

            var wmiDn = wmiObj.DistinguishedName;
            gpo.SetAttribute("gPCWMIFilter", new DirectoryAttribute("gPCWMIFilter", wmiDn));
        }

        gpo.WhenChanged = DateTimeOffset.UtcNow;
        await _store.UpdateAsync(gpo, ct);
        return true;
    }

    #region Private helpers

    private async Task<DirectoryObject> ResolveGpoAsync(string guidOrDn, CancellationToken ct)
    {
        // Try as GUID first
        if (Guid.TryParse(guidOrDn.Trim('{', '}'), out _))
        {
            var byGuid = await _store.GetByGuidAsync("default", guidOrDn.Trim('{', '}'), ct);
            if (byGuid != null && !byGuid.IsDeleted) return byGuid;
        }

        // Try as DN
        var byDn = await _store.GetByDnAsync("default", guidOrDn, ct);
        if (byDn != null && !byDn.IsDeleted) return byDn;

        return null;
    }

    private async Task<IReadOnlyList<GpoLinkInfo>> GetGpoLinksAsync(string gpoDn, CancellationToken ct)
    {
        var links = new List<GpoLinkInfo>();
        var domainDn = _ncService.GetDomainNc().Dn;

        // Search all containers that might have gPLink referencing this GPO
        // Check domain object itself, then all OUs
        var containers = new List<DirectoryObject>();

        var domainObj = await _store.GetByDnAsync("default", domainDn, ct);
        if (domainObj != null) containers.Add(domainObj);

        var ouFilter = new EqualityFilterNode("objectClass", "organizationalUnit");
        var ouResult = await _store.SearchAsync("default", domainDn, SearchScope.WholeSubtree,
            ouFilter, null, pageSize: 5000, ct: ct);
        containers.AddRange(ouResult.Entries.Where(e => !e.IsDeleted));

        // Also check sites
        var configDn = _ncService.GetConfigurationDn();
        var siteFilter = new EqualityFilterNode("objectClass", "site");
        var siteResult = await _store.SearchAsync("default", $"CN=Sites,{configDn}", SearchScope.WholeSubtree,
            siteFilter, null, pageSize: 500, ct: ct);
        containers.AddRange(siteResult.Entries.Where(e => !e.IsDeleted));

        foreach (var container in containers)
        {
            if (string.IsNullOrEmpty(container.GPLink)) continue;

            var parsed = GroupPolicyHandler.ParseGpLink(container.GPLink);
            foreach (var link in parsed)
            {
                if (string.Equals(link.GpoDn, gpoDn, StringComparison.OrdinalIgnoreCase))
                {
                    links.Add(new GpoLinkInfo
                    {
                        TargetDn = container.DistinguishedName,
                        TargetName = container.DisplayName ?? container.Cn ?? container.DistinguishedName,
                        IsEnforced = link.IsEnforced,
                        IsDisabled = link.IsDisabled,
                    });
                }
            }
        }

        return links;
    }

    private async Task<IReadOnlyList<RsopGpoEntry>> FilterAndResolveGposAsync(
        IReadOnlyList<AppliedGpo> appliedGpos, DirectoryObject target,
        bool isComputer, CancellationToken ct)
    {
        var entries = new List<RsopGpoEntry>();
        var blockInheritance = target.GPOptions == 1; // gPOptions = 1 means block inheritance

        foreach (var applied in appliedGpos)
        {
            // If blocking inheritance, skip non-enforced GPOs from parent containers
            if (blockInheritance && !applied.IsEnforced)
            {
                var targetParent = DistinguishedName.Parse(target.DistinguishedName).Parent()?.ToString() ?? "";
                if (!string.Equals(applied.SourceContainerDn, targetParent, StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            var gpo = await _store.GetByDnAsync("default", applied.GpoDn, ct);
            if (gpo == null || gpo.IsDeleted) continue;

            var flags = GetGpoFlags(gpo);

            // Check if GPO is enabled for this target type
            if (isComputer && (flags & 2) != 0) continue; // Computer settings disabled
            if (!isComputer && (flags & 1) != 0) continue; // User settings disabled

            // Security filtering: check if target's groups intersect with GPO's security filtering
            var secFilter = ExtractSecurityFiltering(gpo);
            if (secFilter.Count > 0 && !PassesSecurityFilter(target, secFilter))
                continue;

            // WMI filter evaluation (basic: check if WMI filter attribute exists and is "TRUE" or empty)
            var wmiFilter = gpo.GetAttribute("gPCWMIFilter")?.GetFirstString();
            if (!string.IsNullOrEmpty(wmiFilter) && wmiFilter != "TRUE")
            {
                _logger.LogDebug("GPO {Dn} skipped due to WMI filter", gpo.DistinguishedName);
                continue;
            }

            var settings = ExtractPolicySettings(gpo);

            entries.Add(new RsopGpoEntry
            {
                GpoDn = applied.GpoDn,
                DisplayName = applied.DisplayName,
                SourceContainerDn = applied.SourceContainerDn,
                IsEnforced = applied.IsEnforced,
                LinkOrder = applied.LinkOrder,
                Settings = settings,
            });
        }

        return entries;
    }

    private bool PassesSecurityFilter(DirectoryObject target, IReadOnlyList<string> securityFilterDns)
    {
        // If "Authenticated Users" or similar well-known group is in the filter, pass
        if (securityFilterDns.Any(f => f.Contains("Authenticated Users", StringComparison.OrdinalIgnoreCase)))
            return true;

        // Check if target DN is directly in the filter
        if (securityFilterDns.Any(f => string.Equals(f, target.DistinguishedName, StringComparison.OrdinalIgnoreCase)))
            return true;

        // Check if any of target's group memberships match the filter
        foreach (var groupDn in target.MemberOf)
        {
            if (securityFilterDns.Any(f => string.Equals(f, groupDn, StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        return false;
    }

    private static void MergePolicyInto(GpoPolicySettings target, GpoPolicySettings source)
    {
        // Password policy
        if (source.PasswordPolicy != null)
        {
            target.PasswordPolicy ??= new PasswordPolicySettings();
            if (source.PasswordPolicy.MinimumLength.HasValue) target.PasswordPolicy.MinimumLength = source.PasswordPolicy.MinimumLength;
            if (source.PasswordPolicy.ComplexityEnabled.HasValue) target.PasswordPolicy.ComplexityEnabled = source.PasswordPolicy.ComplexityEnabled;
            if (source.PasswordPolicy.HistoryCount.HasValue) target.PasswordPolicy.HistoryCount = source.PasswordPolicy.HistoryCount;
            if (source.PasswordPolicy.MaxAgeDays.HasValue) target.PasswordPolicy.MaxAgeDays = source.PasswordPolicy.MaxAgeDays;
            if (source.PasswordPolicy.MinAgeDays.HasValue) target.PasswordPolicy.MinAgeDays = source.PasswordPolicy.MinAgeDays;
            if (source.PasswordPolicy.ReversibleEncryption.HasValue) target.PasswordPolicy.ReversibleEncryption = source.PasswordPolicy.ReversibleEncryption;
        }

        // Account lockout
        if (source.AccountLockout != null)
        {
            target.AccountLockout ??= new AccountLockoutSettings();
            if (source.AccountLockout.Threshold.HasValue) target.AccountLockout.Threshold = source.AccountLockout.Threshold;
            if (source.AccountLockout.DurationMinutes.HasValue) target.AccountLockout.DurationMinutes = source.AccountLockout.DurationMinutes;
            if (source.AccountLockout.ObservationWindowMinutes.HasValue) target.AccountLockout.ObservationWindowMinutes = source.AccountLockout.ObservationWindowMinutes;
        }

        // Audit policy
        if (source.AuditPolicy != null)
        {
            target.AuditPolicy ??= new AuditPolicySettings();
            if (source.AuditPolicy.AuditLogonEvents.HasValue) target.AuditPolicy.AuditLogonEvents = source.AuditPolicy.AuditLogonEvents;
            if (source.AuditPolicy.AuditObjectAccess.HasValue) target.AuditPolicy.AuditObjectAccess = source.AuditPolicy.AuditObjectAccess;
            if (source.AuditPolicy.AuditPrivilegeUse.HasValue) target.AuditPolicy.AuditPrivilegeUse = source.AuditPolicy.AuditPrivilegeUse;
            if (source.AuditPolicy.AuditPolicyChange.HasValue) target.AuditPolicy.AuditPolicyChange = source.AuditPolicy.AuditPolicyChange;
            if (source.AuditPolicy.AuditAccountManagement.HasValue) target.AuditPolicy.AuditAccountManagement = source.AuditPolicy.AuditAccountManagement;
            if (source.AuditPolicy.AuditProcessTracking.HasValue) target.AuditPolicy.AuditProcessTracking = source.AuditPolicy.AuditProcessTracking;
            if (source.AuditPolicy.AuditDsAccess.HasValue) target.AuditPolicy.AuditDsAccess = source.AuditPolicy.AuditDsAccess;
            if (source.AuditPolicy.AuditAccountLogon.HasValue) target.AuditPolicy.AuditAccountLogon = source.AuditPolicy.AuditAccountLogon;
            if (source.AuditPolicy.AuditSystemEvents.HasValue) target.AuditPolicy.AuditSystemEvents = source.AuditPolicy.AuditSystemEvents;
        }

        // User rights
        if (source.UserRights != null)
        {
            target.UserRights ??= new UserRightsSettings();
            if (source.UserRights.AllowLogOnLocally != null) target.UserRights.AllowLogOnLocally = source.UserRights.AllowLogOnLocally;
            if (source.UserRights.DenyLogOnLocally != null) target.UserRights.DenyLogOnLocally = source.UserRights.DenyLogOnLocally;
            if (source.UserRights.AllowRemoteDesktop != null) target.UserRights.AllowRemoteDesktop = source.UserRights.AllowRemoteDesktop;
            if (source.UserRights.DenyRemoteDesktop != null) target.UserRights.DenyRemoteDesktop = source.UserRights.DenyRemoteDesktop;
            if (source.UserRights.BackupFilesAndDirectories != null) target.UserRights.BackupFilesAndDirectories = source.UserRights.BackupFilesAndDirectories;
            if (source.UserRights.RestoreFilesAndDirectories != null) target.UserRights.RestoreFilesAndDirectories = source.UserRights.RestoreFilesAndDirectories;
            if (source.UserRights.ShutdownSystem != null) target.UserRights.ShutdownSystem = source.UserRights.ShutdownSystem;
            if (source.UserRights.ChangeSystemTime != null) target.UserRights.ChangeSystemTime = source.UserRights.ChangeSystemTime;
        }

        // Security options
        if (source.SecurityOptions != null)
        {
            target.SecurityOptions ??= new SecurityOptionSettings();
            if (source.SecurityOptions.LanManagerAuthLevel.HasValue) target.SecurityOptions.LanManagerAuthLevel = source.SecurityOptions.LanManagerAuthLevel;
            if (source.SecurityOptions.RequireSmbSigning.HasValue) target.SecurityOptions.RequireSmbSigning = source.SecurityOptions.RequireSmbSigning;
            if (source.SecurityOptions.LdapClientSigningRequirement.HasValue) target.SecurityOptions.LdapClientSigningRequirement = source.SecurityOptions.LdapClientSigningRequirement;
            if (source.SecurityOptions.LdapServerSigningRequirement.HasValue) target.SecurityOptions.LdapServerSigningRequirement = source.SecurityOptions.LdapServerSigningRequirement;
            if (source.SecurityOptions.MinimumSessionSecurity.HasValue) target.SecurityOptions.MinimumSessionSecurity = source.SecurityOptions.MinimumSessionSecurity;
            if (source.SecurityOptions.RenameAdministratorAccount != null) target.SecurityOptions.RenameAdministratorAccount = source.SecurityOptions.RenameAdministratorAccount;
            if (source.SecurityOptions.RenameGuestAccount != null) target.SecurityOptions.RenameGuestAccount = source.SecurityOptions.RenameGuestAccount;
            if (source.SecurityOptions.EnableGuestAccount.HasValue) target.SecurityOptions.EnableGuestAccount = source.SecurityOptions.EnableGuestAccount;
        }

        // Software restriction
        if (source.SoftwareRestriction != null)
        {
            target.SoftwareRestriction ??= new SoftwareRestrictionSettings();
            if (source.SoftwareRestriction.DefaultLevel.HasValue) target.SoftwareRestriction.DefaultLevel = source.SoftwareRestriction.DefaultLevel;
            if (source.SoftwareRestriction.EnforcementScope.HasValue) target.SoftwareRestriction.EnforcementScope = source.SoftwareRestriction.EnforcementScope;
            if (source.SoftwareRestriction.Rules != null && source.SoftwareRestriction.Rules.Count > 0)
                target.SoftwareRestriction.Rules = source.SoftwareRestriction.Rules;
        }

        // Scripts and drive mappings
        if (source.LogonScripts != null && source.LogonScripts.Count > 0)
            target.LogonScripts = source.LogonScripts;
        if (source.LogoffScripts != null && source.LogoffScripts.Count > 0)
            target.LogoffScripts = source.LogoffScripts;
        if (source.StartupScripts != null && source.StartupScripts.Count > 0)
            target.StartupScripts = source.StartupScripts;
        if (source.ShutdownScripts != null && source.ShutdownScripts.Count > 0)
            target.ShutdownScripts = source.ShutdownScripts;
        if (source.DriveMappings != null && source.DriveMappings.Count > 0)
            target.DriveMappings = source.DriveMappings;
    }

    private static GpoPolicySettings MergeFinalPolicy(GpoPolicySettings userPolicy, GpoPolicySettings computerPolicy)
    {
        var merged = new GpoPolicySettings();

        if (userPolicy != null)
            MergePolicyInto(merged, userPolicy);

        // Computer policy overwrites user policy where overlapping
        if (computerPolicy != null)
            MergePolicyInto(merged, computerPolicy);

        return merged;
    }

    private static GpoPolicySettings ExtractPolicySettings(DirectoryObject gpo)
    {
        var json = gpo.GetAttribute("policySettingsJson")?.GetFirstString();
        if (string.IsNullOrEmpty(json))
            return new GpoPolicySettings();

        try
        {
            return JsonSerializer.Deserialize<GpoPolicySettings>(json, JsonOptions) ?? new GpoPolicySettings();
        }
        catch
        {
            return new GpoPolicySettings();
        }
    }

    private static void StorePolicySettings(DirectoryObject gpo, GpoPolicySettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        gpo.SetAttribute("policySettingsJson", new DirectoryAttribute("policySettingsJson", json));
    }

    private static IReadOnlyList<string> ExtractSecurityFiltering(DirectoryObject gpo)
    {
        var attr = gpo.GetAttribute("gpoSecurityFiltering");
        if (attr == null) return [];

        return attr.GetStrings().ToList();
    }

    private static int GetGpoFlags(DirectoryObject gpo)
    {
        var flagsAttr = gpo.GetAttribute("flags");
        if (flagsAttr == null) return 0;

        if (int.TryParse(flagsAttr.GetFirstString(), out var flags))
            return flags;

        return 0;
    }

    private static int GetVersionNumber(DirectoryObject gpo)
    {
        var attr = gpo.GetAttribute("versionNumber");
        if (attr == null) return 0;

        if (int.TryParse(attr.GetFirstString(), out var ver))
            return ver;

        return 0;
    }

    private static string BuildGpLinkString(IEnumerable<GpLinkEntry> links)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var link in links)
        {
            sb.Append($"[LDAP://{link.GpoDn};{link.Options}]");
        }
        return sb.ToString();
    }

    private string GetDomainDnsName()
    {
        var domainDn = _ncService.GetDomainNc().Dn;
        // Convert DC=corp,DC=example,DC=com -> corp.example.com
        var parts = domainDn.Split(',')
            .Where(p => p.StartsWith("DC=", StringComparison.OrdinalIgnoreCase))
            .Select(p => p[3..]);
        return string.Join(".", parts);
    }

    /// <summary>
    /// Builds the gPCFileSysPath for a GPO, using the configured SMB server hostname
    /// if available, otherwise falling back to the domain DNS name.
    /// Format: \\{host}\SYSVOL\{domainDnsName}\Policies\{gpoGuid}
    /// </summary>
    internal string BuildGpcFileSysPath(string gpoGuid)
    {
        var domainDnsName = GetDomainDnsName();
        var smbHost = SysvolHostnameProvider?.Invoke();
        var host = !string.IsNullOrEmpty(smbHost) ? smbHost : domainDnsName;
        return $@"\\{host}\SYSVOL\{domainDnsName}\Policies\{gpoGuid}";
    }

    /// <summary>
    /// Recalculates gPCFileSysPath for all GPOs in the domain using the current
    /// SYSVOL hostname configuration. Called when the SYSVOL config changes.
    /// </summary>
    public async Task UpdateAllGpoFilePathsAsync(CancellationToken ct = default)
    {
        var domainDn = _ncService.GetDomainNc().Dn;
        var policiesDn = $"CN=Policies,CN=System,{domainDn}";

        var filter = new EqualityFilterNode("objectClass", "groupPolicyContainer");
        var result = await _store.SearchAsync("default", policiesDn, SearchScope.SingleLevel, filter,
            null, pageSize: 1000, ct: ct);

        foreach (var entry in result.Entries.Where(e => !e.IsDeleted))
        {
            var gpoGuid = entry.Cn ?? "";
            var newPath = BuildGpcFileSysPath(gpoGuid);
            if (entry.GPCFileSysPath != newPath)
            {
                entry.GPCFileSysPath = newPath;
                entry.WhenChanged = DateTimeOffset.UtcNow;
                await _store.UpdateAsync(entry, ct);
                _logger.LogInformation("Updated gPCFileSysPath for GPO {Guid} to {Path}", gpoGuid, newPath);
            }
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    #endregion
}

#region New Models

public class SecurityFilterEntry
{
    public string Dn { get; init; } = "";
    public string Name { get; init; } = "";
    public string ObjectSid { get; init; } = "";
    public string ObjectClass { get; init; } = "";
}

public class GpoBackup
{
    public string BackupId { get; init; } = "";
    public string GpoGuid { get; init; } = "";
    public string GpoDisplayName { get; init; } = "";
    public string Description { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }
}

public class GpoBackupData
{
    [JsonPropertyName("gpoGuid")]
    public string GpoGuid { get; set; } = "";

    [JsonPropertyName("gpoDn")]
    public string GpoDn { get; set; } = "";

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("flags")]
    public int Flags { get; set; }

    [JsonPropertyName("policySettings")]
    public GpoPolicySettings PolicySettings { get; set; } = new();

    [JsonPropertyName("securityFiltering")]
    public List<string> SecurityFiltering { get; set; } = [];
}

public class WmiFilter
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string Query { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset ModifiedAt { get; init; }
}

#endregion

#region Models

public class GpoSummary
{
    public string Dn { get; init; } = "";
    public string ObjectGuid { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Cn { get; init; } = "";
    public int Flags { get; init; }
    public bool IsUserEnabled { get; init; }
    public bool IsComputerEnabled { get; init; }
    public int VersionNumber { get; init; }
    public int LinkCount { get; init; }
    public DateTimeOffset WhenCreated { get; init; }
    public DateTimeOffset WhenChanged { get; init; }
}

public class GpoDetail : GpoSummary
{
    public int UserVersion { get; init; }
    public int ComputerVersion { get; init; }
    public string GPCFileSysPath { get; init; } = "";
    public string GPCMachineExtensionNames { get; init; } = "";
    public string GPCUserExtensionNames { get; init; } = "";
    public IReadOnlyList<GpoLinkInfo> Links { get; init; } = [];
    public IReadOnlyList<string> SecurityFiltering { get; init; } = [];
    public GpoPolicySettings PolicySettings { get; init; } = new();
}

public class GpoLinkInfo
{
    public string TargetDn { get; init; } = "";
    public string TargetName { get; init; } = "";
    public bool IsEnforced { get; init; }
    public bool IsDisabled { get; init; }
}

public class RsopResult
{
    public IReadOnlyList<RsopGpoEntry> UserGpos { get; set; } = [];
    public IReadOnlyList<RsopGpoEntry> ComputerGpos { get; set; } = [];
    public GpoPolicySettings UserPolicy { get; set; } = new();
    public GpoPolicySettings ComputerPolicy { get; set; } = new();
    public GpoPolicySettings MergedPolicy { get; set; } = new();
}

public class RsopGpoEntry
{
    public string GpoDn { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string SourceContainerDn { get; init; } = "";
    public bool IsEnforced { get; init; }
    public int LinkOrder { get; init; }
    public GpoPolicySettings Settings { get; init; } = new();
}

/// <summary>
/// Structured policy settings stored as JSON within GPO entries,
/// replacing traditional SYSVOL .pol files and Registry.pol.
/// </summary>
public class GpoPolicySettings
{
    [JsonPropertyName("passwordPolicy")]
    public PasswordPolicySettings PasswordPolicy { get; set; }

    [JsonPropertyName("accountLockout")]
    public AccountLockoutSettings AccountLockout { get; set; }

    [JsonPropertyName("auditPolicy")]
    public AuditPolicySettings AuditPolicy { get; set; }

    [JsonPropertyName("userRights")]
    public UserRightsSettings UserRights { get; set; }

    [JsonPropertyName("securityOptions")]
    public SecurityOptionSettings SecurityOptions { get; set; }

    [JsonPropertyName("softwareRestriction")]
    public SoftwareRestrictionSettings SoftwareRestriction { get; set; }

    [JsonPropertyName("logonScripts")]
    public List<ScriptReference> LogonScripts { get; set; }

    [JsonPropertyName("logoffScripts")]
    public List<ScriptReference> LogoffScripts { get; set; }

    [JsonPropertyName("startupScripts")]
    public List<ScriptReference> StartupScripts { get; set; }

    [JsonPropertyName("shutdownScripts")]
    public List<ScriptReference> ShutdownScripts { get; set; }

    [JsonPropertyName("driveMappings")]
    public List<DriveMapping> DriveMappings { get; set; }
}

public class PasswordPolicySettings
{
    [JsonPropertyName("minimumLength")]
    public int? MinimumLength { get; set; }

    [JsonPropertyName("complexityEnabled")]
    public bool? ComplexityEnabled { get; set; }

    [JsonPropertyName("historyCount")]
    public int? HistoryCount { get; set; }

    [JsonPropertyName("maxAgeDays")]
    public int? MaxAgeDays { get; set; }

    [JsonPropertyName("minAgeDays")]
    public int? MinAgeDays { get; set; }

    [JsonPropertyName("reversibleEncryption")]
    public bool? ReversibleEncryption { get; set; }
}

public class AccountLockoutSettings
{
    [JsonPropertyName("threshold")]
    public int? Threshold { get; set; }

    [JsonPropertyName("durationMinutes")]
    public int? DurationMinutes { get; set; }

    [JsonPropertyName("observationWindowMinutes")]
    public int? ObservationWindowMinutes { get; set; }
}

public class AuditPolicySettings
{
    [JsonPropertyName("auditLogonEvents")]
    public int? AuditLogonEvents { get; set; }

    [JsonPropertyName("auditObjectAccess")]
    public int? AuditObjectAccess { get; set; }

    [JsonPropertyName("auditPrivilegeUse")]
    public int? AuditPrivilegeUse { get; set; }

    [JsonPropertyName("auditPolicyChange")]
    public int? AuditPolicyChange { get; set; }

    [JsonPropertyName("auditAccountManagement")]
    public int? AuditAccountManagement { get; set; }

    [JsonPropertyName("auditProcessTracking")]
    public int? AuditProcessTracking { get; set; }

    [JsonPropertyName("auditDsAccess")]
    public int? AuditDsAccess { get; set; }

    [JsonPropertyName("auditAccountLogon")]
    public int? AuditAccountLogon { get; set; }

    [JsonPropertyName("auditSystemEvents")]
    public int? AuditSystemEvents { get; set; }
}

public class UserRightsSettings
{
    [JsonPropertyName("allowLogOnLocally")]
    public List<string> AllowLogOnLocally { get; set; }

    [JsonPropertyName("denyLogOnLocally")]
    public List<string> DenyLogOnLocally { get; set; }

    [JsonPropertyName("allowRemoteDesktop")]
    public List<string> AllowRemoteDesktop { get; set; }

    [JsonPropertyName("denyRemoteDesktop")]
    public List<string> DenyRemoteDesktop { get; set; }

    [JsonPropertyName("backupFilesAndDirectories")]
    public List<string> BackupFilesAndDirectories { get; set; }

    [JsonPropertyName("restoreFilesAndDirectories")]
    public List<string> RestoreFilesAndDirectories { get; set; }

    [JsonPropertyName("shutdownSystem")]
    public List<string> ShutdownSystem { get; set; }

    [JsonPropertyName("changeSystemTime")]
    public List<string> ChangeSystemTime { get; set; }
}

public class SecurityOptionSettings
{
    /// <summary>
    /// LAN Manager authentication level (0-5).
    /// 0=LM&amp;NTLM, 1=LM&amp;NTLM if negotiated, 2=NTLM only, 3=NTLMv2 only,
    /// 4=NTLMv2 refuse LM, 5=NTLMv2 refuse LM&amp;NTLM.
    /// </summary>
    [JsonPropertyName("lanManagerAuthLevel")]
    public int? LanManagerAuthLevel { get; set; }

    [JsonPropertyName("requireSmbSigning")]
    public bool? RequireSmbSigning { get; set; }

    [JsonPropertyName("ldapClientSigningRequirement")]
    public int? LdapClientSigningRequirement { get; set; }

    [JsonPropertyName("ldapServerSigningRequirement")]
    public int? LdapServerSigningRequirement { get; set; }

    [JsonPropertyName("minimumSessionSecurity")]
    public int? MinimumSessionSecurity { get; set; }

    [JsonPropertyName("renameAdministratorAccount")]
    public string RenameAdministratorAccount { get; set; }

    [JsonPropertyName("renameGuestAccount")]
    public string RenameGuestAccount { get; set; }

    [JsonPropertyName("enableGuestAccount")]
    public bool? EnableGuestAccount { get; set; }
}

public class SoftwareRestrictionSettings
{
    /// <summary>
    /// Default security level: 0=Disallowed, 0x40000=Unrestricted.
    /// </summary>
    [JsonPropertyName("defaultLevel")]
    public int? DefaultLevel { get; set; }

    /// <summary>
    /// 0=All users, 1=All users except local admins.
    /// </summary>
    [JsonPropertyName("enforcementScope")]
    public int? EnforcementScope { get; set; }

    [JsonPropertyName("rules")]
    public List<SoftwareRule> Rules { get; set; }
}

public class SoftwareRule
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = ""; // "hash", "path", "certificate", "zone"

    [JsonPropertyName("value")]
    public string Value { get; set; } = "";

    [JsonPropertyName("securityLevel")]
    public int SecurityLevel { get; set; } // 0=Disallowed, 0x40000=Unrestricted

    [JsonPropertyName("description")]
    public string Description { get; set; }
}

public class ScriptReference
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("parameters")]
    public string Parameters { get; set; }

    [JsonPropertyName("order")]
    public int Order { get; set; }
}

public class DriveMapping
{
    [JsonPropertyName("driveLetter")]
    public string DriveLetter { get; set; } = "";

    [JsonPropertyName("uncPath")]
    public string UncPath { get; set; } = "";

    [JsonPropertyName("label")]
    public string Label { get; set; }

    [JsonPropertyName("action")]
    public string Action { get; set; } = "Create"; // Create, Replace, Update, Delete

    [JsonPropertyName("reconnect")]
    public bool Reconnect { get; set; } = true;
}

#endregion
