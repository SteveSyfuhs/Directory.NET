using System.Security.Cryptography;

namespace Directory.Replication.Drsr;

/// <summary>
/// Decrypts secret attributes received via DRS replication.
/// Windows DCs encrypt certain sensitive attributes (unicodePwd, supplementalCredentials, etc.)
/// using a session key derived from the replication authentication.
/// See MS-DRSR section 4.1.10.6.12 (DecryptValuesIfNecessary).
/// </summary>
public static class DrsSecretDecryptor
{
    /// <summary>
    /// Secret attribute names that are always encrypted during replication.
    /// </summary>
    private static readonly HashSet<string> SecretAttributeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "unicodePwd",
        "dBCSPwd",
        "supplementalCredentials",
        "currentValue",
        "priorValue",
        "trustAuthIncoming",
        "trustAuthOutgoing",
        "initialAuthIncoming",
        "initialAuthOutgoing",
    };

    /// <summary>
    /// Returns true if the given attribute name is a secret attribute that will be
    /// encrypted during DRS replication.
    /// </summary>
    public static bool IsSecretAttribute(string attributeName)
        => SecretAttributeNames.Contains(attributeName);

    /// <summary>
    /// Check if a value needs decryption. The encrypted blob must be at least 32 bytes
    /// (16 bytes salt + 16 bytes checksum + at least 0 bytes encrypted data).
    /// </summary>
    public static bool NeedsDecryption(byte[] value)
        => value.Length >= 32;

    /// <summary>
    /// Decrypt a secret attribute value using the DRS session key.
    /// Per MS-DRSR section 4.1.10.6.12:
    ///   1. The encrypted blob is: Salt(16 bytes) + Checksum(16 bytes) + EncryptedData(N bytes)
    ///   2. DecryptionKey = MD5(SessionKey + Salt)
    ///   3. PlaintextData = RC4(DecryptionKey, EncryptedData)
    ///   4. Verify: MD5(SessionKey + PlaintextData) == Checksum
    /// </summary>
    /// <param name="encryptedValue">The encrypted attribute value blob from replication.</param>
    /// <param name="sessionKey">The RPC session key from NTLM authentication.</param>
    /// <returns>The decrypted plaintext attribute value.</returns>
    /// <exception cref="CryptographicException">
    /// Thrown when the checksum verification fails, indicating the session key is wrong
    /// or the data is corrupted.
    /// </exception>
    public static byte[] Decrypt(byte[] encryptedValue, byte[] sessionKey)
    {
        if (encryptedValue.Length < 32)
            throw new ArgumentException("Encrypted value is too short (must be at least 32 bytes).", nameof(encryptedValue));

        if (sessionKey.Length == 0)
            throw new ArgumentException("Session key must not be empty.", nameof(sessionKey));

        // Extract components from the encrypted blob
        var salt = encryptedValue.AsSpan(0, 16);
        var expectedChecksum = encryptedValue.AsSpan(16, 16);
        var encryptedData = encryptedValue.AsSpan(32);

        // Derive the RC4 decryption key: MD5(sessionKey + salt)
        byte[] decryptionKey;
        using (var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5))
        {
            md5.AppendData(sessionKey);
            md5.AppendData(salt);
            decryptionKey = md5.GetHashAndReset();
        }

        // Decrypt using RC4
        var plaintext = new byte[encryptedData.Length];
        Rc4Transform(decryptionKey, encryptedData, plaintext);

        // Verify checksum: MD5(sessionKey + plaintext)
        byte[] actualChecksum;
        using (var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5))
        {
            md5.AppendData(sessionKey);
            md5.AppendData(plaintext);
            actualChecksum = md5.GetHashAndReset();
        }

        if (!CryptographicOperations.FixedTimeEquals(actualChecksum, expectedChecksum))
        {
            throw new CryptographicException(
                "DRS secret attribute checksum verification failed. " +
                "The session key may be incorrect or the data is corrupted.");
        }

        return plaintext;
    }

    /// <summary>
    /// RC4 (ARC4) stream cipher implementation.
    /// RC4 is a symmetric stream cipher where encryption and decryption are the same operation.
    /// </summary>
    private static void Rc4Transform(byte[] key, ReadOnlySpan<byte> input, Span<byte> output)
    {
        // KSA (Key Scheduling Algorithm)
        Span<byte> s = stackalloc byte[256];
        for (int i = 0; i < 256; i++)
            s[i] = (byte)i;

        int j = 0;
        for (int i = 0; i < 256; i++)
        {
            j = (j + s[i] + key[i % key.Length]) & 0xFF;
            (s[i], s[j]) = (s[j], s[i]);
        }

        // PRGA (Pseudo-Random Generation Algorithm)
        int si = 0, sj = 0;
        for (int k = 0; k < input.Length; k++)
        {
            si = (si + 1) & 0xFF;
            sj = (sj + s[si]) & 0xFF;
            (s[si], s[sj]) = (s[sj], s[si]);
            byte keyByte = s[(s[si] + s[sj]) & 0xFF];
            output[k] = (byte)(input[k] ^ keyByte);
        }
    }
}
