using Directory.CosmosDb;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace Directory.Web.Services;

/// <summary>
/// A wrapper around <see cref="CosmosDbInitializer"/> that skips database initialization
/// when the CosmosClient has not been configured yet (first-run scenario).
/// The setup wizard calls <see cref="InitializeAsync"/> after the user provides a valid
/// connection string and the CosmosClientHolder is configured.
/// </summary>
public class DeferredCosmosDbInitializer : IHostedService
{
    private readonly CosmosClientHolder _holder;
    private readonly IOptions<CosmosDbOptions> _options;
    private readonly ILogger<DeferredCosmosDbInitializer> _logger;

    public DeferredCosmosDbInitializer(
        CosmosClientHolder holder,
        IOptions<CosmosDbOptions> options,
        ILogger<DeferredCosmosDbInitializer> logger)
    {
        _holder = holder;
        _options = options;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_holder.IsConfigured)
        {
            _logger.LogInformation("Cosmos DB not configured — skipping database initialization. The setup wizard will initialize the database.");
            return;
        }

        await InitializeAsync(cancellationToken);
    }

    /// <summary>
    /// Initializes Cosmos DB containers. Called at startup if already configured,
    /// or by the setup wizard after the user provides a valid connection string.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var client = _holder.Client;
        var databaseName = _holder.DatabaseName ?? _options.Value.DatabaseName;

        _logger.LogInformation("Initializing Cosmos DB database: {Database}", databaseName);

        var database = (await client.CreateDatabaseIfNotExistsAsync(
            databaseName,
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
                },
            },
            DefaultTimeToLive = -1,
        };

        var throughput = Math.Max(_options.Value.DefaultThroughput, 1000);
        await database.CreateContainerIfNotExistsAsync(
            containerProperties,
            ThroughputProperties.CreateAutoscaleThroughput(throughput),
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
