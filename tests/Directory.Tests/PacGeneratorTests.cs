using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Kerberos;
using Directory.Security.Claims;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Directory.Tests;

public class PacGeneratorTests
{
    private readonly PacGenerator _generator;
    private readonly InMemoryDirectoryStore _store;
    private readonly StubClaimsProvider _claimsProvider;

    public PacGeneratorTests()
    {
        _store = new InMemoryDirectoryStore();
        _claimsProvider = new StubClaimsProvider();
        _generator = new PacGenerator(
            _store,
            _claimsProvider,
            NullLogger<PacGenerator>.Instance);
    }

    [Fact]
    public async Task GenerateAsync_SetsLogonInfoUserNameAndDomain()
    {
        // Arrange
        var user = CreateTestUser("jdoe", "S-1-5-21-1234567890-1234567890-1234567890-1001");
        var domainDn = "DC=corp,DC=com";

        // Act
        var pac = await _generator.GenerateAsync(user, domainDn, "default");

        // Assert
        Assert.NotNull(pac.LogonInfo);
        Assert.Equal("jdoe", pac.LogonInfo.UserName);
        Assert.Equal("CORP", pac.LogonInfo.DomainName);
    }

    [Fact]
    public async Task GenerateAsync_SetsCorrectUserRid()
    {
        // Arrange
        var user = CreateTestUser("jdoe", "S-1-5-21-1234567890-1234567890-1234567890-1001");
        var domainDn = "DC=corp,DC=com";

        // Act
        var pac = await _generator.GenerateAsync(user, domainDn, "default");

        // Assert
        Assert.Equal(1001u, pac.LogonInfo.UserId);
    }

    [Fact]
    public async Task GenerateAsync_IncludesGroupSidsFromMembership()
    {
        // Arrange
        var user = CreateTestUser("jdoe", "S-1-5-21-100-200-300-1001");
        user.MemberOf.Add("CN=Developers,DC=corp,DC=com");

        var group = new DirectoryObject
        {
            Id = "cn=developers,dc=corp,dc=com",
            TenantId = "default",
            DistinguishedName = "CN=Developers,DC=corp,DC=com",
            ObjectClass = ["top", "group"],
            ObjectCategory = "group",
            Cn = "Developers",
            ObjectSid = "S-1-5-21-100-200-300-1100",
            DomainDn = "DC=corp,DC=com",
        };
        _store.Add(group);

        var domainDn = "DC=corp,DC=com";

        // Act
        var pac = await _generator.GenerateAsync(user, domainDn, "default");

        // Assert
        Assert.NotNull(pac.LogonInfo.GroupIds);
        Assert.Contains(pac.LogonInfo.GroupIds, g => g.RelativeId == 1100);
    }

    [Fact]
    public async Task GenerateAsync_IncludesPrimaryGroupInGroupIds()
    {
        // Arrange
        var user = CreateTestUser("jdoe", "S-1-5-21-100-200-300-1001");
        user.PrimaryGroupId = 513; // Domain Users

        var domainDn = "DC=corp,DC=com";

        // Act
        var pac = await _generator.GenerateAsync(user, domainDn, "default");

        // Assert
        Assert.NotNull(pac.LogonInfo.GroupIds);
        Assert.Contains(pac.LogonInfo.GroupIds, g => g.RelativeId == 513);
    }

    [Fact]
    public async Task GenerateAsync_ClientClaimsPopulatedWhenClaimsExist()
    {
        // Arrange
        var user = CreateTestUser("jdoe", "S-1-5-21-100-200-300-1001");
        _claimsProvider.ClaimsToReturn = new ClaimsSet
        {
            ClaimsArrays =
            [
                new ClaimsArray
                {
                    ClaimSourceType = ClaimSourceType.AD,
                    ClaimEntries =
                    [
                        new ClaimEntry
                        {
                            Id = "ad://ext/department",
                            Type = ClaimValueType.STRING,
                            StringValues = ["Engineering"],
                        }
                    ]
                }
            ]
        };

        var domainDn = "DC=corp,DC=com";

        // Act
        var pac = await _generator.GenerateAsync(user, domainDn, "default");

        // Assert
        Assert.NotNull(pac.ClientClaims);
        Assert.NotNull(pac.ClientClaims.ClaimsSet);
        Assert.NotEmpty(pac.ClientClaims.ClaimsSet.ClaimsArray);
    }

    [Fact]
    public async Task GenerateAsync_ClientClaimsNullWhenNoClaimsExist()
    {
        // Arrange
        var user = CreateTestUser("jdoe", "S-1-5-21-100-200-300-1001");
        _claimsProvider.ClaimsToReturn = new ClaimsSet(); // Empty claims

        var domainDn = "DC=corp,DC=com";

        // Act
        var pac = await _generator.GenerateAsync(user, domainDn, "default");

        // Assert
        Assert.Null(pac.ClientClaims);
    }

    [Fact]
    public async Task GenerateAsync_ContainsServerAndKdcSignaturePlaceholders()
    {
        // Arrange
        var user = CreateTestUser("jdoe", "S-1-5-21-100-200-300-1001");
        var domainDn = "DC=corp,DC=com";

        // Act
        var pac = await _generator.GenerateAsync(user, domainDn, "default");

        // Assert
        Assert.NotNull(pac.ServerSignature);
        Assert.NotNull(pac.KdcSignature);
    }

    [Fact]
    public async Task GenerateAsync_UpnDomainInfoHasCorrectUpnAndDnsDomain()
    {
        // Arrange
        var user = CreateTestUser("jdoe", "S-1-5-21-100-200-300-1001");
        user.UserPrincipalName = "jdoe@corp.com";
        var domainDn = "DC=corp,DC=com";

        // Act
        var pac = await _generator.GenerateAsync(user, domainDn, "default");

        // Assert
        Assert.NotNull(pac.UpnDomainInformation);
        Assert.Equal("jdoe@corp.com", pac.UpnDomainInformation.Upn);
        Assert.Equal("corp.com", pac.UpnDomainInformation.Domain);
    }

    [Fact]
    public async Task GenerateAsync_NoUpn_UpnDomainInfoFallsBackToSamAccountName()
    {
        // Arrange
        var user = CreateTestUser("jdoe", "S-1-5-21-100-200-300-1001");
        user.UserPrincipalName = null;
        var domainDn = "DC=corp,DC=com";

        // Act
        var pac = await _generator.GenerateAsync(user, domainDn, "default");

        // Assert — UPN_DNS_INFO is always populated; falls back to sAMAccountName@domain
        Assert.NotNull(pac.UpnDomainInformation);
        Assert.Contains("jdoe", pac.UpnDomainInformation.Upn);
        Assert.Equal("corp.com", pac.UpnDomainInformation.Domain);
    }

    [Fact]
    public async Task GenerateAsync_EmptyGroupList_ProducesValidPac()
    {
        // Arrange
        var user = CreateTestUser("jdoe", "S-1-5-21-100-200-300-1001");
        user.PrimaryGroupId = 0; // No primary group
        user.MemberOf.Clear();
        var domainDn = "DC=corp,DC=com";

        // Act
        var pac = await _generator.GenerateAsync(user, domainDn, "default");

        // Assert
        Assert.NotNull(pac);
        Assert.NotNull(pac.LogonInfo);
        Assert.NotNull(pac.LogonInfo.GroupIds);
    }

    [Fact]
    public async Task GenerateAsync_SetsClientInformation()
    {
        // Arrange
        var user = CreateTestUser("jdoe", "S-1-5-21-100-200-300-1001");
        var domainDn = "DC=corp,DC=com";

        // Act
        var pac = await _generator.GenerateAsync(user, domainDn, "default");

        // Assert
        Assert.NotNull(pac.ClientInformation);
        Assert.Equal("jdoe", pac.ClientInformation.Name);
    }

    [Fact]
    public async Task GenerateAsync_ExpandsNestedGroups()
    {
        // Arrange
        var user = CreateTestUser("jdoe", "S-1-5-21-100-200-300-1001");
        user.MemberOf.Add("CN=TeamA,DC=corp,DC=com");

        var teamA = new DirectoryObject
        {
            Id = "cn=teama,dc=corp,dc=com",
            TenantId = "default",
            DistinguishedName = "CN=TeamA,DC=corp,DC=com",
            ObjectClass = ["top", "group"],
            ObjectCategory = "group",
            Cn = "TeamA",
            ObjectSid = "S-1-5-21-100-200-300-1100",
            DomainDn = "DC=corp,DC=com",
            MemberOf = ["CN=AllTeams,DC=corp,DC=com"],
        };

        var allTeams = new DirectoryObject
        {
            Id = "cn=allteams,dc=corp,dc=com",
            TenantId = "default",
            DistinguishedName = "CN=AllTeams,DC=corp,DC=com",
            ObjectClass = ["top", "group"],
            ObjectCategory = "group",
            Cn = "AllTeams",
            ObjectSid = "S-1-5-21-100-200-300-1200",
            DomainDn = "DC=corp,DC=com",
        };

        _store.Add(teamA);
        _store.Add(allTeams);

        var domainDn = "DC=corp,DC=com";

        // Act
        var pac = await _generator.GenerateAsync(user, domainDn, "default");

        // Assert
        Assert.NotNull(pac.LogonInfo.GroupIds);
        Assert.Contains(pac.LogonInfo.GroupIds, g => g.RelativeId == 1100);
        Assert.Contains(pac.LogonInfo.GroupIds, g => g.RelativeId == 1200);
    }

    [Fact]
    public async Task GenerateAsync_DisplayNameSetInLogonInfo()
    {
        // Arrange
        var user = CreateTestUser("jdoe", "S-1-5-21-100-200-300-1001");
        user.DisplayName = "John Doe";
        var domainDn = "DC=corp,DC=com";

        // Act
        var pac = await _generator.GenerateAsync(user, domainDn, "default");

        // Assert
        Assert.Equal("John Doe", pac.LogonInfo.UserDisplayName);
    }

    [Fact]
    public async Task GenerateAsync_DomainSidParsedFromUserSid()
    {
        // Arrange
        var user = CreateTestUser("jdoe", "S-1-5-21-100-200-300-1001");
        user.MemberOf.Add("CN=Group1,DC=corp,DC=com");

        var group = new DirectoryObject
        {
            Id = "cn=group1,dc=corp,dc=com",
            TenantId = "default",
            DistinguishedName = "CN=Group1,DC=corp,DC=com",
            ObjectClass = ["top", "group"],
            ObjectCategory = "group",
            Cn = "Group1",
            ObjectSid = "S-1-5-21-100-200-300-2001",
            DomainDn = "DC=corp,DC=com",
        };
        _store.Add(group);

        var domainDn = "DC=corp,DC=com";

        // Act
        var pac = await _generator.GenerateAsync(user, domainDn, "default");

        // Assert
        // Group from same domain should appear in GroupIds (domain-relative)
        Assert.Contains(pac.LogonInfo.GroupIds, g => g.RelativeId == 2001);
    }

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
        };
    }
}

/// <summary>
/// Minimal in-memory directory store for unit testing.
/// Only implements the methods needed by the code under test.
/// </summary>
internal class InMemoryDirectoryStore : IDirectoryStore
{
    private readonly Dictionary<string, DirectoryObject> _byDn = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DirectoryObject> _bySam = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DirectoryObject> _byUpn = new(StringComparer.OrdinalIgnoreCase);

    public void Add(DirectoryObject obj)
    {
        _byDn[obj.DistinguishedName] = obj;
        if (!string.IsNullOrEmpty(obj.SAMAccountName))
            _bySam[$"{obj.DomainDn}|{obj.SAMAccountName}"] = obj;
        if (!string.IsNullOrEmpty(obj.UserPrincipalName))
            _byUpn[obj.UserPrincipalName] = obj;
    }

    public Task<DirectoryObject> GetByDnAsync(string tenantId, string dn, CancellationToken ct = default)
        => Task.FromResult(_byDn.TryGetValue(dn, out var obj) ? obj : null);

    public Task<DirectoryObject> GetByGuidAsync(string tenantId, string guid, CancellationToken ct = default)
        => Task.FromResult<DirectoryObject>(null);

    public Task<DirectoryObject> GetBySamAccountNameAsync(string tenantId, string domainDn, string samAccountName, CancellationToken ct = default)
        => Task.FromResult(_bySam.TryGetValue($"{domainDn}|{samAccountName}", out var obj) ? obj : null);

    public Task<DirectoryObject> GetByUpnAsync(string tenantId, string upn, CancellationToken ct = default)
        => Task.FromResult(_byUpn.TryGetValue(upn, out var obj) ? obj : null);

    public Task<SearchResult> SearchAsync(string tenantId, string baseDn, SearchScope scope, FilterNode filter,
        string[] attributes, int sizeLimit = 0, int timeLimitSeconds = 0, string continuationToken = null,
        int pageSize = 1000, bool includeDeleted = false, CancellationToken ct = default)
        => Task.FromResult(new SearchResult { Entries = _byDn.Values.ToList() });

    public Task CreateAsync(DirectoryObject obj, CancellationToken ct = default)
    {
        Add(obj);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(DirectoryObject obj, CancellationToken ct = default) => Task.CompletedTask;

    public Task DeleteAsync(string tenantId, string dn, bool hardDelete = false, CancellationToken ct = default)
    {
        _byDn.Remove(dn);
        return Task.CompletedTask;
    }

    public Task MoveAsync(string tenantId, string oldDn, string newDn, CancellationToken ct = default) => Task.CompletedTask;
    public Task<long> GetNextUsnAsync(string tenantId, string domainDn, CancellationToken ct = default) => Task.FromResult(1L);
    public Task<long> ClaimRidPoolAsync(string tenantId, string domainDn, int poolSize, CancellationToken ct = default) => Task.FromResult(1000L);
    public Task<IReadOnlyList<DirectoryObject>> GetChildrenAsync(string tenantId, string parentDn, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<DirectoryObject>>([]);

    public Task<IReadOnlyList<DirectoryObject>> GetByServicePrincipalNameAsync(string tenantId, string spn, CancellationToken ct = default)
    {
        var matches = _byDn.Values
            .Where(o => o.ServicePrincipalName.Contains(spn, StringComparer.OrdinalIgnoreCase))
            .ToList();
        return Task.FromResult<IReadOnlyList<DirectoryObject>>(matches);
    }

    public Task<IReadOnlyList<DirectoryObject>> GetByDnsAsync(string tenantId, IEnumerable<string> dns, CancellationToken ct = default)
    {
        var results = dns
            .Select(dn => _byDn.TryGetValue(dn, out var obj) ? obj : null)
            .Where(o => o is not null)
            .Cast<DirectoryObject>()
            .ToList();
        return Task.FromResult<IReadOnlyList<DirectoryObject>>(results);
    }
}

/// <summary>
/// Stub claims provider that returns configurable claims for testing.
/// </summary>
internal class StubClaimsProvider : IClaimsProvider
{
    public ClaimsSet ClaimsToReturn { get; set; } = new();

    public Task<ClaimsSet> GenerateUserClaimsAsync(DirectoryObject user, CancellationToken ct = default)
        => Task.FromResult(ClaimsToReturn);

    public Task<ClaimsSet> GenerateDeviceClaimsAsync(DirectoryObject computer, CancellationToken ct = default)
        => Task.FromResult(new ClaimsSet());
}
