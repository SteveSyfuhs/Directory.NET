using System.Text.Json;
using System.Text.Json.Serialization;

namespace Directory.CosmosDb.Configuration;

/// <summary>
/// Represents a configuration section stored in the Cosmos DB Configuration container.
/// Each document holds the settings for one section (e.g., "Cache", "Ldap") scoped
/// to either the entire cluster or a specific node.
/// </summary>
public class ConfigurationDocument
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("tenantId")]
    public string TenantId { get; set; } = "default";

    /// <summary>"cluster" for shared settings, or "node:{hostname}" for per-node overrides.</summary>
    public string Scope { get; set; } = "cluster";

    /// <summary>The configuration section name, e.g. "Cache", "Ldap", "Kerberos".</summary>
    public string Section { get; set; } = "";

    public int Version { get; set; } = 1;

    public string ModifiedBy { get; set; } = "system";

    public DateTimeOffset ModifiedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Key-value pairs for this section. Values are stored as JsonElement to preserve types.</summary>
    public Dictionary<string, JsonElement> Values { get; set; } = new();

    [JsonPropertyName("_etag")]
    public string ETag { get; set; }
}
