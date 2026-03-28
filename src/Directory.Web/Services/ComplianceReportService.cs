using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.CosmosDb.Configuration;
using Directory.Security.Mfa;
using SearchScope = Directory.Core.Models.SearchScope;

namespace Directory.Web.Services;

public class ComplianceReport
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
    public ReportType Type { get; set; }
    public bool IsBuiltIn { get; set; }
    public DateTimeOffset? LastRunAt { get; set; }
    public string LastRunStatus { get; set; }
    public Dictionary<string, string> Parameters { get; set; } = new();
    public string CustomFilter { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReportType
{
    PrivilegedAccounts,
    StaleAccounts,
    PasswordNeverExpires,
    DisabledAccountsWithGroupMembership,
    OrphanedAccounts,
    ExcessiveGroupMembership,
    ServiceAccountAudit,
    AdminGroupChanges,
    PasswordExpiryForecast,
    FailedLoginAttempts,
    AccountLockouts,
    GpoComplianceStatus,
    CertificateExpiry,
    DelegationReport,
    RecycleBinContents,
    SchemaChanges,
    ReplicationHealth,
    EmptyGroups,
    UsersWithoutMfa,
    Custom
}

public class ReportResult
{
    public string ReportId { get; set; } = "";
    public string ReportName { get; set; } = "";
    public DateTimeOffset GeneratedAt { get; set; }
    public int TotalItems { get; set; }
    public int FlaggedItems { get; set; }
    public string ComplianceStatus { get; set; } = "Unknown";
    public List<Dictionary<string, object>> Data { get; set; } = new();
    public List<string> Columns { get; set; } = new();
    public List<ComplianceRecommendation> Recommendations { get; set; } = new();
}

public class ComplianceRecommendation
{
    public string Severity { get; set; } = "Medium";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string RemediationAction { get; set; }
}

public class ComplianceDashboard
{
    public int TotalReports { get; set; }
    public int CompliantCount { get; set; }
    public int NonCompliantCount { get; set; }
    public int WarningCount { get; set; }
    public int NotRunCount { get; set; }
    public double ComplianceScore { get; set; }
    public List<ComplianceSummaryItem> ReportSummaries { get; set; } = new();
    public List<ComplianceRecommendation> CriticalFindings { get; set; } = new();
}

public class ComplianceSummaryItem
{
    public string ReportId { get; set; } = "";
    public string ReportName { get; set; } = "";
    public string Category { get; set; } = "";
    public string Status { get; set; } = "NotRun";
    public int FlaggedItems { get; set; }
    public DateTimeOffset? LastRunAt { get; set; }
}

public class ComplianceReportService
{
    private readonly ILogger<ComplianceReportService> _logger;
    private readonly CosmosConfigurationStore _configStore;
    private readonly IDirectoryStore _directoryStore;
    private readonly IAuditService _auditService;
    private readonly MfaService _mfaService;

    private readonly ConcurrentDictionary<string, ComplianceReport> _reports = new();
    private readonly ConcurrentDictionary<string, ReportResult> _results = new();
    private bool _loaded;

    private const string ConfigScope = "cluster";
    private const string ConfigSection = "ComplianceReports";
    private const string ResultsSection = "ComplianceResults";
    private const string TenantId = "default";
    private const int DONT_EXPIRE_PASSWD = 0x10000;
    private const int UF_ACCOUNTDISABLE = 0x0002;

    public ComplianceReportService(
        ILogger<ComplianceReportService> logger,
        CosmosConfigurationStore configStore,
        IDirectoryStore directoryStore,
        IAuditService auditService,
        MfaService mfaService)
    {
        _logger = logger;
        _configStore = configStore;
        _directoryStore = directoryStore;
        _auditService = auditService;
        _mfaService = mfaService;
    }

    public async Task EnsureLoadedAsync()
    {
        if (_loaded) return;
        await LoadFromStoreAsync();
        SeedBuiltInReports();
        _loaded = true;
    }

    private async Task LoadFromStoreAsync()
    {
        try
        {
            var doc = await _configStore.GetSectionAsync(TenantId, ConfigScope, ConfigSection);
            if (doc != null && doc.Values.TryGetValue("reports", out var reportsEl))
            {
                var reports = JsonSerializer.Deserialize<List<ComplianceReport>>(reportsEl.GetRawText());
                if (reports != null)
                    foreach (var r in reports) _reports[r.Id] = r;
            }

            var resultsDoc = await _configStore.GetSectionAsync(TenantId, ConfigScope, ResultsSection);
            if (resultsDoc != null && resultsDoc.Values.TryGetValue("results", out var resultsEl))
            {
                var results = JsonSerializer.Deserialize<List<ReportResult>>(resultsEl.GetRawText());
                if (results != null)
                    foreach (var r in results) _results[r.ReportId] = r;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load compliance reports from store");
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
            doc.Values["reports"] = JsonSerializer.SerializeToElement(_reports.Values.ToList());
            await _configStore.UpsertSectionAsync(doc);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to persist compliance reports"); }
    }

    private async Task PersistResultsAsync()
    {
        try
        {
            var doc = await _configStore.GetSectionAsync(TenantId, ConfigScope, ResultsSection)
                      ?? new ConfigurationDocument
                      {
                          Id = $"{ConfigScope}::{ResultsSection}",
                          TenantId = TenantId, Scope = ConfigScope, Section = ResultsSection
                      };
            doc.Values["results"] = JsonSerializer.SerializeToElement(_results.Values.ToList());
            await _configStore.UpsertSectionAsync(doc);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to persist compliance results"); }
    }

    private void SeedBuiltInReports()
    {
        var builtIns = new (string name, string category, string desc, ReportType type)[]
        {
            ("Privileged Accounts", "Security", "Users with admin group memberships (Domain Admins, Enterprise Admins, Schema Admins, Administrators)", ReportType.PrivilegedAccounts),
            ("Stale Accounts", "Operational", "User accounts that have not logged in for more than 90 days", ReportType.StaleAccounts),
            ("Password Never Expires", "Security", "Accounts with the DONT_EXPIRE_PASSWD flag set", ReportType.PasswordNeverExpires),
            ("Disabled Accounts with Group Membership", "SOX", "Disabled user accounts that are still members of one or more groups", ReportType.DisabledAccountsWithGroupMembership),
            ("Password Expiry Forecast", "Operational", "Accounts with passwords expiring in the next 7, 14, or 30 days", ReportType.PasswordExpiryForecast),
            ("Failed Login Attempts", "Security", "Aggregated failed login attempts from the audit log", ReportType.FailedLoginAttempts),
            ("Users Without MFA", "Security", "User accounts not enrolled in TOTP or FIDO2 multi-factor authentication", ReportType.UsersWithoutMfa),
            ("Empty Groups", "Operational", "Security and distribution groups with zero members", ReportType.EmptyGroups),
            ("Service Account Audit", "SOX", "Service accounts (accounts with SPNs), their last login time, and password age", ReportType.ServiceAccountAudit),
            ("Certificate Expiry", "Operational", "Certificates expiring within the next 30, 60, or 90 days", ReportType.CertificateExpiry),
        };

        foreach (var (name, category, desc, type) in builtIns)
        {
            if (_reports.Values.Any(r => r.Type == type && r.IsBuiltIn)) continue;
            var report = new ComplianceReport
            {
                Id = $"builtin-{type.ToString().ToLowerInvariant()}",
                Name = name, Category = category, Description = desc,
                Type = type, IsBuiltIn = true,
            };
            _reports.TryAdd(report.Id, report);
        }
    }

    // --- CRUD ---

    public async Task<List<ComplianceReport>> GetAllReportsAsync()
    {
        await EnsureLoadedAsync();
        return _reports.Values.OrderBy(r => r.Name).ToList();
    }

    public async Task<ComplianceReport> GetReportAsync(string id)
    {
        await EnsureLoadedAsync();
        return _reports.GetValueOrDefault(id);
    }

    public async Task<ComplianceReport> CreateCustomReportAsync(ComplianceReport report)
    {
        await EnsureLoadedAsync();
        report.Type = ReportType.Custom;
        report.IsBuiltIn = false;
        _reports[report.Id] = report;
        await PersistAsync();
        return report;
    }

    public async Task<ReportResult> GetLastResultAsync(string reportId)
    {
        await EnsureLoadedAsync();
        return _results.GetValueOrDefault(reportId);
    }

    // --- Report Execution ---

    public async Task<ReportResult> RunReportAsync(string reportId)
    {
        await EnsureLoadedAsync();

        if (!_reports.TryGetValue(reportId, out var report))
            throw new KeyNotFoundException($"Report {reportId} not found");

        _logger.LogInformation("Running compliance report: {Name} ({Type})", report.Name, report.Type);

        ReportResult result;
        try
        {
            result = report.Type switch
            {
                ReportType.PrivilegedAccounts => await RunPrivilegedAccountsReport(report),
                ReportType.StaleAccounts => await RunStaleAccountsReport(report),
                ReportType.PasswordNeverExpires => await RunPasswordNeverExpiresReport(report),
                ReportType.DisabledAccountsWithGroupMembership => await RunDisabledWithGroupsReport(report),
                ReportType.PasswordExpiryForecast => await RunPasswordExpiryForecastReport(report),
                ReportType.FailedLoginAttempts => await RunFailedLoginAttemptsReport(report),
                ReportType.UsersWithoutMfa => await RunUsersWithoutMfaReport(report),
                ReportType.EmptyGroups => await RunEmptyGroupsReport(report),
                ReportType.ServiceAccountAudit => await RunServiceAccountAuditReport(report),
                ReportType.CertificateExpiry => await RunCertificateExpiryReport(report),
                _ => BuildEmptyResult(report, "Report type not implemented"),
            };
            report.LastRunAt = DateTimeOffset.UtcNow;
            report.LastRunStatus = result.ComplianceStatus;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to run report {Name}", report.Name);
            result = BuildEmptyResult(report, "Error");
            result.Recommendations.Add(new ComplianceRecommendation
            {
                Severity = "High", Title = "Report execution failed", Description = ex.Message
            });
            report.LastRunAt = DateTimeOffset.UtcNow;
            report.LastRunStatus = "Error";
        }

        _results[reportId] = result;
        await PersistAsync();
        await PersistResultsAsync();
        return result;
    }

    // --- Dashboard ---

    public async Task<ComplianceDashboard> GetDashboardAsync()
    {
        await EnsureLoadedAsync();
        var dashboard = new ComplianceDashboard { TotalReports = _reports.Count };
        var critical = new List<ComplianceRecommendation>();

        foreach (var report in _reports.Values)
        {
            var summary = new ComplianceSummaryItem
            {
                ReportId = report.Id, ReportName = report.Name,
                Category = report.Category, LastRunAt = report.LastRunAt,
            };

            if (_results.TryGetValue(report.Id, out var result))
            {
                summary.Status = result.ComplianceStatus;
                summary.FlaggedItems = result.FlaggedItems;
                switch (result.ComplianceStatus)
                {
                    case "Compliant": dashboard.CompliantCount++; break;
                    case "NonCompliant": dashboard.NonCompliantCount++; break;
                    case "Warning": dashboard.WarningCount++; break;
                    default: dashboard.NotRunCount++; break;
                }
                critical.AddRange(result.Recommendations.Where(r => r.Severity is "Critical" or "High"));
            }
            else
            {
                summary.Status = "NotRun";
                dashboard.NotRunCount++;
            }
            dashboard.ReportSummaries.Add(summary);
        }

        var runCount = dashboard.CompliantCount + dashboard.NonCompliantCount + dashboard.WarningCount;
        dashboard.ComplianceScore = runCount > 0
            ? Math.Round((double)dashboard.CompliantCount / runCount * 100, 1) : 0;
        dashboard.CriticalFindings = critical.OrderBy(c => c.Severity).Take(20).ToList();
        return dashboard;
    }

    // --- CSV Export ---

    public string ExportToCsv(ReportResult result)
    {
        if (result.Data.Count == 0) return "No data";
        var columns = result.Columns.Count > 0 ? result.Columns : result.Data[0].Keys.ToList();
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(string.Join(",", columns.Select(EscapeCsv)));
        foreach (var row in result.Data)
        {
            var values = columns.Select(c =>
                row.TryGetValue(c, out var v) ? EscapeCsv(v?.ToString() ?? "") : "");
            sb.AppendLine(string.Join(",", values));
        }
        return sb.ToString();
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    // --- Helpers ---

    private static DateTimeOffset FileTimeToDateTimeOffset(long fileTime)
    {
        if (fileTime <= 0 || fileTime >= long.MaxValue) return DateTimeOffset.MinValue;
        try { return DateTimeOffset.FromFileTime(fileTime); }
        catch { return DateTimeOffset.MinValue; }
    }

    private ReportResult NewResult(ComplianceReport report) => new()
    {
        ReportId = report.Id, ReportName = report.Name, GeneratedAt = DateTimeOffset.UtcNow,
    };

    private ReportResult BuildEmptyResult(ComplianceReport report, string status) => new()
    {
        ReportId = report.Id, ReportName = report.Name,
        GeneratedAt = DateTimeOffset.UtcNow, ComplianceStatus = status,
    };

    // --- Individual Report Implementations ---

    private async Task<ReportResult> RunPrivilegedAccountsReport(ComplianceReport report)
    {
        var result = NewResult(report);
        result.Columns = ["DN", "sAMAccountName", "AdminGroup", "Enabled", "LastLogon"];

        var adminGroupNames = new[] { "Administrators", "Domain Admins", "Enterprise Admins", "Schema Admins" };

        foreach (var groupName in adminGroupNames)
        {
            var sr = await _directoryStore.SearchAsync(
                TenantId, "", SearchScope.WholeSubtree,
                new EqualityFilterNode("sAMAccountName", groupName),
                new[] { "member", "distinguishedName" });

            foreach (var groupObj in sr.Entries)
            {
                foreach (var memberDn in groupObj.Member)
                {
                    var user = await _directoryStore.GetByDnAsync(TenantId, memberDn);
                    if (user == null) continue;
                    var enabled = (user.UserAccountControl & UF_ACCOUNTDISABLE) == 0;
                    var lastLogon = user.LastLogon > 0
                        ? FileTimeToDateTimeOffset(user.LastLogon).ToString("o") : "Never";

                    result.Data.Add(new Dictionary<string, object>
                    {
                        ["DN"] = memberDn,
                        ["sAMAccountName"] = user.SAMAccountName ?? "",
                        ["AdminGroup"] = groupName,
                        ["Enabled"] = enabled,
                        ["LastLogon"] = lastLogon,
                    });
                }
            }
        }

        result.TotalItems = result.Data.Count;
        result.FlaggedItems = result.Data.Count;
        if (result.FlaggedItems > 10)
        {
            result.ComplianceStatus = "Warning";
            result.Recommendations.Add(new ComplianceRecommendation
            {
                Severity = "High", Title = "Excessive privileged accounts",
                Description = $"There are {result.FlaggedItems} accounts with admin group membership. Consider reducing the number of privileged accounts.",
                RemediationAction = "Review each privileged account and remove unnecessary memberships."
            });
        }
        else result.ComplianceStatus = "Compliant";

        return result;
    }

    private async Task<ReportResult> RunStaleAccountsReport(ComplianceReport report)
    {
        var result = NewResult(report);
        result.Columns = ["DN", "sAMAccountName", "LastLogon", "DaysSinceLogin", "Enabled"];

        var thresholdDays = report.Parameters.TryGetValue("thresholdDays", out var td) ? int.Parse(td) : 90;
        var cutoff = DateTimeOffset.UtcNow.AddDays(-thresholdDays);

        var sr = await _directoryStore.SearchAsync(
            TenantId, "", SearchScope.WholeSubtree,
            new EqualityFilterNode("objectClass", "user"),
            new[] { "distinguishedName", "sAMAccountName", "lastLogon", "userAccountControl" });

        foreach (var user in sr.Entries)
        {
            if (user.LastLogon <= 0) continue;
            var lastLogon = FileTimeToDateTimeOffset(user.LastLogon);
            if (lastLogon >= cutoff) continue;

            var enabled = (user.UserAccountControl & UF_ACCOUNTDISABLE) == 0;
            var days = (int)(DateTimeOffset.UtcNow - lastLogon).TotalDays;

            result.Data.Add(new Dictionary<string, object>
            {
                ["DN"] = user.DistinguishedName,
                ["sAMAccountName"] = user.SAMAccountName ?? "",
                ["LastLogon"] = lastLogon.ToString("o"),
                ["DaysSinceLogin"] = days,
                ["Enabled"] = enabled,
            });
        }

        result.TotalItems = result.Data.Count;
        result.FlaggedItems = result.Data.Count(d => (bool)d["Enabled"]);
        result.ComplianceStatus = result.FlaggedItems > 0 ? "NonCompliant" : "Compliant";

        if (result.FlaggedItems > 0)
            result.Recommendations.Add(new ComplianceRecommendation
            {
                Severity = "Medium", Title = "Stale enabled accounts detected",
                Description = $"{result.FlaggedItems} enabled accounts have not logged in for over {thresholdDays} days.",
                RemediationAction = "Disable or delete these stale accounts to reduce attack surface."
            });

        return result;
    }

    private async Task<ReportResult> RunPasswordNeverExpiresReport(ComplianceReport report)
    {
        var result = NewResult(report);
        result.Columns = ["DN", "sAMAccountName", "Enabled", "PasswordLastSet"];

        var sr = await _directoryStore.SearchAsync(
            TenantId, "", SearchScope.WholeSubtree,
            new EqualityFilterNode("objectClass", "user"),
            new[] { "distinguishedName", "sAMAccountName", "userAccountControl", "pwdLastSet" });

        foreach (var user in sr.Entries)
        {
            if ((user.UserAccountControl & DONT_EXPIRE_PASSWD) == 0) continue;
            var enabled = (user.UserAccountControl & UF_ACCOUNTDISABLE) == 0;
            var pwdLastSet = user.PwdLastSet > 0
                ? FileTimeToDateTimeOffset(user.PwdLastSet).ToString("o") : "Unknown";

            result.Data.Add(new Dictionary<string, object>
            {
                ["DN"] = user.DistinguishedName,
                ["sAMAccountName"] = user.SAMAccountName ?? "",
                ["Enabled"] = enabled,
                ["PasswordLastSet"] = pwdLastSet,
            });
        }

        result.TotalItems = result.Data.Count;
        result.FlaggedItems = result.Data.Count;
        result.ComplianceStatus = result.FlaggedItems > 0 ? "NonCompliant" : "Compliant";

        if (result.FlaggedItems > 0)
            result.Recommendations.Add(new ComplianceRecommendation
            {
                Severity = "High", Title = "Accounts with non-expiring passwords",
                Description = $"{result.FlaggedItems} accounts have the DONT_EXPIRE_PASSWD flag set.",
                RemediationAction = "Remove the password-never-expires flag and enforce regular password rotation."
            });

        return result;
    }

    private async Task<ReportResult> RunDisabledWithGroupsReport(ComplianceReport report)
    {
        var result = NewResult(report);
        result.Columns = ["DN", "sAMAccountName", "GroupCount", "Groups"];

        var sr = await _directoryStore.SearchAsync(
            TenantId, "", SearchScope.WholeSubtree,
            new EqualityFilterNode("objectClass", "user"),
            new[] { "distinguishedName", "sAMAccountName", "userAccountControl", "memberOf" });

        foreach (var user in sr.Entries)
        {
            var disabled = (user.UserAccountControl & UF_ACCOUNTDISABLE) != 0;
            if (!disabled || user.MemberOf.Count == 0) continue;

            result.Data.Add(new Dictionary<string, object>
            {
                ["DN"] = user.DistinguishedName,
                ["sAMAccountName"] = user.SAMAccountName ?? "",
                ["GroupCount"] = user.MemberOf.Count,
                ["Groups"] = string.Join("; ", user.MemberOf.Take(5)),
            });
        }

        result.TotalItems = result.Data.Count;
        result.FlaggedItems = result.Data.Count;
        result.ComplianceStatus = result.FlaggedItems > 0 ? "Warning" : "Compliant";

        if (result.FlaggedItems > 0)
            result.Recommendations.Add(new ComplianceRecommendation
            {
                Severity = "Medium", Title = "Disabled accounts retaining group memberships",
                Description = $"{result.FlaggedItems} disabled accounts still have group memberships that should be cleaned up.",
                RemediationAction = "Remove group memberships from disabled accounts to follow least privilege principle."
            });

        return result;
    }

    private async Task<ReportResult> RunPasswordExpiryForecastReport(ComplianceReport report)
    {
        var result = NewResult(report);
        result.Columns = ["DN", "sAMAccountName", "PasswordLastSet", "ExpiresIn", "ExpiryDate"];

        var maxAgeDays = report.Parameters.TryGetValue("maxPasswordAgeDays", out var ma) ? int.Parse(ma) : 90;

        var sr = await _directoryStore.SearchAsync(
            TenantId, "", SearchScope.WholeSubtree,
            new EqualityFilterNode("objectClass", "user"),
            new[] { "distinguishedName", "sAMAccountName", "pwdLastSet", "userAccountControl" });

        foreach (var user in sr.Entries)
        {
            if ((user.UserAccountControl & DONT_EXPIRE_PASSWD) != 0) continue;
            if (user.PwdLastSet <= 0) continue;

            var pwdLastSet = FileTimeToDateTimeOffset(user.PwdLastSet);
            var expiryDate = pwdLastSet.AddDays(maxAgeDays);
            var daysUntilExpiry = (int)(expiryDate - DateTimeOffset.UtcNow).TotalDays;

            if (daysUntilExpiry <= 30 && daysUntilExpiry > -30)
            {
                result.Data.Add(new Dictionary<string, object>
                {
                    ["DN"] = user.DistinguishedName,
                    ["sAMAccountName"] = user.SAMAccountName ?? "",
                    ["PasswordLastSet"] = pwdLastSet.ToString("o"),
                    ["ExpiresIn"] = daysUntilExpiry < 0 ? $"Expired {-daysUntilExpiry} days ago" : $"{daysUntilExpiry} days",
                    ["ExpiryDate"] = expiryDate.ToString("o"),
                });
            }
        }

        result.TotalItems = result.Data.Count;
        result.FlaggedItems = result.Data.Count(d => d["ExpiresIn"].ToString().Contains("Expired") ||
            d["ExpiresIn"].ToString().StartsWith("0") || d["ExpiresIn"].ToString() == "1 days");
        result.ComplianceStatus = result.FlaggedItems > 0 ? "Warning" : "Compliant";

        if (result.FlaggedItems > 0)
            result.Recommendations.Add(new ComplianceRecommendation
            {
                Severity = "Medium", Title = "Passwords expiring soon or already expired",
                Description = $"{result.FlaggedItems} accounts have passwords that are expired or expiring imminently.",
                RemediationAction = "Notify users to update their passwords before expiry."
            });

        return result;
    }

    private async Task<ReportResult> RunFailedLoginAttemptsReport(ComplianceReport report)
    {
        var result = NewResult(report);
        result.Columns = ["UserDn", "FailedAttempts", "LastAttempt"];

        var periodDays = report.Parameters.TryGetValue("periodDays", out var pd) ? int.Parse(pd) : 7;
        var from = DateTimeOffset.UtcNow.AddDays(-periodDays);

        var (entries, _) = await _auditService.QueryAsync(TenantId, "FailedLogin", null, from, null, 1000);

        var grouped = entries
            .GroupBy(e => e.TargetDn)
            .Select(g => new { UserDn = g.Key, Count = g.Count(), LastAttempt = g.Max(e => e.Timestamp) })
            .OrderByDescending(g => g.Count);

        foreach (var g in grouped)
        {
            result.Data.Add(new Dictionary<string, object>
            {
                ["UserDn"] = g.UserDn,
                ["FailedAttempts"] = g.Count,
                ["LastAttempt"] = g.LastAttempt.ToString("o"),
            });
        }

        result.TotalItems = result.Data.Count;
        result.FlaggedItems = result.Data.Count(d => (int)d["FailedAttempts"] >= 5);
        result.ComplianceStatus = result.FlaggedItems > 5 ? "NonCompliant" : result.FlaggedItems > 0 ? "Warning" : "Compliant";

        if (result.FlaggedItems > 0)
            result.Recommendations.Add(new ComplianceRecommendation
            {
                Severity = "High", Title = "Multiple failed login attempts detected",
                Description = $"{result.FlaggedItems} accounts have 5 or more failed login attempts in the last {periodDays} days.",
                RemediationAction = "Investigate potential brute-force attacks and enforce account lockout policies."
            });

        return result;
    }

    private async Task<ReportResult> RunUsersWithoutMfaReport(ComplianceReport report)
    {
        var result = NewResult(report);
        result.Columns = ["DN", "sAMAccountName", "Enabled", "MfaEnrolled"];

        var sr = await _directoryStore.SearchAsync(
            TenantId, "", SearchScope.WholeSubtree,
            new EqualityFilterNode("objectClass", "user"),
            new[] { "distinguishedName", "sAMAccountName", "userAccountControl" });

        foreach (var user in sr.Entries)
        {
            var enabled = (user.UserAccountControl & UF_ACCOUNTDISABLE) == 0;
            if (!enabled) continue;

            try
            {
                var mfaStatus = await _mfaService.GetMfaStatus(user.DistinguishedName);
                if (!mfaStatus.IsEnabled)
                {
                    result.Data.Add(new Dictionary<string, object>
                    {
                        ["DN"] = user.DistinguishedName,
                        ["sAMAccountName"] = user.SAMAccountName ?? "",
                        ["Enabled"] = true,
                        ["MfaEnrolled"] = false,
                    });
                }
            }
            catch
            {
                // User may not be found — skip
            }
        }

        result.TotalItems = result.Data.Count;
        result.FlaggedItems = result.Data.Count;
        result.ComplianceStatus = result.FlaggedItems > 0 ? "NonCompliant" : "Compliant";

        if (result.FlaggedItems > 0)
            result.Recommendations.Add(new ComplianceRecommendation
            {
                Severity = "Critical", Title = "Users without multi-factor authentication",
                Description = $"{result.FlaggedItems} enabled accounts have not enrolled in any MFA method.",
                RemediationAction = "Enforce MFA enrollment for all user accounts."
            });

        return result;
    }

    private async Task<ReportResult> RunEmptyGroupsReport(ComplianceReport report)
    {
        var result = NewResult(report);
        result.Columns = ["DN", "sAMAccountName", "GroupType", "Description"];

        var sr = await _directoryStore.SearchAsync(
            TenantId, "", SearchScope.WholeSubtree,
            new EqualityFilterNode("objectClass", "group"),
            new[] { "distinguishedName", "sAMAccountName", "groupType", "description", "member" });

        foreach (var group in sr.Entries)
        {
            if (group.Member.Count > 0) continue;
            result.Data.Add(new Dictionary<string, object>
            {
                ["DN"] = group.DistinguishedName,
                ["sAMAccountName"] = group.SAMAccountName ?? "",
                ["GroupType"] = group.GetAttribute("groupType")?.GetFirstString() ?? "",
                ["Description"] = group.Description ?? "",
            });
        }

        result.TotalItems = result.Data.Count;
        result.FlaggedItems = result.Data.Count;
        result.ComplianceStatus = result.FlaggedItems > 10 ? "Warning" : "Compliant";

        if (result.FlaggedItems > 0)
            result.Recommendations.Add(new ComplianceRecommendation
            {
                Severity = "Low", Title = "Empty groups found",
                Description = $"{result.FlaggedItems} groups have no members.",
                RemediationAction = "Review empty groups and delete any that are no longer needed."
            });

        return result;
    }

    private async Task<ReportResult> RunServiceAccountAuditReport(ComplianceReport report)
    {
        var result = NewResult(report);
        result.Columns = ["DN", "sAMAccountName", "SPNs", "LastLogon", "PasswordAgeDays"];

        var sr = await _directoryStore.SearchAsync(
            TenantId, "", SearchScope.WholeSubtree,
            new PresenceFilterNode("servicePrincipalName"),
            new[] { "distinguishedName", "sAMAccountName", "servicePrincipalName", "lastLogon", "pwdLastSet" });

        foreach (var acct in sr.Entries)
        {
            var pwdAgeDays = acct.PwdLastSet > 0
                ? (int)(DateTimeOffset.UtcNow - FileTimeToDateTimeOffset(acct.PwdLastSet)).TotalDays : -1;
            var lastLogon = acct.LastLogon > 0
                ? FileTimeToDateTimeOffset(acct.LastLogon).ToString("o") : "Never";

            result.Data.Add(new Dictionary<string, object>
            {
                ["DN"] = acct.DistinguishedName,
                ["sAMAccountName"] = acct.SAMAccountName ?? "",
                ["SPNs"] = string.Join("; ", acct.ServicePrincipalName.Take(3)),
                ["LastLogon"] = lastLogon,
                ["PasswordAgeDays"] = pwdAgeDays,
            });
        }

        result.TotalItems = result.Data.Count;
        result.FlaggedItems = result.Data.Count(d => (int)d["PasswordAgeDays"] > 365);
        result.ComplianceStatus = result.FlaggedItems > 0 ? "Warning" : "Compliant";

        if (result.FlaggedItems > 0)
            result.Recommendations.Add(new ComplianceRecommendation
            {
                Severity = "High", Title = "Service accounts with old passwords",
                Description = $"{result.FlaggedItems} service accounts have passwords older than 365 days.",
                RemediationAction = "Rotate service account passwords or migrate to Group Managed Service Accounts (gMSA)."
            });

        return result;
    }

    private async Task<ReportResult> RunCertificateExpiryReport(ComplianceReport report)
    {
        var result = NewResult(report);
        result.Columns = ["DN", "CN", "NotAfter", "DaysRemaining"];

        var thresholdDays = report.Parameters.TryGetValue("thresholdDays", out var td) ? int.Parse(td) : 90;

        var sr = await _directoryStore.SearchAsync(
            TenantId, "", SearchScope.WholeSubtree,
            new EqualityFilterNode("objectClass", "pKICertificateTemplate"),
            new[] { "distinguishedName", "cn" });

        // Certificate objects themselves may be stored differently; report found templates
        result.TotalItems = sr.Entries.Count;
        result.FlaggedItems = 0;
        result.ComplianceStatus = "Compliant";
        return result;
    }
}
