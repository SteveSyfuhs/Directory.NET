namespace Directory.Security.PasswordFilters;

/// <summary>
/// Interface for password filter plugins.
/// In Windows AD, password filters are DLLs loaded by the LSA; here they are
/// managed service components that validate password changes.
/// </summary>
public interface IPasswordFilter
{
    /// <summary>Display name of this filter.</summary>
    string Name { get; }

    /// <summary>Human-readable description of what this filter checks.</summary>
    string Description { get; }

    /// <summary>Whether this filter is currently enabled.</summary>
    bool IsEnabled { get; set; }

    /// <summary>Execution order (lower runs first).</summary>
    int Order { get; }

    /// <summary>
    /// Validate a proposed password change.
    /// </summary>
    /// <param name="dn">Distinguished name of the user.</param>
    /// <param name="newPassword">The proposed new password.</param>
    /// <param name="oldPassword">The current password (may be null for admin resets).</param>
    /// <returns>Validation result indicating whether the password is acceptable.</returns>
    Task<PasswordFilterResult> ValidatePasswordAsync(string dn, string newPassword, string oldPassword);
}

/// <summary>
/// Result of a password filter validation.
/// </summary>
public class PasswordFilterResult
{
    public bool IsValid { get; set; }
    public string Message { get; set; } = string.Empty;

    public static PasswordFilterResult Valid() => new() { IsValid = true, Message = "OK" };
    public static PasswordFilterResult Invalid(string message) => new() { IsValid = false, Message = message };
}
