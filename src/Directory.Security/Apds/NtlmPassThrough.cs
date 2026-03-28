using System.Security.Cryptography;
using System.Text;
using Directory.Core.Models;
using Microsoft.Extensions.Logging;

namespace Directory.Security.Apds;

/// <summary>
/// Result of an NTLM response validation.
/// </summary>
public class NtlmValidationResult
{
    /// <summary>
    /// Whether the NTLM response was valid.
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// The derived session key from NTLM authentication.
    /// This is HMAC-MD5(NTLMv2Hash, NTProofStr) for NTLMv2.
    /// </summary>
    public byte[] SessionKey { get; set; }
}

/// <summary>
/// NTLM supplemental credential data that can be included in a PAC
/// or returned as supplemental credential info.
/// Reference: MS-NLMP 2.2.2.6.
/// </summary>
public class NtlmSupplementalCredential
{
    /// <summary>
    /// Version of the structure (always 0).
    /// </summary>
    public uint Version { get; set; }

    /// <summary>
    /// Flags (0x00000000).
    /// </summary>
    public uint Flags { get; set; }

    /// <summary>
    /// The LM hash (16 bytes). Usually zeroed for NTLMv2-only environments.
    /// </summary>
    public byte[] LmPassword { get; set; } = new byte[16];

    /// <summary>
    /// The NT hash (MD4 of UTF-16LE password, 16 bytes).
    /// </summary>
    public byte[] NtPassword { get; set; } = new byte[16];
}

/// <summary>
/// MSV1_0 validation info returned on successful NTLM pass-through authentication.
/// This augments the NETLOGON_VALIDATION with NTLM-specific session key data.
/// Reference: MS-APDS 3.1.5.
/// </summary>
public class Msv10ValidationInfo
{
    /// <summary>
    /// The validation info structure containing user/group information.
    /// </summary>
    public NetlogonValidationSamInfo4 ValidationInfo { get; set; } = new();

    /// <summary>
    /// The NTLM user session key derived during authentication.
    /// </summary>
    public byte[] UserSessionKey { get; set; } = new byte[16];

    /// <summary>
    /// Supplemental credentials (NT hash, LM hash) for credential caching.
    /// </summary>
    public NtlmSupplementalCredential SupplementalCredential { get; set; }
}

/// <summary>
/// Implements NTLM pass-through authentication as defined in MS-APDS section 3.1.5.2.
/// Validates NTLMv2 challenge/response pairs against stored NT hashes, generates
/// session keys, and produces NTLM_SUPPLEMENTAL_CREDENTIAL and MSV1_0_VALIDATION_INFO
/// structures.
/// </summary>
public class NtlmPassThrough
{
    private readonly ILogger<NtlmPassThrough> _logger;

    public NtlmPassThrough(ILogger<NtlmPassThrough> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Validates an NTLM response (NTLMv2) against the user's stored NT hash.
    /// MS-APDS 3.1.5.2: The DC verifies the NTLMv2 response by recomputing
    /// the expected NTProofStr using HMAC-MD5.
    /// </summary>
    /// <param name="user">The directory object containing the user's NT hash.</param>
    /// <param name="domain">The domain name used in the NTLM identity computation.</param>
    /// <param name="username">The username used in the NTLM identity computation.</param>
    /// <param name="serverChallenge">The 8-byte server challenge.</param>
    /// <param name="ntResponse">The NT response from the client (NTProofStr + NTLMv2 client blob).</param>
    /// <param name="lmResponse">The LM response from the client (optional for NTLMv2).</param>
    /// <returns>Validation result with session key if successful.</returns>
    public NtlmValidationResult ValidateNtlmResponse(
        DirectoryObject user,
        string domain,
        string username,
        byte[] serverChallenge,
        byte[] ntResponse,
        byte[] lmResponse)
    {
        if (string.IsNullOrEmpty(user.NTHash))
        {
            return new NtlmValidationResult { IsValid = false };
        }

        if (serverChallenge.Length != 8)
        {
            _logger.LogWarning("Invalid server challenge length: {Length}", serverChallenge.Length);
            return new NtlmValidationResult { IsValid = false };
        }

        var ntHash = Convert.FromHexString(user.NTHash);

        // NTLMv2 validation
        if (ntResponse.Length > 24)
        {
            return ValidateNtlmv2Response(ntHash, domain, username, serverChallenge, ntResponse);
        }

        // NTLMv1 validation (legacy, 24-byte response)
        if (ntResponse.Length == 24)
        {
            return ValidateNtlmv1Response(ntHash, serverChallenge, ntResponse);
        }

        _logger.LogWarning("Unexpected NT response length: {Length}", ntResponse.Length);
        return new NtlmValidationResult { IsValid = false };
    }

    /// <summary>
    /// Validates an NTLMv2 response.
    ///
    /// Algorithm per MS-NLMP 3.3.2:
    /// 1. NTLMv2Hash = HMAC-MD5(NTHash, UPPERCASE(username) + domain)
    /// 2. NTProofStr = HMAC-MD5(NTLMv2Hash, serverChallenge + clientBlob)
    /// 3. SessionBaseKey = HMAC-MD5(NTLMv2Hash, NTProofStr)
    /// </summary>
    private NtlmValidationResult ValidateNtlmv2Response(
        byte[] ntHash,
        string domain,
        string username,
        byte[] serverChallenge,
        byte[] ntResponse)
    {
        // The NTLMv2 response is: NTProofStr (16 bytes) + ClientBlob (variable)
        if (ntResponse.Length < 16)
        {
            return new NtlmValidationResult { IsValid = false };
        }

        var receivedProof = ntResponse.AsSpan(0, 16);
        var clientBlob = ntResponse.AsSpan(16);

        // Step 1: Compute NTLMv2 hash
        // HMAC-MD5(NTHash, UPPERCASE(username) + domain)
        var identityBytes = Encoding.Unicode.GetBytes(
            username.ToUpperInvariant() + domain);
        var ntlmv2Hash = HmacMd5(ntHash, identityBytes);

        // Step 2: Compute NTProofStr
        // HMAC-MD5(NTLMv2Hash, serverChallenge + clientBlob)
        var proofInput = new byte[serverChallenge.Length + clientBlob.Length];
        serverChallenge.CopyTo(proofInput, 0);
        clientBlob.CopyTo(proofInput.AsSpan(serverChallenge.Length));
        var expectedProof = HmacMd5(ntlmv2Hash, proofInput);

        if (!CryptographicOperations.FixedTimeEquals(receivedProof, expectedProof))
        {
            _logger.LogDebug("NTLMv2 proof mismatch for user {User}", username);
            return new NtlmValidationResult { IsValid = false };
        }

        // Step 3: Compute session base key
        // HMAC-MD5(NTLMv2Hash, NTProofStr)
        var sessionBaseKey = HmacMd5(ntlmv2Hash, expectedProof);

        return new NtlmValidationResult
        {
            IsValid = true,
            SessionKey = sessionBaseKey,
        };
    }

    /// <summary>
    /// Validates a legacy NTLMv1 response (24-byte DES-based).
    /// This is included for backwards compatibility but NTLMv2 is strongly preferred.
    /// </summary>
    private NtlmValidationResult ValidateNtlmv1Response(
        byte[] ntHash,
        byte[] serverChallenge,
        byte[] ntResponse)
    {
        // NTLMv1: DES_ECB(key, serverChallenge) where key is derived from NT hash
        // Split NT hash into three 7-byte DES keys and encrypt the challenge
        var expectedResponse = ComputeNtlmv1Response(ntHash, serverChallenge);

        if (!CryptographicOperations.FixedTimeEquals(
            ntResponse.AsSpan(0, 24),
            expectedResponse.AsSpan(0, 24)))
        {
            return new NtlmValidationResult { IsValid = false };
        }

        // NTLMv1 session key is MD4(NT hash) - simplified
        // In practice we use the NT hash itself as the session key basis
        var sessionKey = new byte[16];
        using (var md5 = MD5.Create())
        {
            var hash = md5.ComputeHash(ntHash);
            Array.Copy(hash, sessionKey, 16);
        }

        return new NtlmValidationResult
        {
            IsValid = true,
            SessionKey = sessionKey,
        };
    }

    /// <summary>
    /// Generates an NTLM_SUPPLEMENTAL_CREDENTIAL structure for inclusion
    /// in a PAC or credential cache. Contains the NT and LM hashes.
    /// Reference: MS-NLMP 2.2.2.6.
    /// </summary>
    public NtlmSupplementalCredential GenerateSupplementalCredential(DirectoryObject user)
    {
        var credential = new NtlmSupplementalCredential
        {
            Version = 0,
            Flags = 0,
        };

        if (!string.IsNullOrEmpty(user.NTHash))
        {
            credential.NtPassword = Convert.FromHexString(user.NTHash);
        }

        // LM hash is not stored; leave as zeros (standard for NTLMv2-only environments)
        return credential;
    }

    /// <summary>
    /// Generates an MSV1_0_VALIDATION_INFO structure combining NTLM-specific
    /// session key data with the standard NETLOGON validation info.
    /// </summary>
    public Msv10ValidationInfo GenerateMsv10ValidationInfo(
        NetlogonValidationSamInfo4 validationInfo,
        byte[] sessionKey,
        DirectoryObject user)
    {
        return new Msv10ValidationInfo
        {
            ValidationInfo = validationInfo,
            UserSessionKey = sessionKey,
            SupplementalCredential = GenerateSupplementalCredential(user),
        };
    }

    /// <summary>
    /// Validates NTLM pass-through for transitive trust scenarios.
    /// When a user from a trusted domain authenticates, the DC forwards
    /// the NTLM request to the trusted domain's DC.
    /// In this implementation, we only support the local domain.
    /// </summary>
    public NtlmValidationResult ValidateTransitiveTrust(
        string trustedDomain,
        string username,
        byte[] serverChallenge,
        byte[] ntResponse,
        byte[] lmResponse)
    {
        // Transitive trust validation requires forwarding the request to the
        // trusted domain's DC via Netlogon secure channel. Since this is a
        // single-domain implementation, we return failure for cross-domain requests.
        _logger.LogWarning(
            "Transitive trust validation not supported for domain {Domain}", trustedDomain);

        return new NtlmValidationResult { IsValid = false };
    }

    /// <summary>
    /// Computes the NTLMv1 response (24 bytes) using DES encryption.
    /// </summary>
    private static byte[] ComputeNtlmv1Response(byte[] ntHash, byte[] serverChallenge)
    {
        // Pad NT hash to 21 bytes
        var paddedHash = new byte[21];
        Array.Copy(ntHash, paddedHash, Math.Min(ntHash.Length, 16));

        var response = new byte[24];

        // Split into three 7-byte keys, each encrypts the 8-byte challenge
        DesEncrypt(paddedHash.AsSpan(0, 7), serverChallenge, response.AsSpan(0, 8));
        DesEncrypt(paddedHash.AsSpan(7, 7), serverChallenge, response.AsSpan(8, 8));
        DesEncrypt(paddedHash.AsSpan(14, 7), serverChallenge, response.AsSpan(16, 8));

        return response;
    }

    /// <summary>
    /// Encrypts 8 bytes of data using a 7-byte key expanded to a DES key.
    /// </summary>
    private static void DesEncrypt(ReadOnlySpan<byte> key7, byte[] data, Span<byte> output)
    {
        var desKey = ExpandDesKey(key7);
        using var des = DES.Create();
        des.Mode = CipherMode.ECB;
        des.Padding = PaddingMode.None;
        des.Key = desKey;

        Span<byte> result = stackalloc byte[8];
        des.TryEncryptEcb(data.AsSpan(0, 8), result, PaddingMode.None, out _);
        result.CopyTo(output);
    }

    /// <summary>
    /// Expands a 7-byte key to an 8-byte DES key by spreading bits.
    /// </summary>
    private static byte[] ExpandDesKey(ReadOnlySpan<byte> key7)
    {
        var key8 = new byte[8];
        key8[0] = (byte)(key7[0] >> 1);
        key8[1] = (byte)(((key7[0] & 0x01) << 6) | (key7[1] >> 2));
        key8[2] = (byte)(((key7[1] & 0x03) << 5) | (key7[2] >> 3));
        key8[3] = (byte)(((key7[2] & 0x07) << 4) | (key7[3] >> 4));
        key8[4] = (byte)(((key7[3] & 0x0F) << 3) | (key7[4] >> 5));
        key8[5] = (byte)(((key7[4] & 0x1F) << 2) | (key7[5] >> 6));
        key8[6] = (byte)(((key7[5] & 0x3F) << 1) | (key7[6] >> 7));
        key8[7] = (byte)(key7[6] & 0x7F);

        // Set parity bits
        for (int i = 0; i < 8; i++)
        {
            key8[i] = (byte)(key8[i] << 1);
        }

        return key8;
    }

    private static byte[] HmacMd5(byte[] key, byte[] data)
    {
        using var hmac = new HMACMD5(key);
        return hmac.ComputeHash(data);
    }
}
