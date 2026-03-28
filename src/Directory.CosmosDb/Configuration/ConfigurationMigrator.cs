using System.Text.Json;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Directory.CosmosDb.Configuration;

/// <summary>
/// Hosted service that runs once at startup to seed Cosmos DB configuration
/// documents from the current IConfiguration (appsettings.json values).
/// Idempotent — does nothing if documents already exist.
/// </summary>
public class ConfigurationMigrator : IHostedService
{
    private readonly CosmosClient _client;
    private readonly CosmosDbOptions _cosmosOptions;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ConfigurationMigrator> _logger;
    private readonly string _hostname;
    private readonly string _tenantId;

    private const string ContainerName = "Configuration";

    /// <summary>
    /// Known configuration sections to migrate.
    /// Key = section name in config, Value = section name in appsettings.json.
    /// </summary>
    private static readonly string[] ClusterSections =
    [
        "Cache",
        "Ldap",
        "Kerberos",
        "Dns",
        "RpcServer",
        "Replication",
    ];

    public ConfigurationMigrator(
        CosmosClient client,
        IOptions<CosmosDbOptions> cosmosOptions,
        IConfiguration configuration,
        ILogger<ConfigurationMigrator> logger)
    {
        _client = client;
        _cosmosOptions = cosmosOptions.Value;
        _configuration = configuration;
        _logger = logger;
        _hostname = _configuration["DcNode:Hostname"] ?? Environment.MachineName;
        _tenantId = _configuration["DcNode:TenantId"] ?? "default";
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var container = _client.GetDatabase(_cosmosOptions.DatabaseName).GetContainer(ContainerName);

            // Migrate cluster-scoped sections
            foreach (var section in ClusterSections)
            {
                await MigrateSectionAsync(container, "cluster", section, cancellationToken);
            }

            // Migrate DcNode as both a cluster default and a node-specific override
            await MigrateSectionAsync(container, "cluster", "DcNode", cancellationToken);
            await MigrateSectionAsync(container, $"node:{_hostname}", "DcNode", cancellationToken);

            _logger.LogInformation("Configuration migration check complete");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Configuration migration failed; existing Cosmos config (if any) is unchanged");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task MigrateSectionAsync(
        Container container, string scope, string sectionName, CancellationToken ct)
    {
        var docId = $"{scope}::{sectionName}";

        // Check if document already exists
        try
        {
            await container.ReadItemAsync<ConfigurationDocument>(
                docId, new PartitionKey(_tenantId), cancellationToken: ct);
            // Already exists — skip
            return;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Does not exist — proceed with migration
        }

        // Read current values from IConfiguration for this section
        var configSection = _configuration.GetSection(sectionName);
        var values = new Dictionary<string, JsonElement>();

        foreach (var child in configSection.GetChildren())
        {
            var element = ConvertConfigToJsonElement(child);
            if (element.HasValue)
            {
                values[child.Key] = element.Value;
            }
        }

        if (values.Count == 0)
        {
            _logger.LogDebug("No values found for section {Section}, skipping migration", sectionName);
            return;
        }

        var doc = new ConfigurationDocument
        {
            Id = docId,
            TenantId = _tenantId,
            Scope = scope,
            Section = sectionName,
            Version = 1,
            ModifiedBy = "migrator",
            ModifiedAt = DateTimeOffset.UtcNow,
            Values = values,
        };

        await container.CreateItemAsync(doc, new PartitionKey(_tenantId), cancellationToken: ct);
        _logger.LogInformation("Migrated configuration section {Section} (scope={Scope}) with {Count} values",
            sectionName, scope, values.Count);
    }

    /// <summary>
    /// Converts an IConfigurationSection to a JsonElement, preserving structure for nested objects and arrays.
    /// </summary>
    private static JsonElement? ConvertConfigToJsonElement(IConfigurationSection section)
    {
        var children = section.GetChildren().ToList();

        if (children.Count == 0)
        {
            // Leaf value
            var value = section.Value;
            if (value == null)
                return null;

            // Try to preserve type information
            if (bool.TryParse(value, out var boolVal))
                return JsonSerializer.SerializeToElement(boolVal);
            if (int.TryParse(value, out var intVal))
                return JsonSerializer.SerializeToElement(intVal);
            if (long.TryParse(value, out var longVal))
                return JsonSerializer.SerializeToElement(longVal);
            if (double.TryParse(value, out var doubleVal) && !value.Contains(':'))
                return JsonSerializer.SerializeToElement(doubleVal);

            return JsonSerializer.SerializeToElement(value);
        }

        // Check if children are indexed (array) or named (object)
        var isArray = children.All(c => int.TryParse(c.Key, out _));

        if (isArray)
        {
            var items = new List<JsonElement>();
            foreach (var child in children.OrderBy(c => int.Parse(c.Key)))
            {
                var element = ConvertConfigToJsonElement(child);
                if (element.HasValue)
                    items.Add(element.Value);
            }
            return JsonSerializer.SerializeToElement(items);
        }
        else
        {
            var dict = new Dictionary<string, JsonElement>();
            foreach (var child in children)
            {
                var element = ConvertConfigToJsonElement(child);
                if (element.HasValue)
                    dict[child.Key] = element.Value;
            }
            return JsonSerializer.SerializeToElement(dict);
        }
    }
}
