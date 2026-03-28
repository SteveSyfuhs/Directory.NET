using System.Text.Json.Serialization;
using Directory.Core.Interfaces;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Directory.Replication;

/// <summary>
/// Tracks schema change entries for replication across instances.
/// Schema changes are published to a dedicated Cosmos DB container and
/// detected by other instances via the change feed.
/// </summary>
public class SchemaChangeEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("changeType")]
    public string ChangeType { get; set; } = string.Empty; // AttributeAdded, AttributeModified, ClassAdded, ClassModified

    [JsonPropertyName("objectName")]
    public string ObjectName { get; set; } = string.Empty; // Attribute or class name

    [JsonPropertyName("schemaVersion")]
    public long SchemaVersion { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("originServer")]
    public string OriginServer { get; set; } = string.Empty;

    [JsonPropertyName("changes")]
    public Dictionary<string, object> Changes { get; set; } = new();

    // Partition key for Cosmos DB
    [JsonPropertyName("partitionKey")]
    public string PartitionKey { get; set; } = "schema";
}

/// <summary>
/// Current schema replication status for this instance.
/// </summary>
public class SchemaReplicationStatus
{
    public long CurrentSchemaVersion { get; set; }
    public DateTimeOffset LastSyncTime { get; set; }
    public string OriginServer { get; set; } = string.Empty;
    public int PendingChanges { get; set; }
    public List<SchemaChangeEntry> RecentChanges { get; set; } = [];
}

/// <summary>
/// Service that manages schema replication via Cosmos DB.
/// Publishes schema changes to the SchemaChanges container and
/// provides replication status information.
/// </summary>
public class SchemaReplicationService
{
    private readonly Func<CosmosClient> _clientFactory;
    private readonly ReplicationOptions _options;
    private readonly string _hostname;
    private readonly ISchemaService _schema;
    private readonly ILogger<SchemaReplicationService> _logger;

    private DateTimeOffset _lastSyncTime = DateTimeOffset.UtcNow;
    private long _lastKnownVersion;
    private int _pendingChanges;

    public SchemaReplicationService(
        Func<CosmosClient> clientFactory,
        IOptions<ReplicationOptions> options,
        ISchemaService schema,
        ILogger<SchemaReplicationService> logger)
    {
        _clientFactory = clientFactory;
        _options = options.Value;
        _hostname = Environment.MachineName;
        _schema = schema;
        _logger = logger;
        _lastKnownVersion = schema.SchemaVersion;
    }

    /// <summary>
    /// Publish a schema change entry so other instances can detect and replicate it.
    /// </summary>
    public async Task PublishChangeAsync(string changeType, string objectName, Dictionary<string, object> changes = null, CancellationToken ct = default)
    {
        var entry = new SchemaChangeEntry
        {
            ChangeType = changeType,
            ObjectName = objectName,
            SchemaVersion = _schema.SchemaVersion,
            Timestamp = DateTimeOffset.UtcNow,
            OriginServer = _hostname,
            Changes = changes ?? new(),
        };

        try
        {
            var container = GetSchemaChangesContainer();
            await container.CreateItemAsync(entry, new PartitionKey(entry.PartitionKey), cancellationToken: ct);
            _logger.LogInformation("Published schema change: {ChangeType} for {ObjectName} (version {Version})",
                changeType, objectName, entry.SchemaVersion);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish schema change for {ObjectName}", objectName);
        }
    }

    /// <summary>
    /// Get the current schema replication status.
    /// </summary>
    public async Task<SchemaReplicationStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var recentChanges = await GetRecentChangesAsync(10, ct);

        return new SchemaReplicationStatus
        {
            CurrentSchemaVersion = _schema.SchemaVersion,
            LastSyncTime = _lastSyncTime,
            OriginServer = _hostname,
            PendingChanges = _pendingChanges,
            RecentChanges = recentChanges,
        };
    }

    /// <summary>
    /// Retrieve the most recent schema change entries.
    /// </summary>
    public async Task<List<SchemaChangeEntry>> GetRecentChangesAsync(int count = 50, CancellationToken ct = default)
    {
        var results = new List<SchemaChangeEntry>();

        try
        {
            var container = GetSchemaChangesContainer();
            var query = new QueryDefinition(
                "SELECT TOP @count * FROM c WHERE c.partitionKey = 'schema' ORDER BY c.timestamp DESC")
                .WithParameter("@count", count);

            using var iterator = container.GetItemQueryIterator<SchemaChangeEntry>(query,
                requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey("schema") });

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync(ct);
                results.AddRange(response);
            }
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogDebug("SchemaChanges container not found; returning empty history");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to retrieve schema change history");
        }

        return results;
    }

    /// <summary>
    /// Force a schema sync by reloading from the directory store.
    /// </summary>
    public async Task ForceSyncAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Forcing schema sync from store");
        await _schema.ReloadSchemaAsync(ct);
        _lastSyncTime = DateTimeOffset.UtcNow;
        _lastKnownVersion = _schema.SchemaVersion;
        _pendingChanges = 0;
    }

    /// <summary>
    /// Called by the change feed processor when schema changes are detected.
    /// </summary>
    internal void OnSchemaChangeDetected(long newVersion)
    {
        if (newVersion > _lastKnownVersion)
        {
            _pendingChanges = (int)(newVersion - _lastKnownVersion);
            _logger.LogInformation("Schema change detected: version {New} (current: {Current}, pending: {Pending})",
                newVersion, _lastKnownVersion, _pendingChanges);
        }
    }

    /// <summary>
    /// Mark sync as completed after schema reload.
    /// </summary>
    internal void MarkSynced()
    {
        _lastSyncTime = DateTimeOffset.UtcNow;
        _lastKnownVersion = _schema.SchemaVersion;
        _pendingChanges = 0;
    }

    private Container GetSchemaChangesContainer()
    {
        var database = _clientFactory().GetDatabase(_options.DatabaseName);
        return database.GetContainer("SchemaChanges");
    }
}

/// <summary>
/// Hosted service that monitors the Cosmos DB change feed for the SchemaChanges container.
/// When changes are detected, invalidates the local schema cache and triggers a reload.
/// </summary>
public class SchemaChangeFeedService : IHostedService
{
    private readonly Func<CosmosClient> _clientFactory;
    private readonly ReplicationOptions _options;
    private readonly string _instanceName;
    private readonly string _hostname;
    private readonly ISchemaService _schema;
    private readonly SchemaReplicationService _replicationService;
    private readonly ILogger<SchemaChangeFeedService> _logger;
    private ChangeFeedProcessor _processor;

    public SchemaChangeFeedService(
        Func<CosmosClient> clientFactory,
        IOptions<ReplicationOptions> options,
        ISchemaService schema,
        SchemaReplicationService replicationService,
        ILogger<SchemaChangeFeedService> logger)
    {
        _clientFactory = clientFactory;
        _options = options.Value;
        _instanceName = $"{Environment.MachineName}-schema";
        _hostname = Environment.MachineName;
        _schema = schema;
        _replicationService = replicationService;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var database = _clientFactory().GetDatabase(_options.DatabaseName);

            // Ensure SchemaChanges container exists
            await database.CreateContainerIfNotExistsAsync(
                new ContainerProperties("SchemaChanges", "/partitionKey"),
                cancellationToken: cancellationToken);

            // Ensure lease container exists
            await database.CreateContainerIfNotExistsAsync(
                new ContainerProperties("SchemaChangeFeedLeases", "/id"),
                cancellationToken: cancellationToken);

            var sourceContainer = database.GetContainer("SchemaChanges");
            var leaseContainer = database.GetContainer("SchemaChangeFeedLeases");

            _processor = sourceContainer
                .GetChangeFeedProcessorBuilder<SchemaChangeEntry>(
                    "SchemaReplication",
                    HandleChangesAsync)
                .WithInstanceName(_instanceName)
                .WithLeaseContainer(leaseContainer)
                .WithStartTime(DateTime.UtcNow.AddMinutes(-5))
                .Build();

            await _processor.StartAsync();
            _logger.LogInformation("Schema change feed processor started");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to start schema change feed processor; schema replication will be unavailable");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_processor is not null)
        {
            await _processor.StopAsync();
            _logger.LogInformation("Schema change feed processor stopped");
        }
    }

    private async Task HandleChangesAsync(
        ChangeFeedProcessorContext context,
        IReadOnlyCollection<SchemaChangeEntry> changes,
        CancellationToken ct)
    {
        _logger.LogDebug("Processing {Count} schema changes from partition {Partition}",
            changes.Count, context.LeaseToken);

        var hasRemoteChanges = false;
        long maxVersion = 0;

        foreach (var change in changes)
        {
            // Skip changes from this instance
            if (string.Equals(change.OriginServer, _hostname, StringComparison.OrdinalIgnoreCase))
                continue;

            hasRemoteChanges = true;
            if (change.SchemaVersion > maxVersion)
                maxVersion = change.SchemaVersion;

            _logger.LogInformation("Schema change from {Server}: {ChangeType} on {Object} (v{Version})",
                change.OriginServer, change.ChangeType, change.ObjectName, change.SchemaVersion);
        }

        if (hasRemoteChanges)
        {
            _replicationService.OnSchemaChangeDetected(maxVersion);

            // Reload schema from the store
            try
            {
                await _schema.ReloadSchemaAsync(ct);
                _replicationService.MarkSynced();
                _logger.LogInformation("Schema reloaded after remote change (version now: {Version})", _schema.SchemaVersion);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reload schema after remote change");
            }
        }
    }
}
