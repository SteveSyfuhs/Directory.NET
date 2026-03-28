namespace Directory.Security.PasswordFilters;

/// <summary>
/// Rejects passwords that contain the username or parts of the distinguished name.
/// This is similar to Windows AD's password complexity requirement that checks
/// whether the password contains the user's account name or display name.
/// </summary>
public class UsernameFilter : IPasswordFilter
{
    public string Name => "Username Filter";
    public string Description => "Rejects passwords that contain the user's account name or parts of the distinguished name.";
    public bool IsEnabled { get; set; } = true;
    public int Order => 30;

    /// <summary>
    /// Minimum token length to check against the password.
    /// Short tokens (1-2 chars) produce too many false positives.
    /// </summary>
    private const int MinTokenLength = 3;

    public Task<PasswordFilterResult> ValidatePasswordAsync(string dn, string newPassword, string oldPassword)
    {
        if (string.IsNullOrEmpty(dn) || string.IsNullOrEmpty(newPassword))
            return Task.FromResult(PasswordFilterResult.Valid());

        var lowerPassword = newPassword.ToLowerInvariant();

        // Extract username tokens from DN
        var tokens = ExtractTokensFromDn(dn);

        foreach (var token in tokens)
        {
            if (token.Length < MinTokenLength) continue;

            if (lowerPassword.Contains(token.ToLowerInvariant()))
            {
                return Task.FromResult(PasswordFilterResult.Invalid(
                    $"Password must not contain your username or name (matched: '{token}')."));
            }

            // Also check reversed
            var reversed = new string(token.Reverse().ToArray());
            if (reversed.Length >= MinTokenLength && lowerPassword.Contains(reversed.ToLowerInvariant()))
            {
                return Task.FromResult(PasswordFilterResult.Invalid(
                    "Password must not contain your username or name in reverse."));
            }
        }

        return Task.FromResult(PasswordFilterResult.Valid());
    }

    /// <summary>
    /// Extract username-like tokens from a Distinguished Name.
    /// E.g., "CN=John Smith,OU=Users,DC=contoso,DC=com" yields ["John", "Smith", "John Smith"]
    /// </summary>
    private static List<string> ExtractTokensFromDn(string dn)
    {
        var tokens = new List<string>();

        // Extract CN value
        var parts = dn.Split(',');
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (trimmed.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
            {
                var cn = trimmed[3..].Trim();
                tokens.Add(cn);

                // Split CN into individual words
                foreach (var word in cn.Split(' ', '.', '-', '_'))
                {
                    if (!string.IsNullOrWhiteSpace(word))
                        tokens.Add(word);
                }
            }
            else if (trimmed.StartsWith("uid=", StringComparison.OrdinalIgnoreCase))
            {
                tokens.Add(trimmed[4..].Trim());
            }
        }

        return tokens;
    }
}
