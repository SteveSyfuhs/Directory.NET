using System.Text.Json.Serialization;

namespace Directory.Replication;

/// <summary>
/// Represents a change log entry for directory replication tracking.
/// </summary>
public class ChangeLogEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("tenantId")]
    public string TenantId { get; set; } = string.Empty;

    [JsonPropertyName("domainDn")]
    public string DomainDn { get; set; } = string.Empty;

    [JsonPropertyName("usn")]
    public long Usn { get; set; }

    [JsonPropertyName("objectDn")]
    public string ObjectDn { get; set; } = string.Empty;

    [JsonPropertyName("changeType")]
    public string ChangeType { get; set; } = string.Empty; // add, modify, delete

    [JsonPropertyName("changedAttributes")]
    public List<string> ChangedAttributes { get; set; } = [];

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// The USN assigned by the originating DC where the change was first made.
    /// </summary>
    [JsonPropertyName("originatingUsn")]
    public long OriginatingUsn { get; set; }

    /// <summary>
    /// The USN assigned by the local DC when the change was replicated in.
    /// For locally-originated changes, this equals OriginatingUsn.
    /// </summary>
    [JsonPropertyName("replicatedUsn")]
    public long ReplicatedUsn { get; set; }

    /// <summary>
    /// The invocation ID of the DC that originated the change.
    /// Used for up-to-dateness vector tracking per [MS-DRSR].
    /// </summary>
    [JsonPropertyName("invocationId")]
    public string InvocationId { get; set; } = string.Empty;
}
