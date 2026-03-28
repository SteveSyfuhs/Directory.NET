using Directory.Replication.Drsr;
using Xunit;

namespace Directory.Tests;

/// <summary>
/// Tests for FSMO (Flexible Single-Master Operations) role management,
/// including the five standard roles, role object DN resolution, and
/// the FsmoTransferResult model.
/// </summary>
public class FsmoRoleTests
{
    // ────────────────────────────────────────────────────────────────
    // Five FSMO roles exist
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void FsmoRole_Enum_HasExactlyFiveValues()
    {
        var values = Enum.GetValues<FsmoRole>();
        Assert.Equal(5, values.Length);
    }

    [Fact]
    public void FsmoRole_ContainsSchemaMaster()
    {
        Assert.Contains(FsmoRole.SchemaMaster, Enum.GetValues<FsmoRole>());
    }

    [Fact]
    public void FsmoRole_ContainsDomainNamingMaster()
    {
        Assert.Contains(FsmoRole.DomainNamingMaster, Enum.GetValues<FsmoRole>());
    }

    [Fact]
    public void FsmoRole_ContainsPdcEmulator()
    {
        Assert.Contains(FsmoRole.PdcEmulator, Enum.GetValues<FsmoRole>());
    }

    [Fact]
    public void FsmoRole_ContainsRidMaster()
    {
        Assert.Contains(FsmoRole.RidMaster, Enum.GetValues<FsmoRole>());
    }

    [Fact]
    public void FsmoRole_ContainsInfrastructureMaster()
    {
        Assert.Contains(FsmoRole.InfrastructureMaster, Enum.GetValues<FsmoRole>());
    }

    // ────────────────────────────────────────────────────────────────
    // GetRoleObjectDn — maps each role to its well-known DN
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void GetRoleObjectDn_PdcEmulator_ReturnsDomainDn()
    {
        var dn = FsmoRoleManager.GetRoleObjectDn(FsmoRole.PdcEmulator, "DC=corp,DC=com");
        Assert.Equal("DC=corp,DC=com", dn);
    }

    [Fact]
    public void GetRoleObjectDn_RidMaster_ReturnsRidManagerDn()
    {
        var dn = FsmoRoleManager.GetRoleObjectDn(FsmoRole.RidMaster, "DC=corp,DC=com");
        Assert.Equal("CN=RID Manager$,CN=System,DC=corp,DC=com", dn);
    }

    [Fact]
    public void GetRoleObjectDn_InfrastructureMaster_ReturnsInfrastructureDn()
    {
        var dn = FsmoRoleManager.GetRoleObjectDn(FsmoRole.InfrastructureMaster, "DC=corp,DC=com");
        Assert.Equal("CN=Infrastructure,DC=corp,DC=com", dn);
    }

    [Fact]
    public void GetRoleObjectDn_SchemaMaster_ReturnsSchemaDn()
    {
        var dn = FsmoRoleManager.GetRoleObjectDn(FsmoRole.SchemaMaster, "DC=corp,DC=com");
        Assert.Equal("CN=Schema,CN=Configuration,DC=corp,DC=com", dn);
    }

    [Fact]
    public void GetRoleObjectDn_DomainNamingMaster_ReturnsPartitionsDn()
    {
        var dn = FsmoRoleManager.GetRoleObjectDn(FsmoRole.DomainNamingMaster, "DC=corp,DC=com");
        Assert.Equal("CN=Partitions,CN=Configuration,DC=corp,DC=com", dn);
    }

    // ────────────────────────────────────────────────────────────────
    // GetRoleObjectDn — different domain DNs
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void GetRoleObjectDn_DifferentDomain_ProducesCorrectDn()
    {
        var dn = FsmoRoleManager.GetRoleObjectDn(FsmoRole.RidMaster, "DC=example,DC=org");
        Assert.Equal("CN=RID Manager$,CN=System,DC=example,DC=org", dn);
    }

    [Fact]
    public void GetRoleObjectDn_MultiLevelDomain_ProducesCorrectDn()
    {
        var dn = FsmoRoleManager.GetRoleObjectDn(FsmoRole.SchemaMaster, "DC=sub,DC=corp,DC=com");
        Assert.Equal("CN=Schema,CN=Configuration,DC=sub,DC=corp,DC=com", dn);
    }

    // ────────────────────────────────────────────────────────────────
    // GetRoleObjectDn — invalid role
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void GetRoleObjectDn_InvalidRole_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            FsmoRoleManager.GetRoleObjectDn((FsmoRole)999, "DC=corp,DC=com"));
    }

    // ────────────────────────────────────────────────────────────────
    // Role holder comparison (DN-based)
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void RoleHolderComparison_SameDn_AreEqual()
    {
        var ntdsDn1 = "CN=NTDS Settings,CN=DC1,CN=Servers,CN=Default-First-Site-Name,CN=Sites,CN=Configuration,DC=corp,DC=com";
        var ntdsDn2 = "CN=NTDS Settings,CN=DC1,CN=Servers,CN=Default-First-Site-Name,CN=Sites,CN=Configuration,DC=corp,DC=com";

        Assert.Equal(ntdsDn1, ntdsDn2, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void RoleHolderComparison_DifferentCase_AreEqualOrdinalIgnoreCase()
    {
        var ntdsDn1 = "CN=NTDS Settings,CN=DC1,CN=Servers,CN=Default-First-Site-Name,CN=Sites,CN=Configuration,DC=CORP,DC=COM";
        var ntdsDn2 = "cn=ntds settings,cn=dc1,cn=servers,cn=default-first-site-name,cn=sites,cn=configuration,dc=corp,dc=com";

        Assert.Equal(ntdsDn1, ntdsDn2, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void RoleHolderComparison_DifferentDC_AreNotEqual()
    {
        var ntdsDn1 = "CN=NTDS Settings,CN=DC1,CN=Servers,CN=Default-First-Site-Name,CN=Sites,CN=Configuration,DC=corp,DC=com";
        var ntdsDn2 = "CN=NTDS Settings,CN=DC2,CN=Servers,CN=Default-First-Site-Name,CN=Sites,CN=Configuration,DC=corp,DC=com";

        Assert.False(string.Equals(ntdsDn1, ntdsDn2, StringComparison.OrdinalIgnoreCase));
    }

    // ────────────────────────────────────────────────────────────────
    // FsmoTransferResult model
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void FsmoTransferResult_SuccessfulTransfer_HasExpectedProperties()
    {
        var result = new FsmoTransferResult
        {
            Success = true,
            PreviousHolder = "CN=NTDS Settings,CN=DC1,...",
            NewHolder = "CN=NTDS Settings,CN=DC2,...",
            WasSeized = false,
        };

        Assert.True(result.Success);
        Assert.NotNull(result.PreviousHolder);
        Assert.NotNull(result.NewHolder);
        Assert.False(result.WasSeized);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void FsmoTransferResult_FailedTransfer_HasErrorMessage()
    {
        var result = new FsmoTransferResult
        {
            Success = false,
            ErrorMessage = "FSMO role object not found",
        };

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.Null(result.NewHolder);
    }

    [Fact]
    public void FsmoTransferResult_Seizure_SetsWasSeized()
    {
        var result = new FsmoTransferResult
        {
            Success = true,
            NewHolder = "CN=NTDS Settings,CN=DC3,...",
            WasSeized = true,
        };

        Assert.True(result.WasSeized);
        Assert.True(result.Success);
    }

    // ────────────────────────────────────────────────────────────────
    // All five roles map to different DNs for the same domain
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void AllFsmoRoles_MapToDistinctDns()
    {
        var domainDn = "DC=corp,DC=com";
        var dns = Enum.GetValues<FsmoRole>()
            .Select(r => FsmoRoleManager.GetRoleObjectDn(r, domainDn))
            .ToList();

        // All five should be distinct
        Assert.Equal(5, dns.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    // ────────────────────────────────────────────────────────────────
    // Forest-wide vs domain-wide roles
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void ForestWideRoles_AreSchemaMasterAndDomainNamingMaster()
    {
        // Forest-wide roles are stored under CN=Configuration
        var domainDn = "DC=corp,DC=com";

        var schemaDn = FsmoRoleManager.GetRoleObjectDn(FsmoRole.SchemaMaster, domainDn);
        var namingDn = FsmoRoleManager.GetRoleObjectDn(FsmoRole.DomainNamingMaster, domainDn);

        Assert.Contains("CN=Configuration", schemaDn);
        Assert.Contains("CN=Configuration", namingDn);
    }

    [Fact]
    public void DomainWideRoles_DoNotContainConfigurationContainer()
    {
        var domainDn = "DC=corp,DC=com";

        var pdcDn = FsmoRoleManager.GetRoleObjectDn(FsmoRole.PdcEmulator, domainDn);
        var ridDn = FsmoRoleManager.GetRoleObjectDn(FsmoRole.RidMaster, domainDn);
        var infraDn = FsmoRoleManager.GetRoleObjectDn(FsmoRole.InfrastructureMaster, domainDn);

        // PDC Emulator is just the domain DN itself, so it doesn't have "Configuration"
        Assert.DoesNotContain("CN=Configuration", pdcDn);
        Assert.DoesNotContain("CN=Configuration", ridDn);
        Assert.DoesNotContain("CN=Configuration", infraDn);
    }
}
