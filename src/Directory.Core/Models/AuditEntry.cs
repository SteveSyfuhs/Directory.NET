using System.Text.Json.Serialization;

namespace Directory.Core.Models;

public class AuditEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("tenantId")]
    public string TenantId { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty; // Create, Update, Delete, Move, PasswordReset, Enable, Disable

    [JsonPropertyName("targetDn")]
    public string TargetDn { get; set; } = string.Empty;

    [JsonPropertyName("targetObjectClass")]
    public string TargetObjectClass { get; set; } = string.Empty;

    [JsonPropertyName("actorIdentity")]
    public string ActorIdentity { get; set; } = "web-console";

    [JsonPropertyName("sourceIp")]
    public string SourceIp { get; set; } = string.Empty;

    [JsonPropertyName("details")]
    public Dictionary<string, string> Details { get; set; } = new();

    [JsonPropertyName("success")]
    public bool Success { get; set; } = true;

    [JsonPropertyName("errorMessage")]
    public string ErrorMessage { get; set; }
}
