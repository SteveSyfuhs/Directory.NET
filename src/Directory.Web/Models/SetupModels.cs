namespace Directory.Web.Models;

public record SetupStatusResponse(
    bool IsDatabaseConfigured,
    bool IsProvisioned,
    bool IsProvisioning,
    int ProvisioningProgress,
    string ProvisioningPhase,
    string ProvisioningError
);

public record ConfigureDatabaseRequest(string ConnectionString, string DatabaseName);

public record ValidateConnectionRequest(string ConnectionString, string DatabaseName);
public record ValidateConnectionResponse(bool Success, string Error);

public record ValidateDomainRequest(string DomainName);
public record ValidateDomainResponse(bool Valid, string DomainDn, string SuggestedNetBios, string Error);

public record ValidatePasswordRequest(string Password);
public record ValidatePasswordResponse(bool Valid, string Reason);

public record ProvisionRequest(
    string DomainName,
    string NetBiosName,
    string AdminPassword,
    string AdminUsername = "Administrator",
    string SiteName = "Default-First-Site-Name",
    string CosmosConnectionString = null,
    string CosmosDatabaseName = null
);

public record ProvisionResponse(bool Started, string Error);

/// <summary>
/// Request to validate connectivity and retrieve metadata from a source DC.
/// </summary>
/// <param name="SourceDcUrl">Base URL of the source DC's DRS HTTP service (e.g., "https://dc1.corp.com:9389").</param>
public record ValidateSourceDcRequest(string SourceDcUrl);

/// <summary>
/// Response from validating a source DC, containing domain metadata on success.
/// </summary>
/// <param name="Success">Whether the source DC was reachable and returned valid metadata.</param>
/// <param name="Error">Error message if validation failed; null on success.</param>
/// <param name="DomainName">DNS domain name discovered from the source DC's rootDSE.</param>
/// <param name="DomainDn">Distinguished name of the domain naming context.</param>
/// <param name="ForestName">DNS name of the forest root domain.</param>
/// <param name="DcHostname">Server name of the source DC.</param>
/// <param name="DsaGuid">DSA object GUID of the source DC.</param>
/// <param name="FunctionalLevel">Domain controller functional level advertised by the source DC.</param>
/// <param name="Transport">Transport protocol to use (default: "http").</param>
public record ValidateSourceDcResponse(
    bool Success,
    string Error,
    string DomainName,
    string DomainDn,
    string ForestName,
    string DcHostname,
    string DsaGuid,
    int? FunctionalLevel,
    string Transport = "http"
);

/// <summary>
/// Request to discover a Windows AD domain by DNS name.
/// The backend performs SRV lookup, EPM port resolution, and RPC DRSBind to verify.
/// </summary>
/// <param name="DomainName">DNS domain name to discover (e.g., "contoso.com").</param>
public record DiscoverDomainRequest(string DomainName);

/// <summary>
/// Result of domain discovery via DNS + RPC.
/// </summary>
/// <param name="Success">Whether domain discovery succeeded.</param>
/// <param name="Error">Error message if discovery failed; null on success.</param>
/// <param name="DcHostname">Discovered DC hostname (e.g., "DC1.contoso.com").</param>
/// <param name="DcIpAddress">Resolved IP address of the DC (e.g., "10.0.0.5").</param>
/// <param name="DcRpcPort">Dynamic RPC port discovered via EPM.</param>
/// <param name="DomainDn">Distinguished name derived from domain (e.g., "DC=contoso,DC=com").</param>
/// <param name="ForestName">Forest DNS name (e.g., "contoso.com").</param>
/// <param name="DsaGuid">DC's DSA GUID if available.</param>
/// <param name="FunctionalLevel">Domain functional level if available.</param>
public record DiscoverDomainResponse(
    bool Success,
    string Error,
    string DcHostname,
    string DcIpAddress,
    int? DcRpcPort,
    string DomainDn,
    string ForestName,
    string DsaGuid,
    int? FunctionalLevel
);

/// <summary>
/// Request to start replica DC provisioning by replicating from a source DC.
/// Supports both HTTP transport (with SourceDcUrl) and RPC transport (with DomainName).
/// </summary>
/// <param name="SourceDcUrl">Base URL of the source DC's DRS HTTP service (for HTTP transport).</param>
/// <param name="DomainName">DNS domain name for Windows DC mode (RPC transport, no URL needed).</param>
/// <param name="AdminUpn">User principal name for authenticating against the source DC.</param>
/// <param name="AdminPassword">Password for the admin account on the source DC.</param>
/// <param name="SiteName">AD site name for this replica DC. Defaults to "Default-First-Site-Name".</param>
/// <param name="Hostname">Hostname override for this DC. Defaults to the machine name if null.</param>
/// <param name="Transport">Transport protocol: "http" for Directory.NET peer DC, "rpc" for Windows AD DC.</param>
public record ProvisionReplicaRequest(
    string SourceDcUrl,
    string DomainName,
    string AdminUpn,
    string AdminPassword,
    string SiteName = "Default-First-Site-Name",
    string Hostname = null,
    string Transport = "http"
);

/// <summary>
/// Detailed progress information for a replication phase during replica provisioning.
/// </summary>
/// <param name="Phase">Friendly name of the current replication phase (e.g., "Schema", "Configuration", "Domain").</param>
/// <param name="NamingContext">Distinguished name of the naming context being replicated.</param>
/// <param name="ObjectsProcessed">Number of objects replicated so far in this phase.</param>
/// <param name="ObjectsTotal">Estimated total objects, if known; null when unknown.</param>
/// <param name="BytesTransferred">Cumulative bytes transferred during replication.</param>
public record ReplicationPhaseProgress(
    string Phase,
    string NamingContext,
    int ObjectsProcessed,
    int? ObjectsTotal,
    long BytesTransferred
);
