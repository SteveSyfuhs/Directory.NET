using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Microsoft.Extensions.Logging;

namespace Directory.Security.Delegation;

/// <summary>
/// Manages delegated administration roles (RBAC) for the directory service.
/// Roles are cached in-memory and persisted as directory objects via IDirectoryStore.
/// </summary>
public class DelegationService
{
    private readonly IDirectoryStore _store;
    private readonly INamingContextService _ncService;
    private readonly ILogger<DelegationService> _logger;

    private readonly ConcurrentDictionary<string, AdminRole> _roles = new();
    private bool _loaded;
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private const string ContainerCn = "Admin Roles";
    private const string ObjectClassName = "adminRole";

    public DelegationService(
        IDirectoryStore store,
        INamingContextService ncService,
        ILogger<DelegationService> logger)
    {
        _store = store;
        _ncService = ncService;
        _logger = logger;
    }

    // ── Permission Definitions ────────────────────────────────

    private static readonly List<DelegationPermission> AllPermissions =
    [
        new() { Key = "users:read", Category = "Users", DisplayName = "Read Users", Description = "View user accounts and their properties" },
        new() { Key = "users:create", Category = "Users", DisplayName = "Create Users", Description = "Create new user accounts" },
        new() { Key = "users:modify", Category = "Users", DisplayName = "Modify Users", Description = "Edit user account properties" },
        new() { Key = "users:delete", Category = "Users", DisplayName = "Delete Users", Description = "Delete user accounts" },
        new() { Key = "users:resetPassword", Category = "Users", DisplayName = "Reset Passwords", Description = "Reset user passwords" },
        new() { Key = "users:unlock", Category = "Users", DisplayName = "Unlock Accounts", Description = "Unlock locked user accounts" },

        new() { Key = "groups:read", Category = "Groups", DisplayName = "Read Groups", Description = "View groups and their properties" },
        new() { Key = "groups:create", Category = "Groups", DisplayName = "Create Groups", Description = "Create new groups" },
        new() { Key = "groups:modify", Category = "Groups", DisplayName = "Modify Groups", Description = "Edit group properties" },
        new() { Key = "groups:delete", Category = "Groups", DisplayName = "Delete Groups", Description = "Delete groups" },
        new() { Key = "groups:manageMembers", Category = "Groups", DisplayName = "Manage Group Members", Description = "Add or remove group members" },

        new() { Key = "computers:read", Category = "Computers", DisplayName = "Read Computers", Description = "View computer accounts" },
        new() { Key = "computers:create", Category = "Computers", DisplayName = "Create Computers", Description = "Create computer accounts" },
        new() { Key = "computers:modify", Category = "Computers", DisplayName = "Modify Computers", Description = "Edit computer account properties" },
        new() { Key = "computers:delete", Category = "Computers", DisplayName = "Delete Computers", Description = "Delete computer accounts" },

        new() { Key = "ous:read", Category = "Organizational Units", DisplayName = "Read OUs", Description = "View organizational units" },
        new() { Key = "ous:create", Category = "Organizational Units", DisplayName = "Create OUs", Description = "Create organizational units" },
        new() { Key = "ous:modify", Category = "Organizational Units", DisplayName = "Modify OUs", Description = "Edit OU properties" },
        new() { Key = "ous:delete", Category = "Organizational Units", DisplayName = "Delete OUs", Description = "Delete organizational units" },

        new() { Key = "dns:read", Category = "DNS", DisplayName = "Read DNS", Description = "View DNS zones and records" },
        new() { Key = "dns:manage", Category = "DNS", DisplayName = "Manage DNS", Description = "Create, modify, and delete DNS records" },

        new() { Key = "gpo:read", Category = "Group Policy", DisplayName = "Read GPOs", Description = "View group policy objects" },
        new() { Key = "gpo:manage", Category = "Group Policy", DisplayName = "Manage GPOs", Description = "Create, modify, and delete GPOs" },

        new() { Key = "schema:read", Category = "Schema", DisplayName = "Read Schema", Description = "View directory schema" },
        new() { Key = "schema:modify", Category = "Schema", DisplayName = "Modify Schema", Description = "Modify directory schema" },

        new() { Key = "certificates:read", Category = "Certificates", DisplayName = "Read Certificates", Description = "View certificate services" },
        new() { Key = "certificates:manage", Category = "Certificates", DisplayName = "Manage Certificates", Description = "Issue, revoke, and manage certificates" },

        new() { Key = "audit:read", Category = "Audit", DisplayName = "Read Audit Logs", Description = "View audit log entries" },
        new() { Key = "backup:manage", Category = "Backup", DisplayName = "Manage Backups", Description = "Create and restore backups" },
        new() { Key = "settings:manage", Category = "Settings", DisplayName = "Manage Settings", Description = "Modify service and domain settings" },
    ];

    private static readonly List<AdminRole> BuiltInRoles =
    [
        new()
        {
            Id = "builtin-domain-admin",
            Name = "Domain Admin",
            Description = "Full administrative access to all directory services",
            Permissions = AllPermissions.Select(p => p.Key).ToList(),
            ScopeDns = [],
            IsBuiltIn = true,
            CreatedAt = DateTimeOffset.UnixEpoch,
        },
        new()
        {
            Id = "builtin-help-desk",
            Name = "Help Desk",
            Description = "Reset passwords and unlock user accounts",
            Permissions = ["users:read", "users:resetPassword", "users:unlock", "groups:read"],
            ScopeDns = [],
            IsBuiltIn = true,
            CreatedAt = DateTimeOffset.UnixEpoch,
        },
        new()
        {
            Id = "builtin-account-operator",
            Name = "Account Operator",
            Description = "Create, modify, and delete user and group accounts",
            Permissions = [
                "users:read", "users:create", "users:modify", "users:delete",
                "users:resetPassword", "users:unlock",
                "groups:read", "groups:create", "groups:modify", "groups:delete", "groups:manageMembers",
            ],
            ScopeDns = [],
            IsBuiltIn = true,
            CreatedAt = DateTimeOffset.UnixEpoch,
        },
        new()
        {
            Id = "builtin-dns-admin",
            Name = "DNS Admin",
            Description = "Manage DNS zones and records",
            Permissions = ["dns:read", "dns:manage"],
            ScopeDns = [],
            IsBuiltIn = true,
            CreatedAt = DateTimeOffset.UnixEpoch,
        },
        new()
        {
            Id = "builtin-read-only",
            Name = "Read-Only Admin",
            Description = "Read-only access to all directory objects",
            Permissions = AllPermissions.Where(p => p.Key.EndsWith(":read")).Select(p => p.Key).ToList(),
            ScopeDns = [],
            IsBuiltIn = true,
            CreatedAt = DateTimeOffset.UnixEpoch,
        },
    ];

    // ── Initialization ────────────────────────────────────────

    private string GetContainerDn()
    {
        var domainDn = _ncService.GetDomainNc().Dn;
        return $"CN={ContainerCn},CN=System,{domainDn}";
    }

    private string GetRoleDn(string roleId) =>
        $"CN={roleId},{GetContainerDn()}";

    private async Task EnsureLoadedAsync(CancellationToken ct = default)
    {
        if (_loaded) return;

        await _loadLock.WaitAsync(ct);
        try
        {
            if (_loaded) return;

            // Seed built-in roles
            foreach (var role in BuiltInRoles)
            {
                _roles[role.Id] = role;
            }

            // Load persisted roles from directory
            try
            {
                var containerDn = GetContainerDn();
                var children = await _store.GetChildrenAsync("default", containerDn, ct);

                foreach (var obj in children)
                {
                    if (obj.IsDeleted) continue;
                    var json = obj.GetAttribute("delegationRoleData")?.GetFirstString();
                    if (string.IsNullOrEmpty(json)) continue;

                    var role = JsonSerializer.Deserialize<AdminRole>(json, JsonOpts);
                    if (role != null)
                    {
                        _roles[role.Id] = role;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to load delegation roles (container may not exist yet)");
            }

            _loaded = true;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    private async Task PersistRoleAsync(AdminRole role, CancellationToken ct)
    {
        var dn = GetRoleDn(role.Id);
        var domainDn = _ncService.GetDomainNc().Dn;
        var json = JsonSerializer.Serialize(role, JsonOpts);

        var existing = await _store.GetByDnAsync("default", dn, ct);

        if (existing != null)
        {
            existing.SetAttribute("delegationRoleData", new DirectoryAttribute("delegationRoleData", json));
            existing.DisplayName = role.Name;
            existing.Description = role.Description;
            existing.WhenChanged = DateTimeOffset.UtcNow;
            await _store.UpdateAsync(existing, ct);
        }
        else
        {
            var containerDn = GetContainerDn();

            // Ensure container exists
            await EnsureContainerAsync(containerDn, domainDn, ct);

            var usn = await _store.GetNextUsnAsync("default", domainDn, ct);
            var now = DateTimeOffset.UtcNow;

            var obj = new DirectoryObject
            {
                Id = dn.ToLowerInvariant(),
                TenantId = "default",
                DistinguishedName = dn,
                DomainDn = domainDn,
                ObjectCategory = ObjectClassName,
                ObjectClass = ["top", ObjectClassName],
                Cn = role.Id,
                DisplayName = role.Name,
                Description = role.Description,
                ParentDn = containerDn,
                ObjectGuid = Guid.NewGuid().ToString(),
                WhenCreated = now,
                WhenChanged = now,
                USNCreated = usn,
                USNChanged = usn,
            };

            obj.SetAttribute("delegationRoleData", new DirectoryAttribute("delegationRoleData", json));
            await _store.CreateAsync(obj, ct);
        }
    }

    private async Task EnsureContainerAsync(string containerDn, string domainDn, CancellationToken ct)
    {
        var existing = await _store.GetByDnAsync("default", containerDn, ct);
        if (existing != null) return;

        var systemDn = $"CN=System,{domainDn}";
        var usn = await _store.GetNextUsnAsync("default", domainDn, ct);
        var now = DateTimeOffset.UtcNow;

        var container = new DirectoryObject
        {
            Id = containerDn.ToLowerInvariant(),
            TenantId = "default",
            DistinguishedName = containerDn,
            DomainDn = domainDn,
            ObjectCategory = "container",
            ObjectClass = ["top", "container"],
            Cn = ContainerCn,
            DisplayName = "Delegated Administration Roles",
            Description = "Contains admin role definitions for delegated administration",
            ParentDn = systemDn,
            ObjectGuid = Guid.NewGuid().ToString(),
            WhenCreated = now,
            WhenChanged = now,
            USNCreated = usn,
            USNChanged = usn,
        };

        try
        {
            await _store.CreateAsync(container, ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Container may already exist, continuing");
        }
    }

    private async Task DeletePersistedRoleAsync(string roleId, CancellationToken ct)
    {
        var dn = GetRoleDn(roleId);
        try
        {
            await _store.DeleteAsync("default", dn, hardDelete: true, ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to delete persisted role {RoleId}", roleId);
        }
    }

    // ── Public API ────────────────────────────────────────────

    public List<DelegationPermission> GetAvailablePermissions() => AllPermissions;

    public async Task<List<AdminRole>> GetAllRolesAsync(CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);
        return _roles.Values.OrderBy(r => r.IsBuiltIn ? 0 : 1).ThenBy(r => r.Name).ToList();
    }

    public async Task<AdminRole> GetRoleAsync(string id, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);
        return _roles.TryGetValue(id, out var role) ? role : null;
    }

    public async Task<AdminRole> CreateRoleAsync(AdminRole role, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);

        role.Id = Guid.NewGuid().ToString();
        role.IsBuiltIn = false;
        role.CreatedAt = DateTimeOffset.UtcNow;

        // Validate permission keys
        var validKeys = AllPermissions.Select(p => p.Key).ToHashSet();
        role.Permissions = role.Permissions.Where(p => validKeys.Contains(p)).ToList();

        _roles[role.Id] = role;

        try
        {
            await PersistRoleAsync(role, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist new role {RoleName}", role.Name);
        }

        return role;
    }

    public async Task<AdminRole> UpdateRoleAsync(string id, AdminRole update, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);

        if (!_roles.TryGetValue(id, out var existing))
            return null;

        // Allow updating built-in roles' members and scope, but not permissions
        if (!existing.IsBuiltIn)
        {
            existing.Name = update.Name;
            existing.Description = update.Description;

            var validKeys = AllPermissions.Select(p => p.Key).ToHashSet();
            existing.Permissions = update.Permissions.Where(p => validKeys.Contains(p)).ToList();
        }

        existing.ScopeDns = update.ScopeDns;
        existing.AssignedMembers = update.AssignedMembers;

        try
        {
            await PersistRoleAsync(existing, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist updated role {RoleId}", id);
        }

        return existing;
    }

    public async Task<bool> DeleteRoleAsync(string id, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);

        if (!_roles.TryGetValue(id, out var role))
            return false;

        if (role.IsBuiltIn)
            return false;

        _roles.TryRemove(id, out _);
        await DeletePersistedRoleAsync(id, ct);
        return true;
    }

    public async Task<AdminRole> AssignMemberAsync(string roleId, string memberDn, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);

        if (!_roles.TryGetValue(roleId, out var role))
            return null;

        if (!role.AssignedMembers.Contains(memberDn, StringComparer.OrdinalIgnoreCase))
        {
            role.AssignedMembers.Add(memberDn);
            try { await PersistRoleAsync(role, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to persist member assignment"); }
        }

        return role;
    }

    public async Task<AdminRole> RemoveMemberAsync(string roleId, string memberDn, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);

        if (!_roles.TryGetValue(roleId, out var role))
            return null;

        role.AssignedMembers.RemoveAll(m => string.Equals(m, memberDn, StringComparison.OrdinalIgnoreCase));
        try { await PersistRoleAsync(role, ct); }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to persist member removal"); }

        return role;
    }

    // ── Effective Permission Resolution ───────────────────────

    public async Task<EffectivePermissions> GetPermissionsForUserAsync(string userDn, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);

        var result = new EffectivePermissions { UserDn = userDn };
        var allPermissions = new HashSet<string>();

        // Resolve group memberships for the user
        var userGroups = await ResolveGroupMembershipsAsync(userDn, ct);
        var allIdentities = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { userDn };
        foreach (var g in userGroups) allIdentities.Add(g);

        foreach (var role in _roles.Values)
        {
            foreach (var member in role.AssignedMembers)
            {
                if (allIdentities.Contains(member))
                {
                    result.Roles.Add(new EffectiveRoleSummary
                    {
                        RoleId = role.Id,
                        RoleName = role.Name,
                        AssignedVia = member,
                        ScopeDns = role.ScopeDns,
                    });

                    foreach (var perm in role.Permissions)
                    {
                        allPermissions.Add(perm);
                    }
                    break; // Only add role once per user
                }
            }
        }

        result.Permissions = allPermissions.OrderBy(p => p).ToList();
        return result;
    }

    public async Task<bool> HasPermissionAsync(string userDn, string permissionKey, string targetDn = null, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);

        var userGroups = await ResolveGroupMembershipsAsync(userDn, ct);
        var allIdentities = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { userDn };
        foreach (var g in userGroups) allIdentities.Add(g);

        foreach (var role in _roles.Values)
        {
            if (!role.Permissions.Contains(permissionKey))
                continue;

            var isMember = role.AssignedMembers.Any(m => allIdentities.Contains(m));
            if (!isMember)
                continue;

            // Check scope: empty ScopeDns means global
            if (role.ScopeDns.Count == 0)
                return true;

            if (targetDn == null)
                return true;

            // Check if targetDn is under any of the scoped OUs
            if (role.ScopeDns.Any(scope =>
                targetDn.EndsWith(scope, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<List<string>> ResolveGroupMembershipsAsync(string userDn, CancellationToken ct)
    {
        var groups = new List<string>();

        try
        {
            var user = await _store.GetByDnAsync("default", userDn, ct);
            if (user != null)
            {
                groups.AddRange(user.MemberOf);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to resolve group memberships for {UserDn}", userDn);
        }

        return groups;
    }
}
