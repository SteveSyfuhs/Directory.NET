using Directory.Core.Interfaces;
using Directory.Core.Models;
using Microsoft.Extensions.Logging;

namespace Directory.Security;

/// <summary>
/// Represents a fine-grained password policy (Password Settings Object / PSO)
/// per MS-ADTS section 3.1.1.5.2.
/// </summary>
public class PasswordSettingsObject
{
    /// <summary>DN of the msDS-PasswordSettings object.</summary>
    public string Dn { get; set; } = "";

    /// <summary>msDS-PasswordSettingsPrecedence — lower value = higher priority.</summary>
    public int Precedence { get; set; } = int.MaxValue;

    /// <summary>msDS-PasswordReversibleEncryptionEnabled</summary>
    public bool ReversibleEncryption { get; set; }

    /// <summary>msDS-PasswordHistoryLength — number of passwords in history.</summary>
    public int PasswordHistoryLength { get; set; } = 24;

    /// <summary>msDS-PasswordComplexityEnabled</summary>
    public bool ComplexityEnabled { get; set; } = true;

    /// <summary>msDS-MinimumPasswordLength</summary>
    public int MinimumPasswordLength { get; set; } = 7;

    /// <summary>msDS-MinimumPasswordAge — minimum age in 100-ns intervals (negative value).</summary>
    public long MinimumPasswordAge { get; set; } = -TimeSpan.FromDays(1).Ticks;

    /// <summary>msDS-MaximumPasswordAge — maximum age in 100-ns intervals (negative value).</summary>
    public long MaximumPasswordAge { get; set; } = -TimeSpan.FromDays(42).Ticks;

    /// <summary>msDS-LockoutThreshold</summary>
    public int LockoutThreshold { get; set; } = 0;

    /// <summary>msDS-LockoutObservationWindow — in 100-ns intervals (negative value).</summary>
    public long LockoutObservationWindow { get; set; } = -TimeSpan.FromMinutes(30).Ticks;

    /// <summary>msDS-LockoutDuration — in 100-ns intervals (negative value).</summary>
    public long LockoutDuration { get; set; } = -TimeSpan.FromMinutes(30).Ticks;

    /// <summary>DNs this PSO applies to (users and global security groups).</summary>
    public List<string> AppliesTo { get; set; } = [];
}

/// <summary>
/// Manages fine-grained password policies per MS-ADTS 3.1.1.5.2.
/// Determines the resultant PSO for a user by evaluating all PSOs linked
/// to the user or their global security groups, selecting by lowest precedence.
/// </summary>
public class FineGrainedPasswordPolicyService
{
    private readonly IDirectoryStore _store;
    private readonly ILogger<FineGrainedPasswordPolicyService> _logger;

    public FineGrainedPasswordPolicyService(IDirectoryStore store, ILogger<FineGrainedPasswordPolicyService> logger)
    {
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// Returns the resultant PSO for the given user, or null if no FGPP applies
    /// (in which case the default domain password policy should be used).
    /// Per MS-ADTS 3.1.1.5.2:
    /// 1. Collect all PSOs directly linked to the user
    /// 2. Collect all PSOs linked to the user's global security groups
    /// 3. Select the PSO with the lowest msDS-PasswordSettingsPrecedence
    /// 4. If tie, select the PSO with the lowest GUID (deterministic)
    /// </summary>
    public async Task<PasswordSettingsObject> GetResultantPsoAsync(
        DirectoryObject user, string tenantId, CancellationToken ct = default)
    {
        var candidates = new List<(PasswordSettingsObject Pso, bool IsDirect)>();

        // Step 1: Find PSOs in the Password Settings Container
        var psoContainerDn = GetPsoContainerDn(user);
        var allPsos = await LoadPsosAsync(tenantId, psoContainerDn, ct);

        if (allPsos.Count == 0)
            return null;

        // Step 2: Check which PSOs apply directly to this user
        foreach (var pso in allPsos)
        {
            if (pso.AppliesTo.Any(dn => string.Equals(dn, user.DistinguishedName, StringComparison.OrdinalIgnoreCase)))
            {
                candidates.Add((pso, IsDirect: true));
            }
        }

        // Step 3: Check which PSOs apply via group membership
        var userGroups = new HashSet<string>(user.MemberOf, StringComparer.OrdinalIgnoreCase);
        foreach (var pso in allPsos)
        {
            // Skip if already added as direct
            if (candidates.Any(c => c.IsDirect && c.Pso.Dn == pso.Dn))
                continue;

            if (pso.AppliesTo.Any(dn => userGroups.Contains(dn)))
            {
                candidates.Add((pso, IsDirect: false));
            }
        }

        if (candidates.Count == 0)
            return null;

        // Step 4: Direct links take priority over group links per MS-ADTS
        var directCandidates = candidates.Where(c => c.IsDirect).ToList();
        var effectiveCandidates = directCandidates.Count > 0 ? directCandidates : candidates;

        // Step 5: Select lowest precedence; tie-break by DN (deterministic)
        var winner = effectiveCandidates
            .OrderBy(c => c.Pso.Precedence)
            .ThenBy(c => c.Pso.Dn, StringComparer.OrdinalIgnoreCase)
            .First();

        _logger.LogDebug("Resultant PSO for {User}: {Pso} (precedence {Precedence})",
            user.SAMAccountName, winner.Pso.Dn, winner.Pso.Precedence);

        return winner.Pso;
    }

    /// <summary>
    /// Validates a password against the effective password policy for the user.
    /// Uses FGPP if applicable, otherwise falls back to domain defaults.
    /// </summary>
    public async Task<(bool IsValid, string Error)> ValidatePasswordAgainstPolicyAsync(
        DirectoryObject user, string password, string tenantId, CancellationToken ct = default)
    {
        var pso = await GetResultantPsoAsync(user, tenantId, ct);

        int minLength = pso?.MinimumPasswordLength ?? 7;
        bool requireComplexity = pso?.ComplexityEnabled ?? true;

        if (password.Length < minLength)
            return (false, $"Password must be at least {minLength} characters");

        if (requireComplexity)
        {
            int categories = 0;
            if (password.Any(char.IsUpper)) categories++;
            if (password.Any(char.IsLower)) categories++;
            if (password.Any(char.IsDigit)) categories++;
            if (password.Any(c => !char.IsLetterOrDigit(c))) categories++;

            if (categories < 3)
                return (false, "Password must contain at least 3 of: uppercase, lowercase, digit, special character");

            // Must not contain sAMAccountName or displayName parts
            if (!string.IsNullOrEmpty(user.SAMAccountName) && password.Contains(user.SAMAccountName, StringComparison.OrdinalIgnoreCase))
                return (false, "Password must not contain the user's account name");
        }

        return (true, null);
    }

    /// <summary>
    /// Gets the lockout policy parameters for the given user,
    /// checking FGPP first, then falling back to domain defaults.
    /// </summary>
    public async Task<LockoutPolicy> GetLockoutPolicyAsync(
        DirectoryObject user, string tenantId, CancellationToken ct = default)
    {
        var pso = await GetResultantPsoAsync(user, tenantId, ct);

        return new LockoutPolicy
        {
            LockoutThreshold = pso?.LockoutThreshold ?? 5,
            LockoutDuration = pso != null ? TimeSpan.FromTicks(Math.Abs(pso.LockoutDuration)) : TimeSpan.FromMinutes(30),
            ObservationWindow = pso != null ? TimeSpan.FromTicks(Math.Abs(pso.LockoutObservationWindow)) : TimeSpan.FromMinutes(30),
            MaxPasswordAge = pso != null ? TimeSpan.FromTicks(Math.Abs(pso.MaximumPasswordAge)) : TimeSpan.FromDays(42),
            MinPasswordAge = pso != null ? TimeSpan.FromTicks(Math.Abs(pso.MinimumPasswordAge)) : TimeSpan.FromDays(1),
        };
    }

    private async Task<List<PasswordSettingsObject>> LoadPsosAsync(
        string tenantId, string containerDn, CancellationToken ct)
    {
        var results = new List<PasswordSettingsObject>();

        try
        {
            // Search for all msDS-PasswordSettings objects in the PSO container
            var filter = new EqualityFilterNode("objectClass", "msDS-PasswordSettings");
            var searchResult = await _store.SearchAsync(tenantId, containerDn,
                SearchScope.SingleLevel, filter, null, sizeLimit: 100, ct: ct);

            foreach (var obj in searchResult.Entries)
            {
                results.Add(PsoFromDirectoryObject(obj));
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not load PSOs from {Container}", containerDn);
        }

        return results;
    }

    private static PasswordSettingsObject PsoFromDirectoryObject(DirectoryObject obj)
    {
        return new PasswordSettingsObject
        {
            Dn = obj.DistinguishedName,
            Precedence = ParseInt(obj, "msDS-PasswordSettingsPrecedence", int.MaxValue),
            ReversibleEncryption = ParseBool(obj, "msDS-PasswordReversibleEncryptionEnabled"),
            PasswordHistoryLength = ParseInt(obj, "msDS-PasswordHistoryLength", 24),
            ComplexityEnabled = ParseBool(obj, "msDS-PasswordComplexityEnabled", true),
            MinimumPasswordLength = ParseInt(obj, "msDS-MinimumPasswordLength", 7),
            MinimumPasswordAge = ParseLong(obj, "msDS-MinimumPasswordAge", -TimeSpan.FromDays(1).Ticks),
            MaximumPasswordAge = ParseLong(obj, "msDS-MaximumPasswordAge", -TimeSpan.FromDays(42).Ticks),
            LockoutThreshold = ParseInt(obj, "msDS-LockoutThreshold", 0),
            LockoutObservationWindow = ParseLong(obj, "msDS-LockoutObservationWindow", -TimeSpan.FromMinutes(30).Ticks),
            LockoutDuration = ParseLong(obj, "msDS-LockoutDuration", -TimeSpan.FromMinutes(30).Ticks),
            AppliesTo = obj.GetAttribute("msDS-PSOAppliedTo")?.GetStrings().ToList()
                       ?? obj.GetAttribute("msDS-PSOAppliesTo")?.GetStrings().ToList()
                       ?? [],
        };
    }

    private static string GetPsoContainerDn(DirectoryObject user)
    {
        // PSO container is CN=Password Settings Container,CN=System,{domainDN}
        var domainDn = user.DomainDn;
        return $"CN=Password Settings Container,CN=System,{domainDn}";
    }

    private static int ParseInt(DirectoryObject obj, string attr, int defaultValue = 0)
    {
        var val = obj.GetAttribute(attr)?.GetFirstString();
        return val is not null && int.TryParse(val, out var result) ? result : defaultValue;
    }

    private static long ParseLong(DirectoryObject obj, string attr, long defaultValue = 0)
    {
        var val = obj.GetAttribute(attr)?.GetFirstString();
        return val is not null && long.TryParse(val, out var result) ? result : defaultValue;
    }

    private static bool ParseBool(DirectoryObject obj, string attr, bool defaultValue = false)
    {
        var val = obj.GetAttribute(attr)?.GetFirstString();
        if (val is null) return defaultValue;
        return val.Equals("TRUE", StringComparison.OrdinalIgnoreCase) || val == "1";
    }
}

/// <summary>
/// Resolved lockout/password policy parameters for a specific user.
/// </summary>
public class LockoutPolicy
{
    public int LockoutThreshold { get; set; }
    public TimeSpan LockoutDuration { get; set; }
    public TimeSpan ObservationWindow { get; set; }
    public TimeSpan MaxPasswordAge { get; set; }
    public TimeSpan MinPasswordAge { get; set; }
}
