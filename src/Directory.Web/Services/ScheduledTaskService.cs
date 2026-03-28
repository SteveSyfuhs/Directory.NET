using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.CosmosDb.Configuration;
using Directory.Dns;
using Microsoft.Extensions.Options;

namespace Directory.Web.Services;

public class ScheduledTask
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public ScheduledTaskType TaskType { get; set; }
    public string CronExpression { get; set; } = "";
    public bool IsEnabled { get; set; }
    public Dictionary<string, string> Parameters { get; set; } = new();
    public DateTimeOffset? LastRunAt { get; set; }
    public string LastRunStatus { get; set; } = "";
    public string LastRunMessage { get; set; } = "";
    public DateTimeOffset? NextRunAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ScheduledTaskType
{
    DnsScavenging,
    BackupExport,
    PasswordExpiryReport,
    StaleAccountCleanup,
    GroupMembershipReport,
    RecycleBinPurge,
    CertificateExpiryCheck,
    Custom
}

public class TaskExecutionRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TaskId { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string Status { get; set; } = "Running";
    public string Message { get; set; } = "";
}

public class ScheduledTaskService : IHostedService, IDisposable
{
    private readonly ILogger<ScheduledTaskService> _logger;
    private readonly CosmosConfigurationStore _configStore;
    private readonly IServiceProvider _serviceProvider;
    private Timer _timer;

    private readonly ConcurrentDictionary<string, ScheduledTask> _tasks = new();
    private readonly ConcurrentDictionary<string, List<TaskExecutionRecord>> _history = new();
    private bool _loaded;

    private const string ConfigScope = "cluster";
    private const string ConfigSection = "ScheduledTasks";
    private const int MaxHistoryPerTask = 50;

    public ScheduledTaskService(
        ILogger<ScheduledTaskService> logger,
        CosmosConfigurationStore configStore,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _configStore = configStore;
        _serviceProvider = serviceProvider;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await LoadFromStoreAsync();
        SeedBuiltInTasks();
        RecalculateNextRuns();

        _timer = new Timer(CheckAndRunDueTasks, null, TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(1));
        _logger.LogInformation("ScheduledTaskService started with {Count} tasks", _tasks.Count);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Change(Timeout.Infinite, 0);
        _logger.LogInformation("ScheduledTaskService stopped");
        return Task.CompletedTask;
    }

    public void Dispose() => _timer?.Dispose();

    // --- Public API ---

    public List<ScheduledTask> GetAllTasks() => _tasks.Values.OrderBy(t => t.Name).ToList();

    public ScheduledTask GetTask(string id) => _tasks.GetValueOrDefault(id);

    public async Task<ScheduledTask> CreateTask(ScheduledTask task)
    {
        task.CreatedAt = DateTimeOffset.UtcNow;
        task.NextRunAt = task.IsEnabled ? GetNextRun(task.CronExpression) : null;
        _tasks[task.Id] = task;
        await PersistAsync();
        return task;
    }

    public async Task<ScheduledTask> UpdateTask(string id, ScheduledTask updated)
    {
        if (!_tasks.ContainsKey(id)) return null;
        updated.Id = id;
        updated.NextRunAt = updated.IsEnabled ? GetNextRun(updated.CronExpression) : null;
        _tasks[id] = updated;
        await PersistAsync();
        return updated;
    }

    public async Task<bool> DeleteTask(string id)
    {
        if (!_tasks.TryRemove(id, out _)) return false;
        _history.TryRemove(id, out _);
        await PersistAsync();
        return true;
    }

    public async Task<TaskExecutionRecord> RunNow(string id)
    {
        if (!_tasks.TryGetValue(id, out var task))
            throw new KeyNotFoundException($"Task {id} not found");

        return await ExecuteTask(task);
    }

    public List<TaskExecutionRecord> GetTaskHistory(string id)
    {
        return _history.TryGetValue(id, out var list) ? list.OrderByDescending(r => r.StartedAt).ToList() : new();
    }

    // --- Scheduling ---

    private void CheckAndRunDueTasks(object state)
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var task in _tasks.Values.Where(t => t.IsEnabled && t.NextRunAt.HasValue && t.NextRunAt <= now))
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await ExecuteTask(task);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error executing scheduled task {TaskName}", task.Name);
                }
            });
        }
    }

    private async Task<TaskExecutionRecord> ExecuteTask(ScheduledTask task)
    {
        var record = new TaskExecutionRecord
        {
            TaskId = task.Id,
            StartedAt = DateTimeOffset.UtcNow,
            Status = "Running"
        };

        task.LastRunAt = record.StartedAt;
        task.LastRunStatus = "Running";

        AddHistoryRecord(task.Id, record);

        try
        {
            var message = await RunTaskLogic(task);
            record.Status = "Success";
            record.Message = message;
            task.LastRunStatus = "Success";
            task.LastRunMessage = message;
        }
        catch (Exception ex)
        {
            record.Status = "Failed";
            record.Message = ex.Message;
            task.LastRunStatus = "Failed";
            task.LastRunMessage = ex.Message;
            _logger.LogError(ex, "Task {TaskName} failed", task.Name);
        }
        finally
        {
            record.CompletedAt = DateTimeOffset.UtcNow;
            task.NextRunAt = task.IsEnabled ? GetNextRun(task.CronExpression) : null;
            await PersistAsync();
        }

        return record;
    }

    private async Task<string> RunTaskLogic(ScheduledTask task)
    {
        // Each task type performs its specific logic
        switch (task.TaskType)
        {
            case ScheduledTaskType.DnsScavenging:
                return await RunDnsScavenging(task);
            case ScheduledTaskType.BackupExport:
                return await RunBackupExport(task);
            case ScheduledTaskType.PasswordExpiryReport:
                return await RunPasswordExpiryReport(task);
            case ScheduledTaskType.StaleAccountCleanup:
                return await RunStaleAccountCleanup(task);
            case ScheduledTaskType.GroupMembershipReport:
                return await RunGroupMembershipReport(task);
            case ScheduledTaskType.RecycleBinPurge:
                return await RunRecycleBinPurge(task);
            case ScheduledTaskType.CertificateExpiryCheck:
                return await RunCertificateExpiryCheck(task);
            case ScheduledTaskType.Custom:
                return "Custom task executed (no-op placeholder).";
            default:
                return "Unknown task type.";
        }
    }

    // --- Task Implementations ---

    private async Task<string> RunDnsScavenging(ScheduledTask task)
    {
        var maxAgeDays = int.Parse(task.Parameters.GetValueOrDefault("maxAgeDays", "14"));
        _logger.LogInformation("DNS Scavenging: removing records older than {Days} days", maxAgeDays);

        using var scope = _serviceProvider.CreateScope();
        var dnsZoneStore = scope.ServiceProvider.GetRequiredService<DnsZoneStore>();
        var dnsOptions = scope.ServiceProvider.GetRequiredService<IOptions<DnsOptions>>();

        var cutoff = DateTimeOffset.UtcNow.AddDays(-maxAgeDays);
        int scanned = 0;
        int removed = 0;

        foreach (var zoneName in dnsOptions.Value.Domains)
        {
            // Fetch all records in the zone (type 0 = all types)
            var allRecords = await dnsZoneStore.GetAllRecordsAsync("default", zoneName, 0);
            scanned += allRecords.Count;

            foreach (var record in allRecords)
            {
                // Skip SOA and NS records - they are essential zone infrastructure
                if (record.Type == DnsRecordType.SOA || record.Type == DnsRecordType.NS)
                    continue;

                // Check the underlying dnsNode's WhenChanged timestamp via the directory store
                var store = scope.ServiceProvider.GetRequiredService<IDirectoryStore>();
                var parts = zoneName.Split('.');
                var domainDn = string.Join(",", parts.Select(p => $"DC={p}"));
                var zoneContainerDn = $"DC={zoneName},CN=MicrosoftDNS,DC=DomainDnsZones,{domainDn}";
                var recordDn = $"DC={record.Name},{zoneContainerDn}";

                var obj = await store.GetByDnAsync("default", recordDn);
                if (obj != null && obj.WhenChanged < cutoff)
                {
                    _logger.LogInformation("DNS Scavenging: removing stale record {Name} ({Type}) in zone {Zone}, last updated {When}",
                        record.Name, record.Type, zoneName, obj.WhenChanged);
                    await dnsZoneStore.DeleteRecordAsync("default", zoneName, record.Name);
                    removed++;
                }
            }
        }

        return $"DNS scavenging completed. Scanned {scanned} records across {dnsOptions.Value.Domains.Count} zone(s), removed {removed} stale record(s) older than {maxAgeDays} days.";
    }

    private async Task<string> RunBackupExport(ScheduledTask task)
    {
        var format = task.Parameters.GetValueOrDefault("format", "json");
        _logger.LogInformation("Backup export in {Format} format", format);

        using var scope = _serviceProvider.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IDirectoryStore>();
        var configStore = scope.ServiceProvider.GetRequiredService<CosmosConfigurationStore>();

        // Search for all objects in the directory
        var result = await store.SearchAsync(
            "default", "", SearchScope.WholeSubtree,
            new PresenceFilterNode("objectClass"),
            null, 0, 0);

        var allObjects = new List<DirectoryObject>(result.Entries);

        // Page through remaining results if there is a continuation token
        while (result.ContinuationToken != null)
        {
            result = await store.SearchAsync(
                "default", "", SearchScope.WholeSubtree,
                new PresenceFilterNode("objectClass"),
                null, 0, 0,
                continuationToken: result.ContinuationToken);
            allObjects.AddRange(result.Entries);

            // Safety limit to prevent unbounded memory usage
            if (allObjects.Count >= 100000) break;
        }

        // Serialize to JSON and store as a configuration document for retrieval
        var backupPayload = JsonSerializer.Serialize(allObjects, new JsonSerializerOptions
        {
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });

        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
        var backupDoc = new ConfigurationDocument
        {
            Id = $"backup::{timestamp}",
            TenantId = "default",
            Scope = "backup",
            Section = timestamp
        };
        backupDoc.Values["objectCount"] = JsonSerializer.SerializeToElement(allObjects.Count);
        backupDoc.Values["exportedAt"] = JsonSerializer.SerializeToElement(DateTimeOffset.UtcNow);
        backupDoc.Values["format"] = JsonSerializer.SerializeToElement(format);
        backupDoc.Values["data"] = JsonSerializer.SerializeToElement(backupPayload);

        await configStore.UpsertSectionAsync(backupDoc);

        _logger.LogInformation("Backup export completed: {Count} objects exported at {Timestamp}", allObjects.Count, timestamp);
        return $"Backup export completed. Exported {allObjects.Count} directory objects in {format} format. Backup ID: {timestamp}.";
    }

    private async Task<string> RunPasswordExpiryReport(ScheduledTask task)
    {
        var daysAhead = int.Parse(task.Parameters.GetValueOrDefault("daysAhead", "14"));
        _logger.LogInformation("Password expiry report: checking {Days} days ahead", daysAhead);

        using var scope = _serviceProvider.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IDirectoryStore>();

        // Search for all user objects
        var result = await store.SearchAsync(
            "default", "", SearchScope.WholeSubtree,
            new EqualityFilterNode("objectClass", "user"),
            new[] { "distinguishedName", "sAMAccountName", "userPrincipalName", "pwdLastSet", "userAccountControl", "displayName" });

        // Default max password age is 42 days (AD default)
        var defaultMaxAgeTicks = TimeSpan.FromDays(42).Ticks;
        var now = DateTimeOffset.UtcNow;
        var warningThreshold = now.AddDays(daysAhead);

        var expiringUsers = new List<string>();
        var expiredUsers = new List<string>();

        foreach (var user in result.Entries)
        {
            // Skip disabled accounts (UF_ACCOUNTDISABLE = 0x0002)
            if ((user.UserAccountControl & 0x0002) != 0) continue;

            // Skip accounts with password never expires (UF_DONT_EXPIRE_PASSWD = 0x10000)
            if ((user.UserAccountControl & 0x10000) != 0) continue;

            // PwdLastSet of 0 means "must change at next logon" - skip
            if (user.PwdLastSet <= 0) continue;

            var pwdLastSetTime = DateTimeOffset.FromFileTime(user.PwdLastSet);
            var expiryTime = pwdLastSetTime.AddTicks(defaultMaxAgeTicks);
            var identifier = user.UserPrincipalName ?? user.SAMAccountName ?? user.DistinguishedName;

            if (expiryTime < now)
            {
                expiredUsers.Add(identifier);
            }
            else if (expiryTime <= warningThreshold)
            {
                var daysLeft = (int)(expiryTime - now).TotalDays;
                expiringUsers.Add($"{identifier} (expires in {daysLeft} day(s))");
            }
        }

        var report = new StringBuilder();
        report.AppendLine($"Password Expiry Report - Generated {now:u}");
        report.AppendLine($"Lookahead window: {daysAhead} days");
        report.AppendLine($"Total users checked: {result.Entries.Count}");
        report.AppendLine($"Already expired: {expiredUsers.Count}");
        report.AppendLine($"Expiring within {daysAhead} days: {expiringUsers.Count}");

        // Store the report in the configuration store
        var configStore = scope.ServiceProvider.GetRequiredService<CosmosConfigurationStore>();
        var timestamp = now.ToString("yyyyMMdd-HHmmss");
        var reportDoc = new ConfigurationDocument
        {
            Id = $"report::password-expiry::{timestamp}",
            TenantId = "default",
            Scope = "reports",
            Section = $"password-expiry-{timestamp}"
        };
        reportDoc.Values["generatedAt"] = JsonSerializer.SerializeToElement(now);
        reportDoc.Values["expiredCount"] = JsonSerializer.SerializeToElement(expiredUsers.Count);
        reportDoc.Values["expiringCount"] = JsonSerializer.SerializeToElement(expiringUsers.Count);
        reportDoc.Values["expiredUsers"] = JsonSerializer.SerializeToElement(expiredUsers);
        reportDoc.Values["expiringUsers"] = JsonSerializer.SerializeToElement(expiringUsers);

        await configStore.UpsertSectionAsync(reportDoc);

        return $"Password expiry report generated. {expiredUsers.Count} expired, {expiringUsers.Count} expiring within {daysAhead} days (out of {result.Entries.Count} users checked). Report ID: {timestamp}.";
    }

    private async Task<string> RunStaleAccountCleanup(ScheduledTask task)
    {
        var inactiveDays = int.Parse(task.Parameters.GetValueOrDefault("inactiveDays", "90"));
        _logger.LogInformation("Stale account cleanup: {Days} days inactive threshold", inactiveDays);

        using var scope = _serviceProvider.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IDirectoryStore>();

        // Search for all user accounts
        var result = await store.SearchAsync(
            "default", "", SearchScope.WholeSubtree,
            new EqualityFilterNode("objectClass", "user"),
            new[] { "distinguishedName", "sAMAccountName", "userPrincipalName", "lastLogon", "userAccountControl", "whenChanged" });

        var cutoff = DateTimeOffset.UtcNow.AddDays(-inactiveDays);
        int disabled = 0;
        int alreadyDisabled = 0;
        int skipped = 0;

        foreach (var user in result.Entries)
        {
            // Skip already disabled accounts
            if ((user.UserAccountControl & 0x0002) != 0)
            {
                alreadyDisabled++;
                continue;
            }

            // Determine if the account is stale based on lastLogon
            bool isStale;

            if (user.LastLogon <= 0)
            {
                // Never logged in - check whenChanged as a fallback
                isStale = user.WhenChanged < cutoff;
            }
            else
            {
                var lastLogonTime = DateTimeOffset.FromFileTime(Math.Max(user.LastLogon, 1));
                isStale = lastLogonTime < cutoff;
            }

            if (!isStale)
            {
                skipped++;
                continue;
            }

            // Disable the stale account by setting UF_ACCOUNTDISABLE (0x0002)
            user.UserAccountControl |= 0x0002;
            user.WhenChanged = DateTimeOffset.UtcNow;

            try
            {
                await store.UpdateAsync(user);
                disabled++;
                _logger.LogInformation("Stale account disabled: {SAM} (last logon: {LastLogon})",
                    user.SAMAccountName ?? user.DistinguishedName,
                    user.LastLogon > 0 ? DateTimeOffset.FromFileTime(user.LastLogon).ToString("u") : "never");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to disable stale account {DN}", user.DistinguishedName);
            }
        }

        return $"Stale account cleanup completed. Checked {result.Entries.Count} user(s), disabled {disabled} stale account(s) inactive for >{inactiveDays} days. {alreadyDisabled} already disabled, {skipped} active.";
    }

    private async Task<string> RunGroupMembershipReport(ScheduledTask task)
    {
        _logger.LogInformation("Generating group membership report");

        using var scope = _serviceProvider.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IDirectoryStore>();

        // Search for all group objects
        var result = await store.SearchAsync(
            "default", "", SearchScope.WholeSubtree,
            new EqualityFilterNode("objectClass", "group"),
            new[] { "distinguishedName", "sAMAccountName", "displayName", "member", "groupType", "description" });

        var reportEntries = new List<Dictionary<string, object>>();
        int totalMembers = 0;
        int emptyGroups = 0;

        foreach (var group in result.Entries)
        {
            var memberCount = group.Member.Count;
            totalMembers += memberCount;

            if (memberCount == 0) emptyGroups++;

            // Classify group type
            string groupScope;
            if ((group.GroupType & 0x00000002) != 0) groupScope = "Global";
            else if ((group.GroupType & 0x00000004) != 0) groupScope = "DomainLocal";
            else if ((group.GroupType & 0x00000008) != 0) groupScope = "Universal";
            else groupScope = "Unknown";

            var isSecurity = (group.GroupType & unchecked((int)0x80000000)) != 0;

            reportEntries.Add(new Dictionary<string, object>
            {
                ["dn"] = group.DistinguishedName,
                ["name"] = group.SAMAccountName ?? group.Cn ?? group.DistinguishedName,
                ["displayName"] = group.DisplayName ?? "",
                ["description"] = group.Description ?? "",
                ["scope"] = groupScope,
                ["type"] = isSecurity ? "Security" : "Distribution",
                ["memberCount"] = memberCount,
                ["members"] = group.Member.Take(100).ToList() // Cap members in report to avoid huge payloads
            });
        }

        // Persist the report
        var configStore = scope.ServiceProvider.GetRequiredService<CosmosConfigurationStore>();
        var now = DateTimeOffset.UtcNow;
        var timestamp = now.ToString("yyyyMMdd-HHmmss");
        var reportDoc = new ConfigurationDocument
        {
            Id = $"report::group-membership::{timestamp}",
            TenantId = "default",
            Scope = "reports",
            Section = $"group-membership-{timestamp}"
        };
        reportDoc.Values["generatedAt"] = JsonSerializer.SerializeToElement(now);
        reportDoc.Values["totalGroups"] = JsonSerializer.SerializeToElement(result.Entries.Count);
        reportDoc.Values["emptyGroups"] = JsonSerializer.SerializeToElement(emptyGroups);
        reportDoc.Values["totalMemberships"] = JsonSerializer.SerializeToElement(totalMembers);
        reportDoc.Values["groups"] = JsonSerializer.SerializeToElement(reportEntries);

        await configStore.UpsertSectionAsync(reportDoc);

        return $"Group membership report generated. {result.Entries.Count} group(s) found, {totalMembers} total membership(s), {emptyGroups} empty group(s). Report ID: {timestamp}.";
    }

    private async Task<string> RunRecycleBinPurge(ScheduledTask task)
    {
        var maxAgeDays = int.Parse(task.Parameters.GetValueOrDefault("maxAgeDays", "180"));
        _logger.LogInformation("Recycle bin purge: removing items older than {Days} days", maxAgeDays);

        using var scope = _serviceProvider.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IDirectoryStore>();

        var cutoff = DateTimeOffset.UtcNow.AddDays(-maxAgeDays);

        // Search for all soft-deleted objects
        var result = await store.SearchAsync(
            "default", "", SearchScope.WholeSubtree,
            new EqualityFilterNode("isDeleted", "TRUE"),
            new[] { "distinguishedName", "whenChanged", "deletedTime", "objectClass", "sAMAccountName" },
            includeDeleted: true);

        int purged = 0;
        int errors = 0;
        int retained = 0;

        foreach (var obj in result.Entries)
        {
            // Use deletedTime if available, otherwise fall back to whenChanged
            var deletionTime = obj.DeletedTime ?? obj.WhenChanged;

            if (deletionTime >= cutoff)
            {
                retained++;
                continue;
            }

            try
            {
                await store.DeleteAsync("default", obj.DistinguishedName, hardDelete: true);
                purged++;
                _logger.LogDebug("Recycle bin purge: permanently deleted {DN} (deleted {When})",
                    obj.DistinguishedName, deletionTime);
            }
            catch (Exception ex)
            {
                errors++;
                _logger.LogWarning(ex, "Recycle bin purge: failed to hard-delete {DN}", obj.DistinguishedName);
            }
        }

        return $"Recycle bin purge completed. Found {result.Entries.Count} deleted object(s), permanently removed {purged}, retained {retained} (within {maxAgeDays}-day window), {errors} error(s).";
    }

    private async Task<string> RunCertificateExpiryCheck(ScheduledTask task)
    {
        var daysAhead = int.Parse(task.Parameters.GetValueOrDefault("daysAhead", "30"));
        _logger.LogInformation("Certificate expiry check: {Days} days ahead", daysAhead);

        using var scope = _serviceProvider.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IDirectoryStore>();

        var now = DateTimeOffset.UtcNow;
        var warningThreshold = now.AddDays(daysAhead);

        // Search for certificate-related objects (CA, enrollment services, and certificate templates)
        var result = await store.SearchAsync(
            "default", "", SearchScope.WholeSubtree,
            new OrFilterNode(new List<FilterNode>
            {
                new EqualityFilterNode("objectClass", "certificationAuthority"),
                new EqualityFilterNode("objectClass", "pkiEnrollmentService"),
                new EqualityFilterNode("objectClass", "pKICertificateTemplate"),
            }),
            new[] { "distinguishedName", "cn", "whenChanged", "notAfter", "validityPeriod", "objectClass" });

        var expiring = new List<string>();
        var expired = new List<string>();
        int healthy = 0;

        foreach (var certObj in result.Entries)
        {
            var identifier = certObj.Cn ?? certObj.DistinguishedName;

            // Try to read the notAfter attribute for expiration time
            DateTimeOffset expiryTime = DateTimeOffset.MaxValue;
            bool hasExpiry = false;

            if (certObj.Attributes.TryGetValue("notAfter", out var notAfterAttr))
            {
                var notAfterStr = notAfterAttr.GetFirstString();
                if (notAfterStr != null && DateTimeOffset.TryParse(notAfterStr, out var parsed))
                {
                    expiryTime = parsed;
                    hasExpiry = true;
                }
            }

            if (!hasExpiry)
            {
                // No explicit expiry attribute - skip as we cannot determine certificate validity
                healthy++;
                continue;
            }

            if (expiryTime < now)
            {
                expired.Add($"{identifier} (expired {expiryTime:u})");
            }
            else if (expiryTime <= warningThreshold)
            {
                var daysLeft = (int)(expiryTime - now).TotalDays;
                expiring.Add($"{identifier} (expires in {daysLeft} day(s) on {expiryTime:u})");
            }
            else
            {
                healthy++;
            }
        }

        // Persist the report
        var configStore = scope.ServiceProvider.GetRequiredService<CosmosConfigurationStore>();
        var timestamp = now.ToString("yyyyMMdd-HHmmss");
        var reportDoc = new ConfigurationDocument
        {
            Id = $"report::certificate-expiry::{timestamp}",
            TenantId = "default",
            Scope = "reports",
            Section = $"certificate-expiry-{timestamp}"
        };
        reportDoc.Values["generatedAt"] = JsonSerializer.SerializeToElement(now);
        reportDoc.Values["expiredCount"] = JsonSerializer.SerializeToElement(expired.Count);
        reportDoc.Values["expiringCount"] = JsonSerializer.SerializeToElement(expiring.Count);
        reportDoc.Values["healthyCount"] = JsonSerializer.SerializeToElement(healthy);
        reportDoc.Values["expired"] = JsonSerializer.SerializeToElement(expired);
        reportDoc.Values["expiring"] = JsonSerializer.SerializeToElement(expiring);

        await configStore.UpsertSectionAsync(reportDoc);

        return $"Certificate expiry check completed. {result.Entries.Count} certificate object(s) checked: {expired.Count} expired, {expiring.Count} expiring within {daysAhead} days, {healthy} healthy. Report ID: {timestamp}.";
    }

    // --- History ---

    private void AddHistoryRecord(string taskId, TaskExecutionRecord record)
    {
        var list = _history.GetOrAdd(taskId, _ => new List<TaskExecutionRecord>());
        lock (list)
        {
            list.Add(record);
            if (list.Count > MaxHistoryPerTask)
                list.RemoveRange(0, list.Count - MaxHistoryPerTask);
        }
    }

    // --- Cron Parsing ---

    /// <summary>
    /// Minimal cron parser supporting: minute hour day-of-month month day-of-week.
    /// Supports * and specific numeric values. Checks next 366 days.
    /// </summary>
    public static DateTimeOffset? GetNextRun(string cronExpression)
    {
        if (string.IsNullOrWhiteSpace(cronExpression)) return null;

        var parts = cronExpression.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5) return null;

        var minuteSet = ParseCronField(parts[0], 0, 59);
        var hourSet = ParseCronField(parts[1], 0, 23);
        var domSet = ParseCronField(parts[2], 1, 31);
        var monthSet = ParseCronField(parts[3], 1, 12);
        var dowSet = ParseCronField(parts[4], 0, 6);

        if (minuteSet == null || hourSet == null || domSet == null || monthSet == null || dowSet == null)
            return null;

        var now = DateTimeOffset.UtcNow;
        var candidate = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, TimeSpan.Zero)
            .AddMinutes(1);

        for (int i = 0; i < 366 * 24 * 60; i++)
        {
            if (monthSet.Contains(candidate.Month) &&
                domSet.Contains(candidate.Day) &&
                dowSet.Contains((int)candidate.DayOfWeek) &&
                hourSet.Contains(candidate.Hour) &&
                minuteSet.Contains(candidate.Minute))
            {
                return candidate;
            }
            candidate = candidate.AddMinutes(1);
        }

        return null;
    }

    private static HashSet<int> ParseCronField(string field, int min, int max)
    {
        var result = new HashSet<int>();

        foreach (var part in field.Split(','))
        {
            var trimmed = part.Trim();

            if (trimmed == "*")
            {
                for (int i = min; i <= max; i++) result.Add(i);
                continue;
            }

            // Handle */N step values
            if (trimmed.StartsWith("*/") && int.TryParse(trimmed[2..], out var step) && step > 0)
            {
                for (int i = min; i <= max; i += step) result.Add(i);
                continue;
            }

            // Handle ranges like 1-5
            if (trimmed.Contains('-'))
            {
                var rangeParts = trimmed.Split('-');
                if (rangeParts.Length == 2 &&
                    int.TryParse(rangeParts[0], out var rangeStart) &&
                    int.TryParse(rangeParts[1], out var rangeEnd))
                {
                    for (int i = rangeStart; i <= rangeEnd && i <= max; i++)
                        if (i >= min) result.Add(i);
                    continue;
                }
                return null;
            }

            if (int.TryParse(trimmed, out var val) && val >= min && val <= max)
            {
                result.Add(val);
                continue;
            }

            return null;
        }

        return result.Count > 0 ? result : null;
    }

    private void RecalculateNextRuns()
    {
        foreach (var task in _tasks.Values)
        {
            task.NextRunAt = task.IsEnabled ? GetNextRun(task.CronExpression) : null;
        }
    }

    // --- Persistence ---

    private async Task LoadFromStoreAsync()
    {
        if (_loaded) return;

        try
        {
            var doc = await _configStore.GetSectionAsync("default", ConfigScope, ConfigSection);
            if (doc != null && doc.Values.TryGetValue("tasks", out var tasksElement))
            {
                var tasks = JsonSerializer.Deserialize<List<ScheduledTask>>(tasksElement.GetRawText());
                if (tasks != null)
                {
                    foreach (var t in tasks)
                        _tasks[t.Id] = t;
                }
            }
            _loaded = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load scheduled tasks from configuration store — starting with defaults");
            _loaded = true;
        }
    }

    private async Task PersistAsync()
    {
        try
        {
            var tasksJson = JsonSerializer.SerializeToElement(_tasks.Values.ToList());

            var doc = await _configStore.GetSectionAsync("default", ConfigScope, ConfigSection)
                      ?? new ConfigurationDocument
                      {
                          Id = $"{ConfigScope}::{ConfigSection}",
                          TenantId = "default",
                          Scope = ConfigScope,
                          Section = ConfigSection
                      };

            doc.Values["tasks"] = tasksJson;
            await _configStore.UpsertSectionAsync(doc);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist scheduled tasks");
        }
    }

    private void SeedBuiltInTasks()
    {
        var builtIns = new[]
        {
            new ScheduledTask
            {
                Id = "builtin-dns-scavenging",
                Name = "DNS Scavenging",
                Description = "Remove stale DNS records that haven't been refreshed within the configured age threshold.",
                TaskType = ScheduledTaskType.DnsScavenging,
                CronExpression = "0 2 * * *",
                IsEnabled = false,
                Parameters = new() { ["maxAgeDays"] = "14" }
            },
            new ScheduledTask
            {
                Id = "builtin-backup-export",
                Name = "Nightly Backup Export",
                Description = "Export directory data as JSON backup on a nightly schedule.",
                TaskType = ScheduledTaskType.BackupExport,
                CronExpression = "0 3 * * *",
                IsEnabled = false,
                Parameters = new() { ["format"] = "json" }
            },
            new ScheduledTask
            {
                Id = "builtin-password-expiry",
                Name = "Password Expiry Report",
                Description = "Generate a report of user accounts with passwords expiring in the configured number of days.",
                TaskType = ScheduledTaskType.PasswordExpiryReport,
                CronExpression = "0 7 * * 1",
                IsEnabled = false,
                Parameters = new() { ["daysAhead"] = "14" }
            },
            new ScheduledTask
            {
                Id = "builtin-recycle-bin-purge",
                Name = "Recycle Bin Purge",
                Description = "Permanently remove deleted objects from the recycle bin that are older than the configured age.",
                TaskType = ScheduledTaskType.RecycleBinPurge,
                CronExpression = "0 4 * * 0",
                IsEnabled = false,
                Parameters = new() { ["maxAgeDays"] = "180" }
            }
        };

        foreach (var task in builtIns)
        {
            _tasks.TryAdd(task.Id, task);
        }
    }
}
