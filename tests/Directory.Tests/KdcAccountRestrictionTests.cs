using Directory.Core.Models;
using Directory.Kerberos;
using Directory.Security.Apds;
using Kerberos.NET;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Directory.Tests;

/// <summary>
/// Tests that account restrictions are enforced during Kerberos AS-REQ processing
/// (via CosmosKerberosPrincipal.RetrieveLongTermCredential) and directly via
/// AccountRestrictions.CheckAccountRestrictions.
/// </summary>
public class KdcAccountRestrictionTests
{
    private readonly AccountRestrictions _accountRestrictions;

    private const string DomainDn = "DC=corp,DC=com";
    private const string DomainSid = "S-1-5-21-100-200-300";
    private const string DefaultRealm = "CORP.COM";

    // UAC flag constants
    private const int UacNormalAccount = 0x00000200;
    private const int UacAccountDisable = 0x00000002;
    private const int UacDontExpirePassword = 0x00010000;
    private const int UacLockout = 0x00000010;

    public KdcAccountRestrictionTests()
    {
        _accountRestrictions = new AccountRestrictions(
            NullLogger<AccountRestrictions>.Instance);
    }

    // ════════════════════════════════════════════════════════════════
    //  KDC-level tests (CosmosKerberosPrincipal.RetrieveLongTermCredential)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ActiveAccount_Succeeds_NoException()
    {
        var user = CreateTestUser("jdoe", $"{DomainSid}-1001");
        user.UserAccountControl = UacNormalAccount;
        user.PwdLastSet = DateTimeOffset.UtcNow.AddDays(-5).ToFileTime();
        SetKerberosKeys(user);

        var principal = CreatePrincipal(user);

        // Should not throw
        var key = principal.RetrieveLongTermCredential();
        Assert.NotNull(key);
    }

    [Fact]
    public void DisabledAccount_Throws_KerberosValidationException()
    {
        var user = CreateTestUser("jdoe", $"{DomainSid}-1001");
        user.UserAccountControl = UacNormalAccount | UacAccountDisable;
        user.PwdLastSet = DateTimeOffset.UtcNow.AddDays(-5).ToFileTime();
        SetKerberosKeys(user);

        var principal = CreatePrincipal(user);

        var ex = Assert.Throws<KerberosValidationException>(() => principal.RetrieveLongTermCredential());
        Assert.Contains("disabled", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LockedAccount_Throws_KerberosValidationException()
    {
        var user = CreateTestUser("jdoe", $"{DomainSid}-1001");
        user.UserAccountControl = UacNormalAccount | UacLockout;
        user.PwdLastSet = DateTimeOffset.UtcNow.AddDays(-5).ToFileTime();
        SetKerberosKeys(user);

        var principal = CreatePrincipal(user);

        var ex = Assert.Throws<KerberosValidationException>(() => principal.RetrieveLongTermCredential());
        Assert.Contains("locked", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExpiredAccount_Throws_KerberosValidationException()
    {
        var user = CreateTestUser("jdoe", $"{DomainSid}-1001");
        user.UserAccountControl = UacNormalAccount;
        user.PwdLastSet = DateTimeOffset.UtcNow.AddDays(-5).ToFileTime();
        // Set account expiry in the past
        user.AccountExpires = DateTimeOffset.UtcNow.AddDays(-1).ToFileTime();
        SetKerberosKeys(user);

        var principal = CreatePrincipal(user);

        var ex = Assert.Throws<KerberosValidationException>(() => principal.RetrieveLongTermCredential());
        Assert.Contains("expired", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PasswordExpired_Throws_KerberosValidationException()
    {
        var user = CreateTestUser("jdoe", $"{DomainSid}-1001");
        user.UserAccountControl = UacNormalAccount;
        // pwdLastSet = 0 means must change password
        user.PwdLastSet = 0;
        SetKerberosKeys(user);

        var principal = CreatePrincipal(user);

        var ex = Assert.Throws<KerberosValidationException>(() => principal.RetrieveLongTermCredential());
        Assert.Contains("expired", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DontExpirePassword_BypassesPasswordExpiry()
    {
        var user = CreateTestUser("jdoe", $"{DomainSid}-1001");
        user.UserAccountControl = UacNormalAccount | UacDontExpirePassword;
        // pwdLastSet = 0 would normally trigger must-change, but DONT_EXPIRE bypasses it
        user.PwdLastSet = 0;
        SetKerberosKeys(user);

        var principal = CreatePrincipal(user);

        // Should not throw because DONT_EXPIRE_PASSWORD is set
        var key = principal.RetrieveLongTermCredential();
        Assert.NotNull(key);
    }

    [Fact]
    public void NormalEnabledAccount_PassesRestrictions()
    {
        var user = CreateTestUser("jdoe", $"{DomainSid}-1001");
        user.UserAccountControl = UacNormalAccount;
        user.PwdLastSet = DateTimeOffset.UtcNow.AddDays(-5).ToFileTime();
        user.AccountExpires = 0x7FFFFFFFFFFFFFFF; // Never expires
        SetKerberosKeys(user);

        var principal = CreatePrincipal(user);

        var key = principal.RetrieveLongTermCredential();
        Assert.NotNull(key);
    }

    // ════════════════════════════════════════════════════════════════
    //  Direct AccountRestrictions tests
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void CheckAccountRestrictions_Disabled_ReturnsStatusAccountDisabled()
    {
        var user = CreateTestUser("jdoe", $"{DomainSid}-1001");
        user.UserAccountControl = UacNormalAccount | UacAccountDisable;
        user.PwdLastSet = DateTimeOffset.UtcNow.AddDays(-5).ToFileTime();

        var status = _accountRestrictions.CheckAccountRestrictions(user, LogonType.Network);

        Assert.Equal(NtStatus.StatusAccountDisabled, status);
    }

    [Fact]
    public void CheckAccountRestrictions_Locked_ReturnsStatusAccountLockedOut()
    {
        var user = CreateTestUser("jdoe", $"{DomainSid}-1001");
        user.UserAccountControl = UacNormalAccount | UacLockout;
        user.PwdLastSet = DateTimeOffset.UtcNow.AddDays(-5).ToFileTime();

        var status = _accountRestrictions.CheckAccountRestrictions(user, LogonType.Network);

        Assert.Equal(NtStatus.StatusAccountLockedOut, status);
    }

    [Fact]
    public void CheckAccountRestrictions_Expired_ReturnsStatusAccountExpired()
    {
        var user = CreateTestUser("jdoe", $"{DomainSid}-1001");
        user.UserAccountControl = UacNormalAccount;
        user.PwdLastSet = DateTimeOffset.UtcNow.AddDays(-5).ToFileTime();
        user.AccountExpires = DateTimeOffset.UtcNow.AddDays(-1).ToFileTime();

        var status = _accountRestrictions.CheckAccountRestrictions(user, LogonType.Network);

        Assert.Equal(NtStatus.StatusAccountExpired, status);
    }

    [Fact]
    public void CheckAccountRestrictions_Valid_ReturnsSuccess()
    {
        var user = CreateTestUser("jdoe", $"{DomainSid}-1001");
        user.UserAccountControl = UacNormalAccount;
        user.PwdLastSet = DateTimeOffset.UtcNow.AddDays(-5).ToFileTime();
        user.AccountExpires = 0x7FFFFFFFFFFFFFFF;

        var status = _accountRestrictions.CheckAccountRestrictions(user, LogonType.Network);

        Assert.Equal(NtStatus.StatusSuccess, status);
    }

    [Fact]
    public void CheckAccountRestrictions_PasswordMustChange_ReturnsStatusPasswordMustChange()
    {
        var user = CreateTestUser("jdoe", $"{DomainSid}-1001");
        user.UserAccountControl = UacNormalAccount; // No DONT_EXPIRE
        user.PwdLastSet = 0; // Must change password

        var status = _accountRestrictions.CheckAccountRestrictions(user, LogonType.Network);

        Assert.Equal(NtStatus.StatusPasswordMustChange, status);
    }

    // ════════════════════════════════════════════════════════════════
    //  Helpers
    // ════════════════════════════════════════════════════════════════

    private static DirectoryObject CreateTestUser(string samAccountName, string sid)
    {
        return new DirectoryObject
        {
            Id = $"cn={samAccountName},ou=users,dc=corp,dc=com",
            TenantId = "default",
            DistinguishedName = $"CN={samAccountName},OU=Users,DC=corp,DC=com",
            ObjectClass = ["top", "person", "organizationalPerson", "user"],
            ObjectCategory = "user",
            Cn = samAccountName,
            SAMAccountName = samAccountName,
            ObjectSid = sid,
            DomainDn = DomainDn,
            PrimaryGroupId = 513,
            AccountExpires = 0x7FFFFFFFFFFFFFFF,
        };
    }

    /// <summary>
    /// Sets dummy Kerberos keys on the user so RetrieveLongTermCredential can return a key.
    /// </summary>
    private static void SetKerberosKeys(DirectoryObject user)
    {
        // Set an AES256 key (EncryptionType 18) with a dummy 32-byte value
        var dummyAes256Key = Convert.ToBase64String(new byte[32]);
        var dummyAes128Key = Convert.ToBase64String(new byte[16]);
        user.KerberosKeys = [$"18:{dummyAes256Key}", $"17:{dummyAes128Key}"];
    }

    private CosmosKerberosPrincipal CreatePrincipal(DirectoryObject user)
    {
        return new CosmosKerberosPrincipal(
            user,
            new InMemoryDirectoryStore(),
            new StubPasswordPolicy(),
            new KerberosOptions { DefaultRealm = DefaultRealm },
            NullLogger<CosmosKerberosPrincipal>.Instance,
            pacGenerator: null,
            accountRestrictions: _accountRestrictions);
    }
}

/// <summary>
/// Stub password policy for tests that don't need real password validation.
/// </summary>
internal class StubPasswordPolicy : Directory.Core.Interfaces.IPasswordPolicy
{
    public Task<bool> ValidatePasswordAsync(string tenantId, string dn, string password, CancellationToken ct = default)
        => Task.FromResult(true);

    public Task SetPasswordAsync(string tenantId, string dn, string password, CancellationToken ct = default)
        => Task.CompletedTask;

    public bool MeetsComplexityRequirements(string password, string samAccountName = null)
        => true;

    public List<Directory.Core.Interfaces.KerberosKeyData> DeriveKerberosKeys(string principalName, string password, string realm)
        => [];
}
