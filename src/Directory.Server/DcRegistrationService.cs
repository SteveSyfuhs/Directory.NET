using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Dns;

namespace Directory.Server;

/// <summary>
/// Registers this DC instance in the directory on startup and maintains a heartbeat.
/// Creates server objects, NTDS Settings, computer accounts, SPNs, and DNS SRV/A
/// records as needed. Other DCs discover active instances by querying the Sites
/// container and DNS zone store.
/// </summary>
public class DcRegistrationService : IHostedService, IDisposable
{
    private readonly DcInstanceInfo _dcInfo;
    private readonly IDirectoryStore _store;
    private readonly INamingContextService _ncService;
    private readonly DnsZoneStore _zoneStore;
    private readonly DomainConfiguration _domainConfig;
    private readonly ILogger<DcRegistrationService> _logger;
    private Timer _heartbeatTimer;
    private Timer _dnsRefreshTimer;
    private string _tenantId = "default";

    /// <summary>
    /// Tracks all DNS records registered by this DC instance so they can be
    /// cleanly deregistered on shutdown.
    /// </summary>
    private readonly List<(string ZoneName, DnsRecord Record)> _registeredDnsRecords = [];

    public DcRegistrationService(
        DcInstanceInfo dcInfo,
        IDirectoryStore store,
        INamingContextService ncService,
        DnsZoneStore zoneStore,
        DomainConfiguration domainConfig,
        ILogger<DcRegistrationService> logger)
    {
        _dcInfo = dcInfo;
        _store = store;
        _ncService = ncService;
        _zoneStore = zoneStore;
        _domainConfig = domainConfig;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var domainNc = _ncService.GetDomainNc();
        var domainDn = domainNc.Dn;
        _tenantId = "default"; // Matches the default tenant

        _logger.LogInformation(
            "DC Registration: InstanceId={InstanceId}, Hostname={Hostname}, Site={Site}, InvocationId={InvocationId}",
            _dcInfo.InstanceId, _dcInfo.Hostname, _dcInfo.SiteName, _dcInfo.InvocationId);

        try
        {
            await EnsureDcRegisteredAsync(domainDn, cancellationToken);
            await RegisterSpnEntriesAsync(domainDn, domainNc.DnsName ?? "", cancellationToken);
            await RegisterDnsSrvRecordsAsync(cancellationToken);

            // Start heartbeat timer (every 5 minutes)
            _heartbeatTimer = new Timer(
                async _ => await UpdateHeartbeatAsync(domainDn),
                null,
                TimeSpan.FromMinutes(5),
                TimeSpan.FromMinutes(5));

            // Start DNS refresh timer (every 5 minutes) to maintain SRV records
            _dnsRefreshTimer = new Timer(
                async _ => await RefreshDnsRecordsAsync(),
                null,
                TimeSpan.FromMinutes(5),
                TimeSpan.FromMinutes(5));

            _logger.LogInformation("DC registration complete for {Hostname} in site {Site}",
                _dcInfo.Hostname, _dcInfo.SiteName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DC registration encountered an error; the DC will operate but may not be discoverable by peers");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _heartbeatTimer?.Change(Timeout.Infinite, 0);
        _dnsRefreshTimer?.Change(Timeout.Infinite, 0);

        // Gracefully deregister DNS records for this DC
        try
        {
            await DeregisterDnsSrvRecordsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to deregister DNS SRV records during shutdown");
        }
    }

    public void Dispose()
    {
        _heartbeatTimer?.Dispose();
        _dnsRefreshTimer?.Dispose();
    }

    /// <summary>
    /// Registers all required DC locator SRV and A records per MS-ADTS 6.3.6.
    /// Records are stored in the AD-integrated DNS zone store so they are
    /// queryable by the DNS server and replicated across DCs.
    /// </summary>
    private async Task RegisterDnsSrvRecordsAsync(CancellationToken ct)
    {
        var domainDnsName = _domainConfig.DomainDnsName;
        var forestDnsName = _domainConfig.ForestDnsName;
        var siteName = _dcInfo.SiteName;
        var dcFqdn = _dcInfo.Fqdn(domainDnsName);
        var dcIp = _dcInfo.IpAddress;

        // Generate a deterministic domain GUID from the domain DN
        var domainGuid = GenerateDomainGuid(_domainConfig.DomainDn);

        if (string.IsNullOrEmpty(domainDnsName))
        {
            _logger.LogWarning("Domain DNS name not yet configured; skipping DNS SRV registration");
            return;
        }

        if (string.IsNullOrEmpty(forestDnsName))
            forestDnsName = domainDnsName;

        // Ensure DNS zone containers exist before upserting records.
        // DomainProvisioner creates these during initial setup, but on subsequent
        // restarts they must exist for record upserts to land in the right place.
        var zones = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { domainDnsName, forestDnsName };
        foreach (var zoneName in zones)
        {
            await EnsureDnsZoneContainersAsync(zoneName, ct);
        }

        _registeredDnsRecords.Clear();

        // ── LDAP SRV records ──
        RegisterSrvRecord(domainDnsName, $"_ldap._tcp.{domainDnsName}", 0, 100, 389, dcFqdn);
        RegisterSrvRecord(domainDnsName, $"_ldap._tcp.{siteName}._sites.{domainDnsName}", 0, 100, 389, dcFqdn);
        RegisterSrvRecord(domainDnsName, $"_ldap._tcp.dc._msdcs.{domainDnsName}", 0, 100, 389, dcFqdn);
        RegisterSrvRecord(domainDnsName, $"_ldap._tcp.{siteName}._sites.dc._msdcs.{domainDnsName}", 0, 100, 389, dcFqdn);
        RegisterSrvRecord(forestDnsName, $"_ldap._tcp.{domainGuid}.domains._msdcs.{forestDnsName}", 0, 100, 389, dcFqdn);

        // ── Kerberos SRV records ──
        RegisterSrvRecord(domainDnsName, $"_kerberos._tcp.{domainDnsName}", 0, 100, 88, dcFqdn);
        RegisterSrvRecord(domainDnsName, $"_kerberos._tcp.{siteName}._sites.{domainDnsName}", 0, 100, 88, dcFqdn);
        RegisterSrvRecord(domainDnsName, $"_kerberos._tcp.dc._msdcs.{domainDnsName}", 0, 100, 88, dcFqdn);
        RegisterSrvRecord(domainDnsName, $"_kerberos._tcp.{siteName}._sites.dc._msdcs.{domainDnsName}", 0, 100, 88, dcFqdn);
        RegisterSrvRecord(domainDnsName, $"_kerberos._udp.{domainDnsName}", 0, 100, 88, dcFqdn);

        // ── Kpasswd SRV records ──
        RegisterSrvRecord(domainDnsName, $"_kpasswd._tcp.{domainDnsName}", 0, 100, 464, dcFqdn);
        RegisterSrvRecord(domainDnsName, $"_kpasswd._udp.{domainDnsName}", 0, 100, 464, dcFqdn);

        // ── Global Catalog SRV records ──
        RegisterSrvRecord(domainDnsName, $"_gc._tcp.{domainDnsName}", 0, 100, 3268, dcFqdn);
        RegisterSrvRecord(forestDnsName, $"_gc._tcp.{forestDnsName}", 0, 100, 3268, dcFqdn);
        RegisterSrvRecord(forestDnsName, $"_gc._tcp.{siteName}._sites.{forestDnsName}", 0, 100, 3268, dcFqdn);
        RegisterSrvRecord(forestDnsName, $"_ldap._tcp.gc._msdcs.{forestDnsName}", 0, 100, 3268, dcFqdn);

        // ── PDC Emulator SRV record (first DC is always PDC) ──
        RegisterSrvRecord(domainDnsName, $"_ldap._tcp.pdc._msdcs.{domainDnsName}", 0, 100, 389, dcFqdn);

        // ── A records ──
        RegisterARecord(domainDnsName, dcFqdn, dcIp);

        // GC A record (gc._msdcs.{forestDnsName})
        RegisterARecord(forestDnsName, $"gc._msdcs.{forestDnsName}", dcIp);

        // Upsert all records into the zone store
        foreach (var (zoneName, record) in _registeredDnsRecords)
        {
            try
            {
                await _zoneStore.UpsertRecordAsync(_tenantId, zoneName, record, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to register DNS record {Name} in zone {Zone}", record.Name, zoneName);
            }
        }

        _logger.LogInformation("Registered {Count} DNS records for DC {Hostname}",
            _registeredDnsRecords.Count, _dcInfo.Hostname);
    }

    /// <summary>
    /// Deregisters all DNS records for this DC on graceful shutdown.
    /// Only removes this DC's records (by matching target/data), leaving
    /// other DCs' records intact.
    /// </summary>
    private async Task DeregisterDnsSrvRecordsAsync(CancellationToken ct)
    {
        var count = 0;
        foreach (var (zoneName, record) in _registeredDnsRecords)
        {
            try
            {
                await _zoneStore.DeleteRecordByDataAsync(
                    _tenantId, zoneName, record.Name, record.Type, record.Data, ct);
                count++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to deregister DNS record {Name} from zone {Zone}", record.Name, zoneName);
            }
        }

        _logger.LogInformation("Deregistered {Count} DNS records for DC {Hostname}", count, _dcInfo.Hostname);
    }

    /// <summary>
    /// Periodic refresh of DNS records to ensure they remain current.
    /// </summary>
    private async Task RefreshDnsRecordsAsync()
    {
        try
        {
            foreach (var (zoneName, record) in _registeredDnsRecords)
            {
                await _zoneStore.UpsertRecordAsync(_tenantId, zoneName, record);
            }

            _logger.LogDebug("Refreshed {Count} DNS records for DC {Hostname}",
                _registeredDnsRecords.Count, _dcInfo.Hostname);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh DNS records");
        }
    }

    private void RegisterSrvRecord(string zoneName, string name, ushort priority, ushort weight, ushort port, string target)
    {
        var record = new DnsRecord
        {
            Name = name,
            Type = DnsRecordType.SRV,
            Ttl = 600,
            Data = $"{priority} {weight} {port} {target}",
        };
        _registeredDnsRecords.Add((zoneName, record));
    }

    private void RegisterARecord(string zoneName, string name, string ipAddress)
    {
        var record = new DnsRecord
        {
            Name = name,
            Type = DnsRecordType.A,
            Ttl = 600,
            Data = ipAddress,
        };
        _registeredDnsRecords.Add((zoneName, record));
    }

    /// <summary>
    /// Generates a deterministic GUID from the domain DN for use in
    /// _ldap._tcp.{DomainGuid}.domains._msdcs.{ForestDnsName} records.
    /// </summary>
    private static string GenerateDomainGuid(string domainDn)
    {
        var bytes = System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes(domainDn.ToLowerInvariant()));
        return new Guid(bytes).ToString();
    }

    /// <summary>
    /// Ensures the DNS zone container hierarchy exists for the given zone name.
    /// Creates DC=DomainDnsZones, CN=MicrosoftDNS, and DC={zoneName} containers if missing.
    /// </summary>
    private async Task EnsureDnsZoneContainersAsync(string zoneName, CancellationToken ct)
    {
        var domainDn = _domainConfig.DomainDn;
        if (string.IsNullOrEmpty(domainDn))
            return;

        var schemaDn = $"CN=Schema,CN=Configuration,{domainDn}";

        // DC=DomainDnsZones,{domainDn}
        var dnsZonesDn = $"DC=DomainDnsZones,{domainDn}";
        if (await _store.GetByDnAsync(_tenantId, dnsZonesDn, ct) == null)
        {
            _logger.LogInformation("Creating DNS application partition: {Dn}", dnsZonesDn);
            var dnsZones = CreateBaseObject(_tenantId, domainDn, dnsZonesDn,
                ["top", "domainDNS"], $"CN=Domain-DNS,{schemaDn}", domainDn);
            dnsZones.Cn = "DomainDnsZones";
            await _store.CreateAsync(dnsZones, ct);
        }

        // CN=MicrosoftDNS,DC=DomainDnsZones,{domainDn}
        var microsoftDnsDn = $"CN=MicrosoftDNS,{dnsZonesDn}";
        if (await _store.GetByDnAsync(_tenantId, microsoftDnsDn, ct) == null)
        {
            _logger.LogInformation("Creating MicrosoftDNS container: {Dn}", microsoftDnsDn);
            var microsoftDns = CreateBaseObject(_tenantId, domainDn, microsoftDnsDn,
                ["top", "container"], $"CN=Container,{schemaDn}", dnsZonesDn);
            microsoftDns.Cn = "MicrosoftDNS";
            await _store.CreateAsync(microsoftDns, ct);
        }

        // DC={zoneName},CN=MicrosoftDNS,DC=DomainDnsZones,{domainDn}
        var zoneContainerDn = $"DC={zoneName},{microsoftDnsDn}";
        if (await _store.GetByDnAsync(_tenantId, zoneContainerDn, ct) == null)
        {
            _logger.LogInformation("Creating DNS zone container: {Dn}", zoneContainerDn);
            var zoneContainer = CreateBaseObject(_tenantId, domainDn, zoneContainerDn,
                ["top", "dnsZone"], $"CN=Dns-Zone,{schemaDn}", microsoftDnsDn);
            zoneContainer.Cn = zoneName;
            zoneContainer.SetAttribute("dc", new DirectoryAttribute("dc", zoneName));
            await _store.CreateAsync(zoneContainer, ct);
        }
    }

    private async Task EnsureDcRegisteredAsync(string domainDn, CancellationToken ct)
    {
        var configDn = $"CN=Configuration,{domainDn}";
        var schemaDn = $"CN=Schema,CN=Configuration,{domainDn}";
        var sitesDn = $"CN=Sites,{configDn}";
        var siteDn = $"CN={_dcInfo.SiteName},{sitesDn}";
        var serversDn = $"CN=Servers,{siteDn}";
        var serverDn = _dcInfo.ServerDn(domainDn);
        var ntdsSettingsDn = _dcInfo.NtdsSettingsDn(domainDn);

        // Ensure site exists
        var site = await _store.GetByDnAsync(_tenantId, siteDn, ct);
        if (site == null)
        {
            _logger.LogInformation("Creating site {SiteName}", _dcInfo.SiteName);
            site = CreateBaseObject(_tenantId, domainDn, siteDn, ["top", "site"],
                $"CN=Site,{schemaDn}", sitesDn);
            site.Cn = _dcInfo.SiteName;
            await _store.CreateAsync(site, ct);

            // Site settings
            var siteSettings = CreateBaseObject(_tenantId, domainDn,
                $"CN=NTDS Site Settings,{siteDn}", ["top", "nTDSSiteSettings"],
                $"CN=NTDS-Site-Settings,{schemaDn}", siteDn);
            siteSettings.Cn = "NTDS Site Settings";
            await _store.CreateAsync(siteSettings, ct);

            // Servers container
            var serversContainer = CreateBaseObject(_tenantId, domainDn, serversDn,
                ["top", "serversContainer"],
                $"CN=Servers-Container,{schemaDn}", siteDn);
            serversContainer.Cn = "Servers";
            await _store.CreateAsync(serversContainer, ct);
        }
        else
        {
            // Make sure servers container exists
            var serversContainer = await _store.GetByDnAsync(_tenantId, serversDn, ct);
            if (serversContainer == null)
            {
                serversContainer = CreateBaseObject(_tenantId, domainDn, serversDn,
                    ["top", "serversContainer"],
                    $"CN=Servers-Container,{schemaDn}", siteDn);
                serversContainer.Cn = "Servers";
                await _store.CreateAsync(serversContainer, ct);
            }
        }

        // Ensure server object
        var server = await _store.GetByDnAsync(_tenantId, serverDn, ct);
        if (server == null)
        {
            _logger.LogInformation("Creating server object for {Hostname}", _dcInfo.Hostname);
            server = CreateBaseObject(_tenantId, domainDn, serverDn, ["top", "server"],
                $"CN=Server,{schemaDn}", serversDn);
            server.Cn = _dcInfo.Hostname;
            server.DnsHostName = _dcInfo.Fqdn(_ncService.GetDomainNc().DnsName ?? "");
            server.SetAttribute("invocationId", new DirectoryAttribute("invocationId", _dcInfo.InvocationId));
            server.SetAttribute("dcInstanceId", new DirectoryAttribute("dcInstanceId", _dcInfo.InstanceId));
            await _store.CreateAsync(server, ct);
        }
        else
        {
            // Update heartbeat and invocation ID
            server.WhenChanged = DateTimeOffset.UtcNow;
            server.SetAttribute("invocationId", new DirectoryAttribute("invocationId", _dcInfo.InvocationId));
            server.SetAttribute("dcInstanceId", new DirectoryAttribute("dcInstanceId", _dcInfo.InstanceId));
            await _store.UpdateAsync(server, ct);
        }

        // Ensure NTDS Settings
        var ntdsSettings = await _store.GetByDnAsync(_tenantId, ntdsSettingsDn, ct);
        if (ntdsSettings == null)
        {
            _logger.LogInformation("Creating NTDS Settings for {Hostname}", _dcInfo.Hostname);
            ntdsSettings = CreateBaseObject(_tenantId, domainDn, ntdsSettingsDn, ["top", "nTDSDSA"],
                $"CN=NTDS-DSA,{schemaDn}", serverDn);
            ntdsSettings.Cn = "NTDS Settings";
            ntdsSettings.SetAttribute("invocationId", new DirectoryAttribute("invocationId", _dcInfo.InvocationId));
            ntdsSettings.SetAttribute("options", new DirectoryAttribute("options", 1)); // IS_GC
            await _store.CreateAsync(ntdsSettings, ct);
        }
        else
        {
            ntdsSettings.WhenChanged = DateTimeOffset.UtcNow;
            ntdsSettings.SetAttribute("invocationId", new DirectoryAttribute("invocationId", _dcInfo.InvocationId));
            await _store.UpdateAsync(ntdsSettings, ct);
        }

        // Ensure computer account for this DC
        var computerDn = $"CN={_dcInfo.Hostname},OU=Domain Controllers,{domainDn}";
        var computer = await _store.GetByDnAsync(_tenantId, computerDn, ct);
        if (computer == null)
        {
            _logger.LogInformation("Creating computer account for DC {Hostname}", _dcInfo.Hostname);
            computer = CreateBaseObject(_tenantId, domainDn, computerDn,
                ["top", "person", "organizationalPerson", "user", "computer"],
                $"CN=Computer,{schemaDn}", $"OU=Domain Controllers,{domainDn}");
            computer.Cn = _dcInfo.Hostname;
            computer.SAMAccountName = $"{_dcInfo.Hostname}$";
            computer.DnsHostName = _dcInfo.Fqdn(_ncService.GetDomainNc().DnsName ?? "");
            computer.SetAttribute("userAccountControl",
                new DirectoryAttribute("userAccountControl", 532480)); // SERVER_TRUST_ACCOUNT | TRUSTED_FOR_DELEGATION
            computer.SetAttribute("serverReferenceBL",
                new DirectoryAttribute("serverReferenceBL", serverDn));
            await _store.CreateAsync(computer, ct);
        }
    }

    private async Task RegisterSpnEntriesAsync(string domainDn, string domainDnsName, CancellationToken ct)
    {
        var computerDn = $"CN={_dcInfo.Hostname},OU=Domain Controllers,{domainDn}";
        var computer = await _store.GetByDnAsync(_tenantId, computerDn, ct);
        if (computer == null) return;

        var fqdn = _dcInfo.Fqdn(domainDnsName);
        var hostname = _dcInfo.Hostname;

        var spns = new List<string>
        {
            $"ldap/{fqdn}",
            $"ldap/{hostname}",
            $"ldap/{fqdn}/{domainDnsName}",
            $"HOST/{fqdn}",
            $"HOST/{hostname}",
            $"GC/{fqdn}/{domainDnsName}",
            $"E3514235-4B06-11D1-AB04-00C04FC2DCD2/{_dcInfo.InvocationId}/{domainDnsName}",
        };

        computer.ServicePrincipalName = spns;
        await _store.UpdateAsync(computer, ct);

        _logger.LogInformation("Registered {Count} SPNs for DC {Hostname}", spns.Count, hostname);
    }

    private async Task UpdateHeartbeatAsync(string domainDn)
    {
        try
        {
            var serverDn = _dcInfo.ServerDn(domainDn);
            var server = await _store.GetByDnAsync(_tenantId, serverDn);
            if (server != null)
            {
                server.WhenChanged = DateTimeOffset.UtcNow;
                await _store.UpdateAsync(server);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update DC heartbeat");
        }
    }

    private static DirectoryObject CreateBaseObject(
        string tenantId, string domainDn, string dn,
        List<string> objectClass, string objectCategory, string parentDn)
    {
        var now = DateTimeOffset.UtcNow;
        var cn = dn.Split(',')[0];
        if (cn.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
            cn = cn[3..];

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
}
