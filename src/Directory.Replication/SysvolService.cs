using System.Security.Cryptography;
using System.Text.Json.Serialization;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Directory.Replication;

/// <summary>
/// Represents a file entry in the SYSVOL store.
/// In traditional AD, SYSVOL is replicated via DFS-R and contains Group Policy templates
/// and logon scripts. This cloud-native implementation stores entries in Cosmos DB.
/// </summary>
public class SysvolEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty; // e.g., "Policies/{GUID}/Machine/Registry.pol"

    [JsonPropertyName("contentType")]
    public string ContentType { get; set; } = "application/octet-stream";

    [JsonPropertyName("content")]
    public byte[] Content { get; set; }

    [JsonPropertyName("contentHash")]
    public string ContentHash { get; set; } = string.Empty; // SHA256 for integrity

    [JsonPropertyName("version")]
    public long Version { get; set; } = 1;

    [JsonPropertyName("lastModified")]
    public DateTimeOffset LastModified { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("modifiedBy")]
    public string ModifiedBy { get; set; } = string.Empty;

    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; set; }

    [JsonPropertyName("isDeleted")]
    public bool IsDeleted { get; set; }

    // Partition key: top-level folder (e.g., "Policies", "scripts")
    [JsonPropertyName("partitionKey")]
    public string PartitionKey { get; set; } = "sysvol";
}

/// <summary>
/// Represents a replication conflict where two instances modified the same file concurrently.
/// </summary>
public class SysvolConflict
{
    public string Path { get; set; } = string.Empty;
    public long LocalVersion { get; set; }
    public long RemoteVersion { get; set; }
    public DateTimeOffset DetectedAt { get; set; }
    public string LocalModifiedBy { get; set; } = string.Empty;
    public string RemoteModifiedBy { get; set; } = string.Empty;
}

/// <summary>
/// Overall SYSVOL replication health status.
/// </summary>
public class SysvolReplicationStatus
{
    public int TotalFiles { get; set; }
    public long TotalSizeBytes { get; set; }
    public DateTimeOffset LastReplicationTime { get; set; }
    public int PendingChanges { get; set; }
    public string ReplicationHealth { get; set; } = "Healthy"; // Healthy, Warning, Error
}

/// <summary>
/// Cloud-native SYSVOL service using Cosmos DB for storage and change feed for replication.
/// Provides file management operations analogous to DFS-R replicated SYSVOL.
/// </summary>
public class SysvolService
{
    private readonly Func<CosmosClient> _clientFactory;
    private readonly ReplicationOptions _options;
    private readonly ILogger<SysvolService> _logger;

    private DateTimeOffset _lastReplicationTime = DateTimeOffset.UtcNow;
    private int _pendingChanges;
    private readonly List<SysvolConflict> _conflicts = [];

    public SysvolService(
        Func<CosmosClient> clientFactory,
        IOptions<ReplicationOptions> options,
        ILogger<SysvolService> logger)
    {
        _clientFactory = clientFactory;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// List SYSVOL entries under a given path prefix.
    /// </summary>
    public async Task<List<SysvolEntry>> ListFilesAsync(string path = "", CancellationToken ct = default)
    {
        var results = new List<SysvolEntry>();

        try
        {
            var container = GetSysvolContainer();

            // Normalize path
            path = NormalizePath(path);

            QueryDefinition query;
            if (string.IsNullOrEmpty(path))
            {
                query = new QueryDefinition(
                    "SELECT * FROM c WHERE c.isDeleted = false ORDER BY c.path");
            }
            else
            {
                query = new QueryDefinition(
                    "SELECT * FROM c WHERE STARTSWITH(c.path, @path) AND c.isDeleted = false ORDER BY c.path")
                    .WithParameter("@path", path);
            }

            using var iterator = container.GetItemQueryIterator<SysvolEntry>(query);
            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync(ct);
                results.AddRange(response);
            }
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogDebug("Sysvol container not found");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list SYSVOL files at path '{Path}'", path);
        }

        return results;
    }

    /// <summary>
    /// Get a specific SYSVOL file by path.
    /// </summary>
    public async Task<SysvolEntry> GetFileAsync(string path, CancellationToken ct = default)
    {
        path = NormalizePath(path);

        try
        {
            var container = GetSysvolContainer();
            var query = new QueryDefinition(
                "SELECT * FROM c WHERE c.path = @path AND c.isDeleted = false")
                .WithParameter("@path", path);

            using var iterator = container.GetItemQueryIterator<SysvolEntry>(query);
            if (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync(ct);
                return response.FirstOrDefault();
            }
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogDebug("SYSVOL file not found: {Path}", path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get SYSVOL file: {Path}", path);
        }

        return null;
    }

    /// <summary>
    /// Create or update a SYSVOL file. Computes content hash and increments version.
    /// </summary>
    public async Task<SysvolEntry> PutFileAsync(string path, byte[] content, string contentType, string modifiedBy, CancellationToken ct = default)
    {
        path = NormalizePath(path);
        var partitionKey = GetPartitionKey(path);
        var contentHash = ComputeHash(content);

        var existing = await GetFileAsync(path, ct);

        var entry = existing ?? new SysvolEntry();
        entry.Path = path;
        entry.Content = content;
        entry.ContentType = contentType;
        entry.ContentHash = contentHash;
        entry.Version = (existing?.Version ?? 0) + 1;
        entry.LastModified = DateTimeOffset.UtcNow;
        entry.ModifiedBy = modifiedBy;
        entry.SizeBytes = content.Length;
        entry.IsDeleted = false;
        entry.PartitionKey = partitionKey;

        try
        {
            var container = GetSysvolContainer();

            if (existing != null)
            {
                await container.ReplaceItemAsync(entry, entry.Id,
                    new PartitionKey(partitionKey), cancellationToken: ct);
                _logger.LogInformation("Updated SYSVOL file: {Path} (v{Version})", path, entry.Version);
            }
            else
            {
                await container.CreateItemAsync(entry,
                    new PartitionKey(partitionKey), cancellationToken: ct);
                _logger.LogInformation("Created SYSVOL file: {Path}", path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write SYSVOL file: {Path}", path);
            throw;
        }

        return entry;
    }

    /// <summary>
    /// Soft-delete a SYSVOL file.
    /// </summary>
    public async Task<bool> DeleteFileAsync(string path, CancellationToken ct = default)
    {
        path = NormalizePath(path);

        var existing = await GetFileAsync(path, ct);
        if (existing == null)
            return false;

        existing.IsDeleted = true;
        existing.LastModified = DateTimeOffset.UtcNow;
        existing.Content = null;

        try
        {
            var container = GetSysvolContainer();
            await container.ReplaceItemAsync(existing, existing.Id,
                new PartitionKey(existing.PartitionKey), cancellationToken: ct);
            _logger.LogInformation("Deleted SYSVOL file: {Path}", path);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete SYSVOL file: {Path}", path);
            return false;
        }
    }

    /// <summary>
    /// Get overall SYSVOL replication status.
    /// </summary>
    public async Task<SysvolReplicationStatus> GetReplicationStatusAsync(CancellationToken ct = default)
    {
        var files = await ListFilesAsync("", ct);

        return new SysvolReplicationStatus
        {
            TotalFiles = files.Count,
            TotalSizeBytes = files.Sum(f => f.SizeBytes),
            LastReplicationTime = _lastReplicationTime,
            PendingChanges = _pendingChanges,
            ReplicationHealth = _pendingChanges == 0 ? "Healthy" : _pendingChanges < 5 ? "Warning" : "Error",
        };
    }

    /// <summary>
    /// Get any replication conflicts detected.
    /// </summary>
    public List<SysvolConflict> GetReplicationConflicts()
    {
        lock (_conflicts)
        {
            return [.. _conflicts];
        }
    }

    /// <summary>
    /// Record a replication conflict.
    /// </summary>
    internal void AddConflict(SysvolConflict conflict)
    {
        lock (_conflicts)
        {
            _conflicts.Add(conflict);
            if (_conflicts.Count > 100)
                _conflicts.RemoveAt(0);
        }
    }

    /// <summary>
    /// Mark replication as complete, resetting pending changes.
    /// </summary>
    internal void MarkReplicated()
    {
        _lastReplicationTime = DateTimeOffset.UtcNow;
        _pendingChanges = 0;
    }

    private Container GetSysvolContainer()
    {
        var database = _clientFactory().GetDatabase(_options.DatabaseName);
        return database.GetContainer("Sysvol");
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return string.Empty;

        // Normalize slashes and remove leading/trailing
        return path.Replace('\\', '/').Trim('/');
    }

    private static string GetPartitionKey(string path)
    {
        // Use top-level folder as partition key, or "root" for top-level files
        var firstSlash = path.IndexOf('/');
        return firstSlash > 0 ? path[..firstSlash] : "root";
    }

    private static string ComputeHash(byte[] content)
    {
        var hash = SHA256.HashData(content);
        return Convert.ToHexString(hash);
    }
}
