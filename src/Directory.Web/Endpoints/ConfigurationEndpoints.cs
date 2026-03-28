using System.Net;
using Directory.Core;
using Directory.CosmosDb.Configuration;
using Microsoft.Azure.Cosmos;

namespace Directory.Web.Endpoints;

public static class ConfigurationEndpoints
{
    // Known configuration sections with their allowed scopes
    private static readonly Dictionary<string, SectionInfo> KnownSections = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Cache"] = new("Cache", ["cluster"], new[]
        {
            new FieldMeta("Enabled", "bool", "Enable the in-memory cache layer", true, true),
            new FieldMeta("DefaultTtlSeconds", "int", "Default TTL for cached entries in seconds", 300, true, 10, 86400),
            new FieldMeta("MaxEntries", "int", "Maximum number of entries in the cache", 10000, false, 100, 1000000),
            new FieldMeta("SlidingExpirationSeconds", "int", "Sliding expiration window in seconds", 60, true, 0, 3600),
        }),
        ["Ldap"] = new("Ldap", ["cluster", "node"], new[]
        {
            new FieldMeta("Port", "int", "LDAP listen port", 389, false, 1, 65535),
            new FieldMeta("SslPort", "int", "LDAPS listen port", 636, false, 1, 65535),
            new FieldMeta("MaxConnections", "int", "Maximum concurrent LDAP connections", 1000, true, 10, 100000),
            new FieldMeta("IdleTimeoutSeconds", "int", "Idle connection timeout in seconds", 900, true, 30, 7200),
            new FieldMeta("MaxPageSize", "int", "Maximum page size for search results", 1000, true, 100, 10000),
            new FieldMeta("EnableTls", "bool", "Require TLS for LDAP connections", false, false),
            new FieldMeta("CertificatePath", "string", "Path to TLS certificate file", "", false),
        }),
        ["Kerberos"] = new("Kerberos", ["cluster", "node"], new[]
        {
            new FieldMeta("Port", "int", "Kerberos KDC listen port", 88, false, 1, 65535),
            new FieldMeta("MaxTicketLifetimeHours", "int", "Maximum TGT lifetime in hours", 10, true, 1, 720),
            new FieldMeta("MaxRenewLifetimeDays", "int", "Maximum ticket renewal lifetime in days", 7, true, 1, 365),
            new FieldMeta("ClockSkewMinutes", "int", "Maximum allowed clock skew in minutes", 5, true, 1, 60),
            new FieldMeta("EnablePreAuth", "bool", "Require Kerberos pre-authentication", true, true),
        }),
        ["Dns"] = new("Dns", ["cluster", "node"], new[]
        {
            new FieldMeta("Port", "int", "DNS listen port", 53, false, 1, 65535),
            new FieldMeta("EnableRecursion", "bool", "Allow recursive DNS queries", false, true),
            new FieldMeta("ForwarderAddresses", "string", "Comma-separated list of DNS forwarder addresses", "", true),
            new FieldMeta("CacheTtlSeconds", "int", "DNS response cache TTL in seconds", 3600, true, 0, 86400),
            new FieldMeta("ZoneTransferEnabled", "bool", "Allow DNS zone transfers", false, false),
        }),
        ["Replication"] = new("Replication", ["cluster"], new[]
        {
            new FieldMeta("IntervalSeconds", "int", "Replication interval in seconds", 300, true, 15, 86400),
            new FieldMeta("UrgentIntervalSeconds", "int", "Urgent replication interval in seconds", 15, true, 5, 300),
            new FieldMeta("MaxBatchSize", "int", "Maximum objects per replication batch", 1000, true, 100, 50000),
            new FieldMeta("CompressionEnabled", "bool", "Enable replication data compression", true, true),
            new FieldMeta("ConnectionString", "string", "Cosmos DB connection string for replication", "", false),
        }),
        ["RpcServer"] = new("RpcServer", ["cluster", "node"], new[]
        {
            new FieldMeta("Port", "int", "RPC listen port", 135, false, 1, 65535),
            new FieldMeta("MaxConcurrentCalls", "int", "Maximum concurrent RPC calls", 500, true, 10, 10000),
            new FieldMeta("TimeoutSeconds", "int", "RPC call timeout in seconds", 120, true, 10, 600),
        }),
        ["DcNode"] = new("DcNode", ["cluster", "node"], new[]
        {
            new FieldMeta("SiteName", "string", "AD site name for this DC", "Default-First-Site-Name", false),
            new FieldMeta("IsGlobalCatalog", "bool", "Whether this DC serves as a Global Catalog", true, false),
            new FieldMeta("IsReadOnly", "bool", "Whether this DC is a Read-Only Domain Controller", false, false),
            new FieldMeta("HeartbeatIntervalSeconds", "int", "Heartbeat reporting interval in seconds", 60, true, 10, 600),
        }),
    };

    public static RouteGroupBuilder MapConfigurationEndpoints(this RouteGroupBuilder group)
    {
        // GET /sections — list all known sections
        group.MapGet("/sections", async (CosmosConfigurationStore store) =>
        {
            List<ConfigurationDocument> existing;
            try
            {
                existing = await store.GetAllSectionsAsync(DirectoryConstants.DefaultTenantId);
            }
            catch
            {
                existing = new List<ConfigurationDocument>();
            }

            var result = KnownSections.Select(kvp =>
            {
                var hasCluster = existing.Any(d =>
                    d.Section.Equals(kvp.Key, StringComparison.OrdinalIgnoreCase) &&
                    d.Scope == "cluster");

                var nodeOverrides = existing
                    .Where(d =>
                        d.Section.Equals(kvp.Key, StringComparison.OrdinalIgnoreCase) &&
                        d.Scope.StartsWith("node:"))
                    .Select(d => d.Scope.Substring(5))
                    .ToList();

                return new
                {
                    Name = kvp.Key,
                    HasClusterConfig = hasCluster,
                    HasNodeOverrides = nodeOverrides,
                    Scopes = kvp.Value.Scopes,
                };
            });

            return Results.Ok(result);
        })
        .WithName("ListConfigSections")
        .WithTags("Configuration");

        // GET /schema/{section} — field metadata for a section
        group.MapGet("/schema/{section}", (string section) =>
        {
            if (!KnownSections.TryGetValue(section, out var info))
                return Results.Problem(statusCode: 404, detail: $"Unknown section: {section}");

            return Results.Ok(info.Fields.Select(f => new
            {
                f.Name,
                f.Type,
                f.Description,
                f.DefaultValue,
                f.HotReloadable,
                f.MinValue,
                f.MaxValue,
            }));
        })
        .WithName("GetConfigSchema")
        .WithTags("Configuration");

        // GET /{section}/cluster — cluster-scoped values
        group.MapGet("/{section}/cluster", async (string section, CosmosConfigurationStore store) =>
        {
            if (!KnownSections.ContainsKey(section))
                return Results.Problem(statusCode: 404, detail: $"Unknown section: {section}");

            var doc = await store.GetSectionAsync(DirectoryConstants.DefaultTenantId, "cluster", section);
            if (doc == null)
            {
                return Results.Ok(new
                {
                    Section = section,
                    Scope = "cluster",
                    Version = 0,
                    ModifiedBy = "",
                    ModifiedAt = "",
                    Values = new Dictionary<string, object>(),
                    Etag = (string)null,
                });
            }

            return Results.Ok(new
            {
                doc.Section,
                doc.Scope,
                doc.Version,
                doc.ModifiedBy,
                ModifiedAt = doc.ModifiedAt.ToString("o"),
                doc.Values,
                Etag = doc.ETag,
            });
        })
        .WithName("GetClusterConfig")
        .WithTags("Configuration");

        // GET /{section}/node/{hostname} — node-scoped overrides
        group.MapGet("/{section}/node/{hostname}", async (string section, string hostname, CosmosConfigurationStore store) =>
        {
            if (!KnownSections.ContainsKey(section))
                return Results.Problem(statusCode: 404, detail: $"Unknown section: {section}");

            var doc = await store.GetSectionAsync(DirectoryConstants.DefaultTenantId, $"node:{hostname}", section);
            if (doc == null)
            {
                return Results.Ok(new
                {
                    Section = section,
                    Scope = $"node:{hostname}",
                    Version = 0,
                    ModifiedBy = "",
                    ModifiedAt = "",
                    Values = new Dictionary<string, object>(),
                    Etag = (string)null,
                });
            }

            return Results.Ok(new
            {
                doc.Section,
                doc.Scope,
                doc.Version,
                doc.ModifiedBy,
                ModifiedAt = doc.ModifiedAt.ToString("o"),
                doc.Values,
                Etag = doc.ETag,
            });
        })
        .WithName("GetNodeConfig")
        .WithTags("Configuration");

        // PUT /{section}/cluster — upsert cluster config
        group.MapPut("/{section}/cluster", async (string section, ConfigUpdateRequest request, CosmosConfigurationStore store) =>
        {
            if (!KnownSections.ContainsKey(section))
                return Results.Problem(statusCode: 404, detail: $"Unknown section: {section}");

            var existing = await store.GetSectionAsync(DirectoryConstants.DefaultTenantId, "cluster", section);
            var doc = existing ?? new ConfigurationDocument
            {
                Id = $"cluster::{section}",
                TenantId = DirectoryConstants.DefaultTenantId,
                Scope = "cluster",
                Section = section,
                Version = 0,
            };

            doc.Values = request.Values;
            doc.Version++;
            doc.ModifiedBy = "admin";

            if (!string.IsNullOrEmpty(request.Etag))
                doc.ETag = request.Etag;

            try
            {
                var result = await store.UpsertSectionAsync(doc);
                return Results.Ok(new
                {
                    result.Section,
                    result.Scope,
                    result.Version,
                    result.ModifiedBy,
                    ModifiedAt = result.ModifiedAt.ToString("o"),
                    result.Values,
                    Etag = result.ETag,
                });
            }
            catch (CosmosException ex) when (ex.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed)
            {
                return Results.Problem(statusCode: 409, detail: "ETag conflict — the document was modified by another user. Please refresh and try again.");
            }
        })
        .WithName("UpdateClusterConfig")
        .WithTags("Configuration");

        // PUT /{section}/node/{hostname} — upsert node config
        group.MapPut("/{section}/node/{hostname}", async (string section, string hostname, ConfigUpdateRequest request, CosmosConfigurationStore store) =>
        {
            if (!KnownSections.ContainsKey(section))
                return Results.Problem(statusCode: 404, detail: $"Unknown section: {section}");

            var scope = $"node:{hostname}";
            var existing = await store.GetSectionAsync(DirectoryConstants.DefaultTenantId, scope, section);
            var doc = existing ?? new ConfigurationDocument
            {
                Id = $"{scope}::{section}",
                TenantId = DirectoryConstants.DefaultTenantId,
                Scope = scope,
                Section = section,
                Version = 0,
            };

            doc.Values = request.Values;
            doc.Version++;
            doc.ModifiedBy = "admin";

            if (!string.IsNullOrEmpty(request.Etag))
                doc.ETag = request.Etag;

            try
            {
                var result = await store.UpsertSectionAsync(doc);
                return Results.Ok(new
                {
                    result.Section,
                    result.Scope,
                    result.Version,
                    result.ModifiedBy,
                    ModifiedAt = result.ModifiedAt.ToString("o"),
                    result.Values,
                    Etag = result.ETag,
                });
            }
            catch (CosmosException ex) when (ex.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed)
            {
                return Results.Problem(statusCode: 409, detail: "ETag conflict — the document was modified by another user. Please refresh and try again.");
            }
        })
        .WithName("UpdateNodeConfig")
        .WithTags("Configuration");

        // DELETE /{section}/node/{hostname} — remove node override
        group.MapDelete("/{section}/node/{hostname}", async (string section, string hostname, CosmosConfigurationStore store) =>
        {
            if (!KnownSections.ContainsKey(section))
                return Results.Problem(statusCode: 404, detail: $"Unknown section: {section}");

            var id = $"node:{hostname}::{section}";
            await store.DeleteSectionAsync(DirectoryConstants.DefaultTenantId, id);
            return Results.NoContent();
        })
        .WithName("DeleteNodeConfig")
        .WithTags("Configuration");

        // GET /nodes — list registered nodes
        group.MapGet("/nodes", async (CosmosConfigurationStore store) =>
        {
            List<string> hostnames;
            try
            {
                hostnames = await store.GetRegisteredNodesAsync(DirectoryConstants.DefaultTenantId);
            }
            catch
            {
                hostnames = new List<string>();
            }

            List<ConfigurationDocument> allDocs;
            try
            {
                allDocs = await store.GetAllSectionsAsync(DirectoryConstants.DefaultTenantId);
            }
            catch
            {
                allDocs = new List<ConfigurationDocument>();
            }

            var clusterVersion = allDocs
                .Where(d => d.Scope == "cluster")
                .Select(d => d.Version)
                .DefaultIfEmpty(0)
                .Max();

            var nodes = hostnames.Select(h =>
            {
                var nodeDocs = allDocs.Where(d => d.Scope == $"node:{h}").ToList();
                var nodeVersion = nodeDocs.Select(d => d.Version).DefaultIfEmpty(0).Max();
                var lastModified = nodeDocs.Select(d => d.ModifiedAt).DefaultIfEmpty(DateTimeOffset.MinValue).Max();

                return new
                {
                    Hostname = h,
                    Site = "Default-First-Site-Name",
                    ConfigVersion = nodeVersion,
                    ClusterVersion = clusterVersion,
                    LastSeen = lastModified.ToString("o"),
                };
            }).ToList();

            return Results.Ok(nodes);
        })
        .WithName("ListConfigNodes")
        .WithTags("Configuration");

        return group;
    }

    private record SectionInfo(string Name, string[] Scopes, FieldMeta[] Fields);

    private record FieldMeta(
        string Name,
        string Type,
        string Description,
        object DefaultValue,
        bool HotReloadable,
        object MinValue = null,
        object MaxValue = null);
}

public record ConfigUpdateRequest(
    Dictionary<string, System.Text.Json.JsonElement> Values,
    string Etag = null);
