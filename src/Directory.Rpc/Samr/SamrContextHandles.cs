namespace Directory.Rpc.Samr;

/// <summary>
/// Server-level context handle created by SamrConnect / SamrConnect5.
/// </summary>
public class SamrServerHandle
{
    public uint GrantedAccess { get; set; }
    public string TenantId { get; set; } = "";
}

/// <summary>
/// Domain-level context handle created by SamrOpenDomain.
/// </summary>
public class SamrDomainHandle
{
    public uint GrantedAccess { get; set; }
    public string DomainDn { get; set; } = "";
    public string DomainSid { get; set; } = "";
    public string DomainName { get; set; } = "";
    public string TenantId { get; set; } = "";
}

/// <summary>
/// User-level context handle created by SamrOpenUser or SamrCreateUser2InDomain.
/// </summary>
public class SamrUserHandle
{
    public uint GrantedAccess { get; set; }
    public string UserDn { get; set; } = "";
    public uint Rid { get; set; }
    public string DomainDn { get; set; } = "";
    public string TenantId { get; set; } = "";
}

/// <summary>
/// Group-level context handle created by SamrOpenGroup.
/// </summary>
public class SamrGroupHandle
{
    public uint GrantedAccess { get; set; }
    public string GroupDn { get; set; } = "";
    public uint Rid { get; set; }
    public string DomainDn { get; set; } = "";
    public string TenantId { get; set; } = "";
}

/// <summary>
/// Alias-level context handle created by SamrOpenAlias.
/// </summary>
public class SamrAliasHandle
{
    public uint GrantedAccess { get; set; }
    public string AliasDn { get; set; } = "";
    public uint Rid { get; set; }
    public string DomainDn { get; set; } = "";
    public string TenantId { get; set; } = "";
}
