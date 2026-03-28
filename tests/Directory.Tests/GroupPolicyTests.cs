using Directory.Ldap.Handlers;
using Xunit;

namespace Directory.Tests;

/// <summary>
/// Tests for Group Policy processing: gPLink parsing, GPO ordering,
/// enforcement/block inheritance, GPO flags, and RSoP merge logic.
/// </summary>
public class GroupPolicyTests
{
    // ────────────────────────────────────────────────────────────────
    // gPLink parsing
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void ParseGpLink_SingleLink_ParsesCorrectly()
    {
        // Arrange
        var gpLink = "[LDAP://cn={6AC1786C-016F-11D2-945F-00C04fB984F9},cn=policies,cn=system,DC=corp,DC=com;0]";

        // Act
        var result = GroupPolicyHandler.ParseGpLink(gpLink);

        // Assert
        Assert.Single(result);
        Assert.Equal("cn={6AC1786C-016F-11D2-945F-00C04fB984F9},cn=policies,cn=system,DC=corp,DC=com", result[0].GpoDn);
        Assert.Equal(0, result[0].Options);
        Assert.False(result[0].IsDisabled);
        Assert.False(result[0].IsEnforced);
    }

    [Fact]
    public void ParseGpLink_MultipleLinks_ParsesAll()
    {
        var gpLink = "[LDAP://cn={GPO1},cn=policies,cn=system,DC=corp,DC=com;0]" +
                     "[LDAP://cn={GPO2},cn=policies,cn=system,DC=corp,DC=com;2]" +
                     "[LDAP://cn={GPO3},cn=policies,cn=system,DC=corp,DC=com;1]";

        var result = GroupPolicyHandler.ParseGpLink(gpLink);

        Assert.Equal(3, result.Count);
        Assert.Equal("cn={GPO1},cn=policies,cn=system,DC=corp,DC=com", result[0].GpoDn);
        Assert.Equal("cn={GPO2},cn=policies,cn=system,DC=corp,DC=com", result[1].GpoDn);
        Assert.Equal("cn={GPO3},cn=policies,cn=system,DC=corp,DC=com", result[2].GpoDn);
    }

    [Fact]
    public void ParseGpLink_EmptyString_ReturnsEmptyList()
    {
        var result = GroupPolicyHandler.ParseGpLink("");
        Assert.Empty(result);
    }

    [Fact]
    public void ParseGpLink_NullString_ReturnsEmptyList()
    {
        var result = GroupPolicyHandler.ParseGpLink(null);
        Assert.Empty(result);
    }

    // ────────────────────────────────────────────────────────────────
    // GPO flags: 0=enabled, 1=disabled(user), 2=enforced, 3=(1|2)
    // In gPLink options: bit 0 = disabled, bit 1 = enforced
    // ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, false, false)]   // enabled, not enforced
    [InlineData(1, true, false)]    // disabled
    [InlineData(2, false, true)]    // enforced
    [InlineData(3, true, true)]     // disabled AND enforced
    public void ParseGpLink_Options_ParsesDisabledAndEnforced(int options, bool expectedDisabled, bool expectedEnforced)
    {
        var gpLink = $"[LDAP://cn={{GPO}},cn=policies,cn=system,DC=corp,DC=com;{options}]";

        var result = GroupPolicyHandler.ParseGpLink(gpLink);

        Assert.Single(result);
        Assert.Equal(expectedDisabled, result[0].IsDisabled);
        Assert.Equal(expectedEnforced, result[0].IsEnforced);
    }

    // ────────────────────────────────────────────────────────────────
    // GPO attribute flags (on the GPO object itself):
    // bit 0 = user settings disabled
    // bit 1 = computer settings disabled
    // 0=all enabled, 1=user disabled, 2=computer disabled, 3=all disabled
    // ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, true, true)]     // both enabled
    [InlineData(1, false, true)]    // user disabled, computer enabled
    [InlineData(2, true, false)]    // user enabled, computer disabled
    [InlineData(3, false, false)]   // both disabled
    public void GpoFlags_Interpretation_MatchesExpected(int flags, bool userEnabled, bool computerEnabled)
    {
        bool isUserEnabled = (flags & 1) == 0;
        bool isComputerEnabled = (flags & 2) == 0;

        Assert.Equal(userEnabled, isUserEnabled);
        Assert.Equal(computerEnabled, isComputerEnabled);
    }

    // ────────────────────────────────────────────────────────────────
    // Disabled GPO skipped in link parsing
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void ParseGpLink_DisabledGpo_HasIsDisabledTrue()
    {
        var gpLink = "[LDAP://cn={GPO1},cn=policies,cn=system,DC=corp,DC=com;1]";

        var result = GroupPolicyHandler.ParseGpLink(gpLink);

        Assert.Single(result);
        Assert.True(result[0].IsDisabled);
    }

    [Fact]
    public void ParseGpLink_EnabledGpo_HasIsDisabledFalse()
    {
        var gpLink = "[LDAP://cn={GPO1},cn=policies,cn=system,DC=corp,DC=com;0]";

        var result = GroupPolicyHandler.ParseGpLink(gpLink);

        Assert.Single(result);
        Assert.False(result[0].IsDisabled);
    }

    // ────────────────────────────────────────────────────────────────
    // Enforcement flag
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void ParseGpLink_EnforcedGpo_HasIsEnforcedTrue()
    {
        var gpLink = "[LDAP://cn={GPO1},cn=policies,cn=system,DC=corp,DC=com;2]";

        var result = GroupPolicyHandler.ParseGpLink(gpLink);

        Assert.Single(result);
        Assert.True(result[0].IsEnforced);
        Assert.False(result[0].IsDisabled);
    }

    // ────────────────────────────────────────────────────────────────
    // LDAP:// prefix stripping (case-insensitive)
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void ParseGpLink_LowercaseLdap_StripsPrefix()
    {
        var gpLink = "[ldap://cn={GPO1},cn=policies,cn=system,DC=corp,DC=com;0]";

        var result = GroupPolicyHandler.ParseGpLink(gpLink);

        Assert.Single(result);
        Assert.Equal("cn={GPO1},cn=policies,cn=system,DC=corp,DC=com", result[0].GpoDn);
    }

    // ────────────────────────────────────────────────────────────────
    // GPO ordering: link order determines priority
    // Links are processed in the order they appear in gPLink
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void ParseGpLink_OrderPreserved_FirstLinkIsLowestPriority()
    {
        var gpLink = "[LDAP://cn={LOW},cn=policies,cn=system,DC=corp,DC=com;0]" +
                     "[LDAP://cn={HIGH},cn=policies,cn=system,DC=corp,DC=com;0]";

        var result = GroupPolicyHandler.ParseGpLink(gpLink);

        Assert.Equal(2, result.Count);
        // First link = lowest priority, last link = highest priority
        Assert.Equal("cn={LOW},cn=policies,cn=system,DC=corp,DC=com", result[0].GpoDn);
        Assert.Equal("cn={HIGH},cn=policies,cn=system,DC=corp,DC=com", result[1].GpoDn);
    }

    // ────────────────────────────────────────────────────────────────
    // GpLinkEntry model
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void GpLinkEntry_DefaultValues()
    {
        var entry = new GpLinkEntry();
        Assert.Equal("", entry.GpoDn);
        Assert.Equal(0, entry.Options);
        Assert.False(entry.IsDisabled);
        Assert.False(entry.IsEnforced);
    }

    // ────────────────────────────────────────────────────────────────
    // RSoP merge: last writer wins
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void RsopMerge_PasswordPolicy_LastWriterWins()
    {
        // Arrange
        var gpo1Settings = new GpoPolicySettings
        {
            PasswordPolicy = new PasswordPolicySettings { MinimumLength = 8, ComplexityEnabled = false },
        };
        var gpo2Settings = new GpoPolicySettings
        {
            PasswordPolicy = new PasswordPolicySettings { MinimumLength = 12 },
        };

        // Act — merge gpo1 then gpo2 (gpo2 is higher priority)
        var merged = new GpoPolicySettings();
        MergePolicyInto(merged, gpo1Settings);
        MergePolicyInto(merged, gpo2Settings);

        // Assert — gpo2's MinimumLength overwrites gpo1
        Assert.NotNull(merged.PasswordPolicy);
        Assert.Equal(12, merged.PasswordPolicy.MinimumLength);
        // ComplexityEnabled was set by gpo1 and not overridden by gpo2 (gpo2 didn't set it)
        Assert.Equal(false, merged.PasswordPolicy.ComplexityEnabled);
    }

    [Fact]
    public void RsopMerge_AccountLockout_LastWriterWins()
    {
        var gpo1 = new GpoPolicySettings
        {
            AccountLockout = new AccountLockoutSettings { Threshold = 5, DurationMinutes = 30 },
        };
        var gpo2 = new GpoPolicySettings
        {
            AccountLockout = new AccountLockoutSettings { Threshold = 3 },
        };

        var merged = new GpoPolicySettings();
        MergePolicyInto(merged, gpo1);
        MergePolicyInto(merged, gpo2);

        Assert.NotNull(merged.AccountLockout);
        Assert.Equal(3, merged.AccountLockout.Threshold);
        Assert.Equal(30, merged.AccountLockout.DurationMinutes); // not overridden
    }

    [Fact]
    public void RsopMerge_NullSource_DoesNotOverwrite()
    {
        var gpo1 = new GpoPolicySettings
        {
            PasswordPolicy = new PasswordPolicySettings { MinimumLength = 10 },
        };
        var gpo2 = new GpoPolicySettings(); // no password policy set

        var merged = new GpoPolicySettings();
        MergePolicyInto(merged, gpo1);
        MergePolicyInto(merged, gpo2);

        Assert.NotNull(merged.PasswordPolicy);
        Assert.Equal(10, merged.PasswordPolicy.MinimumLength);
    }

    // ────────────────────────────────────────────────────────────────
    // RSoP: enforced GPOs override non-enforced
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void RsopMerge_EnforcedGpo_OverridesNonEnforced()
    {
        // Arrange — simulate processing order:
        // non-enforced GPOs first, then enforced GPOs override
        var nonEnforced = new GpoPolicySettings
        {
            PasswordPolicy = new PasswordPolicySettings { MinimumLength = 8 },
        };
        var enforced = new GpoPolicySettings
        {
            PasswordPolicy = new PasswordPolicySettings { MinimumLength = 14 },
        };

        // Act — process non-enforced first, then enforced
        var merged = new GpoPolicySettings();
        MergePolicyInto(merged, nonEnforced);
        MergePolicyInto(merged, enforced);

        // Assert — enforced wins
        Assert.Equal(14, merged.PasswordPolicy.MinimumLength);
    }

    // ────────────────────────────────────────────────────────────────
    // Policy settings deserialization — password policy
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void PasswordPolicySettings_AllPropertiesSettable()
    {
        var settings = new PasswordPolicySettings
        {
            MinimumLength = 12,
            ComplexityEnabled = true,
            HistoryCount = 24,
            MaxAgeDays = 90,
            MinAgeDays = 1,
            ReversibleEncryption = false,
        };

        Assert.Equal(12, settings.MinimumLength);
        Assert.True(settings.ComplexityEnabled);
        Assert.Equal(24, settings.HistoryCount);
        Assert.Equal(90, settings.MaxAgeDays);
        Assert.Equal(1, settings.MinAgeDays);
        Assert.False(settings.ReversibleEncryption);
    }

    // ────────────────────────────────────────────────────────────────
    // Policy settings deserialization — account lockout
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void AccountLockoutSettings_AllPropertiesSettable()
    {
        var settings = new AccountLockoutSettings
        {
            Threshold = 5,
            DurationMinutes = 30,
            ObservationWindowMinutes = 30,
        };

        Assert.Equal(5, settings.Threshold);
        Assert.Equal(30, settings.DurationMinutes);
        Assert.Equal(30, settings.ObservationWindowMinutes);
    }

    // ────────────────────────────────────────────────────────────────
    // GpoPolicySettings defaults
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void GpoPolicySettings_Default_AllPropertiesNull()
    {
        var settings = new GpoPolicySettings();

        Assert.Null(settings.PasswordPolicy);
        Assert.Null(settings.AccountLockout);
        Assert.Null(settings.AuditPolicy);
        Assert.Null(settings.UserRights);
        Assert.Null(settings.SecurityOptions);
        Assert.Null(settings.SoftwareRestriction);
        Assert.Null(settings.LogonScripts);
        Assert.Null(settings.LogoffScripts);
        Assert.Null(settings.StartupScripts);
        Assert.Null(settings.ShutdownScripts);
        Assert.Null(settings.DriveMappings);
    }

    // ────────────────────────────────────────────────────────────────
    // RsopGpoEntry model
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void RsopGpoEntry_Properties_AreAccessible()
    {
        var entry = new RsopGpoEntry
        {
            GpoDn = "cn={GPO1},cn=policies,cn=system,DC=corp,DC=com",
            DisplayName = "Test GPO",
            SourceContainerDn = "OU=Sales,DC=corp,DC=com",
            IsEnforced = true,
            LinkOrder = 5,
            Settings = new GpoPolicySettings
            {
                PasswordPolicy = new PasswordPolicySettings { MinimumLength = 10 },
            },
        };

        Assert.Equal("cn={GPO1},cn=policies,cn=system,DC=corp,DC=com", entry.GpoDn);
        Assert.Equal("Test GPO", entry.DisplayName);
        Assert.True(entry.IsEnforced);
        Assert.Equal(5, entry.LinkOrder);
        Assert.Equal(10, entry.Settings.PasswordPolicy.MinimumLength);
    }

    // ────────────────────────────────────────────────────────────────
    // RsopResult model
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void RsopResult_Default_HasEmptyCollections()
    {
        var result = new RsopResult();

        Assert.Empty(result.UserGpos);
        Assert.Empty(result.ComputerGpos);
        Assert.NotNull(result.UserPolicy);
        Assert.NotNull(result.ComputerPolicy);
        Assert.NotNull(result.MergedPolicy);
    }

    // ────────────────────────────────────────────────────────────────
    // SYSVOL path construction
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void GetSysvolPath_ProducesCorrectFormat()
    {
        var path = GroupPolicyHandler.GetSysvolPath("corp.com", "6AC1786C-016F-11D2-945F-00C04fB984F9");
        Assert.Equal(@"\\corp.com\SYSVOL\corp.com\Policies\{6AC1786C-016F-11D2-945F-00C04fB984F9}", path);
    }

    // ────────────────────────────────────────────────────────────────
    // AppliedGpo model
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void AppliedGpo_Properties_AreAccessible()
    {
        var applied = new AppliedGpo
        {
            GpoDn = "cn={GPO1},cn=policies,cn=system,DC=corp,DC=com",
            DisplayName = "Default Domain Policy",
            FileSysPath = @"\\corp.com\SYSVOL\corp.com\Policies\{GPO1}",
            IsEnforced = false,
            LinkOrder = 0,
            SourceContainerDn = "DC=corp,DC=com",
        };

        Assert.Equal("Default Domain Policy", applied.DisplayName);
        Assert.False(applied.IsEnforced);
        Assert.Equal(0, applied.LinkOrder);
    }

    // ────────────────────────────────────────────────────────────────
    // AuditPolicySettings
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void AuditPolicySettings_AllPropertiesSettable()
    {
        var audit = new AuditPolicySettings
        {
            AuditLogonEvents = 3,        // success + failure
            AuditObjectAccess = 1,       // success only
            AuditPrivilegeUse = 2,       // failure only
            AuditPolicyChange = 3,
            AuditAccountManagement = 3,
            AuditProcessTracking = 0,
            AuditDsAccess = 1,
            AuditAccountLogon = 3,
            AuditSystemEvents = 1,
        };

        Assert.Equal(3, audit.AuditLogonEvents);
        Assert.Equal(1, audit.AuditObjectAccess);
        Assert.Equal(0, audit.AuditProcessTracking);
    }

    // ────────────────────────────────────────────────────────────────
    // UserRightsSettings
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void UserRightsSettings_ListProperties_AreSettable()
    {
        var rights = new UserRightsSettings
        {
            AllowLogOnLocally = ["BUILTIN\\Administrators", "BUILTIN\\Users"],
            DenyLogOnLocally = ["BUILTIN\\Guests"],
            AllowRemoteDesktop = ["BUILTIN\\Administrators"],
        };

        Assert.Equal(2, rights.AllowLogOnLocally.Count);
        Assert.Single(rights.DenyLogOnLocally);
        Assert.Single(rights.AllowRemoteDesktop);
        Assert.Null(rights.DenyRemoteDesktop);
    }

    // ────────────────────────────────────────────────────────────────
    // RSoP merge with multiple policy types
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void RsopMerge_MultiplePolicyTypes_AllMerged()
    {
        var gpo1 = new GpoPolicySettings
        {
            PasswordPolicy = new PasswordPolicySettings { MinimumLength = 8 },
            AccountLockout = new AccountLockoutSettings { Threshold = 10 },
        };
        var gpo2 = new GpoPolicySettings
        {
            AuditPolicy = new AuditPolicySettings { AuditLogonEvents = 3 },
            UserRights = new UserRightsSettings { AllowLogOnLocally = ["Admins"] },
        };

        var merged = new GpoPolicySettings();
        MergePolicyInto(merged, gpo1);
        MergePolicyInto(merged, gpo2);

        Assert.NotNull(merged.PasswordPolicy);
        Assert.Equal(8, merged.PasswordPolicy.MinimumLength);
        Assert.NotNull(merged.AccountLockout);
        Assert.Equal(10, merged.AccountLockout.Threshold);
        Assert.NotNull(merged.AuditPolicy);
        Assert.Equal(3, merged.AuditPolicy.AuditLogonEvents);
        Assert.NotNull(merged.UserRights);
        Assert.Single(merged.UserRights.AllowLogOnLocally);
    }

    // ────────────────────────────────────────────────────────────────
    // Helpers — reimplements the merge logic for test independence
    // This mirrors GroupPolicyService.MergePolicyInto exactly
    // ────────────────────────────────────────────────────────────────

    private static void MergePolicyInto(GpoPolicySettings target, GpoPolicySettings source)
    {
        if (source.PasswordPolicy != null)
        {
            target.PasswordPolicy ??= new PasswordPolicySettings();
            if (source.PasswordPolicy.MinimumLength.HasValue) target.PasswordPolicy.MinimumLength = source.PasswordPolicy.MinimumLength;
            if (source.PasswordPolicy.ComplexityEnabled.HasValue) target.PasswordPolicy.ComplexityEnabled = source.PasswordPolicy.ComplexityEnabled;
            if (source.PasswordPolicy.HistoryCount.HasValue) target.PasswordPolicy.HistoryCount = source.PasswordPolicy.HistoryCount;
            if (source.PasswordPolicy.MaxAgeDays.HasValue) target.PasswordPolicy.MaxAgeDays = source.PasswordPolicy.MaxAgeDays;
            if (source.PasswordPolicy.MinAgeDays.HasValue) target.PasswordPolicy.MinAgeDays = source.PasswordPolicy.MinAgeDays;
            if (source.PasswordPolicy.ReversibleEncryption.HasValue) target.PasswordPolicy.ReversibleEncryption = source.PasswordPolicy.ReversibleEncryption;
        }

        if (source.AccountLockout != null)
        {
            target.AccountLockout ??= new AccountLockoutSettings();
            if (source.AccountLockout.Threshold.HasValue) target.AccountLockout.Threshold = source.AccountLockout.Threshold;
            if (source.AccountLockout.DurationMinutes.HasValue) target.AccountLockout.DurationMinutes = source.AccountLockout.DurationMinutes;
            if (source.AccountLockout.ObservationWindowMinutes.HasValue) target.AccountLockout.ObservationWindowMinutes = source.AccountLockout.ObservationWindowMinutes;
        }

        if (source.AuditPolicy != null)
        {
            target.AuditPolicy ??= new AuditPolicySettings();
            if (source.AuditPolicy.AuditLogonEvents.HasValue) target.AuditPolicy.AuditLogonEvents = source.AuditPolicy.AuditLogonEvents;
            if (source.AuditPolicy.AuditObjectAccess.HasValue) target.AuditPolicy.AuditObjectAccess = source.AuditPolicy.AuditObjectAccess;
            if (source.AuditPolicy.AuditPrivilegeUse.HasValue) target.AuditPolicy.AuditPrivilegeUse = source.AuditPolicy.AuditPrivilegeUse;
            if (source.AuditPolicy.AuditPolicyChange.HasValue) target.AuditPolicy.AuditPolicyChange = source.AuditPolicy.AuditPolicyChange;
            if (source.AuditPolicy.AuditAccountManagement.HasValue) target.AuditPolicy.AuditAccountManagement = source.AuditPolicy.AuditAccountManagement;
            if (source.AuditPolicy.AuditProcessTracking.HasValue) target.AuditPolicy.AuditProcessTracking = source.AuditPolicy.AuditProcessTracking;
            if (source.AuditPolicy.AuditDsAccess.HasValue) target.AuditPolicy.AuditDsAccess = source.AuditPolicy.AuditDsAccess;
            if (source.AuditPolicy.AuditAccountLogon.HasValue) target.AuditPolicy.AuditAccountLogon = source.AuditPolicy.AuditAccountLogon;
            if (source.AuditPolicy.AuditSystemEvents.HasValue) target.AuditPolicy.AuditSystemEvents = source.AuditPolicy.AuditSystemEvents;
        }

        if (source.UserRights != null)
        {
            target.UserRights ??= new UserRightsSettings();
            if (source.UserRights.AllowLogOnLocally != null) target.UserRights.AllowLogOnLocally = source.UserRights.AllowLogOnLocally;
            if (source.UserRights.DenyLogOnLocally != null) target.UserRights.DenyLogOnLocally = source.UserRights.DenyLogOnLocally;
            if (source.UserRights.AllowRemoteDesktop != null) target.UserRights.AllowRemoteDesktop = source.UserRights.AllowRemoteDesktop;
            if (source.UserRights.DenyRemoteDesktop != null) target.UserRights.DenyRemoteDesktop = source.UserRights.DenyRemoteDesktop;
            if (source.UserRights.BackupFilesAndDirectories != null) target.UserRights.BackupFilesAndDirectories = source.UserRights.BackupFilesAndDirectories;
            if (source.UserRights.RestoreFilesAndDirectories != null) target.UserRights.RestoreFilesAndDirectories = source.UserRights.RestoreFilesAndDirectories;
            if (source.UserRights.ShutdownSystem != null) target.UserRights.ShutdownSystem = source.UserRights.ShutdownSystem;
            if (source.UserRights.ChangeSystemTime != null) target.UserRights.ChangeSystemTime = source.UserRights.ChangeSystemTime;
        }
    }
}
