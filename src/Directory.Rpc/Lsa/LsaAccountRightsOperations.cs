using System.Collections.Concurrent;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Rpc.Dispatch;
using Directory.Rpc.Ndr;
using Microsoft.Extensions.Logging;

namespace Directory.Rpc.Lsa;

/// <summary>
/// Implements MS-LSAD account rights operations (opnums 35, 36, 37, 38).
/// Manages privilege/right assignment to users and groups (SeNetworkLogonRight, etc.).
/// </summary>
public class LsaAccountRightsOperations
{
    private readonly IDirectoryStore _store;
    private readonly ILogger<LsaAccountRightsOperations> _logger;

    /// <summary>
    /// In-memory account rights store: SID string -> set of right/privilege names.
    /// Each entry represents the rights assigned to a specific security principal.
    /// </summary>
    private static readonly ConcurrentDictionary<string, HashSet<string>> AccountRights = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Well-known account rights and privileges that can be assigned via LSA.
    /// </summary>
    private static readonly HashSet<string> ValidRights = new(StringComparer.OrdinalIgnoreCase)
    {
        "SeNetworkLogonRight",
        "SeInteractiveLogonRight",
        "SeRemoteInteractiveLogonRight",
        "SeBatchLogonRight",
        "SeServiceLogonRight",
        "SeDenyNetworkLogonRight",
        "SeDenyInteractiveLogonRight",
        "SeDenyRemoteInteractiveLogonRight",
        "SeDenyBatchLogonRight",
        "SeDenyServiceLogonRight",
        "SeAssignPrimaryTokenPrivilege",
        "SeAuditPrivilege",
        "SeBackupPrivilege",
        "SeChangeNotifyPrivilege",
        "SeCreateGlobalPrivilege",
        "SeCreatePagefilePrivilege",
        "SeCreatePermanentPrivilege",
        "SeCreateSymbolicLinkPrivilege",
        "SeCreateTokenPrivilege",
        "SeDebugPrivilege",
        "SeEnableDelegationPrivilege",
        "SeImpersonatePrivilege",
        "SeIncreaseBasePriorityPrivilege",
        "SeIncreaseQuotaPrivilege",
        "SeIncreaseWorkingSetPrivilege",
        "SeLoadDriverPrivilege",
        "SeLockMemoryPrivilege",
        "SeMachineAccountPrivilege",
        "SeManageVolumePrivilege",
        "SeProfileSingleProcessPrivilege",
        "SeRelabelPrivilege",
        "SeRemoteShutdownPrivilege",
        "SeRestorePrivilege",
        "SeSecurityPrivilege",
        "SeShutdownPrivilege",
        "SeSyncAgentPrivilege",
        "SeSystemEnvironmentPrivilege",
        "SeSystemProfilePrivilege",
        "SeSystemtimePrivilege",
        "SeTakeOwnershipPrivilege",
        "SeTcbPrivilege",
        "SeTimeZonePrivilege",
        "SeTrustedCredManAccessPrivilege",
        "SeUndockPrivilege",
    };

    public LsaAccountRightsOperations(
        IDirectoryStore store,
        ILogger<LsaAccountRightsOperations> logger)
    {
        _store = store;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Opnum 36: LsarEnumerateAccountRights
    // ─────────────────────────────────────────────────────────────────────

    public Task<byte[]> LsarEnumerateAccountRightsAsync(ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // Policy handle (20 bytes)
        var (attr, uuid) = reader.ReadContextHandle();
        var handle = GetPolicyHandle(context, attr, uuid);

        // AccountSid: RPC_SID (conformant)
        var subAuthCount = reader.ReadUInt32(); // conformant max count
        var sid = reader.ReadRpcSid();

        _logger.LogDebug("LsarEnumerateAccountRights: SID={Sid}", sid);

        AccountRights.TryGetValue(sid, out var rights);

        var writer = new NdrWriter();

        if (rights == null || rights.Count == 0)
        {
            // LSAPR_USER_RIGHT_SET: Entries=0, pointer=null
            writer.WriteUInt32(0);
            writer.WritePointer(true);
            writer.WriteUInt32(LsaConstants.StatusObjectNameNotFound);
            return Task.FromResult(writer.ToArray());
        }

        var rightsList = rights.ToArray();

        // LSAPR_USER_RIGHT_SET:
        //   Entries (uint32)
        //   pointer to array of RPC_UNICODE_STRING
        writer.WriteUInt32((uint)rightsList.Length);
        writer.WritePointer(false);

        // Conformant array MaxCount
        writer.WriteUInt32((uint)rightsList.Length);

        // Array of RPC_UNICODE_STRING headers
        foreach (var right in rightsList)
        {
            writer.WriteRpcUnicodeString(right);
        }

        // Flush deferred string bodies
        writer.FlushDeferred();

        writer.WriteUInt32(LsaConstants.StatusSuccess);
        return Task.FromResult(writer.ToArray());
    }

    // ─────────────────────────────────────────────────────────────────────
    // Opnum 37: LsarAddAccountRights
    // ─────────────────────────────────────────────────────────────────────

    public Task<byte[]> LsarAddAccountRightsAsync(ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // Policy handle (20 bytes)
        var (attr, uuid) = reader.ReadContextHandle();
        var handle = GetPolicyHandle(context, attr, uuid);

        // AccountSid: RPC_SID (conformant)
        var subAuthCount = reader.ReadUInt32(); // conformant max count
        var sid = reader.ReadRpcSid();

        // LSAPR_USER_RIGHT_SET:
        //   Entries (uint32)
        //   pointer to array of RPC_UNICODE_STRING
        var entries = reader.ReadUInt32();
        var arrayPtr = reader.ReadPointer();

        _logger.LogDebug("LsarAddAccountRights: SID={Sid}, Entries={Entries}", sid, entries);

        if (arrayPtr != 0 && entries > 0)
        {
            // Conformant array MaxCount
            var maxCount = reader.ReadUInt32();

            // Read RPC_UNICODE_STRING headers
            var headers = new (ushort Length, ushort MaxLength, uint ReferentId)[entries];
            for (int i = 0; i < entries; i++)
            {
                headers[i] = reader.ReadRpcUnicodeString();
            }

            // Read deferred string bodies and add rights
            var rightsSet = AccountRights.GetOrAdd(sid, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));

            for (int i = 0; i < entries; i++)
            {
                if (headers[i].ReferentId != 0)
                {
                    var rightName = reader.ReadConformantVaryingString();

                    if (!ValidRights.Contains(rightName))
                    {
                        _logger.LogWarning("LsarAddAccountRights: unknown right {Right}", rightName);
                        var errWriter = new NdrWriter();
                        errWriter.WriteUInt32(LsaConstants.StatusNoSuchPrivilege);
                        return Task.FromResult(errWriter.ToArray());
                    }

                    lock (rightsSet)
                    {
                        rightsSet.Add(rightName);
                    }
                }
            }

            _logger.LogInformation("LsarAddAccountRights: added {Count} rights to SID {Sid}", entries, sid);
        }

        var writer = new NdrWriter();
        writer.WriteUInt32(LsaConstants.StatusSuccess);
        return Task.FromResult(writer.ToArray());
    }

    // ─────────────────────────────────────────────────────────────────────
    // Opnum 38: LsarRemoveAccountRights
    // ─────────────────────────────────────────────────────────────────────

    public Task<byte[]> LsarRemoveAccountRightsAsync(ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // Policy handle (20 bytes)
        var (attr, uuid) = reader.ReadContextHandle();
        var handle = GetPolicyHandle(context, attr, uuid);

        // AccountSid: RPC_SID (conformant)
        var subAuthCount = reader.ReadUInt32(); // conformant max count
        var sid = reader.ReadRpcSid();

        // AllRights (uint8/boolean)
        var allRights = reader.ReadByte();

        _logger.LogDebug("LsarRemoveAccountRights: SID={Sid}, AllRights={All}", sid, allRights);

        if (allRights != 0)
        {
            // Remove all rights for this account
            AccountRights.TryRemove(sid, out _);
        }
        else
        {
            // LSAPR_USER_RIGHT_SET:
            //   Entries (uint32)
            //   pointer to array of RPC_UNICODE_STRING
            var entries = reader.ReadUInt32();
            var arrayPtr = reader.ReadPointer();

            if (arrayPtr != 0 && entries > 0 && AccountRights.TryGetValue(sid, out var rightsSet))
            {
                // Conformant array MaxCount
                var maxCount = reader.ReadUInt32();

                // Read RPC_UNICODE_STRING headers
                var headers = new (ushort Length, ushort MaxLength, uint ReferentId)[entries];
                for (int i = 0; i < entries; i++)
                {
                    headers[i] = reader.ReadRpcUnicodeString();
                }

                // Read deferred string bodies and remove rights
                for (int i = 0; i < entries; i++)
                {
                    if (headers[i].ReferentId != 0)
                    {
                        var rightName = reader.ReadConformantVaryingString();
                        lock (rightsSet)
                        {
                            rightsSet.Remove(rightName);
                        }
                    }
                }
            }
        }

        _logger.LogInformation("LsarRemoveAccountRights: updated rights for SID {Sid}", sid);

        var writer = new NdrWriter();
        writer.WriteUInt32(LsaConstants.StatusSuccess);
        return Task.FromResult(writer.ToArray());
    }

    // ─────────────────────────────────────────────────────────────────────
    // Opnum 35: LsarEnumerateAccountsWithUserRight
    // ─────────────────────────────────────────────────────────────────────

    public Task<byte[]> LsarEnumerateAccountsWithUserRightAsync(ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // Policy handle (20 bytes)
        var (attr, uuid) = reader.ReadContextHandle();
        var handle = GetPolicyHandle(context, attr, uuid);

        // UserRight: [in, unique] PRPC_UNICODE_STRING
        var rightNamePtr = reader.ReadPointer();
        string rightName = null;

        if (rightNamePtr != 0)
        {
            var header = reader.ReadRpcUnicodeString();
            if (header.ReferentId != 0)
                rightName = reader.ReadConformantVaryingString();
        }

        _logger.LogDebug("LsarEnumerateAccountsWithUserRight: Right={Right}", rightName ?? "(all)");

        // Find all SIDs that have the specified right
        var matchingSids = new List<string>();

        if (rightName != null)
        {
            // Validate the right name
            if (!ValidRights.Contains(rightName))
            {
                var errWriter = new NdrWriter();
                // LSAPR_ACCOUNT_ENUM_BUFFER: Entries=0, null pointer
                errWriter.WriteUInt32(0);
                errWriter.WritePointer(true);
                errWriter.WriteUInt32(LsaConstants.StatusNoSuchPrivilege);
                return Task.FromResult(errWriter.ToArray());
            }

            foreach (var kvp in AccountRights)
            {
                HashSet<string> rights = kvp.Value;
                bool hasRight;
                lock (rights)
                {
                    hasRight = rights.Contains(rightName);
                }
                if (hasRight)
                    matchingSids.Add(kvp.Key);
            }
        }
        else
        {
            // Return all accounts that have any rights
            matchingSids.AddRange(AccountRights.Keys);
        }

        var writer = new NdrWriter();

        if (matchingSids.Count == 0)
        {
            // LSAPR_ACCOUNT_ENUM_BUFFER: Entries=0, null pointer
            writer.WriteUInt32(0);
            writer.WritePointer(true);
            writer.WriteUInt32(LsaConstants.StatusNoMoreEntries);
            return Task.FromResult(writer.ToArray());
        }

        // LSAPR_ACCOUNT_ENUM_BUFFER:
        //   EntriesRead (uint32)
        //   pointer to array of LSAPR_ACCOUNT_INFORMATION
        writer.WriteUInt32((uint)matchingSids.Count);
        writer.WritePointer(false);

        // Conformant array MaxCount
        writer.WriteUInt32((uint)matchingSids.Count);

        // Each LSAPR_ACCOUNT_INFORMATION: Sid (pointer to RPC_SID)
        foreach (var sid in matchingSids)
        {
            writer.WritePointer(false); // SID pointer
        }

        // Write deferred SID bodies
        foreach (var sid in matchingSids)
        {
            writer.WriteRpcSid(sid);
        }

        writer.WriteUInt32(LsaConstants.StatusSuccess);
        return Task.FromResult(writer.ToArray());
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

    private LsaPolicyHandle GetPolicyHandle(RpcCallContext context, uint attr, Guid uuid)
    {
        var handleBytes = new byte[20];
        BitConverter.GetBytes(attr).CopyTo(handleBytes, 0);
        uuid.TryWriteBytes(handleBytes.AsSpan(4));

        var handle = context.ContextHandles.GetHandle<LsaPolicyHandle>(handleBytes);
        if (handle == null)
            throw new RpcFaultException(LsaConstants.StatusInvalidHandle, "Invalid LSA policy handle");

        return handle;
    }
}
