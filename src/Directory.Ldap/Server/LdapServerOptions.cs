namespace Directory.Ldap.Server;

public class LdapServerOptions
{
    public const string SectionName = "Ldap";

    public int Port { get; set; } = 389;
    public int TlsPort { get; set; } = 636;
    public string CertificatePath { get; set; }
    public string CertificatePassword { get; set; }
    public int MaxConnections { get; set; } = 2000;
    public int MaxMessageSize { get; set; } = 10 * 1024 * 1024; // 10MB
    public int MaxPageSize { get; set; } = 1000;
    public int MaxAdminSearchLimit { get; set; } = 1000;
    public string DefaultTenantId { get; set; } = "default";
    public string DefaultDomain { get; set; } = "DC=directory,DC=local";

    /// <summary>Idle connection timeout in seconds. Connections with no activity are closed. Default: 900 (15 min).</summary>
    public int IdleTimeoutSeconds { get; set; } = 900;

    /// <summary>Global Catalog plain-text port. Default: 3268.</summary>
    public int GcPort { get; set; } = 3268;

    /// <summary>Global Catalog TLS port. Default: 3269.</summary>
    public int GcTlsPort { get; set; } = 3269;
}
