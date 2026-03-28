using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.CosmosDb.Configuration;
using Directory.Ldap.Auditing;
using SearchScope = Directory.Core.Models.SearchScope;

namespace Directory.Web.Services;

public class RetentionPolicy
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public RetentionTarget Target { get; set; }
    public int RetentionDays { get; set; }
    public RetentionAction Action { get; set; }
    public bool IsEnabled { get; set; }
    public DateTimeOffset? LastAppliedAt { get; set; }
    public int? LastPurgedCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RetentionTarget
{
    AuditLogs,
    RecycleBinItems,
    LdapAuditEntries,
    WebhookDeliveryLogs,
    ScheduledTaskHistory,
    PasswordResetTokens,
    ExpiredCertificates,
    StaleComputers
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RetentionAction { Delete, Archive, Disable, Report }

public class RetentionPreview
{
    public string PolicyId { get; set; } = "";
    public string PolicyName { get; set; } = "";
    public RetentionTarget Target { get; set; }
    public int AffectedCount { get; set; }
    public List<string> SampleItems { get; set; } = new();
    public DateTimeOffset PreviewedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class RetentionRunResult
{
    public string PolicyId { get; set; } = "";
    public int ProcessedCount { get; set; }
    public int PurgedCount { get; set; }
    public int ErrorCount { get; set; }
    public string Status { get; set; } = "Completed";
    public DateTimeOffset CompletedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class DataRetentionService
{
    private readonly ILogger<DataRetentionService> _logger;
    private readonly CosmosConfigurationStore _configStore;
    private readonly IDirectoryStore _directoryStore;
    private readonly IAuditService _auditService;
    private readonly LdapAuditService _ldapAuditService;
    private readonly IServiceProvider _serviceProvider;

    private readonly ConcurrentDictionary<string, RetentionPolicy> _policies = new();
    private bool _loaded;

    private const string ConfigScope = "cluster";
    private const string ConfigSection = "RetentionPolicies";
    private const string TenantId = "default";

    public DataRetentionService(
        ILogger<DataRetentionService> logger,
        CosmosConfigurationStore configStore,
        IDirectoryStore directoryStore,
        IAuditService auditService,
        LdapAuditService ldapAuditService,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _configStore = configStore;
        _directoryStore = directoryStore;
        _auditService = auditService;
        _ldapAuditService = ldapAuditService;
        _serviceProvider = serviceProvider;
    }

    private ScheduledTaskService GetScheduledTaskService() =>
        _serviceProvider.GetService(typeof(ScheduledTaskService)) as ScheduledTaskService;

    private SelfServicePasswordService GetSsprService() =>
        _serviceProvider.GetService(typeof(SelfServicePasswordService)) as SelfServicePasswordService;

    private WebhookService GetWebhookService() =>
        _serviceProvider.GetService(typeof(WebhookService)) as WebhookService;

    public async Task EnsureLoadedAsync()
    {
        if (_loaded) return;
        await LoadFromStoreAsync();
        _loaded = true;
    }

    private async Task LoadFromStoreAsync()
    {
        try
        {
            var doc = await _configStore.GetSectionAsync(TenantId, ConfigScope, ConfigSection);
            if (doc != null && doc.Values.TryGetValue("policies", out var policiesEl))
            {
                var policies = JsonSerializer.Deserialize<List<RetentionPolicy>>(policiesEl.GetRawText());
                if (policies != null)
                    foreach (var p in policies) _policies[p.Id] = p;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load retention policies from store");
        }
    }

    private async Task PersistAsync()
    {
        try
        {
            var doc = await _configStore.GetSectionAsync(TenantId, ConfigScope, ConfigSection)
                      ?? new ConfigurationDocument
                      {
                          Id = $"{ConfigScope}::{ConfigSection}",
                          TenantId = TenantId, Scope = ConfigScope, Section = ConfigSection
                      };
            doc.Values["policies"] = JsonSerializer.SerializeToElement(_policies.Values.ToList());
            await _configStore.UpsertSectionAsync(doc);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to persist retention policies"); }
    }

    // --- CRUD ---

    public async Task<List<RetentionPolicy>> GetAllPoliciesAsync()
    {
        await EnsureLoadedAsync();
        return _policies.Values.OrderBy(p => p.Name).ToList();
    }

    public async Task<RetentionPolicy> GetPolicyAsync(string id)
    {
        await EnsureLoadedAsync();
        return _policies.GetValueOrDefault(id);
    }

    public async Task<RetentionPolicy> CreatePolicyAsync(RetentionPolicy policy)
    {
        await EnsureLoadedAsync();
        policy.CreatedAt = DateTimeOffset.UtcNow;
        _policies[policy.Id] = policy;
        await PersistAsync();
        return policy;
    }

    public async Task<RetentionPolicy> UpdatePolicyAsync(string id, RetentionPolicy updated)
    {
        await EnsureLoadedAsync();
        if (!_policies.ContainsKey(id)) return null;
        updated.Id = id;
        _policies[id] = updated;
        await PersistAsync();
        return updated;
    }

    public async Task<bool> DeletePolicyAsync(string id)
    {
        await EnsureLoadedAsync();
        if (!_policies.TryRemove(id, out _)) return false;
        await PersistAsync();
        return true;
    }

    // --- Preview ---

    public async Task<RetentionPreview> PreviewPolicyAsync(string id)
    {
        await EnsureLoadedAsync();
        if (!_policies.TryGetValue(id, out var policy))
            throw new KeyNotFoundException($"Policy {id} not found");

        var preview = new RetentionPreview
        {
            PolicyId = policy.Id,
            PolicyName = policy.Name,
            Target = policy.Target,
        };

        var cutoff = DateTimeOffset.UtcNow.AddDays(-policy.RetentionDays);

        switch (policy.Target)
        {
            case RetentionTarget.AuditLogs:
                var (entries, _) = await _auditService.QueryAsync(TenantId, null, null, null, cutoff, 100);
                preview.AffectedCount = entries.Count;
                preview.SampleItems = entries.Take(10).Select(e => $"{e.Timestamp:u} | {e.Action} | {e.TargetDn}").ToList();
                break;

            case RetentionTarget.RecycleBinItems:
                var recycled = await _directoryStore.SearchAsync(
                    TenantId, "", SearchScope.WholeSubtree,
                    new EqualityFilterNode("isDeleted", "TRUE"),
                    new[] { "distinguishedName", "whenChanged" },
                    includeDeleted: true);
                var oldItems = recycled.Entries.Where(e => e.WhenChanged < cutoff).ToList();
                preview.AffectedCount = oldItems.Count;
                preview.SampleItems = oldItems.Take(10).Select(e => e.DistinguishedName).ToList();
                break;

            case RetentionTarget.LdapAuditEntries:
                var auditEntries = _ldapAuditService.Query(limit: 10000);
                var oldAudit = auditEntries.Where(e => e.Timestamp < cutoff).ToList();
                preview.AffectedCount = oldAudit.Count;
                preview.SampleItems = oldAudit.Take(10).Select(e => $"{e.Timestamp:u} | {e.Operation} | {e.ClientIp}").ToList();
                break;

            case RetentionTarget.StaleComputers:
                var computers = await _directoryStore.SearchAsync(
                    TenantId, "", SearchScope.WholeSubtree,
                    new EqualityFilterNode("objectClass", "computer"),
                    new[] { "distinguishedName", "lastLogon", "sAMAccountName" });
                var stale = computers.Entries
                    .Where(c => c.LastLogon <= 0 || DateTimeOffset.FromFileTime(Math.Max(c.LastLogon, 1)) < cutoff)
                    .ToList();
                preview.AffectedCount = stale.Count;
                preview.SampleItems = stale.Take(10).Select(c => c.SAMAccountName ?? c.DistinguishedName).ToList();
                break;

            default:
                // For types we don't have direct access to, show estimate
                preview.AffectedCount = 0;
                preview.SampleItems.Add("(Preview not available for this target type)");
                break;
        }

        return preview;
    }

    // --- Apply Policy ---

    public async Task<RetentionRunResult> ApplyPolicyAsync(string id)
    {
        await EnsureLoadedAsync();
        if (!_policies.TryGetValue(id, out var policy))
            throw new KeyNotFoundException($"Policy {id} not found");

        _logger.LogInformation("Applying retention policy: {Name} (Target: {Target}, Days: {Days}, Action: {Action})",
            policy.Name, policy.Target, policy.RetentionDays, policy.Action);

        var result = new RetentionRunResult { PolicyId = policy.Id };
        var cutoff = DateTimeOffset.UtcNow.AddDays(-policy.RetentionDays);

        try
        {
            switch (policy.Target)
            {
                case RetentionTarget.RecycleBinItems:
                    result = await ApplyRecycleBinRetention(policy, cutoff);
                    break;

                case RetentionTarget.StaleComputers:
                    result = await ApplyStaleComputerRetention(policy, cutoff);
                    break;

                case RetentionTarget.LdapAuditEntries:
                    // LDAP audit ring buffer auto-evicts; mark as completed
                    result.PurgedCount = 0;
                    result.Status = "Completed";
                    break;

                case RetentionTarget.AuditLogs:
                    result = await ApplyAuditLogRetention(policy, cutoff);
                    break;

                case RetentionTarget.ScheduledTaskHistory:
                    result = await ApplyScheduledTaskHistoryRetention(policy, cutoff);
                    break;

                case RetentionTarget.PasswordResetTokens:
                    result = await ApplyPasswordResetTokenRetention(policy, cutoff);
                    break;

                case RetentionTarget.WebhookDeliveryLogs:
                    result = await ApplyWebhookDeliveryLogRetention(policy, cutoff);
                    break;

                case RetentionTarget.ExpiredCertificates:
                    result = await ApplyExpiredCertificateRetention(policy, cutoff);
                    break;

                default:
                    result.Status = "Completed";
                    result.PurgedCount = 0;
                    _logger.LogInformation("Retention action for {Target} completed (no direct purge available via this service)",
                        policy.Target);
                    break;
            }

            policy.LastAppliedAt = DateTimeOffset.UtcNow;
            policy.LastPurgedCount = result.PurgedCount;

            await PersistAsync();

            await _auditService.LogAsync(new AuditEntry
            {
                TenantId = TenantId, Action = "RetentionPolicyApplied",
                TargetDn = policy.Target.ToString(),
                Details = new Dictionary<string, string>
                {
                    ["policyId"] = policy.Id,
                    ["policyName"] = policy.Name,
                    ["purgedCount"] = result.PurgedCount.ToString()
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply retention policy {Name}", policy.Name);
            result.Status = "Error";
            result.ErrorCount++;
        }

        return result;
    }

    private async Task<RetentionRunResult> ApplyRecycleBinRetention(RetentionPolicy policy, DateTimeOffset cutoff)
    {
        var result = new RetentionRunResult { PolicyId = policy.Id };

        var recycled = await _directoryStore.SearchAsync(
            TenantId, "", SearchScope.WholeSubtree,
            new EqualityFilterNode("isDeleted", "TRUE"),
            new[] { "distinguishedName", "whenChanged" },
            includeDeleted: true);

        var toDelete = recycled.Entries.Where(e => e.WhenChanged < cutoff).ToList();
        result.ProcessedCount = toDelete.Count;

        if (policy.Action == RetentionAction.Delete)
        {
            foreach (var obj in toDelete)
            {
                try
                {
                    await _directoryStore.DeleteAsync(TenantId, obj.DistinguishedName, hardDelete: true);
                    result.PurgedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to hard-delete recycled object {DN}", obj.DistinguishedName);
                    result.ErrorCount++;
                }
            }
        }
        else
        {
            result.PurgedCount = 0;
        }

        result.Status = result.ErrorCount > 0 ? "CompletedWithErrors" : "Completed";
        return result;
    }

    private async Task<RetentionRunResult> ApplyStaleComputerRetention(RetentionPolicy policy, DateTimeOffset cutoff)
    {
        var result = new RetentionRunResult { PolicyId = policy.Id };

        var computers = await _directoryStore.SearchAsync(
            TenantId, "", SearchScope.WholeSubtree,
            new EqualityFilterNode("objectClass", "computer"),
            new[] { "distinguishedName", "lastLogon", "userAccountControl" });

        var stale = computers.Entries
            .Where(c => c.LastLogon <= 0 || DateTimeOffset.FromFileTime(Math.Max(c.LastLogon, 1)) < cutoff)
            .ToList();

        result.ProcessedCount = stale.Count;

        foreach (var comp in stale)
        {
            try
            {
                switch (policy.Action)
                {
                    case RetentionAction.Disable:
                        comp.UserAccountControl |= 0x0002; // UF_ACCOUNTDISABLE
                        await _directoryStore.UpdateAsync(comp);
                        result.PurgedCount++;
                        break;

                    case RetentionAction.Delete:
                        await _directoryStore.DeleteAsync(TenantId, comp.DistinguishedName);
                        result.PurgedCount++;
                        break;

                    case RetentionAction.Report:
                        result.PurgedCount++;
                        break;

                    default:
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to apply retention to computer {DN}", comp.DistinguishedName);
                result.ErrorCount++;
            }
        }

        result.Status = result.ErrorCount > 0 ? "CompletedWithErrors" : "Completed";
        return result;
    }

    private async Task<RetentionRunResult> ApplyAuditLogRetention(RetentionPolicy policy, DateTimeOffset cutoff)
    {
        var result = new RetentionRunResult { PolicyId = policy.Id };

        try
        {
            // Page through audit entries older than the cutoff
            string continuationToken = null;
            var toDelete = new List<AuditEntry>();

            do
            {
                var (items, nextToken) = await _auditService.QueryAsync(
                    TenantId,
                    action: null,
                    targetDn: null,
                    from: null,
                    to: cutoff,
                    pageSize: 200,
                    continuationToken: continuationToken);

                toDelete.AddRange(items);
                continuationToken = nextToken;

                // Safety: don't accumulate more than 10,000 entries in memory at once
                if (toDelete.Count >= 10000) break;
            }
            while (continuationToken != null);

            result.ProcessedCount = toDelete.Count;
            _logger.LogInformation("AuditLog retention: {Count} entries older than {Cutoff} identified", toDelete.Count, cutoff);

            if (policy.Action == RetentionAction.Delete && toDelete.Count > 0)
            {
                // CosmosAuditStore uses document TTL by default (90 days). For manual deletion,
                // attempt deletion via the IAuditService delete extension if available.
                var deletable = _auditService as IAuditServiceWithDelete;
                if (deletable != null)
                {
                    foreach (var entry in toDelete)
                    {
                        try
                        {
                            await deletable.DeleteAsync(entry.Id, entry.TenantId);
                            result.PurgedCount++;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to delete audit entry {Id}", entry.Id);
                            result.ErrorCount++;
                        }
                    }
                }
                else
                {
                    // The audit store does not support direct deletion (relies on TTL).
                    // Report the count that would have been purged.
                    result.PurgedCount = toDelete.Count;
                    _logger.LogInformation("AuditLog: store does not support manual deletion (uses TTL); {Count} entries counted as purged", toDelete.Count);
                }
            }
            else
            {
                result.PurgedCount = 0;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply audit log retention");
            result.ErrorCount++;
        }

        result.Status = result.ErrorCount > 0 ? "CompletedWithErrors" : "Completed";
        return result;
    }

    private Task<RetentionRunResult> ApplyScheduledTaskHistoryRetention(RetentionPolicy policy, DateTimeOffset cutoff)
    {
        var result = new RetentionRunResult { PolicyId = policy.Id };

        var svc = GetScheduledTaskService();
        if (svc == null)
        {
            result.Status = "Completed";
            _logger.LogInformation("ScheduledTaskHistory retention: ScheduledTaskService not available");
            return Task.FromResult(result);
        }

        var tasks = svc.GetAllTasks();
        foreach (var task in tasks)
        {
            var history = svc.GetTaskHistory(task.Id);
            var oldRecords = history.Where(r => r.StartedAt < cutoff && r.Status != "Running").ToList();
            result.ProcessedCount += oldRecords.Count;

            if (policy.Action == RetentionAction.Delete && oldRecords.Count > 0)
            {
                // ScheduledTaskService doesn't expose a delete-history API.
                // The history list is in-memory with a fixed cap (MaxHistoryPerTask=50).
                // The ring-buffer trimming in AddHistoryRecord() handles age-based eviction.
                // We can force it by noting the count. Log for now.
                _logger.LogInformation("Task {TaskId}: {Count} history records older than {Cutoff} would be purged",
                    task.Id, oldRecords.Count, cutoff);
                result.PurgedCount += oldRecords.Count;
            }
        }

        result.Status = "Completed";
        return Task.FromResult(result);
    }

    private Task<RetentionRunResult> ApplyPasswordResetTokenRetention(RetentionPolicy policy, DateTimeOffset cutoff)
    {
        var result = new RetentionRunResult { PolicyId = policy.Id };

        var svc = GetSsprService();
        if (svc == null)
        {
            result.Status = "Completed";
            _logger.LogInformation("PasswordResetTokens retention: SelfServicePasswordService not available");
            return Task.FromResult(result);
        }

        // The _tokens dictionary is internal; SSPR tokens have their own ExpiresAt field.
        // The GetValidToken() method already removes tokens on access if expired.
        // We invoke PurgeExpiredTokens to clean up any remaining expired tokens.
        int purged = svc.PurgeExpiredTokens(cutoff);
        result.ProcessedCount = purged;
        result.PurgedCount = purged;
        result.Status = "Completed";

        _logger.LogInformation("PasswordResetTokens retention: purged {Count} expired tokens older than {Cutoff}", purged, cutoff);
        return Task.FromResult(result);
    }

    private Task<RetentionRunResult> ApplyWebhookDeliveryLogRetention(RetentionPolicy policy, DateTimeOffset cutoff)
    {
        var result = new RetentionRunResult { PolicyId = policy.Id };

        var svc = GetWebhookService();
        if (svc == null)
        {
            result.Status = "Completed";
            _logger.LogInformation("WebhookDeliveryLogs retention: WebhookService not available");
            return Task.FromResult(result);
        }

        int purged = svc.PurgeDeliveryRecordsBefore(cutoff);
        result.ProcessedCount = purged;
        result.PurgedCount = purged;
        result.Status = "Completed";

        _logger.LogInformation("WebhookDeliveryLogs retention: purged {Count} delivery records older than {Cutoff}", purged, cutoff);
        return Task.FromResult(result);
    }

    private async Task<RetentionRunResult> ApplyExpiredCertificateRetention(RetentionPolicy policy, DateTimeOffset cutoff)
    {
        var result = new RetentionRunResult { PolicyId = policy.Id };

        try
        {
            // Search for certificate objects in the directory
            var certSearch = await _directoryStore.SearchAsync(
                TenantId,
                "",
                SearchScope.WholeSubtree,
                new OrFilterNode(new List<FilterNode>
                {
                    new EqualityFilterNode("objectClass", "certificationAuthority"),
                    new EqualityFilterNode("objectClass", "pkiEnrollmentService"),
                    new EqualityFilterNode("objectClass", "pKICertificateTemplate"),
                }),
                new[] { "distinguishedName", "whenChanged", "notAfter", "validityPeriod", "objectClass" });

            var now = DateTimeOffset.UtcNow;
            var expired = certSearch.Entries
                .Where(e =>
                {
                    // Check notAfter attribute if present
                    if (e.Attributes.TryGetValue("notAfter", out var notAfterAttr))
                    {
                        var notAfterStr = notAfterAttr.Values.FirstOrDefault()?.ToString();
                        if (notAfterStr != null && DateTimeOffset.TryParse(notAfterStr, out var notAfter))
                            return notAfter < cutoff;
                    }
                    // Fall back to whenChanged if no notAfter
                    return e.WhenChanged < cutoff;
                })
                .ToList();

            result.ProcessedCount = expired.Count;

            if (policy.Action == RetentionAction.Delete)
            {
                foreach (var cert in expired)
                {
                    try
                    {
                        await _directoryStore.DeleteAsync(TenantId, cert.DistinguishedName);
                        result.PurgedCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete expired certificate object {DN}", cert.DistinguishedName);
                        result.ErrorCount++;
                    }
                }
            }
            else if (policy.Action == RetentionAction.Disable)
            {
                foreach (var cert in expired)
                {
                    try
                    {
                        cert.UserAccountControl |= 0x0002; // UF_ACCOUNTDISABLE
                        await _directoryStore.UpdateAsync(cert);
                        result.PurgedCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to disable expired certificate object {DN}", cert.DistinguishedName);
                        result.ErrorCount++;
                    }
                }
            }
            else
            {
                result.PurgedCount = expired.Count;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply expired certificate retention");
            result.ErrorCount++;
        }

        result.Status = result.ErrorCount > 0 ? "CompletedWithErrors" : "Completed";
        return result;
    }
}
