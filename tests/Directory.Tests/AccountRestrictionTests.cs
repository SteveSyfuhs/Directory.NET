using Directory.Core.Models;
using Directory.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Directory.Tests;

public class AccountRestrictionTests
{
    private readonly UserAccountControlService _uacService = new(NullLogger<UserAccountControlService>.Instance);

    /// <summary>
    /// Creates a standard enabled user with NORMAL_ACCOUNT flag and a recent password.
    /// </summary>
    private static DirectoryObject CreateValidUser()
    {
        return new DirectoryObject
        {
            DistinguishedName = "CN=TestUser,OU=Users,DC=corp,DC=com",
            SAMAccountName = "testuser",
            ObjectClass = ["top", "person", "organizationalPerson", "user"],
            ObjectCategory = "person",
            Cn = "TestUser",
            UserAccountControl = (int)UserAccountControlFlags.NORMAL_ACCOUNT,
            PwdLastSet = DateTimeOffset.UtcNow.ToFileTime(),
            AccountExpires = 0, // never expires
        };
    }

    // ── Normal valid account passes all checks ─────────────────────────

    [Fact]
    public void ValidateLogon_ValidAccount_ReturnsNull()
    {
        var user = CreateValidUser();

        var result = _uacService.ValidateLogon(user);

        Assert.Null(result); // null means all checks passed
    }

    // ── Disabled account ───────────────────────────────────────────────

    [Fact]
    public void ValidateLogon_DisabledAccount_ReturnsError()
    {
        var user = CreateValidUser();
        user.UserAccountControl |= (int)UserAccountControlFlags.ACCOUNTDISABLE; // 0x0002

        var result = _uacService.ValidateLogon(user);

        Assert.NotNull(result);
        Assert.Contains("disabled", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IsAccountDisabled_DisabledFlag_ReturnsTrue()
    {
        var user = CreateValidUser();
        user.UserAccountControl |= 0x0002; // ACCOUNTDISABLE

        Assert.True(_uacService.IsAccountDisabled(user));
    }

    [Fact]
    public void IsAccountDisabled_NormalAccount_ReturnsFalse()
    {
        var user = CreateValidUser();

        Assert.False(_uacService.IsAccountDisabled(user));
    }

    // ── Expired account ────────────────────────────────────────────────

    [Fact]
    public void ValidateLogon_ExpiredAccount_ReturnsError()
    {
        var user = CreateValidUser();
        // Set accountExpires to a date in the past
        user.AccountExpires = DateTimeOffset.UtcNow.AddDays(-1).ToFileTime();

        var result = _uacService.ValidateLogon(user);

        Assert.NotNull(result);
        Assert.Contains("expired", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateLogon_AccountExpiresZero_Passes()
    {
        // accountExpires = 0 means "never expires"
        var user = CreateValidUser();
        user.AccountExpires = 0;

        var result = _uacService.ValidateLogon(user);

        Assert.Null(result);
    }

    [Fact]
    public void ValidateLogon_AccountExpiresMaxValue_Passes()
    {
        // accountExpires = long.MaxValue also means "never expires"
        var user = CreateValidUser();
        user.AccountExpires = long.MaxValue;

        var result = _uacService.ValidateLogon(user);

        Assert.Null(result);
    }

    [Fact]
    public void ValidateLogon_AccountExpiresFuture_Passes()
    {
        var user = CreateValidUser();
        user.AccountExpires = DateTimeOffset.UtcNow.AddDays(30).ToFileTime();

        var result = _uacService.ValidateLogon(user);

        Assert.Null(result);
    }

    // ── Password must change (pwdLastSet = 0) ──────────────────────────

    [Fact]
    public void ValidateLogon_PwdLastSetZero_ReturnsExpiredError()
    {
        var user = CreateValidUser();
        user.PwdLastSet = 0; // Must change password at next logon

        var result = _uacService.ValidateLogon(user);

        Assert.NotNull(result);
        Assert.Contains("expired", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IsPasswordExpired_PwdLastSetZero_ReturnsTrue()
    {
        var user = CreateValidUser();
        user.PwdLastSet = 0;

        Assert.True(_uacService.IsPasswordExpired(user));
    }

    [Fact]
    public void IsPasswordExpired_DontExpirePasswordFlag_ReturnsFalse()
    {
        var user = CreateValidUser();
        user.PwdLastSet = 0;
        user.UserAccountControl |= (int)UserAccountControlFlags.DONT_EXPIRE_PASSWORD;

        // DONT_EXPIRE_PASSWORD overrides everything
        Assert.False(_uacService.IsPasswordExpired(user));
    }

    // ── Locked out account ─────────────────────────────────────────────

    [Fact]
    public void ValidateLogon_LockedOutAccount_ReturnsError()
    {
        var user = CreateValidUser();
        user.UserAccountControl |= (int)UserAccountControlFlags.LOCKOUT;
        // Set lockoutTime to a recent time (within default 30-min lockout duration)
        user.Attributes["lockoutTime"] = new DirectoryAttribute("lockoutTime", DateTimeOffset.UtcNow.ToFileTime().ToString());

        var result = _uacService.ValidateLogon(user);

        Assert.NotNull(result);
        Assert.Contains("locked", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IsAccountLockedOut_LockoutFlagSetWithRecentLockoutTime_ReturnsTrue()
    {
        var user = CreateValidUser();
        user.UserAccountControl |= (int)UserAccountControlFlags.LOCKOUT;
        user.Attributes["lockoutTime"] = new DirectoryAttribute("lockoutTime",
            DateTimeOffset.UtcNow.ToFileTime().ToString());

        Assert.True(_uacService.IsAccountLockedOut(user));
    }

    [Fact]
    public void IsAccountLockedOut_LockoutExpired_ReturnsFalse()
    {
        var user = CreateValidUser();
        user.UserAccountControl |= (int)UserAccountControlFlags.LOCKOUT;
        // Set lockoutTime to well in the past (beyond default 30-min lockout duration)
        user.Attributes["lockoutTime"] = new DirectoryAttribute("lockoutTime",
            DateTimeOffset.UtcNow.AddHours(-2).ToFileTime().ToString());

        Assert.False(_uacService.IsAccountLockedOut(user));
    }

    [Fact]
    public void IsAccountLockedOut_LockoutTimeZero_ReturnsFalse()
    {
        var user = CreateValidUser();
        user.UserAccountControl |= (int)UserAccountControlFlags.LOCKOUT;
        user.Attributes["lockoutTime"] = new DirectoryAttribute("lockoutTime", "0");

        // lockoutTime of 0 means not actually locked
        Assert.False(_uacService.IsAccountLockedOut(user));
    }

    [Fact]
    public void IsAccountLockedOut_PermanentLockout_ReturnsTrue()
    {
        var user = CreateValidUser();
        user.UserAccountControl |= (int)UserAccountControlFlags.LOCKOUT;
        user.Attributes["lockoutTime"] = new DirectoryAttribute("lockoutTime",
            DateTimeOffset.UtcNow.AddHours(-2).ToFileTime().ToString());
        // lockoutDuration of 0 = permanent (admin must unlock)
        user.Attributes["lockoutDuration"] = new DirectoryAttribute("lockoutDuration", "0");

        Assert.True(_uacService.IsAccountLockedOut(user));
    }

    [Fact]
    public void IsAccountLockedOut_NoLockoutFlag_ReturnsFalse()
    {
        var user = CreateValidUser();
        // No LOCKOUT flag set
        Assert.False(_uacService.IsAccountLockedOut(user));
    }

    // ── Invalid account type ───────────────────────────────────────────

    [Fact]
    public void ValidateLogon_NoAccountTypeFlag_ReturnsError()
    {
        var user = CreateValidUser();
        // Clear all account type flags, leaving nothing valid
        user.UserAccountControl = 0;

        var result = _uacService.ValidateLogon(user);

        Assert.NotNull(result);
        Assert.Contains("account type", result, StringComparison.OrdinalIgnoreCase);
    }

    // ── Explicit PASSWORD_EXPIRED flag ─────────────────────────────────

    [Fact]
    public void IsPasswordExpired_ExplicitFlag_ReturnsTrue()
    {
        var user = CreateValidUser();
        user.UserAccountControl |= (int)UserAccountControlFlags.PASSWORD_EXPIRED;

        Assert.True(_uacService.IsPasswordExpired(user));
    }
}
