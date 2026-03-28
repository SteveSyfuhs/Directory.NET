namespace Directory.Rpc.Lsa;

public class LsaPolicyHandle
{
    public uint GrantedAccess { get; set; }
    public string SystemName { get; set; } = "";
    public string TenantId { get; set; } = "";
    public string DomainDn { get; set; } = "";
}

/// <summary>
/// Handle for an LSA secret object (machine account passwords, trust credentials, DPAPI keys).
/// </summary>
public class LsaSecretHandle
{
    public string SecretName { get; set; } = "";
    public string SecretDn { get; set; } = "";
    public uint GrantedAccess { get; set; }
    public string TenantId { get; set; } = "";
    public string DomainDn { get; set; } = "";
}

/// <summary>
/// Handle for an LSA trusted domain object.
/// </summary>
public class LsaTrustedDomainHandle
{
    public string TrustedDomainDn { get; set; } = "";
    public string TrustedDomainSid { get; set; } = "";
    public string TrustedDomainName { get; set; } = "";
    public uint GrantedAccess { get; set; }
    public string TenantId { get; set; } = "";
    public string DomainDn { get; set; } = "";
}
