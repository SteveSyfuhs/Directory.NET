using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Dns;
using Directory.Dns.Dnssec;
using Directory.Kerberos;
using Directory.Ldap.Handlers;
using Directory.Ldap.Server;
using Directory.Replication;
using Directory.Replication.Drsr;
using Directory.Rpc;
using Directory.Rpc.Dispatch;
using Directory.Rpc.Transport;
using Directory.Rpc.Drsr;
using Directory.Rpc.Lsa;
using Directory.Rpc.Nrpc;
using Directory.Rpc.Samr;
using Directory.Schema;
using Directory.Security;
using Directory.Security.Apds;
using Directory.Security.Claims;
using Directory.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Directory.Tests;

/// <summary>
/// Integration test fixture that starts all AD protocol servers (LDAP, RPC, DNS) on random
/// high ports, backed by an InMemoryDirectoryStore. Tests make real TCP/UDP connections.
/// </summary>
public class AdTestHost : IAsyncLifetime
{
    public const string TenantId = "default";
    public const string DomainDn = "DC=test,DC=local";
    public const string DomainDns = "test.local";
    public const string DomainNetbios = "TEST";
    public const string DomainSid = "S-1-5-21-1000-2000-3000";
    public const string AdminUpn = "admin@test.local";
    public const string AdminPassword = "P@ssw0rd!";
    public const string AdminDn = $"CN=Administrator,CN=Users,{DomainDn}";
    public const string ServerHostname = "dc1.test.local";

    public int LdapPort { get; private set; }
    public int RpcEpmPort { get; private set; }
    public int RpcServicePort { get; private set; }
    public int DnsPort { get; private set; }

    private IHost _host;
    public IServiceProvider Services => _host.Services;
    internal InMemoryDirectoryStore Store { get; } = new();

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public async Task InitializeAsync()
    {
        LdapPort = GetFreePort();
        RpcEpmPort = GetFreePort();
        RpcServicePort = GetFreePort();
        DnsPort = GetFreePort();

        var builder = Host.CreateApplicationBuilder();

        builder.Services.Configure<LdapServerOptions>(o =>
        {
            o.Port = LdapPort;
            o.DefaultDomain = DomainDn;
            o.DefaultTenantId = TenantId;
        });
        builder.Services.Configure<RpcServerOptions>(o =>
        {
            o.EndpointMapperPort = RpcEpmPort;
            o.ServicePort = RpcServicePort;
            o.DomainDn = DomainDn;
            o.TenantId = TenantId;
        });
        builder.Services.Configure<DnsOptions>(o =>
        {
            o.Port = DnsPort;
            o.Domains = [DomainDns];
            o.ServerHostname = ServerHostname;
            o.ServerIpAddresses = ["127.0.0.1"];
        });
        builder.Services.Configure<KerberosOptions>(o =>
        {
            o.DefaultRealm = DomainDns.ToUpperInvariant();
        });
        builder.Services.Configure<NamingContextOptions>(o =>
        {
            o.DomainDn = DomainDn;
            o.ForestDnsName = DomainDns;
            o.DomainSid = DomainSid;
        });

        // In-memory store instead of Cosmos
        builder.Services.AddSingleton<IDirectoryStore>(Store);

        // Core services
        builder.Services.AddSingleton<ISchemaService, SchemaService>();
        builder.Services.AddSingleton<SchemaModificationService>();
        builder.Services.AddSingleton<PasswordService>();
        builder.Services.AddSingleton<IPasswordPolicy>(sp => sp.GetRequiredService<PasswordService>());
        builder.Services.AddSingleton<NtlmAuthenticator>();
        builder.Services.AddSingleton<IUserAccountControlService, UserAccountControlService>();
        builder.Services.AddSingleton<IAccessControlService, AccessControlService>();
        builder.Services.AddSingleton<GroupMembershipMaterializer>();
        builder.Services.AddSingleton<ILinkedAttributeService, LinkedAttributeService>();
        builder.Services.AddSingleton<IConstructedAttributeService, ConstructedAttributeService>();
        builder.Services.AddSingleton<IRidAllocator, RidAllocator>();
        builder.Services.AddSingleton<INamingContextService, NamingContextService>();

        // APDS
        builder.Services.AddSingleton<AccountRestrictions>();
        builder.Services.AddSingleton<ApdsLogonProcessor>();
        builder.Services.AddSingleton<NtlmPassThrough>();
        builder.Services.AddSingleton<PacValidation>();
        builder.Services.AddSingleton<DigestValidation>();

        // Claims
        builder.Services.AddSingleton<IClaimTypeStore, ClaimTypeStore>();
        builder.Services.AddSingleton<IClaimsProvider, ClaimsProvider>();
        builder.Services.AddSingleton<ClaimTransformationEngine>();
        builder.Services.AddSingleton<ITrustClaimsPolicy, TrustClaimsPolicy>();
        builder.Services.AddSingleton<DynamicAccessControlEvaluator>();
        builder.Services.AddSingleton<ClaimsSerialization>();
        builder.Services.AddSingleton<FineGrainedPasswordPolicyService>();

        // DC identity
        builder.Services.AddSingleton(new DcInstanceInfo { Hostname = ServerHostname, SiteName = "Default-First-Site-Name" });
        builder.Services.AddSingleton(new DomainConfiguration
        {
            DomainDn = DomainDn,
            DomainDnsName = DomainDns,
            NetBiosName = DomainNetbios,
            DomainSid = DomainSid,
            ForestDnsName = DomainDns,
            KerberosRealm = DomainDns.ToUpperInvariant(),
        });

        // LDAP handlers
        builder.Services.AddSingleton<RootDseProvider>();
        builder.Services.AddSingleton<SaslHandler>();
        builder.Services.AddSingleton<SaslGssapiHandler>();
        builder.Services.AddSingleton<LdapControlHandler>();
        builder.Services.AddSingleton<PasswordModifyHandler>();
        builder.Services.AddSingleton<GroupPolicyHandler>();
        builder.Services.AddSingleton<ILdapOperationHandler, BindHandler>();
        builder.Services.AddSingleton<ILdapOperationHandler, SearchHandler>();
        builder.Services.AddSingleton<ILdapOperationHandler, AddHandler>();
        builder.Services.AddSingleton<ILdapOperationHandler, ModifyHandler>();
        builder.Services.AddSingleton<ILdapOperationHandler, DeleteHandler>();
        builder.Services.AddSingleton<ILdapOperationHandler, ModifyDNHandler>();
        builder.Services.AddSingleton<ILdapOperationHandler, CompareHandler>();
        builder.Services.AddSingleton<ILdapOperationHandler, AbandonHandler>();
        builder.Services.AddSingleton<ILdapOperationHandler, ExtendedHandler>();
        builder.Services.AddSingleton<ILdapOperationHandler, UnbindHandler>();
        builder.Services.AddSingleton<ILdapOperationDispatcher, LdapOperationDispatcher>();
        builder.Services.AddSingleton<LdapConnectionStats>();
        builder.Services.AddHostedService<LdapServer>();

        // RPC
        builder.Services.AddSingleton<SamrOperations>();
        builder.Services.AddSingleton<LsaOperations>();
        builder.Services.AddSingleton<LsaSecretOperations>();
        builder.Services.AddSingleton<LsaTrustedDomainOperations>();
        builder.Services.AddSingleton<LsaAccountRightsOperations>();
        builder.Services.AddSingleton<NrpcOperations>();
        builder.Services.AddSingleton<IRpcInterfaceHandler, SamrInterfaceHandler>();
        builder.Services.AddSingleton<IRpcInterfaceHandler, LsaInterfaceHandler>();
        builder.Services.AddSingleton<IRpcInterfaceHandler, NrpcInterfaceHandler>();
        builder.Services.AddSingleton<RodcService>();

        // DRSR
        builder.Services.AddSingleton<ReplicationEngine>();
        builder.Services.AddSingleton<ReplicationTopology>();
        builder.Services.AddSingleton<DsCrackNamesService>();
        builder.Services.AddSingleton<DrsNameResolution>();
        builder.Services.AddSingleton<FsmoRoleManager>();
        builder.Services.AddSingleton<SchemaPrefixTable>();
        builder.Services.AddSingleton<ConflictResolver>();
        builder.Services.AddSingleton<LinkedValueReplication>();
        builder.Services.AddSingleton<DrsInterfaceHandler>();
        builder.Services.AddSingleton<IRpcInterfaceHandler>(sp => sp.GetRequiredService<DrsInterfaceHandler>());

        // Register RPC transport manually to avoid circular dependency
        // (EndpointMapper → RpcInterfaceDispatcher → IRpcInterfaceHandler → EndpointMapper)
        builder.Services.AddSingleton<RpcInterfaceDispatcher>();
        builder.Services.AddHostedService<RpcServer>();

        // Kerberos
        builder.Services.AddSingleton<PacGenerator>();
        builder.Services.AddSingleton<SpnService>();
        builder.Services.AddSingleton<S4UService>();
        builder.Services.AddSingleton<TrustedRealmService>();

        // DNS
        builder.Services.AddSingleton<DnsZoneStore>();
        builder.Services.AddSingleton<DnsDynamicUpdateHandler>();
        builder.Services.AddSingleton<DnsSiteService>();
        builder.Services.AddSingleton<DnssecService>();
        builder.Services.AddHostedService<DnsServer>();

        // Minimal logging
        builder.Services.AddLogging(l => l.ClearProviders());

        _host = builder.Build();

        await SeedDirectoryAsync();
        await _host.StartAsync();

        // Allow sockets to bind
        await Task.Delay(300);
    }

    private async Task SeedDirectoryAsync()
    {
        await Store.CreateAsync(new DirectoryObject
        {
            Id = DomainDn.ToLowerInvariant(),
            TenantId = TenantId, DomainDn = DomainDn,
            DistinguishedName = DomainDn,
            ObjectGuid = Guid.NewGuid().ToString(),
            ObjectSid = DomainSid,
            ObjectClass = ["top", "domain", "domainDNS"],
            ObjectCategory = "domainDNS",
            Cn = "test",
            WhenCreated = DateTimeOffset.UtcNow, WhenChanged = DateTimeOffset.UtcNow,
        });

        await Store.CreateAsync(new DirectoryObject
        {
            Id = $"cn=computers,{DomainDn}".ToLowerInvariant(),
            TenantId = TenantId, DomainDn = DomainDn,
            DistinguishedName = $"CN=Computers,{DomainDn}",
            ObjectGuid = Guid.NewGuid().ToString(),
            ObjectClass = ["top", "container"], ObjectCategory = "container",
            Cn = "Computers", ParentDn = DomainDn,
            WhenCreated = DateTimeOffset.UtcNow, WhenChanged = DateTimeOffset.UtcNow,
        });

        await Store.CreateAsync(new DirectoryObject
        {
            Id = $"cn=users,{DomainDn}".ToLowerInvariant(),
            TenantId = TenantId, DomainDn = DomainDn,
            DistinguishedName = $"CN=Users,{DomainDn}",
            ObjectGuid = Guid.NewGuid().ToString(),
            ObjectClass = ["top", "container"], ObjectCategory = "container",
            Cn = "Users", ParentDn = DomainDn,
            WhenCreated = DateTimeOffset.UtcNow, WhenChanged = DateTimeOffset.UtcNow,
        });

        await Store.CreateAsync(new DirectoryObject
        {
            Id = AdminDn.ToLowerInvariant(),
            TenantId = TenantId, DomainDn = DomainDn,
            DistinguishedName = AdminDn,
            ObjectGuid = Guid.NewGuid().ToString(),
            ObjectSid = $"{DomainSid}-500",
            SAMAccountName = "Administrator",
            UserPrincipalName = AdminUpn,
            ObjectClass = ["top", "person", "organizationalPerson", "user"],
            ObjectCategory = "person",
            Cn = "Administrator",
            ParentDn = $"CN=Users,{DomainDn}",
            UserAccountControl = (int)UserAccountControlFlags.NORMAL_ACCOUNT,
            PrimaryGroupId = 512,
            WhenCreated = DateTimeOffset.UtcNow, WhenChanged = DateTimeOffset.UtcNow,
            AccountExpires = 0x7FFFFFFFFFFFFFFF,
        });

        var pwdService = _host.Services.GetRequiredService<PasswordService>();
        await pwdService.SetPasswordAsync(TenantId, AdminDn, AdminPassword);

        await Store.CreateAsync(new DirectoryObject
        {
            Id = $"cn=domain admins,cn=users,{DomainDn}".ToLowerInvariant(),
            TenantId = TenantId, DomainDn = DomainDn,
            DistinguishedName = $"CN=Domain Admins,CN=Users,{DomainDn}",
            ObjectGuid = Guid.NewGuid().ToString(),
            ObjectSid = $"{DomainSid}-512",
            SAMAccountName = "Domain Admins",
            ObjectClass = ["top", "group"], ObjectCategory = "group",
            Cn = "Domain Admins",
            ParentDn = $"CN=Users,{DomainDn}",
            GroupType = -2147483646,
            Member = [AdminDn],
            WhenCreated = DateTimeOffset.UtcNow, WhenChanged = DateTimeOffset.UtcNow,
        });

        await Store.CreateAsync(new DirectoryObject
        {
            Id = $"cn=domain computers,cn=users,{DomainDn}".ToLowerInvariant(),
            TenantId = TenantId, DomainDn = DomainDn,
            DistinguishedName = $"CN=Domain Computers,CN=Users,{DomainDn}",
            ObjectGuid = Guid.NewGuid().ToString(),
            ObjectSid = $"{DomainSid}-515",
            SAMAccountName = "Domain Computers",
            ObjectClass = ["top", "group"], ObjectCategory = "group",
            Cn = "Domain Computers",
            ParentDn = $"CN=Users,{DomainDn}",
            GroupType = -2147483646,
            WhenCreated = DateTimeOffset.UtcNow, WhenChanged = DateTimeOffset.UtcNow,
        });

        // Seed DNS SRV records in the zone store (no longer hardcoded in DnsServer)
        var zoneStore = _host.Services.GetRequiredService<DnsZoneStore>();
        var srvRecords = new (string name, int port)[]
        {
            ($"_ldap._tcp.{DomainDns}", 389),
            ($"_kerberos._tcp.{DomainDns}", 88),
            ($"_kerberos._udp.{DomainDns}", 88),
            ($"_kpasswd._tcp.{DomainDns}", 464),
            ($"_kpasswd._udp.{DomainDns}", 464),
            ($"_gc._tcp.{DomainDns}", 3268),
            ($"_ldap._tcp.dc._msdcs.{DomainDns}", 389),
            ($"_kerberos._tcp.dc._msdcs.{DomainDns}", 88),
            ($"_ldap._tcp.pdc._msdcs.{DomainDns}", 389),
            ($"_ldap._tcp.gc._msdcs.{DomainDns}", 3268),
        };
        foreach (var (name, port) in srvRecords)
        {
            await zoneStore.UpsertRecordAsync(TenantId, DomainDns, new DnsRecord
            {
                Name = name,
                Type = DnsRecordType.SRV,
                Ttl = 600,
                Data = $"0 100 {port} {ServerHostname}",
            });
        }
        // A record for the DC hostname
        await zoneStore.UpsertRecordAsync(TenantId, DomainDns, new DnsRecord
        {
            Name = ServerHostname,
            Type = DnsRecordType.A,
            Ttl = 600,
            Data = "127.0.0.1",
        });
    }

    public async Task DisposeAsync()
    {
        if (_host != null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  Integration Tests — Real TCP/UDP connections verify each domain join stage
// ═══════════════════════════════════════════════════════════════════════════

public class DomainJoinIntegrationTests : IClassFixture<AdTestHost>
{
    private readonly AdTestHost _host;

    public DomainJoinIntegrationTests(AdTestHost host) => _host = host;

    // ════════════════════════════════════════════════════════════════
    //  Stage 1: DNS SRV Discovery
    //  Windows: DsGetDcName → DNS query _ldap._tcp.dc._msdcs.<domain>
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Stage1a_DnsDiscovery_LdapSrvRecord()
    {
        var resp = await DnsQuerySrv($"_ldap._tcp.{AdTestHost.DomainDns}");
        Assert.True(resp.IsResponse, "DNS reply must have QR=1");
        Assert.True(resp.AnswerCount > 0, "Must have SRV answer");
        Assert.Equal(389, resp.SrvPort);
        Assert.Contains("dc1.test.local", resp.SrvTarget, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Stage1b_DnsDiscovery_KerberosSrvRecord()
    {
        var resp = await DnsQuerySrv($"_kerberos._tcp.{AdTestHost.DomainDns}");
        Assert.True(resp.IsResponse);
        Assert.True(resp.AnswerCount > 0);
        Assert.Equal(88, resp.SrvPort);
    }

    [Fact]
    public async Task Stage1c_DnsDiscovery_MsdcsSrvRecord()
    {
        var resp = await DnsQuerySrv($"_ldap._tcp.dc._msdcs.{AdTestHost.DomainDns}");
        Assert.True(resp.IsResponse);
        Assert.True(resp.AnswerCount > 0);
        Assert.Equal(389, resp.SrvPort);
    }

    [Fact]
    public async Task Stage1d_DnsDiscovery_KpasswdSrvRecord()
    {
        var resp = await DnsQuerySrv($"_kpasswd._tcp.{AdTestHost.DomainDns}");
        Assert.True(resp.IsResponse);
        Assert.True(resp.AnswerCount > 0);
        Assert.Equal(464, resp.SrvPort);
    }

    [Fact]
    public async Task Stage1e_DnsDiscovery_GcSrvRecord()
    {
        var resp = await DnsQuerySrv($"_gc._tcp.{AdTestHost.DomainDns}");
        Assert.True(resp.IsResponse);
        Assert.True(resp.AnswerCount > 0);
        Assert.Equal(3268, resp.SrvPort);
    }

    // ════════════════════════════════════════════════════════════════
    //  Stage 2: LDAP RootDSE (Anonymous Read)
    //  Windows reads base="" to confirm this is an AD DC
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Stage2a_LdapRootDse_DefaultNamingContext()
    {
        var attrs = await LdapSearchRootDse("defaultNamingContext");
        Assert.True(attrs.ContainsKey("defaultNamingContext"));
        Assert.Contains(AdTestHost.DomainDn, attrs["defaultNamingContext"]);
    }

    [Fact]
    public async Task Stage2b_LdapRootDse_SaslMechanisms()
    {
        var attrs = await LdapSearchRootDse("supportedSASLMechanisms");
        Assert.True(attrs.ContainsKey("supportedSASLMechanisms"));
        Assert.Contains("GSS-SPNEGO", attrs["supportedSASLMechanisms"]);
        Assert.Contains("GSSAPI", attrs["supportedSASLMechanisms"]);
    }

    [Fact]
    public async Task Stage2c_LdapRootDse_AdCapabilityOid()
    {
        var attrs = await LdapSearchRootDse("supportedCapabilities");
        Assert.True(attrs.ContainsKey("supportedCapabilities"));
        Assert.Contains("1.2.840.113556.1.4.800", attrs["supportedCapabilities"]);
    }

    [Fact]
    public async Task Stage2d_LdapRootDse_DnsHostName()
    {
        var attrs = await LdapSearchRootDse("dnsHostName");
        Assert.True(attrs.ContainsKey("dnsHostName"));
        // dnsHostName is derived from actual machine name + configured domain
        Assert.True(attrs["dnsHostName"].Count > 0, "dnsHostName must have a value");
        Assert.Contains(".test.local", attrs["dnsHostName"][0]);
    }

    // ════════════════════════════════════════════════════════════════
    //  Stage 3: LDAP Simple Bind (Admin Authentication)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Stage3a_LdapBind_AdminSuccess()
    {
        int rc = await LdapSimpleBind(AdTestHost.AdminDn, AdTestHost.AdminPassword);
        Assert.Equal(0, rc);
    }

    [Fact]
    public async Task Stage3b_LdapBind_WrongPassword_Fails()
    {
        int rc = await LdapSimpleBind(AdTestHost.AdminDn, "WrongPassword!");
        Assert.NotEqual(0, rc);
    }

    [Fact]
    public async Task Stage3c_LdapBind_NonexistentUser_Fails()
    {
        int rc = await LdapSimpleBind("CN=Nobody,CN=Users,DC=test,DC=local", "Whatever");
        Assert.NotEqual(0, rc);
    }

    // ════════════════════════════════════════════════════════════════
    //  Stage 4: LDAP Search (authenticated)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Stage4_LdapSearch_FindsDomainRoot()
    {
        var conn = await LdapConnectAndBind();
        var results = await LdapSearchOnConnection(conn.stream, AdTestHost.DomainDn, 0, "objectClass");
        Assert.True(results.Count > 0, "Should find the domain root object");
    }

    // ════════════════════════════════════════════════════════════════
    //  Stage 5: RPC Endpoint Mapper
    //  Windows queries port 135 to discover SAMR/NRPC dynamic port
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Stage5_RpcEpm_AcceptsBindAndReturnsAck()
    {
        byte pduType = await RpcBindAndGetAckType(
            _host.RpcEpmPort,
            new Guid("e1af8308-5d1f-11c9-91a4-08002b14a0fa"), 3); // EPM interface
        Assert.Equal(12, pduType); // BIND_ACK
    }

    // ════════════════════════════════════════════════════════════════
    //  Stage 6: RPC SAMR Interface Bind
    //  Windows creates machine account via SamrCreateUser2InDomain
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Stage6_RpcSamr_BindAccepted()
    {
        byte pduType = await RpcBindAndGetAckType(
            _host.RpcServicePort,
            new Guid("12345778-1234-abcd-ef00-0123456789ac"), 1); // SAMR
        Assert.Equal(12, pduType);
    }

    // ════════════════════════════════════════════════════════════════
    //  Stage 7: RPC NRPC Interface Bind
    //  Machine establishes secure channel for ongoing auth
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Stage7_RpcNrpc_BindAccepted()
    {
        byte pduType = await RpcBindAndGetAckType(
            _host.RpcServicePort,
            new Guid("12345678-1234-abcd-ef00-01234567cffb"), 1); // NRPC
        Assert.Equal(12, pduType);
    }

    // ════════════════════════════════════════════════════════════════
    //  Stage 8: DNS Dynamic Update (machine A record registration)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Stage8_DnsDynamicUpdate_AcceptsUpdatePacket()
    {
        var update = BuildDnsUpdate(
            zone: AdTestHost.DomainDns,
            name: $"workstation1.{AdTestHost.DomainDns}",
            ipAddress: IPAddress.Parse("192.168.1.100"));

        using var udp = new UdpClient();
        var ep = new IPEndPoint(IPAddress.Loopback, _host.DnsPort);
        await udp.SendAsync(update, update.Length, ep);

        var result = await ReceiveWithTimeout(udp, TimeSpan.FromSeconds(3));
        Assert.NotNull(result);
        Assert.True(result.Length >= 12, "DNS response must be at least 12 bytes");
        Assert.True((result[2] & 0x80) != 0, "QR bit must be set (response)");
    }

    // ════════════════════════════════════════════════════════════════
    //  Stage 9: All RPC interfaces bindable
    //  LSA, DRSUAPI also needed during join/post-join
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Stage9a_RpcLsa_BindAccepted()
    {
        byte pduType = await RpcBindAndGetAckType(
            _host.RpcServicePort,
            new Guid("12345778-1234-abcd-ef00-0123456789ab"), 0); // LSA
        Assert.Equal(12, pduType);
    }

    [Fact]
    public async Task Stage9b_RpcDrsuapi_BindAccepted()
    {
        byte pduType = await RpcBindAndGetAckType(
            _host.RpcServicePort,
            new Guid("e3514235-4b06-11d1-ab04-00c04fc2dcd2"), 4); // DRSUAPI
        Assert.Equal(12, pduType);
    }

    // ════════════════════════════════════════════════════════════════
    //  Stage 10: Full sequence — DNS → RootDSE → Bind → Search
    //  Simulates what Windows does in order during domain join
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Stage10_FullSequence_DnsToLdapBindToSearch()
    {
        // 1. DNS discover LDAP SRV
        var dns = await DnsQuerySrv($"_ldap._tcp.{AdTestHost.DomainDns}");
        Assert.True(dns.AnswerCount > 0);
        Assert.Equal(389, dns.SrvPort);

        // 2. Read RootDSE (anonymous)
        var rootDse = await LdapSearchRootDse("defaultNamingContext", "supportedSASLMechanisms");
        Assert.Contains(AdTestHost.DomainDn, rootDse["defaultNamingContext"]);

        // 3. Bind as admin
        int bindResult = await LdapSimpleBind(AdTestHost.AdminDn, AdTestHost.AdminPassword);
        Assert.Equal(0, bindResult);

        // 4. Search domain root
        var conn = await LdapConnectAndBind();
        var results = await LdapSearchOnConnection(conn.stream, AdTestHost.DomainDn, 0, "objectClass");
        Assert.True(results.Count > 0);

        // 5. EPM is reachable
        byte epmAck = await RpcBindAndGetAckType(
            _host.RpcEpmPort,
            new Guid("e1af8308-5d1f-11c9-91a4-08002b14a0fa"), 3);
        Assert.Equal(12, epmAck);

        // 6. SAMR is reachable
        byte samrAck = await RpcBindAndGetAckType(
            _host.RpcServicePort,
            new Guid("12345778-1234-abcd-ef00-0123456789ac"), 1);
        Assert.Equal(12, samrAck);

        // 7. NRPC is reachable
        byte nrpcAck = await RpcBindAndGetAckType(
            _host.RpcServicePort,
            new Guid("12345678-1234-abcd-ef00-01234567cffb"), 1);
        Assert.Equal(12, nrpcAck);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  Protocol Helpers
    // ═══════════════════════════════════════════════════════════════════════════

    #region DNS

    private async Task<DnsTestResponse> DnsQuerySrv(string name)
    {
        var query = BuildDnsQuery(name, 33);
        using var udp = new UdpClient();
        var ep = new IPEndPoint(IPAddress.Loopback, _host.DnsPort);
        await udp.SendAsync(query, query.Length, ep);
        var data = await ReceiveWithTimeout(udp, TimeSpan.FromSeconds(5));
        Assert.NotNull(data);
        return ParseDnsResponse(data);
    }

    private static byte[] BuildDnsQuery(string name, ushort qtype)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        ushort id = (ushort)Random.Shared.Next(0, 65536);
        w.Write(BinaryPrimitives.ReverseEndianness(id));
        w.Write(BinaryPrimitives.ReverseEndianness((ushort)0x0100));
        w.Write(BinaryPrimitives.ReverseEndianness((ushort)1));
        w.Write(BinaryPrimitives.ReverseEndianness((ushort)0));
        w.Write(BinaryPrimitives.ReverseEndianness((ushort)0));
        w.Write(BinaryPrimitives.ReverseEndianness((ushort)0));
        foreach (var label in name.Split('.'))
        {
            w.Write((byte)label.Length);
            w.Write(Encoding.ASCII.GetBytes(label));
        }
        w.Write((byte)0);
        w.Write(BinaryPrimitives.ReverseEndianness(qtype));
        w.Write(BinaryPrimitives.ReverseEndianness((ushort)1));
        return ms.ToArray();
    }

    private static byte[] BuildDnsUpdate(string zone, string name, IPAddress ipAddress)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        ushort id = (ushort)Random.Shared.Next(0, 65536);
        w.Write(BinaryPrimitives.ReverseEndianness(id));
        w.Write(BinaryPrimitives.ReverseEndianness((ushort)0x2800));
        w.Write(BinaryPrimitives.ReverseEndianness((ushort)1));
        w.Write(BinaryPrimitives.ReverseEndianness((ushort)0));
        w.Write(BinaryPrimitives.ReverseEndianness((ushort)1));
        w.Write(BinaryPrimitives.ReverseEndianness((ushort)0));
        WriteDnsName(w, zone);
        w.Write(BinaryPrimitives.ReverseEndianness((ushort)6));
        w.Write(BinaryPrimitives.ReverseEndianness((ushort)1));
        WriteDnsName(w, name);
        w.Write(BinaryPrimitives.ReverseEndianness((ushort)1));
        w.Write(BinaryPrimitives.ReverseEndianness((ushort)1));
        w.Write(BinaryPrimitives.ReverseEndianness(300));
        var addr = ipAddress.GetAddressBytes();
        w.Write(BinaryPrimitives.ReverseEndianness((ushort)addr.Length));
        w.Write(addr);
        return ms.ToArray();
    }

    private static void WriteDnsName(BinaryWriter w, string name)
    {
        foreach (var label in name.Split('.'))
        {
            w.Write((byte)label.Length);
            w.Write(Encoding.ASCII.GetBytes(label));
        }
        w.Write((byte)0);
    }

    private static DnsTestResponse ParseDnsResponse(byte[] data)
    {
        var resp = new DnsTestResponse
        {
            IsResponse = (data[2] & 0x80) != 0,
            AnswerCount = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(6)),
        };
        if (resp.AnswerCount == 0) return resp;

        int offset = 12;
        offset = SkipDnsName(data, offset);
        offset += 4;

        offset = SkipDnsName(data, offset);
        ushort rtype = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset));
        offset += 2 + 2 + 4; // TYPE + CLASS + TTL
        ushort rdlength = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset));
        offset += 2;

        if (rtype == 33 && rdlength >= 6)
        {
            offset += 2 + 2; // priority + weight
            resp.SrvPort = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset));
            offset += 2;
            resp.SrvTarget = ReadDnsName(data, offset);
        }
        return resp;
    }

    private static int SkipDnsName(byte[] data, int offset)
    {
        while (offset < data.Length)
        {
            byte len = data[offset];
            if (len == 0) return offset + 1;
            if ((len & 0xC0) == 0xC0) return offset + 2;
            offset += 1 + len;
        }
        return offset;
    }

    private static string ReadDnsName(byte[] data, int offset)
    {
        var parts = new List<string>();
        while (offset < data.Length)
        {
            byte len = data[offset];
            if (len == 0) break;
            if ((len & 0xC0) == 0xC0)
            {
                int ptr = ((len & 0x3F) << 8) | data[offset + 1];
                return string.Join(".", parts) + (parts.Count > 0 ? "." : "") + ReadDnsName(data, ptr);
            }
            offset++;
            parts.Add(Encoding.ASCII.GetString(data, offset, len));
            offset += len;
        }
        return string.Join(".", parts);
    }

    private static async Task<byte[]> ReceiveWithTimeout(UdpClient udp, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try { var r = await udp.ReceiveAsync(cts.Token); return r.Buffer; }
        catch (OperationCanceledException) { return null; }
    }

    private record DnsTestResponse
    {
        public bool IsResponse { get; init; }
        public int AnswerCount { get; init; }
        public int SrvPort { get; set; }
        public string SrvTarget { get; set; } = "";
    }

    #endregion

    #region LDAP

    private async Task<Dictionary<string, List<string>>> LdapSearchRootDse(params string[] requestedAttrs)
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, _host.LdapPort);
        var stream = tcp.GetStream();

        var search = BuildLdapSearchRequest(1, "", 0, BuildPresentFilter("objectClass"), requestedAttrs);
        await stream.WriteAsync(search);
        await stream.FlushAsync();

        var messages = await ReadLdapMessages(stream, 2);
        var attrs = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var msg in messages)
            if (FindApplicationTag(msg) == 4)
                ParseSearchResultEntry(msg, attrs);
        return attrs;
    }

    private async Task<int> LdapSimpleBind(string dn, string password)
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, _host.LdapPort);
        var stream = tcp.GetStream();
        var bind = BuildLdapBindRequest(1, dn, password);
        await stream.WriteAsync(bind);
        await stream.FlushAsync();
        var resp = await ReadLdapMessage(stream);
        return ParseBindResultCode(resp);
    }

    private async Task<(TcpClient tcp, NetworkStream stream)> LdapConnectAndBind()
    {
        var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, _host.LdapPort);
        var stream = tcp.GetStream();
        var bind = BuildLdapBindRequest(1, AdTestHost.AdminDn, AdTestHost.AdminPassword);
        await stream.WriteAsync(bind);
        await stream.FlushAsync();
        await ReadLdapMessage(stream); // consume bind response
        return (tcp, stream);
    }

    private static async Task<List<Dictionary<string, List<string>>>> LdapSearchOnConnection(
        NetworkStream stream, string baseDn, int scope, params string[] attrs)
    {
        var search = BuildLdapSearchRequest(2, baseDn, scope, BuildPresentFilter("objectClass"), attrs);
        await stream.WriteAsync(search);
        await stream.FlushAsync();
        var messages = await ReadLdapMessages(stream, 10);
        var results = new List<Dictionary<string, List<string>>>();
        foreach (var msg in messages)
        {
            if (FindApplicationTag(msg) == 4)
            {
                var a = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                ParseSearchResultEntry(msg, a);
                results.Add(a);
            }
        }
        return results;
    }

    private static byte[] BuildLdapSearchRequest(int messageId, string baseDn, int scope,
        byte[] filter, string[] attributes)
    {
        var attrSeq = BerSequence(attributes.Select(BerOctetString).ToArray());
        var searchReq = BerApplication(3,
            BerOctetString(baseDn), BerEnumerated(scope), BerEnumerated(0),
            BerInteger(0), BerInteger(0), BerBoolean(false), filter, attrSeq);
        return BerSequence(BerInteger(messageId), searchReq);
    }

    private static byte[] BuildLdapBindRequest(int messageId, string dn, string password)
    {
        var bindReq = BerApplication(0,
            BerInteger(3), BerOctetString(dn),
            BerContext(0, Encoding.UTF8.GetBytes(password)));
        return BerSequence(BerInteger(messageId), bindReq);
    }

    private static byte[] BuildPresentFilter(string attribute) =>
        BerContextImplicit(7, Encoding.UTF8.GetBytes(attribute));

    // ── BER encoding ──

    private static byte[] BerSequence(params byte[][] e) => BerTlv(0x30, Concat(e));
    private static byte[] BerApplication(int tag, params byte[][] e) => BerTlv((byte)(0x60 | tag), Concat(e));
    private static byte[] BerContext(int tag, byte[] v) => BerTlv((byte)(0x80 | tag), v);
    private static byte[] BerContextImplicit(int tag, byte[] v) => BerTlv((byte)(0x80 | tag), v);
    private static byte[] BerOctetString(string v) => BerTlv(0x04, Encoding.UTF8.GetBytes(v));
    private static byte[] BerBoolean(bool v) => BerTlv(0x01, [v ? (byte)0xFF : (byte)0x00]);
    private static byte[] BerEnumerated(int v) => BerTlv(0x0A, [(byte)v]);

    private static byte[] BerInteger(int value)
    {
        if (value == 0) return BerTlv(0x02, [0]);
        var bytes = new List<byte>();
        int v = value;
        while (v > 0) { bytes.Insert(0, (byte)(v & 0xFF)); v >>= 8; }
        if (bytes[0] >= 0x80) bytes.Insert(0, 0);
        return BerTlv(0x02, bytes.ToArray());
    }

    private static byte[] BerTlv(byte tag, byte[] value)
    {
        using var ms = new MemoryStream();
        ms.WriteByte(tag);
        if (value.Length < 0x80) ms.WriteByte((byte)value.Length);
        else if (value.Length < 0x100) { ms.WriteByte(0x81); ms.WriteByte((byte)value.Length); }
        else { ms.WriteByte(0x82); ms.WriteByte((byte)(value.Length >> 8)); ms.WriteByte((byte)(value.Length & 0xFF)); }
        ms.Write(value);
        return ms.ToArray();
    }

    private static byte[] Concat(byte[][] arrays)
    {
        int total = arrays.Sum(a => a.Length);
        var result = new byte[total];
        int off = 0;
        foreach (var a in arrays) { Buffer.BlockCopy(a, 0, result, off, a.Length); off += a.Length; }
        return result;
    }

    // ── BER decoding ──

    private static async Task<byte[]> ReadLdapMessage(NetworkStream stream)
    {
        var firstByte = new byte[1];
        stream.ReadTimeout = 3000;
        if (await stream.ReadAsync(firstByte) == 0) return [];
        byte tag = firstByte[0];
        int length = await ReadBerLength(stream);
        var value = new byte[length];
        await ReadExact(stream, value);
        using var ms = new MemoryStream();
        ms.WriteByte(tag);
        if (length < 0x80) ms.WriteByte((byte)length);
        else if (length < 0x100) { ms.WriteByte(0x81); ms.WriteByte((byte)length); }
        else { ms.WriteByte(0x82); ms.WriteByte((byte)(length >> 8)); ms.WriteByte((byte)(length & 0xFF)); }
        ms.Write(value);
        return ms.ToArray();
    }

    private static async Task<List<byte[]>> ReadLdapMessages(NetworkStream stream, int max)
    {
        var msgs = new List<byte[]>();
        for (int i = 0; i < max; i++)
        {
            try
            {
                var msg = await ReadLdapMessage(stream);
                if (msg.Length == 0) break;
                msgs.Add(msg);
                int appTag = FindApplicationTag(msg);
                if (appTag == 5 || appTag == 1) break; // SearchResultDone or BindResponse
            }
            catch (IOException) { break; }
        }
        return msgs;
    }

    private static async Task<int> ReadBerLength(NetworkStream stream)
    {
        var b = new byte[1];
        await ReadExact(stream, b);
        if (b[0] < 0x80) return b[0];
        int numBytes = b[0] & 0x7F;
        var lenBytes = new byte[numBytes];
        await ReadExact(stream, lenBytes);
        int length = 0;
        for (int i = 0; i < numBytes; i++) length = (length << 8) | lenBytes[i];
        return length;
    }

    private static int FindApplicationTag(byte[] msg)
    {
        if (msg.Length < 5 || msg[0] != 0x30) return -1;
        int offset = 1;
        offset += SkipBerLength(msg, offset, out _);
        if (offset >= msg.Length || msg[offset] != 0x02) return -1;
        offset++;
        offset += SkipBerLength(msg, offset, out int intLen);
        offset += intLen;
        if (offset >= msg.Length) return -1;
        byte appTag = msg[offset];
        return (appTag & 0x40) != 0 ? appTag & 0x1F : -1;
    }

    private static int SkipBerLength(byte[] data, int offset, out int length)
    {
        if (offset >= data.Length) { length = 0; return 1; }
        byte b = data[offset];
        if (b < 0x80) { length = b; return 1; }
        int n = b & 0x7F;
        length = 0;
        for (int i = 0; i < n && offset + 1 + i < data.Length; i++)
            length = (length << 8) | data[offset + 1 + i];
        return 1 + n;
    }

    private static void ParseSearchResultEntry(byte[] msg, Dictionary<string, List<string>> attrs)
    {
        try
        {
            int offset = 1;
            offset += SkipBerLength(msg, offset, out _);
            if (msg[offset] == 0x02) { offset++; offset += SkipBerLength(msg, offset, out int il); offset += il; }
            if (offset < msg.Length && (msg[offset] & 0x60) == 0x60) { offset++; offset += SkipBerLength(msg, offset, out _); }
            if (offset < msg.Length && msg[offset] == 0x04) { offset++; offset += SkipBerLength(msg, offset, out int sl); offset += sl; }

            if (offset < msg.Length && msg[offset] == 0x30)
            {
                offset++;
                offset += SkipBerLength(msg, offset, out int seqLen);
                int seqEnd = offset + seqLen;

                while (offset < seqEnd && offset < msg.Length)
                {
                    if (msg[offset] != 0x30) break;
                    offset++;
                    offset += SkipBerLength(msg, offset, out int attrSeqLen);
                    int attrEnd = offset + attrSeqLen;

                    if (offset >= msg.Length || msg[offset] != 0x04) { offset = attrEnd; continue; }
                    offset++;
                    offset += SkipBerLength(msg, offset, out int nameLen);
                    string name = Encoding.UTF8.GetString(msg, offset, nameLen);
                    offset += nameLen;

                    var vals = new List<string>();
                    if (offset < attrEnd && msg[offset] == 0x31)
                    {
                        offset++;
                        offset += SkipBerLength(msg, offset, out int setLen);
                        int setEnd = offset + setLen;
                        while (offset < setEnd)
                        {
                            if (msg[offset] != 0x04) break;
                            offset++;
                            offset += SkipBerLength(msg, offset, out int vl);
                            vals.Add(Encoding.UTF8.GetString(msg, offset, vl));
                            offset += vl;
                        }
                    }
                    attrs[name] = vals;
                    offset = attrEnd;
                }
            }
        }
        catch { }
    }

    private static int ParseBindResultCode(byte[] msg)
    {
        try
        {
            int offset = 1;
            offset += SkipBerLength(msg, offset, out _);
            if (msg[offset] == 0x02) { offset++; offset += SkipBerLength(msg, offset, out int il); offset += il; }
            if (offset < msg.Length && (msg[offset] & 0x60) == 0x60) { offset++; offset += SkipBerLength(msg, offset, out _); }
            if (offset < msg.Length && msg[offset] == 0x0A) { offset++; offset += SkipBerLength(msg, offset, out _); return msg[offset]; }
        }
        catch { }
        return -1;
    }

    #endregion

    #region RPC

    private async Task<byte> RpcBindAndGetAckType(int port, Guid interfaceUuid, ushort majorVersion)
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, port);
        var bind = BuildRpcBind(1, interfaceUuid, majorVersion);
        var stream = tcp.GetStream();
        await stream.WriteAsync(bind);
        await stream.FlushAsync();
        var header = new byte[16];
        stream.ReadTimeout = 3000;
        await ReadExact(stream, header);
        return header[2];
    }

    private static byte[] BuildRpcBind(uint callId, Guid interfaceUuid, ushort majorVersion)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((byte)5);    // rpc_vers
        w.Write((byte)0);    // rpc_vers_minor
        w.Write((byte)11);   // PTYPE = BIND
        w.Write((byte)0x03); // PFC_FIRST_FRAG | PFC_LAST_FRAG
        w.Write(0x00000010); // data_rep: little-endian
        w.Write((ushort)0);  // frag_length placeholder
        w.Write((ushort)0);  // auth_length
        w.Write(callId);
        w.Write((ushort)4280); // max_xmit_frag
        w.Write((ushort)4280); // max_recv_frag
        w.Write((uint)0);      // assoc_group_id
        w.Write((byte)1);     // num_ctx_items
        w.Write((byte)0); w.Write((ushort)0); // padding
        w.Write((ushort)0);    // context_id
        w.Write((byte)1);     // num_transfer_syntaxes
        w.Write((byte)0);     // padding
        w.Write(interfaceUuid.ToByteArray());
        w.Write(majorVersion);
        w.Write((ushort)0);
        w.Write(new Guid("8a885d04-1ceb-11c9-9fe8-08002b104860").ToByteArray()); // NDR
        w.Write((ushort)2);
        w.Write((ushort)0);
        var data = ms.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(8), (ushort)data.Length);
        return data;
    }

    #endregion

    #region Common

    private static async Task ReadExact(Stream stream, byte[] buffer)
    {
        int off = 0;
        while (off < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(off));
            if (read == 0) throw new EndOfStreamException();
            off += read;
        }
    }

    #endregion
}
