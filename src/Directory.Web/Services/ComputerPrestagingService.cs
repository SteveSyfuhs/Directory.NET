using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Web.Models;

namespace Directory.Web.Services;

public class PrestagingRequest
{
    public string ComputerName { get; set; } = "";
    public string DnsHostName { get; set; }
    public string OrganizationalUnit { get; set; }  // Target OU DN
    public string ManagedBy { get; set; }           // DN of user/group managing this computer
    public string Description { get; set; }
    public string OperatingSystem { get; set; }
    public List<string> AllowedToJoin { get; set; } // DNs of users allowed to join this specific machine
}

public class PrestagingResult
{
    public bool Success { get; set; }
    public string ComputerDn { get; set; } = "";
    public string ComputerSid { get; set; } = "";
    public string SamAccountName { get; set; } = "";
    public string ErrorMessage { get; set; }
}

public class ComputerPrestagingService
{
    private readonly IDirectoryStore _store;
    private readonly IRidAllocator _ridAllocator;
    private readonly INamingContextService _ncService;
    private readonly IPasswordPolicy _passwordPolicy;
    private readonly ILogger<ComputerPrestagingService> _logger;

    public ComputerPrestagingService(
        IDirectoryStore store,
        IRidAllocator ridAllocator,
        INamingContextService ncService,
        IPasswordPolicy passwordPolicy,
        ILogger<ComputerPrestagingService> logger)
    {
        _store = store;
        _ridAllocator = ridAllocator;
        _ncService = ncService;
        _passwordPolicy = passwordPolicy;
        _logger = logger;
    }

    /// <summary>
    /// Pre-stage a single computer account. Creates a disabled computer with
    /// WORKSTATION_TRUST_ACCOUNT, sets managedBy and ms-DS-CreatorSID for join authorization.
    /// </summary>
    public async Task<PrestagingResult> PrestageComputerAsync(PrestagingRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.ComputerName))
            return new PrestagingResult { Success = false, ErrorMessage = "Computer name is required" };

        var domainDn = _ncService.GetDomainNc().Dn;

        // Determine target container — default to CN=Computers,<domainDn>
        var containerDn = !string.IsNullOrWhiteSpace(request.OrganizationalUnit)
            ? request.OrganizationalUnit
            : $"CN=Computers,{domainDn}";

        // Normalize name: strip trailing $ if provided
        var computerName = request.ComputerName.TrimEnd('$');
        var samAccountName = computerName + "$";
        var dn = $"CN={computerName},{containerDn}";

        // Check for existing account
        var existing = await _store.GetByDnAsync("default", dn, ct);
        if (existing != null && !existing.IsDeleted)
            return new PrestagingResult { Success = false, ErrorMessage = $"Computer account already exists at {dn}" };

        var objectSid = await _ridAllocator.GenerateObjectSidAsync("default", domainDn);
        var now = DateTimeOffset.UtcNow;
        var usn = await _store.GetNextUsnAsync("default", domainDn, ct);

        var obj = new DirectoryObject
        {
            Id = dn.ToLowerInvariant(),
            TenantId = "default",
            DistinguishedName = dn,
            DomainDn = domainDn,
            ObjectCategory = "computer",
            ObjectClass = ["top", "person", "organizationalPerson", "user", "computer"],
            Cn = computerName,
            SAMAccountName = samAccountName,
            DisplayName = computerName,
            Description = request.Description,
            DnsHostName = request.DnsHostName,
            ParentDn = containerDn,
            ObjectGuid = Guid.NewGuid().ToString(),
            ObjectSid = objectSid,
            // WORKSTATION_TRUST_ACCOUNT (0x1000) | ACCOUNTDISABLE (0x2) — pre-staged = disabled
            UserAccountControl = 0x1000 | 0x2,
            PrimaryGroupId = 515, // Domain Computers
            WhenCreated = now,
            WhenChanged = now,
            USNCreated = usn,
            USNChanged = usn,
            SAMAccountType = 0x30000001, // SAM_MACHINE_ACCOUNT
        };

        // Set managedBy
        if (!string.IsNullOrWhiteSpace(request.ManagedBy))
        {
            obj.SetAttribute("managedBy",
                new DirectoryAttribute("managedBy", request.ManagedBy));
        }

        // Set ms-DS-CreatorSID — used for join authorization
        if (!string.IsNullOrWhiteSpace(request.ManagedBy))
        {
            obj.SetAttribute("ms-DS-CreatorSID",
                new DirectoryAttribute("ms-DS-CreatorSID", request.ManagedBy));
        }

        // Set operating system if specified
        if (!string.IsNullOrWhiteSpace(request.OperatingSystem))
        {
            obj.SetAttribute("operatingSystem",
                new DirectoryAttribute("operatingSystem", request.OperatingSystem));
        }

        // Store allowed-to-join list as ms-DS-AllowedToActOnBehalfOfOtherIdentity (multi-value)
        if (request.AllowedToJoin is { Count: > 0 })
        {
            obj.SetAttribute("ms-DS-AllowedToJoin",
                new DirectoryAttribute("ms-DS-AllowedToJoin", [.. request.AllowedToJoin.Cast<object>()]));
        }

        // Set a random initial password (machine will reset on first join)
        var randomPassword = Guid.NewGuid().ToString("N") + "Aa1!";
        await _store.CreateAsync(obj, ct);
        await _passwordPolicy.SetPasswordAsync("default", dn, randomPassword);

        _logger.LogInformation("Pre-staged computer account {ComputerName} at {Dn}", computerName, dn);

        return new PrestagingResult
        {
            Success = true,
            ComputerDn = dn,
            ComputerSid = objectSid,
            SamAccountName = samAccountName,
        };
    }

    /// <summary>
    /// Bulk pre-stage multiple computer accounts. Returns per-item results.
    /// </summary>
    public async Task<List<PrestagingResult>> BulkPrestageAsync(List<PrestagingRequest> requests, CancellationToken ct = default)
    {
        var results = new List<PrestagingResult>(requests.Count);

        foreach (var request in requests)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var result = await PrestageComputerAsync(request, ct);
                results.Add(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to pre-stage computer {ComputerName}", request.ComputerName);
                results.Add(new PrestagingResult
                {
                    Success = false,
                    SamAccountName = request.ComputerName + "$",
                    ErrorMessage = ex.Message,
                });
            }
        }

        return results;
    }

    /// <summary>
    /// List pre-staged (disabled) computer accounts.
    /// </summary>
    public async Task<List<PrestagedComputerSummary>> GetPrestagedComputersAsync(CancellationToken ct = default)
    {
        var domainDn = _ncService.GetDomainNc().Dn;
        var filter = new EqualityFilterNode("objectClass", "computer");

        var result = await _store.SearchAsync(
            "default", domainDn, SearchScope.WholeSubtree, filter,
            null, pageSize: 1000, ct: ct);

        return result.Entries
            .Where(e => !e.IsDeleted && (e.UserAccountControl & 0x2) != 0) // disabled only
            .Select(e =>
            {
                var managedBy = e.GetAttribute("managedBy")?.GetFirstString();
                return new PrestagedComputerSummary
                {
                    ObjectGuid = e.ObjectGuid,
                    Name = e.Cn ?? e.SAMAccountName ?? "",
                    Dn = e.DistinguishedName,
                    SamAccountName = e.SAMAccountName ?? "",
                    ObjectSid = e.ObjectSid ?? "",
                    ManagedBy = managedBy,
                    Description = e.Description,
                    WhenCreated = e.WhenCreated,
                    Enabled = false,
                };
            })
            .ToList();
    }

    /// <summary>
    /// Delete a pre-staged computer account that has never been joined (still disabled).
    /// </summary>
    public async Task<bool> DeletePrestagedComputerAsync(string computerName, CancellationToken ct = default)
    {
        var domainDn = _ncService.GetDomainNc().Dn;
        var name = computerName.TrimEnd('$');

        // Search for the computer by sAMAccountName
        var filter = new EqualityFilterNode("sAMAccountName", name + "$");
        var result = await _store.SearchAsync("default", domainDn, SearchScope.WholeSubtree, filter,
            null, pageSize: 1, ct: ct);

        var obj = result.Entries.FirstOrDefault(e => !e.IsDeleted);
        if (obj == null)
            return false;

        // Only allow deletion of disabled (pre-staged, never joined) accounts
        if ((obj.UserAccountControl & 0x2) == 0)
            return false;

        await _store.DeleteAsync("default", obj.DistinguishedName, hardDelete: true, ct);
        _logger.LogInformation("Deleted pre-staged computer {ComputerName}", name);

        return true;
    }
}

public class PrestagedComputerSummary
{
    public string ObjectGuid { get; init; } = "";
    public string Name { get; init; } = "";
    public string Dn { get; init; } = "";
    public string SamAccountName { get; init; } = "";
    public string ObjectSid { get; init; } = "";
    public string ManagedBy { get; init; }
    public string Description { get; init; }
    public DateTimeOffset WhenCreated { get; init; }
    public bool Enabled { get; init; }
}
