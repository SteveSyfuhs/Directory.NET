using Directory.Core.Interfaces;
using Directory.Core.Models;
using Microsoft.Extensions.Logging;

namespace Directory.Schema;

/// <summary>
/// Manages AD DS optional features per [MS-ADTS] section 3.1.1.9.
/// Tracks which optional features have been enabled and provides
/// a query interface. Feature state is stored as objects under
/// CN=Optional Features,CN=Directory Service,CN=Windows NT,CN=Services,CN=Configuration,{domainDn}.
/// </summary>
public class OptionalFeatureService
{
    private readonly IDirectoryStore _store;
    private readonly ILogger<OptionalFeatureService> _logger;

    /// <summary>
    /// Recycle Bin feature (Windows Server 2008 R2+).
    /// OID: 766ddcd8-acd0-445e-f3b9-a7f9b6744f2a
    /// </summary>
    public static readonly Guid RecycleBinFeature =
        new("766ddcd8-acd0-445e-f3b9-a7f9b6744f2a");

    /// <summary>
    /// Privileged Access Management feature (Windows Server 2016+).
    /// OID: 73e843ec-e906-4bbf-8ea7-c40d7f7c04a0
    /// </summary>
    public static readonly Guid PrivilegedAccessManagementFeature =
        new("73e843ec-e906-4bbf-8ea7-c40d7f7c04a0");

    /// <summary>
    /// Known feature definitions: (GUID, CN, description).
    /// </summary>
    private static readonly (Guid Id, string Name, string Description)[] KnownFeatures =
    [
        (RecycleBinFeature, "Recycle Bin Feature", "Enables the Active Directory Recycle Bin, allowing deleted objects to be restored"),
        (PrivilegedAccessManagementFeature, "Privileged Access Management Feature", "Enables Privileged Access Management, allowing time-bound group memberships"),
    ];

    /// <summary>
    /// In-memory cache of enabled features, keyed by tenantId.
    /// </summary>
    private readonly Dictionary<string, HashSet<Guid>> _enabledFeatures = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _lock = new(1, 1);

    public OptionalFeatureService(
        IDirectoryStore store,
        ILogger<OptionalFeatureService> logger)
    {
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// Returns the known optional feature definitions.
    /// </summary>
    public static IReadOnlyList<(Guid Id, string Name, string Description)> GetKnownFeatures() => KnownFeatures;

    /// <summary>
    /// Returns the DN of the Optional Features container for the given domain.
    /// </summary>
    public static string GetOptionalFeaturesContainerDn(string domainDn) =>
        $"CN=Optional Features,CN=Directory Service,CN=Windows NT,CN=Services,CN=Configuration,{domainDn}";

    /// <summary>
    /// Checks whether a specific optional feature is enabled for the given tenant.
    /// </summary>
    public async Task<bool> IsFeatureEnabledAsync(
        string tenantId, string domainDn, Guid featureId, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_enabledFeatures.TryGetValue(tenantId, out var cached))
            {
                return cached.Contains(featureId);
            }
        }
        finally
        {
            _lock.Release();
        }

        // Load from store
        await LoadFeaturesAsync(tenantId, domainDn, ct);

        await _lock.WaitAsync(ct);
        try
        {
            return _enabledFeatures.TryGetValue(tenantId, out var set) && set.Contains(featureId);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Synchronous check using the in-memory cache only.
    /// Call <see cref="LoadFeaturesAsync"/> at startup to populate the cache.
    /// </summary>
    public bool IsFeatureEnabled(string tenantId, Guid featureId)
    {
        lock (_enabledFeatures)
        {
            return _enabledFeatures.TryGetValue(tenantId, out var set) && set.Contains(featureId);
        }
    }

    /// <summary>
    /// Enables an optional feature. Creates the feature object in the directory
    /// and updates the in-memory cache.
    /// </summary>
    public async Task EnableFeatureAsync(
        string tenantId, string domainDn, Guid featureId, CancellationToken ct = default)
    {
        var knownFeature = KnownFeatures.FirstOrDefault(f => f.Id == featureId);
        if (knownFeature == default)
        {
            throw new ArgumentException($"Unknown optional feature: {featureId}");
        }

        var containerDn = GetOptionalFeaturesContainerDn(domainDn);

        // Ensure the container hierarchy exists
        await EnsureContainerHierarchyAsync(tenantId, domainDn, ct);

        // Create the feature object if it doesn't exist
        var featureDn = $"CN={knownFeature.Name},{containerDn}";
        var existing = await _store.GetByDnAsync(tenantId, featureDn, ct);

        if (existing == null)
        {
            var now = DateTimeOffset.UtcNow;
            var featureObj = new DirectoryObject
            {
                Id = featureDn.ToLowerInvariant(),
                TenantId = tenantId,
                DomainDn = domainDn,
                DistinguishedName = featureDn,
                ObjectGuid = Guid.NewGuid().ToString(),
                ObjectClass = ["top", "msDS-OptionalFeature"],
                ParentDn = containerDn,
                Cn = knownFeature.Name,
                Description = knownFeature.Description,
                WhenCreated = now,
                WhenChanged = now,
            };

            featureObj.SetAttribute("msDS-OptionalFeatureGUID",
                new DirectoryAttribute("msDS-OptionalFeatureGUID", featureId.ToString()));
            featureObj.SetAttribute("msDS-OptionalFeatureFlags",
                new DirectoryAttribute("msDS-OptionalFeatureFlags", 1)); // Enabled
            featureObj.SetAttribute("msDS-RequiredForestBehaviorVersion",
                new DirectoryAttribute("msDS-RequiredForestBehaviorVersion",
                    featureId == RecycleBinFeature ? 4 : 7));

            await _store.CreateAsync(featureObj, ct);
            _logger.LogInformation("Enabled optional feature: {Name} ({Id})", knownFeature.Name, featureId);
        }

        // Update cache
        await _lock.WaitAsync(ct);
        try
        {
            if (!_enabledFeatures.TryGetValue(tenantId, out var set))
            {
                set = [];
                _enabledFeatures[tenantId] = set;
            }
            set.Add(featureId);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Loads the enabled features from the directory into the in-memory cache.
    /// Should be called at startup.
    /// </summary>
    public async Task LoadFeaturesAsync(string tenantId, string domainDn, CancellationToken ct = default)
    {
        var containerDn = GetOptionalFeaturesContainerDn(domainDn);
        var enabledSet = new HashSet<Guid>();

        try
        {
            var children = await _store.GetChildrenAsync(tenantId, containerDn, ct);
            foreach (var child in children)
            {
                var guidAttr = child.GetAttribute("msDS-OptionalFeatureGUID");
                if (guidAttr != null && Guid.TryParse(guidAttr.GetFirstString(), out var fid))
                {
                    enabledSet.Add(fid);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not load optional features from {Container} — container may not exist yet", containerDn);
        }

        await _lock.WaitAsync(ct);
        try
        {
            _enabledFeatures[tenantId] = enabledSet;
        }
        finally
        {
            _lock.Release();
        }

        _logger.LogInformation("Loaded {Count} enabled optional features for tenant {TenantId}",
            enabledSet.Count, tenantId);
    }

    /// <summary>
    /// Provisions the Optional Features container hierarchy.
    /// Creates intermediate containers if they do not exist.
    /// </summary>
    public async Task ProvisionContainerAsync(string tenantId, string domainDn, CancellationToken ct = default)
    {
        await EnsureContainerHierarchyAsync(tenantId, domainDn, ct);
    }

    private async Task EnsureContainerHierarchyAsync(string tenantId, string domainDn, CancellationToken ct)
    {
        var configDn = $"CN=Configuration,{domainDn}";

        // Build the hierarchy from top to bottom
        var hierarchy = new[]
        {
            ($"CN=Services,{configDn}", "Services"),
            ($"CN=Windows NT,CN=Services,{configDn}", "Windows NT"),
            ($"CN=Directory Service,CN=Windows NT,CN=Services,{configDn}", "Directory Service"),
            ($"CN=Optional Features,CN=Directory Service,CN=Windows NT,CN=Services,{configDn}", "Optional Features"),
        };

        string parentDn = configDn;
        foreach (var (dn, cn) in hierarchy)
        {
            var existing = await _store.GetByDnAsync(tenantId, dn, ct);
            if (existing == null)
            {
                var now = DateTimeOffset.UtcNow;
                var obj = new DirectoryObject
                {
                    Id = dn.ToLowerInvariant(),
                    TenantId = tenantId,
                    DomainDn = domainDn,
                    DistinguishedName = dn,
                    ObjectGuid = Guid.NewGuid().ToString(),
                    ObjectClass = ["top", "container"],
                    ParentDn = parentDn,
                    Cn = cn,
                    WhenCreated = now,
                    WhenChanged = now,
                };

                await _store.CreateAsync(obj, ct);
                _logger.LogDebug("Created container: {Dn}", dn);
            }

            parentDn = dn;
        }
    }
}
