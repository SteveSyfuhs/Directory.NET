namespace Directory.Security.PasswordFilters;

/// <summary>
/// Rejects passwords with excessive character repetition.
/// For example, "aaaaabbb" or "111111" would be rejected.
/// </summary>
public class RepetitionFilter : IPasswordFilter
{
    public string Name => "Repetition Filter";
    public string Description => "Rejects passwords with too many repeated or sequential characters.";
    public bool IsEnabled { get; set; } = true;
    public int Order => 20;

    /// <summary>
    /// Maximum consecutive identical characters allowed.
    /// </summary>
    private const int MaxConsecutiveRepeats = 3;

    /// <summary>
    /// Maximum percentage of the password that can be the same character.
    /// </summary>
    private const double MaxSameCharPercent = 0.5;

    public Task<PasswordFilterResult> ValidatePasswordAsync(string dn, string newPassword, string oldPassword)
    {
        if (string.IsNullOrEmpty(newPassword))
            return Task.FromResult(PasswordFilterResult.Invalid("Password cannot be empty."));

        // Check consecutive repetition
        var maxRun = 1;
        var currentRun = 1;
        for (var i = 1; i < newPassword.Length; i++)
        {
            if (char.ToLowerInvariant(newPassword[i]) == char.ToLowerInvariant(newPassword[i - 1]))
            {
                currentRun++;
                if (currentRun > maxRun) maxRun = currentRun;
            }
            else
            {
                currentRun = 1;
            }
        }

        if (maxRun > MaxConsecutiveRepeats)
        {
            return Task.FromResult(PasswordFilterResult.Invalid(
                $"Password contains {maxRun} consecutive repeated characters. Maximum allowed is {MaxConsecutiveRepeats}."));
        }

        // Check if too much of the password is the same character
        var charCounts = new Dictionary<char, int>();
        foreach (var c in newPassword.ToLowerInvariant())
        {
            charCounts.TryGetValue(c, out var count);
            charCounts[c] = count + 1;
        }

        var maxCount = charCounts.Values.Max();
        var ratio = (double)maxCount / newPassword.Length;
        if (ratio > MaxSameCharPercent)
        {
            return Task.FromResult(PasswordFilterResult.Invalid(
                "Password has too many instances of the same character. Please use a more varied password."));
        }

        // Check for keyboard sequences
        if (HasKeyboardSequence(newPassword))
        {
            return Task.FromResult(PasswordFilterResult.Invalid(
                "Password contains a keyboard sequence pattern. Please choose a less predictable password."));
        }

        return Task.FromResult(PasswordFilterResult.Valid());
    }

    private static bool HasKeyboardSequence(string password)
    {
        var sequences = new[]
        {
            "qwerty", "asdfgh", "zxcvbn", "qwertz", "123456", "abcdef",
            "ytrewq", "hgfdsa", "nbvcxz",
        };

        var lower = password.ToLowerInvariant();
        return sequences.Any(seq => lower.Contains(seq));
    }
}
