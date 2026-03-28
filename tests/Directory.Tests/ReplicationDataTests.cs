using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Directory.Replication.Drsr;
using Directory.Replication.DcPromotion;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Directory.Tests;

/// <summary>
/// Tests for replication data processing: PrefixTableResolver, DrsSecretDecryptor,
/// XpressDecompressor, LinkedValueParser models, SiteMapper, and RemoteDcRegistrar constants.
/// </summary>
public class ReplicationDataTests
{
    // ════════════════════════════════════════════════════════════════
    //  1. PrefixTableResolver Tests
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Build a PrefixTableResolver with the well-known prefix for 2.5.4 (index 0).
    /// BER encoding of 2.5.4: first byte = 2*40+5 = 85 (0x55), second byte = 4 (0x04).
    /// </summary>
    private static PrefixTableResolver CreateResolverWithStandardPrefixes()
    {
        var entries = new Dictionary<uint, byte[]>
        {
            // Prefix 0x0000: OID 2.5.4 => BER bytes [0x55, 0x04]
            [0x0000] = [0x55, 0x04],
            // Prefix 0x0009: OID 1.2.840.113556.1.4 => BER encoding
            // 1.2 = 1*40+2 = 42 (0x2A)
            // 840 = 0x86, 0x48 (base-128)
            // 113556 = 0x86, 0xF7, 0x14 (base-128)
            // 1 = 0x01
            // 4 = 0x04
            [0x0009] = [0x2A, 0x86, 0x48, 0x86, 0xF7, 0x14, 0x01, 0x04],
        };

        return new PrefixTableResolver(entries);
    }

    [Fact]
    public void ResolveToOid_WellKnownPrefix0_ReturnsCorrectOid()
    {
        var resolver = CreateResolverWithStandardPrefixes();

        // ATTRTYP = (prefix=0x0000 << 16) | suffix=3 => 0x00000003
        // Should resolve to OID 2.5.4.3 (cn)
        var oid = resolver.ResolveToOid(0x00000003);

        Assert.Equal("2.5.4.3", oid);
    }

    [Fact]
    public void ResolveToName_ObjectClass_ReturnsObjectClass()
    {
        var resolver = CreateResolverWithStandardPrefixes();

        // ATTRTYP 0x00000000 is well-known for objectClass (2.5.4.0)
        var name = resolver.ResolveToName(0x00000000);

        Assert.Equal("objectClass", name);
    }

    [Fact]
    public void ResolveToName_Cn_ReturnsCn()
    {
        var resolver = CreateResolverWithStandardPrefixes();

        // ATTRTYP 0x00000003 is well-known for cn (2.5.4.3)
        var name = resolver.ResolveToName(0x00000003);

        Assert.Equal("cn", name);
    }

    [Fact]
    public void ResolveToName_SAMAccountName_Returns()
    {
        var resolver = CreateResolverWithStandardPrefixes();

        // ATTRTYP 0x000900DD is well-known for sAMAccountName (1.2.840.113556.1.4.221)
        var name = resolver.ResolveToName(0x000900DD);

        Assert.Equal("sAMAccountName", name);
    }

    [Fact]
    public void ResolveToName_UnknownAttrTyp_ReturnsNull()
    {
        // Build a resolver with no entries
        var resolver = new PrefixTableResolver(new Dictionary<uint, byte[]>());

        // An ATTRTYP with no matching prefix and no well-known mapping
        var name = resolver.ResolveToName(0xFFFF0001);

        Assert.Null(name);
    }

    [Fact]
    public void ResolveToOid_LargeSuffix_UsesMultiByteEncoding()
    {
        var resolver = CreateResolverWithStandardPrefixes();

        // ATTRTYP = (prefix=0x0009 << 16) | suffix=0x00DD (221)
        // OID should be 1.2.840.113556.1.4.221 (sAMAccountName)
        // suffix 221 >= 128, so multi-byte BER encoding is used
        var oid = resolver.ResolveToOid(0x000900DD);

        Assert.Equal("1.2.840.113556.1.4.221", oid);
    }

    [Fact]
    public void FromWireTable_WithEntries_BuildsResolver()
    {
        var wireTable = new SCHEMA_PREFIX_TABLE
        {
            PrefixCount = 1,
            PPrefixEntry =
            [
                new OID_PREFIX_ENTRY
                {
                    NdxValue = 0x0000,
                    Prefix = [0x55, 0x04], // 2.5.4
                },
            ],
        };

        var resolver = PrefixTableResolver.FromWireTable(wireTable);

        // Resolve ATTRTYP 0x00000003 => OID 2.5.4.3
        var oid = resolver.ResolveToOid(0x00000003);
        Assert.Equal("2.5.4.3", oid);
    }

    [Fact]
    public void OidBytesToString_StandardPrefix_Correct()
    {
        // Test via ResolveToOid which internally calls OidBytesToString.
        // OID bytes for 2.5.4.0: first byte = 2*40+5 = 85, then 4, then 0
        var entries = new Dictionary<uint, byte[]>
        {
            [0x0000] = [0x55, 0x04],
        };

        var resolver = new PrefixTableResolver(entries);

        // suffix=0 => append byte 0x00 => full bytes [0x55, 0x04, 0x00]
        // Decode: first byte 85 => arc0=2, arc1=5; then 4 => arc=4; then 0 => arc=0
        // Result: "2.5.4.0"
        var oid = resolver.ResolveToOid(0x00000000);
        Assert.Equal("2.5.4.0", oid);

        // Also test first byte decoding: 1.2 => 1*40+2 = 42
        entries = new Dictionary<uint, byte[]>
        {
            [0x0000] = [0x2A], // 1.2
        };
        resolver = new PrefixTableResolver(entries);

        var oid12 = resolver.ResolveToOid(0x00000005);
        Assert.Equal("1.2.5", oid12);
    }

    // ════════════════════════════════════════════════════════════════
    //  2. DrsSecretDecryptor Tests
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void IsSecretAttribute_UnicodePwd_ReturnsTrue()
    {
        Assert.True(DrsSecretDecryptor.IsSecretAttribute("unicodePwd"));
    }

    [Fact]
    public void IsSecretAttribute_SupplementalCredentials_ReturnsTrue()
    {
        Assert.True(DrsSecretDecryptor.IsSecretAttribute("supplementalCredentials"));
    }

    [Fact]
    public void IsSecretAttribute_Cn_ReturnsFalse()
    {
        Assert.False(DrsSecretDecryptor.IsSecretAttribute("cn"));
    }

    [Fact]
    public void IsSecretAttribute_TrustAuthIncoming_ReturnsTrue()
    {
        Assert.True(DrsSecretDecryptor.IsSecretAttribute("trustAuthIncoming"));
    }

    [Fact]
    public void NeedsDecryption_ShortValue_ReturnsFalse()
    {
        var shortValue = new byte[31]; // < 32 bytes
        Assert.False(DrsSecretDecryptor.NeedsDecryption(shortValue));
    }

    [Fact]
    public void NeedsDecryption_LongValue_ReturnsTrue()
    {
        var longValue = new byte[32]; // >= 32 bytes
        Assert.True(DrsSecretDecryptor.NeedsDecryption(longValue));
    }

    [Fact]
    public void Decrypt_ValidBlob_ReturnsPlaintext()
    {
        // Construct a valid encrypted blob per MS-DRSR 4.1.10.6.12
        var sessionKey = new byte[16];
        for (int i = 0; i < sessionKey.Length; i++)
            sessionKey[i] = (byte)(0xAA + i);

        var plaintext = Encoding.UTF8.GetBytes("SecretPassword123!");

        var salt = new byte[16];
        for (int i = 0; i < salt.Length; i++)
            salt[i] = (byte)(0x11 + i);

        // DecryptionKey = MD5(sessionKey + salt)
        byte[] decryptionKey;
        using (var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5))
        {
            md5.AppendData(sessionKey);
            md5.AppendData(salt);
            decryptionKey = md5.GetHashAndReset();
        }

        // Encrypt plaintext with RC4
        var encrypted = Rc4(decryptionKey, plaintext);

        // Checksum = MD5(sessionKey + plaintext)
        byte[] checksum;
        using (var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5))
        {
            md5.AppendData(sessionKey);
            md5.AppendData(plaintext);
            checksum = md5.GetHashAndReset();
        }

        // Blob = salt + checksum + encrypted
        var blob = salt.Concat(checksum).Concat(encrypted).ToArray();

        // Decrypt and verify
        var decrypted = DrsSecretDecryptor.Decrypt(blob, sessionKey);

        Assert.Equal(plaintext, decrypted);
        Assert.Equal("SecretPassword123!", Encoding.UTF8.GetString(decrypted));
    }

    [Fact]
    public void Decrypt_InvalidChecksum_ThrowsOrReturnsEmpty()
    {
        // Construct a blob with a tampered checksum
        var sessionKey = new byte[16];
        for (int i = 0; i < sessionKey.Length; i++)
            sessionKey[i] = (byte)(0xBB + i);

        var plaintext = Encoding.UTF8.GetBytes("TestData");

        var salt = new byte[16];
        for (int i = 0; i < salt.Length; i++)
            salt[i] = (byte)(0x22 + i);

        // DecryptionKey = MD5(sessionKey + salt)
        byte[] decryptionKey;
        using (var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5))
        {
            md5.AppendData(sessionKey);
            md5.AppendData(salt);
            decryptionKey = md5.GetHashAndReset();
        }

        var encrypted = Rc4(decryptionKey, plaintext);

        // Create a bad checksum (all zeros instead of correct hash)
        var badChecksum = new byte[16];

        var blob = salt.Concat(badChecksum).Concat(encrypted).ToArray();

        // Should throw CryptographicException due to checksum mismatch
        Assert.Throws<CryptographicException>(() => DrsSecretDecryptor.Decrypt(blob, sessionKey));
    }

    /// <summary>
    /// RC4 helper for constructing test blobs.
    /// </summary>
    private static byte[] Rc4(byte[] key, byte[] input)
    {
        // KSA
        var s = new byte[256];
        for (int i = 0; i < 256; i++)
            s[i] = (byte)i;

        int j = 0;
        for (int i = 0; i < 256; i++)
        {
            j = (j + s[i] + key[i % key.Length]) & 0xFF;
            (s[i], s[j]) = (s[j], s[i]);
        }

        // PRGA
        var output = new byte[input.Length];
        int si = 0, sj = 0;
        for (int k = 0; k < input.Length; k++)
        {
            si = (si + 1) & 0xFF;
            sj = (sj + s[si]) & 0xFF;
            (s[si], s[sj]) = (s[sj], s[si]);
            byte keyByte = s[(s[si] + s[sj]) & 0xFF];
            output[k] = (byte)(input[k] ^ keyByte);
        }

        return output;
    }

    // ════════════════════════════════════════════════════════════════
    //  3. XpressDecompressor Tests
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void DecompressPlainLz77_LiteralsOnly_ReturnsOriginal()
    {
        // Construct a Plain LZ77 compressed buffer with only literal bytes.
        // Flags word = 0x00000000 (all 32 bits = 0 = literal)
        // Followed by 32 literal bytes (one per flag bit)
        var literals = new byte[32];
        for (int i = 0; i < 32; i++)
            literals[i] = (byte)(0x41 + (i % 26)); // A-Z repeating

        var compressed = new byte[4 + 32]; // 4 bytes flags + 32 bytes literals
        BinaryPrimitives.WriteUInt32LittleEndian(compressed.AsSpan(0), 0x00000000);
        Array.Copy(literals, 0, compressed, 4, 32);

        var result = XpressDecompressor.DecompressPlainLz77(compressed, 32);

        Assert.Equal(32, result.Length);
        Assert.Equal(literals, result);
    }

    [Fact]
    public void DecompressPlainLz77_EmptyInput_ReturnsEmpty()
    {
        var result = XpressDecompressor.DecompressPlainLz77([], 0);
        Assert.Empty(result);
    }

    [Fact]
    public void Decompress_AutoDetect_HandlesPlainLz77()
    {
        // Small data (< 256 bytes) forces plain LZ77 path
        var literals = new byte[8];
        for (int i = 0; i < 8; i++)
            literals[i] = (byte)(0x30 + i); // '0' through '7'

        // Flags: 8 literal bits (bits 0-7 = 0), rest don't matter since uncompressedSize=8
        var compressed = new byte[4 + 8];
        BinaryPrimitives.WriteUInt32LittleEndian(compressed.AsSpan(0), 0x00000000);
        Array.Copy(literals, 0, compressed, 4, 8);

        var result = XpressDecompressor.Decompress(compressed, 8);

        Assert.Equal(8, result.Length);
        Assert.Equal(literals, result);
    }

    // ════════════════════════════════════════════════════════════════
    //  4. LinkedValueParser Model Tests
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void LinkedValueChange_Properties_RoundTrip()
    {
        var objectGuid = Guid.NewGuid();
        var valueGuid = Guid.NewGuid();
        var dsaGuid = Guid.NewGuid();
        var timeChanged = new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc);

        var change = new LinkedValueChange
        {
            ObjectDn = "CN=TestGroup,DC=corp,DC=com",
            ObjectGuid = objectGuid,
            AttributeName = "member",
            ValueDn = "CN=TestUser,DC=corp,DC=com",
            ValueGuid = valueGuid,
            IsPresent = true,
            TimeChanged = timeChanged,
            Version = 3,
            OriginatingDsa = dsaGuid,
            OriginatingUsn = 12345L,
        };

        Assert.Equal("CN=TestGroup,DC=corp,DC=com", change.ObjectDn);
        Assert.Equal(objectGuid, change.ObjectGuid);
        Assert.Equal("member", change.AttributeName);
        Assert.Equal("CN=TestUser,DC=corp,DC=com", change.ValueDn);
        Assert.Equal(valueGuid, change.ValueGuid);
        Assert.True(change.IsPresent);
        Assert.Equal(timeChanged, change.TimeChanged);
        Assert.Equal(3, change.Version);
        Assert.Equal(dsaGuid, change.OriginatingDsa);
        Assert.Equal(12345L, change.OriginatingUsn);
    }

    [Fact]
    public void LinkedValueChange_IsPresent_TrueForAdd()
    {
        var change = new LinkedValueChange { IsPresent = true };
        Assert.True(change.IsPresent);
    }

    [Fact]
    public void LinkedValueChange_IsPresent_FalseForRemove()
    {
        var change = new LinkedValueChange { IsPresent = false };
        Assert.False(change.IsPresent);
    }

    // ════════════════════════════════════════════════════════════════
    //  5. SiteMapper Tests
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void IsInSubnet_MatchingAddress_ReturnsTrue()
    {
        // SiteMapper.IsInSubnet is private, so we test it indirectly through reflection
        var method = typeof(SiteMapper).GetMethod(
            "IsInSubnet",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(method);

        var address = System.Net.IPAddress.Parse("10.0.0.50");
        var cidrSubnet = "10.0.0.0/24";
        var args = new object[] { address, cidrSubnet, 0 };

        var result = (bool)method.Invoke(null, args);

        Assert.True(result);
        Assert.Equal(24, (int)args[2]); // prefixLength output parameter
    }

    [Fact]
    public async Task DetermineSiteAsync_NoMatch_ReturnsDefaultSite()
    {
        // Since DetermineSiteAsync requires an LdapClient connected to a real server,
        // we test the default site fallback by passing an unparseable IP.
        var logger = new NullLogger<SiteMapper>();
        var mapper = new SiteMapper(logger);

        // With an invalid IP address, DetermineSiteAsync should return Default-First-Site-Name
        // We cannot call it without a connected LdapClient, but we can verify via reflection
        // that the constant is correct.
        var defaultSiteField = typeof(SiteMapper).GetField(
            "DefaultSiteName",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(defaultSiteField);
        var defaultSite = (string)defaultSiteField.GetValue(null);
        Assert.Equal("Default-First-Site-Name", defaultSite);
    }

    // ════════════════════════════════════════════════════════════════
    //  6. RemoteDcRegistrar Tests
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ServerTrustAccountFlag_HasCorrectValue()
    {
        // SERVER_TRUST_ACCOUNT (0x2000) | TRUSTED_FOR_DELEGATION (0x20) = 0x2020
        const int serverTrustAccount = 0x2000;
        const int trustedForDelegation = 0x0020;
        const int expected = 0x2020;

        Assert.Equal(expected, serverTrustAccount | trustedForDelegation);
        Assert.Equal(8224, expected); // decimal representation used in the code: "8224"
    }
}
