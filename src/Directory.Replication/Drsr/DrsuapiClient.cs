using Directory.Core.Interfaces;
using Directory.Rpc.Client;
using Directory.Rpc.Ndr;
using Directory.Rpc.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Directory.Replication.Drsr;

/// <summary>
/// High-level DRSUAPI RPC client that wraps <see cref="RpcTcpClient"/> to provide
/// typed methods for the Directory Replication Service Remote Protocol (MS-DRSR).
/// Uses RPC over TCP (ncacn_ip_tcp) with NTLM authentication to speak to real
/// Windows Domain Controllers.
///
/// DRSUAPI interface UUID: e3514235-4b06-11d1-ab04-00c04fc2dcd2, version 4.0
/// </summary>
public class DrsuapiClient : IAsyncDisposable
{
    /// <summary>
    /// DRSUAPI interface UUID per [MS-DRSR] section 2.
    /// </summary>
    public static readonly Guid DrsuapiInterfaceId =
        new("e3514235-4b06-11d1-ab04-00c04fc2dcd2");

    private const ushort DrsuapiMajorVersion = 4;
    private const ushort DrsuapiMinorVersion = 0;

    private RpcTcpClient _rpcClient;
    private readonly ILogger _logger;

    private byte[] _bindHandle = new byte[20];
    private Guid _bindHandleGuid = Guid.Empty;
    private DRS_EXTENSIONS_INT _serverExtensions;
    private PrefixTableResolver _lastPrefixTableResolver;

    /// <summary>
    /// Whether the RPC connection is established and bound.
    /// </summary>
    public bool IsConnected => _rpcClient?.IsConnected == true;

    /// <summary>
    /// Whether DRSBind has completed and a context handle is available.
    /// </summary>
    public bool IsBound => _bindHandleGuid != Guid.Empty;

    /// <summary>
    /// The DRS extensions advertised by the remote DC, available after <see cref="DrsBindAsync"/>.
    /// </summary>
    public DRS_EXTENSIONS_INT ServerExtensions => _serverExtensions;

    /// <summary>
    /// The NTLM session key from the underlying RPC connection, used for decrypting
    /// secret attributes during replication. Available after <see cref="ConnectAsync"/>.
    /// </summary>
    public byte[] SessionKey => _rpcClient?.SessionKey ?? [];

    /// <summary>
    /// The <see cref="PrefixTableResolver"/> built from the most recent GetNCChanges response.
    /// Can be used to resolve ATTRTYPs from replicated entries to attribute names.
    /// </summary>
    public PrefixTableResolver LastPrefixTableResolver => _lastPrefixTableResolver;

    /// <summary>
    /// Creates a new DRSUAPI client with the specified logger.
    /// </summary>
    /// <param name="logger">Logger instance for diagnostics.</param>
    public DrsuapiClient(ILogger logger)
    {
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Creates a new DRSUAPI client with no logging.
    /// </summary>
    public DrsuapiClient() : this(NullLogger.Instance)
    {
    }

    /// <summary>
    /// Connects to a remote DC using EPM to discover the DRSUAPI dynamic port,
    /// then performs an authenticated RPC bind with Kerberos.
    /// </summary>
    /// <param name="hostname">DNS hostname or IP address of the target DC.</param>
    /// <param name="username">Username for authentication.</param>
    /// <param name="domain">Domain name for authentication.</param>
    /// <param name="password">Password for authentication.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task ConnectAsync(
        string hostname, string username, string domain, string password,
        CancellationToken ct = default)
    {
        _logger.LogInformation("DRSUAPI: resolving dynamic port via EPM on {Hostname}:135", hostname);

        // Step 1: Resolve the dynamic DRSUAPI port via EPM on port 135
        int drsuapiPort = await EpmClient.ResolvePortAsync(
            hostname, DrsuapiInterfaceId, DrsuapiMajorVersion, _logger, ct);

        _logger.LogInformation("DRSUAPI: resolved to TCP port {Port} on {Hostname}", drsuapiPort, hostname);

        // Step 2: Connect to the DRSUAPI port
        _rpcClient = new RpcTcpClient(_logger);
        await _rpcClient.ConnectAsync(hostname, drsuapiPort, ct);

        // Step 3: Authenticated bind to the DRSUAPI interface
        await _rpcClient.BindAsync(
            DrsuapiInterfaceId, DrsuapiMajorVersion, DrsuapiMinorVersion,
            username, domain, password, ct);

        _logger.LogInformation("DRSUAPI: authenticated RPC bind complete on {Hostname}:{Port}", hostname, drsuapiPort);
    }

    /// <summary>
    /// Performs IDL_DRSBind (opnum 0) to establish a replication context handle
    /// and exchange DRS extension capabilities with the remote DC.
    /// </summary>
    /// <param name="clientDsaGuid">The DSA object GUID of this (client) DC.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The DSA GUID of the remote DC (from the server extensions).</returns>
    public async Task<Guid> DrsBindAsync(Guid clientDsaGuid, CancellationToken ct = default)
    {
        EnsureConnected();

        var writer = new NdrWriter();

        // puuidClientDsa — pointer + GUID
        writer.WritePointer(isNull: false);
        Span<byte> clientGuidBytes = stackalloc byte[16];
        clientDsaGuid.TryWriteBytes(clientGuidBytes);
        writer.WriteBytes(clientGuidBytes);

        // pextClient — pointer to DRS_EXTENSIONS_INT
        writer.WritePointer(isNull: false);

        // cb (size of extension data): 48 bytes for full DRS_EXTENSIONS_INT
        uint extSize = 48;
        writer.WriteUInt32(extSize);

        // dwFlags
        var clientFlags = DrsExtFlags.DRS_EXT_BASE
                        | DrsExtFlags.DRS_EXT_GETCHGREQ_V8
                        | DrsExtFlags.DRS_EXT_GETCHGREPLY_V6
                        | DrsExtFlags.DRS_EXT_LINKED_VALUE_REPLICATION
                        | DrsExtFlags.DRS_EXT_STRONG_ENCRYPTION
                        | DrsExtFlags.DRS_EXT_GET_REPL_INFO
                        | DrsExtFlags.DRS_EXT_POST_BETA3
                        | DrsExtFlags.DRS_EXT_REMOVEAPI
                        | DrsExtFlags.DRS_EXT_ADDENTRY_V2;
        writer.WriteUInt32((uint)clientFlags);

        // SiteObjGuid (16 bytes, zeros)
        writer.WriteBytes(new byte[16]);

        // Pid
        writer.WriteInt32(Environment.ProcessId);

        // DwReplEpoch
        writer.WriteUInt32(0);

        // DwFlagsExt
        writer.WriteUInt32(0);

        // ConfigObjGuid (16 bytes, zeros)
        writer.WriteBytes(new byte[16]);

        byte[] requestStub = writer.ToArray();

        _logger.LogDebug("DRSUAPI: sending DRSBind (opnum 0), stub={Length} bytes", requestStub.Length);

        byte[] responseStub = await _rpcClient.RequestAsync(0, requestStub, ct);

        // Parse response: the server WRITES what we now READ
        var reader = new NdrReader(responseStub);

        // ppextServer — pointer to server extensions
        var extPointer = reader.ReadPointer();
        _serverExtensions = new DRS_EXTENSIONS_INT();
        if (extPointer != 0)
        {
            var cb = reader.ReadUInt32();
            if (cb >= 4) _serverExtensions.DwFlags = (DrsExtFlags)reader.ReadUInt32();
            if (cb >= 20)
            {
                var siteBytes = reader.ReadBytes(16);
                _serverExtensions.SiteObjGuid = new Guid(siteBytes.Span);
            }
            if (cb >= 24) _serverExtensions.Pid = reader.ReadInt32();
            if (cb >= 28) _serverExtensions.DwReplEpoch = reader.ReadUInt32();
            if (cb >= 32) _serverExtensions.DwFlagsExt = (DrsExtMoreFlags)reader.ReadUInt32();
            if (cb >= 48)
            {
                var cfgBytes = reader.ReadBytes(16);
                _serverExtensions.ConfigObjGuid = new Guid(cfgBytes.Span);
            }
        }

        // phDrs — context handle (20 bytes: 4 attributes + 16 GUID)
        var (handleAttr, handleGuid) = reader.ReadContextHandle();
        _bindHandleGuid = handleGuid;

        // Build the 20-byte handle for subsequent calls
        _bindHandle = new byte[20];
        BitConverter.TryWriteBytes(_bindHandle.AsSpan(0, 4), handleAttr);
        handleGuid.TryWriteBytes(_bindHandle.AsSpan(4, 16));

        // Return code
        var returnCode = reader.ReadUInt32();
        if (returnCode != 0)
        {
            throw new DrsReplicationException(
                $"DRSBind failed with return code 0x{returnCode:X8}.");
        }

        _logger.LogInformation(
            "DRSUAPI: DRSBind succeeded, handle={Handle}, serverFlags={Flags}",
            _bindHandleGuid, _serverExtensions.DwFlags);

        return _bindHandleGuid;
    }

    /// <summary>
    /// Performs IDL_DRSUnbind (opnum 1) to release the replication context handle.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task DrsUnbindAsync(CancellationToken ct = default)
    {
        if (_rpcClient == null || !_rpcClient.IsConnected || _bindHandleGuid == Guid.Empty)
            return;

        var writer = new NdrWriter();

        // Context handle (20 bytes)
        writer.WriteContextHandle(0, _bindHandleGuid);

        byte[] requestStub = writer.ToArray();
        byte[] responseStub = await _rpcClient.RequestAsync(1, requestStub, ct);

        var reader = new NdrReader(responseStub);

        // Returned zeroed handle
        reader.ReadContextHandle();

        // Return code
        var returnCode = reader.ReadUInt32();

        _bindHandleGuid = Guid.Empty;
        _bindHandle = new byte[20];

        _logger.LogDebug("DRSUAPI: DRSUnbind completed (rc=0x{ReturnCode:X8})", returnCode);
    }

    /// <summary>
    /// Performs IDL_DRSGetNCChanges (opnum 3) to pull replication changes for a naming context.
    /// Sends a V8 request and reads a V6 response.
    /// </summary>
    /// <param name="request">The GetNCChanges request parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The V6 reply containing replicated objects, metadata, and continuation state.</returns>
    public async Task<DRS_MSG_GETCHGREPLY_V6> DrsGetNCChangesAsync(
        DRS_MSG_GETCHGREQ_V8 request, CancellationToken ct = default)
    {
        EnsureBound();

        var writer = new NdrWriter();

        // Context handle
        writer.WriteContextHandle(0, _bindHandleGuid);

        // dwInVersion = 8
        writer.WriteUInt32(8);

        // Write DRS_MSG_GETCHGREQ_V8
        WriteGetNCChangesRequestV8(writer, request);

        byte[] requestStub = writer.ToArray();

        _logger.LogDebug("DRSUAPI: sending DRSGetNCChanges (opnum 3), stub={Length} bytes", requestStub.Length);

        byte[] responseStub = await _rpcClient.RequestAsync(3, requestStub, ct);

        // Parse response
        var reader = new NdrReader(responseStub);

        // pdwOutVersion
        var outVersion = reader.ReadUInt32();

        if (outVersion != 6)
        {
            _logger.LogWarning("DRSUAPI: DRSGetNCChanges returned version {Version}, expected 6", outVersion);
        }

        // Read DRS_MSG_GETCHGREPLY_V6
        var reply = ReadGetNCChangesReplyV6(reader);

        // Return code
        var returnCode = reader.ReadUInt32();
        reply.DwDRSError = returnCode;

        // Build a PrefixTableResolver from the response prefix table for ATTRTYP resolution
        _lastPrefixTableResolver = PrefixTableResolver.FromWireTable(reply.PrefixTableSrc);

        _logger.LogDebug(
            "DRSUAPI: DRSGetNCChanges reply: {Objects} objects, {Bytes} bytes, moreData={More}",
            reply.CNumObjects, reply.CNumBytes, reply.FMoreData);

        // Decrypt secret attributes if we have a session key
        var sessionKey = SessionKey;
        if (sessionKey.Length > 0)
        {
            DecryptSecretAttributes(reply, _lastPrefixTableResolver, sessionKey);
        }

        return reply;
    }

    /// <summary>
    /// Performs IDL_DRSUpdateRefs (opnum 4) to add or remove change notification references.
    /// </summary>
    /// <param name="request">The UpdateRefs request parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task DrsUpdateRefsAsync(DRS_MSG_UPDREFS_V1 request, CancellationToken ct = default)
    {
        EnsureBound();

        var writer = new NdrWriter();

        // Context handle
        writer.WriteContextHandle(0, _bindHandleGuid);

        // dwVersion = 1
        writer.WriteUInt32(1);

        // Write DRS_MSG_UPDREFS_V1
        // pNC pointer + data
        writer.WritePointer(isNull: request.PNC == null);
        if (request.PNC != null)
        {
            WriteDsName(writer, request.PNC);
        }

        // pszDsaDest pointer + string
        writer.WritePointer(isNull: request.PszDsaDest == null);
        if (request.PszDsaDest != null)
        {
            writer.WriteDeferredConformantVaryingString(request.PszDsaDest);
        }

        // uuidDsaObjDest
        Span<byte> destGuid = stackalloc byte[16];
        request.UuidDsaObjDest.TryWriteBytes(destGuid);
        writer.WriteBytes(destGuid);

        // ulOptions
        writer.WriteUInt32((uint)request.UlOptions);

        byte[] requestStub = writer.ToArray();

        _logger.LogDebug("DRSUAPI: sending DRSUpdateRefs (opnum 4)");

        byte[] responseStub = await _rpcClient.RequestAsync(4, requestStub, ct);

        var reader = new NdrReader(responseStub);
        var returnCode = reader.ReadUInt32();

        if (returnCode != 0)
        {
            throw new DrsReplicationException(
                $"DRSUpdateRefs failed with return code 0x{returnCode:X8}.");
        }

        _logger.LogDebug("DRSUAPI: DRSUpdateRefs succeeded");
    }

    /// <summary>
    /// Performs IDL_DRSCrackNames (opnum 12) to translate object names between formats.
    /// </summary>
    /// <param name="request">The CrackNames request parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The crack names reply containing resolved name items.</returns>
    public async Task<DRS_MSG_CRACKREPLY_V1> DrsCrackNamesAsync(
        DRS_MSG_CRACKREQ_V1 request, CancellationToken ct = default)
    {
        EnsureBound();

        var writer = new NdrWriter();

        // Context handle
        writer.WriteContextHandle(0, _bindHandleGuid);

        // dwVersion = 1
        writer.WriteUInt32(1);

        // Write DRS_MSG_CRACKREQ_V1
        writer.WriteUInt32(request.CodePage);
        writer.WriteUInt32(request.LocaleId);
        writer.WriteUInt32((uint)request.DwFlags);
        writer.WriteUInt32((uint)request.FormatOffered);
        writer.WriteUInt32((uint)request.FormatDesired);
        writer.WriteUInt32(request.CNames);

        // rpNames — pointer to array of string pointers
        writer.WritePointer(isNull: request.RpNames.Count == 0);
        if (request.RpNames.Count > 0)
        {
            writer.WriteUInt32((uint)request.RpNames.Count);

            // Write string pointers first
            foreach (var name in request.RpNames)
            {
                writer.WritePointer(isNull: name == null);
            }

            // Write deferred string data
            foreach (var name in request.RpNames)
            {
                if (name != null)
                {
                    writer.WriteDeferredConformantVaryingString(name);
                }
            }
        }

        byte[] requestStub = writer.ToArray();

        _logger.LogDebug("DRSUAPI: sending DRSCrackNames (opnum 12), {Count} names", request.CNames);

        byte[] responseStub = await _rpcClient.RequestAsync(12, requestStub, ct);

        // Parse response
        var reader = new NdrReader(responseStub);

        // pdwOutVersion
        var outVersion = reader.ReadUInt32();

        // DRS_MSG_CRACKREPLY_V1 -> DS_NAME_RESULTW pointer
        var reply = new DRS_MSG_CRACKREPLY_V1();
        var resultPointer = reader.ReadPointer();

        if (resultPointer != 0)
        {
            var result = new DS_NAME_RESULTW();
            result.CItems = reader.ReadUInt32();

            var itemsPointer = reader.ReadPointer();
            if (itemsPointer != 0)
            {
                var itemCount = reader.ReadUInt32();

                // Read item headers (status + 2 pointers each)
                var items = new List<(DsNameStatus Status, uint DomainPtr, uint NamePtr)>();
                for (uint i = 0; i < itemCount && i < result.CItems; i++)
                {
                    var status = (DsNameStatus)reader.ReadUInt32();
                    var domainPtr = reader.ReadPointer();
                    var namePtr = reader.ReadPointer();
                    items.Add((status, domainPtr, namePtr));
                }

                // Read deferred string data
                foreach (var (status, domainPtr, namePtr) in items)
                {
                    var item = new DS_NAME_RESULT_ITEMW { Status = status };

                    if (domainPtr != 0)
                    {
                        item.PDomain = reader.ReadConformantVaryingString();
                    }

                    if (namePtr != 0)
                    {
                        item.PName = reader.ReadConformantVaryingString();
                    }

                    result.RItems.Add(item);
                }
            }

            reply.PResult = result;
        }

        // Return code
        var returnCode = reader.ReadUInt32();

        if (returnCode != 0)
        {
            _logger.LogWarning("DRSUAPI: DRSCrackNames returned 0x{ReturnCode:X8}", returnCode);
        }

        _logger.LogDebug("DRSUAPI: DRSCrackNames returned {Count} items",
            reply.PResult?.CItems ?? 0);

        return reply;
    }

    /// <summary>
    /// Performs a full replication of a naming context by repeatedly calling
    /// <see cref="DrsGetNCChangesAsync"/> until no more data is available,
    /// applying each batch to the local store via the replication engine.
    /// </summary>
    /// <param name="ncDn">Distinguished name of the naming context to replicate.</param>
    /// <param name="clientDsaGuid">DSA object GUID of this (client) DC.</param>
    /// <param name="engine">Replication engine for applying incoming changes.</param>
    /// <param name="tenantId">Tenant identifier for the local directory store.</param>
    /// <param name="prefixTable">Schema prefix table for the destination, sent in each request.</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Total number of objects applied to the local store.</returns>
    public async Task<int> ReplicateFullNcAsync(
        string ncDn,
        Guid clientDsaGuid,
        ReplicationEngine engine,
        string tenantId,
        SCHEMA_PREFIX_TABLE prefixTable = null,
        IProgress<ReplicationProgress> progress = null,
        CancellationToken ct = default)
    {
        EnsureBound();

        _logger.LogInformation("DRSUAPI: starting full replication of NC={NC}", ncDn);

        int totalApplied = 0;
        int totalReceived = 0;
        long totalBytes = 0;
        bool moreData;

        var usnFrom = new USN_VECTOR();

        var ncDsName = DSNAME.FromDn(ncDn);

        progress?.Report(new ReplicationProgress
        {
            Phase = "Starting",
            NamingContext = ncDn,
            Message = $"Beginning full replication of {ncDn}",
        });

        do
        {
            var request = new DRS_MSG_GETCHGREQ_V8
            {
                UuidDsaObjDest = clientDsaGuid,
                UuidInvocIdSrc = _bindHandleGuid,
                PNC = ncDsName,
                UsnvecFrom = usnFrom,
                UlFlags = DrsGetNcChangesFlags.DRS_INIT_SYNC
                        | DrsGetNcChangesFlags.DRS_WRIT_REP
                        | DrsGetNcChangesFlags.DRS_GET_ANC,
                CMaxObjects = 1000,
                CMaxBytes = 10_000_000,
                PrefixTableDest = prefixTable ?? new SCHEMA_PREFIX_TABLE(),
            };

            var reply = await DrsGetNCChangesAsync(request, ct);

            totalReceived += (int)reply.CNumObjects;
            totalBytes += reply.CNumBytes;

            progress?.Report(new ReplicationProgress
            {
                Phase = "Pulling changes",
                NamingContext = ncDn,
                ObjectsProcessed = totalReceived,
                BytesTransferred = totalBytes,
                Message = $"Received {reply.CNumObjects} objects",
            });

            var applied = await engine.ApplyIncomingChangesAsync(reply, tenantId, ct);
            totalApplied += applied;

            // Update watermark for next request
            usnFrom = reply.UsnvecTo;
            moreData = reply.FMoreData;

            _logger.LogInformation(
                "DRSUAPI: applied {Applied} objects (total: {Total}), moreData={More}",
                applied, totalApplied, moreData);

        } while (moreData);

        progress?.Report(new ReplicationProgress
        {
            Phase = "Complete",
            NamingContext = ncDn,
            ObjectsProcessed = totalReceived,
            BytesTransferred = totalBytes,
            Message = $"Full replication complete: {totalApplied} objects applied",
        });

        _logger.LogInformation(
            "DRSUAPI: full replication of NC={NC} complete, {Applied} objects, {Bytes} bytes",
            ncDn, totalApplied, totalBytes);

        return totalApplied;
    }

    #region Decompression and Decryption Helpers

    /// <summary>
    /// Decompresses XPRESS-compressed response data from GetNCChanges.
    /// Call this when the response indicates compression was used (e.g., the server
    /// negotiated DRS_EXT_GETCHG_DEFLATE or DRS_EXT_W2K3_DEFLATE).
    /// </summary>
    /// <param name="compressedStub">The compressed NDR response stub data.</param>
    /// <param name="uncompressedSize">The expected uncompressed size (from the response header).</param>
    /// <returns>The decompressed NDR response stub data.</returns>
    public byte[] DecompressResponseStub(byte[] compressedStub, int uncompressedSize)
    {
        _logger.LogDebug(
            "DRSUAPI: decompressing response stub: {CompressedSize} bytes -> {UncompressedSize} bytes",
            compressedStub.Length, uncompressedSize);

        return XpressDecompressor.Decompress(compressedStub, uncompressedSize);
    }

    /// <summary>
    /// Iterates over all replicated objects in a GetNCChanges reply and decrypts
    /// any secret attributes (unicodePwd, supplementalCredentials, etc.) using the
    /// RPC session key. Per MS-DRSR section 4.1.10.6.12.
    /// </summary>
    private void DecryptSecretAttributes(
        DRS_MSG_GETCHGREPLY_V6 reply,
        PrefixTableResolver prefixTable,
        byte[] sessionKey)
    {
        var current = reply.PObjects;
        while (current != null)
        {
            foreach (var attr in current.Entinf.AttrBlock.PAttr)
            {
                var attrName = prefixTable.ResolveToName(attr.AttrTyp.Value);
                if (attrName == null || !DrsSecretDecryptor.IsSecretAttribute(attrName))
                {
                    continue;
                }

                // Decrypt each value of this secret attribute
                for (int i = 0; i < attr.AttrVal.PVal.Count; i++)
                {
                    var val = attr.AttrVal.PVal[i];
                    if (val.PVal.Length > 0 && DrsSecretDecryptor.NeedsDecryption(val.PVal))
                    {
                        try
                        {
                            var decrypted = DrsSecretDecryptor.Decrypt(val.PVal, sessionKey);
                            val.PVal = decrypted;
                            val.ValLen = (uint)decrypted.Length;

                            _logger.LogDebug(
                                "DRSUAPI: decrypted secret attribute {AttrName} on {DN}",
                                attrName, current.Entinf.PName.StringName);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex,
                                "DRSUAPI: failed to decrypt secret attribute {AttrName} on {DN}",
                                attrName, current.Entinf.PName.StringName);
                        }
                    }
                }
            }

            current = current.PNextEntInf;
        }
    }

    /// <summary>
    /// Parses linked value changes from the GetNCChanges reply using the response's
    /// prefix table, returning structured <see cref="LinkedValueChange"/> objects.
    /// </summary>
    /// <param name="reply">The GetNCChanges reply containing linked values.</param>
    /// <param name="prefixTable">
    /// Optional prefix table resolver. If null, uses the <see cref="LastPrefixTableResolver"/>.
    /// </param>
    /// <returns>Parsed linked value changes, or an empty list if there are none.</returns>
    public List<LinkedValueChange> ParseLinkedValueChanges(
        DRS_MSG_GETCHGREPLY_V6 reply,
        PrefixTableResolver prefixTable = null)
    {
        if (reply.RgValues.Count == 0)
            return [];

        var resolver = prefixTable ?? _lastPrefixTableResolver;
        if (resolver == null)
        {
            _logger.LogWarning("DRSUAPI: cannot parse linked values without a prefix table resolver");
            return [];
        }

        return LinkedValueParser.ParseLinkedValues(reply.RgValues, resolver);
    }

    #endregion

    #region NDR Write Helpers (client WRITES what server READS)

    /// <summary>
    /// Writes a DRS_MSG_GETCHGREQ_V8 structure in NDR format.
    /// </summary>
    private static void WriteGetNCChangesRequestV8(NdrWriter writer, DRS_MSG_GETCHGREQ_V8 request)
    {
        // uuidDsaObjDest (16 bytes)
        Span<byte> destGuid = stackalloc byte[16];
        request.UuidDsaObjDest.TryWriteBytes(destGuid);
        writer.WriteBytes(destGuid);

        // uuidInvocIdSrc (16 bytes)
        Span<byte> invocGuid = stackalloc byte[16];
        request.UuidInvocIdSrc.TryWriteBytes(invocGuid);
        writer.WriteBytes(invocGuid);

        // pNC pointer + DSNAME
        writer.WritePointer(isNull: request.PNC == null);
        if (request.PNC != null)
        {
            WriteDsName(writer, request.PNC);
        }

        // USN_VECTOR usnvecFrom
        writer.WriteInt64(request.UsnvecFrom.UsnHighObjUpdate);
        writer.WriteInt64(request.UsnvecFrom.UsnReserved);
        writer.WriteInt64(request.UsnvecFrom.UsnHighPropUpdate);

        // pUpToDateVecDest pointer
        writer.WritePointer(isNull: request.PUpToDateVecDest == null);
        if (request.PUpToDateVecDest != null)
        {
            var utd = request.PUpToDateVecDest;
            writer.WriteUInt32(utd.DwVersion);
            writer.WriteUInt32(utd.DwReserved);
            writer.WriteUInt32(utd.CNumCursors);

            foreach (var cursor in utd.RgCursors)
            {
                byte[] cGuidArr = new byte[16];
                cursor.UuidDsa.TryWriteBytes(cGuidArr);
                writer.WriteBytes(cGuidArr);
                writer.WriteInt64(cursor.UsnHighPropUpdate);
                writer.WriteInt64(cursor.FtimeLastSyncSuccess);
            }
        }

        // ulFlags
        writer.WriteUInt32((uint)request.UlFlags);

        // cMaxObjects
        writer.WriteUInt32(request.CMaxObjects);

        // cMaxBytes
        writer.WriteUInt32(request.CMaxBytes);

        // ulExtendedOp
        writer.WriteUInt32(request.UlExtendedOp);

        // liFsmoInfo
        writer.WriteUInt64(request.LiFsmoInfo);

        // pPartialAttrSet pointer
        writer.WritePointer(isNull: request.PPartialAttrSet == null);
        if (request.PPartialAttrSet != null)
        {
            WritePartialAttrVector(writer, request.PPartialAttrSet);
        }

        // pPartialAttrSetEx pointer
        writer.WritePointer(isNull: request.PPartialAttrSetEx == null);
        if (request.PPartialAttrSetEx != null)
        {
            WritePartialAttrVector(writer, request.PPartialAttrSetEx);
        }

        // PrefixTableDest
        WritePrefixTable(writer, request.PrefixTableDest);
    }

    /// <summary>
    /// Writes a PARTIAL_ATTR_VECTOR structure in NDR format.
    /// </summary>
    private static void WritePartialAttrVector(NdrWriter writer, PARTIAL_ATTR_VECTOR pav)
    {
        writer.WriteUInt32(pav.CAttrs);
        foreach (var attr in pav.RgPartialAttr)
        {
            writer.WriteUInt32(attr.Value);
        }
    }

    /// <summary>
    /// Writes a SCHEMA_PREFIX_TABLE structure in NDR format.
    /// Mirrors the server-side ReadPrefixTable with deferred pointer data.
    /// </summary>
    private static void WritePrefixTable(NdrWriter writer, SCHEMA_PREFIX_TABLE table)
    {
        writer.WriteUInt32(table.PrefixCount);
        writer.WritePointer(isNull: table.PPrefixEntry.Count == 0);

        if (table.PPrefixEntry.Count > 0)
        {
            writer.WriteUInt32((uint)table.PPrefixEntry.Count);

            // Write entry headers: NdxValue, prefix length, pointer
            foreach (var entry in table.PPrefixEntry)
            {
                writer.WriteUInt32(entry.NdxValue);
                writer.WriteUInt32((uint)entry.Prefix.Length);
                writer.WritePointer(isNull: entry.Prefix.Length == 0);
            }

            // Write deferred prefix data
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

    /// <summary>
    /// Writes a DSNAME structure in NDR format.
    /// </summary>
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

    #endregion

    #region NDR Read Helpers (client READS what server WRITES)

    /// <summary>
    /// Reads a DRS_MSG_GETCHGREPLY_V6 structure from NDR-encoded response data.
    /// </summary>
    private DRS_MSG_GETCHGREPLY_V6 ReadGetNCChangesReplyV6(NdrReader reader)
    {
        var reply = new DRS_MSG_GETCHGREPLY_V6();

        // uuidDsaObjSrc (16 bytes)
        var srcGuid = reader.ReadBytes(16);
        reply.UuidDsaObjSrc = new Guid(srcGuid.Span);

        // uuidInvocIdSrc (16 bytes)
        var invocGuid = reader.ReadBytes(16);
        reply.UuidInvocIdSrc = new Guid(invocGuid.Span);

        // pNC pointer
        var ncPointer = reader.ReadPointer();
        if (ncPointer != 0)
        {
            reply.PNC = ReadDsName(reader);
        }

        // usnvecFrom
        reply.UsnvecFrom = new USN_VECTOR
        {
            UsnHighObjUpdate = reader.ReadInt64(),
            UsnReserved = reader.ReadInt64(),
            UsnHighPropUpdate = reader.ReadInt64(),
        };

        // usnvecTo
        reply.UsnvecTo = new USN_VECTOR
        {
            UsnHighObjUpdate = reader.ReadInt64(),
            UsnReserved = reader.ReadInt64(),
            UsnHighPropUpdate = reader.ReadInt64(),
        };

        // pUpToDateVecSrc pointer
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
                var cGuid = reader.ReadBytes(16);
                utd.RgCursors.Add(new UPTODATE_CURSOR_V2
                {
                    UuidDsa = new Guid(cGuid.Span),
                    UsnHighPropUpdate = reader.ReadInt64(),
                    FtimeLastSyncSuccess = reader.ReadInt64(),
                });
            }

            reply.PUpToDateVecSrc = utd;
        }

        // PrefixTableSrc
        reply.PrefixTableSrc = ReadPrefixTable(reader);

        // ulExtendedRet
        reply.UlExtendedRet = reader.ReadUInt32();

        // cNumObjects
        reply.CNumObjects = reader.ReadUInt32();

        // cNumBytes
        reply.CNumBytes = reader.ReadUInt32();

        // pObjects pointer
        var objectsPointer = reader.ReadPointer();
        if (objectsPointer != 0)
        {
            reply.PObjects = ReadReplEntInfList(reader);
        }

        // fMoreData
        reply.FMoreData = reader.ReadBoolean();

        // cNumNcSizeObjects
        reply.CNumNcSizeObjects = reader.ReadUInt32();

        // cNumNcSizeValues
        reply.CNumNcSizeValues = reader.ReadUInt32();

        // rgValues pointer
        var valuesPointer = reader.ReadPointer();
        if (valuesPointer != 0)
        {
            var valueCount = reader.ReadUInt32();
            for (uint i = 0; i < valueCount; i++)
            {
                reply.RgValues.Add(ReadReplValInf(reader));
            }
        }

        // dwDRSError (read by caller, not here — it follows in the response)

        return reply;
    }

    /// <summary>
    /// Reads the REPLENTINFLIST linked list from NDR-encoded data.
    /// The server writes: pNextEntInf pointer, ENTINF, fIsNCPrefix, pMetaDataExt pointer + data,
    /// then recurses if pNextEntInf was non-null.
    /// </summary>
    private REPLENTINFLIST ReadReplEntInfList(NdrReader reader)
    {
        REPLENTINFLIST first = null;
        REPLENTINFLIST last = null;

        bool hasMore = true;
        while (hasMore)
        {
            var entry = new REPLENTINFLIST();

            // pNextEntInf pointer
            var nextPointer = reader.ReadPointer();

            // ENTINF
            entry.Entinf = ReadEntInf(reader);

            // fIsNCPrefix
            entry.FIsNCPrefix = reader.ReadBoolean();

            // pMetaDataExt pointer
            var metaPointer = reader.ReadPointer();
            if (metaPointer != 0)
            {
                var metaVector = new PROPERTY_META_DATA_EXT_VECTOR
                {
                    CNumProps = reader.ReadUInt32(),
                };

                for (uint i = 0; i < metaVector.CNumProps; i++)
                {
                    metaVector.RgMetaData.Add(new PROPERTY_META_DATA_EXT
                    {
                        DwVersion = reader.ReadUInt32(),
                        TimeChanged = reader.ReadInt64(),
                        UuidDsaOriginating = new Guid(reader.ReadBytes(16).Span),
                        UsnOriginating = reader.ReadInt64(),
                    });
                }

                entry.PMetaDataExt = metaVector;
            }

            // Link into the list
            if (first == null)
            {
                first = entry;
                last = entry;
            }
            else
            {
                last.PNextEntInf = entry;
                last = entry;
            }

            hasMore = nextPointer != 0;
        }

        return first;
    }

    /// <summary>
    /// Reads an ENTINF structure: DSNAME + ulFlags + ATTRBLOCK.
    /// </summary>
    private static ENTINF ReadEntInf(NdrReader reader)
    {
        return new ENTINF
        {
            PName = ReadDsName(reader),
            UlFlags = reader.ReadUInt32(),
            AttrBlock = ReadAttrBlock(reader),
        };
    }

    /// <summary>
    /// Reads an ATTRBLOCK with its nested attribute values.
    /// Mirrors the server-side WriteAttrBlock with deferred pointer data.
    /// </summary>
    private static ATTRBLOCK ReadAttrBlock(NdrReader reader)
    {
        var block = new ATTRBLOCK();
        block.AttrCount = reader.ReadUInt32();

        // Array pointer
        var arrayPointer = reader.ReadPointer();
        if (arrayPointer != 0)
        {
            var count = reader.ReadUInt32();

            // Read attr headers: attrTyp, valCount, values pointer
            var attrs = new List<(ATTRTYP Type, uint ValCount, uint ValPointer)>();
            for (uint i = 0; i < count; i++)
            {
                var attrTyp = new ATTRTYP(reader.ReadUInt32());
                var valCount = reader.ReadUInt32();
                var valPointer = reader.ReadPointer();
                attrs.Add((attrTyp, valCount, valPointer));
            }

            // For each attr with values, read the value headers then data
            foreach (var (attrTyp, valCount, valPointer) in attrs)
            {
                var attr = new ATTR
                {
                    AttrTyp = attrTyp,
                    AttrVal = new ATTRVALBLOCK { ValCount = valCount },
                };

                if (valPointer != 0)
                {
                    var innerCount = reader.ReadUInt32();

                    // Read value headers: valLen + data pointer
                    var valHeaders = new List<(uint ValLen, uint DataPointer)>();
                    for (uint j = 0; j < innerCount; j++)
                    {
                        var valLen = reader.ReadUInt32();
                        var dataPointer = reader.ReadPointer();
                        valHeaders.Add((valLen, dataPointer));
                    }

                    // Read deferred value data
                    foreach (var (valLen, dataPointer) in valHeaders)
                    {
                        var attrVal = new ATTRVAL { ValLen = valLen };

                        if (dataPointer != 0)
                        {
                            var dataLen = reader.ReadUInt32();
                            attrVal.PVal = reader.ReadBytes((int)dataLen).ToArray();
                        }

                        attr.AttrVal.PVal.Add(attrVal);
                    }
                }

                block.PAttr.Add(attr);
            }
        }

        return block;
    }

    /// <summary>
    /// Reads a DSNAME structure from NDR-encoded data.
    /// </summary>
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
            var nameBytes = reader.ReadBytes((int)(dsname.NameLen + 1) * 2); // +1 for null terminator
            dsname.StringName = System.Text.Encoding.Unicode
                .GetString(nameBytes.Span).TrimEnd('\0');
        }

        return dsname;
    }

    /// <summary>
    /// Reads a SCHEMA_PREFIX_TABLE with deferred OID prefix data.
    /// </summary>
    private static SCHEMA_PREFIX_TABLE ReadPrefixTable(NdrReader reader)
    {
        var table = new SCHEMA_PREFIX_TABLE { PrefixCount = reader.ReadUInt32() };

        var entryPointer = reader.ReadPointer();
        if (entryPointer != 0)
        {
            var count = reader.ReadUInt32();

            // Read entry headers: NdxValue, prefix length, pointer
            var entryHeaders = new List<(uint NdxValue, uint PrefixLen, uint PrefixPointer)>();
            for (uint i = 0; i < count && i < table.PrefixCount; i++)
            {
                var ndx = reader.ReadUInt32();
                var prefixLen = reader.ReadUInt32();
                var prefixPointer = reader.ReadPointer();
                entryHeaders.Add((ndx, prefixLen, prefixPointer));
            }

            // Read deferred prefix data
            foreach (var (ndx, prefixLen, prefixPointer) in entryHeaders)
            {
                var entry = new OID_PREFIX_ENTRY { NdxValue = ndx };

                if (prefixPointer != 0)
                {
                    var dataLen = reader.ReadUInt32();
                    entry.Prefix = reader.ReadBytes((int)dataLen).ToArray();
                }

                table.PPrefixEntry.Add(entry);
            }
        }

        return table;
    }

    /// <summary>
    /// Reads a REPLVALINF_V3 (linked value replication entry).
    /// </summary>
    private static REPLVALINF_V3 ReadReplValInf(NdrReader reader)
    {
        var val = new REPLVALINF_V3();

        val.PObject = ReadDsName(reader);
        val.AttrTyp = new ATTRTYP(reader.ReadUInt32());
        val.AttrVal = new ATTRVAL { ValLen = reader.ReadUInt32() };

        var dataPointer = reader.ReadPointer();
        if (dataPointer != 0)
        {
            var dataLen = reader.ReadUInt32();
            val.AttrVal.PVal = reader.ReadBytes((int)dataLen).ToArray();
        }

        val.FIsPresent = reader.ReadBoolean();

        // VALUE_META_DATA_EXT
        val.MetaData = new VALUE_META_DATA_EXT
        {
            TimeCreated = reader.ReadInt64(),
            DwVersion = reader.ReadUInt32(),
            TimeChanged = reader.ReadInt64(),
            UuidDsaOriginating = new Guid(reader.ReadBytes(16).Span),
            UsnOriginating = reader.ReadInt64(),
        };

        return val;
    }

    #endregion

    #region Connection management

    /// <summary>
    /// Validates that the RPC connection is established.
    /// </summary>
    private void EnsureConnected()
    {
        if (_rpcClient == null || !_rpcClient.IsConnected)
        {
            throw new InvalidOperationException(
                "Not connected. Call ConnectAsync before performing DRSUAPI operations.");
        }
    }

    /// <summary>
    /// Validates that DRSBind has completed and a context handle is available.
    /// </summary>
    private void EnsureBound()
    {
        EnsureConnected();

        if (_bindHandleGuid == Guid.Empty)
        {
            throw new InvalidOperationException(
                "Not bound. Call DrsBindAsync before performing DRSUAPI operations.");
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        try
        {
            await DrsUnbindAsync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error during DRSUnbind in DisposeAsync");
        }

        if (_rpcClient != null)
        {
            await _rpcClient.DisposeAsync();
            _rpcClient = null;
        }
    }

    #endregion
}
