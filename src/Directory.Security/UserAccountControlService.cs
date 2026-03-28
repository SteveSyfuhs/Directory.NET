using Directory.Core.Interfaces;
using Directory.Core.Models;
using Microsoft.Extensions.Logging;

namespace Directory.Security;

/// <summary>
/// User Account Control flag values as defined in MS-ADTS 2.2.16.
/// These flags control authentication behaviour, account type, and security policy for directory objects.
/// </summary>
[Flags]
public enum UserAccountControlFlags
{
    /// <summary>The logon script is executed.</summary>
    SCRIPT = 0x0001,

    /// <summary>The user account is disabled.</summary>
    ACCOUNTDISABLE = 0x0002,

    /// <summary>The home directory is required.</summary>
    HOMEDIR_REQUIRED = 0x0008,

    /// <summary>The account is currently locked out.</summary>
    LOCKOUT = 0x0010,

    /// <summary>No password is required.</summary>
    PASSWD_NOTREQD = 0x0020,

    /// <summary>The user cannot change the password.</summary>
    PASSWD_CANT_CHANGE = 0x0040,

    /// <summary>The user can send an encrypted password.</summary>
    ENCRYPTED_TEXT_PWD_ALLOWED = 0x0080,

    /// <summary>Default account type for most users.</summary>
    NORMAL_ACCOUNT = 0x0200,

    /// <summary>Trust account for a domain that trusts other domains.</summary>
    INTERDOMAIN_TRUST_ACCOUNT = 0x0800,

    /// <summary>Computer account for a workstation or member server joined to the domain.</summary>
    WORKSTATION_TRUST_ACCOUNT = 0x1000,

    /// <summary>Computer account for a domain controller.</summary>
    SERVER_TRUST_ACCOUNT = 0x2000,

    /// <summary>The password for this account will never expire.</summary>
    DONT_EXPIRE_PASSWORD = 0x10000,

    /// <summary>MNS logon account.</summary>
    MNS_LOGON_ACCOUNT = 0x20000,

    /// <summary>Forces the user to log on using a smart card.</summary>
    SMARTCARD_REQUIRED = 0x40000,

    /// <summary>The service account is trusted for Kerberos delegation.</summary>
    TRUSTED_FOR_DELEGATION = 0x80000,

    /// <summary>The security context of the user will not be delegated.</summary>
    NOT_DELEGATED = 0x100000,

    /// <summary>Restrict this principal to use only DES encryption types for keys.</summary>
    USE_DES_KEY_ONLY = 0x200000,

    /// <summary>This account does not require Kerberos pre-authentication.</summary>
    DONT_REQ_PREAUTH = 0x400000,

    /// <summary>The user password has expired.</summary>
    PASSWORD_EXPIRED = 0x800000,

    /// <summary>The account is trusted to authenticate for delegation (S4U2Self/S4U2Proxy).</summary>
    TRUSTED_TO_AUTH_FOR_DELEGATION = 0x1000000,

    /// <summary>The account is a read-only domain controller (RODC) partial secrets account.</summary>
    PARTIAL_SECRETS_ACCOUNT = 0x4000000,
}

/// <summary>
/// Implements UAC flag evaluation and enforcement for directory objects.
/// Handles pre-authentication checks, default flag assignment, and domain join flag configuration.
/// </summary>
public class UserAccountControlService : IUserAccountControlService
{
    // Default maximum password age: 42 days (Windows default)
    private static readonly long DefaultMaxPwdAgeTicks = TimeSpan.FromDays(42).Ticks;

    // Default lockout duration: 30 minutes
    private static readonly long DefaultLockoutDurationTicks = TimeSpan.FromMinutes(30).Ticks;

    private readonly ILogger<UserAccountControlService> _logger;

    public UserAccountControlService(ILogger<UserAccountControlService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsAccountDisabled(DirectoryObject obj)
    {
        return HasFlag(obj, UserAccountControlFlags.ACCOUNTDISABLE);
    }

    /// <inheritdoc />
    public bool IsAccountLockedOut(DirectoryObject obj)
    {
        if (!HasFlag(obj, UserAccountControlFlags.LOCKOUT))
            return false;

        // Check if the lockout has expired based on lockoutTime and lockoutDuration.
        // In AD, lockoutTime is stored as a Windows FILETIME (100-ns intervals since 1601-01-01).
        var lockoutTimeAttr = obj.GetAttribute("lockoutTime");
        if (lockoutTimeAttr is null)
            return true; // Flag is set but no timestamp — treat as locked

        if (!long.TryParse(lockoutTimeAttr.GetFirstString(), out var lockoutTime) || lockoutTime == 0)
            return false; // lockoutTime of 0 means not locked

        // Get lockout duration from domain policy (stored in attributes or use default).
        // A lockoutDuration of 0 means "locked until admin unlocks".
        var lockoutDuration = GetLockoutDuration(obj);

        if (lockoutDuration == 0)
            return true; // Permanent lockout until admin unlocks

        var lockoutExpiry = lockoutTime + lockoutDuration;
        var now = DateTimeOffset.UtcNow.ToFileTime();

        if (now >= lockoutExpiry)
        {
            _logger.LogDebug("Lockout expired for {DN} (expired at {Expiry})",
                obj.DistinguishedName, DateTimeOffset.FromFileTime(lockoutExpiry));
            return false;
        }

        return true;
    }

    /// <inheritdoc />
    public bool IsPasswordExpired(DirectoryObject obj)
    {
        // DONT_EXPIRE_PASSWORD overrides expiration
        if (HasFlag(obj, UserAccountControlFlags.DONT_EXPIRE_PASSWORD))
            return false;

        // Explicit PASSWORD_EXPIRED flag
        if (HasFlag(obj, UserAccountControlFlags.PASSWORD_EXPIRED))
            return true;

        // pwdLastSet of 0 means "must change password at next logon"
        if (obj.PwdLastSet == 0)
            return true;

        // Check against maxPwdAge policy
        var maxPwdAge = GetMaxPasswordAge(obj);
        if (maxPwdAge <= 0)
            return false; // No max age policy — passwords never expire by policy

        var passwordExpiry = obj.PwdLastSet + maxPwdAge;
        var now = DateTimeOffset.UtcNow.ToFileTime();

        return now >= passwordExpiry;
    }

    /// <inheritdoc />
    public string ValidateLogon(DirectoryObject obj)
    {
        // 1. Account disabled
        if (IsAccountDisabled(obj))
        {
            _logger.LogDebug("Logon validation failed for {DN}: account disabled", obj.DistinguishedName);
            return "Account is disabled";
        }

        // 2. Account locked out
        if (IsAccountLockedOut(obj))
        {
            _logger.LogDebug("Logon validation failed for {DN}: account locked out", obj.DistinguishedName);
            return "Account is locked out";
        }

        // 3. Password expired
        if (IsPasswordExpired(obj))
        {
            _logger.LogDebug("Logon validation failed for {DN}: password expired", obj.DistinguishedName);
            return "Password has expired";
        }

        // 4. Validate account type — must be a normal logon-capable account type
        var uac = (UserAccountControlFlags)obj.UserAccountControl;
        var hasValidType =
            uac.HasFlag(UserAccountControlFlags.NORMAL_ACCOUNT) ||
            uac.HasFlag(UserAccountControlFlags.WORKSTATION_TRUST_ACCOUNT) ||
            uac.HasFlag(UserAccountControlFlags.SERVER_TRUST_ACCOUNT) ||
            uac.HasFlag(UserAccountControlFlags.INTERDOMAIN_TRUST_ACCOUNT);

        if (!hasValidType)
        {
            _logger.LogDebug("Logon validation failed for {DN}: no valid account type flag set (UAC=0x{UAC:X})",
                obj.DistinguishedName, obj.UserAccountControl);
            return "Invalid account type";
        }

        // 5. Account expiration
        if (obj.AccountExpires != 0 && obj.AccountExpires != long.MaxValue)
        {
            var now = DateTimeOffset.UtcNow.ToFileTime();
            if (now >= obj.AccountExpires)
            {
                _logger.LogDebug("Logon validation failed for {DN}: account expired", obj.DistinguishedName);
                return "Account has expired";
            }
        }

        return null; // All checks passed
    }

    /// <inheritdoc />
    public int GetDefaultUac(string objectClass)
    {
        return objectClass.ToLowerInvariant() switch
        {
            "user" or "person" => (int)UserAccountControlFlags.NORMAL_ACCOUNT,                                                     // 0x0200
            "computer"        => (int)(UserAccountControlFlags.WORKSTATION_TRUST_ACCOUNT | UserAccountControlFlags.NORMAL_ACCOUNT), // 0x1000 | 0x0200
            "domaincontroller" or "dc" => (int)(UserAccountControlFlags.SERVER_TRUST_ACCOUNT | UserAccountControlFlags.NORMAL_ACCOUNT), // 0x2000 | 0x0200
            _ => (int)UserAccountControlFlags.NORMAL_ACCOUNT, // Default to normal account
        };
    }

    /// <inheritdoc />
    public void SetComputerAccountFlags(DirectoryObject obj)
    {
        // For domain join, a computer account needs WORKSTATION_TRUST_ACCOUNT.
        // Also keep NORMAL_ACCOUNT. Remove ACCOUNTDISABLE if previously set (pre-created accounts
        // are often created disabled and enabled on join).
        var flags = (UserAccountControlFlags)obj.UserAccountControl;

        // Set required flags
        flags |= UserAccountControlFlags.WORKSTATION_TRUST_ACCOUNT;
        flags |= UserAccountControlFlags.NORMAL_ACCOUNT;

        // Clear disable flag — the machine is actively joining
        flags &= ~UserAccountControlFlags.ACCOUNTDISABLE;

        // Clear lockout if present
        flags &= ~UserAccountControlFlags.LOCKOUT;

        obj.UserAccountControl = (int)flags;

        _logger.LogInformation(
            "Set computer account flags for {DN}: UAC=0x{UAC:X}",
            obj.DistinguishedName, obj.UserAccountControl);
    }

    #region Private Helpers

    private static bool HasFlag(DirectoryObject obj, UserAccountControlFlags flag)
    {
        return ((UserAccountControlFlags)obj.UserAccountControl).HasFlag(flag);
    }

    /// <summary>
    /// Gets the maximum password age in FILETIME ticks from domain policy.
    /// Falls back to the Windows default of 42 days.
    /// </summary>
    private static long GetMaxPasswordAge(DirectoryObject obj)
    {
        var attr = obj.GetAttribute("maxPwdAge");
        if (attr is not null && long.TryParse(attr.GetFirstString(), out var maxAge) && maxAge != 0)
        {
            // In AD, maxPwdAge is stored as a negative FILETIME interval
            return Math.Abs(maxAge);
        }

        return DefaultMaxPwdAgeTicks;
    }

    /// <summary>
    /// Gets the lockout duration in FILETIME ticks.
    /// Returns 0 if lockout is permanent (admin must unlock).
    /// </summary>
    private static long GetLockoutDuration(DirectoryObject obj)
    {
        var attr = obj.GetAttribute("lockoutDuration");
        if (attr is not null && long.TryParse(attr.GetFirstString(), out var duration))
        {
            // Stored as a negative FILETIME interval; 0 means permanent lockout
            return Math.Abs(duration);
        }

        return DefaultLockoutDurationTicks;
    }

    #endregion
}
