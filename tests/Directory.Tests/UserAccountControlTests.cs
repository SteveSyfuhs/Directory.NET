using Directory.Core.Models;
using Directory.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Directory.Tests;

public class UserAccountControlTests
{
    private readonly UserAccountControlService _uacService = new(NullLogger<UserAccountControlService>.Instance);

    // ── Flag enum value tests ──────────────────────────────────────────

    [Fact]
    public void UacFlags_ACCOUNTDISABLE_Is0x0002()
    {
        Assert.Equal(0x0002, (int)UserAccountControlFlags.ACCOUNTDISABLE);
    }

    [Fact]
    public void UacFlags_NORMAL_ACCOUNT_Is0x0200()
    {
        Assert.Equal(0x0200, (int)UserAccountControlFlags.NORMAL_ACCOUNT);
    }

    [Fact]
    public void UacFlags_DONT_EXPIRE_PASSWORD_Is0x10000()
    {
        Assert.Equal(0x10000, (int)UserAccountControlFlags.DONT_EXPIRE_PASSWORD);
    }

    [Fact]
    public void UacFlags_PASSWORD_EXPIRED_Is0x800000()
    {
        Assert.Equal(0x800000, (int)UserAccountControlFlags.PASSWORD_EXPIRED);
    }

    [Fact]
    public void UacFlags_TRUSTED_FOR_DELEGATION_Is0x80000()
    {
        Assert.Equal(0x80000, (int)UserAccountControlFlags.TRUSTED_FOR_DELEGATION);
    }

    [Fact]
    public void UacFlags_LOCKOUT_Is0x0010()
    {
        Assert.Equal(0x0010, (int)UserAccountControlFlags.LOCKOUT);
    }

    [Fact]
    public void UacFlags_WORKSTATION_TRUST_ACCOUNT_Is0x1000()
    {
        Assert.Equal(0x1000, (int)UserAccountControlFlags.WORKSTATION_TRUST_ACCOUNT);
    }

    [Fact]
    public void UacFlags_SERVER_TRUST_ACCOUNT_Is0x2000()
    {
        Assert.Equal(0x2000, (int)UserAccountControlFlags.SERVER_TRUST_ACCOUNT);
    }

    // ── Multiple flags combined (bitmask operations) ───────────────────

    [Fact]
    public void UacFlags_CombinedFlags_PreserveIndividualBits()
    {
        var combined = UserAccountControlFlags.NORMAL_ACCOUNT
                     | UserAccountControlFlags.DONT_EXPIRE_PASSWORD
                     | UserAccountControlFlags.TRUSTED_FOR_DELEGATION;

        Assert.True(combined.HasFlag(UserAccountControlFlags.NORMAL_ACCOUNT));
        Assert.True(combined.HasFlag(UserAccountControlFlags.DONT_EXPIRE_PASSWORD));
        Assert.True(combined.HasFlag(UserAccountControlFlags.TRUSTED_FOR_DELEGATION));
        Assert.False(combined.HasFlag(UserAccountControlFlags.ACCOUNTDISABLE));
    }

    [Fact]
    public void UacFlags_CombinedBitmaskValue_IsCorrect()
    {
        var combined = UserAccountControlFlags.NORMAL_ACCOUNT | UserAccountControlFlags.ACCOUNTDISABLE;
        Assert.Equal(0x0202, (int)combined);
    }

    // ── Flag setting and clearing ──────────────────────────────────────

    [Fact]
    public void UacFlags_SetFlag_AppliesCorrectly()
    {
        var flags = UserAccountControlFlags.NORMAL_ACCOUNT;
        flags |= UserAccountControlFlags.DONT_EXPIRE_PASSWORD;

        Assert.True(flags.HasFlag(UserAccountControlFlags.DONT_EXPIRE_PASSWORD));
        Assert.True(flags.HasFlag(UserAccountControlFlags.NORMAL_ACCOUNT));
    }

    [Fact]
    public void UacFlags_ClearFlag_RemovesCorrectly()
    {
        var flags = UserAccountControlFlags.NORMAL_ACCOUNT | UserAccountControlFlags.ACCOUNTDISABLE;
        flags &= ~UserAccountControlFlags.ACCOUNTDISABLE;

        Assert.True(flags.HasFlag(UserAccountControlFlags.NORMAL_ACCOUNT));
        Assert.False(flags.HasFlag(UserAccountControlFlags.ACCOUNTDISABLE));
    }

    [Fact]
    public void UacFlags_CastToInt_Roundtrips()
    {
        var original = UserAccountControlFlags.NORMAL_ACCOUNT
                     | UserAccountControlFlags.DONT_EXPIRE_PASSWORD;

        int intValue = (int)original;
        var restored = (UserAccountControlFlags)intValue;

        Assert.Equal(original, restored);
    }

    // ── Service method tests ───────────────────────────────────────────

    [Fact]
    public void IsAccountDisabled_FlagSet_ReturnsTrue()
    {
        var obj = new DirectoryObject
        {
            UserAccountControl = (int)(UserAccountControlFlags.NORMAL_ACCOUNT | UserAccountControlFlags.ACCOUNTDISABLE)
        };

        Assert.True(_uacService.IsAccountDisabled(obj));
    }

    [Fact]
    public void IsAccountDisabled_FlagNotSet_ReturnsFalse()
    {
        var obj = new DirectoryObject
        {
            UserAccountControl = (int)UserAccountControlFlags.NORMAL_ACCOUNT
        };

        Assert.False(_uacService.IsAccountDisabled(obj));
    }

    [Fact]
    public void IsPasswordExpired_PASSWORD_EXPIRED_Flag_ReturnsTrue()
    {
        var obj = new DirectoryObject
        {
            UserAccountControl = (int)(UserAccountControlFlags.NORMAL_ACCOUNT | UserAccountControlFlags.PASSWORD_EXPIRED),
            PwdLastSet = DateTimeOffset.UtcNow.ToFileTime(),
        };

        Assert.True(_uacService.IsPasswordExpired(obj));
    }

    [Fact]
    public void IsPasswordExpired_DONT_EXPIRE_PASSWORD_OverridesAll()
    {
        var obj = new DirectoryObject
        {
            UserAccountControl = (int)(UserAccountControlFlags.NORMAL_ACCOUNT
                                     | UserAccountControlFlags.PASSWORD_EXPIRED
                                     | UserAccountControlFlags.DONT_EXPIRE_PASSWORD),
            PwdLastSet = 0, // would normally mean "must change"
        };

        // DONT_EXPIRE_PASSWORD trumps everything
        Assert.False(_uacService.IsPasswordExpired(obj));
    }

    // ── GetDefaultUac tests ────────────────────────────────────────────

    [Fact]
    public void GetDefaultUac_User_ReturnsNormalAccount()
    {
        var uac = _uacService.GetDefaultUac("user");
        Assert.Equal((int)UserAccountControlFlags.NORMAL_ACCOUNT, uac);
    }

    [Fact]
    public void GetDefaultUac_Computer_ReturnsWorkstationTrustAndNormal()
    {
        var uac = _uacService.GetDefaultUac("computer");
        var expected = (int)(UserAccountControlFlags.WORKSTATION_TRUST_ACCOUNT | UserAccountControlFlags.NORMAL_ACCOUNT);
        Assert.Equal(expected, uac);
    }

    [Fact]
    public void GetDefaultUac_DomainController_ReturnsServerTrustAndNormal()
    {
        var uac = _uacService.GetDefaultUac("domaincontroller");
        var expected = (int)(UserAccountControlFlags.SERVER_TRUST_ACCOUNT | UserAccountControlFlags.NORMAL_ACCOUNT);
        Assert.Equal(expected, uac);
    }

    // ── SetComputerAccountFlags ────────────────────────────────────────

    [Fact]
    public void SetComputerAccountFlags_SetsWorkstationTrust()
    {
        var obj = new DirectoryObject
        {
            DistinguishedName = "CN=PC01,CN=Computers,DC=corp,DC=com",
            UserAccountControl = (int)(UserAccountControlFlags.NORMAL_ACCOUNT | UserAccountControlFlags.ACCOUNTDISABLE),
        };

        _uacService.SetComputerAccountFlags(obj);

        var flags = (UserAccountControlFlags)obj.UserAccountControl;
        Assert.True(flags.HasFlag(UserAccountControlFlags.WORKSTATION_TRUST_ACCOUNT));
        Assert.True(flags.HasFlag(UserAccountControlFlags.NORMAL_ACCOUNT));
        Assert.False(flags.HasFlag(UserAccountControlFlags.ACCOUNTDISABLE)); // cleared on join
        Assert.False(flags.HasFlag(UserAccountControlFlags.LOCKOUT)); // cleared on join
    }

    // ── ValidateLogon integration test ─────────────────────────────────

    [Fact]
    public void ValidateLogon_NormalEnabledAccount_ReturnsNull()
    {
        var obj = new DirectoryObject
        {
            DistinguishedName = "CN=User,OU=Users,DC=corp,DC=com",
            UserAccountControl = (int)UserAccountControlFlags.NORMAL_ACCOUNT,
            PwdLastSet = DateTimeOffset.UtcNow.ToFileTime(),
            AccountExpires = 0,
        };

        Assert.Null(_uacService.ValidateLogon(obj));
    }

    [Fact]
    public void ValidateLogon_TrustedForDelegation_StillAllowed()
    {
        var obj = new DirectoryObject
        {
            DistinguishedName = "CN=SvcAccount,OU=Services,DC=corp,DC=com",
            UserAccountControl = (int)(UserAccountControlFlags.NORMAL_ACCOUNT | UserAccountControlFlags.TRUSTED_FOR_DELEGATION),
            PwdLastSet = DateTimeOffset.UtcNow.ToFileTime(),
            AccountExpires = 0,
        };

        // TRUSTED_FOR_DELEGATION is not a blocking flag
        Assert.Null(_uacService.ValidateLogon(obj));
    }
}
