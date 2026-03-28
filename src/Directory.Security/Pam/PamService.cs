using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Microsoft.Extensions.Logging;

namespace Directory.Security.Pam;

/// <summary>
/// Privileged Access Management (PAM) service providing just-in-time (JIT) privilege elevation,
/// approval workflows, and break-glass emergency access for privileged directory groups.
/// PAM configuration is stored as a custom attribute (msDS-PamData) on the domain root object.
/// </summary>
public class PamService : IDisposable
{
    private const string PamAttributeName = "msDS-PamData";

    private readonly IDirectoryStore _store;
    private readonly INamingContextService _ncService;
    private readonly IPasswordPolicy _passwordPolicy;
    private readonly IAuditService _audit;
    private readonly ILogger<PamService> _logger;
    private readonly Timer _expirationTimer;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public PamService(
        IDirectoryStore store,
        INamingContextService ncService,
        IPasswordPolicy passwordPolicy,
        IAuditService audit,
        ILogger<PamService> logger)
    {
        _store = store;
        _ncService = ncService;
        _passwordPolicy = passwordPolicy;
        _audit = audit;
        _logger = logger;

        // Check for expired activations every 30 seconds
        _expirationTimer = new Timer(CheckExpiredActivations, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    // ── Role Management ──────────────────────────────────────────

    public async Task<List<PrivilegedRole>> GetPrivilegedRolesAsync(CancellationToken ct = default)
    {
        var data = await LoadPamDataAsync(ct);
        return data.Roles;
    }

    public async Task<PrivilegedRole> CreateRoleAsync(PrivilegedRole role, CancellationToken ct = default)
    {
        var data = await LoadPamDataAsync(ct);

        if (data.Roles.Any(r => r.Name.Equals(role.Name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"A privileged role with name '{role.Name}' already exists.");

        role.Id = Guid.NewGuid().ToString();
        data.Roles.Add(role);
        await SavePamDataAsync(data, ct);

        _logger.LogInformation("PAM role created: {RoleName} -> {GroupDn}", role.Name, role.GroupDn);
        await AuditAsync("PamRoleCreated", role.GroupDn, new() { ["roleName"] = role.Name }, ct);

        return role;
    }

    public async Task<PrivilegedRole> UpdateRoleAsync(string roleId, PrivilegedRole updated, CancellationToken ct = default)
    {
        var data = await LoadPamDataAsync(ct);
        var existing = data.Roles.FirstOrDefault(r => r.Id == roleId)
            ?? throw new InvalidOperationException($"Privileged role not found: {roleId}");

        existing.Name = updated.Name;
        existing.GroupDn = updated.GroupDn;
        existing.MaxActivationHours = updated.MaxActivationHours;
        existing.RequireJustification = updated.RequireJustification;
        existing.RequireApproval = updated.RequireApproval;
        existing.Approvers = updated.Approvers;
        existing.RequireMfa = updated.RequireMfa;
        existing.IsEnabled = updated.IsEnabled;

        await SavePamDataAsync(data, ct);

        _logger.LogInformation("PAM role updated: {RoleName}", existing.Name);
        await AuditAsync("PamRoleUpdated", existing.GroupDn, new() { ["roleName"] = existing.Name }, ct);

        return existing;
    }

    public async Task<bool> DeleteRoleAsync(string roleId, CancellationToken ct = default)
    {
        var data = await LoadPamDataAsync(ct);
        var role = data.Roles.FirstOrDefault(r => r.Id == roleId);
        if (role == null) return false;

        // Deactivate any active activations for this role
        var activeForRole = data.Activations
            .Where(a => a.RoleId == roleId && a.Status is ActivationStatus.Active or ActivationStatus.PendingApproval)
            .ToList();

        foreach (var activation in activeForRole)
        {
            if (activation.Status == ActivationStatus.Active)
            {
                await RemoveUserFromGroupAsync(activation.UserDn, activation.GroupDn, ct);
                activation.Status = ActivationStatus.Deactivated;
                activation.DeactivatedAt = DateTimeOffset.UtcNow;
            }
            else
            {
                activation.Status = ActivationStatus.Cancelled;
            }
        }

        data.Roles.Remove(role);
        await SavePamDataAsync(data, ct);

        _logger.LogInformation("PAM role deleted: {RoleName}", role.Name);
        await AuditAsync("PamRoleDeleted", role.GroupDn, new() { ["roleName"] = role.Name }, ct);

        return true;
    }

    // ── Activation Workflow ──────────────────────────────────────

    public async Task<RoleActivation> RequestActivationAsync(
        string userDn, string roleId, string justification, int hours, CancellationToken ct = default)
    {
        var data = await LoadPamDataAsync(ct);
        var role = data.Roles.FirstOrDefault(r => r.Id == roleId)
            ?? throw new InvalidOperationException($"Privileged role not found: {roleId}");

        if (!role.IsEnabled)
            throw new InvalidOperationException($"Privileged role '{role.Name}' is not enabled.");

        if (role.RequireJustification && string.IsNullOrWhiteSpace(justification))
            throw new InvalidOperationException("Justification is required for this role.");

        if (hours < 1 || hours > role.MaxActivationHours)
            throw new InvalidOperationException($"Activation hours must be between 1 and {role.MaxActivationHours}.");

        // Check for existing active activation
        var existingActive = data.Activations.FirstOrDefault(a =>
            a.UserDn.Equals(userDn, StringComparison.OrdinalIgnoreCase) &&
            a.RoleId == roleId &&
            a.Status is ActivationStatus.Active or ActivationStatus.PendingApproval);

        if (existingActive != null)
            throw new InvalidOperationException($"User already has an active or pending activation for role '{role.Name}'.");

        var activation = new RoleActivation
        {
            Id = Guid.NewGuid().ToString(),
            RoleId = roleId,
            RoleName = role.Name,
            UserDn = userDn,
            GroupDn = role.GroupDn,
            Justification = justification,
            RequestedAt = DateTimeOffset.UtcNow,
            RequestedHours = hours,
        };

        if (role.RequireApproval)
        {
            activation.Status = ActivationStatus.PendingApproval;
        }
        else
        {
            // Auto-approve: activate immediately
            activation.Status = ActivationStatus.Active;
            activation.ActivatedAt = DateTimeOffset.UtcNow;
            activation.ExpiresAt = DateTimeOffset.UtcNow.AddHours(hours);
            await AddUserToGroupAsync(userDn, role.GroupDn, ct);
        }

        data.Activations.Add(activation);
        await SavePamDataAsync(data, ct);

        _logger.LogInformation(
            "PAM activation requested: {User} -> {Role} ({Status})",
            userDn, role.Name, activation.Status);

        await AuditAsync("PamActivationRequested", userDn, new()
        {
            ["roleName"] = role.Name,
            ["groupDn"] = role.GroupDn,
            ["status"] = activation.Status.ToString(),
            ["hours"] = hours.ToString(),
            ["justification"] = justification,
        }, ct);

        return activation;
    }

    public async Task<RoleActivation> ApproveActivationAsync(
        string activationId, string approverDn, CancellationToken ct = default)
    {
        var data = await LoadPamDataAsync(ct);
        var activation = data.Activations.FirstOrDefault(a => a.Id == activationId)
            ?? throw new InvalidOperationException($"Activation not found: {activationId}");

        if (activation.Status != ActivationStatus.PendingApproval)
            throw new InvalidOperationException($"Activation is not pending approval (current: {activation.Status}).");

        // Verify approver is authorized
        var role = data.Roles.FirstOrDefault(r => r.Id == activation.RoleId);
        if (role?.Approvers.Count > 0 &&
            !role.Approvers.Any(a => a.Equals(approverDn, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("You are not authorized to approve activations for this role.");
        }

        activation.Status = ActivationStatus.Active;
        activation.ApprovedBy = approverDn;
        activation.ActivatedAt = DateTimeOffset.UtcNow;
        activation.ExpiresAt = DateTimeOffset.UtcNow.AddHours(activation.RequestedHours);

        await AddUserToGroupAsync(activation.UserDn, activation.GroupDn, ct);
        await SavePamDataAsync(data, ct);

        _logger.LogInformation("PAM activation approved: {ActivationId} by {Approver}", activationId, approverDn);
        await AuditAsync("PamActivationApproved", activation.UserDn, new()
        {
            ["activationId"] = activationId,
            ["roleName"] = activation.RoleName,
            ["approvedBy"] = approverDn,
        }, ct);

        return activation;
    }

    public async Task<RoleActivation> DenyActivationAsync(
        string activationId, string denierDn, string reason, CancellationToken ct = default)
    {
        var data = await LoadPamDataAsync(ct);
        var activation = data.Activations.FirstOrDefault(a => a.Id == activationId)
            ?? throw new InvalidOperationException($"Activation not found: {activationId}");

        if (activation.Status != ActivationStatus.PendingApproval)
            throw new InvalidOperationException($"Activation is not pending approval (current: {activation.Status}).");

        activation.Status = ActivationStatus.Denied;
        activation.DeniedBy = denierDn;
        activation.DenyReason = reason;

        await SavePamDataAsync(data, ct);

        _logger.LogInformation("PAM activation denied: {ActivationId} by {Denier}", activationId, denierDn);
        await AuditAsync("PamActivationDenied", activation.UserDn, new()
        {
            ["activationId"] = activationId,
            ["roleName"] = activation.RoleName,
            ["deniedBy"] = denierDn,
            ["reason"] = reason,
        }, ct);

        return activation;
    }

    public async Task<RoleActivation> DeactivateAsync(string activationId, CancellationToken ct = default)
    {
        var data = await LoadPamDataAsync(ct);
        var activation = data.Activations.FirstOrDefault(a => a.Id == activationId)
            ?? throw new InvalidOperationException($"Activation not found: {activationId}");

        if (activation.Status != ActivationStatus.Active)
            throw new InvalidOperationException($"Activation is not active (current: {activation.Status}).");

        activation.Status = ActivationStatus.Deactivated;
        activation.DeactivatedAt = DateTimeOffset.UtcNow;

        await RemoveUserFromGroupAsync(activation.UserDn, activation.GroupDn, ct);
        await SavePamDataAsync(data, ct);

        _logger.LogInformation("PAM activation deactivated: {ActivationId}", activationId);
        await AuditAsync("PamActivationDeactivated", activation.UserDn, new()
        {
            ["activationId"] = activationId,
            ["roleName"] = activation.RoleName,
        }, ct);

        return activation;
    }

    public async Task<List<RoleActivation>> GetActiveActivationsAsync(CancellationToken ct = default)
    {
        var data = await LoadPamDataAsync(ct);
        return data.Activations
            .Where(a => a.Status == ActivationStatus.Active)
            .OrderByDescending(a => a.ActivatedAt)
            .ToList();
    }

    public async Task<List<RoleActivation>> GetPendingActivationsAsync(CancellationToken ct = default)
    {
        var data = await LoadPamDataAsync(ct);
        return data.Activations
            .Where(a => a.Status == ActivationStatus.PendingApproval)
            .OrderByDescending(a => a.RequestedAt)
            .ToList();
    }

    public async Task<List<RoleActivation>> GetActivationHistoryAsync(CancellationToken ct = default)
    {
        var data = await LoadPamDataAsync(ct);
        return data.Activations
            .OrderByDescending(a => a.RequestedAt)
            .ToList();
    }

    // ── Break-Glass ──────────────────────────────────────────────

    public async Task<List<BreakGlassAccount>> GetBreakGlassAccountsAsync(CancellationToken ct = default)
    {
        var data = await LoadPamDataAsync(ct);
        return data.BreakGlassAccounts;
    }

    public async Task<BreakGlassAccount> SealAccountAsync(string accountDn, string description, CancellationToken ct = default)
    {
        var data = await LoadPamDataAsync(ct);

        if (data.BreakGlassAccounts.Any(b => b.AccountDn.Equals(accountDn, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Break-glass account already exists for: {accountDn}");

        // Generate a strong emergency password and set it
        var password = GenerateEmergencyPassword();
        await _passwordPolicy.SetPasswordAsync("default", accountDn, password, ct);

        var account = new BreakGlassAccount
        {
            Id = Guid.NewGuid().ToString(),
            AccountDn = accountDn,
            Description = description,
            IsSealed = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        data.BreakGlassAccounts.Add(account);
        await SavePamDataAsync(data, ct);

        _logger.LogInformation("Break-glass account sealed: {AccountDn}", accountDn);
        await AuditAsync("BreakGlassSealed", accountDn, new() { ["description"] = description }, ct);

        return account;
    }

    public async Task<string> BreakGlassAsync(string accountId, string reason, string accessedBy, CancellationToken ct = default)
    {
        var data = await LoadPamDataAsync(ct);
        var account = data.BreakGlassAccounts.FirstOrDefault(b => b.Id == accountId)
            ?? throw new InvalidOperationException($"Break-glass account not found: {accountId}");

        if (!account.IsSealed)
            throw new InvalidOperationException("Account is already unsealed. Reseal first.");

        // Generate a new temporary password and set it
        var password = GenerateEmergencyPassword();
        await _passwordPolicy.SetPasswordAsync("default", account.AccountDn, password, ct);

        account.IsSealed = false;
        account.LastAccessedAt = DateTimeOffset.UtcNow;
        account.LastAccessedBy = accessedBy;

        await SavePamDataAsync(data, ct);

        _logger.LogWarning(
            "BREAK-GLASS ACCESS: {AccountDn} accessed by {AccessedBy}. Reason: {Reason}",
            account.AccountDn, accessedBy, reason);

        await AuditAsync("BreakGlassAccess", account.AccountDn, new()
        {
            ["accessedBy"] = accessedBy,
            ["reason"] = reason,
        }, ct);

        return password;
    }

    public async Task<BreakGlassAccount> ResealAccountAsync(string accountId, CancellationToken ct = default)
    {
        var data = await LoadPamDataAsync(ct);
        var account = data.BreakGlassAccounts.FirstOrDefault(b => b.Id == accountId)
            ?? throw new InvalidOperationException($"Break-glass account not found: {accountId}");

        // Generate new password (overwriting the break-glass password)
        var password = GenerateEmergencyPassword();
        await _passwordPolicy.SetPasswordAsync("default", account.AccountDn, password, ct);

        account.IsSealed = true;

        await SavePamDataAsync(data, ct);

        _logger.LogInformation("Break-glass account resealed: {AccountDn}", account.AccountDn);
        await AuditAsync("BreakGlassResealed", account.AccountDn, new(), ct);

        return account;
    }

    // ── Expiration Timer ─────────────────────────────────────────

    private async void CheckExpiredActivations(object state)
    {
        try
        {
            var data = await LoadPamDataAsync(CancellationToken.None);
            var now = DateTimeOffset.UtcNow;
            var expired = data.Activations
                .Where(a => a.Status == ActivationStatus.Active && a.ExpiresAt.HasValue && a.ExpiresAt.Value <= now)
                .ToList();

            if (expired.Count == 0) return;

            foreach (var activation in expired)
            {
                activation.Status = ActivationStatus.Expired;
                activation.DeactivatedAt = now;

                try
                {
                    await RemoveUserFromGroupAsync(activation.UserDn, activation.GroupDn, CancellationToken.None);
                    _logger.LogInformation(
                        "PAM activation expired: {User} removed from {Group}",
                        activation.UserDn, activation.GroupDn);

                    await AuditAsync("PamActivationExpired", activation.UserDn, new()
                    {
                        ["roleName"] = activation.RoleName,
                        ["groupDn"] = activation.GroupDn,
                    }, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to remove expired PAM activation: {User} from {Group}",
                        activation.UserDn, activation.GroupDn);
                }
            }

            await SavePamDataAsync(data, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking expired PAM activations");
        }
    }

    // ── Internal Helpers ─────────────────────────────────────────

    private async Task<PamData> LoadPamDataAsync(CancellationToken ct)
    {
        var domainDn = _ncService.GetDomainNc().Dn;
        var obj = await _store.GetByDnAsync("default", domainDn, ct);
        if (obj == null) return new PamData();

        if (obj.Attributes.TryGetValue(PamAttributeName, out var attr) &&
            attr.Values.Count > 0 &&
            attr.Values[0] is string val && !string.IsNullOrWhiteSpace(val))
        {
            try
            {
                return JsonSerializer.Deserialize<PamData>(val, JsonOpts) ?? new PamData();
            }
            catch
            {
                return new PamData();
            }
        }

        return new PamData();
    }

    private async Task SavePamDataAsync(PamData data, CancellationToken ct)
    {
        var domainDn = _ncService.GetDomainNc().Dn;
        var obj = await _store.GetByDnAsync("default", domainDn, ct);
        if (obj == null) return;

        var json = JsonSerializer.Serialize(data, JsonOpts);
        obj.Attributes[PamAttributeName] = new DirectoryAttribute(PamAttributeName, json);
        obj.WhenChanged = DateTimeOffset.UtcNow;

        await _store.UpdateAsync(obj, ct);
    }

    private async Task AddUserToGroupAsync(string userDn, string groupDn, CancellationToken ct)
    {
        var group = await _store.GetByDnAsync("default", groupDn, ct);
        if (group == null)
        {
            _logger.LogWarning("PAM: Group not found for activation: {GroupDn}", groupDn);
            return;
        }

        if (!group.Member.Contains(userDn, StringComparer.OrdinalIgnoreCase))
        {
            group.Member.Add(userDn);
            group.WhenChanged = DateTimeOffset.UtcNow;
            await _store.UpdateAsync(group, ct);
        }

        var user = await _store.GetByDnAsync("default", userDn, ct);
        if (user != null && !user.MemberOf.Contains(groupDn, StringComparer.OrdinalIgnoreCase))
        {
            user.MemberOf.Add(groupDn);
            user.WhenChanged = DateTimeOffset.UtcNow;
            await _store.UpdateAsync(user, ct);
        }
    }

    private async Task RemoveUserFromGroupAsync(string userDn, string groupDn, CancellationToken ct)
    {
        var group = await _store.GetByDnAsync("default", groupDn, ct);
        if (group != null)
        {
            group.Member.RemoveAll(m => m.Equals(userDn, StringComparison.OrdinalIgnoreCase));
            group.WhenChanged = DateTimeOffset.UtcNow;
            await _store.UpdateAsync(group, ct);
        }

        var user = await _store.GetByDnAsync("default", userDn, ct);
        if (user != null)
        {
            user.MemberOf.RemoveAll(m => m.Equals(groupDn, StringComparison.OrdinalIgnoreCase));
            user.WhenChanged = DateTimeOffset.UtcNow;
            await _store.UpdateAsync(user, ct);
        }
    }

    private static string GenerateEmergencyPassword()
    {
        const string upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        const string lower = "abcdefghijklmnopqrstuvwxyz";
        const string digits = "0123456789";
        const string special = "!@#$%^&*()-_=+[]{}|;:,.<>?";
        const string all = upper + lower + digits + special;

        var password = new char[32];
        var rng = RandomNumberGenerator.Create();
        var bytes = new byte[32];
        rng.GetBytes(bytes);

        // Ensure at least one of each category
        password[0] = upper[bytes[0] % upper.Length];
        password[1] = lower[bytes[1] % lower.Length];
        password[2] = digits[bytes[2] % digits.Length];
        password[3] = special[bytes[3] % special.Length];

        for (int i = 4; i < password.Length; i++)
            password[i] = all[bytes[i] % all.Length];

        // Shuffle using Fisher-Yates
        rng.GetBytes(bytes);
        for (int i = password.Length - 1; i > 0; i--)
        {
            int j = bytes[i] % (i + 1);
            (password[i], password[j]) = (password[j], password[i]);
        }

        return new string(password);
    }

    private async Task AuditAsync(string action, string targetDn, Dictionary<string, string> details, CancellationToken ct)
    {
        try
        {
            await _audit.LogAsync(new AuditEntry
            {
                TenantId = "default",
                Action = action,
                TargetDn = targetDn,
                TargetObjectClass = "pamService",
                ActorIdentity = "web-console",
                Details = details,
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write PAM audit entry");
        }
    }

    public void Dispose()
    {
        _expirationTimer.Dispose();
    }
}
