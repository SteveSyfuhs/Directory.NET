using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Web.Models;

using SearchScope = Directory.Core.Models.SearchScope;

namespace Directory.Web.Services;

/// <summary>
/// Manages FSMO (Flexible Single Master Operations) roles for the domain and forest.
/// Supports querying current holders, graceful transfer, and forced seizure.
/// </summary>
public class FsmoRoleService
{
    private readonly IDirectoryStore _store;
    private readonly DcInstanceInfo _dcInfo;
    private readonly DomainConfiguration _domainConfig;
    private readonly ILogger<FsmoRoleService> _logger;

    private const string TenantId = "default";

    public FsmoRoleService(
        IDirectoryStore store,
        DcInstanceInfo dcInfo,
        DomainConfiguration domainConfig,
        ILogger<FsmoRoleService> logger)
    {
        _store = store;
        _dcInfo = dcInfo;
        _domainConfig = domainConfig;
        _logger = logger;
    }

    /// <summary>
    /// Returns the current holder of each FSMO role by reading the fSMORoleOwner attribute
    /// on the well-known objects that hold each role.
    /// </summary>
    public async Task<List<FsmoRoleInfo>> GetAllRoleHoldersAsync(CancellationToken ct = default)
    {
        var domainDn = _domainConfig.DomainDn;
        var configDn = $"CN=Configuration,{domainDn}";
        var schemaDn = $"CN=Schema,{configDn}";

        var results = new List<FsmoRoleInfo>();

        // Schema Master — stored on CN=Schema,CN=Configuration,{domainDn}
        results.Add(await GetRoleInfoAsync(
            FsmoRole.SchemaMaster, schemaDn,
            "Forest", "Controls schema modifications across the entire forest.", ct));

        // Domain Naming Master — stored on CN=Partitions,CN=Configuration,{domainDn}
        results.Add(await GetRoleInfoAsync(
            FsmoRole.DomainNamingMaster, $"CN=Partitions,{configDn}",
            "Forest", "Controls addition/removal of domains in the forest.", ct));

        // PDC Emulator — stored on the domain root object
        results.Add(await GetRoleInfoAsync(
            FsmoRole.PdcEmulator, domainDn,
            "Domain", "Primary DC for password changes, time sync, and legacy client support.", ct));

        // RID Master — stored on CN=RID Manager$,CN=System,{domainDn}
        results.Add(await GetRoleInfoAsync(
            FsmoRole.RidMaster, $"CN=RID Manager$,CN=System,{domainDn}",
            "Domain", "Allocates RID pools for SID generation on all DCs.", ct));

        // Infrastructure Master — stored on CN=Infrastructure,{domainDn}
        results.Add(await GetRoleInfoAsync(
            FsmoRole.InfrastructureMaster, $"CN=Infrastructure,{domainDn}",
            "Domain", "Maintains cross-domain object references and phantom updates.", ct));

        return results;
    }

    /// <summary>
    /// Gracefully transfers a FSMO role to the specified target DC.
    /// Updates the fSMORoleOwner attribute on the role-bearing object.
    /// </summary>
    public async Task<FsmoRoleTransferResult> TransferRoleAsync(
        FsmoRole role, string targetNtdsSettingsDn, CancellationToken ct = default)
    {
        try
        {
            var roleObjectDn = GetRoleObjectDn(role);
            var roleObject = await _store.GetByDnAsync(TenantId, roleObjectDn, ct);

            if (roleObject == null)
            {
                return new FsmoRoleTransferResult
                {
                    Success = false,
                    ErrorMessage = $"Role object not found at {roleObjectDn}. Domain may not be fully provisioned.",
                };
            }

            // Verify target DC exists
            var targetNtds = await _store.GetByDnAsync(TenantId, targetNtdsSettingsDn, ct);
            if (targetNtds == null)
            {
                return new FsmoRoleTransferResult
                {
                    Success = false,
                    ErrorMessage = $"Target NTDS Settings object not found: {targetNtdsSettingsDn}",
                };
            }

            var currentHolder = roleObject.GetAttribute("fSMORoleOwner")?.GetFirstString();
            if (currentHolder?.Equals(targetNtdsSettingsDn, StringComparison.OrdinalIgnoreCase) == true)
            {
                return new FsmoRoleTransferResult
                {
                    Success = true,
                    PreviousHolder = currentHolder,
                    NewHolder = targetNtdsSettingsDn,
                    ErrorMessage = "Target DC already holds this role.",
                };
            }

            // Perform the transfer by updating fSMORoleOwner
            roleObject.SetAttribute("fSMORoleOwner",
                new DirectoryAttribute("fSMORoleOwner", targetNtdsSettingsDn));
            roleObject.WhenChanged = DateTimeOffset.UtcNow;
            await _store.UpdateAsync(roleObject, ct);

            _logger.LogInformation("FSMO role {Role} transferred from {From} to {To}",
                role, currentHolder, targetNtdsSettingsDn);

            return new FsmoRoleTransferResult
            {
                Success = true,
                PreviousHolder = currentHolder ?? "(none)",
                NewHolder = targetNtdsSettingsDn,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to transfer FSMO role {Role}", role);
            return new FsmoRoleTransferResult
            {
                Success = false,
                ErrorMessage = ex.Message,
            };
        }
    }

    /// <summary>
    /// Forcibly seizes a FSMO role when the current holder is offline or unavailable.
    /// This is identical to transfer but skips communication with the current holder.
    /// </summary>
    public async Task<FsmoRoleTransferResult> SeizeRoleAsync(
        FsmoRole role, string targetNtdsSettingsDn, CancellationToken ct = default)
    {
        _logger.LogWarning("Seizing FSMO role {Role} — current holder may be offline", role);
        // Seizure is the same write operation; the difference in real AD is that
        // transfer communicates with the current holder while seize does not.
        return await TransferRoleAsync(role, targetNtdsSettingsDn, ct);
    }

    /// <summary>
    /// Lists all DCs in the domain (their NTDS Settings DNs) for use in transfer target selection.
    /// </summary>
    public async Task<List<DcInfo>> GetAllDcsAsync(CancellationToken ct = default)
    {
        var domainDn = _domainConfig.DomainDn;
        var configDn = $"CN=Configuration,{domainDn}";
        var sitesDn = $"CN=Sites,{configDn}";

        var searchResult = await _store.SearchAsync(TenantId, sitesDn,
            SearchScope.WholeSubtree,
            new EqualityFilterNode("objectClass", "nTDSDSA"),
            null, ct: ct);

        return searchResult.Entries.Select(o =>
        {
            // NTDS Settings DN: CN=NTDS Settings,CN={hostname},CN=Servers,CN={site},CN=Sites,CN=Configuration,...
            var parts = o.DistinguishedName.Split(',');
            var serverName = parts.Length > 1 ? parts[1].Replace("CN=", "") : "Unknown";
            var siteName = parts.Length > 3 ? parts[3].Replace("CN=", "") : "Unknown";

            return new DcInfo
            {
                NtdsSettingsDn = o.DistinguishedName,
                ServerName = serverName,
                SiteName = siteName,
                IsCurrentDc = o.DistinguishedName.Equals(
                    _dcInfo.NtdsSettingsDn(domainDn), StringComparison.OrdinalIgnoreCase),
            };
        }).ToList();
    }

    // ── Private helpers ─────────────────────────────────────────────────────

    private async Task<FsmoRoleInfo> GetRoleInfoAsync(
        FsmoRole role, string objectDn, string scope, string description, CancellationToken ct)
    {
        var info = new FsmoRoleInfo
        {
            Role = role,
            Scope = scope,
            Description = description,
            HolderDn = "(unknown)",
            HolderServerName = "(unknown)",
        };

        try
        {
            var obj = await _store.GetByDnAsync(TenantId, objectDn, ct);
            if (obj != null)
            {
                var holder = obj.GetAttribute("fSMORoleOwner")?.GetFirstString();
                if (!string.IsNullOrEmpty(holder))
                {
                    info.HolderDn = holder;
                    // Extract server name from NTDS Settings DN
                    var parts = holder.Split(',');
                    info.HolderServerName = parts.Length > 1 ? parts[1].Replace("CN=", "") : holder;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read FSMO role holder for {Role} at {Dn}", role, objectDn);
        }

        return info;
    }

    private string GetRoleObjectDn(FsmoRole role)
    {
        var domainDn = _domainConfig.DomainDn;
        var configDn = $"CN=Configuration,{domainDn}";

        return role switch
        {
            FsmoRole.SchemaMaster => $"CN=Schema,{configDn}",
            FsmoRole.DomainNamingMaster => $"CN=Partitions,{configDn}",
            FsmoRole.PdcEmulator => domainDn,
            FsmoRole.RidMaster => $"CN=RID Manager$,CN=System,{domainDn}",
            FsmoRole.InfrastructureMaster => $"CN=Infrastructure,{domainDn}",
            _ => throw new ArgumentOutOfRangeException(nameof(role)),
        };
    }
}

// ── Models ──────────────────────────────────────────────────────────────────

public enum FsmoRole
{
    SchemaMaster,
    DomainNamingMaster,
    PdcEmulator,
    RidMaster,
    InfrastructureMaster,
}

public class FsmoRoleInfo
{
    public FsmoRole Role { get; set; }
    public string HolderDn { get; set; } = "";
    public string HolderServerName { get; set; } = "";
    public string Scope { get; set; } = "";
    public string Description { get; set; } = "";
}

public class FsmoRoleTransferResult
{
    public bool Success { get; set; }
    public string PreviousHolder { get; set; }
    public string NewHolder { get; set; }
    public string ErrorMessage { get; set; }
}

public class DcInfo
{
    public string NtdsSettingsDn { get; set; } = "";
    public string ServerName { get; set; } = "";
    public string SiteName { get; set; } = "";
    public bool IsCurrentDc { get; set; }
}
