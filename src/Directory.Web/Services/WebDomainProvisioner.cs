using System.Security.Cryptography;
using System.Text.Json;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Web.Models;

namespace Directory.Web.Services;

/// <summary>
/// Web-compatible domain provisioner adapted from Directory.Server's DomainProvisioner.
/// Reports progress via SetupStateService for the Vue frontend to poll.
/// </summary>
public class WebDomainProvisioner
{
    private readonly IDirectoryStore _store;
    private readonly IPasswordPolicy _passwordPolicy;
    private readonly IRidAllocator _ridAllocator;
    private readonly INamingContextService _namingContextService;
    private readonly ISchemaService _schemaService;
    private readonly SetupStateService _setupState;
    private readonly WebDomainConfigurationService _domainConfigService;
    private readonly ILogger<WebDomainProvisioner> _logger;
    private int _objectCount;

    public WebDomainProvisioner(
        IDirectoryStore store,
        IPasswordPolicy passwordPolicy,
        IRidAllocator ridAllocator,
        INamingContextService namingContextService,
        ISchemaService schemaService,
        SetupStateService setupState,
        WebDomainConfigurationService domainConfigService,
        ILogger<WebDomainProvisioner> logger)
    {
        _store = store;
        _passwordPolicy = passwordPolicy;
        _ridAllocator = ridAllocator;
        _namingContextService = namingContextService;
        _schemaService = schemaService;
        _setupState = setupState;
        _domainConfigService = domainConfigService;
        _logger = logger;
    }

    /// <summary>
    /// Provisions a new domain. Called from a background task so it does not block the HTTP request.
    /// Progress is reported through <see cref="SetupStateService"/>.
    /// </summary>
    public async Task ProvisionAsync(ProvisionRequest request, CancellationToken ct = default)
    {
        try
        {
            _setupState.IsProvisioning = true;
            _setupState.ProvisioningProgress = 0;
            _setupState.ProvisioningError = null;
            _setupState.ProvisioningPhase = "Starting...";

            var options = new SetupOptions
            {
                DomainName = request.DomainName,
                NetBiosName = request.NetBiosName,
                AdminPassword = request.AdminPassword,
                AdminUsername = request.AdminUsername,
                SiteName = request.SiteName,
                TenantId = "default",
            };

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
            ReportProgress(5, "Creating naming contexts...");
            await CreateDomainRootAsync(options, domainSid, ct);
            await CreateConfigurationNcAsync(options, domainSid, ct);
            await CreateSchemaNcAsync(options, domainSid, ct);

            // Phase 2: Create well-known containers
            ReportProgress(15, "Creating well-known containers...");
            await CreateWellKnownContainersAsync(options, domainSid, ct);

            // Phase 3: Create built-in security principals
            ReportProgress(25, "Creating built-in security principals...");
            await CreateBuiltInAccountsAsync(options, domainSid, ct);

            // Phase 4: Create default groups
            ReportProgress(40, "Creating default domain groups...");
            await CreateDefaultGroupsAsync(options, domainSid, ct);

            // Phase 5: Create Administrator account
            ReportProgress(55, "Creating Administrator account...");
            await CreateAdministratorAsync(options, domainSid, ct);

            // Phase 6: Create krbtgt account
            ReportProgress(65, "Creating Kerberos service account...");
            await CreateKrbtgtAsync(options, domainSid, ct);

            // Phase 7: Create site topology
            ReportProgress(75, "Creating site topology...");
            await CreateSiteTopologyAsync(options, domainSid, ct);

            // Phase 8: Create default GPOs
            ReportProgress(85, "Creating default Group Policy objects...");
            await CreateDefaultGpoAsync(options, domainSid, ct);

            // Phase 9: Set group memberships
            ReportProgress(90, "Configuring group memberships...");
            await ConfigureGroupMembershipsAsync(options, domainSid, ct);

            // Phase 10: Update configuration
            ReportProgress(95, "Updating configuration...");
            await UpdateConfigurationAsync(options, request, ct);

            // Reconfigure the NamingContextService singleton with the actual provisioned domain
            // so all subsequent queries (tree browsing, searches, etc.) use the correct DNs.
            _namingContextService.Reconfigure(options.DomainDn, options.DomainName, domainSid);

            // Reload domain configuration from the newly provisioned objects
            await _domainConfigService.ReloadAsync(ct);

            ReportProgress(100, "Complete!");

            _setupState.IsProvisioned = true;
            _setupState.IsProvisioning = false;

            _logger.LogInformation("Domain provisioning complete! Objects created: {Count}", _objectCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Domain provisioning failed");
            _setupState.ProvisioningError = ex.Message;
            _setupState.IsProvisioning = false;
            _setupState.ProvisioningPhase = "Failed";
        }
    }

    private void ReportProgress(int percent, string phase)
    {
        _setupState.ProvisioningProgress = percent;
        _setupState.ProvisioningPhase = phase;
        _logger.LogInformation("Provisioning: {Percent}% - {Phase}", percent, phase);
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
        deletedObjects.SystemFlags = 0x02000000;
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

        const int globalSecurity = -2147483646;
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

        // NTDS Settings
        var ntdsSettingsDn = $"CN=NTDS Settings,{serverDn}";
        var ntdsSettings = CreateBaseObject(options.TenantId, options.DomainDn,
            ntdsSettingsDn, ["top", "nTDSDSA"],
            $"CN=NTDS-DSA,{options.SchemaDn}", serverDn);
        ntdsSettings.Cn = "NTDS Settings";
        ntdsSettings.SetAttribute("options", new DirectoryAttribute("options", 1)); // IS_GC
        ntdsSettings.SetAttribute("invocationId",
            new DirectoryAttribute("invocationId", Guid.NewGuid().ToString()));
        await StoreObjectAsync(ntdsSettings, ct);

        // Set FSMO role holders
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

        // Infrastructure Master
        var infraObj = await _store.GetByDnAsync(options.TenantId, $"CN=Infrastructure,{options.DomainDn}", ct);
        if (infraObj != null)
        {
            infraObj.SetAttribute("fSMORoleOwner", new DirectoryAttribute("fSMORoleOwner", ntdsSettingsDn));
            await _store.UpdateAsync(infraObj, ct);
        }

        // CrossRef objects in Partitions
        var partitionsDn = $"CN=Partitions,{configDn}";

        var domainCrossRef = CreateBaseObject(options.TenantId, options.DomainDn,
            $"CN={options.NetBiosName},{partitionsDn}", ["top", "crossRef"],
            $"CN=Cross-Ref,{options.SchemaDn}", partitionsDn);
        domainCrossRef.Cn = options.NetBiosName;
        domainCrossRef.SetAttribute("nCName", new DirectoryAttribute("nCName", options.DomainDn));
        domainCrossRef.SetAttribute("dnsRoot", new DirectoryAttribute("dnsRoot", options.DomainName));
        domainCrossRef.SetAttribute("netBIOSName", new DirectoryAttribute("netBIOSName", options.NetBiosName));
        domainCrossRef.SetAttribute("systemFlags", new DirectoryAttribute("systemFlags", 3));
        await StoreObjectAsync(domainCrossRef, ct);

        var configCrossRef = CreateBaseObject(options.TenantId, options.DomainDn,
            $"CN=Configuration,{partitionsDn}", ["top", "crossRef"],
            $"CN=Cross-Ref,{options.SchemaDn}", partitionsDn);
        configCrossRef.Cn = "Configuration";
        configCrossRef.SetAttribute("nCName", new DirectoryAttribute("nCName", configDn));
        configCrossRef.SetAttribute("dnsRoot", new DirectoryAttribute("dnsRoot", options.DomainName));
        configCrossRef.SetAttribute("systemFlags", new DirectoryAttribute("systemFlags", 1));
        await StoreObjectAsync(configCrossRef, ct);

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

        await AddGroupMemberAsync(options.TenantId, builtinAdminsDn, domainAdminsDn, ct);
        await AddGroupMemberAsync(options.TenantId, builtinAdminsDn, enterpriseAdminsDn, ct);
        await AddGroupMemberAsync(options.TenantId, domainAdminsDn, adminDn, ct);
        await AddGroupMemberAsync(options.TenantId, enterpriseAdminsDn, adminDn, ct);
        await AddGroupMemberAsync(options.TenantId, schemaAdminsDn, adminDn, ct);
        await AddGroupMemberAsync(options.TenantId, gpCreatorsDn, adminDn, ct);
    }

    // ── Phase 10: Configuration Update ─────────────────────────────────────

    private async Task UpdateConfigurationAsync(SetupOptions options, ProvisionRequest request, CancellationToken ct)
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
        var config = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? [];

        // Update NamingContexts
        var namingContexts = new Dictionary<string, object>
        {
            ["DomainDn"] = options.DomainDn,
            ["ForestDnsName"] = options.DomainName,
            ["DomainSid"] = _ridAllocator.GetDomainSid(options.TenantId, options.DomainDn)
        };
        config["NamingContexts"] = JsonSerializer.SerializeToElement(namingContexts);

        // DC Node configuration
        var hostname = Environment.MachineName.ToLowerInvariant();
        var dcNode = new Dictionary<string, object>
        {
            ["Hostname"] = hostname,
            ["SiteName"] = options.SiteName,
            ["TenantId"] = options.TenantId,
            ["DomainDn"] = options.DomainDn,
            ["BindAddresses"] = new[] { "0.0.0.0" }
        };
        config["DcNode"] = JsonSerializer.SerializeToElement(dcNode);

        // Update CosmosDb if provided in request
        if (!string.IsNullOrEmpty(request.CosmosConnectionString))
        {
            var cosmosDb = new Dictionary<string, object>
            {
                ["ConnectionString"] = request.CosmosConnectionString,
                ["DatabaseName"] = request.CosmosDatabaseName ?? "DirectoryService",
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
        await _store.CreateAsync(obj, ct);
        _objectCount++;
        _logger.LogDebug("Created: {Dn}", obj.DistinguishedName);
    }

    private static string ExtractCn(string dn)
    {
        var firstComponent = dn.Split(',')[0];
        var eqIndex = firstComponent.IndexOf('=');
        return eqIndex >= 0 ? firstComponent[(eqIndex + 1)..] : firstComponent;
    }

    private static byte[] BuildDomainRootSd(string domainSid)
    {
        var sd = new SecurityDescriptor
        {
            OwnerSid = $"{domainSid}-{WellKnownSids.DomainAdminsRid}",
            GroupSid = $"{domainSid}-{WellKnownSids.DomainAdminsRid}",
            Control = SdControlFlags.DaclPresent | SdControlFlags.DaclProtected,
            Dacl = new AccessControlList
            {
                Aces =
                [
                    new AccessControlEntry
                    {
                        Type = AceType.AccessAllowed,
                        Flags = AceFlags.ContainerInherit | AceFlags.ObjectInherit,
                        Mask = AccessMask.GenericAll,
                        TrusteeSid = $"{domainSid}-{WellKnownSids.DomainAdminsRid}"
                    },
                    new AccessControlEntry
                    {
                        Type = AceType.AccessAllowed,
                        Flags = AceFlags.ContainerInherit | AceFlags.ObjectInherit,
                        Mask = AccessMask.GenericAll,
                        TrusteeSid = WellKnownSids.System
                    },
                    new AccessControlEntry
                    {
                        Type = AceType.AccessAllowed,
                        Flags = AceFlags.ContainerInherit | AceFlags.ObjectInherit,
                        Mask = AccessMask.ReadProperty | AccessMask.ListContents | AccessMask.ReadControl | AccessMask.ListObject,
                        TrusteeSid = WellKnownSids.AuthenticatedUsers
                    },
                    new AccessControlEntry
                    {
                        Type = AceType.AccessAllowed,
                        Flags = AceFlags.ContainerInherit | AceFlags.ObjectInherit,
                        Mask = AccessMask.GenericAll,
                        TrusteeSid = WellKnownSids.Administrators
                    },
                ]
            }
        };

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

    /// <summary>
    /// Internal SetupOptions class to avoid depending on Directory.Server.
    /// Mirrors the structure of Directory.Server.Setup.SetupOptions.
    /// </summary>
    private class SetupOptions
    {
        public string DomainName { get; set; } = "";
        public string NetBiosName { get; set; } = "";
        public string AdminPassword { get; set; } = "";
        public string TenantId { get; set; } = "default";
        public string AdminUsername { get; set; } = "Administrator";
        public string SiteName { get; set; } = "Default-First-Site-Name";
        public int ForestFunctionalLevel { get; set; } = 7;
        public int DomainFunctionalLevel { get; set; } = 7;

        public string DomainDn => string.Join(",", DomainName.Split('.').Select(p => $"DC={p}"));
        public string ConfigurationDn => $"CN=Configuration,{DomainDn}";
        public string SchemaDn => $"CN=Schema,CN=Configuration,{DomainDn}";
    }
}
