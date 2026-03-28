namespace Directory.Security.Mfa;

public class ConditionalAccessPolicy
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsEnabled { get; set; } = true;
    public int Priority { get; set; } // Lower = higher priority

    // Conditions (all must match)
    public PolicyConditions Conditions { get; set; } = new();

    // Actions when conditions match
    public PolicyActions Actions { get; set; } = new();

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ModifiedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class PolicyConditions
{
    public List<string> IncludeUsers { get; set; } = new();    // DNs, "All"
    public List<string> ExcludeUsers { get; set; } = new();
    public List<string> IncludeGroups { get; set; } = new();
    public List<string> ExcludeGroups { get; set; } = new();
    public List<string> IncludeApplications { get; set; } = new(); // OAuth client IDs
    public List<string> IpRanges { get; set; } = new();        // Trusted IP ranges (CIDR)
    public List<string> Countries { get; set; } = new();        // Country codes
    public RiskLevel? MinRiskLevel { get; set; }
    public List<string> DevicePlatforms { get; set; } = new(); // Windows, macOS, Linux, iOS, Android
}

public enum RiskLevel { Low, Medium, High }

public class PolicyActions
{
    public bool RequireMfa { get; set; }
    public List<string> AllowedMfaMethods { get; set; } = new() { "totp", "fido2" };
    public bool BlockAccess { get; set; }
    public bool RequirePasswordChange { get; set; }
    public int? SessionLifetimeMinutes { get; set; }
}

/// <summary>
/// Input for evaluating conditional access policies against a sign-in attempt.
/// </summary>
public class AccessEvaluationRequest
{
    public string UserDn { get; set; } = "";
    public string ClientIp { get; set; }
    public string ApplicationId { get; set; }
    public DeviceInfo Device { get; set; }
    public RiskLevel? RiskLevel { get; set; }
}

public class DeviceInfo
{
    public string Platform { get; set; }     // Windows, macOS, Linux, iOS, Android
    public string UserAgent { get; set; }
    public bool? IsCompliant { get; set; }
}

/// <summary>
/// Result of evaluating all conditional access policies for a sign-in attempt.
/// </summary>
public class AccessEvaluationResult
{
    public bool AccessGranted { get; set; } = true;
    public bool MfaRequired { get; set; }
    public List<string> AllowedMfaMethods { get; set; } = new();
    public bool PasswordChangeRequired { get; set; }
    public int? SessionLifetimeMinutes { get; set; }
    public List<PolicyEvaluationEntry> EvaluatedPolicies { get; set; } = new();
    public string BlockReason { get; set; }
}

public class PolicyEvaluationEntry
{
    public string PolicyId { get; set; } = "";
    public string PolicyName { get; set; } = "";
    public bool Matched { get; set; }
    public string Reason { get; set; }
}

/// <summary>
/// A record of a sign-in evaluation for the audit log.
/// </summary>
public class SignInLogEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserDn { get; set; } = "";
    public string ClientIp { get; set; }
    public string ApplicationId { get; set; }
    public string DevicePlatform { get; set; }
    public AccessEvaluationResult Result { get; set; } = new();
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}
