using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Web.Models;

using SearchScope = Directory.Core.Models.SearchScope;

namespace Directory.Web.Services;

/// <summary>
/// Manages domain and forest functional levels. Supports querying current levels,
/// determining the maximum achievable level, identifying blocking DCs, and raising
/// levels (irreversible operations).
/// </summary>
public class FunctionalLevelService
{
    private readonly IDirectoryStore _store;
    private readonly DcInstanceInfo _dcInfo;
    private readonly DomainConfiguration _domainConfig;
    private readonly ILogger<FunctionalLevelService> _logger;

    private const string TenantId = "default";

    /// <summary>
    /// Static feature catalog describing features unlocked at each functional level.
    /// </summary>
    private static readonly List<FunctionalLevelFeature> FeatureCatalog = new()
    {
        new() { Name = "Basic AD Operations", Description = "Core LDAP, Kerberos, Group Policy support", RequiredDomainLevel = DomainFunctionalLevel.Windows2000 },
        new() { Name = "Universal Group Caching", Description = "Cache universal group membership at site level", RequiredDomainLevel = DomainFunctionalLevel.Windows2003 },
        new() { Name = "Constrained Delegation", Description = "Kerberos constrained delegation (S4U2Proxy)", RequiredDomainLevel = DomainFunctionalLevel.Windows2003 },
        new() { Name = "Fine-Grained Password Policies", Description = "Multiple password policies per domain via PSOs", RequiredDomainLevel = DomainFunctionalLevel.Windows2008 },
        new() { Name = "AES Kerberos Encryption", Description = "AES 128/256 for Kerberos authentication", RequiredDomainLevel = DomainFunctionalLevel.Windows2008 },
        new() { Name = "AD Recycle Bin", Description = "Recover deleted objects with all attributes intact", RequiredDomainLevel = DomainFunctionalLevel.Windows2008R2 },
        new() { Name = "Managed Service Accounts", Description = "Automatic password management for service accounts", RequiredDomainLevel = DomainFunctionalLevel.Windows2008R2 },
        new() { Name = "Claims-Based Access Control", Description = "Dynamic access control using claims and resource properties", RequiredDomainLevel = DomainFunctionalLevel.Windows2012 },
        new() { Name = "Group Managed Service Accounts", Description = "gMSA for multi-server service account scenarios", RequiredDomainLevel = DomainFunctionalLevel.Windows2012 },
        new() { Name = "Authentication Policies", Description = "Authentication policy silos for protected users", RequiredDomainLevel = DomainFunctionalLevel.Windows2012R2 },
        new() { Name = "Protected Users Group", Description = "Enhanced credential protection for sensitive accounts", RequiredDomainLevel = DomainFunctionalLevel.Windows2012R2 },
        new() { Name = "Privileged Access Management", Description = "Time-limited group membership and shadow principals", RequiredDomainLevel = DomainFunctionalLevel.Windows2016 },
        new() { Name = "NTLM Authentication Restrictions", Description = "Enhanced NTLM blocking policies", RequiredDomainLevel = DomainFunctionalLevel.Windows2016 },
        new() { Name = "DNSSEC Auto-Signing", Description = "Automatic DNSSEC signing for all AD-integrated DNS zones", RequiredDomainLevel = DomainFunctionalLevel.DirectoryNET_v1 },
        new() { Name = "Built-in MFA", Description = "Native multi-factor authentication without third-party add-ons", RequiredDomainLevel = DomainFunctionalLevel.DirectoryNET_v1 },
        new() { Name = "Cosmos DB Geo-Replication", Description = "Native Cosmos DB multi-region replication support", RequiredDomainLevel = DomainFunctionalLevel.DirectoryNET_v1 },
        new() { Name = "Modern REST API", Description = "Full REST API for all directory operations", RequiredDomainLevel = DomainFunctionalLevel.DirectoryNET_v1 },
    };

    public FunctionalLevelService(
        IDirectoryStore store,
        DcInstanceInfo dcInfo,
        DomainConfiguration domainConfig,
        ILogger<FunctionalLevelService> logger)
    {
        _store = store;
        _dcInfo = dcInfo;
        _domainConfig = domainConfig;
        _logger = logger;
    }

    /// <summary>
    /// Returns the current domain and forest functional levels, the maximum achievable
    /// levels given all DCs, and which DCs are blocking an upgrade.
    /// </summary>
    public async Task<FunctionalLevelStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var domainDn = _domainConfig.DomainDn;
        var currentDomainLevel = (DomainFunctionalLevel)_domainConfig.DomainFunctionalLevel;

        // Read forest functional level from the Partitions container
        var configDn = $"CN=Configuration,{domainDn}";
        var currentForestLevel = await GetForestFunctionalLevelAsync(configDn, ct);

        // Determine max achievable levels based on all DCs
        var (maxDomain, maxForest, blockingDcs) = await CalculateMaxLevelsAsync(domainDn, ct);

        // Build feature availability list
        var features = FeatureCatalog.Select(f => new FunctionalLevelFeature
        {
            Name = f.Name,
            Description = f.Description,
            RequiredDomainLevel = f.RequiredDomainLevel,
            IsEnabled = currentDomainLevel >= f.RequiredDomainLevel,
        }).ToList();

        return new FunctionalLevelStatus
        {
            CurrentDomainLevel = currentDomainLevel,
            CurrentForestLevel = currentForestLevel,
            MaxDomainLevel = maxDomain,
            MaxForestLevel = maxForest,
            BlockingDcs = blockingDcs,
            AvailableFeatures = features,
        };
    }

    /// <summary>
    /// Raises the domain functional level. This operation is irreversible.
    /// </summary>
    public async Task<FunctionalLevelRaiseResult> RaiseDomainFunctionalLevelAsync(
        DomainFunctionalLevel target, CancellationToken ct = default)
    {
        try
        {
            var currentLevel = (DomainFunctionalLevel)_domainConfig.DomainFunctionalLevel;

            if (target <= currentLevel)
            {
                return new FunctionalLevelRaiseResult
                {
                    Success = false,
                    ErrorMessage = $"Target level {target} ({(int)target}) is not higher than current level {currentLevel} ({(int)currentLevel}).",
                };
            }

            // Verify all DCs support the target level
            var (maxDomain, _, blockingDcs) = await CalculateMaxLevelsAsync(_domainConfig.DomainDn, ct);
            if (target > maxDomain)
            {
                return new FunctionalLevelRaiseResult
                {
                    Success = false,
                    ErrorMessage = $"Cannot raise to {target}: blocked by DCs: {string.Join(", ", blockingDcs)}",
                };
            }

            // Update the domain object's msDS-Behavior-Version attribute
            var domainObj = await _store.GetByDnAsync(TenantId, _domainConfig.DomainDn, ct);
            if (domainObj == null)
            {
                return new FunctionalLevelRaiseResult
                {
                    Success = false,
                    ErrorMessage = "Domain root object not found.",
                };
            }

            domainObj.SetAttribute("msDS-Behavior-Version",
                new DirectoryAttribute("msDS-Behavior-Version", (int)target));
            domainObj.WhenChanged = DateTimeOffset.UtcNow;
            await _store.UpdateAsync(domainObj, ct);

            // Update the in-memory configuration
            _domainConfig.DomainFunctionalLevel = (int)target;

            _logger.LogInformation("Domain functional level raised from {From} to {To}",
                currentLevel, target);

            return new FunctionalLevelRaiseResult
            {
                Success = true,
                PreviousLevel = (int)currentLevel,
                NewLevel = (int)target,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to raise domain functional level");
            return new FunctionalLevelRaiseResult
            {
                Success = false,
                ErrorMessage = ex.Message,
            };
        }
    }

    /// <summary>
    /// Raises the forest functional level. This operation is irreversible.
    /// The domain functional level must be at least as high as the target forest level.
    /// </summary>
    public async Task<FunctionalLevelRaiseResult> RaiseForestFunctionalLevelAsync(
        ForestFunctionalLevel target, CancellationToken ct = default)
    {
        try
        {
            var configDn = $"CN=Configuration,{_domainConfig.DomainDn}";
            var currentForestLevel = await GetForestFunctionalLevelAsync(configDn, ct);

            if (target <= currentForestLevel)
            {
                return new FunctionalLevelRaiseResult
                {
                    Success = false,
                    ErrorMessage = $"Target level {target} ({(int)target}) is not higher than current forest level {currentForestLevel} ({(int)currentForestLevel}).",
                };
            }

            // Forest level cannot exceed domain level
            var currentDomainLevel = (DomainFunctionalLevel)_domainConfig.DomainFunctionalLevel;
            if ((int)target > (int)currentDomainLevel)
            {
                return new FunctionalLevelRaiseResult
                {
                    Success = false,
                    ErrorMessage = $"Cannot raise forest level to {target}: domain functional level is only {currentDomainLevel}. Raise domain level first.",
                };
            }

            // Update the Partitions container's msDS-Behavior-Version
            var partitionsDn = $"CN=Partitions,{configDn}";
            var partitionsObj = await _store.GetByDnAsync(TenantId, partitionsDn, ct);
            if (partitionsObj == null)
            {
                return new FunctionalLevelRaiseResult
                {
                    Success = false,
                    ErrorMessage = "Partitions container not found in Configuration NC.",
                };
            }

            partitionsObj.SetAttribute("msDS-Behavior-Version",
                new DirectoryAttribute("msDS-Behavior-Version", (int)target));
            partitionsObj.WhenChanged = DateTimeOffset.UtcNow;
            await _store.UpdateAsync(partitionsObj, ct);

            _logger.LogInformation("Forest functional level raised from {From} to {To}",
                currentForestLevel, target);

            return new FunctionalLevelRaiseResult
            {
                Success = true,
                PreviousLevel = (int)currentForestLevel,
                NewLevel = (int)target,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to raise forest functional level");
            return new FunctionalLevelRaiseResult
            {
                Success = false,
                ErrorMessage = ex.Message,
            };
        }
    }

    /// <summary>
    /// Returns the feature catalog with availability based on the current domain level.
    /// </summary>
    public List<FunctionalLevelFeature> GetAvailableFeatures()
    {
        var currentLevel = (DomainFunctionalLevel)_domainConfig.DomainFunctionalLevel;
        return FeatureCatalog.Select(f => new FunctionalLevelFeature
        {
            Name = f.Name,
            Description = f.Description,
            RequiredDomainLevel = f.RequiredDomainLevel,
            IsEnabled = currentLevel >= f.RequiredDomainLevel,
        }).ToList();
    }

    // ── Private helpers ─────────────────────────────────────────────────────

    private async Task<ForestFunctionalLevel> GetForestFunctionalLevelAsync(string configDn, CancellationToken ct)
    {
        var partitionsDn = $"CN=Partitions,{configDn}";
        var partitionsObj = await _store.GetByDnAsync(TenantId, partitionsDn, ct);

        if (partitionsObj != null)
        {
            var levelStr = partitionsObj.GetAttribute("msDS-Behavior-Version")?.GetFirstString();
            if (int.TryParse(levelStr, out var level))
            {
                return (ForestFunctionalLevel)level;
            }
        }

        // Default to same as domain level
        return (ForestFunctionalLevel)_domainConfig.DomainFunctionalLevel;
    }

    private async Task<(DomainFunctionalLevel MaxDomain, ForestFunctionalLevel MaxForest, List<string> BlockingDcs)>
        CalculateMaxLevelsAsync(string domainDn, CancellationToken ct)
    {
        var configDn = $"CN=Configuration,{domainDn}";
        var sitesDn = $"CN=Sites,{configDn}";

        // Find all DCs by searching for nTDSDSA objects
        var searchResult = await _store.SearchAsync(TenantId, sitesDn,
            SearchScope.WholeSubtree,
            new EqualityFilterNode("objectClass", "nTDSDSA"),
            null, ct: ct);

        if (searchResult.Entries.Count == 0)
        {
            // No DCs found — allow maximum level
            return (DomainFunctionalLevel.DirectoryNET_v1, ForestFunctionalLevel.DirectoryNET_v1, new List<string>());
        }

        var minLevel = int.MaxValue;
        var blockingDcs = new List<string>();

        foreach (var ntds in searchResult.Entries)
        {
            // Read the DC's functional level from its msDS-Behavior-Version or default to 2016
            var levelStr = ntds.GetAttribute("msDS-Behavior-Version")?.GetFirstString();
            var dcLevel = 7; // Default to Windows 2016
            if (int.TryParse(levelStr, out var parsed))
            {
                dcLevel = parsed;
            }

            if (dcLevel < minLevel)
            {
                minLevel = dcLevel;
                blockingDcs.Clear();
            }

            if (dcLevel == minLevel && dcLevel < (int)DomainFunctionalLevel.DirectoryNET_v1)
            {
                var parts = ntds.DistinguishedName.Split(',');
                var serverName = parts.Length > 1 ? parts[1].Replace("CN=", "") : ntds.DistinguishedName;
                blockingDcs.Add(serverName);
            }
        }

        // The max level is the minimum DC level across all DCs
        var maxDomainLevel = (DomainFunctionalLevel)minLevel;
        var maxForestLevel = (ForestFunctionalLevel)minLevel;

        // Only list blocking DCs if they prevent raising above current
        var currentDomainLevel = (DomainFunctionalLevel)_domainConfig.DomainFunctionalLevel;
        if (maxDomainLevel <= currentDomainLevel)
        {
            // DCs at the current level are only "blocking" if there is a higher level possible
            blockingDcs.Clear();
        }

        return (maxDomainLevel, maxForestLevel, blockingDcs);
    }
}

// ── Models ──────────────────────────────────────────────────────────────────

public enum DomainFunctionalLevel
{
    Windows2000 = 0,
    Windows2003 = 2,
    Windows2008 = 3,
    Windows2008R2 = 4,
    Windows2012 = 5,
    Windows2012R2 = 6,
    Windows2016 = 7,
    DirectoryNET_v1 = 100,
}

public enum ForestFunctionalLevel
{
    Windows2000 = 0,
    Windows2003 = 2,
    Windows2008 = 3,
    Windows2008R2 = 4,
    Windows2012 = 5,
    Windows2012R2 = 6,
    Windows2016 = 7,
    DirectoryNET_v1 = 100,
}

public class FunctionalLevelStatus
{
    public DomainFunctionalLevel CurrentDomainLevel { get; set; }
    public ForestFunctionalLevel CurrentForestLevel { get; set; }
    public DomainFunctionalLevel MaxDomainLevel { get; set; }
    public ForestFunctionalLevel MaxForestLevel { get; set; }
    public List<string> BlockingDcs { get; set; } = new();
    public List<FunctionalLevelFeature> AvailableFeatures { get; set; } = new();
}

public class FunctionalLevelFeature
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public DomainFunctionalLevel RequiredDomainLevel { get; set; }
    public bool IsEnabled { get; set; }
}

public class FunctionalLevelRaiseResult
{
    public bool Success { get; set; }
    public int? PreviousLevel { get; set; }
    public int? NewLevel { get; set; }
    public string ErrorMessage { get; set; }
}
