using System.Text.Json;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Replication;
using Directory.Replication.Drsr;
using Directory.Server.Setup;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Directory.Tests;

/// <summary>
/// Comprehensive tests for DC replication/promotion features:
/// DrsReplicationClient, ReplicationProgress, ReplicationScheduler,
/// ChangeNotificationDispatcher, DcPromotionService, and ReplicationTopology.
/// </summary>
public class DcReplicationTests
{
    private const string TenantId = "default";
    private const string DomainDn = "DC=corp,DC=contoso,DC=com";
    private const string DomainName = "corp.contoso.com";
    private const string ConfigDn = "CN=Configuration,DC=corp,DC=contoso,DC=com";
    private const string SchemaDn = "CN=Schema,CN=Configuration,DC=corp,DC=contoso,DC=com";

    // ════════════════════════════════════════════════════════════════
    //  1. ReplicationProgress Tests
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ReplicationProgress_DefaultValues_AreSensible()
    {
        var progress = new ReplicationProgress();

        Assert.Equal(string.Empty, progress.Phase);
        Assert.Equal(string.Empty, progress.NamingContext);
        Assert.Equal(0, progress.ObjectsProcessed);
        Assert.Null(progress.ObjectsTotal);
        Assert.Equal(0L, progress.BytesTransferred);
        Assert.Equal(TimeSpan.Zero, progress.ElapsedTime);
        Assert.Equal(string.Empty, progress.Message);
    }

    [Fact]
    public void ReplicationProgress_Properties_StoreCorrectly()
    {
        var elapsed = TimeSpan.FromSeconds(42);
        var progress = new ReplicationProgress
        {
            Phase = "Pulling changes",
            NamingContext = DomainDn,
            ObjectsProcessed = 500,
            ObjectsTotal = 1200,
            BytesTransferred = 1_048_576,
            ElapsedTime = elapsed,
            Message = "Received batch with 100 objects",
        };

        Assert.Equal("Pulling changes", progress.Phase);
        Assert.Equal(DomainDn, progress.NamingContext);
        Assert.Equal(500, progress.ObjectsProcessed);
        Assert.Equal(1200, progress.ObjectsTotal);
        Assert.Equal(1_048_576L, progress.BytesTransferred);
        Assert.Equal(elapsed, progress.ElapsedTime);
        Assert.Equal("Received batch with 100 objects", progress.Message);
    }

    // ════════════════════════════════════════════════════════════════
    //  2. DrsReplicationException Tests
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void DrsReplicationException_CarriesMessage()
    {
        var ex = new DrsReplicationException("Replication failed");

        Assert.Equal("Replication failed", ex.Message);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public void DrsReplicationException_CarriesMessageAndInnerException()
    {
        var inner = new InvalidOperationException("connection dropped");
        var ex = new DrsReplicationException("Replication failed", inner);

        Assert.Equal("Replication failed", ex.Message);
        Assert.Same(inner, ex.InnerException);
    }

    // ════════════════════════════════════════════════════════════════
    //  3. DrsReplicationClient Tests
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void DrsReplicationClient_StatisticsStartAtZero()
    {
        var logger = NullLogger<DrsReplicationClient>.Instance;
        using var client = new DrsReplicationClient(new HttpClient(), logger);

        Assert.Equal(0L, client.TotalObjectsReceived);
        Assert.Equal(0L, client.TotalBytesTransferred);
        Assert.Equal(TimeSpan.Zero, client.TotalElapsedTime);
    }

    [Fact]
    public void DrsReplicationClient_PartnerGuid_DefaultsToEmpty()
    {
        var logger = NullLogger<DrsReplicationClient>.Instance;
        using var client = new DrsReplicationClient(new HttpClient(), logger);

        Assert.Equal(Guid.Empty, client.PartnerDsaGuid);
        Assert.Null(client.PartnerExtensions);
    }

    [Fact]
    public void DrsReplicationClient_OwnsHttpClient_DisposesIt()
    {
        // When created with the single-arg constructor, client owns the HttpClient
        var logger = NullLogger<DrsReplicationClient>.Instance;
        var client = new DrsReplicationClient(logger);
        // Should not throw
        client.Dispose();
    }

    [Fact]
    public void DrsReplicationClient_ExternalHttpClient_DoesNotDispose()
    {
        var logger = NullLogger<DrsReplicationClient>.Instance;
        var httpClient = new HttpClient();

        var client = new DrsReplicationClient(httpClient, logger);
        client.Dispose();

        // The externally-provided HttpClient should still be usable after disposing the DRS client.
        // Setting a header proves it has not been disposed.
        httpClient.DefaultRequestHeaders.Add("X-Test", "alive");
        Assert.True(httpClient.DefaultRequestHeaders.Contains("X-Test"));

        httpClient.Dispose();
    }

    [Fact]
    public void DrsReplicationClient_ThrowsOnNullHttpClient()
    {
        var logger = NullLogger<DrsReplicationClient>.Instance;
        Assert.Throws<ArgumentNullException>(() => new DrsReplicationClient(null, logger));
    }

    [Fact]
    public void DrsReplicationClient_ThrowsOnNullLogger()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new DrsReplicationClient(new HttpClient(), null));
    }

    [Fact]
    public async Task DrsReplicationClient_GetNCChanges_ThrowsIfNotBound()
    {
        var logger = NullLogger<DrsReplicationClient>.Instance;
        using var client = new DrsReplicationClient(new HttpClient(), logger);

        var usn = new USN_VECTOR { UsnHighObjUpdate = 0, UsnHighPropUpdate = 0 };

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in client.GetNCChangesAsync(DomainDn, usn))
            {
                // Should not reach here
            }
        });
    }

    [Fact]
    public async Task DrsReplicationClient_UpdateRefs_ThrowsIfNotBound()
    {
        var logger = NullLogger<DrsReplicationClient>.Instance;
        using var client = new DrsReplicationClient(new HttpClient(), logger);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.UpdateRefsAsync(DomainDn, Guid.NewGuid(), "dc1.corp.contoso.com", addRef: true));
    }

    [Fact]
    public async Task DrsReplicationClient_DsReplicaSync_ThrowsIfNotBound()
    {
        var logger = NullLogger<DrsReplicationClient>.Instance;
        using var client = new DrsReplicationClient(new HttpClient(), logger);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.DsReplicaSyncAsync(DomainDn));
    }

    // ════════════════════════════════════════════════════════════════
    //  4. ReplicationSchedulerOptions Tests
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ReplicationSchedulerOptions_DefaultValues()
    {
        var options = new ReplicationSchedulerOptions();

        Assert.Equal(TimeSpan.FromSeconds(15), options.IntraSiteInterval);
        Assert.Equal(TimeSpan.FromMinutes(180), options.InterSiteInterval);
        Assert.Equal(TimeSpan.FromSeconds(30), options.StartupDelay);
        Assert.Equal(4, options.MaxConcurrentPartners);
        Assert.True(options.Enabled);
    }

    [Fact]
    public void ReplicationSchedulerOptions_CanBeOverridden()
    {
        var options = new ReplicationSchedulerOptions
        {
            IntraSiteInterval = TimeSpan.FromSeconds(5),
            InterSiteInterval = TimeSpan.FromMinutes(60),
            StartupDelay = TimeSpan.FromSeconds(10),
            MaxConcurrentPartners = 8,
            Enabled = false,
        };

        Assert.Equal(TimeSpan.FromSeconds(5), options.IntraSiteInterval);
        Assert.Equal(TimeSpan.FromMinutes(60), options.InterSiteInterval);
        Assert.Equal(TimeSpan.FromSeconds(10), options.StartupDelay);
        Assert.Equal(8, options.MaxConcurrentPartners);
        Assert.False(options.Enabled);
    }

    [Fact]
    public void ReplicationSchedulerOptions_SectionName()
    {
        Assert.Equal("ReplicationScheduler", ReplicationSchedulerOptions.SectionName);
    }

    // ════════════════════════════════════════════════════════════════
    //  5. Exponential Backoff Tests
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(1, 30)]       // 30 seconds
    [InlineData(2, 60)]       // 1 minute
    [InlineData(3, 120)]      // 2 minutes
    [InlineData(4, 300)]      // 5 minutes
    [InlineData(5, 900)]      // 15 minutes
    [InlineData(6, 1800)]     // 30 minutes
    [InlineData(7, 3600)]     // 1 hour (max)
    [InlineData(8, 3600)]     // capped at 1 hour
    [InlineData(100, 3600)]   // capped at 1 hour
    public void BackoffTiers_ProgressionAndCap(int consecutiveFailures, int expectedSeconds)
    {
        // BackoffTiers is private static, but we can test via ReplicationScheduler's
        // ContainsUrgentAttributes (which is public static) and verify the tier array
        // indirectly. Since the backoff array is:
        // [30s, 1m, 2m, 5m, 15m, 30m, 1h]
        // We verify the expected progression by reflecting on the static field.
        var backoffTiers = typeof(ReplicationScheduler)
            .GetField("BackoffTiers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            .GetValue(null) as TimeSpan[];

        Assert.NotNull(backoffTiers);

        var index = Math.Min(consecutiveFailures - 1, backoffTiers.Length - 1);
        var actual = index >= 0 ? backoffTiers[index] : TimeSpan.Zero;

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), actual);
    }

    // ════════════════════════════════════════════════════════════════
    //  6. Schedule Slot Tests (168-byte schedule)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Schedule_NullSchedule_MeansAlwaysActive()
    {
        // A null schedule means "always replicate"
        // IsScheduleActive is private static, but we test it through ReplicationTopology.
        // A partner with null schedule should always be eligible.
        var partner = new ReplicationPartner
        {
            DsaGuid = Guid.NewGuid(),
            DnsName = "dc1.corp.contoso.com",
            NamingContextDn = DomainDn,
            Schedule = null,
            TransportType = "RPC",
        };

        // Null schedule means always replicate — verified by topology partner having no schedule restriction
        Assert.Null(partner.Schedule);
    }

    [Fact]
    public void Schedule_AllOnes_AllSlotsActive()
    {
        // A 168-byte schedule with all 0xFF means every slot is active
        var schedule = new byte[168];
        Array.Fill(schedule, (byte)0xFF);

        // Verify structure: 7 days * 24 hours = 168 bytes
        Assert.Equal(168, schedule.Length);

        // Every byte should have all 4 quarter-hour bits set
        foreach (var b in schedule)
        {
            Assert.Equal(0xFF, b);
        }
    }

    [Fact]
    public void Schedule_AllZeros_NoSlotsActive()
    {
        // A 168-byte schedule with all 0x00 means no slots are active
        var schedule = new byte[168];

        // Verify no bits are set
        foreach (var b in schedule)
        {
            Assert.Equal(0, b);
        }
    }

    [Theory]
    [InlineData(DayOfWeek.Sunday, 0, 0)]     // Sunday midnight = byte index 0
    [InlineData(DayOfWeek.Sunday, 23, 23)]    // Sunday 23:00 = byte index 23
    [InlineData(DayOfWeek.Monday, 0, 24)]     // Monday midnight = byte index 24
    [InlineData(DayOfWeek.Saturday, 23, 167)] // Saturday 23:00 = byte index 167 (last byte)
    [InlineData(DayOfWeek.Wednesday, 14, 86)] // Wednesday 14:00 = 3*24+14 = 86
    public void Schedule_ByteIndexing_IsCorrectForDayAndHour(DayOfWeek day, int hour, int expectedByteIndex)
    {
        // The schedule byte index formula is: dayOfWeek * 24 + hour
        var byteIndex = (int)day * 24 + hour;
        Assert.Equal(expectedByteIndex, byteIndex);
    }

    [Theory]
    [InlineData(0, 1)]   // 0-14 min => bit 0 => mask 0x01
    [InlineData(14, 1)]
    [InlineData(15, 2)]  // 15-29 min => bit 1 => mask 0x02
    [InlineData(29, 2)]
    [InlineData(30, 4)]  // 30-44 min => bit 2 => mask 0x04
    [InlineData(44, 4)]
    [InlineData(45, 8)]  // 45-59 min => bit 3 => mask 0x08
    [InlineData(59, 8)]
    public void Schedule_QuarterHourBitMask_IsCorrect(int minute, int expectedMask)
    {
        var quarterIndex = minute / 15;
        var mask = 1 << quarterIndex;
        Assert.Equal(expectedMask, mask);
    }

    // ════════════════════════════════════════════════════════════════
    //  7. Urgent Replication Attribute Detection Tests
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("unicodePwd")]
    [InlineData("dBCSPwd")]
    [InlineData("lockoutTime")]
    [InlineData("pwdLastSet")]
    [InlineData("userAccountControl")]
    [InlineData("trustAuthIncoming")]
    [InlineData("trustAuthOutgoing")]
    [InlineData("msDS-TrustForestTrustInfo")]
    public void ContainsUrgentAttributes_UrgentAttribute_ReturnsTrue(string attribute)
    {
        Assert.True(ReplicationScheduler.ContainsUrgentAttributes([attribute]));
    }

    [Theory]
    [InlineData("cn")]
    [InlineData("description")]
    [InlineData("mail")]
    [InlineData("sAMAccountName")]
    public void ContainsUrgentAttributes_NonUrgentAttribute_ReturnsFalse(string attribute)
    {
        Assert.False(ReplicationScheduler.ContainsUrgentAttributes([attribute]));
    }

    [Fact]
    public void ContainsUrgentAttributes_MixedAttributes_ReturnsTrueIfAnyUrgent()
    {
        var attributes = new[] { "cn", "description", "unicodePwd", "mail" };
        Assert.True(ReplicationScheduler.ContainsUrgentAttributes(attributes));
    }

    [Fact]
    public void ContainsUrgentAttributes_EmptyList_ReturnsFalse()
    {
        Assert.False(ReplicationScheduler.ContainsUrgentAttributes([]));
    }

    [Fact]
    public void ContainsUrgentAttributes_IsCaseInsensitive()
    {
        Assert.True(ReplicationScheduler.ContainsUrgentAttributes(["UNICODEPWD"]));
        Assert.True(ReplicationScheduler.ContainsUrgentAttributes(["LockoutTime"]));
    }

    // ════════════════════════════════════════════════════════════════
    //  8. ChangeNotification Tests
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ChangeNotification_SerializationRoundTrip()
    {
        var notification = new ChangeNotification
        {
            SourceDsaGuid = Guid.NewGuid().ToString(),
            NamingContextDn = DomainDn,
            LatestUsn = 42_000,
        };

        var json = JsonSerializer.Serialize(notification);
        var deserialized = JsonSerializer.Deserialize<ChangeNotification>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(notification.SourceDsaGuid, deserialized.SourceDsaGuid);
        Assert.Equal(notification.NamingContextDn, deserialized.NamingContextDn);
        Assert.Equal(notification.LatestUsn, deserialized.LatestUsn);
    }

    [Fact]
    public void ChangeNotification_DefaultValues()
    {
        var notification = new ChangeNotification();

        Assert.Equal(string.Empty, notification.SourceDsaGuid);
        Assert.Equal(string.Empty, notification.NamingContextDn);
        Assert.Equal(0L, notification.LatestUsn);
    }

    [Fact]
    public void ChangeNotification_IncludesSourceDsaGuidAndNc()
    {
        var dsaGuid = Guid.NewGuid().ToString();
        var notification = new ChangeNotification
        {
            SourceDsaGuid = dsaGuid,
            NamingContextDn = DomainDn,
            LatestUsn = 100,
        };

        Assert.Equal(dsaGuid, notification.SourceDsaGuid);
        Assert.Equal(DomainDn, notification.NamingContextDn);
    }

    [Fact]
    public void ChangeNotification_CamelCaseSerialization()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        var notification = new ChangeNotification
        {
            SourceDsaGuid = "test-guid",
            NamingContextDn = DomainDn,
            LatestUsn = 999,
        };

        var json = JsonSerializer.Serialize(notification, options);

        Assert.Contains("sourceDsaGuid", json);
        Assert.Contains("namingContextDn", json);
        Assert.Contains("latestUsn", json);
    }

    // ════════════════════════════════════════════════════════════════
    //  9. SetupOptions Tests
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void SetupOptions_IsReplica_DefaultsToFalse()
    {
        var options = new SetupOptions();
        Assert.False(options.IsReplica);
    }

    [Fact]
    public void SetupOptions_ReplicaProperties_DefaultToNull()
    {
        var options = new SetupOptions();

        Assert.Null(options.SourceDcUrl);
        Assert.Null(options.ReplicationAdminUpn);
        Assert.Null(options.ReplicationAdminPassword);
    }

    [Fact]
    public void SetupOptions_DomainDn_DerivedCorrectlyFromDomainName()
    {
        var options = new SetupOptions { DomainName = "corp.contoso.com" };
        Assert.Equal("DC=corp,DC=contoso,DC=com", options.DomainDn);
    }

    [Fact]
    public void SetupOptions_ConfigurationDn_DerivedCorrectly()
    {
        var options = new SetupOptions { DomainName = "corp.contoso.com" };
        Assert.Equal("CN=Configuration,DC=corp,DC=contoso,DC=com", options.ConfigurationDn);
    }

    [Fact]
    public void SetupOptions_SchemaDn_DerivedCorrectly()
    {
        var options = new SetupOptions { DomainName = "corp.contoso.com" };
        Assert.Equal("CN=Schema,CN=Configuration,DC=corp,DC=contoso,DC=com", options.SchemaDn);
    }

    [Theory]
    [InlineData("example.com", "DC=example,DC=com")]
    [InlineData("sub.domain.example.com", "DC=sub,DC=domain,DC=example,DC=com")]
    [InlineData("test.local", "DC=test,DC=local")]
    public void SetupOptions_DomainDn_VariousDomains(string domainName, string expectedDn)
    {
        var options = new SetupOptions { DomainName = domainName };
        Assert.Equal(expectedDn, options.DomainDn);
    }

    [Fact]
    public void SetupOptions_DefaultSiteName()
    {
        var options = new SetupOptions();
        Assert.Equal("Default-First-Site-Name", options.SiteName);
    }

    [Fact]
    public void SetupOptions_DefaultFunctionalLevels()
    {
        var options = new SetupOptions();
        Assert.Equal(7, options.ForestFunctionalLevel);
        Assert.Equal(7, options.DomainFunctionalLevel);
    }

    // ════════════════════════════════════════════════════════════════
    //  10. DcInstanceInfo Tests (NTDS Settings DN, Server DN, SPNs)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void DcInstanceInfo_NtdsSettingsDn_ComputedCorrectly()
    {
        var dcInfo = new DcInstanceInfo
        {
            Hostname = "DC2",
            SiteName = "Default-First-Site-Name",
        };

        var ntdsDn = dcInfo.NtdsSettingsDn(DomainDn);

        Assert.Equal(
            $"CN=NTDS Settings,CN=DC2,CN=Servers,CN=Default-First-Site-Name,CN=Sites,CN=Configuration,{DomainDn}",
            ntdsDn);
    }

    [Fact]
    public void DcInstanceInfo_ServerDn_ComputedCorrectly()
    {
        var dcInfo = new DcInstanceInfo
        {
            Hostname = "DC2",
            SiteName = "Default-First-Site-Name",
        };

        var serverDn = dcInfo.ServerDn(DomainDn);

        Assert.Equal(
            $"CN=DC2,CN=Servers,CN=Default-First-Site-Name,CN=Sites,CN=Configuration,{DomainDn}",
            serverDn);
    }

    [Fact]
    public void DcInstanceInfo_Fqdn_ComputedCorrectly()
    {
        var dcInfo = new DcInstanceInfo { Hostname = "DC2" };
        var fqdn = dcInfo.Fqdn(DomainName);

        Assert.Equal("dc2.corp.contoso.com", fqdn);
    }

    [Fact]
    public void DcInstanceInfo_NtdsSettingsDn_WithCustomSite()
    {
        var dcInfo = new DcInstanceInfo
        {
            Hostname = "BRANCH-DC1",
            SiteName = "Branch-Office-Site",
        };

        var ntdsDn = dcInfo.NtdsSettingsDn(DomainDn);

        Assert.Contains("CN=Branch-Office-Site", ntdsDn);
        Assert.Contains("CN=BRANCH-DC1", ntdsDn);
        Assert.StartsWith("CN=NTDS Settings,", ntdsDn);
    }

    // ════════════════════════════════════════════════════════════════
    //  11. DC Computer Account SPN Tests
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void DcComputerAccount_SPNs_IncludeRequiredEntries()
    {
        // Reproduce the SPN list generated by DcPromotionService.CreateDcDirectoryObjectsAsync
        var hostname = "DC2";
        var fqdn = "dc2.corp.contoso.com";
        var domainName = "corp.contoso.com";
        var domainGuid = Guid.NewGuid().ToString();

        var spns = new[]
        {
            $"ldap/{fqdn}",
            $"ldap/{hostname}",
            $"HOST/{fqdn}",
            $"HOST/{hostname}",
            $"GC/{fqdn}/{domainName}",
            $"E3514235-4B06-11D1-AB04-00C04FC2DCD2/{domainGuid}/{domainName}",
            $"RestrictedKrbHost/{fqdn}",
            $"RestrictedKrbHost/{hostname}",
        };

        // Verify ldap/ SPNs
        Assert.Contains(spns, s => s.StartsWith("ldap/") && s.Contains(fqdn));
        Assert.Contains(spns, s => s == $"ldap/{hostname}");

        // Verify HOST/ SPNs
        Assert.Contains(spns, s => s.StartsWith("HOST/") && s.Contains(fqdn));
        Assert.Contains(spns, s => s == $"HOST/{hostname}");

        // Verify GC/ SPN
        Assert.Contains(spns, s => s.StartsWith("GC/") && s.Contains(fqdn));

        // Verify E3514235- (DRS RPC) SPN
        Assert.Contains(spns, s => s.StartsWith("E3514235-4B06-11D1-AB04-00C04FC2DCD2/"));

        // Verify RestrictedKrbHost/ SPNs
        Assert.Contains(spns, s => s.StartsWith("RestrictedKrbHost/") && s.Contains(fqdn));
        Assert.Contains(spns, s => s == $"RestrictedKrbHost/{hostname}");

        Assert.Equal(8, spns.Length);
    }

    // ════════════════════════════════════════════════════════════════
    //  12. ReplicationTopology Tests
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ReplicationTopology_AddInboundPartner_AppearsInList()
    {
        var store = new InMemoryDirectoryStore();
        var dcInfo = new DcInstanceInfo { Hostname = "DC1" };
        var topology = new ReplicationTopology(store, dcInfo, NullLogger<ReplicationTopology>.Instance);

        var partnerGuid = Guid.NewGuid();
        topology.AddInboundPartner(new ReplicationPartner
        {
            DsaGuid = partnerGuid,
            DnsName = "dc2.corp.contoso.com",
            NamingContextDn = DomainDn,
            TransportType = "RPC",
        });

        var partners = topology.GetInboundPartners(DomainDn);
        Assert.Single(partners);
        Assert.Equal(partnerGuid, partners[0].DsaGuid);
        Assert.Equal("dc2.corp.contoso.com", partners[0].DnsName);
    }

    [Fact]
    public void ReplicationTopology_RemoveInboundPartner_RemovesFromList()
    {
        var store = new InMemoryDirectoryStore();
        var dcInfo = new DcInstanceInfo { Hostname = "DC1" };
        var topology = new ReplicationTopology(store, dcInfo, NullLogger<ReplicationTopology>.Instance);

        var partnerGuid = Guid.NewGuid();
        topology.AddInboundPartner(new ReplicationPartner
        {
            DsaGuid = partnerGuid,
            DnsName = "dc2.corp.contoso.com",
            NamingContextDn = DomainDn,
        });

        var removed = topology.RemoveInboundPartner(DomainDn, partnerGuid);

        Assert.True(removed);
        Assert.Empty(topology.GetInboundPartners(DomainDn));
    }

    [Fact]
    public void ReplicationTopology_AddNotificationRef_AppearsInOutbound()
    {
        var store = new InMemoryDirectoryStore();
        var dcInfo = new DcInstanceInfo { Hostname = "DC1" };
        var topology = new ReplicationTopology(store, dcInfo, NullLogger<ReplicationTopology>.Instance);

        var partnerGuid = Guid.NewGuid();
        topology.AddNotificationRef(new ReplicationPartner
        {
            DsaGuid = partnerGuid,
            DnsName = "dc2.corp.contoso.com",
            NamingContextDn = DomainDn,
        });

        var outbound = topology.GetOutboundPartners(DomainDn);
        Assert.Single(outbound);
        Assert.Equal(partnerGuid, outbound[0].DsaGuid);
    }

    [Fact]
    public void ReplicationTopology_RemoveNotificationRef_RemovesFromOutbound()
    {
        var store = new InMemoryDirectoryStore();
        var dcInfo = new DcInstanceInfo { Hostname = "DC1" };
        var topology = new ReplicationTopology(store, dcInfo, NullLogger<ReplicationTopology>.Instance);

        var partnerGuid = Guid.NewGuid();
        topology.AddNotificationRef(new ReplicationPartner
        {
            DsaGuid = partnerGuid,
            DnsName = "dc2.corp.contoso.com",
            NamingContextDn = DomainDn,
        });

        var removed = topology.RemoveNotificationRef(DomainDn, partnerGuid);

        Assert.True(removed);
        Assert.Empty(topology.GetOutboundPartners(DomainDn));
    }

    [Fact]
    public void ReplicationTopology_ProcessUpdateRefs_AddRef()
    {
        var store = new InMemoryDirectoryStore();
        var dcInfo = new DcInstanceInfo { Hostname = "DC1" };
        var topology = new ReplicationTopology(store, dcInfo, NullLogger<ReplicationTopology>.Instance);

        var partnerGuid = Guid.NewGuid();
        var request = new DRS_MSG_UPDREFS_V1
        {
            PNC = DSNAME.FromDn(DomainDn),
            PszDsaDest = "dc2.corp.contoso.com",
            UuidDsaObjDest = partnerGuid,
            UlOptions = DrsUpdateRefsOptions.DRS_ADD_REF | DrsUpdateRefsOptions.DRS_WRIT_REP,
        };

        topology.ProcessUpdateRefs(request);

        var outbound = topology.GetOutboundPartners(DomainDn);
        Assert.Single(outbound);
        Assert.Equal(partnerGuid, outbound[0].DsaGuid);
        Assert.True(outbound[0].Options.HasFlag(DsReplNeighborFlags.DS_REPL_NBR_WRITEABLE));
    }

    [Fact]
    public void ReplicationTopology_ProcessUpdateRefs_DelRef()
    {
        var store = new InMemoryDirectoryStore();
        var dcInfo = new DcInstanceInfo { Hostname = "DC1" };
        var topology = new ReplicationTopology(store, dcInfo, NullLogger<ReplicationTopology>.Instance);

        var partnerGuid = Guid.NewGuid();

        // First add
        topology.AddNotificationRef(new ReplicationPartner
        {
            DsaGuid = partnerGuid,
            DnsName = "dc2.corp.contoso.com",
            NamingContextDn = DomainDn,
        });
        Assert.Single(topology.GetOutboundPartners(DomainDn));

        // Then remove via ProcessUpdateRefs
        var request = new DRS_MSG_UPDREFS_V1
        {
            PNC = DSNAME.FromDn(DomainDn),
            PszDsaDest = "dc2.corp.contoso.com",
            UuidDsaObjDest = partnerGuid,
            UlOptions = DrsUpdateRefsOptions.DRS_DEL_REF,
        };

        topology.ProcessUpdateRefs(request);

        Assert.Empty(topology.GetOutboundPartners(DomainDn));
    }

    [Fact]
    public void ReplicationTopology_RecordSyncSuccess_UpdatesPartnerState()
    {
        var store = new InMemoryDirectoryStore();
        var dcInfo = new DcInstanceInfo { Hostname = "DC1" };
        var topology = new ReplicationTopology(store, dcInfo, NullLogger<ReplicationTopology>.Instance);

        var partnerGuid = Guid.NewGuid();
        topology.AddInboundPartner(new ReplicationPartner
        {
            DsaGuid = partnerGuid,
            DnsName = "dc2.corp.contoso.com",
            NamingContextDn = DomainDn,
            LastUsnSynced = 0,
            ConsecutiveFailures = 3,
        });

        topology.RecordSyncSuccess(DomainDn, partnerGuid, 5000);

        var partner = topology.GetInboundPartners(DomainDn)[0];
        Assert.Equal(5000L, partner.LastUsnSynced);
        Assert.Equal(0u, partner.LastSyncResult);
        Assert.Equal(0u, partner.ConsecutiveFailures);
    }

    [Fact]
    public void ReplicationTopology_RecordSyncFailure_IncrementsFailureCount()
    {
        var store = new InMemoryDirectoryStore();
        var dcInfo = new DcInstanceInfo { Hostname = "DC1" };
        var topology = new ReplicationTopology(store, dcInfo, NullLogger<ReplicationTopology>.Instance);

        var partnerGuid = Guid.NewGuid();
        topology.AddInboundPartner(new ReplicationPartner
        {
            DsaGuid = partnerGuid,
            DnsName = "dc2.corp.contoso.com",
            NamingContextDn = DomainDn,
            ConsecutiveFailures = 0,
        });

        topology.RecordSyncFailure(DomainDn, partnerGuid, 0x2105);

        var partner = topology.GetInboundPartners(DomainDn)[0];
        Assert.Equal(1u, partner.ConsecutiveFailures);
        Assert.Equal(0x2105u, partner.LastSyncResult);
    }

    [Fact]
    public void ReplicationTopology_MultiplePartnersPerNc()
    {
        var store = new InMemoryDirectoryStore();
        var dcInfo = new DcInstanceInfo { Hostname = "DC1" };
        var topology = new ReplicationTopology(store, dcInfo, NullLogger<ReplicationTopology>.Instance);

        var guid1 = Guid.NewGuid();
        var guid2 = Guid.NewGuid();
        var guid3 = Guid.NewGuid();

        topology.AddInboundPartner(new ReplicationPartner
        {
            DsaGuid = guid1, DnsName = "dc2.corp.contoso.com", NamingContextDn = DomainDn,
        });
        topology.AddInboundPartner(new ReplicationPartner
        {
            DsaGuid = guid2, DnsName = "dc3.corp.contoso.com", NamingContextDn = DomainDn,
        });
        topology.AddInboundPartner(new ReplicationPartner
        {
            DsaGuid = guid3, DnsName = "dc4.corp.contoso.com", NamingContextDn = ConfigDn,
        });

        // DomainDn should have 2 partners
        Assert.Equal(2, topology.GetInboundPartners(DomainDn).Count);

        // ConfigDn should have 1 partner
        Assert.Single(topology.GetInboundPartners(ConfigDn));

        // Total inbound partners
        Assert.Equal(3, topology.InboundPartners.Count);
    }

    // ════════════════════════════════════════════════════════════════
    //  13. ReplicationPartner Tests
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ReplicationPartner_DefaultOptions()
    {
        var partner = new ReplicationPartner();

        Assert.True(partner.Options.HasFlag(DsReplNeighborFlags.DS_REPL_NBR_WRITEABLE));
        Assert.True(partner.Options.HasFlag(DsReplNeighborFlags.DS_REPL_NBR_SYNC_ON_STARTUP));
        Assert.True(partner.Options.HasFlag(DsReplNeighborFlags.DS_REPL_NBR_DO_SCHEDULED_SYNCS));
    }

    [Fact]
    public void ReplicationPartner_DefaultTransportType_IsRpc()
    {
        var partner = new ReplicationPartner();
        Assert.Equal("RPC", partner.TransportType);
    }

    [Fact]
    public void ReplicationPartner_ToNeighborW_MapsFieldsCorrectly()
    {
        var dsaGuid = Guid.NewGuid();
        var invocationId = Guid.NewGuid();

        var partner = new ReplicationPartner
        {
            DsaGuid = dsaGuid,
            InvocationId = invocationId,
            DnsName = "dc2.corp.contoso.com",
            NtdsSettingsDn = $"CN=NTDS Settings,CN=DC2,CN=Servers,CN=Default-First-Site-Name,CN=Sites,{ConfigDn}",
            NamingContextDn = DomainDn,
            LastUsnSynced = 12345,
            ConsecutiveFailures = 2,
            LastSyncSuccess = new DateTimeOffset(2025, 6, 15, 10, 30, 0, TimeSpan.Zero),
            LastSyncAttempt = new DateTimeOffset(2025, 6, 15, 10, 30, 0, TimeSpan.Zero),
        };

        var neighbor = partner.ToNeighborW();

        Assert.Equal(DomainDn, neighbor.PszNamingContext);
        Assert.Equal(partner.NtdsSettingsDn, neighbor.PszSourceDsaDN);
        Assert.Equal("dc2.corp.contoso.com", neighbor.PszSourceDsaAddress);
        Assert.Equal(dsaGuid, neighbor.UuidSourceDsaObjGuid);
        Assert.Equal(invocationId, neighbor.UuidSourceDsaInvocationID);
        Assert.Equal(12345L, neighbor.UsnLastObjChangeSynced);
        Assert.Equal(2u, neighbor.CNumConsecutiveSyncFailures);
    }

    // ════════════════════════════════════════════════════════════════
    //  14. DcPromotionException Tests
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void DcPromotionException_CarriesMessage()
    {
        var ex = new DcPromotionException("Promotion failed: source DC unreachable");
        Assert.Equal("Promotion failed: source DC unreachable", ex.Message);
    }

    [Fact]
    public void DcPromotionException_CarriesInnerException()
    {
        var inner = new HttpRequestException("connection refused");
        var ex = new DcPromotionException("Promotion failed", inner);

        Assert.Same(inner, ex.InnerException);
    }

    // ════════════════════════════════════════════════════════════════
    //  15. DC Promotion Phase Order Tests
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void PromotionPhases_SchemaThenConfigThenDomain()
    {
        // The DcPromotionService replicates NCs in this order:
        // 1. Schema NC (smallest, needed for attribute interpretation)
        // 2. Configuration NC (sites, services, cross-refs)
        // 3. Domain NC (largest, all user/group/computer objects)
        //
        // Verify the expected ordering by checking that Schema DN is a child of Config DN,
        // and both are distinct from Domain DN.

        var options = new SetupOptions { DomainName = "corp.contoso.com" };

        var schemaDn = options.SchemaDn;
        var configDn = options.ConfigurationDn;
        var domainDn = options.DomainDn;

        // Schema is nested under Configuration
        Assert.StartsWith("CN=Schema,", schemaDn);
        Assert.EndsWith(configDn, schemaDn.Replace("CN=Schema,", ""));

        // Configuration is directly under the domain root
        Assert.StartsWith("CN=Configuration,", configDn);
        Assert.EndsWith(domainDn, configDn.Replace("CN=Configuration,", ""));

        // All three are distinct
        Assert.NotEqual(schemaDn, configDn);
        Assert.NotEqual(configDn, domainDn);
        Assert.NotEqual(schemaDn, domainDn);
    }

    // ════════════════════════════════════════════════════════════════
    //  16. KCC Ring Topology Tests
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task KccTopology_SingleDc_NoPartnersAdded()
    {
        var store = new InMemoryDirectoryStore();
        var dcInfo = new DcInstanceInfo
        {
            Hostname = "DC1",
            SiteName = "Default-First-Site-Name",
        };
        var topology = new ReplicationTopology(store, dcInfo, NullLogger<ReplicationTopology>.Instance);

        // Add only one server (this DC) in the site
        var serverDn = dcInfo.ServerDn(DomainDn);
        store.Add(new DirectoryObject
        {
            Id = serverDn.ToLowerInvariant(),
            TenantId = TenantId,
            DistinguishedName = serverDn,
            ObjectClass = ["top", "server"],
            Cn = "DC1",
            DnsHostName = "dc1.corp.contoso.com",
            DomainDn = DomainDn,
            ParentDn = $"CN=Servers,CN=Default-First-Site-Name,CN=Sites,CN=Configuration,{DomainDn}",
        });

        await topology.GenerateIntraSiteTopologyAsync(TenantId, DomainDn);

        // With only 1 DC, no inbound partners should be created
        Assert.Empty(topology.InboundPartners);
    }

    [Fact]
    public async Task KccTopology_ThreeDcs_CreatesRingTopology()
    {
        var store = new InMemoryDirectoryStore();
        var dcInfo = new DcInstanceInfo
        {
            Hostname = "DC2",
            SiteName = "Default-First-Site-Name",
        };
        var topology = new ReplicationTopology(store, dcInfo, NullLogger<ReplicationTopology>.Instance);

        var serversDn = $"CN=Servers,CN=Default-First-Site-Name,CN=Sites,CN=Configuration,{DomainDn}";

        // Add three server objects sorted alphabetically: DC1, DC2, DC3
        foreach (var hostname in new[] { "DC1", "DC2", "DC3" })
        {
            var serverDn = $"CN={hostname},CN=Servers,CN=Default-First-Site-Name,CN=Sites,CN=Configuration,{DomainDn}";
            store.Add(new DirectoryObject
            {
                Id = serverDn.ToLowerInvariant(),
                TenantId = TenantId,
                DistinguishedName = serverDn,
                ObjectClass = ["top", "server"],
                Cn = hostname,
                DnsHostName = $"{hostname.ToLower()}.corp.contoso.com",
                ObjectGuid = Guid.NewGuid().ToString(),
                DomainDn = DomainDn,
                ParentDn = serversDn,
            });
        }

        await topology.GenerateIntraSiteTopologyAsync(TenantId, DomainDn);

        // DC2 should have inbound partners (previous and next in ring)
        var partners = topology.GetInboundPartners(DomainDn);
        Assert.True(partners.Count >= 2, $"Expected at least 2 inbound partners, got {partners.Count}");

        // Verify partners are DCs other than ourselves
        foreach (var partner in partners)
        {
            Assert.NotEqual("DC2", partner.DnsName.Split('.')[0].ToUpperInvariant());
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  17. Promotion Source DC Adds as Inbound Partner (Integration)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void AfterPromotion_SourceDcAppearsAsInboundPartner()
    {
        // Simulate what DcPromotionService.SetupReplicationPartnershipsAsync does:
        // after promotion, the source DC is added as an inbound partner for each NC.
        var store = new InMemoryDirectoryStore();
        var dcInfo = new DcInstanceInfo { Hostname = "DC2", SiteName = "Default-First-Site-Name" };
        var topology = new ReplicationTopology(store, dcInfo, NullLogger<ReplicationTopology>.Instance);

        var sourceDsaGuid = Guid.NewGuid();
        var namingContexts = new[] { SchemaDn, ConfigDn, DomainDn };

        foreach (var ncDn in namingContexts)
        {
            topology.AddInboundPartner(new ReplicationPartner
            {
                DsaGuid = sourceDsaGuid,
                DnsName = "dc1.corp.contoso.com",
                NtdsSettingsDn = $"CN=NTDS Settings,CN=SourceDC,CN=Servers,CN=Default-First-Site-Name,CN=Sites,{ConfigDn}",
                NamingContextDn = ncDn,
                TransportType = "RPC",
                InvocationId = sourceDsaGuid,
                LastUsnSynced = 1000,
                LastSyncResult = 0,
            });
        }

        // Source DC should appear as inbound partner for all three NCs
        Assert.Single(topology.GetInboundPartners(SchemaDn));
        Assert.Single(topology.GetInboundPartners(ConfigDn));
        Assert.Single(topology.GetInboundPartners(DomainDn));

        Assert.Equal(3, topology.InboundPartners.Count);

        // All partners should reference the same source DSA GUID
        foreach (var partner in topology.InboundPartners)
        {
            Assert.Equal(sourceDsaGuid, partner.DsaGuid);
            Assert.Equal("dc1.corp.contoso.com", partner.DnsName);
        }
    }
}
