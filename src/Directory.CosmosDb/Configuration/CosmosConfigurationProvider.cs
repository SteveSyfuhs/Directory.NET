using System.Text.Json;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;

namespace Directory.CosmosDb.Configuration;

/// <summary>
/// Custom ConfigurationProvider that loads settings from the Cosmos DB Configuration container.
/// Creates its own CosmosClient because this runs before DI is built (bootstrap problem).
/// Cluster-scoped values are loaded first, then node-specific values overlay them.
/// </summary>
public class CosmosConfigurationProvider : ConfigurationProvider, IDisposable
{
    private readonly string _connectionString;
    private readonly string _databaseName;
    private readonly string _tenantId;
    private readonly string _hostname;
    private CosmosClient _client;

    private const string ContainerName = "Configuration";

    public CosmosConfigurationProvider(
        string connectionString, string databaseName,
        string tenantId, string hostname)
    {
        _connectionString = connectionString;
        _databaseName = databaseName;
        _tenantId = tenantId;
        _hostname = hostname;
    }

    public override void Load()
    {
        try
        {
            LoadAsync().GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // Cosmos is not reachable — fall through to appsettings defaults.
            // The warning is logged once the app's logging infrastructure is available
            // via ConfigurationChangeFeedService startup.
        }
    }

    /// <summary>
    /// Reloads configuration from Cosmos DB and triggers IOptionsMonitor notifications.
    /// Called by ConfigurationChangeFeedService when changes are detected.
    /// </summary>
    public async Task ReloadFromCosmosAsync()
    {
        try
        {
            await LoadAsync();
            OnReload();
        }
        catch (Exception)
        {
            // Swallow — keep existing config if Cosmos becomes unreachable.
        }
    }

    private async Task LoadAsync()
    {
        EnsureClient();

        var container = _client.GetDatabase(_databaseName).GetContainer(ContainerName);
        var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var query = new QueryDefinition("SELECT * FROM c WHERE c.tenantId = @tenantId")
            .WithParameter("@tenantId", _tenantId);

        var docs = new List<ConfigurationDocument>();

        using var iterator = container.GetItemQueryIterator<ConfigurationDocument>(
            query, requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(_tenantId) });

        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            docs.AddRange(response);
        }

        // Load cluster-scoped docs first
        foreach (var doc in docs.Where(d => d.Scope == "cluster").OrderBy(d => d.Section))
        {
            FlattenValues(data, doc.Section, doc.Values);
        }

        // Overlay node-specific docs (overrides cluster values)
        var nodeScope = $"node:{_hostname}";
        foreach (var doc in docs.Where(d => d.Scope == nodeScope).OrderBy(d => d.Section))
        {
            FlattenValues(data, doc.Section, doc.Values);
        }

        Data = data;
    }

    /// <summary>
    /// Flattens a Values dictionary into configuration keys like "Section:Key".
    /// Handles nested objects by recursing with ":" separators.
    /// </summary>
    private static void FlattenValues(
        Dictionary<string, string> data,
        string section,
        Dictionary<string, JsonElement> values)
    {
        foreach (var kvp in values)
        {
            var key = $"{section}:{kvp.Key}";
            FlattenJsonElement(data, key, kvp.Value);
        }
    }

    private static void FlattenJsonElement(
        Dictionary<string, string> data, string prefix, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    FlattenJsonElement(data, $"{prefix}:{property.Name}", property.Value);
                }
                break;

            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    FlattenJsonElement(data, $"{prefix}:{index}", item);
                    index++;
                }
                break;

            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                data[prefix] = null;
                break;

            default:
                data[prefix] = element.ToString();
                break;
        }
    }

    private void EnsureClient()
    {
        _client ??= new CosmosClient(_connectionString, new CosmosClientOptions
        {
            SerializerOptions = new CosmosSerializationOptions
            {
                PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase,
            },
            ConnectionMode = ConnectionMode.Direct,
            MaxRetryAttemptsOnRateLimitedRequests = 3,
            MaxRetryWaitTimeOnRateLimitedRequests = TimeSpan.FromSeconds(10),
        });
    }

    public void Dispose()
    {
        _client?.Dispose();
    }
}
