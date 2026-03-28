using System.Net;
using System.Text.Json;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Microsoft.Extensions.Logging;

namespace Directory.Security.Mfa;

/// <summary>
/// Evaluates conditional access policies to determine whether MFA is required,
/// access should be blocked, or other actions should be taken for a given sign-in attempt.
/// Policies are stored as a custom attribute on a well-known configuration object.
/// </summary>
public class ConditionalMfaService
{
    private const string PolicyAttributeName = "msDS-ConditionalAccessPolicies";
    private const string SignInLogAttributeName = "msDS-ConditionalAccessSignInLog";
    private const string ConfigObjectCn = "CN=Conditional Access Config";
    private const int MaxSignInLogEntries = 1000;

    private readonly IDirectoryStore _store;
    private readonly INamingContextService _ncService;
    private readonly ILogger<ConditionalMfaService> _logger;

    public ConditionalMfaService(
        IDirectoryStore store,
        INamingContextService ncService,
        ILogger<ConditionalMfaService> logger)
    {
        _store = store;
        _ncService = ncService;
        _logger = logger;
    }

    /// <summary>
    /// Evaluate all enabled conditional access policies against a sign-in attempt.
    /// Policies are evaluated in priority order (lower number = higher priority).
    /// </summary>
    public async Task<AccessEvaluationResult> EvaluateAccess(
        AccessEvaluationRequest request, CancellationToken ct = default)
    {
        var policies = await GetPolicies(ct);
        var enabledPolicies = policies.Where(p => p.IsEnabled).OrderBy(p => p.Priority).ToList();

        var result = new AccessEvaluationResult
        {
            AccessGranted = true,
            MfaRequired = false,
            AllowedMfaMethods = new List<string>(),
        };

        // Resolve user group memberships for group-based conditions
        var userGroups = await ResolveUserGroupsAsync(request.UserDn, ct);

        foreach (var policy in enabledPolicies)
        {
            var entry = new PolicyEvaluationEntry
            {
                PolicyId = policy.Id,
                PolicyName = policy.Name,
            };

            var matches = EvaluateConditions(policy.Conditions, request, userGroups);
            entry.Matched = matches;
            entry.Reason = matches ? "All conditions matched" : "Conditions not met";

            if (matches)
            {
                // Apply actions
                if (policy.Actions.BlockAccess)
                {
                    result.AccessGranted = false;
                    result.BlockReason = $"Blocked by policy: {policy.Name}";
                }

                if (policy.Actions.RequireMfa)
                {
                    result.MfaRequired = true;
                    // Intersect allowed methods if already set, otherwise set
                    if (result.AllowedMfaMethods.Count > 0)
                    {
                        result.AllowedMfaMethods = result.AllowedMfaMethods
                            .Intersect(policy.Actions.AllowedMfaMethods)
                            .ToList();
                    }
                    else
                    {
                        result.AllowedMfaMethods = new List<string>(policy.Actions.AllowedMfaMethods);
                    }
                }

                if (policy.Actions.RequirePasswordChange)
                    result.PasswordChangeRequired = true;

                if (policy.Actions.SessionLifetimeMinutes.HasValue)
                {
                    // Use the most restrictive session lifetime
                    if (!result.SessionLifetimeMinutes.HasValue ||
                        policy.Actions.SessionLifetimeMinutes.Value < result.SessionLifetimeMinutes.Value)
                    {
                        result.SessionLifetimeMinutes = policy.Actions.SessionLifetimeMinutes;
                    }
                }
            }

            result.EvaluatedPolicies.Add(entry);
        }

        // Log the evaluation
        await LogSignInEvaluationAsync(request, result, ct);

        _logger.LogInformation("Conditional access evaluated for {UserDn}: granted={Granted}, mfa={Mfa}",
            request.UserDn, result.AccessGranted, result.MfaRequired);

        return result;
    }

    /// <summary>
    /// Evaluate whether the policy conditions match the given request.
    /// All specified conditions must match (AND logic).
    /// </summary>
    private bool EvaluateConditions(
        PolicyConditions conditions, AccessEvaluationRequest request, HashSet<string> userGroups)
    {
        // User includes/excludes
        if (conditions.ExcludeUsers.Count > 0 &&
            conditions.ExcludeUsers.Contains(request.UserDn, StringComparer.OrdinalIgnoreCase))
            return false;

        if (conditions.ExcludeGroups.Count > 0 &&
            conditions.ExcludeGroups.Any(g => userGroups.Contains(g)))
            return false;

        if (conditions.IncludeUsers.Count > 0)
        {
            var includesAll = conditions.IncludeUsers.Contains("All", StringComparer.OrdinalIgnoreCase);
            if (!includesAll && !conditions.IncludeUsers.Contains(request.UserDn, StringComparer.OrdinalIgnoreCase))
                return false;
        }

        if (conditions.IncludeGroups.Count > 0)
        {
            if (!conditions.IncludeGroups.Any(g => userGroups.Contains(g)))
                return false;
        }

        // Application
        if (conditions.IncludeApplications.Count > 0 && !string.IsNullOrEmpty(request.ApplicationId))
        {
            if (!conditions.IncludeApplications.Contains(request.ApplicationId, StringComparer.OrdinalIgnoreCase))
                return false;
        }

        // IP range check
        if (conditions.IpRanges.Count > 0 && !string.IsNullOrEmpty(request.ClientIp))
        {
            if (IPAddress.TryParse(request.ClientIp, out var clientIp))
            {
                var inRange = conditions.IpRanges.Any(cidr => IsInCidrRange(clientIp, cidr));
                if (!inRange)
                    return false;
            }
        }

        // Country check
        if (conditions.Countries.Count > 0)
        {
            // Country resolution would typically use a GeoIP database.
            // For now, this condition is evaluated as a pass-through when no GeoIP is available.
        }

        // Risk level
        if (conditions.MinRiskLevel.HasValue && request.RiskLevel.HasValue)
        {
            if (request.RiskLevel.Value < conditions.MinRiskLevel.Value)
                return false;
        }

        // Device platform
        if (conditions.DevicePlatforms.Count > 0 && request.Device?.Platform is not null)
        {
            if (!conditions.DevicePlatforms.Contains(request.Device.Platform, StringComparer.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Check if an IP address falls within a CIDR range.
    /// </summary>
    private static bool IsInCidrRange(IPAddress address, string cidr)
    {
        try
        {
            var parts = cidr.Split('/');
            if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var network) ||
                !int.TryParse(parts[1], out var prefixLength))
                return false;

            var addressBytes = address.GetAddressBytes();
            var networkBytes = network.GetAddressBytes();

            if (addressBytes.Length != networkBytes.Length)
                return false;

            var wholeBytesToCheck = prefixLength / 8;
            var remainingBits = prefixLength % 8;

            for (var i = 0; i < wholeBytesToCheck; i++)
            {
                if (addressBytes[i] != networkBytes[i])
                    return false;
            }

            if (remainingBits > 0 && wholeBytesToCheck < addressBytes.Length)
            {
                var mask = (byte)(0xFF << (8 - remainingBits));
                if ((addressBytes[wholeBytesToCheck] & mask) != (networkBytes[wholeBytesToCheck] & mask))
                    return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<HashSet<string>> ResolveUserGroupsAsync(string userDn, CancellationToken ct)
    {
        try
        {
            var obj = await _store.GetByDnAsync("default", userDn, ct);
            if (obj is null) return new HashSet<string>();

            // Use the pre-materialized MemberOf list (transitive group DNs)
            var groups = obj.MemberOf ?? [];
            return new HashSet<string>(groups, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new HashSet<string>();
        }
    }

    // --- CRUD operations for policies ---

    public async Task<List<ConditionalAccessPolicy>> GetPolicies(CancellationToken ct = default)
    {
        var configObj = await GetOrCreateConfigObjectAsync(ct);
        return GetPoliciesFromObject(configObj);
    }

    public async Task<ConditionalAccessPolicy> CreatePolicy(ConditionalAccessPolicy policy, CancellationToken ct = default)
    {
        policy.Id = Guid.NewGuid().ToString();
        policy.CreatedAt = DateTimeOffset.UtcNow;
        policy.ModifiedAt = DateTimeOffset.UtcNow;

        var configObj = await GetOrCreateConfigObjectAsync(ct);
        var policies = GetPoliciesFromObject(configObj);
        policies.Add(policy);
        await SavePoliciesAsync(configObj, policies, ct);

        _logger.LogInformation("Conditional access policy created: {Name} (ID={Id})", policy.Name, policy.Id);
        return policy;
    }

    public async Task<ConditionalAccessPolicy> UpdatePolicy(ConditionalAccessPolicy policy, CancellationToken ct = default)
    {
        var configObj = await GetOrCreateConfigObjectAsync(ct);
        var policies = GetPoliciesFromObject(configObj);
        var idx = policies.FindIndex(p => p.Id == policy.Id);
        if (idx < 0)
            throw new InvalidOperationException($"Policy not found: {policy.Id}");

        policy.ModifiedAt = DateTimeOffset.UtcNow;
        policies[idx] = policy;
        await SavePoliciesAsync(configObj, policies, ct);

        _logger.LogInformation("Conditional access policy updated: {Name} (ID={Id})", policy.Name, policy.Id);
        return policy;
    }

    public async Task DeletePolicy(string policyId, CancellationToken ct = default)
    {
        var configObj = await GetOrCreateConfigObjectAsync(ct);
        var policies = GetPoliciesFromObject(configObj);
        var removed = policies.RemoveAll(p => p.Id == policyId);
        if (removed == 0)
            throw new InvalidOperationException($"Policy not found: {policyId}");

        await SavePoliciesAsync(configObj, policies, ct);
        _logger.LogInformation("Conditional access policy deleted: {Id}", policyId);
    }

    // --- Sign-in log ---

    public async Task<List<SignInLogEntry>> GetSignInLog(int count = 100, CancellationToken ct = default)
    {
        var configObj = await GetOrCreateConfigObjectAsync(ct);
        return GetSignInLogFromObject(configObj).Take(count).ToList();
    }

    private async Task LogSignInEvaluationAsync(
        AccessEvaluationRequest request, AccessEvaluationResult result, CancellationToken ct)
    {
        try
        {
            var configObj = await GetOrCreateConfigObjectAsync(ct);
            var log = GetSignInLogFromObject(configObj);

            log.Insert(0, new SignInLogEntry
            {
                UserDn = request.UserDn,
                ClientIp = request.ClientIp,
                ApplicationId = request.ApplicationId,
                DevicePlatform = request.Device?.Platform,
                Result = result,
                Timestamp = DateTimeOffset.UtcNow,
            });

            // Keep only recent entries
            if (log.Count > MaxSignInLogEntries)
                log.RemoveRange(MaxSignInLogEntries, log.Count - MaxSignInLogEntries);

            var json = JsonSerializer.Serialize(log);
            configObj.Attributes[SignInLogAttributeName] = new DirectoryAttribute
            {
                Name = SignInLogAttributeName,
                Values = [json],
            };
            configObj.WhenChanged = DateTimeOffset.UtcNow;
            await _store.UpdateAsync(configObj, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to log sign-in evaluation");
        }
    }

    // --- Storage helpers ---

    private async Task<DirectoryObject> GetOrCreateConfigObjectAsync(CancellationToken ct)
    {
        var domainDn = _ncService.GetDomainNc().Dn;
        var configDn = $"{ConfigObjectCn},CN=System,{domainDn}";

        var obj = await _store.GetByDnAsync("default", configDn, ct);
        if (obj is not null) return obj;

        obj = new DirectoryObject
        {
            TenantId = "default",
            DistinguishedName = configDn,
            Cn = "Conditional Access Config",
            ObjectClass = ["top", "container"],
            WhenCreated = DateTimeOffset.UtcNow,
            WhenChanged = DateTimeOffset.UtcNow,
        };

        try
        {
            await _store.CreateAsync(obj, ct);
        }
        catch
        {
            // Race condition — another instance created it first
            obj = await _store.GetByDnAsync("default", configDn, ct);
            if (obj is null) throw;
        }

        return obj;
    }

    private static List<ConditionalAccessPolicy> GetPoliciesFromObject(DirectoryObject obj)
    {
        if (!obj.Attributes.TryGetValue(PolicyAttributeName, out var attr))
            return new List<ConditionalAccessPolicy>();

        var json = attr.Values.FirstOrDefault()?.ToString();
        if (string.IsNullOrEmpty(json)) return new List<ConditionalAccessPolicy>();

        try
        {
            return JsonSerializer.Deserialize<List<ConditionalAccessPolicy>>(json) ?? new List<ConditionalAccessPolicy>();
        }
        catch
        {
            return new List<ConditionalAccessPolicy>();
        }
    }

    private async Task SavePoliciesAsync(DirectoryObject obj, List<ConditionalAccessPolicy> policies, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(policies);
        obj.Attributes[PolicyAttributeName] = new DirectoryAttribute
        {
            Name = PolicyAttributeName,
            Values = [json],
        };
        obj.WhenChanged = DateTimeOffset.UtcNow;
        await _store.UpdateAsync(obj, ct);
    }

    private static List<SignInLogEntry> GetSignInLogFromObject(DirectoryObject obj)
    {
        if (!obj.Attributes.TryGetValue(SignInLogAttributeName, out var attr))
            return new List<SignInLogEntry>();

        var json = attr.Values.FirstOrDefault()?.ToString();
        if (string.IsNullOrEmpty(json)) return new List<SignInLogEntry>();

        try
        {
            return JsonSerializer.Deserialize<List<SignInLogEntry>>(json) ?? new List<SignInLogEntry>();
        }
        catch
        {
            return new List<SignInLogEntry>();
        }
    }
}
