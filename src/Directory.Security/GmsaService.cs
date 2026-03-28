using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Microsoft.Extensions.Logging;

namespace Directory.Security;

/// <summary>
/// Implements Group Managed Service Account (gMSA) management per MS-GMSAD.
///
/// gMSA passwords are automatically rotated on a configurable schedule (default 30 days).
/// The password is derived deterministically from a Key Distribution Service (KDS) root key
/// using HMAC-SHA256 with the account SID and time interval as inputs, ensuring all authorized
/// domain controllers compute the same password without needing to synchronize it.
/// </summary>
public class GmsaService
{
    private readonly IDirectoryStore _store;
    private readonly INamingContextService _ncService;
    private readonly IRidAllocator _ridAllocator;
    private readonly ILogger<GmsaService> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private const string KdsContainerCn = "Master Root Keys";
    private const string KdsObjectClass = "msKds-ProvRootKey";
    private const string GmsaObjectClass = "msDS-GroupManagedServiceAccount";

    public GmsaService(
        IDirectoryStore store,
        INamingContextService ncService,
        IRidAllocator ridAllocator,
        ILogger<GmsaService> logger)
    {
        _store = store;
        _ncService = ncService;
        _ridAllocator = ridAllocator;
        _logger = logger;
    }

    // ── gMSA Account Management ─────────────────────────────────────

    /// <summary>
    /// Create a new gMSA account. The account is stored as a directory object with
    /// objectClass=msDS-GroupManagedServiceAccount in the Managed Service Accounts container.
    /// </summary>
    public async Task<GmsaAccount> CreateGmsaAccountAsync(GmsaAccount account, CancellationToken ct = default)
    {
        var domainDn = _ncService.GetDomainNc().Dn;
        var containerDn = $"CN=Managed Service Accounts,{domainDn}";

        // Ensure name ends with $
        if (!account.Name.EndsWith('$'))
            account.Name += "$";

        var cn = account.Name.TrimEnd('$');
        var dn = $"CN={cn},{containerDn}";
        account.DistinguishedName = dn;

        // Check for existing
        var existing = await _store.GetByDnAsync("default", dn, ct);
        if (existing != null)
            throw new InvalidOperationException($"gMSA account already exists: {dn}");

        var objectSid = await _ridAllocator.GenerateObjectSidAsync("default", domainDn);
        var now = DateTimeOffset.UtcNow;
        var usn = await _store.GetNextUsnAsync("default", domainDn, ct);

        account.Id = dn.ToLowerInvariant();
        account.CreatedAt = now;
        account.PasswordLastSet = now;
        account.NextPasswordChange = now.AddDays(account.ManagedPasswordIntervalInDays);
        if (!account.IsEnabled) account.IsEnabled = true;

        var obj = new DirectoryObject
        {
            Id = dn.ToLowerInvariant(),
            TenantId = "default",
            DistinguishedName = dn,
            DomainDn = domainDn,
            ObjectCategory = GmsaObjectClass,
            ObjectClass = ["top", "person", "organizationalPerson", "user", "computer", GmsaObjectClass],
            Cn = cn,
            SAMAccountName = account.Name,
            DisplayName = cn,
            DnsHostName = account.DnsHostName,
            ParentDn = containerDn,
            ObjectGuid = Guid.NewGuid().ToString(),
            ObjectSid = objectSid,
            UserAccountControl = 0x1000, // WORKSTATION_TRUST_ACCOUNT
            WhenCreated = now,
            WhenChanged = now,
            USNCreated = usn,
            USNChanged = usn,
            SAMAccountType = 0x30000001, // SAM_MACHINE_ACCOUNT
        };

        // Set gMSA-specific attributes
        obj.SetAttribute("msDS-ManagedPasswordInterval",
            new DirectoryAttribute("msDS-ManagedPasswordInterval", account.ManagedPasswordIntervalInDays));

        if (account.PrincipalsAllowedToRetrievePassword.Count > 0)
        {
            obj.SetAttribute("msDS-GroupMSAMembership",
                new DirectoryAttribute("msDS-GroupMSAMembership",
                    [.. account.PrincipalsAllowedToRetrievePassword.Cast<object>()]));
        }

        if (account.ServicePrincipalNames.Count > 0)
        {
            obj.ServicePrincipalName = account.ServicePrincipalNames;
        }

        // Store gMSA metadata as a JSON attribute for our extended fields
        var metadataJson = JsonSerializer.Serialize(new GmsaMetadata
        {
            PasswordLastSet = account.PasswordLastSet,
            NextPasswordChange = account.NextPasswordChange,
            ManagedPasswordIntervalInDays = account.ManagedPasswordIntervalInDays,
        }, JsonOpts);
        obj.SetAttribute("gmsaMetadata", new DirectoryAttribute("gmsaMetadata", metadataJson));

        // Generate and store initial password hash
        var password = await DerivePasswordAsync(objectSid, now, ct);
        obj.NTHash = Convert.ToHexString(password);
        obj.PwdLastSet = now.ToFileTime();

        await _store.CreateAsync(obj, ct);

        _logger.LogInformation("Created gMSA account {Name} at {Dn}", account.Name, dn);
        return account;
    }

    /// <summary>
    /// List all gMSA accounts.
    /// </summary>
    public async Task<List<GmsaAccount>> GetGmsaAccountsAsync(CancellationToken ct = default)
    {
        var domainDn = _ncService.GetDomainNc().Dn;
        var filter = new EqualityFilterNode("objectClass", GmsaObjectClass);
        var result = await _store.SearchAsync("default", domainDn, SearchScope.WholeSubtree,
            filter, null, pageSize: 1000, ct: ct);

        return result.Entries
            .Where(e => !e.IsDeleted)
            .Select(MapToGmsaAccount)
            .ToList();
    }

    /// <summary>
    /// Get a specific gMSA account by sAMAccountName.
    /// </summary>
    public async Task<GmsaAccount> GetGmsaAccountAsync(string name, CancellationToken ct = default)
    {
        var obj = await ResolveGmsaObjectAsync(name, ct);
        if (obj == null) return null;
        return MapToGmsaAccount(obj);
    }

    /// <summary>
    /// Delete a gMSA account.
    /// </summary>
    public async Task<bool> DeleteGmsaAccountAsync(string name, CancellationToken ct = default)
    {
        var obj = await ResolveGmsaObjectAsync(name, ct);
        if (obj == null) return false;

        await _store.DeleteAsync("default", obj.DistinguishedName, hardDelete: true, ct);
        _logger.LogInformation("Deleted gMSA account {Name}", name);
        return true;
    }

    /// <summary>
    /// Retrieve the current managed password for a gMSA account.
    /// Only authorized principals (listed in msDS-GroupMSAMembership / PrincipalsAllowedToRetrievePassword)
    /// may retrieve the password.
    /// </summary>
    public async Task<byte[]> RetrieveManagedPasswordAsync(
        string name, string requestorDn, CancellationToken ct = default)
    {
        var obj = await ResolveGmsaObjectAsync(name, ct);
        if (obj == null) return null;

        // Check authorization
        var allowedPrincipals = obj.GetAttribute("msDS-GroupMSAMembership")?.GetStrings().ToList() ?? [];
        if (!allowedPrincipals.Any(p => p.Equals(requestorDn, StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogWarning(
                "Managed password retrieval denied for {Name}: requestor {Requestor} not in authorized list",
                name, requestorDn);
            return null;
        }

        // Check if password needs rotation
        var metadata = GetMetadata(obj);
        if (metadata != null && DateTimeOffset.UtcNow >= metadata.NextPasswordChange)
        {
            _logger.LogInformation("gMSA {Name} password has expired, rotating", name);
            await RotatePasswordInternalAsync(obj, metadata, ct);
        }

        // Derive current password
        if (string.IsNullOrEmpty(obj.ObjectSid)) return null;

        var passwordLastSet = obj.PwdLastSet > 0
            ? DateTimeOffset.FromFileTime(obj.PwdLastSet)
            : DateTimeOffset.UtcNow;

        return await DerivePasswordAsync(obj.ObjectSid, passwordLastSet, ct);
    }

    /// <summary>
    /// Force password rotation for a gMSA account.
    /// </summary>
    public async Task<bool> RotatePasswordAsync(string name, CancellationToken ct = default)
    {
        var obj = await ResolveGmsaObjectAsync(name, ct);
        if (obj == null) return false;

        var metadata = GetMetadata(obj) ?? new GmsaMetadata
        {
            ManagedPasswordIntervalInDays = 30,
        };

        await RotatePasswordInternalAsync(obj, metadata, ct);

        _logger.LogInformation("Rotated password for gMSA account {Name}", name);
        return true;
    }

    private async Task RotatePasswordInternalAsync(
        DirectoryObject obj, GmsaMetadata metadata, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        // Derive new password based on current time
        var newPassword = await DerivePasswordAsync(obj.ObjectSid, now, ct);
        obj.NTHash = Convert.ToHexString(newPassword);
        obj.PwdLastSet = now.ToFileTime();

        // Update metadata
        metadata.PasswordLastSet = now;
        metadata.NextPasswordChange = now.AddDays(metadata.ManagedPasswordIntervalInDays);
        var metadataJson = JsonSerializer.Serialize(metadata, JsonOpts);
        obj.SetAttribute("gmsaMetadata", new DirectoryAttribute("gmsaMetadata", metadataJson));

        obj.WhenChanged = now;
        await _store.UpdateAsync(obj, ct);
    }

    // ── KDS Root Key Management ─────────────────────────────────────

    /// <summary>
    /// Create a new KDS root key. At least one root key must exist before
    /// gMSA passwords can be derived.
    /// </summary>
    public async Task<KdsRootKey> CreateKdsRootKeyAsync(CancellationToken ct = default)
    {
        var domainDn = _ncService.GetDomainNc().Dn;
        var configDn = _ncService.GetConfigurationDn();
        var containerDn = $"CN={KdsContainerCn},CN=Group Key Distribution Service,CN=Services,{configDn}";

        var keyId = Guid.NewGuid().ToString();
        var keyValue = new byte[32]; // 256-bit key
        RandomNumberGenerator.Fill(keyValue);

        var now = DateTimeOffset.UtcNow;
        // Effective time is set to 10 hours in the future per AD default
        // to allow replication before any DC uses it
        var effectiveTime = now.AddHours(10);

        var rootKey = new KdsRootKey
        {
            Id = keyId,
            KeyValue = keyValue,
            EffectiveTime = effectiveTime,
            CreatedAt = now,
            KdfAlgorithm = "SP800_108_CTR_HMAC",
        };

        // Persist as a directory object
        var dn = $"CN={keyId},{containerDn}";
        var usn = await _store.GetNextUsnAsync("default", domainDn, ct);

        var obj = new DirectoryObject
        {
            Id = dn.ToLowerInvariant(),
            TenantId = "default",
            DistinguishedName = dn,
            DomainDn = domainDn,
            ObjectCategory = KdsObjectClass,
            ObjectClass = ["top", KdsObjectClass],
            Cn = keyId,
            DisplayName = $"KDS Root Key {keyId[..8]}",
            ParentDn = containerDn,
            ObjectGuid = Guid.NewGuid().ToString(),
            WhenCreated = now,
            WhenChanged = now,
            USNCreated = usn,
            USNChanged = usn,
        };

        var keyJson = JsonSerializer.Serialize(rootKey, JsonOpts);
        obj.SetAttribute("kdsRootKeyData", new DirectoryAttribute("kdsRootKeyData", keyJson));
        obj.SetAttribute("msKds-KDFAlgorithmID",
            new DirectoryAttribute("msKds-KDFAlgorithmID", rootKey.KdfAlgorithm));

        // Ensure container hierarchy exists
        await EnsureKdsContainerAsync(containerDn, configDn, domainDn, ct);

        await _store.CreateAsync(obj, ct);

        _logger.LogInformation("Created KDS root key {KeyId}, effective at {EffectiveTime}",
            keyId, effectiveTime);

        return rootKey;
    }

    /// <summary>
    /// List all KDS root keys.
    /// </summary>
    public async Task<List<KdsRootKey>> GetKdsRootKeysAsync(CancellationToken ct = default)
    {
        var configDn = _ncService.GetConfigurationDn();
        var containerDn = $"CN={KdsContainerCn},CN=Group Key Distribution Service,CN=Services,{configDn}";

        IReadOnlyList<DirectoryObject> children;
        try
        {
            children = await _store.GetChildrenAsync("default", containerDn, ct);
        }
        catch
        {
            return [];
        }

        var keys = new List<KdsRootKey>();
        foreach (var child in children)
        {
            if (child.IsDeleted) continue;
            var json = child.GetAttribute("kdsRootKeyData")?.GetFirstString();
            if (string.IsNullOrEmpty(json)) continue;

            try
            {
                var key = JsonSerializer.Deserialize<KdsRootKey>(json, JsonOpts);
                if (key != null)
                {
                    // Redact key material for list display
                    keys.Add(key with { KeyValue = [] });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize KDS root key from {Dn}", child.DistinguishedName);
            }
        }

        return keys.OrderByDescending(k => k.CreatedAt).ToList();
    }

    // ── Password Derivation ─────────────────────────────────────────

    /// <summary>
    /// Derive a gMSA password using HMAC-SHA256 with the KDS root key,
    /// account SID, and time interval as inputs per the SP800-108 CTR HMAC model.
    /// </summary>
    private async Task<byte[]> DerivePasswordAsync(
        string accountSid, DateTimeOffset passwordSetTime, CancellationToken ct)
    {
        var rootKey = await GetEffectiveRootKeyAsync(ct);
        if (rootKey == null)
            throw new InvalidOperationException(
                "No effective KDS root key found. Create a KDS root key before creating gMSA accounts.");

        // Build the derivation input: rootKey + accountSID bytes + timeInterval
        // This is a simplified version of the SP800-108 CTR-HMAC-SHA256 KDF
        var sidBytes = Encoding.UTF8.GetBytes(accountSid);
        var timeBytes = BitConverter.GetBytes(passwordSetTime.ToUnixTimeSeconds());

        using var hmac = new HMACSHA256(rootKey.KeyValue);

        // L = 256 bits, r = 32 bits counter, n = 1 iteration
        // PRF(KI, [i]2 || Label || 0x00 || Context || [L]2)
        var input = new byte[4 + sidBytes.Length + 1 + timeBytes.Length + 4];
        // Counter (i = 1)
        BitConverter.GetBytes(1).CopyTo(input, 0);
        // Label = SID
        sidBytes.CopyTo(input, 4);
        // Separator
        input[4 + sidBytes.Length] = 0x00;
        // Context = timestamp
        timeBytes.CopyTo(input, 4 + sidBytes.Length + 1);
        // L = 256
        BitConverter.GetBytes(256).CopyTo(input, input.Length - 4);

        return hmac.ComputeHash(input);
    }

    /// <summary>
    /// Get the most recent effective KDS root key (effective time in the past).
    /// </summary>
    private async Task<KdsRootKey> GetEffectiveRootKeyAsync(CancellationToken ct)
    {
        var configDn = _ncService.GetConfigurationDn();
        var containerDn = $"CN={KdsContainerCn},CN=Group Key Distribution Service,CN=Services,{configDn}";

        IReadOnlyList<DirectoryObject> children;
        try
        {
            children = await _store.GetChildrenAsync("default", containerDn, ct);
        }
        catch
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        KdsRootKey best = null;

        foreach (var child in children)
        {
            if (child.IsDeleted) continue;
            var json = child.GetAttribute("kdsRootKeyData")?.GetFirstString();
            if (string.IsNullOrEmpty(json)) continue;

            try
            {
                var key = JsonSerializer.Deserialize<KdsRootKey>(json, JsonOpts);
                if (key == null || key.EffectiveTime > now) continue;

                if (best == null || key.EffectiveTime > best.EffectiveTime)
                    best = key;
            }
            catch { /* skip malformed keys */ }
        }

        return best;
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private async Task<DirectoryObject> ResolveGmsaObjectAsync(string name, CancellationToken ct)
    {
        var domainDn = _ncService.GetDomainNc().Dn;

        // Normalize name — accept both "svc$" and "svc" forms
        var samName = name.EndsWith('$') ? name : name + "$";

        var obj = await _store.GetBySamAccountNameAsync("default", domainDn, samName, ct);
        if (obj != null && obj.ObjectClass.Contains(GmsaObjectClass))
            return obj;

        // Also try without $ suffix in case that's how it was stored
        var bare = name.TrimEnd('$');
        obj = await _store.GetBySamAccountNameAsync("default", domainDn, bare, ct);
        if (obj != null && obj.ObjectClass.Contains(GmsaObjectClass))
            return obj;

        return null;
    }

    private static GmsaAccount MapToGmsaAccount(DirectoryObject obj)
    {
        var principals = obj.GetAttribute("msDS-GroupMSAMembership")?.GetStrings().ToList() ?? [];
        var metadata = GetMetadata(obj);

        var interval = 30;
        var intervalAttr = obj.GetAttribute("msDS-ManagedPasswordInterval");
        if (intervalAttr != null && int.TryParse(intervalAttr.GetFirstString(), out var parsed))
            interval = parsed;

        return new GmsaAccount
        {
            Id = obj.ObjectGuid ?? obj.Id,
            Name = obj.SAMAccountName ?? obj.Cn ?? "",
            DistinguishedName = obj.DistinguishedName,
            DnsHostName = obj.DnsHostName ?? "",
            ServicePrincipalNames = obj.ServicePrincipalName.ToList(),
            PrincipalsAllowedToRetrievePassword = principals,
            ManagedPasswordIntervalInDays = interval,
            PasswordLastSet = metadata?.PasswordLastSet
                ?? (obj.PwdLastSet > 0 ? DateTimeOffset.FromFileTime(obj.PwdLastSet) : DateTimeOffset.MinValue),
            NextPasswordChange = metadata?.NextPasswordChange ?? DateTimeOffset.MinValue,
            IsEnabled = (obj.UserAccountControl & 0x2) == 0,
            CreatedAt = obj.WhenCreated,
        };
    }

    private static GmsaMetadata GetMetadata(DirectoryObject obj)
    {
        var json = obj.GetAttribute("gmsaMetadata")?.GetFirstString();
        if (string.IsNullOrEmpty(json)) return null;

        try
        {
            return JsonSerializer.Deserialize<GmsaMetadata>(json, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            });
        }
        catch
        {
            return null;
        }
    }

    private async Task EnsureKdsContainerAsync(
        string containerDn, string configDn, string domainDn, CancellationToken ct)
    {
        // Ensure the nested container hierarchy:
        // CN=Services,<configDn> -> CN=Group Key Distribution Service -> CN=Master Root Keys
        var servicesDn = $"CN=Services,{configDn}";
        var gkdsDn = $"CN=Group Key Distribution Service,CN=Services,{configDn}";

        await EnsureContainerObjectAsync(servicesDn, "Services", configDn, domainDn, ct);
        await EnsureContainerObjectAsync(gkdsDn, "Group Key Distribution Service", servicesDn, domainDn, ct);
        await EnsureContainerObjectAsync(containerDn, KdsContainerCn, gkdsDn, domainDn, ct);
    }

    private async Task EnsureContainerObjectAsync(
        string dn, string cn, string parentDn, string domainDn, CancellationToken ct)
    {
        var existing = await _store.GetByDnAsync("default", dn, ct);
        if (existing != null) return;

        var usn = await _store.GetNextUsnAsync("default", domainDn, ct);
        var now = DateTimeOffset.UtcNow;

        var container = new DirectoryObject
        {
            Id = dn.ToLowerInvariant(),
            TenantId = "default",
            DistinguishedName = dn,
            DomainDn = domainDn,
            ObjectCategory = "container",
            ObjectClass = ["top", "container"],
            Cn = cn,
            DisplayName = cn,
            ParentDn = parentDn,
            ObjectGuid = Guid.NewGuid().ToString(),
            WhenCreated = now,
            WhenChanged = now,
            USNCreated = usn,
            USNChanged = usn,
        };

        try
        {
            await _store.CreateAsync(container, ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Container {Dn} may already exist, continuing", dn);
        }
    }
}

// ── Models ──────────────────────────────────────────────────────────

/// <summary>
/// Represents a Group Managed Service Account (gMSA).
/// </summary>
public class GmsaAccount
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>sAMAccountName, must end with $.</summary>
    public string Name { get; set; } = "";

    public string DistinguishedName { get; set; } = "";
    public string DnsHostName { get; set; } = "";
    public List<string> ServicePrincipalNames { get; set; } = new();

    /// <summary>
    /// DNs of principals (computers/groups) allowed to retrieve the managed password.
    /// </summary>
    public List<string> PrincipalsAllowedToRetrievePassword { get; set; } = new();

    public int ManagedPasswordIntervalInDays { get; set; } = 30;
    public DateTimeOffset PasswordLastSet { get; set; }
    public DateTimeOffset NextPasswordChange { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Represents a Key Distribution Service root key used for gMSA password derivation.
/// </summary>
public record KdsRootKey
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>256-bit root key value. Redacted (empty) in list responses.</summary>
    public byte[] KeyValue { get; set; } = [];

    /// <summary>
    /// Time after which this key becomes effective. Set to 10 hours after creation
    /// by default to allow replication.
    /// </summary>
    public DateTimeOffset EffectiveTime { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Key derivation function algorithm identifier.</summary>
    public string KdfAlgorithm { get; set; } = "SP800_108_CTR_HMAC";
}

/// <summary>
/// Internal metadata stored on the gMSA directory object for tracking password rotation.
/// </summary>
internal class GmsaMetadata
{
    public DateTimeOffset PasswordLastSet { get; set; }
    public DateTimeOffset NextPasswordChange { get; set; }
    public int ManagedPasswordIntervalInDays { get; set; } = 30;
}
