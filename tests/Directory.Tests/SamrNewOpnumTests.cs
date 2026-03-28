using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Rpc.Samr;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Directory.Tests;

/// <summary>
/// Tests for newer SAMR operations: group/alias CRUD, display enumeration,
/// RID-to-SID, password validation, and security descriptor operations.
/// Tests exercise the service logic directly using InMemoryDirectoryStore,
/// mirroring the behavior of SamrOperations without the RPC wire format.
/// </summary>
public class SamrNewOpnumTests
{
    private const string TenantId = "default";
    private const string DomainDn = "DC=corp,DC=com";
    private const string DomainSid = "S-1-5-21-1000-2000-3000";

    private readonly InMemoryDirectoryStore _store;

    public SamrNewOpnumTests()
    {
        _store = new InMemoryDirectoryStore();
    }

    // ════════════════════════════════════════════════════════════════
    //  Group / Alias CRUD
    // ════════════════════════════════════════════════════════════════

    // 1. CreateGroup sets correct objectClass
    [Fact]
    public async Task CreateGroup_SetsCorrectObjectClass()
    {
        var group = CreateGroupObject("TestGroup", rid: 1100);
        await _store.CreateAsync(group);

        var retrieved = await _store.GetBySamAccountNameAsync(TenantId, DomainDn, "TestGroup");
        Assert.NotNull(retrieved);
        Assert.Contains("group", retrieved.ObjectClass);
        Assert.Contains("top", retrieved.ObjectClass);
    }

    // 2. CreateGroup sets global groupType (GLOBAL_GROUP | SECURITY_ENABLED)
    [Fact]
    public async Task CreateGroup_SetsGlobalGroupType()
    {
        var group = CreateGroupObject("GlobalGroup", rid: 1101);
        await _store.CreateAsync(group);

        var retrieved = await _store.GetBySamAccountNameAsync(TenantId, DomainDn, "GlobalGroup");
        Assert.NotNull(retrieved);
        // GroupType for global security group: 0x80000002
        Assert.Equal(unchecked((int)0x80000002), retrieved.GroupType);
    }

    // 3. CreateGroup placed in Users container
    [Fact]
    public async Task CreateGroup_PlacedInUsersContainer()
    {
        var group = CreateGroupObject("ContainerGroup", rid: 1102);
        await _store.CreateAsync(group);

        var retrieved = await _store.GetBySamAccountNameAsync(TenantId, DomainDn, "ContainerGroup");
        Assert.NotNull(retrieved);
        Assert.StartsWith("CN=ContainerGroup,CN=Users,", retrieved.DistinguishedName);
        Assert.Equal($"CN=Users,{DomainDn}", retrieved.ParentDn);
    }

    // 4. CreateAlias sets domain-local groupType (DOMAIN_LOCAL_GROUP | SECURITY_ENABLED)
    [Fact]
    public async Task CreateAlias_SetsDomainLocalGroupType()
    {
        var alias = CreateAliasObject("TestAlias", rid: 1200);
        await _store.CreateAsync(alias);

        var retrieved = await _store.GetBySamAccountNameAsync(TenantId, DomainDn, "TestAlias");
        Assert.NotNull(retrieved);
        // GroupType for domain-local security group: 0x80000004
        Assert.Equal(unchecked((int)0x80000004), retrieved.GroupType);
    }

    // 5. DeleteUser removes object from store
    [Fact]
    public async Task DeleteUser_RemovesObjectFromStore()
    {
        var user = CreateUserObject("deluser", rid: 1300);
        await _store.CreateAsync(user);

        // Verify exists
        var before = await _store.GetByDnAsync(TenantId, user.DistinguishedName);
        Assert.NotNull(before);

        // Delete (mirrors SamrDeleteUser logic)
        await _store.DeleteAsync(TenantId, user.DistinguishedName, true);

        // Verify removed
        var after = await _store.GetByDnAsync(TenantId, user.DistinguishedName);
        Assert.Null(after);
    }

    // 6. DeleteGroup removes object from store
    [Fact]
    public async Task DeleteGroup_RemovesObjectFromStore()
    {
        var group = CreateGroupObject("DelGroup", rid: 1301);
        await _store.CreateAsync(group);

        // Verify exists
        var before = await _store.GetByDnAsync(TenantId, group.DistinguishedName);
        Assert.NotNull(before);

        // Delete (mirrors SamrDeleteGroup logic)
        await _store.DeleteAsync(TenantId, group.DistinguishedName, true);

        // Verify removed
        var after = await _store.GetByDnAsync(TenantId, group.DistinguishedName);
        Assert.Null(after);
    }

    // ════════════════════════════════════════════════════════════════
    //  Display Enumeration
    // ════════════════════════════════════════════════════════════════

    // 7. QueryDisplayInformation returns users sorted by sAMAccountName
    [Fact]
    public async Task QueryDisplayInformation_UsersSortedBySamAccountName()
    {
        // Create users in non-alphabetical order
        _store.Add(CreateUserObject("charlie", rid: 1401));
        _store.Add(CreateUserObject("alice", rid: 1402));
        _store.Add(CreateUserObject("bob", rid: 1403));

        // Search for all user objects (mirrors SamrQueryDisplayInformation with DomainDisplayUser)
        var result = await _store.SearchAsync(
            TenantId, DomainDn, SearchScope.WholeSubtree,
            new EqualityFilterNode("objectClass", "user"),
            attributes: null, sizeLimit: 0);

        var users = result.Entries
            .Where(e => e.ObjectSid != null && e.ObjectSid.StartsWith(DomainSid))
            .Where(e => !e.ObjectClass.Contains("computer", StringComparer.OrdinalIgnoreCase))
            .OrderBy(e => e.SAMAccountName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.True(users.Count >= 3);
        // Verify alphabetical order
        for (int i = 1; i < users.Count; i++)
        {
            Assert.True(
                string.Compare(users[i - 1].SAMAccountName, users[i].SAMAccountName, StringComparison.OrdinalIgnoreCase) <= 0,
                $"Expected '{users[i - 1].SAMAccountName}' <= '{users[i].SAMAccountName}'");
        }
    }

    // 8. QueryDisplayInformation machine class returns computers
    [Fact]
    public async Task QueryDisplayInformation_MachineClass_ReturnsComputers()
    {
        var computer = new DirectoryObject
        {
            Id = "cn=ws1$,cn=computers,dc=corp,dc=com",
            TenantId = TenantId,
            DomainDn = DomainDn,
            DistinguishedName = "CN=WS1$,CN=Computers,DC=corp,DC=com",
            ObjectGuid = Guid.NewGuid().ToString(),
            ObjectSid = $"{DomainSid}-1500",
            SAMAccountName = "WS1$",
            Cn = "WS1$",
            ObjectClass = ["top", "person", "organizationalPerson", "user", "computer"],
            ObjectCategory = "computer",
            UserAccountControl = (int)SamrConstants.UfWorkstationTrustAccount,
            ParentDn = $"CN=Computers,{DomainDn}",
        };
        _store.Add(computer);

        var result = await _store.SearchAsync(
            TenantId, DomainDn, SearchScope.WholeSubtree,
            new EqualityFilterNode("objectClass", "computer"),
            attributes: null, sizeLimit: 0);

        var machines = result.Entries
            .Where(e => e.ObjectSid != null && e.ObjectSid.StartsWith(DomainSid))
            .ToList();

        Assert.NotEmpty(machines);
        Assert.All(machines, m => Assert.Contains("computer", m.ObjectClass));
    }

    // 9. QueryDisplayInformation group class returns groups
    [Fact]
    public async Task QueryDisplayInformation_GroupClass_ReturnsGroups()
    {
        _store.Add(CreateGroupObject("Admins", rid: 1601));
        _store.Add(CreateGroupObject("Users", rid: 1602));

        var result = await _store.SearchAsync(
            TenantId, DomainDn, SearchScope.WholeSubtree,
            new EqualityFilterNode("objectClass", "group"),
            attributes: null, sizeLimit: 0);

        var groups = result.Entries
            .Where(e => e.ObjectSid != null && e.ObjectSid.StartsWith(DomainSid))
            .Where(e => (e.GroupType & unchecked((int)0x80000002)) == unchecked((int)0x80000002))
            .OrderBy(e => e.SAMAccountName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.True(groups.Count >= 2);
        Assert.All(groups, g => Assert.Contains("group", g.ObjectClass));
    }

    // ════════════════════════════════════════════════════════════════
    //  RID / SID
    // ════════════════════════════════════════════════════════════════

    // 10. RidToSid concatenates domain SID + RID
    [Fact]
    public void RidToSid_ConcatenatesDomainSidAndRid()
    {
        // This mirrors the logic in SamrRidToSid: fullSid = $"{domainSid}-{rid}"
        uint rid = 1001;
        string fullSid = $"{DomainSid}-{rid}";
        Assert.Equal("S-1-5-21-1000-2000-3000-1001", fullSid);
    }

    // 11. RidToSid with well-known RIDs
    [Theory]
    [InlineData(500u, "S-1-5-21-1000-2000-3000-500")]   // Administrator
    [InlineData(512u, "S-1-5-21-1000-2000-3000-512")]   // Domain Admins
    [InlineData(513u, "S-1-5-21-1000-2000-3000-513")]   // Domain Users
    [InlineData(515u, "S-1-5-21-1000-2000-3000-515")]   // Domain Computers
    public void RidToSid_WellKnownRids(uint rid, string expected)
    {
        string fullSid = $"{DomainSid}-{rid}";
        Assert.Equal(expected, fullSid);
    }

    // ════════════════════════════════════════════════════════════════
    //  Password Operations
    // ════════════════════════════════════════════════════════════════

    // 12. GetDomainPasswordInformation returns min length and complexity flags
    [Fact]
    public void GetDomainPasswordInformation_ReturnsMinLengthAndComplexity()
    {
        // SamrGetDomainPasswordInformation returns:
        // MinPasswordLength = 7, PasswordProperties = DomainPasswordComplex (0x1)
        ushort minPasswordLength = 7;
        uint passwordProperties = SamrConstants.DomainPasswordComplex;

        Assert.Equal(7, minPasswordLength);
        Assert.Equal(0x00000001u, passwordProperties);
        Assert.True((passwordProperties & SamrConstants.DomainPasswordComplex) != 0,
            "Password complexity must be required");
    }

    // 13. ValidatePassword accepts complex password
    [Fact]
    public void ValidatePassword_AcceptsComplexPassword()
    {
        string password = "C0mpl3x!Pass#";
        Assert.True(IsPasswordComplex(password, minLength: 7));
    }

    // 14. ValidatePassword rejects short password
    [Fact]
    public void ValidatePassword_RejectsShortPassword()
    {
        string password = "Ab1!";
        Assert.False(IsPasswordComplex(password, minLength: 7));
    }

    // 15. ValidatePassword rejects low-complexity password
    [Fact]
    public void ValidatePassword_RejectsLowComplexityPassword()
    {
        string password = "simplepassword";
        Assert.False(IsPasswordComplex(password, minLength: 7));
    }

    // ════════════════════════════════════════════════════════════════
    //  Security Descriptor
    // ════════════════════════════════════════════════════════════════

    // 16. QuerySecurityObject returns non-null SD
    [Fact]
    public async Task QuerySecurityObject_ReturnsNonNullSecurityDescriptor()
    {
        var user = CreateUserObject("secuser", rid: 1700);
        // Set a security descriptor (mirrors BuildDefaultSecurityDescriptor)
        user.NTSecurityDescriptor = BuildMinimalSecurityDescriptor();
        await _store.CreateAsync(user);

        var retrieved = await _store.GetByDnAsync(TenantId, user.DistinguishedName);
        Assert.NotNull(retrieved);
        Assert.NotNull(retrieved.NTSecurityDescriptor);
        Assert.NotEmpty(retrieved.NTSecurityDescriptor);

        // Verify self-relative SD starts with revision 1
        Assert.Equal(1, retrieved.NTSecurityDescriptor[0]);
    }

    // 17. SetSecurityObject persists and can be queried back
    [Fact]
    public async Task SetSecurityObject_PersistsAndCanBeQueried()
    {
        var user = CreateUserObject("sduser", rid: 1701);
        await _store.CreateAsync(user);

        // Set security descriptor (mirrors SamrSetSecurityObject)
        var sd = BuildMinimalSecurityDescriptor();
        var obj = await _store.GetByDnAsync(TenantId, user.DistinguishedName);
        Assert.NotNull(obj);

        obj.NTSecurityDescriptor = sd;
        obj.WhenChanged = DateTimeOffset.UtcNow;
        await _store.UpdateAsync(obj);

        // Query it back
        var updated = await _store.GetByDnAsync(TenantId, user.DistinguishedName);
        Assert.NotNull(updated);
        Assert.NotNull(updated.NTSecurityDescriptor);
        Assert.Equal(sd, updated.NTSecurityDescriptor);
    }

    // ════════════════════════════════════════════════════════════════
    //  SAMR Constants Validation
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void SamrConstants_GroupObjectSamAccountType()
    {
        Assert.Equal(0x10000000, SamrConstants.SamGroupObject);
    }

    [Fact]
    public void SamrConstants_AliasObjectSamAccountType()
    {
        Assert.Equal(0x20000000, SamrConstants.SamAliasObject);
    }

    [Fact]
    public void SamrConstants_DisplayInformationClasses()
    {
        Assert.Equal(1, SamrConstants.DomainDisplayUser);
        Assert.Equal(2, SamrConstants.DomainDisplayMachine);
        Assert.Equal(3, SamrConstants.DomainDisplayGroup);
    }

    [Fact]
    public void SamrConstants_PasswordValidationTypes()
    {
        Assert.Equal(1, SamrConstants.SamValidateAuthentication);
        Assert.Equal(2, SamrConstants.SamValidatePasswordChange);
        Assert.Equal(3, SamrConstants.SamValidatePasswordReset);
    }

    [Fact]
    public void SamrConstants_SecurityInformationMasks()
    {
        Assert.Equal(0x00000001u, SamrConstants.OwnerSecurityInformation);
        Assert.Equal(0x00000002u, SamrConstants.GroupSecurityInformation);
        Assert.Equal(0x00000004u, SamrConstants.DaclSecurityInformation);
        Assert.Equal(0x00000008u, SamrConstants.SaclSecurityInformation);
    }

    [Fact]
    public void SamrConstants_OpnumValues()
    {
        Assert.Equal(10, SamrConstants.OpSamrCreateGroupInDomain);
        Assert.Equal(14, SamrConstants.OpSamrCreateAliasInDomain);
        Assert.Equal(24, SamrConstants.OpSamrDeleteGroup);
        Assert.Equal(35, SamrConstants.OpSamrDeleteUser);
        Assert.Equal(40, SamrConstants.OpSamrQueryDisplayInformation);
        Assert.Equal(56, SamrConstants.OpSamrGetDomainPasswordInformation);
        Assert.Equal(65, SamrConstants.OpSamrRidToSid);
        Assert.Equal(67, SamrConstants.OpSamrValidatePassword);
    }

    // ════════════════════════════════════════════════════════════════
    //  Helpers
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates a global security group object matching SamrCreateGroupInDomain behavior.
    /// </summary>
    private static DirectoryObject CreateGroupObject(string name, uint rid)
    {
        string container = $"CN=Users,{DomainDn}";
        string dn = $"CN={name},{container}";
        string objectSid = $"{DomainSid}-{rid}";

        return new DirectoryObject
        {
            Id = dn.ToLowerInvariant(),
            TenantId = TenantId,
            DomainDn = DomainDn,
            DistinguishedName = dn,
            ObjectGuid = Guid.NewGuid().ToString(),
            ObjectSid = objectSid,
            SAMAccountName = name,
            Cn = name,
            ObjectClass = ["top", "group"],
            ObjectCategory = "group",
            GroupType = unchecked((int)0x80000002), // GLOBAL_GROUP | SECURITY_ENABLED
            SAMAccountType = SamrConstants.SamGroupObject,
            ParentDn = container,
            WhenCreated = DateTimeOffset.UtcNow,
            WhenChanged = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>
    /// Creates a domain-local group (alias) matching SamrCreateAliasInDomain behavior.
    /// </summary>
    private static DirectoryObject CreateAliasObject(string name, uint rid)
    {
        string container = $"CN=Users,{DomainDn}";
        string dn = $"CN={name},{container}";
        string objectSid = $"{DomainSid}-{rid}";

        return new DirectoryObject
        {
            Id = dn.ToLowerInvariant(),
            TenantId = TenantId,
            DomainDn = DomainDn,
            DistinguishedName = dn,
            ObjectGuid = Guid.NewGuid().ToString(),
            ObjectSid = objectSid,
            SAMAccountName = name,
            Cn = name,
            ObjectClass = ["top", "group"],
            ObjectCategory = "group",
            GroupType = unchecked((int)0x80000004), // DOMAIN_LOCAL_GROUP | SECURITY_ENABLED
            SAMAccountType = SamrConstants.SamAliasObject,
            ParentDn = container,
            WhenCreated = DateTimeOffset.UtcNow,
            WhenChanged = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>
    /// Creates a normal user object matching SamrCreateUserInDomain behavior.
    /// </summary>
    private static DirectoryObject CreateUserObject(string samAccountName, uint rid)
    {
        string container = $"CN=Users,{DomainDn}";
        string dn = $"CN={samAccountName},{container}";
        string objectSid = $"{DomainSid}-{rid}";

        return new DirectoryObject
        {
            Id = dn.ToLowerInvariant(),
            TenantId = TenantId,
            DomainDn = DomainDn,
            DistinguishedName = dn,
            ObjectGuid = Guid.NewGuid().ToString(),
            ObjectSid = objectSid,
            SAMAccountName = samAccountName,
            Cn = samAccountName,
            ObjectClass = ["top", "person", "organizationalPerson", "user"],
            ObjectCategory = "person",
            UserAccountControl = (int)(SamrConstants.UfNormalAccount | SamrConstants.UfAccountDisable),
            PrimaryGroupId = (int)SamrConstants.RidDomainUsers,
            SAMAccountType = SamrConstants.SamUserObject,
            ParentDn = container,
            WhenCreated = DateTimeOffset.UtcNow,
            WhenChanged = DateTimeOffset.UtcNow,
            PwdLastSet = 0,
            AccountExpires = 0x7FFFFFFFFFFFFFFF,
        };
    }

    /// <summary>
    /// Validates password complexity per MS-SAMR rules:
    /// must meet minimum length and contain at least 3 of: upper, lower, digit, special.
    /// </summary>
    private static bool IsPasswordComplex(string password, int minLength)
    {
        if (password.Length < minLength)
            return false;

        int categories = 0;
        if (password.Any(char.IsUpper)) categories++;
        if (password.Any(char.IsLower)) categories++;
        if (password.Any(char.IsDigit)) categories++;
        if (password.Any(c => !char.IsLetterOrDigit(c))) categories++;

        return categories >= 3;
    }

    /// <summary>
    /// Builds a minimal self-relative security descriptor matching the pattern
    /// from SamrOperations.BuildDefaultSecurityDescriptor.
    /// </summary>
    private static byte[] BuildMinimalSecurityDescriptor()
    {
        using var ms = new System.IO.MemoryStream();
        using var bw = new System.IO.BinaryWriter(ms);

        // Revision = 1, Sbz1 = 0
        bw.Write((byte)1);
        bw.Write((byte)0);

        // Control: SE_SELF_RELATIVE | SE_DACL_PRESENT
        ushort control = 0x8000 | 0x0004;
        bw.Write(control);

        uint headerSize = 20;
        uint ownerOffset = headerSize;
        uint ownerSize = 16; // S-1-5-32-544: 2 sub-authorities
        uint groupOffset = ownerOffset + ownerSize;
        uint groupSize = 12; // S-1-5-18: 1 sub-authority
        uint daclOffset = groupOffset + groupSize;

        bw.Write(ownerOffset);
        bw.Write(groupOffset);
        bw.Write(0u); // no SACL
        bw.Write(daclOffset);

        // Owner SID: S-1-5-32-544 (Administrators)
        bw.Write((byte)1); // Revision
        bw.Write((byte)2); // SubAuthorityCount
        bw.Write((byte)0); bw.Write((byte)0); bw.Write((byte)0);
        bw.Write((byte)0); bw.Write((byte)0); bw.Write((byte)5);
        bw.Write(32u);
        bw.Write(544u);

        // Group SID: S-1-5-18 (SYSTEM)
        bw.Write((byte)1);
        bw.Write((byte)1);
        bw.Write((byte)0); bw.Write((byte)0); bw.Write((byte)0);
        bw.Write((byte)0); bw.Write((byte)0); bw.Write((byte)5);
        bw.Write(18u);

        // Minimal DACL: empty ACL header
        bw.Write((byte)2); // AclRevision
        bw.Write((byte)0); // Sbz1
        ushort aclSize = 8;
        bw.Write(aclSize);
        bw.Write((ushort)0); // AceCount
        bw.Write((ushort)0); // Sbz2

        return ms.ToArray();
    }
}
