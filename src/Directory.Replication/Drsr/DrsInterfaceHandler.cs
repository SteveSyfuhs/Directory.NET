using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Rpc.Dispatch;
using Directory.Rpc.Ndr;
using Microsoft.Extensions.Logging;

namespace Directory.Replication.Drsr;

/// <summary>
/// RPC interface handler for DRSUAPI (Directory Replication Service).
/// Interface UUID: e3514235-4b06-11d1-ab04-00c04fc2dcd2, version 4.0
///
/// Implements the following operations per [MS-DRSR]:
///   Opnum  0 — IDL_DRSBind
///   Opnum  1 — IDL_DRSUnbind
///   Opnum  2 — IDL_DRSReplicaSync
///   Opnum  3 — IDL_DRSGetNCChanges
///   Opnum  4 — IDL_DRSUpdateRefs
///   Opnum  8 — IDL_DRSVerifyNames
///   Opnum  9 — IDL_DRSGetMemberships
///   Opnum 12 — IDL_DRSCrackNames
///   Opnum 13 — IDL_DRSWriteSPN
///   Opnum 14 — IDL_DRSRemoveDsServer
///   Opnum 15 — IDL_DRSRemoveDsDomain
///   Opnum 19 — IDL_DRSGetReplInfo
///   Opnum 28 — IDL_DRSAddCloneDC
/// </summary>
public class DrsInterfaceHandler : IRpcInterfaceHandler
{
    /// <summary>
    /// DRSUAPI interface UUID per [MS-DRSR] section 2.
    /// </summary>
    public static readonly Guid DrsInterfaceUuid =
        new("e3514235-4b06-11d1-ab04-00c04fc2dcd2");

    private readonly ReplicationEngine _engine;
    private readonly ReplicationTopology _topology;
    private readonly DrsNameResolution _nameResolution;
    private readonly FsmoRoleManager _fsmoManager;
    private readonly IDirectoryStore _store;
    private readonly IRidAllocator _ridAllocator;
    private readonly DcInstanceInfo _dcInfo;
    private readonly SchemaPrefixTable _prefixTable;
    private readonly ILogger<DrsInterfaceHandler> _logger;

    public DrsInterfaceHandler(
        ReplicationEngine engine,
        ReplicationTopology topology,
        DrsNameResolution nameResolution,
        FsmoRoleManager fsmoManager,
        IDirectoryStore store,
        IRidAllocator ridAllocator,
        DcInstanceInfo dcInfo,
        SchemaPrefixTable prefixTable,
        ILogger<DrsInterfaceHandler> logger)
    {
        _engine = engine;
        _topology = topology;
        _nameResolution = nameResolution;
        _fsmoManager = fsmoManager;
        _store = store;
        _ridAllocator = ridAllocator;
        _dcInfo = dcInfo;
        _prefixTable = prefixTable;
        _logger = logger;
    }

    public Guid InterfaceId => DrsInterfaceUuid;
    public ushort MajorVersion => 4;
    public ushort MinorVersion => 0;

    // Opnum constants
    private const ushort OpDrsBind = 0;
    private const ushort OpDrsUnbind = 1;
    private const ushort OpDrsReplicaSync = 2;
    private const ushort OpDrsGetNCChanges = 3;
    private const ushort OpDrsUpdateRefs = 4;
    private const ushort OpDrsVerifyNames = 8;
    private const ushort OpDrsGetMemberships = 9;
    private const ushort OpDrsCrackNames = 12;
    private const ushort OpDrsWriteSPN = 13;
    private const ushort OpDrsRemoveDsServer = 14;
    private const ushort OpDrsRemoveDsDomain = 15;
    private const ushort OpDrsGetReplInfo = 19;
    private const ushort OpDrsAddCloneDC = 28;

    public Task<byte[]> HandleRequestAsync(
        ushort opnum,
        ReadOnlyMemory<byte> stubData,
        RpcCallContext context,
        CancellationToken ct)
    {
        return opnum switch
        {
            OpDrsBind => HandleDrsBindAsync(stubData, context, ct),
            OpDrsUnbind => HandleDrsUnbindAsync(stubData, context, ct),
            OpDrsReplicaSync => HandleDrsReplicaSyncAsync(stubData, context, ct),
            OpDrsGetNCChanges => HandleDrsGetNCChangesAsync(stubData, context, ct),
            OpDrsUpdateRefs => HandleDrsUpdateRefsAsync(stubData, context, ct),
            OpDrsVerifyNames => HandleDrsVerifyNamesAsync(stubData, context, ct),
            OpDrsGetMemberships => HandleDrsGetMembershipsAsync(stubData, context, ct),
            OpDrsCrackNames => HandleDrsCrackNamesAsync(stubData, context, ct),
            OpDrsWriteSPN => HandleDrsWriteSpnAsync(stubData, context, ct),
            OpDrsRemoveDsServer => HandleDrsRemoveDsServerAsync(stubData, context, ct),
            OpDrsRemoveDsDomain => HandleDrsRemoveDsDomainAsync(stubData, context, ct),
            OpDrsGetReplInfo => HandleDrsGetReplInfoAsync(stubData, context, ct),
            OpDrsAddCloneDC => HandleDrsAddCloneDCAsync(stubData, context, ct),
            _ => throw new InvalidOperationException($"DRSUAPI opnum {opnum} is not supported."),
        };
    }

    #region IDL_DRSBind (opnum 0)

    /// <summary>
    /// IDL_DRSBind — establishes a replication context handle.
    /// Reads the client's DRS_EXTENSIONS_INT and returns a context handle
    /// along with the server's extensions.
    /// </summary>
    private Task<byte[]> HandleDrsBindAsync(
        ReadOnlyMemory<byte> stubData,
        RpcCallContext context,
        CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // Read client UUID (puuidClientDsa) — pointer + GUID
        var clientDsaPointer = reader.ReadPointer();
        Guid clientDsaGuid = Guid.Empty;
        if (clientDsaPointer != 0)
        {
            var guidBytes = reader.ReadBytes(16);
            clientDsaGuid = new Guid(guidBytes.Span);
        }

        // Read client extensions pointer
        var extPointer = reader.ReadPointer();
        var clientExtensions = new DRS_EXTENSIONS_INT();
        if (extPointer != 0)
        {
            // cb (size of extensions data)
            var cb = reader.ReadUInt32();
            if (cb >= 4) clientExtensions.DwFlags = (DrsExtFlags)reader.ReadUInt32();
            if (cb >= 20)
            {
                var siteBytes = reader.ReadBytes(16);
                clientExtensions.SiteObjGuid = new Guid(siteBytes.Span);
            }
            if (cb >= 24) clientExtensions.Pid = reader.ReadInt32();
            if (cb >= 28) clientExtensions.DwReplEpoch = reader.ReadUInt32();
            if (cb >= 32) clientExtensions.DwFlagsExt = (DrsExtMoreFlags)reader.ReadUInt32();
            if (cb >= 48)
            {
                var cfgBytes = reader.ReadBytes(16);
                clientExtensions.ConfigObjGuid = new Guid(cfgBytes.Span);
            }
        }

        _logger.LogInformation(
            "DRSBind: client DSA={ClientGuid}, flags={Flags}",
            clientDsaGuid, clientExtensions.DwFlags);

        // Create server extensions
        var serverExtensions = new DRS_EXTENSIONS_INT
        {
            DwFlags = DrsExtFlags.DRS_EXT_BASE
                    | DrsExtFlags.DRS_EXT_REMOVEAPI
                    | DrsExtFlags.DRS_EXT_GETCHGREQ_V8
                    | DrsExtFlags.DRS_EXT_GETCHGREPLY_V6
                    | DrsExtFlags.DRS_EXT_LINKED_VALUE_REPLICATION
                    | DrsExtFlags.DRS_EXT_GET_REPL_INFO
                    | DrsExtFlags.DRS_EXT_STRONG_ENCRYPTION
                    | DrsExtFlags.DRS_EXT_ADDENTRY_V2
                    | DrsExtFlags.DRS_EXT_TRANSITIVE_MEMBERSHIP
                    | DrsExtFlags.DRS_EXT_POST_BETA3
                    | DrsExtFlags.DRS_EXT_RECYCLE_BIN,
            Pid = Environment.ProcessId,
            DwReplEpoch = 1,
        };

        // Create bind context
        var bindContext = new DrsBindContext
        {
            BindHandle = Guid.NewGuid(),
            ClientExtensions = clientExtensions,
            ServerExtensions = serverExtensions,
            TenantId = context.TenantId,
            DomainDn = context.DomainDn,
        };

        // Store the context handle
        var handleBytes = context.ContextHandles.CreateHandle(bindContext);

        // Write response
        var writer = new NdrWriter();

        // ppextServer — pointer to server extensions
        writer.WritePointer(isNull: false);
        // cb field
        uint extSize = 48; // Full DRS_EXTENSIONS_INT size
        writer.WriteUInt32(extSize);
        writer.WriteUInt32((uint)serverExtensions.DwFlags);
        // SiteObjGuid
        Span<byte> siteGuidBytes = stackalloc byte[16];
        serverExtensions.SiteObjGuid.TryWriteBytes(siteGuidBytes);
        writer.WriteBytes(siteGuidBytes);
        writer.WriteInt32(serverExtensions.Pid);
        writer.WriteUInt32(serverExtensions.DwReplEpoch);
        writer.WriteUInt32((uint)serverExtensions.DwFlagsExt);
        // ConfigObjGuid
        Span<byte> cfgGuidBytes = stackalloc byte[16];
        serverExtensions.ConfigObjGuid.TryWriteBytes(cfgGuidBytes);
        writer.WriteBytes(cfgGuidBytes);

        // phDrs — context handle (20 bytes)
        writer.WriteContextHandle(0, bindContext.BindHandle);

        // Return code (ULONG)
        writer.WriteUInt32(0); // ERROR_SUCCESS

        return Task.FromResult(writer.ToArray());
    }

    #endregion

    #region IDL_DRSUnbind (opnum 1)

    /// <summary>
    /// IDL_DRSUnbind — releases the replication context handle.
    /// </summary>
    private Task<byte[]> HandleDrsUnbindAsync(
        ReadOnlyMemory<byte> stubData,
        RpcCallContext context,
        CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // Read the context handle
        var (_, handleGuid) = reader.ReadContextHandle();

        _logger.LogInformation("DRSUnbind: handle={Handle}", handleGuid);

        // Close the handle
        var handleBytes = new byte[20];
        handleGuid.TryWriteBytes(handleBytes.AsSpan(4, 16));
        context.ContextHandles.CloseHandle(handleBytes);

        // Write response — zeroed handle + return code
        var writer = new NdrWriter();
        writer.WriteContextHandle(0, Guid.Empty); // zeroed handle
        writer.WriteUInt32(0); // ERROR_SUCCESS

        return Task.FromResult(writer.ToArray());
    }

    #endregion

    #region IDL_DRSReplicaSync (opnum 2)

    /// <summary>
    /// IDL_DRSReplicaSync — triggers replication from a source DC for a naming context.
    /// </summary>
    private Task<byte[]> HandleDrsReplicaSyncAsync(
        ReadOnlyMemory<byte> stubData,
        RpcCallContext context,
        CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // Read context handle
        var (_, handleGuid) = reader.ReadContextHandle();
        var dwVersion = reader.ReadUInt32();

        // Read DRS_MSG_REPSYNC_V1
        var request = new DRS_MSG_REPSYNC_V1();

        // pNC pointer
        var ncPointer = reader.ReadPointer();
        if (ncPointer != 0)
        {
            request.PNC = ReadDsName(reader);
        }

        // uuidDsaSrc
        var srcGuidBytes = reader.ReadBytes(16);
        request.UuidDsaSrc = new Guid(srcGuidBytes.Span);

        // pszDsaSrc pointer
        var srcStrPointer = reader.ReadPointer();
        if (srcStrPointer != 0)
        {
            request.PszDsaSrc = reader.ReadConformantVaryingString();
        }

        request.UlOptions = (DrsReplicaSyncOptions)reader.ReadUInt32();

        _logger.LogInformation(
            "DRSReplicaSync: NC={NC}, Source={Src}, Options={Opts}",
            request.PNC?.StringName, request.PszDsaSrc, request.UlOptions);

        // In our Cosmos DB architecture, replication is handled by the change feed.
        // Acknowledge the request.

        var writer = new NdrWriter();
        writer.WriteUInt32(0); // ERROR_SUCCESS

        return Task.FromResult(writer.ToArray());
    }

    #endregion

    #region IDL_DRSGetNCChanges (opnum 3)

    /// <summary>
    /// IDL_DRSGetNCChanges — returns changes to objects in a naming context since a USN.
    /// This is the core replication pull operation.
    /// </summary>
    private async Task<byte[]> HandleDrsGetNCChangesAsync(
        ReadOnlyMemory<byte> stubData,
        RpcCallContext context,
        CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // Context handle
        var (_, handleGuid) = reader.ReadContextHandle();
        var dwInVersion = reader.ReadUInt32();

        // Read request (V8)
        var request = ReadGetNCChangesRequestV8(reader);

        // Process using the replication engine
        var reply = await _engine.ProcessGetNCChangesAsync(request, context.TenantId, ct);

        // Serialize the response
        var writer = new NdrWriter();

        // pdwOutVersion
        writer.WriteUInt32(6); // V6 response

        // Write DRS_MSG_GETCHGREPLY_V6
        WriteGetNCChangesReplyV6(writer, reply);

        // Return code
        writer.WriteUInt32(reply.DwDRSError);

        return writer.ToArray();
    }

    #endregion

    #region IDL_DRSUpdateRefs (opnum 4)

    /// <summary>
    /// IDL_DRSUpdateRefs — adds or removes outbound change notification references.
    /// </summary>
    private Task<byte[]> HandleDrsUpdateRefsAsync(
        ReadOnlyMemory<byte> stubData,
        RpcCallContext context,
        CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // Context handle
        var (_, handleGuid) = reader.ReadContextHandle();
        var dwVersion = reader.ReadUInt32();

        // Read DRS_MSG_UPDREFS_V1
        var request = new DRS_MSG_UPDREFS_V1();

        var ncPointer = reader.ReadPointer();
        if (ncPointer != 0)
        {
            request.PNC = ReadDsName(reader);
        }

        var destStrPointer = reader.ReadPointer();
        if (destStrPointer != 0)
        {
            request.PszDsaDest = reader.ReadConformantVaryingString();
        }

        var destGuidBytes = reader.ReadBytes(16);
        request.UuidDsaObjDest = new Guid(destGuidBytes.Span);
        request.UlOptions = (DrsUpdateRefsOptions)reader.ReadUInt32();

        _logger.LogInformation(
            "DRSUpdateRefs: NC={NC}, Dest={Dest}, Options={Opts}",
            request.PNC?.StringName, request.PszDsaDest, request.UlOptions);

        _topology.ProcessUpdateRefs(request);

        var writer = new NdrWriter();
        writer.WriteUInt32(0); // ERROR_SUCCESS

        return Task.FromResult(writer.ToArray());
    }

    #endregion

    #region IDL_DRSVerifyNames (opnum 8)

    /// <summary>
    /// IDL_DRSVerifyNames — verifies the existence of objects by GUID, SID, or DN.
    /// </summary>
    private async Task<byte[]> HandleDrsVerifyNamesAsync(
        ReadOnlyMemory<byte> stubData,
        RpcCallContext context,
        CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // Context handle
        var (_, handleGuid) = reader.ReadContextHandle();
        var dwVersion = reader.ReadUInt32();

        // Read DRS_MSG_VERIFYREQ_V1
        var flags = (DrsVerifyNamesFlags)reader.ReadUInt32();
        var cNames = reader.ReadUInt32();

        var reply = new DRS_MSG_VERIFYREPLY_V1
        {
            PrefixTable = _prefixTable.ToWireFormat(),
        };

        // Read and verify each name
        var namesPointer = reader.ReadPointer();
        if (namesPointer != 0)
        {
            var nameCount = reader.ReadUInt32();

            for (uint i = 0; i < nameCount && i < cNames; i++)
            {
                var dsname = ReadDsName(reader);
                DirectoryObject obj = null;

                // Try to find by GUID first, then by DN
                if (dsname.Guid != Guid.Empty)
                {
                    obj = await _store.GetByGuidAsync(context.TenantId, dsname.Guid.ToString(), ct);
                }

                if (obj == null && !string.IsNullOrEmpty(dsname.StringName))
                {
                    obj = await _store.GetByDnAsync(context.TenantId, dsname.StringName, ct);
                }

                if (obj != null)
                {
                    reply.RpEntInf.Add(new ENTINF
                    {
                        PName = DSNAME.FromDn(
                            obj.DistinguishedName,
                            Guid.TryParse(obj.ObjectGuid, out var g) ? g : Guid.Empty),
                        UlFlags = 0,
                        AttrBlock = new ATTRBLOCK(),
                    });
                }
                else
                {
                    // Object not found — return empty entry
                    reply.RpEntInf.Add(new ENTINF
                    {
                        PName = dsname,
                        UlFlags = 1, // ENTINF_FROM_MASTER indicates not found
                        AttrBlock = new ATTRBLOCK(),
                    });
                }
            }
        }

        reply.CNames = (uint)reply.RpEntInf.Count;

        var writer = new NdrWriter();
        writer.WriteUInt32(1); // pdwOutVersion = 1

        // Write DRS_MSG_VERIFYREPLY_V1
        writer.WriteUInt32(reply.CNames);
        WriteEntInfArray(writer, reply.RpEntInf);
        WritePrefixTable(writer, reply.PrefixTable);

        writer.WriteUInt32(0); // return code

        return writer.ToArray();
    }

    #endregion

    #region IDL_DRSGetMemberships (opnum 9)

    /// <summary>
    /// IDL_DRSGetMemberships — returns transitive group memberships for one or more objects.
    /// Similar to the tokenGroups constructed attribute but accessible via DRS RPC.
    /// </summary>
    private async Task<byte[]> HandleDrsGetMembershipsAsync(
        ReadOnlyMemory<byte> stubData,
        RpcCallContext context,
        CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // Context handle
        var (_, handleGuid) = reader.ReadContextHandle();
        var dwVersion = reader.ReadUInt32();

        // Read DRS_MSG_REVMEMB_REQ_V1
        var cNames = reader.ReadUInt32();

        // Read names array pointer
        var namesPointer = reader.ReadPointer();
        var names = new List<DSNAME>();
        if (namesPointer != 0)
        {
            var nameCount = reader.ReadUInt32();
            for (uint i = 0; i < nameCount && i < cNames; i++)
            {
                names.Add(ReadDsName(reader));
            }
        }

        var operationType = (RevMembGetType)reader.ReadUInt32();

        // pLimitingDomain pointer
        var limitPointer = reader.ReadPointer();
        DSNAME limitingDomain = null;
        if (limitPointer != 0)
        {
            limitingDomain = ReadDsName(reader);
        }

        _logger.LogInformation(
            "DRSGetMemberships: {Count} names, op={Op}",
            cNames, operationType);

        var reply = new DRS_MSG_REVMEMB_REPLY_V1();

        // For each name, compute transitive group memberships
        foreach (var dsname in names)
        {
            DirectoryObject obj = null;

            if (dsname.Guid != Guid.Empty)
            {
                obj = await _store.GetByGuidAsync(context.TenantId, dsname.Guid.ToString(), ct);
            }

            if (obj == null && !string.IsNullOrEmpty(dsname.StringName))
            {
                obj = await _store.GetByDnAsync(context.TenantId, dsname.StringName, ct);
            }

            if (obj == null)
                continue;

            // Compute transitive memberships by walking memberOf
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<string>(obj.MemberOf);

            while (queue.Count > 0)
            {
                ct.ThrowIfCancellationRequested();

                string groupDn = queue.Dequeue();
                if (!visited.Add(groupDn))
                    continue;

                var group = await _store.GetByDnAsync(context.TenantId, groupDn, ct);
                if (group == null)
                    continue;

                // Apply scope filtering based on operation type
                if (operationType == RevMembGetType.REVMEMB_GET_ACCOUNT_GROUPS)
                {
                    // Only global groups
                    int scopeBits = group.GroupType & 0x0F;
                    if (scopeBits != 0x02) continue;
                }
                else if (operationType == RevMembGetType.REVMEMB_GET_UNIVERSAL_GROUPS)
                {
                    // Only universal groups
                    int scopeBits = group.GroupType & 0x0F;
                    if (scopeBits != 0x08) continue;
                }
                else if (operationType == RevMembGetType.REVMEMB_GET_RESOURCE_GROUPS)
                {
                    // Only domain-local groups
                    int scopeBits = group.GroupType & 0x0F;
                    if (scopeBits != 0x04) continue;
                }

                reply.PpDsNames.Add(DSNAME.FromDn(
                    group.DistinguishedName,
                    Guid.TryParse(group.ObjectGuid, out var g) ? g : Guid.Empty));

                // Continue walking up
                foreach (string parentDn in group.MemberOf)
                    queue.Enqueue(parentDn);
            }
        }

        reply.CDsNames = (uint)reply.PpDsNames.Count;

        // Write response
        var writer = new NdrWriter();
        writer.WriteUInt32(1); // pdwOutVersion = 1

        // DRS_MSG_REVMEMB_REPLY_V1
        writer.WriteUInt32(reply.ErrCode);
        writer.WriteUInt32(reply.CDsNames);

        // ppDsNames pointer
        writer.WritePointer(isNull: reply.PpDsNames.Count == 0);
        if (reply.PpDsNames.Count > 0)
        {
            writer.WriteUInt32((uint)reply.PpDsNames.Count);
            foreach (var dsn in reply.PpDsNames)
            {
                WriteDsName(writer, dsn);
            }
        }

        // pAttributes (empty)
        WriteAttrBlock(writer, reply.PAttributes);

        // cSidHistory
        writer.WriteUInt32(reply.CSidHistory);
        // ppSidHistory pointer
        writer.WritePointer(isNull: true);

        writer.WriteUInt32(0); // return code

        return writer.ToArray();
    }

    #endregion

    #region IDL_DRSCrackNames (opnum 12)

    /// <summary>
    /// IDL_DRSCrackNames — converts object names between various formats.
    /// </summary>
    private async Task<byte[]> HandleDrsCrackNamesAsync(
        ReadOnlyMemory<byte> stubData,
        RpcCallContext context,
        CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // Context handle
        var (_, handleGuid) = reader.ReadContextHandle();
        var dwVersion = reader.ReadUInt32();

        // Read DRS_MSG_CRACKREQ_V1
        var request = new DRS_MSG_CRACKREQ_V1
        {
            CodePage = reader.ReadUInt32(),
            LocaleId = reader.ReadUInt32(),
            DwFlags = (DsNameFlags)reader.ReadUInt32(),
            FormatOffered = (DsNameFormat)reader.ReadUInt32(),
            FormatDesired = (DsNameFormat)reader.ReadUInt32(),
            CNames = reader.ReadUInt32(),
        };

        // Read name array pointer
        var namesPointer = reader.ReadPointer();
        if (namesPointer != 0)
        {
            var nameCount = reader.ReadUInt32();
            for (uint i = 0; i < nameCount && i < request.CNames; i++)
            {
                // Each name is a pointer to a string
                var strPointer = reader.ReadPointer();
                if (strPointer != 0)
                {
                    // Deferred read — names come after all pointers
                }
            }

            // Read the actual string data
            for (uint i = 0; i < nameCount && i < request.CNames; i++)
            {
                request.RpNames.Add(reader.ReadConformantVaryingString());
            }
        }

        _logger.LogInformation(
            "DRSCrackNames: {Count} names, {Offered}->{Desired}, flags={Flags}",
            request.CNames, request.FormatOffered, request.FormatDesired, request.DwFlags);

        var reply = await _nameResolution.CrackNamesAsync(
            request, context.TenantId, context.DomainDn, ct);

        // Write response
        var writer = new NdrWriter();
        writer.WriteUInt32(1); // pdwOutVersion = 1

        // DRS_MSG_CRACKREPLY_V1 -> DS_NAME_RESULTW
        var result = reply.PResult;
        writer.WritePointer(isNull: result == null);

        if (result != null)
        {
            writer.WriteUInt32(result.CItems);

            // Pointer to items array
            writer.WritePointer(isNull: result.RItems.Count == 0);

            if (result.RItems.Count > 0)
            {
                writer.WriteUInt32((uint)result.RItems.Count);

                foreach (var item in result.RItems)
                {
                    writer.WriteUInt32((uint)item.Status);
                    // pDomain pointer
                    writer.WritePointer(isNull: item.PDomain == null);
                    // pName pointer
                    writer.WritePointer(isNull: item.PName == null);
                }

                // Write deferred string data
                foreach (var item in result.RItems)
                {
                    if (item.PDomain != null)
                        writer.WriteDeferredConformantVaryingString(item.PDomain);
                    if (item.PName != null)
                        writer.WriteDeferredConformantVaryingString(item.PName);
                }
            }
        }

        writer.WriteUInt32(0); // return code

        return writer.ToArray();
    }

    #endregion

    #region IDL_DRSWriteSPN (opnum 13)

    /// <summary>
    /// IDL_DRSWriteSPN — adds, removes, or replaces SPNs on a computer/service account.
    /// </summary>
    private async Task<byte[]> HandleDrsWriteSpnAsync(
        ReadOnlyMemory<byte> stubData,
        RpcCallContext context,
        CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // Context handle
        var (_, handleGuid) = reader.ReadContextHandle();
        var dwVersion = reader.ReadUInt32();

        // Read DRS_MSG_WRITESPNREQ_V1
        var operation = (DrsWriteSpnOperation)reader.ReadUInt32();

        // Account DN pointer
        string accountDn = null;
        var accountPointer = reader.ReadPointer();
        if (accountPointer != 0)
        {
            accountDn = reader.ReadConformantVaryingString();
        }

        var cSpn = reader.ReadUInt32();
        var spnList = new List<string>();

        // SPN array pointer
        var spnArrayPointer = reader.ReadPointer();
        if (spnArrayPointer != 0)
        {
            var spnCount = reader.ReadUInt32();
            // Read string pointers first
            for (uint i = 0; i < spnCount && i < cSpn; i++)
            {
                reader.ReadPointer(); // deferred
            }
            // Then the actual strings
            for (uint i = 0; i < spnCount && i < cSpn; i++)
            {
                spnList.Add(reader.ReadConformantVaryingString());
            }
        }

        _logger.LogInformation(
            "DRSWriteSPN: op={Op}, account={Account}, {Count} SPNs",
            operation, accountDn, spnList.Count);

        uint win32Error = 0;

        if (!string.IsNullOrEmpty(accountDn))
        {
            var obj = await _store.GetByDnAsync(context.TenantId, accountDn, ct);

            if (obj == null)
            {
                win32Error = 2; // ERROR_FILE_NOT_FOUND
            }
            else
            {
                switch (operation)
                {
                    case DrsWriteSpnOperation.DS_SPN_ADD_SPN_OP:
                        foreach (var spn in spnList)
                        {
                            if (!obj.ServicePrincipalName.Contains(spn, StringComparer.OrdinalIgnoreCase))
                                obj.ServicePrincipalName.Add(spn);
                        }
                        break;

                    case DrsWriteSpnOperation.DS_SPN_REPLACE_SPN_OP:
                        obj.ServicePrincipalName = [.. spnList];
                        break;

                    case DrsWriteSpnOperation.DS_SPN_DELETE_SPN_OP:
                        obj.ServicePrincipalName.RemoveAll(
                            s => spnList.Contains(s, StringComparer.OrdinalIgnoreCase));
                        break;
                }

                var usn = await _store.GetNextUsnAsync(context.TenantId, context.DomainDn, ct);
                obj.USNChanged = usn;
                obj.WhenChanged = DateTimeOffset.UtcNow;
                await _store.UpdateAsync(obj, ct);
            }
        }
        else
        {
            win32Error = 87; // ERROR_INVALID_PARAMETER
        }

        // Write response
        var writer = new NdrWriter();
        writer.WriteUInt32(1); // pdwOutVersion = 1
        writer.WriteUInt32(win32Error);
        writer.WriteUInt32(0); // return code

        return writer.ToArray();
    }

    #endregion

    #region IDL_DRSRemoveDsServer (opnum 14)

    /// <summary>
    /// IDL_DRSRemoveDsServer — removes a DC's metadata from the directory after demotion.
    /// Deletes the server object from CN=Servers under the site and returns whether
    /// this was the last DC in the domain.
    /// </summary>
    private async Task<byte[]> HandleDrsRemoveDsServerAsync(
        ReadOnlyMemory<byte> stubData,
        RpcCallContext context,
        CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // Context handle
        var (_, handleGuid) = reader.ReadContextHandle();
        var dwVersion = reader.ReadUInt32();

        // Read DRS_MSG_RMSVRREQ_V1
        string serverDn = null;
        var serverDnPointer = reader.ReadPointer();
        if (serverDnPointer != 0)
        {
            serverDn = reader.ReadConformantVaryingString();
        }

        string domainDn = null;
        var domainDnPointer = reader.ReadPointer();
        if (domainDnPointer != 0)
        {
            domainDn = reader.ReadConformantVaryingString();
        }

        var fCommit = reader.ReadUInt32() != 0;

        _logger.LogInformation(
            "DRSRemoveDsServer: server={Server}, domain={Domain}, commit={Commit}",
            serverDn, domainDn, fCommit);

        bool fLastDcInDomain = false;
        uint win32Error = 0;

        if (string.IsNullOrEmpty(serverDn))
        {
            win32Error = 87; // ERROR_INVALID_PARAMETER
        }
        else
        {
            // Check if the server object exists
            var serverObj = await _store.GetByDnAsync(context.TenantId, serverDn, ct);
            if (serverObj == null)
            {
                win32Error = 2; // ERROR_FILE_NOT_FOUND
            }
            else
            {
                // Check if this is the last DC — look for sibling server objects
                var parentDn = serverObj.ParentDn;
                if (!string.IsNullOrEmpty(parentDn))
                {
                    var siblings = await _store.GetChildrenAsync(context.TenantId, parentDn, ct);
                    var dcCount = siblings.Count(s =>
                        s.ObjectClass.Contains("server", StringComparer.OrdinalIgnoreCase));
                    fLastDcInDomain = dcCount <= 1;
                }

                if (fCommit)
                {
                    // Delete the NTDS Settings object first (child), then the server object
                    var ntdsSettingsDn = $"CN=NTDS Settings,{serverDn}";
                    var ntdsObj = await _store.GetByDnAsync(context.TenantId, ntdsSettingsDn, ct);
                    if (ntdsObj != null)
                    {
                        await _store.DeleteAsync(context.TenantId, ntdsSettingsDn, hardDelete: true, ct);
                    }

                    await _store.DeleteAsync(context.TenantId, serverDn, hardDelete: true, ct);

                    _logger.LogInformation("DRSRemoveDsServer: deleted server metadata for {Server}", serverDn);
                }
            }
        }

        // Write response
        var writer = new NdrWriter();
        writer.WriteUInt32(1); // pdwOutVersion = 1
        writer.WriteBoolean(fLastDcInDomain);
        writer.WriteUInt32(win32Error); // return code

        return writer.ToArray();
    }

    #endregion

    #region IDL_DRSRemoveDsDomain (opnum 15)

    /// <summary>
    /// IDL_DRSRemoveDsDomain — removes a domain's cross-ref object from the directory
    /// after the last DC for that domain has been demoted.
    /// </summary>
    private async Task<byte[]> HandleDrsRemoveDsDomainAsync(
        ReadOnlyMemory<byte> stubData,
        RpcCallContext context,
        CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // Context handle
        var (_, handleGuid) = reader.ReadContextHandle();
        var dwVersion = reader.ReadUInt32();

        // Read DRS_MSG_RMDMNREQ_V1
        string domainDn = null;
        var domainDnPointer = reader.ReadPointer();
        if (domainDnPointer != 0)
        {
            domainDn = reader.ReadConformantVaryingString();
        }

        _logger.LogInformation("DRSRemoveDsDomain: domain={Domain}", domainDn);

        uint win32Error = 0;

        if (string.IsNullOrEmpty(domainDn))
        {
            win32Error = 87; // ERROR_INVALID_PARAMETER
        }
        else
        {
            // Find and delete the cross-ref object in CN=Partitions,CN=Configuration
            var configDn = $"CN=Configuration,{context.DomainDn}";
            var partitionsDn = $"CN=Partitions,{configDn}";
            var crossRefs = await _store.GetChildrenAsync(context.TenantId, partitionsDn, ct);

            var crossRef = crossRefs.FirstOrDefault(cr =>
                cr.GetAttribute("nCName")?.GetFirstString()?.Equals(domainDn, StringComparison.OrdinalIgnoreCase) == true);

            if (crossRef != null)
            {
                await _store.DeleteAsync(context.TenantId, crossRef.DistinguishedName, hardDelete: true, ct);
                _logger.LogInformation("DRSRemoveDsDomain: deleted cross-ref for {Domain}", domainDn);
            }
            else
            {
                win32Error = 2; // ERROR_FILE_NOT_FOUND
            }
        }

        // Write response
        var writer = new NdrWriter();
        writer.WriteUInt32(1); // pdwOutVersion = 1
        writer.WriteUInt32(win32Error); // return code

        return writer.ToArray();
    }

    #endregion

    #region IDL_DRSAddCloneDC (opnum 28)

    /// <summary>
    /// IDL_DRSAddCloneDC — creates a new DC account for virtual DC cloning per [MS-DRSR] section 4.1.27.
    ///
    /// DC cloning allows a virtualized domain controller to be duplicated without running dcpromo.
    /// The source DC (this server) provisions directory objects for the clone:
    ///   1. A computer account in the Domain Controllers OU
    ///   2. A server object under CN=Sites,CN=Configuration
    ///   3. An NTDS Settings object (nTDSDSA) under the server object
    ///   4. Replication partnership nTDSConnection objects so the clone can replicate
    ///
    /// Production considerations not fully implemented here:
    ///   - SID allocation via RID pool for the clone's computer account
    ///   - Full ACL propagation on created objects (nTSecurityDescriptor)
    ///   - Cross-reference updates for additional naming contexts (Schema, Config)
    ///   - SYSVOL / DFS-R membership objects
    ///   - DNS registration records (SRV, A/AAAA)
    ///   - Group Policy container replication
    ///   - Machine account password seeding
    ///   - FRS / DFS-R subscriber object creation
    ///   - KCC notification to recompute the inter-site topology
    /// </summary>
    private async Task<byte[]> HandleDrsAddCloneDCAsync(
        ReadOnlyMemory<byte> stubData,
        RpcCallContext context,
        CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // Context handle
        var (_, handleGuid) = reader.ReadContextHandle();
        var dwVersion = reader.ReadUInt32();

        // Read DRS_MSG_ADDCLONEDCREQ — version 1
        // CloneDcName pointer
        string cloneDcName = null;
        var cloneNamePointer = reader.ReadPointer();
        if (cloneNamePointer != 0)
        {
            cloneDcName = reader.ReadConformantVaryingString();
        }

        // SiteName pointer
        string siteName = null;
        var siteNamePointer = reader.ReadPointer();
        if (siteNamePointer != 0)
        {
            siteName = reader.ReadConformantVaryingString();
        }

        _logger.LogInformation(
            "DRSAddCloneDC: cloneName={CloneName}, site={Site}",
            cloneDcName, siteName);

        uint win32Error = 0;
        string sourceDsaDn = null;

        try
        {
            // ── Step 1: Validate request parameters ──────────────────────────────
            if (string.IsNullOrWhiteSpace(cloneDcName))
            {
                _logger.LogWarning("DRSAddCloneDC: clone DC name is required");
                win32Error = 87; // ERROR_INVALID_PARAMETER
                return WriteAddCloneDCResponse(win32Error, null);
            }

            // Sanitize the clone name — must be a valid NetBIOS name (<=15 chars, no dots)
            if (cloneDcName.Length > 15 || cloneDcName.Contains('.'))
            {
                _logger.LogWarning("DRSAddCloneDC: invalid clone DC name '{Name}'", cloneDcName);
                win32Error = 123; // ERROR_INVALID_NAME
                return WriteAddCloneDCResponse(win32Error, null);
            }

            var domainDn = context.DomainDn;
            if (string.IsNullOrEmpty(domainDn))
            {
                _logger.LogWarning("DRSAddCloneDC: no domain DN in context");
                win32Error = 87; // ERROR_INVALID_PARAMETER
                return WriteAddCloneDCResponse(win32Error, null);
            }

            // Default to the source DC's site if not specified
            if (string.IsNullOrWhiteSpace(siteName))
            {
                siteName = _dcInfo.SiteName;
            }

            // Verify the target site exists in the configuration partition
            var siteContainerDn = $"CN={siteName},CN=Sites,CN=Configuration,{domainDn}";
            var siteObj = await _store.GetByDnAsync(context.TenantId, siteContainerDn, ct);
            if (siteObj == null)
            {
                _logger.LogWarning("DRSAddCloneDC: site '{Site}' not found at {DN}", siteName, siteContainerDn);
                win32Error = 2; // ERROR_FILE_NOT_FOUND
                return WriteAddCloneDCResponse(win32Error, null);
            }

            // Check that a DC with the same name does not already exist
            var cloneServerDn = $"CN={cloneDcName},CN=Servers,CN={siteName},CN=Sites,CN=Configuration,{domainDn}";
            var existingServer = await _store.GetByDnAsync(context.TenantId, cloneServerDn, ct);
            if (existingServer != null)
            {
                _logger.LogWarning("DRSAddCloneDC: server object already exists at {DN}", cloneServerDn);
                win32Error = 183; // ERROR_ALREADY_EXISTS
                return WriteAddCloneDCResponse(win32Error, null);
            }

            // Also check for an existing computer account in the Domain Controllers OU
            var dcOuDn = $"OU=Domain Controllers,{domainDn}";
            var cloneComputerDn = $"CN={cloneDcName},{dcOuDn}";
            var existingComputer = await _store.GetByDnAsync(context.TenantId, cloneComputerDn, ct);
            if (existingComputer != null)
            {
                _logger.LogWarning("DRSAddCloneDC: computer account already exists at {DN}", cloneComputerDn);
                win32Error = 183; // ERROR_ALREADY_EXISTS
                return WriteAddCloneDCResponse(win32Error, null);
            }

            // ── Step 2: Create the computer account in the Domain Controllers OU ─
            // Per [MS-ADTS], a DC computer account has:
            //   objectClass: top, person, organizationalPerson, user, computer
            //   userAccountControl: UF_SERVER_TRUST_ACCOUNT | UF_TRUSTED_FOR_DELEGATION
            //   sAMAccountType: SAM_MACHINE_ACCOUNT
            var now = DateTimeOffset.UtcNow;
            var usn = await _store.GetNextUsnAsync(context.TenantId, domainDn, ct);
            var cloneObjectGuid = Guid.NewGuid();

            // Build the DNS domain name from the domain DN (DC=corp,DC=example,DC=com -> corp.example.com)
            var dnsDomainName = DomainDnToDnsName(domainDn);

            var computerObj = new DirectoryObject
            {
                Id = cloneComputerDn.ToLowerInvariant(),
                TenantId = context.TenantId,
                DomainDn = domainDn,
                DistinguishedName = cloneComputerDn,
                ObjectGuid = cloneObjectGuid.ToString(),
                Cn = cloneDcName,
                DisplayName = cloneDcName,
                SAMAccountName = cloneDcName + "$",
                ObjectClass = ["top", "person", "organizationalPerson", "user", "computer"],
                ObjectCategory = "computer",
                ParentDn = dcOuDn,
                DnsHostName = $"{cloneDcName.ToLowerInvariant()}.{dnsDomainName}",
                // UF_SERVER_TRUST_ACCOUNT (0x2000) | UF_TRUSTED_FOR_DELEGATION (0x80000)
                UserAccountControl = 0x82000,
                SAMAccountType = 0x30000001, // SAM_MACHINE_ACCOUNT
                OperatingSystem = "Windows Server",
                OperatingSystemVersion = "10.0 (20348)",
                PrimaryGroupId = 516, // Domain Controllers group RID
                USNCreated = usn,
                USNChanged = usn,
                WhenCreated = now,
                WhenChanged = now,
                ServicePrincipalName =
                [
                    $"HOST/{cloneDcName}",
                    $"HOST/{cloneDcName.ToLowerInvariant()}.{dnsDomainName}",
                    $"ldap/{cloneDcName}",
                    $"ldap/{cloneDcName.ToLowerInvariant()}.{dnsDomainName}",
                    $"GC/{cloneDcName.ToLowerInvariant()}.{dnsDomainName}/{dnsDomainName}",
                    $"E3514235-4B06-11D1-AB04-00C04FC2DCD2/{cloneObjectGuid}/{dnsDomainName}",
                ],
            };

            // Allocate a proper SID from the domain's RID pool
            computerObj.ObjectSid = await _ridAllocator.GenerateObjectSidAsync(context.TenantId, domainDn, ct);
            _logger.LogDebug("DRSAddCloneDC: allocated SID {Sid} for computer account {Name}", computerObj.ObjectSid, cloneDcName);

            await _store.CreateAsync(computerObj, ct);
            _logger.LogInformation("DRSAddCloneDC: created computer account at {DN}", cloneComputerDn);

            // ── Step 3: Create the server object under CN=Sites configuration ────
            // The server object represents the DC in the site topology.
            var serversContainerDn = $"CN=Servers,CN={siteName},CN=Sites,CN=Configuration,{domainDn}";
            var serverUsn = await _store.GetNextUsnAsync(context.TenantId, domainDn, ct);
            var serverObjectGuid = Guid.NewGuid();

            var serverObj = new DirectoryObject
            {
                Id = cloneServerDn.ToLowerInvariant(),
                TenantId = context.TenantId,
                DomainDn = domainDn,
                DistinguishedName = cloneServerDn,
                ObjectGuid = serverObjectGuid.ToString(),
                Cn = cloneDcName,
                ObjectClass = ["top", "server"],
                ObjectCategory = "server",
                ParentDn = serversContainerDn,
                DnsHostName = $"{cloneDcName.ToLowerInvariant()}.{dnsDomainName}",
                USNCreated = serverUsn,
                USNChanged = serverUsn,
                WhenCreated = now,
                WhenChanged = now,
            };

            // serverReference links the server object to the computer account
            serverObj.Attributes["serverReference"] = new DirectoryAttribute("serverReference", cloneComputerDn);

            await _store.CreateAsync(serverObj, ct);
            _logger.LogInformation("DRSAddCloneDC: created server object at {DN}", cloneServerDn);

            // ── Step 4: Create the NTDS Settings (nTDSDSA) object ────────────────
            // This is the DSA object that identifies the clone as a directory system agent.
            // It holds the invocationId, replication options, and is the parent for
            // nTDSConnection objects that define replication partnerships.
            var cloneNtdsSettingsDn = $"CN=NTDS Settings,{cloneServerDn}";
            var ntdsUsn = await _store.GetNextUsnAsync(context.TenantId, domainDn, ct);
            var cloneDsaGuid = Guid.NewGuid();
            var cloneInvocationId = Guid.NewGuid();

            var ntdsObj = new DirectoryObject
            {
                Id = cloneNtdsSettingsDn.ToLowerInvariant(),
                TenantId = context.TenantId,
                DomainDn = domainDn,
                DistinguishedName = cloneNtdsSettingsDn,
                ObjectGuid = cloneDsaGuid.ToString(),
                Cn = "NTDS Settings",
                ObjectClass = ["top", "applicationSettings", "nTDSDSA"],
                ObjectCategory = "nTDSDSA",
                ParentDn = cloneServerDn,
                USNCreated = ntdsUsn,
                USNChanged = ntdsUsn,
                WhenCreated = now,
                WhenChanged = now,
            };

            // invocationId — unique identifier that changes if the DC is restored from backup
            ntdsObj.Attributes["invocationId"] = new DirectoryAttribute("invocationId", cloneInvocationId.ToString());

            // options — bit 0 = IS_GC (we set the clone as a GC candidate by default)
            // Production note: this should mirror the source DC's options or be configurable
            ntdsObj.Attributes["options"] = new DirectoryAttribute("options", "1");

            // hasMasterNCs — the naming contexts this DC holds writable replicas of
            ntdsObj.Attributes["hasMasterNCs"] = new DirectoryAttribute("hasMasterNCs",
                domainDn,
                $"CN=Configuration,{domainDn}",
                $"CN=Schema,CN=Configuration,{domainDn}");

            // msDS-HasDomainNCs — the domain NC
            ntdsObj.Attributes["msDS-HasDomainNCs"] = new DirectoryAttribute("msDS-HasDomainNCs", domainDn);

            // msDS-hasMasterNCs — same as hasMasterNCs but extended version
            ntdsObj.Attributes["msDS-hasMasterNCs"] = new DirectoryAttribute("msDS-hasMasterNCs",
                domainDn,
                $"CN=Configuration,{domainDn}",
                $"CN=Schema,CN=Configuration,{domainDn}");

            await _store.CreateAsync(ntdsObj, ct);
            _logger.LogInformation("DRSAddCloneDC: created NTDS Settings at {DN}", cloneNtdsSettingsDn);

            // ── Step 5: Create replication partnership (nTDSConnection) ───────────
            // An nTDSConnection object under the clone's NTDS Settings defines the
            // inbound replication source. The clone will pull changes from the source DC
            // (this server) for all naming contexts.
            var sourceNtdsSettingsDn = _dcInfo.NtdsSettingsDn(domainDn);
            var connectionDn = $"CN=Repl-From-{_dcInfo.Hostname},{cloneNtdsSettingsDn}";
            var connUsn = await _store.GetNextUsnAsync(context.TenantId, domainDn, ct);

            var connectionObj = new DirectoryObject
            {
                Id = connectionDn.ToLowerInvariant(),
                TenantId = context.TenantId,
                DomainDn = domainDn,
                DistinguishedName = connectionDn,
                ObjectGuid = Guid.NewGuid().ToString(),
                Cn = $"Repl-From-{_dcInfo.Hostname}",
                ObjectClass = ["top", "nTDSConnection"],
                ObjectCategory = "nTDSConnection",
                ParentDn = cloneNtdsSettingsDn,
                USNCreated = connUsn,
                USNChanged = connUsn,
                WhenCreated = now,
                WhenChanged = now,
            };

            // fromServer — the source NTDS Settings DN (this DC)
            connectionObj.Attributes["fromServer"] = new DirectoryAttribute("fromServer", sourceNtdsSettingsDn);

            // enabledConnection — TRUE to activate replication
            connectionObj.Attributes["enabledConnection"] = new DirectoryAttribute("enabledConnection", "TRUE");

            // options — bit 0 = IS_GENERATED (KCC-generated), bit 2 = USE_NOTIFY
            // 0x5 = IS_GENERATED | USE_NOTIFY — standard for intra-site auto-generated connections
            connectionObj.Attributes["options"] = new DirectoryAttribute("options", "5");

            // transportType — DN of the IP inter-site transport
            var ipTransportDn = $"CN=IP,CN=Inter-Site Transports,CN=Sites,CN=Configuration,{domainDn}";
            connectionObj.Attributes["transportType"] = new DirectoryAttribute("transportType", ipTransportDn);

            await _store.CreateAsync(connectionObj, ct);
            _logger.LogInformation("DRSAddCloneDC: created replication connection at {DN}", connectionDn);

            // ── Step 6: Register replication partnerships in the topology ─────────
            // Add the clone as an outbound notification partner so this DC will push
            // change notifications to it once it comes online.
            _topology.AddNotificationRef(new ReplicationPartner
            {
                DsaGuid = cloneDsaGuid,
                DnsName = $"{cloneDcName.ToLowerInvariant()}.{dnsDomainName}",
                NtdsSettingsDn = cloneNtdsSettingsDn,
                NamingContextDn = domainDn,
                Options = DsReplNeighborFlags.DS_REPL_NBR_WRITEABLE
                        | DsReplNeighborFlags.DS_REPL_NBR_SYNC_ON_STARTUP
                        | DsReplNeighborFlags.DS_REPL_NBR_DO_SCHEDULED_SYNCS,
                InvocationId = cloneInvocationId,
            });

            // Also register for the Configuration NC
            _topology.AddNotificationRef(new ReplicationPartner
            {
                DsaGuid = cloneDsaGuid,
                DnsName = $"{cloneDcName.ToLowerInvariant()}.{dnsDomainName}",
                NtdsSettingsDn = cloneNtdsSettingsDn,
                NamingContextDn = $"CN=Configuration,{domainDn}",
                Options = DsReplNeighborFlags.DS_REPL_NBR_WRITEABLE
                        | DsReplNeighborFlags.DS_REPL_NBR_SYNC_ON_STARTUP
                        | DsReplNeighborFlags.DS_REPL_NBR_DO_SCHEDULED_SYNCS,
                InvocationId = cloneInvocationId,
            });

            _logger.LogInformation(
                "DRSAddCloneDC: registered replication partnerships for clone {Name} (DSA={DsaGuid})",
                cloneDcName, cloneDsaGuid);

            // Return the source DSA DN — the clone needs this to know where to pull from
            sourceDsaDn = sourceNtdsSettingsDn;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DRSAddCloneDC: failed to create clone DC '{Name}'", cloneDcName);
            // Map to a generic error; production would map specific exceptions
            // (e.g., concurrency conflict -> ERROR_BUSY, not found -> ERROR_FILE_NOT_FOUND)
            win32Error = 32; // ERROR_SHARING_VIOLATION (generic transient failure)
        }

        return WriteAddCloneDCResponse(win32Error, sourceDsaDn);
    }

    /// <summary>
    /// Writes the DRS_MSG_ADDCLONEDCREPLY_V1 response for IDL_DRSAddCloneDC.
    /// Wire format: pdwOutVersion (DWORD) + win32Error (DWORD) + pszSourceDsaDN (pointer + string) + return code (DWORD).
    /// </summary>
    private byte[] WriteAddCloneDCResponse(uint win32Error, string sourceDsaDn)
    {
        var writer = new NdrWriter();
        writer.WriteUInt32(1); // pdwOutVersion = 1
        writer.WriteUInt32(win32Error); // DRS_MSG_ADDCLONEDCREPLY_V1.dwWin32Error

        // pszSourceDsaDN — conformant varying string pointer
        writer.WritePointer(isNull: sourceDsaDn == null);
        if (sourceDsaDn != null)
        {
            writer.WriteDeferredConformantVaryingString(sourceDsaDn);
        }

        writer.WriteUInt32(win32Error); // return code

        return writer.ToArray();
    }

    /// <summary>
    /// Converts a domain DN (e.g., "DC=corp,DC=example,DC=com") to a DNS name ("corp.example.com").
    /// </summary>
    private static string DomainDnToDnsName(string domainDn)
    {
        var parts = domainDn.Split(',')
            .Where(p => p.Trim().StartsWith("DC=", StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Trim().Substring(3));
        return string.Join(".", parts).ToLowerInvariant();
    }

    #endregion

    #region IDL_DRSGetReplInfo (opnum 19)

    /// <summary>
    /// IDL_DRSGetReplInfo — returns replication state information (neighbors, cursors, etc.).
    /// </summary>
    private Task<byte[]> HandleDrsGetReplInfoAsync(
        ReadOnlyMemory<byte> stubData,
        RpcCallContext context,
        CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // Context handle
        var (_, handleGuid) = reader.ReadContextHandle();
        var infoType = (DsReplInfoType)reader.ReadUInt32();

        // Read object DN pointer
        string objectDn = null;
        var objPointer = reader.ReadPointer();
        if (objPointer != 0)
        {
            objectDn = reader.ReadConformantVaryingString();
        }

        _logger.LogInformation("DRSGetReplInfo: type={Type}, object={DN}", infoType, objectDn);

        var writer = new NdrWriter();

        switch (infoType)
        {
            case DsReplInfoType.DS_REPL_INFO_NEIGHBORS:
                WriteNeighborsInfo(writer, objectDn);
                break;

            case DsReplInfoType.DS_REPL_INFO_CURSORS_FOR_NC:
            case DsReplInfoType.DS_REPL_INFO_CURSORS_3_FOR_NC:
                WriteCursorsInfo(writer, objectDn);
                break;

            case DsReplInfoType.DS_REPL_INFO_KCC_DSA_CONNECT_FAILURES:
            case DsReplInfoType.DS_REPL_INFO_KCC_DSA_LINK_FAILURES:
                WriteKccFailureInfo(writer);
                break;

            default:
                // Unsupported info type — return empty
                writer.WriteUInt32((uint)infoType);
                writer.WriteUInt32(0); // count = 0
                break;
        }

        writer.WriteUInt32(0); // return code

        return Task.FromResult(writer.ToArray());
    }

    #endregion

    #region NDR serialization helpers

    private static DSNAME ReadDsName(NdrReader reader)
    {
        var dsname = new DSNAME();

        dsname.StructLen = reader.ReadUInt32();
        dsname.SidLen = reader.ReadUInt32();

        var guidBytes = reader.ReadBytes(16);
        dsname.Guid = new Guid(guidBytes.Span);

        if (dsname.SidLen > 0)
        {
            dsname.Sid = reader.ReadBytes((int)dsname.SidLen).ToArray();
        }

        dsname.NameLen = reader.ReadUInt32();
        if (dsname.NameLen > 0)
        {
            var nameBytes = reader.ReadBytes((int)(dsname.NameLen + 1) * 2); // +1 for null
            dsname.StringName = System.Text.Encoding.Unicode
                .GetString(nameBytes.Span).TrimEnd('\0');
        }

        return dsname;
    }

    private static void WriteDsName(NdrWriter writer, DSNAME dsname)
    {
        writer.WriteUInt32(dsname.ComputeStructLen());
        writer.WriteUInt32(dsname.SidLen);

        Span<byte> guidBytes = stackalloc byte[16];
        dsname.Guid.TryWriteBytes(guidBytes);
        writer.WriteBytes(guidBytes);

        if (dsname.SidLen > 0)
        {
            writer.WriteBytes(dsname.Sid);
        }

        writer.WriteUInt32(dsname.NameLen);
        if (dsname.NameLen > 0)
        {
            var nameBytes = System.Text.Encoding.Unicode.GetBytes(dsname.StringName + '\0');
            writer.WriteBytes(nameBytes);
        }
    }

    private DRS_MSG_GETCHGREQ_V8 ReadGetNCChangesRequestV8(NdrReader reader)
    {
        var request = new DRS_MSG_GETCHGREQ_V8();

        // uuidDsaObjDest
        var destGuid = reader.ReadBytes(16);
        request.UuidDsaObjDest = new Guid(destGuid.Span);

        // uuidInvocIdSrc
        var invocGuid = reader.ReadBytes(16);
        request.UuidInvocIdSrc = new Guid(invocGuid.Span);

        // pNC pointer
        var ncPointer = reader.ReadPointer();
        if (ncPointer != 0)
        {
            request.PNC = ReadDsName(reader);
        }

        // USN_VECTOR usnvecFrom
        request.UsnvecFrom = new USN_VECTOR
        {
            UsnHighObjUpdate = reader.ReadInt64(),
            UsnReserved = reader.ReadInt64(),
            UsnHighPropUpdate = reader.ReadInt64(),
        };

        // pUpToDateVecDest pointer
        var utdPointer = reader.ReadPointer();
        if (utdPointer != 0)
        {
            var utd = new UPTODATE_VECTOR_V2_EXT
            {
                DwVersion = reader.ReadUInt32(),
                DwReserved = reader.ReadUInt32(),
                CNumCursors = reader.ReadUInt32(),
            };

            for (uint i = 0; i < utd.CNumCursors; i++)
            {
                var cursorGuid = reader.ReadBytes(16);
                utd.RgCursors.Add(new UPTODATE_CURSOR_V2
                {
                    UuidDsa = new Guid(cursorGuid.Span),
                    UsnHighPropUpdate = reader.ReadInt64(),
                    FtimeLastSyncSuccess = reader.ReadInt64(),
                });
            }

            request.PUpToDateVecDest = utd;
        }

        // ulFlags
        request.UlFlags = (DrsGetNcChangesFlags)reader.ReadUInt32();

        // cMaxObjects
        request.CMaxObjects = reader.ReadUInt32();

        // cMaxBytes
        request.CMaxBytes = reader.ReadUInt32();

        // ulExtendedOp
        request.UlExtendedOp = reader.ReadUInt32();

        // liFsmoInfo
        request.LiFsmoInfo = reader.ReadUInt64();

        // pPartialAttrSet pointer
        var pasPointer = reader.ReadPointer();
        if (pasPointer != 0)
        {
            request.PPartialAttrSet = ReadPartialAttrVector(reader);
        }

        // pPartialAttrSetEx pointer
        var pasExPointer = reader.ReadPointer();
        if (pasExPointer != 0)
        {
            request.PPartialAttrSetEx = ReadPartialAttrVector(reader);
        }

        // PrefixTableDest
        request.PrefixTableDest = ReadPrefixTable(reader);

        return request;
    }

    private static PARTIAL_ATTR_VECTOR ReadPartialAttrVector(NdrReader reader)
    {
        var pav = new PARTIAL_ATTR_VECTOR { CAttrs = reader.ReadUInt32() };

        for (uint i = 0; i < pav.CAttrs; i++)
        {
            pav.RgPartialAttr.Add(new ATTRTYP(reader.ReadUInt32()));
        }

        return pav;
    }

    private static SCHEMA_PREFIX_TABLE ReadPrefixTable(NdrReader reader)
    {
        var table = new SCHEMA_PREFIX_TABLE { PrefixCount = reader.ReadUInt32() };

        var entryPointer = reader.ReadPointer();
        if (entryPointer != 0)
        {
            var count = reader.ReadUInt32();
            for (uint i = 0; i < count && i < table.PrefixCount; i++)
            {
                var entry = new OID_PREFIX_ENTRY
                {
                    NdxValue = reader.ReadUInt32(),
                };

                var prefixLen = reader.ReadUInt32();
                var prefixPointer = reader.ReadPointer();
                if (prefixPointer != 0)
                {
                    entry.Prefix = reader.ReadBytes((int)prefixLen).ToArray();
                }

                table.PPrefixEntry.Add(entry);
            }
        }

        return table;
    }

    private void WriteGetNCChangesReplyV6(NdrWriter writer, DRS_MSG_GETCHGREPLY_V6 reply)
    {
        // uuidDsaObjSrc
        Span<byte> srcGuid = stackalloc byte[16];
        reply.UuidDsaObjSrc.TryWriteBytes(srcGuid);
        writer.WriteBytes(srcGuid);

        // uuidInvocIdSrc
        Span<byte> invocGuid = stackalloc byte[16];
        reply.UuidInvocIdSrc.TryWriteBytes(invocGuid);
        writer.WriteBytes(invocGuid);

        // pNC pointer
        writer.WritePointer(isNull: reply.PNC == null);
        if (reply.PNC != null)
        {
            WriteDsName(writer, reply.PNC);
        }

        // usnvecFrom
        writer.WriteInt64(reply.UsnvecFrom.UsnHighObjUpdate);
        writer.WriteInt64(reply.UsnvecFrom.UsnReserved);
        writer.WriteInt64(reply.UsnvecFrom.UsnHighPropUpdate);

        // usnvecTo
        writer.WriteInt64(reply.UsnvecTo.UsnHighObjUpdate);
        writer.WriteInt64(reply.UsnvecTo.UsnReserved);
        writer.WriteInt64(reply.UsnvecTo.UsnHighPropUpdate);

        // pUpToDateVecSrc pointer
        writer.WritePointer(isNull: reply.PUpToDateVecSrc == null);
        if (reply.PUpToDateVecSrc != null)
        {
            writer.WriteUInt32(reply.PUpToDateVecSrc.DwVersion);
            writer.WriteUInt32(reply.PUpToDateVecSrc.DwReserved);
            writer.WriteUInt32(reply.PUpToDateVecSrc.CNumCursors);

            foreach (var cursor in reply.PUpToDateVecSrc.RgCursors)
            {
                byte[] cGuidArr = new byte[16];
                cursor.UuidDsa.TryWriteBytes(cGuidArr);
                writer.WriteBytes(cGuidArr);
                writer.WriteInt64(cursor.UsnHighPropUpdate);
                writer.WriteInt64(cursor.FtimeLastSyncSuccess);
            }
        }

        // PrefixTableSrc
        WritePrefixTable(writer, reply.PrefixTableSrc);

        // ulExtendedRet
        writer.WriteUInt32(reply.UlExtendedRet);

        // cNumObjects
        writer.WriteUInt32(reply.CNumObjects);

        // cNumBytes
        writer.WriteUInt32(reply.CNumBytes);

        // pObjects pointer
        writer.WritePointer(isNull: reply.PObjects == null);
        if (reply.PObjects != null)
        {
            WriteReplEntInfList(writer, reply.PObjects);
        }

        // fMoreData
        writer.WriteBoolean(reply.FMoreData);

        // cNumNcSizeObjects
        writer.WriteUInt32(reply.CNumNcSizeObjects);

        // cNumNcSizeValues
        writer.WriteUInt32(reply.CNumNcSizeValues);

        // rgValues array pointer
        writer.WritePointer(isNull: reply.RgValues.Count == 0);
        if (reply.RgValues.Count > 0)
        {
            writer.WriteUInt32((uint)reply.RgValues.Count);
            foreach (var val in reply.RgValues)
            {
                WriteReplValInf(writer, val);
            }
        }

        // dwDRSError
        writer.WriteUInt32(reply.DwDRSError);
    }

    private void WriteReplEntInfList(NdrWriter writer, REPLENTINFLIST entry)
    {
        var current = entry;
        while (current != null)
        {
            // pNextEntInf pointer
            writer.WritePointer(isNull: current.PNextEntInf == null);

            // ENTINF
            WriteDsName(writer, current.Entinf.PName);
            writer.WriteUInt32(current.Entinf.UlFlags);
            WriteAttrBlock(writer, current.Entinf.AttrBlock);

            // fIsNCPrefix
            writer.WriteBoolean(current.FIsNCPrefix);

            // pMetaDataExt pointer
            writer.WritePointer(isNull: current.PMetaDataExt == null);
            if (current.PMetaDataExt != null)
            {
                writer.WriteUInt32(current.PMetaDataExt.CNumProps);
                foreach (var meta in current.PMetaDataExt.RgMetaData)
                {
                    writer.WriteUInt32(meta.DwVersion);
                    writer.WriteInt64(meta.TimeChanged);
                    byte[] dsaGuidArr = new byte[16];
                    meta.UuidDsaOriginating.TryWriteBytes(dsaGuidArr);
                    writer.WriteBytes(dsaGuidArr);
                    writer.WriteInt64(meta.UsnOriginating);
                }
            }

            current = current.PNextEntInf;
        }
    }

    private static void WriteAttrBlock(NdrWriter writer, ATTRBLOCK block)
    {
        writer.WriteUInt32(block.AttrCount);

        // Array pointer
        writer.WritePointer(isNull: block.PAttr.Count == 0);

        if (block.PAttr.Count > 0)
        {
            writer.WriteUInt32((uint)block.PAttr.Count);

            foreach (var attr in block.PAttr)
            {
                writer.WriteUInt32(attr.AttrTyp.Value);
                writer.WriteUInt32(attr.AttrVal.ValCount);

                // Values pointer
                writer.WritePointer(isNull: attr.AttrVal.PVal.Count == 0);

                if (attr.AttrVal.PVal.Count > 0)
                {
                    writer.WriteUInt32((uint)attr.AttrVal.PVal.Count);

                    foreach (var val in attr.AttrVal.PVal)
                    {
                        writer.WriteUInt32(val.ValLen);
                        writer.WritePointer(isNull: val.PVal.Length == 0);
                    }

                    // Write value data
                    foreach (var val in attr.AttrVal.PVal)
                    {
                        if (val.PVal.Length > 0)
                        {
                            writer.WriteUInt32((uint)val.PVal.Length);
                            writer.WriteBytes(val.PVal);
                        }
                    }
                }
            }
        }
    }

    private static void WriteReplValInf(NdrWriter writer, REPLVALINF_V3 val)
    {
        WriteDsName(writer, val.PObject);
        writer.WriteUInt32(val.AttrTyp.Value);
        writer.WriteUInt32(val.AttrVal.ValLen);
        writer.WritePointer(isNull: val.AttrVal.PVal.Length == 0);
        if (val.AttrVal.PVal.Length > 0)
        {
            writer.WriteUInt32((uint)val.AttrVal.PVal.Length);
            writer.WriteBytes(val.AttrVal.PVal);
        }
        writer.WriteBoolean(val.FIsPresent);

        // VALUE_META_DATA_EXT
        writer.WriteInt64(val.MetaData.TimeCreated);
        writer.WriteUInt32(val.MetaData.DwVersion);
        writer.WriteInt64(val.MetaData.TimeChanged);
        Span<byte> dsaGuid = stackalloc byte[16];
        val.MetaData.UuidDsaOriginating.TryWriteBytes(dsaGuid);
        writer.WriteBytes(dsaGuid);
        writer.WriteInt64(val.MetaData.UsnOriginating);
    }

    private static void WritePrefixTable(NdrWriter writer, SCHEMA_PREFIX_TABLE table)
    {
        writer.WriteUInt32(table.PrefixCount);
        writer.WritePointer(isNull: table.PPrefixEntry.Count == 0);

        if (table.PPrefixEntry.Count > 0)
        {
            writer.WriteUInt32((uint)table.PPrefixEntry.Count);

            foreach (var entry in table.PPrefixEntry)
            {
                writer.WriteUInt32(entry.NdxValue);
                writer.WriteUInt32((uint)entry.Prefix.Length);
                writer.WritePointer(isNull: entry.Prefix.Length == 0);
            }

            // Write prefix data
            foreach (var entry in table.PPrefixEntry)
            {
                if (entry.Prefix.Length > 0)
                {
                    writer.WriteUInt32((uint)entry.Prefix.Length);
                    writer.WriteBytes(entry.Prefix);
                }
            }
        }
    }

    private static void WriteEntInfArray(NdrWriter writer, List<ENTINF> entries)
    {
        writer.WritePointer(isNull: entries.Count == 0);

        if (entries.Count > 0)
        {
            writer.WriteUInt32((uint)entries.Count);

            foreach (var entry in entries)
            {
                WriteDsName(writer, entry.PName);
                writer.WriteUInt32(entry.UlFlags);
                WriteAttrBlock(writer, entry.AttrBlock);
            }
        }
    }

    private void WriteNeighborsInfo(NdrWriter writer, string objectDn)
    {
        writer.WriteUInt32((uint)DsReplInfoType.DS_REPL_INFO_NEIGHBORS);

        var partners = string.IsNullOrEmpty(objectDn)
            ? _topology.InboundPartners.ToList()
            : _topology.GetInboundPartners(objectDn);

        writer.WriteUInt32((uint)partners.Count);

        foreach (var partner in partners)
        {
            var neighbor = partner.ToNeighborW();

            // Write string pointers
            writer.WritePointer(isNull: neighbor.PszNamingContext == null);
            writer.WritePointer(isNull: neighbor.PszSourceDsaDN == null);
            writer.WritePointer(isNull: neighbor.PszSourceDsaAddress == null);
            writer.WritePointer(isNull: neighbor.PszAsyncIntersiteTransportDN == null);
            writer.WriteUInt32((uint)neighbor.DwReplicaFlags);
            writer.WriteUInt32(neighbor.DwReserved);

            // GUIDs
            byte[] ncGuidArr = new byte[16];
            neighbor.UuidNamingContextObjGuid.TryWriteBytes(ncGuidArr);
            writer.WriteBytes(ncGuidArr);

            byte[] srcGuidArr = new byte[16];
            neighbor.UuidSourceDsaObjGuid.TryWriteBytes(srcGuidArr);
            writer.WriteBytes(srcGuidArr);

            byte[] invocGuidArr = new byte[16];
            neighbor.UuidSourceDsaInvocationID.TryWriteBytes(invocGuidArr);
            writer.WriteBytes(invocGuidArr);

            byte[] transpGuidArr = new byte[16];
            neighbor.UuidAsyncIntersiteTransportObjGuid.TryWriteBytes(transpGuidArr);
            writer.WriteBytes(transpGuidArr);

            writer.WriteInt64(neighbor.UsnLastObjChangeSynced);
            writer.WriteInt64(neighbor.UsnAttributeFilter);
            writer.WriteInt64(neighbor.FtimeLastSyncSuccess);
            writer.WriteInt64(neighbor.FtimeLastSyncAttempt);
            writer.WriteUInt32(neighbor.DwLastSyncResult);
            writer.WriteUInt32(neighbor.CNumConsecutiveSyncFailures);

            // Write deferred strings
            if (neighbor.PszNamingContext != null)
                writer.WriteDeferredConformantVaryingString(neighbor.PszNamingContext);
            if (neighbor.PszSourceDsaDN != null)
                writer.WriteDeferredConformantVaryingString(neighbor.PszSourceDsaDN);
            if (neighbor.PszSourceDsaAddress != null)
                writer.WriteDeferredConformantVaryingString(neighbor.PszSourceDsaAddress);
            if (neighbor.PszAsyncIntersiteTransportDN != null)
                writer.WriteDeferredConformantVaryingString(neighbor.PszAsyncIntersiteTransportDN);
        }
    }

    private void WriteCursorsInfo(NdrWriter writer, string ncDn)
    {
        writer.WriteUInt32((uint)DsReplInfoType.DS_REPL_INFO_CURSORS_3_FOR_NC);

        // Build cursors from inbound partners
        var partners = string.IsNullOrEmpty(ncDn)
            ? _topology.InboundPartners.ToList()
            : _topology.GetInboundPartners(ncDn);

        writer.WriteUInt32((uint)partners.Count);

        foreach (var partner in partners)
        {
            byte[] guidArr = new byte[16];
            partner.InvocationId.TryWriteBytes(guidArr);
            writer.WriteBytes(guidArr);

            writer.WriteInt64(partner.LastUsnSynced);
            writer.WriteInt64(partner.LastSyncSuccess.ToFileTime());

            writer.WritePointer(isNull: partner.NtdsSettingsDn == null);
            if (partner.NtdsSettingsDn != null)
                writer.WriteDeferredConformantVaryingString(partner.NtdsSettingsDn);
        }
    }

    private static void WriteKccFailureInfo(NdrWriter writer)
    {
        writer.WriteUInt32((uint)DsReplInfoType.DS_REPL_INFO_KCC_DSA_CONNECT_FAILURES);
        writer.WriteUInt32(0); // No failures to report
    }

    #endregion
}
