namespace Directory.Security.PasswordFilters;

/// <summary>
/// Rejects passwords that are dictionary words with simple substitutions.
/// Detects leet-speak transformations like "p@ssw0rd" and common patterns.
/// </summary>
public class DictionaryWordFilter : IPasswordFilter
{
    public string Name => "Dictionary Word Filter";
    public string Description => "Rejects passwords that are dictionary words with common character substitutions (leet-speak).";
    public bool IsEnabled { get; set; } = true;
    public int Order => 40;

    /// <summary>
    /// Common dictionary words that should not be used as passwords.
    /// </summary>
    private static readonly HashSet<string> DictionaryWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "welcome", "administrator", "admin", "login", "letmein",
        "master", "default", "access", "security", "server", "system",
        "domain", "network", "computer", "internet", "windows", "microsoft",
        "company", "business", "office", "desktop", "laptop", "monitor",
        "keyboard", "manager", "director", "employee", "customer", "support",
        "service", "database", "backup", "recovery", "install", "update",
        "change", "secret", "private", "public", "general", "control",
        "session", "connect", "account", "profile", "settings", "configure",
        "summer", "winter", "spring", "autumn", "monday", "tuesday",
        "wednesday", "thursday", "friday", "saturday", "sunday",
        "january", "february", "march", "april", "june", "july",
        "august", "september", "october", "november", "december",
        "baseball", "football", "soccer", "basketball", "hockey",
        "dragon", "shadow", "sunshine", "thunder", "diamond",
        "princess", "butterfly", "rainbow", "dolphin", "phoenix",
        "qwerty", "trustno", "iloveyou", "starwars", "pokemon",
    };

    /// <summary>
    /// Common leet-speak substitution mappings.
    /// </summary>
    private static readonly Dictionary<char, char[]> LeetSubstitutions = new()
    {
        ['@'] = ['a'],
        ['4'] = ['a'],
        ['8'] = ['b'],
        ['('] = ['c'],
        ['3'] = ['e'],
        ['6'] = ['g'],
        ['#'] = ['h'],
        ['!'] = ['i', 'l'],
        ['1'] = ['i', 'l'],
        ['|'] = ['i', 'l'],
        ['0'] = ['o'],
        ['$'] = ['s'],
        ['5'] = ['s'],
        ['+'] = ['t'],
        ['7'] = ['t'],
        ['%'] = ['x'],
        ['2'] = ['z'],
    };

    public Task<PasswordFilterResult> ValidatePasswordAsync(string dn, string newPassword, string oldPassword)
    {
        if (string.IsNullOrEmpty(newPassword))
            return Task.FromResult(PasswordFilterResult.Valid());

        // Strip trailing digits/symbols before checking
        var stripped = StripTrailingModifiers(newPassword);

        // Check direct match
        if (DictionaryWords.Contains(stripped))
        {
            return Task.FromResult(PasswordFilterResult.Invalid(
                "Password is a common dictionary word. Please choose a more complex password."));
        }

        // Normalize leet-speak and check again
        var normalized = NormalizeLeetSpeak(stripped);
        if (DictionaryWords.Contains(normalized))
        {
            return Task.FromResult(PasswordFilterResult.Invalid(
                "Password is a dictionary word with character substitutions. Please choose a more complex password."));
        }

        // Also try the full password normalized
        var fullNormalized = NormalizeLeetSpeak(newPassword);
        var fullStripped = StripTrailingModifiers(fullNormalized);
        if (DictionaryWords.Contains(fullStripped))
        {
            return Task.FromResult(PasswordFilterResult.Invalid(
                "Password is a dictionary word with character substitutions. Please choose a more complex password."));
        }

        return Task.FromResult(PasswordFilterResult.Valid());
    }

    /// <summary>
    /// Convert leet-speak characters back to their alphabetic equivalents.
    /// </summary>
    private static string NormalizeLeetSpeak(string input)
    {
        var chars = input.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (LeetSubstitutions.TryGetValue(chars[i], out var replacements))
            {
                chars[i] = replacements[0]; // Use first substitution
            }
        }
        return new string(chars).ToLowerInvariant();
    }

    /// <summary>
    /// Remove trailing digits and common suffix characters.
    /// For example, "password123!" becomes "password".
    /// </summary>
    private static string StripTrailingModifiers(string input)
    {
        var i = input.Length - 1;
        while (i >= 0 && (char.IsDigit(input[i]) || "!@#$%^&*".Contains(input[i])))
        {
            i--;
        }
        return i >= 0 ? input[..(i + 1)] : input;
    }
}
