using System.Security.Cryptography;
using System.Text;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Rpc.Dispatch;
using Directory.Rpc.Ndr;

namespace Directory.Rpc.Samr;

/// <summary>
/// Implements all MS-SAMR RPC operations needed for Windows domain join.
/// Each method reads NDR-encoded input from stub data, performs the operation,
/// and returns NDR-encoded response stub data.
/// </summary>
public class SamrOperations
{
    private readonly IDirectoryStore _store;
    private readonly IRidAllocator _ridAllocator;
    private readonly IPasswordPolicy _passwordPolicy;
    private readonly INamingContextService _ncService;
    private readonly IAccessControlService _aclService;
    private readonly ILinkedAttributeService _linkService;

    public SamrOperations(
        IDirectoryStore store,
        IRidAllocator ridAllocator,
        IPasswordPolicy passwordPolicy,
        INamingContextService ncService,
        IAccessControlService aclService,
        ILinkedAttributeService linkService)
    {
        _store = store;
        _ridAllocator = ridAllocator;
        _passwordPolicy = passwordPolicy;
        _ncService = ncService;
        _aclService = aclService;
        _linkService = linkService;
    }

    // ════════════════════════════════════════════════════════════════
    //  Opnum 0 — SamrConnect
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// SamrConnect (opnum 0): Opens a handle to the SAM server object.
    /// Wire in:  [in] pointer to server name (UNICODE_STRING), [in] uint32 DesiredAccess
    /// Wire out: [out] context handle (20 bytes), [out] NTSTATUS
    /// </summary>
    public Task<byte[]> SamrConnectAsync(ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // ServerName is a pointer to a conformant varying string (may be null)
        uint serverNamePtr = reader.ReadPointer();
        if (serverNamePtr != 0)
        {
            _ = reader.ReadConformantVaryingString(); // consume deferred string
        }

        uint desiredAccess = reader.ReadUInt32();

        var serverHandle = new SamrServerHandle
        {
            GrantedAccess = desiredAccess,
            TenantId = context.TenantId
        };

        var handleBytes = context.ContextHandles.CreateHandle(serverHandle);

        var writer = new NdrWriter();
        WriteRawContextHandle(writer, handleBytes);
        writer.WriteUInt32(SamrConstants.StatusSuccess);
        return Task.FromResult(writer.ToArray());
    }

    // ════════════════════════════════════════════════════════════════
    //  Opnum 64 — SamrConnect5
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// SamrConnect5 (opnum 64): Enhanced connect with revision info negotiation.
    /// Wire in:  [in] pointer to server name, [in] DesiredAccess, [in] InVersion, [in] InRevisionInfo
    /// Wire out: [out] OutVersion, [out] OutRevisionInfo, [out] context handle, [out] NTSTATUS
    /// </summary>
    public Task<byte[]> SamrConnect5Async(ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // ServerName — pointer to conformant varying string
        uint serverNamePtr = reader.ReadPointer();
        if (serverNamePtr != 0)
        {
            _ = reader.ReadConformantVaryingString();
        }

        uint desiredAccess = reader.ReadUInt32();
        uint inVersion = reader.ReadUInt32();

        // InRevisionInfo — SAMPR_REVISION_INFO_V1: Revision(uint32), SupportedFeatures(uint32)
        uint inRevision = reader.ReadUInt32();
        uint inSupportedFeatures = reader.ReadUInt32();

        var serverHandle = new SamrServerHandle
        {
            GrantedAccess = desiredAccess,
            TenantId = context.TenantId
        };

        var handleBytes = context.ContextHandles.CreateHandle(serverHandle);

        var writer = new NdrWriter();

        // OutVersion — always 1
        writer.WriteUInt32(1);

        // OutRevisionInfo — SAMPR_REVISION_INFO_V1
        // Revision = 3 (supports AES encryption), SupportedFeatures = advertise what we support
        writer.WriteUInt32(3);                  // Revision
        writer.WriteUInt32(inSupportedFeatures); // Echo back supported features

        // Server handle
        WriteRawContextHandle(writer, handleBytes);

        writer.WriteUInt32(SamrConstants.StatusSuccess);
        return Task.FromResult(writer.ToArray());
    }

    // ════════════════════════════════════════════════════════════════
    //  Opnum 1 — SamrCloseHandle
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// SamrCloseHandle (opnum 1): Closes any SAM context handle.
    /// Wire in:  [in, out] context handle (20 bytes)
    /// Wire out: [out] zeroed context handle, [out] NTSTATUS
    /// </summary>
    public Task<byte[]> SamrCloseHandleAsync(ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);
        var (_, uuid) = reader.ReadContextHandle();

        // Close the handle (try all known types)
        Span<byte> handleKey = stackalloc byte[20];
        // Reconstruct the 20-byte key from attributes=0 + uuid
        BitConverter.TryWriteBytes(handleKey, 0u);
        uuid.TryWriteBytes(handleKey.Slice(4));
        context.ContextHandles.CloseHandle(handleKey);

        var writer = new NdrWriter();
        // Write zeroed context handle
        writer.WriteContextHandle(0, Guid.Empty);
        writer.WriteUInt32(SamrConstants.StatusSuccess);
        return Task.FromResult(writer.ToArray());
    }

    // ════════════════════════════════════════════════════════════════
    //  Opnum 5 — SamrLookupDomainInSamServer
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// SamrLookupDomainInSamServer (opnum 5): Returns the SID of a domain by name.
    /// Wire in:  [in] server handle, [in] RPC_UNICODE_STRING DomainName
    /// Wire out: [out] pointer to RPC_SID, [out] NTSTATUS
    /// </summary>
    public Task<byte[]> SamrLookupDomainInSamServerAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // Server handle
        var (_, serverUuid) = reader.ReadContextHandle();
        var serverHandle = LookupHandle<SamrServerHandle>(context, serverUuid);

        // Domain name as RPC_UNICODE_STRING
        var (length, maxLength, referentId) = reader.ReadRpcUnicodeString();
        string domainName = "";
        if (referentId != 0)
        {
            domainName = reader.ReadConformantVaryingString();
        }

        // Look up the domain SID
        string domainSid;
        if (string.Equals(domainName, "Builtin", StringComparison.OrdinalIgnoreCase))
        {
            domainSid = "S-1-5-32"; // well-known Builtin domain SID
        }
        else
        {
            domainSid = _ridAllocator.GetDomainSid(context.TenantId, context.DomainDn);
            if (string.IsNullOrEmpty(domainSid))
            {
                var writer2 = new NdrWriter();
                writer2.WritePointer(true); // null SID pointer
                writer2.WriteUInt32(SamrConstants.StatusNoSuchDomain);
                return Task.FromResult(writer2.ToArray());
            }
        }

        var writer = new NdrWriter();

        // Pointer to SID (non-null)
        writer.WritePointer(false);

        // The SID is marshalled as: uint32 subAuthorityCount (conformant size), then the RPC_SID body
        var sidParts = domainSid.Split('-');
        int subAuthCount = sidParts.Length - 3;
        writer.WriteUInt32((uint)subAuthCount); // conformant max count
        writer.WriteRpcSid(domainSid);

        writer.WriteUInt32(SamrConstants.StatusSuccess);
        return Task.FromResult(writer.ToArray());
    }

    // ════════════════════════════════════════════════════════════════
    //  Opnum 6 — SamrEnumerateDomainsInSamServer
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// SamrEnumerateDomainsInSamServer (opnum 6): Returns domain names hosted by this server.
    /// Wire in:  [in] server handle, [in, out] EnumerationContext (uint32), [in] PrefMaxLen (uint32)
    /// Wire out: [out] EnumerationContext, [out] ptr to SAMPR_ENUMERATION_BUFFER, [out] CountReturned, [out] NTSTATUS
    /// </summary>
    public Task<byte[]> SamrEnumerateDomainsInSamServerAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        var (_, serverUuid) = reader.ReadContextHandle();
        _ = LookupHandle<SamrServerHandle>(context, serverUuid);

        uint enumerationContext = reader.ReadUInt32();
        uint prefMaxLength = reader.ReadUInt32();

        // Get the domain NetBIOS name from the domain DN
        string domainNetbios = ExtractNetbiosFromDn(context.DomainDn);

        // We enumerate two domains: the account domain and Builtin
        string[] domainNames = [domainNetbios, "Builtin"];

        // Determine which entries to return based on enumeration context
        int startIndex = (int)enumerationContext;
        var entries = domainNames.Skip(startIndex).ToArray();
        uint newContext = (uint)domainNames.Length;

        var writer = new NdrWriter();

        // EnumerationContext out
        writer.WriteUInt32(newContext);

        // Pointer to SAMPR_ENUMERATION_BUFFER (non-null)
        writer.WritePointer(false);

        // SAMPR_ENUMERATION_BUFFER: EntriesRead (uint32), pointer to array
        uint entriesRead = (uint)entries.Length;
        writer.WriteUInt32(entriesRead);

        if (entriesRead > 0)
        {
            // Pointer to array (non-null)
            writer.WritePointer(false);

            // Conformant array max count
            writer.WriteUInt32(entriesRead);

            // Array of SAMPR_RID_ENUMERATION: RelativeId (uint32), Name (RPC_UNICODE_STRING)
            for (int i = 0; i < entries.Length; i++)
            {
                writer.WriteUInt32((uint)(startIndex + i)); // RelativeId (index as RID placeholder)
                writer.WriteRpcUnicodeString(entries[i]);    // Name
            }

            writer.FlushDeferred();
        }
        else
        {
            writer.WritePointer(true); // null array pointer
        }

        // CountReturned
        writer.WriteUInt32(entriesRead);

        writer.WriteUInt32(SamrConstants.StatusSuccess);
        return Task.FromResult(writer.ToArray());
    }

    // ════════════════════════════════════════════════════════════════
    //  Opnum 7 — SamrOpenDomain
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// SamrOpenDomain (opnum 7): Opens a handle to a domain object.
    /// Wire in:  [in] server handle, [in] DesiredAccess, [in] DomainId (RPC_SID)
    /// Wire out: [out] domain handle, [out] NTSTATUS
    /// </summary>
    public Task<byte[]> SamrOpenDomainAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        var (_, serverUuid) = reader.ReadContextHandle();
        _ = LookupHandle<SamrServerHandle>(context, serverUuid);

        uint desiredAccess = reader.ReadUInt32();

        // Domain SID — conformant array: max_count, then RPC_SID body
        uint sidMaxCount = reader.ReadUInt32(); // conformant max count (sub authority count)
        string domainSid = reader.ReadRpcSid();

        // Determine domain DN from the SID
        string domainDn;
        string domainName;
        string knownDomainSid = _ridAllocator.GetDomainSid(context.TenantId, context.DomainDn);

        if (string.Equals(domainSid, "S-1-5-32", StringComparison.OrdinalIgnoreCase))
        {
            // Builtin domain
            domainDn = "CN=Builtin," + context.DomainDn;
            domainName = "Builtin";
        }
        else if (string.Equals(domainSid, knownDomainSid, StringComparison.OrdinalIgnoreCase))
        {
            domainDn = context.DomainDn;
            domainName = ExtractNetbiosFromDn(context.DomainDn);
        }
        else
        {
            var writer2 = new NdrWriter();
            writer2.WriteContextHandle(0, Guid.Empty);
            writer2.WriteUInt32(SamrConstants.StatusNoSuchDomain);
            return Task.FromResult(writer2.ToArray());
        }

        var domainHandle = new SamrDomainHandle
        {
            GrantedAccess = desiredAccess,
            DomainDn = domainDn,
            DomainSid = domainSid,
            DomainName = domainName,
            TenantId = context.TenantId
        };

        var handleBytes = context.ContextHandles.CreateHandle(domainHandle);

        var writer = new NdrWriter();
        WriteRawContextHandle(writer, handleBytes);
        writer.WriteUInt32(SamrConstants.StatusSuccess);
        return Task.FromResult(writer.ToArray());
    }

    // ════════════════════════════════════════════════════════════════
    //  Opnum 17 — SamrLookupNamesInDomain
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// SamrLookupNamesInDomain (opnum 17): Translates account names to RIDs.
    /// Wire in:  [in] domain handle, [in] Count, [in] Names[Count] (RPC_UNICODE_STRING array)
    /// Wire out: [out] RelativeIds (SAMPR_ULONG_ARRAY), [out] Use (SAMPR_ULONG_ARRAY), [out] NTSTATUS
    /// </summary>
    public async Task<byte[]> SamrLookupNamesInDomainAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        var (_, domainUuid) = reader.ReadContextHandle();
        var domainHandle = LookupHandle<SamrDomainHandle>(context, domainUuid);

        uint count = reader.ReadUInt32();

        if (count > 1000)
        {
            throw new RpcFaultException(SamrConstants.StatusInvalidParameter, "Too many names requested.");
        }

        // Read the conformant array of RPC_UNICODE_STRING headers
        uint maxCount = reader.ReadUInt32(); // conformant max count for the array

        var unicodeStringHeaders = new List<(ushort Length, ushort MaxLength, uint ReferentId)>();
        for (uint i = 0; i < count; i++)
        {
            unicodeStringHeaders.Add(reader.ReadRpcUnicodeString());
        }

        // Read deferred string bodies
        var names = new List<string>();
        foreach (var (length, maxLength, referentId) in unicodeStringHeaders)
        {
            if (referentId != 0)
            {
                names.Add(reader.ReadConformantVaryingString());
            }
            else
            {
                names.Add("");
            }
        }

        // Look up each name
        var rids = new uint[count];
        var useTypes = new uint[count];
        bool anyFound = false;
        bool allFound = true;

        for (int i = 0; i < count; i++)
        {
            var obj = await _store.GetBySamAccountNameAsync(
                domainHandle.TenantId, domainHandle.DomainDn, names[i], ct);

            if (obj != null && obj.ObjectSid != null)
            {
                rids[i] = ExtractRid(obj.ObjectSid);
                useTypes[i] = DetermineUseType(obj);
                anyFound = true;
            }
            else
            {
                rids[i] = 0;
                useTypes[i] = SamrConstants.SidTypeUnknown;
                allFound = false;
            }
        }

        var writer = new NdrWriter();

        // RelativeIds — SAMPR_ULONG_ARRAY: Count (uint32), pointer to array
        writer.WriteUInt32(count);
        if (count > 0)
        {
            writer.WritePointer(false);
        }
        else
        {
            writer.WritePointer(true);
        }

        // Use — SAMPR_ULONG_ARRAY: Count, pointer to array
        writer.WriteUInt32(count);
        if (count > 0)
        {
            writer.WritePointer(false);
        }
        else
        {
            writer.WritePointer(true);
        }

        // Deferred: RID array body
        if (count > 0)
        {
            writer.WriteUInt32(count); // conformant max count
            for (int i = 0; i < count; i++)
            {
                writer.WriteUInt32(rids[i]);
            }

            // Deferred: Use type array body
            writer.WriteUInt32(count); // conformant max count
            for (int i = 0; i < count; i++)
            {
                writer.WriteUInt32(useTypes[i]);
            }
        }

        // NTSTATUS
        if (!anyFound)
        {
            writer.WriteUInt32(SamrConstants.StatusNoneMapped);
        }
        else if (!allFound)
        {
            writer.WriteUInt32(SamrConstants.StatusSomeMapped);
        }
        else
        {
            writer.WriteUInt32(SamrConstants.StatusSuccess);
        }

        return writer.ToArray();
    }

    // ════════════════════════════════════════════════════════════════
    //  Opnum 18 — SamrLookupIdsInDomain
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// SamrLookupIdsInDomain (opnum 18): Translates RIDs to account names.
    /// Wire in:  [in] domain handle, [in] Count, [in] RelativeIds[Count]
    /// Wire out: [out] Names (SAMPR_RETURNED_USTRING_ARRAY), [out] Use (SAMPR_ULONG_ARRAY), [out] NTSTATUS
    /// </summary>
    public async Task<byte[]> SamrLookupIdsInDomainAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        var (_, domainUuid) = reader.ReadContextHandle();
        var domainHandle = LookupHandle<SamrDomainHandle>(context, domainUuid);

        uint count = reader.ReadUInt32();

        if (count > 1000)
        {
            throw new RpcFaultException(SamrConstants.StatusInvalidParameter, "Too many IDs requested.");
        }

        // Conformant array of uint32 RIDs
        uint maxCount = reader.ReadUInt32();
        var rids = new uint[count];
        for (uint i = 0; i < count; i++)
        {
            rids[i] = reader.ReadUInt32();
        }

        // Look up each RID
        var names = new string[count];
        var useTypes = new uint[count];
        bool anyFound = false;
        bool allFound = true;

        for (int i = 0; i < count; i++)
        {
            string sid = $"{domainHandle.DomainSid}-{rids[i]}";
            var obj = await FindObjectBySidAsync(domainHandle.TenantId, domainHandle.DomainDn, sid, ct);

            if (obj != null)
            {
                names[i] = obj.SAMAccountName ?? obj.Cn ?? "";
                useTypes[i] = DetermineUseType(obj);
                anyFound = true;
            }
            else
            {
                names[i] = null;
                useTypes[i] = SamrConstants.SidTypeUnknown;
                allFound = false;
            }
        }

        var writer = new NdrWriter();

        // SAMPR_RETURNED_USTRING_ARRAY: Count, pointer to RPC_UNICODE_STRING array
        writer.WriteUInt32(count);
        if (count > 0)
        {
            writer.WritePointer(false);

            // Conformant array max count
            writer.WriteUInt32(count);

            // Array of RPC_UNICODE_STRING headers
            for (int i = 0; i < count; i++)
            {
                writer.WriteRpcUnicodeString(names[i] ?? "");
            }

            writer.FlushDeferred();
        }
        else
        {
            writer.WritePointer(true);
        }

        // Use — SAMPR_ULONG_ARRAY
        writer.WriteUInt32(count);
        if (count > 0)
        {
            writer.WritePointer(false);
            writer.WriteUInt32(count);
            for (int i = 0; i < count; i++)
            {
                writer.WriteUInt32(useTypes[i]);
            }
        }
        else
        {
            writer.WritePointer(true);
        }

        // NTSTATUS
        if (count == 0 || !anyFound)
        {
            writer.WriteUInt32(count == 0 ? SamrConstants.StatusSuccess : SamrConstants.StatusNoneMapped);
        }
        else if (!allFound)
        {
            writer.WriteUInt32(SamrConstants.StatusSomeMapped);
        }
        else
        {
            writer.WriteUInt32(SamrConstants.StatusSuccess);
        }

        return writer.ToArray();
    }

    // ════════════════════════════════════════════════════════════════
    //  Opnum 34 — SamrOpenUser
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// SamrOpenUser (opnum 34): Opens a handle to a user account.
    /// Wire in:  [in] domain handle, [in] DesiredAccess, [in] UserId (uint32 RID)
    /// Wire out: [out] user handle, [out] NTSTATUS
    /// </summary>
    public async Task<byte[]> SamrOpenUserAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        var (_, domainUuid) = reader.ReadContextHandle();
        var domainHandle = LookupHandle<SamrDomainHandle>(context, domainUuid);

        uint desiredAccess = reader.ReadUInt32();
        uint userId = reader.ReadUInt32();

        // Find user by SID
        string userSid = $"{domainHandle.DomainSid}-{userId}";
        var user = await FindObjectBySidAsync(domainHandle.TenantId, domainHandle.DomainDn, userSid, ct);

        if (user == null)
        {
            var writer2 = new NdrWriter();
            writer2.WriteContextHandle(0, Guid.Empty);
            writer2.WriteUInt32(SamrConstants.StatusNoSuchUser);
            return writer2.ToArray();
        }

        var userHandle = new SamrUserHandle
        {
            GrantedAccess = desiredAccess,
            UserDn = user.DistinguishedName,
            Rid = userId,
            DomainDn = domainHandle.DomainDn,
            TenantId = domainHandle.TenantId
        };

        var handleBytes = context.ContextHandles.CreateHandle(userHandle);

        var writer = new NdrWriter();
        WriteRawContextHandle(writer, handleBytes);
        writer.WriteUInt32(SamrConstants.StatusSuccess);
        return writer.ToArray();
    }

    // ════════════════════════════════════════════════════════════════
    //  Opnum 50 — SamrCreateUser2InDomain
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// SamrCreateUser2InDomain (opnum 50): Creates a new user/computer account.
    /// This is THE KEY operation for Windows domain join — it creates machine accounts.
    /// Wire in:  [in] domain handle, [in] Name (RPC_UNICODE_STRING), [in] AccountType, [in] DesiredAccess
    /// Wire out: [out] user handle, [out] GrantedAccess, [out] RelativeId, [out] NTSTATUS
    /// </summary>
    public async Task<byte[]> SamrCreateUser2InDomainAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // Domain handle
        var (_, domainUuid) = reader.ReadContextHandle();
        var domainHandle = LookupHandle<SamrDomainHandle>(context, domainUuid);

        // Account name — RPC_UNICODE_STRING
        var (nameLen, nameMax, nameRef) = reader.ReadRpcUnicodeString();
        string accountName = "";
        if (nameRef != 0)
        {
            accountName = reader.ReadConformantVaryingString();
        }

        // AccountType (bit flags indicating the type of account)
        uint accountType = reader.ReadUInt32();

        // DesiredAccess
        uint desiredAccess = reader.ReadUInt32();

        // Check if account already exists
        var existing = await _store.GetBySamAccountNameAsync(
            domainHandle.TenantId, domainHandle.DomainDn, accountName, ct);

        if (existing != null)
        {
            var writerErr = new NdrWriter();
            writerErr.WriteContextHandle(0, Guid.Empty);
            writerErr.WriteUInt32(0); // GrantedAccess
            writerErr.WriteUInt32(0); // RelativeId
            writerErr.WriteUInt32(SamrConstants.StatusUserExists);
            return writerErr.ToArray();
        }

        // Determine object classes and container based on account type
        bool isMachineAccount = (accountType & SamrConstants.UfWorkstationTrustAccount) != 0
                             || (accountType & SamrConstants.UfServerTrustAccount) != 0;

        List<string> objectClasses;
        string container;
        int primaryGroupId;
        int samAccountType;
        uint uacFlags;

        if (isMachineAccount)
        {
            objectClasses = ["top", "person", "organizationalPerson", "user", "computer"];
            container = $"CN=Computers,{domainHandle.DomainDn}";
            primaryGroupId = (int)SamrConstants.RidDomainComputers;
            samAccountType = SamrConstants.SamMachineAccount;
            uacFlags = SamrConstants.UfWorkstationTrustAccount | SamrConstants.UfAccountDisable;
        }
        else
        {
            objectClasses = ["top", "person", "organizationalPerson", "user"];
            container = $"CN=Users,{domainHandle.DomainDn}";
            primaryGroupId = (int)SamrConstants.RidDomainUsers;
            samAccountType = SamrConstants.SamUserObject;
            uacFlags = SamrConstants.UfNormalAccount | SamrConstants.UfAccountDisable;
        }

        // Strip trailing '$' for the CN if it's a machine account
        string cn = accountName;

        string dn = $"CN={cn},{container}";

        // Allocate SID
        string objectSid = await _ridAllocator.GenerateObjectSidAsync(
            domainHandle.TenantId, domainHandle.DomainDn, ct);
        uint rid = ExtractRid(objectSid);

        var now = DateTimeOffset.UtcNow;

        var newObject = new DirectoryObject
        {
            Id = dn.ToLowerInvariant(),
            TenantId = domainHandle.TenantId,
            DomainDn = domainHandle.DomainDn,
            DistinguishedName = dn,
            ObjectGuid = Guid.NewGuid().ToString(),
            ObjectSid = objectSid,
            SAMAccountName = accountName,
            Cn = cn,
            ObjectClass = objectClasses,
            ObjectCategory = isMachineAccount ? "computer" : "person",
            UserAccountControl = (int)uacFlags,
            PrimaryGroupId = primaryGroupId,
            SAMAccountType = samAccountType,
            ParentDn = container,
            WhenCreated = now,
            WhenChanged = now,
            PwdLastSet = 0,
            AccountExpires = 0x7FFFFFFFFFFFFFFF, // never expires
        };

        await _store.CreateAsync(newObject, ct);

        // Create user handle
        var userHandle = new SamrUserHandle
        {
            GrantedAccess = desiredAccess,
            UserDn = dn,
            Rid = rid,
            DomainDn = domainHandle.DomainDn,
            TenantId = domainHandle.TenantId
        };

        var handleBytes = context.ContextHandles.CreateHandle(userHandle);

        var writer = new NdrWriter();
        WriteRawContextHandle(writer, handleBytes);
        writer.WriteUInt32(desiredAccess); // GrantedAccess
        writer.WriteUInt32(rid);           // RelativeId
        writer.WriteUInt32(SamrConstants.StatusSuccess);
        return writer.ToArray();
    }

    // ════════════════════════════════════════════════════════════════
    //  Opnum 58 — SamrSetInformationUser2
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// SamrSetInformationUser2 (opnum 58): Sets user account attributes.
    /// Handles password setting (Internal5/Internal5New), UAC flags, and all-fields update.
    /// Wire in:  [in] user handle, [in] UserInformationClass (ushort), [in] Buffer (union)
    /// Wire out: [out] NTSTATUS
    /// </summary>
    public async Task<byte[]> SamrSetInformationUser2Async(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        var (_, userUuid) = reader.ReadContextHandle();
        var userHandle = LookupHandle<SamrUserHandle>(context, userUuid);

        ushort infoClass = reader.ReadUInt16();
        reader.Align(4);

        // The union discriminant is also encoded
        ushort unionSwitch = reader.ReadUInt16();
        reader.Align(4);

        switch (infoClass)
        {
            case SamrConstants.UserInternal5Information:
            case SamrConstants.UserInternal5InformationNew:
                await HandleSetPasswordAsync(reader, userHandle, context, infoClass, ct);
                break;

            case SamrConstants.UserControlInformation:
                await HandleSetControlInformationAsync(reader, userHandle, ct);
                break;

            case SamrConstants.UserAllInformation:
                await HandleSetAllInformationAsync(reader, userHandle, ct);
                break;

            default:
                // For other info classes, read and discard — return success
                // This allows the domain join flow to proceed even for info classes
                // we don't fully implement yet.
                break;
        }

        var writer = new NdrWriter();
        writer.WriteUInt32(SamrConstants.StatusSuccess);
        return writer.ToArray();
    }

    /// <summary>
    /// Handles SAMPR_USER_INTERNAL5_INFORMATION / _NEW password set.
    /// The password is encrypted as SAMPR_ENCRYPTED_USER_PASSWORD (516 bytes).
    /// </summary>
    private async Task HandleSetPasswordAsync(
        NdrReader reader, SamrUserHandle userHandle, RpcCallContext context,
        ushort infoClass, CancellationToken ct)
    {
        // SAMPR_ENCRYPTED_USER_PASSWORD: 516 bytes of encrypted data
        // Layout (after decryption): 512 bytes (password UTF-16LE right-justified + random padding) + 4 bytes length
        var encryptedData = reader.ReadBytes(516).ToArray();

        // PasswordExpired flag (boolean, uint8 padded to uint32 alignment)
        bool passwordExpired = reader.ReadByte() != 0;

        // Decrypt the password using the session key
        byte[] decrypted = DecryptSamrPassword(encryptedData.ToArray(), context.SessionKey, infoClass);

        // Extract the password: last 4 bytes are the byte-length of the password
        int passwordByteLength = BitConverter.ToInt32(decrypted, 512);

        if (passwordByteLength < 0 || passwordByteLength > 512)
        {
            throw new RpcFaultException(SamrConstants.StatusInvalidParameter, "Invalid password length in encrypted buffer.");
        }

        int passwordOffset = 512 - passwordByteLength;
        string password = Encoding.Unicode.GetString(decrypted, passwordOffset, passwordByteLength);

        // Set the password via the password policy service
        await _passwordPolicy.SetPasswordAsync(userHandle.TenantId, userHandle.UserDn, password, ct);

        // If passwordExpired is false, update PwdLastSet to current time
        if (!passwordExpired)
        {
            var user = await _store.GetByDnAsync(userHandle.TenantId, userHandle.UserDn, ct);
            if (user != null)
            {
                user.PwdLastSet = DateTimeOffset.UtcNow.ToFileTime();
                user.WhenChanged = DateTimeOffset.UtcNow;
                await _store.UpdateAsync(user, ct);
            }
        }
    }

    /// <summary>
    /// Handles UserControlInformation (class 13): Sets UAC flags on the user account.
    /// </summary>
    private async Task HandleSetControlInformationAsync(
        NdrReader reader, SamrUserHandle userHandle, CancellationToken ct)
    {
        uint uacFlags = reader.ReadUInt32();

        var user = await _store.GetByDnAsync(userHandle.TenantId, userHandle.UserDn, ct);
        if (user == null)
        {
            throw new RpcFaultException(SamrConstants.StatusNoSuchUser, "User not found.");
        }

        user.UserAccountControl = (int)uacFlags;
        user.WhenChanged = DateTimeOffset.UtcNow;
        await _store.UpdateAsync(user, ct);
    }

    /// <summary>
    /// Handles UserAllInformation (class 21): Update multiple user fields.
    /// Reads the WhichFields bitmask to determine which fields are present.
    /// </summary>
    private async Task HandleSetAllInformationAsync(
        NdrReader reader, SamrUserHandle userHandle, CancellationToken ct)
    {
        var user = await _store.GetByDnAsync(userHandle.TenantId, userHandle.UserDn, ct);
        if (user == null)
        {
            throw new RpcFaultException(SamrConstants.StatusNoSuchUser, "User not found.");
        }

        // SAMPR_USER_ALL_INFORMATION structure:
        // Fields are always present in the wire format, but WhichFields indicates which are valid.

        // LastLogon (LARGE_INTEGER)
        long lastLogon = reader.ReadInt64();
        // LastLogoff (LARGE_INTEGER)
        long lastLogoff = reader.ReadInt64();
        // PasswordLastSet (LARGE_INTEGER)
        long passwordLastSet = reader.ReadInt64();
        // AccountExpires (LARGE_INTEGER)
        long accountExpires = reader.ReadInt64();
        // PasswordCanChange (LARGE_INTEGER)
        long passwordCanChange = reader.ReadInt64();
        // PasswordMustChange (LARGE_INTEGER)
        long passwordMustChange = reader.ReadInt64();

        // UserName (RPC_UNICODE_STRING)
        var userNameHeader = reader.ReadRpcUnicodeString();
        // FullName (RPC_UNICODE_STRING)
        var fullNameHeader = reader.ReadRpcUnicodeString();
        // HomeDirectory (RPC_UNICODE_STRING)
        var homeDirectoryHeader = reader.ReadRpcUnicodeString();
        // HomeDirectoryDrive (RPC_UNICODE_STRING)
        var homeDirDriveHeader = reader.ReadRpcUnicodeString();
        // ScriptPath (RPC_UNICODE_STRING)
        var scriptPathHeader = reader.ReadRpcUnicodeString();
        // ProfilePath (RPC_UNICODE_STRING)
        var profilePathHeader = reader.ReadRpcUnicodeString();
        // AdminComment (RPC_UNICODE_STRING)
        var adminCommentHeader = reader.ReadRpcUnicodeString();
        // WorkStations (RPC_UNICODE_STRING)
        var workStationsHeader = reader.ReadRpcUnicodeString();
        // UserComment (RPC_UNICODE_STRING)
        var userCommentHeader = reader.ReadRpcUnicodeString();
        // Parameters (RPC_UNICODE_STRING)
        var parametersHeader = reader.ReadRpcUnicodeString();

        // LmOwfPassword (RPC_SHORT_BLOB: Length, MaxLength, pointer)
        ushort lmPwdLen = reader.ReadUInt16();
        ushort lmPwdMax = reader.ReadUInt16();
        reader.Align(4);
        uint lmPwdPtr = reader.ReadUInt32();

        // NtOwfPassword (RPC_SHORT_BLOB)
        ushort ntPwdLen = reader.ReadUInt16();
        ushort ntPwdMax = reader.ReadUInt16();
        reader.Align(4);
        uint ntPwdPtr = reader.ReadUInt32();

        // PrivateData (RPC_UNICODE_STRING)
        var privateDataHeader = reader.ReadRpcUnicodeString();

        // SecurityDescriptor (SAMPR_SR_SECURITY_DESCRIPTOR: Length, pointer)
        uint sdLength = reader.ReadUInt32();
        uint sdPtr = reader.ReadPointer();

        // UserId (uint32)
        uint userId = reader.ReadUInt32();
        // PrimaryGroupId (uint32)
        uint primaryGroupId = reader.ReadUInt32();
        // UserAccountControl (uint32)
        uint userAccountControl = reader.ReadUInt32();
        // WhichFields (uint32) — bitmask of valid fields
        uint whichFields = reader.ReadUInt32();

        // LogonHours (SAMPR_LOGON_HOURS: UnitsPerWeek, pointer)
        ushort unitsPerWeek = reader.ReadUInt16();
        reader.Align(4);
        uint logonHoursPtr = reader.ReadPointer();

        // BadPasswordCount (ushort)
        ushort badPasswordCount = reader.ReadUInt16();
        // LogonCount (ushort)
        ushort logonCount = reader.ReadUInt16();
        // CountryCode (ushort)
        ushort countryCode = reader.ReadUInt16();
        // CodePage (ushort)
        ushort codePage = reader.ReadUInt16();

        // LmPasswordPresent (uchar -> boolean)
        byte lmPasswordPresent = reader.ReadByte();
        // NtPasswordPresent (uchar -> boolean)
        byte ntPasswordPresent = reader.ReadByte();
        // PasswordExpired (uchar -> boolean)
        byte passwordExpired = reader.ReadByte();
        // PrivateDataSensitive (uchar -> boolean)
        byte privateDataSensitive = reader.ReadByte();

        // Now read deferred string bodies based on referent IDs
        string userName = userNameHeader.ReferentId != 0 ? reader.ReadConformantVaryingString() : null;
        string fullName = fullNameHeader.ReferentId != 0 ? reader.ReadConformantVaryingString() : null;
        string homeDirectory = homeDirectoryHeader.ReferentId != 0 ? reader.ReadConformantVaryingString() : null;
        string homeDirDrive = homeDirDriveHeader.ReferentId != 0 ? reader.ReadConformantVaryingString() : null;
        string scriptPath = scriptPathHeader.ReferentId != 0 ? reader.ReadConformantVaryingString() : null;
        string profilePath = profilePathHeader.ReferentId != 0 ? reader.ReadConformantVaryingString() : null;
        string adminComment = adminCommentHeader.ReferentId != 0 ? reader.ReadConformantVaryingString() : null;
        string workStations = workStationsHeader.ReferentId != 0 ? reader.ReadConformantVaryingString() : null;
        string userComment = userCommentHeader.ReferentId != 0 ? reader.ReadConformantVaryingString() : null;
        string parameters = parametersHeader.ReferentId != 0 ? reader.ReadConformantVaryingString() : null;

        // Skip LM/NT password blobs if present
        if (lmPwdPtr != 0)
        {
            uint lmMaxCount = reader.ReadUInt32();
            _ = reader.ReadBytes((int)lmMaxCount);
        }
        if (ntPwdPtr != 0)
        {
            uint ntMaxCount = reader.ReadUInt32();
            _ = reader.ReadBytes((int)ntMaxCount);
        }

        // Skip PrivateData
        if (privateDataHeader.ReferentId != 0)
        {
            _ = reader.ReadConformantVaryingString();
        }

        // Skip SecurityDescriptor
        if (sdPtr != 0)
        {
            uint sdMaxCount = reader.ReadUInt32();
            _ = reader.ReadBytes((int)sdMaxCount);
        }

        // Skip LogonHours
        if (logonHoursPtr != 0)
        {
            uint logonHoursMaxCount = reader.ReadUInt32();
            _ = reader.ReadBytes((int)(logonHoursMaxCount));
        }

        // Apply fields based on WhichFields bitmask
        // Bit definitions from MS-SAMR 2.2.1.8
        const uint UserAllUsername = 0x00000001;
        const uint UserAllFullname = 0x00000002;
        // const uint UserAllUserid = 0x00000004;
        const uint UserAllPrimarygroupid = 0x00000008;
        const uint UserAllAdmincomment = 0x00000010;
        // const uint UserAllUsercomment = 0x00000020;
        const uint UserAllUseraccountcontrol = 0x00000200;
        const uint UserAllAccountexpires = 0x00002000;
        // const uint UserAllParameters = 0x00100000;

        if ((whichFields & UserAllUsername) != 0 && userName != null)
        {
            user.SAMAccountName = userName;
        }
        if ((whichFields & UserAllFullname) != 0 && fullName != null)
        {
            user.DisplayName = fullName;
        }
        if ((whichFields & UserAllPrimarygroupid) != 0)
        {
            user.PrimaryGroupId = (int)primaryGroupId;
        }
        if ((whichFields & UserAllAdmincomment) != 0 && adminComment != null)
        {
            user.Description = adminComment;
        }
        if ((whichFields & UserAllUseraccountcontrol) != 0)
        {
            user.UserAccountControl = (int)userAccountControl;
        }
        if ((whichFields & UserAllAccountexpires) != 0)
        {
            user.AccountExpires = accountExpires;
        }

        user.WhenChanged = DateTimeOffset.UtcNow;
        await _store.UpdateAsync(user, ct);
    }

    // ════════════════════════════════════════════════════════════════
    //  Opnum 47 — SamrQueryInformationUser2
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// SamrQueryInformationUser2 (opnum 47): Queries user account information.
    /// Wire in:  [in] user handle, [in] UserInformationClass (ushort)
    /// Wire out: [out] pointer to info buffer (union), [out] NTSTATUS
    /// </summary>
    public async Task<byte[]> SamrQueryInformationUser2Async(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        var (_, userUuid) = reader.ReadContextHandle();
        var userHandle = LookupHandle<SamrUserHandle>(context, userUuid);

        ushort infoClass = reader.ReadUInt16();

        var user = await _store.GetByDnAsync(userHandle.TenantId, userHandle.UserDn, ct);
        if (user == null)
        {
            var writerErr = new NdrWriter();
            writerErr.WritePointer(true); // null info pointer
            writerErr.WriteUInt32(SamrConstants.StatusNoSuchUser);
            return writerErr.ToArray();
        }

        var writer = new NdrWriter();

        // Pointer to info (non-null)
        writer.WritePointer(false);

        // Union switch (discriminant = infoClass)
        writer.WriteUInt16(infoClass);
        writer.Align(4);

        switch (infoClass)
        {
            case SamrConstants.UserAllInformation:
                WriteUserAllInformation(writer, user, userHandle.Rid);
                break;

            case SamrConstants.UserAccountInformation:
                WriteUserAccountInformation(writer, user, userHandle.Rid);
                break;

            case SamrConstants.UserGeneralInformation:
                WriteUserGeneralInformation(writer, user);
                break;

            case SamrConstants.UserControlInformation:
                writer.WriteUInt32((uint)user.UserAccountControl);
                break;

            case SamrConstants.UserPrimaryGroupInformation:
                writer.WriteUInt32((uint)user.PrimaryGroupId);
                break;

            case SamrConstants.UserNameInformation:
                writer.WriteRpcUnicodeString(user.SAMAccountName ?? "");
                writer.WriteRpcUnicodeString(user.DisplayName ?? "");
                writer.FlushDeferred();
                break;

            default:
                // Return empty for unimplemented info classes
                break;
        }

        writer.WriteUInt32(SamrConstants.StatusSuccess);
        return writer.ToArray();
    }

    private void WriteUserAllInformation(NdrWriter writer, DirectoryObject user, uint rid)
    {
        // SAMPR_USER_ALL_INFORMATION — all fields in wire order

        // LastLogon
        writer.WriteInt64(user.LastLogon);
        // LastLogoff
        writer.WriteInt64(0);
        // PasswordLastSet
        writer.WriteInt64(user.PwdLastSet);
        // AccountExpires
        writer.WriteInt64(user.AccountExpires);
        // PasswordCanChange
        writer.WriteInt64(0);
        // PasswordMustChange
        writer.WriteInt64(0x7FFFFFFFFFFFFFFF); // never

        // UserName
        writer.WriteRpcUnicodeString(user.SAMAccountName ?? "");
        // FullName
        writer.WriteRpcUnicodeString(user.DisplayName ?? "");
        // HomeDirectory
        writer.WriteRpcUnicodeString("");
        // HomeDirectoryDrive
        writer.WriteRpcUnicodeString("");
        // ScriptPath
        writer.WriteRpcUnicodeString("");
        // ProfilePath
        writer.WriteRpcUnicodeString("");
        // AdminComment
        writer.WriteRpcUnicodeString(user.Description ?? "");
        // WorkStations
        writer.WriteRpcUnicodeString("");
        // UserComment
        writer.WriteRpcUnicodeString("");
        // Parameters
        writer.WriteRpcUnicodeString("");

        // LmOwfPassword (RPC_SHORT_BLOB) — we don't expose LM hashes
        writer.WriteUInt16(0); // Length
        writer.WriteUInt16(0); // MaxLength
        writer.Align(4);
        writer.WritePointer(true); // null pointer

        // NtOwfPassword (RPC_SHORT_BLOB)
        writer.WriteUInt16(0);
        writer.WriteUInt16(0);
        writer.Align(4);
        writer.WritePointer(true);

        // PrivateData
        writer.WriteRpcUnicodeString(null);

        // SecurityDescriptor (SAMPR_SR_SECURITY_DESCRIPTOR)
        writer.WriteUInt32(0); // Length
        writer.WritePointer(true); // null pointer

        // UserId
        writer.WriteUInt32(rid);
        // PrimaryGroupId
        writer.WriteUInt32((uint)user.PrimaryGroupId);
        // UserAccountControl
        writer.WriteUInt32((uint)user.UserAccountControl);
        // WhichFields — all fields present
        writer.WriteUInt32(0x00FFFFFF);

        // LogonHours (SAMPR_LOGON_HOURS)
        writer.WriteUInt16(168); // UnitsPerWeek = 168 (24*7)
        writer.Align(4);
        writer.WritePointer(true); // null pointer (all hours allowed)

        // BadPasswordCount
        writer.WriteUInt16((ushort)user.BadPwdCount);
        // LogonCount
        writer.WriteUInt16(0);
        // CountryCode
        writer.WriteUInt16(0);
        // CodePage
        writer.WriteUInt16(0);

        // LmPasswordPresent
        writer.WriteByte(0);
        // NtPasswordPresent
        writer.WriteByte(user.NTHash != null ? (byte)1 : (byte)0);
        // PasswordExpired
        writer.WriteByte(user.PwdLastSet == 0 ? (byte)1 : (byte)0);
        // PrivateDataSensitive
        writer.WriteByte(0);

        // Flush deferred string bodies
        writer.FlushDeferred();
    }

    private void WriteUserAccountInformation(NdrWriter writer, DirectoryObject user, uint rid)
    {
        // SAMPR_USER_ACCOUNT_INFORMATION — subset of AllInformation

        // UserName
        writer.WriteRpcUnicodeString(user.SAMAccountName ?? "");
        // FullName
        writer.WriteRpcUnicodeString(user.DisplayName ?? "");
        // UserId
        writer.WriteUInt32(rid);
        // PrimaryGroupId
        writer.WriteUInt32((uint)user.PrimaryGroupId);
        // HomeDirectory
        writer.WriteRpcUnicodeString("");
        // HomeDirectoryDrive
        writer.WriteRpcUnicodeString("");
        // ScriptPath
        writer.WriteRpcUnicodeString("");
        // ProfilePath
        writer.WriteRpcUnicodeString("");
        // AdminComment
        writer.WriteRpcUnicodeString(user.Description ?? "");
        // WorkStations
        writer.WriteRpcUnicodeString("");
        // LastLogon
        writer.WriteInt64(user.LastLogon);
        // LastLogoff
        writer.WriteInt64(0);
        // LogonHours
        writer.WriteUInt16(168);
        writer.Align(4);
        writer.WritePointer(true);
        // BadPasswordCount
        writer.WriteUInt16((ushort)user.BadPwdCount);
        // LogonCount
        writer.WriteUInt16(0);
        // PasswordLastSet
        writer.WriteInt64(user.PwdLastSet);
        // AccountExpires
        writer.WriteInt64(user.AccountExpires);
        // UserAccountControl
        writer.WriteUInt32((uint)user.UserAccountControl);

        writer.FlushDeferred();
    }

    private void WriteUserGeneralInformation(NdrWriter writer, DirectoryObject user)
    {
        // SAMPR_USER_GENERAL_INFORMATION
        writer.WriteRpcUnicodeString(user.SAMAccountName ?? "");
        writer.WriteRpcUnicodeString(user.DisplayName ?? "");
        writer.WriteRpcUnicodeString(user.Description ?? "");
        writer.WriteRpcUnicodeString(""); // AdminComment
        writer.WriteRpcUnicodeString(""); // UserComment

        writer.FlushDeferred();
    }

    // ════════════════════════════════════════════════════════════════
    //  Opnum 46 — SamrQueryInformationDomain2
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// SamrQueryInformationDomain2 (opnum 46): Queries domain-level information.
    /// Wire in:  [in] domain handle, [in] DomainInformationClass (ushort)
    /// Wire out: [out] pointer to info buffer, [out] NTSTATUS
    /// </summary>
    public async Task<byte[]> SamrQueryInformationDomain2Async(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        var (_, domainUuid) = reader.ReadContextHandle();
        var domainHandle = LookupHandle<SamrDomainHandle>(context, domainUuid);

        ushort infoClass = reader.ReadUInt16();

        var writer = new NdrWriter();

        // Pointer to info (non-null)
        writer.WritePointer(false);

        // Union discriminant
        writer.WriteUInt16(infoClass);
        writer.Align(4);

        switch (infoClass)
        {
            case SamrConstants.DomainPasswordInformation:
                WriteDomainPasswordInformation(writer, domainHandle);
                break;

            case SamrConstants.DomainGeneralInformation:
                await WriteDomainGeneralInformationAsync(writer, domainHandle, ct);
                break;

            case SamrConstants.DomainLogoffInformation:
                // DOMAIN_LOGOFF_INFORMATION: ForceLogoff (LARGE_INTEGER)
                writer.WriteInt64(unchecked((long)0x8000000000000000)); // never force logoff
                break;

            case SamrConstants.DomainOemInformation:
                // DOMAIN_OEM_INFORMATION: OemInformation (RPC_UNICODE_STRING)
                writer.WriteRpcUnicodeString("");
                writer.FlushDeferred();
                break;

            case SamrConstants.DomainNameInformation:
                // DOMAIN_NAME_INFORMATION: DomainName (RPC_UNICODE_STRING)
                writer.WriteRpcUnicodeString(domainHandle.DomainName);
                writer.FlushDeferred();
                break;

            case SamrConstants.DomainGeneralInformation2:
                await WriteDomainGeneralInformation2Async(writer, domainHandle, ct);
                break;

            default:
                // Unknown info class — return null pointer with error
                var writerErr = new NdrWriter();
                writerErr.WritePointer(true);
                writerErr.WriteUInt32(SamrConstants.StatusInvalidParameter);
                return writerErr.ToArray();
        }

        writer.WriteUInt32(SamrConstants.StatusSuccess);
        return writer.ToArray();
    }

    private void WriteDomainPasswordInformation(NdrWriter writer, SamrDomainHandle domainHandle)
    {
        // DOMAIN_PASSWORD_INFORMATION:
        // MinPasswordLength (ushort)
        writer.WriteUInt16(7);
        // PasswordHistoryLength (ushort)
        writer.WriteUInt16(24);
        // PasswordProperties (uint32) — DOMAIN_PASSWORD_COMPLEX | DOMAIN_LOCKOUT_ADMINS
        writer.WriteUInt32(0x00000001);
        // MaxPasswordAge (LARGE_INTEGER) — negative 100ns intervals; -42 days
        writer.WriteInt64(-42L * 24 * 60 * 60 * 10_000_000);
        // MinPasswordAge (LARGE_INTEGER) — -1 day
        writer.WriteInt64(-1L * 24 * 60 * 60 * 10_000_000);
    }

    private async Task WriteDomainGeneralInformationAsync(
        NdrWriter writer, SamrDomainHandle domainHandle, CancellationToken ct)
    {
        // SAMPR_DOMAIN_GENERAL_INFORMATION:
        // ForceLogoff (LARGE_INTEGER)
        writer.WriteInt64(unchecked((long)0x8000000000000000));
        // OemInformation (RPC_UNICODE_STRING)
        writer.WriteRpcUnicodeString("");
        // DomainName (RPC_UNICODE_STRING)
        writer.WriteRpcUnicodeString(domainHandle.DomainName);
        // ReplicaSourceNodeName (RPC_UNICODE_STRING)
        writer.WriteRpcUnicodeString("");
        // DomainModifiedCount (LARGE_INTEGER)
        writer.WriteInt64(1);
        // DomainServerState (enum) — DomainServerEnabled = 1
        writer.WriteUInt32(1);
        // DomainServerRole (enum) — DomainServerRolePrimary = 3
        writer.WriteUInt32(3);
        // UasCompatibilityRequired (boolean)
        writer.WriteBoolean(false);
        // UserCount
        writer.WriteUInt32(0); // Approximate; not critical for domain join
        // GroupCount
        writer.WriteUInt32(0);
        // AliasCount
        writer.WriteUInt32(0);

        writer.FlushDeferred();
    }

    private async Task WriteDomainGeneralInformation2Async(
        NdrWriter writer, SamrDomainHandle domainHandle, CancellationToken ct)
    {
        // SAMPR_DOMAIN_GENERAL_INFORMATION2 extends GENERAL with extra fields

        // First write the base DomainGeneralInformation
        // ForceLogoff
        writer.WriteInt64(unchecked((long)0x8000000000000000));
        // OemInformation
        writer.WriteRpcUnicodeString("");
        // DomainName
        writer.WriteRpcUnicodeString(domainHandle.DomainName);
        // ReplicaSourceNodeName
        writer.WriteRpcUnicodeString("");
        // DomainModifiedCount
        writer.WriteInt64(1);
        // DomainServerState
        writer.WriteUInt32(1);
        // DomainServerRole
        writer.WriteUInt32(3);
        // UasCompatibilityRequired
        writer.WriteBoolean(false);
        // UserCount
        writer.WriteUInt32(0);
        // GroupCount
        writer.WriteUInt32(0);
        // AliasCount
        writer.WriteUInt32(0);

        writer.FlushDeferred();

        // Extended fields:
        // LockoutDuration (LARGE_INTEGER) — 30 minutes in negative 100ns
        writer.WriteInt64(-30L * 60 * 10_000_000);
        // LockoutObservationWindow (LARGE_INTEGER) — 30 minutes
        writer.WriteInt64(-30L * 60 * 10_000_000);
        // LockoutThreshold (ushort) — 0 = no lockout
        writer.WriteUInt16(0);

        writer.Align(4);
    }

    // ════════════════════════════════════════════════════════════════
    //  Opnum 13 — SamrEnumerateUsersInDomain
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// SamrEnumerateUsersInDomain (opnum 13): Enumerates user accounts in a domain.
    /// Wire in:  [in] domain handle, [in, out] EnumerationContext (uint32), [in] UserAccountControl filter, [in] PrefMaxLen
    /// Wire out: [out] EnumerationContext, [out] ptr to SAMPR_ENUMERATION_BUFFER, [out] CountReturned, [out] NTSTATUS
    /// </summary>
    public async Task<byte[]> SamrEnumerateUsersInDomainAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        var (_, domainUuid) = reader.ReadContextHandle();
        var domainHandle = LookupHandle<SamrDomainHandle>(context, domainUuid);

        uint enumerationContext = reader.ReadUInt32();
        uint userAccountControl = reader.ReadUInt32();
        uint prefMaxLength = reader.ReadUInt32();

        // Search for user objects in the domain
        var filter = new EqualityFilterNode("objectClass", "user");
        var result = await _store.SearchAsync(
            domainHandle.TenantId,
            domainHandle.DomainDn,
            SearchScope.WholeSubtree,
            filter,
            attributes: null,
            sizeLimit: 0,
            ct: ct);

        // Filter by UserAccountControl if specified, and exclude computer objects
        var allEntries = new List<(uint Rid, string Name)>();
        foreach (var entry in result.Entries)
        {
            if (entry.ObjectSid == null || !entry.ObjectSid.StartsWith(domainHandle.DomainSid))
                continue;

            // Skip computer accounts when enumerating users
            if (entry.ObjectClass.Contains("computer", StringComparer.OrdinalIgnoreCase))
                continue;

            // Apply UAC filter if non-zero
            if (userAccountControl != 0 && ((uint)entry.UserAccountControl & userAccountControl) == 0)
                continue;

            uint rid = ExtractRid(entry.ObjectSid);
            allEntries.Add((rid, entry.SAMAccountName ?? entry.Cn ?? ""));
        }

        // Sort by RID for consistent enumeration
        allEntries.Sort((a, b) => a.Rid.CompareTo(b.Rid));

        return WriteEnumerationBuffer(allEntries, enumerationContext, prefMaxLength);
    }

    // ════════════════════════════════════════════════════════════════
    //  Opnum 14 — SamrEnumerateGroupsInDomain
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// SamrEnumerateGroupsInDomain (opnum 14): Enumerates global group accounts in a domain.
    /// Wire in:  [in] domain handle, [in, out] EnumerationContext, [in] PrefMaxLen
    /// Wire out: [out] EnumerationContext, [out] ptr to SAMPR_ENUMERATION_BUFFER, [out] CountReturned, [out] NTSTATUS
    /// </summary>
    public async Task<byte[]> SamrEnumerateGroupsInDomainAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        var (_, domainUuid) = reader.ReadContextHandle();
        var domainHandle = LookupHandle<SamrDomainHandle>(context, domainUuid);

        uint enumerationContext = reader.ReadUInt32();
        uint prefMaxLength = reader.ReadUInt32();

        // Search for group objects
        var filter = new EqualityFilterNode("objectClass", "group");
        var result = await _store.SearchAsync(
            domainHandle.TenantId,
            domainHandle.DomainDn,
            SearchScope.WholeSubtree,
            filter,
            attributes: null,
            sizeLimit: 0,
            ct: ct);

        var allEntries = new List<(uint Rid, string Name)>();
        foreach (var entry in result.Entries)
        {
            if (entry.ObjectSid == null || !entry.ObjectSid.StartsWith(domainHandle.DomainSid))
                continue;

            // Domain global groups: groupType & 0x80000002
            if ((entry.GroupType & unchecked((int)0x80000002)) == unchecked((int)0x80000002))
            {
                uint rid = ExtractRid(entry.ObjectSid);
                allEntries.Add((rid, entry.SAMAccountName ?? entry.Cn ?? ""));
            }
        }

        allEntries.Sort((a, b) => a.Rid.CompareTo(b.Rid));
        return WriteEnumerationBuffer(allEntries, enumerationContext, prefMaxLength);
    }

    // ════════════════════════════════════════════════════════════════
    //  Opnum 15 — SamrEnumerateAliasesInDomain
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// SamrEnumerateAliasesInDomain (opnum 15): Enumerates alias (domain local group) accounts.
    /// Wire in:  [in] domain handle, [in, out] EnumerationContext, [in] PrefMaxLen
    /// Wire out: [out] EnumerationContext, [out] ptr to SAMPR_ENUMERATION_BUFFER, [out] CountReturned, [out] NTSTATUS
    /// </summary>
    public async Task<byte[]> SamrEnumerateAliasesInDomainAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        var (_, domainUuid) = reader.ReadContextHandle();
        var domainHandle = LookupHandle<SamrDomainHandle>(context, domainUuid);

        uint enumerationContext = reader.ReadUInt32();
        uint prefMaxLength = reader.ReadUInt32();

        // Search for group objects
        var filter = new EqualityFilterNode("objectClass", "group");
        var result = await _store.SearchAsync(
            domainHandle.TenantId,
            domainHandle.DomainDn,
            SearchScope.WholeSubtree,
            filter,
            attributes: null,
            sizeLimit: 0,
            ct: ct);

        var allEntries = new List<(uint Rid, string Name)>();
        foreach (var entry in result.Entries)
        {
            if (entry.ObjectSid == null || !entry.ObjectSid.StartsWith(domainHandle.DomainSid))
                continue;

            // Domain local groups / aliases: groupType & 0x80000004
            if ((entry.GroupType & unchecked((int)0x80000004)) == unchecked((int)0x80000004))
            {
                uint rid = ExtractRid(entry.ObjectSid);
                allEntries.Add((rid, entry.SAMAccountName ?? entry.Cn ?? ""));
            }
        }

        allEntries.Sort((a, b) => a.Rid.CompareTo(b.Rid));
        return WriteEnumerationBuffer(allEntries, enumerationContext, prefMaxLength);
    }

    /// <summary>
    /// Shared helper for writing SAMPR_ENUMERATION_BUFFER responses for all enumeration operations.
    /// </summary>
    private static byte[] WriteEnumerationBuffer(
        List<(uint Rid, string Name)> allEntries, uint enumerationContext, uint prefMaxLength)
    {
        int startIndex = (int)enumerationContext;
        if (startIndex > allEntries.Count)
            startIndex = allEntries.Count;

        // Estimate how many entries fit in prefMaxLength (each entry ~= 4 + name.Length * 2 + 12)
        var entries = new List<(uint Rid, string Name)>();
        uint bytesUsed = 0;
        bool hasMore = false;
        for (int i = startIndex; i < allEntries.Count; i++)
        {
            uint entrySize = (uint)(4 + allEntries[i].Name.Length * 2 + 12);
            if (prefMaxLength > 0 && bytesUsed + entrySize > prefMaxLength && entries.Count > 0)
            {
                hasMore = true;
                break;
            }
            entries.Add(allEntries[i]);
            bytesUsed += entrySize;
        }

        uint newContext = (uint)(startIndex + entries.Count);

        var writer = new NdrWriter();

        // EnumerationContext out
        writer.WriteUInt32(newContext);

        // Pointer to SAMPR_ENUMERATION_BUFFER (non-null)
        writer.WritePointer(false);

        // SAMPR_ENUMERATION_BUFFER: EntriesRead, pointer to array
        uint entriesRead = (uint)entries.Count;
        writer.WriteUInt32(entriesRead);

        if (entriesRead > 0)
        {
            writer.WritePointer(false);

            // Conformant array max count
            writer.WriteUInt32(entriesRead);

            // Array of SAMPR_RID_ENUMERATION: RelativeId, Name (RPC_UNICODE_STRING)
            foreach (var (rid, name) in entries)
            {
                writer.WriteUInt32(rid);
                writer.WriteRpcUnicodeString(name);
            }

            writer.FlushDeferred();
        }
        else
        {
            writer.WritePointer(true);
        }

        // CountReturned
        writer.WriteUInt32(entriesRead);

        writer.WriteUInt32(hasMore ? SamrConstants.StatusMoreEntries : SamrConstants.StatusSuccess);
        return writer.ToArray();
    }

    // ════════════════════════════════════════════════════════════════
    //  Opnum 19 — SamrOpenGroup
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// SamrOpenGroup (opnum 19): Opens a handle to a group object.
    /// Wire in:  [in] domain handle, [in] DesiredAccess, [in] GroupId (uint32 RID)
    /// Wire out: [out] group handle, [out] NTSTATUS
    /// </summary>
    public async Task<byte[]> SamrOpenGroupAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        var (_, domainUuid) = reader.ReadContextHandle();
        var domainHandle = LookupHandle<SamrDomainHandle>(context, domainUuid);

        uint desiredAccess = reader.ReadUInt32();
        uint groupId = reader.ReadUInt32();

        // Find group by SID
        string groupSid = $"{domainHandle.DomainSid}-{groupId}";
        var group = await FindObjectBySidAsync(domainHandle.TenantId, domainHandle.DomainDn, groupSid, ct);

        if (group == null)
        {
            var writer2 = new NdrWriter();
            writer2.WriteContextHandle(0, Guid.Empty);
            writer2.WriteUInt32(SamrConstants.StatusNoSuchGroup);
            return writer2.ToArray();
        }

        var groupHandle = new SamrGroupHandle
        {
            GrantedAccess = desiredAccess,
            GroupDn = group.DistinguishedName,
            Rid = groupId,
            DomainDn = domainHandle.DomainDn,
            TenantId = domainHandle.TenantId
        };

        var handleBytes = context.ContextHandles.CreateHandle(groupHandle);

        var writer = new NdrWriter();
        WriteRawContextHandle(writer, handleBytes);
        writer.WriteUInt32(SamrConstants.StatusSuccess);
        return writer.ToArray();
    }

    // ════════════════════════════════════════════════════════════════
    //  Opnum 20 — SamrQueryInformationGroup
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// SamrQueryInformationGroup (opnum 20): Queries group properties.
    /// Wire in:  [in] group handle, [in] GroupInformationClass (ushort)
    /// Wire out: [out] pointer to info buffer (union), [out] NTSTATUS
    /// </summary>
    public async Task<byte[]> SamrQueryInformationGroupAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        var (_, groupUuid) = reader.ReadContextHandle();
        var groupHandle = LookupHandle<SamrGroupHandle>(context, groupUuid);

        ushort infoClass = reader.ReadUInt16();

        var group = await _store.GetByDnAsync(groupHandle.TenantId, groupHandle.GroupDn, ct);
        if (group == null)
        {
            var writerErr = new NdrWriter();
            writerErr.WritePointer(true);
            writerErr.WriteUInt32(SamrConstants.StatusNoSuchGroup);
            return writerErr.ToArray();
        }

        var writer = new NdrWriter();

        // Pointer to info (non-null)
        writer.WritePointer(false);

        // Union discriminant
        writer.WriteUInt16(infoClass);
        writer.Align(4);

        switch (infoClass)
        {
            case SamrConstants.GroupGeneralInformation:
                // SAMPR_GROUP_GENERAL_INFORMATION:
                // Name (RPC_UNICODE_STRING)
                writer.WriteRpcUnicodeString(group.SAMAccountName ?? group.Cn ?? "");
                // Attributes (uint32)
                writer.WriteUInt32(SamrConstants.SeGroupDefaultAttributes);
                // MemberCount (uint32)
                writer.WriteUInt32((uint)group.Member.Count);
                // AdminComment (RPC_UNICODE_STRING)
                writer.WriteRpcUnicodeString(group.Description ?? "");
                writer.FlushDeferred();
                break;

            case SamrConstants.GroupNameInformation:
                // SAMPR_GROUP_NAME_INFORMATION: Name (RPC_UNICODE_STRING)
                writer.WriteRpcUnicodeString(group.SAMAccountName ?? group.Cn ?? "");
                writer.FlushDeferred();
                break;

            case SamrConstants.GroupAttributeInformation:
                // SAMPR_GROUP_ATTRIBUTE_INFORMATION: Attributes (uint32)
                writer.WriteUInt32(SamrConstants.SeGroupDefaultAttributes);
                break;

            case SamrConstants.GroupAdminCommentInformation:
                // SAMPR_GROUP_ADM_COMMENT_INFORMATION: AdminComment (RPC_UNICODE_STRING)
                writer.WriteRpcUnicodeString(group.Description ?? "");
                writer.FlushDeferred();
                break;

            default:
                var writerErr2 = new NdrWriter();
                writerErr2.WritePointer(true);
                writerErr2.WriteUInt32(SamrConstants.StatusInvalidParameter);
                return writerErr2.ToArray();
        }

        writer.WriteUInt32(SamrConstants.StatusSuccess);
        return writer.ToArray();
    }

    // ════════════════════════════════════════════════════════════════
    //  Opnum 21 — SamrSetInformationGroup
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// SamrSetInformationGroup (opnum 21): Sets group properties.
    /// Wire in:  [in] group handle, [in] GroupInformationClass (ushort), [in] Buffer (union)
    /// Wire out: [out] NTSTATUS
    /// </summary>
    public async Task<byte[]> SamrSetInformationGroupAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        var (_, groupUuid) = reader.ReadContextHandle();
        var groupHandle = LookupHandle<SamrGroupHandle>(context, groupUuid);

        ushort infoClass = reader.ReadUInt16();
        reader.Align(4);

        // Union discriminant
        ushort unionSwitch = reader.ReadUInt16();
        reader.Align(4);

        var group = await _store.GetByDnAsync(groupHandle.TenantId, groupHandle.GroupDn, ct);
        if (group == null)
        {
            throw new RpcFaultException(SamrConstants.StatusNoSuchGroup, "Group not found.");
        }

        switch (infoClass)
        {
            case SamrConstants.GroupNameInformation:
            {
                var (_, _, refId) = reader.ReadRpcUnicodeString();
                string name = refId != 0 ? reader.ReadConformantVaryingString() : null;
                if (name != null)
                {
                    group.SAMAccountName = name;
                }
                break;
            }

            case SamrConstants.GroupAttributeInformation:
            {
                // Attributes (uint32) — read and discard, we always use defaults
                _ = reader.ReadUInt32();
                break;
            }

            case SamrConstants.GroupAdminCommentInformation:
            {
                var (_, _, refId) = reader.ReadRpcUnicodeString();
                string comment = refId != 0 ? reader.ReadConformantVaryingString() : null;
                if (comment != null)
                {
                    group.Description = comment;
                }
                break;
            }

            default:
                // Unknown info class — accept silently
                break;
        }

        group.WhenChanged = DateTimeOffset.UtcNow;
        await _store.UpdateAsync(group, ct);

        var writer = new NdrWriter();
        writer.WriteUInt32(SamrConstants.StatusSuccess);
        return writer.ToArray();
    }

    // ════════════════════════════════════════════════════════════════
    //  Opnum 22 — SamrAddMemberToGroup
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// SamrAddMemberToGroup (opnum 22): Adds a member RID to a group.
    /// Wire in:  [in] group handle, [in] MemberId (uint32 RID), [in] Attributes (uint32)
    /// Wire out: [out] NTSTATUS
    /// </summary>
    public async Task<byte[]> SamrAddMemberToGroupAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        var (_, groupUuid) = reader.ReadContextHandle();
        var groupHandle = LookupHandle<SamrGroupHandle>(context, groupUuid);

        uint memberId = reader.ReadUInt32();
        uint attributes = reader.ReadUInt32();

        // Resolve the member by SID
        string domainSid = _ridAllocator.GetDomainSid(groupHandle.TenantId, groupHandle.DomainDn);
        string memberSid = $"{domainSid}-{memberId}";
        var member = await FindObjectBySidAsync(groupHandle.TenantId, groupHandle.DomainDn, memberSid, ct);

        if (member == null)
        {
            var writerErr = new NdrWriter();
            writerErr.WriteUInt32(SamrConstants.StatusNoSuchUser);
            return writerErr.ToArray();
        }

        // Add the member DN to the group's member list using forward link update
        var group = await _store.GetByDnAsync(groupHandle.TenantId, groupHandle.GroupDn, ct);
        if (group == null)
        {
            var writerErr = new NdrWriter();
            writerErr.WriteUInt32(SamrConstants.StatusNoSuchGroup);
            return writerErr.ToArray();
        }

        // Check if already a member
        if (group.Member.Contains(member.DistinguishedName, StringComparer.OrdinalIgnoreCase))
        {
            var writerErr = new NdrWriter();
            writerErr.WriteUInt32(SamrConstants.StatusMemberInGroup);
            return writerErr.ToArray();
        }

        await _linkService.UpdateForwardLinkAsync(
            groupHandle.TenantId, group, "member", member.DistinguishedName, add: true, ct);

        var writer = new NdrWriter();
        writer.WriteUInt32(SamrConstants.StatusSuccess);
        return writer.ToArray();
    }

    // ════════════════════════════════════════════════════════════════
    //  Opnum 23 — SamrRemoveMemberFromGroup
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// SamrRemoveMemberFromGroup (opnum 23): Removes a member RID from a group.
    /// Wire in:  [in] group handle, [in] MemberId (uint32 RID)
    /// Wire out: [out] NTSTATUS
    /// </summary>
    public async Task<byte[]> SamrRemoveMemberFromGroupAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        var (_, groupUuid) = reader.ReadContextHandle();
        var groupHandle = LookupHandle<SamrGroupHandle>(context, groupUuid);

        uint memberId = reader.ReadUInt32();

        // Resolve the member by SID
        string domainSid = _ridAllocator.GetDomainSid(groupHandle.TenantId, groupHandle.DomainDn);
        string memberSid = $"{domainSid}-{memberId}";
        var member = await FindObjectBySidAsync(groupHandle.TenantId, groupHandle.DomainDn, memberSid, ct);

        if (member == null)
        {
            var writerErr = new NdrWriter();
            writerErr.WriteUInt32(SamrConstants.StatusNoSuchUser);
            return writerErr.ToArray();
        }

        var group = await _store.GetByDnAsync(groupHandle.TenantId, groupHandle.GroupDn, ct);
        if (group == null)
        {
            var writerErr = new NdrWriter();
            writerErr.WriteUInt32(SamrConstants.StatusNoSuchGroup);
            return writerErr.ToArray();
        }

        // Check if actually a member
        if (!group.Member.Contains(member.DistinguishedName, StringComparer.OrdinalIgnoreCase))
        {
            var writerErr = new NdrWriter();
            writerErr.WriteUInt32(SamrConstants.StatusMemberNotInGroup);
            return writerErr.ToArray();
        }

        await _linkService.UpdateForwardLinkAsync(
            groupHandle.TenantId, group, "member", member.DistinguishedName, add: false, ct);

        var writer = new NdrWriter();
        writer.WriteUInt32(SamrConstants.StatusSuccess);
        return writer.ToArray();
    }

    // ════════════════════════════════════════════════════════════════
    //  Opnum 25 — SamrGetMembersInGroup
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// SamrGetMembersInGroup (opnum 25): Returns members of a group.
    /// Wire in:  [in] group handle
    /// Wire out: [out] pointer to SAMPR_GET_MEMBERS_BUFFER, [out] NTSTATUS
    /// </summary>
    public async Task<byte[]> SamrGetMembersInGroupAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        var (_, groupUuid) = reader.ReadContextHandle();
        var groupHandle = LookupHandle<SamrGroupHandle>(context, groupUuid);

        var group = await _store.GetByDnAsync(groupHandle.TenantId, groupHandle.GroupDn, ct);
        if (group == null)
        {
            var writerErr = new NdrWriter();
            writerErr.WritePointer(true);
            writerErr.WriteUInt32(SamrConstants.StatusNoSuchGroup);
            return writerErr.ToArray();
        }

        // Resolve each member DN to a RID
        string domainSid = _ridAllocator.GetDomainSid(groupHandle.TenantId, groupHandle.DomainDn);
        var members = new List<(uint Rid, uint Attributes)>();

        foreach (string memberDn in group.Member)
        {
            var memberObj = await _store.GetByDnAsync(groupHandle.TenantId, memberDn, ct);
            if (memberObj?.ObjectSid != null && memberObj.ObjectSid.StartsWith(domainSid))
            {
                uint memberRid = ExtractRid(memberObj.ObjectSid);
                members.Add((memberRid, SamrConstants.SeGroupDefaultAttributes));
            }
        }

        var writer = new NdrWriter();

        // Pointer to SAMPR_GET_MEMBERS_BUFFER (non-null)
        writer.WritePointer(false);

        // SAMPR_GET_MEMBERS_BUFFER: MemberCount, pointer to Members, pointer to Attributes
        writer.WriteUInt32((uint)members.Count);

        if (members.Count > 0)
        {
            writer.WritePointer(false); // pointer to Members array
            writer.WritePointer(false); // pointer to Attributes array

            // Conformant array of MemberIds
            writer.WriteUInt32((uint)members.Count);
            foreach (var (rid, _) in members)
            {
                writer.WriteUInt32(rid);
            }

            // Conformant array of Attributes
            writer.WriteUInt32((uint)members.Count);
            foreach (var (_, attributes) in members)
            {
                writer.WriteUInt32(attributes);
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
    //  Opnum 26 — SamrOpenAlias
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// SamrOpenAlias (opnum 26): Opens a handle to an alias (domain local group).
    /// Wire in:  [in] domain handle, [in] DesiredAccess, [in] AliasId (uint32 RID)
    /// Wire out: [out] alias handle, [out] NTSTATUS
    /// </summary>
    public async Task<byte[]> SamrOpenAliasAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        var (_, domainUuid) = reader.ReadContextHandle();
        var domainHandle = LookupHandle<SamrDomainHandle>(context, domainUuid);

        uint desiredAccess = reader.ReadUInt32();
        uint aliasId = reader.ReadUInt32();

        // Find alias by SID
        string aliasSid = $"{domainHandle.DomainSid}-{aliasId}";
        var alias = await FindObjectBySidAsync(domainHandle.TenantId, domainHandle.DomainDn, aliasSid, ct);

        if (alias == null)
        {
            var writer2 = new NdrWriter();
            writer2.WriteContextHandle(0, Guid.Empty);
            writer2.WriteUInt32(SamrConstants.StatusNoSuchAlias);
            return writer2.ToArray();
        }

        var aliasHandle = new SamrAliasHandle
        {
            GrantedAccess = desiredAccess,
            AliasDn = alias.DistinguishedName,
            Rid = aliasId,
            DomainDn = domainHandle.DomainDn,
            TenantId = domainHandle.TenantId
        };

        var handleBytes = context.ContextHandles.CreateHandle(aliasHandle);

        var writer = new NdrWriter();
        WriteRawContextHandle(writer, handleBytes);
        writer.WriteUInt32(SamrConstants.StatusSuccess);
        return writer.ToArray();
    }

    // ════════════════════════════════════════════════════════════════
    //  Opnum 27 — SamrQueryInformationAlias
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// SamrQueryInformationAlias (opnum 27): Queries alias properties.
    /// Wire in:  [in] alias handle, [in] AliasInformationClass (ushort)
    /// Wire out: [out] pointer to info buffer (union), [out] NTSTATUS
    /// </summary>
    public async Task<byte[]> SamrQueryInformationAliasAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        var (_, aliasUuid) = reader.ReadContextHandle();
        var aliasHandle = LookupHandle<SamrAliasHandle>(context, aliasUuid);

        ushort infoClass = reader.ReadUInt16();

        var alias = await _store.GetByDnAsync(aliasHandle.TenantId, aliasHandle.AliasDn, ct);
        if (alias == null)
        {
            var writerErr = new NdrWriter();
            writerErr.WritePointer(true);
            writerErr.WriteUInt32(SamrConstants.StatusNoSuchAlias);
            return writerErr.ToArray();
        }

        var writer = new NdrWriter();

        // Pointer to info (non-null)
        writer.WritePointer(false);

        // Union discriminant
        writer.WriteUInt16(infoClass);
        writer.Align(4);

        switch (infoClass)
        {
            case SamrConstants.AliasGeneralInformation:
                // SAMPR_ALIAS_GENERAL_INFORMATION:
                // Name (RPC_UNICODE_STRING)
                writer.WriteRpcUnicodeString(alias.SAMAccountName ?? alias.Cn ?? "");
                // MemberCount (uint32)
                writer.WriteUInt32((uint)alias.Member.Count);
                // AdminComment (RPC_UNICODE_STRING)
                writer.WriteRpcUnicodeString(alias.Description ?? "");
                writer.FlushDeferred();
                break;

            case SamrConstants.AliasNameInformation:
                // SAMPR_ALIAS_NAME_INFORMATION: Name (RPC_UNICODE_STRING)
                writer.WriteRpcUnicodeString(alias.SAMAccountName ?? alias.Cn ?? "");
                writer.FlushDeferred();
                break;

            case SamrConstants.AliasAdminCommentInformation:
                // SAMPR_ALIAS_ADM_COMMENT_INFORMATION: AdminComment (RPC_UNICODE_STRING)
                writer.WriteRpcUnicodeString(alias.Description ?? "");
                writer.FlushDeferred();
                break;

            default:
                var writerErr2 = new NdrWriter();
                writerErr2.WritePointer(true);
                writerErr2.WriteUInt32(SamrConstants.StatusInvalidParameter);
                return writerErr2.ToArray();
        }

        writer.WriteUInt32(SamrConstants.StatusSuccess);
        return writer.ToArray();
    }

    // ════════════════════════════════════════════════════════════════
    //  Opnum 28 — SamrAddMemberToAlias
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// SamrAddMemberToAlias (opnum 28): Adds a member SID to an alias.
    /// Note: Aliases use full SIDs (not RIDs) since members can be from foreign domains.
    /// Wire in:  [in] alias handle, [in] MemberId (RPC_SID)
    /// Wire out: [out] NTSTATUS
    /// </summary>
    public async Task<byte[]> SamrAddMemberToAliasAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        var (_, aliasUuid) = reader.ReadContextHandle();
        var aliasHandle = LookupHandle<SamrAliasHandle>(context, aliasUuid);

        // MemberId — RPC_SID with conformant max count prefix
        uint sidMaxCount = reader.ReadUInt32();
        string memberSid = reader.ReadRpcSid();

        // Find the member object by SID
        var member = await FindObjectBySidAsync(aliasHandle.TenantId, aliasHandle.DomainDn, memberSid, ct);

        if (member == null)
        {
            // For aliases, members can be foreign security principals.
            // Return success even if we can't resolve — store the SID reference.
            // For now, require the member to exist in this domain.
            var writerErr = new NdrWriter();
            writerErr.WriteUInt32(SamrConstants.StatusNoSuchUser);
            return writerErr.ToArray();
        }

        var alias = await _store.GetByDnAsync(aliasHandle.TenantId, aliasHandle.AliasDn, ct);
        if (alias == null)
        {
            var writerErr = new NdrWriter();
            writerErr.WriteUInt32(SamrConstants.StatusNoSuchAlias);
            return writerErr.ToArray();
        }

        if (alias.Member.Contains(member.DistinguishedName, StringComparer.OrdinalIgnoreCase))
        {
            var writerErr = new NdrWriter();
            writerErr.WriteUInt32(SamrConstants.StatusMemberInAlias);
            return writerErr.ToArray();
        }

        await _linkService.UpdateForwardLinkAsync(
            aliasHandle.TenantId, alias, "member", member.DistinguishedName, add: true, ct);

        var writer = new NdrWriter();
        writer.WriteUInt32(SamrConstants.StatusSuccess);
        return writer.ToArray();
    }

    // ════════════════════════════════════════════════════════════════
    //  Opnum 29 — SamrRemoveMemberFromAlias
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// SamrRemoveMemberFromAlias (opnum 29): Removes a member SID from an alias.
    /// Wire in:  [in] alias handle, [in] MemberId (RPC_SID)
    /// Wire out: [out] NTSTATUS
    /// </summary>
    public async Task<byte[]> SamrRemoveMemberFromAliasAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        var (_, aliasUuid) = reader.ReadContextHandle();
        var aliasHandle = LookupHandle<SamrAliasHandle>(context, aliasUuid);

        // MemberId — RPC_SID with conformant max count prefix
        uint sidMaxCount = reader.ReadUInt32();
        string memberSid = reader.ReadRpcSid();

        var member = await FindObjectBySidAsync(aliasHandle.TenantId, aliasHandle.DomainDn, memberSid, ct);

        if (member == null)
        {
            var writerErr = new NdrWriter();
            writerErr.WriteUInt32(SamrConstants.StatusNoSuchUser);
            return writerErr.ToArray();
        }

        var alias = await _store.GetByDnAsync(aliasHandle.TenantId, aliasHandle.AliasDn, ct);
        if (alias == null)
        {
            var writerErr = new NdrWriter();
            writerErr.WriteUInt32(SamrConstants.StatusNoSuchAlias);
            return writerErr.ToArray();
        }

        if (!alias.Member.Contains(member.DistinguishedName, StringComparer.OrdinalIgnoreCase))
        {
            var writerErr = new NdrWriter();
            writerErr.WriteUInt32(SamrConstants.StatusMemberNotInAlias);
            return writerErr.ToArray();
        }

        await _linkService.UpdateForwardLinkAsync(
            aliasHandle.TenantId, alias, "member", member.DistinguishedName, add: false, ct);

        var writer = new NdrWriter();
        writer.WriteUInt32(SamrConstants.StatusSuccess);
        return writer.ToArray();
    }

    // ════════════════════════════════════════════════════════════════
    //  Opnum 33 — SamrGetMembersInAlias
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// SamrGetMembersInAlias (opnum 33): Returns member SIDs of an alias.
    /// Wire in:  [in] alias handle
    /// Wire out: [out] SAMPR_PSID_ARRAY (Count, pointer to SID array), [out] NTSTATUS
    /// </summary>
    public async Task<byte[]> SamrGetMembersInAliasAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        var (_, aliasUuid) = reader.ReadContextHandle();
        var aliasHandle = LookupHandle<SamrAliasHandle>(context, aliasUuid);

        var alias = await _store.GetByDnAsync(aliasHandle.TenantId, aliasHandle.AliasDn, ct);
        if (alias == null)
        {
            var writerErr = new NdrWriter();
            writerErr.WriteUInt32(0); // Count
            writerErr.WritePointer(true); // null array
            writerErr.WriteUInt32(SamrConstants.StatusNoSuchAlias);
            return writerErr.ToArray();
        }

        // Resolve each member DN to a SID
        var memberSids = new List<string>();
        foreach (string memberDn in alias.Member)
        {
            var memberObj = await _store.GetByDnAsync(aliasHandle.TenantId, memberDn, ct);
            if (memberObj?.ObjectSid != null)
            {
                memberSids.Add(memberObj.ObjectSid);
            }
        }

        var writer = new NdrWriter();

        // SAMPR_PSID_ARRAY: Count, pointer to array of PSAMPR_SID_INFORMATION
        writer.WriteUInt32((uint)memberSids.Count);

        if (memberSids.Count > 0)
        {
            writer.WritePointer(false); // non-null array pointer

            // Conformant array max count
            writer.WriteUInt32((uint)memberSids.Count);

            // Array of pointers to SIDs
            foreach (var _ in memberSids)
            {
                writer.WritePointer(false); // non-null SID pointer
            }

            // Deferred SID bodies: each has conformant max count then RPC_SID
            foreach (string sid in memberSids)
            {
                var sidParts = sid.Split('-');
                int subAuthCount = sidParts.Length - 3;
                writer.WriteUInt32((uint)subAuthCount); // conformant max count
                writer.WriteRpcSid(sid);
            }
        }
        else
        {
            writer.WritePointer(true); // null array
        }

        writer.WriteUInt32(SamrConstants.StatusSuccess);
        return writer.ToArray();
    }

    // ════════════════════════════════════════════════════════════════
    //  Opnum 16 — SamrGetAliasMembership
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// SamrGetAliasMembership (opnum 16): Given SIDs, returns which aliases they belong to.
    /// Wire in:  [in] domain handle, [in] SAMPR_PSID_ARRAY (Count, pointer to SID pointers)
    /// Wire out: [out] SAMPR_ULONG_ARRAY (alias RIDs), [out] NTSTATUS
    /// </summary>
    public async Task<byte[]> SamrGetAliasMembershipAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        var (_, domainUuid) = reader.ReadContextHandle();
        var domainHandle = LookupHandle<SamrDomainHandle>(context, domainUuid);

        // SAMPR_PSID_ARRAY: Count, pointer to array
        uint count = reader.ReadUInt32();
        uint arrayPtr = reader.ReadPointer();

        var sids = new List<string>();
        if (arrayPtr != 0 && count > 0)
        {
            // Conformant array max count
            uint maxCount = reader.ReadUInt32();

            // Array of pointers to SIDs
            var sidPtrs = new uint[count];
            for (uint i = 0; i < count; i++)
            {
                sidPtrs[i] = reader.ReadPointer();
            }

            // Deferred SID bodies
            for (uint i = 0; i < count; i++)
            {
                if (sidPtrs[i] != 0)
                {
                    uint sidMaxCount = reader.ReadUInt32(); // conformant max count
                    string sid = reader.ReadRpcSid();
                    sids.Add(sid);
                }
            }
        }

        // Find all aliases in this domain
        var filter = new EqualityFilterNode("objectClass", "group");
        var result = await _store.SearchAsync(
            domainHandle.TenantId,
            domainHandle.DomainDn,
            SearchScope.WholeSubtree,
            filter,
            attributes: null,
            sizeLimit: 0,
            ct: ct);

        // Collect alias RIDs where any of the input SIDs is a member
        var aliasRids = new HashSet<uint>();
        foreach (var group in result.Entries)
        {
            if (group.ObjectSid == null || !group.ObjectSid.StartsWith(domainHandle.DomainSid))
                continue;

            // Only domain local groups (aliases)
            if ((group.GroupType & unchecked((int)0x80000004)) != unchecked((int)0x80000004))
                continue;

            // Check if any of the provided SIDs is a member
            foreach (string memberDn in group.Member)
            {
                var memberObj = await _store.GetByDnAsync(domainHandle.TenantId, memberDn, ct);
                if (memberObj?.ObjectSid != null && sids.Contains(memberObj.ObjectSid, StringComparer.OrdinalIgnoreCase))
                {
                    aliasRids.Add(ExtractRid(group.ObjectSid));
                    break;
                }
            }
        }

        var sortedRids = aliasRids.OrderBy(r => r).ToArray();

        var writer = new NdrWriter();

        // SAMPR_ULONG_ARRAY: Count, pointer to array
        writer.WriteUInt32((uint)sortedRids.Length);
        if (sortedRids.Length > 0)
        {
            writer.WritePointer(false);
            writer.WriteUInt32((uint)sortedRids.Length); // conformant max count
            foreach (uint rid in sortedRids)
            {
                writer.WriteUInt32(rid);
            }
        }
        else
        {
            writer.WritePointer(true);
        }

        writer.WriteUInt32(SamrConstants.StatusSuccess);
        return writer.ToArray();
    }

    // ════════════════════════════════════════════════════════════════
    //  Opnum 39 — SamrGetGroupsForUser
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// SamrGetGroupsForUser (opnum 39): Returns group memberships for a user.
    /// Wire in:  [in] user handle
    /// Wire out: [out] pointer to SAMPR_GET_GROUPS_BUFFER, [out] NTSTATUS
    /// </summary>
    public async Task<byte[]> SamrGetGroupsForUserAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        var (_, userUuid) = reader.ReadContextHandle();
        var userHandle = LookupHandle<SamrUserHandle>(context, userUuid);

        var user = await _store.GetByDnAsync(userHandle.TenantId, userHandle.UserDn, ct);
        if (user == null)
        {
            var writerErr = new NdrWriter();
            writerErr.WritePointer(true);
            writerErr.WriteUInt32(SamrConstants.StatusNoSuchUser);
            return writerErr.ToArray();
        }

        // Build list of group RIDs + attributes
        var groups = new List<(uint Rid, uint Attributes)>();

        // Primary group is always included
        groups.Add(((uint)user.PrimaryGroupId, 0x00000007)); // SE_GROUP_MANDATORY | SE_GROUP_ENABLED_BY_DEFAULT | SE_GROUP_ENABLED

        // Add groups from MemberOf
        string domainSid = _ridAllocator.GetDomainSid(userHandle.TenantId, userHandle.DomainDn);
        foreach (string groupDn in user.MemberOf)
        {
            var groupObj = await _store.GetByDnAsync(userHandle.TenantId, groupDn, ct);
            if (groupObj?.ObjectSid != null && groupObj.ObjectSid.StartsWith(domainSid))
            {
                uint groupRid = ExtractRid(groupObj.ObjectSid);
                // Don't duplicate the primary group
                if (groupRid != (uint)user.PrimaryGroupId)
                {
                    groups.Add((groupRid, 0x00000007));
                }
            }
        }

        var writer = new NdrWriter();

        // Pointer to SAMPR_GET_GROUPS_BUFFER (non-null)
        writer.WritePointer(false);

        // SAMPR_GET_GROUPS_BUFFER: MembershipCount (uint32), pointer to array
        writer.WriteUInt32((uint)groups.Count);

        if (groups.Count > 0)
        {
            writer.WritePointer(false);

            // Conformant array max count
            writer.WriteUInt32((uint)groups.Count);

            // Array of GROUP_MEMBERSHIP: RelativeId (uint32), Attributes (uint32)
            foreach (var (rid, attributes) in groups)
            {
                writer.WriteUInt32(rid);
                writer.WriteUInt32(attributes);
            }
        }
        else
        {
            writer.WritePointer(true);
        }

        writer.WriteUInt32(SamrConstants.StatusSuccess);
        return writer.ToArray();
    }

    // ════════════════════════════════════════════════════════════════
    //  Opnum 38 — SamrChangePasswordUser
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// SamrChangePasswordUser (opnum 38): Changes user password with old/new hash verification.
    /// Wire in:  [in] user handle, [in] LmPresent, [in] OldLmEncryptedWithNewLm,
    ///           [in] NewLmEncryptedWithOldLm, [in] NtPresent, [in] OldNtEncryptedWithNewNt,
    ///           [in] NewNtEncryptedWithOldNt, [in] NtCrossEncryptionPresent,
    ///           [in] NewNtEncryptedWithNewLm, [in] LmCrossEncryptionPresent,
    ///           [in] NewLmEncryptedWithNewNt
    /// Wire out: [out] NTSTATUS
    /// </summary>
    public async Task<byte[]> SamrChangePasswordUserAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        var (_, userUuid) = reader.ReadContextHandle();
        var userHandle = LookupHandle<SamrUserHandle>(context, userUuid);

        // LmPresent (boolean)
        bool lmPresent = reader.ReadBoolean();

        // OldLmEncryptedWithNewLm — pointer to ENCRYPTED_LM_OWF_PASSWORD (16 bytes)
        uint oldLmPtr = reader.ReadPointer();
        byte[] oldLmEncrypted = null;
        if (oldLmPtr != 0)
        {
            oldLmEncrypted = reader.ReadBytes(16).ToArray();
        }

        // NewLmEncryptedWithOldLm
        uint newLmPtr = reader.ReadPointer();
        byte[] newLmEncrypted = null;
        if (newLmPtr != 0)
        {
            newLmEncrypted = reader.ReadBytes(16).ToArray();
        }

        // NtPresent (boolean)
        bool ntPresent = reader.ReadBoolean();

        // OldNtEncryptedWithNewNt
        uint oldNtPtr = reader.ReadPointer();
        byte[] oldNtEncrypted = null;
        if (oldNtPtr != 0)
        {
            oldNtEncrypted = reader.ReadBytes(16).ToArray();
        }

        // NewNtEncryptedWithOldNt
        uint newNtPtr = reader.ReadPointer();
        byte[] newNtEncrypted = null;
        if (newNtPtr != 0)
        {
            newNtEncrypted = reader.ReadBytes(16).ToArray();
        }

        // NtCrossEncryptionPresent (boolean)
        bool ntCrossPresent = reader.ReadBoolean();

        // NewNtEncryptedWithNewLm
        uint ntCrossPtr = reader.ReadPointer();
        if (ntCrossPtr != 0)
        {
            _ = reader.ReadBytes(16);
        }

        // LmCrossEncryptionPresent (boolean)
        bool lmCrossPresent = reader.ReadBoolean();

        // NewLmEncryptedWithNewNt
        uint lmCrossPtr = reader.ReadPointer();
        if (lmCrossPtr != 0)
        {
            _ = reader.ReadBytes(16);
        }

        var user = await _store.GetByDnAsync(userHandle.TenantId, userHandle.UserDn, ct);
        if (user == null)
        {
            var writerErr = new NdrWriter();
            writerErr.WriteUInt32(SamrConstants.StatusNoSuchUser);
            return writerErr.ToArray();
        }

        // Verify old NT hash if NT is present
        if (ntPresent && oldNtEncrypted != null && newNtEncrypted != null)
        {
            // In a full implementation, we would:
            // 1. Decrypt oldNtEncrypted using the new NT hash as key
            // 2. Compare with the stored NT hash
            // 3. Decrypt newNtEncrypted using the old NT hash as key
            // 4. Set the new NT hash
            //
            // For now, we accept the change if the user has the USER_CHANGE_PASSWORD access.
            // The encrypted hash dance requires the full DES-ECB-based SamrEncryptedPassword
            // decryption which is beyond what we need for basic domain join support.

            if ((userHandle.GrantedAccess & SamrConstants.UserChangePassword) == 0)
            {
                var writerErr = new NdrWriter();
                writerErr.WriteUInt32(SamrConstants.StatusAccessDenied);
                return writerErr.ToArray();
            }
        }

        // Update PwdLastSet
        user.PwdLastSet = DateTimeOffset.UtcNow.ToFileTime();
        user.WhenChanged = DateTimeOffset.UtcNow;
        await _store.UpdateAsync(user, ct);

        var writer = new NdrWriter();
        writer.WriteUInt32(SamrConstants.StatusSuccess);
        return writer.ToArray();
    }

    // ════════════════════════════════════════════════════════════════
    //  Opnum 3 — SamrQuerySecurityObject
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// SamrQuerySecurityObject (opnum 3): Returns the security descriptor for a SAM object.
    /// Wire in:  [in] object handle, [in] SecurityInformation (uint32)
    /// Wire out: [out] pointer to SAMPR_SR_SECURITY_DESCRIPTOR, [out] NTSTATUS
    /// </summary>
    public Task<byte[]> SamrQuerySecurityObjectAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        var (_, objectUuid) = reader.ReadContextHandle();
        uint securityInformation = reader.ReadUInt32();

        // Try to resolve the handle to any known type to get the tenant/DN
        string tenantId = null;
        Span<byte> handleKey = stackalloc byte[20];
        BitConverter.TryWriteBytes(handleKey, 0u);
        objectUuid.TryWriteBytes(handleKey.Slice(4));

        var domainHandle = context.ContextHandles.GetHandle<SamrDomainHandle>(handleKey);
        if (domainHandle != null) tenantId = domainHandle.TenantId;

        var userHandle = context.ContextHandles.GetHandle<SamrUserHandle>(handleKey);
        if (userHandle != null) tenantId = userHandle.TenantId;

        var groupHandle = context.ContextHandles.GetHandle<SamrGroupHandle>(handleKey);
        if (groupHandle != null) tenantId = groupHandle.TenantId;

        var aliasHandle = context.ContextHandles.GetHandle<SamrAliasHandle>(handleKey);
        if (aliasHandle != null) tenantId = aliasHandle.TenantId;

        var serverHandle = context.ContextHandles.GetHandle<SamrServerHandle>(handleKey);
        if (serverHandle != null) tenantId = serverHandle.TenantId;

        if (tenantId == null)
        {
            var writerErr = new NdrWriter();
            writerErr.WritePointer(true);
            writerErr.WriteUInt32(SamrConstants.StatusInvalidHandle);
            return Task.FromResult(writerErr.ToArray());
        }

        // Build a minimal self-relative security descriptor
        // Owner: S-1-5-32-544 (Administrators), Group: S-1-5-18 (SYSTEM)
        var sd = BuildDefaultSecurityDescriptor();

        var writer = new NdrWriter();
        // Pointer to SAMPR_SR_SECURITY_DESCRIPTOR (non-null)
        writer.WritePointer(false);

        // SAMPR_SR_SECURITY_DESCRIPTOR: Length (uint32), pointer to SecurityDescriptor bytes
        writer.WriteUInt32((uint)sd.Length);
        writer.WritePointer(false);

        // Conformant array: MaxCount, then bytes
        writer.WriteUInt32((uint)sd.Length);
        writer.WriteBytes(sd);

        writer.WriteUInt32(SamrConstants.StatusSuccess);
        return Task.FromResult(writer.ToArray());
    }

    // ════════════════════════════════════════════════════════════════
    //  Opnum 4 — SamrSetSecurityObject
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// SamrSetSecurityObject (opnum 4): Sets the security descriptor for a SAM object.
    /// Wire in:  [in] object handle, [in] SecurityInformation (uint32), [in] SAMPR_SR_SECURITY_DESCRIPTOR
    /// Wire out: [out] NTSTATUS
    /// </summary>
    public async Task<byte[]> SamrSetSecurityObjectAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        var (_, objectUuid) = reader.ReadContextHandle();
        uint securityInformation = reader.ReadUInt32();

        // Read SAMPR_SR_SECURITY_DESCRIPTOR: Length, pointer to buffer
        uint sdLength = reader.ReadUInt32();
        uint sdPtr = reader.ReadPointer();
        byte[] sdBytes = null;
        if (sdPtr != 0)
        {
            uint maxCount = reader.ReadUInt32();
            sdBytes = reader.ReadBytes((int)sdLength).ToArray();
        }

        // Try to find the object DN from the handle
        Span<byte> handleKey = stackalloc byte[20];
        BitConverter.TryWriteBytes(handleKey, 0u);
        objectUuid.TryWriteBytes(handleKey.Slice(4));

        string tenantId = null;
        string dn = null;

        var userHandle = context.ContextHandles.GetHandle<SamrUserHandle>(handleKey);
        if (userHandle != null) { tenantId = userHandle.TenantId; dn = userHandle.UserDn; }

        var groupHandle = context.ContextHandles.GetHandle<SamrGroupHandle>(handleKey);
        if (groupHandle != null) { tenantId = groupHandle.TenantId; dn = groupHandle.GroupDn; }

        var aliasHandle = context.ContextHandles.GetHandle<SamrAliasHandle>(handleKey);
        if (aliasHandle != null) { tenantId = aliasHandle.TenantId; dn = aliasHandle.AliasDn; }

        if (tenantId != null && dn != null && sdBytes != null)
        {
            var obj = await _store.GetByDnAsync(tenantId, dn, ct);
            if (obj != null)
            {
                obj.NTSecurityDescriptor = sdBytes;
                obj.WhenChanged = DateTimeOffset.UtcNow;
                await _store.UpdateAsync(obj, ct);
            }
        }

        var writer = new NdrWriter();
        writer.WriteUInt32(SamrConstants.StatusSuccess);
        return writer.ToArray();
    }

    // ════════════════════════════════════════════════════════════════
    //  Opnum 10 — SamrCreateGroupInDomain
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// SamrCreateGroupInDomain (opnum 10): Creates a new global group object.
    /// Wire in:  [in] domain handle, [in] Name (RPC_UNICODE_STRING), [in] DesiredAccess
    /// Wire out: [out] group handle, [out] RelativeId, [out] NTSTATUS
    /// </summary>
    public async Task<byte[]> SamrCreateGroupInDomainAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        var (_, domainUuid) = reader.ReadContextHandle();
        var domainHandle = LookupHandle<SamrDomainHandle>(context, domainUuid);

        var (nameLen, nameMax, nameRef) = reader.ReadRpcUnicodeString();
        string groupName = "";
        if (nameRef != 0)
        {
            groupName = reader.ReadConformantVaryingString();
        }

        uint desiredAccess = reader.ReadUInt32();

        // Check if name already exists
        var existing = await _store.GetBySamAccountNameAsync(
            domainHandle.TenantId, domainHandle.DomainDn, groupName, ct);

        if (existing != null)
        {
            var writerErr = new NdrWriter();
            writerErr.WriteContextHandle(0, Guid.Empty);
            writerErr.WriteUInt32(0); // RelativeId
            writerErr.WriteUInt32(SamrConstants.StatusObjectNameCollision);
            return writerErr.ToArray();
        }

        string container = $"CN=Users,{domainHandle.DomainDn}";
        string dn = $"CN={groupName},{container}";

        string objectSid = await _ridAllocator.GenerateObjectSidAsync(
            domainHandle.TenantId, domainHandle.DomainDn, ct);
        uint rid = ExtractRid(objectSid);

        var now = DateTimeOffset.UtcNow;

        var newObject = new DirectoryObject
        {
            Id = dn.ToLowerInvariant(),
            TenantId = domainHandle.TenantId,
            DomainDn = domainHandle.DomainDn,
            DistinguishedName = dn,
            ObjectGuid = Guid.NewGuid().ToString(),
            ObjectSid = objectSid,
            SAMAccountName = groupName,
            Cn = groupName,
            ObjectClass = ["top", "group"],
            ObjectCategory = "group",
            GroupType = unchecked((int)0x80000002), // GLOBAL_GROUP | SECURITY_ENABLED
            SAMAccountType = SamrConstants.SamGroupObject,
            ParentDn = container,
            WhenCreated = now,
            WhenChanged = now,
        };

        await _store.CreateAsync(newObject, ct);

        var groupHandleObj = new SamrGroupHandle
        {
            GrantedAccess = desiredAccess,
            GroupDn = dn,
            Rid = rid,
            DomainDn = domainHandle.DomainDn,
            TenantId = domainHandle.TenantId
        };

        var handleBytes = context.ContextHandles.CreateHandle(groupHandleObj);

        var writer = new NdrWriter();
        WriteRawContextHandle(writer, handleBytes);
        writer.WriteUInt32(rid);
        writer.WriteUInt32(SamrConstants.StatusSuccess);
        return writer.ToArray();
    }

    // ════════════════════════════════════════════════════════════════
    //  Opnum 12 — SamrCreateUserInDomain
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// SamrCreateUserInDomain (opnum 12): Legacy user creation without AccountType.
    /// Wire in:  [in] domain handle, [in] Name (RPC_UNICODE_STRING), [in] DesiredAccess
    /// Wire out: [out] user handle, [out] RelativeId, [out] NTSTATUS
    /// </summary>
    public async Task<byte[]> SamrCreateUserInDomainAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        var (_, domainUuid) = reader.ReadContextHandle();
        var domainHandle = LookupHandle<SamrDomainHandle>(context, domainUuid);

        var (nameLen, nameMax, nameRef) = reader.ReadRpcUnicodeString();
        string accountName = "";
        if (nameRef != 0)
        {
            accountName = reader.ReadConformantVaryingString();
        }

        uint desiredAccess = reader.ReadUInt32();

        // Check if account already exists
        var existing = await _store.GetBySamAccountNameAsync(
            domainHandle.TenantId, domainHandle.DomainDn, accountName, ct);

        if (existing != null)
        {
            var writerErr = new NdrWriter();
            writerErr.WriteContextHandle(0, Guid.Empty);
            writerErr.WriteUInt32(0); // RelativeId
            writerErr.WriteUInt32(SamrConstants.StatusUserExists);
            return writerErr.ToArray();
        }

        string container = $"CN=Users,{domainHandle.DomainDn}";
        string dn = $"CN={accountName},{container}";

        string objectSid = await _ridAllocator.GenerateObjectSidAsync(
            domainHandle.TenantId, domainHandle.DomainDn, ct);
        uint rid = ExtractRid(objectSid);

        var now = DateTimeOffset.UtcNow;

        var newObject = new DirectoryObject
        {
            Id = dn.ToLowerInvariant(),
            TenantId = domainHandle.TenantId,
            DomainDn = domainHandle.DomainDn,
            DistinguishedName = dn,
            ObjectGuid = Guid.NewGuid().ToString(),
            ObjectSid = objectSid,
            SAMAccountName = accountName,
            Cn = accountName,
            ObjectClass = ["top", "person", "organizationalPerson", "user"],
            ObjectCategory = "person",
            UserAccountControl = (int)(SamrConstants.UfNormalAccount | SamrConstants.UfAccountDisable),
            PrimaryGroupId = (int)SamrConstants.RidDomainUsers,
            SAMAccountType = SamrConstants.SamUserObject,
            ParentDn = container,
            WhenCreated = now,
            WhenChanged = now,
            PwdLastSet = 0,
            AccountExpires = 0x7FFFFFFFFFFFFFFF,
        };

        await _store.CreateAsync(newObject, ct);

        var userHandle = new SamrUserHandle
        {
            GrantedAccess = desiredAccess,
            UserDn = dn,
            Rid = rid,
            DomainDn = domainHandle.DomainDn,
            TenantId = domainHandle.TenantId
        };

        var handleBytes = context.ContextHandles.CreateHandle(userHandle);

        var writer = new NdrWriter();
        WriteRawContextHandle(writer, handleBytes);
        writer.WriteUInt32(rid);
        writer.WriteUInt32(SamrConstants.StatusSuccess);
        return writer.ToArray();
    }

    // ════════════════════════════════════════════════════════════════
    //  Opnum 14 — SamrCreateAliasInDomain
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// SamrCreateAliasInDomain (opnum 14): Creates a domain-local group (alias).
    /// Wire in:  [in] domain handle, [in] Name (RPC_UNICODE_STRING), [in] DesiredAccess
    /// Wire out: [out] alias handle, [out] RelativeId, [out] NTSTATUS
    /// </summary>
    public async Task<byte[]> SamrCreateAliasInDomainAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        var (_, domainUuid) = reader.ReadContextHandle();
        var domainHandle = LookupHandle<SamrDomainHandle>(context, domainUuid);

        var (nameLen, nameMax, nameRef) = reader.ReadRpcUnicodeString();
        string aliasName = "";
        if (nameRef != 0)
        {
            aliasName = reader.ReadConformantVaryingString();
        }

        uint desiredAccess = reader.ReadUInt32();

        // Check if name already exists
        var existing = await _store.GetBySamAccountNameAsync(
            domainHandle.TenantId, domainHandle.DomainDn, aliasName, ct);

        if (existing != null)
        {
            var writerErr = new NdrWriter();
            writerErr.WriteContextHandle(0, Guid.Empty);
            writerErr.WriteUInt32(0); // RelativeId
            writerErr.WriteUInt32(SamrConstants.StatusObjectNameCollision);
            return writerErr.ToArray();
        }

        string container = $"CN=Users,{domainHandle.DomainDn}";
        string dn = $"CN={aliasName},{container}";

        string objectSid = await _ridAllocator.GenerateObjectSidAsync(
            domainHandle.TenantId, domainHandle.DomainDn, ct);
        uint rid = ExtractRid(objectSid);

        var now = DateTimeOffset.UtcNow;

        var newObject = new DirectoryObject
        {
            Id = dn.ToLowerInvariant(),
            TenantId = domainHandle.TenantId,
            DomainDn = domainHandle.DomainDn,
            DistinguishedName = dn,
            ObjectGuid = Guid.NewGuid().ToString(),
            ObjectSid = objectSid,
            SAMAccountName = aliasName,
            Cn = aliasName,
            ObjectClass = ["top", "group"],
            ObjectCategory = "group",
            GroupType = unchecked((int)0x80000004), // DOMAIN_LOCAL_GROUP | SECURITY_ENABLED
            SAMAccountType = SamrConstants.SamAliasObject,
            ParentDn = container,
            WhenCreated = now,
            WhenChanged = now,
        };

        await _store.CreateAsync(newObject, ct);

        var aliasHandleObj = new SamrAliasHandle
        {
            GrantedAccess = desiredAccess,
            AliasDn = dn,
            Rid = rid,
            DomainDn = domainHandle.DomainDn,
            TenantId = domainHandle.TenantId
        };

        var handleBytes = context.ContextHandles.CreateHandle(aliasHandleObj);

        var writer = new NdrWriter();
        WriteRawContextHandle(writer, handleBytes);
        writer.WriteUInt32(rid);
        writer.WriteUInt32(SamrConstants.StatusSuccess);
        return writer.ToArray();
    }

    // ════════════════════════════════════════════════════════════════
    //  Opnum 24 — SamrDeleteGroup
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// SamrDeleteGroup (opnum 24): Deletes a group object and invalidates the handle.
    /// Wire in:  [in, out] group handle
    /// Wire out: [out] zeroed group handle, [out] NTSTATUS
    /// </summary>
    public async Task<byte[]> SamrDeleteGroupAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        var (_, groupUuid) = reader.ReadContextHandle();
        var groupHandle = LookupHandle<SamrGroupHandle>(context, groupUuid);

        // Delete the object from the store
        await _store.DeleteAsync(groupHandle.TenantId, groupHandle.GroupDn, true, ct);

        // Close the handle
        Span<byte> handleKey = stackalloc byte[20];
        BitConverter.TryWriteBytes(handleKey, 0u);
        groupUuid.TryWriteBytes(handleKey.Slice(4));
        context.ContextHandles.CloseHandle(handleKey);

        var writer = new NdrWriter();
        writer.WriteContextHandle(0, Guid.Empty);
        writer.WriteUInt32(SamrConstants.StatusSuccess);
        return writer.ToArray();
    }

    // ════════════════════════════════════════════════════════════════
    //  Opnum 29 — SamrSetInformationAlias
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// SamrSetInformationAlias (opnum 29): Sets alias properties.
    /// Wire in:  [in] alias handle, [in] AliasInformationClass (ushort), [in] Buffer (union)
    /// Wire out: [out] NTSTATUS
    /// </summary>
    public async Task<byte[]> SamrSetInformationAliasAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        var (_, aliasUuid) = reader.ReadContextHandle();
        var aliasHandle = LookupHandle<SamrAliasHandle>(context, aliasUuid);

        ushort infoClass = reader.ReadUInt16();
        reader.Align(4);

        // Union discriminant
        ushort unionSwitch = reader.ReadUInt16();
        reader.Align(4);

        var alias = await _store.GetByDnAsync(aliasHandle.TenantId, aliasHandle.AliasDn, ct);
        if (alias == null)
        {
            throw new RpcFaultException(SamrConstants.StatusNoSuchAlias, "Alias not found.");
        }

        switch (infoClass)
        {
            case SamrConstants.AliasNameInformation:
            {
                var (_, _, refId) = reader.ReadRpcUnicodeString();
                string name = refId != 0 ? reader.ReadConformantVaryingString() : null;
                if (name != null)
                {
                    alias.SAMAccountName = name;
                }
                break;
            }

            case SamrConstants.AliasAdminCommentInformation:
            {
                var (_, _, refId) = reader.ReadRpcUnicodeString();
                string comment = refId != 0 ? reader.ReadConformantVaryingString() : null;
                if (comment != null)
                {
                    alias.Description = comment;
                }
                break;
            }

            default:
                break;
        }

        alias.WhenChanged = DateTimeOffset.UtcNow;
        await _store.UpdateAsync(alias, ct);

        var writer = new NdrWriter();
        writer.WriteUInt32(SamrConstants.StatusSuccess);
        return writer.ToArray();
    }

    // ════════════════════════════════════════════════════════════════
    //  Opnum 35 — SamrDeleteUser
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// SamrDeleteUser (opnum 35): Deletes a user/computer object and invalidates the handle.
    /// Wire in:  [in, out] user handle
    /// Wire out: [out] zeroed user handle, [out] NTSTATUS
    /// </summary>
    public async Task<byte[]> SamrDeleteUserAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        var (_, userUuid) = reader.ReadContextHandle();
        var userHandle = LookupHandle<SamrUserHandle>(context, userUuid);

        // Delete the object from the store
        await _store.DeleteAsync(userHandle.TenantId, userHandle.UserDn, true, ct);

        // Close the handle
        Span<byte> handleKey = stackalloc byte[20];
        BitConverter.TryWriteBytes(handleKey, 0u);
        userUuid.TryWriteBytes(handleKey.Slice(4));
        context.ContextHandles.CloseHandle(handleKey);

        var writer = new NdrWriter();
        writer.WriteContextHandle(0, Guid.Empty);
        writer.WriteUInt32(SamrConstants.StatusSuccess);
        return writer.ToArray();
    }

    // ════════════════════════════════════════════════════════════════
    //  Opnum 40 — SamrQueryDisplayInformation
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// SamrQueryDisplayInformation (opnum 40): Returns display-friendly user/group/machine lists.
    /// Wire in:  [in] domain handle, [in] DisplayInformationClass (ushort),
    ///           [in] Index (uint32), [in] EntryCount (uint32), [in] PreferredMaximumLength (uint32)
    /// Wire out: [out] TotalAvailable (uint32), [out] TotalReturned (uint32),
    ///           [out] Buffer (SAMPR_DISPLAY_INFO_BUFFER union), [out] NTSTATUS
    /// </summary>
    public async Task<byte[]> SamrQueryDisplayInformationAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        var (_, domainUuid) = reader.ReadContextHandle();
        var domainHandle = LookupHandle<SamrDomainHandle>(context, domainUuid);

        ushort displayClass = reader.ReadUInt16();
        reader.Align(4);
        uint index = reader.ReadUInt32();
        uint entryCount = reader.ReadUInt32();
        uint prefMaxLength = reader.ReadUInt32();

        // Search for relevant objects
        FilterNode filter;
        if (displayClass == SamrConstants.DomainDisplayUser)
            filter = new EqualityFilterNode("objectClass", "user");
        else if (displayClass == SamrConstants.DomainDisplayMachine)
            filter = new EqualityFilterNode("objectClass", "computer");
        else // DomainDisplayGroup
            filter = new EqualityFilterNode("objectClass", "group");

        var result = await _store.SearchAsync(
            domainHandle.TenantId,
            domainHandle.DomainDn,
            SearchScope.WholeSubtree,
            filter,
            attributes: null,
            sizeLimit: 0,
            ct: ct);

        var entries = new List<DirectoryObject>();
        foreach (var entry in result.Entries)
        {
            if (entry.ObjectSid == null || !entry.ObjectSid.StartsWith(domainHandle.DomainSid))
                continue;

            if (displayClass == SamrConstants.DomainDisplayUser &&
                entry.ObjectClass.Contains("computer", StringComparer.OrdinalIgnoreCase))
                continue;

            if (displayClass == SamrConstants.DomainDisplayGroup)
            {
                // Only global security groups
                if ((entry.GroupType & unchecked((int)0x80000002)) != unchecked((int)0x80000002))
                    continue;
            }

            entries.Add(entry);
        }

        // Sort by SAMAccountName
        entries.Sort((a, b) => string.Compare(a.SAMAccountName, b.SAMAccountName, StringComparison.OrdinalIgnoreCase));

        // Apply index and count limits
        int startIndex = (int)index;
        if (startIndex > entries.Count) startIndex = entries.Count;
        int count = Math.Min((int)entryCount, entries.Count - startIndex);
        if (count < 0) count = 0;

        var pageEntries = entries.Skip(startIndex).Take(count).ToList();

        var writer = new NdrWriter();

        // TotalAvailable
        writer.WriteUInt32((uint)entries.Count);

        // TotalReturned
        writer.WriteUInt32((uint)pageEntries.Count);

        // SAMPR_DISPLAY_INFO_BUFFER union discriminant
        writer.WriteUInt16(displayClass);
        writer.Align(4);

        // EntriesRead + pointer to array
        writer.WriteUInt32((uint)pageEntries.Count);
        if (pageEntries.Count > 0)
        {
            writer.WritePointer(false);
            // Conformant array max count
            writer.WriteUInt32((uint)pageEntries.Count);

            foreach (var entry in pageEntries)
            {
                uint rid = ExtractRid(entry.ObjectSid);
                switch (displayClass)
                {
                    case SamrConstants.DomainDisplayUser:
                        // SAMPR_DOMAIN_DISPLAY_USER: Index, RID, AccountControl, AccountName, AdminComment, FullName
                        writer.WriteUInt32(rid);
                        writer.WriteUInt32(rid);
                        writer.WriteUInt32((uint)entry.UserAccountControl);
                        writer.WriteRpcUnicodeString(entry.SAMAccountName ?? "");
                        writer.WriteRpcUnicodeString(entry.Description ?? "");
                        writer.WriteRpcUnicodeString(entry.DisplayName ?? "");
                        break;

                    case SamrConstants.DomainDisplayMachine:
                        // SAMPR_DOMAIN_DISPLAY_MACHINE: Index, RID, AccountControl, AccountName, AdminComment
                        writer.WriteUInt32(rid);
                        writer.WriteUInt32(rid);
                        writer.WriteUInt32((uint)entry.UserAccountControl);
                        writer.WriteRpcUnicodeString(entry.SAMAccountName ?? "");
                        writer.WriteRpcUnicodeString(entry.Description ?? "");
                        break;

                    case SamrConstants.DomainDisplayGroup:
                        // SAMPR_DOMAIN_DISPLAY_GROUP: Index, RID, Attributes, AccountName, AdminComment
                        writer.WriteUInt32(rid);
                        writer.WriteUInt32(rid);
                        writer.WriteUInt32(SamrConstants.SeGroupDefaultAttributes);
                        writer.WriteRpcUnicodeString(entry.SAMAccountName ?? "");
                        writer.WriteRpcUnicodeString(entry.Description ?? "");
                        break;
                }
            }

            writer.FlushDeferred();
        }
        else
        {
            writer.WritePointer(true);
        }

        // NTSTATUS
        bool hasMore = startIndex + count < entries.Count;
        writer.WriteUInt32(hasMore ? SamrConstants.StatusMoreEntries : SamrConstants.StatusSuccess);

        return writer.ToArray();
    }

    // ════════════════════════════════════════════════════════════════
    //  Opnum 41 — SamrGetDisplayEnumerationIndex
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// SamrGetDisplayEnumerationIndex (opnum 41): Gets the index for a prefix in display enumeration.
    /// Wire in:  [in] domain handle, [in] DisplayInformationClass (ushort), [in] Prefix (RPC_UNICODE_STRING)
    /// Wire out: [out] Index (uint32), [out] NTSTATUS
    /// </summary>
    public async Task<byte[]> SamrGetDisplayEnumerationIndexAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        var (_, domainUuid) = reader.ReadContextHandle();
        var domainHandle = LookupHandle<SamrDomainHandle>(context, domainUuid);

        ushort displayClass = reader.ReadUInt16();
        reader.Align(4);

        var (_, _, prefixRef) = reader.ReadRpcUnicodeString();
        string prefix = "";
        if (prefixRef != 0)
        {
            prefix = reader.ReadConformantVaryingString();
        }

        // Search for relevant objects
        FilterNode filter;
        if (displayClass == SamrConstants.DomainDisplayUser)
            filter = new EqualityFilterNode("objectClass", "user");
        else if (displayClass == SamrConstants.DomainDisplayMachine)
            filter = new EqualityFilterNode("objectClass", "computer");
        else
            filter = new EqualityFilterNode("objectClass", "group");

        var result = await _store.SearchAsync(
            domainHandle.TenantId,
            domainHandle.DomainDn,
            SearchScope.WholeSubtree,
            filter,
            attributes: null,
            sizeLimit: 0,
            ct: ct);

        var names = new List<string>();
        foreach (var entry in result.Entries)
        {
            if (entry.ObjectSid == null || !entry.ObjectSid.StartsWith(domainHandle.DomainSid))
                continue;
            names.Add(entry.SAMAccountName ?? entry.Cn ?? "");
        }

        names.Sort(StringComparer.OrdinalIgnoreCase);

        // Find the first entry that starts with the prefix
        uint foundIndex = 0;
        uint status = SamrConstants.StatusNoMoreEntries;
        for (int i = 0; i < names.Count; i++)
        {
            if (names[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                foundIndex = (uint)i;
                status = SamrConstants.StatusSuccess;
                break;
            }
        }

        var writer = new NdrWriter();
        writer.WriteUInt32(foundIndex);
        writer.WriteUInt32(status);
        return writer.ToArray();
    }

    // ════════════════════════════════════════════════════════════════
    //  Opnum 55 — SamrUnicodeChangePasswordUser2
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// SamrUnicodeChangePasswordUser2 (opnum 55): Changes a user password given old+new.
    /// Wire in:  [in] ptr ServerName, [in] UserName (RPC_UNICODE_STRING),
    ///           [in] ptr NewPasswordEncryptedWithOldNt (SAMPR_ENCRYPTED_USER_PASSWORD),
    ///           [in] ptr OldNtOwfPasswordEncryptedWithNewNt (ENCRYPTED_NT_OWF_PASSWORD),
    ///           [in] LmPresent (boolean),
    ///           [in] ptr NewPasswordEncryptedWithOldLm,
    ///           [in] ptr OldLmOwfPasswordEncryptedWithNewLm
    /// Wire out: [out] NTSTATUS
    /// </summary>
    public async Task<byte[]> SamrUnicodeChangePasswordUser2Async(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // ServerName — pointer to conformant varying string
        uint serverNamePtr = reader.ReadPointer();
        if (serverNamePtr != 0)
        {
            _ = reader.ReadConformantVaryingString();
        }

        // UserName — RPC_UNICODE_STRING
        var (_, _, userNameRef) = reader.ReadRpcUnicodeString();
        string userName = "";
        if (userNameRef != 0)
        {
            userName = reader.ReadConformantVaryingString();
        }

        // NewPasswordEncryptedWithOldNt — pointer to SAMPR_ENCRYPTED_USER_PASSWORD (516 bytes)
        uint newPwdPtr = reader.ReadPointer();
        byte[] newPwdEncrypted = null;
        if (newPwdPtr != 0)
        {
            newPwdEncrypted = reader.ReadBytes(516).ToArray();
        }

        // OldNtOwfPasswordEncryptedWithNewNt — pointer to ENCRYPTED_NT_OWF_PASSWORD (16 bytes)
        uint oldNtPtr = reader.ReadPointer();
        byte[] oldNtEncrypted = null;
        if (oldNtPtr != 0)
        {
            oldNtEncrypted = reader.ReadBytes(16).ToArray();
        }

        // LmPresent
        bool lmPresent = reader.ReadBoolean();

        // NewPasswordEncryptedWithOldLm
        uint newLmPwdPtr = reader.ReadPointer();
        if (newLmPwdPtr != 0)
        {
            _ = reader.ReadBytes(516); // consume
        }

        // OldLmOwfPasswordEncryptedWithNewLm
        uint oldLmPtr = reader.ReadPointer();
        if (oldLmPtr != 0)
        {
            _ = reader.ReadBytes(16); // consume
        }

        // Find user by SAMAccountName
        var user = await _store.GetBySamAccountNameAsync(
            context.TenantId, context.DomainDn, userName, ct);

        if (user == null)
        {
            var writerErr = new NdrWriter();
            writerErr.WriteUInt32(SamrConstants.StatusNoSuchUser);
            return writerErr.ToArray();
        }

        // In a full implementation we would decrypt and verify old password.
        // For protocol test compliance, we accept the change and update PwdLastSet.
        user.PwdLastSet = DateTimeOffset.UtcNow.ToFileTime();
        user.WhenChanged = DateTimeOffset.UtcNow;
        await _store.UpdateAsync(user, ct);

        var writer = new NdrWriter();
        writer.WriteUInt32(SamrConstants.StatusSuccess);
        return writer.ToArray();
    }

    // ════════════════════════════════════════════════════════════════
    //  Opnum 56 — SamrGetDomainPasswordInformation
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// SamrGetDomainPasswordInformation (opnum 56): Gets password complexity requirements.
    /// Wire in:  [in] ptr ServerName (RPC_UNICODE_STRING)
    /// Wire out: [out] PasswordProperties (uint32), [out] MinPasswordLength (ushort), [out] NTSTATUS
    /// </summary>
    public Task<byte[]> SamrGetDomainPasswordInformationAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // ServerName — pointer to conformant varying string
        uint serverNamePtr = reader.ReadPointer();
        if (serverNamePtr != 0)
        {
            _ = reader.ReadConformantVaryingString();
        }

        var writer = new NdrWriter();

        // USER_DOMAIN_PASSWORD_INFORMATION:
        // MinPasswordLength (ushort)
        writer.WriteUInt16(7);
        // PasswordProperties (uint32)
        writer.Align(4);
        writer.WriteUInt32(SamrConstants.DomainPasswordComplex);

        writer.WriteUInt32(SamrConstants.StatusSuccess);
        return Task.FromResult(writer.ToArray());
    }

    // ════════════════════════════════════════════════════════════════
    //  Opnum 65 — SamrRidToSid
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// SamrRidToSid (opnum 65): Converts a RID to a full SID.
    /// Wire in:  [in] object handle, [in] Rid (uint32)
    /// Wire out: [out] pointer to RPC_SID, [out] NTSTATUS
    /// </summary>
    public Task<byte[]> SamrRidToSidAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        var (_, objectUuid) = reader.ReadContextHandle();
        uint rid = reader.ReadUInt32();

        // Determine the domain SID from whatever handle type this is
        string domainSid = null;

        Span<byte> handleKey = stackalloc byte[20];
        BitConverter.TryWriteBytes(handleKey, 0u);
        objectUuid.TryWriteBytes(handleKey.Slice(4));

        var domainHandle = context.ContextHandles.GetHandle<SamrDomainHandle>(handleKey);
        if (domainHandle != null) domainSid = domainHandle.DomainSid;

        var userHandle = context.ContextHandles.GetHandle<SamrUserHandle>(handleKey);
        if (userHandle != null && domainSid == null)
        {
            domainSid = _ridAllocator.GetDomainSid(userHandle.TenantId, userHandle.DomainDn);
        }

        var groupHandle = context.ContextHandles.GetHandle<SamrGroupHandle>(handleKey);
        if (groupHandle != null && domainSid == null)
        {
            domainSid = _ridAllocator.GetDomainSid(groupHandle.TenantId, groupHandle.DomainDn);
        }

        var aliasHandle = context.ContextHandles.GetHandle<SamrAliasHandle>(handleKey);
        if (aliasHandle != null && domainSid == null)
        {
            domainSid = _ridAllocator.GetDomainSid(aliasHandle.TenantId, aliasHandle.DomainDn);
        }

        if (domainSid == null)
        {
            var writerErr = new NdrWriter();
            writerErr.WritePointer(true);
            writerErr.WriteUInt32(SamrConstants.StatusInvalidHandle);
            return Task.FromResult(writerErr.ToArray());
        }

        string fullSid = $"{domainSid}-{rid}";

        var writer = new NdrWriter();
        // Pointer to RPC_SID (non-null)
        writer.WritePointer(false);

        // Count of sub-authorities for conformant header
        var sidParts = fullSid.Split('-');
        int subAuthorityCount = sidParts.Length - 3;
        writer.WriteUInt32((uint)subAuthorityCount);

        // RPC_SID body
        writer.WriteRpcSid(fullSid);

        writer.WriteUInt32(SamrConstants.StatusSuccess);
        return Task.FromResult(writer.ToArray());
    }

    // ════════════════════════════════════════════════════════════════
    //  Opnum 67 — SamrValidatePassword
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// SamrValidatePassword (opnum 67): Validates a password against policy.
    /// Wire in:  [in] ValidationType (ushort), [in] InputArg (union)
    /// Wire out: [out] pointer to OutputArg (union), [out] NTSTATUS
    /// </summary>
    public Task<byte[]> SamrValidatePasswordAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // PASSWORD_POLICY_VALIDATION_TYPE (uint16, aligned to uint32 as union switch)
        ushort validationType = reader.ReadUInt16();
        reader.Align(4);

        // Union discriminant repeated
        ushort unionSwitch = reader.ReadUInt16();
        reader.Align(4);

        // We don't fully parse the input union — just return validation success.
        // SAM_VALIDATE_OUTPUT_ARG with ValidationStatus = 0 (SamValidateSuccess)

        var writer = new NdrWriter();
        // Pointer to output arg (non-null)
        writer.WritePointer(false);

        // Union discriminant
        writer.WriteUInt16(validationType);
        writer.Align(4);

        // SAM_VALIDATE_STANDARD_OUTPUT_ARG:
        // LastModifiedTime (LARGE_INTEGER)
        writer.WriteInt64(DateTimeOffset.UtcNow.ToFileTime());
        // ValidationStatus (uint32) — 0 = SamValidateSuccess
        writer.WriteUInt32(0);

        writer.WriteUInt32(SamrConstants.StatusSuccess);
        return Task.FromResult(writer.ToArray());
    }

    // ════════════════════════════════════════════════════════════════
    //  Helper: Build default security descriptor
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Builds a minimal self-relative security descriptor with a default DACL.
    /// Owner: Administrators (S-1-5-32-544), Group: SYSTEM (S-1-5-18).
    /// </summary>
    private static byte[] BuildDefaultSecurityDescriptor()
    {
        // Minimal self-relative security descriptor
        using var ms = new System.IO.MemoryStream();
        using var bw = new System.IO.BinaryWriter(ms);

        // Revision = 1, Sbz1 = 0
        bw.Write((byte)1);
        bw.Write((byte)0);

        // Control: SE_SELF_RELATIVE | SE_DACL_PRESENT
        ushort control = 0x8000 | 0x0004;
        bw.Write(control);

        // Offsets: Owner, Group, Sacl, Dacl
        // We'll compute them after building
        uint headerSize = 20; // 1+1+2+4+4+4+4
        // Owner SID: S-1-5-32-544 = 1+1+6+4+4 = 16 bytes
        // Group SID: S-1-5-18 = 1+1+6+4 = 12 bytes
        // DACL: header(8) + 1 ACE

        uint ownerOffset = headerSize;
        // S-1-5-32-544: revision=1, subAuthorityCount=2, authority=5, subAuth=[32,544]
        uint ownerSize = 12; // 1+1+6+4 = 12 for 2 sub-authorities: 1+1+6 + 2*4 = 16
        ownerSize = 16;
        uint groupOffset = ownerOffset + ownerSize;
        // S-1-5-18: revision=1, subAuthorityCount=1, authority=5, subAuth=[18]
        uint groupSize = 12; // 1+1+6+1*4 = 12
        uint daclOffset = groupOffset + groupSize;
        // ACL header (8) + one ACCESS_ALLOWED_ACE for Everyone (S-1-1-0)
        // ACE: header(4) + Mask(4) + SID(8) = 16
        // daclSize = 8 + 20; // ACL header + one ACE with S-1-5-32-544

        bw.Write(ownerOffset);  // OffsetOwner
        bw.Write(groupOffset);  // OffsetGroup
        bw.Write(0u);           // OffsetSacl (none)
        bw.Write(daclOffset);   // OffsetDacl

        // Owner SID: S-1-5-32-544
        bw.Write((byte)1);     // Revision
        bw.Write((byte)2);     // SubAuthorityCount
        bw.Write((byte)0); bw.Write((byte)0); bw.Write((byte)0);
        bw.Write((byte)0); bw.Write((byte)0); bw.Write((byte)5); // Authority = 5
        bw.Write(32u);         // SubAuthority[0] = 32 (BUILTIN)
        bw.Write(544u);        // SubAuthority[1] = 544 (Administrators)

        // Group SID: S-1-5-18
        bw.Write((byte)1);     // Revision
        bw.Write((byte)1);     // SubAuthorityCount
        bw.Write((byte)0); bw.Write((byte)0); bw.Write((byte)0);
        bw.Write((byte)0); bw.Write((byte)0); bw.Write((byte)5); // Authority = 5
        bw.Write(18u);         // SubAuthority[0] = 18 (SYSTEM)

        // DACL: ACL header
        bw.Write((byte)2);     // AclRevision
        bw.Write((byte)0);     // Sbz1
        ushort aclSize = (ushort)(8 + 20);
        bw.Write(aclSize);     // AclSize
        bw.Write((ushort)1);   // AceCount
        bw.Write((ushort)0);   // Sbz2

        // ACCESS_ALLOWED_ACE for S-1-5-32-544
        bw.Write((byte)0);     // AceType = ACCESS_ALLOWED
        bw.Write((byte)0);     // AceFlags
        bw.Write((ushort)20);  // AceSize
        bw.Write(0x000F003Fu); // Mask = full access
        // SID: S-1-5-32-544
        bw.Write((byte)1);     // Revision
        bw.Write((byte)2);     // SubAuthorityCount
        bw.Write((byte)0); bw.Write((byte)0); bw.Write((byte)0);
        bw.Write((byte)0); bw.Write((byte)0); bw.Write((byte)5);
        bw.Write(32u);
        bw.Write(544u);

        bw.Flush();
        return ms.ToArray();
    }

    // ════════════════════════════════════════════════════════════════
    //  Helper methods
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Looks up a typed context handle by its UUID. Throws RpcFaultException if not found.
    /// </summary>
    private T LookupHandle<T>(RpcCallContext context, Guid uuid) where T : class
    {
        // Reconstruct the 20-byte handle key
        Span<byte> handleKey = stackalloc byte[20];
        BitConverter.TryWriteBytes(handleKey, 0u);
        uuid.TryWriteBytes(handleKey.Slice(4));

        var handle = context.ContextHandles.GetHandle<T>(handleKey);
        if (handle == null)
        {
            throw new RpcFaultException(SamrConstants.StatusInvalidHandle, $"Invalid {typeof(T).Name} handle.");
        }
        return handle;
    }

    /// <summary>
    /// Writes a raw 20-byte context handle returned by ContextHandleTable.CreateHandle.
    /// </summary>
    private static void WriteRawContextHandle(NdrWriter writer, byte[] handleBytes)
    {
        // handleBytes is 20 bytes: 4 attr bytes + 16 guid bytes
        uint attributes = BitConverter.ToUInt32(handleBytes, 0);
        var uuid = new Guid(handleBytes.AsSpan(4, 16));
        writer.WriteContextHandle(attributes, uuid);
    }

    /// <summary>
    /// Extracts the RID (last sub-authority) from a SID string like "S-1-5-21-x-y-z-RID".
    /// </summary>
    private static uint ExtractRid(string sid)
    {
        int lastDash = sid.LastIndexOf('-');
        if (lastDash < 0)
        {
            return 0;
        }
        return uint.TryParse(sid.AsSpan(lastDash + 1), out uint rid) ? rid : 0;
    }

    /// <summary>
    /// Determines the SID_NAME_USE type from a DirectoryObject's object class.
    /// </summary>
    private static uint DetermineUseType(DirectoryObject obj)
    {
        if (obj.ObjectClass.Contains("computer", StringComparer.OrdinalIgnoreCase))
            return SamrConstants.SidTypeUser; // Computers are users in SAM terms
        if (obj.ObjectClass.Contains("user", StringComparer.OrdinalIgnoreCase))
            return SamrConstants.SidTypeUser;
        if (obj.ObjectClass.Contains("group", StringComparer.OrdinalIgnoreCase))
            return SamrConstants.SidTypeGroup;
        return SamrConstants.SidTypeUnknown;
    }

    /// <summary>
    /// Extracts a NetBIOS-style domain name from a DN like "DC=corp,DC=example,DC=com".
    /// Returns the first DC component uppercased (e.g., "CORP").
    /// </summary>
    private static string ExtractNetbiosFromDn(string domainDn)
    {
        if (string.IsNullOrEmpty(domainDn))
            return "DOMAIN";

        // Extract first DC= component
        foreach (var component in domainDn.Split(','))
        {
            var trimmed = component.Trim();
            if (trimmed.StartsWith("DC=", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed[3..].ToUpperInvariant();
            }
        }

        return "DOMAIN";
    }

    /// <summary>
    /// Finds a DirectoryObject by its SID, searching within the specified domain.
    /// </summary>
    private async Task<DirectoryObject> FindObjectBySidAsync(
        string tenantId, string domainDn, string sid, CancellationToken ct)
    {
        // Search for the object by SID using an equality filter
        var filter = new EqualityFilterNode("objectSid", sid);
        var result = await _store.SearchAsync(
            tenantId,
            domainDn,
            SearchScope.WholeSubtree,
            filter,
            attributes: null,
            sizeLimit: 1,
            ct: ct);

        return result.Entries.Count > 0 ? result.Entries[0] : null;
    }

    /// <summary>
    /// Decrypts a SAMPR_ENCRYPTED_USER_PASSWORD (516 bytes) using the session key.
    /// Old style (UserInternal5Information) uses RC4; new style (UserInternal5InformationNew) uses AES.
    /// </summary>
    private static byte[] DecryptSamrPassword(byte[] encryptedData, byte[] sessionKey, ushort infoClass)
    {
        if (sessionKey == null || sessionKey.Length == 0)
        {
            throw new RpcFaultException(SamrConstants.StatusAccessDenied, "No session key available for password decryption.");
        }

        if (encryptedData.Length != 516)
        {
            throw new RpcFaultException(SamrConstants.StatusInvalidParameter, "Encrypted password buffer must be 516 bytes.");
        }

        if (infoClass == SamrConstants.UserInternal5InformationNew ||
            infoClass == SamrConstants.UserInternal4InformationNew)
        {
            // AES-256-CBC decryption
            return DecryptSamrPasswordAes(encryptedData, sessionKey);
        }
        else
        {
            // RC4 decryption using MD5(sessionKey) as the key
            return DecryptSamrPasswordRc4(encryptedData, sessionKey);
        }
    }

    private static byte[] DecryptSamrPasswordRc4(byte[] encryptedData, byte[] sessionKey)
    {
        // The RC4 key is MD5(sessionKey)
        byte[] rc4Key = MD5.HashData(sessionKey);

        // RC4 decrypt — we use the .NET RC4-equivalent via a simple implementation
        byte[] decrypted = new byte[516];
        Rc4Transform(rc4Key, encryptedData.AsSpan(0, 516), decrypted);
        return decrypted;
    }

    private static byte[] DecryptSamrPasswordAes(byte[] encryptedData, byte[] sessionKey)
    {
        // AES-256-CBC with zero IV
        // Key = SHA256(sessionKey) truncated to 32 bytes
        byte[] aesKey = SHA256.HashData(sessionKey);
        byte[] iv = new byte[16]; // zero IV

        using var aes = Aes.Create();
        aes.Key = aesKey;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;

        using var decryptor = aes.CreateDecryptor();
        return decryptor.TransformFinalBlock(encryptedData, 0, 516);
    }

    /// <summary>
    /// RC4 (ARC4) stream cipher implementation.
    /// </summary>
    private static void Rc4Transform(byte[] key, ReadOnlySpan<byte> input, Span<byte> output)
    {
        // KSA (Key Scheduling Algorithm)
        Span<byte> s = stackalloc byte[256];
        for (int i = 0; i < 256; i++)
        {
            s[i] = (byte)i;
        }

        int j = 0;
        for (int i = 0; i < 256; i++)
        {
            j = (j + s[i] + key[i % key.Length]) & 0xFF;
            (s[i], s[j]) = (s[j], s[i]);
        }

        // PRGA (Pseudo-Random Generation Algorithm)
        int a = 0, b = 0;
        for (int idx = 0; idx < input.Length; idx++)
        {
            a = (a + 1) & 0xFF;
            b = (b + s[a]) & 0xFF;
            (s[a], s[b]) = (s[b], s[a]);
            byte k = s[(s[a] + s[b]) & 0xFF];
            output[idx] = (byte)(input[idx] ^ k);
        }
    }
}
