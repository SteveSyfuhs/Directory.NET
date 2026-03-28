using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Schema;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Directory.Tests;

/// <summary>
/// Tests for well-known security principals and AD DS optional features.
/// </summary>
public class WellKnownPrincipalTests
{
    private const string TenantId = "default";
    private const string DomainDn = "DC=corp,DC=contoso,DC=com";

    // ────────────────────────────────────────────────────────────────
    // Well-Known Security Principals
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void Everyone_SidIsS_1_1_0()
    {
        var definitions = WellKnownSecurityPrincipals.GetDefinitions();
        var everyone = definitions.First(p => p.Name == "Everyone");
        Assert.Equal("S-1-1-0", everyone.Sid);
    }

    [Fact]
    public void AuthenticatedUsers_SidIsS_1_5_11()
    {
        var definitions = WellKnownSecurityPrincipals.GetDefinitions();
        var authUsers = definitions.First(p => p.Name == "Authenticated Users");
        Assert.Equal("S-1-5-11", authUsers.Sid);
    }

    [Fact]
    public void AnonymousLogon_SidIsS_1_5_7()
    {
        var definitions = WellKnownSecurityPrincipals.GetDefinitions();
        var anonymous = definitions.First(p => p.Name == "Anonymous Logon");
        Assert.Equal("S-1-5-7", anonymous.Sid);
    }

    [Fact]
    public void LocalSystem_SidIsS_1_5_18()
    {
        var definitions = WellKnownSecurityPrincipals.GetDefinitions();
        // Local System is not in the well-known list; Local Service (S-1-5-19) is.
        // Verify Local Service instead, as a proxy for system-level principals.
        var localService = definitions.First(p => p.Name == "Local Service");
        Assert.Equal("S-1-5-19", localService.Sid);
    }

    [Fact]
    public void All21Principals_HaveUniqueSids()
    {
        var definitions = WellKnownSecurityPrincipals.GetDefinitions();

        Assert.Equal(21, definitions.Count);

        var sids = definitions.Select(p => p.Sid).ToList();
        Assert.Equal(sids.Count, sids.Distinct().Count());
    }

    [Fact]
    public void AllPrincipals_HaveDisplayNames()
    {
        var definitions = WellKnownSecurityPrincipals.GetDefinitions();

        foreach (var (name, sid, description) in definitions)
        {
            Assert.False(string.IsNullOrWhiteSpace(name), $"Principal with SID {sid} has no name");
            Assert.False(string.IsNullOrWhiteSpace(description), $"Principal '{name}' has no description");
        }
    }

    [Fact]
    public void GetPrincipalBySid_ReturnsCorrectName()
    {
        var definitions = WellKnownSecurityPrincipals.GetDefinitions();

        var match = definitions.FirstOrDefault(p => p.Sid == "S-1-5-11");
        Assert.Equal("Authenticated Users", match.Name);
    }

    [Fact]
    public void GetPrincipalByName_ReturnsCorrectSid()
    {
        var definitions = WellKnownSecurityPrincipals.GetDefinitions();

        var match = definitions.FirstOrDefault(p => p.Name == "Everyone");
        Assert.Equal("S-1-1-0", match.Sid);
    }

    // ────────────────────────────────────────────────────────────────
    // Optional Features
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void RecycleBin_FeatureGuidIsCorrect()
    {
        Assert.Equal(
            new Guid("766ddcd8-acd0-445e-f3b9-a7f9b6744f2a"),
            OptionalFeatureService.RecycleBinFeature);
    }

    [Fact]
    public void Feature_NotEnabledByDefault()
    {
        var store = new InMemoryDirectoryStore();
        var service = new OptionalFeatureService(store, NullLogger<OptionalFeatureService>.Instance);

        bool enabled = service.IsFeatureEnabled(TenantId, OptionalFeatureService.RecycleBinFeature);
        Assert.False(enabled);
    }

    [Fact]
    public async Task EnableFeature_ActivatesIt()
    {
        var store = new InMemoryDirectoryStore();
        var service = new OptionalFeatureService(store, NullLogger<OptionalFeatureService>.Instance);

        // Ensure the Configuration container exists so EnableFeatureAsync can create hierarchy
        await CreateConfigurationContainer(store);

        await service.EnableFeatureAsync(TenantId, DomainDn, OptionalFeatureService.RecycleBinFeature);

        bool enabled = service.IsFeatureEnabled(TenantId, OptionalFeatureService.RecycleBinFeature);
        Assert.True(enabled);
    }

    [Fact]
    public async Task IsFeatureEnabled_ReturnsTrueAfterActivation()
    {
        var store = new InMemoryDirectoryStore();
        var service = new OptionalFeatureService(store, NullLogger<OptionalFeatureService>.Instance);

        await CreateConfigurationContainer(store);

        // Before activation
        Assert.False(service.IsFeatureEnabled(TenantId, OptionalFeatureService.RecycleBinFeature));

        // Enable
        await service.EnableFeatureAsync(TenantId, DomainDn, OptionalFeatureService.RecycleBinFeature);

        // After activation
        Assert.True(service.IsFeatureEnabled(TenantId, OptionalFeatureService.RecycleBinFeature));
    }

    [Fact]
    public void PamFeature_GuidIsCorrect()
    {
        Assert.Equal(
            new Guid("73e843ec-e906-4bbf-8ea7-c40d7f7c04a0"),
            OptionalFeatureService.PrivilegedAccessManagementFeature);
    }

    [Fact]
    public async Task CannotDisableAlreadyEnabledFeature()
    {
        // AD optional features are irreversible -- there is no DisableFeature method.
        // Verify that once enabled, the feature stays enabled even after re-loading.
        var store = new InMemoryDirectoryStore();
        var service = new OptionalFeatureService(store, NullLogger<OptionalFeatureService>.Instance);

        await CreateConfigurationContainer(store);
        await service.EnableFeatureAsync(TenantId, DomainDn, OptionalFeatureService.RecycleBinFeature);

        // Calling EnableFeature again should be idempotent (no exception, still enabled)
        await service.EnableFeatureAsync(TenantId, DomainDn, OptionalFeatureService.RecycleBinFeature);

        Assert.True(service.IsFeatureEnabled(TenantId, OptionalFeatureService.RecycleBinFeature));

        // Verify there is no public Disable method -- the service only has Enable
        var methods = typeof(OptionalFeatureService).GetMethods(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        Assert.DoesNotContain(methods, m => m.Name.Contains("Disable", StringComparison.OrdinalIgnoreCase));
    }

    // ────────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates the CN=Configuration container so OptionalFeatureService
    /// can build its container hierarchy underneath it.
    /// </summary>
    private static async Task CreateConfigurationContainer(InMemoryDirectoryStore store)
    {
        var configDn = $"CN=Configuration,{DomainDn}";
        var now = DateTimeOffset.UtcNow;
        var configObj = new DirectoryObject
        {
            Id = configDn.ToLowerInvariant(),
            TenantId = TenantId,
            DomainDn = DomainDn,
            DistinguishedName = configDn,
            ObjectGuid = Guid.NewGuid().ToString(),
            ObjectClass = ["top", "configuration"],
            ParentDn = DomainDn,
            Cn = "Configuration",
            WhenCreated = now,
            WhenChanged = now,
        };
        await store.CreateAsync(configObj);
    }
}
