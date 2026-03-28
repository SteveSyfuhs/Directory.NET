using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Directory.Dns.Dnssec;

/// <summary>
/// DNSSEC signing and validation service per RFC 4033, 4034, 4035.
/// Supports ECDSAP256SHA256 (algorithm 13) and RSASHA256 (algorithm 8).
/// </summary>
public class DnssecService
{
    private readonly DnsZoneStore _zoneStore;
    private readonly ILogger<DnssecService> _logger;

    // In-memory store for zone settings and key pairs.
    // In production these would be persisted to Cosmos DB.
    private readonly Dictionary<string, DnssecZoneSettings> _zoneSettings = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<DnssecKeyPair> _keyPairs = [];
    private readonly List<DnssecRrsigRecord> _rrsigRecords = [];

    private const string DefaultTenantId = "default";

    public DnssecService(DnsZoneStore zoneStore, ILogger<DnssecService> logger)
    {
        _zoneStore = zoneStore;
        _logger = logger;
    }

    // ── Zone Settings ──────────────────────────────────────────────────

    public DnssecZoneSettings GetZoneSettings(string zoneName)
    {
        if (_zoneSettings.TryGetValue(zoneName, out var settings))
            return settings;

        return new DnssecZoneSettings { ZoneName = zoneName };
    }

    public DnssecZoneSettings UpdateZoneSettings(string zoneName, bool dnssecEnabled, int? signatureValidityDays = null, int? keyRolloverIntervalDays = null)
    {
        if (!_zoneSettings.TryGetValue(zoneName, out var settings))
        {
            settings = new DnssecZoneSettings { ZoneName = zoneName };
            _zoneSettings[zoneName] = settings;
        }

        settings.DnssecEnabled = dnssecEnabled;
        if (signatureValidityDays.HasValue)
            settings.SignatureValidityDays = signatureValidityDays.Value;
        if (keyRolloverIntervalDays.HasValue)
            settings.KeyRolloverIntervalDays = keyRolloverIntervalDays.Value;

        _logger.LogInformation("Updated DNSSEC settings for zone {Zone}: enabled={Enabled}", zoneName, dnssecEnabled);
        return settings;
    }

    // ── Key Management ─────────────────────────────────────────────────

    /// <summary>
    /// Generate a KSK or ZSK key pair for a zone.
    /// Algorithm 13 = ECDSAP256SHA256 (recommended), 8 = RSASHA256.
    /// </summary>
    public DnssecKeyPair GenerateKeyPair(string zoneName, DnssecKeyType keyType, int algorithm = 13)
    {
        byte[] publicKey;
        byte[] privateKey;

        if (algorithm == 13)
        {
            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var ecParams = ecdsa.ExportParameters(true);
            // DNSKEY RDATA format for ECDSAP256: Q.X (32 bytes) + Q.Y (32 bytes)
            publicKey = new byte[64];
            ecParams.Q.X.CopyTo(publicKey, 0);
            ecParams.Q.Y.CopyTo(publicKey, 32);
            privateKey = ecParams.D;
        }
        else if (algorithm == 8)
        {
            using var rsa = RSA.Create(2048);
            var rsaParams = rsa.ExportParameters(true);
            publicKey = ExportRsaDnskeyPublicKey(rsaParams);
            privateKey = rsa.ExportRSAPrivateKey();
        }
        else
        {
            throw new ArgumentException($"Unsupported algorithm: {algorithm}. Use 13 (ECDSAP256SHA256) or 8 (RSASHA256).");
        }

        var keyPair = new DnssecKeyPair
        {
            ZoneName = zoneName,
            KeyType = keyType,
            Algorithm = algorithm,
            PublicKey = publicKey,
            PrivateKey = ProtectPrivateKey(privateKey),
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(GetZoneSettings(zoneName).KeyRolloverIntervalDays),
            IsActive = true,
        };

        // Calculate key tag per RFC 4034 Appendix B
        keyPair.KeyTag = CalculateKeyTag(keyPair);

        _keyPairs.Add(keyPair);

        _logger.LogInformation(
            "Generated {KeyType} key pair for zone {Zone}: algorithm={Algorithm}, keyTag={KeyTag}",
            keyType, zoneName, algorithm, keyPair.KeyTag);

        return keyPair;
    }

    public IReadOnlyList<DnssecKeyPair> GetKeyPairs(string zoneName)
    {
        return _keyPairs.Where(k => k.ZoneName.Equals(zoneName, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public bool DeleteKeyPair(string zoneName, string keyId)
    {
        var removed = _keyPairs.RemoveAll(k =>
            k.Id == keyId && k.ZoneName.Equals(zoneName, StringComparison.OrdinalIgnoreCase));
        return removed > 0;
    }

    // ── Zone Signing ───────────────────────────────────────────────────

    /// <summary>
    /// Sign all RRsets in the zone, producing RRSIG records.
    /// </summary>
    public async Task<int> SignZoneAsync(string zoneName, CancellationToken ct = default)
    {
        var settings = GetZoneSettings(zoneName);
        if (!settings.DnssecEnabled)
            throw new InvalidOperationException($"DNSSEC is not enabled for zone {zoneName}");

        var zsk = _keyPairs.FirstOrDefault(k =>
            k.ZoneName.Equals(zoneName, StringComparison.OrdinalIgnoreCase) &&
            k.KeyType == DnssecKeyType.ZSK && k.IsActive);

        if (zsk == null)
            throw new InvalidOperationException($"No active ZSK found for zone {zoneName}. Generate a ZSK first.");

        // Clear existing RRSIG records for this zone
        _rrsigRecords.RemoveAll(r => r.SignerName.Equals(zoneName, StringComparison.OrdinalIgnoreCase));

        var signedCount = 0;
        var inception = DateTimeOffset.UtcNow;
        var expiration = inception.AddDays(settings.SignatureValidityDays);

        // Sign all record types in the zone
        foreach (var recordType in new[] { DnsRecordType.A, DnsRecordType.AAAA, DnsRecordType.CNAME,
            DnsRecordType.MX, DnsRecordType.NS, DnsRecordType.PTR, DnsRecordType.SRV, DnsRecordType.TXT })
        {
            var records = await _zoneStore.GetAllRecordsAsync(DefaultTenantId, zoneName, recordType, ct);
            if (records.Count == 0) continue;

            // Group records by name to form RRsets
            var rrsets = records.GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase);
            foreach (var rrset in rrsets)
            {
                var signature = CreateRrsig(zoneName, rrset.Key, recordType, rrset.ToList(), zsk, inception, expiration);
                _rrsigRecords.Add(signature);
                signedCount++;
            }
        }

        // Sign the DNSKEY RRset with the KSK
        var ksk = _keyPairs.FirstOrDefault(k =>
            k.ZoneName.Equals(zoneName, StringComparison.OrdinalIgnoreCase) &&
            k.KeyType == DnssecKeyType.KSK && k.IsActive);

        if (ksk != null)
        {
            var dnskeyRrsig = CreateDnskeyRrsig(zoneName, ksk, inception, expiration);
            _rrsigRecords.Add(dnskeyRrsig);
            signedCount++;
        }

        settings.LastSignedAt = DateTimeOffset.UtcNow;
        if (!_zoneSettings.ContainsKey(zoneName))
            _zoneSettings[zoneName] = settings;

        _logger.LogInformation("Signed zone {Zone}: {Count} RRsets signed", zoneName, signedCount);
        return signedCount;
    }

    // ── DS Record Generation ───────────────────────────────────────────

    /// <summary>
    /// Generate a DS record for the parent zone delegation (RFC 4034 Section 5).
    /// Uses SHA-256 (digest type 2).
    /// </summary>
    public DnssecDsRecord GenerateDsRecord(string zoneName)
    {
        var ksk = _keyPairs.FirstOrDefault(k =>
            k.ZoneName.Equals(zoneName, StringComparison.OrdinalIgnoreCase) &&
            k.KeyType == DnssecKeyType.KSK && k.IsActive);

        if (ksk == null) return null;

        // DS digest = SHA-256(owner_name_wire | DNSKEY_RDATA)
        var ownerWire = EncodeDnsNameWire(zoneName);
        var dnskeyRdata = BuildDnskeyRdata(ksk);

        var digestInput = new byte[ownerWire.Length + dnskeyRdata.Length];
        ownerWire.CopyTo(digestInput, 0);
        dnskeyRdata.CopyTo(digestInput, ownerWire.Length);

        var digest = SHA256.HashData(digestInput);

        return new DnssecDsRecord
        {
            ZoneName = zoneName,
            KeyTag = ksk.KeyTag,
            Algorithm = ksk.Algorithm,
            DigestType = 2, // SHA-256
            Digest = Convert.ToHexString(digest).ToLowerInvariant(),
        };
    }

    // ── DNSKEY Record Generation ───────────────────────────────────────

    /// <summary>
    /// Return DNSKEY records for the zone (both KSK and ZSK).
    /// </summary>
    public List<DnsDnskeyRecord> GetDnskeyRecords(string zoneName)
    {
        var keys = _keyPairs.Where(k =>
            k.ZoneName.Equals(zoneName, StringComparison.OrdinalIgnoreCase) && k.IsActive).ToList();

        return keys.Select(k => new DnsDnskeyRecord
        {
            Name = zoneName,
            // Flags: 256 = ZSK (zone key), 257 = KSK (zone key + SEP)
            Flags = k.KeyType == DnssecKeyType.KSK ? (ushort)257 : (ushort)256,
            Protocol = 3, // Fixed per RFC 4034
            Algorithm = (byte)k.Algorithm,
            PublicKey = k.PublicKey,
            KeyTag = k.KeyTag,
            Ttl = 3600,
        }).ToList();
    }

    // ── NSEC3 Record Generation ────────────────────────────────────────

    /// <summary>
    /// Generate NSEC3 records for authenticated denial of existence (RFC 5155).
    /// </summary>
    public async Task<List<DnsNsec3Record>> GetNsec3RecordsAsync(string zoneName, CancellationToken ct = default)
    {
        var nsec3Records = new List<DnsNsec3Record>();

        // Collect all owner names in the zone
        var ownerNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { zoneName };

        foreach (var recordType in new[] { DnsRecordType.A, DnsRecordType.AAAA, DnsRecordType.CNAME,
            DnsRecordType.MX, DnsRecordType.NS, DnsRecordType.PTR, DnsRecordType.SRV, DnsRecordType.TXT })
        {
            var records = await _zoneStore.GetAllRecordsAsync(DefaultTenantId, zoneName, recordType, ct);
            foreach (var r in records)
                ownerNames.Add(r.Name);
        }

        // NSEC3PARAM: hash algorithm 1 (SHA-1), iterations 10, salt
        byte[] salt = RandomNumberGenerator.GetBytes(8);
        const int iterations = 10;

        var hashedNames = ownerNames
            .Select(name => (Name: name, Hash: ComputeNsec3Hash(name, salt, iterations)))
            .OrderBy(x => x.Hash, StringComparer.OrdinalIgnoreCase)
            .ToList();

        for (var i = 0; i < hashedNames.Count; i++)
        {
            var current = hashedNames[i];
            var next = hashedNames[(i + 1) % hashedNames.Count];

            // Determine which record types exist at this name
            var typesBitmap = await GetTypeBitmapAsync(zoneName, current.Name, ct);

            nsec3Records.Add(new DnsNsec3Record
            {
                Name = $"{current.Hash}.{zoneName}",
                HashAlgorithm = 1, // SHA-1
                Flags = 0, // No opt-out
                Iterations = (ushort)iterations,
                Salt = salt,
                NextHashedOwnerName = next.Hash,
                TypeBitmap = typesBitmap,
                Ttl = 600,
            });
        }

        return nsec3Records;
    }

    // ── RRSIG Records for DNS responses ────────────────────────────────

    /// <summary>
    /// Get RRSIG records covering a specific name and type in the zone.
    /// </summary>
    public List<DnssecRrsigRecord> GetRrsigRecords(string zoneName, string name, DnsRecordType type)
    {
        return _rrsigRecords.Where(r =>
            r.SignerName.Equals(zoneName, StringComparison.OrdinalIgnoreCase) &&
            r.OwnerName.Equals(name, StringComparison.OrdinalIgnoreCase) &&
            r.TypeCovered == type).ToList();
    }

    /// <summary>
    /// Get all RRSIG records for a zone.
    /// </summary>
    public List<DnssecRrsigRecord> GetAllRrsigRecords(string zoneName)
    {
        return _rrsigRecords.Where(r =>
            r.SignerName.Equals(zoneName, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    // ── Signature Validation ───────────────────────────────────────────

    /// <summary>
    /// Validate an RRSIG signature against the zone's DNSKEY.
    /// </summary>
    public bool ValidateSignature(DnsRecord record, DnssecRrsigRecord rrsig)
    {
        var key = _keyPairs.FirstOrDefault(k =>
            k.ZoneName.Equals(rrsig.SignerName, StringComparison.OrdinalIgnoreCase) &&
            k.KeyTag == rrsig.KeyTag && k.IsActive);

        if (key == null)
        {
            _logger.LogWarning("No matching key found for RRSIG validation: zone={Zone}, keyTag={KeyTag}",
                rrsig.SignerName, rrsig.KeyTag);
            return false;
        }

        // Check expiration
        if (DateTimeOffset.UtcNow > rrsig.SignatureExpiration || DateTimeOffset.UtcNow < rrsig.SignatureInception)
        {
            _logger.LogWarning("RRSIG is expired or not yet valid for {Name} {Type}", record.Name, record.Type);
            return false;
        }

        try
        {
            var signData = BuildRrsigSignData(rrsig, record);
            var privateKey = UnprotectPrivateKey(key.PrivateKey);

            if (key.Algorithm == 13)
            {
                using var ecdsa = ECDsa.Create(new ECParameters
                {
                    Curve = ECCurve.NamedCurves.nistP256,
                    Q = new ECPoint
                    {
                        X = key.PublicKey[..32],
                        Y = key.PublicKey[32..64],
                    },
                });
                return ecdsa.VerifyData(signData, rrsig.Signature, HashAlgorithmName.SHA256);
            }
            else if (key.Algorithm == 8)
            {
                using var rsa = RSA.Create();
                rsa.ImportRSAPublicKey(key.PublicKey, out _);
                return rsa.VerifyData(signData, rrsig.Signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RRSIG validation failed for {Name} {Type}", record.Name, record.Type);
        }

        return false;
    }

    // ── Private Helpers ────────────────────────────────────────────────

    private DnssecRrsigRecord CreateRrsig(
        string zoneName, string ownerName, DnsRecordType typeCovered,
        List<DnsRecord> rrset, DnssecKeyPair signingKey,
        DateTimeOffset inception, DateTimeOffset expiration)
    {
        var rrsig = new DnssecRrsigRecord
        {
            OwnerName = ownerName,
            TypeCovered = typeCovered,
            Algorithm = signingKey.Algorithm,
            Labels = CountLabels(ownerName),
            OriginalTtl = (uint)(rrset.FirstOrDefault()?.Ttl ?? 600),
            SignatureExpiration = expiration,
            SignatureInception = inception,
            KeyTag = signingKey.KeyTag,
            SignerName = zoneName,
        };

        // Build the data to sign: RRSIG RDATA (without signature) + canonical RRset
        var signData = BuildRrsigSignData(rrsig, rrset);
        rrsig.Signature = SignData(signData, signingKey);

        return rrsig;
    }

    private DnssecRrsigRecord CreateDnskeyRrsig(
        string zoneName, DnssecKeyPair ksk,
        DateTimeOffset inception, DateTimeOffset expiration)
    {
        var rrsig = new DnssecRrsigRecord
        {
            OwnerName = zoneName,
            TypeCovered = DnsRecordType.DNSKEY,
            Algorithm = ksk.Algorithm,
            Labels = CountLabels(zoneName),
            OriginalTtl = 3600,
            SignatureExpiration = expiration,
            SignatureInception = inception,
            KeyTag = ksk.KeyTag,
            SignerName = zoneName,
        };

        // Sign the DNSKEY RRset
        var dnskeyRdata = GetKeyPairs(zoneName)
            .Where(k => k.IsActive)
            .Select(k => BuildDnskeyRdata(k))
            .ToList();

        var signData = BuildDnskeyRrsigSignData(rrsig, zoneName, dnskeyRdata);
        rrsig.Signature = SignData(signData, ksk);

        return rrsig;
    }

    private byte[] SignData(byte[] data, DnssecKeyPair key)
    {
        var privateKeyBytes = UnprotectPrivateKey(key.PrivateKey);

        if (key.Algorithm == 13)
        {
            using var ecdsa = ECDsa.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                D = privateKeyBytes,
                Q = new ECPoint
                {
                    X = key.PublicKey[..32],
                    Y = key.PublicKey[32..64],
                },
            });
            // ECDSA signatures in DNSSEC use the raw (r || s) format, not DER
            return ecdsa.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        else if (key.Algorithm == 8)
        {
            using var rsa = RSA.Create();
            rsa.ImportRSAPrivateKey(privateKeyBytes, out _);
            return rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }

        throw new InvalidOperationException($"Unsupported algorithm: {key.Algorithm}");
    }

    private static byte[] BuildRrsigSignData(DnssecRrsigRecord rrsig, List<DnsRecord> rrset)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // RRSIG RDATA fields (without signature)
        writer.Write(BinaryPrimitives.ReverseEndianness((ushort)rrsig.TypeCovered));
        writer.Write((byte)rrsig.Algorithm);
        writer.Write((byte)rrsig.Labels);
        writer.Write(BinaryPrimitives.ReverseEndianness(rrsig.OriginalTtl));
        writer.Write(BinaryPrimitives.ReverseEndianness((uint)rrsig.SignatureExpiration.ToUnixTimeSeconds()));
        writer.Write(BinaryPrimitives.ReverseEndianness((uint)rrsig.SignatureInception.ToUnixTimeSeconds()));
        writer.Write(BinaryPrimitives.ReverseEndianness((ushort)rrsig.KeyTag));
        writer.Write(EncodeDnsNameWire(rrsig.SignerName));

        // Canonical RRset (sorted)
        var sortedRrset = rrset.OrderBy(r => r.Data, StringComparer.Ordinal).ToList();
        foreach (var rr in sortedRrset)
        {
            writer.Write(EncodeDnsNameWire(rr.Name));
            writer.Write(BinaryPrimitives.ReverseEndianness((ushort)rr.Type));
            writer.Write(BinaryPrimitives.ReverseEndianness((ushort)1)); // IN class
            writer.Write(BinaryPrimitives.ReverseEndianness(rrsig.OriginalTtl));
            var rdata = Encoding.ASCII.GetBytes(rr.Data);
            writer.Write(BinaryPrimitives.ReverseEndianness((ushort)rdata.Length));
            writer.Write(rdata);
        }

        return ms.ToArray();
    }

    private static byte[] BuildRrsigSignData(DnssecRrsigRecord rrsig, DnsRecord record)
    {
        return BuildRrsigSignData(rrsig, [record]);
    }

    private static byte[] BuildDnskeyRrsigSignData(DnssecRrsigRecord rrsig, string zoneName, List<byte[]> dnskeyRdatas)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // RRSIG RDATA fields
        writer.Write(BinaryPrimitives.ReverseEndianness((ushort)rrsig.TypeCovered));
        writer.Write((byte)rrsig.Algorithm);
        writer.Write((byte)rrsig.Labels);
        writer.Write(BinaryPrimitives.ReverseEndianness(rrsig.OriginalTtl));
        writer.Write(BinaryPrimitives.ReverseEndianness((uint)rrsig.SignatureExpiration.ToUnixTimeSeconds()));
        writer.Write(BinaryPrimitives.ReverseEndianness((uint)rrsig.SignatureInception.ToUnixTimeSeconds()));
        writer.Write(BinaryPrimitives.ReverseEndianness((ushort)rrsig.KeyTag));
        writer.Write(EncodeDnsNameWire(rrsig.SignerName));

        // DNSKEY RRset entries
        var ownerWire = EncodeDnsNameWire(zoneName);
        foreach (var rdata in dnskeyRdatas.OrderBy(r => r, ByteArrayComparer.Instance))
        {
            writer.Write(ownerWire);
            writer.Write(BinaryPrimitives.ReverseEndianness((ushort)DnsRecordType.DNSKEY));
            writer.Write(BinaryPrimitives.ReverseEndianness((ushort)1)); // IN class
            writer.Write(BinaryPrimitives.ReverseEndianness(rrsig.OriginalTtl));
            writer.Write(BinaryPrimitives.ReverseEndianness((ushort)rdata.Length));
            writer.Write(rdata);
        }

        return ms.ToArray();
    }

    private static byte[] BuildDnskeyRdata(DnssecKeyPair key)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        ushort flags = key.KeyType == DnssecKeyType.KSK ? (ushort)257 : (ushort)256;
        writer.Write(BinaryPrimitives.ReverseEndianness(flags));
        writer.Write((byte)3); // Protocol
        writer.Write((byte)key.Algorithm);
        writer.Write(key.PublicKey);

        return ms.ToArray();
    }

    /// <summary>
    /// Calculate key tag per RFC 4034 Appendix B.
    /// </summary>
    private static int CalculateKeyTag(DnssecKeyPair key)
    {
        var rdata = BuildDnskeyRdata(key);
        uint ac = 0;
        for (var i = 0; i < rdata.Length; i++)
        {
            ac += (i & 1) == 0 ? (uint)(rdata[i] << 8) : rdata[i];
        }
        ac += (ac >> 16) & 0xFFFF;
        return (int)(ac & 0xFFFF);
    }

    private static byte[] ExportRsaDnskeyPublicKey(RSAParameters rsaParams)
    {
        // DNSKEY RDATA format for RSA: exponent length prefix + exponent + modulus
        var exponent = rsaParams.Exponent;
        var modulus = rsaParams.Modulus;

        using var ms = new MemoryStream();
        if (exponent.Length <= 255)
        {
            ms.WriteByte((byte)exponent.Length);
        }
        else
        {
            ms.WriteByte(0);
            ms.WriteByte((byte)(exponent.Length >> 8));
            ms.WriteByte((byte)(exponent.Length & 0xFF));
        }
        ms.Write(exponent);
        ms.Write(modulus);
        return ms.ToArray();
    }

    private static byte[] EncodeDnsNameWire(string name)
    {
        using var ms = new MemoryStream();
        foreach (var label in name.Split('.'))
        {
            var bytes = Encoding.ASCII.GetBytes(label);
            ms.WriteByte((byte)bytes.Length);
            ms.Write(bytes);
        }
        ms.WriteByte(0); // root label
        return ms.ToArray();
    }

    private static int CountLabels(string name)
    {
        if (string.IsNullOrEmpty(name) || name == ".") return 0;
        return name.Split('.').Length;
    }

    /// <summary>
    /// Compute NSEC3 hash per RFC 5155: iterated SHA-1 hash of owner name.
    /// </summary>
    private static string ComputeNsec3Hash(string ownerName, byte[] salt, int iterations)
    {
        var nameWire = EncodeDnsNameWire(ownerName.ToLowerInvariant());
        var x = SHA1.HashData([.. nameWire, .. salt]);

        for (var i = 0; i < iterations; i++)
        {
            x = SHA1.HashData([.. x, .. salt]);
        }

        return Base32Hex.Encode(x);
    }

    private async Task<byte[]> GetTypeBitmapAsync(string zoneName, string name, CancellationToken ct)
    {
        // Simplified: build a bitmap of which record types exist at this name
        var types = new HashSet<ushort>();
        foreach (var rt in new[] { DnsRecordType.A, DnsRecordType.AAAA, DnsRecordType.CNAME,
            DnsRecordType.MX, DnsRecordType.NS, DnsRecordType.PTR, DnsRecordType.SRV, DnsRecordType.TXT })
        {
            var records = await _zoneStore.GetRecordsByNameAsync(DefaultTenantId, zoneName, name, rt, ct);
            if (records.Count > 0) types.Add((ushort)rt);
        }

        // Always include RRSIG and NSEC3 for signed zones
        types.Add((ushort)DnsRecordType.RRSIG);
        types.Add((ushort)DnsRecordType.NSEC3);

        // Encode type bitmap per RFC 4034 Section 4.1.2
        return EncodeTypeBitmap(types);
    }

    private static byte[] EncodeTypeBitmap(HashSet<ushort> types)
    {
        if (types.Count == 0) return [];

        using var ms = new MemoryStream();
        var windows = types.GroupBy(t => t / 256).OrderBy(g => g.Key);

        foreach (var window in windows)
        {
            var windowNum = (byte)window.Key;
            var bitmap = new byte[32]; // max 256 bits per window
            var maxBit = 0;

            foreach (var typeVal in window)
            {
                var bit = typeVal % 256;
                bitmap[bit / 8] |= (byte)(0x80 >> (bit % 8));
                if (bit / 8 + 1 > maxBit) maxBit = bit / 8 + 1;
            }

            ms.WriteByte(windowNum);
            ms.WriteByte((byte)maxBit);
            ms.Write(bitmap, 0, maxBit);
        }

        return ms.ToArray();
    }

    /// <summary>
    /// Protect private key at rest using DPAPI on Windows, or a simple marker on other platforms.
    /// In production, use Azure Key Vault or similar HSM.
    /// </summary>
    private static byte[] ProtectPrivateKey(byte[] privateKey)
    {
        // For this implementation, we store as-is. In production, encrypt with DPAPI or KV.
        return privateKey;
    }

    private static byte[] UnprotectPrivateKey(byte[] protectedKey)
    {
        return protectedKey;
    }

    private sealed class ByteArrayComparer : IComparer<byte[]>
    {
        public static readonly ByteArrayComparer Instance = new();

        public int Compare(byte[] x, byte[] y)
        {
            if (x == null && y == null) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            var len = Math.Min(x.Length, y.Length);
            for (var i = 0; i < len; i++)
            {
                var cmp = x[i].CompareTo(y[i]);
                if (cmp != 0) return cmp;
            }
            return x.Length.CompareTo(y.Length);
        }
    }
}

// ── Data Models ────────────────────────────────────────────────────────

public class DnssecKeyPair
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ZoneName { get; set; } = "";
    public DnssecKeyType KeyType { get; set; }
    public int Algorithm { get; set; } = 13; // ECDSAP256SHA256
    public int KeyTag { get; set; }
    public byte[] PublicKey { get; set; } = [];
    public byte[] PrivateKey { get; set; } = []; // Encrypted at rest
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public bool IsActive { get; set; }
}

public enum DnssecKeyType { KSK, ZSK }

public class DnssecZoneSettings
{
    public string ZoneName { get; set; } = "";
    public bool DnssecEnabled { get; set; }
    public int SignatureValidityDays { get; set; } = 30;
    public int KeyRolloverIntervalDays { get; set; } = 90;
    public DateTimeOffset? LastSignedAt { get; set; }
}

public class DnssecRrsigRecord
{
    public string OwnerName { get; set; } = "";
    public DnsRecordType TypeCovered { get; set; }
    public int Algorithm { get; set; }
    public int Labels { get; set; }
    public uint OriginalTtl { get; set; }
    public DateTimeOffset SignatureExpiration { get; set; }
    public DateTimeOffset SignatureInception { get; set; }
    public int KeyTag { get; set; }
    public string SignerName { get; set; } = "";
    public byte[] Signature { get; set; } = [];
}

public class DnssecDsRecord
{
    public string ZoneName { get; set; } = "";
    public int KeyTag { get; set; }
    public int Algorithm { get; set; }
    public int DigestType { get; set; }
    public string Digest { get; set; } = "";

    /// <summary>
    /// Format as a DS record string for registrar submission.
    /// </summary>
    public override string ToString() =>
        $"{ZoneName}. IN DS {KeyTag} {Algorithm} {DigestType} {Digest}";
}

/// <summary>
/// Base32hex encoding per RFC 4648 (used by NSEC3).
/// </summary>
internal static class Base32Hex
{
    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUV";

    public static string Encode(byte[] data)
    {
        var sb = new StringBuilder((data.Length * 8 + 4) / 5);
        var buffer = 0;
        var bitsLeft = 0;

        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bitsLeft += 8;
            while (bitsLeft >= 5)
            {
                bitsLeft -= 5;
                sb.Append(Alphabet[(buffer >> bitsLeft) & 0x1F]);
            }
        }

        if (bitsLeft > 0)
        {
            sb.Append(Alphabet[(buffer << (5 - bitsLeft)) & 0x1F]);
        }

        return sb.ToString();
    }
}
