namespace Directory.Security.PasswordFilters;

/// <summary>
/// Rejects passwords found in a list of the top 100 most common passwords.
/// Analogous to Azure AD Password Protection's banned password list.
/// </summary>
public class CommonPasswordFilter : IPasswordFilter
{
    public string Name => "Common Password Filter";
    public string Description => "Rejects passwords from a list of the top 100 most commonly used passwords.";
    public bool IsEnabled { get; set; } = true;
    public int Order => 10;

    private static readonly HashSet<string> CommonPasswords = new(StringComparer.OrdinalIgnoreCase)
    {
        "123456", "password", "12345678", "qwerty", "123456789",
        "12345", "1234", "111111", "1234567", "dragon",
        "123123", "baseball", "abc123", "football", "monkey",
        "letmein", "shadow", "master", "666666", "qwertyuiop",
        "123321", "mustang", "1234567890", "michael", "654321",
        "superman", "1qaz2wsx", "7777777", "fuckyou", "121212",
        "000000", "qazwsx", "123qwe", "killer", "trustno1",
        "jordan", "jennifer", "zxcvbnm", "asdfgh", "hunter",
        "buster", "soccer", "harley", "batman", "andrew",
        "tigger", "sunshine", "iloveyou", "2000", "charlie",
        "robert", "thomas", "hockey", "ranger", "daniel",
        "starwars", "klaster", "112233", "george", "computer",
        "michelle", "jessica", "pepper", "1111", "zxcvbn",
        "555555", "11111111", "131313", "freedom", "777777",
        "pass", "maggie", "159753", "aaaaaa", "ginger",
        "princess", "joshua", "cheese", "amanda", "summer",
        "love", "ashley", "nicole", "chelsea", "biteme",
        "matthew", "access", "yankees", "987654321", "dallas",
        "austin", "thunder", "taylor", "matrix", "minecraft",
        "william", "corvette", "hello", "martin", "heather",
        "secret", "merlin", "diamond", "1234qwer", "gfhjkm",
        "hammer", "silver", "222222", "88888888", "anthony",
    };

    public Task<PasswordFilterResult> ValidatePasswordAsync(string dn, string newPassword, string oldPassword)
    {
        if (CommonPasswords.Contains(newPassword))
        {
            return Task.FromResult(PasswordFilterResult.Invalid(
                "This password is too common. Please choose a more unique password."));
        }

        // Also check with common suffix/prefix patterns
        var lower = newPassword.ToLowerInvariant();
        foreach (var common in CommonPasswords)
        {
            if (lower == common + "1" || lower == common + "!" || lower == common + "123")
            {
                return Task.FromResult(PasswordFilterResult.Invalid(
                    "This password is a trivial variation of a common password. Please choose a more unique password."));
            }
        }

        return Task.FromResult(PasswordFilterResult.Valid());
    }
}
