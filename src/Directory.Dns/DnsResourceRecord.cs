using System.Buffers.Binary;
using System.Net;
using System.Text;

namespace Directory.Dns;

/// <summary>
/// DNS resource record with type-specific RDATA encoding.
/// </summary>
public abstract class DnsResourceRecord
{
    public string Name { get; set; } = string.Empty;
    public DnsRecordType Type { get; set; }
    public ushort Class { get; set; } = 1; // IN
    public uint Ttl { get; set; } = 600;

    public abstract void WriteRData(byte[] buffer, ref int offset);

    protected static void WriteDnsName(byte[] buffer, ref int offset, string name)
    {
        foreach (var label in name.Split('.'))
        {
            buffer[offset++] = (byte)label.Length;
            Encoding.ASCII.GetBytes(label, buffer.AsSpan(offset));
            offset += label.Length;
        }
        buffer[offset++] = 0;
    }
}

public class DnsSrvRecord : DnsResourceRecord
{
    public ushort Priority { get; set; }
    public ushort Weight { get; set; } = 100;
    public ushort Port { get; set; }
    public string Target { get; set; } = string.Empty;

    public DnsSrvRecord() => Type = DnsRecordType.SRV;

    public override void WriteRData(byte[] buffer, ref int offset)
    {
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(offset), Priority);
        offset += 2;
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(offset), Weight);
        offset += 2;
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(offset), Port);
        offset += 2;

        WriteDnsName(buffer, ref offset, Target);
    }
}

public class DnsARecord : DnsResourceRecord
{
    public IPAddress Address { get; set; } = IPAddress.Loopback;

    public DnsARecord() => Type = DnsRecordType.A;

    public override void WriteRData(byte[] buffer, ref int offset)
    {
        var bytes = Address.GetAddressBytes();
        bytes.CopyTo(buffer, offset);
        offset += bytes.Length;
    }
}

public class DnsAaaaRecord : DnsResourceRecord
{
    public IPAddress Address { get; set; } = IPAddress.IPv6Loopback;

    public DnsAaaaRecord() => Type = DnsRecordType.AAAA;

    public override void WriteRData(byte[] buffer, ref int offset)
    {
        var bytes = Address.GetAddressBytes();
        bytes.CopyTo(buffer, offset);
        offset += bytes.Length;
    }
}

public class DnsSoaRecord : DnsResourceRecord
{
    public string PrimaryNs { get; set; } = string.Empty;
    public string AdminMailbox { get; set; } = string.Empty;
    public uint Serial { get; set; } = 1;
    public uint Refresh { get; set; } = 3600;
    public uint Retry { get; set; } = 600;
    public uint Expire { get; set; } = 86400;
    public uint MinimumTtl { get; set; } = 600;

    public DnsSoaRecord() => Type = DnsRecordType.SOA;

    public override void WriteRData(byte[] buffer, ref int offset)
    {
        WriteDnsName(buffer, ref offset, PrimaryNs);
        WriteDnsName(buffer, ref offset, AdminMailbox);
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(offset), Serial);
        offset += 4;
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(offset), Refresh);
        offset += 4;
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(offset), Retry);
        offset += 4;
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(offset), Expire);
        offset += 4;
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(offset), MinimumTtl);
        offset += 4;
    }
}

public class DnsNsRecord : DnsResourceRecord
{
    public string NsName { get; set; } = string.Empty;

    public DnsNsRecord() => Type = DnsRecordType.NS;

    public override void WriteRData(byte[] buffer, ref int offset)
    {
        WriteDnsName(buffer, ref offset, NsName);
    }
}

/// <summary>
/// PTR record for reverse DNS lookups (in-addr.arpa / ip6.arpa).
/// </summary>
public class DnsPtrRecord : DnsResourceRecord
{
    public string DomainName { get; set; } = string.Empty;

    public DnsPtrRecord() => Type = DnsRecordType.PTR;

    public override void WriteRData(byte[] buffer, ref int offset)
    {
        WriteDnsName(buffer, ref offset, DomainName);
    }
}

/// <summary>
/// OPT pseudo-record for EDNS0 (RFC 6891).
/// The Class field carries the UDP payload size, and TTL carries extended RCODE/flags.
/// </summary>
public class DnsOptRecord : DnsResourceRecord
{
    public ushort UdpPayloadSize { get; set; } = 4096;

    public DnsOptRecord()
    {
        Type = DnsRecordType.OPT;
        Name = ""; // OPT records use root name (empty)
        Class = 4096; // Class field = UDP payload size
        Ttl = 0; // Extended RCODE + flags
    }

    public override void WriteRData(byte[] buffer, ref int offset)
    {
        // Empty RDATA for basic EDNS0 support
    }
}

/// <summary>
/// DNSKEY record (type 48) per RFC 4034 Section 2.
/// Contains a zone's public key used for DNSSEC validation.
/// </summary>
public class DnsDnskeyRecord : DnsResourceRecord
{
    public ushort Flags { get; set; } // 256=ZSK, 257=KSK
    public byte Protocol { get; set; } = 3;
    public byte Algorithm { get; set; } // 13=ECDSAP256SHA256, 8=RSASHA256
    public byte[] PublicKey { get; set; } = [];
    public int KeyTag { get; set; } // Informational, not part of wire format

    public DnsDnskeyRecord() => Type = DnsRecordType.DNSKEY;

    public override void WriteRData(byte[] buffer, ref int offset)
    {
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(offset), Flags);
        offset += 2;
        buffer[offset++] = Protocol;
        buffer[offset++] = Algorithm;
        PublicKey.CopyTo(buffer, offset);
        offset += PublicKey.Length;
    }
}

/// <summary>
/// RRSIG record (type 46) per RFC 4034 Section 3.
/// Contains a cryptographic signature covering an RRset.
/// </summary>
public class DnsRrsigRecord : DnsResourceRecord
{
    public DnsRecordType TypeCovered { get; set; }
    public byte Algorithm { get; set; }
    public byte Labels { get; set; }
    public uint OriginalTtl { get; set; }
    public uint SignatureExpiration { get; set; } // Unix timestamp
    public uint SignatureInception { get; set; }  // Unix timestamp
    public ushort KeyTag { get; set; }
    public string SignerName { get; set; } = "";
    public byte[] Signature { get; set; } = [];

    public DnsRrsigRecord() => Type = DnsRecordType.RRSIG;

    public override void WriteRData(byte[] buffer, ref int offset)
    {
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(offset), (ushort)TypeCovered);
        offset += 2;
        buffer[offset++] = Algorithm;
        buffer[offset++] = Labels;
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(offset), OriginalTtl);
        offset += 4;
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(offset), SignatureExpiration);
        offset += 4;
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(offset), SignatureInception);
        offset += 4;
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(offset), KeyTag);
        offset += 2;
        WriteDnsName(buffer, ref offset, SignerName);
        Signature.CopyTo(buffer, offset);
        offset += Signature.Length;
    }
}

/// <summary>
/// DS record (type 43) per RFC 4034 Section 5.
/// Delegation signer — links a child zone's KSK to the parent zone.
/// </summary>
public class DnsDsRecord : DnsResourceRecord
{
    public ushort KeyTag { get; set; }
    public byte Algorithm { get; set; }
    public byte DigestType { get; set; } // 2 = SHA-256
    public byte[] Digest { get; set; } = [];

    public DnsDsRecord() => Type = DnsRecordType.DS;

    public override void WriteRData(byte[] buffer, ref int offset)
    {
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(offset), KeyTag);
        offset += 2;
        buffer[offset++] = Algorithm;
        buffer[offset++] = DigestType;
        Digest.CopyTo(buffer, offset);
        offset += Digest.Length;
    }
}

/// <summary>
/// NSEC3 record (type 50) per RFC 5155.
/// Authenticated denial of existence using hashed owner names.
/// </summary>
public class DnsNsec3Record : DnsResourceRecord
{
    public byte HashAlgorithm { get; set; } = 1; // SHA-1
    public byte Flags { get; set; }
    public ushort Iterations { get; set; }
    public byte[] Salt { get; set; } = [];
    public string NextHashedOwnerName { get; set; } = "";
    public byte[] TypeBitmap { get; set; } = [];

    public DnsNsec3Record() => Type = DnsRecordType.NSEC3;

    public override void WriteRData(byte[] buffer, ref int offset)
    {
        buffer[offset++] = HashAlgorithm;
        buffer[offset++] = Flags;
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(offset), Iterations);
        offset += 2;
        buffer[offset++] = (byte)Salt.Length;
        Salt.CopyTo(buffer, offset);
        offset += Salt.Length;

        // Next hashed owner name as raw bytes (base32hex decoded)
        var nextHashBytes = DecodeBase32Hex(NextHashedOwnerName);
        buffer[offset++] = (byte)nextHashBytes.Length;
        nextHashBytes.CopyTo(buffer, offset);
        offset += nextHashBytes.Length;

        TypeBitmap.CopyTo(buffer, offset);
        offset += TypeBitmap.Length;
    }

    private static byte[] DecodeBase32Hex(string encoded)
    {
        const string alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUV";
        var bits = 0;
        var buffer = 0;
        var result = new List<byte>();

        foreach (var c in encoded.ToUpperInvariant())
        {
            var val = alphabet.IndexOf(c);
            if (val < 0) continue;
            buffer = (buffer << 5) | val;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                result.Add((byte)(buffer >> bits));
                buffer &= (1 << bits) - 1;
            }
        }

        return result.ToArray();
    }
}

/// <summary>
/// NSEC3PARAM record (type 51) per RFC 5155.
/// Specifies NSEC3 parameters for a zone.
/// </summary>
public class DnsNsec3ParamRecord : DnsResourceRecord
{
    public byte HashAlgorithm { get; set; } = 1; // SHA-1
    public byte Flags { get; set; }
    public ushort Iterations { get; set; } = 10;
    public byte[] Salt { get; set; } = [];

    public DnsNsec3ParamRecord() => Type = DnsRecordType.NSEC3PARAM;

    public override void WriteRData(byte[] buffer, ref int offset)
    {
        buffer[offset++] = HashAlgorithm;
        buffer[offset++] = Flags;
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(offset), Iterations);
        offset += 2;
        buffer[offset++] = (byte)Salt.Length;
        Salt.CopyTo(buffer, offset);
        offset += Salt.Length;
    }
}
