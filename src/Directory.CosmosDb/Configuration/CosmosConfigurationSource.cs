using Microsoft.Extensions.Configuration;

namespace Directory.CosmosDb.Configuration;

/// <summary>
/// IConfigurationSource that creates CosmosConfigurationProvider.
/// Added to the configuration builder during host startup.
/// </summary>
public class CosmosConfigurationSource : IConfigurationSource
{
    public string ConnectionString { get; set; } = "";
    public string DatabaseName { get; set; } = "DirectoryService";
    public string TenantId { get; set; } = "default";
    public string Hostname { get; set; } = Environment.MachineName;

    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        return new CosmosConfigurationProvider(ConnectionString, DatabaseName, TenantId, Hostname);
    }
}
