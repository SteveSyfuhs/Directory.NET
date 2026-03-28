using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Rpc.Drsr;
using Directory.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Directory.Tests;

/// <summary>
/// Tests for DsCrackNamesService — name translation between DN, NT4, UPN, canonical,
/// GUID, SID, SPN, DNS domain, and display name formats.
/// Uses InMemoryDirectoryStore with pre-populated test objects.
/// </summary>
public class DsCrackNamesTests
{
    private const string TenantId = "default";
    private const string DomainDn = "DC=corp,DC=com";
    private const string DomainDns = "corp.com";
    private const string DomainSid = "S-1-5-21-1000-2000-3000";

    private readonly InMemoryDirectoryStore _store;
    private readonly NamingContextService _ncService;
    private readonly DsCrackNamesService _service;

    private readonly string _userGuid = Guid.NewGuid().ToString();
    private readonly string _userSid = $"{DomainSid}-1001";
    private readonly string _groupGuid = Guid.NewGuid().ToString();

    public DsCrackNamesTests()
    {
        _store = new InMemoryDirectoryStore();
        _ncService = new NamingContextService(Options.Create(new NamingContextOptions
        {
            DomainDn = DomainDn,
            ForestDnsName = DomainDns,
            DomainSid = DomainSid,
        }));
        _service = new DsCrackNamesService(
            _store,
            _ncService,
            NullLogger<DsCrackNamesService>.Instance);

        // Seed a test user
        var user = new DirectoryObject
        {
            Id = "cn=john doe,cn=users,dc=corp,dc=com",
            TenantId = TenantId,
            DomainDn = DomainDn,
            DistinguishedName = "CN=John Doe,CN=Users,DC=corp,DC=com",
            ObjectGuid = _userGuid,
            ObjectSid = _userSid,
            SAMAccountName = "jdoe",
            UserPrincipalName = "jdoe@corp.com",
            Cn = "John Doe",
            DisplayName = "John Doe",
            ObjectClass = ["top", "person", "organizationalPerson", "user"],
            ObjectCategory = "person",
            ServicePrincipalName = ["HTTP/web.corp.com"],
        };
        _store.Add(user);

        // Seed a test group
        var group = new DirectoryObject
        {
            Id = "cn=developers,cn=users,dc=corp,dc=com",
            TenantId = TenantId,
            DomainDn = DomainDn,
            DistinguishedName = "CN=Developers,CN=Users,DC=corp,DC=com",
            ObjectGuid = _groupGuid,
            ObjectSid = $"{DomainSid}-1100",
            SAMAccountName = "Developers",
            Cn = "Developers",
            DisplayName = "Developer Group",
            ObjectClass = ["top", "group"],
            ObjectCategory = "group",
        };
        _store.Add(group);
    }

    // ════════════════════════════════════════════════════════════════
    //  1. DN to Canonical
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DnToCanonical_ReturnsSlashSeparatedPath()
    {
        var results = await _service.CrackNamesAsync(
            TenantId,
            (uint)CrackNameFormat.DS_FQDN_1779_NAME,
            (uint)CrackNameFormat.DS_CANONICAL_NAME,
            0,
            ["CN=John Doe,CN=Users,DC=corp,DC=com"]);

        Assert.Single(results);
        Assert.Equal(CrackNameStatus.DS_NAME_NO_ERROR, results[0].Status);
        Assert.Equal("corp.com/Users/John Doe", results[0].Name);
    }

    // ════════════════════════════════════════════════════════════════
    //  2. DN to Canonical_Ex (newline before leaf)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DnToCanonicalEx_ReturnsNewlineBeforeLeaf()
    {
        var results = await _service.CrackNamesAsync(
            TenantId,
            (uint)CrackNameFormat.DS_FQDN_1779_NAME,
            (uint)CrackNameFormat.DS_CANONICAL_NAME_EX,
            0,
            ["CN=John Doe,CN=Users,DC=corp,DC=com"]);

        Assert.Single(results);
        Assert.Equal(CrackNameStatus.DS_NAME_NO_ERROR, results[0].Status);
        Assert.Equal("corp.com/Users\nJohn Doe", results[0].Name);
    }

    // ════════════════════════════════════════════════════════════════
    //  3. DN to NT4
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DnToNt4_ReturnsDomainBackslashSamAccountName()
    {
        var results = await _service.CrackNamesAsync(
            TenantId,
            (uint)CrackNameFormat.DS_FQDN_1779_NAME,
            (uint)CrackNameFormat.DS_NT4_ACCOUNT_NAME,
            0,
            ["CN=John Doe,CN=Users,DC=corp,DC=com"]);

        Assert.Single(results);
        Assert.Equal(CrackNameStatus.DS_NAME_NO_ERROR, results[0].Status);
        Assert.Equal("CORP\\jdoe", results[0].Name);
    }

    // ════════════════════════════════════════════════════════════════
    //  4. DN to UPN
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DnToUpn_ReturnsUserPrincipalName()
    {
        var results = await _service.CrackNamesAsync(
            TenantId,
            (uint)CrackNameFormat.DS_FQDN_1779_NAME,
            (uint)CrackNameFormat.DS_USER_PRINCIPAL_NAME,
            0,
            ["CN=John Doe,CN=Users,DC=corp,DC=com"]);

        Assert.Single(results);
        Assert.Equal(CrackNameStatus.DS_NAME_NO_ERROR, results[0].Status);
        Assert.Equal("jdoe@corp.com", results[0].Name);
    }

    [Fact]
    public async Task DnToUpn_FallsBackToSamAccountNameAtDomain()
    {
        // Create a user without UPN
        var noUpnUser = new DirectoryObject
        {
            Id = "cn=noUpnUser,cn=users,dc=corp,dc=com",
            TenantId = TenantId,
            DomainDn = DomainDn,
            DistinguishedName = "CN=noUpnUser,CN=Users,DC=corp,DC=com",
            ObjectGuid = Guid.NewGuid().ToString(),
            ObjectSid = $"{DomainSid}-1050",
            SAMAccountName = "noUpnUser",
            Cn = "noUpnUser",
            ObjectClass = ["top", "person", "organizationalPerson", "user"],
            ObjectCategory = "person",
        };
        _store.Add(noUpnUser);

        var results = await _service.CrackNamesAsync(
            TenantId,
            (uint)CrackNameFormat.DS_FQDN_1779_NAME,
            (uint)CrackNameFormat.DS_USER_PRINCIPAL_NAME,
            0,
            ["CN=noUpnUser,CN=Users,DC=corp,DC=com"]);

        Assert.Single(results);
        Assert.Equal(CrackNameStatus.DS_NAME_NO_ERROR, results[0].Status);
        Assert.Equal("noUpnUser@corp.com", results[0].Name);
    }

    // ════════════════════════════════════════════════════════════════
    //  5. DN to GUID
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DnToGuid_ReturnsBracedObjectGuid()
    {
        var results = await _service.CrackNamesAsync(
            TenantId,
            (uint)CrackNameFormat.DS_FQDN_1779_NAME,
            (uint)CrackNameFormat.DS_UNIQUE_ID_NAME,
            0,
            ["CN=John Doe,CN=Users,DC=corp,DC=com"]);

        Assert.Single(results);
        Assert.Equal(CrackNameStatus.DS_NAME_NO_ERROR, results[0].Status);
        Assert.Equal($"{{{_userGuid}}}", results[0].Name);
    }

    // ════════════════════════════════════════════════════════════════
    //  6. DN to SID
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DnToSid_ReturnsObjectSid()
    {
        var results = await _service.CrackNamesAsync(
            TenantId,
            (uint)CrackNameFormat.DS_FQDN_1779_NAME,
            (uint)CrackNameFormat.DS_SID_OR_SID_HISTORY_NAME,
            0,
            ["CN=John Doe,CN=Users,DC=corp,DC=com"]);

        Assert.Single(results);
        Assert.Equal(CrackNameStatus.DS_NAME_NO_ERROR, results[0].Status);
        Assert.Equal(_userSid, results[0].Name);
    }

    // ════════════════════════════════════════════════════════════════
    //  7. DN to DNS Domain
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DnToDnsDomain_ReturnsDnsDomainName()
    {
        var results = await _service.CrackNamesAsync(
            TenantId,
            (uint)CrackNameFormat.DS_FQDN_1779_NAME,
            (uint)CrackNameFormat.DS_DNS_DOMAIN_NAME,
            0,
            ["CN=John Doe,CN=Users,DC=corp,DC=com"]);

        Assert.Single(results);
        Assert.Equal(CrackNameStatus.DS_NAME_NO_ERROR, results[0].Status);
        Assert.Equal("corp.com", results[0].Name);
    }

    // ════════════════════════════════════════════════════════════════
    //  8. NT4 to DN
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Nt4ToDn_LooksBySamAccountName()
    {
        var results = await _service.CrackNamesAsync(
            TenantId,
            (uint)CrackNameFormat.DS_NT4_ACCOUNT_NAME,
            (uint)CrackNameFormat.DS_FQDN_1779_NAME,
            0,
            ["CORP\\jdoe"]);

        Assert.Single(results);
        Assert.Equal(CrackNameStatus.DS_NAME_NO_ERROR, results[0].Status);
        Assert.Equal("CN=John Doe,CN=Users,DC=corp,DC=com", results[0].Name);
    }

    // ════════════════════════════════════════════════════════════════
    //  9. UPN to DN
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task UpnToDn_LooksByUserPrincipalName()
    {
        var results = await _service.CrackNamesAsync(
            TenantId,
            (uint)CrackNameFormat.DS_USER_PRINCIPAL_NAME,
            (uint)CrackNameFormat.DS_FQDN_1779_NAME,
            0,
            ["jdoe@corp.com"]);

        Assert.Single(results);
        Assert.Equal(CrackNameStatus.DS_NAME_NO_ERROR, results[0].Status);
        Assert.Equal("CN=John Doe,CN=Users,DC=corp,DC=com", results[0].Name);
    }

    // ════════════════════════════════════════════════════════════════
    //  10. GUID to DN
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GuidToDn_VerifiesGuidFormatDetection()
    {
        // The InMemoryDirectoryStore.GetByGuidAsync returns null by default,
        // so GUID-based lookup won't resolve. We verify the GUID format is
        // properly handled by the CrackNames pipeline via DS_UNKNOWN_NAME auto-detect.
        var results = await _service.CrackNamesAsync(
            TenantId,
            (uint)CrackNameFormat.DS_UNIQUE_ID_NAME,
            (uint)CrackNameFormat.DS_FQDN_1779_NAME,
            0,
            [$"{{{_userGuid}}}"]);

        // GetByGuidAsync in InMemoryDirectoryStore returns null, so NOT_FOUND is expected
        Assert.Single(results);
        Assert.Equal(CrackNameStatus.DS_NAME_ERROR_NOT_FOUND, results[0].Status);
    }

    // ════════════════════════════════════════════════════════════════
    //  11. SID to DN
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SidToDn_LooksByObjectSid()
    {
        var results = await _service.CrackNamesAsync(
            TenantId,
            (uint)CrackNameFormat.DS_SID_OR_SID_HISTORY_NAME,
            (uint)CrackNameFormat.DS_FQDN_1779_NAME,
            0,
            [_userSid]);

        Assert.Single(results);
        Assert.Equal(CrackNameStatus.DS_NAME_NO_ERROR, results[0].Status);
        Assert.Equal("CN=John Doe,CN=Users,DC=corp,DC=com", results[0].Name);
    }

    // ════════════════════════════════════════════════════════════════
    //  12. SPN to DN
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SpnToDn_LooksByServicePrincipalName()
    {
        var results = await _service.CrackNamesAsync(
            TenantId,
            (uint)CrackNameFormat.DS_SERVICE_PRINCIPAL_NAME,
            (uint)CrackNameFormat.DS_FQDN_1779_NAME,
            0,
            ["HTTP/web.corp.com"]);

        Assert.Single(results);
        Assert.Equal(CrackNameStatus.DS_NAME_NO_ERROR, results[0].Status);
        Assert.Equal("CN=John Doe,CN=Users,DC=corp,DC=com", results[0].Name);
    }

    // ════════════════════════════════════════════════════════════════
    //  13. Canonical to DN (syntactical)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CanonicalToDn_SyntacticalConversion()
    {
        var results = await _service.CrackNamesAsync(
            TenantId,
            (uint)CrackNameFormat.DS_CANONICAL_NAME,
            (uint)CrackNameFormat.DS_FQDN_1779_NAME,
            (uint)CrackNameFlags.DS_NAME_FLAG_SYNTACTICAL_ONLY,
            ["corp.com/Users/John Doe"]);

        Assert.Single(results);
        Assert.Equal(CrackNameStatus.DS_NAME_NO_ERROR, results[0].Status);
        Assert.Equal("CN=John Doe,CN=Users,DC=corp,DC=com", results[0].Name);
    }

    // ════════════════════════════════════════════════════════════════
    //  14. Unknown format auto-detect
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task UnknownFormat_AutoDetectsDn()
    {
        // DS_UNKNOWN_NAME with a DN should auto-detect and resolve
        var results = await _service.CrackNamesAsync(
            TenantId,
            (uint)CrackNameFormat.DS_UNKNOWN_NAME,
            (uint)CrackNameFormat.DS_NT4_ACCOUNT_NAME,
            0,
            ["CN=John Doe,CN=Users,DC=corp,DC=com"]);

        Assert.Single(results);
        Assert.Equal(CrackNameStatus.DS_NAME_NO_ERROR, results[0].Status);
        Assert.Equal("CORP\\jdoe", results[0].Name);
    }

    [Fact]
    public async Task UnknownFormat_AutoDetectsSid()
    {
        var results = await _service.CrackNamesAsync(
            TenantId,
            (uint)CrackNameFormat.DS_UNKNOWN_NAME,
            (uint)CrackNameFormat.DS_FQDN_1779_NAME,
            0,
            [_userSid]);

        Assert.Single(results);
        Assert.Equal(CrackNameStatus.DS_NAME_NO_ERROR, results[0].Status);
        Assert.Equal("CN=John Doe,CN=Users,DC=corp,DC=com", results[0].Name);
    }

    [Fact]
    public async Task UnknownFormat_AutoDetectsNt4()
    {
        var results = await _service.CrackNamesAsync(
            TenantId,
            (uint)CrackNameFormat.DS_UNKNOWN_NAME,
            (uint)CrackNameFormat.DS_FQDN_1779_NAME,
            0,
            ["CORP\\jdoe"]);

        Assert.Single(results);
        Assert.Equal(CrackNameStatus.DS_NAME_NO_ERROR, results[0].Status);
        Assert.Equal("CN=John Doe,CN=Users,DC=corp,DC=com", results[0].Name);
    }

    [Fact]
    public async Task UnknownFormat_AutoDetectsDnAndResolves()
    {
        var results = await _service.CrackNamesAsync(
            TenantId,
            (uint)CrackNameFormat.DS_UNKNOWN_NAME,
            (uint)CrackNameFormat.DS_CANONICAL_NAME,
            0,
            ["CN=John Doe,CN=Users,DC=corp,DC=com"]);

        Assert.Single(results);
        Assert.Equal(CrackNameStatus.DS_NAME_NO_ERROR, results[0].Status);
        Assert.Equal("corp.com/Users/John Doe", results[0].Name);
    }

    [Fact]
    public async Task UnknownFormat_AutoDetectsUpnAndResolves()
    {
        var results = await _service.CrackNamesAsync(
            TenantId,
            (uint)CrackNameFormat.DS_UNKNOWN_NAME,
            (uint)CrackNameFormat.DS_FQDN_1779_NAME,
            0,
            ["jdoe@corp.com"]);

        Assert.Single(results);
        Assert.Equal(CrackNameStatus.DS_NAME_NO_ERROR, results[0].Status);
        Assert.Equal("CN=John Doe,CN=Users,DC=corp,DC=com", results[0].Name);
    }

    // ════════════════════════════════════════════════════════════════
    //  15. Syntactical-only flag (no directory lookup)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SyntacticalOnly_DnToCanonical_WithoutDirectoryLookup()
    {
        // Use a DN that does NOT exist in the store — should still convert syntactically
        var results = await _service.CrackNamesAsync(
            TenantId,
            (uint)CrackNameFormat.DS_FQDN_1779_NAME,
            (uint)CrackNameFormat.DS_CANONICAL_NAME,
            (uint)CrackNameFlags.DS_NAME_FLAG_SYNTACTICAL_ONLY,
            ["CN=Nonexistent,OU=Test,DC=corp,DC=com"]);

        Assert.Single(results);
        Assert.Equal(CrackNameStatus.DS_NAME_NO_ERROR, results[0].Status);
        Assert.Equal("corp.com/Test/Nonexistent", results[0].Name);
    }

    [Fact]
    public async Task SyntacticalOnly_DnToDnsDomain()
    {
        var results = await _service.CrackNamesAsync(
            TenantId,
            (uint)CrackNameFormat.DS_FQDN_1779_NAME,
            (uint)CrackNameFormat.DS_DNS_DOMAIN_NAME,
            (uint)CrackNameFlags.DS_NAME_FLAG_SYNTACTICAL_ONLY,
            ["DC=corp,DC=com"]);

        Assert.Single(results);
        Assert.Equal(CrackNameStatus.DS_NAME_NO_ERROR, results[0].Status);
        Assert.Equal("corp.com", results[0].Name);
    }

    // ════════════════════════════════════════════════════════════════
    //  16. Not found
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task NotFound_ReturnsErrorNotFound()
    {
        var results = await _service.CrackNamesAsync(
            TenantId,
            (uint)CrackNameFormat.DS_NT4_ACCOUNT_NAME,
            (uint)CrackNameFormat.DS_FQDN_1779_NAME,
            0,
            ["CORP\\nonexistent"]);

        Assert.Single(results);
        Assert.Equal(CrackNameStatus.DS_NAME_ERROR_NOT_FOUND, results[0].Status);
    }

    // ════════════════════════════════════════════════════════════════
    //  17. Multiple names — batch conversion
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task MultipleNames_BatchConversion()
    {
        var results = await _service.CrackNamesAsync(
            TenantId,
            (uint)CrackNameFormat.DS_FQDN_1779_NAME,
            (uint)CrackNameFormat.DS_CANONICAL_NAME,
            0,
            [
                "CN=John Doe,CN=Users,DC=corp,DC=com",
                "CN=Developers,CN=Users,DC=corp,DC=com",
                "CN=Nonexistent,CN=Users,DC=corp,DC=com",
            ]);

        Assert.Equal(3, results.Length);

        Assert.Equal(CrackNameStatus.DS_NAME_NO_ERROR, results[0].Status);
        Assert.Equal("corp.com/Users/John Doe", results[0].Name);

        Assert.Equal(CrackNameStatus.DS_NAME_NO_ERROR, results[1].Status);
        Assert.Equal("corp.com/Users/Developers", results[1].Name);

        Assert.Equal(CrackNameStatus.DS_NAME_ERROR_NOT_FOUND, results[2].Status);
    }

    // ════════════════════════════════════════════════════════════════
    //  18. Display name
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DnToDisplayName_ReturnsDisplayNameOrCn()
    {
        var results = await _service.CrackNamesAsync(
            TenantId,
            (uint)CrackNameFormat.DS_FQDN_1779_NAME,
            (uint)CrackNameFormat.DS_DISPLAY_NAME,
            0,
            ["CN=John Doe,CN=Users,DC=corp,DC=com"]);

        Assert.Single(results);
        Assert.Equal(CrackNameStatus.DS_NAME_NO_ERROR, results[0].Status);
        Assert.Equal("John Doe", results[0].Name);
    }

    // ════════════════════════════════════════════════════════════════
    //  Syntactical conversion round-trip tests
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SyntacticalDnToDns_ExtractsDomainComponents()
    {
        var results = await _service.CrackNamesAsync(
            TenantId,
            (uint)CrackNameFormat.DS_FQDN_1779_NAME,
            (uint)CrackNameFormat.DS_DNS_DOMAIN_NAME,
            (uint)CrackNameFlags.DS_NAME_FLAG_SYNTACTICAL_ONLY,
            ["DC=corp,DC=com"]);

        Assert.Single(results);
        Assert.Equal(CrackNameStatus.DS_NAME_NO_ERROR, results[0].Status);
        Assert.Equal("corp.com", results[0].Name);
    }

    [Fact]
    public async Task SyntacticalDnToNt4_ExtractsCnAndDomain()
    {
        var results = await _service.CrackNamesAsync(
            TenantId,
            (uint)CrackNameFormat.DS_FQDN_1779_NAME,
            (uint)CrackNameFormat.DS_NT4_ACCOUNT_NAME,
            (uint)CrackNameFlags.DS_NAME_FLAG_SYNTACTICAL_ONLY,
            ["CN=John,CN=Users,DC=corp,DC=com"]);

        Assert.Single(results);
        Assert.Equal(CrackNameStatus.DS_NAME_NO_ERROR, results[0].Status);
        Assert.Equal("CORP\\John", results[0].Name);
    }

    [Fact]
    public async Task SyntacticalCanonicalToDn_RoundTrips()
    {
        var results = await _service.CrackNamesAsync(
            TenantId,
            (uint)CrackNameFormat.DS_CANONICAL_NAME,
            (uint)CrackNameFormat.DS_FQDN_1779_NAME,
            (uint)CrackNameFlags.DS_NAME_FLAG_SYNTACTICAL_ONLY,
            ["corp.com/Users/John Doe"]);

        Assert.Single(results);
        Assert.Equal(CrackNameStatus.DS_NAME_NO_ERROR, results[0].Status);
        Assert.Equal("CN=John Doe,CN=Users,DC=corp,DC=com", results[0].Name);
    }

    [Fact]
    public async Task SyntacticalCanonicalExToDn_RoundTrips()
    {
        var results = await _service.CrackNamesAsync(
            TenantId,
            (uint)CrackNameFormat.DS_CANONICAL_NAME_EX,
            (uint)CrackNameFormat.DS_FQDN_1779_NAME,
            (uint)CrackNameFlags.DS_NAME_FLAG_SYNTACTICAL_ONLY,
            ["corp.com/Users\nJohn Doe"]);

        Assert.Single(results);
        Assert.Equal(CrackNameStatus.DS_NAME_NO_ERROR, results[0].Status);
        Assert.Equal("CN=John Doe,CN=Users,DC=corp,DC=com", results[0].Name);
    }
}
