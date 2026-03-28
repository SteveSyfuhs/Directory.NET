using System.Collections.Concurrent;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Rpc.Dispatch;
using Directory.Rpc.Ndr;
using Microsoft.Extensions.Logging;

namespace Directory.Rpc.Lsa;

/// <summary>
/// Implements MS-LSAD trusted domain operations (opnums 12, 25, 26, 27).
/// Manages trusted domain objects for forest/domain trust relationships.
/// </summary>
public class LsaTrustedDomainOperations
{
    private readonly IDirectoryStore _store;
    private readonly INamingContextService _ncService;
    private readonly IRidAllocator _ridAllocator;
    private readonly ILogger<LsaTrustedDomainOperations> _logger;

    /// <summary>
    /// Trusted domain handle table: GUID -> handle info.
    /// </summary>
    private static readonly ConcurrentDictionary<Guid, LsaTrustedDomainHandle> TdHandles = new();

    public LsaTrustedDomainOperations(
        IDirectoryStore store,
        INamingContextService ncService,
        IRidAllocator ridAllocator,
        ILogger<LsaTrustedDomainOperations> logger)
    {
        _store = store;
        _ncService = ncService;
        _ridAllocator = ridAllocator;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Opnum 12: LsarCreateTrustedDomain
    // ─────────────────────────────────────────────────────────────────────

    public async Task<byte[]> LsarCreateTrustedDomainAsync(ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // Policy handle (20 bytes)
        var (attr, uuid) = reader.ReadContextHandle();
        var handle = GetPolicyHandle(context, attr, uuid);

        // LSAPR_TRUST_INFORMATION:
        //   Name (RPC_UNICODE_STRING)
        //   Sid (pointer to RPC_SID)
        var nameHeader = reader.ReadRpcUnicodeString();
        var sidPtr = reader.ReadPointer();

        // Read deferred name string
        string trustedDomainName = "";
        if (nameHeader.ReferentId != 0)
            trustedDomainName = reader.ReadConformantVaryingString();

        // Read deferred SID
        string trustedDomainSid = "";
        if (sidPtr != 0)
        {
            var subAuthCount = reader.ReadUInt32(); // conformant max count
            trustedDomainSid = reader.ReadRpcSid();
        }

        // DesiredAccess (uint32)
        var desiredAccess = reader.ReadUInt32();

        _logger.LogDebug("LsarCreateTrustedDomain: Name={Name}, SID={Sid}", trustedDomainName, trustedDomainSid);

        // Create the trustedDomain object under CN=System,{domainDn}
        var systemDn = $"CN=System,{handle.DomainDn}";
        var tdDn = $"CN={trustedDomainName},{systemDn}";

        // Check if it already exists
        var existing = await _store.GetByDnAsync(handle.TenantId, tdDn, ct);
        if (existing != null)
        {
            var errWriter = new NdrWriter();
            errWriter.WriteContextHandle(0, Guid.Empty);
            errWriter.WriteUInt32(LsaConstants.StatusObjectNameCollision);
            return errWriter.ToArray();
        }

        var tdObj = new DirectoryObject
        {
            TenantId = handle.TenantId,
            DistinguishedName = tdDn,
            Cn = trustedDomainName,
            ObjectClass = ["top", "trustedDomain"],
            ObjectSid = trustedDomainSid,
        };
        tdObj.SetAttribute("trustPartner", new DirectoryAttribute("trustPartner", trustedDomainName));
        tdObj.SetAttribute("flatName", new DirectoryAttribute("flatName", trustedDomainName.Split('.')[0].ToUpperInvariant()));
        tdObj.SetAttribute("trustDirection", new DirectoryAttribute("trustDirection", LsaConstants.TrustDirectionBidirectional.ToString()));
        tdObj.SetAttribute("trustType", new DirectoryAttribute("trustType", LsaConstants.TrustTypeUplevel.ToString()));
        tdObj.SetAttribute("trustAttributes", new DirectoryAttribute("trustAttributes", "0"));

        await _store.CreateAsync(tdObj, ct);

        // Create a handle for the new trusted domain
        var tdHandleGuid = Guid.NewGuid();
        TdHandles[tdHandleGuid] = new LsaTrustedDomainHandle
        {
            TrustedDomainDn = tdDn,
            TrustedDomainSid = trustedDomainSid,
            TrustedDomainName = trustedDomainName,
            GrantedAccess = desiredAccess,
            TenantId = handle.TenantId,
            DomainDn = handle.DomainDn,
        };

        var writer = new NdrWriter();
        writer.WriteContextHandle(0, tdHandleGuid);
        writer.WriteUInt32(LsaConstants.StatusSuccess);

        return writer.ToArray();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Opnum 25: LsarOpenTrustedDomain
    // ─────────────────────────────────────────────────────────────────────

    public async Task<byte[]> LsarOpenTrustedDomainAsync(ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // Policy handle (20 bytes)
        var (attr, uuid) = reader.ReadContextHandle();
        var handle = GetPolicyHandle(context, attr, uuid);

        // TrustedDomainSid: pointer to RPC_SID (inline, not pointer — conformant array)
        var subAuthCount = reader.ReadUInt32(); // conformant max count
        var trustedDomainSid = reader.ReadRpcSid();

        // DesiredAccess (uint32)
        var desiredAccess = reader.ReadUInt32();

        _logger.LogDebug("LsarOpenTrustedDomain: SID={Sid}", trustedDomainSid);

        // Search for trustedDomain object by SID
        var systemDn = $"CN=System,{handle.DomainDn}";
        var searchResult = await _store.SearchAsync(
            handle.TenantId,
            systemDn,
            SearchScope.SingleLevel,
            new AndFilterNode([
                new EqualityFilterNode("objectClass", "trustedDomain"),
                new EqualityFilterNode("objectSid", trustedDomainSid),
            ]),
            new[] { "cn", "trustPartner", "objectSid" },
            sizeLimit: 1,
            ct: ct);

        if (searchResult.Entries.Count == 0)
        {
            var errWriter = new NdrWriter();
            errWriter.WriteContextHandle(0, Guid.Empty);
            errWriter.WriteUInt32(LsaConstants.StatusObjectNameNotFound);
            return errWriter.ToArray();
        }

        var tdObj = searchResult.Entries[0];

        var tdHandleGuid = Guid.NewGuid();
        TdHandles[tdHandleGuid] = new LsaTrustedDomainHandle
        {
            TrustedDomainDn = tdObj.DistinguishedName,
            TrustedDomainSid = trustedDomainSid,
            TrustedDomainName = tdObj.GetAttribute("trustPartner")?.GetFirstString() ?? tdObj.Cn ?? "",
            GrantedAccess = desiredAccess,
            TenantId = handle.TenantId,
            DomainDn = handle.DomainDn,
        };

        var writer = new NdrWriter();
        writer.WriteContextHandle(0, tdHandleGuid);
        writer.WriteUInt32(LsaConstants.StatusSuccess);

        return writer.ToArray();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Opnum 26: LsarQueryInfoTrustedDomain
    // ─────────────────────────────────────────────────────────────────────

    public async Task<byte[]> LsarQueryInfoTrustedDomainAsync(ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // Trusted domain handle (20 bytes)
        var (attr, uuid) = reader.ReadContextHandle();

        // InformationClass (ushort)
        var infoClass = reader.ReadUInt16();

        _logger.LogDebug("LsarQueryInfoTrustedDomain: handle={Handle}, infoClass={InfoClass}", uuid, infoClass);

        if (!TdHandles.TryGetValue(uuid, out var tdHandle))
        {
            var errWriter = new NdrWriter();
            errWriter.WritePointer(true); // null info
            errWriter.WriteUInt32(LsaConstants.StatusInvalidHandle);
            return errWriter.ToArray();
        }

        // Fetch the trusted domain object from the store
        var tdObj = await _store.GetByDnAsync(tdHandle.TenantId, tdHandle.TrustedDomainDn, ct);
        if (tdObj == null)
        {
            var errWriter = new NdrWriter();
            errWriter.WritePointer(true);
            errWriter.WriteUInt32(LsaConstants.StatusObjectNameNotFound);
            return errWriter.ToArray();
        }

        var writer = new NdrWriter();

        // Pointer to info union (non-null)
        writer.WritePointer(false);

        // Encapsulated union: switch discriminant (uint32) then arm data
        writer.WriteUInt32(infoClass);

        switch (infoClass)
        {
            case LsaConstants.TrustedDomainNameInformation:
                // LSAPR_TRUSTED_DOMAIN_NAME_INFO: Name (RPC_UNICODE_STRING)
                writer.WriteRpcUnicodeString(tdHandle.TrustedDomainName);
                writer.FlushDeferred();
                break;

            case LsaConstants.TrustedPosixOffsetInformation:
                // TRUSTED_POSIX_OFFSET_INFO: Offset (uint32)
                writer.WriteUInt32(0); // POSIX offset (not commonly used)
                break;

            case LsaConstants.TrustedDomainInformationBasic:
                // LSAPR_TRUSTED_DOMAIN_INFORMATION_BASIC: Name + Sid
                writer.WriteRpcUnicodeString(tdHandle.TrustedDomainName);
                writer.WritePointer(string.IsNullOrEmpty(tdHandle.TrustedDomainSid));
                writer.FlushDeferred();
                if (!string.IsNullOrEmpty(tdHandle.TrustedDomainSid))
                    writer.WriteRpcSid(tdHandle.TrustedDomainSid);
                break;

            case LsaConstants.TrustedDomainInformationEx:
                WriteTrustedDomainInfoEx(writer, tdObj, tdHandle);
                break;

            case LsaConstants.TrustedDomainAuthInformation:
                // LSAPR_TRUSTED_DOMAIN_AUTH_INFORMATION:
                //   IncomingAuthInfoSize (uint32), IncomingAuthInfo (pointer)
                //   OutgoingAuthInfoSize (uint32), OutgoingAuthInfo (pointer)
                writer.WriteUInt32(0); // IncomingAuthInfoSize
                writer.WritePointer(true); // null IncomingAuthInfo
                writer.WriteUInt32(0); // OutgoingAuthInfoSize
                writer.WritePointer(true); // null OutgoingAuthInfo
                break;

            case LsaConstants.TrustedDomainFullInformation:
                // LSAPR_TRUSTED_DOMAIN_FULL_INFORMATION:
                //   Information (TRUSTED_DOMAIN_INFORMATION_EX)
                //   PosixOffset (TRUSTED_POSIX_OFFSET_INFO)
                //   AuthInformation (LSAPR_TRUSTED_DOMAIN_AUTH_INFORMATION)
                WriteTrustedDomainInfoEx(writer, tdObj, tdHandle);
                // PosixOffset
                writer.WriteUInt32(0);
                // Auth info (empty)
                writer.WriteUInt32(0);
                writer.WritePointer(true);
                writer.WriteUInt32(0);
                writer.WritePointer(true);
                break;

            default:
                _logger.LogWarning("LsarQueryInfoTrustedDomain: unsupported infoClass {InfoClass}", infoClass);
                var errWriter2 = new NdrWriter();
                errWriter2.WritePointer(true);
                errWriter2.WriteUInt32(LsaConstants.StatusInvalidParameter);
                return errWriter2.ToArray();
        }

        writer.WriteUInt32(LsaConstants.StatusSuccess);
        return writer.ToArray();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Opnum 27: LsarSetInformationTrustedDomain
    // ─────────────────────────────────────────────────────────────────────

    public async Task<byte[]> LsarSetInformationTrustedDomainAsync(ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // Trusted domain handle (20 bytes)
        var (attr, uuid) = reader.ReadContextHandle();

        // InformationClass (ushort)
        var infoClass = reader.ReadUInt16();

        _logger.LogDebug("LsarSetInformationTrustedDomain: handle={Handle}, infoClass={InfoClass}", uuid, infoClass);

        if (!TdHandles.TryGetValue(uuid, out var tdHandle))
        {
            var errWriter = new NdrWriter();
            errWriter.WriteUInt32(LsaConstants.StatusInvalidHandle);
            return errWriter.ToArray();
        }

        var tdObj = await _store.GetByDnAsync(tdHandle.TenantId, tdHandle.TrustedDomainDn, ct);
        if (tdObj == null)
        {
            var errWriter = new NdrWriter();
            errWriter.WriteUInt32(LsaConstants.StatusObjectNameNotFound);
            return errWriter.ToArray();
        }

        switch (infoClass)
        {
            case LsaConstants.TrustedDomainNameInformation:
                // Read the new name
                var nameHeader = reader.ReadRpcUnicodeString();
                if (nameHeader.ReferentId != 0)
                {
                    var newName = reader.ReadConformantVaryingString();
                    tdObj.SetAttribute("trustPartner", new DirectoryAttribute("trustPartner", newName));
                    tdHandle.TrustedDomainName = newName;
                }
                break;

            case LsaConstants.TrustedPosixOffsetInformation:
                // TRUSTED_POSIX_OFFSET_INFO: Offset (uint32) — accept and store
                var posixOffset = reader.ReadUInt32();
                tdObj.SetAttribute("trustPosixOffset", new DirectoryAttribute("trustPosixOffset", posixOffset.ToString()));
                break;

            case LsaConstants.TrustedDomainInformationEx:
                // Read TRUSTED_DOMAIN_INFORMATION_EX fields
                ReadAndApplyTrustedDomainInfoEx(reader, tdObj);
                break;

            case LsaConstants.TrustedDomainAuthInformation:
                // Accept auth info update — in production this would store encrypted trust keys
                // For now, skip the auth data and return success
                _logger.LogInformation("LsarSetInformationTrustedDomain: auth info update for {Name}", tdHandle.TrustedDomainName);
                break;

            case LsaConstants.TrustedPasswordInformation:
                // Trust password — accept and log
                _logger.LogInformation("LsarSetInformationTrustedDomain: password update for {Name}", tdHandle.TrustedDomainName);
                break;

            default:
                _logger.LogWarning("LsarSetInformationTrustedDomain: unsupported infoClass {InfoClass}", infoClass);
                var errWriter2 = new NdrWriter();
                errWriter2.WriteUInt32(LsaConstants.StatusInvalidParameter);
                return errWriter2.ToArray();
        }

        await _store.UpdateAsync(tdObj, ct);

        var writer = new NdrWriter();
        writer.WriteUInt32(LsaConstants.StatusSuccess);
        return writer.ToArray();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

    private void WriteTrustedDomainInfoEx(NdrWriter writer, DirectoryObject tdObj, LsaTrustedDomainHandle tdHandle)
    {
        var trustPartner = tdObj.GetAttribute("trustPartner")?.GetFirstString() ?? tdHandle.TrustedDomainName;
        var flatName = tdObj.GetAttribute("flatName")?.GetFirstString() ?? trustPartner.Split('.')[0].ToUpperInvariant();

        int.TryParse(tdObj.GetAttribute("trustDirection")?.GetFirstString(), out var trustDirection);
        int.TryParse(tdObj.GetAttribute("trustType")?.GetFirstString(), out var trustType);
        int.TryParse(tdObj.GetAttribute("trustAttributes")?.GetFirstString(), out var trustAttributes);

        // LSAPR_TRUSTED_DOMAIN_INFORMATION_EX:
        //   Name (RPC_UNICODE_STRING)
        //   FlatName (RPC_UNICODE_STRING)
        //   Sid (pointer to RPC_SID)
        //   TrustDirection (uint32)
        //   TrustType (uint32)
        //   TrustAttributes (uint32)
        writer.WriteRpcUnicodeString(trustPartner);
        writer.WriteRpcUnicodeString(flatName);
        writer.WritePointer(string.IsNullOrEmpty(tdHandle.TrustedDomainSid));
        writer.WriteUInt32((uint)trustDirection);
        writer.WriteUInt32((uint)trustType);
        writer.WriteUInt32((uint)trustAttributes);

        writer.FlushDeferred();

        if (!string.IsNullOrEmpty(tdHandle.TrustedDomainSid))
            writer.WriteRpcSid(tdHandle.TrustedDomainSid);
    }

    private static void ReadAndApplyTrustedDomainInfoEx(NdrReader reader, DirectoryObject tdObj)
    {
        // LSAPR_TRUSTED_DOMAIN_INFORMATION_EX:
        //   Name (RPC_UNICODE_STRING)
        //   FlatName (RPC_UNICODE_STRING)
        //   Sid (pointer)
        //   TrustDirection (uint32)
        //   TrustType (uint32)
        //   TrustAttributes (uint32)

        // Read union discriminant
        var discriminant = reader.ReadUInt32();

        var nameHeader = reader.ReadRpcUnicodeString();
        var flatNameHeader = reader.ReadRpcUnicodeString();
        var sidPtr = reader.ReadPointer();
        var trustDirection = reader.ReadUInt32();
        var trustType = reader.ReadUInt32();
        var trustAttributes = reader.ReadUInt32();

        // Read deferred data
        if (nameHeader.ReferentId != 0)
        {
            var name = reader.ReadConformantVaryingString();
            tdObj.SetAttribute("trustPartner", new DirectoryAttribute("trustPartner", name));
        }
        if (flatNameHeader.ReferentId != 0)
        {
            var flatName = reader.ReadConformantVaryingString();
            tdObj.SetAttribute("flatName", new DirectoryAttribute("flatName", flatName));
        }
        if (sidPtr != 0)
        {
            var subAuthCount = reader.ReadUInt32();
            var sid = reader.ReadRpcSid();
            tdObj.ObjectSid = sid;
        }

        tdObj.SetAttribute("trustDirection", new DirectoryAttribute("trustDirection", trustDirection.ToString()));
        tdObj.SetAttribute("trustType", new DirectoryAttribute("trustType", trustType.ToString()));
        tdObj.SetAttribute("trustAttributes", new DirectoryAttribute("trustAttributes", trustAttributes.ToString()));
    }

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
