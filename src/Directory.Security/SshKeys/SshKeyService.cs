using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Microsoft.Extensions.Logging;

namespace Directory.Security.SshKeys;

/// <summary>
/// Manages SSH public keys for directory users. Keys are stored as the standard
/// sshPublicKey LDAP attribute on user objects, enabling integration with OpenSSH's
/// AuthorizedKeysCommand for centralized SSH key management.
/// </summary>
public class SshKeyService
{
    private const string SshKeyAttributeName = "sshPublicKey";

    private static readonly HashSet<string> SupportedKeyTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "ssh-rsa",
        "ssh-ed25519",
        "ecdsa-sha2-nistp256",
        "ecdsa-sha2-nistp384",
        "ecdsa-sha2-nistp521",
        "ssh-dss",
        "sk-ssh-ed25519@openssh.com",
        "sk-ecdsa-sha2-nistp256@openssh.com",
    };

    private readonly IDirectoryStore _store;
    private readonly INamingContextService _ncService;
    private readonly IAuditService _audit;
    private readonly ILogger<SshKeyService> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public SshKeyService(
        IDirectoryStore store,
        INamingContextService ncService,
        IAuditService audit,
        ILogger<SshKeyService> logger)
    {
        _store = store;
        _ncService = ncService;
        _audit = audit;
        _logger = logger;
    }

    /// <summary>
    /// Add an SSH public key to a user. Parses the standard "type base64 comment" format,
    /// validates the key, computes the SHA256 fingerprint, and stores it on the user object.
    /// </summary>
    public async Task<SshPublicKey> AddKeyAsync(string userDn, string publicKeyString, CancellationToken ct = default)
    {
        var parsed = ParsePublicKey(publicKeyString);
        if (parsed == null)
            throw new InvalidOperationException("Invalid SSH public key format. Expected: type base64-data [comment]");

        var (keyType, keyData, comment) = parsed.Value;

        if (!SupportedKeyTypes.Contains(keyType))
            throw new InvalidOperationException($"Unsupported key type: {keyType}. Supported: {string.Join(", ", SupportedKeyTypes)}");

        var fingerprint = ComputeFingerprint(keyData);

        var user = await _store.GetByDnAsync("default", userDn, ct)
            ?? throw new InvalidOperationException($"User not found: {userDn}");

        var sshData = LoadSshData(user);

        // Check for duplicate fingerprint
        if (sshData.Keys.Any(k => k.Fingerprint == fingerprint))
            throw new InvalidOperationException("This SSH key is already registered for this user.");

        var key = new SshPublicKey
        {
            Id = Guid.NewGuid().ToString(),
            UserDn = userDn,
            KeyType = keyType,
            PublicKeyData = keyData,
            Comment = comment,
            Fingerprint = fingerprint,
            AddedAt = DateTimeOffset.UtcNow,
            IsEnabled = true,
        };

        sshData.Keys.Add(key);
        SaveSshData(user, sshData);

        user.WhenChanged = DateTimeOffset.UtcNow;
        await _store.UpdateAsync(user, ct);

        _logger.LogInformation("SSH key added for {UserDn}: {KeyType} {Fingerprint}", userDn, keyType, fingerprint);
        await AuditAsync("SshKeyAdded", userDn, new()
        {
            ["keyType"] = keyType,
            ["fingerprint"] = fingerprint,
            ["comment"] = comment ?? "",
        }, ct);

        return key;
    }

    /// <summary>
    /// List all SSH keys for a user.
    /// </summary>
    public async Task<List<SshPublicKey>> ListKeysAsync(string userDn, CancellationToken ct = default)
    {
        var user = await _store.GetByDnAsync("default", userDn, ct);
        if (user == null) return new List<SshPublicKey>();

        var sshData = LoadSshData(user);
        return sshData.Keys;
    }

    /// <summary>
    /// Delete an SSH key by its ID.
    /// </summary>
    public async Task<bool> DeleteKeyAsync(string userDn, string keyId, CancellationToken ct = default)
    {
        var user = await _store.GetByDnAsync("default", userDn, ct);
        if (user == null) return false;

        var sshData = LoadSshData(user);
        var key = sshData.Keys.FirstOrDefault(k => k.Id == keyId);
        if (key == null) return false;

        sshData.Keys.Remove(key);
        SaveSshData(user, sshData);

        user.WhenChanged = DateTimeOffset.UtcNow;
        await _store.UpdateAsync(user, ct);

        _logger.LogInformation("SSH key deleted for {UserDn}: {Fingerprint}", userDn, key.Fingerprint);
        await AuditAsync("SshKeyDeleted", userDn, new()
        {
            ["fingerprint"] = key.Fingerprint ?? "",
            ["keyType"] = key.KeyType,
        }, ct);

        return true;
    }

    /// <summary>
    /// Get authorized_keys format output for sshd AuthorizedKeysCommand integration.
    /// Looks up the user by sAMAccountName and returns all enabled keys in OpenSSH format.
    /// </summary>
    public async Task<string> GetAuthorizedKeysAsync(string username, CancellationToken ct = default)
    {
        var domainDn = _ncService.GetDomainNc().Dn;
        var user = await _store.GetBySamAccountNameAsync("default", domainDn, username, ct);
        if (user == null) return string.Empty;

        var sshData = LoadSshData(user);

        var sb = new StringBuilder();
        foreach (var key in sshData.Keys.Where(k => k.IsEnabled))
        {
            sb.Append(key.KeyType);
            sb.Append(' ');
            sb.Append(key.PublicKeyData);
            if (!string.IsNullOrEmpty(key.Comment))
            {
                sb.Append(' ');
                sb.Append(key.Comment);
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Validate an SSH public key string format without storing it.
    /// </summary>
    public static bool ValidateKeyFormat(string publicKeyString)
    {
        return ParsePublicKey(publicKeyString) != null;
    }

    // ── Internal Helpers ─────────────────────────────────────────

    private static (string keyType, string keyData, string comment)? ParsePublicKey(string publicKeyString)
    {
        if (string.IsNullOrWhiteSpace(publicKeyString))
            return null;

        var trimmed = publicKeyString.Trim();
        var parts = trimmed.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 2)
            return null;

        var keyType = parts[0];
        var keyData = parts[1];
        var comment = parts.Length > 2 ? parts[2] : null;

        // Validate base64
        try
        {
            Convert.FromBase64String(keyData);
        }
        catch
        {
            return null;
        }

        if (!SupportedKeyTypes.Contains(keyType))
            return null;

        return (keyType, keyData, comment);
    }

    private static string ComputeFingerprint(string base64KeyData)
    {
        var keyBytes = Convert.FromBase64String(base64KeyData);
        var hash = SHA256.HashData(keyBytes);
        return "SHA256:" + Convert.ToBase64String(hash).TrimEnd('=');
    }

    private static SshKeyData LoadSshData(DirectoryObject user)
    {
        if (user.Attributes.TryGetValue(SshKeyAttributeName, out var attr) &&
            attr.Values.Count > 0 &&
            attr.Values[0] is string val && !string.IsNullOrWhiteSpace(val))
        {
            try
            {
                return JsonSerializer.Deserialize<SshKeyData>(val, JsonOpts) ?? new SshKeyData();
            }
            catch
            {
                return new SshKeyData();
            }
        }

        return new SshKeyData();
    }

    private static void SaveSshData(DirectoryObject user, SshKeyData data)
    {
        var json = JsonSerializer.Serialize(data, JsonOpts);
        user.Attributes[SshKeyAttributeName] = new DirectoryAttribute(SshKeyAttributeName, json);
    }

    private async Task AuditAsync(string action, string targetDn, Dictionary<string, string> details, CancellationToken ct)
    {
        try
        {
            await _audit.LogAsync(new AuditEntry
            {
                TenantId = "default",
                Action = action,
                TargetDn = targetDn,
                TargetObjectClass = "sshKeyService",
                ActorIdentity = "web-console",
                Details = details,
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write SSH key audit entry");
        }
    }
}
