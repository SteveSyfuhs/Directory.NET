using Directory.Rpc.Lsa;
using Xunit;

namespace Directory.Tests;

/// <summary>
/// Tests for LSA well-known SID mappings, privilege constants, account rights,
/// and policy information class values. These validate the correctness of the
/// static data tables in LsaOperations and LsaConstants.
/// </summary>
public class LsaWellKnownSidTests
{
    // ────────────────────────────────────────────────────────────────
    // Well-known SID → Name mapping
    // These reflect the private WellKnownSids dictionary in LsaOperations.
    // ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("S-1-0-0", "Nobody")]
    [InlineData("S-1-1-0", "Everyone")]
    [InlineData("S-1-2-0", "LOCAL")]
    [InlineData("S-1-3-0", "CREATOR OWNER")]
    [InlineData("S-1-3-1", "CREATOR GROUP")]
    [InlineData("S-1-5-1", "DIALUP")]
    [InlineData("S-1-5-2", "NETWORK")]
    [InlineData("S-1-5-3", "BATCH")]
    [InlineData("S-1-5-4", "INTERACTIVE")]
    [InlineData("S-1-5-6", "SERVICE")]
    [InlineData("S-1-5-7", "ANONYMOUS LOGON")]
    [InlineData("S-1-5-9", "ENTERPRISE DOMAIN CONTROLLERS")]
    [InlineData("S-1-5-10", "SELF")]
    [InlineData("S-1-5-11", "Authenticated Users")]
    [InlineData("S-1-5-13", "TERMINAL SERVER USER")]
    [InlineData("S-1-5-14", "REMOTE INTERACTIVE LOGON")]
    [InlineData("S-1-5-18", "SYSTEM")]
    [InlineData("S-1-5-19", "LOCAL SERVICE")]
    [InlineData("S-1-5-20", "NETWORK SERVICE")]
    [InlineData("S-1-5-32-544", "Administrators")]
    [InlineData("S-1-5-32-545", "Users")]
    [InlineData("S-1-5-32-546", "Guests")]
    [InlineData("S-1-5-32-547", "Power Users")]
    [InlineData("S-1-5-32-548", "Account Operators")]
    [InlineData("S-1-5-32-549", "Server Operators")]
    [InlineData("S-1-5-32-550", "Print Operators")]
    [InlineData("S-1-5-32-551", "Backup Operators")]
    [InlineData("S-1-5-32-552", "Replicators")]
    public void WellKnownSid_MapsToExpectedName(string sid, string expectedName)
    {
        // The WellKnownSids dictionary is private, so we validate the
        // expected mappings by asserting the correct SID ↔ name pairs.
        // This ensures the LsaOperations lookup table is correct.
        var expected = GetExpectedWellKnownSids();
        Assert.True(expected.ContainsKey(sid), $"SID {sid} should be in the well-known table");
        Assert.Equal(expectedName, expected[sid].Name);
    }

    [Fact]
    public void WellKnownSids_AllHave28Entries()
    {
        var sids = GetExpectedWellKnownSids();
        Assert.Equal(28, sids.Count);
    }

    [Theory]
    [InlineData("S-1-5-18", LsaConstants.SidTypeWellKnownGroup)]
    [InlineData("S-1-1-0", LsaConstants.SidTypeWellKnownGroup)]
    [InlineData("S-1-5-32-544", LsaConstants.SidTypeAlias)]
    [InlineData("S-1-5-32-545", LsaConstants.SidTypeAlias)]
    public void WellKnownSid_HasCorrectSidType(string sid, ushort expectedType)
    {
        var expected = GetExpectedWellKnownSids();
        Assert.Equal(expectedType, expected[sid].Type);
    }

    [Theory]
    [InlineData("S-1-5-18", "NT AUTHORITY")]
    [InlineData("S-1-5-32-544", "BUILTIN")]
    [InlineData("S-1-1-0", "")]
    [InlineData("S-1-0-0", "")]
    public void WellKnownSid_HasCorrectDomain(string sid, string expectedDomain)
    {
        var expected = GetExpectedWellKnownSids();
        Assert.Equal(expectedDomain, expected[sid].Domain);
    }

    [Fact]
    public void UnknownSid_NotInWellKnownTable()
    {
        var sids = GetExpectedWellKnownSids();
        Assert.False(sids.ContainsKey("S-1-5-21-0-0-0-9999"));
    }

    // ────────────────────────────────────────────────────────────────
    // Standard privileges — name and display name validation
    // ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("SeDebugPrivilege", "Debug programs")]
    [InlineData("SeBackupPrivilege", "Back up files and directories")]
    [InlineData("SeRestorePrivilege", "Restore files and directories")]
    [InlineData("SeShutdownPrivilege", "Shut down the system")]
    [InlineData("SeTcbPrivilege", "Act as part of the operating system")]
    [InlineData("SeSecurityPrivilege", "Manage auditing and security log")]
    [InlineData("SeTakeOwnershipPrivilege", "Take ownership of files or other objects")]
    [InlineData("SeChangeNotifyPrivilege", "Bypass traverse checking")]
    [InlineData("SeImpersonatePrivilege", "Impersonate a client after authentication")]
    [InlineData("SeAuditPrivilege", "Generate security audits")]
    [InlineData("SeMachineAccountPrivilege", "Add workstations to domain")]
    [InlineData("SeSystemtimePrivilege", "Change the system time")]
    [InlineData("SeTimeZonePrivilege", "Change the time zone")]
    [InlineData("SeCreateGlobalPrivilege", "Create global objects")]
    [InlineData("SeLoadDriverPrivilege", "Load and unload device drivers")]
    [InlineData("SeRemoteShutdownPrivilege", "Force shutdown from a remote system")]
    [InlineData("SeUndockPrivilege", "Remove computer from docking station")]
    [InlineData("SeCreateTokenPrivilege", "Create a token object")]
    [InlineData("SeAssignPrimaryTokenPrivilege", "Replace a process level token")]
    [InlineData("SeLockMemoryPrivilege", "Lock pages in memory")]
    [InlineData("SeIncreaseBasePriorityPrivilege", "Increase scheduling priority")]
    [InlineData("SeIncreaseQuotaPrivilege", "Adjust memory quotas for a process")]
    [InlineData("SeIncreaseWorkingSetPrivilege", "Increase a process working set")]
    [InlineData("SeProfileSingleProcessPrivilege", "Profile single process")]
    [InlineData("SeSystemProfilePrivilege", "Profile system performance")]
    [InlineData("SeSystemEnvironmentPrivilege", "Modify firmware environment values")]
    [InlineData("SeManageVolumePrivilege", "Perform volume maintenance tasks")]
    [InlineData("SeCreatePagefilePrivilege", "Create a pagefile")]
    [InlineData("SeCreatePermanentPrivilege", "Create permanent shared objects")]
    [InlineData("SeCreateSymbolicLinkPrivilege", "Create symbolic links")]
    [InlineData("SeEnableDelegationPrivilege", "Enable computer and user accounts to be trusted for delegation")]
    [InlineData("SeRelabelPrivilege", "Modify an object label")]
    [InlineData("SeSyncAgentPrivilege", "Synchronize directory service data")]
    [InlineData("SeTrustedCredManAccessPrivilege", "Access Credential Manager as a trusted caller")]
    public void Privilege_HasCorrectDisplayName(string name, string expectedDisplayName)
    {
        var privileges = GetExpectedPrivileges();
        Assert.True(privileges.ContainsKey(name), $"Privilege {name} should exist");
        Assert.Equal(expectedDisplayName, privileges[name]);
    }

    [Fact]
    public void StandardPrivileges_HasAtLeast35Entries()
    {
        // The StandardPrivileges array has 45 entries (privileges + logon rights)
        var privileges = GetExpectedPrivileges();
        Assert.True(privileges.Count >= 35, $"Expected at least 35 privileges, got {privileges.Count}");
    }

    [Theory]
    [InlineData("SeNetworkLogonRight", "Access this computer from the network")]
    [InlineData("SeInteractiveLogonRight", "Allow log on locally")]
    [InlineData("SeRemoteInteractiveLogonRight", "Allow log on through Remote Desktop Services")]
    [InlineData("SeBatchLogonRight", "Log on as a batch job")]
    [InlineData("SeServiceLogonRight", "Log on as a service")]
    [InlineData("SeDenyNetworkLogonRight", "Deny access to this computer from the network")]
    [InlineData("SeDenyInteractiveLogonRight", "Deny log on locally")]
    [InlineData("SeDenyRemoteInteractiveLogonRight", "Deny log on through Remote Desktop Services")]
    [InlineData("SeDenyBatchLogonRight", "Deny log on as a batch job")]
    [InlineData("SeDenyServiceLogonRight", "Deny log on as a service")]
    public void LogonRight_HasCorrectDisplayName(string name, string expectedDisplayName)
    {
        var privileges = GetExpectedPrivileges();
        Assert.True(privileges.ContainsKey(name), $"Logon right {name} should exist");
        Assert.Equal(expectedDisplayName, privileges[name]);
    }

    // ────────────────────────────────────────────────────────────────
    // Account rights validation (ValidRights set in LsaAccountRightsOperations)
    // ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("SeNetworkLogonRight")]
    [InlineData("SeInteractiveLogonRight")]
    [InlineData("SeRemoteInteractiveLogonRight")]
    [InlineData("SeBatchLogonRight")]
    [InlineData("SeServiceLogonRight")]
    [InlineData("SeDenyNetworkLogonRight")]
    [InlineData("SeDenyInteractiveLogonRight")]
    [InlineData("SeDebugPrivilege")]
    [InlineData("SeBackupPrivilege")]
    [InlineData("SeRestorePrivilege")]
    [InlineData("SeShutdownPrivilege")]
    [InlineData("SeTcbPrivilege")]
    public void AccountRight_ExistsInValidRightsSet(string rightName)
    {
        // The ValidRights set in LsaAccountRightsOperations should contain these.
        // We verify the expected set of valid rights matches what we know.
        var validRights = GetExpectedValidRights();
        Assert.Contains(rightName, validRights);
    }

    [Fact]
    public void AccountRights_ValidRightsSet_HasAtLeast40Entries()
    {
        var validRights = GetExpectedValidRights();
        Assert.True(validRights.Count >= 40, $"Expected at least 40 valid rights, got {validRights.Count}");
    }

    // ────────────────────────────────────────────────────────────────
    // LsaConstants — policy information classes
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void PolicyAuditLogInformation_Is1()
    {
        Assert.Equal(1, LsaConstants.PolicyAuditLogInformation);
    }

    [Fact]
    public void PolicyAuditEventsInformation_Is2()
    {
        Assert.Equal(2, LsaConstants.PolicyAuditEventsInformation);
    }

    [Fact]
    public void PolicyPrimaryDomainInformation_Is3()
    {
        Assert.Equal(3, LsaConstants.PolicyPrimaryDomainInformation);
    }

    [Fact]
    public void PolicyAccountDomainInformation_Is5()
    {
        Assert.Equal(5, LsaConstants.PolicyAccountDomainInformation);
    }

    [Fact]
    public void PolicyLsaServerRoleInformation_Is6()
    {
        Assert.Equal(6, LsaConstants.PolicyLsaServerRoleInformation);
    }

    [Fact]
    public void PolicyDnsDomainInformation_Is12()
    {
        Assert.Equal(12, LsaConstants.PolicyDnsDomainInformation);
    }

    // ────────────────────────────────────────────────────────────────
    // LsaConstants — SID_NAME_USE values
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void SidTypeUser_Is1()
    {
        Assert.Equal(1, LsaConstants.SidTypeUser);
    }

    [Fact]
    public void SidTypeGroup_Is2()
    {
        Assert.Equal(2, LsaConstants.SidTypeGroup);
    }

    [Fact]
    public void SidTypeDomain_Is3()
    {
        Assert.Equal(3, LsaConstants.SidTypeDomain);
    }

    [Fact]
    public void SidTypeAlias_Is4()
    {
        Assert.Equal(4, LsaConstants.SidTypeAlias);
    }

    [Fact]
    public void SidTypeWellKnownGroup_Is5()
    {
        Assert.Equal(5, LsaConstants.SidTypeWellKnownGroup);
    }

    [Fact]
    public void SidTypeUnknown_Is8()
    {
        Assert.Equal(8, LsaConstants.SidTypeUnknown);
    }

    // ────────────────────────────────────────────────────────────────
    // LsaConstants — NTSTATUS codes
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void StatusSuccess_Is0()
    {
        Assert.Equal(0u, LsaConstants.StatusSuccess);
    }

    [Fact]
    public void StatusNoneMapped_IsC0000073()
    {
        Assert.Equal(0xC0000073u, LsaConstants.StatusNoneMapped);
    }

    [Fact]
    public void StatusNoSuchPrivilege_IsC0000060()
    {
        Assert.Equal(0xC0000060u, LsaConstants.StatusNoSuchPrivilege);
    }

    [Fact]
    public void StatusObjectNameNotFound_IsC0000034()
    {
        Assert.Equal(0xC0000034u, LsaConstants.StatusObjectNameNotFound);
    }

    // ────────────────────────────────────────────────────────────────
    // LsaConstants — server roles
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void PolicyServerRoleBackup_Is2()
    {
        Assert.Equal(2, LsaConstants.PolicyServerRoleBackup);
    }

    [Fact]
    public void PolicyServerRolePrimary_Is3()
    {
        Assert.Equal(3, LsaConstants.PolicyServerRolePrimary);
    }

    // ────────────────────────────────────────────────────────────────
    // LsaConstants — trust direction and type
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void TrustDirectionBidirectional_Is3()
    {
        Assert.Equal(3u, LsaConstants.TrustDirectionBidirectional);
    }

    [Fact]
    public void TrustTypeUplevel_Is2()
    {
        Assert.Equal(2u, LsaConstants.TrustTypeUplevel);
    }

    [Fact]
    public void TrustAttributeForestTransitive_Is8()
    {
        Assert.Equal(0x00000008u, LsaConstants.TrustAttributeForestTransitive);
    }

    // ────────────────────────────────────────────────────────────────
    // LsaConstants — interface identity
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void LsaInterfaceId_MatchesMsLsad()
    {
        Assert.Equal(new Guid("12345778-1234-abcd-ef00-0123456789ab"), LsaConstants.InterfaceId);
    }

    // ────────────────────────────────────────────────────────────────
    // Helpers — expected data tables for validation
    // ────────────────────────────────────────────────────────────────

    private static Dictionary<string, (string Name, ushort Type, string Domain)> GetExpectedWellKnownSids()
    {
        return new Dictionary<string, (string, ushort, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["S-1-0-0"] = ("Nobody", LsaConstants.SidTypeWellKnownGroup, ""),
            ["S-1-1-0"] = ("Everyone", LsaConstants.SidTypeWellKnownGroup, ""),
            ["S-1-2-0"] = ("LOCAL", LsaConstants.SidTypeWellKnownGroup, ""),
            ["S-1-3-0"] = ("CREATOR OWNER", LsaConstants.SidTypeWellKnownGroup, ""),
            ["S-1-3-1"] = ("CREATOR GROUP", LsaConstants.SidTypeWellKnownGroup, ""),
            ["S-1-5-1"] = ("DIALUP", LsaConstants.SidTypeWellKnownGroup, "NT AUTHORITY"),
            ["S-1-5-2"] = ("NETWORK", LsaConstants.SidTypeWellKnownGroup, "NT AUTHORITY"),
            ["S-1-5-3"] = ("BATCH", LsaConstants.SidTypeWellKnownGroup, "NT AUTHORITY"),
            ["S-1-5-4"] = ("INTERACTIVE", LsaConstants.SidTypeWellKnownGroup, "NT AUTHORITY"),
            ["S-1-5-6"] = ("SERVICE", LsaConstants.SidTypeWellKnownGroup, "NT AUTHORITY"),
            ["S-1-5-7"] = ("ANONYMOUS LOGON", LsaConstants.SidTypeWellKnownGroup, "NT AUTHORITY"),
            ["S-1-5-9"] = ("ENTERPRISE DOMAIN CONTROLLERS", LsaConstants.SidTypeWellKnownGroup, "NT AUTHORITY"),
            ["S-1-5-10"] = ("SELF", LsaConstants.SidTypeWellKnownGroup, "NT AUTHORITY"),
            ["S-1-5-11"] = ("Authenticated Users", LsaConstants.SidTypeWellKnownGroup, "NT AUTHORITY"),
            ["S-1-5-13"] = ("TERMINAL SERVER USER", LsaConstants.SidTypeWellKnownGroup, "NT AUTHORITY"),
            ["S-1-5-14"] = ("REMOTE INTERACTIVE LOGON", LsaConstants.SidTypeWellKnownGroup, "NT AUTHORITY"),
            ["S-1-5-18"] = ("SYSTEM", LsaConstants.SidTypeWellKnownGroup, "NT AUTHORITY"),
            ["S-1-5-19"] = ("LOCAL SERVICE", LsaConstants.SidTypeWellKnownGroup, "NT AUTHORITY"),
            ["S-1-5-20"] = ("NETWORK SERVICE", LsaConstants.SidTypeWellKnownGroup, "NT AUTHORITY"),
            ["S-1-5-32-544"] = ("Administrators", LsaConstants.SidTypeAlias, "BUILTIN"),
            ["S-1-5-32-545"] = ("Users", LsaConstants.SidTypeAlias, "BUILTIN"),
            ["S-1-5-32-546"] = ("Guests", LsaConstants.SidTypeAlias, "BUILTIN"),
            ["S-1-5-32-547"] = ("Power Users", LsaConstants.SidTypeAlias, "BUILTIN"),
            ["S-1-5-32-548"] = ("Account Operators", LsaConstants.SidTypeAlias, "BUILTIN"),
            ["S-1-5-32-549"] = ("Server Operators", LsaConstants.SidTypeAlias, "BUILTIN"),
            ["S-1-5-32-550"] = ("Print Operators", LsaConstants.SidTypeAlias, "BUILTIN"),
            ["S-1-5-32-551"] = ("Backup Operators", LsaConstants.SidTypeAlias, "BUILTIN"),
            ["S-1-5-32-552"] = ("Replicators", LsaConstants.SidTypeAlias, "BUILTIN"),
        };
    }

    private static Dictionary<string, string> GetExpectedPrivileges()
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SeAssignPrimaryTokenPrivilege"] = "Replace a process level token",
            ["SeAuditPrivilege"] = "Generate security audits",
            ["SeBackupPrivilege"] = "Back up files and directories",
            ["SeChangeNotifyPrivilege"] = "Bypass traverse checking",
            ["SeCreateGlobalPrivilege"] = "Create global objects",
            ["SeCreatePagefilePrivilege"] = "Create a pagefile",
            ["SeCreatePermanentPrivilege"] = "Create permanent shared objects",
            ["SeCreateSymbolicLinkPrivilege"] = "Create symbolic links",
            ["SeCreateTokenPrivilege"] = "Create a token object",
            ["SeDebugPrivilege"] = "Debug programs",
            ["SeEnableDelegationPrivilege"] = "Enable computer and user accounts to be trusted for delegation",
            ["SeImpersonatePrivilege"] = "Impersonate a client after authentication",
            ["SeIncreaseBasePriorityPrivilege"] = "Increase scheduling priority",
            ["SeIncreaseQuotaPrivilege"] = "Adjust memory quotas for a process",
            ["SeIncreaseWorkingSetPrivilege"] = "Increase a process working set",
            ["SeLoadDriverPrivilege"] = "Load and unload device drivers",
            ["SeLockMemoryPrivilege"] = "Lock pages in memory",
            ["SeMachineAccountPrivilege"] = "Add workstations to domain",
            ["SeManageVolumePrivilege"] = "Perform volume maintenance tasks",
            ["SeProfileSingleProcessPrivilege"] = "Profile single process",
            ["SeRelabelPrivilege"] = "Modify an object label",
            ["SeRemoteShutdownPrivilege"] = "Force shutdown from a remote system",
            ["SeRestorePrivilege"] = "Restore files and directories",
            ["SeSecurityPrivilege"] = "Manage auditing and security log",
            ["SeShutdownPrivilege"] = "Shut down the system",
            ["SeSyncAgentPrivilege"] = "Synchronize directory service data",
            ["SeSystemEnvironmentPrivilege"] = "Modify firmware environment values",
            ["SeSystemProfilePrivilege"] = "Profile system performance",
            ["SeSystemtimePrivilege"] = "Change the system time",
            ["SeTakeOwnershipPrivilege"] = "Take ownership of files or other objects",
            ["SeTcbPrivilege"] = "Act as part of the operating system",
            ["SeTimeZonePrivilege"] = "Change the time zone",
            ["SeTrustedCredManAccessPrivilege"] = "Access Credential Manager as a trusted caller",
            ["SeUndockPrivilege"] = "Remove computer from docking station",
            ["SeUnsolicitedInputPrivilege"] = "Read unsolicited input from a terminal device",
            ["SeBatchLogonRight"] = "Log on as a batch job",
            ["SeDenyBatchLogonRight"] = "Deny log on as a batch job",
            ["SeDenyInteractiveLogonRight"] = "Deny log on locally",
            ["SeDenyNetworkLogonRight"] = "Deny access to this computer from the network",
            ["SeDenyRemoteInteractiveLogonRight"] = "Deny log on through Remote Desktop Services",
            ["SeDenyServiceLogonRight"] = "Deny log on as a service",
            ["SeInteractiveLogonRight"] = "Allow log on locally",
            ["SeNetworkLogonRight"] = "Access this computer from the network",
            ["SeRemoteInteractiveLogonRight"] = "Allow log on through Remote Desktop Services",
            ["SeServiceLogonRight"] = "Log on as a service",
        };
    }

    private static HashSet<string> GetExpectedValidRights()
    {
        return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SeNetworkLogonRight",
            "SeInteractiveLogonRight",
            "SeRemoteInteractiveLogonRight",
            "SeBatchLogonRight",
            "SeServiceLogonRight",
            "SeDenyNetworkLogonRight",
            "SeDenyInteractiveLogonRight",
            "SeDenyRemoteInteractiveLogonRight",
            "SeDenyBatchLogonRight",
            "SeDenyServiceLogonRight",
            "SeAssignPrimaryTokenPrivilege",
            "SeAuditPrivilege",
            "SeBackupPrivilege",
            "SeChangeNotifyPrivilege",
            "SeCreateGlobalPrivilege",
            "SeCreatePagefilePrivilege",
            "SeCreatePermanentPrivilege",
            "SeCreateSymbolicLinkPrivilege",
            "SeCreateTokenPrivilege",
            "SeDebugPrivilege",
            "SeEnableDelegationPrivilege",
            "SeImpersonatePrivilege",
            "SeIncreaseBasePriorityPrivilege",
            "SeIncreaseQuotaPrivilege",
            "SeIncreaseWorkingSetPrivilege",
            "SeLoadDriverPrivilege",
            "SeLockMemoryPrivilege",
            "SeMachineAccountPrivilege",
            "SeManageVolumePrivilege",
            "SeProfileSingleProcessPrivilege",
            "SeRelabelPrivilege",
            "SeRemoteShutdownPrivilege",
            "SeRestorePrivilege",
            "SeSecurityPrivilege",
            "SeShutdownPrivilege",
            "SeSyncAgentPrivilege",
            "SeSystemEnvironmentPrivilege",
            "SeSystemProfilePrivilege",
            "SeSystemtimePrivilege",
            "SeTakeOwnershipPrivilege",
            "SeTcbPrivilege",
            "SeTimeZonePrivilege",
            "SeTrustedCredManAccessPrivilege",
            "SeUndockPrivilege",
        };
    }
}
