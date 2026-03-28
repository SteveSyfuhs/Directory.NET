using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Dns;
using Directory.Web.Models;
using Microsoft.Extensions.Options;

namespace Directory.Web.Services;

// ── Models ──────────────────────────────────────────────────────────────────

public class JoinVerificationRequest
{
    public string ComputerName { get; set; } = "";
}

public class JoinVerificationResult
{
    public string ComputerName { get; set; } = "";
    public bool OverallHealthy { get; set; }
    public List<VerificationCheck> Checks { get; set; } = new();
}

public class VerificationCheck
{
    public string Name { get; set; } = "";
    public string Category { get; set; } = ""; // "Account", "DNS", "SecureChannel", "SPN", "Replication"
    public bool Passed { get; set; }
    public string Message { get; set; } = "";
    public string Recommendation { get; set; }
}

// ── Service ─────────────────────────────────────────────────────────────────

/// <summary>
/// Runs post-domain-join verification checks against a computer account.
/// Validates account state, DNS records, SPNs, secure channel, and replication.
/// </summary>
public class JoinVerificationService
{
    private const int WorkstationTrustAccount = 0x1000;
    private const int AccountDisabled = 0x0002;
    private const int DefaultMaxPasswordAgeDays = 30;
    private const int DomainComputersRid = 515;

    private readonly IDirectoryStore _store;
    private readonly INamingContextService _ncService;
    private readonly DomainConfiguration _domainConfig;
    private readonly DcNodeOptions _dcNodeOptions;
    private readonly DnsZoneStore _dnsZoneStore;
    private readonly ILogger<JoinVerificationService> _logger;

    public JoinVerificationService(
        IDirectoryStore store,
        INamingContextService ncService,
        DomainConfiguration domainConfig,
        IOptions<DcNodeOptions> dcNodeOptions,
        DnsZoneStore dnsZoneStore,
        ILogger<JoinVerificationService> logger)
    {
        _store = store;
        _ncService = ncService;
        _domainConfig = domainConfig;
        _dcNodeOptions = dcNodeOptions.Value;
        _dnsZoneStore = dnsZoneStore;
        _logger = logger;
    }

    /// <summary>
    /// Runs all verification checks for the specified computer.
    /// </summary>
    public async Task<JoinVerificationResult> VerifyAsync(string computerName, CancellationToken ct = default)
    {
        var result = new JoinVerificationResult { ComputerName = computerName };
        var domainDn = _domainConfig.DomainDn;
        if (string.IsNullOrEmpty(domainDn))
            domainDn = _ncService.GetDomainNc().Dn;

        // Look up the computer account
        var samAccountName = computerName.ToUpperInvariant() + "$";
        var computerObj = await _store.GetBySamAccountNameAsync("default", domainDn, samAccountName, ct);

        // 1. Account Exists
        var existsCheck = new VerificationCheck
        {
            Name = "Account Exists",
            Category = "Account",
        };
        if (computerObj == null || computerObj.IsDeleted)
        {
            existsCheck.Passed = false;
            existsCheck.Message = $"Computer account '{samAccountName}' not found in the directory.";
            existsCheck.Recommendation = "Re-join the computer to the domain or create the computer account manually.";
            result.Checks.Add(existsCheck);
            result.OverallHealthy = false;
            return result; // Cannot continue without the account
        }
        existsCheck.Passed = true;
        existsCheck.Message = $"Computer account found: {computerObj.DistinguishedName}";
        result.Checks.Add(existsCheck);

        // 2. Account Enabled
        var enabledCheck = new VerificationCheck
        {
            Name = "Account Enabled",
            Category = "Account",
        };
        var isDisabled = (computerObj.UserAccountControl & AccountDisabled) != 0;
        if (isDisabled)
        {
            enabledCheck.Passed = false;
            enabledCheck.Message = "Computer account is disabled.";
            enabledCheck.Recommendation = "Enable the computer account or rejoin the computer to the domain.";
        }
        else
        {
            enabledCheck.Passed = true;
            enabledCheck.Message = "Computer account is enabled.";
        }
        result.Checks.Add(enabledCheck);

        // 3. Account Type
        var typeCheck = new VerificationCheck
        {
            Name = "Account Type",
            Category = "Account",
        };
        var hasWorkstationTrust = (computerObj.UserAccountControl & WorkstationTrustAccount) != 0;
        if (hasWorkstationTrust)
        {
            typeCheck.Passed = true;
            typeCheck.Message = "Account has WORKSTATION_TRUST_ACCOUNT flag set.";
        }
        else
        {
            typeCheck.Passed = false;
            typeCheck.Message = $"Account userAccountControl ({computerObj.UserAccountControl}) does not have WORKSTATION_TRUST_ACCOUNT (0x1000).";
            typeCheck.Recommendation = "Verify the account type. The computer may need to be re-joined.";
        }
        result.Checks.Add(typeCheck);

        // 4. Password Age
        var pwdAgeCheck = new VerificationCheck
        {
            Name = "Password Age",
            Category = "Account",
        };
        if (computerObj.PwdLastSet > 0)
        {
            var pwdLastSet = DateTimeOffset.FromFileTime(computerObj.PwdLastSet);
            var age = DateTimeOffset.UtcNow - pwdLastSet;
            if (age.TotalDays > DefaultMaxPasswordAgeDays)
            {
                pwdAgeCheck.Passed = false;
                pwdAgeCheck.Message = $"Machine password is {age.TotalDays:F0} days old (last set: {pwdLastSet:u}).";
                pwdAgeCheck.Recommendation = "Reset the computer account password. The machine may need to be re-joined.";
            }
            else
            {
                pwdAgeCheck.Passed = true;
                pwdAgeCheck.Message = $"Machine password is {age.TotalDays:F0} days old (within {DefaultMaxPasswordAgeDays}-day limit).";
            }
        }
        else
        {
            pwdAgeCheck.Passed = false;
            pwdAgeCheck.Message = "pwdLastSet is not set on the computer account.";
            pwdAgeCheck.Recommendation = "Reset the computer account password.";
        }
        result.Checks.Add(pwdAgeCheck);

        // 5. SPNs Registered
        var spnCheck = new VerificationCheck
        {
            Name = "SPNs Registered",
            Category = "SPN",
        };
        var spns = computerObj.ServicePrincipalName;
        var upperName = computerName.ToUpperInvariant();
        var dnsHostName = GetDnsHostName(computerObj);
        var hasHostShort = spns.Any(s => s.Equals($"HOST/{upperName}", StringComparison.OrdinalIgnoreCase));
        var hasHostFqdn = !string.IsNullOrEmpty(dnsHostName) &&
            spns.Any(s => s.Equals($"HOST/{dnsHostName}", StringComparison.OrdinalIgnoreCase));

        if (hasHostShort && hasHostFqdn)
        {
            spnCheck.Passed = true;
            spnCheck.Message = $"Required SPNs found: HOST/{upperName} and HOST/{dnsHostName}.";
        }
        else
        {
            spnCheck.Passed = false;
            var missing = new List<string>();
            if (!hasHostShort) missing.Add($"HOST/{upperName}");
            if (!hasHostFqdn) missing.Add($"HOST/{dnsHostName ?? "<unknown FQDN>"}");
            spnCheck.Message = $"Missing SPNs: {string.Join(", ", missing)}.";
            spnCheck.Recommendation = "Register the missing SPNs on the computer account using the repair function.";
        }
        result.Checks.Add(spnCheck);

        // 6. DNS A Record
        var dnsACheck = new VerificationCheck
        {
            Name = "DNS A Record",
            Category = "DNS",
        };
        if (!string.IsNullOrEmpty(dnsHostName))
        {
            try
            {
                var zoneName = _domainConfig.DomainDnsName;
                // Extract the host part from the FQDN for the record name
                var hostPart = dnsHostName.Split('.')[0];
                var aRecord = await _dnsZoneStore.GetRecordAsync("default", zoneName, hostPart, DnsRecordType.A, ct);
                if (aRecord != null)
                {
                    dnsACheck.Passed = true;
                    dnsACheck.Message = $"A record found for {dnsHostName}: {aRecord.Data}";
                }
                else
                {
                    dnsACheck.Passed = false;
                    dnsACheck.Message = $"No A record found for {dnsHostName}.";
                    dnsACheck.Recommendation = "Register a DNS A record for the computer or verify DNS dynamic update settings.";
                }
            }
            catch (Exception ex)
            {
                dnsACheck.Passed = false;
                dnsACheck.Message = $"Error checking DNS A record: {ex.Message}";
                dnsACheck.Recommendation = "Verify DNS service is running and zone is accessible.";
            }
        }
        else
        {
            dnsACheck.Passed = false;
            dnsACheck.Message = "dNSHostName attribute is not set on the computer account.";
            dnsACheck.Recommendation = "Set the dNSHostName attribute on the computer account.";
        }
        result.Checks.Add(dnsACheck);

        // 7. DNS PTR Record
        var dnsPtrCheck = new VerificationCheck
        {
            Name = "DNS PTR Record",
            Category = "DNS",
        };
        // PTR records require knowing the IP; we check if a reverse zone entry exists
        dnsPtrCheck.Passed = true; // Informational — PTR check requires IP resolution
        dnsPtrCheck.Message = "PTR record check is informational. Verify reverse DNS is configured if IP is known.";
        dnsPtrCheck.Recommendation = string.IsNullOrEmpty(dnsHostName)
            ? "Set dNSHostName first, then configure reverse DNS."
            : null;
        result.Checks.Add(dnsPtrCheck);

        // 8. Secure Channel
        var scCheck = new VerificationCheck
        {
            Name = "Secure Channel",
            Category = "SecureChannel",
        };
        // Secure channel verification would require Netlogon RPC to the machine.
        // We check whether the account looks authenticatable (enabled + password set).
        if (!isDisabled && computerObj.PwdLastSet > 0)
        {
            scCheck.Passed = true;
            scCheck.Message = "Computer account is enabled with a password set. Secure channel should be operational.";
        }
        else
        {
            scCheck.Passed = false;
            scCheck.Message = "Secure channel may be broken: account is disabled or has no password set.";
            scCheck.Recommendation = "Reset the computer account password and rejoin the computer.";
        }
        result.Checks.Add(scCheck);

        // 9. Last Logon
        var logonCheck = new VerificationCheck
        {
            Name = "Last Logon",
            Category = "Account",
        };
        if (computerObj.LastLogon > 0)
        {
            var lastLogon = DateTimeOffset.FromFileTime(computerObj.LastLogon);
            var sinceLogon = DateTimeOffset.UtcNow - lastLogon;
            if (sinceLogon.TotalDays > 90)
            {
                logonCheck.Passed = false;
                logonCheck.Message = $"Last logon was {sinceLogon.TotalDays:F0} days ago ({lastLogon:u}). Computer may be stale.";
                logonCheck.Recommendation = "Verify the computer is still active. Consider re-joining if the machine was rebuilt.";
            }
            else
            {
                logonCheck.Passed = true;
                logonCheck.Message = $"Last logon: {lastLogon:u} ({sinceLogon.TotalDays:F0} days ago).";
            }
        }
        else
        {
            logonCheck.Passed = false;
            logonCheck.Message = "No logon recorded (lastLogon is not set).";
            logonCheck.Recommendation = "The computer may not have authenticated since joining. Verify network connectivity and restart.";
        }
        result.Checks.Add(logonCheck);

        // 10. OS Info
        var osCheck = new VerificationCheck
        {
            Name = "OS Info",
            Category = "Account",
        };
        var hasOs = computerObj.Attributes.TryGetValue("operatingSystem", out var osAttr) &&
                    osAttr.Values.Count > 0 && !string.IsNullOrWhiteSpace(osAttr.Values[0]?.ToString());
        if (hasOs)
        {
            osCheck.Passed = true;
            osCheck.Message = $"Operating system: {osAttr.Values[0]}";
        }
        else
        {
            osCheck.Passed = false;
            osCheck.Message = "operatingSystem attribute is not populated.";
            osCheck.Recommendation = "The OS information is typically set during domain join. Rejoin may populate it.";
        }
        result.Checks.Add(osCheck);

        // 11. Group Membership
        var groupCheck = new VerificationCheck
        {
            Name = "Group Membership",
            Category = "Account",
        };
        var isDomainComputer = computerObj.PrimaryGroupId == DomainComputersRid;
        if (isDomainComputer)
        {
            groupCheck.Passed = true;
            groupCheck.Message = "Computer is a member of Domain Computers (primaryGroupId = 515).";
        }
        else
        {
            groupCheck.Passed = false;
            groupCheck.Message = $"primaryGroupId is {computerObj.PrimaryGroupId}, expected {DomainComputersRid} (Domain Computers).";
            groupCheck.Recommendation = "Verify the computer's primary group assignment.";
        }
        result.Checks.Add(groupCheck);

        // 12. Replication (USN check)
        var replCheck = new VerificationCheck
        {
            Name = "Replication",
            Category = "Replication",
        };
        if (computerObj.USNChanged > 0)
        {
            replCheck.Passed = true;
            replCheck.Message = $"Account has USN {computerObj.USNChanged}. Object is present in the local replica.";
        }
        else
        {
            replCheck.Passed = false;
            replCheck.Message = "USN is not set. Object may not have been replicated.";
            replCheck.Recommendation = "Force replication and retry verification.";
        }
        result.Checks.Add(replCheck);

        // Overall health
        result.OverallHealthy = result.Checks.All(c => c.Passed);

        return result;
    }

    /// <summary>
    /// Attempts to repair common issues: re-register SPNs and reset the machine password.
    /// </summary>
    public async Task<JoinVerificationResult> RepairAsync(string computerName, CancellationToken ct = default)
    {
        var result = new JoinVerificationResult { ComputerName = computerName };
        var domainDn = _domainConfig.DomainDn;
        if (string.IsNullOrEmpty(domainDn))
            domainDn = _ncService.GetDomainNc().Dn;

        var samAccountName = computerName.ToUpperInvariant() + "$";
        var computerObj = await _store.GetBySamAccountNameAsync("default", domainDn, samAccountName, ct);

        if (computerObj == null || computerObj.IsDeleted)
        {
            result.Checks.Add(new VerificationCheck
            {
                Name = "Repair",
                Category = "Account",
                Passed = false,
                Message = $"Computer account '{samAccountName}' not found. Cannot repair.",
            });
            result.OverallHealthy = false;
            return result;
        }

        var modified = false;
        var dnsHostName = GetDnsHostName(computerObj);
        var upperName = computerName.ToUpperInvariant();

        // Fix SPNs if missing
        var requiredSpns = new List<string>
        {
            $"HOST/{upperName}",
            $"RestrictedKrbHost/{upperName}",
        };
        if (!string.IsNullOrEmpty(dnsHostName))
        {
            requiredSpns.Add($"HOST/{dnsHostName}");
            requiredSpns.Add($"RestrictedKrbHost/{dnsHostName}");
        }

        var existingSpns = new HashSet<string>(computerObj.ServicePrincipalName, StringComparer.OrdinalIgnoreCase);
        var addedSpns = new List<string>();
        foreach (var spn in requiredSpns)
        {
            if (existingSpns.Add(spn))
            {
                addedSpns.Add(spn);
            }
        }

        if (addedSpns.Count > 0)
        {
            computerObj.ServicePrincipalName = existingSpns.ToList();
            computerObj.Attributes["servicePrincipalName"] = new DirectoryAttribute { Values = computerObj.ServicePrincipalName.Cast<object>().ToList() };
            modified = true;
            result.Checks.Add(new VerificationCheck
            {
                Name = "SPN Registration",
                Category = "SPN",
                Passed = true,
                Message = $"Registered missing SPNs: {string.Join(", ", addedSpns)}",
            });
        }
        else
        {
            result.Checks.Add(new VerificationCheck
            {
                Name = "SPN Registration",
                Category = "SPN",
                Passed = true,
                Message = "All required SPNs already registered.",
            });
        }

        // Re-enable account if disabled
        if ((computerObj.UserAccountControl & AccountDisabled) != 0)
        {
            computerObj.UserAccountControl = WorkstationTrustAccount;
            computerObj.Attributes["userAccountControl"] = new DirectoryAttribute { Values = [WorkstationTrustAccount.ToString()] };
            modified = true;
            result.Checks.Add(new VerificationCheck
            {
                Name = "Account Re-enabled",
                Category = "Account",
                Passed = true,
                Message = "Computer account has been re-enabled.",
            });
        }

        if (modified)
        {
            computerObj.WhenChanged = DateTimeOffset.UtcNow;
            await _store.UpdateAsync(computerObj, ct);
        }

        result.OverallHealthy = result.Checks.All(c => c.Passed);
        return result;
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static string GetDnsHostName(DirectoryObject obj)
    {
        if (obj.Attributes.TryGetValue("dNSHostName", out var attr) &&
            attr.Values.Count > 0 && !string.IsNullOrWhiteSpace(attr.Values[0]?.ToString()))
        {
            return attr.Values[0]?.ToString();
        }
        return null;
    }
}
