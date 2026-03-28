using System.Security.Cryptography;

namespace Directory.Rpc.Nrpc;

/// <summary>
/// Represents a NETLOGON_CREDENTIAL (MS-NRPC 2.2.1.3.4) — an 8-byte value
/// used during secure channel authentication.
/// </summary>
public class NETLOGON_CREDENTIAL
{
    public byte[] Data { get; set; } = new byte[8];
}

/// <summary>
/// Tracks the state of a Netlogon secure channel between a workstation and the DC.
/// Holds the challenge pair, session key, and credential state for ongoing authentication.
/// </summary>
public class SecureChannelSession
{
    public string ComputerName { get; set; } = "";
    public string AccountName { get; set; } = "";
    public byte[] ClientChallenge { get; set; } = Array.Empty<byte>();
    public byte[] ServerChallenge { get; set; } = Array.Empty<byte>();
    public byte[] SessionKey { get; set; } = Array.Empty<byte>();
    public uint NegotiateFlags { get; set; }
    public NETLOGON_CREDENTIAL ClientCredential { get; set; } = new();
    public NETLOGON_CREDENTIAL ServerCredential { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Computes the Netlogon session key per MS-NRPC 3.1.4.3.1.
    /// </summary>
    /// <param name="sharedSecret">The NT hash of the machine account password.</param>
    /// <param name="clientChallenge">8-byte client challenge from NetrServerReqChallenge.</param>
    /// <param name="serverChallenge">8-byte server challenge from NetrServerReqChallenge.</param>
    /// <param name="useAes">True if AES negotiation flag is set.</param>
    /// <returns>16-byte session key.</returns>
    public static byte[] ComputeSessionKey(byte[] sharedSecret, byte[] clientChallenge, byte[] serverChallenge, bool useAes)
    {
        // Concatenate challenges: clientChallenge || serverChallenge
        var challengeData = new byte[16];
        Buffer.BlockCopy(clientChallenge, 0, challengeData, 0, 8);
        Buffer.BlockCopy(serverChallenge, 0, challengeData, 8, 8);

        if (useAes)
        {
            // AES: HMAC-SHA256(sharedSecret, clientChallenge || serverChallenge), take first 16 bytes
            using var hmac = new HMACSHA256(sharedSecret);
            var hash = hmac.ComputeHash(challengeData);
            var sessionKey = new byte[16];
            Buffer.BlockCopy(hash, 0, sessionKey, 0, 16);
            return sessionKey;
        }
        else
        {
            // Non-AES: HMAC-MD5(sharedSecret, clientChallenge || serverChallenge)
            using var hmac = new HMACMD5(sharedSecret);
            return hmac.ComputeHash(challengeData);
        }
    }

    /// <summary>
    /// Computes a NETLOGON_CREDENTIAL from an 8-byte input and session key,
    /// per MS-NRPC 3.1.4.4.2 (ComputeNetlogonCredential).
    /// </summary>
    /// <param name="input">8-byte input value (challenge).</param>
    /// <param name="sessionKey">16-byte session key.</param>
    /// <param name="useAes">True if AES negotiation flag is set.</param>
    /// <returns>8-byte credential.</returns>
    public static byte[] ComputeNetlogonCredential(byte[] input, byte[] sessionKey, bool useAes)
    {
        if (useAes)
        {
            // AES-CFB8 encrypt input with session key and zero IV
            var iv = new byte[16]; // zero IV
            using var aes = Aes.Create();
            aes.Key = sessionKey;
            aes.IV = iv;
            aes.Mode = CipherMode.CFB;
            aes.FeedbackSize = 8;
            aes.Padding = PaddingMode.None;

            using var encryptor = aes.CreateEncryptor();
            var result = new byte[8];
            encryptor.TransformBlock(input, 0, 8, result, 0);
            return result;
        }
        else
        {
            // DES-ECB based computation (MS-NRPC 3.1.4.4.1):
            // Split session key into two 7-byte DES keys, encrypt each half of input
            var desKey1 = DeriveDesKey(sessionKey, 0);
            var desKey2 = DeriveDesKey(sessionKey, 7);

            var output = new byte[8];

            // First DES: encrypt input[0..7] with key1, XOR result into input, encrypt with key2
            var temp = DesEcbEncrypt(desKey1, input);
            for (int i = 0; i < 8; i++)
                temp[i] ^= 0; // first pass, no XOR needed

            // Actually: Cred = DES(k2, DES(k1, input))
            // Per MS-NRPC: output = DES_ECB(k1, input) then output = DES_ECB(k2, output)
            var pass1 = DesEcbEncrypt(desKey1, input);
            var pass2 = DesEcbEncrypt(desKey2, pass1);
            Buffer.BlockCopy(pass2, 0, output, 0, 8);

            return output;
        }
    }

    /// <summary>
    /// Verifies a NETLOGON_AUTHENTICATOR by checking the credential and advancing the timestamp.
    /// Returns a return-authenticator on success, or null on failure.
    /// </summary>
    /// <param name="authCredential">8-byte credential from the authenticator.</param>
    /// <param name="authTimestamp">Timestamp from the authenticator.</param>
    /// <returns>The 8-byte return authenticator credential, or null if verification fails.</returns>
    public byte[] VerifyAuthenticator(byte[] authCredential, uint authTimestamp)
    {
        bool useAes = (NegotiateFlags & NrpcConstants.NegotiateAes) != 0;

        // The client's authenticator credential should be:
        // Credential = ComputeNetlogonCredential(ClientCredential.Data + TimeStamp, SessionKey)
        // We verify by computing the expected credential and comparing.
        var expectedInput = new byte[8];
        Buffer.BlockCopy(ClientCredential.Data, 0, expectedInput, 0, 8);

        // Add the timestamp to the credential (modular addition of first 4 bytes)
        uint credValue = BitConverter.ToUInt32(expectedInput, 0);
        credValue += authTimestamp;
        BitConverter.GetBytes(credValue).CopyTo(expectedInput, 0);

        var expectedCredential = ComputeNetlogonCredential(expectedInput, SessionKey, useAes);

        if (!CryptographicOperations.FixedTimeEquals(authCredential, expectedCredential))
            return null;

        // Update the stored client credential to the new value
        Buffer.BlockCopy(expectedInput, 0, ClientCredential.Data, 0, 8);

        // Compute return authenticator: credential + 1
        var returnInput = new byte[8];
        Buffer.BlockCopy(ClientCredential.Data, 0, returnInput, 0, 8);
        uint returnValue = BitConverter.ToUInt32(returnInput, 0);
        returnValue += 1;
        BitConverter.GetBytes(returnValue).CopyTo(returnInput, 0);

        var returnCred = ComputeNetlogonCredential(returnInput, SessionKey, useAes);

        // Advance stored client credential
        Buffer.BlockCopy(returnInput, 0, ClientCredential.Data, 0, 8);

        return returnCred;
    }

    /// <summary>
    /// Decrypts an NL_TRUST_PASSWORD structure (516 bytes: 512 encrypted + 4 byte length)
    /// using the session key.
    /// </summary>
    /// <param name="encryptedBuffer">516-byte encrypted password buffer.</param>
    /// <returns>The decrypted Unicode password string.</returns>
    public string DecryptNewPassword(byte[] encryptedBuffer)
    {
        if (encryptedBuffer.Length != 516)
            throw new ArgumentException("NL_TRUST_PASSWORD must be 516 bytes.");

        bool useAes = (NegotiateFlags & NrpcConstants.NegotiateAes) != 0;

        byte[] decrypted;
        if (useAes)
        {
            // AES-CFB8 decrypt with session key and zero IV
            var iv = new byte[16];
            using var aes = Aes.Create();
            aes.Key = SessionKey;
            aes.IV = iv;
            aes.Mode = CipherMode.CFB;
            aes.FeedbackSize = 8;
            aes.Padding = PaddingMode.None;

            using var decryptor = aes.CreateDecryptor();
            decrypted = new byte[516];
            decryptor.TransformBlock(encryptedBuffer, 0, 516, decrypted, 0);
        }
        else
        {
            // RC4 decrypt with session key
            decrypted = Rc4Transform(SessionKey, encryptedBuffer);
        }

        // Last 4 bytes = password length in bytes (little-endian)
        int passwordLength = BitConverter.ToInt32(decrypted, 512);
        if (passwordLength < 0 || passwordLength > 512)
            throw new InvalidOperationException("Invalid decrypted password length.");

        // Password is stored at the end of the 512-byte buffer, right-justified
        int offset = 512 - passwordLength;
        return System.Text.Encoding.Unicode.GetString(decrypted, offset, passwordLength);
    }

    // ---- Private helpers ----

    /// <summary>
    /// Derives an 8-byte DES key from 7 bytes of key material per the MS-NRPC spec.
    /// </summary>
    private static byte[] DeriveDesKey(byte[] input, int offset)
    {
        var key = new byte[8];
        key[0] = (byte)(input[offset + 0] >> 1);
        key[1] = (byte)(((input[offset + 0] & 0x01) << 6) | (input[offset + 1] >> 2));
        key[2] = (byte)(((input[offset + 1] & 0x03) << 5) | (input[offset + 2] >> 3));
        key[3] = (byte)(((input[offset + 2] & 0x07) << 4) | (input[offset + 3] >> 4));
        key[4] = (byte)(((input[offset + 3] & 0x0F) << 3) | (input[offset + 4] >> 5));
        key[5] = (byte)(((input[offset + 4] & 0x1F) << 2) | (input[offset + 5] >> 6));
        key[6] = (byte)(((input[offset + 5] & 0x3F) << 1) | (input[offset + 6] >> 7));
        key[7] = (byte)(input[offset + 6] & 0x7F);

        // Set parity bits
        for (int i = 0; i < 8; i++)
        {
            key[i] = (byte)((key[i] << 1) & 0xFE);
            // Add odd parity
            int parity = 0;
            for (int bit = 0; bit < 8; bit++)
                parity += (key[i] >> bit) & 1;
            if (parity % 2 == 0)
                key[i] |= 1;
        }

        return key;
    }

    /// <summary>
    /// Performs a single-block DES-ECB encryption.
    /// </summary>
    private static byte[] DesEcbEncrypt(byte[] key, byte[] data)
    {
        using var des = DES.Create();
        des.Key = key;
        des.Mode = CipherMode.ECB;
        des.Padding = PaddingMode.None;

        using var encryptor = des.CreateEncryptor();
        var result = new byte[8];
        encryptor.TransformBlock(data, 0, 8, result, 0);
        return result;
    }

    /// <summary>
    /// RC4 stream cipher transform (encrypt/decrypt are the same operation).
    /// </summary>
    private static byte[] Rc4Transform(byte[] key, byte[] data)
    {
        // Initialize S-box
        var s = new byte[256];
        for (int i = 0; i < 256; i++)
            s[i] = (byte)i;

        int j = 0;
        for (int i = 0; i < 256; i++)
        {
            j = (j + s[i] + key[i % key.Length]) & 0xFF;
            (s[i], s[j]) = (s[j], s[i]);
        }

        // Transform
        var output = new byte[data.Length];
        int x = 0, y = 0;
        for (int k = 0; k < data.Length; k++)
        {
            x = (x + 1) & 0xFF;
            y = (y + s[x]) & 0xFF;
            (s[x], s[y]) = (s[y], s[x]);
            output[k] = (byte)(data[k] ^ s[(s[x] + s[y]) & 0xFF]);
        }

        return output;
    }
}
