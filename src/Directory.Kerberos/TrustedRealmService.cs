using System.Collections.Concurrent;
using System.Security.Cryptography;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Kerberos.NET.Crypto;
using Kerberos.NET.Entities;
using Kerberos.NET.Entities.Pac;
using Kerberos.NET.Server;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Directory.Kerberos;

/// <summary>
/// Manages Kerberos trust relationships between realms for cross-realm referrals.
/// Per MS-KILE, when a TGS-REQ targets a principal in another realm,
/// the KDC returns a referral ticket (krbtgt/TARGET@SOURCE) encrypted with
/// the inter-realm trust key so the client can follow the referral.
///
/// Trust objects are stored in CosmosDB as trustedDomain objects and cached in memory.
/// </summary>
public class TrustedRealmService : ITrustedRealmService
{
    private readonly IDirectoryStore _store;
    private readonly ILogger<TrustedRealmService> _logger;
    private readonly ConcurrentDictionary<string, TrustRelationship> _trustsByDns = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, TrustRelationship> _trustsByNetBios = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _localRealm;
    private bool _loaded;

    public TrustedRealmService(IDirectoryStore store, IOptions<KerberosOptions> options, ILogger<TrustedRealmService> logger)
    {
        _store = store;
        _localRealm = options.Value.DefaultRealm;
        _logger = logger;
    }

    /// <summary>
    /// Load trust objects from CosmosDB and populate the in-memory cache.
    /// Called on startup and after trust mutations.
    /// </summary>
    public async Task LoadTrustsAsync(CancellationToken ct = default)
    {
        try
        {
            var domainDn = RealmToDomainDn(_localRealm);
            var systemDn = $"CN=System,{domainDn}";

            var filter = new EqualityFilterNode("objectCategory", "trustedDomain");
            var result = await _store.SearchAsync(
                "default", systemDn, SearchScope.SingleLevel, filter,
                null, pageSize: 500, ct: ct);

            _trustsByDns.Clear();
            _trustsByNetBios.Clear();

            foreach (var entry in result.Entries.Where(e => !e.IsDeleted))
            {
                var trust = DirectoryObjectToTrust(entry);
                if (trust is not null)
                {
                    _trustsByDns[trust.TargetRealm] = trust;
                    if (!string.IsNullOrEmpty(trust.FlatName))
                        _trustsByNetBios[trust.FlatName] = trust;

                    _logger.LogInformation(
                        "Loaded trust: {Source} -> {Target} (type={Type}, direction={Direction})",
                        trust.SourceRealm, trust.TargetRealm, trust.TrustType, trust.Direction);
                }
            }

            _loaded = true;
            _logger.LogInformation("Loaded {Count} trust relationships from directory", _trustsByDns.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load trust relationships from directory; trusts will be empty");
            _loaded = true;
        }
    }

    /// <summary>
    /// Register a trust relationship with another realm (in-memory + persistent).
    /// </summary>
    public async Task AddTrustAsync(TrustRelationship trust, CancellationToken ct = default)
    {
        var obj = TrustToDirectoryObject(trust);
        await _store.CreateAsync(obj, ct);

        _trustsByDns[trust.TargetRealm] = trust;
        if (!string.IsNullOrEmpty(trust.FlatName))
            _trustsByNetBios[trust.FlatName] = trust;

        _logger.LogInformation(
            "Trust added: {Source} -> {Target} (type={Type}, direction={Direction})",
            trust.SourceRealm, trust.TargetRealm, trust.TrustType, trust.Direction);
    }

    /// <summary>
    /// Update an existing trust relationship.
    /// </summary>
    public async Task UpdateTrustAsync(TrustRelationship trust, CancellationToken ct = default)
    {
        var domainDn = RealmToDomainDn(_localRealm);
        var dn = $"CN={trust.TargetRealm},CN=System,{domainDn}";
        var existing = await _store.GetByDnAsync("default", dn, ct);

        if (existing is null)
            throw new InvalidOperationException($"Trust object not found: {dn}");

        ApplyTrustToDirectoryObject(existing, trust);
        await _store.UpdateAsync(existing, ct);

        _trustsByDns[trust.TargetRealm] = trust;
        if (!string.IsNullOrEmpty(trust.FlatName))
            _trustsByNetBios[trust.FlatName] = trust;

        _logger.LogInformation("Trust updated: {Target}", trust.TargetRealm);
    }

    /// <summary>
    /// Remove a trust relationship.
    /// </summary>
    public async Task<bool> RemoveTrustAsync(string targetRealm, CancellationToken ct = default)
    {
        var domainDn = RealmToDomainDn(_localRealm);
        var dn = $"CN={targetRealm},CN=System,{domainDn}";

        await _store.DeleteAsync("default", dn, hardDelete: true, ct);

        var removed = _trustsByDns.TryRemove(targetRealm, out var trust);
        if (trust is not null && !string.IsNullOrEmpty(trust.FlatName))
            _trustsByNetBios.TryRemove(trust.FlatName, out _);

        if (removed)
            _logger.LogInformation("Trust removed for realm: {Realm}", targetRealm);

        return removed;
    }

    /// <summary>
    /// Get all configured trust relationships.
    /// </summary>
    public IReadOnlyCollection<TrustRelationship> GetTrusts() => _trustsByDns.Values.ToList();

    /// <summary>
    /// Find a trust by DNS domain name or NetBIOS name.
    /// </summary>
    public TrustRelationship FindTrust(string realmName)
    {
        if (_trustsByDns.TryGetValue(realmName, out var trust))
            return trust;
        if (_trustsByNetBios.TryGetValue(realmName, out trust))
            return trust;
        return null;
    }

    /// <summary>
    /// Check if a trust exists for the given realm.
    /// </summary>
    public bool HasTrust(string realm) =>
        _trustsByDns.ContainsKey(realm) || _trustsByNetBios.ContainsKey(realm);

    /// <summary>
    /// Compute the trust path from local realm to target realm for transitive trusts.
    /// Uses BFS over the trust graph. Returns the ordered list of realms to traverse,
    /// or null if no path exists.
    /// </summary>
    public IReadOnlyList<string> ComputeTrustPath(string targetRealm)
    {
        if (_trustsByDns.TryGetValue(targetRealm, out var directTrust) && CanRefer(directTrust))
            return [targetRealm];

        // For transitive trust path computation, we do a BFS from the local realm
        // Only forest trusts and parent-child trusts are transitive
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { _localRealm };
        var queue = new Queue<(string Realm, List<string> Path)>();

        foreach (var t in _trustsByDns.Values.Where(IsTransitiveTrust))
        {
            if (CanRefer(t))
            {
                if (t.TargetRealm.Equals(targetRealm, StringComparison.OrdinalIgnoreCase))
                    return [t.TargetRealm];

                visited.Add(t.TargetRealm);
                queue.Enqueue((t.TargetRealm, [t.TargetRealm]));
            }
        }

        // Walk the trust graph using BFS. For parent-child trusts within a forest,
        // the path follows the DNS hierarchy. For forest trusts, transitive trust
        // chains are discovered by checking whether intermediate realms have known
        // trusts that reach the target. We also infer parent-child trust paths by
        // walking up/down the DNS hierarchy when both endpoints share a common root.
        while (queue.Count > 0)
        {
            var (current, path) = queue.Dequeue();

            // 1. Check if any of our known transitive trusts point from 'current' to the target.
            //    This handles forest trust chains where an intermediate realm has a direct trust
            //    to the target or to another hop on the way.
            foreach (var t in _trustsByDns.Values.Where(IsTransitiveTrust))
            {
                if (!visited.Contains(t.TargetRealm) && CanRefer(t))
                {
                    if (t.TargetRealm.Equals(targetRealm, StringComparison.OrdinalIgnoreCase))
                    {
                        var finalPath = new List<string>(path) { t.TargetRealm };
                        return finalPath;
                    }

                    visited.Add(t.TargetRealm);
                    queue.Enqueue((t.TargetRealm, new List<string>(path) { t.TargetRealm }));
                }
            }

            // 2. Infer parent-child trust paths via DNS hierarchy.
            //    In an AD forest, parent-child domains have automatic two-way transitive trusts
            //    that follow the DNS naming hierarchy (e.g., child.corp.example.com → corp.example.com).
            //    Walk up from current realm and down toward target to find a meeting point.
            var parentOfCurrent = GetParentDomain(current);
            if (parentOfCurrent != null && !visited.Contains(parentOfCurrent))
            {
                // Walking up the tree toward a common ancestor
                if (parentOfCurrent.Equals(targetRealm, StringComparison.OrdinalIgnoreCase))
                {
                    var finalPath = new List<string>(path) { parentOfCurrent };
                    return finalPath;
                }

                if (IsAncestorDomain(parentOfCurrent, targetRealm) ||
                    IsAncestorDomain(parentOfCurrent, current))
                {
                    visited.Add(parentOfCurrent);
                    queue.Enqueue((parentOfCurrent, new List<string>(path) { parentOfCurrent }));
                }
            }

            // Check if target is a child of current (walk down)
            if (IsAncestorDomain(current, targetRealm))
            {
                // Find the next child domain in the path toward target
                var nextChild = GetNextChildDomain(current, targetRealm);
                if (nextChild != null && !visited.Contains(nextChild))
                {
                    if (nextChild.Equals(targetRealm, StringComparison.OrdinalIgnoreCase))
                    {
                        var finalPath = new List<string>(path) { nextChild };
                        return finalPath;
                    }

                    visited.Add(nextChild);
                    queue.Enqueue((nextChild, new List<string>(path) { nextChild }));
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Propose a transit/referral for a TGS-REQ targeting another realm.
    /// Returns a referral if a trust path exists, null otherwise.
    /// </summary>
    public IRealmReferral ProposeTransit(KrbTgsReq tgsReq, PreAuthenticationContext context)
    {
        if (!_loaded)
        {
            _logger.LogWarning("TrustedRealmService not yet loaded; cannot propose transit");
            return null;
        }

        var targetRealm = tgsReq.Body.Realm;

        // Direct trust lookup
        var trust = FindTrust(targetRealm);

        if (trust is null)
        {
            // Try transitive trust path
            var path = ComputeTrustPath(targetRealm);
            if (path is null || path.Count == 0)
            {
                _logger.LogDebug("No trust found for realm: {Realm}", targetRealm);
                return null;
            }

            // Refer to the first hop in the trust path
            trust = FindTrust(path[0]);
            if (trust is null)
            {
                _logger.LogDebug("Trust path computed but first hop not found: {Hop}", path[0]);
                return null;
            }
        }

        // Verify the trust direction allows outbound referrals
        if (!CanRefer(trust))
        {
            _logger.LogDebug(
                "Trust to {Realm} exists but direction ({Direction}) does not allow outbound referrals",
                targetRealm, trust.Direction);
            return null;
        }

        _logger.LogInformation(
            "Proposing referral to {Realm} via {TrustType} trust",
            trust.TargetRealm, trust.TrustType);

        return new RealmReferral(trust, _localRealm, _logger);
    }

    /// <summary>
    /// Verify that a trust relationship is functional by checking the trust object
    /// and inter-realm key are present.
    /// </summary>
    public TrustVerificationResult VerifyTrust(string targetRealm)
    {
        var trust = FindTrust(targetRealm);
        if (trust is null)
            return new TrustVerificationResult(false, "Trust not found");

        if (trust.TrustKey is null || trust.TrustKey.Length == 0)
            return new TrustVerificationResult(false, "Inter-realm key is not configured");

        if (trust.Direction == TrustDirection.Disabled)
            return new TrustVerificationResult(false, "Trust is disabled");

        return new TrustVerificationResult(true, "Trust is operational",
            trust.Direction, trust.TrustType);
    }

    /// <summary>
    /// Generate an inter-realm trust key from a shared secret.
    /// The key is derived using PBKDF2 consistent with Kerberos key derivation.
    /// </summary>
    public static byte[] DeriveInterRealmKey(string sharedSecret, string sourceRealm, string targetRealm)
    {
        var salt = $"krbtgt/{targetRealm.ToUpperInvariant()}@{sourceRealm.ToUpperInvariant()}";
        var saltBytes = System.Text.Encoding.UTF8.GetBytes(salt);
        return System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(
            System.Text.Encoding.UTF8.GetBytes(sharedSecret),
            saltBytes,
            4096,
            HashAlgorithmName.SHA256,
            32); // AES-256 key length
    }

    private static bool CanRefer(TrustRelationship trust) =>
        trust.Direction is TrustDirection.Outbound or TrustDirection.Bidirectional;

    private static bool IsTransitiveTrust(TrustRelationship trust) =>
        trust.TrustType is TrustType.Forest or TrustType.ParentChild or TrustType.CrossLink
        && (trust.TrustAttributes & TrustAttributes.NonTransitive) == 0;

    /// <summary>
    /// Gets the parent domain from a DNS domain name (e.g., "child.corp.example.com" → "corp.example.com").
    /// Returns null if the domain has 2 or fewer labels (already at root).
    /// </summary>
    private static string GetParentDomain(string domain)
    {
        var dotIndex = domain.IndexOf('.');
        if (dotIndex < 0) return null;

        var parent = domain[(dotIndex + 1)..];
        // Require at least 2 labels in the parent (e.g., "example.com")
        return parent.Contains('.') ? parent : null;
    }

    /// <summary>
    /// Returns true if <paramref name="ancestor"/> is a DNS ancestor of <paramref name="descendant"/>.
    /// E.g., "corp.example.com" is an ancestor of "child.corp.example.com".
    /// </summary>
    private static bool IsAncestorDomain(string ancestor, string descendant)
    {
        return descendant.Length > ancestor.Length + 1
            && descendant.EndsWith(ancestor, StringComparison.OrdinalIgnoreCase)
            && descendant[descendant.Length - ancestor.Length - 1] == '.';
    }

    /// <summary>
    /// Given an ancestor domain and a descendant domain, returns the next immediate child
    /// domain on the path from ancestor to descendant.
    /// E.g., ancestor="corp.example.com", descendant="leaf.child.corp.example.com" → "child.corp.example.com".
    /// </summary>
    private static string GetNextChildDomain(string ancestor, string descendant)
    {
        if (!IsAncestorDomain(ancestor, descendant))
            return null;

        // Strip the ancestor suffix (+1 for the dot)
        var prefix = descendant[..^(ancestor.Length + 1)];

        // The next child is the last label in the prefix + ancestor
        var lastDot = prefix.LastIndexOf('.');
        var childLabel = lastDot >= 0 ? prefix[(lastDot + 1)..] : prefix;
        return $"{childLabel}.{ancestor}";
    }

    private TrustRelationship DirectoryObjectToTrust(DirectoryObject obj)
    {
        try
        {
            var trustPartner = obj.GetAttribute("trustPartner")?.GetFirstString();
            if (string.IsNullOrEmpty(trustPartner))
                return null;

            var flatName = obj.GetAttribute("flatName")?.GetFirstString() ?? "";

            var directionStr = obj.GetAttribute("trustDirection")?.GetFirstString();
            var direction = Enum.TryParse<TrustDirection>(directionStr, out var dir) ? dir : TrustDirection.Disabled;

            var typeStr = obj.GetAttribute("trustType")?.GetFirstString();
            var trustType = Enum.TryParse<TrustType>(typeStr, out var tt) ? tt : TrustType.External;

            var attrsStr = obj.GetAttribute("trustAttributes")?.GetFirstString();
            var trustAttributes = int.TryParse(attrsStr, out var ta) ? (TrustAttributes)ta : TrustAttributes.None;

            var sid = obj.GetAttribute("securityIdentifier")?.GetFirstString() ?? "";

            var keyBase64 = obj.GetAttribute("interRealmKey")?.GetFirstString();
            var trustKey = !string.IsNullOrEmpty(keyBase64) ? Convert.FromBase64String(keyBase64) : null;

            return new TrustRelationship
            {
                ObjectGuid = obj.ObjectGuid,
                SourceRealm = _localRealm,
                TargetRealm = trustPartner.ToUpperInvariant(),
                FlatName = flatName.ToUpperInvariant(),
                TrustType = trustType,
                Direction = direction,
                TrustAttributes = trustAttributes,
                SecurityIdentifier = sid,
                TrustKey = trustKey,
                WhenCreated = obj.WhenCreated,
                WhenChanged = obj.WhenChanged,
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse trust object: {DN}", obj.DistinguishedName);
            return null;
        }
    }

    private DirectoryObject TrustToDirectoryObject(TrustRelationship trust)
    {
        var domainDn = RealmToDomainDn(_localRealm);
        var dn = $"CN={trust.TargetRealm},CN=System,{domainDn}";
        var now = DateTimeOffset.UtcNow;

        var obj = new DirectoryObject
        {
            Id = dn.ToLowerInvariant(),
            TenantId = "default",
            DistinguishedName = dn,
            Cn = trust.TargetRealm,
            DomainDn = domainDn,
            ObjectCategory = "trustedDomain",
            ObjectClass = ["top", "leaf", "trustedDomain"],
            ParentDn = $"CN=System,{domainDn}",
            ObjectGuid = string.IsNullOrEmpty(trust.ObjectGuid) ? Guid.NewGuid().ToString() : trust.ObjectGuid,
            WhenCreated = now,
            WhenChanged = now,
        };

        ApplyTrustToDirectoryObject(obj, trust);
        return obj;
    }

    private static void ApplyTrustToDirectoryObject(DirectoryObject obj, TrustRelationship trust)
    {
        obj.SetAttribute("trustPartner", new DirectoryAttribute("trustPartner", trust.TargetRealm));
        obj.SetAttribute("flatName", new DirectoryAttribute("flatName", trust.FlatName ?? ""));
        obj.SetAttribute("trustDirection", new DirectoryAttribute("trustDirection", ((int)trust.Direction).ToString()));
        obj.SetAttribute("trustType", new DirectoryAttribute("trustType", ((int)trust.TrustType).ToString()));
        obj.SetAttribute("trustAttributes", new DirectoryAttribute("trustAttributes", ((int)trust.TrustAttributes).ToString()));
        obj.SetAttribute("securityIdentifier", new DirectoryAttribute("securityIdentifier", trust.SecurityIdentifier ?? ""));

        if (trust.TrustKey is not null)
        {
            obj.SetAttribute("interRealmKey", new DirectoryAttribute("interRealmKey", Convert.ToBase64String(trust.TrustKey)));
        }
    }

    private static string RealmToDomainDn(string realm)
    {
        var parts = realm.ToLowerInvariant().Split('.');
        return string.Join(",", parts.Select(p => $"DC={p}"));
    }
}

/// <summary>
/// Represents a trust relationship between two Kerberos realms.
/// </summary>
public class TrustRelationship
{
    /// <summary>ObjectGuid of the trust object in the directory.</summary>
    public string ObjectGuid { get; init; } = "";

    /// <summary>Source (local) realm name.</summary>
    public string SourceRealm { get; init; } = "";

    /// <summary>Target (remote) realm DNS name (uppercase).</summary>
    public string TargetRealm { get; init; } = "";

    /// <summary>NetBIOS (flat) name of the partner domain.</summary>
    public string FlatName { get; init; }

    /// <summary>Type of trust (forest, external, parent-child, etc.).</summary>
    public TrustType TrustType { get; init; }

    /// <summary>Direction of the trust.</summary>
    public TrustDirection Direction { get; init; }

    /// <summary>Trust attribute flags.</summary>
    public TrustAttributes TrustAttributes { get; init; }

    /// <summary>Security identifier of the partner domain (S-1-5-21-...).</summary>
    public string SecurityIdentifier { get; init; }

    /// <summary>Inter-realm trust key used to encrypt referral tickets.</summary>
    public byte[] TrustKey { get; init; }

    /// <summary>When the trust was created.</summary>
    public DateTimeOffset WhenCreated { get; init; }

    /// <summary>When the trust was last modified.</summary>
    public DateTimeOffset WhenChanged { get; init; }
}

/// <summary>
/// Trust type per MS-ADTS section 6.1.6.7.
/// </summary>
public enum TrustType
{
    /// <summary>Downlevel trust (Windows NT 4.0 and earlier).</summary>
    Downlevel = 1,

    /// <summary>Uplevel trust (Windows 2000 and later).</summary>
    Uplevel = 2,

    /// <summary>Trust with a non-Windows MIT Kerberos realm.</summary>
    MIT = 3,

    /// <summary>DCE-style trust.</summary>
    DCE = 4,

    /// <summary>Non-transitive trust with a specific Windows domain.</summary>
    External = 5,

    /// <summary>Transitive trust with an entire AD forest.</summary>
    Forest = 6,

    /// <summary>Automatic transitive trust between parent and child domains.</summary>
    ParentChild = 7,

    /// <summary>Shortcut trust between two domains in the same forest.</summary>
    CrossLink = 8,

    /// <summary>Trust with a non-Windows MIT Kerberos realm.</summary>
    Realm = 9,
}

/// <summary>
/// Trust direction flags.
/// </summary>
public enum TrustDirection
{
    Disabled = 0,
    Inbound = 1,
    Outbound = 2,
    Bidirectional = 3,
}

/// <summary>
/// Trust attribute flags per MS-ADTS.
/// </summary>
[Flags]
public enum TrustAttributes
{
    None = 0,
    NonTransitive = 1,
    UplevelOnly = 2,
    QuarantinedDomain = 4,
    ForestTransitive = 8,
    CrossOrganization = 16,
    WithinForest = 32,
    TreatAsExternal = 64,
    UsesRc4Encryption = 128,
    CrossOrganizationNoTgtDelegation = 512,
    CrossOrganizationEnableTgtDelegation = 2048,
}

/// <summary>
/// Result of a trust verification check.
/// </summary>
public record TrustVerificationResult(
    bool IsOperational,
    string Message,
    TrustDirection? Direction = null,
    TrustType? TrustType = null);

/// <summary>
/// Referral to a trusted realm, implementing the Kerberos.NET IRealmReferral interface.
/// </summary>
internal class RealmReferral : IRealmReferral
{
    private readonly TrustRelationship _trust;
    private readonly string _localRealm;
    private readonly ILogger _logger;

    public RealmReferral(TrustRelationship trust, string localRealm, ILogger logger)
    {
        _trust = trust;
        _localRealm = localRealm;
        _logger = logger;
    }

    public string Realm => _trust.TargetRealm;

    public IKerberosPrincipal Refer()
    {
        if (_trust.TrustKey is null || _trust.TrustKey.Length == 0)
        {
            throw new InvalidOperationException(
                $"Inter-realm key not configured for trust to '{_trust.TargetRealm}'");
        }

        _logger.LogInformation(
            "Generating inter-realm referral principal krbtgt/{Target}@{Source}",
            _trust.TargetRealm, _localRealm);

        return new InterRealmTgtPrincipal(
            _trust.TargetRealm,
            _localRealm,
            _trust.TrustKey);
    }
}

/// <summary>
/// A synthetic Kerberos principal representing the inter-realm krbtgt account
/// (krbtgt/TARGETREALM@LOCALREALM). The long-term credential is the inter-realm
/// trust key, allowing the KDC to encrypt referral TGTs that the target realm can decrypt.
/// </summary>
internal class InterRealmTgtPrincipal : IKerberosPrincipal
{
    private readonly string _targetRealm;
    private readonly string _localRealm;
    private readonly byte[] _trustKey;

    public InterRealmTgtPrincipal(string targetRealm, string localRealm, byte[] trustKey)
    {
        _targetRealm = targetRealm;
        _localRealm = localRealm;
        _trustKey = trustKey;
    }

    public IEnumerable<PaDataType> SupportedPreAuthenticationTypes { get; set; } = [];

    public SupportedEncryptionTypes SupportedEncryptionTypes { get; set; } =
        SupportedEncryptionTypes.Aes256CtsHmacSha196 |
        SupportedEncryptionTypes.Aes128CtsHmacSha196 |
        SupportedEncryptionTypes.Rc4Hmac;

    public PrincipalType Type => PrincipalType.Service;

    public string PrincipalName => $"krbtgt/{_targetRealm}";

    public DateTimeOffset? Expires => null;

    public KerberosKey RetrieveLongTermCredential()
    {
        return new KerberosKey(
            _trustKey,
            principal: new PrincipalName(
                PrincipalNameType.NT_SRV_INST,
                _localRealm,
                ["krbtgt", _targetRealm]),
            etype: EncryptionType.AES256_CTS_HMAC_SHA1_96);
    }

    public KerberosKey RetrieveLongTermCredential(EncryptionType etype)
    {
        return new KerberosKey(
            _trustKey,
            principal: new PrincipalName(
                PrincipalNameType.NT_SRV_INST,
                _localRealm,
                ["krbtgt", _targetRealm]),
            etype: etype);
    }

    public PrivilegedAttributeCertificate GeneratePac()
    {
        // Inter-realm TGTs carry a minimal PAC with just the referral info.
        return new PrivilegedAttributeCertificate
        {
            ServerSignature = new PacSignature(PacType.SERVER_CHECKSUM, EncryptionType.NULL),
            KdcSignature = new PacSignature(PacType.PRIVILEGE_SERVER_CHECKSUM, EncryptionType.NULL),
        };
    }

    public void Validate(System.Security.Cryptography.X509Certificates.X509Certificate2Collection certificates) { }
}
