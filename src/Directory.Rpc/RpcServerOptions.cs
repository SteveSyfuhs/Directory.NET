namespace Directory.Rpc;

public class RpcServerOptions
{
    public const string SectionName = "RpcServer";

    /// <summary>
    /// Default port changed from 135 to avoid conflict with the Windows RPC Endpoint Mapper
    /// (RpcSs) which always occupies port 135.
    /// </summary>
    public int EndpointMapperPort { get; set; } = 1135;

    public int ServicePort { get; set; } = 49664; // Single dynamic port for all interfaces

    public int MaxConnections { get; set; } = 1024;

    public string TenantId { get; set; } = "default";

    public string DomainDn { get; set; } = "DC=directory,DC=net";

    /// <summary>
    /// NetBIOS domain name used in NTLM challenges and RPC authentication.
    /// </summary>
    public string NetBiosDomainName { get; set; } = "DIRECTORY";

    /// <summary>
    /// DNS domain name used in NTLM target info AV_PAIR blocks.
    /// </summary>
    public string DnsDomainName { get; set; } = "directory.net";

    /// <summary>
    /// Server hostname (NetBIOS) used in NTLM target info AV_PAIR blocks.
    /// </summary>
    public string ServerName { get; set; } = Environment.MachineName;

    /// <summary>
    /// Server FQDN used in NTLM target info AV_PAIR blocks.
    /// </summary>
    public string ServerFqdn { get; set; } = $"{Environment.MachineName}.directory.net";
}
