using System.Collections.Concurrent;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Rpc.Dispatch;
using Directory.Rpc.Ndr;
using Microsoft.Extensions.Logging;

namespace Directory.Rpc.Lsa;

/// <summary>
/// Implements MS-LSAD / MS-LSAT operations for Local Security Authority RPC interface.
/// Provides SID-to-name translation, domain policy queries, and trusted domain enumeration
/// required for Windows domain join.
/// </summary>
public class LsaOperations
{
    private readonly IDirectoryStore _store;
    private readonly IRidAllocator _ridAllocator;
    private readonly INamingContextService _ncService;
    private readonly ILogger<LsaOperations> _logger;

    // Well-known SID mappings for lookup
    private static readonly Dictionary<string, (string Name, ushort Type, string Domain)> WellKnownSids = new(StringComparer.OrdinalIgnoreCase)
    {
        ["S-1-0-0"] = ("Nobody", LsaConstants.SidTypeWellKnownGroup, ""),
        ["S-1-1-0"] = ("Everyone", LsaConstants.SidTypeWellKnownGroup, ""),
        ["S-1-2-0"] = ("LOCAL", LsaConstants.SidTypeWellKnownGroup, ""),
        ["S-1-3-0"] = ("CREATOR OWNER", LsaConstants.SidTypeWellKnownGroup, ""),
        ["S-1-3-1"] = ("CREATOR GROUP", LsaConstants.SidTypeWellKnownGroup, ""),
        ["S-1-5-1"] = ("DIALUP", LsaConstants.SidTypeWellKnownGroup, "NT AUTHORITY"),
        ["S-1-5-2"] = ("NETWORK", LsaConstants.SidTypeWellKnownGroup, "NT AUTHORITY"),
        ["S-1-5-3"] = ("BATCH", LsaConstants.SidTypeWellKnownGroup, "NT AUTHORITY"),
        ["S-1-5-4"] = ("INTERACTIVE", LsaConstants.SidTypeWellKnownGroup, "NT AUTHORITY"),
        ["S-1-5-6"] = ("SERVICE", LsaConstants.SidTypeWellKnownGroup, "NT AUTHORITY"),
        ["S-1-5-7"] = ("ANONYMOUS LOGON", LsaConstants.SidTypeWellKnownGroup, "NT AUTHORITY"),
        ["S-1-5-9"] = ("ENTERPRISE DOMAIN CONTROLLERS", LsaConstants.SidTypeWellKnownGroup, "NT AUTHORITY"),
        ["S-1-5-10"] = ("SELF", LsaConstants.SidTypeWellKnownGroup, "NT AUTHORITY"),
        ["S-1-5-11"] = ("Authenticated Users", LsaConstants.SidTypeWellKnownGroup, "NT AUTHORITY"),
        ["S-1-5-13"] = ("TERMINAL SERVER USER", LsaConstants.SidTypeWellKnownGroup, "NT AUTHORITY"),
        ["S-1-5-14"] = ("REMOTE INTERACTIVE LOGON", LsaConstants.SidTypeWellKnownGroup, "NT AUTHORITY"),
        ["S-1-5-18"] = ("SYSTEM", LsaConstants.SidTypeWellKnownGroup, "NT AUTHORITY"),
        ["S-1-5-19"] = ("LOCAL SERVICE", LsaConstants.SidTypeWellKnownGroup, "NT AUTHORITY"),
        ["S-1-5-20"] = ("NETWORK SERVICE", LsaConstants.SidTypeWellKnownGroup, "NT AUTHORITY"),
        ["S-1-5-32-544"] = ("Administrators", LsaConstants.SidTypeAlias, "BUILTIN"),
        ["S-1-5-32-545"] = ("Users", LsaConstants.SidTypeAlias, "BUILTIN"),
        ["S-1-5-32-546"] = ("Guests", LsaConstants.SidTypeAlias, "BUILTIN"),
        ["S-1-5-32-547"] = ("Power Users", LsaConstants.SidTypeAlias, "BUILTIN"),
        ["S-1-5-32-548"] = ("Account Operators", LsaConstants.SidTypeAlias, "BUILTIN"),
        ["S-1-5-32-549"] = ("Server Operators", LsaConstants.SidTypeAlias, "BUILTIN"),
        ["S-1-5-32-550"] = ("Print Operators", LsaConstants.SidTypeAlias, "BUILTIN"),
        ["S-1-5-32-551"] = ("Backup Operators", LsaConstants.SidTypeAlias, "BUILTIN"),
        ["S-1-5-32-552"] = ("Replicators", LsaConstants.SidTypeAlias, "BUILTIN"),
    };

    // Standard Windows privileges: (Name, DisplayName, LUID high, LUID low)
    private static readonly (string Name, string DisplayName, uint LuidHigh, uint LuidLow)[] StandardPrivileges =
    {
        ("SeAssignPrimaryTokenPrivilege", "Replace a process level token", 0, 1),
        ("SeAuditPrivilege", "Generate security audits", 0, 2),
        ("SeBackupPrivilege", "Back up files and directories", 0, 3),
        ("SeChangeNotifyPrivilege", "Bypass traverse checking", 0, 4),
        ("SeCreateGlobalPrivilege", "Create global objects", 0, 5),
        ("SeCreatePagefilePrivilege", "Create a pagefile", 0, 6),
        ("SeCreatePermanentPrivilege", "Create permanent shared objects", 0, 7),
        ("SeCreateSymbolicLinkPrivilege", "Create symbolic links", 0, 8),
        ("SeCreateTokenPrivilege", "Create a token object", 0, 9),
        ("SeDebugPrivilege", "Debug programs", 0, 10),
        ("SeEnableDelegationPrivilege", "Enable computer and user accounts to be trusted for delegation", 0, 11),
        ("SeImpersonatePrivilege", "Impersonate a client after authentication", 0, 12),
        ("SeIncreaseBasePriorityPrivilege", "Increase scheduling priority", 0, 13),
        ("SeIncreaseQuotaPrivilege", "Adjust memory quotas for a process", 0, 14),
        ("SeIncreaseWorkingSetPrivilege", "Increase a process working set", 0, 15),
        ("SeLoadDriverPrivilege", "Load and unload device drivers", 0, 16),
        ("SeLockMemoryPrivilege", "Lock pages in memory", 0, 17),
        ("SeMachineAccountPrivilege", "Add workstations to domain", 0, 18),
        ("SeManageVolumePrivilege", "Perform volume maintenance tasks", 0, 19),
        ("SeProfileSingleProcessPrivilege", "Profile single process", 0, 20),
        ("SeRelabelPrivilege", "Modify an object label", 0, 21),
        ("SeRemoteShutdownPrivilege", "Force shutdown from a remote system", 0, 22),
        ("SeRestorePrivilege", "Restore files and directories", 0, 23),
        ("SeSecurityPrivilege", "Manage auditing and security log", 0, 24),
        ("SeShutdownPrivilege", "Shut down the system", 0, 25),
        ("SeSyncAgentPrivilege", "Synchronize directory service data", 0, 26),
        ("SeSystemEnvironmentPrivilege", "Modify firmware environment values", 0, 27),
        ("SeSystemProfilePrivilege", "Profile system performance", 0, 28),
        ("SeSystemtimePrivilege", "Change the system time", 0, 29),
        ("SeTakeOwnershipPrivilege", "Take ownership of files or other objects", 0, 30),
        ("SeTcbPrivilege", "Act as part of the operating system", 0, 31),
        ("SeTimeZonePrivilege", "Change the time zone", 0, 32),
        ("SeTrustedCredManAccessPrivilege", "Access Credential Manager as a trusted caller", 0, 33),
        ("SeUndockPrivilege", "Remove computer from docking station", 0, 34),
        ("SeUnsolicitedInputPrivilege", "Read unsolicited input from a terminal device", 0, 35),
        ("SeBatchLogonRight", "Log on as a batch job", 0, 36),
        ("SeDenyBatchLogonRight", "Deny log on as a batch job", 0, 37),
        ("SeDenyInteractiveLogonRight", "Deny log on locally", 0, 38),
        ("SeDenyNetworkLogonRight", "Deny access to this computer from the network", 0, 39),
        ("SeDenyRemoteInteractiveLogonRight", "Deny log on through Remote Desktop Services", 0, 40),
        ("SeDenyServiceLogonRight", "Deny log on as a service", 0, 41),
        ("SeInteractiveLogonRight", "Allow log on locally", 0, 42),
        ("SeNetworkLogonRight", "Access this computer from the network", 0, 43),
        ("SeRemoteInteractiveLogonRight", "Allow log on through Remote Desktop Services", 0, 44),
        ("SeServiceLogonRight", "Log on as a service", 0, 45),
    };

    // Lookup dictionaries built from StandardPrivileges
    private static readonly Dictionary<string, int> PrivilegeNameToIndex;
    private static readonly Dictionary<(uint High, uint Low), int> PrivilegeLuidToIndex;

    // In-memory account privilege store: SID string -> set of privilege indices
    private static readonly ConcurrentDictionary<string, HashSet<int>> AccountPrivileges = new(StringComparer.OrdinalIgnoreCase);

    // Account handles: GUID -> SID
    private static readonly ConcurrentDictionary<Guid, string> AccountHandles = new();

    static LsaOperations()
    {
        PrivilegeNameToIndex = new(StringComparer.OrdinalIgnoreCase);
        PrivilegeLuidToIndex = new();
        for (int i = 0; i < StandardPrivileges.Length; i++)
        {
            PrivilegeNameToIndex[StandardPrivileges[i].Name] = i;
            PrivilegeLuidToIndex[(StandardPrivileges[i].LuidHigh, StandardPrivileges[i].LuidLow)] = i;
        }
    }

    public LsaOperations(
        IDirectoryStore store,
        IRidAllocator ridAllocator,
        INamingContextService ncService,
        ILogger<LsaOperations> logger)
    {
        _store = store;
        _ridAllocator = ridAllocator;
        _ncService = ncService;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Opnum 44: LsarOpenPolicy2
    // ─────────────────────────────────────────────────────────────────────

    public Task<byte[]> LsarOpenPolicy2Async(ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // SystemName: [in, unique, string] wchar_t*
        var systemNamePtr = reader.ReadPointer();
        string systemName = "";
        if (systemNamePtr != 0)
        {
            systemName = reader.ReadConformantVaryingString();
        }

        // LSAPR_OBJECT_ATTRIBUTES: 24 bytes
        //   Length (uint32), RootDirectory (uint32 ptr), ObjectName (uint32 ptr),
        //   Attributes (uint32), SecurityDescriptor (uint32 ptr), SecurityQualityOfService (uint32 ptr)
        var objAttrLength = reader.ReadUInt32();
        var rootDirPtr = reader.ReadUInt32();
        var objectNamePtr = reader.ReadUInt32();
        var attributes = reader.ReadUInt32();
        var secDescPtr = reader.ReadUInt32();
        var secQosPtr = reader.ReadUInt32();

        // Read any deferred pointer data for object attributes (typically all null)
        if (objectNamePtr != 0)
        {
            // RPC_UNICODE_STRING body — skip
            reader.ReadConformantVaryingString();
        }
        if (secQosPtr != 0)
        {
            // SECURITY_QUALITY_OF_SERVICE: Length(uint32), ImpersonationLevel(uint16), ContextTrackingMode(byte), EffectiveOnly(byte)
            reader.ReadUInt32(); // length
            reader.ReadUInt16(); // impersonation level
            reader.ReadByte(); // context tracking mode
            reader.ReadByte(); // effective only
        }

        // DesiredAccess (uint32)
        var desiredAccess = reader.ReadUInt32();

        var handle = new LsaPolicyHandle
        {
            GrantedAccess = desiredAccess,
            SystemName = systemName,
            TenantId = context.TenantId,
            DomainDn = context.DomainDn,
        };

        var handleBytes = context.ContextHandles.CreateHandle(handle);
        var handleGuid = new Guid(handleBytes.AsSpan().Slice(4, 16));

        var writer = new NdrWriter();
        writer.WriteContextHandle(0, handleGuid);
        writer.WriteUInt32(LsaConstants.StatusSuccess);

        return Task.FromResult(writer.ToArray());
    }

    // ─────────────────────────────────────────────────────────────────────
    // Opnum 0: LsarClose
    // ─────────────────────────────────────────────────────────────────────

    public Task<byte[]> LsarCloseAsync(ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);
        var (attr, uuid) = reader.ReadContextHandle();

        // Build the 20-byte handle for lookup
        var handleBytes = new byte[20];
        BitConverter.GetBytes(attr).CopyTo(handleBytes, 0);
        uuid.TryWriteBytes(handleBytes.AsSpan(4));

        context.ContextHandles.CloseHandle(handleBytes);

        var writer = new NdrWriter();
        // Return a zeroed-out handle
        writer.WriteContextHandle(0, Guid.Empty);
        writer.WriteUInt32(LsaConstants.StatusSuccess);

        return Task.FromResult(writer.ToArray());
    }

    // ─────────────────────────────────────────────────────────────────────
    // Opnum 7/46: LsarQueryInformationPolicy / LsarQueryInformationPolicy2
    // ─────────────────────────────────────────────────────────────────────

    public Task<byte[]> LsarQueryInformationPolicyAsync(ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        return QueryInformationPolicyCore(stubData, context, ct);
    }

    public Task<byte[]> LsarQueryInformationPolicy2Async(ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        return QueryInformationPolicyCore(stubData, context, ct);
    }

    private Task<byte[]> QueryInformationPolicyCore(ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // Policy handle (20 bytes)
        var (attr, uuid) = reader.ReadContextHandle();
        var handle = GetPolicyHandle(context, attr, uuid);

        // Information class (ushort)
        var infoClass = reader.ReadUInt16();

        _logger.LogDebug("LsarQueryInformationPolicy: infoClass={InfoClass}", infoClass);

        var domainNc = _ncService.GetDomainNc();
        var domainSid = _ridAllocator.GetDomainSid(handle.TenantId, handle.DomainDn);

        var writer = new NdrWriter();

        // Pointer to the info union (non-null)
        writer.WritePointer(false);

        // Encapsulated union: switch discriminant (uint32) then arm data
        writer.WriteUInt32(infoClass);

        switch (infoClass)
        {
            case LsaConstants.PolicyDnsDomainInformation:
            case LsaConstants.PolicyDnsDomainInformationInt:
                WritePolicyDnsDomainInfo(writer, domainNc, domainSid);
                break;

            case LsaConstants.PolicyAccountDomainInformation:
                WritePolicyAccountDomainInfo(writer, domainNc, domainSid);
                break;

            case LsaConstants.PolicyPrimaryDomainInformation:
                WritePolicyPrimaryDomainInfo(writer, domainNc, domainSid);
                break;

            case LsaConstants.PolicyLsaServerRoleInformation:
                writer.WriteUInt16(LsaConstants.PolicyServerRolePrimary);
                break;

            case LsaConstants.PolicyAuditEventsInformation:
                // POLICY_AUDIT_EVENTS_INFO:
                //   AuditingMode (uint32) — 0 = disabled, 1 = enabled
                //   pointer to EventAuditingOptions array
                //   MaximumAuditEventCount (uint32)
                // We report 9 standard audit categories (AuditCategoryCount on Server 2012+)
                writer.WriteUInt32(1); // auditing enabled
                writer.WritePointer(false); // non-null event array
                writer.WriteUInt32(9); // MaximumAuditEventCount

                // Deferred: conformant array of uint32 audit options (9 entries, all POLICY_AUDIT_EVENT_NONE=0)
                writer.WriteUInt32(9); // conformant max count
                for (int auditIdx = 0; auditIdx < 9; auditIdx++)
                    writer.WriteUInt32(0); // POLICY_AUDIT_EVENT_NONE
                break;

            case LsaConstants.PolicyModificationInformation:
                // POLICY_MODIFICATION_INFO:
                //   ModifiedId (LARGE_INTEGER) — monotonically increasing modification serial
                //   DatabaseCreationTime (LARGE_INTEGER)
                writer.WriteInt64(1); // ModifiedId
                writer.WriteInt64(DateTimeOffset.UtcNow.AddYears(-1).ToFileTime()); // DatabaseCreationTime
                break;

            case LsaConstants.PolicyAuditLogInformation:
                // POLICY_AUDIT_LOG_INFO:
                //   AuditLogPercentFull (uint32), MaximumLogSize (uint32),
                //   AuditRetentionPeriod (LARGE_INTEGER),
                //   AuditLogFullShutdownInProgress (uint8), TimeToShutdown (LARGE_INTEGER),
                //   NextAuditRecordId (uint32)
                writer.WriteUInt32(0); // AuditLogPercentFull
                writer.WriteUInt32(512 * 1024); // MaximumLogSize (512 KB default)
                writer.WriteInt64(0); // AuditRetentionPeriod (0 = overwrite as needed)
                writer.WriteByte(0); // AuditLogFullShutdownInProgress
                writer.Align(8);
                writer.WriteInt64(0); // TimeToShutdown
                writer.WriteUInt32(1); // NextAuditRecordId
                break;

            default:
                _logger.LogWarning("LsarQueryInformationPolicy: unsupported infoClass {InfoClass}", infoClass);
                // Return null pointer for unsupported info classes
                var errWriter = new NdrWriter();
                errWriter.WritePointer(true); // null info
                errWriter.WriteUInt32(LsaConstants.StatusInvalidParameter);
                return Task.FromResult(errWriter.ToArray());
        }

        writer.FlushDeferred();
        writer.WriteUInt32(LsaConstants.StatusSuccess);

        return Task.FromResult(writer.ToArray());
    }

    private void WritePolicyDnsDomainInfo(NdrWriter writer, NamingContext domainNc, string domainSid)
    {
        // Extract NetBIOS name from DN (e.g., "DC=directory,DC=net" → "DIRECTORY")
        var netbiosName = ExtractNetBiosName(domainNc.Dn);
        var dnsName = domainNc.DnsName;
        var forestName = dnsName; // single-domain forest

        // Derive a deterministic domain GUID from the DN
        var domainGuid = DeterministicGuid(domainNc.Dn);

        // LSAPR_POLICY_DNS_DOMAIN_INFO:
        //   Name (RPC_UNICODE_STRING)
        //   DnsDomainName (RPC_UNICODE_STRING)
        //   DnsForestName (RPC_UNICODE_STRING)
        //   DomainGuid (16 bytes)
        //   Sid (pointer to RPC_SID)
        writer.WriteRpcUnicodeString(netbiosName);
        writer.WriteRpcUnicodeString(dnsName);
        writer.WriteRpcUnicodeString(forestName);

        // DomainGuid (16 bytes, inline, not deferred)
        Span<byte> guidBytes = stackalloc byte[16];
        domainGuid.TryWriteBytes(guidBytes);
        writer.WriteBytes(guidBytes);

        // Sid pointer (non-null)
        writer.WritePointer(false);

        // Flush deferred strings first
        writer.FlushDeferred();

        // Write the SID body (deferred from the pointer)
        writer.WriteRpcSid(domainSid);
    }

    private void WritePolicyAccountDomainInfo(NdrWriter writer, NamingContext domainNc, string domainSid)
    {
        var netbiosName = ExtractNetBiosName(domainNc.Dn);

        // LSAPR_POLICY_ACCOUNT_DOM_INFO:
        //   DomainName (RPC_UNICODE_STRING)
        //   DomainSid (pointer to RPC_SID)
        writer.WriteRpcUnicodeString(netbiosName);
        writer.WritePointer(false); // Sid pointer

        writer.FlushDeferred();
        writer.WriteRpcSid(domainSid);
    }

    private void WritePolicyPrimaryDomainInfo(NdrWriter writer, NamingContext domainNc, string domainSid)
    {
        // For a DC, primary domain info is the same as account domain info
        WritePolicyAccountDomainInfo(writer, domainNc, domainSid);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Opnum 8/47: LsarSetInformationPolicy / LsarSetInformationPolicy2
    // ─────────────────────────────────────────────────────────────────────

    public Task<byte[]> LsarSetInformationPolicyAsync(ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        return SetInformationPolicyCore(stubData, context, ct);
    }

    public Task<byte[]> LsarSetInformationPolicy2Async(ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        return SetInformationPolicyCore(stubData, context, ct);
    }

    private Task<byte[]> SetInformationPolicyCore(ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // Policy handle (20 bytes)
        var (attr, uuid) = reader.ReadContextHandle();
        var handle = GetPolicyHandle(context, attr, uuid);

        // Information class (ushort)
        var infoClass = reader.ReadUInt16();

        _logger.LogDebug("LsarSetInformationPolicy: infoClass={InfoClass}", infoClass);

        // For most policy information classes, we accept the update and return success.
        // In a production implementation, these would be persisted to the policy store.
        switch (infoClass)
        {
            case LsaConstants.PolicyAuditEventsInformation:
                // POLICY_AUDIT_EVENTS_INFO:
                //   AuditingMode (uint32) + pointer to array + MaxCount
                _logger.LogInformation("LsarSetInformationPolicy: audit events configuration updated");
                break;

            case LsaConstants.PolicyPrimaryDomainInformation:
                // LSAPR_POLICY_PRIMARY_DOM_INFO: Name + Sid
                _logger.LogInformation("LsarSetInformationPolicy: primary domain info update accepted");
                break;

            case LsaConstants.PolicyAccountDomainInformation:
                // LSAPR_POLICY_ACCOUNT_DOM_INFO: DomainName + DomainSid
                _logger.LogInformation("LsarSetInformationPolicy: account domain info update accepted");
                break;

            case LsaConstants.PolicyDnsDomainInformation:
            case LsaConstants.PolicyDnsDomainInformationInt:
                // LSAPR_POLICY_DNS_DOMAIN_INFO
                _logger.LogInformation("LsarSetInformationPolicy: DNS domain info update accepted");
                break;

            case LsaConstants.PolicyLsaServerRoleInformation:
                // POLICY_LSA_SERVER_ROLE_INFO: ServerRole (ushort)
                _logger.LogInformation("LsarSetInformationPolicy: server role update accepted");
                break;

            case LsaConstants.PolicyModificationInformation:
                // POLICY_MODIFICATION_INFO: ModifiedId + DatabaseCreationTime
                _logger.LogInformation("LsarSetInformationPolicy: modification info update accepted");
                break;

            default:
                _logger.LogWarning("LsarSetInformationPolicy: unsupported infoClass {InfoClass}", infoClass);
                var errWriter = new NdrWriter();
                errWriter.WriteUInt32(LsaConstants.StatusInvalidParameter);
                return Task.FromResult(errWriter.ToArray());
        }

        var writer = new NdrWriter();
        writer.WriteUInt32(LsaConstants.StatusSuccess);
        return Task.FromResult(writer.ToArray());
    }

    // ─────────────────────────────────────────────────────────────────────
    // Opnum 14/58/68: LsarLookupNames / LsarLookupNames2 / LsarLookupNames3
    // ─────────────────────────────────────────────────────────────────────

    public Task<byte[]> LsarLookupNamesAsync(ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        return LookupNamesCore(stubData, context, LookupNamesVersion.V1, ct);
    }

    public Task<byte[]> LsarLookupNames2Async(ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        return LookupNamesCore(stubData, context, LookupNamesVersion.V2, ct);
    }

    public Task<byte[]> LsarLookupNames3Async(ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        return LookupNamesCore(stubData, context, LookupNamesVersion.V3, ct);
    }

    private async Task<byte[]> LookupNamesCore(ReadOnlyMemory<byte> stubData, RpcCallContext context, LookupNamesVersion version, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // Policy handle
        var (attr, uuid) = reader.ReadContextHandle();
        var handle = GetPolicyHandle(context, attr, uuid);

        // Count of names
        var count = reader.ReadUInt32();

        _logger.LogDebug("LsarLookupNames{Version}: count={Count}", version, count);

        // Array of RPC_UNICODE_STRING (headers)
        // Conformant array: MaxCount first
        var maxCount = reader.ReadUInt32();

        var nameHeaders = new (ushort Length, ushort MaxLength, uint ReferentId)[count];
        for (int i = 0; i < count; i++)
        {
            nameHeaders[i] = reader.ReadRpcUnicodeString();
        }

        // Read deferred string bodies
        var names = new string[count];
        for (int i = 0; i < count; i++)
        {
            if (nameHeaders[i].ReferentId != 0)
            {
                names[i] = reader.ReadConformantVaryingString();
            }
            else
            {
                names[i] = "";
            }
        }

        // Skip remaining input parameters:
        // TranslatedSids (output-only, but present on wire as empty struct)
        // LookupLevel (ushort), MappedCount (pointer + uint32)
        // We've read what we need; ignore the rest.

        var domainNc = _ncService.GetDomainNc();
        var domainSid = _ridAllocator.GetDomainSid(handle.TenantId, handle.DomainDn);
        var netbiosName = ExtractNetBiosName(domainNc.Dn);

        // Resolve each name
        var results = new List<(ushort Use, string Sid, int DomainIndex, uint Flags)>();
        var referencedDomains = new List<(string Name, string Sid)>();
        int localDomainIndex = -1;
        uint mappedCount = 0;

        for (int i = 0; i < count; i++)
        {
            var name = names[i];
            // Strip domain prefix if present (e.g., "DOMAIN\user" → "user")
            var lookupName = name;
            if (name.Contains('\\'))
            {
                lookupName = name.Split('\\', 2)[1];
            }

            var obj = await _store.GetBySamAccountNameAsync(handle.TenantId, handle.DomainDn, lookupName, ct);

            if (obj != null && obj.ObjectSid != null)
            {
                if (localDomainIndex < 0)
                {
                    localDomainIndex = referencedDomains.Count;
                    referencedDomains.Add((netbiosName, domainSid));
                }

                var sidType = ClassifyObjectSidType(obj);
                results.Add((sidType, obj.ObjectSid, localDomainIndex, 0));
                mappedCount++;
            }
            else
            {
                results.Add((LsaConstants.SidTypeUnknown, null, -1, 0));
            }
        }

        // Determine return status
        uint status;
        if (mappedCount == count && count > 0)
            status = LsaConstants.StatusSuccess;
        else if (mappedCount > 0)
            status = LsaConstants.StatusSomeMapped;
        else if (count > 0)
            status = LsaConstants.StatusNoneMapped;
        else
            status = LsaConstants.StatusSuccess;

        var writer = new NdrWriter();

        // Write referenced domain list
        WriteReferencedDomainList(writer, referencedDomains);

        // Write translated SIDs
        switch (version)
        {
            case LookupNamesVersion.V1:
                WriteTranslatedSidsV1(writer, results);
                break;
            case LookupNamesVersion.V2:
                WriteTranslatedSidsV2(writer, results);
                break;
            case LookupNamesVersion.V3:
                WriteTranslatedSidsEx2(writer, results);
                break;
        }

        // MappedCount
        writer.WriteUInt32(mappedCount);

        // NTSTATUS
        writer.WriteUInt32(status);

        return writer.ToArray();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Opnum 15/57/76: LsarLookupSids / LsarLookupSids2 / LsarLookupSids3
    // ─────────────────────────────────────────────────────────────────────

    public Task<byte[]> LsarLookupSidsAsync(ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        return LookupSidsCore(stubData, context, LookupSidsVersion.V1, ct);
    }

    public Task<byte[]> LsarLookupSids2Async(ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        return LookupSidsCore(stubData, context, LookupSidsVersion.V2, ct);
    }

    public Task<byte[]> LsarLookupSids3Async(ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        return LookupSidsCore(stubData, context, LookupSidsVersion.V3, ct);
    }

    private async Task<byte[]> LookupSidsCore(ReadOnlyMemory<byte> stubData, RpcCallContext context, LookupSidsVersion version, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        LsaPolicyHandle handle;
        if (version == LookupSidsVersion.V3)
        {
            // LsarLookupSids3 does not take a policy handle — uses RPC binding
            handle = new LsaPolicyHandle
            {
                GrantedAccess = LsaConstants.PolicyLookupNames,
                TenantId = context.TenantId,
                DomainDn = context.DomainDn,
            };
        }
        else
        {
            var (attr, uuid) = reader.ReadContextHandle();
            handle = GetPolicyHandle(context, attr, uuid);
        }

        // LSAPR_SID_ENUM_BUFFER: count (uint32), pointer to SID array
        var sidCount = reader.ReadUInt32();
        var sidArrayPtr = reader.ReadPointer();

        var sids = new string[sidCount];
        if (sidArrayPtr != 0 && sidCount > 0)
        {
            // Conformant array of pointers to SIDs
            var arrayMaxCount = reader.ReadUInt32();
            var sidPtrs = new uint[sidCount];
            for (int i = 0; i < sidCount; i++)
            {
                sidPtrs[i] = reader.ReadPointer();
            }

            // Read deferred SID bodies
            for (int i = 0; i < sidCount; i++)
            {
                if (sidPtrs[i] != 0)
                {
                    // RPC_SID preceded by conformant max_count
                    var subAuthCount = reader.ReadUInt32(); // conformant max count for sub-auths
                    sids[i] = reader.ReadRpcSid();
                }
                else
                {
                    sids[i] = "";
                }
            }
        }

        _logger.LogDebug("LsarLookupSids{Version}: count={Count}", version, sidCount);

        var domainNc = _ncService.GetDomainNc();
        var domainSid = _ridAllocator.GetDomainSid(handle.TenantId, handle.DomainDn);
        var netbiosName = ExtractNetBiosName(domainNc.Dn);

        var results = new List<(ushort Use, string Name, int DomainIndex, uint Flags)>();
        var referencedDomains = new List<(string Name, string Sid)>();
        int localDomainIndex = -1;
        int builtinDomainIndex = -1;
        int ntAuthorityIndex = -1;
        uint mappedCount = 0;

        for (int i = 0; i < sidCount; i++)
        {
            var sid = sids[i];
            if (string.IsNullOrEmpty(sid))
            {
                results.Add((LsaConstants.SidTypeUnknown, sid, -1, 0));
                continue;
            }

            // Check well-known SIDs first
            if (WellKnownSids.TryGetValue(sid, out var wellKnown))
            {
                int domIdx;
                if (wellKnown.Domain == "BUILTIN")
                {
                    if (builtinDomainIndex < 0)
                    {
                        builtinDomainIndex = referencedDomains.Count;
                        referencedDomains.Add(("BUILTIN", "S-1-5-32"));
                    }
                    domIdx = builtinDomainIndex;
                }
                else if (wellKnown.Domain == "NT AUTHORITY")
                {
                    if (ntAuthorityIndex < 0)
                    {
                        ntAuthorityIndex = referencedDomains.Count;
                        referencedDomains.Add(("NT AUTHORITY", "S-1-5"));
                    }
                    domIdx = ntAuthorityIndex;
                }
                else
                {
                    domIdx = -1;
                }

                results.Add((wellKnown.Type, wellKnown.Name, domIdx, 0));
                mappedCount++;
                continue;
            }

            // Check if this SID is the domain SID itself
            if (sid.Equals(domainSid, StringComparison.OrdinalIgnoreCase))
            {
                if (localDomainIndex < 0)
                {
                    localDomainIndex = referencedDomains.Count;
                    referencedDomains.Add((netbiosName, domainSid));
                }
                results.Add((LsaConstants.SidTypeDomain, netbiosName, localDomainIndex, 0));
                mappedCount++;
                continue;
            }

            // Search directory by objectSid
            var searchResult = await _store.SearchAsync(
                handle.TenantId,
                handle.DomainDn,
                SearchScope.WholeSubtree,
                new EqualityFilterNode("objectSid", sid),
                new[] { "sAMAccountName", "objectClass", "cn" },
                sizeLimit: 1,
                ct: ct);

            if (searchResult.Entries.Count > 0)
            {
                var obj = searchResult.Entries[0];
                if (localDomainIndex < 0)
                {
                    localDomainIndex = referencedDomains.Count;
                    referencedDomains.Add((netbiosName, domainSid));
                }

                var sidType = ClassifyObjectSidType(obj);
                var name = obj.SAMAccountName ?? obj.Cn ?? "";
                results.Add((sidType, name, localDomainIndex, 0u));
                mappedCount++;
            }
            else
            {
                results.Add((LsaConstants.SidTypeUnknown, sid, -1, 0));
            }
        }

        // Determine return status
        uint status;
        if (mappedCount == sidCount && sidCount > 0)
            status = LsaConstants.StatusSuccess;
        else if (mappedCount > 0)
            status = LsaConstants.StatusSomeMapped;
        else if (sidCount > 0)
            status = LsaConstants.StatusNoneMapped;
        else
            status = LsaConstants.StatusSuccess;

        var writer = new NdrWriter();

        // Write referenced domain list
        WriteReferencedDomainList(writer, referencedDomains);

        // Write translated names
        switch (version)
        {
            case LookupSidsVersion.V1:
                WriteTranslatedNamesV1(writer, results);
                break;
            case LookupSidsVersion.V2:
            case LookupSidsVersion.V3:
                WriteTranslatedNamesEx(writer, results);
                break;
        }

        // MappedCount
        writer.WriteUInt32(mappedCount);

        // NTSTATUS
        writer.WriteUInt32(status);

        return writer.ToArray();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Opnum 50: LsarEnumerateTrustedDomainsEx
    // ─────────────────────────────────────────────────────────────────────

    public async Task<byte[]> LsarEnumerateTrustedDomainsExAsync(ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // Policy handle
        var (attr, uuid) = reader.ReadContextHandle();
        var handle = GetPolicyHandle(context, attr, uuid);

        // Enumeration context (in/out)
        var enumContext = reader.ReadUInt32();

        // PrefMaxLen
        var prefMaxLen = reader.ReadUInt32();

        _logger.LogDebug("LsarEnumerateTrustedDomainsEx: enumContext={EnumContext}", enumContext);

        // Search for trustedDomain objects under CN=System,{domainDn}
        var systemDn = $"CN=System,{handle.DomainDn}";
        var searchResult = await _store.SearchAsync(
            handle.TenantId,
            systemDn,
            SearchScope.SingleLevel,
            new EqualityFilterNode("objectClass", "trustedDomain"),
            new[] { "cn", "trustPartner", "trustDirection", "trustType", "trustAttributes", "objectSid" },
            ct: ct);

        var trustedDomains = searchResult.Entries;

        var writer = new NdrWriter();

        // Enumeration context (output) — set to total count to signal we've returned all
        writer.WriteUInt32((uint)trustedDomains.Count);

        if (trustedDomains.Count == 0)
        {
            // Empty buffer: pointer to LSAPR_TRUSTED_DOMAIN_INFORMATION_LIST_EX
            writer.WritePointer(true); // null pointer — no data
            writer.WriteUInt32(LsaConstants.StatusNoMoreEntries);
            return writer.ToArray();
        }

        // Pointer to buffer
        writer.WritePointer(false);

        // LSAPR_TRUSTED_DOMAIN_INFORMATION_LIST_EX:
        //   EntriesRead (uint32)
        //   Pointer to array of LSAPR_TRUSTED_DOMAIN_INFORMATION_EX
        writer.WriteUInt32((uint)trustedDomains.Count);
        writer.WritePointer(false); // pointer to array

        // Conformant array max count
        writer.WriteUInt32((uint)trustedDomains.Count);

        // Write each LSAPR_TRUSTED_DOMAIN_INFORMATION_EX inline structure
        foreach (var td in trustedDomains)
        {
            var tdName = td.Cn ?? "";
            var flatName = td.GetAttribute("flatName")?.GetFirstString() ?? tdName;
            var trustPartner = td.GetAttribute("trustPartner")?.GetFirstString() ?? "";
            var trustDirection = 0;
            var trustType = 0;
            var trustAttributes = 0;

            if (td.Attributes.TryGetValue("trustDirection", out var tdDir))
                int.TryParse(tdDir.GetFirstString(), out trustDirection);
            if (td.Attributes.TryGetValue("trustType", out var tdType))
                int.TryParse(tdType.GetFirstString(), out trustType);
            if (td.Attributes.TryGetValue("trustAttributes", out var tdAttr))
                int.TryParse(tdAttr.GetFirstString(), out trustAttributes);

            // Name (RPC_UNICODE_STRING)
            writer.WriteRpcUnicodeString(trustPartner);
            // FlatName (RPC_UNICODE_STRING)
            writer.WriteRpcUnicodeString(flatName);
            // Sid (pointer to RPC_SID)
            writer.WritePointer(td.ObjectSid == null);
            // TrustDirection (uint32)
            writer.WriteUInt32((uint)trustDirection);
            // TrustType (uint32)
            writer.WriteUInt32((uint)trustType);
            // TrustAttributes (uint32)
            writer.WriteUInt32((uint)trustAttributes);
        }

        // Flush deferred strings
        writer.FlushDeferred();

        // Write deferred SID bodies
        foreach (var td in trustedDomains)
        {
            if (td.ObjectSid != null)
            {
                writer.WriteRpcSid(td.ObjectSid);
            }
        }

        writer.WriteUInt32(LsaConstants.StatusSuccess);
        return writer.ToArray();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Opnum 2: LsarEnumeratePrivileges
    // ─────────────────────────────────────────────────────────────────────

    public Task<byte[]> LsarEnumeratePrivilegesAsync(ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // Policy handle (20 bytes)
        var (attr, uuid) = reader.ReadContextHandle();
        var handle = GetPolicyHandle(context, attr, uuid);

        // EnumerationContext (in/out uint32)
        var enumContext = reader.ReadUInt32();

        // PreferedMaximumLength (uint32)
        var prefMaxLen = reader.ReadUInt32();

        _logger.LogDebug("LsarEnumeratePrivileges: enumContext={EnumContext}", enumContext);

        var startIndex = (int)enumContext;
        var remaining = StandardPrivileges.Length - startIndex;

        if (remaining <= 0)
        {
            // No more entries
            var errWriter = new NdrWriter();
            errWriter.WriteUInt32((uint)StandardPrivileges.Length); // EnumerationContext
            // LSAPR_PRIVILEGE_ENUM_BUFFER: Entries=0, null pointer
            errWriter.WriteUInt32(0);
            errWriter.WritePointer(true);
            errWriter.WriteUInt32(LsaConstants.StatusNoMoreEntries);
            return Task.FromResult(errWriter.ToArray());
        }

        var count = remaining;

        var writer = new NdrWriter();

        // EnumerationContext (output)
        writer.WriteUInt32((uint)(startIndex + count));

        // LSAPR_PRIVILEGE_ENUM_BUFFER: Entries (uint32), pointer to array
        writer.WriteUInt32((uint)count);
        writer.WritePointer(false);

        // Conformant array max count
        writer.WriteUInt32((uint)count);

        // Each LSAPR_LUID_AND_ATTRIBUTES: LUID (uint32 low, uint32 high), Attributes (uint32)
        // But actually MS-LSAD says LSAPR_PRIVILEGE_ENUM_BUFFER contains array of
        // { LUID Luid; RPC_UNICODE_STRING Name; }
        for (int i = startIndex; i < startIndex + count; i++)
        {
            var priv = StandardPrivileges[i];
            // LUID: LowPart (uint32), HighPart (uint32)  — note: MS spec says LowPart first
            writer.WriteUInt32(priv.LuidLow);
            writer.WriteUInt32(priv.LuidHigh);
            // Name (RPC_UNICODE_STRING)
            writer.WriteRpcUnicodeString(priv.Name);
        }

        writer.FlushDeferred();
        writer.WriteUInt32(LsaConstants.StatusSuccess);

        return Task.FromResult(writer.ToArray());
    }

    // ─────────────────────────────────────────────────────────────────────
    // Opnum 35: LsarLookupPrivilegeValue
    // ─────────────────────────────────────────────────────────────────────

    public Task<byte[]> LsarLookupPrivilegeValueAsync(ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // Policy handle
        var (attr, uuid) = reader.ReadContextHandle();
        var handle = GetPolicyHandle(context, attr, uuid);

        // Name: RPC_UNICODE_STRING
        var nameHeader = reader.ReadRpcUnicodeString();
        string name = "";
        if (nameHeader.ReferentId != 0)
            name = reader.ReadConformantVaryingString();

        _logger.LogDebug("LsarLookupPrivilegeValue: Name={Name}", name);

        if (!PrivilegeNameToIndex.TryGetValue(name, out var index))
        {
            var errWriter = new NdrWriter();
            // LUID output (zeroed)
            errWriter.WriteUInt32(0);
            errWriter.WriteUInt32(0);
            errWriter.WriteUInt32(LsaConstants.StatusObjectNameNotFound);
            return Task.FromResult(errWriter.ToArray());
        }

        var priv = StandardPrivileges[index];
        var writer = new NdrWriter();
        // LUID: LowPart, HighPart
        writer.WriteUInt32(priv.LuidLow);
        writer.WriteUInt32(priv.LuidHigh);
        writer.WriteUInt32(LsaConstants.StatusSuccess);

        return Task.FromResult(writer.ToArray());
    }

    // ─────────────────────────────────────────────────────────────────────
    // Opnum 36: LsarLookupPrivilegeName
    // ─────────────────────────────────────────────────────────────────────

    public Task<byte[]> LsarLookupPrivilegeNameAsync(ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // Policy handle
        var (attr, uuid) = reader.ReadContextHandle();
        var handle = GetPolicyHandle(context, attr, uuid);

        // LUID: LowPart (uint32), HighPart (uint32)
        var luidLow = reader.ReadUInt32();
        var luidHigh = reader.ReadUInt32();

        _logger.LogDebug("LsarLookupPrivilegeName: LUID=({High},{Low})", luidHigh, luidLow);

        if (!PrivilegeLuidToIndex.TryGetValue((luidHigh, luidLow), out var index))
        {
            var errWriter = new NdrWriter();
            errWriter.WritePointer(true); // null name
            errWriter.WriteUInt32(LsaConstants.StatusObjectNameNotFound);
            return Task.FromResult(errWriter.ToArray());
        }

        var priv = StandardPrivileges[index];
        var writer = new NdrWriter();
        // [out] PRPC_UNICODE_STRING* Name — pointer to RPC_UNICODE_STRING
        writer.WritePointer(false);
        writer.WriteRpcUnicodeString(priv.Name);
        writer.FlushDeferred();
        writer.WriteUInt32(LsaConstants.StatusSuccess);

        return Task.FromResult(writer.ToArray());
    }

    // ─────────────────────────────────────────────────────────────────────
    // Opnum 37: LsarLookupPrivilegeDisplayName
    // ─────────────────────────────────────────────────────────────────────

    public Task<byte[]> LsarLookupPrivilegeDisplayNameAsync(ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // Policy handle
        var (attr, uuid) = reader.ReadContextHandle();
        var handle = GetPolicyHandle(context, attr, uuid);

        // Name: RPC_UNICODE_STRING
        var nameHeader = reader.ReadRpcUnicodeString();
        string name = "";
        if (nameHeader.ReferentId != 0)
            name = reader.ReadConformantVaryingString();

        // ClientLanguage (int16), ClientSystemDefaultLanguage (int16)
        // We ignore these — always return English

        _logger.LogDebug("LsarLookupPrivilegeDisplayName: Name={Name}", name);

        if (!PrivilegeNameToIndex.TryGetValue(name, out var index))
        {
            var errWriter = new NdrWriter();
            errWriter.WritePointer(true); // null display name
            errWriter.WriteUInt16(0); // LanguageReturned
            errWriter.WriteUInt32(LsaConstants.StatusObjectNameNotFound);
            return Task.FromResult(errWriter.ToArray());
        }

        var priv = StandardPrivileges[index];
        var writer = new NdrWriter();
        // [out] PRPC_UNICODE_STRING* DisplayName — pointer to RPC_UNICODE_STRING
        writer.WritePointer(false);
        writer.WriteRpcUnicodeString(priv.DisplayName);
        writer.FlushDeferred();
        // LanguageReturned (ushort) — 0x0409 = English
        writer.WriteUInt16(0x0409);
        writer.WriteUInt32(LsaConstants.StatusSuccess);

        return Task.FromResult(writer.ToArray());
    }

    // ─────────────────────────────────────────────────────────────────────
    // Opnum 11: LsarEnumerateAccounts
    // ─────────────────────────────────────────────────────────────────────

    public Task<byte[]> LsarEnumerateAccountsAsync(ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // Policy handle
        var (attr, uuid) = reader.ReadContextHandle();
        var handle = GetPolicyHandle(context, attr, uuid);

        // EnumerationContext (in/out uint32)
        var enumContext = reader.ReadUInt32();

        // PreferedMaximumLength (uint32)
        var prefMaxLen = reader.ReadUInt32();

        _logger.LogDebug("LsarEnumerateAccounts: enumContext={EnumContext}", enumContext);

        // Return empty — no accounts with privilege assignments
        var writer = new NdrWriter();
        writer.WriteUInt32(0); // EnumerationContext
        // LSAPR_ACCOUNT_ENUM_BUFFER: Entries=0, null pointer
        writer.WriteUInt32(0);
        writer.WritePointer(true);
        writer.WriteUInt32(LsaConstants.StatusNoMoreEntries);

        return Task.FromResult(writer.ToArray());
    }

    // ─────────────────────────────────────────────────────────────────────
    // Opnum 18: LsarOpenAccount
    // ─────────────────────────────────────────────────────────────────────

    public Task<byte[]> LsarOpenAccountAsync(ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // Policy handle
        var (attr, uuid) = reader.ReadContextHandle();
        var handle = GetPolicyHandle(context, attr, uuid);

        // AccountSid: pointer to RPC_SID
        var subAuthCount = reader.ReadUInt32(); // conformant max count
        var sid = reader.ReadRpcSid();

        // DesiredAccess (uint32)
        var desiredAccess = reader.ReadUInt32();

        _logger.LogDebug("LsarOpenAccount: SID={Sid}", sid);

        // Create an account handle
        var accountHandleGuid = Guid.NewGuid();
        AccountHandles[accountHandleGuid] = sid;

        var writer = new NdrWriter();
        // AccountHandle (context handle)
        writer.WriteContextHandle(0, accountHandleGuid);
        writer.WriteUInt32(LsaConstants.StatusSuccess);

        return Task.FromResult(writer.ToArray());
    }

    // ─────────────────────────────────────────────────────────────────────
    // Opnum 19: LsarEnumeratePrivilegesAccount
    // ─────────────────────────────────────────────────────────────────────

    public Task<byte[]> LsarEnumeratePrivilegesAccountAsync(ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // Account handle (20 bytes)
        var (attr, uuid) = reader.ReadContextHandle();

        _logger.LogDebug("LsarEnumeratePrivilegesAccount: handle={Handle}", uuid);

        // Look up the SID from the account handle
        HashSet<int> privIndices = null;
        if (AccountHandles.TryGetValue(uuid, out var sid))
        {
            AccountPrivileges.TryGetValue(sid, out privIndices);
        }

        var writer = new NdrWriter();

        if (privIndices == null || privIndices.Count == 0)
        {
            // Pointer to LSAPR_PRIVILEGE_SET
            writer.WritePointer(false);
            // LSAPR_PRIVILEGE_SET: Entries=0, Control=0
            writer.WriteUInt32(0); // Entries
            writer.WriteUInt32(0); // Control
            // No array to write
            writer.WriteUInt32(LsaConstants.StatusSuccess);
            return Task.FromResult(writer.ToArray());
        }

        var privList = privIndices.ToArray();

        // Pointer to LSAPR_PRIVILEGE_SET
        writer.WritePointer(false);
        // LSAPR_PRIVILEGE_SET: Entries, Control, then array of LUID_AND_ATTRIBUTES
        writer.WriteUInt32((uint)privList.Length);
        writer.WriteUInt32(0); // Control

        foreach (var idx in privList)
        {
            var priv = StandardPrivileges[idx];
            // LUID_AND_ATTRIBUTES: LUID (LowPart uint32, HighPart uint32), Attributes (uint32)
            writer.WriteUInt32(priv.LuidLow);
            writer.WriteUInt32(priv.LuidHigh);
            writer.WriteUInt32(0x00000002); // SE_PRIVILEGE_ENABLED
        }

        writer.WriteUInt32(LsaConstants.StatusSuccess);

        return Task.FromResult(writer.ToArray());
    }

    // ─────────────────────────────────────────────────────────────────────
    // Opnum 20: LsarAddPrivilegesToAccount
    // ─────────────────────────────────────────────────────────────────────

    public Task<byte[]> LsarAddPrivilegesToAccountAsync(ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // Account handle
        var (attr, uuid) = reader.ReadContextHandle();

        _logger.LogDebug("LsarAddPrivilegesToAccount: handle={Handle}", uuid);

        // LSAPR_PRIVILEGE_SET: Entries (uint32), Control (uint32), then array
        var entries = reader.ReadUInt32();
        var control = reader.ReadUInt32();

        if (AccountHandles.TryGetValue(uuid, out var sid))
        {
            var privSet = AccountPrivileges.GetOrAdd(sid, _ => new HashSet<int>());
            for (uint i = 0; i < entries; i++)
            {
                var luidLow = reader.ReadUInt32();
                var luidHigh = reader.ReadUInt32();
                var attributes = reader.ReadUInt32();
                if (PrivilegeLuidToIndex.TryGetValue((luidHigh, luidLow), out var index))
                {
                    lock (privSet) { privSet.Add(index); }
                }
            }
        }

        var writer = new NdrWriter();
        writer.WriteUInt32(LsaConstants.StatusSuccess);
        return Task.FromResult(writer.ToArray());
    }

    // ─────────────────────────────────────────────────────────────────────
    // Opnum 21: LsarRemovePrivilegesFromAccount
    // ─────────────────────────────────────────────────────────────────────

    public Task<byte[]> LsarRemovePrivilegesFromAccountAsync(ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // Account handle
        var (attr, uuid) = reader.ReadContextHandle();

        // AllPrivileges (uint8/boolean)
        var allPrivileges = reader.ReadByte();

        _logger.LogDebug("LsarRemovePrivilegesFromAccount: handle={Handle}, all={All}", uuid, allPrivileges);

        // Pointer to LSAPR_PRIVILEGE_SET (may be null if AllPrivileges=TRUE)
        var privSetPtr = reader.ReadPointer();

        if (AccountHandles.TryGetValue(uuid, out var sid))
        {
            if (allPrivileges != 0)
            {
                AccountPrivileges.TryRemove(sid, out _);
            }
            else if (privSetPtr != 0)
            {
                var entries = reader.ReadUInt32();
                var control = reader.ReadUInt32();

                if (AccountPrivileges.TryGetValue(sid, out var privSet))
                {
                    for (uint i = 0; i < entries; i++)
                    {
                        var luidLow = reader.ReadUInt32();
                        var luidHigh = reader.ReadUInt32();
                        var attributes = reader.ReadUInt32();
                        if (PrivilegeLuidToIndex.TryGetValue((luidHigh, luidLow), out var index))
                        {
                            lock (privSet) { privSet.Remove(index); }
                        }
                    }
                }
            }
        }

        var writer = new NdrWriter();
        writer.WriteUInt32(LsaConstants.StatusSuccess);
        return Task.FromResult(writer.ToArray());
    }

    // ─────────────────────────────────────────────────────────────────────
    // Opnum 3: LsarQuerySecurityObject
    // ─────────────────────────────────────────────────────────────────────

    public Task<byte[]> LsarQuerySecurityObjectAsync(ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // Object handle (20 bytes)
        var (attr, uuid) = reader.ReadContextHandle();

        // SecurityInformation (uint32)
        var securityInfo = reader.ReadUInt32();

        _logger.LogDebug("LsarQuerySecurityObject: securityInfo=0x{SecurityInfo:X8}", securityInfo);

        // Build a minimal self-relative security descriptor with
        // Administrators (S-1-5-32-544) having full access.
        // SECURITY_DESCRIPTOR (self-relative):
        //   Revision(1) Sbz1(0) Control(0x8004=SE_DACL_PRESENT|SE_SELF_RELATIVE) OffsetOwner OffsetGroup OffsetSacl OffsetDacl
        //   Then: Owner SID, Group SID, DACL
        // Administrators SID: S-1-5-32-544 => 1,2,0,0,0,5, 32,0,0,0, 544(=0x220)

        // Pre-build the SD bytes
        var adminsSid = new byte[] { 1, 2, 0, 0, 0, 0, 0, 5, 32, 0, 0, 0, 0, 2, 0, 0 };
        // ACL with one ACE granting full access to Administrators
        // ACL: AclRevision(2), Sbz1(0), AclSize(ushort), AceCount(ushort), Sbz2(ushort)
        // ACE: AceType(0=ACCESS_ALLOWED), AceFlags(0), AceSize(ushort), Mask(uint32), SID
        var aceSize = (ushort)(8 + adminsSid.Length); // 8 = type(1)+flags(1)+size(2)+mask(4)
        var aclSize = (ushort)(8 + aceSize);

        // Build the ACL
        var acl = new byte[aclSize];
        acl[0] = 2; // AclRevision
        acl[2] = (byte)(aclSize & 0xFF);
        acl[3] = (byte)((aclSize >> 8) & 0xFF);
        acl[4] = 1; // AceCount
        // ACE starts at offset 8
        acl[8] = 0; // ACCESS_ALLOWED_ACE_TYPE
        acl[9] = 0; // AceFlags
        acl[10] = (byte)(aceSize & 0xFF);
        acl[11] = (byte)((aceSize >> 8) & 0xFF);
        // Mask = 0x000F0FFF (GENERIC_ALL for policy)
        acl[12] = 0xFF; acl[13] = 0x0F; acl[14] = 0x0F; acl[15] = 0x00;
        Array.Copy(adminsSid, 0, acl, 16, adminsSid.Length);

        // SD header: 20 bytes
        var sdHeaderSize = 20;
        var ownerOffset = sdHeaderSize;
        var groupOffset = ownerOffset + adminsSid.Length;
        var daclOffset = groupOffset + adminsSid.Length;
        var totalSdSize = sdHeaderSize + adminsSid.Length + adminsSid.Length + acl.Length;

        var sd = new byte[totalSdSize];
        sd[0] = 1; // Revision
        // Control: SE_DACL_PRESENT (0x0004) | SE_SELF_RELATIVE (0x8000) = 0x8004
        sd[2] = 0x04; sd[3] = 0x80;
        // OffsetOwner (uint32 LE at offset 4)
        BitConverter.GetBytes(ownerOffset).CopyTo(sd, 4);
        // OffsetGroup (uint32 LE at offset 8)
        BitConverter.GetBytes(groupOffset).CopyTo(sd, 8);
        // OffsetSacl = 0 (at offset 12)
        // OffsetDacl (uint32 LE at offset 16)
        BitConverter.GetBytes(daclOffset).CopyTo(sd, 16);
        // Owner SID
        Array.Copy(adminsSid, 0, sd, ownerOffset, adminsSid.Length);
        // Group SID
        Array.Copy(adminsSid, 0, sd, groupOffset, adminsSid.Length);
        // DACL
        Array.Copy(acl, 0, sd, daclOffset, acl.Length);

        var writer = new NdrWriter();

        // [out] PLSAPR_SR_SECURITY_DESCRIPTOR* SecurityDescriptor — pointer to pointer
        writer.WritePointer(false);

        // LSAPR_SR_SECURITY_DESCRIPTOR: Length (uint32), pointer to SecurityDescriptor byte array
        writer.WriteUInt32((uint)sd.Length);
        writer.WritePointer(false);

        // Conformant byte array: MaxCount, then bytes
        writer.WriteUInt32((uint)sd.Length);
        writer.WriteBytes(sd);

        writer.WriteUInt32(LsaConstants.StatusSuccess);

        return Task.FromResult(writer.ToArray());
    }

    // ─────────────────────────────────────────────────────────────────────
    // Opnum 4: LsarSetSecurityObject
    // ─────────────────────────────────────────────────────────────────────

    public Task<byte[]> LsarSetSecurityObjectAsync(ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        _logger.LogDebug("LsarSetSecurityObject: stub — returning success");

        // Stub: accept the request and return success without persisting
        var writer = new NdrWriter();
        writer.WriteUInt32(LsaConstants.StatusSuccess);
        return Task.FromResult(writer.ToArray());
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helper: Write referenced domain list
    // ─────────────────────────────────────────────────────────────────────

    private static void WriteReferencedDomainList(NdrWriter writer, List<(string Name, string Sid)> domains)
    {
        // Pointer to LSAPR_REFERENCED_DOMAIN_LIST
        writer.WritePointer(false);

        // Entries (uint32)
        writer.WriteUInt32((uint)domains.Count);

        if (domains.Count > 0)
        {
            // Pointer to array of LSAPR_TRUST_INFORMATION
            writer.WritePointer(false);

            // MaxEntries (uint32) — the max size of the Domains array
            writer.WriteUInt32((uint)domains.Count);

            // Conformant array MaxCount
            writer.WriteUInt32((uint)domains.Count);

            // Write each LSAPR_TRUST_INFORMATION inline: Name (RPC_UNICODE_STRING), Sid (pointer)
            foreach (var (name, sid) in domains)
            {
                writer.WriteRpcUnicodeString(name);
                writer.WritePointer(false); // SID pointer
            }

            // Flush deferred strings
            writer.FlushDeferred();

            // Write deferred SID bodies
            foreach (var (name, sid) in domains)
            {
                writer.WriteRpcSid(sid);
            }
        }
        else
        {
            // Null pointer for empty array
            writer.WritePointer(true);
            // MaxEntries
            writer.WriteUInt32(0);
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers: Write translated SIDs (for LookupNames)
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// LSAPR_TRANSLATED_SIDS (opnum 14 — v1):
    /// Each entry: Use (ushort pad to uint32? No — uint16), RelativeId (uint32), DomainIndex (int32)
    /// Actually MS-LSAT spec: Use(uint16, pad), RelativeId(uint32), DomainIndex(int32)
    /// Wrapped in: Entries(uint32), pointer to array
    /// </summary>
    private static void WriteTranslatedSidsV1(NdrWriter writer, List<(ushort Use, string Sid, int DomainIndex, uint Flags)> results)
    {
        // LSAPR_TRANSLATED_SIDS: Entries (uint32), pointer to array
        writer.WriteUInt32((uint)results.Count);

        if (results.Count > 0)
        {
            writer.WritePointer(false); // pointer to array

            // Conformant array max count
            writer.WriteUInt32((uint)results.Count);

            foreach (var (use, sid, domainIndex, flags) in results)
            {
                // LSAPR_TRANSLATED_SID: Use(ushort), pad(ushort), RelativeId(uint32), DomainIndex(int32)
                writer.WriteUInt16(use);
                writer.Align(4);
                writer.WriteUInt32(ExtractRid(sid));
                writer.WriteInt32(domainIndex);
            }
        }
        else
        {
            writer.WritePointer(true);
        }
    }

    /// <summary>
    /// LSAPR_TRANSLATED_SIDS_EX (opnum 58 — v2):
    /// Each entry: Use(ushort), pad, RelativeId(uint32), DomainIndex(int32), Flags(uint32)
    /// </summary>
    private static void WriteTranslatedSidsV2(NdrWriter writer, List<(ushort Use, string Sid, int DomainIndex, uint Flags)> results)
    {
        writer.WriteUInt32((uint)results.Count);

        if (results.Count > 0)
        {
            writer.WritePointer(false);
            writer.WriteUInt32((uint)results.Count);

            foreach (var (use, sid, domainIndex, flags) in results)
            {
                writer.WriteUInt16(use);
                writer.Align(4);
                writer.WriteUInt32(ExtractRid(sid));
                writer.WriteInt32(domainIndex);
                writer.WriteUInt32(flags);
            }
        }
        else
        {
            writer.WritePointer(true);
        }
    }

    /// <summary>
    /// LSAPR_TRANSLATED_SIDS_EX2 (opnum 68 — v3):
    /// Each entry: Use(ushort), pad, Sid(pointer to RPC_SID), DomainIndex(int32), Flags(uint32)
    /// </summary>
    private static void WriteTranslatedSidsEx2(NdrWriter writer, List<(ushort Use, string Sid, int DomainIndex, uint Flags)> results)
    {
        writer.WriteUInt32((uint)results.Count);

        if (results.Count > 0)
        {
            writer.WritePointer(false);
            writer.WriteUInt32((uint)results.Count);

            // Write inline entries
            foreach (var (use, sid, domainIndex, flags) in results)
            {
                writer.WriteUInt16(use);
                writer.Align(4);
                writer.WritePointer(sid == null); // Sid pointer
                writer.WriteInt32(domainIndex);
                writer.WriteUInt32(flags);
            }

            // Write deferred SID bodies
            foreach (var (use, sid, domainIndex, flags) in results)
            {
                if (sid != null)
                {
                    writer.WriteRpcSid(sid);
                }
            }
        }
        else
        {
            writer.WritePointer(true);
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers: Write translated names (for LookupSids)
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// LSAPR_TRANSLATED_NAMES (opnum 15 — v1):
    /// Each entry: Use(ushort), Name(RPC_UNICODE_STRING), DomainIndex(int32)
    /// </summary>
    private static void WriteTranslatedNamesV1(NdrWriter writer, List<(ushort Use, string Name, int DomainIndex, uint Flags)> results)
    {
        // LSAPR_TRANSLATED_NAMES: Entries(uint32), pointer to array
        writer.WriteUInt32((uint)results.Count);

        if (results.Count > 0)
        {
            writer.WritePointer(false);
            writer.WriteUInt32((uint)results.Count);

            foreach (var (use, name, domainIndex, flags) in results)
            {
                writer.WriteUInt16(use);
                writer.Align(4);
                writer.WriteRpcUnicodeString(use != LsaConstants.SidTypeUnknown ? name : null);
                writer.WriteInt32(domainIndex);
            }

            writer.FlushDeferred();
        }
        else
        {
            writer.WritePointer(true);
        }
    }

    /// <summary>
    /// LSAPR_TRANSLATED_NAMES_EX (opnum 57/76 — v2/v3):
    /// Each entry: Use(ushort), Name(RPC_UNICODE_STRING), DomainIndex(int32), Flags(uint32)
    /// </summary>
    private static void WriteTranslatedNamesEx(NdrWriter writer, List<(ushort Use, string Name, int DomainIndex, uint Flags)> results)
    {
        writer.WriteUInt32((uint)results.Count);

        if (results.Count > 0)
        {
            writer.WritePointer(false);
            writer.WriteUInt32((uint)results.Count);

            foreach (var (use, name, domainIndex, flags) in results)
            {
                writer.WriteUInt16(use);
                writer.Align(4);
                writer.WriteRpcUnicodeString(use != LsaConstants.SidTypeUnknown ? name : null);
                writer.WriteInt32(domainIndex);
                writer.WriteUInt32(flags);
            }

            writer.FlushDeferred();
        }
        else
        {
            writer.WritePointer(true);
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Utility methods
    // ─────────────────────────────────────────────────────────────────────

    private LsaPolicyHandle GetPolicyHandle(RpcCallContext context, uint attr, Guid uuid)
    {
        var handleBytes = new byte[20];
        BitConverter.GetBytes(attr).CopyTo(handleBytes, 0);
        uuid.TryWriteBytes(handleBytes.AsSpan(4));

        var handle = context.ContextHandles.GetHandle<LsaPolicyHandle>(handleBytes);
        if (handle == null)
        {
            throw new RpcFaultException(LsaConstants.StatusInvalidHandle, "Invalid LSA policy handle");
        }
        return handle;
    }

    private static ushort ClassifyObjectSidType(DirectoryObject obj)
    {
        var classes = obj.ObjectClass;
        if (classes.Contains("computer", StringComparer.OrdinalIgnoreCase))
            return LsaConstants.SidTypeComputer;
        if (classes.Contains("user", StringComparer.OrdinalIgnoreCase))
            return LsaConstants.SidTypeUser;
        if (classes.Contains("group", StringComparer.OrdinalIgnoreCase))
            return LsaConstants.SidTypeGroup;
        return LsaConstants.SidTypeUnknown;
    }

    /// <summary>
    /// Extracts the NetBIOS domain name from a domain DN.
    /// E.g., "DC=directory,DC=net" → "DIRECTORY"
    /// </summary>
    private static string ExtractNetBiosName(string domainDn)
    {
        if (string.IsNullOrEmpty(domainDn))
            return "DOMAIN";

        // Take the first DC= component
        var parts = domainDn.Split(',');
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (trimmed.StartsWith("DC=", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed[3..].ToUpperInvariant();
            }
        }

        return "DOMAIN";
    }

    /// <summary>
    /// Extracts the RID (last sub-authority) from a SID string.
    /// </summary>
    private static uint ExtractRid(string sid)
    {
        if (string.IsNullOrEmpty(sid))
            return 0;

        var lastDash = sid.LastIndexOf('-');
        if (lastDash >= 0 && uint.TryParse(sid.AsSpan(lastDash + 1), out var rid))
            return rid;

        return 0;
    }

    /// <summary>
    /// Creates a deterministic GUID from a string (for domain GUID derivation).
    /// </summary>
    private static Guid DeterministicGuid(string input)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(input));
        // Use first 16 bytes as a version-4-ish GUID
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x40); // version 4
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // variant 1
        return new Guid(bytes.AsSpan(0, 16));
    }

    private enum LookupNamesVersion { V1, V2, V3 }
    private enum LookupSidsVersion { V1, V2, V3 }
}
