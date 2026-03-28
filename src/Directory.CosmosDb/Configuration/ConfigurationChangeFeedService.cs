using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Directory.CosmosDb.Configuration;

/// <summary>
/// Hosted service that watches the Configuration container's change feed.
/// When configuration documents change, it triggers a reload of the
/// CosmosConfigurationProvider so IOptionsMonitor subscribers get updated values.
/// </summary>
public class ConfigurationChangeFeedService : IHostedService
{
    private readonly CosmosClient _client;
    private readonly CosmosDbOptions _cosmosOptions;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ConfigurationChangeFeedService> _logger;
    private ChangeFeedProcessor _processor;

    public ConfigurationChangeFeedService(
        CosmosClient client,
        IOptions<CosmosDbOptions> cosmosOptions,
        IConfiguration configuration,
        ILogger<ConfigurationChangeFeedService> logger)
    {
        _client = client;
        _cosmosOptions = cosmosOptions.Value;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var database = _client.GetDatabase(_cosmosOptions.DatabaseName);
            var sourceContainer = database.GetContainer("Configuration");

            // Ensure lease container exists
            try
            {
                await database.CreateContainerIfNotExistsAsync(
                    new ContainerProperties("ChangeFeedLeases", "/id"),
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not create lease container for configuration change feed; it may already exist");
            }

            var leasesContainer = database.GetContainer("ChangeFeedLeases");

            _processor = sourceContainer
                .GetChangeFeedProcessorBuilder<ConfigurationDocument>(
                    "ConfigurationReload",
                    HandleChangesAsync)
                .WithInstanceName($"config-{Environment.MachineName}")
                .WithLeaseContainer(leasesContainer)
                .WithStartTime(DateTime.UtcNow)
                .Build();

            await _processor.StartAsync();

            _logger.LogInformation("Configuration change feed processor started");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not start configuration change feed processor; configuration will not auto-reload");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_processor is not null)
        {
            try
            {
                await _processor.StopAsync();
                _logger.LogInformation("Configuration change feed processor stopped");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error stopping configuration change feed processor");
            }
        }
    }

    private async Task HandleChangesAsync(
        ChangeFeedProcessorContext context,
        IReadOnlyCollection<ConfigurationDocument> changes,
        CancellationToken ct)
    {
        _logger.LogInformation("Configuration change detected: {Count} document(s) changed", changes.Count);

        foreach (var change in changes)
        {
            _logger.LogInformation("Configuration changed: {Section} (scope={Scope}, tenant={Tenant})",
                change.Section, change.Scope, change.TenantId);
        }

        // Find the CosmosConfigurationProvider in the configuration root and trigger reload
        if (_configuration is IConfigurationRoot configRoot)
        {
            foreach (var provider in configRoot.Providers)
            {
                if (provider is CosmosConfigurationProvider cosmosProvider)
                {
                    await cosmosProvider.ReloadFromCosmosAsync();
                    _logger.LogInformation("Configuration reloaded from Cosmos DB");
                    break;
                }
            }
        }
    }
}
