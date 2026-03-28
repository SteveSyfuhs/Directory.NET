using Directory.Core.Interfaces;
using Directory.Core.Models;
using Microsoft.Extensions.Logging;

namespace Directory.Replication.Drsr;

/// <summary>
/// The five FSMO (Flexible Single-Master Operations) roles in Active Directory.
/// Only one DC holds each role at any time. Per [MS-ADTS] section 3.1.1.1.11.
/// </summary>
public enum FsmoRole
{
    /// <summary>
    /// PDC Emulator — handles password changes, time sync, GPO editing, account lockout.
    /// Stored on the domain NC head object's fSMORoleOwner attribute.
    /// </summary>
    PdcEmulator,

    /// <summary>
    /// RID Master — allocates RID pools to DCs for SID generation.
    /// Stored on the RID Manager object's fSMORoleOwner attribute.
    /// </summary>
    RidMaster,

    /// <summary>
    /// Infrastructure Master — updates cross-domain group-to-user references.
    /// Stored on the Infrastructure object's fSMORoleOwner attribute.
    /// </summary>
    InfrastructureMaster,

    /// <summary>
    /// Schema Master — controls schema modifications (forest-wide, one per forest).
    /// Stored on the Schema NC head's fSMORoleOwner attribute.
    /// </summary>
    SchemaMaster,

    /// <summary>
    /// Domain Naming Master — controls addition/removal of domains (forest-wide).
    /// Stored on the Partitions container's fSMORoleOwner attribute.
    /// </summary>
    DomainNamingMaster,
}

/// <summary>
/// Manages FSMO role ownership, transfer, and seizure per [MS-DRSR] and [MS-ADTS].
///
/// Each FSMO role is tracked by the fSMORoleOwner attribute on a specific
/// well-known directory object. The attribute value is the DN of the NTDS Settings
/// object of the DC that currently holds the role.
/// </summary>
public class FsmoRoleManager
{
    private readonly IDirectoryStore _store;
    private readonly DcInstanceInfo _dcInfo;
    private readonly ILogger<FsmoRoleManager> _logger;

    public FsmoRoleManager(
        IDirectoryStore store,
        DcInstanceInfo dcInfo,
        ILogger<FsmoRoleManager> logger)
    {
        _store = store;
        _dcInfo = dcInfo;
        _logger = logger;
    }

    /// <summary>
    /// Gets the DN of the DC currently holding the specified FSMO role.
    /// Returns the NTDS Settings DN of the role holder, or null if not assigned.
    /// </summary>
    public async Task<string> GetRoleHolderAsync(
        FsmoRole role,
        string tenantId,
        string domainDn,
        CancellationToken ct = default)
    {
        var roleObjectDn = GetRoleObjectDn(role, domainDn);
        var obj = await _store.GetByDnAsync(tenantId, roleObjectDn, ct);

        if (obj == null)
        {
            _logger.LogWarning("FSMO role object not found: {DN}", roleObjectDn);
            return null;
        }

        var attr = obj.GetAttribute("fSMORoleOwner");
        return attr?.GetFirstString();
    }

    /// <summary>
    /// Checks whether this DC instance currently holds the specified FSMO role.
    /// </summary>
    public async Task<bool> IsFsmoRoleHolderAsync(
        FsmoRole role,
        string tenantId,
        string domainDn,
        CancellationToken ct = default)
    {
        var holder = await GetRoleHolderAsync(role, tenantId, domainDn, ct);
        if (holder == null)
            return false;

        var myNtdsDn = _dcInfo.NtdsSettingsDn(domainDn);
        return string.Equals(holder, myNtdsDn, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gracefully transfers a FSMO role to a new holder.
    /// This is a cooperative operation that requires the current holder to be reachable.
    ///
    /// Per [MS-DRSR] section 4.1.10.4:
    /// 1. The requesting DC contacts the current role holder.
    /// 2. The current holder updates fSMORoleOwner to point to the new holder.
    /// 3. The change replicates to all DCs.
    /// </summary>
    public async Task<FsmoTransferResult> TransferRoleAsync(
        FsmoRole role,
        string newHolderNtdsDn,
        string tenantId,
        string domainDn,
        CancellationToken ct = default)
    {
        var roleObjectDn = GetRoleObjectDn(role, domainDn);
        var obj = await _store.GetByDnAsync(tenantId, roleObjectDn, ct);

        if (obj == null)
        {
            return new FsmoTransferResult
            {
                Success = false,
                ErrorMessage = $"FSMO role object not found: {roleObjectDn}",
            };
        }

        var currentHolder = obj.GetAttribute("fSMORoleOwner")?.GetFirstString();

        _logger.LogInformation(
            "Transferring FSMO role {Role} from {Current} to {New}",
            role, currentHolder ?? "(none)", newHolderNtdsDn);

        // Update the fSMORoleOwner attribute
        obj.SetAttribute("fSMORoleOwner",
            new DirectoryAttribute("fSMORoleOwner", newHolderNtdsDn));
        obj.WhenChanged = DateTimeOffset.UtcNow;

        var usn = await _store.GetNextUsnAsync(tenantId, domainDn, ct);
        obj.USNChanged = usn;

        await _store.UpdateAsync(obj, ct);

        _logger.LogInformation("FSMO role {Role} successfully transferred to {New}", role, newHolderNtdsDn);

        return new FsmoTransferResult
        {
            Success = true,
            PreviousHolder = currentHolder,
            NewHolder = newHolderNtdsDn,
        };
    }

    /// <summary>
    /// Forcefully seizes a FSMO role when the current holder is unreachable.
    /// This is a destructive operation that should only be used in disaster recovery.
    ///
    /// Per [MS-ADTS] section 3.1.1.1.11:
    /// The seizure overwrites fSMORoleOwner locally. The old holder must be
    /// decommissioned before it comes back online to prevent split-brain.
    /// </summary>
    public async Task<FsmoTransferResult> SeizeRoleAsync(
        FsmoRole role,
        string tenantId,
        string domainDn,
        CancellationToken ct = default)
    {
        var myNtdsDn = _dcInfo.NtdsSettingsDn(domainDn);
        var roleObjectDn = GetRoleObjectDn(role, domainDn);
        var obj = await _store.GetByDnAsync(tenantId, roleObjectDn, ct);

        if (obj == null)
        {
            // Create the role object if it doesn't exist
            _logger.LogWarning("FSMO role object missing during seizure, creating: {DN}", roleObjectDn);

            obj = new DirectoryObject
            {
                Id = roleObjectDn.ToLowerInvariant(),
                TenantId = tenantId,
                DomainDn = domainDn,
                DistinguishedName = roleObjectDn,
                ObjectClass = ["top", "infrastructureUpdate"],
                WhenCreated = DateTimeOffset.UtcNow,
                WhenChanged = DateTimeOffset.UtcNow,
            };

            obj.SetAttribute("fSMORoleOwner", new DirectoryAttribute("fSMORoleOwner", myNtdsDn));

            var createUsn = await _store.GetNextUsnAsync(tenantId, domainDn, ct);
            obj.USNCreated = createUsn;
            obj.USNChanged = createUsn;

            await _store.CreateAsync(obj, ct);
        }
        else
        {
            var currentHolder = obj.GetAttribute("fSMORoleOwner")?.GetFirstString();

            _logger.LogWarning(
                "SEIZING FSMO role {Role} from {Current} to this DC ({MyDn}). " +
                "The previous holder MUST be decommissioned!",
                role, currentHolder ?? "(none)", myNtdsDn);

            obj.SetAttribute("fSMORoleOwner", new DirectoryAttribute("fSMORoleOwner", myNtdsDn));
            obj.WhenChanged = DateTimeOffset.UtcNow;

            var usn = await _store.GetNextUsnAsync(tenantId, domainDn, ct);
            obj.USNChanged = usn;

            await _store.UpdateAsync(obj, ct);
        }

        return new FsmoTransferResult
        {
            Success = true,
            NewHolder = myNtdsDn,
            WasSeized = true,
        };
    }

    /// <summary>
    /// Returns the DN of the directory object that stores the fSMORoleOwner
    /// attribute for each FSMO role.
    /// </summary>
    public static string GetRoleObjectDn(FsmoRole role, string domainDn)
    {
        return role switch
        {
            // PDC Emulator: stored on the domain NC head
            FsmoRole.PdcEmulator => domainDn,

            // RID Master: stored on CN=RID Manager$,CN=System,<domainDn>
            FsmoRole.RidMaster => $"CN=RID Manager$,CN=System,{domainDn}",

            // Infrastructure Master: stored on CN=Infrastructure,<domainDn>
            FsmoRole.InfrastructureMaster => $"CN=Infrastructure,{domainDn}",

            // Schema Master: stored on CN=Schema,CN=Configuration,<domainDn>
            FsmoRole.SchemaMaster => $"CN=Schema,CN=Configuration,{domainDn}",

            // Domain Naming Master: stored on CN=Partitions,CN=Configuration,<domainDn>
            FsmoRole.DomainNamingMaster => $"CN=Partitions,CN=Configuration,{domainDn}",

            _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown FSMO role"),
        };
    }
}

/// <summary>
/// Result of a FSMO role transfer or seizure operation.
/// </summary>
public class FsmoTransferResult
{
    public bool Success { get; init; }
    public string ErrorMessage { get; init; }
    public string PreviousHolder { get; init; }
    public string NewHolder { get; init; }
    public bool WasSeized { get; init; }
}
