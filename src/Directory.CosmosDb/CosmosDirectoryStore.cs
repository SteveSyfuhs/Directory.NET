using System.Net;
using System.Runtime.CompilerServices;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Directory.CosmosDb;

/// <summary>
/// Cosmos DB implementation of IDirectoryStore using hierarchical partition keys.
/// </summary>
public class CosmosDirectoryStore : IDirectoryStore, IStreamingDirectoryStore
{
    private readonly Func<CosmosClient> _clientFactory;
    private readonly CosmosDbOptions _options;
    private readonly ILogger<CosmosDirectoryStore> _logger;
    private Container DirectoryObjects => _clientFactory().GetContainer(_options.DatabaseName, "DirectoryObjects");
    private Container UsnCounters => _clientFactory().GetContainer(_options.DatabaseName, "UsnCounters");

    /// <summary>
    /// Creates a new CosmosDirectoryStore using a factory function to resolve the CosmosClient.
    /// This supports scenarios where the client may be reconfigured at runtime (e.g., setup wizard).
    /// </summary>
    public CosmosDirectoryStore(Func<CosmosClient> clientFactory, IOptions<CosmosDbOptions> options, ILogger<CosmosDirectoryStore> logger)
    {
        _clientFactory = clientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<DirectoryObject> GetByDnAsync(string tenantId, string dn, CancellationToken ct = default)
    {
        var id = dn.ToLowerInvariant();

        try
        {
            // We don't know objectCategory from a DN alone, so we can't do a full point read.
            // However, we constrain to the tenantId partition key level to avoid cross-partition scan.
            var query = new QueryDefinition("SELECT * FROM c WHERE c.id = @id AND c.tenantId = @tenantId")
                .WithParameter("@id", id)
                .WithParameter("@tenantId", tenantId);

            var options = new QueryRequestOptions
            {
                PartitionKey = new PartitionKeyBuilder()
                    .Add(tenantId)
                    .Build(),
                MaxItemCount = 1,
            };

            using var feed = DirectoryObjects.GetItemQueryIterator<DirectoryObject>(query, requestOptions: options);
            while (feed.HasMoreResults)
            {
                var response = await feed.ReadNextAsync(ct);
                var result = response.FirstOrDefault();
                if (result is not null) return result;
            }
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        return null;
    }

    public async Task<DirectoryObject> GetByGuidAsync(string tenantId, string guid, CancellationToken ct = default)
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.objectGuid = @guid AND c.tenantId = @tenantId")
            .WithParameter("@guid", guid)
            .WithParameter("@tenantId", tenantId);

        var options = new QueryRequestOptions
        {
            PartitionKey = new PartitionKeyBuilder()
                .Add(tenantId)
                .Build(),
            MaxItemCount = 1,
        };

        return await QuerySingleAsync(query, options, ct);
    }

    public async Task<DirectoryObject> GetBySamAccountNameAsync(string tenantId, string domainDn, string samAccountName, CancellationToken ct = default)
    {
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE LOWER(c.samAccountName) = LOWER(@sam) AND c.tenantId = @tenantId AND c.domainDn = @domainDn")
            .WithParameter("@sam", samAccountName)
            .WithParameter("@tenantId", tenantId)
            .WithParameter("@domainDn", domainDn);

        var options = new QueryRequestOptions
        {
            PartitionKey = new PartitionKeyBuilder()
                .Add(tenantId)
                .Add(domainDn)
                .Build(),
            MaxItemCount = 1,
        };

        return await QuerySingleAsync(query, options, ct);
    }

    public async Task<DirectoryObject> GetByUpnAsync(string tenantId, string upn, CancellationToken ct = default)
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE LOWER(c.userPrincipalName) = LOWER(@upn) AND c.tenantId = @tenantId")
            .WithParameter("@upn", upn)
            .WithParameter("@tenantId", tenantId);

        var options = new QueryRequestOptions
        {
            PartitionKey = new PartitionKeyBuilder()
                .Add(tenantId)
                .Build(),
            MaxItemCount = 1,
        };

        return await QuerySingleAsync(query, options, ct);
    }

    public async Task<SearchResult> SearchAsync(
        string tenantId, string baseDn, SearchScope scope, FilterNode filter,
        string[] attributes, int sizeLimit = 0, int timeLimitSeconds = 0,
        string continuationToken = null, int pageSize = 1000,
        bool includeDeleted = false, CancellationToken ct = default)
    {
        // For BaseObject scope, redirect to point-read path
        if (scope == SearchScope.BaseObject)
        {
            var obj = await GetByDnAsync(tenantId, baseDn, ct);
            if (obj is not null && !obj.IsDeleted)
                return new SearchResult { Entries = [obj] };
            return new SearchResult();
        }

        var (sql, parameters) = BuildSearchQuery(tenantId, baseDn, scope, filter, includeDeleted);

        var queryDef = new QueryDefinition(sql);
        foreach (var (name, value) in parameters)
        {
            queryDef = queryDef.WithParameter(name, value);
        }

        var requestOptions = BuildQueryOptions(tenantId, baseDn, scope, sizeLimit, pageSize);

        var entries = new List<DirectoryObject>();

        using var feed = DirectoryObjects.GetItemQueryIterator<DirectoryObject>(
            queryDef, continuationToken, requestOptions);

        string nextContinuation = null;

        while (feed.HasMoreResults)
        {
            var response = await feed.ReadNextAsync(ct);
            foreach (var item in response)
            {
                entries.Add(item);
                if (sizeLimit > 0 && entries.Count >= sizeLimit)
                    break;
            }

            nextContinuation = response.ContinuationToken;

            if (sizeLimit > 0 && entries.Count >= sizeLimit)
                break;
        }

        return new SearchResult
        {
            Entries = entries,
            ContinuationToken = nextContinuation,
        };
    }

    public async IAsyncEnumerable<DirectoryObject> SearchStreamAsync(
        string tenantId,
        string baseDn,
        SearchScope scope,
        FilterNode filter,
        string[] attributes,
        int sizeLimit = 0,
        int timeLimitSeconds = 0,
        bool includeDeleted = false,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // For BaseObject scope, just yield the single object
        if (scope == SearchScope.BaseObject)
        {
            var obj = await GetByDnAsync(tenantId, baseDn, ct);
            if (obj is not null && !obj.IsDeleted)
                yield return obj;
            yield break;
        }

        var (sql, parameters) = BuildSearchQuery(tenantId, baseDn, scope, filter, includeDeleted);

        var queryDef = new QueryDefinition(sql);
        foreach (var (name, value) in parameters)
        {
            queryDef = queryDef.WithParameter(name, value);
        }

        var requestOptions = BuildQueryOptions(tenantId, baseDn, scope, sizeLimit, pageSize: 100);

        int count = 0;
        using var iterator = DirectoryObjects.GetItemQueryIterator<DirectoryObject>(queryDef, requestOptions: requestOptions);

        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(ct);
            foreach (var item in page)
            {
                if (sizeLimit > 0 && count >= sizeLimit)
                    yield break;

                yield return item;
                count++;
            }
        }
    }

    /// <summary>
    /// Builds the SQL query and parameters for a search operation.
    /// Shared by SearchAsync and SearchStreamAsync.
    /// </summary>
    private static (string Sql, List<(string Name, object Value)> Parameters) BuildSearchQuery(
        string tenantId, string baseDn, SearchScope scope, FilterNode filter, bool includeDeleted)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("SELECT ");

        // If specific attributes requested, project only those (plus always-needed fields)
        // For now, select all — attribute projection is done in the LDAP layer
        sb.Append("* FROM c WHERE c.tenantId = @tenantId");

        var parameters = new List<(string Name, object Value)>
        {
            ("@tenantId", tenantId),
        };

        // Scope filtering
        switch (scope)
        {
            case SearchScope.SingleLevel:
                sb.Append(" AND c.parentDn = @baseDn");
                parameters.Add(("@baseDn", baseDn));
                break;
            case SearchScope.WholeSubtree:
                if (!string.IsNullOrEmpty(baseDn))
                {
                    sb.Append(" AND (c.distinguishedName = @baseDn OR ENDSWITH(c.distinguishedName, @baseDnSuffix, true))");
                    parameters.Add(("@baseDn", baseDn));
                    parameters.Add(("@baseDnSuffix", "," + baseDn));
                }
                break;
        }

        // Deleted objects filter
        if (!includeDeleted)
        {
            sb.Append(" AND (NOT IS_DEFINED(c.isDeleted) OR c.isDeleted = false)");
        }

        // Apply LDAP filter
        if (filter is not null)
        {
            var (whereClause, filterParams) = LdapFilterToSqlVisitor.GenerateSql(filter);
            if (whereClause != "true")
            {
                sb.Append(" AND ");
                sb.Append(whereClause);
                parameters.AddRange(filterParams);
            }
        }

        return (sb.ToString(), parameters);
    }

    /// <summary>
    /// Builds query request options with partition key scoping.
    /// Shared by SearchAsync and SearchStreamAsync.
    /// </summary>
    private static QueryRequestOptions BuildQueryOptions(
        string tenantId, string baseDn, SearchScope scope, int sizeLimit, int pageSize)
    {
        var requestOptions = new QueryRequestOptions
        {
            MaxItemCount = sizeLimit > 0 ? Math.Min(sizeLimit, pageSize) : pageSize,
        };

        var domainDn = ExtractDomainDn(baseDn);

        if (scope == SearchScope.SingleLevel && !string.IsNullOrEmpty(domainDn))
        {
            requestOptions.PartitionKey = new PartitionKeyBuilder()
                .Add(tenantId)
                .Add(domainDn)
                .Build();
        }
        else if (scope == SearchScope.WholeSubtree && !string.IsNullOrEmpty(domainDn))
        {
            requestOptions.PartitionKey = new PartitionKeyBuilder()
                .Add(tenantId)
                .Add(domainDn)
                .Build();
        }
        else
        {
            requestOptions.PartitionKey = new PartitionKeyBuilder()
                .Add(tenantId)
                .Build();
        }

        return requestOptions;
    }

    public async Task CreateAsync(DirectoryObject obj, CancellationToken ct = default)
    {
        obj.Id = obj.DistinguishedName.ToLowerInvariant();

        var parsed = DistinguishedName.Parse(obj.DistinguishedName);

        // Only compute DomainDn/ParentDn from DN if not already set by the caller.
        // Callers like DnsZoneStore set these explicitly to handle AD application
        // partition DNs (DC=DomainDnsZones) which are DC= components but not
        // part of the actual domain DN.
        if (string.IsNullOrEmpty(obj.DomainDn))
            obj.DomainDn = parsed.GetDomainDn();
        if (string.IsNullOrEmpty(obj.ParentDn))
            obj.ParentDn = parsed.Parent().ToString();

        if (string.IsNullOrEmpty(obj.ObjectGuid))
            obj.ObjectGuid = Guid.NewGuid().ToString();

        obj.WhenCreated = DateTimeOffset.UtcNow;
        obj.WhenChanged = DateTimeOffset.UtcNow;

        var partitionKey = new PartitionKeyBuilder()
            .Add(obj.TenantId)
            .Add(obj.DomainDn)
            .Add(obj.ObjectCategory)
            .Build();

        await DirectoryObjects.CreateItemAsync(obj, partitionKey, cancellationToken: ct);

        _logger.LogDebug("Created directory object: {DN}", obj.DistinguishedName);
    }

    public async Task UpdateAsync(DirectoryObject obj, CancellationToken ct = default)
    {
        obj.WhenChanged = DateTimeOffset.UtcNow;
        obj.Id = obj.DistinguishedName.ToLowerInvariant();

        var partitionKey = new PartitionKeyBuilder()
            .Add(obj.TenantId)
            .Add(obj.DomainDn)
            .Add(obj.ObjectCategory)
            .Build();

        var options = new ItemRequestOptions();
        if (!string.IsNullOrEmpty(obj.ETag))
            options.IfMatchEtag = obj.ETag;

        await DirectoryObjects.ReplaceItemAsync(obj, obj.Id, partitionKey, options, ct);

        _logger.LogDebug("Updated directory object: {DN}", obj.DistinguishedName);
    }

    public async Task DeleteAsync(string tenantId, string dn, bool hardDelete = false, CancellationToken ct = default)
    {
        var obj = await GetByDnAsync(tenantId, dn, ct);
        if (obj is null) return;

        if (hardDelete)
        {
            var partitionKey = new PartitionKeyBuilder()
                .Add(obj.TenantId)
                .Add(obj.DomainDn)
                .Add(obj.ObjectCategory)
                .Build();

            await DirectoryObjects.DeleteItemAsync<DirectoryObject>(obj.Id, partitionKey, cancellationToken: ct);
        }
        else
        {
            // Tombstone
            obj.IsDeleted = true;
            obj.WhenChanged = DateTimeOffset.UtcNow;
            await UpdateAsync(obj, ct);
        }

        _logger.LogDebug("Deleted directory object: {DN} (hard={Hard})", dn, hardDelete);
    }

    public async Task MoveAsync(string tenantId, string oldDn, string newDn, CancellationToken ct = default)
    {
        var obj = await GetByDnAsync(tenantId, oldDn, ct);
        if (obj is null)
            throw new InvalidOperationException($"Object not found: {oldDn}");

        // Delete old document
        await DeleteAsync(tenantId, oldDn, hardDelete: true, ct);

        // Update DN and parent
        var parsed = DistinguishedName.Parse(newDn);
        obj.DistinguishedName = newDn;
        obj.Id = newDn.ToLowerInvariant();
        obj.ParentDn = parsed.Parent().ToString();
        obj.DomainDn = parsed.GetDomainDn();
        obj.Cn = parsed.Rdn.Value;
        obj.WhenChanged = DateTimeOffset.UtcNow;

        // Create new document
        await CreateAsync(obj, ct);

        _logger.LogDebug("Moved directory object: {OldDN} -> {NewDN}", oldDn, newDn);
    }

    public async Task<long> GetNextUsnAsync(string tenantId, string domainDn, CancellationToken ct = default)
    {
        var id = $"{tenantId}:{domainDn}:usn";

        try
        {
            var response = await UsnCounters.ReadItemAsync<UsnCounter>(
                id, new PartitionKey(tenantId), cancellationToken: ct);

            var counter = response.Resource;
            counter.Value++;
            counter.LastUpdated = DateTimeOffset.UtcNow;

            await UsnCounters.ReplaceItemAsync(counter, id,
                new PartitionKey(tenantId),
                new ItemRequestOptions { IfMatchEtag = response.ETag },
                ct);

            return counter.Value;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            var counter = new UsnCounter
            {
                Id = id,
                TenantId = tenantId,
                DomainDn = domainDn,
                Value = 1,
                LastUpdated = DateTimeOffset.UtcNow,
            };

            await UsnCounters.CreateItemAsync(counter, new PartitionKey(tenantId), cancellationToken: ct);
            return counter.Value;
        }
    }

    public async Task<IReadOnlyList<DirectoryObject>> GetChildrenAsync(string tenantId, string parentDn, CancellationToken ct = default)
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.parentDn = @parentDn AND c.tenantId = @tenantId AND (NOT IS_DEFINED(c.isDeleted) OR c.isDeleted = false)")
            .WithParameter("@parentDn", parentDn)
            .WithParameter("@tenantId", tenantId);

        var domainDn = ExtractDomainDn(parentDn);
        var options = new QueryRequestOptions();

        if (!string.IsNullOrEmpty(domainDn))
        {
            options.PartitionKey = new PartitionKeyBuilder()
                .Add(tenantId)
                .Add(domainDn)
                .Build();
        }
        else
        {
            options.PartitionKey = new PartitionKeyBuilder()
                .Add(tenantId)
                .Build();
        }

        return await QueryMultipleAsync(query, options, ct);
    }

    public async Task<IReadOnlyList<DirectoryObject>> GetByServicePrincipalNameAsync(string tenantId, string spn, CancellationToken ct = default)
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE EXISTS(SELECT VALUE v FROM v IN c.servicePrincipalName WHERE LOWER(v) = LOWER(@spn)) AND c.tenantId = @tenantId")
            .WithParameter("@spn", spn)
            .WithParameter("@tenantId", tenantId);

        var options = new QueryRequestOptions
        {
            PartitionKey = new PartitionKeyBuilder()
                .Add(tenantId)
                .Build(),
        };

        return await QueryMultipleAsync(query, options, ct);
    }

    public async Task<IReadOnlyList<DirectoryObject>> GetByDnsAsync(
        string tenantId, IEnumerable<string> dns, CancellationToken ct = default)
    {
        var dnList = dns.ToList();
        if (dnList.Count == 0) return [];
        if (dnList.Count == 1)
        {
            var single = await GetByDnAsync(tenantId, dnList[0], ct);
            return single is not null ? [single] : [];
        }

        // Use IN query for batches up to 50; split larger sets into chunks
        var results = new List<DirectoryObject>();
        foreach (var chunk in dnList.Chunk(50))
        {
            var ids = chunk.Select(dn => dn.ToLowerInvariant()).ToList();
            var paramList = string.Join(",", ids.Select((_, i) => $"@id{i}"));
            var sql = $"SELECT * FROM c WHERE c.id IN ({paramList}) AND c.tenantId = @tid";
            var query = new QueryDefinition(sql).WithParameter("@tid", tenantId);
            for (int i = 0; i < ids.Count; i++)
                query = query.WithParameter($"@id{i}", ids[i]);

            var options = new QueryRequestOptions
            {
                PartitionKey = new PartitionKeyBuilder().Add(tenantId).Build(),
            };

            using var iter = DirectoryObjects.GetItemQueryIterator<DirectoryObject>(query, requestOptions: options);
            while (iter.HasMoreResults)
            {
                var page = await iter.ReadNextAsync(ct);
                results.AddRange(page);
            }
        }
        return results;
    }

    private async Task<DirectoryObject> QuerySingleAsync(QueryDefinition query, QueryRequestOptions options, CancellationToken ct)
    {
        options ??= new QueryRequestOptions { MaxItemCount = 1 };
        using var feed = DirectoryObjects.GetItemQueryIterator<DirectoryObject>(query, requestOptions: options);
        while (feed.HasMoreResults)
        {
            var response = await feed.ReadNextAsync(ct);
            var result = response.FirstOrDefault();
            if (result is not null) return result;
        }
        return null;
    }

    private async Task<IReadOnlyList<DirectoryObject>> QueryMultipleAsync(QueryDefinition query, QueryRequestOptions options, CancellationToken ct)
    {
        var results = new List<DirectoryObject>();
        using var feed = DirectoryObjects.GetItemQueryIterator<DirectoryObject>(query, requestOptions: options);
        while (feed.HasMoreResults)
        {
            var response = await feed.ReadNextAsync(ct);
            results.AddRange(response);
        }
        return results;
    }

    /// <summary>
    /// Extract the domain DN portion (DC=...) from a full DN.
    /// E.g., "CN=Users,DC=test,DC=local" returns "DC=test,DC=local".
    ///
    /// Handles AD application partition DNs correctly:
    /// "DC=corp.com,CN=MicrosoftDNS,DC=DomainDnsZones,DC=corp,DC=com" returns "DC=corp,DC=com"
    /// by walking from the end and stopping at well-known application partition names
    /// (DomainDnsZones, ForestDnsZones) or non-DC components.
    /// </summary>
    private static string ExtractDomainDn(string dn)
    {
        var parts = dn.Split(',');
        var dcParts = new List<string>();

        for (int i = parts.Length - 1; i >= 0; i--)
        {
            var part = parts[i].Trim();
            if (part.StartsWith("DC=", StringComparison.OrdinalIgnoreCase))
            {
                var value = part[3..];
                // Stop at well-known AD application partition names
                if (value.Equals("DomainDnsZones", StringComparison.OrdinalIgnoreCase) ||
                    value.Equals("ForestDnsZones", StringComparison.OrdinalIgnoreCase))
                    break;
                dcParts.Insert(0, part);
            }
            else
            {
                break;
            }
        }

        return dcParts.Count > 0 ? string.Join(",", dcParts) : null;
    }

    public async Task<long> ClaimRidPoolAsync(string tenantId, string domainDn, int poolSize, CancellationToken ct = default)
    {
        var id = $"ridpool:{tenantId}:{domainDn.ToLowerInvariant()}";

        const int maxRetries = 10;
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                var response = await UsnCounters.ReadItemAsync<RidPoolDocument>(
                    id, new PartitionKey(tenantId), cancellationToken: ct);

                var doc = response.Resource;
                var poolStart = doc.NextPoolStart;
                doc.NextPoolStart = poolStart + poolSize;
                doc.LastUpdated = DateTimeOffset.UtcNow;

                await UsnCounters.ReplaceItemAsync(doc, id,
                    new PartitionKey(tenantId),
                    new ItemRequestOptions { IfMatchEtag = response.ETag },
                    ct);

                return poolStart;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                // First time — create the document with initial pool start at 1000
                var doc = new RidPoolDocument
                {
                    Id = id,
                    TenantId = tenantId,
                    DomainDn = domainDn,
                    NextPoolStart = 1000 + poolSize, // First pool is [1000, 1000+poolSize-1]
                    LastUpdated = DateTimeOffset.UtcNow,
                };

                try
                {
                    await UsnCounters.CreateItemAsync(doc, new PartitionKey(tenantId), cancellationToken: ct);
                    return 1000;
                }
                catch (CosmosException createEx) when (createEx.StatusCode == HttpStatusCode.Conflict)
                {
                    // Another DC created it first — retry will read and update
                }
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed)
            {
                // Etag conflict — another DC claimed a pool concurrently. Retry.
                _logger.LogDebug("RID pool etag conflict on attempt {Attempt}, retrying", attempt + 1);
            }

            // Exponential backoff: 50ms, 100ms, 200ms, ...
            await Task.Delay(TimeSpan.FromMilliseconds(50 * Math.Pow(2, attempt)), ct);
        }

        throw new InvalidOperationException($"Failed to claim RID pool after {maxRetries} attempts due to contention.");
    }

    private class UsnCounter
    {
        public string Id { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
        public string DomainDn { get; set; } = string.Empty;
        public long Value { get; set; }
        public DateTimeOffset LastUpdated { get; set; }
    }

    private class RidPoolDocument
    {
        public string Id { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
        public string DomainDn { get; set; } = string.Empty;
        public long NextPoolStart { get; set; }
        public DateTimeOffset LastUpdated { get; set; }
    }
}
