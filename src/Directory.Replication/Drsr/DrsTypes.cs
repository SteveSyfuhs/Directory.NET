using System.Text;

namespace Directory.Replication.Drsr;

// ──────────────────────────────────────────────────────────────────────
// DRSUAPI data structures per [MS-DRSR]
// https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-drsr/
// ──────────────────────────────────────────────────────────────────────

#region Primitive attribute types (section 5.16)

/// <summary>
/// Compact attribute type identifier — an OID mapped through the schema prefix table.
/// </summary>
public readonly record struct ATTRTYP(uint Value)
{
    public static implicit operator uint(ATTRTYP a) => a.Value;
    public static implicit operator ATTRTYP(uint v) => new(v);
}

/// <summary>
/// A single attribute value (variable-length byte blob).
/// </summary>
public class ATTRVAL
{
    public uint ValLen { get; set; }
    public byte[] PVal { get; set; } = [];
}

/// <summary>
/// A block of attribute values.
/// </summary>
public class ATTRVALBLOCK
{
    public uint ValCount { get; set; }
    public List<ATTRVAL> PVal { get; set; } = [];
}

/// <summary>
/// A typed attribute: type identifier + value block.
/// </summary>
public class ATTR
{
    public ATTRTYP AttrTyp { get; set; }
    public ATTRVALBLOCK AttrVal { get; set; } = new();
}

/// <summary>
/// A block of attributes.
/// </summary>
public class ATTRBLOCK
{
    public uint AttrCount { get; set; }
    public List<ATTR> PAttr { get; set; } = [];
}

#endregion

#region DSNAME (section 5.49)

/// <summary>
/// Identifies a directory object by GUID, SID and/or DN string.
/// </summary>
public class DSNAME
{
    public uint StructLen { get; set; }
    public uint SidLen { get; set; }
    public Guid Guid { get; set; }
    public byte[] Sid { get; set; } = [];
    public uint NameLen { get; set; }
    public string StringName { get; set; } = string.Empty;

    /// <summary>
    /// Compute the on-wire struct length (header + SID + null-terminated UTF-16 name).
    /// </summary>
    public uint ComputeStructLen()
    {
        // 28 bytes header (4 structLen + 4 sidLen + 16 guid + 4 nameLen)
        // + SidLen bytes for SID
        // + (NameLen + 1) * 2 bytes for null-terminated UTF-16 name
        return (uint)(28 + SidLen + (NameLen + 1) * 2);
    }

    public static DSNAME FromDn(string dn, Guid? guid = null, byte[] sid = null)
    {
        var name = new DSNAME
        {
            Guid = guid ?? Guid.Empty,
            StringName = dn,
            NameLen = (uint)dn.Length,
        };

        if (sid is { Length: > 0 })
        {
            name.Sid = sid;
            name.SidLen = (uint)sid.Length;
        }

        name.StructLen = name.ComputeStructLen();
        return name;
    }
}

#endregion

#region USN_VECTOR (section 5.219)

/// <summary>
/// A USN watermark triplet used to track replication progress.
/// </summary>
public class USN_VECTOR
{
    public long UsnHighObjUpdate { get; set; }
    public long UsnReserved { get; set; }
    public long UsnHighPropUpdate { get; set; }
}

#endregion

#region Up-to-dateness vector (section 5.166)

/// <summary>
/// A single cursor entry in the up-to-dateness vector (V2).
/// </summary>
public class UPTODATE_CURSOR_V2
{
    public Guid UuidDsa { get; set; }
    public long UsnHighPropUpdate { get; set; }
    public long FtimeLastSyncSuccess { get; set; }
}

/// <summary>
/// Up-to-dateness vector v2 — tracks the highest USN from each originating DC.
/// </summary>
public class UPTODATE_VECTOR_V2_EXT
{
    public uint DwVersion { get; set; } = 2;
    public uint DwReserved { get; set; }
    public uint CNumCursors { get; set; }
    public List<UPTODATE_CURSOR_V2> RgCursors { get; set; } = [];
}

#endregion

#region Property metadata (section 5.119)

/// <summary>
/// Per-attribute replication metadata — version, time, originating DC and USN.
/// </summary>
public class PROPERTY_META_DATA_EXT
{
    public uint DwVersion { get; set; }
    public long TimeChanged { get; set; }
    public Guid UuidDsaOriginating { get; set; }
    public long UsnOriginating { get; set; }
}

/// <summary>
/// Vector of per-attribute metadata entries.
/// </summary>
public class PROPERTY_META_DATA_EXT_VECTOR
{
    public uint CNumProps { get; set; }
    public List<PROPERTY_META_DATA_EXT> RgMetaData { get; set; } = [];
}

#endregion

#region Schema prefix table (section 5.141)

/// <summary>
/// A single entry in the schema prefix table, mapping a compact index to an OID prefix.
/// </summary>
public class OID_PREFIX_ENTRY
{
    /// <summary>
    /// The NDNC value: upper 16 bits of the ATTRTYP.
    /// </summary>
    public uint NdxValue { get; set; }

    /// <summary>
    /// The BER-encoded OID prefix bytes.
    /// </summary>
    public byte[] Prefix { get; set; } = [];
}

/// <summary>
/// Maps compact ATTRTYP values to full OID strings.
/// </summary>
public class SCHEMA_PREFIX_TABLE
{
    public uint PrefixCount { get; set; }
    public List<OID_PREFIX_ENTRY> PPrefixEntry { get; set; } = [];
}

#endregion

#region ENTINF / REPLENTINFLIST (section 5.50, 5.133)

/// <summary>
/// A directory entry for replication, containing the object DN and its attributes.
/// </summary>
public class ENTINF
{
    public DSNAME PName { get; set; } = new();
    public uint UlFlags { get; set; }
    public ATTRBLOCK AttrBlock { get; set; } = new();
}

/// <summary>
/// Linked list of replicated entry information.
/// </summary>
public class REPLENTINFLIST
{
    public REPLENTINFLIST PNextEntInf { get; set; }
    public ENTINF Entinf { get; set; } = new();
    public bool FIsNCPrefix { get; set; }
    public PROPERTY_META_DATA_EXT_VECTOR PMetaDataExt { get; set; }
}

#endregion

#region Linked value replication (section 5.134)

/// <summary>
/// A single linked value change for DN-valued multi-valued attributes (LVR).
/// </summary>
public class REPLVALINF_V3
{
    public DSNAME PObject { get; set; } = new();
    public ATTRTYP AttrTyp { get; set; }
    public ATTRVAL AttrVal { get; set; } = new();
    public bool FIsPresent { get; set; }
    public VALUE_META_DATA_EXT MetaData { get; set; } = new();
}

/// <summary>
/// Metadata associated with a single linked value.
/// </summary>
public class VALUE_META_DATA_EXT
{
    public long TimeCreated { get; set; }
    public uint DwVersion { get; set; }
    public long TimeChanged { get; set; }
    public Guid UuidDsaOriginating { get; set; }
    public long UsnOriginating { get; set; }
}

#endregion

#region DRS_EXTENSIONS_INT (section 5.39)

/// <summary>
/// DRS extension negotiation — advertises capabilities during DRSBind.
/// </summary>
public class DRS_EXTENSIONS_INT
{
    public DrsExtFlags DwFlags { get; set; }
    public Guid SiteObjGuid { get; set; }
    public int Pid { get; set; }
    public uint DwReplEpoch { get; set; }
    public DrsExtMoreFlags DwFlagsExt { get; set; }
    public Guid ConfigObjGuid { get; set; }
}

[Flags]
public enum DrsExtFlags : uint
{
    None = 0,
    DRS_EXT_BASE = 0x00000001,
    DRS_EXT_ASYNCREPL = 0x00000002,
    DRS_EXT_REMOVEAPI = 0x00000004,
    DRS_EXT_MOVEREQ_V2 = 0x00000008,
    DRS_EXT_GETCHG_DEFLATE = 0x00000010,
    DRS_EXT_DCINFO_V1 = 0x00000020,
    DRS_EXT_RESTORE_USN_OPTIMIZATION = 0x00000040,
    DRS_EXT_ADDENTRY = 0x00000080,
    DRS_EXT_KCC_EXECUTE = 0x00000100,
    DRS_EXT_ADDENTRY_V2 = 0x00000200,
    DRS_EXT_LINKED_VALUE_REPLICATION = 0x00000400,
    DRS_EXT_DCINFO_V2 = 0x00000800,
    DRS_EXT_INSTANCE_TYPE_NOT_REQ_ON_OBJ = 0x00001000,
    DRS_EXT_CRYPTO_BIND = 0x00002000,
    DRS_EXT_GET_REPL_INFO = 0x00004000,
    DRS_EXT_STRONG_ENCRYPTION = 0x00008000,
    DRS_EXT_DCINFO_VFFFFFFFF = 0x00010000,
    DRS_EXT_TRANSITIVE_MEMBERSHIP = 0x00020000,
    DRS_EXT_ADD_SID_HISTORY = 0x00040000,
    DRS_EXT_POST_BETA3 = 0x00080000,
    DRS_EXT_GETCHGREQ_V5 = 0x00100000,
    DRS_EXT_GETMEMBERSHIPS2 = 0x00200000,
    DRS_EXT_GETCHGREQ_V6 = 0x00400000,
    DRS_EXT_NONDOMAIN_NCS = 0x00800000,
    DRS_EXT_GETCHGREQ_V8 = 0x01000000,
    DRS_EXT_GETCHGREPLY_V5 = 0x02000000,
    DRS_EXT_GETCHGREPLY_V6 = 0x04000000,
    DRS_EXT_GETCHGREPLY_V9 = 0x00000100,
    DRS_EXT_WHISTLER_BETA3 = 0x08000000,
    DRS_EXT_W2K3_DEFLATE = 0x10000000,
    DRS_EXT_GETCHGREQ_V10 = 0x20000000,
    DRS_EXT_RECYCLE_BIN = 0x80000000,
}

[Flags]
public enum DrsExtMoreFlags : uint
{
    None = 0,
    DRS_EXT_ADAM = 0x00000001,
    DRS_EXT_LH_BETA2 = 0x00000002,
    DRS_EXT_RPC_CORRELATIONID_1 = 0x00000004,
}

#endregion

#region GetNCChanges request V8 (section 4.1.10.2.3)

/// <summary>
/// Request message for IDL_DRSGetNCChanges, version 8.
/// </summary>
public class DRS_MSG_GETCHGREQ_V8
{
    public Guid UuidDsaObjDest { get; set; }
    public Guid UuidInvocIdSrc { get; set; }
    public DSNAME PNC { get; set; }
    public USN_VECTOR UsnvecFrom { get; set; } = new();
    public UPTODATE_VECTOR_V2_EXT PUpToDateVecDest { get; set; }
    public DrsGetNcChangesFlags UlFlags { get; set; }
    public uint CMaxObjects { get; set; }
    public uint CMaxBytes { get; set; }
    public uint UlExtendedOp { get; set; }
    public ulong LiFsmoInfo { get; set; }
    public PARTIAL_ATTR_VECTOR PPartialAttrSet { get; set; }
    public PARTIAL_ATTR_VECTOR PPartialAttrSetEx { get; set; }
    public SCHEMA_PREFIX_TABLE PrefixTableDest { get; set; } = new();
}

[Flags]
public enum DrsGetNcChangesFlags : uint
{
    None = 0,
    DRS_INIT_SYNC = 0x00000020,
    DRS_WRIT_REP = 0x00000010,
    DRS_INIT_SYNC_NOW = 0x00000800,
    DRS_FULL_SYNC_NOW = 0x00008000,
    DRS_SYNC_URGENT = 0x00080000,
    DRS_SYNC_PAS = 0x40000000,
    DRS_GET_ANC = 0x00000200,
    DRS_GET_NC_SIZE = 0x00010000,
    DRS_CRITICAL_ONLY = 0x00000001,
}

/// <summary>
/// Partial attribute set — specifies which attributes to replicate for GC.
/// </summary>
public class PARTIAL_ATTR_VECTOR
{
    public uint CAttrs { get; set; }
    public List<ATTRTYP> RgPartialAttr { get; set; } = [];
}

#endregion

#region GetNCChanges reply V6 (section 4.1.10.2.9)

/// <summary>
/// Response message for IDL_DRSGetNCChanges, version 6.
/// </summary>
public class DRS_MSG_GETCHGREPLY_V6
{
    public Guid UuidDsaObjSrc { get; set; }
    public Guid UuidInvocIdSrc { get; set; }
    public DSNAME PNC { get; set; }
    public USN_VECTOR UsnvecFrom { get; set; } = new();
    public USN_VECTOR UsnvecTo { get; set; } = new();
    public UPTODATE_VECTOR_V2_EXT PUpToDateVecSrc { get; set; }
    public SCHEMA_PREFIX_TABLE PrefixTableSrc { get; set; } = new();
    public uint UlExtendedRet { get; set; }
    public uint CNumObjects { get; set; }
    public uint CNumBytes { get; set; }
    public REPLENTINFLIST PObjects { get; set; }
    public bool FMoreData { get; set; }
    public uint CNumNcSizeObjects { get; set; }
    public uint CNumNcSizeValues { get; set; }
    public List<REPLVALINF_V3> RgValues { get; set; } = [];
    public uint DwDRSError { get; set; }
}

#endregion

#region DRSReplicaSync (section 4.1.23.1.2)

/// <summary>
/// Request message for IDL_DRSReplicaSync, version 1.
/// </summary>
public class DRS_MSG_REPSYNC_V1
{
    public DSNAME PNC { get; set; }
    public Guid UuidDsaSrc { get; set; }
    public string PszDsaSrc { get; set; }
    public DrsReplicaSyncOptions UlOptions { get; set; }
}

[Flags]
public enum DrsReplicaSyncOptions : uint
{
    None = 0,
    DRS_ASYNC_OP = 0x00000001,
    DRS_WRIT_REP = 0x00000010,
    DRS_INIT_SYNC = 0x00000020,
    DRS_PER_SYNC = 0x00000040,
    DRS_MAIL_REP = 0x00000080,
    DRS_ASYNC_REP = 0x00000100,
    DRS_FULL_SYNC_NOW = 0x00008000,
    DRS_SYNC_URGENT = 0x00080000,
    DRS_SYNC_FORCED = 0x10000000,
    DRS_SYNC_ALL = 0x00000008,
}

#endregion

#region DRSUpdateRefs (section 4.1.26.1.2)

/// <summary>
/// Request message for IDL_DRSUpdateRefs, version 1.
/// </summary>
public class DRS_MSG_UPDREFS_V1
{
    public DSNAME PNC { get; set; }
    public string PszDsaDest { get; set; }
    public Guid UuidDsaObjDest { get; set; }
    public DrsUpdateRefsOptions UlOptions { get; set; }
}

[Flags]
public enum DrsUpdateRefsOptions : uint
{
    None = 0,
    DRS_WRIT_REP = 0x00000010,
    DRS_ADD_REF = 0x00000004,
    DRS_DEL_REF = 0x00000008,
    DRS_DEL_REF_NO_ERROR = 0x00000100,
}

#endregion

#region DRSCrackNames (section 4.1.4.1.2)

/// <summary>
/// Request message for IDL_DRSCrackNames, version 1.
/// </summary>
public class DRS_MSG_CRACKREQ_V1
{
    public uint CodePage { get; set; }
    public uint LocaleId { get; set; }
    public DsNameFlags DwFlags { get; set; }
    public DsNameFormat FormatOffered { get; set; }
    public DsNameFormat FormatDesired { get; set; }
    public uint CNames { get; set; }
    public List<string> RpNames { get; set; } = [];
}

[Flags]
public enum DsNameFlags : uint
{
    DS_NAME_NO_FLAGS = 0x0,
    DS_NAME_FLAG_SYNTACTICAL_ONLY = 0x1,
    DS_NAME_FLAG_EVAL_AT_DC = 0x2,
    DS_NAME_FLAG_GCVERIFY = 0x4,
    DS_NAME_FLAG_TRUST_REFERRAL = 0x8,
}

public enum DsNameFormat : uint
{
    DS_UNKNOWN_NAME = 0,
    DS_FQDN_1779_NAME = 1,
    DS_NT4_ACCOUNT_NAME = 2,
    DS_DISPLAY_NAME = 3,
    DS_UNIQUE_ID_NAME = 6,
    DS_CANONICAL_NAME = 7,
    DS_USER_PRINCIPAL_NAME = 8,
    DS_CANONICAL_NAME_EX = 9,
    DS_SERVICE_PRINCIPAL_NAME = 10,
    DS_SID_OR_SID_HISTORY_NAME = 11,
    DS_DNS_DOMAIN_NAME = 12,
}

/// <summary>
/// Response message for IDL_DRSCrackNames, version 1.
/// </summary>
public class DRS_MSG_CRACKREPLY_V1
{
    public DS_NAME_RESULTW PResult { get; set; }
}

/// <summary>
/// Container for name resolution results.
/// </summary>
public class DS_NAME_RESULTW
{
    public uint CItems { get; set; }
    public List<DS_NAME_RESULT_ITEMW> RItems { get; set; } = [];
}

public class DS_NAME_RESULT_ITEMW
{
    public DsNameStatus Status { get; set; }
    public string PDomain { get; set; }
    public string PName { get; set; }
}

public enum DsNameStatus : uint
{
    DS_NAME_NO_ERROR = 0,
    DS_NAME_ERROR_RESOLVING = 1,
    DS_NAME_ERROR_NOT_FOUND = 2,
    DS_NAME_ERROR_NOT_UNIQUE = 3,
    DS_NAME_ERROR_NO_MAPPING = 4,
    DS_NAME_ERROR_DOMAIN_ONLY = 5,
    DS_NAME_ERROR_NO_SYNTACTICAL_MAPPING = 6,
    DS_NAME_ERROR_TRUST_REFERRAL = 7,
}

#endregion

#region DRSVerifyNames (section 4.1.28)

/// <summary>
/// Request message for IDL_DRSVerifyNames, version 1.
/// </summary>
public class DRS_MSG_VERIFYREQ_V1
{
    public DrsVerifyNamesFlags DwFlags { get; set; }
    public uint CNames { get; set; }
    public List<DSNAME> RpNames { get; set; } = [];
    public ATTRBLOCK RequiredAttrs { get; set; } = new();
    public SCHEMA_PREFIX_TABLE PrefixTable { get; set; } = new();
}

[Flags]
public enum DrsVerifyNamesFlags : uint
{
    None = 0,
    DRS_VERIFY_DSNAMES = 0x00000001,
    DRS_VERIFY_SIDS = 0x00000002,
    DRS_VERIFY_SAM_ACCOUNT_NAMES = 0x00000004,
    DRS_VERIFY_FPOS = 0x00000008,
}

/// <summary>
/// Response message for IDL_DRSVerifyNames, version 1.
/// </summary>
public class DRS_MSG_VERIFYREPLY_V1
{
    public uint CNames { get; set; }
    public List<ENTINF> RpEntInf { get; set; } = [];
    public SCHEMA_PREFIX_TABLE PrefixTable { get; set; } = new();
}

#endregion

#region DRSWriteSPN (section 4.1.29)

/// <summary>
/// Request message for IDL_DRSWriteSPN, version 1.
/// </summary>
public class DRS_MSG_WRITESPNREQ_V1
{
    public DrsWriteSpnOperation Operation { get; set; }
    public string PwszAccount { get; set; }
    public uint CSpn { get; set; }
    public List<string> PpszSpn { get; set; } = [];
}

public enum DrsWriteSpnOperation : uint
{
    DS_SPN_ADD_SPN_OP = 0,
    DS_SPN_REPLACE_SPN_OP = 1,
    DS_SPN_DELETE_SPN_OP = 2,
}

/// <summary>
/// Response message for IDL_DRSWriteSPN, version 1.
/// </summary>
public class DRS_MSG_WRITESPNREPLY_V1
{
    public uint DwWin32Error { get; set; }
}

#endregion

#region Replication neighbor / KCC types (section 5.127)

/// <summary>
/// Describes a replication partner for a naming context.
/// </summary>
public class DS_REPL_NEIGHBORW
{
    public string PszNamingContext { get; set; }
    public string PszSourceDsaDN { get; set; }
    public string PszSourceDsaAddress { get; set; }
    public string PszAsyncIntersiteTransportDN { get; set; }
    public DsReplNeighborFlags DwReplicaFlags { get; set; }
    public uint DwReserved { get; set; }
    public Guid UuidNamingContextObjGuid { get; set; }
    public Guid UuidSourceDsaObjGuid { get; set; }
    public Guid UuidSourceDsaInvocationID { get; set; }
    public Guid UuidAsyncIntersiteTransportObjGuid { get; set; }
    public long UsnLastObjChangeSynced { get; set; }
    public long UsnAttributeFilter { get; set; }
    public long FtimeLastSyncSuccess { get; set; }
    public long FtimeLastSyncAttempt { get; set; }
    public uint DwLastSyncResult { get; set; }
    public uint CNumConsecutiveSyncFailures { get; set; }
}

[Flags]
public enum DsReplNeighborFlags : uint
{
    None = 0,
    DS_REPL_NBR_WRITEABLE = 0x00000010,
    DS_REPL_NBR_SYNC_ON_STARTUP = 0x00000020,
    DS_REPL_NBR_DO_SCHEDULED_SYNCS = 0x00000040,
    DS_REPL_NBR_USE_ASYNC_INTERSITE_TRANSPORT = 0x00000080,
    DS_REPL_NBR_TWO_WAY_SYNC = 0x00000200,
    DS_REPL_NBR_FULL_SYNC_IN_PROGRESS = 0x00010000,
    DS_REPL_NBR_FULL_SYNC_NEXT_PACKET = 0x00020000,
    DS_REPL_NBR_NEVER_SYNCED = 0x00200000,
    DS_REPL_NBR_COMPRESS_CHANGES = 0x10000000,
    DS_REPL_NBR_NO_CHANGE_NOTIFICATIONS = 0x20000000,
    DS_REPL_NBR_PARTIAL_ATTRIBUTE_SET = 0x40000000,
}

/// <summary>
/// Replication cursor — tracks the highest USN from an originating DC (v3 with string name).
/// </summary>
public class DS_REPL_CURSORS_3W
{
    public Guid UuidSourceDsaInvocationID { get; set; }
    public long UsnAttributeFilter { get; set; }
    public long FtimeLastSyncSuccess { get; set; }
    public string PszSourceDsaDN { get; set; }
}

/// <summary>
/// KCC DSA failure record.
/// </summary>
public class DS_REPL_KCC_DSA_FAILUREW
{
    public string PszDsaDN { get; set; }
    public Guid UuidDsaObjGuid { get; set; }
    public long FtimeFirstFailure { get; set; }
    public uint CNumFailures { get; set; }
    public uint DwLastResult { get; set; }
}

#endregion

#region GetReplInfo types

/// <summary>
/// Info type codes for IDL_DRSGetReplInfo.
/// </summary>
public enum DsReplInfoType : uint
{
    DS_REPL_INFO_NEIGHBORS = 0,
    DS_REPL_INFO_CURSORS_FOR_NC = 1,
    DS_REPL_INFO_METADATA_FOR_OBJ = 2,
    DS_REPL_INFO_KCC_DSA_CONNECT_FAILURES = 3,
    DS_REPL_INFO_KCC_DSA_LINK_FAILURES = 4,
    DS_REPL_INFO_PENDING_OPS = 5,
    DS_REPL_INFO_METADATA_2_FOR_OBJ = 6,
    DS_REPL_INFO_METADATA_2_FOR_ATTR_VALUE = 7,
    DS_REPL_INFO_CURSORS_3_FOR_NC = 8,
    DS_REPL_INFO_METADATA_EXT_FOR_ATTR_VALUE = 9,
}

#endregion

#region DRS bind context

/// <summary>
/// Tracks per-connection DRS bind state including negotiated extensions.
/// </summary>
public class DrsBindContext
{
    public Guid BindHandle { get; set; }
    public DRS_EXTENSIONS_INT ClientExtensions { get; set; } = new();
    public DRS_EXTENSIONS_INT ServerExtensions { get; set; } = new();
    public string TenantId { get; set; } = "default";
    public string DomainDn { get; set; } = string.Empty;
}

#endregion

#region DRSGetMemberships (section 4.1.8)

/// <summary>
/// Operation type for IDL_DRSGetMemberships per [MS-DRSR] section 4.1.8.1.2.
/// </summary>
public enum RevMembGetType : uint
{
    REVMEMB_GET_GROUPS_FOR_USER = 1,
    REVMEMB_GET_ACCOUNT_GROUPS = 2,
    REVMEMB_GET_RESOURCE_GROUPS = 3,
    REVMEMB_GET_UNIVERSAL_GROUPS = 4,
}

/// <summary>
/// Request message for IDL_DRSGetMemberships, version 1.
/// </summary>
public class DRS_MSG_REVMEMB_REQ_V1
{
    public uint CNames { get; set; }
    public List<DSNAME> PpDsNames { get; set; } = [];
    public RevMembGetType OperationType { get; set; }
    public DSNAME PLimitingDomain { get; set; }
}

/// <summary>
/// Response message for IDL_DRSGetMemberships, version 1.
/// </summary>
public class DRS_MSG_REVMEMB_REPLY_V1
{
    public uint ErrCode { get; set; }
    public uint CDsNames { get; set; }
    public List<DSNAME> PpDsNames { get; set; } = [];
    public ATTRBLOCK PAttributes { get; set; } = new();
    public uint CSidHistory { get; set; }
    public List<byte[]> PpSidHistory { get; set; } = [];
}

#endregion

#region DRSRemoveDsServer (section 4.1.22)

/// <summary>
/// Request message for IDL_DRSRemoveDsServer, version 1.
/// </summary>
public class DRS_MSG_RMSVRREQ_V1
{
    public string ServerDN { get; set; }
    public string DomainDN { get; set; }
    public bool FCommit { get; set; }
}

/// <summary>
/// Response message for IDL_DRSRemoveDsServer, version 1.
/// </summary>
public class DRS_MSG_RMSVRREPLY_V1
{
    public bool FLastDcInDomain { get; set; }
}

#endregion

#region DRSRemoveDsDomain (section 4.1.21)

/// <summary>
/// Request message for IDL_DRSRemoveDsDomain, version 1.
/// </summary>
public class DRS_MSG_RMDMNREQ_V1
{
    public string DomainDN { get; set; }
}

/// <summary>
/// Response message for IDL_DRSRemoveDsDomain, version 1.
/// </summary>
public class DRS_MSG_RMDMNREPLY_V1
{
    // No fields in the reply other than the return code.
}

#endregion

#region DRSAddCloneDC (section 4.1.29)

/// <summary>
/// Request message for IDL_DRSAddCloneDC.
/// </summary>
public class DRS_MSG_ADDCLONEDCREQ
{
    public string CloneDcName { get; set; }
    public string SiteName { get; set; }
}

/// <summary>
/// Response message for IDL_DRSAddCloneDC.
/// </summary>
public class DRS_MSG_ADDCLONEDCREPLY
{
    public uint DwWin32Error { get; set; }
    public string SourceDsaDN { get; set; }
}

#endregion
