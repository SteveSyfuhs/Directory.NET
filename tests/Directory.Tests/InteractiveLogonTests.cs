using Directory.Core.Models;
using Directory.Kerberos;
using Directory.Security;
using Directory.Security.Apds;
using Directory.Security.Claims;
using Kerberos.NET.Entities.Pac;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Directory.Tests;

/// <summary>
/// Tests the complete interactive logon flow: Kerberos PAC generation,
/// password validation, and post-logon field verification.
/// </summary>
public class InteractiveLogonTests
{
    private readonly InMemoryDirectoryStore _store;
    private readonly PasswordService _passwordService;
    private readonly PacGenerator _pacGenerator;
    private readonly AccountRestrictions _accountRestrictions;

    private const string TenantId = "default";
    private const string DomainDn = "DC=corp,DC=com";
    private const string DomainSid = "S-1-5-21-100-200-300";
    private const string TestPassword = "P@ssw0rd!Complex#1";

    public InteractiveLogonTests()
    {
        _store = new InMemoryDirectoryStore();
        _passwordService = new PasswordService(_store, NullLogger<PasswordService>.Instance);
        _pacGenerator = new PacGenerator(
            _store,
            new StubClaimsProvider(),
            NullLogger<PacGenerator>.Instance);
        _accountRestrictions = new AccountRestrictions(
            NullLogger<AccountRestrictions>.Instance);
    }

    // ════════════════════════════════════════════════════════════════
    //  Kerberos Path
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task KerberosPath_UserGetsValidPac()
    {
        var user = await CreateAndStoreTestUser("jdoe", 1001);

        var pac = await _pacGenerator.GenerateAsync(user, DomainDn, TenantId);

        Assert.NotNull(pac.LogonInfo);
        Assert.Equal("jdoe", pac.LogonInfo.UserName);
        Assert.Equal("CORP", pac.LogonInfo.DomainName);
    }

    [Fact]
    public async Task KerberosPath_PacIncludesGroupSids()
    {
        var user = await CreateAndStoreTestUser("jdoe", 1001);

        // Create a group and add user to it
        var group = new DirectoryObject
        {
            Id = "cn=developers,ou=groups,dc=corp,dc=com",
            TenantId = TenantId,
            DistinguishedName = "CN=Developers,OU=Groups,DC=corp,DC=com",
            ObjectClass = ["top", "group"],
            ObjectCategory = "group",
            Cn = "Developers",
            ObjectSid = $"{DomainSid}-1100",
            DomainDn = DomainDn,
        };
        _store.Add(group);
        user.MemberOf.Add("CN=Developers,OU=Groups,DC=corp,DC=com");

        var pac = await _pacGenerator.GenerateAsync(user, DomainDn, TenantId);

        Assert.NotNull(pac.LogonInfo.GroupIds);
        Assert.Contains(pac.LogonInfo.GroupIds, g => g.RelativeId == 1100);
    }

    [Fact]
    public async Task KerberosPath_DisabledUserRejected()
    {
        var user = await CreateAndStoreTestUser("jdoe", 1001);
        user.UserAccountControl |= 0x02; // ACCOUNTDISABLE

        var status = _accountRestrictions.CheckAccountRestrictions(user, LogonType.Interactive);

        Assert.Equal(NtStatus.StatusAccountDisabled, status);
    }

    // ════════════════════════════════════════════════════════════════
    //  Password Validation Path
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PasswordValidation_CorrectPasswordSucceeds()
    {
        var user = await CreateAndStoreTestUser("jdoe", 1001);
        await _passwordService.SetPasswordAsync(TenantId, user.DistinguishedName, TestPassword);

        var result = await _passwordService.ValidatePasswordAsync(
            TenantId, user.DistinguishedName, TestPassword);

        Assert.True(result, "Correct password should validate successfully");
    }

    [Fact]
    public async Task PasswordValidation_WrongPasswordFails()
    {
        var user = await CreateAndStoreTestUser("jdoe", 1001);
        await _passwordService.SetPasswordAsync(TenantId, user.DistinguishedName, TestPassword);

        var result = await _passwordService.ValidatePasswordAsync(
            TenantId, user.DistinguishedName, "WrongPassword123!");

        Assert.False(result, "Wrong password should not validate");
    }

    // ════════════════════════════════════════════════════════════════
    //  Post-Logon Verification
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PostLogon_UserHasCorrectSid()
    {
        var user = await CreateAndStoreTestUser("jdoe", 1001);

        Assert.Equal($"{DomainSid}-1001", user.ObjectSid);
    }

    [Fact]
    public async Task PostLogon_UserHasCorrectUpnInPac()
    {
        var user = await CreateAndStoreTestUser("jdoe", 1001);
        user.UserPrincipalName = "jdoe@corp.com";

        var pac = await _pacGenerator.GenerateAsync(user, DomainDn, TenantId);

        Assert.NotNull(pac.UpnDomainInformation);
        Assert.Equal("jdoe@corp.com", pac.UpnDomainInformation.Upn);
    }

    [Fact]
    public async Task PostLogon_UserHasPrimaryGroupInPac()
    {
        var user = await CreateAndStoreTestUser("jdoe", 1001);
        user.PrimaryGroupId = 513;

        var pac = await _pacGenerator.GenerateAsync(user, DomainDn, TenantId);

        Assert.Equal(513u, pac.LogonInfo.GroupId);
        // Primary group should also appear in GroupIds
        Assert.Contains(pac.LogonInfo.GroupIds, g => g.RelativeId == 513);
    }

    // ════════════════════════════════════════════════════════════════
    //  End-to-End Interactive Logon
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task EndToEnd_InteractiveLogon_FullVerification()
    {
        // Step 1: Create user with full attributes
        var user = await CreateAndStoreTestUser("jdoe", 1001);
        user.DisplayName = "John Doe";
        user.UserPrincipalName = "jdoe@corp.com";
        user.PrimaryGroupId = 513;
        user.SetAttribute("scriptPath", new DirectoryAttribute("scriptPath", "logon.bat"));
        user.SetAttribute("profilePath", new DirectoryAttribute("profilePath", @"\\server\profiles\jdoe"));
        user.SetAttribute("homeDirectory", new DirectoryAttribute("homeDirectory", @"\\server\home\jdoe"));
        user.SetAttribute("homeDrive", new DirectoryAttribute("homeDrive", "H:"));

        // Step 2: Set password
        await _passwordService.SetPasswordAsync(TenantId, user.DistinguishedName, TestPassword);
        var storedUser = await _store.GetByDnAsync(TenantId, user.DistinguishedName);
        Assert.NotNull(storedUser);
        Assert.NotEmpty(storedUser.KerberosKeys);

        // Step 3: Add group membership
        var group = new DirectoryObject
        {
            Id = "cn=developers,ou=groups,dc=corp,dc=com",
            TenantId = TenantId,
            DistinguishedName = "CN=Developers,OU=Groups,DC=corp,DC=com",
            ObjectClass = ["top", "group"],
            ObjectCategory = "group",
            Cn = "Developers",
            ObjectSid = $"{DomainSid}-1100",
            DomainDn = DomainDn,
        };
        _store.Add(group);
        storedUser.MemberOf.Add("CN=Developers,OU=Groups,DC=corp,DC=com");

        // Step 4: Validate password (Kerberos key comparison)
        var passwordValid = await _passwordService.ValidatePasswordAsync(
            TenantId, storedUser.DistinguishedName, TestPassword);
        Assert.True(passwordValid, "Password must validate");

        // Step 5: Check account restrictions
        var status = _accountRestrictions.CheckAccountRestrictions(storedUser, LogonType.Interactive);
        Assert.Equal(NtStatus.StatusSuccess, status);

        // Step 6: Generate PAC (Kerberos path)
        var pac = await _pacGenerator.GenerateAsync(storedUser, DomainDn, TenantId);

        // Step 7: Verify all PAC fields
        Assert.NotNull(pac);
        Assert.NotNull(pac.LogonInfo);

        // Identity
        Assert.Equal("jdoe", pac.LogonInfo.UserName);
        Assert.Equal("John Doe", pac.LogonInfo.UserDisplayName);
        Assert.Equal("CORP", pac.LogonInfo.DomainName);
        Assert.Equal(1001u, pac.LogonInfo.UserId);

        // Groups
        Assert.Equal(513u, pac.LogonInfo.GroupId);
        Assert.NotNull(pac.LogonInfo.GroupIds);
        Assert.Contains(pac.LogonInfo.GroupIds, g => g.RelativeId == 513);  // Domain Users
        Assert.Contains(pac.LogonInfo.GroupIds, g => g.RelativeId == 1100); // Developers

        // ExtraSids
        Assert.NotNull(pac.LogonInfo.ExtraIds);
        Assert.True(pac.LogonInfo.ExtraIds.Count() >= 3);

        // UPN
        Assert.NotNull(pac.UpnDomainInformation);
        Assert.Equal("jdoe@corp.com", pac.UpnDomainInformation.Upn);
        Assert.Equal("corp.com", pac.UpnDomainInformation.Domain);

        // Profile fields
        Assert.Equal("logon.bat", pac.LogonInfo.LogonScript);
        Assert.Equal(@"\\server\profiles\jdoe", pac.LogonInfo.ProfilePath);
        Assert.Equal(@"\\server\home\jdoe", pac.LogonInfo.HomeDirectory);
        Assert.Equal("H:", pac.LogonInfo.HomeDrive);

        // Server
        Assert.Equal(Environment.MachineName, pac.LogonInfo.ServerName);

        // Password times
        DateTimeOffset pwdLastChange = pac.LogonInfo.PwdLastChangeTime;
        Assert.True(pwdLastChange > DateTimeOffset.MinValue);

        // Signatures
        Assert.NotNull(pac.ServerSignature);
        Assert.NotNull(pac.KdcSignature);
    }

    // ════════════════════════════════════════════════════════════════
    //  Helpers
    // ════════════════════════════════════════════════════════════════

    private async Task<DirectoryObject> CreateAndStoreTestUser(string samAccountName, int rid)
    {
        var user = new DirectoryObject
        {
            Id = $"cn={samAccountName},ou=users,dc=corp,dc=com",
            TenantId = TenantId,
            DistinguishedName = $"CN={samAccountName},OU=Users,DC=corp,DC=com",
            ObjectClass = ["top", "person", "organizationalPerson", "user"],
            ObjectCategory = "user",
            Cn = samAccountName,
            SAMAccountName = samAccountName,
            ObjectSid = $"{DomainSid}-{rid}",
            DomainDn = DomainDn,
            PrimaryGroupId = 513,
            UserAccountControl = 0x200, // NORMAL_ACCOUNT
            PwdLastSet = DateTimeOffset.UtcNow.AddDays(-5).ToFileTime(),
            AccountExpires = 0x7FFFFFFFFFFFFFFF,
            WhenCreated = DateTimeOffset.UtcNow,
            WhenChanged = DateTimeOffset.UtcNow,
        };

        await _store.CreateAsync(user);
        return user;
    }
}
