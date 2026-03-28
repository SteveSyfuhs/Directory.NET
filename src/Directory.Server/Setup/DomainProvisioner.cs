using System.Security.Cryptography;
using System.Text.Json;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Dns;
using Microsoft.Extensions.Logging;

namespace Directory.Server.Setup;

public class DomainProvisioner
{
    private readonly IDirectoryStore _store;
    private readonly IPasswordPolicy _passwordPolicy;
    private readonly IRidAllocator _ridAllocator;
    private readonly INamingContextService _namingContextService;
    private readonly ISchemaService _schemaService;
    private readonly IAccessControlService _acl;
    private readonly DnsZoneStore _dnsZoneStore;
    private readonly ILogger<DomainProvisioner> _logger;
    private int _objectCount;

    public DomainProvisioner(
        IDirectoryStore store,
        IPasswordPolicy passwordPolicy,
        IRidAllocator ridAllocator,
        INamingContextService namingContextService,
        ISchemaService schemaService,
        IAccessControlService acl,
        DnsZoneStore dnsZoneStore,
        ILogger<DomainProvisioner> logger)
    {
        _store = store;
        _passwordPolicy = passwordPolicy;
        _ridAllocator = ridAllocator;
        _namingContextService = namingContextService;
        _schemaService = schemaService;
        _acl = acl;
        _dnsZoneStore = dnsZoneStore;
        _logger = logger;
    }

    public async Task ProvisionAsync(SetupOptions options, CancellationToken ct = default)
    {
        _logger.LogInformation("Starting domain provisioning for {Domain}...", options.DomainName);

        // Check if domain already exists
        var existing = await _store.GetByDnAsync(options.TenantId, options.DomainDn, ct);
        if (existing != null)
        {
            throw new InvalidOperationException($"Domain {options.DomainDn} already exists. Cannot reprovision.");
        }

        var domainSid = _ridAllocator.GetDomainSid(options.TenantId, options.DomainDn);
        _objectCount = 0;

        // Phase 1: Create Naming Context head objects
        WritePhase(1, "Creating naming context roots");
        await CreateDomainRootAsync(options, domainSid, ct);
        await CreateConfigurationNcAsync(options, domainSid, ct);
        await CreateSchemaNcAsync(options, domainSid, ct);
        WritePhaseComplete();

        // Phase 2: Create well-known containers
        WritePhase(2, "Creating well-known containers");
        await CreateWellKnownContainersAsync(options, domainSid, ct);
        WritePhaseComplete();

        // Phase 3: Create built-in security principals
        WritePhase(3, "Creating built-in security principals");
        await CreateBuiltInAccountsAsync(options, domainSid, ct);
        WritePhaseComplete();

        // Phase 4: Create default groups
        WritePhase(4, "Creating default domain groups");
        await CreateDefaultGroupsAsync(options, domainSid, ct);
        WritePhaseComplete();

        // Phase 5: Create Administrator account
        WritePhase(5, "Creating Administrator account");
        await CreateAdministratorAsync(options, domainSid, ct);
        WritePhaseComplete();

        // Phase 6: Create krbtgt account
        WritePhase(6, "Creating krbtgt account");
        await CreateKrbtgtAsync(options, domainSid, ct);
        WritePhaseComplete();

        // Phase 7: Create site topology
        WritePhase(7, "Creating site topology");
        await CreateSiteTopologyAsync(options, domainSid, ct);
        WritePhaseComplete();

        // Phase 8: Create default GPOs
        WritePhase(8, "Creating default Group Policy objects");
        await CreateDefaultGpoAsync(options, domainSid, ct);
        WritePhaseComplete();

        // Phase 9: Set group memberships
        WritePhase(9, "Configuring group memberships");
        await ConfigureGroupMembershipsAsync(options, domainSid, ct);
        WritePhaseComplete();

        // Phase 10: Update configuration file
        WritePhase(10, "Updating configuration");
        await UpdateConfigurationAsync(options, ct);
        WritePhaseComplete();

        // Phase 11: Seed initial DNS SRV records for this DC
        WritePhase(11, "Seeding DNS SRV records");
        await SeedDnsSrvRecordsAsync(options, ct);
        WritePhaseComplete();

        _logger.LogInformation("Domain provisioning complete!");

        // Show completion banner
        Console.WriteLine();
        Console.WriteLine("  \u2554\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2557");
        Console.WriteLine("  \u2551              Domain Provisioning Complete!               \u2551");
        Console.WriteLine("  \u255a\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u255d");
        Console.WriteLine();
        Console.WriteLine($"  Domain:         {options.DomainName}");
        Console.WriteLine($"  NetBIOS:        {options.NetBiosName}");
        Console.WriteLine($"  Domain DN:      {options.DomainDn}");
        Console.WriteLine($"  Admin Account:  {options.AdminUsername}@{options.DomainName}");
        Console.WriteLine($"  Domain SID:     {domainSid}");
        Console.WriteLine();
        Console.WriteLine($"  Objects created: {_objectCount}");
        Console.WriteLine();
        Console.WriteLine("  To start the domain controller, run:");
        Console.WriteLine("    dotnet run");
        Console.WriteLine();
    }

    /// <summary>
    /// Provisions a replica DC by joining an existing domain. Does NOT create
    /// any domain objects — they already exist. Only creates this DC's server
    /// object, NTDS Settings, and computer account.
    /// </summary>
    public async Task ProvisionReplicaAsync(SetupOptions options, CancellationToken ct = default)
    {
        _logger.LogInformation("Starting replica DC provisioning for {Domain}...", options.DomainName);

        // Verify domain already exists
        var existing = await _store.GetByDnAsync(options.TenantId, options.DomainDn, ct);
        if (existing == null)
        {
            throw new InvalidOperationException(
                $"Domain {options.DomainDn} does not exist. Use 'setup' without --replica to create a new domain first.");
        }

        var domainSid = _ridAllocator.GetDomainSid(options.TenantId, options.DomainDn);
        var hostname = options.Hostname.ToUpperInvariant();
        var configDn = options.ConfigurationDn;
        var schemaDn = options.SchemaDn;
        _objectCount = 0;

        WritePhase(1, "Verifying domain exists");
        Console.WriteLine($"    Domain found: {existing.DistinguishedName}");
        WritePhaseComplete();

        WritePhase(2, "Creating replica DC site topology objects");

        var sitesDn = $"CN=Sites,{configDn}";
        var siteDn = $"CN={options.SiteName},{sitesDn}";
        var serversDn = $"CN=Servers,{siteDn}";
        var serverDn = $"CN={hostname},{serversDn}";

        // Ensure site exists (it may be a different site from the first DC)
        var site = await _store.GetByDnAsync(options.TenantId, siteDn, ct);
        if (site == null)
        {
            site = CreateBaseObject(options.TenantId, options.DomainDn, siteDn, ["top", "site"],
                $"CN=Site,{schemaDn}", sitesDn);
            site.Cn = options.SiteName;
            await StoreObjectAsync(site, ct);

            var siteSettings = CreateBaseObject(options.TenantId, options.DomainDn,
                $"CN=NTDS Site Settings,{siteDn}", ["top", "nTDSSiteSettings"],
                $"CN=NTDS-Site-Settings,{schemaDn}", siteDn);
            siteSettings.Cn = "NTDS Site Settings";
            await StoreObjectAsync(siteSettings, ct);

            var serversContainer = CreateBaseObject(options.TenantId, options.DomainDn, serversDn,
                ["top", "serversContainer"], $"CN=Servers-Container,{schemaDn}", siteDn);
            serversContainer.Cn = "Servers";
            await StoreObjectAsync(serversContainer, ct);
        }
        else
        {
            // Ensure servers container exists
            var serversContainer = await _store.GetByDnAsync(options.TenantId, serversDn, ct);
            if (serversContainer == null)
            {
                serversContainer = CreateBaseObject(options.TenantId, options.DomainDn, serversDn,
                    ["top", "serversContainer"], $"CN=Servers-Container,{schemaDn}", siteDn);
                serversContainer.Cn = "Servers";
                await StoreObjectAsync(serversContainer, ct);
            }
        }

        // Create this replica's server object
        var existingServer = await _store.GetByDnAsync(options.TenantId, serverDn, ct);
        if (existingServer != null)
        {
            _logger.LogWarning("Server object {ServerDn} already exists; updating", serverDn);
            existingServer.DnsHostName = $"{hostname.ToLowerInvariant()}.{options.DomainName}";
            existingServer.WhenChanged = DateTimeOffset.UtcNow;
            await _store.UpdateAsync(existingServer, ct);
        }
        else
        {
            var server = CreateBaseObject(options.TenantId, options.DomainDn, serverDn, ["top", "server"],
                $"CN=Server,{schemaDn}", serversDn);
            server.Cn = hostname;
            server.DnsHostName = $"{hostname.ToLowerInvariant()}.{options.DomainName}";
            await StoreObjectAsync(server, ct);
        }

        // NTDS Settings
        var ntdsSettingsDn = $"CN=NTDS Settings,{serverDn}";
        var existingNtds = await _store.GetByDnAsync(options.TenantId, ntdsSettingsDn, ct);
        if (existingNtds == null)
        {
            var ntdsSettings = CreateBaseObject(options.TenantId, options.DomainDn,
                ntdsSettingsDn, ["top", "nTDSDSA"],
                $"CN=NTDS-DSA,{schemaDn}", serverDn);
            ntdsSettings.Cn = "NTDS Settings";
            ntdsSettings.SetAttribute("invocationId",
                new DirectoryAttribute("invocationId", Guid.NewGuid().ToString()));
            ntdsSettings.SetAttribute("options", new DirectoryAttribute("options", 1)); // IS_GC
            await StoreObjectAsync(ntdsSettings, ct);
        }

        WritePhaseComplete();

        // Create computer account for this DC (if not exists)
        WritePhase(3, "Creating DC computer account");
        var computerDn = $"CN={hostname},OU=Domain Controllers,{options.DomainDn}";
        var existingComputer = await _store.GetByDnAsync(options.TenantId, computerDn, ct);
        if (existingComputer == null)
        {
            var computer = CreateBaseObject(options.TenantId, options.DomainDn, computerDn,
                ["top", "person", "organizationalPerson", "user", "computer"],
                $"CN=Computer,{schemaDn}", $"OU=Domain Controllers,{options.DomainDn}");
            computer.Cn = hostname;
            computer.SAMAccountName = $"{hostname}$";
            computer.DnsHostName = $"{hostname.ToLowerInvariant()}.{options.DomainName}";
            computer.ObjectSid = await _ridAllocator.GenerateObjectSidAsync(options.TenantId, options.DomainDn, ct);
            computer.SetAttribute("userAccountControl",
                new DirectoryAttribute("userAccountControl", 532480));
            computer.SetAttribute("serverReferenceBL",
                new DirectoryAttribute("serverReferenceBL", serverDn));
            await StoreObjectAsync(computer, ct);

            // Add to Domain Controllers group
            var dcGroupDn = $"CN=Domain Controllers,CN=Users,{options.DomainDn}";
            await AddGroupMemberAsync(options.TenantId, dcGroupDn, computerDn, ct);
        }
        WritePhaseComplete();

        // Update local configuration
        WritePhase(4, "Updating configuration");
        await UpdateConfigurationAsync(options, ct);
        WritePhaseComplete();

        _logger.LogInformation("Replica DC provisioning complete!");

        Console.WriteLine();
        Console.WriteLine("  \u2554\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2557");
        Console.WriteLine("  \u2551          Replica DC Provisioning Complete!              \u2551");
        Console.WriteLine("  \u255a\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u255d");
        Console.WriteLine();
        Console.WriteLine($"  Domain:         {options.DomainName}");
        Console.WriteLine($"  Hostname:       {hostname}");
        Console.WriteLine($"  Site:           {options.SiteName}");
        Console.WriteLine($"  Server DN:      {serverDn}");
        Console.WriteLine($"  Domain SID:     {domainSid}");
        Console.WriteLine();
        Console.WriteLine($"  Objects created: {_objectCount}");
        Console.WriteLine();
        Console.WriteLine("  To start this replica domain controller, run:");
        Console.WriteLine("    dotnet run");
        Console.WriteLine();
    }

    // ── Phase 1: Naming Context Roots ──────────────────────────────────────

    private async Task CreateDomainRootAsync(SetupOptions options, string domainSid, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var firstDc = options.DomainName.Split('.')[0];

        var domainRoot = new DirectoryObject
        {
            Id = options.DomainDn.ToLowerInvariant(),
            TenantId = options.TenantId,
            DomainDn = options.DomainDn,
            DistinguishedName = options.DomainDn,
            ObjectGuid = Guid.NewGuid().ToString(),
            ObjectSid = domainSid,
            ObjectClass = ["top", "domain", "domainDNS"],
            ObjectCategory = $"CN=Domain-DNS,{options.SchemaDn}",
            Cn = firstDc,
            ParentDn = "",
            WhenCreated = now,
            WhenChanged = now,
            GPLink = $"[LDAP://CN={{31B2F340-016D-11D2-945F-00C04FB984F9}},CN=Policies,CN=System,{options.DomainDn};0]",
            NTSecurityDescriptor = BuildDomainRootSd(domainSid),
        };

        domainRoot.SetAttribute("dc", new DirectoryAttribute("dc", firstDc));
        domainRoot.SetAttribute("name", new DirectoryAttribute("name", firstDc));
        domainRoot.SetAttribute("instanceType", new DirectoryAttribute("instanceType", 5));
        domainRoot.SetAttribute("msDS-Behavior-Version", new DirectoryAttribute("msDS-Behavior-Version", options.DomainFunctionalLevel));

        // Password policy attributes on the domain object
        domainRoot.SetAttribute("minPwdLength", new DirectoryAttribute("minPwdLength", 7));
        domainRoot.SetAttribute("pwdHistoryLength", new DirectoryAttribute("pwdHistoryLength", 24));
        domainRoot.SetAttribute("maxPwdAge", new DirectoryAttribute("maxPwdAge", -(42L * 24 * 60 * 60 * 10000000L)));
        domainRoot.SetAttribute("minPwdAge", new DirectoryAttribute("minPwdAge", -(1L * 24 * 60 * 60 * 10000000L)));
        domainRoot.SetAttribute("pwdProperties", new DirectoryAttribute("pwdProperties", 1));
        domainRoot.SetAttribute("lockoutThreshold", new DirectoryAttribute("lockoutThreshold", 0));
        domainRoot.SetAttribute("lockoutDuration", new DirectoryAttribute("lockoutDuration", -(30L * 60 * 10000000L)));
        domainRoot.SetAttribute("lockoutObservationWindow", new DirectoryAttribute("lockoutObservationWindow", -(30L * 60 * 10000000L)));

        await StoreObjectAsync(domainRoot, ct);
    }

    private async Task CreateConfigurationNcAsync(SetupOptions options, string domainSid, CancellationToken ct)
    {
        var configRoot = CreateBaseObject(
            options.TenantId,
            options.DomainDn,
            options.ConfigurationDn,
            ["top", "configuration"],
            $"CN=Configuration,{options.SchemaDn}",
            options.DomainDn
        );
        configRoot.SetAttribute("instanceType", new DirectoryAttribute("instanceType", 5));
        configRoot.Cn = "Configuration";

        await StoreObjectAsync(configRoot, ct);
    }

    private async Task CreateSchemaNcAsync(SetupOptions options, string domainSid, CancellationToken ct)
    {
        var schemaRoot = CreateBaseObject(
            options.TenantId,
            options.DomainDn,
            options.SchemaDn,
            ["top", "dMD"],
            $"CN=dMD,{options.SchemaDn}",
            options.ConfigurationDn
        );
        schemaRoot.SetAttribute("instanceType", new DirectoryAttribute("instanceType", 5));
        schemaRoot.Cn = "Schema";

        await StoreObjectAsync(schemaRoot, ct);
    }

    // ── Phase 2: Well-Known Containers ─────────────────────────────────────

    private async Task CreateWellKnownContainersAsync(SetupOptions options, string domainSid, CancellationToken ct)
    {
        var domainDn = options.DomainDn;
        var configDn = options.ConfigurationDn;

        // Domain containers
        await CreateContainerAsync(options, $"CN=Users,{domainDn}", ["top", "container"], domainDn,
            wellKnownGuid: WellKnownGuids.Users, ct: ct);

        await CreateContainerAsync(options, $"CN=Computers,{domainDn}", ["top", "container"], domainDn,
            wellKnownGuid: WellKnownGuids.Computers, ct: ct);

        var dcOu = CreateBaseObject(options.TenantId, domainDn,
            $"OU=Domain Controllers,{domainDn}", ["top", "organizationalUnit"],
            $"CN=Organizational-Unit,{options.SchemaDn}", domainDn);
        dcOu.Cn = "Domain Controllers";
        await StoreObjectAsync(dcOu, ct);

        await CreateContainerAsync(options, $"CN=System,{domainDn}", ["top", "container"], domainDn,
            wellKnownGuid: WellKnownGuids.System, ct: ct);

        var builtin = CreateBaseObject(options.TenantId, domainDn,
            $"CN=Builtin,{domainDn}", ["top", "builtinDomain"],
            $"CN=Builtin-Domain,{options.SchemaDn}", domainDn);
        builtin.ObjectSid = "S-1-5-32";
        builtin.Cn = "Builtin";
        await StoreObjectAsync(builtin, ct);

        await CreateContainerAsync(options, $"CN=ForeignSecurityPrincipals,{domainDn}", ["top", "container"], domainDn,
            wellKnownGuid: WellKnownGuids.ForeignSecurityPrincipals, ct: ct);

        await CreateContainerAsync(options, $"CN=Managed Service Accounts,{domainDn}", ["top", "container"], domainDn, ct: ct);

        var infra = CreateBaseObject(options.TenantId, domainDn,
            $"CN=Infrastructure,{domainDn}", ["top", "infrastructureUpdate"],
            $"CN=Infrastructure-Update,{options.SchemaDn}", domainDn);
        infra.Cn = "Infrastructure";
        await StoreObjectAsync(infra, ct);

        await CreateContainerAsync(options, $"CN=LostAndFound,{domainDn}", ["top", "lostAndFound"], domainDn,
            wellKnownGuid: WellKnownGuids.LostAndFound, ct: ct);

        await CreateContainerAsync(options, $"CN=NTDS Quotas,{domainDn}", ["top", "msDS-QuotaContainer"], domainDn,
            wellKnownGuid: WellKnownGuids.NtdsQuotas, ct: ct);

        await CreateContainerAsync(options, $"CN=Policies,CN=System,{domainDn}", ["top", "container"],
            $"CN=System,{domainDn}", ct: ct);

        await CreateContainerAsync(options, $"CN=Password Settings Container,CN=System,{domainDn}",
            ["top", "msDS-PasswordSettingsContainer"], $"CN=System,{domainDn}", ct: ct);

        await CreateContainerAsync(options, $"CN=MicrosoftDNS,CN=System,{domainDn}", ["top", "container"],
            $"CN=System,{domainDn}", ct: ct);

        var deletedObjects = CreateBaseObject(options.TenantId, domainDn,
            $"CN=Deleted Objects,{domainDn}", ["top", "container"],
            $"CN=Container,{options.SchemaDn}", domainDn);
        deletedObjects.WellKnownGuid = WellKnownGuids.DeletedObjects;
        deletedObjects.SystemFlags = 0x02000000; // FLAG_DISALLOW_DELETE | hide from normal searches
        deletedObjects.Cn = "Deleted Objects";
        await StoreObjectAsync(deletedObjects, ct);

        // Configuration containers
        await CreateContainerAsync(options, $"CN=Sites,{configDn}", ["top", "sitesContainer"], configDn, ct: ct);
        await CreateContainerAsync(options, $"CN=Services,{configDn}", ["top", "container"], configDn, ct: ct);
        await CreateContainerAsync(options, $"CN=Partitions,{configDn}", ["top", "container"], configDn, ct: ct);
        await CreateContainerAsync(options, $"CN=DisplaySpecifiers,{configDn}", ["top", "container"], configDn, ct: ct);
        await CreateContainerAsync(options, $"CN=Extended-Rights,{configDn}", ["top", "container"], configDn, ct: ct);
    }

    // ── Phase 3: Built-in Security Principals ──────────────────────────────

    private async Task CreateBuiltInAccountsAsync(SetupOptions options, string domainSid, CancellationToken ct)
    {
        var builtinDn = $"CN=Builtin,{options.DomainDn}";

        await CreateBuiltinGroupAsync(options, "Administrators", "S-1-5-32-544", builtinDn,
            "Built-in account for administering the computer/domain", ct);
        await CreateBuiltinGroupAsync(options, "Users", "S-1-5-32-545", builtinDn,
            "Built-in group for regular users", ct);
        await CreateBuiltinGroupAsync(options, "Guests", "S-1-5-32-546", builtinDn,
            "Built-in group for guest access", ct);
        await CreateBuiltinGroupAsync(options, "Account Operators", "S-1-5-32-548", builtinDn,
            "Built-in group for managing user accounts", ct);
        await CreateBuiltinGroupAsync(options, "Server Operators", "S-1-5-32-549", builtinDn,
            "Built-in group for server administration", ct);
        await CreateBuiltinGroupAsync(options, "Print Operators", "S-1-5-32-550", builtinDn,
            "Built-in group for managing printers", ct);
        await CreateBuiltinGroupAsync(options, "Backup Operators", "S-1-5-32-551", builtinDn,
            "Built-in group for backup and restore", ct);
        await CreateBuiltinGroupAsync(options, "Pre-Windows 2000 Compatible Access", "S-1-5-32-554", builtinDn,
            "Backward compatibility group", ct);
        await CreateBuiltinGroupAsync(options, "Remote Desktop Users", "S-1-5-32-555", builtinDn,
            "Built-in group for remote desktop access", ct);
    }

    // ── Phase 4: Default Domain Groups ─────────────────────────────────────

    private async Task CreateDefaultGroupsAsync(SetupOptions options, string domainSid, CancellationToken ct)
    {
        var usersDn = $"CN=Users,{options.DomainDn}";

        // Global security groups (groupType = -2147483646)
        const int globalSecurity = -2147483646;
        // Universal security groups (groupType = -2147483640)
        const int universalSecurity = -2147483640;

        await CreateDomainGroupAsync(options, "Domain Admins", $"{domainSid}-512", usersDn,
            globalSecurity, "Designated administrators of the domain",
            samAccountType: 0x10000000, ct: ct);
        await CreateDomainGroupAsync(options, "Domain Users", $"{domainSid}-513", usersDn,
            globalSecurity, "All domain users",
            samAccountType: 0x10000000, ct: ct);
        await CreateDomainGroupAsync(options, "Domain Guests", $"{domainSid}-514", usersDn,
            globalSecurity, "All domain guests",
            samAccountType: 0x10000000, ct: ct);
        await CreateDomainGroupAsync(options, "Domain Computers", $"{domainSid}-515", usersDn,
            globalSecurity, "All workstations and servers joined to the domain",
            samAccountType: 0x10000000, ct: ct);
        await CreateDomainGroupAsync(options, "Domain Controllers", $"{domainSid}-516", usersDn,
            globalSecurity, "All domain controllers in the domain",
            samAccountType: 0x10000000, ct: ct);
        await CreateDomainGroupAsync(options, "Cert Publishers", $"{domainSid}-517", usersDn,
            globalSecurity, "Certificate publishers",
            samAccountType: 0x10000000, ct: ct);
        await CreateDomainGroupAsync(options, "Schema Admins", $"{domainSid}-518", usersDn,
            universalSecurity, "Designated administrators of the schema",
            samAccountType: 0x10000000, ct: ct);
        await CreateDomainGroupAsync(options, "Enterprise Admins", $"{domainSid}-519", usersDn,
            universalSecurity, "Designated administrators of the enterprise",
            samAccountType: 0x10000000, ct: ct);
        await CreateDomainGroupAsync(options, "Group Policy Creator Owners", $"{domainSid}-520", usersDn,
            globalSecurity, "Members can modify group policy for the domain",
            samAccountType: 0x10000000, ct: ct);
        await CreateDomainGroupAsync(options, "Allowed RODC Password Replication Group", $"{domainSid}-571", usersDn,
            globalSecurity, "Members can have passwords replicated to RODCs",
            samAccountType: 0x10000000, ct: ct);
        await CreateDomainGroupAsync(options, "Denied RODC Password Replication Group", $"{domainSid}-572", usersDn,
            globalSecurity, "Members cannot have passwords replicated to RODCs",
            samAccountType: 0x10000000, ct: ct);
        await CreateDomainGroupAsync(options, "Protected Users", $"{domainSid}-525", usersDn,
            globalSecurity, "Members of this group are afforded additional protections",
            samAccountType: 0x10000000, ct: ct);
    }

    // ── Phase 5: Administrator Account ─────────────────────────────────────

    private async Task CreateAdministratorAsync(SetupOptions options, string domainSid, CancellationToken ct)
    {
        var adminDn = $"CN={options.AdminUsername},CN=Users,{options.DomainDn}";
        var admin = CreateBaseObject(
            options.TenantId,
            options.DomainDn,
            adminDn,
            ["top", "person", "organizationalPerson", "user"],
            $"CN=Person,{options.SchemaDn}",
            $"CN=Users,{options.DomainDn}"
        );

        admin.ObjectSid = $"{domainSid}-500";
        admin.SAMAccountName = options.AdminUsername;
        admin.UserPrincipalName = $"{options.AdminUsername}@{options.DomainName}";
        admin.UserAccountControl = 512; // NORMAL_ACCOUNT
        admin.PrimaryGroupId = 513; // Domain Users
        admin.Cn = options.AdminUsername;
        admin.DisplayName = options.AdminUsername;
        admin.SAMAccountType = 0x30000000; // SAM_USER_OBJECT

        admin.SetAttribute("adminCount", new DirectoryAttribute("adminCount", 1));
        admin.SetAttribute("isCriticalSystemObject", new DirectoryAttribute("isCriticalSystemObject", true));
        admin.SetAttribute("name", new DirectoryAttribute("name", options.AdminUsername));

        await StoreObjectAsync(admin, ct);

        // Set password
        await _passwordPolicy.SetPasswordAsync(options.TenantId, adminDn, options.AdminPassword, ct);
        _logger.LogInformation("Administrator password configured");
    }

    // ── Phase 6: krbtgt Account ────────────────────────────────────────────

    private async Task CreateKrbtgtAsync(SetupOptions options, string domainSid, CancellationToken ct)
    {
        var krbtgtDn = $"CN=krbtgt,CN=Users,{options.DomainDn}";
        var krbtgt = CreateBaseObject(
            options.TenantId,
            options.DomainDn,
            krbtgtDn,
            ["top", "person", "organizationalPerson", "user"],
            $"CN=Person,{options.SchemaDn}",
            $"CN=Users,{options.DomainDn}"
        );

        krbtgt.ObjectSid = $"{domainSid}-502";
        krbtgt.SAMAccountName = "krbtgt";
        krbtgt.UserAccountControl = 514; // NORMAL_ACCOUNT | ACCOUNTDISABLE
        krbtgt.PrimaryGroupId = 513;
        krbtgt.Cn = "krbtgt";
        krbtgt.DisplayName = "krbtgt";
        krbtgt.SAMAccountType = 0x30000000;

        krbtgt.SetAttribute("isCriticalSystemObject", new DirectoryAttribute("isCriticalSystemObject", true));
        krbtgt.SetAttribute("name", new DirectoryAttribute("name", "krbtgt"));

        await StoreObjectAsync(krbtgt, ct);

        // Generate random password for krbtgt
        var randomPassword = GenerateRandomPassword(32);
        await _passwordPolicy.SetPasswordAsync(options.TenantId, krbtgtDn, randomPassword, ct);
        _logger.LogInformation("krbtgt account keys generated");
    }

    // ── Phase 7: Site Topology ─────────────────────────────────────────────

    private async Task CreateSiteTopologyAsync(SetupOptions options, string domainSid, CancellationToken ct)
    {
        var configDn = options.ConfigurationDn;
        var siteName = options.SiteName;
        var hostname = Environment.MachineName.ToUpperInvariant();

        var sitesDn = $"CN=Sites,{configDn}";
        var siteDn = $"CN={siteName},{sitesDn}";
        var serversDn = $"CN=Servers,{siteDn}";
        var serverDn = $"CN={hostname},{serversDn}";

        // Site object
        var site = CreateBaseObject(options.TenantId, options.DomainDn, siteDn, ["top", "site"],
            $"CN=Site,{options.SchemaDn}", sitesDn);
        site.Cn = siteName;
        await StoreObjectAsync(site, ct);

        // NTDS Site Settings
        var siteSettings = CreateBaseObject(options.TenantId, options.DomainDn,
            $"CN=NTDS Site Settings,{siteDn}", ["top", "nTDSSiteSettings"],
            $"CN=NTDS-Site-Settings,{options.SchemaDn}", siteDn);
        siteSettings.Cn = "NTDS Site Settings";
        await StoreObjectAsync(siteSettings, ct);

        // Servers container
        var serversContainer = CreateBaseObject(options.TenantId, options.DomainDn, serversDn, ["top", "serversContainer"],
            $"CN=Servers-Container,{options.SchemaDn}", siteDn);
        serversContainer.Cn = "Servers";
        await StoreObjectAsync(serversContainer, ct);

        // Server object
        var server = CreateBaseObject(options.TenantId, options.DomainDn, serverDn, ["top", "server"],
            $"CN=Server,{options.SchemaDn}", serversDn);
        server.Cn = hostname;
        server.DnsHostName = $"{hostname.ToLowerInvariant()}.{options.DomainName}";
        await StoreObjectAsync(server, ct);

        // NTDS Settings — this is the first DC, so it holds all FSMO roles
        var ntdsSettingsDn = $"CN=NTDS Settings,{serverDn}";
        var ntdsSettings = CreateBaseObject(options.TenantId, options.DomainDn,
            ntdsSettingsDn, ["top", "nTDSDSA"],
            $"CN=NTDS-DSA,{options.SchemaDn}", serverDn);
        ntdsSettings.Cn = "NTDS Settings";
        ntdsSettings.SetAttribute("options", new DirectoryAttribute("options", 1)); // IS_GC
        ntdsSettings.SetAttribute("invocationId",
            new DirectoryAttribute("invocationId", Guid.NewGuid().ToString()));
        await StoreObjectAsync(ntdsSettings, ct);

        // Set FSMO role holders — all five roles go to the first DC
        // RID Master: stored on the RID Manager object
        var ridManagerDn = $"CN=RID Manager$,CN=System,{options.DomainDn}";
        var ridManager = CreateBaseObject(options.TenantId, options.DomainDn, ridManagerDn,
            ["top", "rIDManager"], $"CN=RID-Manager,{options.SchemaDn}", $"CN=System,{options.DomainDn}");
        ridManager.Cn = "RID Manager$";
        ridManager.SetAttribute("fSMORoleOwner", new DirectoryAttribute("fSMORoleOwner", ntdsSettingsDn));
        await StoreObjectAsync(ridManager, ct);

        // PDC Emulator & Infrastructure: stored on domain root
        var domainRoot = await _store.GetByDnAsync(options.TenantId, options.DomainDn, ct);
        if (domainRoot != null)
        {
            domainRoot.SetAttribute("fSMORoleOwner", new DirectoryAttribute("fSMORoleOwner", ntdsSettingsDn));
            await _store.UpdateAsync(domainRoot, ct);
        }

        // Domain Naming Master: stored on Partitions container
        var partitionsObj = await _store.GetByDnAsync(options.TenantId, $"CN=Partitions,{configDn}", ct);
        if (partitionsObj != null)
        {
            partitionsObj.SetAttribute("fSMORoleOwner", new DirectoryAttribute("fSMORoleOwner", ntdsSettingsDn));
            await _store.UpdateAsync(partitionsObj, ct);
        }

        // Schema Master: stored on Schema NC root
        var schemaRoot = await _store.GetByDnAsync(options.TenantId, options.SchemaDn, ct);
        if (schemaRoot != null)
        {
            schemaRoot.SetAttribute("fSMORoleOwner", new DirectoryAttribute("fSMORoleOwner", ntdsSettingsDn));
            await _store.UpdateAsync(schemaRoot, ct);
        }

        // Infrastructure Master: stored on Infrastructure object
        var infraObj = await _store.GetByDnAsync(options.TenantId, $"CN=Infrastructure,{options.DomainDn}", ct);
        if (infraObj != null)
        {
            infraObj.SetAttribute("fSMORoleOwner", new DirectoryAttribute("fSMORoleOwner", ntdsSettingsDn));
            await _store.UpdateAsync(infraObj, ct);
        }

        // CrossRef objects in Partitions
        var partitionsDn = $"CN=Partitions,{configDn}";

        // Domain partition crossRef
        var domainCrossRef = CreateBaseObject(options.TenantId, options.DomainDn,
            $"CN={options.NetBiosName},{partitionsDn}", ["top", "crossRef"],
            $"CN=Cross-Ref,{options.SchemaDn}", partitionsDn);
        domainCrossRef.Cn = options.NetBiosName;
        domainCrossRef.SetAttribute("nCName", new DirectoryAttribute("nCName", options.DomainDn));
        domainCrossRef.SetAttribute("dnsRoot", new DirectoryAttribute("dnsRoot", options.DomainName));
        domainCrossRef.SetAttribute("netBIOSName", new DirectoryAttribute("netBIOSName", options.NetBiosName));
        domainCrossRef.SetAttribute("systemFlags", new DirectoryAttribute("systemFlags", 3));
        await StoreObjectAsync(domainCrossRef, ct);

        // Configuration partition crossRef
        var configCrossRef = CreateBaseObject(options.TenantId, options.DomainDn,
            $"CN=Configuration,{partitionsDn}", ["top", "crossRef"],
            $"CN=Cross-Ref,{options.SchemaDn}", partitionsDn);
        configCrossRef.Cn = "Configuration";
        configCrossRef.SetAttribute("nCName", new DirectoryAttribute("nCName", configDn));
        configCrossRef.SetAttribute("dnsRoot", new DirectoryAttribute("dnsRoot", options.DomainName));
        configCrossRef.SetAttribute("systemFlags", new DirectoryAttribute("systemFlags", 1));
        await StoreObjectAsync(configCrossRef, ct);

        // Schema partition crossRef
        var schemaCrossRef = CreateBaseObject(options.TenantId, options.DomainDn,
            $"CN=Schema,{partitionsDn}", ["top", "crossRef"],
            $"CN=Cross-Ref,{options.SchemaDn}", partitionsDn);
        schemaCrossRef.Cn = "Schema";
        schemaCrossRef.SetAttribute("nCName", new DirectoryAttribute("nCName", options.SchemaDn));
        schemaCrossRef.SetAttribute("dnsRoot", new DirectoryAttribute("dnsRoot", options.DomainName));
        schemaCrossRef.SetAttribute("systemFlags", new DirectoryAttribute("systemFlags", 1));
        await StoreObjectAsync(schemaCrossRef, ct);
    }

    // ── Phase 8: Default GPOs ──────────────────────────────────────────────

    private async Task CreateDefaultGpoAsync(SetupOptions options, string domainSid, CancellationToken ct)
    {
        var policiesDn = $"CN=Policies,CN=System,{options.DomainDn}";

        // Default Domain Policy
        var ddpGuid = "{31B2F340-016D-11D2-945F-00C04FB984F9}";
        var ddpDn = $"CN={ddpGuid},{policiesDn}";
        var ddp = CreateBaseObject(options.TenantId, options.DomainDn, ddpDn,
            ["top", "groupPolicyContainer"],
            $"CN=Group-Policy-Container,{options.SchemaDn}", policiesDn);
        ddp.Cn = ddpGuid;
        ddp.DisplayName = "Default Domain Policy";
        ddp.GPCFileSysPath = $"\\\\{options.DomainName}\\sysvol\\{options.DomainName}\\Policies\\{ddpGuid}";
        ddp.SetAttribute("versionNumber", new DirectoryAttribute("versionNumber", 0));
        ddp.SetAttribute("gPCFunctionalityVersion", new DirectoryAttribute("gPCFunctionalityVersion", 2));
        await StoreObjectAsync(ddp, ct);

        // Default Domain Controllers Policy
        var ddcpGuid = "{6AC1786C-016F-11D2-945F-00C04FB984F9}";
        var ddcpDn = $"CN={ddcpGuid},{policiesDn}";
        var ddcp = CreateBaseObject(options.TenantId, options.DomainDn, ddcpDn,
            ["top", "groupPolicyContainer"],
            $"CN=Group-Policy-Container,{options.SchemaDn}", policiesDn);
        ddcp.Cn = ddcpGuid;
        ddcp.DisplayName = "Default Domain Controllers Policy";
        ddcp.GPCFileSysPath = $"\\\\{options.DomainName}\\sysvol\\{options.DomainName}\\Policies\\{ddcpGuid}";
        ddcp.SetAttribute("versionNumber", new DirectoryAttribute("versionNumber", 0));
        ddcp.SetAttribute("gPCFunctionalityVersion", new DirectoryAttribute("gPCFunctionalityVersion", 2));
        await StoreObjectAsync(ddcp, ct);

        // Link DC policy to Domain Controllers OU
        var dcOuDn = $"OU=Domain Controllers,{options.DomainDn}";
        var dcOu = await _store.GetByDnAsync(options.TenantId, dcOuDn, ct);
        if (dcOu != null)
        {
            dcOu.GPLink = $"[LDAP://{ddcpDn};0]";
            await _store.UpdateAsync(dcOu, ct);
        }
    }

    // ── Phase 9: Group Memberships ─────────────────────────────────────────

    private async Task ConfigureGroupMembershipsAsync(SetupOptions options, string domainSid, CancellationToken ct)
    {
        var adminDn = $"CN={options.AdminUsername},CN=Users,{options.DomainDn}";
        var domainAdminsDn = $"CN=Domain Admins,CN=Users,{options.DomainDn}";
        var enterpriseAdminsDn = $"CN=Enterprise Admins,CN=Users,{options.DomainDn}";
        var schemaAdminsDn = $"CN=Schema Admins,CN=Users,{options.DomainDn}";
        var gpCreatorsDn = $"CN=Group Policy Creator Owners,CN=Users,{options.DomainDn}";
        var builtinAdminsDn = $"CN=Administrators,CN=Builtin,{options.DomainDn}";

        // Add Domain Admins to Builtin\Administrators
        await AddGroupMemberAsync(options.TenantId, builtinAdminsDn, domainAdminsDn, ct);

        // Add Enterprise Admins to Builtin\Administrators
        await AddGroupMemberAsync(options.TenantId, builtinAdminsDn, enterpriseAdminsDn, ct);

        // Add Administrator to Domain Admins
        await AddGroupMemberAsync(options.TenantId, domainAdminsDn, adminDn, ct);

        // Add Administrator to Enterprise Admins
        await AddGroupMemberAsync(options.TenantId, enterpriseAdminsDn, adminDn, ct);

        // Add Administrator to Schema Admins
        await AddGroupMemberAsync(options.TenantId, schemaAdminsDn, adminDn, ct);

        // Add Administrator to Group Policy Creator Owners
        await AddGroupMemberAsync(options.TenantId, gpCreatorsDn, adminDn, ct);
    }

    // ── Phase 10: Configuration Update ─────────────────────────────────────

    private async Task UpdateConfigurationAsync(SetupOptions options, CancellationToken ct)
    {
        var configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

        // Also try the source directory (for development)
        var sourceConfigPath = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "appsettings.json");
        if (File.Exists(sourceConfigPath))
            configPath = Path.GetFullPath(sourceConfigPath);

        if (!File.Exists(configPath))
        {
            _logger.LogWarning("Could not find appsettings.json at {Path}, skipping configuration update", configPath);
            return;
        }

        var json = await File.ReadAllTextAsync(configPath, ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Build updated config using mutable dictionary approach
        var config = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? [];

        // Update NamingContexts
        var namingContexts = new Dictionary<string, object>
        {
            ["DomainDn"] = options.DomainDn,
            ["ForestDnsName"] = options.DomainName,
            ["DomainSid"] = _ridAllocator.GetDomainSid(options.TenantId, options.DomainDn)
        };
        config["NamingContexts"] = JsonSerializer.SerializeToElement(namingContexts);

        // Update Ldap
        var ldap = new Dictionary<string, object>
        {
            ["Port"] = 389,
            ["TlsPort"] = 636,
            ["MaxConnections"] = 10000,
            ["MaxPageSize"] = 1000,
            ["DefaultTenantId"] = options.TenantId,
            ["DefaultDomain"] = options.DomainDn
        };
        config["Ldap"] = JsonSerializer.SerializeToElement(ldap);

        // Update Kerberos realm
        var kerberos = new Dictionary<string, object>
        {
            ["DefaultRealm"] = options.DomainName.ToUpperInvariant(),
            ["Port"] = 88,
            ["MaximumSkew"] = "00:05:00",
            ["SessionLifetime"] = "10:00:00",
            ["MaximumRenewalWindow"] = "7.00:00:00"
        };
        config["Kerberos"] = JsonSerializer.SerializeToElement(kerberos);

        // Update DNS
        var hostname = Environment.MachineName.ToLowerInvariant();
        var forestName = string.IsNullOrEmpty(options.ForestName) ? options.DomainName : options.ForestName;
        var dns = new Dictionary<string, object>
        {
            ["Port"] = 53,
            ["ServerHostname"] = $"{hostname}.{options.DomainName}",
            ["ServerIpAddresses"] = new[] { "127.0.0.1" },
            ["Domains"] = new[] { options.DomainName },
            ["ForestDnsName"] = forestName,
            ["DefaultTtl"] = 600
        };
        config["Dns"] = JsonSerializer.SerializeToElement(dns);

        // Update RpcServer if present
        var rpcServer = new Dictionary<string, object>
        {
            ["EndpointMapperPort"] = 135,
            ["ServicePort"] = 49664,
            ["MaxConnections"] = 256,
            ["TenantId"] = options.TenantId,
            ["DomainDn"] = options.DomainDn
        };
        config["RpcServer"] = JsonSerializer.SerializeToElement(rpcServer);

        // DC Node configuration (per-node settings for multi-DC)
        var dcNode = new Dictionary<string, object>
        {
            ["Hostname"] = hostname,
            ["SiteName"] = options.SiteName,
            ["TenantId"] = options.TenantId,
            ["DomainDn"] = options.DomainDn,
            ["BindAddresses"] = new[] { "0.0.0.0" }
        };
        config["DcNode"] = JsonSerializer.SerializeToElement(dcNode);

        // Preserve existing CosmosDb settings
        if (!config.ContainsKey("CosmosDb"))
        {
            var cosmosDb = new Dictionary<string, object>
            {
                ["ConnectionString"] = options.CosmosConnectionString ?? "AccountEndpoint=https://localhost:8081;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==",
                ["DatabaseName"] = options.CosmosDatabaseName ?? "DirectoryService",
                ["DefaultThroughput"] = 1000
            };
            config["CosmosDb"] = JsonSerializer.SerializeToElement(cosmosDb);
        }

        var writeOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = null
        };

        var updatedJson = JsonSerializer.Serialize(config, writeOptions);
        await File.WriteAllTextAsync(configPath, updatedJson, ct);

        _logger.LogInformation("Configuration updated at {Path}", configPath);
    }

    // ── Phase 11: DNS SRV Record Seeding ─────────────────────────────────

    /// <summary>
    /// Seeds the initial set of DC locator SRV and A records into the
    /// AD-integrated DNS zone store for the first DC, per MS-ADTS 6.3.6.
    /// </summary>
    private async Task SeedDnsSrvRecordsAsync(SetupOptions options, CancellationToken ct)
    {
        var domainDnsName = options.DomainName;
        var forestDnsName = string.IsNullOrEmpty(options.ForestName) ? domainDnsName : options.ForestName;
        var siteName = options.SiteName;
        var hostname = Environment.MachineName.ToLowerInvariant();
        var dcFqdn = $"{hostname}.{domainDnsName}";
        var tenantId = options.TenantId;

        // Generate a deterministic domain GUID from the domain DN
        var domainGuid = GenerateDomainGuid(options.DomainDn);

        // Ensure DNS zone container hierarchy exists:
        //   DC=DomainDnsZones,{domainDn}
        //     CN=MicrosoftDNS,DC=DomainDnsZones,{domainDn}
        //       DC={zoneName},CN=MicrosoftDNS,DC=DomainDnsZones,{domainDn}
        var zones = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { domainDnsName, forestDnsName };
        foreach (var zoneName in zones)
        {
            await EnsureDnsZoneContainerAsync(options, zoneName, ct);
        }

        var records = new List<(string ZoneName, DnsRecord Record)>();

        // ── LDAP SRV records ──
        AddSrvRecord(records, domainDnsName, $"_ldap._tcp.{domainDnsName}", 0, 100, 389, dcFqdn);
        AddSrvRecord(records, domainDnsName, $"_ldap._tcp.{siteName}._sites.{domainDnsName}", 0, 100, 389, dcFqdn);
        AddSrvRecord(records, domainDnsName, $"_ldap._tcp.dc._msdcs.{domainDnsName}", 0, 100, 389, dcFqdn);
        AddSrvRecord(records, domainDnsName, $"_ldap._tcp.{siteName}._sites.dc._msdcs.{domainDnsName}", 0, 100, 389, dcFqdn);
        AddSrvRecord(records, forestDnsName, $"_ldap._tcp.{domainGuid}.domains._msdcs.{forestDnsName}", 0, 100, 389, dcFqdn);

        // ── Kerberos SRV records ──
        AddSrvRecord(records, domainDnsName, $"_kerberos._tcp.{domainDnsName}", 0, 100, 88, dcFqdn);
        AddSrvRecord(records, domainDnsName, $"_kerberos._tcp.{siteName}._sites.{domainDnsName}", 0, 100, 88, dcFqdn);
        AddSrvRecord(records, domainDnsName, $"_kerberos._tcp.dc._msdcs.{domainDnsName}", 0, 100, 88, dcFqdn);
        AddSrvRecord(records, domainDnsName, $"_kerberos._tcp.{siteName}._sites.dc._msdcs.{domainDnsName}", 0, 100, 88, dcFqdn);
        AddSrvRecord(records, domainDnsName, $"_kerberos._udp.{domainDnsName}", 0, 100, 88, dcFqdn);

        // ── Kpasswd SRV records ──
        AddSrvRecord(records, domainDnsName, $"_kpasswd._tcp.{domainDnsName}", 0, 100, 464, dcFqdn);
        AddSrvRecord(records, domainDnsName, $"_kpasswd._udp.{domainDnsName}", 0, 100, 464, dcFqdn);

        // ── Global Catalog SRV records ──
        AddSrvRecord(records, domainDnsName, $"_gc._tcp.{domainDnsName}", 0, 100, 3268, dcFqdn);
        AddSrvRecord(records, forestDnsName, $"_gc._tcp.{forestDnsName}", 0, 100, 3268, dcFqdn);
        AddSrvRecord(records, forestDnsName, $"_gc._tcp.{siteName}._sites.{forestDnsName}", 0, 100, 3268, dcFqdn);
        AddSrvRecord(records, forestDnsName, $"_ldap._tcp.gc._msdcs.{forestDnsName}", 0, 100, 3268, dcFqdn);

        // ── PDC Emulator SRV record (first DC is always PDC) ──
        AddSrvRecord(records, domainDnsName, $"_ldap._tcp.pdc._msdcs.{domainDnsName}", 0, 100, 389, dcFqdn);

        // ── A records ──
        AddARecord(records, domainDnsName, dcFqdn, "127.0.0.1");
        AddARecord(records, forestDnsName, $"gc._msdcs.{forestDnsName}", "127.0.0.1");

        foreach (var (zoneName, record) in records)
        {
            try
            {
                await _dnsZoneStore.UpsertRecordAsync(tenantId, zoneName, record, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to seed DNS record {Name} in zone {Zone}", record.Name, zoneName);
            }
        }

        _logger.LogInformation("Seeded {Count} DNS records for first DC", records.Count);
    }

    /// <summary>
    /// Ensures the DNS zone container hierarchy exists in the directory for the given zone.
    /// Creates DC=DomainDnsZones, CN=MicrosoftDNS, and DC={zoneName} containers as needed.
    /// </summary>
    private async Task EnsureDnsZoneContainerAsync(SetupOptions options, string zoneName, CancellationToken ct)
    {
        var domainDn = options.DomainDn;

        // DC=DomainDnsZones,{domainDn}
        var dnsZonesDn = $"DC=DomainDnsZones,{domainDn}";
        if (await _store.GetByDnAsync(options.TenantId, dnsZonesDn, ct) == null)
        {
            var dnsZones = CreateBaseObject(options.TenantId, domainDn, dnsZonesDn,
                ["top", "domainDNS"], $"CN=Domain-DNS,{options.SchemaDn}", domainDn);
            dnsZones.Cn = "DomainDnsZones";
            await StoreObjectAsync(dnsZones, ct);
        }

        // CN=MicrosoftDNS,DC=DomainDnsZones,{domainDn}
        var microsoftDnsDn = $"CN=MicrosoftDNS,{dnsZonesDn}";
        if (await _store.GetByDnAsync(options.TenantId, microsoftDnsDn, ct) == null)
        {
            var microsoftDns = CreateBaseObject(options.TenantId, domainDn, microsoftDnsDn,
                ["top", "container"], $"CN=Container,{options.SchemaDn}", dnsZonesDn);
            microsoftDns.Cn = "MicrosoftDNS";
            await StoreObjectAsync(microsoftDns, ct);
        }

        // DC={zoneName},CN=MicrosoftDNS,DC=DomainDnsZones,{domainDn}
        var zoneContainerDn = $"DC={zoneName},{microsoftDnsDn}";
        if (await _store.GetByDnAsync(options.TenantId, zoneContainerDn, ct) == null)
        {
            var zoneContainer = CreateBaseObject(options.TenantId, domainDn, zoneContainerDn,
                ["top", "dnsZone"], $"CN=Dns-Zone,{options.SchemaDn}", microsoftDnsDn);
            zoneContainer.Cn = zoneName;
            zoneContainer.SetAttribute("dc", new DirectoryAttribute("dc", zoneName));
            await StoreObjectAsync(zoneContainer, ct);
        }
    }

    private static void AddSrvRecord(List<(string, DnsRecord)> records, string zoneName, string name,
        ushort priority, ushort weight, ushort port, string target)
    {
        records.Add((zoneName, new DnsRecord
        {
            Name = name,
            Type = DnsRecordType.SRV,
            Ttl = 600,
            Data = $"{priority} {weight} {port} {target}",
        }));
    }

    private static void AddARecord(List<(string, DnsRecord)> records, string zoneName, string name, string ip)
    {
        records.Add((zoneName, new DnsRecord
        {
            Name = name,
            Type = DnsRecordType.A,
            Ttl = 600,
            Data = ip,
        }));
    }

    /// <summary>
    /// Generates a deterministic GUID from the domain DN for use in
    /// _ldap._tcp.{DomainGuid}.domains._msdcs.{ForestDnsName} records.
    /// </summary>
    private static string GenerateDomainGuid(string domainDn)
    {
        var bytes = MD5.HashData(System.Text.Encoding.UTF8.GetBytes(domainDn.ToLowerInvariant()));
        return new Guid(bytes).ToString();
    }

    // ── Helper Methods ─────────────────────────────────────────────────────

    private DirectoryObject CreateBaseObject(
        string tenantId, string domainDn, string dn,
        List<string> objectClass, string objectCategory, string parentDn)
    {
        var now = DateTimeOffset.UtcNow;
        var cn = ExtractCn(dn);

        return new DirectoryObject
        {
            Id = dn.ToLowerInvariant(),
            TenantId = tenantId,
            DomainDn = domainDn,
            DistinguishedName = dn,
            ObjectGuid = Guid.NewGuid().ToString(),
            ObjectClass = objectClass,
            ObjectCategory = objectCategory,
            ParentDn = parentDn,
            Cn = cn,
            WhenCreated = now,
            WhenChanged = now,
        };
    }

    private async Task CreateContainerAsync(
        SetupOptions options, string dn, List<string> objectClass, string parentDn,
        string wellKnownGuid = null, CancellationToken ct = default)
    {
        var obj = CreateBaseObject(options.TenantId, options.DomainDn, dn, objectClass,
            $"CN=Container,{options.SchemaDn}", parentDn);

        if (wellKnownGuid != null)
            obj.WellKnownGuid = wellKnownGuid;

        await StoreObjectAsync(obj, ct);
    }

    private async Task CreateBuiltinGroupAsync(
        SetupOptions options, string name, string sid, string parentDn,
        string description, CancellationToken ct)
    {
        var dn = $"CN={name},{parentDn}";
        var group = CreateBaseObject(options.TenantId, options.DomainDn, dn,
            ["top", "group"],
            $"CN=Group,{options.SchemaDn}", parentDn);

        group.ObjectSid = sid;
        group.GroupType = -2147483643; // BUILTIN_LOCAL_GROUP
        group.Description = description;
        group.SAMAccountName = name;
        group.SAMAccountType = 0x20000000; // SAM_ALIAS_OBJECT

        await StoreObjectAsync(group, ct);
    }

    private async Task CreateDomainGroupAsync(
        SetupOptions options, string name, string sid, string parentDn,
        int groupType, string description, int samAccountType = 0x10000000,
        CancellationToken ct = default)
    {
        var dn = $"CN={name},{parentDn}";
        var group = CreateBaseObject(options.TenantId, options.DomainDn, dn,
            ["top", "group"],
            $"CN=Group,{options.SchemaDn}", parentDn);

        group.ObjectSid = sid;
        group.GroupType = groupType;
        group.Description = description;
        group.SAMAccountName = name;
        group.SAMAccountType = samAccountType;

        await StoreObjectAsync(group, ct);
    }

    private async Task AddGroupMemberAsync(string tenantId, string groupDn, string memberDn, CancellationToken ct)
    {
        var group = await _store.GetByDnAsync(tenantId, groupDn, ct);
        var member = await _store.GetByDnAsync(tenantId, memberDn, ct);

        if (group == null || member == null)
        {
            _logger.LogWarning("Could not add member {Member} to group {Group}: one or both not found", memberDn, groupDn);
            return;
        }

        if (!group.Member.Contains(memberDn))
        {
            group.Member.Add(memberDn);
            await _store.UpdateAsync(group, ct);
        }

        if (!member.MemberOf.Contains(groupDn))
        {
            member.MemberOf.Add(groupDn);
            await _store.UpdateAsync(member, ct);
        }
    }

    private async Task StoreObjectAsync(DirectoryObject obj, CancellationToken ct)
    {
        // Assign a default security descriptor if one hasn't been set already
        if (obj.NTSecurityDescriptor is null)
        {
            AssignDefaultSecurityDescriptor(obj);
        }

        await _store.CreateAsync(obj, ct);
        _objectCount++;
        _logger.LogDebug("Created: {Dn}", obj.DistinguishedName);
    }

    /// <summary>
    /// Compute and assign the default security descriptor for a provisioned object
    /// based on its object class's SDDL definition. The owner is set to Domain Admins
    /// for system-created objects. ACE inheritance from the parent is applied if possible.
    /// </summary>
    private void AssignDefaultSecurityDescriptor(DirectoryObject obj)
    {
        // Determine domain SID from the object's domain DN
        var domainSid = !string.IsNullOrEmpty(obj.DomainDn)
            ? _ridAllocator.GetDomainSid(obj.TenantId, obj.DomainDn)
            : string.Empty;

        // System-created objects are owned by Domain Admins
        var ownerSid = domainSid.Length > 0
            ? WellKnownSids.DomainAdmins(domainSid)
            : WellKnownSids.Administrators;

        // Get the structural class name
        var objectClassName = obj.ObjectClass.Count > 0 ? obj.ObjectClass[^1] : "top";

        // Build the default SD from the class's SDDL definition
        var sd = _acl.GetDefaultSecurityDescriptor(objectClassName, ownerSid, domainSid);

        // Inherit ACEs from parent if we can look it up synchronously
        // (parent was just created in previous provisioning step, so it's in the store)
        if (!string.IsNullOrEmpty(obj.ParentDn))
        {
            var parentObj = _store.GetByDnAsync(obj.TenantId, obj.ParentDn, CancellationToken.None)
                .GetAwaiter().GetResult();

            if (parentObj?.NTSecurityDescriptor is { Length: > 0 } parentSdBytes)
            {
                var parentSd = SecurityDescriptor.Deserialize(parentSdBytes);
                sd = _acl.InheritAces(parentSd, sd, objectClassName, _schemaService);
            }
        }

        obj.NTSecurityDescriptor = sd.Serialize();
    }

    private static string ExtractCn(string dn)
    {
        // Extract the CN/OU value from the first RDN component
        var firstComponent = dn.Split(',')[0];
        var eqIndex = firstComponent.IndexOf('=');
        return eqIndex >= 0 ? firstComponent[(eqIndex + 1)..] : firstComponent;
    }

    /// <summary>
    /// Build the domain root security descriptor using the domainDNS class's SDDL.
    /// </summary>
    private byte[] BuildDomainRootSd(string domainSid)
    {
        var ownerSid = $"{domainSid}-{WellKnownSids.DomainAdminsRid}";
        var sd = _acl.GetDefaultSecurityDescriptor("domainDNS", ownerSid, domainSid);
        sd.GroupSid = ownerSid;
        return sd.Serialize();
    }

    private static string GenerateRandomPassword(int length)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*";
        var bytes = RandomNumberGenerator.GetBytes(length);
        var result = new char[length];
        for (int i = 0; i < length; i++)
        {
            result[i] = chars[bytes[i] % chars.Length];
        }
        return new string(result);
    }

    private static void WritePhase(int number, string description)
    {
        Console.Write($"  [{number,2}/10] {description}...");
    }

    private static void WritePhaseComplete()
    {
        Console.WriteLine(" done");
    }
}
