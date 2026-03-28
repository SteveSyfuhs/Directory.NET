using Directory.Core.Models;
using Directory.Kerberos;
using Directory.Security.Claims;
using Kerberos.NET.Entities.Pac;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Directory.Tests;

/// <summary>
/// Tests for enhanced PAC generation fields: ExtraSids, DomainSid,
/// password time fields, profile fields, UserFlags, and UPN fallback logic.
/// </summary>
public class PacEnhancedTests
{
    private readonly PacGenerator _generator;
    private readonly InMemoryDirectoryStore _store;

    private const string DomainDn = "DC=corp,DC=com";
    private const string DomainSid = "S-1-5-21-100-200-300";

    public PacEnhancedTests()
    {
        _store = new InMemoryDirectoryStore();
        _generator = new PacGenerator(
            _store,
            new StubClaimsProvider(),
            NullLogger<PacGenerator>.Instance);
    }

    // ════════════════════════════════════════════════════════════════
    //  ExtraSids
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PAC_IncludesAuthenticatedUsers_ExtraSid()
    {
        var user = CreateTestUser("jdoe", $"{DomainSid}-1001");

        var pac = await _generator.GenerateAsync(user, DomainDn, "default");

        Assert.NotNull(pac.LogonInfo.ExtraIds);
        Assert.Contains(pac.LogonInfo.ExtraIds, e => RpcSidToString(e.Sid) == "S-1-5-11");
    }

    [Fact]
    public async Task PAC_IncludesAuthAuthorityAssertedIdentity_ExtraSid()
    {
        var user = CreateTestUser("jdoe", $"{DomainSid}-1001");

        var pac = await _generator.GenerateAsync(user, DomainDn, "default");

        Assert.NotNull(pac.LogonInfo.ExtraIds);
        Assert.Contains(pac.LogonInfo.ExtraIds, e => RpcSidToString(e.Sid) == "S-1-18-1");
    }

    [Fact]
    public async Task PAC_IncludesKeyTrustIdentity_ExtraSid()
    {
        var user = CreateTestUser("jdoe", $"{DomainSid}-1001");

        var pac = await _generator.GenerateAsync(user, DomainDn, "default");

        Assert.NotNull(pac.LogonInfo.ExtraIds);
        Assert.Contains(pac.LogonInfo.ExtraIds, e => RpcSidToString(e.Sid) == "S-1-18-2");
    }

    [Fact]
    public async Task PAC_ExtraSids_ContainsAtLeastThreeWellKnownSids()
    {
        var user = CreateTestUser("jdoe", $"{DomainSid}-1001");

        var pac = await _generator.GenerateAsync(user, DomainDn, "default");

        Assert.NotNull(pac.LogonInfo.ExtraIds);
        // At minimum: S-1-18-1, S-1-5-11, S-1-18-2 (plus S-1-2-0)
        Assert.True(pac.LogonInfo.ExtraIds.Count() >= 3,
            $"Expected at least 3 ExtraSids, got {pac.LogonInfo.ExtraIds.Count()}");
    }

    // ════════════════════════════════════════════════════════════════
    //  DomainSid
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PAC_DomainId_IsNotNull()
    {
        var user = CreateTestUser("jdoe", $"{DomainSid}-1001");

        var pac = await _generator.GenerateAsync(user, DomainDn, "default");

        Assert.NotNull(pac.LogonInfo.DomainId);
    }

    [Fact]
    public async Task PAC_DomainId_MatchesDomainSid()
    {
        var user = CreateTestUser("jdoe", $"{DomainSid}-1001");

        var pac = await _generator.GenerateAsync(user, DomainDn, "default");

        // DomainId should be the domain SID (user SID minus the RID)
        var domainIdStr = RpcSidToString(pac.LogonInfo.DomainId);
        Assert.Equal(DomainSid, domainIdStr);
    }

    [Fact]
    public async Task PAC_DomainId_DiffersFromUserSid()
    {
        var userSid = $"{DomainSid}-1001";
        var user = CreateTestUser("jdoe", userSid);

        var pac = await _generator.GenerateAsync(user, DomainDn, "default");

        var domainIdStr = RpcSidToString(pac.LogonInfo.DomainId);
        // Domain SID has fewer sub-authorities than user SID
        Assert.True(domainIdStr.Length < userSid.Length,
            $"Domain SID '{domainIdStr}' should be shorter than user SID '{userSid}'");
    }

    // ════════════════════════════════════════════════════════════════
    //  Password Time Fields
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PAC_PwdLastChangeTime_Populated()
    {
        var user = CreateTestUser("jdoe", $"{DomainSid}-1001");
        user.PwdLastSet = DateTimeOffset.UtcNow.AddDays(-10).ToFileTime();

        var pac = await _generator.GenerateAsync(user, DomainDn, "default");

        // PwdLastChangeTime should be populated from PwdLastSet
        DateTimeOffset pwdLastChange = pac.LogonInfo.PwdLastChangeTime;
        Assert.True(pwdLastChange > DateTimeOffset.MinValue,
            "PwdLastChangeTime should be populated");
        Assert.True(pwdLastChange < DateTimeOffset.UtcNow,
            "PwdLastChangeTime should be in the past");
    }

    [Fact]
    public async Task PAC_PwdCanChangeTime_Populated()
    {
        var user = CreateTestUser("jdoe", $"{DomainSid}-1001");
        user.PwdLastSet = DateTimeOffset.UtcNow.AddDays(-10).ToFileTime();

        var pac = await _generator.GenerateAsync(user, DomainDn, "default");

        // PwdCanChangeTime = PwdLastSet + min password age (1 day default)
        DateTimeOffset pwdCanChange = pac.LogonInfo.PwdCanChangeTime;
        DateTimeOffset pwdLastChange = pac.LogonInfo.PwdLastChangeTime;
        Assert.True(pwdCanChange > pwdLastChange,
            "PwdCanChangeTime should be after PwdLastChangeTime");
    }

    [Fact]
    public async Task PAC_PwdMustChangeTime_Populated_NormalAccount()
    {
        var user = CreateTestUser("jdoe", $"{DomainSid}-1001");
        user.PwdLastSet = DateTimeOffset.UtcNow.AddDays(-10).ToFileTime();
        user.UserAccountControl = 0x200; // NORMAL_ACCOUNT, no DONT_EXPIRE

        var pac = await _generator.GenerateAsync(user, DomainDn, "default");

        // PwdMustChangeTime = PwdLastSet + max password age (42 days default)
        DateTimeOffset pwdMustChange = pac.LogonInfo.PwdMustChangeTime;
        DateTimeOffset pwdLastChange = pac.LogonInfo.PwdLastChangeTime;
        Assert.True(pwdMustChange > pwdLastChange,
            "PwdMustChangeTime should be after PwdLastChangeTime for normal accounts");
        Assert.True(pwdMustChange < DateTimeOffset.MaxValue,
            "PwdMustChangeTime should not be MaxValue for normal accounts");
    }

    [Fact]
    public async Task PAC_PwdMustChangeTime_MaxValue_WhenDontExpire()
    {
        var user = CreateTestUser("jdoe", $"{DomainSid}-1001");
        user.PwdLastSet = DateTimeOffset.UtcNow.AddDays(-10).ToFileTime();
        user.UserAccountControl = 0x10200; // NORMAL_ACCOUNT | DONT_EXPIRE_PASSWORD

        var pac = await _generator.GenerateAsync(user, DomainDn, "default");

        DateTimeOffset pwdMustChange = pac.LogonInfo.PwdMustChangeTime;
        Assert.Equal(DateTimeOffset.MaxValue, pwdMustChange);
    }

    // ════════════════════════════════════════════════════════════════
    //  Profile Fields
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PAC_LogonScript_FromUserAttribute()
    {
        var user = CreateTestUser("jdoe", $"{DomainSid}-1001");
        user.SetAttribute("scriptPath", new DirectoryAttribute("scriptPath", "logon.bat"));

        var pac = await _generator.GenerateAsync(user, DomainDn, "default");

        Assert.Equal("logon.bat", pac.LogonInfo.LogonScript);
    }

    [Fact]
    public async Task PAC_ProfilePath_FromUserAttribute()
    {
        var user = CreateTestUser("jdoe", $"{DomainSid}-1001");
        user.SetAttribute("profilePath", new DirectoryAttribute("profilePath", @"\\server\profiles\jdoe"));

        var pac = await _generator.GenerateAsync(user, DomainDn, "default");

        Assert.Equal(@"\\server\profiles\jdoe", pac.LogonInfo.ProfilePath);
    }

    [Fact]
    public async Task PAC_HomeDirectory_FromUserAttribute()
    {
        var user = CreateTestUser("jdoe", $"{DomainSid}-1001");
        user.SetAttribute("homeDirectory", new DirectoryAttribute("homeDirectory", @"\\server\home\jdoe"));

        var pac = await _generator.GenerateAsync(user, DomainDn, "default");

        Assert.Equal(@"\\server\home\jdoe", pac.LogonInfo.HomeDirectory);
    }

    [Fact]
    public async Task PAC_HomeDrive_FromUserAttribute()
    {
        var user = CreateTestUser("jdoe", $"{DomainSid}-1001");
        user.SetAttribute("homeDrive", new DirectoryAttribute("homeDrive", "H:"));

        var pac = await _generator.GenerateAsync(user, DomainDn, "default");

        Assert.Equal("H:", pac.LogonInfo.HomeDrive);
    }

    // ════════════════════════════════════════════════════════════════
    //  Other Fields
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PAC_ServerName_IsMachineName()
    {
        var user = CreateTestUser("jdoe", $"{DomainSid}-1001");

        var pac = await _generator.GenerateAsync(user, DomainDn, "default");

        Assert.Equal(Environment.MachineName, pac.LogonInfo.ServerName);
    }

    [Fact]
    public async Task PAC_UserAccountControl_MatchesUser()
    {
        var user = CreateTestUser("jdoe", $"{DomainSid}-1001");
        user.UserAccountControl = 0x200; // NORMAL_ACCOUNT

        var pac = await _generator.GenerateAsync(user, DomainDn, "default");

        Assert.Equal((UserAccountControlFlags)0x200, pac.LogonInfo.UserAccountControl);
    }

    [Fact]
    public async Task PAC_UserFlags_IncludesLogonExtraSids()
    {
        var user = CreateTestUser("jdoe", $"{DomainSid}-1001");

        var pac = await _generator.GenerateAsync(user, DomainDn, "default");

        // ExtraSids are always present (well-known SIDs), so LOGON_EXTRA_SIDS should be set
        Assert.True(pac.LogonInfo.UserFlags.HasFlag(UserFlags.LOGON_EXTRA_SIDS),
            "UserFlags should include LOGON_EXTRA_SIDS when ExtraSids are present");
    }

    [Fact]
    public async Task PAC_PrimaryGroupId_MatchesUser_DomainUsers()
    {
        var user = CreateTestUser("jdoe", $"{DomainSid}-1001");
        user.PrimaryGroupId = 513; // Domain Users

        var pac = await _generator.GenerateAsync(user, DomainDn, "default");

        Assert.Equal(513u, pac.LogonInfo.GroupId);
    }

    [Fact]
    public async Task PAC_PrimaryGroupId_MatchesUser_DomainComputers()
    {
        var user = CreateTestUser("machine$", $"{DomainSid}-1002");
        user.PrimaryGroupId = 515; // Domain Computers
        user.ObjectClass = ["top", "person", "organizationalPerson", "user", "computer"];

        var pac = await _generator.GenerateAsync(user, DomainDn, "default");

        Assert.Equal(515u, pac.LogonInfo.GroupId);
    }

    [Fact]
    public async Task PAC_UpnDomainInfo_AlwaysSet()
    {
        var user = CreateTestUser("jdoe", $"{DomainSid}-1001");
        user.UserPrincipalName = null;

        var pac = await _generator.GenerateAsync(user, DomainDn, "default");

        // UpnDomainInformation is always populated per MS-PAC 2.10
        Assert.NotNull(pac.UpnDomainInformation);
    }

    // ════════════════════════════════════════════════════════════════
    //  UPN Fallback
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PAC_UPN_WithExplicitUpn()
    {
        var user = CreateTestUser("jdoe", $"{DomainSid}-1001");
        user.UserPrincipalName = "jdoe@corp.com";

        var pac = await _generator.GenerateAsync(user, DomainDn, "default");

        Assert.Equal("jdoe@corp.com", pac.UpnDomainInformation.Upn);
    }

    [Fact]
    public async Task PAC_UPN_Fallback_SynthesizesSamAccountNameAtDomain()
    {
        var user = CreateTestUser("jdoe", $"{DomainSid}-1001");
        user.UserPrincipalName = null;

        var pac = await _generator.GenerateAsync(user, DomainDn, "default");

        // Should synthesize sAMAccountName@domain when no UPN
        Assert.Equal("jdoe@corp.com", pac.UpnDomainInformation.Upn);
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
            DomainDn = "DC=corp,DC=com",
            PrimaryGroupId = 513,
            PwdLastSet = DateTimeOffset.UtcNow.AddDays(-5).ToFileTime(),
        };
    }

    /// <summary>
    /// Converts an RpcSid back to a SID string (e.g. "S-1-5-21-100-200-300").
    /// </summary>
    private static string RpcSidToString(RpcSid sid)
    {
        // Reconstruct the authority from the 6-byte big-endian array
        var authBytes = sid.IdentifierAuthority.IdentifierAuthority.Span;
        long authority = 0;
        for (int i = 0; i < 6; i++)
            authority = (authority << 8) | authBytes[i];

        var parts = new List<string> { "S", sid.Revision.ToString(), authority.ToString() };
        var subAuth = sid.SubAuthority.Span;
        for (int i = 0; i < subAuth.Length; i++)
            parts.Add(subAuth[i].ToString());

        return string.Join("-", parts);
    }
}
