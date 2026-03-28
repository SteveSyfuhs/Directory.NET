namespace Directory.CosmosDb.Configuration;

/// <summary>
/// Metadata about a single configuration field, used by the management UI
/// to render editors with validation hints.
/// </summary>
public class ConfigFieldMetadata
{
    public string Name { get; set; } = "";
    /// <summary>"string", "int", "bool", "timespan", "string[]"</summary>
    public string Type { get; set; } = "string";
    public string Description { get; set; } = "";
    public object DefaultValue { get; set; }
    /// <summary>Whether this field can be changed at runtime without a restart.</summary>
    public bool HotReloadable { get; set; }
    public string ValidationRegex { get; set; }
    public object MinValue { get; set; }
    public object MaxValue { get; set; }
}

/// <summary>
/// Registry of all known configuration sections and their fields.
/// Used by the management API to provide schema information for config editors.
/// </summary>
public static class ConfigurationSectionMetadata
{
    public static Dictionary<string, List<ConfigFieldMetadata>> GetAllSections() => new()
    {
        ["Cache"] = new List<ConfigFieldMetadata>
        {
            new() { Name = "Enabled", Type = "bool", Description = "Enable caching", DefaultValue = true, HotReloadable = true },
            new() { Name = "DefaultTtlSeconds", Type = "int", Description = "Default cache TTL in seconds", DefaultValue = 60, HotReloadable = true, MinValue = 5, MaxValue = 3600 },
            new() { Name = "WellKnownTtlSeconds", Type = "int", Description = "TTL for well-known objects like krbtgt (seconds)", DefaultValue = 600, HotReloadable = true, MinValue = 10, MaxValue = 7200 },
            new() { Name = "SpnTtlSeconds", Type = "int", Description = "TTL for SPN lookups (seconds)", DefaultValue = 30, HotReloadable = true, MinValue = 5, MaxValue = 600 },
            new() { Name = "GroupExpansionTtlSeconds", Type = "int", Description = "TTL for group expansion results (seconds)", DefaultValue = 300, HotReloadable = true, MinValue = 10, MaxValue = 3600 },
            new() { Name = "DnsZoneTtlSeconds", Type = "int", Description = "TTL for DNS zone data (seconds)", DefaultValue = 300, HotReloadable = true, MinValue = 10, MaxValue = 3600 },
            new() { Name = "MaxCacheSize", Type = "int", Description = "Maximum cache size in entries", DefaultValue = 50000, HotReloadable = true, MinValue = 1000, MaxValue = 1000000 },
            new() { Name = "RedisConnectionString", Type = "string", Description = "Redis connection string for distributed cache. Empty uses in-memory only.", HotReloadable = false },
            new() { Name = "RedisInstanceName", Type = "string", Description = "Redis instance name prefix for key isolation", DefaultValue = "ad:", HotReloadable = false },
            new() { Name = "EnableDistributedInvalidation", Type = "bool", Description = "Use Redis pub/sub for cross-instance cache invalidation", DefaultValue = true, HotReloadable = false },
        },

        ["Ldap"] = new List<ConfigFieldMetadata>
        {
            new() { Name = "Port", Type = "int", Description = "LDAP listening port", DefaultValue = 389, HotReloadable = false, MinValue = 1, MaxValue = 65535 },
            new() { Name = "TlsPort", Type = "int", Description = "LDAPS (TLS) listening port", DefaultValue = 636, HotReloadable = false, MinValue = 1, MaxValue = 65535 },
            new() { Name = "CertificatePath", Type = "string", Description = "Path to TLS certificate file", HotReloadable = false },
            new() { Name = "CertificatePassword", Type = "string", Description = "Password for TLS certificate", HotReloadable = false },
            new() { Name = "MaxConnections", Type = "int", Description = "Maximum concurrent LDAP connections", DefaultValue = 2000, HotReloadable = true, MinValue = 10, MaxValue = 100000 },
            new() { Name = "MaxMessageSize", Type = "int", Description = "Maximum LDAP message size in bytes", DefaultValue = 10485760, HotReloadable = true, MinValue = 65536, MaxValue = 104857600 },
            new() { Name = "MaxPageSize", Type = "int", Description = "Maximum entries per search page", DefaultValue = 1000, HotReloadable = true, MinValue = 10, MaxValue = 10000 },
            new() { Name = "MaxAdminSearchLimit", Type = "int", Description = "Maximum admin search result limit", DefaultValue = 1000, HotReloadable = true, MinValue = 100, MaxValue = 100000 },
            new() { Name = "DefaultTenantId", Type = "string", Description = "Default tenant ID for LDAP operations", DefaultValue = "default", HotReloadable = false },
            new() { Name = "DefaultDomain", Type = "string", Description = "Default domain DN", DefaultValue = "DC=directory,DC=local", HotReloadable = false },
            new() { Name = "IdleTimeoutSeconds", Type = "int", Description = "Idle connection timeout in seconds", DefaultValue = 900, HotReloadable = true, MinValue = 60, MaxValue = 86400 },
        },

        ["Kerberos"] = new List<ConfigFieldMetadata>
        {
            new() { Name = "DefaultRealm", Type = "string", Description = "Kerberos default realm name", DefaultValue = "DIRECTORY.LOCAL", HotReloadable = false, ValidationRegex = @"^[A-Z][A-Z0-9.\-]+$" },
            new() { Name = "Port", Type = "int", Description = "Kerberos KDC listening port", DefaultValue = 88, HotReloadable = false, MinValue = 1, MaxValue = 65535 },
            new() { Name = "MaximumSkew", Type = "timespan", Description = "Maximum clock skew tolerance", DefaultValue = "00:05:00", HotReloadable = true },
            new() { Name = "SessionLifetime", Type = "timespan", Description = "TGT session lifetime", DefaultValue = "10:00:00", HotReloadable = true },
            new() { Name = "MaximumRenewalWindow", Type = "timespan", Description = "Maximum ticket renewal window", DefaultValue = "7.00:00:00", HotReloadable = true },
        },

        ["Dns"] = new List<ConfigFieldMetadata>
        {
            new() { Name = "Port", Type = "int", Description = "DNS server listening port (UDP+TCP)", DefaultValue = 53, HotReloadable = false, MinValue = 1, MaxValue = 65535 },
            new() { Name = "ServerHostname", Type = "string", Description = "This server's FQDN for SRV/NS records", DefaultValue = "dc1.directory.local", HotReloadable = false },
            new() { Name = "ServerIpAddresses", Type = "string[]", Description = "IP addresses this server listens on", HotReloadable = false },
            new() { Name = "Domains", Type = "string[]", Description = "DNS domains managed by this server", HotReloadable = false },
            new() { Name = "DefaultTtl", Type = "int", Description = "Default TTL for DNS records (seconds)", DefaultValue = 600, HotReloadable = true, MinValue = 30, MaxValue = 86400 },
            new() { Name = "EdnsUdpPayloadSize", Type = "int", Description = "EDNS0 advertised UDP payload size", DefaultValue = 4096, HotReloadable = true, MinValue = 512, MaxValue = 65535 },
            new() { Name = "DefaultForwarder", Type = "string", Description = "Default upstream DNS server for unmatched queries", HotReloadable = true },
        },

        ["DcNode"] = new List<ConfigFieldMetadata>
        {
            new() { Name = "Hostname", Type = "string", Description = "This DC's hostname", HotReloadable = false },
            new() { Name = "SiteName", Type = "string", Description = "Active Directory site name", DefaultValue = "Default-First-Site-Name", HotReloadable = true },
            new() { Name = "TenantId", Type = "string", Description = "Tenant ID this DC serves", DefaultValue = "default", HotReloadable = false },
            new() { Name = "DomainDn", Type = "string", Description = "Distinguished name of the domain this DC serves", HotReloadable = false },
            new() { Name = "BindAddresses", Type = "string[]", Description = "Network addresses to bind to", HotReloadable = false },
        },

        ["RpcServer"] = new List<ConfigFieldMetadata>
        {
            new() { Name = "EndpointMapperPort", Type = "int", Description = "RPC endpoint mapper port", DefaultValue = 135, HotReloadable = false, MinValue = 1, MaxValue = 65535 },
            new() { Name = "ServicePort", Type = "int", Description = "RPC service port for all interfaces", DefaultValue = 49664, HotReloadable = false, MinValue = 1, MaxValue = 65535 },
            new() { Name = "MaxConnections", Type = "int", Description = "Maximum concurrent RPC connections", DefaultValue = 1024, HotReloadable = true, MinValue = 10, MaxValue = 100000 },
            new() { Name = "TenantId", Type = "string", Description = "Tenant ID for RPC operations", DefaultValue = "default", HotReloadable = false },
            new() { Name = "DomainDn", Type = "string", Description = "Domain DN for RPC operations", DefaultValue = "DC=directory,DC=net", HotReloadable = false },
        },

        ["Replication"] = new List<ConfigFieldMetadata>
        {
            new() { Name = "DatabaseName", Type = "string", Description = "Cosmos DB database name for replication", DefaultValue = "DirectoryService", HotReloadable = false },
            new() { Name = "HttpPort", Type = "int", Description = "HTTP port for DRS replication endpoint", DefaultValue = 9389, HotReloadable = false, MinValue = 1, MaxValue = 65535 },
        },
    };
}
