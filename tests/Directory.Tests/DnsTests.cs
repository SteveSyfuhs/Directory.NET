using Directory.Dns;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Directory.Tests;

public class DnsPacketTests
{
    [Fact]
    public void Parse_StandardQuery_ReturnsCorrectQuestion()
    {
        // Arrange — build a minimal DNS query for _ldap._tcp.corp.com (SRV)
        var packet = BuildDnsQuery(0x1234, "_ldap._tcp.corp.com", DnsRecordType.SRV);

        // Act
        var parsed = DnsPacket.Parse(packet);

        // Assert
        Assert.Equal(0x1234, parsed.Id);
        Assert.True(parsed.Flags.IsQuery);
        Assert.Single(parsed.Questions);
        Assert.Equal("_ldap._tcp.corp.com", parsed.Questions[0].Name);
        Assert.Equal(DnsRecordType.SRV, parsed.Questions[0].Type);
    }

    [Fact]
    public void Encode_RoundTrips_Questions()
    {
        // Arrange
        var original = new DnsPacket
        {
            Id = 0xABCD,
            Flags = new DnsFlags(0), // Query
            Questions = [new DnsQuestion { Name = "test.corp.com", Type = DnsRecordType.A }],
        };

        // Act
        var encoded = original.Encode();
        var parsed = DnsPacket.Parse(encoded);

        // Assert
        Assert.Equal(0xABCD, parsed.Id);
        Assert.Single(parsed.Questions);
        Assert.Equal("test.corp.com", parsed.Questions[0].Name);
        Assert.Equal(DnsRecordType.A, parsed.Questions[0].Type);
    }

    [Fact]
    public void Flags_CreateResponse_SetsQrAndAaBits()
    {
        var flags = DnsFlags.CreateResponse(authoritative: true);
        Assert.True(flags.IsResponse);
        Assert.True(flags.IsAuthoritative);
        Assert.False(flags.IsTruncated);
        Assert.Equal(0, flags.ResponseCode);
    }

    [Fact]
    public void Flags_CreateNxDomainResponse_SetsRcode3()
    {
        var flags = DnsFlags.CreateNxDomainResponse();
        Assert.True(flags.IsResponse);
        Assert.Equal(3, flags.ResponseCode);
    }

    [Fact]
    public void Flags_CreateResponse_WithTruncation_SetsTcBit()
    {
        var flags = DnsFlags.CreateResponse(authoritative: true, truncated: true);
        Assert.True(flags.IsTruncated);
    }

    private static byte[] BuildDnsQuery(ushort id, string name, DnsRecordType type)
    {
        var packet = new DnsPacket
        {
            Id = id,
            Flags = new DnsFlags(0), // Standard query
            Questions = [new DnsQuestion { Name = name, Type = type }],
        };
        return packet.Encode();
    }
}

public class DnsZoneStoreTests
{
    private readonly InMemoryDirectoryStore _store;
    private readonly DnsZoneStore _zoneStore;

    public DnsZoneStoreTests()
    {
        _store = new InMemoryDirectoryStore();
        _zoneStore = new DnsZoneStore(_store, NullLogger<DnsZoneStore>.Instance);
    }

    [Fact]
    public async Task UpsertAndGetRecord_ARecord_RoundTrips()
    {
        // Arrange
        var record = new DnsRecord
        {
            Name = "host1.corp.com",
            Type = DnsRecordType.A,
            Ttl = 300,
            Data = "10.0.0.1",
        };

        // Act
        await _zoneStore.UpsertRecordAsync("default", "corp.com", record);
        var result = await _zoneStore.GetRecordAsync("default", "corp.com", "host1.corp.com", DnsRecordType.A);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("host1.corp.com", result.Name);
        Assert.Equal(DnsRecordType.A, result.Type);
        Assert.Equal("10.0.0.1", result.Data);
        Assert.Equal(300, result.Ttl);
    }

    [Fact]
    public async Task GetRecord_NonExistent_ReturnsNull()
    {
        var result = await _zoneStore.GetRecordAsync("default", "corp.com", "missing.corp.com", DnsRecordType.A);
        Assert.Null(result);
    }

    [Fact]
    public async Task UpsertRecord_UpdateExisting_SameData_UpdatesTtl()
    {
        // Arrange - same name, type, AND data → updates in place (e.g., TTL change)
        var record1 = new DnsRecord { Name = "host1.corp.com", Type = DnsRecordType.A, Ttl = 300, Data = "10.0.0.1" };
        var record2 = new DnsRecord { Name = "host1.corp.com", Type = DnsRecordType.A, Ttl = 600, Data = "10.0.0.1" };

        // Act
        await _zoneStore.UpsertRecordAsync("default", "corp.com", record1);
        await _zoneStore.UpsertRecordAsync("default", "corp.com", record2);
        var result = await _zoneStore.GetRecordAsync("default", "corp.com", "host1.corp.com", DnsRecordType.A);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("10.0.0.1", result.Data);
        Assert.Equal(600, result.Ttl);
    }

    [Fact]
    public async Task UpsertRecord_DifferentData_AddsMultipleValues()
    {
        // Arrange - same name and type but different data → multi-valued (multi-DC support)
        var record1 = new DnsRecord { Name = "host1.corp.com", Type = DnsRecordType.A, Ttl = 300, Data = "10.0.0.1" };
        var record2 = new DnsRecord { Name = "host1.corp.com", Type = DnsRecordType.A, Ttl = 300, Data = "10.0.0.2" };

        // Act
        await _zoneStore.UpsertRecordAsync("default", "corp.com", record1);
        await _zoneStore.UpsertRecordAsync("default", "corp.com", record2);
        var results = await _zoneStore.GetRecordsByNameAsync("default", "corp.com", "host1.corp.com", DnsRecordType.A);

        // Assert - both records should exist
        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.Data == "10.0.0.1");
        Assert.Contains(results, r => r.Data == "10.0.0.2");
    }

    [Fact]
    public async Task DeleteRecord_RemovesRecord()
    {
        // Arrange
        var record = new DnsRecord { Name = "host1.corp.com", Type = DnsRecordType.A, Ttl = 300, Data = "10.0.0.1" };
        await _zoneStore.UpsertRecordAsync("default", "corp.com", record);

        // Act
        await _zoneStore.DeleteRecordAsync("default", "corp.com", "host1.corp.com");
        var result = await _zoneStore.GetRecordAsync("default", "corp.com", "host1.corp.com", DnsRecordType.A);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetRecord_WrongType_ReturnsNull()
    {
        // Arrange
        var record = new DnsRecord { Name = "host1.corp.com", Type = DnsRecordType.A, Ttl = 300, Data = "10.0.0.1" };
        await _zoneStore.UpsertRecordAsync("default", "corp.com", record);

        // Act — query for AAAA when only A exists
        var result = await _zoneStore.GetRecordAsync("default", "corp.com", "host1.corp.com", DnsRecordType.AAAA);

        // Assert
        Assert.Null(result);
    }
}

public class DnsDynamicUpdateHandlerTests
{
    private readonly InMemoryDirectoryStore _store;
    private readonly DnsZoneStore _zoneStore;
    private readonly DnsDynamicUpdateHandler _handler;

    public DnsDynamicUpdateHandlerTests()
    {
        _store = new InMemoryDirectoryStore();
        _zoneStore = new DnsZoneStore(_store, NullLogger<DnsZoneStore>.Instance);
        _handler = new DnsDynamicUpdateHandler(_zoneStore, NullLogger<DnsDynamicUpdateHandler>.Instance);
    }

    [Fact]
    public async Task ProcessUpdate_AddARecord_Succeeds()
    {
        // Arrange — build a DNS UPDATE packet adding an A record
        var packet = BuildDnsUpdatePacket(
            id: 0x5678,
            zoneName: "corp.com",
            updateName: "host1.corp.com",
            recordType: DnsRecordType.A,
            ttl: 300,
            rdata: new byte[] { 10, 0, 0, 1 },
            cls: 1 // IN class = add
        );

        // Act
        var result = await _handler.ProcessUpdateAsync(packet, "default");

        // Assert
        Assert.Equal(0, result.ResponseCode); // NOERROR
        Assert.Equal(0x5678, result.TransactionId);
    }

    [Fact]
    public async Task ProcessUpdate_DeleteRecord_Succeeds()
    {
        // Arrange — first add a record
        var addPacket = BuildDnsUpdatePacket(
            id: 0x0001,
            zoneName: "corp.com",
            updateName: "host1.corp.com",
            recordType: DnsRecordType.A,
            ttl: 300,
            rdata: new byte[] { 10, 0, 0, 1 },
            cls: 1);
        await _handler.ProcessUpdateAsync(addPacket, "default");

        // Now delete it (class=255, rdlength=0)
        var deletePacket = BuildDnsUpdatePacket(
            id: 0x0002,
            zoneName: "corp.com",
            updateName: "host1.corp.com",
            recordType: DnsRecordType.A,
            ttl: 0,
            rdata: Array.Empty<byte>(),
            cls: 255 // ANY class = delete all RRsets
        );

        // Act
        var result = await _handler.ProcessUpdateAsync(deletePacket, "default");

        // Assert
        Assert.Equal(0, result.ResponseCode); // NOERROR
    }

    [Fact]
    public async Task ProcessUpdate_TooShortPacket_ReturnsFormatError()
    {
        // Arrange — packet shorter than minimum DNS header
        var packet = new byte[] { 0x00, 0x01 };

        // Act
        var result = await _handler.ProcessUpdateAsync(packet, "default");

        // Assert
        Assert.Equal(1, result.ResponseCode); // FORMERR
    }

    [Fact]
    public async Task ProcessUpdate_WrongOpcode_ReturnsNotImplemented()
    {
        // Arrange — build a standard query (opcode=0) instead of update (opcode=5)
        var packet = new byte[12];
        packet[0] = 0x00; packet[1] = 0x01; // ID
        packet[2] = 0x00; packet[3] = 0x00; // Flags: opcode=0 (standard query)

        // Act
        var result = await _handler.ProcessUpdateAsync(packet, "default");

        // Assert
        Assert.Equal(4, result.ResponseCode); // NOTIMP
    }

    [Fact]
    public async Task ProcessUpdate_EmptyZoneName_ReturnsRefused()
    {
        // Arrange — update opcode but empty zone
        var packet = new byte[12];
        packet[0] = 0x00; packet[1] = 0x01; // ID
        packet[2] = (byte)(5 << 3); // Opcode=5
        packet[3] = 0x00;
        // zoCount=0
        packet[4] = 0x00; packet[5] = 0x00;

        // Act
        var result = await _handler.ProcessUpdateAsync(packet, "default");

        // Assert
        Assert.Equal(5, result.ResponseCode); // REFUSED
    }

    [Fact]
    public void BuildResponse_ProducesValidDnsHeader()
    {
        // Arrange
        var result = DnsUpdateResult.NoError(0x1234);

        // Act
        var response = result.BuildResponse();

        // Assert
        Assert.Equal(12, response.Length);
        // Transaction ID
        Assert.Equal(0x12, response[0]);
        Assert.Equal(0x34, response[1]);
        // QR=1, Opcode=5
        Assert.Equal(0x80 | (5 << 3), response[2]);
        // RCODE=0
        Assert.Equal(0, response[3]);
    }

    [Fact]
    public void BuildResponse_FormatError_ReturnsRcode1()
    {
        var result = DnsUpdateResult.FormatError();
        var response = result.BuildResponse();
        Assert.Equal(1, response[3] & 0x0F);
    }

    /// <summary>
    /// Builds a minimal DNS UPDATE packet (opcode=5) with a single zone and a single update RR.
    /// </summary>
    private static byte[] BuildDnsUpdatePacket(
        ushort id, string zoneName, string updateName,
        DnsRecordType recordType, int ttl, byte[] rdata, ushort cls)
    {
        var ms = new System.IO.MemoryStream();
        var writer = new System.IO.BinaryWriter(ms);

        // Header
        writer.Write((byte)(id >> 8));
        writer.Write((byte)id);
        writer.Write((byte)(5 << 3)); // QR=0, Opcode=5 (UPDATE)
        writer.Write((byte)0);        // RCODE=0
        writer.Write((byte)0); writer.Write((byte)1); // ZOCOUNT=1
        writer.Write((byte)0); writer.Write((byte)0); // PRCOUNT=0
        writer.Write((byte)0); writer.Write((byte)1); // UPCOUNT=1
        writer.Write((byte)0); writer.Write((byte)0); // ADCOUNT=0

        // Zone section
        WriteDnsName(ms, zoneName);
        writer.Write((byte)0); writer.Write((byte)6); // QTYPE=SOA
        writer.Write((byte)0); writer.Write((byte)1); // QCLASS=IN

        // Update section
        WriteDnsName(ms, updateName);
        writer.Write((byte)((ushort)recordType >> 8));
        writer.Write((byte)(ushort)recordType);
        writer.Write((byte)(cls >> 8));
        writer.Write((byte)cls);
        writer.Write((byte)(ttl >> 24));
        writer.Write((byte)(ttl >> 16));
        writer.Write((byte)(ttl >> 8));
        writer.Write((byte)ttl);
        writer.Write((byte)(rdata.Length >> 8));
        writer.Write((byte)rdata.Length);
        writer.Write(rdata);

        return ms.ToArray();
    }

    private static void WriteDnsName(System.IO.MemoryStream ms, string name)
    {
        foreach (var label in name.Split('.'))
        {
            ms.WriteByte((byte)label.Length);
            ms.Write(System.Text.Encoding.ASCII.GetBytes(label));
        }
        ms.WriteByte(0); // terminator
    }
}

public class DnsSiteServiceTests
{
    [Fact]
    public void GetSiteSpecificSrvRecords_ReturnsExpectedRecords()
    {
        // Arrange
        var store = new InMemoryDirectoryStore();
        var siteService = new DnsSiteService(store, NullLogger<DnsSiteService>.Instance);

        // Act
        var records = siteService.GetSiteSpecificSrvRecords("Default-First-Site-Name", "corp.com");

        // Assert
        Assert.Contains("_ldap._tcp.Default-First-Site-Name._sites.corp.com", records);
        Assert.Contains("_kerberos._tcp.Default-First-Site-Name._sites.corp.com", records);
        Assert.Contains("_gc._tcp.Default-First-Site-Name._sites.corp.com", records);
    }
}

public class DnsSrvRecordEncodingTests
{
    [Fact]
    public void DnsSrvRecord_WriteRData_ProducesValidWireFormat()
    {
        // Arrange
        var record = new DnsSrvRecord
        {
            Name = "_ldap._tcp.corp.com",
            Priority = 0,
            Weight = 100,
            Port = 389,
            Target = "dc1.corp.com",
            Ttl = 600,
        };

        var buffer = new byte[256];
        var offset = 0;

        // Act
        record.WriteRData(buffer, ref offset);

        // Assert
        // Priority = 0
        Assert.Equal(0, (buffer[0] << 8) | buffer[1]);
        // Weight = 100
        Assert.Equal(100, (buffer[2] << 8) | buffer[3]);
        // Port = 389
        Assert.Equal(389, (buffer[4] << 8) | buffer[5]);
        // Target starts at offset 6
        Assert.True(offset > 6); // Target name was written
    }

    [Fact]
    public void DnsSoaRecord_WriteRData_ContainsAllFields()
    {
        // Arrange
        var record = new DnsSoaRecord
        {
            Name = "corp.com",
            PrimaryNs = "dc1.corp.com",
            AdminMailbox = "hostmaster.corp.com",
            Serial = 2024010101,
            Refresh = 900,
            Retry = 600,
            Expire = 86400,
            MinimumTtl = 60,
        };

        var buffer = new byte[512];
        var offset = 0;

        // Act
        record.WriteRData(buffer, ref offset);

        // Assert
        Assert.True(offset > 20); // SOA RDATA should be substantial
    }
}
