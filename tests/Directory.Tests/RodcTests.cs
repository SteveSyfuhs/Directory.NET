using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Replication;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Directory.Tests;

/// <summary>
/// Tests for the Read-Only Domain Controller (RODC) Password Replication Policy
/// and credential caching via RodcService.
/// </summary>
public class RodcTests
{
    private const string TenantId = "default";
    private const string DomainDn = "DC=corp,DC=contoso,DC=com";

    private readonly InMemoryDirectoryStore _store;
    private readonly RodcService _rodc;

    public RodcTests()
    {
        _store = new InMemoryDirectoryStore();

        var dcInfo = new DcInstanceInfo();
        var ncService = new StubNamingContextService(DomainDn);

        _rodc = new RodcService(
            _store,
            ncService,
            dcInfo,
            NullLogger<RodcService>.Instance);
    }

    // ────────────────────────────────────────────────────────────────
    // RODC mode
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void IsRodc_DefaultsToFalse()
    {
        Assert.False(_rodc.IsRodc);
    }

    [Fact]
    public void SetRodcMode_EnablesRodc()
    {
        _rodc.IsRodc = true;
        Assert.True(_rodc.IsRodc);
    }

    // ────────────────────────────────────────────────────────────────
    // Password Replication Policy (PRP)
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void AllowedList_AllowsSpecifiedAccounts()
    {
        var userDn = $"CN=User1,CN=Users,{DomainDn}";
        _rodc.AddToAllowedList(userDn);

        Assert.True(_rodc.IsPasswordCacheable(userDn));
    }

    [Fact]
    public void DeniedList_BlocksSpecifiedAccounts()
    {
        var userDn = $"CN=Admin1,CN=Users,{DomainDn}";
        _rodc.AddToDeniedList(userDn);

        Assert.False(_rodc.IsPasswordCacheable(userDn));
    }

    [Fact]
    public void DenyOverridesAllow()
    {
        var userDn = $"CN=ConflictUser,CN=Users,{DomainDn}";

        // Add to both lists — deny should win
        _rodc.AddToAllowedList(userDn);
        _rodc.AddToDeniedList(userDn);

        Assert.False(_rodc.IsPasswordCacheable(userDn));
    }

    [Fact]
    public void DefaultDeny_AccountNotInEitherList()
    {
        var userDn = $"CN=UnknownUser,CN=Users,{DomainDn}";

        // Not in either list — default deny
        Assert.False(_rodc.IsPasswordCacheable(userDn));
    }

    // ────────────────────────────────────────────────────────────────
    // Credential caching
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PrecachePassword_AddsToCacheWhenAllowed()
    {
        _rodc.IsRodc = true;

        var userDn = $"CN=CacheUser,CN=Users,{DomainDn}";
        _rodc.AddToAllowedList(userDn);

        // Create the account in the store so PrecachePasswordAsync finds it
        var user = CreateUserObject(userDn, "CacheUser");
        await _store.CreateAsync(user);

        var result = await _rodc.PrecachePasswordAsync(userDn, TenantId);

        Assert.True(result);
        Assert.True(_rodc.IsCredentialCached(userDn));
    }

    [Fact]
    public async Task GetCachedPasswords_ReturnsCachedAccounts()
    {
        _rodc.IsRodc = true;

        var userDn1 = $"CN=User1,CN=Users,{DomainDn}";
        var userDn2 = $"CN=User2,CN=Users,{DomainDn}";
        _rodc.AddToAllowedList(userDn1);
        _rodc.AddToAllowedList(userDn2);

        await _store.CreateAsync(CreateUserObject(userDn1, "User1"));
        await _store.CreateAsync(CreateUserObject(userDn2, "User2"));

        await _rodc.PrecachePasswordAsync(userDn1, TenantId);
        await _rodc.PrecachePasswordAsync(userDn2, TenantId);

        var cached = _rodc.GetCachedPasswords();
        Assert.Equal(2, cached.Count);

        var cachedDns = cached.Select(c => c.AccountDn).ToList();
        Assert.Contains(userDn1, cachedDns);
        Assert.Contains(userDn2, cachedDns);
    }

    [Fact]
    public void IsPasswordCacheable_ChecksPrp()
    {
        var allowedUser = $"CN=AllowedUser,CN=Users,{DomainDn}";
        var deniedUser = $"CN=DeniedUser,CN=Users,{DomainDn}";
        var unknownUser = $"CN=UnknownUser,CN=Users,{DomainDn}";

        _rodc.AddToAllowedList(allowedUser);
        _rodc.AddToDeniedList(deniedUser);

        Assert.True(_rodc.IsPasswordCacheable(allowedUser));
        Assert.False(_rodc.IsPasswordCacheable(deniedUser));
        Assert.False(_rodc.IsPasswordCacheable(unknownUser));
    }

    // ────────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────────

    private static DirectoryObject CreateUserObject(string dn, string samAccountName)
    {
        var now = DateTimeOffset.UtcNow;
        return new DirectoryObject
        {
            Id = dn.ToLowerInvariant(),
            TenantId = TenantId,
            DomainDn = DomainDn,
            DistinguishedName = dn,
            ObjectGuid = Guid.NewGuid().ToString(),
            SAMAccountName = samAccountName,
            Cn = samAccountName,
            ObjectClass = ["top", "person", "organizationalPerson", "user"],
            ParentDn = $"CN=Users,{DomainDn}",
            NTHash = "E52CAC67419A9A224A3B108F3FA6CB6D", // dummy hash for credential caching tests
            WhenCreated = now,
            WhenChanged = now,
        };
    }
}

/// <summary>
/// Stub naming context service for RODC tests.
/// </summary>
internal class StubNamingContextService : INamingContextService
{
    private readonly string _domainDn;

    public StubNamingContextService(string domainDn) => _domainDn = domainDn;

    public NamingContext GetDomainNc() => new()
    {
        Type = NamingContextType.Domain,
        Dn = _domainDn,
        DnsName = "corp.contoso.com",
    };

    public NamingContext GetConfigurationNc() => new()
    {
        Type = NamingContextType.Configuration,
        Dn = $"CN=Configuration,{_domainDn}",
        DnsName = "corp.contoso.com",
    };

    public NamingContext GetSchemaNc() => new()
    {
        Type = NamingContextType.Schema,
        Dn = $"CN=Schema,CN=Configuration,{_domainDn}",
        DnsName = "corp.contoso.com",
    };

    public NamingContext GetNamingContext(string dn) => null;
    public IReadOnlyList<NamingContext> GetAllNamingContexts() => [GetDomainNc()];
    public bool IsDnInNamingContext(string dn, NamingContextType type) => false;
    public string GetConfigurationDn() => $"CN=Configuration,{_domainDn}";
    public string GetSchemaDn() => $"CN=Schema,CN=Configuration,{_domainDn}";
    public void Reconfigure(string domainDn, string forestDnsName, string domainSid = null) { }
}
