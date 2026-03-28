using System.Text.Json.Serialization;

namespace Directory.Security.Pam;

public class PrivilegedRole
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("groupDn")]
    public string GroupDn { get; set; } = string.Empty;

    [JsonPropertyName("maxActivationHours")]
    public int MaxActivationHours { get; set; } = 8;

    [JsonPropertyName("requireJustification")]
    public bool RequireJustification { get; set; } = true;

    [JsonPropertyName("requireApproval")]
    public bool RequireApproval { get; set; }

    [JsonPropertyName("approvers")]
    public List<string> Approvers { get; set; } = new();

    [JsonPropertyName("requireMfa")]
    public bool RequireMfa { get; set; } = true;

    [JsonPropertyName("isEnabled")]
    public bool IsEnabled { get; set; } = true;
}

public class RoleActivation
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("roleId")]
    public string RoleId { get; set; } = string.Empty;

    [JsonPropertyName("roleName")]
    public string RoleName { get; set; } = string.Empty;

    [JsonPropertyName("userDn")]
    public string UserDn { get; set; } = string.Empty;

    [JsonPropertyName("groupDn")]
    public string GroupDn { get; set; } = string.Empty;

    [JsonPropertyName("justification")]
    public string Justification { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public ActivationStatus Status { get; set; }

    [JsonPropertyName("requestedAt")]
    public DateTimeOffset RequestedAt { get; set; }

    [JsonPropertyName("activatedAt")]
    public DateTimeOffset? ActivatedAt { get; set; }

    [JsonPropertyName("expiresAt")]
    public DateTimeOffset? ExpiresAt { get; set; }

    [JsonPropertyName("deactivatedAt")]
    public DateTimeOffset? DeactivatedAt { get; set; }

    [JsonPropertyName("approvedBy")]
    public string ApprovedBy { get; set; }

    [JsonPropertyName("deniedBy")]
    public string DeniedBy { get; set; }

    [JsonPropertyName("denyReason")]
    public string DenyReason { get; set; }

    [JsonPropertyName("requestedHours")]
    public int RequestedHours { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ActivationStatus
{
    PendingApproval,
    Approved,
    Active,
    Expired,
    Denied,
    Cancelled,
    Deactivated
}

public class BreakGlassAccount
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("accountDn")]
    public string AccountDn { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("isSealed")]
    public bool IsSealed { get; set; } = true;

    [JsonPropertyName("lastAccessedAt")]
    public DateTimeOffset? LastAccessedAt { get; set; }

    [JsonPropertyName("lastAccessedBy")]
    public string LastAccessedBy { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Serialization wrapper stored on a directory object attribute for PAM data.
/// </summary>
public class PamData
{
    [JsonPropertyName("roles")]
    public List<PrivilegedRole> Roles { get; set; } = new();

    [JsonPropertyName("activations")]
    public List<RoleActivation> Activations { get; set; } = new();

    [JsonPropertyName("breakGlassAccounts")]
    public List<BreakGlassAccount> BreakGlassAccounts { get; set; } = new();
}
