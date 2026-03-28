using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Web.Models;
using Microsoft.Extensions.Options;

namespace Directory.Web.Services;

/// <summary>
/// Request model for joining a computer to the domain.
/// </summary>
public class DomainJoinRequest
{
    public string ComputerName { get; set; } = "";
    public string DnsHostName { get; set; } = "";
    public string OrganizationalUnit { get; set; }
    public string AdminUserDn { get; set; } = "";
    public string OperatingSystem { get; set; }
    public string OsVersion { get; set; }
    public string OsServicePack { get; set; }
}

/// <summary>
/// Result returned after a domain join operation.
/// </summary>
public class DomainJoinResult
{
    public bool Success { get; set; }
    public string ComputerDn { get; set; } = "";
    public string ComputerSid { get; set; } = "";
    public string MachinePassword { get; set; } = "";
    public string DomainDnsName { get; set; } = "";
    public string DomainNetBiosName { get; set; } = "";
    public string DomainSid { get; set; } = "";
    public string DcName { get; set; } = "";
    public string DcAddress { get; set; } = "";
    public List<string> ServicePrincipalNames { get; set; } = new();
    public string ErrorMessage { get; set; }
}

/// <summary>
/// Information about the domain available for join operations.
/// </summary>
public class DomainJoinInfo
{
    public string DomainDnsName { get; set; } = "";
    public string DomainNetBiosName { get; set; } = "";
    public string DomainSid { get; set; } = "";
    public string DcName { get; set; } = "";
    public string DcAddress { get; set; } = "";
    public string DefaultComputersOu { get; set; } = "";
    public string DomainDn { get; set; } = "";
}

/// <summary>
/// Validation result for a dry-run join request.
/// </summary>
public class DomainJoinValidation
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public string ResolvedOu { get; set; }
    public string ResolvedDn { get; set; }
}

/// <summary>
/// Record of a domain join/unjoin/rejoin operation.
/// </summary>
public class DomainJoinHistoryEntry
{
    public DateTimeOffset Timestamp { get; set; }
    public string Operation { get; set; } = "";
    public string ComputerName { get; set; } = "";
    public string ComputerDn { get; set; } = "";
    public bool Success { get; set; }
    public string ErrorMessage { get; set; }
    public string Operator { get; set; } = "";
}

/// <summary>
/// Orchestrates the full workstation/member-server domain join flow.
/// Creates computer accounts, generates machine passwords, registers SPNs,
/// and returns all information needed for the client to configure itself.
/// </summary>
public partial class DomainJoinService
{
    private const int MachinePasswordLength = 120;
    private const int MaxComputerNameLength = 15;
    private const int WorkstationTrustAccount = 0x1000;

    // Characters allowed in generated machine passwords (printable ASCII minus ambiguous chars)
    private const string PasswordChars =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!@#$%^&*()-_=+[]{}|;:,.<>?";

    private readonly IDirectoryStore _store;
    private readonly IPasswordPolicy _passwordPolicy;
    private readonly IRidAllocator _ridAllocator;
    private readonly INamingContextService _ncService;
    private readonly IAuditService _audit;
    private readonly DomainConfiguration _domainConfig;
    private readonly DcNodeOptions _dcNodeOptions;
    private readonly ILogger<DomainJoinService> _logger;

    // In-memory history ring buffer (most recent operations)
    private readonly List<DomainJoinHistoryEntry> _history = new();
    private readonly object _historyLock = new();
    private const int MaxHistoryEntries = 200;

    public DomainJoinService(
        IDirectoryStore store,
        IPasswordPolicy passwordPolicy,
        IRidAllocator ridAllocator,
        INamingContextService ncService,
        IAuditService audit,
        DomainConfiguration domainConfig,
        IOptions<DcNodeOptions> dcNodeOptions,
        ILogger<DomainJoinService> logger)
    {
        _store = store;
        _passwordPolicy = passwordPolicy;
        _ridAllocator = ridAllocator;
        _ncService = ncService;
        _audit = audit;
        _domainConfig = domainConfig;
        _dcNodeOptions = dcNodeOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// Returns domain information needed for join operations.
    /// </summary>
    public DomainJoinInfo GetDomainInfo()
    {
        var domainDn = _domainConfig.DomainDn;
        if (string.IsNullOrEmpty(domainDn))
            domainDn = _ncService.GetDomainNc().Dn;

        return new DomainJoinInfo
        {
            DomainDnsName = _domainConfig.DomainDnsName,
            DomainNetBiosName = _domainConfig.NetBiosName,
            DomainSid = _domainConfig.DomainSid,
            DcName = _dcNodeOptions.Hostname,
            DcAddress = _dcNodeOptions.BindAddresses.FirstOrDefault() ?? "127.0.0.1",
            DefaultComputersOu = $"CN=Computers,{domainDn}",
            DomainDn = domainDn,
        };
    }

    /// <summary>
    /// Validates a join request without executing it (dry run).
    /// </summary>
    public async Task<DomainJoinValidation> ValidateAsync(DomainJoinRequest request, CancellationToken ct = default)
    {
        var validation = new DomainJoinValidation();
        var errors = validation.Errors;

        // Validate computer name format
        if (string.IsNullOrWhiteSpace(request.ComputerName))
        {
            errors.Add("Computer name is required.");
        }
        else
        {
            if (request.ComputerName.Length > MaxComputerNameLength)
                errors.Add($"Computer name must not exceed {MaxComputerNameLength} characters.");

            if (!ComputerNameRegex().IsMatch(request.ComputerName))
                errors.Add("Computer name must contain only alphanumeric characters and hyphens, and cannot start or end with a hyphen.");
        }

        if (string.IsNullOrWhiteSpace(request.DnsHostName))
            errors.Add("DNS host name is required.");

        if (string.IsNullOrWhiteSpace(request.AdminUserDn))
            errors.Add("Admin user DN is required.");

        // Check for existing account with the same name
        if (!string.IsNullOrWhiteSpace(request.ComputerName))
        {
            var domainDn = _domainConfig.DomainDn;
            if (string.IsNullOrEmpty(domainDn))
                domainDn = _ncService.GetDomainNc().Dn;

            var samAccountName = request.ComputerName.ToUpperInvariant() + "$";
            var existing = await _store.GetBySamAccountNameAsync("default", domainDn, samAccountName, ct);
            if (existing != null && !existing.IsDeleted)
                errors.Add($"A computer account with sAMAccountName '{samAccountName}' already exists.");
        }

        // Resolve target OU
        var info = GetDomainInfo();
        var ouDn = string.IsNullOrWhiteSpace(request.OrganizationalUnit)
            ? info.DefaultComputersOu
            : request.OrganizationalUnit;

        var ouObj = await _store.GetByDnAsync("default", ouDn, ct);
        if (ouObj == null || ouObj.IsDeleted)
            errors.Add($"Target OU '{ouDn}' does not exist.");

        validation.ResolvedOu = ouDn;
        validation.ResolvedDn = $"CN={request.ComputerName?.ToUpperInvariant()},{ouDn}";
        validation.IsValid = errors.Count == 0;
        return validation;
    }

    /// <summary>
    /// Joins a computer to the domain by creating a computer account, setting its password,
    /// and registering SPNs.
    /// </summary>
    public async Task<DomainJoinResult> JoinAsync(DomainJoinRequest request, string sourceIp, CancellationToken ct = default)
    {
        var info = GetDomainInfo();

        // Step 1: Validate
        var validation = await ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            var errorResult = new DomainJoinResult
            {
                Success = false,
                ErrorMessage = string.Join(" ", validation.Errors),
                DomainDnsName = info.DomainDnsName,
                DomainNetBiosName = info.DomainNetBiosName,
                DomainSid = info.DomainSid,
                DcName = info.DcName,
                DcAddress = info.DcAddress,
            };
            RecordHistory("Join", request.ComputerName, "", false, errorResult.ErrorMessage, request.AdminUserDn);
            return errorResult;
        }

        var computerName = request.ComputerName.ToUpperInvariant();
        var samAccountName = computerName + "$";
        var ouDn = validation.ResolvedOu;
        var computerDn = validation.ResolvedDn;

        try
        {
            // Step 2: Generate SID
            var objectSid = await _ridAllocator.GenerateObjectSidAsync("default", info.DomainDn, ct);

            // Step 3: Create computer account
            var computerObj = new DirectoryObject
            {
                Id = computerDn.ToLowerInvariant(),
                TenantId = "default",
                DomainDn = info.DomainDn,
                ObjectCategory = "computer",
                DistinguishedName = computerDn,
                ObjectGuid = Guid.NewGuid().ToString(),
                ObjectSid = objectSid,
                SAMAccountName = samAccountName,
                Cn = computerName,
                DisplayName = computerName,
                ObjectClass = ["top", "person", "organizationalPerson", "user", "computer"],
                UserAccountControl = WorkstationTrustAccount,
                ParentDn = ouDn,
                PrimaryGroupId = 515, // Domain Computers
                WhenCreated = DateTimeOffset.UtcNow,
                WhenChanged = DateTimeOffset.UtcNow,
            };

            // Set AD attributes
            computerObj.Attributes["dNSHostName"] = new DirectoryAttribute { Values = [request.DnsHostName.ToLowerInvariant()] };
            computerObj.Attributes["sAMAccountName"] = new DirectoryAttribute { Values = [samAccountName] };
            computerObj.Attributes["userAccountControl"] = new DirectoryAttribute { Values = [WorkstationTrustAccount.ToString()] };

            if (!string.IsNullOrWhiteSpace(request.OperatingSystem))
                computerObj.Attributes["operatingSystem"] = new DirectoryAttribute { Values = [request.OperatingSystem] };
            if (!string.IsNullOrWhiteSpace(request.OsVersion))
                computerObj.Attributes["operatingSystemVersion"] = new DirectoryAttribute { Values = [request.OsVersion] };
            if (!string.IsNullOrWhiteSpace(request.OsServicePack))
                computerObj.Attributes["operatingSystemServicePack"] = new DirectoryAttribute { Values = [request.OsServicePack] };

            await _store.CreateAsync(computerObj, ct);

            // Step 4: Generate and set machine password (120 chars, cryptographically random)
            var machinePassword = GenerateMachinePassword();
            await _passwordPolicy.SetPasswordAsync("default", computerDn, machinePassword, ct);

            // Update pwdLastSet
            computerObj.PwdLastSet = DateTimeOffset.UtcNow.ToFileTime();
            computerObj.WhenChanged = DateTimeOffset.UtcNow;

            // Step 5: Register SPNs
            var spns = new List<string>
            {
                $"HOST/{computerName}",
                $"HOST/{request.DnsHostName.ToLowerInvariant()}",
                $"RestrictedKrbHost/{computerName}",
                $"RestrictedKrbHost/{request.DnsHostName.ToLowerInvariant()}",
            };

            computerObj.ServicePrincipalName = spns;
            computerObj.Attributes["servicePrincipalName"] = new DirectoryAttribute { Values = spns.Cast<object>().ToList() };

            await _store.UpdateAsync(computerObj, ct);

            // Step 6: Audit
            await _audit.LogAsync(new AuditEntry
            {
                TenantId = "default",
                Action = "DomainJoin",
                TargetDn = computerDn,
                TargetObjectClass = "computer",
                ActorIdentity = request.AdminUserDn,
                SourceIp = sourceIp,
                Details = new Dictionary<string, string>
                {
                    ["computerName"] = computerName,
                    ["dnsHostName"] = request.DnsHostName,
                    ["ou"] = ouDn,
                },
            }, ct);

            _logger.LogInformation(
                "Computer {ComputerName} joined to domain {Domain} in OU {OU} by {Admin}",
                computerName, info.DomainDnsName, ouDn, request.AdminUserDn);

            var result = new DomainJoinResult
            {
                Success = true,
                ComputerDn = computerDn,
                ComputerSid = objectSid,
                MachinePassword = machinePassword,
                DomainDnsName = info.DomainDnsName,
                DomainNetBiosName = info.DomainNetBiosName,
                DomainSid = info.DomainSid,
                DcName = info.DcName,
                DcAddress = info.DcAddress,
                ServicePrincipalNames = spns,
            };

            RecordHistory("Join", computerName, computerDn, true, null, request.AdminUserDn);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to join computer {ComputerName} to domain", computerName);
            RecordHistory("Join", computerName, computerDn, false, ex.Message, request.AdminUserDn);

            return new DomainJoinResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                DomainDnsName = info.DomainDnsName,
                DomainNetBiosName = info.DomainNetBiosName,
                DomainSid = info.DomainSid,
                DcName = info.DcName,
                DcAddress = info.DcAddress,
            };
        }
    }

    /// <summary>
    /// Resets an existing computer account's password for re-joining.
    /// </summary>
    public async Task<DomainJoinResult> RejoinAsync(string computerName, string adminUserDn, string sourceIp, CancellationToken ct = default)
    {
        var info = GetDomainInfo();
        var upperName = computerName.ToUpperInvariant();
        var samAccountName = upperName + "$";

        try
        {
            var computerObj = await _store.GetBySamAccountNameAsync("default", info.DomainDn, samAccountName, ct);
            if (computerObj == null || computerObj.IsDeleted)
            {
                var err = $"Computer account '{samAccountName}' not found.";
                RecordHistory("Rejoin", upperName, "", false, err, adminUserDn);
                return new DomainJoinResult { Success = false, ErrorMessage = err, DomainDnsName = info.DomainDnsName, DomainNetBiosName = info.DomainNetBiosName, DomainSid = info.DomainSid, DcName = info.DcName, DcAddress = info.DcAddress };
            }

            if (!computerObj.ObjectClass.Contains("computer"))
            {
                var err = "Object is not a computer account.";
                RecordHistory("Rejoin", upperName, computerObj.DistinguishedName, false, err, adminUserDn);
                return new DomainJoinResult { Success = false, ErrorMessage = err, DomainDnsName = info.DomainDnsName, DomainNetBiosName = info.DomainNetBiosName, DomainSid = info.DomainSid, DcName = info.DcName, DcAddress = info.DcAddress };
            }

            // Generate new machine password
            var machinePassword = GenerateMachinePassword();
            await _passwordPolicy.SetPasswordAsync("default", computerObj.DistinguishedName, machinePassword, ct);

            // Re-enable account if disabled
            computerObj.UserAccountControl = WorkstationTrustAccount;
            computerObj.PwdLastSet = DateTimeOffset.UtcNow.ToFileTime();
            computerObj.WhenChanged = DateTimeOffset.UtcNow;
            await _store.UpdateAsync(computerObj, ct);

            await _audit.LogAsync(new AuditEntry
            {
                TenantId = "default",
                Action = "DomainRejoin",
                TargetDn = computerObj.DistinguishedName,
                TargetObjectClass = "computer",
                ActorIdentity = adminUserDn,
                SourceIp = sourceIp,
            }, ct);

            _logger.LogInformation("Computer {ComputerName} re-joined (password reset) by {Admin}", upperName, adminUserDn);

            var result = new DomainJoinResult
            {
                Success = true,
                ComputerDn = computerObj.DistinguishedName,
                ComputerSid = computerObj.ObjectSid ?? "",
                MachinePassword = machinePassword,
                DomainDnsName = info.DomainDnsName,
                DomainNetBiosName = info.DomainNetBiosName,
                DomainSid = info.DomainSid,
                DcName = info.DcName,
                DcAddress = info.DcAddress,
                ServicePrincipalNames = computerObj.ServicePrincipalName,
            };

            RecordHistory("Rejoin", upperName, computerObj.DistinguishedName, true, null, adminUserDn);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rejoin computer {ComputerName}", upperName);
            RecordHistory("Rejoin", upperName, "", false, ex.Message, adminUserDn);
            return new DomainJoinResult { Success = false, ErrorMessage = ex.Message, DomainDnsName = info.DomainDnsName, DomainNetBiosName = info.DomainNetBiosName, DomainSid = info.DomainSid, DcName = info.DcName, DcAddress = info.DcAddress };
        }
    }

    /// <summary>
    /// Disables a computer account (unjoin).
    /// </summary>
    public async Task<DomainJoinResult> UnjoinAsync(string computerName, string adminUserDn, string sourceIp, CancellationToken ct = default)
    {
        var info = GetDomainInfo();
        var upperName = computerName.ToUpperInvariant();
        var samAccountName = upperName + "$";

        try
        {
            var computerObj = await _store.GetBySamAccountNameAsync("default", info.DomainDn, samAccountName, ct);
            if (computerObj == null || computerObj.IsDeleted)
            {
                var err = $"Computer account '{samAccountName}' not found.";
                RecordHistory("Unjoin", upperName, "", false, err, adminUserDn);
                return new DomainJoinResult { Success = false, ErrorMessage = err, DomainDnsName = info.DomainDnsName, DomainNetBiosName = info.DomainNetBiosName, DomainSid = info.DomainSid, DcName = info.DcName, DcAddress = info.DcAddress };
            }

            // Disable the account by setting ACCOUNTDISABLE (0x0002) alongside WORKSTATION_TRUST
            computerObj.UserAccountControl = WorkstationTrustAccount | 0x0002;
            computerObj.WhenChanged = DateTimeOffset.UtcNow;
            await _store.UpdateAsync(computerObj, ct);

            await _audit.LogAsync(new AuditEntry
            {
                TenantId = "default",
                Action = "DomainUnjoin",
                TargetDn = computerObj.DistinguishedName,
                TargetObjectClass = "computer",
                ActorIdentity = adminUserDn,
                SourceIp = sourceIp,
            }, ct);

            _logger.LogInformation("Computer {ComputerName} unjoined (disabled) by {Admin}", upperName, adminUserDn);

            RecordHistory("Unjoin", upperName, computerObj.DistinguishedName, true, null, adminUserDn);

            return new DomainJoinResult
            {
                Success = true,
                ComputerDn = computerObj.DistinguishedName,
                ComputerSid = computerObj.ObjectSid ?? "",
                DomainDnsName = info.DomainDnsName,
                DomainNetBiosName = info.DomainNetBiosName,
                DomainSid = info.DomainSid,
                DcName = info.DcName,
                DcAddress = info.DcAddress,
                ServicePrincipalNames = computerObj.ServicePrincipalName,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unjoin computer {ComputerName}", upperName);
            RecordHistory("Unjoin", upperName, "", false, ex.Message, adminUserDn);
            return new DomainJoinResult { Success = false, ErrorMessage = ex.Message, DomainDnsName = info.DomainDnsName, DomainNetBiosName = info.DomainNetBiosName, DomainSid = info.DomainSid, DcName = info.DcName, DcAddress = info.DcAddress };
        }
    }

    /// <summary>
    /// Returns the most recent join/unjoin/rejoin operations.
    /// </summary>
    public IReadOnlyList<DomainJoinHistoryEntry> GetHistory()
    {
        lock (_historyLock)
        {
            return _history.OrderByDescending(h => h.Timestamp).ToList();
        }
    }

    // ── Private Helpers ──────────────────────────────────────────────

    /// <summary>
    /// Generates a cryptographically random machine password of the specified length.
    /// AD machine passwords default to 120 characters.
    /// </summary>
    private static string GenerateMachinePassword()
    {
        return RandomNumberGenerator.GetString(PasswordChars, MachinePasswordLength);
    }

    private void RecordHistory(string operation, string computerName, string computerDn, bool success, string error, string operatorDn)
    {
        var entry = new DomainJoinHistoryEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Operation = operation,
            ComputerName = computerName,
            ComputerDn = computerDn,
            Success = success,
            ErrorMessage = error,
            Operator = operatorDn,
        };

        lock (_historyLock)
        {
            _history.Add(entry);
            if (_history.Count > MaxHistoryEntries)
                _history.RemoveAt(0);
        }
    }

    [GeneratedRegex(@"^[a-zA-Z0-9]([a-zA-Z0-9\-]{0,13}[a-zA-Z0-9])?$")]
    private static partial Regex ComputerNameRegex();
}
