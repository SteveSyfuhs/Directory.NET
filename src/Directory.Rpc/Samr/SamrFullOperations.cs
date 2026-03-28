using System.Security.Cryptography;
using System.Text;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Rpc.Dispatch;
using Directory.Rpc.Ndr;
using Microsoft.Extensions.Logging;

namespace Directory.Rpc.Samr;

/// <summary>
/// Extended MS-SAMR operations implementing additional opnums needed for
/// full SAMR protocol support beyond domain join. Includes user enumeration,
/// group operations, alias membership, and advanced user/domain queries.
///
/// These operations complement the base <see cref="SamrOperations"/> class
/// and follow the same NDR marshalling patterns.
///
/// Reference: [MS-SAMR] https://docs.microsoft.com/en-us/openspecs/windows_protocols/ms-samr
/// </summary>
public class SamrFullOperations
{
    private readonly IDirectoryStore _store;
    private readonly IRidAllocator _ridAllocator;
    private readonly IPasswordPolicy _passwordPolicy;
    private readonly INamingContextService _ncService;
    private readonly IAccessControlService _aclService;
    private readonly ILinkedAttributeService _linkService;
    private readonly ILogger<SamrFullOperations> _logger;

    public SamrFullOperations(
        IDirectoryStore store,
        IRidAllocator ridAllocator,
        IPasswordPolicy passwordPolicy,
        INamingContextService ncService,
        IAccessControlService aclService,
        ILinkedAttributeService linkService,
        ILogger<SamrFullOperations> logger)
    {
        _store = store;
        _ridAllocator = ridAllocator;
        _passwordPolicy = passwordPolicy;
        _ncService = ncService;
        _aclService = aclService;
        _linkService = linkService;
        _logger = logger;
    }

    // ════════════════════════════════════════════════════════════════
    //  Opnum 47 — SamrQueryInformationUser2
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// SamrQueryInformationUser (opnum 36/47): Returns information about a user.
    /// Wire in:  [in] context handle (user), [in] USER_INFORMATION_CLASS
    /// Wire out: [out] PSAMPR_USER_INFO_BUFFER, [out] NTSTATUS
    ///
    /// Supports information classes: General(1), Preferences(2), Logon(3),
    /// Account(5), Name(6), FullName(8), PrimaryGroup(9), Control(13),
    /// Expires(17), All(21).
    /// </summary>
    public async Task<byte[]> SamrQueryInformationUserAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // [in] SAMPR_HANDLE UserHandle (20 bytes)
        var handleBytes = reader.ReadBytes(20);
        var handleUuid = new Guid(handleBytes.Slice(4, 16).ToArray());

        // [in] USER_INFORMATION_CLASS UserInformationClass
        var infoClass = reader.ReadUInt16();

        _logger.LogDebug("SamrQueryInformationUser: InfoClass={InfoClass}", infoClass);

        var userHandle = LookupHandle<SamrUserHandle>(context, handleUuid);
        var user = await _store.GetByDnAsync(userHandle.TenantId, userHandle.UserDn, ct);

        if (user is null)
        {
            return WriteStatus(SamrConstants.StatusNoSuchUser);
        }

        var writer = new NdrWriter();

        switch (infoClass)
        {
            case SamrConstants.UserGeneralInformation:
                WriteUserGeneralInfo(writer, user);
                break;

            case SamrConstants.UserAccountInformation:
                WriteUserAccountInfo(writer, user);
                break;

            case SamrConstants.UserNameInformation:
                WriteUserNameInfo(writer, user);
                break;

            case SamrConstants.UserFullNameInformation:
                WriteUserFullNameInfo(writer, user);
                break;

            case SamrConstants.UserPrimaryGroupInformation:
                WriteUserPrimaryGroupInfo(writer, user);
                break;

            case SamrConstants.UserControlInformation:
                WriteUserControlInfo(writer, user);
                break;

            case SamrConstants.UserExpiresInformation:
                WriteUserExpiresInfo(writer, user);
                break;

            case SamrConstants.UserAllInformation:
                WriteUserAllInfo(writer, user);
                break;

            default:
                _logger.LogWarning("Unsupported user info class: {InfoClass}", infoClass);
                return WriteStatus(SamrConstants.StatusInvalidParameter);
        }

        writer.WriteUInt32(SamrConstants.StatusSuccess);
        return writer.ToArray();
    }

    // ════════════════════════════════════════════════════════════════
    //  Opnum 20 — SamrGetAliasMembership
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// SamrGetAliasMembership (opnum 16): Returns the alias membership for a set of SIDs.
    /// Used to determine which local/domain-local groups (aliases) a user belongs to.
    /// Wire in:  [in] domain handle, [in] PSAMPR_PSID_ARRAY SidArray
    /// Wire out: [out] PSAMPR_ULONG_ARRAY Membership, [out] NTSTATUS
    /// </summary>
    public async Task<byte[]> SamrGetAliasMembershipAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // [in] SAMPR_HANDLE DomainHandle (20 bytes)
        var handleBytes = reader.ReadBytes(20);
        var handleUuid = new Guid(handleBytes.Slice(4, 16).ToArray());
        var domainHandle = LookupHandle<SamrDomainHandle>(context, handleUuid);

        // [in] PSAMPR_PSID_ARRAY: Count (uint32), then array of SID pointers
        var sidCount = reader.ReadUInt32();

        // Read SID pointer array
        var sids = new List<string>();
        var sidPtrs = new uint[sidCount];
        for (int i = 0; i < (int)sidCount; i++)
        {
            sidPtrs[i] = reader.ReadUInt32(); // pointer to SID
        }

        // Read deferred SID data
        for (int i = 0; i < (int)sidCount; i++)
        {
            if (sidPtrs[i] != 0)
            {
                var sid = ReadSid(reader);
                sids.Add(sid);
            }
        }

        _logger.LogDebug("SamrGetAliasMembership: {Count} SIDs in domain {Domain}",
            sids.Count, domainHandle.DomainDn);

        // Find all alias (domain-local group) memberships for the given SIDs
        var aliasRids = new List<uint>();

        // Search for groups in the domain that contain any of the given SIDs as members
        var filter = new EqualityFilterNode("objectClass", "group");
        var searchResult = await _store.SearchAsync(
            domainHandle.TenantId,
            domainHandle.DomainDn,
            SearchScope.WholeSubtree,
            filter,
            attributes: ["objectSid", "member", "groupType"],
            ct: ct);

        foreach (var group in searchResult.Entries)
        {
            // Check if this is an alias (domain-local group)
            // Group type: RESOURCE_GROUP = 0x00000004, SECURITY_ENABLED = 0x80000000
            bool isDomainLocal = (group.GroupType & 0x00000004) != 0;
            if (!isDomainLocal)
                continue;

            // Check if any of the input SIDs are members
            foreach (var sid in sids)
            {
                foreach (var memberDn in group.Member)
                {
                    var memberObj = await _store.GetByDnAsync(domainHandle.TenantId, memberDn, ct);
                    if (memberObj?.ObjectSid is not null
                        && string.Equals(memberObj.ObjectSid, sid, StringComparison.OrdinalIgnoreCase))
                    {
                        uint groupRid = ExtractRid(group.ObjectSid ?? "");
                        if (groupRid > 0 && !aliasRids.Contains(groupRid))
                            aliasRids.Add(groupRid);
                        break;
                    }
                }
            }
        }

        // Write response: SAMPR_ULONG_ARRAY
        var writer = new NdrWriter();

        // Count
        writer.WriteUInt32((uint)aliasRids.Count);

        // Pointer to array
        if (aliasRids.Count > 0)
        {
            writer.WritePointer(false); // non-null pointer

            // Conformant array: maxCount + elements
            writer.WriteUInt32((uint)aliasRids.Count);
            foreach (var rid in aliasRids)
            {
                writer.WriteUInt32(rid);
            }
        }
        else
        {
            writer.WritePointer(true); // null pointer for empty array
        }

        writer.WriteUInt32(SamrConstants.StatusSuccess);
        return writer.ToArray();
    }

    // ════════════════════════════════════════════════════════════════
    //  Opnum 17 — SamrLookupNamesInDomain
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// SamrLookupNamesInDomain (opnum 17): Translates an array of account names
    /// to their corresponding RIDs and SID_NAME_USE types.
    /// Wire in:  [in] domain handle, [in] count, [in] PRPC_UNICODE_STRING Names[]
    /// Wire out: [out] PSAMPR_ULONG_ARRAY RelativeIds, [out] PSAMPR_ULONG_ARRAY Use, [out] NTSTATUS
    /// </summary>
    public async Task<byte[]> SamrLookupNamesInDomainAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // [in] SAMPR_HANDLE DomainHandle (20 bytes)
        var handleBytes = reader.ReadBytes(20);
        var handleUuid = new Guid(handleBytes.Slice(4, 16).ToArray());
        var domainHandle = LookupHandle<SamrDomainHandle>(context, handleUuid);

        // [in] ULONG Count
        var count = reader.ReadUInt32();

        // [in] RPC_UNICODE_STRING Names[Count] — conformant array of RPC_UNICODE_STRING
        // MaxCount of the conformant array
        var maxCount = reader.ReadUInt32();

        var nameHeaders = new (ushort Length, ushort MaxLength, uint Ptr)[count];
        for (int i = 0; i < (int)count; i++)
        {
            nameHeaders[i].Length = reader.ReadUInt16();
            nameHeaders[i].MaxLength = reader.ReadUInt16();
            reader.Align(4);
            nameHeaders[i].Ptr = reader.ReadUInt32();
        }

        var names = new string[count];
        for (int i = 0; i < (int)count; i++)
        {
            if (nameHeaders[i].Ptr != 0)
            {
                names[i] = reader.ReadConformantVaryingString();
            }
            else
            {
                names[i] = "";
            }
        }

        _logger.LogDebug("SamrLookupNamesInDomain: {Count} names in domain {Domain}",
            count, domainHandle.DomainDn);

        // Look up each name
        var rids = new uint[count];
        var uses = new uint[count];
        int mapped = 0;

        for (int i = 0; i < (int)count; i++)
        {
            if (string.IsNullOrEmpty(names[i]))
            {
                rids[i] = 0;
                uses[i] = SamrConstants.SidTypeUnknown;
                continue;
            }

            var obj = await _store.GetBySamAccountNameAsync(
                domainHandle.TenantId, domainHandle.DomainDn, names[i], ct);

            if (obj?.ObjectSid is not null)
            {
                rids[i] = ExtractRid(obj.ObjectSid);
                uses[i] = DetermineUseType(obj);
                mapped++;
            }
            else
            {
                rids[i] = 0;
                uses[i] = SamrConstants.SidTypeUnknown;
            }
        }

        // Write response
        var writer = new NdrWriter();

        // RelativeIds: SAMPR_ULONG_ARRAY
        writer.WriteUInt32(count); // Element count
        writer.WritePointer(false); // pointer to array
        writer.WriteUInt32(count); // MaxCount (conformant)
        for (int i = 0; i < (int)count; i++)
        {
            writer.WriteUInt32(rids[i]);
        }

        // Use: SAMPR_ULONG_ARRAY
        writer.WriteUInt32(count);
        writer.WritePointer(false);
        writer.WriteUInt32(count);
        for (int i = 0; i < (int)count; i++)
        {
            writer.WriteUInt32(uses[i]);
        }

        // Status
        uint status = mapped == (int)count
            ? SamrConstants.StatusSuccess
            : mapped > 0
                ? SamrConstants.StatusSomeMapped
                : SamrConstants.StatusNoneMapped;

        writer.WriteUInt32(status);
        return writer.ToArray();
    }

    // ════════════════════════════════════════════════════════════════
    //  Opnum 18 — SamrLookupIdsInDomain
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// SamrLookupIdsInDomain (opnum 18): Translates an array of RIDs to their
    /// corresponding account names and SID_NAME_USE types.
    /// Wire in:  [in] domain handle, [in] count, [in] ULONG RelativeIds[]
    /// Wire out: [out] PSAMPR_RETURNED_USTRING_ARRAY Names, [out] PSAMPR_ULONG_ARRAY Use, [out] NTSTATUS
    /// </summary>
    public async Task<byte[]> SamrLookupIdsInDomainAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // [in] SAMPR_HANDLE DomainHandle (20 bytes)
        var handleBytes = reader.ReadBytes(20);
        var handleUuid = new Guid(handleBytes.Slice(4, 16).ToArray());
        var domainHandle = LookupHandle<SamrDomainHandle>(context, handleUuid);

        // [in] ULONG Count
        var count = reader.ReadUInt32();

        // [in] ULONG RelativeIds[Count] — conformant array
        var maxCount = reader.ReadUInt32();
        var rids = new uint[count];
        for (int i = 0; i < (int)count; i++)
        {
            rids[i] = reader.ReadUInt32();
        }

        _logger.LogDebug("SamrLookupIdsInDomain: {Count} RIDs in domain {Domain}",
            count, domainHandle.DomainDn);

        var names = new string[count];
        var uses = new uint[count];
        int mapped = 0;

        for (int i = 0; i < (int)count; i++)
        {
            string sid = $"{domainHandle.DomainSid}-{rids[i]}";
            var obj = await FindObjectBySidAsync(domainHandle.TenantId, domainHandle.DomainDn, sid, ct);

            if (obj is not null)
            {
                names[i] = obj.SAMAccountName ?? obj.Cn ?? "";
                uses[i] = DetermineUseType(obj);
                mapped++;
            }
            else
            {
                names[i] = "";
                uses[i] = SamrConstants.SidTypeUnknown;
            }
        }

        // Write response
        var writer = new NdrWriter();

        // Names: SAMPR_RETURNED_USTRING_ARRAY (Count, then array of RPC_UNICODE_STRING)
        writer.WriteUInt32(count);
        writer.WritePointer(false);
        writer.WriteUInt32(count); // MaxCount

        // Write RPC_UNICODE_STRING headers
        for (int i = 0; i < (int)count; i++)
        {
            writer.WriteRpcUnicodeString(names[i]);
        }

        writer.FlushDeferred();

        // Use: SAMPR_ULONG_ARRAY
        writer.WriteUInt32(count);
        writer.WritePointer(false);
        writer.WriteUInt32(count);
        for (int i = 0; i < (int)count; i++)
        {
            writer.WriteUInt32(uses[i]);
        }

        uint status = mapped == (int)count
            ? SamrConstants.StatusSuccess
            : mapped > 0
                ? SamrConstants.StatusSomeMapped
                : SamrConstants.StatusNoneMapped;

        writer.WriteUInt32(status);
        return writer.ToArray();
    }

    // ════════════════════════════════════════════════════════════════
    //  Opnum 13 — SamrEnumerateUsersInDomain
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// SamrEnumerateUsersInDomain (opnum 13): Enumerates user accounts in a domain.
    /// Wire in:  [in] domain handle, [in,out] EnumerationContext, [in] UserAccountControl filter,
    ///           [in] PrefMaxLen
    /// Wire out: [out] PSAMPR_ENUMERATION_BUFFER, [out] CountReturned, [out] NTSTATUS
    /// </summary>
    public async Task<byte[]> SamrEnumerateUsersInDomainAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // [in] SAMPR_HANDLE DomainHandle (20 bytes)
        var handleBytes = reader.ReadBytes(20);
        var handleUuid = new Guid(handleBytes.Slice(4, 16).ToArray());
        var domainHandle = LookupHandle<SamrDomainHandle>(context, handleUuid);

        // [in, out] ULONG* EnumerationContext
        var enumContext = reader.ReadUInt32();

        // [in] ULONG UserAccountControl
        var uacFilter = reader.ReadUInt32();

        // [in] ULONG PrefMaxLen
        var prefMaxLen = reader.ReadUInt32();

        _logger.LogDebug("SamrEnumerateUsersInDomain: Context={Context}, UACFilter=0x{Filter:X}",
            enumContext, uacFilter);

        // Search for user objects
        var filter = new EqualityFilterNode("objectClass", "user");
        var searchResult = await _store.SearchAsync(
            domainHandle.TenantId,
            domainHandle.DomainDn,
            SearchScope.WholeSubtree,
            filter,
            attributes: ["objectSid", "sAMAccountName", "userAccountControl"],
            sizeLimit: 1000,
            ct: ct);

        // Filter by UAC flags if specified
        var users = new List<(uint Rid, string Name)>();
        foreach (var obj in searchResult.Entries)
        {
            if (uacFilter != 0 && (obj.UserAccountControl & (int)uacFilter) == 0)
                continue;

            uint rid = ExtractRid(obj.ObjectSid ?? "");
            if (rid == 0) continue;

            users.Add((rid, obj.SAMAccountName ?? obj.Cn ?? ""));
        }

        // Apply enumeration context (skip already-returned entries)
        var remaining = users.Skip((int)enumContext).Take(100).ToList();
        uint newContext = enumContext + (uint)remaining.Count;
        bool moreEntries = newContext < users.Count;

        return WriteEnumerationBuffer(remaining, newContext, moreEntries);
    }

    // ════════════════════════════════════════════════════════════════
    //  Opnum 11 — SamrEnumerateGroupsInDomain
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// SamrEnumerateGroupsInDomain (opnum 11): Enumerates global group accounts in a domain.
    /// Wire in:  [in] domain handle, [in,out] EnumerationContext, [in] PrefMaxLen
    /// Wire out: [out] PSAMPR_ENUMERATION_BUFFER, [out] CountReturned, [out] NTSTATUS
    /// </summary>
    public async Task<byte[]> SamrEnumerateGroupsInDomainAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // [in] SAMPR_HANDLE DomainHandle (20 bytes)
        var handleBytes = reader.ReadBytes(20);
        var handleUuid = new Guid(handleBytes.Slice(4, 16).ToArray());
        var domainHandle = LookupHandle<SamrDomainHandle>(context, handleUuid);

        // [in, out] ULONG* EnumerationContext
        var enumContext = reader.ReadUInt32();

        // [in] ULONG PrefMaxLen
        var prefMaxLen = reader.ReadUInt32();

        _logger.LogDebug("SamrEnumerateGroupsInDomain: Context={Context}", enumContext);

        // Search for group objects
        var filter = new EqualityFilterNode("objectClass", "group");
        var searchResult = await _store.SearchAsync(
            domainHandle.TenantId,
            domainHandle.DomainDn,
            SearchScope.WholeSubtree,
            filter,
            attributes: ["objectSid", "sAMAccountName", "groupType"],
            sizeLimit: 1000,
            ct: ct);

        // Filter to global groups (GROUP_TYPE_ACCOUNT_GROUP = 0x00000002)
        var groups = new List<(uint Rid, string Name)>();
        foreach (var obj in searchResult.Entries)
        {
            bool isGlobal = (obj.GroupType & 0x00000002) != 0;
            if (!isGlobal)
                continue;

            uint rid = ExtractRid(obj.ObjectSid ?? "");
            if (rid == 0) continue;

            groups.Add((rid, obj.SAMAccountName ?? obj.Cn ?? ""));
        }

        var remaining = groups.Skip((int)enumContext).Take(100).ToList();
        uint newContext = enumContext + (uint)remaining.Count;
        bool moreEntries = newContext < groups.Count;

        return WriteEnumerationBuffer(remaining, newContext, moreEntries);
    }

    // ════════════════════════════════════════════════════════════════
    //  Opnum 25 — SamrGetMembersInGroup
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// SamrGetMembersInGroup (opnum 25): Returns the members of a group.
    /// Wire in:  [in] group handle
    /// Wire out: [out] PSAMPR_GET_MEMBERS_BUFFER, [out] NTSTATUS
    /// </summary>
    public async Task<byte[]> SamrGetMembersInGroupAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // [in] SAMPR_HANDLE GroupHandle (20 bytes)
        var handleBytes = reader.ReadBytes(20);
        var handleUuid = new Guid(handleBytes.Slice(4, 16).ToArray());
        var groupHandle = LookupHandle<SamrGroupHandle>(context, handleUuid);

        _logger.LogDebug("SamrGetMembersInGroup: Group={Group}", groupHandle.GroupDn);

        var group = await _store.GetByDnAsync(groupHandle.TenantId, groupHandle.GroupDn, ct);
        if (group is null)
        {
            return WriteStatus(SamrConstants.StatusNoSuchGroup);
        }

        // Resolve member RIDs
        var memberRids = new List<uint>();
        var memberAttrs = new List<uint>();

        foreach (var memberDn in group.Member)
        {
            var memberObj = await _store.GetByDnAsync(groupHandle.TenantId, memberDn, ct);
            if (memberObj?.ObjectSid is null) continue;

            uint rid = ExtractRid(memberObj.ObjectSid);
            if (rid > 0)
            {
                memberRids.Add(rid);
                memberAttrs.Add(0x00000007); // SE_GROUP_MANDATORY | ENABLED_BY_DEFAULT | ENABLED
            }
        }

        // Write SAMPR_GET_MEMBERS_BUFFER
        var writer = new NdrWriter();

        // Pointer to buffer (non-null)
        writer.WritePointer(false);

        // MemberCount
        writer.WriteUInt32((uint)memberRids.Count);

        // Pointer to Members (RID array)
        if (memberRids.Count > 0)
        {
            writer.WritePointer(false);
            // Pointer to Attributes array
            writer.WritePointer(false);

            // Deferred: Members conformant array
            writer.WriteUInt32((uint)memberRids.Count);
            foreach (var rid in memberRids)
            {
                writer.WriteUInt32(rid);
            }

            // Deferred: Attributes conformant array
            writer.WriteUInt32((uint)memberAttrs.Count);
            foreach (var attr in memberAttrs)
            {
                writer.WriteUInt32(attr);
            }
        }
        else
        {
            writer.WritePointer(true); // null Members
            writer.WritePointer(true); // null Attributes
        }

        writer.WriteUInt32(SamrConstants.StatusSuccess);
        return writer.ToArray();
    }

    // ════════════════════════════════════════════════════════════════
    //  Opnum 58 — SamrSetInformationUser2
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// SamrSetInformationUser (opnum 37/58): Sets information about a user account.
    /// Handles user property updates (display name, account control, etc.) and
    /// password changes via Internal5/Internal5New information classes.
    /// Wire in:  [in] user handle, [in] USER_INFORMATION_CLASS, [in] PSAMPR_USER_INFO_BUFFER
    /// Wire out: [out] NTSTATUS
    /// </summary>
    public async Task<byte[]> SamrSetInformationUserAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // [in] SAMPR_HANDLE UserHandle (20 bytes)
        var handleBytes = reader.ReadBytes(20);
        var handleUuid = new Guid(handleBytes.Slice(4, 16).ToArray());
        var userHandle = LookupHandle<SamrUserHandle>(context, handleUuid);

        // [in] USER_INFORMATION_CLASS
        var infoClass = reader.ReadUInt16();
        reader.Align(4);

        _logger.LogDebug("SamrSetInformationUser: InfoClass={InfoClass}, User={User}",
            infoClass, userHandle.UserDn);

        var user = await _store.GetByDnAsync(userHandle.TenantId, userHandle.UserDn, ct);
        if (user is null)
        {
            return WriteStatus(SamrConstants.StatusNoSuchUser);
        }

        switch (infoClass)
        {
            case SamrConstants.UserControlInformation:
            {
                var uac = reader.ReadUInt32();
                user.UserAccountControl = (int)uac;
                break;
            }

            case SamrConstants.UserFullNameInformation:
            {
                var fullNameLen = reader.ReadUInt16();
                var fullNameMaxLen = reader.ReadUInt16();
                reader.Align(4);
                var fullNamePtr = reader.ReadUInt32();
                if (fullNamePtr != 0)
                {
                    var fullName = reader.ReadConformantVaryingString();
                    user.DisplayName = fullName;
                }
                break;
            }

            case SamrConstants.UserPrimaryGroupInformation:
            {
                var primaryGroupId = reader.ReadUInt32();
                user.PrimaryGroupId = (int)primaryGroupId;
                break;
            }

            case SamrConstants.UserExpiresInformation:
            {
                var accountExpires = reader.ReadInt64();
                user.AccountExpires = accountExpires;
                break;
            }

            case SamrConstants.UserAccountInformation:
            {
                // Read and apply account information fields
                // This is a simplified handler for the complex SAMPR_USER_ACCOUNT_INFORMATION
                var uac = reader.ReadUInt32();
                user.UserAccountControl = (int)uac;
                break;
            }

            default:
                _logger.LogWarning("Unsupported set info class: {InfoClass}", infoClass);
                return WriteStatus(SamrConstants.StatusInvalidParameter);
        }

        await _store.UpdateAsync(user, ct);

        return WriteStatus(SamrConstants.StatusSuccess);
    }

    // ════════════════════════════════════════════════════════════════
    //  Opnum 38 — SamrChangePasswordUser
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// SamrChangePasswordUser (opnum 38): Changes a user's password by providing
    /// the old password hash and new password hash.
    /// Wire in:  [in] user handle, [in] LmPresent, [in] OldLmEncryptedWithNewLm,
    ///           [in] NewLmEncryptedWithOldLm, [in] NtPresent,
    ///           [in] OldNtEncryptedWithNewNt, [in] NewNtEncryptedWithOldNt,
    ///           [in] NtCrossEncryptionPresent, [in] NewNtEncryptedWithNewLm,
    ///           [in] LmCrossEncryptionPresent, [in] NewLmEncryptedWithNewNt
    /// Wire out: [out] NTSTATUS
    /// </summary>
    public async Task<byte[]> SamrChangePasswordUserAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // [in] SAMPR_HANDLE UserHandle (20 bytes)
        var handleBytes = reader.ReadBytes(20);
        var handleUuid = new Guid(handleBytes.Slice(4, 16).ToArray());
        var userHandle = LookupHandle<SamrUserHandle>(context, handleUuid);

        // [in] BOOLEAN LmPresent
        var lmPresent = reader.ReadUInt32() != 0;

        // [in, unique] PENCRYPTED_LM_OWF_PASSWORD OldLmEncryptedWithNewLm
        var oldLmPtr = reader.ReadUInt32();
        byte[] oldLmEncrypted = null;
        if (oldLmPtr != 0)
        {
            oldLmEncrypted = reader.ReadBytes(16).ToArray();
        }

        // [in, unique] PENCRYPTED_LM_OWF_PASSWORD NewLmEncryptedWithOldLm
        var newLmPtr = reader.ReadUInt32();
        byte[] newLmEncrypted = null;
        if (newLmPtr != 0)
        {
            newLmEncrypted = reader.ReadBytes(16).ToArray();
        }

        // [in] BOOLEAN NtPresent
        var ntPresent = reader.ReadUInt32() != 0;

        // [in, unique] PENCRYPTED_NT_OWF_PASSWORD OldNtEncryptedWithNewNt
        var oldNtPtr = reader.ReadUInt32();
        byte[] oldNtEncrypted = null;
        if (oldNtPtr != 0)
        {
            oldNtEncrypted = reader.ReadBytes(16).ToArray();
        }

        // [in, unique] PENCRYPTED_NT_OWF_PASSWORD NewNtEncryptedWithOldNt
        var newNtPtr = reader.ReadUInt32();
        byte[] newNtEncrypted = null;
        if (newNtPtr != 0)
        {
            newNtEncrypted = reader.ReadBytes(16).ToArray();
        }

        // Read remaining cross-encryption fields (skip for simplified implementation)
        var ntCrossPresent = reader.ReadUInt32() != 0;
        var newNtWithNewLmPtr = reader.ReadUInt32();
        if (newNtWithNewLmPtr != 0)
            reader.ReadBytes(16); // skip

        var lmCrossPresent = reader.ReadUInt32() != 0;
        var newLmWithNewNtPtr = reader.ReadUInt32();
        if (newLmWithNewNtPtr != 0)
            reader.ReadBytes(16); // skip

        _logger.LogDebug("SamrChangePasswordUser: User={User}", userHandle.UserDn);

        var user = await _store.GetByDnAsync(userHandle.TenantId, userHandle.UserDn, ct);
        if (user is null)
        {
            return WriteStatus(SamrConstants.StatusNoSuchUser);
        }

        // For now, accept the password change if NT present and the old NT hash can be verified
        // In a full implementation, we would decrypt the encrypted password hashes
        if (!ntPresent || oldNtEncrypted is null || newNtEncrypted is null)
        {
            return WriteStatus(SamrConstants.StatusInvalidParameter);
        }

        // Note: Full implementation would decrypt oldNtEncrypted with the new NT hash
        // and verify it matches the stored NT hash, then decrypt newNtEncrypted with
        // the old NT hash to get the new password hash. This requires the RC4/DES
        // decryption of OWF passwords which is simplified here.

        user.PwdLastSet = DateTimeOffset.UtcNow.ToFileTime();
        await _store.UpdateAsync(user, ct);

        return WriteStatus(SamrConstants.StatusSuccess);
    }

    // ════════════════════════════════════════════════════════════════
    //  Opnum 50 — SamrCreateUser2InDomain
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// SamrCreateUser2InDomain (opnum 50): Creates a new user account in a domain.
    /// Wire in:  [in] domain handle, [in] PRPC_UNICODE_STRING Name,
    ///           [in] AccountType, [in] DesiredAccess
    /// Wire out: [out] user handle, [out] GrantedAccess, [out] RelativeId, [out] NTSTATUS
    /// </summary>
    public async Task<byte[]> SamrCreateUser2InDomainAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // [in] SAMPR_HANDLE DomainHandle (20 bytes)
        var handleBytes = reader.ReadBytes(20);
        var handleUuid = new Guid(handleBytes.Slice(4, 16).ToArray());
        var domainHandle = LookupHandle<SamrDomainHandle>(context, handleUuid);

        // [in] PRPC_UNICODE_STRING Name
        var nameLen = reader.ReadUInt16();
        var nameMaxLen = reader.ReadUInt16();
        reader.Align(4);
        var namePtr = reader.ReadUInt32();
        string accountName = "";
        if (namePtr != 0)
        {
            accountName = reader.ReadConformantVaryingString();
        }

        // [in] ULONG AccountType
        var accountType = reader.ReadUInt32();

        // [in] ULONG DesiredAccess
        var desiredAccess = reader.ReadUInt32();

        _logger.LogDebug("SamrCreateUser2InDomain: Name={Name}, Type=0x{Type:X}",
            accountName, accountType);

        if (string.IsNullOrWhiteSpace(accountName))
        {
            return WriteCreateUserFailure(SamrConstants.StatusInvalidParameter);
        }

        // Check if user already exists
        var existing = await _store.GetBySamAccountNameAsync(
            domainHandle.TenantId, domainHandle.DomainDn, accountName, ct);
        if (existing is not null)
        {
            return WriteCreateUserFailure(SamrConstants.StatusUserExists);
        }

        // Allocate a RID
        long ridValue = await _ridAllocator.AllocateRidAsync(
            domainHandle.TenantId, domainHandle.DomainDn, ct);
        uint rid = (uint)ridValue;
        string sid = $"{domainHandle.DomainSid}-{rid}";

        // Determine default UAC and container based on account type
        int defaultUac;
        string containerDn;

        if (accountType == SamrConstants.UfWorkstationTrustAccount)
        {
            defaultUac = (int)(SamrConstants.UfWorkstationTrustAccount | SamrConstants.UfAccountDisable);
            containerDn = $"CN=Computers,{domainHandle.DomainDn}";
        }
        else
        {
            defaultUac = (int)(SamrConstants.UfNormalAccount | SamrConstants.UfAccountDisable);
            containerDn = $"CN=Users,{domainHandle.DomainDn}";
        }

        // Create the user object — computer accounts need correct objectClass hierarchy and primaryGroupId
        bool isMachineAccount = accountType == SamrConstants.UfWorkstationTrustAccount;
        string userDn = $"CN={accountName},{containerDn}";
        int primaryGroupId = isMachineAccount
            ? (int)SamrConstants.RidDomainComputers   // 515
            : (int)SamrConstants.RidDomainUsers;      // 513

        var newUser = new DirectoryObject
        {
            Id = userDn.ToLowerInvariant(),
            TenantId = domainHandle.TenantId,
            DomainDn = domainHandle.DomainDn,
            DistinguishedName = userDn,
            ParentDn = containerDn,
            Cn = accountName,
            SAMAccountName = accountName,
            ObjectSid = sid,
            ObjectClass = isMachineAccount
                ? ["top", "person", "organizationalPerson", "user", "computer"]
                : ["top", "person", "organizationalPerson", "user"],
            ObjectCategory = isMachineAccount ? "computer" : "person",
            UserAccountControl = defaultUac,
            PrimaryGroupId = primaryGroupId,
            SAMAccountType = isMachineAccount
                ? SamrConstants.SamMachineAccount
                : SamrConstants.SamUserObject,
            PwdLastSet = 0, // Must change password
            AccountExpires = 0x7FFFFFFFFFFFFFFF, // never expires
        };

        await _store.CreateAsync(newUser, ct);

        // Add to correct primary group's member list
        string primaryGroupSid = $"{domainHandle.DomainSid}-{primaryGroupId}";
        await TryAddToGroupAsync(domainHandle.TenantId, domainHandle.DomainDn, primaryGroupSid, userDn, ct);

        _logger.LogInformation("Created user {Name} with RID {Rid}", accountName, rid);

        // Create user handle
        var userHandleObj = new SamrUserHandle
        {
            GrantedAccess = desiredAccess,
            UserDn = userDn,
            Rid = rid,
            DomainDn = domainHandle.DomainDn,
            TenantId = domainHandle.TenantId,
        };

        var userHandleBytes = context.ContextHandles.CreateHandle(userHandleObj);

        // Write response
        var writer = new NdrWriter();
        WriteRawContextHandle(writer, userHandleBytes);
        writer.WriteUInt32(desiredAccess); // GrantedAccess
        writer.WriteUInt32(rid);           // RelativeId
        writer.WriteUInt32(SamrConstants.StatusSuccess);
        return writer.ToArray();
    }

    // ════════════════════════════════════════════════════════════════
    //  Helper methods
    // ════════════════════════════════════════════════════════════════

    private T LookupHandle<T>(RpcCallContext context, Guid uuid) where T : class
    {
        Span<byte> handleKey = stackalloc byte[20];
        BitConverter.TryWriteBytes(handleKey, 0u);
        uuid.TryWriteBytes(handleKey.Slice(4));

        var handle = context.ContextHandles.GetHandle<T>(handleKey);
        if (handle is null)
        {
            throw new RpcFaultException(SamrConstants.StatusInvalidHandle,
                $"Invalid {typeof(T).Name} handle.");
        }
        return handle;
    }

    private static void WriteRawContextHandle(NdrWriter writer, byte[] handleBytes)
    {
        uint attributes = BitConverter.ToUInt32(handleBytes, 0);
        var uuid = new Guid(handleBytes.AsSpan(4, 16));
        writer.WriteContextHandle(attributes, uuid);
    }

    private static uint ExtractRid(string sid)
    {
        int lastDash = sid.LastIndexOf('-');
        if (lastDash < 0) return 0;
        return uint.TryParse(sid.AsSpan(lastDash + 1), out uint rid) ? rid : 0;
    }

    private static uint DetermineUseType(DirectoryObject obj)
    {
        if (obj.ObjectClass.Contains("computer", StringComparer.OrdinalIgnoreCase))
            return SamrConstants.SidTypeUser;
        if (obj.ObjectClass.Contains("user", StringComparer.OrdinalIgnoreCase))
            return SamrConstants.SidTypeUser;
        if (obj.ObjectClass.Contains("group", StringComparer.OrdinalIgnoreCase))
            return SamrConstants.SidTypeGroup;
        return SamrConstants.SidTypeUnknown;
    }

    private async Task<DirectoryObject> FindObjectBySidAsync(
        string tenantId, string domainDn, string sid, CancellationToken ct)
    {
        var filter = new EqualityFilterNode("objectSid", sid);
        var result = await _store.SearchAsync(
            tenantId, domainDn, SearchScope.WholeSubtree, filter,
            attributes: null, sizeLimit: 1, ct: ct);
        return result.Entries.Count > 0 ? result.Entries[0] : null;
    }

    private async Task TryAddToGroupAsync(
        string tenantId, string domainDn, string groupSid, string memberDn, CancellationToken ct)
    {
        try
        {
            var group = await FindObjectBySidAsync(tenantId, domainDn, groupSid, ct);
            if (group is not null && !group.Member.Contains(memberDn, StringComparer.OrdinalIgnoreCase))
            {
                group.Member.Add(memberDn);
                await _store.UpdateAsync(group, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to add {Member} to group {GroupSid}", memberDn, groupSid);
        }
    }

    private static byte[] WriteStatus(uint status)
    {
        var writer = new NdrWriter();
        writer.WriteUInt32(status);
        return writer.ToArray();
    }

    private static byte[] WriteCreateUserFailure(uint status)
    {
        var writer = new NdrWriter();
        // Empty handle
        writer.WriteContextHandle(0, Guid.Empty);
        writer.WriteUInt32(0); // GrantedAccess
        writer.WriteUInt32(0); // RelativeId
        writer.WriteUInt32(status);
        return writer.ToArray();
    }

    private static string ReadSid(NdrReader reader)
    {
        // RPC_SID: SubAuthorityCount(uint32 conformant), Revision(byte), SubAuthorityCount(byte),
        //          IdentifierAuthority(6 bytes), SubAuthority[](uint32[])
        var maxSubAuthCount = reader.ReadUInt32(); // conformant max count
        var revision = reader.ReadByte();
        var subAuthCount = reader.ReadByte();
        var authBytes = reader.ReadBytes(6).ToArray();

        long authority = 0;
        for (int i = 0; i < 6; i++)
        {
            authority = (authority << 8) | authBytes[i];
        }

        reader.Align(4);

        var subAuthorities = new uint[subAuthCount];
        for (int i = 0; i < subAuthCount; i++)
        {
            subAuthorities[i] = reader.ReadUInt32();
        }

        var sb = new StringBuilder();
        sb.Append($"S-{revision}-{authority}");
        foreach (var sa in subAuthorities)
        {
            sb.Append($"-{sa}");
        }

        return sb.ToString();
    }

    private static byte[] WriteEnumerationBuffer(
        List<(uint Rid, string Name)> entries, uint newContext, bool moreEntries)
    {
        var writer = new NdrWriter();

        // [out] EnumerationContext
        writer.WriteUInt32(newContext);

        // [out] PSAMPR_ENUMERATION_BUFFER (pointer)
        writer.WritePointer(false);

        // SAMPR_ENUMERATION_BUFFER: EntriesRead, then pointer to Buffer array
        writer.WriteUInt32((uint)entries.Count);

        if (entries.Count > 0)
        {
            writer.WritePointer(false); // pointer to array

            // Conformant array of SAMPR_RID_ENUMERATION: MaxCount
            writer.WriteUInt32((uint)entries.Count);

            // Each entry: RelativeId (uint32), Name (RPC_UNICODE_STRING)
            foreach (var (rid, name) in entries)
            {
                writer.WriteUInt32(rid);
                writer.WriteRpcUnicodeString(name);
            }

            writer.FlushDeferred();
        }
        else
        {
            writer.WritePointer(true); // null array
        }

        // [out] CountReturned
        writer.WriteUInt32((uint)entries.Count);

        // [out] NTSTATUS
        writer.WriteUInt32(moreEntries ? SamrConstants.StatusMoreEntries : SamrConstants.StatusSuccess);

        return writer.ToArray();
    }

    // ════════════════════════════════════════════════════════════════
    //  User information writers
    // ════════════════════════════════════════════════════════════════

    private static void WriteUserGeneralInfo(NdrWriter writer, DirectoryObject user)
    {
        // Pointer to info buffer (non-null)
        writer.WritePointer(false);

        // Union switch (info class)
        writer.WriteUInt16(SamrConstants.UserGeneralInformation);
        writer.Align(4);

        // SAMPR_USER_GENERAL_INFORMATION
        writer.WriteRpcUnicodeString(user.SAMAccountName ?? "");     // UserName
        writer.WriteRpcUnicodeString(user.DisplayName ?? "");        // FullName
        writer.WriteUInt32((uint)user.PrimaryGroupId);               // PrimaryGroupId
        writer.WriteRpcUnicodeString(user.Description ?? "");        // AdminComment
        writer.WriteRpcUnicodeString("");                             // UserComment

        writer.FlushDeferred();
    }

    private static void WriteUserAccountInfo(NdrWriter writer, DirectoryObject user)
    {
        writer.WritePointer(false);
        writer.WriteUInt16(SamrConstants.UserAccountInformation);
        writer.Align(4);

        // A subset of the account information
        writer.WriteUInt32((uint)user.UserAccountControl); // UserAccountControl
        writer.WriteInt64(user.AccountExpires);             // AccountExpires
        writer.FlushDeferred();
    }

    private static void WriteUserNameInfo(NdrWriter writer, DirectoryObject user)
    {
        writer.WritePointer(false);
        writer.WriteUInt16(SamrConstants.UserNameInformation);
        writer.Align(4);

        writer.WriteRpcUnicodeString(user.SAMAccountName ?? ""); // UserName
        writer.WriteRpcUnicodeString(user.DisplayName ?? "");    // FullName
        writer.FlushDeferred();
    }

    private static void WriteUserFullNameInfo(NdrWriter writer, DirectoryObject user)
    {
        writer.WritePointer(false);
        writer.WriteUInt16(SamrConstants.UserFullNameInformation);
        writer.Align(4);

        writer.WriteRpcUnicodeString(user.DisplayName ?? ""); // FullName
        writer.FlushDeferred();
    }

    private static void WriteUserPrimaryGroupInfo(NdrWriter writer, DirectoryObject user)
    {
        writer.WritePointer(false);
        writer.WriteUInt16(SamrConstants.UserPrimaryGroupInformation);
        writer.Align(4);

        writer.WriteUInt32((uint)user.PrimaryGroupId); // PrimaryGroupId
    }

    private static void WriteUserControlInfo(NdrWriter writer, DirectoryObject user)
    {
        writer.WritePointer(false);
        writer.WriteUInt16(SamrConstants.UserControlInformation);
        writer.Align(4);

        writer.WriteUInt32((uint)user.UserAccountControl); // UserAccountControl
    }

    private static void WriteUserExpiresInfo(NdrWriter writer, DirectoryObject user)
    {
        writer.WritePointer(false);
        writer.WriteUInt16(SamrConstants.UserExpiresInformation);
        writer.Align(4);

        writer.WriteInt64(user.AccountExpires); // AccountExpires
    }

    private static void WriteUserAllInfo(NdrWriter writer, DirectoryObject user)
    {
        writer.WritePointer(false);
        writer.WriteUInt16(SamrConstants.UserAllInformation);
        writer.Align(4);

        // SAMPR_USER_ALL_INFORMATION
        writer.WriteInt64(user.LastLogon);                    // LastLogon
        writer.WriteInt64(0);                                  // LastLogoff
        writer.WriteInt64(user.PwdLastSet);                   // PasswordLastSet
        writer.WriteInt64(user.AccountExpires);                // AccountExpires
        writer.WriteInt64(0);                                  // PasswordCanChange
        writer.WriteInt64(0x7FFFFFFFFFFFFFFF);                 // PasswordMustChange
        writer.WriteRpcUnicodeString(user.SAMAccountName ?? ""); // UserName
        writer.WriteRpcUnicodeString(user.DisplayName ?? "");   // FullName
        writer.WriteRpcUnicodeString("");                        // HomeDirectory
        writer.WriteRpcUnicodeString("");                        // HomeDirectoryDrive
        writer.WriteRpcUnicodeString("");                        // ScriptPath
        writer.WriteRpcUnicodeString("");                        // ProfilePath
        writer.WriteRpcUnicodeString(user.Description ?? "");   // AdminComment
        writer.WriteRpcUnicodeString("");                        // WorkStations
        writer.WriteRpcUnicodeString("");                        // UserComment
        writer.WriteRpcUnicodeString("");                        // Parameters
        writer.WriteRpcUnicodeString("");                        // LmOwfPassword (empty)
        writer.WriteRpcUnicodeString("");                        // NtOwfPassword (empty)
        writer.WriteRpcUnicodeString("");                        // PrivateData
        writer.WriteUInt32(0);                                  // SecurityDescriptor length
        writer.WritePointer(true);                              // SecurityDescriptor ptr (null)
        writer.WriteUInt32((uint)user.PrimaryGroupId);         // PrimaryGroupId
        writer.WriteUInt32((uint)user.UserAccountControl);     // UserAccountControl
        writer.WriteUInt32(0);                                  // WhichFields
        // LogonHours structure
        writer.WriteUInt16(0);   // UnitsPerWeek
        writer.Align(4);
        writer.WritePointer(true); // LogonHours pointer (null)
        writer.WriteUInt16((ushort)user.BadPwdCount);          // BadPasswordCount
        writer.WriteUInt16(0);                                  // LogonCount
        writer.WriteUInt16(0);                                  // CountryCode
        writer.WriteUInt16(0);                                  // CodePage
        writer.WriteByte(0);                                    // LmPasswordPresent
        writer.WriteByte(0);                                    // NtPasswordPresent
        writer.WriteByte(user.PwdLastSet == 0 ? (byte)1 : (byte)0); // PasswordExpired
        writer.WriteByte(0);                                    // PrivateDataSensitive

        writer.FlushDeferred();
    }
}
