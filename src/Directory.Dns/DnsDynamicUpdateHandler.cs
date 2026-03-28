using Microsoft.Extensions.Logging;

namespace Directory.Dns;

/// <summary>
/// Processes DNS dynamic update requests per RFC 2136.
/// Used by AD clients to register their A/AAAA records.
/// </summary>
public class DnsDynamicUpdateHandler
{
    private readonly DnsZoneStore _zoneStore;
    private readonly ILogger<DnsDynamicUpdateHandler> _logger;

    public DnsDynamicUpdateHandler(DnsZoneStore zoneStore, ILogger<DnsDynamicUpdateHandler> logger)
    {
        _zoneStore = zoneStore;
        _logger = logger;
    }

    public async Task<DnsUpdateResult> ProcessUpdateAsync(
        byte[] packet, string tenantId, CancellationToken ct = default)
    {
        if (packet.Length < 12)
            return DnsUpdateResult.FormatError();

        // Parse DNS header
        var id = (ushort)((packet[0] << 8) | packet[1]);
        var flags = (ushort)((packet[2] << 8) | packet[3]);
        var opcode = (flags >> 11) & 0xF;

        if (opcode != 5) // UPDATE opcode
            return DnsUpdateResult.NotImplemented(id);

        var zoCount = (packet[4] << 8) | packet[5];   // Zone section
        var prCount = (packet[6] << 8) | packet[7];   // Prerequisite section
        var upCount = (packet[8] << 8) | packet[9];   // Update section
        var adCount = (packet[10] << 8) | packet[11];  // Additional section

        int offset = 12;

        // Parse zone section
        string zoneName = "";
        if (zoCount > 0)
        {
            zoneName = ReadDnsName(packet, ref offset);
            offset += 4; // QTYPE (2) + QCLASS (2)
        }

        if (string.IsNullOrEmpty(zoneName))
            return DnsUpdateResult.Refused(id);

        // Parse and enforce prerequisite section per RFC 2136 section 2.4
        for (int i = 0; i < prCount; i++)
        {
            if (offset >= packet.Length) break;

            var prName = ReadDnsName(packet, ref offset);
            if (offset + 10 > packet.Length) break;

            var prType = (DnsRecordType)((packet[offset] << 8) | packet[offset + 1]); offset += 2;
            var prClass = (ushort)((packet[offset] << 8) | packet[offset + 1]); offset += 2;
            offset += 4; // TTL (must be zero, skip)
            var prRdLength = (packet[offset] << 8) | packet[offset + 1]; offset += 2;

            byte[] prRdata = Array.Empty<byte>();
            if (prRdLength > 0 && offset + prRdLength <= packet.Length)
            {
                prRdata = packet[offset..(offset + prRdLength)];
                offset += prRdLength;
            }

            var prereqResult = await CheckPrerequisiteAsync(
                tenantId, zoneName, id, prName, prType, prClass, prRdLength, prRdata, ct);

            if (prereqResult != null)
            {
                _logger.LogInformation(
                    "DNS update prerequisite failed: {Name} type={Type} class={Class} rcode={RCode}",
                    prName, prType, prClass, prereqResult.ResponseCode);
                return prereqResult;
            }
        }

        // Parse update section
        for (int i = 0; i < upCount; i++)
        {
            if (offset >= packet.Length) break;

            var name = ReadDnsName(packet, ref offset);
            if (offset + 10 > packet.Length) break;

            var type = (DnsRecordType)((packet[offset] << 8) | packet[offset + 1]); offset += 2;
            var cls = (ushort)((packet[offset] << 8) | packet[offset + 1]); offset += 2;
            var ttl = (packet[offset] << 24) | (packet[offset + 1] << 16) | (packet[offset + 2] << 8) | packet[offset + 3]; offset += 4;
            var rdLength = (packet[offset] << 8) | packet[offset + 1]; offset += 2;

            if (offset + rdLength > packet.Length) break;
            var rdata = packet[offset..(offset + rdLength)];
            offset += rdLength;

            if (cls == 255 && rdLength == 0)
            {
                // Delete all RRsets for this name
                await _zoneStore.DeleteRecordAsync(tenantId, zoneName, name, ct);
                _logger.LogInformation("DNS dynamic delete: {Name} from zone {Zone}", name, zoneName);
            }
            else if (cls == 0 && rdLength == 0)
            {
                // Delete specific RRset
                await _zoneStore.DeleteRecordAsync(tenantId, zoneName, name, ct);
            }
            else
            {
                // Add/update record
                var record = new DnsRecord
                {
                    Name = name,
                    Type = type,
                    Ttl = ttl,
                    Data = FormatRdata(type, rdata),
                };
                await _zoneStore.UpsertRecordAsync(tenantId, zoneName, record, ct);
                _logger.LogInformation("DNS dynamic update: {Name} {Type} = {Data} in zone {Zone}", name, type, record.Data, zoneName);
            }
        }

        return DnsUpdateResult.NoError(id);
    }

    /// <summary>
    /// Checks a single DNS UPDATE prerequisite per RFC 2136 section 2.4.
    /// Returns null if the prerequisite is satisfied, or a DnsUpdateResult with the
    /// appropriate RCODE if it fails.
    /// </summary>
    private async Task<DnsUpdateResult> CheckPrerequisiteAsync(
        string tenantId, string zoneName, ushort id,
        string name, DnsRecordType type, ushort cls, int rdLength, byte[] rdata,
        CancellationToken ct)
    {
        if (cls == 255) // CLASS ANY
        {
            if (type == DnsRecordType.ANY && rdLength == 0)
            {
                // "Name is in use" — at least one RR of any type must exist
                var anyRecords = await _zoneStore.GetRecordsByNameAsync(tenantId, zoneName, name, 0, ct);
                if (anyRecords.Count == 0)
                    return DnsUpdateResult.NxDomain(id); // RCODE 3
            }
            else if (rdLength == 0)
            {
                // "RRset exists (value independent)" — at least one RR of given type must exist
                var records = await _zoneStore.GetRecordsByNameAsync(tenantId, zoneName, name, type, ct);
                if (records.Count == 0)
                    return DnsUpdateResult.NxRRSet(id); // RCODE 8
            }
        }
        else if (cls == 0) // CLASS NONE
        {
            if (type == DnsRecordType.ANY)
            {
                // "Name is not in use" — no RRs of any type may exist
                var anyRecords = await _zoneStore.GetRecordsByNameAsync(tenantId, zoneName, name, 0, ct);
                if (anyRecords.Count > 0)
                    return DnsUpdateResult.YxDomain(id); // RCODE 7
            }
            else
            {
                // "RRset does not exist" — no RRs of given type may exist
                var records = await _zoneStore.GetRecordsByNameAsync(tenantId, zoneName, name, type, ct);
                if (records.Count > 0)
                    return DnsUpdateResult.YxRRSet(id); // RCODE 6
            }
        }
        else
        {
            // Specific class with RDATA — "RRset exists (value dependent)"
            // Check that an RR with this exact type and data exists
            var formattedData = FormatRdata(type, rdata);
            var records = await _zoneStore.GetRecordsByNameAsync(tenantId, zoneName, name, type, ct);
            var found = false;

            foreach (var record in records)
            {
                if (string.Equals(record.Data, formattedData, StringComparison.OrdinalIgnoreCase))
                {
                    found = true;
                    break;
                }
            }

            if (!found)
                return DnsUpdateResult.NxRRSet(id); // RCODE 8
        }

        return null; // Prerequisite satisfied
    }

    private static string ReadDnsName(byte[] packet, ref int offset)
    {
        var parts = new List<string>();
        while (offset < packet.Length)
        {
            var len = packet[offset];
            if (len == 0) { offset++; break; }
            if ((len & 0xC0) == 0xC0)
            {
                // Compression pointer
                var ptr = ((len & 0x3F) << 8) | packet[offset + 1];
                offset += 2;
                var ptrOffset = ptr;
                parts.Add(ReadDnsName(packet, ref ptrOffset));
                break;
            }
            offset++;
            if (offset + len > packet.Length) break;
            parts.Add(System.Text.Encoding.ASCII.GetString(packet, offset, len));
            offset += len;
        }
        return string.Join(".", parts);
    }

    private static string FormatRdata(DnsRecordType type, byte[] rdata)
    {
        return type switch
        {
            DnsRecordType.A when rdata.Length == 4 => $"{rdata[0]}.{rdata[1]}.{rdata[2]}.{rdata[3]}",
            DnsRecordType.AAAA when rdata.Length == 16 => new System.Net.IPAddress(rdata).ToString(),
            _ => Convert.ToBase64String(rdata),
        };
    }
}

public class DnsUpdateResult
{
    public ushort TransactionId { get; init; }
    public int ResponseCode { get; init; }

    public static DnsUpdateResult NoError(ushort id) => new() { TransactionId = id, ResponseCode = 0 };
    public static DnsUpdateResult FormatError() => new() { TransactionId = 0, ResponseCode = 1 };
    public static DnsUpdateResult NxDomain(ushort id) => new() { TransactionId = id, ResponseCode = 3 };
    public static DnsUpdateResult NotImplemented(ushort id) => new() { TransactionId = id, ResponseCode = 4 };
    public static DnsUpdateResult Refused(ushort id) => new() { TransactionId = id, ResponseCode = 5 };
    public static DnsUpdateResult YxRRSet(ushort id) => new() { TransactionId = id, ResponseCode = 6 };
    public static DnsUpdateResult YxDomain(ushort id) => new() { TransactionId = id, ResponseCode = 7 };
    public static DnsUpdateResult NxRRSet(ushort id) => new() { TransactionId = id, ResponseCode = 8 };

    public byte[] BuildResponse()
    {
        var response = new byte[12];
        response[0] = (byte)(TransactionId >> 8);
        response[1] = (byte)TransactionId;
        // Flags: QR=1, Opcode=5 (UPDATE), RCODE
        response[2] = (byte)(0x80 | (5 << 3)); // QR=1, Opcode=5
        response[3] = (byte)ResponseCode;
        return response;
    }
}
