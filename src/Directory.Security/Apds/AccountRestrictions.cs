using Directory.Core.Models;
using Microsoft.Extensions.Logging;

namespace Directory.Security.Apds;

/// <summary>
/// Performs account restriction checks as specified in MS-APDS section 3.1.5.
/// Validates account status, logon hours, workstation restrictions, and manages
/// bad password tracking / account lockout.
/// </summary>
public class AccountRestrictions
{
    private readonly ILogger<AccountRestrictions> _logger;

    // UAC flag constants (MS-ADTS 2.2.16)
    private const int UacAccountDisable = 0x00000002;
    private const int UacLockout = 0x00000010;
    private const int UacPasswordNotRequired = 0x00000020;
    private const int UacPasswordCantChange = 0x00000040;
    private const int UacNormalAccount = 0x00000200;
    private const int UacWorkstationTrustAccount = 0x00001000;
    private const int UacServerTrustAccount = 0x00002000;
    private const int UacDontExpirePassword = 0x00010000;
    private const int UacSmartcardRequired = 0x00040000;
    private const int UacPasswordExpired = 0x00800000;

    /// <summary>
    /// Default lockout threshold. In production this would come from domain policy.
    /// </summary>
    private const int DefaultLockoutThreshold = 5;

    /// <summary>
    /// Default lockout observation window in 100-nanosecond intervals (30 minutes).
    /// </summary>
    private static readonly long DefaultLockoutObservationWindow = TimeSpan.FromMinutes(30).Ticks;

    /// <summary>
    /// Default lockout duration in 100-nanosecond intervals (30 minutes).
    /// </summary>
    private static readonly long DefaultLockoutDuration = TimeSpan.FromMinutes(30).Ticks;

    /// <summary>
    /// Default maximum password age in 100-nanosecond intervals (42 days).
    /// </summary>
    private static readonly long DefaultMaxPasswordAge = TimeSpan.FromDays(42).Ticks;

    /// <summary>
    /// Never-expires filetime sentinel value.
    /// </summary>
    private const long NeverExpires = 0x7FFFFFFFFFFFFFFF;

    /// <summary>
    /// Zero filetime means "not set" for accountExpires.
    /// </summary>
    private const long NotSet = 0;

    public AccountRestrictions(ILogger<AccountRestrictions> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Performs all account restriction checks as specified in MS-APDS 3.1.5.
    /// Returns <see cref="NtStatus.StatusSuccess"/> if the account passes all checks,
    /// or the appropriate failure status code.
    /// </summary>
    /// <param name="user">The directory object representing the user account.</param>
    /// <param name="logonType">The type of logon being attempted.</param>
    /// <returns>An NtStatus indicating the result of restriction checks.</returns>
    public NtStatus CheckAccountRestrictions(DirectoryObject user, LogonType logonType)
    {
        // Step 1: Check if account exists (caller should handle this, but guard)
        if (user is null)
            return NtStatus.StatusNoSuchUser;

        // Step 2: Check if account is disabled
        if (IsAccountDisabled(user))
        {
            _logger.LogDebug("Account {Sam} is disabled", user.SAMAccountName);
            return NtStatus.StatusAccountDisabled;
        }

        // Step 3: Check if account is locked out
        if (IsAccountLockedOut(user))
        {
            _logger.LogDebug("Account {Sam} is locked out", user.SAMAccountName);
            return NtStatus.StatusAccountLockedOut;
        }

        // Step 4: Check if account has expired
        if (IsAccountExpired(user))
        {
            _logger.LogDebug("Account {Sam} has expired", user.SAMAccountName);
            return NtStatus.StatusAccountExpired;
        }

        // Step 5: Check logon hours restriction
        if (!CheckLogonHours(user))
        {
            _logger.LogDebug("Account {Sam} logon hours restriction", user.SAMAccountName);
            return NtStatus.StatusInvalidLogonHours;
        }

        // Step 6: Check workstation restrictions (for interactive and remote interactive logons)
        if (logonType is LogonType.Interactive or LogonType.RemoteInteractive)
        {
            if (!CheckWorkstationRestrictions(user, workstationName: null))
            {
                _logger.LogDebug("Account {Sam} workstation restriction", user.SAMAccountName);
                return NtStatus.StatusInvalidWorkstation;
            }
        }

        // Step 7: Check if password must change (pwdLastSet == 0 means must change)
        if (IsPasswordMustChange(user))
        {
            _logger.LogDebug("Account {Sam} must change password", user.SAMAccountName);
            return NtStatus.StatusPasswordMustChange;
        }

        // Step 8: Check if password has expired
        if (IsPasswordExpired(user))
        {
            _logger.LogDebug("Account {Sam} password has expired", user.SAMAccountName);
            return NtStatus.StatusPasswordExpired;
        }

        return NtStatus.StatusSuccess;
    }

    /// <summary>
    /// Returns true if the ACCOUNTDISABLE flag (0x0002) is set in userAccountControl.
    /// MS-ADTS 2.2.16.
    /// </summary>
    public bool IsAccountDisabled(DirectoryObject user)
    {
        return (user.UserAccountControl & UacAccountDisable) != 0;
    }

    /// <summary>
    /// Returns true if the account has expired based on the accountExpires attribute.
    /// MS-APDS 3.1.5: If accountExpires is non-zero and not 0x7FFFFFFFFFFFFFFF
    /// and the current time exceeds it, the account is expired.
    /// </summary>
    public bool IsAccountExpired(DirectoryObject user)
    {
        long accountExpires = user.AccountExpires;

        // 0 or max-value means never expires
        if (accountExpires == NotSet || accountExpires == NeverExpires)
            return false;

        // accountExpires is stored as a Windows FILETIME (100-nanosecond intervals since 1601-01-01)
        try
        {
            var expirationTime = DateTimeOffset.FromFileTime(accountExpires);
            return DateTimeOffset.UtcNow > expirationTime;
        }
        catch (ArgumentOutOfRangeException)
        {
            // Invalid filetime value; treat as not expired
            return false;
        }
    }

    /// <summary>
    /// Returns true if the account is locked out.
    /// Checks both the UAC LOCKOUT flag and the lockoutTime attribute
    /// against the lockout duration policy.
    /// </summary>
    public bool IsAccountLockedOut(DirectoryObject user)
    {
        // Check UAC lockout flag
        if ((user.UserAccountControl & UacLockout) != 0)
            return true;

        // Check lockoutTime attribute
        var lockoutTimeAttr = user.GetAttribute("lockoutTime");
        if (lockoutTimeAttr?.GetFirstString() is { } lockoutTimeStr
            && long.TryParse(lockoutTimeStr, out long lockoutTime)
            && lockoutTime > 0)
        {
            // If lockout duration is 0, account stays locked until admin unlocks
            // Otherwise, check if lockout duration has elapsed
            try
            {
                var lockedAt = DateTimeOffset.FromFileTime(lockoutTime);
                var lockoutExpiry = lockedAt.AddTicks(DefaultLockoutDuration);

                if (DateTimeOffset.UtcNow < lockoutExpiry)
                    return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                // Invalid value; treat as not locked
            }
        }

        return false;
    }

    /// <summary>
    /// Checks the logonHours attribute to determine if the user is permitted to log on
    /// at the current time. The logonHours attribute is a 21-byte bitmap
    /// (168 bits = 7 days x 24 hours), one bit per hour starting from Sunday 00:00 UTC.
    /// </summary>
    public bool CheckLogonHours(DirectoryObject user)
    {
        var logonHoursAttr = user.GetAttribute("logonHours");
        if (logonHoursAttr?.GetFirstBytes() is not { } logonHours)
            return true; // No restriction set

        if (logonHours.Length < 21)
            return true; // Malformed; allow

        // Calculate which bit to check: day-of-week * 24 + hour
        var now = DateTime.UtcNow;
        int dayOfWeek = (int)now.DayOfWeek; // Sunday = 0
        int hour = now.Hour;
        int bitIndex = (dayOfWeek * 24) + hour;
        int byteIndex = bitIndex / 8;
        int bitOffset = bitIndex % 8;

        return (logonHours[byteIndex] & (1 << bitOffset)) != 0;
    }

    /// <summary>
    /// Checks the userWorkstations attribute to determine if the user is allowed
    /// to log on from the specified workstation. The attribute contains a comma-separated
    /// list of allowed NetBIOS workstation names.
    /// </summary>
    public bool CheckWorkstationRestrictions(DirectoryObject user, string workstationName)
    {
        var workstationsAttr = user.GetAttribute("userWorkstations");
        if (workstationsAttr?.GetFirstString() is not { } allowedWorkstations)
            return true; // No restriction set

        if (string.IsNullOrWhiteSpace(allowedWorkstations))
            return true;

        if (string.IsNullOrEmpty(workstationName))
            return true; // Can't validate without workstation name; allow

        var allowed = allowedWorkstations.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return allowed.Any(w => string.Equals(w, workstationName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns true if the user must change their password before first logon.
    /// This is indicated by pwdLastSet == 0 and the password-expired UAC flag,
    /// unless the account has DONT_EXPIRE_PASSWORD or PASSWORD_NOT_REQUIRED set.
    /// </summary>
    public bool IsPasswordMustChange(DirectoryObject user)
    {
        if ((user.UserAccountControl & UacDontExpirePassword) != 0)
            return false;

        if ((user.UserAccountControl & UacPasswordNotRequired) != 0)
            return false;

        // pwdLastSet == 0 means the admin requires a password change at next logon
        return user.PwdLastSet == 0;
    }

    /// <summary>
    /// Returns true if the user's password has exceeded the maximum password age.
    /// Respects the DONT_EXPIRE_PASSWORD and PASSWORD_NOT_REQUIRED UAC flags.
    /// </summary>
    public bool IsPasswordExpired(DirectoryObject user)
    {
        if ((user.UserAccountControl & UacDontExpirePassword) != 0)
            return false;

        if ((user.UserAccountControl & UacPasswordNotRequired) != 0)
            return false;

        // Machine accounts (workstation/server trust) don't have password expiry
        if ((user.UserAccountControl & (UacWorkstationTrustAccount | UacServerTrustAccount)) != 0)
            return false;

        long pwdLastSet = user.PwdLastSet;
        if (pwdLastSet <= 0)
            return false; // Handled by IsPasswordMustChange

        try
        {
            var passwordSetTime = DateTimeOffset.FromFileTime(pwdLastSet);
            var expiryTime = passwordSetTime.AddTicks(DefaultMaxPasswordAge);
            return DateTimeOffset.UtcNow > expiryTime;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    /// <summary>
    /// Increments the bad password count for the user account and checks whether
    /// the account should be locked out per MS-APDS 3.1.5.
    /// </summary>
    /// <param name="user">The user's directory object (will be modified in place).</param>
    /// <returns>True if the account is now locked out as a result of this bad password.</returns>
    public bool UpdateBadPasswordCount(DirectoryObject user)
    {
        var now = DateTimeOffset.UtcNow;
        long nowFileTime = now.ToFileTime();

        // Check if the observation window has reset
        if (user.BadPasswordTime > 0)
        {
            try
            {
                var lastBadTime = DateTimeOffset.FromFileTime(user.BadPasswordTime);
                if ((now - lastBadTime).Ticks > DefaultLockoutObservationWindow)
                {
                    // Observation window elapsed; reset count
                    user.BadPwdCount = 0;
                }
            }
            catch (ArgumentOutOfRangeException)
            {
                user.BadPwdCount = 0;
            }
        }

        user.BadPwdCount++;
        user.BadPasswordTime = nowFileTime;

        // Check lockout threshold
        if (DefaultLockoutThreshold > 0 && user.BadPwdCount >= DefaultLockoutThreshold)
        {
            // Lock the account
            user.SetAttribute("lockoutTime", new DirectoryAttribute("lockoutTime", nowFileTime.ToString()));
            user.UserAccountControl |= UacLockout;

            _logger.LogWarning("Account {Sam} locked out after {Count} bad password attempts",
                user.SAMAccountName, user.BadPwdCount);

            return true;
        }

        return false;
    }

    /// <summary>
    /// Resets the bad password count and lockout state after a successful authentication.
    /// </summary>
    public void ResetBadPasswordCount(DirectoryObject user)
    {
        user.BadPwdCount = 0;
        user.BadPasswordTime = 0;

        // Clear lockout if set
        if ((user.UserAccountControl & UacLockout) != 0)
        {
            user.UserAccountControl &= ~UacLockout;
            user.SetAttribute("lockoutTime", new DirectoryAttribute("lockoutTime", "0"));
        }
    }
}
