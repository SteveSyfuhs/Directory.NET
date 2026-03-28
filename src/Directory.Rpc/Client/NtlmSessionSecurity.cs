using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Directory.Rpc.Client;

/// <summary>
/// NTLM session security for RPC packet signing and sealing per MS-NLMP §3.4.4.
/// Supports Extended Session Security (ESS) which is required by modern Windows DCs.
/// </summary>
public class NtlmSessionSecurity
{
    private const uint NtlmsspNegotiateExtendedSessionSecurity = 0x00080000;
    private const uint NtlmsspNegotiate128 = 0x20000000;
    private const uint NtlmsspNegotiate56 = 0x80000000;
    private const uint NtlmsspNegotiateSeal = 0x00000020;

    private readonly byte[] _clientSigningKey;
    private readonly byte[] _serverSigningKey;
    private readonly object _sendLock = new();
    private readonly object _recvLock = new();

    private ICryptoTransform _clientSealingHandle;
    private ICryptoTransform _serverSealingHandle;
    private uint _sendSeqNum;
    private uint _recvSeqNum;
    private readonly uint _negotiateFlags;

    /// <summary>
    /// Creates a new NTLM session security instance.
    /// </summary>
    /// <param name="exportedSessionKey">The exported session key from NTLM authentication.</param>
    /// <param name="negotiateFlags">The negotiate flags agreed upon during authentication.</param>
    public NtlmSessionSecurity(byte[] exportedSessionKey, uint negotiateFlags)
    {
        _negotiateFlags = negotiateFlags;

        if ((negotiateFlags & NtlmsspNegotiateExtendedSessionSecurity) != 0)
        {
            // Determine sealing key length based on negotiate flags
            int sealKeyLen = DetermineSealKeyLength(negotiateFlags);

            // Derive signing keys (always full length)
            _clientSigningKey = DeriveKey(exportedSessionKey,
                "session key to client-to-server signing key magic constant\0");
            _serverSigningKey = DeriveKey(exportedSessionKey,
                "session key to server-to-client signing key magic constant\0");

            // Derive sealing keys (may be truncated)
            byte[] clientSealingKey = DeriveSealingKey(exportedSessionKey,
                "session key to client-to-server sealing key magic constant\0", sealKeyLen);
            byte[] serverSealingKey = DeriveSealingKey(exportedSessionKey,
                "session key to server-to-client sealing key magic constant\0", sealKeyLen);

            // Initialize RC4 cipher states
            _clientSealingHandle = CreateRc4(clientSealingKey);
            _serverSealingHandle = CreateRc4(serverSealingKey);
        }
        else
        {
            // Legacy mode - use session key directly
            _clientSigningKey = exportedSessionKey;
            _serverSigningKey = exportedSessionKey;
            _clientSealingHandle = CreateRc4(exportedSessionKey);
            _serverSealingHandle = CreateRc4(exportedSessionKey);
        }
    }

    /// <summary>
    /// Signs a message producing a 16-byte NTLM signature for use as an auth verifier.
    /// Per MS-NLMP §3.4.4.2 - MAC with Extended Session Security.
    /// </summary>
    public byte[] Sign(byte[] message)
    {
        lock (_sendLock)
        {
            uint seqNum = _sendSeqNum++;
            return ComputeSignature(message, seqNum, _clientSigningKey, _clientSealingHandle, seal: false);
        }
    }

    /// <summary>
    /// Seals (encrypts) a message and produces the 16-byte signature.
    /// Per MS-NLMP §3.4.4.2 - SEAL with Extended Session Security.
    /// The message is encrypted with RC4 and the checksum in the signature is also RC4-encrypted.
    /// </summary>
    public (byte[] encryptedMessage, byte[] signature) Seal(byte[] message)
    {
        lock (_sendLock)
        {
            uint seqNum = _sendSeqNum++;

            // Encrypt the message with RC4
            byte[] encrypted = Rc4Transform(_clientSealingHandle, message);

            // Compute signature with seal=true (RC4-encrypt the checksum)
            byte[] signature = ComputeSignature(message, seqNum, _clientSigningKey, _clientSealingHandle, seal: true);

            return (encrypted, signature);
        }
    }

    /// <summary>
    /// Unseals (decrypts) a message and verifies its signature.
    /// </summary>
    public byte[] Unseal(byte[] encryptedMessage, byte[] signature)
    {
        lock (_recvLock)
        {
            uint seqNum = _recvSeqNum++;

            // Decrypt the message
            byte[] decrypted = Rc4Transform(_serverSealingHandle, encryptedMessage);

            // Verify the signature
            byte[] expectedSig = ComputeSignature(decrypted, seqNum, _serverSigningKey, _serverSealingHandle, seal: true);

            if (!CryptographicOperations.FixedTimeEquals(signature, expectedSig))
            {
                throw new CryptographicException("NTLM signature verification failed on unsealed message.");
            }

            return decrypted;
        }
    }

    /// <summary>
    /// Verifies a signature on a plaintext message (for integrity-only, no sealing).
    /// </summary>
    public bool Verify(byte[] message, byte[] signature)
    {
        lock (_recvLock)
        {
            uint seqNum = _recvSeqNum++;
            byte[] expectedSig = ComputeSignature(message, seqNum, _serverSigningKey, _serverSealingHandle, seal: false);
            return CryptographicOperations.FixedTimeEquals(signature, expectedSig);
        }
    }

    /// <summary>
    /// Computes an NTLM MAC (signature) per MS-NLMP §3.4.4.2.
    /// With Extended Session Security:
    ///   Version = 0x01000000 (4 bytes)
    ///   Checksum = HMAC_MD5(SigningKey, SeqNum || Message)[0..7] (8 bytes)
    ///   SeqNum (4 bytes LE)
    /// If sealing, the Checksum bytes are RC4-encrypted with the sealing handle.
    /// </summary>
    private byte[] ComputeSignature(byte[] message, uint seqNum, byte[] signingKey,
        ICryptoTransform sealingHandle, bool seal)
    {
        if ((_negotiateFlags & NtlmsspNegotiateExtendedSessionSecurity) != 0)
        {
            return ComputeEssSignature(message, seqNum, signingKey, sealingHandle, seal);
        }
        else
        {
            return ComputeLegacySignature(message, seqNum, sealingHandle);
        }
    }

    private static byte[] ComputeEssSignature(byte[] message, uint seqNum, byte[] signingKey,
        ICryptoTransform sealingHandle, bool seal)
    {
        // Compute HMAC_MD5(SigningKey, SeqNum || Message)
        byte[] seqNumBytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(seqNumBytes, seqNum);

        byte[] hmacInput = new byte[4 + message.Length];
        seqNumBytes.CopyTo(hmacInput, 0);
        message.CopyTo(hmacInput, 4);

        byte[] hmacFull;
        using (var hmac = new HMACMD5(signingKey))
        {
            hmacFull = hmac.ComputeHash(hmacInput);
        }

        // Truncate to first 8 bytes
        byte[] checksum = new byte[8];
        Array.Copy(hmacFull, 0, checksum, 0, 8);

        if (seal)
        {
            // RC4-encrypt the checksum
            checksum = Rc4Transform(sealingHandle, checksum);
        }

        // Build 16-byte signature: Version(4) + Checksum(8) + SeqNum(4)
        byte[] signature = new byte[16];
        // Version = 0x01000000
        BinaryPrimitives.WriteUInt32LittleEndian(signature.AsSpan(0), 0x00000001);
        checksum.CopyTo(signature, 4);
        seqNumBytes.CopyTo(signature, 12);

        if (seal)
        {
            // RC4-encrypt the SeqNum
            byte[] encSeqNum = Rc4Transform(sealingHandle, seqNumBytes);
            encSeqNum.CopyTo(signature, 12);
        }

        return signature;
    }

    /// <summary>
    /// Legacy signature computation (without ESS). Uses CRC32 + RC4.
    /// </summary>
    private static byte[] ComputeLegacySignature(byte[] message, uint seqNum, ICryptoTransform sealingHandle)
    {
        byte[] signature = new byte[16];

        // Version = 0x01000000
        BinaryPrimitives.WriteUInt32LittleEndian(signature.AsSpan(0), 0x00000001);

        // CRC32 of the message
        uint crc = Crc32(message);
        byte[] crcBytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(crcBytes, crc);

        // SeqNum
        byte[] seqBytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(seqBytes, seqNum);

        // RC4-encrypt CRC32 (bytes 4-7) and SeqNum (bytes 12-15)
        // For legacy, bytes 4-7 = RC4(CRC32), bytes 8-11 = 0, bytes 12-15 = RC4(SeqNum)
        byte[] toEncrypt = new byte[8];
        crcBytes.CopyTo(toEncrypt, 0);
        seqBytes.CopyTo(toEncrypt, 4);
        byte[] encrypted = Rc4Transform(sealingHandle, toEncrypt);

        encrypted.AsSpan(0, 4).CopyTo(signature.AsSpan(4));
        signature.AsSpan(8, 4).Clear(); // reserved zeros
        encrypted.AsSpan(4, 4).CopyTo(signature.AsSpan(12));

        return signature;
    }

    private static int DetermineSealKeyLength(uint flags)
    {
        if ((flags & NtlmsspNegotiate128) != 0)
            return 16; // 128-bit
        if ((flags & NtlmsspNegotiate56) != 0)
            return 7;  // 56-bit
        return 5;      // 40-bit
    }

    /// <summary>
    /// Derives a key using MD5(SessionKey + magicConstant).
    /// </summary>
    private static byte[] DeriveKey(byte[] sessionKey, string magicConstant)
    {
        byte[] magic = Encoding.ASCII.GetBytes(magicConstant);
        byte[] input = new byte[sessionKey.Length + magic.Length];
        sessionKey.CopyTo(input, 0);
        magic.CopyTo(input, sessionKey.Length);
        return MD5.HashData(input);
    }

    /// <summary>
    /// Derives a sealing key, truncating the session key to the specified length before hashing.
    /// </summary>
    private static byte[] DeriveSealingKey(byte[] sessionKey, string magicConstant, int keyLength)
    {
        byte[] truncatedKey = new byte[keyLength];
        Array.Copy(sessionKey, truncatedKey, Math.Min(sessionKey.Length, keyLength));

        byte[] magic = Encoding.ASCII.GetBytes(magicConstant);
        byte[] input = new byte[truncatedKey.Length + magic.Length];
        truncatedKey.CopyTo(input, 0);
        magic.CopyTo(input, truncatedKey.Length);
        return MD5.HashData(input);
    }

    /// <summary>
    /// Creates an RC4 cipher transform from a key. Uses the managed RC4 implementation
    /// since .NET does not include RC4 in its standard crypto library.
    /// </summary>
    private static ICryptoTransform CreateRc4(byte[] key)
    {
        return new Rc4CipherTransform(key);
    }

    /// <summary>
    /// Transforms data through an RC4 cipher (stateful - maintains stream position).
    /// </summary>
    private static byte[] Rc4Transform(ICryptoTransform transform, byte[] data)
    {
        byte[] output = new byte[data.Length];
        transform.TransformBlock(data, 0, data.Length, output, 0);
        return output;
    }

    /// <summary>
    /// CRC32 computation for legacy NTLM signatures.
    /// </summary>
    private static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
            {
                if ((crc & 1) != 0)
                    crc = (crc >> 1) ^ 0xEDB88320;
                else
                    crc >>= 1;
            }
        }
        return ~crc;
    }

    /// <summary>
    /// Managed RC4 stream cipher implementation.
    /// RC4 is not available in .NET's standard crypto library but is required for NTLM session security.
    /// </summary>
    private sealed class Rc4CipherTransform : ICryptoTransform
    {
        private readonly byte[] _s = new byte[256];
        private int _i;
        private int _j;

        public Rc4CipherTransform(byte[] key)
        {
            // KSA (Key Scheduling Algorithm)
            for (int i = 0; i < 256; i++)
                _s[i] = (byte)i;

            int j = 0;
            for (int i = 0; i < 256; i++)
            {
                j = (j + _s[i] + key[i % key.Length]) & 0xFF;
                (_s[i], _s[j]) = (_s[j], _s[i]);
            }
        }

        public int InputBlockSize => 1;
        public int OutputBlockSize => 1;
        public bool CanTransformMultipleBlocks => true;
        public bool CanReuseTransform => false;

        public int TransformBlock(byte[] inputBuffer, int inputOffset, int inputCount,
            byte[] outputBuffer, int outputOffset)
        {
            for (int k = 0; k < inputCount; k++)
            {
                _i = (_i + 1) & 0xFF;
                _j = (_j + _s[_i]) & 0xFF;
                (_s[_i], _s[_j]) = (_s[_j], _s[_i]);
                byte keystreamByte = _s[(_s[_i] + _s[_j]) & 0xFF];
                outputBuffer[outputOffset + k] = (byte)(inputBuffer[inputOffset + k] ^ keystreamByte);
            }
            return inputCount;
        }

        public byte[] TransformFinalBlock(byte[] inputBuffer, int inputOffset, int inputCount)
        {
            byte[] output = new byte[inputCount];
            TransformBlock(inputBuffer, inputOffset, inputCount, output, 0);
            return output;
        }

        public void Dispose() { }
    }
}
