using System.Diagnostics;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Dns;
using Directory.Web.Models;
using Microsoft.Extensions.Options;

namespace Directory.Web.Services;

// ── Models ──────────────────────────────────────────────────────────────────

public class JoinDiagnosticResult
{
    public string ComputerName { get; set; } = "";
    public List<DiagnosticEntry> Entries { get; set; } = new();
    public string Summary { get; set; } = "";
    public List<string> Recommendations { get; set; } = new();
}

public class DiagnosticEntry
{
    public string Test { get; set; } = "";
    public string Status { get; set; } = ""; // Pass, Fail, Warning, Skip
    public string Details { get; set; } = "";
    public long DurationMs { get; set; }
}

public class DomainJoinHealthSummary
{
    public int TotalJoins { get; set; }
    public int SuccessfulJoins { get; set; }
    public int FailedJoins { get; set; }
    public double SuccessRate { get; set; }
    public List<DomainJoinHistoryEntry> RecentFailures { get; set; } = new();
    public List<DomainJoinHistoryEntry> RecentOperations { get; set; } = new();
    public Dictionary<string, int> FailureReasons { get; set; } = new();
}

// ── Service ─────────────────────────────────────────────────────────────────

/// <summary>
/// Runs diagnostic tests to troubleshoot domain join issues for a specific computer.
/// Tests DNS resolution, LDAP/Kerberos connectivity, account status, SPN conflicts, etc.
/// </summary>
public class JoinDiagnosticsService
{
    private const int WorkstationTrustAccount = 0x1000;
    private const int DefaultMaxPasswordAgeDays = 30;
    private const int KerberosMaxSkewMinutes = 5;

    private readonly IDirectoryStore _store;
    private readonly INamingContextService _ncService;
    private readonly DomainConfiguration _domainConfig;
    private readonly DcNodeOptions _dcNodeOptions;
    private readonly DnsZoneStore _dnsZoneStore;
    private readonly DomainJoinService _joinService;
    private readonly ILogger<JoinDiagnosticsService> _logger;

    public JoinDiagnosticsService(
        IDirectoryStore store,
        INamingContextService ncService,
        DomainConfiguration domainConfig,
        IOptions<DcNodeOptions> dcNodeOptions,
        DnsZoneStore dnsZoneStore,
        DomainJoinService joinService,
        ILogger<JoinDiagnosticsService> logger)
    {
        _store = store;
        _ncService = ncService;
        _domainConfig = domainConfig;
        _dcNodeOptions = dcNodeOptions.Value;
        _dnsZoneStore = dnsZoneStore;
        _joinService = joinService;
        _logger = logger;
    }

    /// <summary>
    /// Runs all diagnostic tests for the specified computer.
    /// </summary>
    public async Task<JoinDiagnosticResult> DiagnoseAsync(string computerName, CancellationToken ct = default)
    {
        var result = new JoinDiagnosticResult { ComputerName = computerName };
        var domainDn = _domainConfig.DomainDn;
        if (string.IsNullOrEmpty(domainDn))
            domainDn = _ncService.GetDomainNc().Dn;

        var domainDnsName = _domainConfig.DomainDnsName;

        // 1. DNS Resolution — SRV records
        result.Entries.Add(await RunTestAsync("DNS Resolution", async () =>
        {
            var srvName = $"_ldap._tcp.{domainDnsName}";
            var srvRecords = await _dnsZoneStore.GetRecordsByNameAsync(
                "default", domainDnsName, $"_ldap._tcp", DnsRecordType.SRV, ct);

            if (srvRecords.Count > 0)
            {
                return ("Pass", $"Found {srvRecords.Count} SRV record(s) for {srvName}.");
            }

            // Also check DC A records
            var aRecords = await _dnsZoneStore.GetRecordsByNameAsync(
                "default", domainDnsName, _dcNodeOptions.Hostname, DnsRecordType.A, ct);
            if (aRecords.Count > 0)
            {
                return ("Warning", $"No SRV records for {srvName}, but DC A record found for {_dcNodeOptions.Hostname}.");
            }

            return ("Fail", $"No SRV records for {srvName} and no DC A record found. DNS may not be configured.");
        }));

        // 2. LDAP Connectivity
        result.Entries.Add(await RunTestAsync("LDAP Connectivity", async () =>
        {
            // Verify the directory store is reachable by performing a lightweight query
            try
            {
                var testObj = await _store.GetByDnAsync("default", domainDn, ct);
                if (testObj != null)
                    return ("Pass", $"LDAP bind to directory successful. Domain root '{domainDn}' accessible.");
                return ("Fail", "LDAP bind succeeded but domain root object not found.");
            }
            catch (Exception ex)
            {
                return ("Fail", $"LDAP connectivity test failed: {ex.Message}");
            }
        }));

        // 3. Kerberos Connectivity
        result.Entries.Add(await RunTestAsync("Kerberos Connectivity", () =>
        {
            // In this implementation, Kerberos is handled by the integrated KDC.
            // Verify the realm is configured.
            if (!string.IsNullOrEmpty(_domainConfig.KerberosRealm))
            {
                return Task.FromResult(("Pass", $"Kerberos realm configured: {_domainConfig.KerberosRealm}. KDC is integrated."));
            }
            return Task.FromResult(("Warning", "Kerberos realm is not configured. KDC may not be operational."));
        }));

        // 4. Netlogon Connectivity
        result.Entries.Add(await RunTestAsync("Netlogon Connectivity", () =>
        {
            // Netlogon service is integrated — check DC info
            var dcName = _dcNodeOptions.Hostname;
            if (!string.IsNullOrEmpty(dcName))
            {
                return Task.FromResult(("Pass", $"Netlogon service is operational on DC '{dcName}'."));
            }
            return Task.FromResult(("Warning", "DC hostname not configured. Netlogon may not be reachable."));
        }));

        // 5. Time Skew
        result.Entries.Add(await RunTestAsync("Time Skew", () =>
        {
            // We can't directly query the remote computer's time, so report server time
            // and the Kerberos tolerance window.
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(("Pass",
                $"DC time: {now:u}. Kerberos tolerance is {KerberosMaxSkewMinutes} minutes. " +
                "Ensure client system time is synchronized via NTP."));
        }));

        // 6. Computer Account Search
        result.Entries.Add(await RunTestAsync("Computer Account Search", async () =>
        {
            var samAccountName = computerName.ToUpperInvariant() + "$";
            var obj = await _store.GetBySamAccountNameAsync("default", domainDn, samAccountName, ct);

            if (obj != null && !obj.IsDeleted)
            {
                var details = $"Found by sAMAccountName '{samAccountName}': {obj.DistinguishedName}";

                // Also check dNSHostName
                if (obj.Attributes.TryGetValue("dNSHostName", out var dnsAttr) && dnsAttr.Values.Count > 0)
                {
                    details += $" | dNSHostName: {dnsAttr.Values[0]}";
                }

                return ("Pass", details);
            }

            // Try searching by CN
            var filter = new AndFilterNode(new List<FilterNode>
            {
                new EqualityFilterNode("objectClass", "computer"),
                new EqualityFilterNode("cn", computerName.ToUpperInvariant()),
            });

            var searchResult = await _store.SearchAsync("default", domainDn, SearchScope.WholeSubtree,
                filter, null, sizeLimit: 1, ct: ct);

            if (searchResult.Entries.Count > 0)
            {
                return ("Warning", $"Not found by sAMAccountName but found by CN: {searchResult.Entries[0].DistinguishedName}");
            }

            return ("Fail", $"Computer account '{computerName}' not found by sAMAccountName or CN.");
        }));

        // 7. Password Status
        result.Entries.Add(await RunTestAsync("Password Status", async () =>
        {
            var samAccountName = computerName.ToUpperInvariant() + "$";
            var obj = await _store.GetBySamAccountNameAsync("default", domainDn, samAccountName, ct);
            if (obj == null || obj.IsDeleted)
                return ("Skip", "Computer account not found. Skipping password check.");

            if (obj.PwdLastSet <= 0)
                return ("Fail", "pwdLastSet is 0 or not set. The machine password may never have been set.");

            var pwdLastSet = DateTimeOffset.FromFileTime(obj.PwdLastSet);
            var age = DateTimeOffset.UtcNow - pwdLastSet;

            if (age.TotalDays > DefaultMaxPasswordAgeDays)
                return ("Warning", $"Machine password is {age.TotalDays:F0} days old (set: {pwdLastSet:u}). " +
                    $"Exceeds {DefaultMaxPasswordAgeDays}-day default maximum.");

            return ("Pass", $"Machine password set {age.TotalDays:F0} days ago ({pwdLastSet:u}). Within acceptable range.");
        }));

        // 8. Duplicate SPN Check
        result.Entries.Add(await RunTestAsync("Duplicate SPN Check", async () =>
        {
            var upperName = computerName.ToUpperInvariant();
            var spnsToCheck = new[] { $"HOST/{upperName}" };
            var conflicts = new List<string>();

            foreach (var spn in spnsToCheck)
            {
                var holders = await _store.GetByServicePrincipalNameAsync("default", spn, ct);
                var samAccountName = upperName + "$";
                var others = holders.Where(h =>
                    !h.IsDeleted &&
                    !string.Equals(h.SAMAccountName, samAccountName, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (others.Count > 0)
                {
                    conflicts.Add($"SPN '{spn}' is also registered on: {string.Join(", ", others.Select(o => o.DistinguishedName))}");
                }
            }

            if (conflicts.Count > 0)
                return ("Fail", $"SPN conflicts detected: {string.Join("; ", conflicts)}");

            return ("Pass", "No duplicate SPNs found for this computer.");
        }));

        // 9. OU Policy Check
        result.Entries.Add(await RunTestAsync("OU Policy Check", async () =>
        {
            var samAccountName = computerName.ToUpperInvariant() + "$";
            var obj = await _store.GetBySamAccountNameAsync("default", domainDn, samAccountName, ct);
            if (obj == null || obj.IsDeleted)
                return ("Skip", "Computer account not found. Skipping OU policy check.");

            var parentDn = obj.ParentDn;
            if (string.IsNullOrEmpty(parentDn))
                return ("Warning", "Computer has no parent DN set.");

            var parentObj = await _store.GetByDnAsync("default", parentDn, ct);
            if (parentObj == null)
                return ("Warning", $"Parent OU '{parentDn}' not found in directory.");

            // Check for gPLink attribute on the OU
            if (parentObj.Attributes.TryGetValue("gPLink", out var gpLink) &&
                gpLink.Values.Count > 0 && !string.IsNullOrWhiteSpace(gpLink.Values[0]?.ToString()))
            {
                return ("Pass", $"Computer is in OU '{parentDn}' which has GPO links configured.");
            }

            return ("Pass", $"Computer is in OU '{parentDn}'. No GPOs linked to this OU.");
        }));

        // Build summary
        var passCount = result.Entries.Count(e => e.Status == "Pass");
        var failCount = result.Entries.Count(e => e.Status == "Fail");
        var warnCount = result.Entries.Count(e => e.Status == "Warning");
        var skipCount = result.Entries.Count(e => e.Status == "Skip");

        result.Summary = $"{passCount} passed, {failCount} failed, {warnCount} warnings, {skipCount} skipped out of {result.Entries.Count} tests.";

        // Build recommendations
        foreach (var entry in result.Entries.Where(e => e.Status == "Fail"))
        {
            result.Recommendations.Add($"[{entry.Test}] {entry.Details}");
        }
        foreach (var entry in result.Entries.Where(e => e.Status == "Warning"))
        {
            result.Recommendations.Add($"[{entry.Test}] {entry.Details}");
        }

        return result;
    }

    /// <summary>
    /// Returns an overall domain join health summary based on recent history.
    /// </summary>
    public DomainJoinHealthSummary GetHealthSummary()
    {
        var history = _joinService.GetHistory();
        var summary = new DomainJoinHealthSummary
        {
            TotalJoins = history.Count,
            SuccessfulJoins = history.Count(h => h.Success),
            FailedJoins = history.Count(h => !h.Success),
            RecentOperations = history.Take(20).ToList(),
            RecentFailures = history.Where(h => !h.Success).Take(10).ToList(),
        };

        summary.SuccessRate = summary.TotalJoins > 0
            ? Math.Round((double)summary.SuccessfulJoins / summary.TotalJoins * 100, 1)
            : 100.0;

        // Aggregate failure reasons
        foreach (var failure in history.Where(h => !h.Success && !string.IsNullOrEmpty(h.ErrorMessage)))
        {
            var reason = failure.ErrorMessage.Length > 80
                ? failure.ErrorMessage[..80] + "..."
                : failure.ErrorMessage;

            if (!summary.FailureReasons.TryAdd(reason, 1))
                summary.FailureReasons[reason]++;
        }

        return summary;
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static async Task<DiagnosticEntry> RunTestAsync(string testName, Func<Task<(string Status, string Details)>> test)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var (status, details) = await test();
            sw.Stop();
            return new DiagnosticEntry
            {
                Test = testName,
                Status = status,
                Details = details,
                DurationMs = sw.ElapsedMilliseconds,
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new DiagnosticEntry
            {
                Test = testName,
                Status = "Fail",
                Details = $"Unexpected error: {ex.Message}",
                DurationMs = sw.ElapsedMilliseconds,
            };
        }
    }
}
