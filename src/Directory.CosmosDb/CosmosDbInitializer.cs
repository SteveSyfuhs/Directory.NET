using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Directory.CosmosDb;

/// <summary>
/// Creates Cosmos DB database and containers on startup with proper partition keys and indexing.
/// </summary>
public class CosmosDbInitializer : IHostedService
{
    private readonly CosmosClient _client;
    private readonly CosmosDbOptions _options;
    private readonly ILogger<CosmosDbInitializer> _logger;

    public CosmosDbInitializer(CosmosClient client, IOptions<CosmosDbOptions> options, ILogger<CosmosDbInitializer> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Initializing Cosmos DB database: {Database}", _options.DatabaseName);

        var database = (await _client.CreateDatabaseIfNotExistsAsync(
            _options.DatabaseName,
            cancellationToken: cancellationToken)).Database;

        // DirectoryObjects container with hierarchical partition keys
        await CreateDirectoryObjectsContainer(database, cancellationToken);

        // Schema container
        await CreateSimpleContainer(database, "Schema", "/tenantId", cancellationToken);

        // Configuration container
        await CreateSimpleContainer(database, "Configuration", "/tenantId", cancellationToken);

        // ChangeLog container for replication
        await CreateChangeLogContainer(database, cancellationToken);

        // USN counter container
        await CreateSimpleContainer(database, "UsnCounters", "/tenantId", cancellationToken);

        // Audit log container with 90-day TTL
        await CreateAuditLogContainer(database, cancellationToken);

        _logger.LogInformation("Cosmos DB initialization complete");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task CreateDirectoryObjectsContainer(Database database, CancellationToken ct)
    {
        var containerProperties = new ContainerProperties("DirectoryObjects", ["/tenantId", "/domainDn", "/objectCategory"])
        {
            IndexingPolicy = new IndexingPolicy
            {
                Automatic = true,
                IndexingMode = IndexingMode.Consistent,
                IncludedPaths =
                {
                    new IncludedPath { Path = "/*" },
                },
                ExcludedPaths =
                {
                    new ExcludedPath { Path = "/attributes/*" },
                    new ExcludedPath { Path = "/ntHash/?" },
                    new ExcludedPath { Path = "/kerberosKeys/*" },
                    new ExcludedPath { Path = "/\"_etag\"/?" },
                },
                CompositeIndexes =
                {
                    new System.Collections.ObjectModel.Collection<CompositePath>
                    {
                        new() { Path = "/tenantId", Order = CompositePathSortOrder.Ascending },
                        new() { Path = "/domainDn", Order = CompositePathSortOrder.Ascending },
                        new() { Path = "/usnChanged", Order = CompositePathSortOrder.Ascending },
                    },
                    new System.Collections.ObjectModel.Collection<CompositePath>
                    {
                        new() { Path = "/tenantId", Order = CompositePathSortOrder.Ascending },
                        new() { Path = "/domainDn", Order = CompositePathSortOrder.Ascending },
                        new() { Path = "/sAMAccountName", Order = CompositePathSortOrder.Ascending },
                    },
                    new System.Collections.ObjectModel.Collection<CompositePath>
                    {
                        new() { Path = "/tenantId", Order = CompositePathSortOrder.Ascending },
                        new() { Path = "/userPrincipalName", Order = CompositePathSortOrder.Ascending },
                    },
                    new System.Collections.ObjectModel.Collection<CompositePath>
                    {
                        new() { Path = "/tenantId", Order = CompositePathSortOrder.Ascending },
                        new() { Path = "/domainDn", Order = CompositePathSortOrder.Ascending },
                        new() { Path = "/parentDn", Order = CompositePathSortOrder.Ascending },
                    },
                },
            },
            DefaultTimeToLive = -1, // Enable TTL but don't default-expire
        };

        await database.CreateContainerIfNotExistsAsync(
            containerProperties,
            ThroughputProperties.CreateAutoscaleThroughput(Math.Max(_options.MaxAutoscaleThroughput, 1000)),
            cancellationToken: ct);

        _logger.LogInformation("DirectoryObjects container initialized with hierarchical partition keys");
    }

    private async Task CreateChangeLogContainer(Database database, CancellationToken ct)
    {
        var containerProperties = new ContainerProperties("ChangeLog", ["/tenantId", "/domainDn"])
        {
            IndexingPolicy = new IndexingPolicy
            {
                Automatic = true,
                IndexingMode = IndexingMode.Consistent,
                IncludedPaths =
                {
                    new IncludedPath { Path = "/*" },
                },
            },
            DefaultTimeToLive = 30 * 24 * 60 * 60, // 30 days retention
        };

        await database.CreateContainerIfNotExistsAsync(
            containerProperties,
            cancellationToken: ct);

        _logger.LogInformation("ChangeLog container initialized");
    }

    private async Task CreateAuditLogContainer(Database database, CancellationToken ct)
    {
        var containerProperties = new ContainerProperties("AuditLog", "/tenantId")
        {
            IndexingPolicy = new IndexingPolicy
            {
                Automatic = true,
                IndexingMode = IndexingMode.Consistent,
                IncludedPaths =
                {
                    new IncludedPath { Path = "/*" },
                },
                ExcludedPaths =
                {
                    new ExcludedPath { Path = "/details/*" },
                    new ExcludedPath { Path = "/\"_etag\"/?" },
                },
                CompositeIndexes =
                {
                    new System.Collections.ObjectModel.Collection<CompositePath>
                    {
                        new() { Path = "/tenantId", Order = CompositePathSortOrder.Ascending },
                        new() { Path = "/timestamp", Order = CompositePathSortOrder.Descending },
                    },
                },
            },
            DefaultTimeToLive = 7776000, // 90 days
        };

        await database.CreateContainerIfNotExistsAsync(
            containerProperties,
            cancellationToken: ct);

        _logger.LogInformation("AuditLog container initialized with 90-day TTL");
    }

    private async Task CreateSimpleContainer(Database database, string name, string partitionKeyPath, CancellationToken ct)
    {
        await database.CreateContainerIfNotExistsAsync(
            new ContainerProperties(name, partitionKeyPath),
            cancellationToken: ct);

        _logger.LogInformation("Container {Name} initialized", name);
    }
}
