using System.Net;
using System.Text.Json;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace Directory.CosmosDb.Configuration;

/// <summary>
/// CRUD operations for configuration documents in the Cosmos DB Configuration container.
/// Used by the management API and the configuration migrator.
/// </summary>
public class CosmosConfigurationStore
{
    private readonly CosmosClient _client;
    private readonly string _databaseName;
    private readonly ILogger<CosmosConfigurationStore> _logger;

    private const string ContainerName = "Configuration";

    public CosmosConfigurationStore(
        CosmosClient client,
        Microsoft.Extensions.Options.IOptions<CosmosDbOptions> options,
        ILogger<CosmosConfigurationStore> logger)
    {
        _client = client;
        _databaseName = options.Value.DatabaseName;
        _logger = logger;
    }

    private Container GetContainer() =>
        _client.GetDatabase(_databaseName).GetContainer(ContainerName);

    /// <summary>
    /// Gets a single configuration section by scope and section name.
    /// The document id is "{scope}::{section}".
    /// </summary>
    public async Task<ConfigurationDocument> GetSectionAsync(
        string tenantId, string scope, string section, CancellationToken ct = default)
    {
        var container = GetContainer();
        var id = $"{scope}::{section}";

        try
        {
            var response = await container.ReadItemAsync<ConfigurationDocument>(
                id, new PartitionKey(tenantId), cancellationToken: ct);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>
    /// Gets all configuration documents for a tenant.
    /// </summary>
    public async Task<List<ConfigurationDocument>> GetAllSectionsAsync(
        string tenantId, CancellationToken ct = default)
    {
        var container = GetContainer();
        var query = new QueryDefinition("SELECT * FROM c WHERE c.tenantId = @tenantId")
            .WithParameter("@tenantId", tenantId);

        var results = new List<ConfigurationDocument>();
        using var iterator = container.GetItemQueryIterator<ConfigurationDocument>(
            query, requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(tenantId) });

        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(ct);
            results.AddRange(response);
        }

        return results;
    }

    /// <summary>
    /// Upserts a configuration document with optimistic concurrency via ETag.
    /// Throws CosmosException with 409/412 on conflict.
    /// </summary>
    public async Task<ConfigurationDocument> UpsertSectionAsync(
        ConfigurationDocument doc, CancellationToken ct = default)
    {
        var container = GetContainer();
        doc.ModifiedAt = DateTimeOffset.UtcNow;

        var requestOptions = new ItemRequestOptions();
        if (!string.IsNullOrEmpty(doc.ETag))
        {
            requestOptions.IfMatchEtag = doc.ETag;
        }

        var response = await container.UpsertItemAsync(
            doc,
            new PartitionKey(doc.TenantId),
            requestOptions,
            ct);

        _logger.LogInformation("Upserted configuration {Id} for tenant {Tenant} (v{Version})",
            doc.Id, doc.TenantId, doc.Version);

        return response.Resource;
    }

    /// <summary>
    /// Deletes a configuration document.
    /// </summary>
    public async Task DeleteSectionAsync(string tenantId, string id, CancellationToken ct = default)
    {
        var container = GetContainer();

        try
        {
            await container.DeleteItemAsync<ConfigurationDocument>(
                id, new PartitionKey(tenantId), cancellationToken: ct);
            _logger.LogInformation("Deleted configuration {Id} for tenant {Tenant}", id, tenantId);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Configuration {Id} not found for deletion", id);
        }
    }

    /// <summary>
    /// Gets all node-scoped override documents for a specific hostname.
    /// </summary>
    public async Task<List<ConfigurationDocument>> GetNodeOverridesAsync(
        string tenantId, string hostname, CancellationToken ct = default)
    {
        var container = GetContainer();
        var scopePrefix = $"node:{hostname}";
        var query = new QueryDefinition(
                "SELECT * FROM c WHERE c.tenantId = @tenantId AND STARTSWITH(c.scope, @scopePrefix)")
            .WithParameter("@tenantId", tenantId)
            .WithParameter("@scopePrefix", scopePrefix);

        var results = new List<ConfigurationDocument>();
        using var iterator = container.GetItemQueryIterator<ConfigurationDocument>(
            query, requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(tenantId) });

        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(ct);
            results.AddRange(response);
        }

        return results;
    }

    /// <summary>
    /// Convenience method: gets a typed value from a specific key within a configuration section.
    /// </summary>
    public async Task<T> GetAsync<T>(string scope, string section, string key, CancellationToken ct = default)
    {
        var doc = await GetSectionAsync("default", scope, section, ct);
        if (doc == null) return default;
        if (!doc.Values.TryGetValue(key, out var element)) return default;
        return JsonSerializer.Deserialize<T>(element.GetRawText());
    }

    /// <summary>
    /// Convenience method: sets a typed value for a specific key within a configuration section.
    /// </summary>
    public async Task SetAsync<T>(string scope, string section, string key, T value, CancellationToken ct = default)
    {
        var doc = await GetSectionAsync("default", scope, section, ct) ?? new ConfigurationDocument
        {
            Id = $"{scope}::{section}",
            TenantId = "default",
            Scope = scope,
            Section = section,
        };
        var json = JsonSerializer.SerializeToElement(value);
        doc.Values[key] = json;
        await UpsertSectionAsync(doc, ct);
    }

    /// <summary>
    /// Gets distinct hostnames that have node-scoped configuration documents.
    /// </summary>
    public async Task<List<string>> GetRegisteredNodesAsync(
        string tenantId, CancellationToken ct = default)
    {
        var container = GetContainer();
        var query = new QueryDefinition(
            "SELECT DISTINCT VALUE SUBSTRING(c.scope, 5, LENGTH(c.scope) - 5) FROM c WHERE c.tenantId = @tenantId AND STARTSWITH(c.scope, 'node:')")
            .WithParameter("@tenantId", tenantId);

        var results = new List<string>();
        using var iterator = container.GetItemQueryIterator<string>(
            query, requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(tenantId) });

        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(ct);
            results.AddRange(response);
        }

        return results;
    }
}
