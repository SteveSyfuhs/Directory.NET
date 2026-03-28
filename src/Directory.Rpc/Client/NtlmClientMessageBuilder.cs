using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Directory.Rpc.Client;

/// <summary>
/// Generates NTLM client messages for the RPC authentication handshake.
/// Implements the client side of NTLMv2 authentication per MS-NLMP.
/// </summary>
public class NtlmClientMessageBuilder
{
    private static readonly byte[] NtlmSignature = "NTLMSSP\0"u8.ToArray();

    /// <summary>
    /// The exported session key derived during authentication. Available after BuildAuthenticateMessage.
    /// When NTLMSSP_NEGOTIATE_KEY_EXCH is negotiated, this is a random 16-byte key encrypted
    /// with the session base key and included in the Type 3 message.
    /// </summary>
    public byte[] ExportedSessionKey { get; private set; } = [];

    /// <summary>
    /// The negotiate flags from the Type 2 challenge, used to create NtlmSessionSecurity.
    /// </summary>
    public uint NegotiateFlags { get; private set; }

    // NTLM negotiate flags
    private const uint NtlmsspNegotiateUnicode = 0x00000001;
    private const uint NtlmsspNegotiateNtlm = 0x00000200;
    private const uint NtlmsspRequestTarget = 0x00000004;
    private const uint NtlmsspNegotiateSeal = 0x00000020;
    private const uint NtlmsspNegotiateSign = 0x00000010;
    private const uint NtlmsspNegotiateExtendedSessionSecurity = 0x00080000;
    private const uint NtlmsspNegotiateKeyExch = 0x40000000;
    private const uint NtlmsspNegotiate128 = 0x20000000;

    private const uint DefaultNegotiateFlags =
        NtlmsspNegotiateUnicode |
        NtlmsspNegotiateNtlm |
        NtlmsspRequestTarget |
        NtlmsspNegotiateSeal |
        NtlmsspNegotiateSign |
        NtlmsspNegotiateExtendedSessionSecurity |
        NtlmsspNegotiateKeyExch |
        NtlmsspNegotiate128;

    /// <summary>
    /// Builds an NTLM Type 1 (Negotiate) message.
    /// </summary>
    public byte[] BuildNegotiateMessage()
    {
        // Type 1 message layout:
        // "NTLMSSP\0" (8 bytes)
        // Type = 1 (uint32)
        // NegotiateFlags (uint32)
        // DomainNameFields: len(2) + maxLen(2) + offset(4) = all zeros
        // WorkstationFields: len(2) + maxLen(2) + offset(4) = all zeros
        // Total: 8 + 4 + 4 + 8 + 8 = 32 bytes

        var message = new byte[32];
        var span = message.AsSpan();

        // Signature
        NtlmSignature.CopyTo(span);

        // Type = 1
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(8), 1);

        // Negotiate flags
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(12), DefaultNegotiateFlags);

        // DomainNameFields and WorkstationFields are all zeros (already default)

        return message;
    }

    /// <summary>
    /// Parses an NTLM Type 2 (Challenge) message from the server.
    /// Returns the 8-byte server challenge, negotiate flags, and optional target name.
    /// </summary>
    public (byte[] ServerChallenge, uint NegotiateFlags, string TargetName) ParseChallengeMessage(byte[] type2Message)
    {
        if (type2Message.Length < 32)
            throw new ArgumentException($"NTLM Type 2 message too short: {type2Message.Length} bytes, minimum 32.");

        var span = type2Message.AsSpan();

        // Verify signature
        if (!span.Slice(0, 8).SequenceEqual(NtlmSignature))
            throw new ArgumentException("Invalid NTLM signature in Type 2 message.");

        // Verify type = 2
        uint messageType = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(8));
        if (messageType != 2)
            throw new ArgumentException($"Expected NTLM Type 2, got type {messageType}.");

        // Target name fields: offset 12
        ushort targetNameLen = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(12));
        uint targetNameOffset = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(16));

        // Negotiate flags: offset 20
        uint negotiateFlags = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(20));

        // Server challenge: offset 24, 8 bytes
        var serverChallenge = span.Slice(24, 8).ToArray();

        // Parse target name if present
        string targetName = null;
        if (targetNameLen > 0 && targetNameOffset + targetNameLen <= type2Message.Length)
        {
            targetName = Encoding.Unicode.GetString(span.Slice((int)targetNameOffset, targetNameLen));
        }

        return (serverChallenge, negotiateFlags, targetName);
    }

    /// <summary>
    /// Builds an NTLM Type 3 (Authenticate) message using NTLMv2 authentication.
    /// Returns the Type 3 message bytes and the derived session key.
    /// </summary>
    public (byte[] Type3Message, byte[] SessionKey) BuildAuthenticateMessage(
        string username, string domain, byte[] ntHash,
        byte[] serverChallenge, uint negotiateFlags)
    {
        // Compute NTLMv2Hash = HMAC-MD5(NTHash, UPPERCASE(username) + domain)
        byte[] identityBytes = Encoding.Unicode.GetBytes(username.ToUpperInvariant() + domain);
        byte[] ntlmv2Hash;
        using (var hmac = new HMACMD5(ntHash))
        {
            ntlmv2Hash = hmac.ComputeHash(identityBytes);
        }

        // Build the NTLMv2 client challenge blob
        byte[] clientChallenge = RandomNumberGenerator.GetBytes(8);
        long timestamp = DateTime.UtcNow.ToFileTimeUtc();
        byte[] blob = BuildNtlmv2Blob(clientChallenge, timestamp, domain);

        // Compute NtProofStr = HMAC-MD5(NTLMv2Hash, ServerChallenge + Blob)
        byte[] ntProofStr;
        byte[] challengePlusBlob = new byte[serverChallenge.Length + blob.Length];
        serverChallenge.CopyTo(challengePlusBlob, 0);
        blob.CopyTo(challengePlusBlob, serverChallenge.Length);

        using (var hmac = new HMACMD5(ntlmv2Hash))
        {
            ntProofStr = hmac.ComputeHash(challengePlusBlob);
        }

        // NtResponse = NtProofStr (16 bytes) + Blob
        byte[] ntResponse = new byte[ntProofStr.Length + blob.Length];
        ntProofStr.CopyTo(ntResponse, 0);
        blob.CopyTo(ntResponse, ntProofStr.Length);

        // LM response: for NTLMv2, use HMAC-MD5(NTLMv2Hash, ServerChallenge + ClientChallenge) + ClientChallenge
        byte[] lmChallengeData = new byte[serverChallenge.Length + clientChallenge.Length];
        serverChallenge.CopyTo(lmChallengeData, 0);
        clientChallenge.CopyTo(lmChallengeData, serverChallenge.Length);

        byte[] lmProof;
        using (var hmac = new HMACMD5(ntlmv2Hash))
        {
            lmProof = hmac.ComputeHash(lmChallengeData);
        }

        byte[] lmResponse = new byte[24];
        lmProof.AsSpan(0, 16).CopyTo(lmResponse);
        clientChallenge.CopyTo(lmResponse, 16);

        // SessionBaseKey = HMAC-MD5(NTLMv2Hash, NtProofStr)
        byte[] sessionBaseKey;
        using (var hmac = new HMACMD5(ntlmv2Hash))
        {
            sessionBaseKey = hmac.ComputeHash(ntProofStr);
        }

        // Handle key exchange: if NTLMSSP_NEGOTIATE_KEY_EXCH is set,
        // generate a random 16-byte exported session key and RC4-encrypt it with the session base key.
        byte[] exportedSessionKey;
        byte[] encryptedRandomSessionKey;
        if ((negotiateFlags & NtlmsspNegotiateKeyExch) != 0)
        {
            exportedSessionKey = RandomNumberGenerator.GetBytes(16);
            // RC4-encrypt the exported session key with the session base key
            encryptedRandomSessionKey = Rc4Encrypt(sessionBaseKey, exportedSessionKey);
        }
        else
        {
            exportedSessionKey = sessionBaseKey;
            encryptedRandomSessionKey = [];
        }

        ExportedSessionKey = exportedSessionKey;
        NegotiateFlags = negotiateFlags;

        // Encode domain, username, workstation as UTF-16LE
        byte[] domainBytes = Encoding.Unicode.GetBytes(domain);
        byte[] usernameBytes = Encoding.Unicode.GetBytes(username);
        byte[] workstationBytes = Encoding.Unicode.GetBytes(Environment.MachineName);

        // Build the Type 3 message
        byte[] type3Message = BuildType3Pdu(
            lmResponse, ntResponse, domainBytes, usernameBytes, workstationBytes,
            negotiateFlags, encryptedRandomSessionKey);

        return (type3Message, exportedSessionKey);
    }

    /// <summary>
    /// Builds the NTLMv2 client challenge blob (also called "temp" in MS-NLMP).
    /// </summary>
    private static byte[] BuildNtlmv2Blob(byte[] clientChallenge, long timestamp, string domain)
    {
        using var ms = new MemoryStream();
        Span<byte> buf = stackalloc byte[8];

        // RespType = 1
        ms.WriteByte(1);
        // HiRespType = 1
        ms.WriteByte(1);
        // Reserved1 = 0 (ushort)
        ms.WriteByte(0);
        ms.WriteByte(0);
        // Reserved2 = 0 (uint)
        BinaryPrimitives.WriteUInt32LittleEndian(buf, 0);
        ms.Write(buf.Slice(0, 4));
        // TimeStamp (FILETIME, int64)
        BinaryPrimitives.WriteInt64LittleEndian(buf, timestamp);
        ms.Write(buf);
        // ClientChallenge (8 bytes)
        ms.Write(clientChallenge);
        // Reserved3 = 0 (uint)
        BinaryPrimitives.WriteUInt32LittleEndian(buf, 0);
        ms.Write(buf.Slice(0, 4));

        // AV_PAIRS
        // MsvAvNbDomainName (type=2)
        WriteAvPair(ms, 2, Encoding.Unicode.GetBytes(domain));
        // MsvAvNbComputerName (type=1)
        WriteAvPair(ms, 1, Encoding.Unicode.GetBytes(Environment.MachineName));
        // MsvAvDnsDomainName (type=4)
        WriteAvPair(ms, 4, Encoding.Unicode.GetBytes(domain.ToLowerInvariant()));
        // MsvAvTimestamp (type=7)
        byte[] timestampBytes = new byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(timestampBytes, timestamp);
        WriteAvPair(ms, 7, timestampBytes);
        // MsvAvEOL (type=0, length=0)
        WriteAvPair(ms, 0, []);

        return ms.ToArray();
    }

    /// <summary>
    /// Writes an AV_PAIR structure: AvId (ushort) + AvLen (ushort) + Value (AvLen bytes).
    /// </summary>
    private static void WriteAvPair(MemoryStream ms, ushort avId, byte[] value)
    {
        Span<byte> buf = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(buf, avId);
        ms.Write(buf);
        BinaryPrimitives.WriteUInt16LittleEndian(buf, (ushort)value.Length);
        ms.Write(buf);
        if (value.Length > 0)
        {
            ms.Write(value);
        }
    }

    /// <summary>
    /// Builds the raw NTLM Type 3 (Authenticate) message PDU.
    /// </summary>
    private static byte[] BuildType3Pdu(
        byte[] lmResponse, byte[] ntResponse,
        byte[] domainBytes, byte[] usernameBytes, byte[] workstationBytes,
        uint negotiateFlags, byte[] encryptedRandomSessionKey)
    {
        // Type 3 header layout:
        // Offset 0:  Signature (8)
        // Offset 8:  Type (4)
        // Offset 12: LmChallengeResponse fields: Len(2) MaxLen(2) Offset(4) = 8
        // Offset 20: NtChallengeResponse fields: 8
        // Offset 28: DomainName fields: 8
        // Offset 36: UserName fields: 8
        // Offset 44: Workstation fields: 8
        // Offset 52: EncryptedRandomSessionKey fields: 8
        // Offset 60: NegotiateFlags: 4
        // Header total: 64 bytes
        // Payload starts at offset 64

        const int headerSize = 64;

        int payloadOffset = headerSize;
        int lmOffset = payloadOffset;
        int ntOffset = lmOffset + lmResponse.Length;
        int domainOffset = ntOffset + ntResponse.Length;
        int userOffset = domainOffset + domainBytes.Length;
        int workstationOffset = userOffset + usernameBytes.Length;
        int sessionKeyOffset = workstationOffset + workstationBytes.Length;
        int totalLength = sessionKeyOffset + encryptedRandomSessionKey.Length;

        var message = new byte[totalLength];
        var span = message.AsSpan();

        // Signature
        NtlmSignature.CopyTo(span);

        // Type = 3
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(8), 3);

        // LmChallengeResponseFields
        WriteSecurityBufferFields(span.Slice(12), (ushort)lmResponse.Length, (uint)lmOffset);

        // NtChallengeResponseFields
        WriteSecurityBufferFields(span.Slice(20), (ushort)ntResponse.Length, (uint)ntOffset);

        // DomainNameFields
        WriteSecurityBufferFields(span.Slice(28), (ushort)domainBytes.Length, (uint)domainOffset);

        // UserNameFields
        WriteSecurityBufferFields(span.Slice(36), (ushort)usernameBytes.Length, (uint)userOffset);

        // WorkstationFields
        WriteSecurityBufferFields(span.Slice(44), (ushort)workstationBytes.Length, (uint)workstationOffset);

        // EncryptedRandomSessionKeyFields
        WriteSecurityBufferFields(span.Slice(52), (ushort)encryptedRandomSessionKey.Length, (uint)sessionKeyOffset);

        // NegotiateFlags
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(60), negotiateFlags);

        // Payload
        lmResponse.CopyTo(span.Slice(lmOffset));
        ntResponse.CopyTo(span.Slice(ntOffset));
        domainBytes.CopyTo(span.Slice(domainOffset));
        usernameBytes.CopyTo(span.Slice(userOffset));
        workstationBytes.CopyTo(span.Slice(workstationOffset));
        if (encryptedRandomSessionKey.Length > 0)
        {
            encryptedRandomSessionKey.CopyTo(span.Slice(sessionKeyOffset));
        }

        return message;
    }

    /// <summary>
    /// Writes an NTLM security buffer field: Length (ushort) + MaxLength (ushort) + Offset (uint32).
    /// </summary>
    private static void WriteSecurityBufferFields(Span<byte> dest, ushort length, uint offset)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(dest, length);
        BinaryPrimitives.WriteUInt16LittleEndian(dest.Slice(2), length); // MaxLength = Length
        BinaryPrimitives.WriteUInt32LittleEndian(dest.Slice(4), offset);
    }

    /// <summary>
    /// RC4-encrypts data with the given key. Used for encrypting the exported session key
    /// during key exchange (NTLMSSP_NEGOTIATE_KEY_EXCH).
    /// </summary>
    private static byte[] Rc4Encrypt(byte[] key, byte[] data)
    {
        // RC4 KSA
        byte[] s = new byte[256];
        for (int i = 0; i < 256; i++) s[i] = (byte)i;
        int j = 0;
        for (int i = 0; i < 256; i++)
        {
            j = (j + s[i] + key[i % key.Length]) & 0xFF;
            (s[i], s[j]) = (s[j], s[i]);
        }

        // RC4 PRGA
        byte[] output = new byte[data.Length];
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
