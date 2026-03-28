using Microsoft.Extensions.Configuration;

namespace Directory.CosmosDb.Configuration;

/// <summary>
/// Extension methods for adding Cosmos DB configuration to the configuration builder.
/// </summary>
public static class CosmosConfigurationExtensions
{
    /// <summary>
    /// Adds Cosmos DB as a configuration source. Configuration documents from the
    /// Configuration container are loaded after appsettings.json, so they take precedence.
    /// </summary>
    public static IConfigurationBuilder AddCosmosConfiguration(
        this IConfigurationBuilder builder,
        string connectionString,
        string databaseName,
        string tenantId = "default",
        string hostname = null)
    {
        return builder.Add(new CosmosConfigurationSource
        {
            ConnectionString = connectionString,
            DatabaseName = databaseName,
            TenantId = tenantId,
            Hostname = hostname ?? Environment.MachineName,
        });
    }
}
