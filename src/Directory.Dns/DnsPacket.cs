using System.Buffers.Binary;
using System.Text;

namespace Directory.Dns;

/// <summary>
/// Minimal DNS packet parser/builder per RFC 1035.
/// Handles the wire format for DNS queries and responses.
/// </summary>
public class DnsPacket
{
    public ushort Id { get; set; }
    public DnsFlags Flags { get; set; }
    public List<DnsQuestion> Questions { get; set; } = [];
    public List<DnsResourceRecord> Answers { get; set; } = [];
    public List<DnsResourceRecord> Authority { get; set; } = [];
    public List<DnsResourceRecord> Additional { get; set; } = [];

    /// <summary>
    /// EDNS0: Client-advertised UDP payload size from OPT record. 0 means no EDNS0.
    /// </summary>
    public ushort ClientUdpPayloadSize { get; set; }

    /// <summary>
    /// Whether the query included an EDNS0 OPT record.
    /// </summary>
    public bool HasEdns { get; set; }

    /// <summary>
    /// EDNS0: Whether the DO (DNSSEC OK) bit is set in the OPT record's extended flags.
    /// When set, the client is requesting DNSSEC records (RRSIG, DNSKEY, DS, NSEC3).
    /// </summary>
    public bool DnssecOk { get; set; }

    public static DnsPacket Parse(ReadOnlySpan<byte> data)
    {
        var offset = 0;
        var packet = new DnsPacket
        {
            Id = BinaryPrimitives.ReadUInt16BigEndian(data[offset..]),
        };
        offset += 2;

        var flags = BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);
        offset += 2;
        packet.Flags = new DnsFlags(flags);

        var qdCount = BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);
        offset += 2;
        var anCount = BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);
        offset += 2;
        var nsCount = BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);
        offset += 2;
        var arCount = BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);
        offset += 2;

        for (var i = 0; i < qdCount; i++)
        {
            var name = ReadName(data, ref offset);
            var qtype = (DnsRecordType)BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);
            offset += 2;
            var qclass = BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);
            offset += 2;

            packet.Questions.Add(new DnsQuestion { Name = name, Type = qtype, Class = qclass });
        }

        // Skip answer and authority sections for query parsing
        for (var i = 0; i < anCount + nsCount; i++)
        {
            if (offset >= data.Length) break;
            SkipResourceRecord(data, ref offset);
        }

        // Parse additional section (for OPT/EDNS0 records)
        for (var i = 0; i < arCount; i++)
        {
            if (offset >= data.Length) break;
            var rrStart = offset;
            var name = ReadName(data, ref offset);
            if (offset + 10 > data.Length) break;

            var rrType = (DnsRecordType)BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);
            offset += 2;
            var rrClass = BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);
            offset += 2;
            var rrTtl = BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
            offset += 4;
            var rdLength = BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);
            offset += 2;

            if (rrType == DnsRecordType.OPT)
            {
                // OPT record: Class field = UDP payload size, TTL = extended RCODE + flags
                packet.ClientUdpPayloadSize = rrClass;
                packet.HasEdns = true;
                // DO bit is bit 15 of the extended flags (lower 16 bits of TTL field)
                packet.DnssecOk = (rrTtl & 0x8000) != 0;
            }

            offset += rdLength;
        }

        return packet;
    }

    public byte[] Encode()
    {
        var buffer = new byte[4096];
        var offset = 0;

        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(offset), Id);
        offset += 2;
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(offset), Flags.Value);
        offset += 2;
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(offset), (ushort)Questions.Count);
        offset += 2;
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(offset), (ushort)Answers.Count);
        offset += 2;
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(offset), (ushort)Authority.Count);
        offset += 2;
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(offset), (ushort)Additional.Count);
        offset += 2;

        foreach (var q in Questions)
        {
            WriteName(buffer, ref offset, q.Name);
            BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(offset), (ushort)q.Type);
            offset += 2;
            BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(offset), q.Class);
            offset += 2;
        }

        foreach (var rr in Answers.Concat(Authority).Concat(Additional))
        {
            WriteResourceRecord(buffer, ref offset, rr);
        }

        return buffer[..offset];
    }

    private static void SkipResourceRecord(ReadOnlySpan<byte> data, ref int offset)
    {
        ReadName(data, ref offset); // skip name
        if (offset + 10 > data.Length) return;
        offset += 8; // TYPE(2) + CLASS(2) + TTL(4)
        var rdLength = BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);
        offset += 2;
        offset += rdLength;
    }

    private static string ReadName(ReadOnlySpan<byte> data, ref int offset)
    {
        var labels = new List<string>();
        var jumped = false;
        var savedOffset = 0;

        while (offset < data.Length)
        {
            var len = data[offset];

            if ((len & 0xC0) == 0xC0)
            {
                // Pointer
                if (!jumped)
                    savedOffset = offset + 2;
                offset = ((len & 0x3F) << 8) | data[offset + 1];
                jumped = true;
                continue;
            }

            if (len == 0)
            {
                offset++;
                break;
            }

            offset++;
            labels.Add(Encoding.ASCII.GetString(data.Slice(offset, len)));
            offset += len;
        }

        if (jumped)
            offset = savedOffset;

        return string.Join(".", labels);
    }

    private static void WriteName(byte[] buffer, ref int offset, string name)
    {
        foreach (var label in name.Split('.'))
        {
            buffer[offset++] = (byte)label.Length;
            Encoding.ASCII.GetBytes(label, buffer.AsSpan(offset));
            offset += label.Length;
        }
        buffer[offset++] = 0; // Terminator
    }

    private static void WriteResourceRecord(byte[] buffer, ref int offset, DnsResourceRecord rr)
    {
        WriteName(buffer, ref offset, rr.Name);
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(offset), (ushort)rr.Type);
        offset += 2;
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(offset), rr.Class);
        offset += 2;
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(offset), rr.Ttl);
        offset += 4;

        var rdataOffset = offset;
        offset += 2; // Skip RDLENGTH for now

        rr.WriteRData(buffer, ref offset);

        // Write RDLENGTH
        var rdataLength = offset - rdataOffset - 2;
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(rdataOffset), (ushort)rdataLength);
    }
}

public class DnsFlags
{
    public ushort Value { get; }

    public DnsFlags(ushort value) => Value = value;

    public bool IsQuery => (Value & 0x8000) == 0;
    public bool IsResponse => !IsQuery;
    public int Opcode => (Value >> 11) & 0xF;
    public bool IsAuthoritative => (Value & 0x0400) != 0;
    public bool IsTruncated => (Value & 0x0200) != 0;
    public bool RecursionDesired => (Value & 0x0100) != 0;
    public bool RecursionAvailable => (Value & 0x0080) != 0;
    public int ResponseCode => Value & 0xF;

    public static DnsFlags CreateResponse(bool authoritative = true, bool truncated = false)
    {
        ushort value = 0x8000; // QR = 1 (response)
        if (authoritative) value |= 0x0400; // AA = 1
        if (truncated) value |= 0x0200; // TC = 1
        return new DnsFlags(value);
    }

    public static DnsFlags CreateNxDomainResponse()
    {
        return new DnsFlags((ushort)(0x8000 | 0x0400 | 3)); // QR=1, AA=1, RCODE=3 (NXDOMAIN)
    }
}

public class DnsQuestion
{
    public string Name { get; set; } = string.Empty;
    public DnsRecordType Type { get; set; }
    public ushort Class { get; set; } = 1; // IN
}

public enum DnsRecordType : ushort
{
    A = 1,
    NS = 2,
    CNAME = 5,
    SOA = 6,
    PTR = 12,
    MX = 15,
    TXT = 16,
    AAAA = 28,
    SRV = 33,
    OPT = 41,
    DS = 43,
    RRSIG = 46,
    DNSKEY = 48,
    NSEC3 = 50,
    NSEC3PARAM = 51,
    ANY = 255,
}
