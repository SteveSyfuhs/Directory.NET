namespace Directory.Web.Endpoints;

/// <summary>
/// GET /api/v1/service-settings — returns current service configuration (non-sensitive).
/// This reads from the live configuration and returns port settings, feature states, etc.
/// It does NOT return connection strings or secrets.
/// </summary>
public static class ServiceSettingsEndpoints
{
    public static RouteGroupBuilder MapServiceSettingsEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", (IConfiguration config) =>
        {
            var settings = new
            {
                ldap = new
                {
                    port = config.GetValue("Ldap:Port", 389),
                    tlsPort = config.GetValue("Ldap:TlsPort", 636),
                    gcPort = config.GetValue("Ldap:GcPort", 3268),
                    gcTlsPort = config.GetValue("Ldap:GcTlsPort", 3269),
                    maxConnections = config.GetValue("Ldap:MaxConnections", 10000),
                    maxPageSize = config.GetValue("Ldap:MaxPageSize", 1000),
                    idleTimeoutSeconds = config.GetValue("Ldap:IdleTimeoutSeconds", 900),
                },
                kerberos = new
                {
                    port = config.GetValue("Kerberos:Port", 88),
                    kpasswdPort = config.GetValue("Kerberos:KpasswdPort", 464),
                    defaultRealm = config.GetValue("Kerberos:DefaultRealm", ""),
                    maximumSkew = config.GetValue("Kerberos:MaximumSkew", "00:05:00"),
                    sessionLifetime = config.GetValue("Kerberos:SessionLifetime", "10:00:00"),
                },
                dns = new
                {
                    port = config.GetValue("Dns:Port", 53),
                    serverHostname = config.GetValue("Dns:ServerHostname", ""),
                    defaultTtl = config.GetValue("Dns:DefaultTtl", 600),
                },
                rpc = new
                {
                    endpointMapperPort = config.GetValue("RpcServer:EndpointMapperPort", 1135),
                    servicePort = config.GetValue("RpcServer:ServicePort", 49664),
                },
                replication = new
                {
                    httpPort = config.GetValue("Replication:HttpPort", 9389),
                },
                cosmosDb = new
                {
                    databaseName = config.GetValue("CosmosDb:DatabaseName", "DirectoryService"),
                    isConfigured = !string.IsNullOrEmpty(config["CosmosDb:ConnectionString"]),
                },
                cache = new
                {
                    redisConfigured = !string.IsNullOrEmpty(config["Cache:RedisConnectionString"]),
                },
                namingContexts = new
                {
                    domainDn = config.GetValue("NamingContexts:DomainDn", ""),
                    forestDnsName = config.GetValue("NamingContexts:ForestDnsName", ""),
                },
                environment = new
                {
                    machineName = Environment.MachineName,
                    osVersion = Environment.OSVersion.ToString(),
                    dotnetVersion = Environment.Version.ToString(),
                    processorCount = Environment.ProcessorCount,
                    is64Bit = Environment.Is64BitOperatingSystem,
                }
            };
            return Results.Ok(settings);
        })
        .WithName("GetServiceSettings")
        .WithTags("Settings");

        return group;
    }
}
