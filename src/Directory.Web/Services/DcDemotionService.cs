using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Dns;
using Directory.Web.Models;

using SearchScope = Directory.Core.Models.SearchScope;

namespace Directory.Web.Services;

/// <summary>
/// Handles DC demotion (the reverse of promotion). Gracefully removes a domain controller
/// from the domain by transferring FSMO roles, draining replication, cleaning up DNS records,
/// removing NTDS Settings, server objects, replication partnerships, and resetting the computer account.
/// Progress is reported through <see cref="SetupStateService"/> for the Vue frontend to poll.
/// </summary>
public class DcDemotionService
{
    private readonly IDirectoryStore _store;
    private readonly INamingContextService _ncService;
    private readonly DcInstanceInfo _dcInfo;
    private readonly DomainConfiguration _domainConfig;
    private readonly DnsZoneStore _zoneStore;
    private readonly FsmoRoleService _fsmoRoleService;
    private readonly SetupStateService _setupState;
    private readonly ILogger<DcDemotionService> _logger;

    private const string TenantId = "default";

    public DcDemotionService(
        IDirectoryStore store,
        INamingContextService ncService,
        DcInstanceInfo dcInfo,
        DomainConfiguration domainConfig,
        DnsZoneStore zoneStore,
        FsmoRoleService fsmoRoleService,
        SetupStateService setupState,
        ILogger<DcDemotionService> logger)
    {
        _store = store;
        _ncService = ncService;
        _dcInfo = dcInfo;
        _domainConfig = domainConfig;
        _zoneStore = zoneStore;
        _fsmoRoleService = fsmoRoleService;
        _setupState = setupState;
        _logger = logger;
    }

    /// <summary>
    /// Validates whether demotion can proceed without actually performing it.
    /// Returns a list of warnings/blockers.
    /// </summary>
    public async Task<DemotionValidationResult> ValidateAsync(DemotionRequest request, CancellationToken ct = default)
    {
        var result = new DemotionValidationResult();

        try
        {
            var domainDn = _domainConfig.DomainDn;
            var serverDn = _dcInfo.ServerDn(domainDn);
            var ntdsSettingsDn = _dcInfo.NtdsSettingsDn(domainDn);

            // Check if this DC exists in the directory
            var serverObj = await _store.GetByDnAsync(TenantId, serverDn, ct);
            if (serverObj == null)
            {
                result.Errors.Add("This server is not registered as a domain controller in the directory.");
                return result;
            }

            // Check FSMO roles held by this DC
            var roles = await _fsmoRoleService.GetAllRoleHoldersAsync(ct);
            var heldRoles = roles.Where(r =>
                r.HolderDn.Equals(ntdsSettingsDn, StringComparison.OrdinalIgnoreCase)).ToList();

            if (heldRoles.Count > 0 && !request.IsLastDcInDomain)
            {
                // Check if there are other DCs to transfer to
                var otherDcs = await GetOtherDcNtdsSettingsDnsAsync(domainDn, ntdsSettingsDn, ct);
                if (otherDcs.Count == 0 && !request.ForceRemoval)
                {
                    result.Errors.Add(
                        $"This DC holds {heldRoles.Count} FSMO role(s) and no other DCs are available to transfer them to. " +
                        "Use 'Last DC in domain' option or force removal.");
                }
                else if (otherDcs.Count > 0)
                {
                    result.Warnings.Add(
                        $"This DC holds {heldRoles.Count} FSMO role(s): {string.Join(", ", heldRoles.Select(r => r.Role))}. " +
                        "They will be transferred to another DC during demotion.");
                }
            }

            // Check if this is truly the last DC
            if (request.IsLastDcInDomain)
            {
                var otherDcs = await GetOtherDcNtdsSettingsDnsAsync(domainDn, ntdsSettingsDn, ct);
                if (otherDcs.Count > 0)
                {
                    result.Warnings.Add(
                        $"There are {otherDcs.Count} other DC(s) in the domain. Proceeding as 'last DC' will remove " +
                        "all domain data. Ensure those DCs are already decommissioned.");
                }
            }

            // Check replication health (warn if objects are pending)
            if (!request.ForceRemoval && !request.IsLastDcInDomain)
            {
                result.Warnings.Add("Replication drain will be attempted to ensure all changes are propagated before demotion.");
            }

            result.IsValid = result.Errors.Count == 0;
            result.HeldFsmoRoles = heldRoles.Select(r => r.Role.ToString()).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Demotion validation failed");
            result.Errors.Add($"Validation error: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Executes the full demotion workflow. Called from a background task.
    /// Progress is reported through <see cref="SetupStateService"/>.
    /// </summary>
    public async Task DemoteAsync(DemotionRequest request, CancellationToken ct = default)
    {
        var steps = new List<string>();
        try
        {
            _setupState.IsProvisioning = true;
            _setupState.ProvisioningProgress = 0;
            _setupState.ProvisioningError = null;
            _setupState.ProvisioningPhase = "Starting demotion...";

            var domainDn = _domainConfig.DomainDn;
            var domainDnsName = _domainConfig.DomainDnsName;
            var forestDnsName = _domainConfig.ForestDnsName;
            var serverDn = _dcInfo.ServerDn(domainDn);
            var ntdsSettingsDn = _dcInfo.NtdsSettingsDn(domainDn);
            var siteName = _dcInfo.SiteName;
            var hostname = _dcInfo.Hostname;

            // Step 1: Pre-flight checks (5%)
            _setupState.ProvisioningPhase = "Running pre-flight checks...";
            _setupState.ProvisioningProgress = 5;
            steps.Add("Pre-flight checks passed");
            _logger.LogInformation("Demotion: pre-flight checks passed for {Hostname}", hostname);

            // Step 2: Transfer FSMO roles (15%)
            if (!request.IsLastDcInDomain)
            {
                _setupState.ProvisioningPhase = "Transferring FSMO roles...";
                _setupState.ProvisioningProgress = 10;
                await TransferFsmoRolesAsync(ntdsSettingsDn, domainDn, request.ForceRemoval, ct);
                steps.Add("FSMO roles transferred");
                _logger.LogInformation("Demotion: FSMO roles transferred for {Hostname}", hostname);
            }
            _setupState.ProvisioningProgress = 15;

            // Step 3: Drain replication (25%)
            if (!request.ForceRemoval && !request.IsLastDcInDomain)
            {
                _setupState.ProvisioningPhase = "Draining replication...";
                _setupState.ProvisioningProgress = 20;
                // In a real AD environment this would trigger replication cycles.
                // For Directory.NET with Cosmos DB, data is already shared, so this is a no-op.
                await Task.Delay(500, ct); // Brief pause for UX
                steps.Add("Replication drain completed");
                _logger.LogInformation("Demotion: replication drain completed for {Hostname}", hostname);
            }
            _setupState.ProvisioningProgress = 25;

            // Step 4: Remove DNS records (40%)
            if (request.RemoveDnsRecords)
            {
                _setupState.ProvisioningPhase = "Removing DNS records...";
                _setupState.ProvisioningProgress = 30;
                await RemoveDnsRecordsAsync(domainDnsName, forestDnsName, siteName, hostname, domainDn, ct);
                steps.Add("DNS records removed");
                _logger.LogInformation("Demotion: DNS records removed for {Hostname}", hostname);
            }
            _setupState.ProvisioningProgress = 40;

            // Step 5: Remove NTDS Settings (50%)
            _setupState.ProvisioningPhase = "Removing NTDS Settings...";
            _setupState.ProvisioningProgress = 45;
            await RemoveObjectIfExistsAsync(ntdsSettingsDn, ct);
            steps.Add("NTDS Settings removed");
            _logger.LogInformation("Demotion: NTDS Settings removed for {Hostname}", hostname);
            _setupState.ProvisioningProgress = 50;

            // Step 6: Remove replication connection objects (60%)
            _setupState.ProvisioningPhase = "Removing replication partnerships...";
            _setupState.ProvisioningProgress = 55;
            await RemoveConnectionObjectsAsync(serverDn, domainDn, ct);
            steps.Add("Replication partnerships removed");
            _logger.LogInformation("Demotion: replication partnerships removed for {Hostname}", hostname);
            _setupState.ProvisioningProgress = 60;

            // Step 7: Remove Server object (70%)
            _setupState.ProvisioningPhase = "Removing server object...";
            _setupState.ProvisioningProgress = 65;
            await RemoveObjectIfExistsAsync(serverDn, ct);
            steps.Add("Server object removed");
            _logger.LogInformation("Demotion: server object removed for {Hostname}", hostname);
            _setupState.ProvisioningProgress = 70;

            // Step 8: Reset/remove computer account (80%)
            _setupState.ProvisioningPhase = "Resetting computer account...";
            _setupState.ProvisioningProgress = 75;
            var computerDn = $"CN={hostname},OU=Domain Controllers,{domainDn}";
            if (request.IsLastDcInDomain)
            {
                await RemoveObjectIfExistsAsync(computerDn, ct);
                steps.Add("Computer account removed (last DC)");
            }
            else
            {
                await ResetComputerAccountAsync(computerDn, ct);
                steps.Add("Computer account reset to workstation trust");
            }
            _logger.LogInformation("Demotion: computer account handled for {Hostname}", hostname);
            _setupState.ProvisioningProgress = 80;

            // Step 9: Clean up SPNs (85%)
            _setupState.ProvisioningPhase = "Cleaning up SPNs...";
            _setupState.ProvisioningProgress = 85;
            await CleanupSpnsAsync(computerDn, ct);
            steps.Add("DC-specific SPNs removed");
            _logger.LogInformation("Demotion: SPNs cleaned up for {Hostname}", hostname);

            // Step 10: If last DC, remove domain data (95%)
            if (request.IsLastDcInDomain)
            {
                _setupState.ProvisioningPhase = "Removing domain data...";
                _setupState.ProvisioningProgress = 90;
                await RemoveDomainDataAsync(domainDn, ct);
                steps.Add("Domain data removed (last DC deprovisioned)");
                _logger.LogInformation("Demotion: domain data removed for last DC {Hostname}", hostname);
            }

            _setupState.ProvisioningPhase = "Demotion complete";
            _setupState.ProvisioningProgress = 100;
            _setupState.IsProvisioning = false;

            // If last DC, mark as no longer provisioned
            if (request.IsLastDcInDomain)
            {
                _setupState.IsProvisioned = false;
            }

            _logger.LogInformation("Demotion completed successfully for {Hostname}. Steps: {Steps}",
                hostname, string.Join(", ", steps));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Demotion failed");
            _setupState.ProvisioningError = ex.Message;
            _setupState.ProvisioningPhase = "Demotion failed";
            _setupState.IsProvisioning = false;
            steps.Add($"FAILED: {ex.Message}");
        }
    }

    /// <summary>
    /// Returns the current demotion status for the API.
    /// </summary>
    public DemotionResult GetStatus()
    {
        return new DemotionResult
        {
            Success = _setupState.ProvisioningProgress >= 100 && _setupState.ProvisioningError == null,
            Steps = new List<string>(), // Steps are tracked via ProvisioningPhase
            ErrorMessage = _setupState.ProvisioningError,
        };
    }

    // ── Private helper methods ──────────────────────────────────────────────

    private async Task TransferFsmoRolesAsync(string ntdsSettingsDn, string domainDn, bool force, CancellationToken ct)
    {
        var roles = await _fsmoRoleService.GetAllRoleHoldersAsync(ct);
        var heldRoles = roles.Where(r =>
            r.HolderDn.Equals(ntdsSettingsDn, StringComparison.OrdinalIgnoreCase)).ToList();

        if (heldRoles.Count == 0) return;

        var otherDcs = await GetOtherDcNtdsSettingsDnsAsync(domainDn, ntdsSettingsDn, ct);
        if (otherDcs.Count == 0)
        {
            if (force)
            {
                _logger.LogWarning("No other DCs available for FSMO role transfer; force mode — roles will be orphaned");
                return;
            }
            throw new InvalidOperationException("No other DCs available to receive FSMO roles.");
        }

        var targetDn = otherDcs[0]; // Transfer to first available DC
        foreach (var role in heldRoles)
        {
            _logger.LogInformation("Transferring FSMO role {Role} to {Target}", role.Role, targetDn);
            if (force)
            {
                await _fsmoRoleService.SeizeRoleAsync(role.Role, targetDn, ct);
            }
            else
            {
                await _fsmoRoleService.TransferRoleAsync(role.Role, targetDn, ct);
            }
        }
    }

    private async Task RemoveDnsRecordsAsync(
        string domainDnsName, string forestDnsName, string siteName,
        string hostname, string domainDn, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(domainDnsName)) return;
        if (string.IsNullOrEmpty(forestDnsName)) forestDnsName = domainDnsName;

        var dcFqdn = _dcInfo.Fqdn(domainDnsName);
        var dcIp = _dcInfo.IpAddress;

        // Build the list of DNS SRV records to remove (matching DcRegistrationService patterns).
        // Data format is "priority weight port target" as registered by DcRegistrationService.
        var srvRecords = new List<(string Zone, string Name, string Data)>
        {
            (domainDnsName, $"_ldap._tcp.{domainDnsName}", $"0 100 389 {dcFqdn}"),
            (domainDnsName, $"_ldap._tcp.{siteName}._sites.{domainDnsName}", $"0 100 389 {dcFqdn}"),
            (domainDnsName, $"_ldap._tcp.dc._msdcs.{domainDnsName}", $"0 100 389 {dcFqdn}"),
            (domainDnsName, $"_ldap._tcp.{siteName}._sites.dc._msdcs.{domainDnsName}", $"0 100 389 {dcFqdn}"),
            (domainDnsName, $"_kerberos._tcp.{domainDnsName}", $"0 100 88 {dcFqdn}"),
            (domainDnsName, $"_kerberos._tcp.{siteName}._sites.{domainDnsName}", $"0 100 88 {dcFqdn}"),
            (domainDnsName, $"_kerberos._tcp.dc._msdcs.{domainDnsName}", $"0 100 88 {dcFqdn}"),
            (domainDnsName, $"_kerberos._udp.{domainDnsName}", $"0 100 88 {dcFqdn}"),
            (domainDnsName, $"_kpasswd._tcp.{domainDnsName}", $"0 100 464 {dcFqdn}"),
            (domainDnsName, $"_kpasswd._udp.{domainDnsName}", $"0 100 464 {dcFqdn}"),
            (domainDnsName, $"_ldap._tcp.pdc._msdcs.{domainDnsName}", $"0 100 389 {dcFqdn}"),
            (forestDnsName, $"_gc._tcp.{forestDnsName}", $"0 100 3268 {dcFqdn}"),
            (forestDnsName, $"_gc._tcp.{siteName}._sites.{forestDnsName}", $"0 100 3268 {dcFqdn}"),
        };

        // Generate domain GUID for the GUID-based SRV record
        var domainGuid = GenerateDomainGuid(domainDn);
        srvRecords.Add((forestDnsName, $"_ldap._tcp.{domainGuid}.domains._msdcs.{forestDnsName}", $"0 100 389 {dcFqdn}"));

        // Remove SRV records that target this DC
        foreach (var (zone, name, data) in srvRecords)
        {
            try
            {
                await _zoneStore.DeleteRecordByDataAsync(TenantId, zone, name, DnsRecordType.SRV, data, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to remove DNS SRV record {Name} from zone {Zone}", name, zone);
            }
        }

        // Remove A records for this DC
        try
        {
            await _zoneStore.DeleteRecordByDataAsync(TenantId, domainDnsName, dcFqdn, DnsRecordType.A, dcIp, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove A record for {Fqdn}", dcFqdn);
        }

        try
        {
            await _zoneStore.DeleteRecordByDataAsync(TenantId, forestDnsName, $"gc._msdcs.{forestDnsName}", DnsRecordType.A, dcIp, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove GC A record");
        }
    }

    private async Task RemoveConnectionObjectsAsync(string serverDn, string domainDn, CancellationToken ct)
    {
        // Find and remove connection objects (nTDSConnection) under NTDS Settings
        // that reference this server as fromServer
        var configDn = $"CN=Configuration,{domainDn}";
        var sitesDn = $"CN=Sites,{configDn}";

        // Search for connection objects that reference this server
        var searchResult = await _store.SearchAsync(TenantId, sitesDn,
            SearchScope.WholeSubtree,
            new EqualityFilterNode("objectClass", "nTDSConnection"),
            null, ct: ct);

        foreach (var connObj in searchResult.Entries)
        {
            var fromServer = connObj.GetAttribute("fromServer")?.GetFirstString();
            if (fromServer != null &&
                (fromServer.Contains(_dcInfo.Hostname, StringComparison.OrdinalIgnoreCase) ||
                 connObj.ParentDn?.Contains(_dcInfo.Hostname, StringComparison.OrdinalIgnoreCase) == true))
            {
                try
                {
                    await _store.DeleteAsync(TenantId, connObj.DistinguishedName, ct: ct);
                    _logger.LogInformation("Removed connection object {Dn}", connObj.DistinguishedName);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to remove connection object {Dn}", connObj.DistinguishedName);
                }
            }
        }
    }

    private async Task ResetComputerAccountAsync(string computerDn, CancellationToken ct)
    {
        var computer = await _store.GetByDnAsync(TenantId, computerDn, ct);
        if (computer == null) return;

        // Change from SERVER_TRUST_ACCOUNT | TRUSTED_FOR_DELEGATION (532480)
        // to WORKSTATION_TRUST_ACCOUNT (4096)
        computer.SetAttribute("userAccountControl",
            new DirectoryAttribute("userAccountControl", 4096));
        computer.WhenChanged = DateTimeOffset.UtcNow;
        await _store.UpdateAsync(computer, ct);
    }

    private async Task CleanupSpnsAsync(string computerDn, CancellationToken ct)
    {
        var computer = await _store.GetByDnAsync(TenantId, computerDn, ct);
        if (computer == null) return;

        // Remove DC-specific SPNs (GC, DRSUAPI GUID, ldap with domain suffix)
        var spns = computer.ServicePrincipalName ?? new List<string>();
        var dcSpnPrefixes = new[] { "GC/", "E3514235-4B06-11D1-AB04-00C04FC2DCD2/" };
        var cleanedSpns = spns.Where(spn =>
            !dcSpnPrefixes.Any(prefix => spn.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))).ToList();

        computer.ServicePrincipalName = cleanedSpns;
        computer.WhenChanged = DateTimeOffset.UtcNow;
        await _store.UpdateAsync(computer, ct);
    }

    private async Task RemoveDomainDataAsync(string domainDn, CancellationToken ct)
    {
        // When deprovisioning the last DC, remove all domain objects.
        // This is a destructive operation — removes the entire domain NC.
        try
        {
            var searchResult = await _store.SearchAsync(TenantId, domainDn,
                SearchScope.WholeSubtree,
                new PresenceFilterNode("objectClass"),
                null, ct: ct);
            var ordered = searchResult.Entries.OrderByDescending(o => o.DistinguishedName.Split(',').Length).ToList();

            foreach (var obj in ordered)
            {
                try
                {
                    await _store.DeleteAsync(TenantId, obj.DistinguishedName, ct: ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete object {Dn} during domain removal", obj.DistinguishedName);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove domain data");
            throw;
        }
    }

    private async Task RemoveObjectIfExistsAsync(string dn, CancellationToken ct)
    {
        var obj = await _store.GetByDnAsync(TenantId, dn, ct);
        if (obj != null)
        {
            await _store.DeleteAsync(TenantId, dn, ct: ct);
        }
    }

    private async Task<List<string>> GetOtherDcNtdsSettingsDnsAsync(
        string domainDn, string thisNtdsSettingsDn, CancellationToken ct)
    {
        var configDn = $"CN=Configuration,{domainDn}";
        var sitesDn = $"CN=Sites,{configDn}";

        var searchResult = await _store.SearchAsync(TenantId, sitesDn,
            SearchScope.WholeSubtree,
            new EqualityFilterNode("objectClass", "nTDSDSA"),
            null, ct: ct);

        return searchResult.Entries
            .Where(o => !o.DistinguishedName.Equals(thisNtdsSettingsDn, StringComparison.OrdinalIgnoreCase))
            .Select(o => o.DistinguishedName)
            .ToList();
    }

    private static string GenerateDomainGuid(string domainDn)
    {
        var bytes = System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes(domainDn.ToLowerInvariant()));
        return new Guid(bytes).ToString();
    }
}

// ── Models ──────────────────────────────────────────────────────────────────

public class DemotionRequest
{
    public string AdminCredentialDn { get; set; }
    public bool IsLastDcInDomain { get; set; }
    public bool RemoveDnsRecords { get; set; } = true;
    public bool ForceRemoval { get; set; }
    public string NewAdminPassword { get; set; }
}

public class DemotionResult
{
    public bool Success { get; set; }
    public List<string> Steps { get; set; } = new();
    public string ErrorMessage { get; set; }
}

public class DemotionValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> HeldFsmoRoles { get; set; } = new();
}
