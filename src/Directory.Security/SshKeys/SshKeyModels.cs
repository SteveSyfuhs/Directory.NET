using System.Text.Json.Serialization;

namespace Directory.Security.SshKeys;

public class SshPublicKey
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("userDn")]
    public string UserDn { get; set; } = string.Empty;

    [JsonPropertyName("keyType")]
    public string KeyType { get; set; } = string.Empty;

    [JsonPropertyName("publicKeyData")]
    public string PublicKeyData { get; set; } = string.Empty;

    [JsonPropertyName("comment")]
    public string Comment { get; set; }

    [JsonPropertyName("fingerprint")]
    public string Fingerprint { get; set; }

    [JsonPropertyName("addedAt")]
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("lastUsedAt")]
    public DateTimeOffset? LastUsedAt { get; set; }

    [JsonPropertyName("isEnabled")]
    public bool IsEnabled { get; set; } = true;
}

/// <summary>
/// Wrapper stored as JSON in the sshPublicKey LDAP attribute on user objects.
/// </summary>
public class SshKeyData
{
    [JsonPropertyName("keys")]
    public List<SshPublicKey> Keys { get; set; } = new();
}
