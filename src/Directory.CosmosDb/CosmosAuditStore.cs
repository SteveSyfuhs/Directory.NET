using System.Net;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Directory.CosmosDb;

/// <summary>
/// Cosmos DB implementation of IAuditService. Stores audit entries in the AuditLog container
/// with a 90-day default TTL.
/// </summary>
public class CosmosAuditStore : IAuditService
{
    private readonly Func<CosmosClient> _clientFactory;
    private readonly CosmosDbOptions _options;
    private readonly ILogger<CosmosAuditStore> _logger;
    private Container AuditLog => _clientFactory().GetContainer(_options.DatabaseName, "AuditLog");

    public CosmosAuditStore(Func<CosmosClient> clientFactory, IOptions<CosmosDbOptions> options, ILogger<CosmosAuditStore> logger)
    {
        _clientFactory = clientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task LogAsync(AuditEntry entry, CancellationToken ct = default)
    {
        try
        {
            await AuditLog.CreateItemAsync(entry, new PartitionKey(entry.TenantId), cancellationToken: ct);
        }
        catch (Exception ex)
        {
            // Audit logging should never break the primary operation
            _logger.LogError(ex, "Failed to write audit entry for {Action} on {TargetDn}", entry.Action, entry.TargetDn);
        }
    }

    public async Task<(IReadOnlyList<AuditEntry> Items, string ContinuationToken)> QueryAsync(
        string tenantId, string action = null, string targetDn = null,
        DateTimeOffset? from = null, DateTimeOffset? to = null,
        int pageSize = 50, string continuationToken = null, CancellationToken ct = default)
    {
        try
        {
            var clauses = new List<string> { "c.tenantId = @tenantId" };
            var queryDef = new QueryDefinition("SELECT * FROM c");

            if (!string.IsNullOrEmpty(action))
                clauses.Add("c.action = @action");
            if (!string.IsNullOrEmpty(targetDn))
                clauses.Add("c.targetDn = @targetDn");
            if (from.HasValue)
                clauses.Add("c.timestamp >= @from");
            if (to.HasValue)
                clauses.Add("c.timestamp <= @to");

            var sql = $"SELECT * FROM c WHERE {string.Join(" AND ", clauses)} ORDER BY c.timestamp DESC";
            queryDef = new QueryDefinition(sql)
                .WithParameter("@tenantId", tenantId);

            if (!string.IsNullOrEmpty(action))
                queryDef = queryDef.WithParameter("@action", action);
            if (!string.IsNullOrEmpty(targetDn))
                queryDef = queryDef.WithParameter("@targetDn", targetDn);
            if (from.HasValue)
                queryDef = queryDef.WithParameter("@from", from.Value);
            if (to.HasValue)
                queryDef = queryDef.WithParameter("@to", to.Value);

            var requestOptions = new QueryRequestOptions
            {
                PartitionKey = new PartitionKey(tenantId),
                MaxItemCount = pageSize,
            };

            var items = new List<AuditEntry>();
            string nextToken = null;

            using var feed = AuditLog.GetItemQueryIterator<AuditEntry>(queryDef, continuationToken, requestOptions);
            if (feed.HasMoreResults)
            {
                var response = await feed.ReadNextAsync(ct);
                items.AddRange(response);
                nextToken = response.ContinuationToken;
            }

            return (items, nextToken);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // AuditLog container may not exist yet (pre-existing database without the container)
            _logger.LogWarning("AuditLog container not found — returning empty results. Restart the service to auto-create the container.");
            return (Array.Empty<AuditEntry>(), null);
        }
    }

    public async Task<AuditEntry> GetByIdAsync(string tenantId, string id, CancellationToken ct = default)
    {
        try
        {
            var response = await AuditLog.ReadItemAsync<AuditEntry>(id, new PartitionKey(tenantId), cancellationToken: ct);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }
}
