namespace Directory.Server.Setup;

public class SetupOptions
{
    // Required - must be provided
    public string DomainName { get; set; } = "";           // e.g., "contoso.com"
    public string NetBiosName { get; set; } = "";          // e.g., "CONTOSO"
    public string AdminPassword { get; set; } = "";        // Password for the Administrator account

    // Optional - sensible defaults
    public string TenantId { get; set; } = "default";
    public string AdminUsername { get; set; } = "Administrator";
    public string ForestName { get; set; } = "";           // defaults to DomainName if empty
    public string SiteName { get; set; } = "Default-First-Site-Name";
    public int ForestFunctionalLevel { get; set; } = 7;    // Windows Server 2016
    public int DomainFunctionalLevel { get; set; } = 7;

    // Derived properties
    public string DomainDn => string.Join(",", DomainName.Split('.').Select(p => $"DC={p}"));
    public string ConfigurationDn => $"CN=Configuration,{DomainDn}";
    public string SchemaDn => $"CN=Schema,CN=Configuration,{DomainDn}";

    // Replica mode — join an existing domain instead of creating a new one
    public bool IsReplica { get; set; }

    /// <summary>
    /// URL of the source DC to replicate from (e.g., "https://dc1.corp.com:9389").
    /// Required when <see cref="IsReplica"/> is true.
    /// </summary>
    public string SourceDcUrl { get; set; }

    /// <summary>
    /// UPN of the admin account used to authenticate against the source DC
    /// for replication (e.g., "administrator@corp.com").
    /// </summary>
    public string ReplicationAdminUpn { get; set; }

    /// <summary>
    /// Password for the replication admin account on the source DC.
    /// </summary>
    public string ReplicationAdminPassword { get; set; }

    public string Hostname { get; set; } = Environment.MachineName;

    // Cosmos DB settings (can override from appsettings)
    public string CosmosConnectionString { get; set; }
    public string CosmosDatabaseName { get; set; }
}
