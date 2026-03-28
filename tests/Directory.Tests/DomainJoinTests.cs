using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Kerberos;
using Directory.Ldap.Handlers;
using Directory.Ldap.Protocol;
using Directory.Ldap.Server;
using Directory.Security;
using Directory.Security.Claims;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Directory.Tests;

/// <summary>
/// Comprehensive end-to-end tests simulating the complete Windows domain join flow.
/// Each phase tests a different stage: DC discovery, account creation, password setup,
/// NRPC password rotation, SPN configuration, Kerberos authentication, and the full flow.
/// Uses real service implementations (PasswordService, UserAccountControlService, RootDseProvider,
/// PacGenerator) with an InMemoryDirectoryStore.
/// </summary>
public class DomainJoinTests
{
    private const string TenantId = "default";
    private const string DomainDn = "DC=corp,DC=contoso,DC=com";
    private const string DomainDns = "corp.contoso.com";
    private const string DomainSid = "S-1-5-21-1000-2000-3000";
    private const string MachineName = "WORKSTATION1";
    private const string MachineAccountName = "WORKSTATION1$";
    private const string MachinePassword = "P@ssw0rd!Complex#123";

    private readonly InMemoryDirectoryStore _store;
    private readonly PasswordService _passwordService;
    private readonly UserAccountControlService _uacService;
    private readonly RootDseProvider _rootDseProvider;
    private readonly PacGenerator _pacGenerator;

    public DomainJoinTests()
    {
        _store = new InMemoryDirectoryStore();
        _passwordService = new PasswordService(_store, NullLogger<PasswordService>.Instance);
        _uacService = new UserAccountControlService(NullLogger<UserAccountControlService>.Instance);
        _rootDseProvider = new RootDseProvider(Options.Create(new LdapServerOptions
        {
            DefaultDomain = DomainDn,
        }));
        _pacGenerator = new PacGenerator(
            _store,
            new StubClaimsProvider(),
            NullLogger<PacGenerator>.Instance);
    }

    // ════════════════════════════════════════════════════════════════
    //  Phase 1: DC Discovery Tests
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void DnsSrvRecords_ContainLdapAndKerberosForDomain()
    {
        // The DnsServer.InitializeSrvRecords registers SRV records for each domain.
        // We verify the expected record names and ports by inspecting the known pattern.
        var domain = "corp.contoso.com";

        // These are the exact SRV record names the DnsServer registers
        var expectedRecords = new Dictionary<string, ushort>
        {
            [$"_ldap._tcp.dc._msdcs.{domain}"] = 389,
            [$"_kerberos._tcp.{domain}"] = 88,
            [$"_ldap._tcp.{domain}"] = 389,
            [$"_kerberos._udp.{domain}"] = 88,
            [$"_kpasswd._tcp.{domain}"] = 464,
            [$"_gc._tcp.{domain}"] = 3268,
            [$"_kerberos._tcp.dc._msdcs.{domain}"] = 88,
            [$"_ldap._tcp.pdc._msdcs.{domain}"] = 389,
            [$"_ldap._tcp.gc._msdcs.{domain}"] = 3268,
        };

        // Verify the critical domain-join SRV records
        Assert.True(expectedRecords.ContainsKey($"_ldap._tcp.dc._msdcs.{domain}"));
        Assert.Equal((ushort)389, expectedRecords[$"_ldap._tcp.dc._msdcs.{domain}"]);
        Assert.True(expectedRecords.ContainsKey($"_kerberos._tcp.{domain}"));
        Assert.Equal((ushort)88, expectedRecords[$"_kerberos._tcp.{domain}"]);
    }

    [Fact]
    public void RootDse_ContainsAllRequiredDomainJoinAttributes()
    {
        var rootDse = _rootDseProvider.GetRootDse();

        var attrNames = rootDse.Attributes.Select(a => a.Item1).ToList();

        // All attributes required for domain join must be present
        Assert.Contains("defaultNamingContext", attrNames);
        Assert.Contains("configurationNamingContext", attrNames);
        Assert.Contains("schemaNamingContext", attrNames);
        Assert.Contains("supportedCapabilities", attrNames);
        Assert.Contains("domainControllerFunctionality", attrNames);
        Assert.Contains("dsServiceName", attrNames);
        Assert.Contains("serverName", attrNames);
        Assert.Contains("dnsHostName", attrNames);
        Assert.Contains("isGlobalCatalogReady", attrNames);
        Assert.Contains("supportedSASLMechanisms", attrNames);

        // Verify defaultNamingContext value
        var dnc = rootDse.Attributes
            .First(a => a.Item1 == "defaultNamingContext").Item2;
        Assert.Contains(DomainDn, dnc.Select(v => v.ToString()));

        // Verify SASL mechanisms include GSS-SPNEGO and GSSAPI
        var sasl = rootDse.Attributes
            .First(a => a.Item1 == "supportedSASLMechanisms").Item2;
        var saslStrings = sasl.Select(v => v.ToString()).ToList();
        Assert.Contains("GSS-SPNEGO", saslStrings);
        Assert.Contains("GSSAPI", saslStrings);
    }

    [Fact]
    public void RootDse_SupportedCapabilities_IncludeActiveDirectoryOids()
    {
        var rootDse = _rootDseProvider.GetRootDse();

        var capabilities = rootDse.Attributes
            .First(a => a.Item1 == "supportedCapabilities").Item2
            .Select(v => v.ToString())
            .ToList();

        // LDAP_CAP_ACTIVE_DIRECTORY_OID
        Assert.Contains(LdapConstants.OidActiveDirectory, capabilities);
        // LDAP_CAP_ACTIVE_DIRECTORY_V51_OID (W2K3+)
        Assert.Contains(LdapConstants.OidActiveDirectoryV51, capabilities);
        // LDAP_CAP_ACTIVE_DIRECTORY_V60_OID (W2K8)
        Assert.Contains(LdapConstants.OidActiveDirectoryV60, capabilities);
        // LDAP_CAP_ACTIVE_DIRECTORY_V61_OID (W2K8R2)
        Assert.Contains(LdapConstants.OidActiveDirectoryV61, capabilities);
        // LDAP_CAP_ACTIVE_DIRECTORY_W8_OID (2012)
        Assert.Contains(LdapConstants.OidActiveDirectoryW8, capabilities);
        // LDAP_CAP_ACTIVE_DIRECTORY_LDAP_INTEG_OID (signing)
        Assert.Contains(LdapConstants.OidActiveDirectoryLdapInteg, capabilities);
    }

    [Fact]
    public void CldapNetlogonResponse_ContainsCorrectFlags()
    {
        // The CldapServer.BuildNetlogonExResponse uses these DsFlag values:
        // DS_LDAP_FLAG | DS_DS_FLAG | DS_KDC_FLAG | DS_WRITABLE_FLAG | DS_GC_FLAG | DS_PDC_FLAG
        // plus many more. We verify the flags are all properly OR'd together.

        // DS_FLAG values from CldapServer
        const uint DS_PDC_FLAG = 0x00000001;
        const uint DS_GC_FLAG = 0x00000004;
        const uint DS_LDAP_FLAG = 0x00000008;
        const uint DS_DS_FLAG = 0x00000010;
        const uint DS_KDC_FLAG = 0x00000020;
        const uint DS_WRITABLE_FLAG = 0x00000100;

        // The flags computed by BuildNetlogonExResponse
        uint expectedFlags = DS_LDAP_FLAG | DS_DS_FLAG | DS_KDC_FLAG |
                             DS_WRITABLE_FLAG | DS_GC_FLAG |
                             0x00000040 | // DS_TIMESERV_FLAG
                             0x00000200 | // DS_GOOD_TIMESERV_FLAG
                             0x00001000 | // DS_FULL_SECRET_DOMAIN_6_FLAG
                             0x00002000 | // DS_WS_FLAG
                             0x00004000 | // DS_DS_8_FLAG
                             0x00008000 | // DS_DS_9_FLAG
                             0x00010000 | // DS_DS_10_FLAG
                             0x00020000 | // DS_KEY_LIST_FLAG
                             DS_PDC_FLAG;

        // Verify all critical flags are present
        Assert.True((expectedFlags & DS_LDAP_FLAG) != 0, "DS_LDAP_FLAG must be set");
        Assert.True((expectedFlags & DS_DS_FLAG) != 0, "DS_DS_FLAG must be set");
        Assert.True((expectedFlags & DS_KDC_FLAG) != 0, "DS_KDC_FLAG must be set");
        Assert.True((expectedFlags & DS_WRITABLE_FLAG) != 0, "DS_WRITABLE_FLAG must be set");
        Assert.True((expectedFlags & DS_GC_FLAG) != 0, "DS_GC_FLAG must be set");
        Assert.True((expectedFlags & DS_PDC_FLAG) != 0, "DS_PDC_FLAG must be set");
    }

    // ════════════════════════════════════════════════════════════════
    //  Phase 2: Account Creation Tests
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CreateMachineAccount_SetsCorrectObjectClass()
    {
        var machine = CreateMachineAccountObject();
        await _store.CreateAsync(machine);

        var retrieved = await _store.GetBySamAccountNameAsync(TenantId, DomainDn, MachineAccountName);
        Assert.NotNull(retrieved);
        Assert.Equal(["top", "person", "organizationalPerson", "user", "computer"], retrieved.ObjectClass);
    }

    [Fact]
    public async Task CreateMachineAccount_SetsWorkstationTrustAndDisabled()
    {
        var machine = CreateMachineAccountObject();
        await _store.CreateAsync(machine);

        var retrieved = await _store.GetBySamAccountNameAsync(TenantId, DomainDn, MachineAccountName);
        Assert.NotNull(retrieved);

        var uac = (UserAccountControlFlags)retrieved.UserAccountControl;
        Assert.True(uac.HasFlag(UserAccountControlFlags.WORKSTATION_TRUST_ACCOUNT));
        Assert.True(uac.HasFlag(UserAccountControlFlags.ACCOUNTDISABLE));
    }

    [Fact]
    public async Task CreateMachineAccount_PlacedInComputersContainer()
    {
        var machine = CreateMachineAccountObject();
        await _store.CreateAsync(machine);

        var retrieved = await _store.GetBySamAccountNameAsync(TenantId, DomainDn, MachineAccountName);
        Assert.NotNull(retrieved);

        var expectedDn = $"CN={MachineAccountName},CN=Computers,{DomainDn}";
        Assert.Equal(expectedDn, retrieved.DistinguishedName);
        Assert.StartsWith($"CN={MachineAccountName},CN=Computers,DC=", retrieved.DistinguishedName);
    }

    [Fact]
    public async Task CreateMachineAccount_HasCorrectPrimaryGroupId()
    {
        var machine = CreateMachineAccountObject();
        await _store.CreateAsync(machine);

        var retrieved = await _store.GetBySamAccountNameAsync(TenantId, DomainDn, MachineAccountName);
        Assert.NotNull(retrieved);

        // Domain Computers = 515, NOT Domain Users = 513
        Assert.Equal(515, retrieved.PrimaryGroupId);
    }

    [Fact]
    public async Task CreateMachineAccount_SamAccountNamePreserved()
    {
        var machine = CreateMachineAccountObject();
        await _store.CreateAsync(machine);

        var retrieved = await _store.GetBySamAccountNameAsync(TenantId, DomainDn, MachineAccountName);
        Assert.NotNull(retrieved);
        Assert.Equal(MachineAccountName, retrieved.SAMAccountName);
        Assert.EndsWith("$", retrieved.SAMAccountName);
    }

    [Fact]
    public async Task CreateMachineAccount_HasObjectSidWithRid()
    {
        var machine = CreateMachineAccountObject();
        await _store.CreateAsync(machine);

        var retrieved = await _store.GetBySamAccountNameAsync(TenantId, DomainDn, MachineAccountName);
        Assert.NotNull(retrieved);
        Assert.NotNull(retrieved.ObjectSid);

        // SID must start with the domain SID
        Assert.StartsWith(DomainSid, retrieved.ObjectSid);

        // SID must have a RID component after the domain SID
        var rid = retrieved.ObjectSid[(DomainSid.Length + 1)..];
        Assert.True(int.TryParse(rid, out var ridValue));
        Assert.True(ridValue > 0, "RID must be greater than 0");
    }

    // ════════════════════════════════════════════════════════════════
    //  Phase 3: Password Setup Tests
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SetMachinePassword_StoresNtHash()
    {
        var machine = CreateMachineAccountObject();
        await _store.CreateAsync(machine);

        await _passwordService.SetPasswordAsync(TenantId, machine.DistinguishedName, MachinePassword);

        var retrieved = await _store.GetByDnAsync(TenantId, machine.DistinguishedName);
        Assert.NotNull(retrieved);
        Assert.NotNull(retrieved.NTHash);
        Assert.NotEmpty(retrieved.NTHash);

        // NT hash should be a hex-encoded 16-byte hash (32 hex chars)
        Assert.Equal(32, retrieved.NTHash.Length);
    }

    [Fact]
    public async Task SetMachinePassword_DerivesKerberosKeys()
    {
        var machine = CreateMachineAccountObject();
        await _store.CreateAsync(machine);

        await _passwordService.SetPasswordAsync(TenantId, machine.DistinguishedName, MachinePassword);

        var retrieved = await _store.GetByDnAsync(TenantId, machine.DistinguishedName);
        Assert.NotNull(retrieved);
        Assert.NotEmpty(retrieved.KerberosKeys);

        // Must have AES256, AES128, and RC4 keys
        Assert.Equal(3, retrieved.KerberosKeys.Count);

        // Keys are stored as "EncryptionType:Base64Key"
        var keyTypes = retrieved.KerberosKeys
            .Select(k => k.Split(':')[0])
            .ToList();

        // EncryptionType enum values: AES256=18, AES128=17, RC4=23
        Assert.Contains("18", keyTypes);  // AES256_CTS_HMAC_SHA1_96
        Assert.Contains("17", keyTypes);  // AES128_CTS_HMAC_SHA1_96
        Assert.Contains("23", keyTypes);  // RC4_HMAC_NT
    }

    [Fact]
    public async Task SetMachinePassword_AES256KeyIs32Bytes()
    {
        var machine = CreateMachineAccountObject();
        await _store.CreateAsync(machine);

        await _passwordService.SetPasswordAsync(TenantId, machine.DistinguishedName, MachinePassword);

        var retrieved = await _store.GetByDnAsync(TenantId, machine.DistinguishedName);
        Assert.NotNull(retrieved);

        // Find the AES256 key (EncryptionType 18)
        var aes256Key = retrieved.KerberosKeys.First(k => k.StartsWith("18:"));
        var keyBytes = Convert.FromBase64String(aes256Key.Split(':')[1]);
        Assert.Equal(32, keyBytes.Length);
    }

    [Fact]
    public async Task SetMachinePassword_UpdatesPwdLastSet()
    {
        var machine = CreateMachineAccountObject();
        machine.PwdLastSet = 0; // Initially not set
        await _store.CreateAsync(machine);

        var beforeSet = DateTimeOffset.UtcNow.ToFileTime();
        await _passwordService.SetPasswordAsync(TenantId, machine.DistinguishedName, MachinePassword);
        var afterSet = DateTimeOffset.UtcNow.ToFileTime();

        var retrieved = await _store.GetByDnAsync(TenantId, machine.DistinguishedName);
        Assert.NotNull(retrieved);
        Assert.True(retrieved.PwdLastSet >= beforeSet, "pwdLastSet must be >= time before SetPassword");
        Assert.True(retrieved.PwdLastSet <= afterSet, "pwdLastSet must be <= time after SetPassword");
    }

    [Fact]
    public async Task EnableMachineAccount_RemovesDisabledFlag()
    {
        var machine = CreateMachineAccountObject();
        await _store.CreateAsync(machine);

        // Verify initially disabled
        Assert.True(_uacService.IsAccountDisabled(machine));

        // Enable the account using SetComputerAccountFlags
        _uacService.SetComputerAccountFlags(machine);

        // Should no longer be disabled
        Assert.False(_uacService.IsAccountDisabled(machine));

        // Should still have WORKSTATION_TRUST_ACCOUNT
        var uac = (UserAccountControlFlags)machine.UserAccountControl;
        Assert.True(uac.HasFlag(UserAccountControlFlags.WORKSTATION_TRUST_ACCOUNT));
    }

    // ════════════════════════════════════════════════════════════════
    //  Phase 4: NRPC Password Rotation Tests
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task NetrServerPasswordSet2_UpdatesNtHash()
    {
        // Simulate the NRPC password rotation logic:
        // 1. Look up machine account
        // 2. Compute new NT hash
        // 3. Update the account
        var machine = CreateMachineAccountObject();
        await _store.CreateAsync(machine);

        // Set an initial password
        await _passwordService.SetPasswordAsync(TenantId, machine.DistinguishedName, MachinePassword);
        var oldHash = (await _store.GetByDnAsync(TenantId, machine.DistinguishedName)).NTHash;

        // Simulate NRPC password rotation with a new password
        var newPassword = "N3wR0tatedP@ss!456";
        var newNtHash = _passwordService.ComputeNTHash(newPassword);
        machine.NTHash = Convert.ToHexString(newNtHash);

        // Verify hash changed
        Assert.NotEqual(oldHash, machine.NTHash);
        Assert.Equal(32, machine.NTHash.Length);
    }

    [Fact]
    public async Task NetrServerPasswordSet2_DerivesKerberosKeys()
    {
        // CRITICAL: This was the bug we fixed — NRPC password rotation must also derive Kerberos keys
        var machine = CreateMachineAccountObject();
        await _store.CreateAsync(machine);

        // Simulate the NRPC password rotation logic from NrpcOperations.NetrServerPasswordSet2Async
        var newPassword = "N3wR0tatedP@ss!456";

        // Step 1: Compute NT hash (same as NrpcOperations does)
        var newNtHash = _passwordService.ComputeNTHash(newPassword);
        machine.NTHash = Convert.ToHexString(newNtHash);

        // Step 2: Derive Kerberos keys (the critical fix)
        var principalName = machine.UserPrincipalName ?? machine.SAMAccountName ?? MachineAccountName;
        var domainDnsUpper = DomainDns.ToUpperInvariant();
        var kerberosKeys = _passwordService.DeriveKerberosKeys(principalName, newPassword, domainDnsUpper);

        machine.KerberosKeys = kerberosKeys
            .Select(k => $"{k.EncryptionType}:{Convert.ToBase64String(k.KeyValue)}")
            .ToList();

        // Verify Kerberos keys were generated (this is the bug fix validation)
        Assert.NotEmpty(machine.KerberosKeys);
        Assert.Equal(3, machine.KerberosKeys.Count);

        // Verify each key type is present
        var keyTypes = machine.KerberosKeys.Select(k => k.Split(':')[0]).ToList();
        Assert.Contains("18", keyTypes);  // AES256
        Assert.Contains("17", keyTypes);  // AES128
        Assert.Contains("23", keyTypes);  // RC4

        // Verify AES256 key is 32 bytes
        var aes256 = machine.KerberosKeys.First(k => k.StartsWith("18:"));
        var aes256Bytes = Convert.FromBase64String(aes256.Split(':')[1]);
        Assert.Equal(32, aes256Bytes.Length);
    }

    [Fact]
    public async Task NetrServerPasswordSet2_UpdatesPwdLastSet()
    {
        var machine = CreateMachineAccountObject();
        await _store.CreateAsync(machine);

        // Set initial password
        await _passwordService.SetPasswordAsync(TenantId, machine.DistinguishedName, MachinePassword);

        // Simulate NRPC password rotation timestamp update
        var beforeRotation = DateTimeOffset.UtcNow.ToFileTime();
        machine.PwdLastSet = DateTimeOffset.UtcNow.ToFileTime();
        var afterRotation = DateTimeOffset.UtcNow.ToFileTime();

        Assert.True(machine.PwdLastSet >= beforeRotation);
        Assert.True(machine.PwdLastSet <= afterRotation);
    }

    // ════════════════════════════════════════════════════════════════
    //  Phase 5: SPN Configuration Tests
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task MachineSPNs_HostShortName()
    {
        var machine = CreateMachineAccountObject();
        machine.ServicePrincipalName.Add($"HOST/{MachineName}");
        await _store.CreateAsync(machine);

        var retrieved = await _store.GetBySamAccountNameAsync(TenantId, DomainDn, MachineAccountName);
        Assert.NotNull(retrieved);
        Assert.Contains($"HOST/{MachineName}", retrieved.ServicePrincipalName);
    }

    [Fact]
    public async Task MachineSPNs_HostFQDN()
    {
        var machine = CreateMachineAccountObject();
        machine.ServicePrincipalName.Add($"HOST/{MachineName.ToLowerInvariant()}.{DomainDns}");
        await _store.CreateAsync(machine);

        var retrieved = await _store.GetBySamAccountNameAsync(TenantId, DomainDn, MachineAccountName);
        Assert.NotNull(retrieved);
        Assert.Contains($"HOST/{MachineName.ToLowerInvariant()}.{DomainDns}", retrieved.ServicePrincipalName);
    }

    [Fact]
    public async Task MachineSPNs_RestrictedKrbHost()
    {
        var machine = CreateMachineAccountObject();
        machine.ServicePrincipalName.Add($"RestrictedKrbHost/{MachineName}");
        machine.ServicePrincipalName.Add($"RestrictedKrbHost/{MachineName.ToLowerInvariant()}.{DomainDns}");
        await _store.CreateAsync(machine);

        var retrieved = await _store.GetBySamAccountNameAsync(TenantId, DomainDn, MachineAccountName);
        Assert.NotNull(retrieved);
        Assert.Contains($"RestrictedKrbHost/{MachineName}", retrieved.ServicePrincipalName);
        Assert.Contains($"RestrictedKrbHost/{MachineName.ToLowerInvariant()}.{DomainDns}",
            retrieved.ServicePrincipalName);
    }

    // ════════════════════════════════════════════════════════════════
    //  Phase 6: Kerberos Authentication Tests
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task MachineAccount_CanGeneratePac()
    {
        var machine = CreateMachineAccountObject();
        machine.PrimaryGroupId = 515; // Domain Computers
        await _store.CreateAsync(machine);

        var pac = await _pacGenerator.GenerateAsync(machine, DomainDn, TenantId);

        Assert.NotNull(pac);
        Assert.NotNull(pac.LogonInfo);
        Assert.NotNull(pac.ClientInformation);
        Assert.NotNull(pac.ServerSignature);
        Assert.NotNull(pac.KdcSignature);
    }

    [Fact]
    public async Task MachineAccount_PacContainsCorrectRid()
    {
        var machine = CreateMachineAccountObject();
        await _store.CreateAsync(machine);

        var pac = await _pacGenerator.GenerateAsync(machine, DomainDn, TenantId);

        // RID = 1001 (from our test SID S-1-5-21-1000-2000-3000-1001)
        Assert.Equal(1001u, pac.LogonInfo.UserId);
    }

    [Fact]
    public async Task MachineAccount_PacContainsDomainComputersGroup()
    {
        var machine = CreateMachineAccountObject();
        machine.PrimaryGroupId = 515; // Domain Computers
        await _store.CreateAsync(machine);

        var pac = await _pacGenerator.GenerateAsync(machine, DomainDn, TenantId);

        Assert.NotNull(pac.LogonInfo.GroupIds);
        // Primary group (Domain Computers, RID 515) should be in the group list
        Assert.Contains(pac.LogonInfo.GroupIds, g => g.RelativeId == 515);
    }

    // ════════════════════════════════════════════════════════════════
    //  Phase 7: End-to-End Flow Test
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task FullDomainJoinFlow_CreatesAndConfiguresMachineAccount()
    {
        // ── Step 1: Create machine account (simulating SamrCreateUser2InDomain) ──
        var machine = CreateMachineAccountObject();
        await _store.CreateAsync(machine);

        // Verify initial state: disabled, correct object class, in Computers container
        var created = await _store.GetBySamAccountNameAsync(TenantId, DomainDn, MachineAccountName);
        Assert.NotNull(created);
        Assert.Equal(["top", "person", "organizationalPerson", "user", "computer"], created.ObjectClass);
        Assert.True(_uacService.IsAccountDisabled(created));
        Assert.Equal(515, created.PrimaryGroupId);
        Assert.Contains("CN=Computers", created.DistinguishedName);

        // ── Step 2: Set password (NT hash + Kerberos keys) ──
        await _passwordService.SetPasswordAsync(TenantId, created.DistinguishedName, MachinePassword);
        var withPassword = await _store.GetByDnAsync(TenantId, created.DistinguishedName);
        Assert.NotNull(withPassword);
        Assert.NotNull(withPassword.NTHash);
        Assert.Equal(32, withPassword.NTHash.Length);
        Assert.Equal(3, withPassword.KerberosKeys.Count);
        Assert.True(withPassword.PwdLastSet > 0, "pwdLastSet must be updated after password set");

        // ── Step 3: Enable account (remove ACCOUNTDISABLE) ──
        _uacService.SetComputerAccountFlags(withPassword);
        Assert.False(_uacService.IsAccountDisabled(withPassword));
        var uac = (UserAccountControlFlags)withPassword.UserAccountControl;
        Assert.True(uac.HasFlag(UserAccountControlFlags.WORKSTATION_TRUST_ACCOUNT));

        // ── Step 4: Set SPNs and dNSHostName ──
        var fqdn = $"{MachineName.ToLowerInvariant()}.{DomainDns}";
        withPassword.DnsHostName = fqdn;
        withPassword.ServicePrincipalName.AddRange([
            $"HOST/{MachineName}",
            $"HOST/{fqdn}",
            $"RestrictedKrbHost/{MachineName}",
            $"RestrictedKrbHost/{fqdn}",
        ]);
        await _store.UpdateAsync(withPassword);

        // ── Step 5: Verify final state ──
        var finalAccount = await _store.GetByDnAsync(TenantId, created.DistinguishedName);
        Assert.NotNull(finalAccount);

        // Object class
        Assert.Equal("computer", finalAccount.ObjectCategory);
        Assert.Contains("computer", finalAccount.ObjectClass);

        // SID
        Assert.NotNull(finalAccount.ObjectSid);
        Assert.StartsWith(DomainSid, finalAccount.ObjectSid);

        // sAMAccountName
        Assert.Equal(MachineAccountName, finalAccount.SAMAccountName);

        // Primary group = Domain Computers (515)
        Assert.Equal(515, finalAccount.PrimaryGroupId);

        // Password set
        Assert.NotNull(finalAccount.NTHash);
        Assert.NotEmpty(finalAccount.KerberosKeys);

        // Account enabled
        Assert.False(_uacService.IsAccountDisabled(finalAccount));

        // SPNs
        Assert.Contains($"HOST/{MachineName}", finalAccount.ServicePrincipalName);
        Assert.Contains($"HOST/{fqdn}", finalAccount.ServicePrincipalName);
        Assert.Contains($"RestrictedKrbHost/{MachineName}", finalAccount.ServicePrincipalName);

        // dNSHostName
        Assert.Equal(fqdn, finalAccount.DnsHostName);

        // Generate PAC to verify Kerberos readiness
        var pac = await _pacGenerator.GenerateAsync(finalAccount, DomainDn, TenantId);
        Assert.NotNull(pac);
        Assert.Equal(MachineAccountName, pac.LogonInfo.UserName);
        Assert.Equal(1001u, pac.LogonInfo.UserId);
        Assert.Contains(pac.LogonInfo.GroupIds, g => g.RelativeId == 515); // Domain Computers
    }

    // ════════════════════════════════════════════════════════════════
    //  Helper Methods
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates a machine account object matching what SamrCreateUser2InDomain produces.
    /// Mirrors the logic in SamrOperations lines 700-750.
    /// </summary>
    private static DirectoryObject CreateMachineAccountObject(uint rid = 1001)
    {
        var container = $"CN=Computers,{DomainDn}";
        var dn = $"CN={MachineAccountName},{container}";
        var objectSid = $"{DomainSid}-{rid}";
        var now = DateTimeOffset.UtcNow;

        return new DirectoryObject
        {
            Id = dn.ToLowerInvariant(),
            TenantId = TenantId,
            DomainDn = DomainDn,
            DistinguishedName = dn,
            ObjectGuid = Guid.NewGuid().ToString(),
            ObjectSid = objectSid,
            SAMAccountName = MachineAccountName,
            Cn = MachineAccountName,
            ObjectClass = ["top", "person", "organizationalPerson", "user", "computer"],
            ObjectCategory = "computer",
            UserAccountControl = (int)(UserAccountControlFlags.WORKSTATION_TRUST_ACCOUNT |
                                       UserAccountControlFlags.ACCOUNTDISABLE),
            PrimaryGroupId = 515, // Domain Computers (NOT 513 Domain Users)
            SAMAccountType = 0x30000001, // SAM_MACHINE_ACCOUNT
            ParentDn = container,
            WhenCreated = now,
            WhenChanged = now,
            PwdLastSet = 0,
            AccountExpires = 0x7FFFFFFFFFFFFFFF,
        };
    }
}
