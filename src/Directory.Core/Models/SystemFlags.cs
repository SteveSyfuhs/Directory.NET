namespace Directory.Core.Models;

[Flags]
public enum SystemFlags
{
    None = 0,

    /// <summary>FLAG_ATTR_NOT_REPLICATED - Attribute is not replicated.</summary>
    AttrNotReplicated = 0x00000001,

    /// <summary>FLAG_ATTR_REQ_PARTIAL_SET_MEMBER - Attribute is a member of the partial attribute set.</summary>
    AttrReqPartialSetMember = 0x00000002,

    /// <summary>FLAG_ATTR_IS_CONSTRUCTED - Attribute is constructed (computed at query time).</summary>
    AttrIsConstructed = 0x00000004,

    /// <summary>FLAG_ATTR_IS_OPERATIONAL - Attribute is operational.</summary>
    AttrIsOperational = 0x00000008,

    /// <summary>FLAG_SCHEMA_BASE_OBJECT - Object is defined in the base schema.</summary>
    SchemaBaseObject = 0x00000010,

    /// <summary>FLAG_ATTR_IS_RDN - Attribute is used as an RDN attribute.</summary>
    AttrIsRdn = 0x00000020,

    /// <summary>FLAG_DISALLOW_MOVE_ON_DELETE - Object cannot be moved to the Deleted Objects container on deletion.</summary>
    DisallowMoveOnDelete = 0x02000000,

    /// <summary>FLAG_DOMAIN_DISALLOW_MOVE - Object cannot be moved.</summary>
    DomainDisallowMove = 0x04000000,

    /// <summary>FLAG_DOMAIN_DISALLOW_RENAME - Object cannot be renamed.</summary>
    DomainDisallowRename = 0x08000000,

    /// <summary>FLAG_CONFIG_ALLOW_LIMITED_MOVE - Object can be moved with restrictions in the configuration NC.</summary>
    ConfigAllowLimitedMove = 0x10000000,

    /// <summary>FLAG_CONFIG_ALLOW_MOVE - Object can be moved in the configuration NC.</summary>
    ConfigAllowMove = 0x20000000,

    /// <summary>FLAG_CONFIG_ALLOW_RENAME - Object can be renamed in the configuration NC.</summary>
    ConfigAllowRename = 0x40000000,

    /// <summary>FLAG_DISALLOW_DELETE - Object cannot be deleted.</summary>
    DisallowDelete = unchecked((int)0x80000000)
}

[Flags]
public enum SearchFlags
{
    None = 0,

    /// <summary>fATTINDEX - Attribute is indexed.</summary>
    Indexed = 1,

    /// <summary>fPDNTATTINDEX - Attribute is indexed as a member of a PDN-based container index.</summary>
    ContainerIndex = 2,

    /// <summary>fANR - Attribute is included in ambiguous name resolution.</summary>
    AmbiguousNameResolution = 4,

    /// <summary>fPRESERVEONDELETE - Attribute is preserved when object is tombstoned.</summary>
    PreserveOnDelete = 8,

    /// <summary>fCOPY - Attribute is copied when object is copied.</summary>
    Copy = 16,

    /// <summary>fTUPLEINDEX - Attribute is indexed using tuple (substring) index.</summary>
    TupleIndex = 32,

    /// <summary>fSUBTREEATTINDEX - Attribute is indexed for subtree search optimization.</summary>
    SubtreeIndex = 64,

    /// <summary>fCONFIDENTIAL - Attribute requires CONTROL_ACCESS right to read.</summary>
    Confidential = 128,

    /// <summary>fNEVERVALUEAUDIT - Attribute changes are never included in value-change audit.</summary>
    NeverValueAudit = 256,

    /// <summary>fRODCFILTERED - Attribute is filtered from replication to RODCs.</summary>
    RodcFiltered = 512
}
