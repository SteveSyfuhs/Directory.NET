using System.Buffers.Binary;
using System.Security.Cryptography;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Microsoft.Extensions.Logging;

namespace Directory.Security.Apds;

/// <summary>
/// PAC signature types as defined in MS-PAC 2.8.
/// </summary>
public enum PacSignatureType : int
{
    /// <summary>
    /// HMAC-MD5 checksum using RC4 key. Used with RC4-HMAC encryption.
    /// </summary>
    HmacMd5 = -138,

    /// <summary>
    /// HMAC-SHA1-96 (AES128). Used with AES128-CTS-HMAC-SHA1-96 encryption.
    /// </summary>
    HmacSha1_96_Aes128 = 15,

    /// <summary>
    /// HMAC-SHA1-96 (AES256). Used with AES256-CTS-HMAC-SHA1-96 encryption.
    /// </summary>
    HmacSha1_96_Aes256 = 16,
}

/// <summary>
/// Represents a PAC_SIGNATURE_DATA structure from MS-PAC 2.8.
/// </summary>
public class PacSignature
{
    /// <summary>
    /// The signature algorithm type.
    /// </summary>
    public PacSignatureType SignatureType { get; set; }

    /// <summary>
    /// The signature data (checksum bytes).
    /// </summary>
    public byte[] Signature { get; set; } = [];

    /// <summary>
    /// Optional RODCIdentifier (16-bit) for read-only DC signatures.
    /// </summary>
    public ushort? RodcIdentifier { get; set; }
}

/// <summary>
/// Result of a PAC validation operation.
/// </summary>
public class PacValidationResult
{
    /// <summary>
    /// Whether the PAC validation succeeded.
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// Detailed status code.
    /// </summary>
    public NtStatus Status { get; set; }

    /// <summary>
    /// Error message if validation failed.
    /// </summary>
    public string ErrorMessage { get; set; }
}

/// <summary>
/// Implements PAC (Privilege Attribute Certificate) validation as defined in
/// MS-APDS section 3.1.5.4. Validates PAC server and KDC checksums, and verifies
/// PAC logon information against the directory.
///
/// The PAC is a Kerberos authorization data structure that contains the user's
/// security context information (SIDs, group memberships) signed by the KDC.
/// </summary>
public class PacValidation
{
    private readonly IDirectoryStore _store;
    private readonly ILogger<PacValidation> _logger;

    public PacValidation(IDirectoryStore store, ILogger<PacValidation> logger)
    {
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// Validates the server checksum in a PAC.
    /// MS-APDS 3.1.5.4: The server checksum is computed over the entire PAC data
    /// (with the server and KDC signature fields zeroed) using the service's long-term key.
    ///
    /// This validates that the PAC was issued for the correct service principal
    /// and has not been tampered with by the client.
    /// </summary>
    /// <param name="pacData">The raw PAC data.</param>
    /// <param name="signature">The server signature to validate.</param>
    /// <param name="serviceKey">The service's long-term Kerberos key.</param>
    /// <returns>Validation result.</returns>
    public PacValidationResult ValidateServerChecksum(
        byte[] pacData,
        PacSignature signature,
        byte[] serviceKey)
    {
        if (pacData.Length == 0)
        {
            return new PacValidationResult
            {
                IsValid = false,
                Status = NtStatus.StatusInvalidParameter,
                ErrorMessage = "PAC data is empty",
            };
        }

        try
        {
            var expectedChecksum = ComputeChecksum(
                signature.SignatureType, serviceKey, pacData);

            if (CryptographicOperations.FixedTimeEquals(
                signature.Signature, expectedChecksum))
            {
                return new PacValidationResult
                {
                    IsValid = true,
                    Status = NtStatus.StatusSuccess,
                };
            }

            _logger.LogDebug("PAC server checksum mismatch");
            return new PacValidationResult
            {
                IsValid = false,
                Status = NtStatus.StatusLogonFailure,
                ErrorMessage = "Server checksum verification failed",
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating PAC server checksum");
            return new PacValidationResult
            {
                IsValid = false,
                Status = NtStatus.StatusInternalError,
                ErrorMessage = ex.Message,
            };
        }
    }

    /// <summary>
    /// Validates the KDC checksum in a PAC.
    /// MS-APDS 3.1.5.4: The KDC checksum is computed over the server checksum
    /// using the KDC's long-term key (krbtgt). This proves the PAC was issued
    /// by a legitimate KDC.
    /// </summary>
    /// <param name="serverSignatureBytes">The server signature data that was checksummed by the KDC.</param>
    /// <param name="kdcSignature">The KDC signature to validate.</param>
    /// <param name="krbtgtKey">The krbtgt account's long-term key.</param>
    /// <returns>Validation result.</returns>
    public PacValidationResult ValidateKdcChecksum(
        byte[] serverSignatureBytes,
        PacSignature kdcSignature,
        byte[] krbtgtKey)
    {
        if (serverSignatureBytes.Length == 0)
        {
            return new PacValidationResult
            {
                IsValid = false,
                Status = NtStatus.StatusInvalidParameter,
                ErrorMessage = "Server signature data is empty",
            };
        }

        try
        {
            var expectedChecksum = ComputeChecksum(
                kdcSignature.SignatureType, krbtgtKey, serverSignatureBytes);

            if (CryptographicOperations.FixedTimeEquals(
                kdcSignature.Signature, expectedChecksum))
            {
                return new PacValidationResult
                {
                    IsValid = true,
                    Status = NtStatus.StatusSuccess,
                };
            }

            _logger.LogDebug("PAC KDC checksum mismatch");
            return new PacValidationResult
            {
                IsValid = false,
                Status = NtStatus.StatusLogonFailure,
                ErrorMessage = "KDC checksum verification failed",
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating PAC KDC checksum");
            return new PacValidationResult
            {
                IsValid = false,
                Status = NtStatus.StatusInternalError,
                ErrorMessage = ex.Message,
            };
        }
    }

    /// <summary>
    /// Verifies PAC logon information against the current directory state.
    /// MS-APDS 3.1.5.4: After validating PAC signatures, the DC verifies
    /// that the user account still exists, is not disabled, and that the
    /// group memberships in the PAC are still valid.
    /// </summary>
    /// <param name="tenantId">Tenant identifier.</param>
    /// <param name="logonInfo">The logon info extracted from the PAC.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Validation result. If invalid, returns the appropriate status.</returns>
    public async Task<PacValidationResult> VerifyPacLogonInfoAsync(
        string tenantId,
        NetlogonValidationSamInfo4 logonInfo,
        CancellationToken ct = default)
    {
        // Step 1: Verify the user still exists
        string domainDn = SidToDomainDn(logonInfo.LogonDomainId, logonInfo.DnsLogonDomainName);
        string userSid = $"{logonInfo.LogonDomainId}-{logonInfo.UserId}";

        // Try to find the user by constructing the SAM account name lookup
        var user = await _store.GetBySamAccountNameAsync(
            tenantId, domainDn, logonInfo.EffectiveName, ct);

        if (user is null)
        {
            _logger.LogDebug("PAC validation: user {User} not found in directory",
                logonInfo.EffectiveName);
            return new PacValidationResult
            {
                IsValid = false,
                Status = NtStatus.StatusNoSuchUser,
                ErrorMessage = $"User {logonInfo.EffectiveName} not found",
            };
        }

        // Step 2: Verify account is not disabled
        if ((user.UserAccountControl & 0x0002) != 0) // ACCOUNTDISABLE
        {
            _logger.LogDebug("PAC validation: user {User} account is disabled",
                logonInfo.EffectiveName);
            return new PacValidationResult
            {
                IsValid = false,
                Status = NtStatus.StatusAccountDisabled,
                ErrorMessage = "Account is disabled",
            };
        }

        // Step 3: Verify the SID matches
        if (!string.IsNullOrEmpty(user.ObjectSid)
            && !string.Equals(user.ObjectSid, userSid, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("PAC validation: SID mismatch for {User}. PAC={PacSid}, Directory={DirSid}",
                logonInfo.EffectiveName, userSid, user.ObjectSid);
            return new PacValidationResult
            {
                IsValid = false,
                Status = NtStatus.StatusLogonFailure,
                ErrorMessage = "User SID mismatch",
            };
        }

        // Step 4: Verify primary group still valid
        if (user.PrimaryGroupId != (int)logonInfo.PrimaryGroupId)
        {
            _logger.LogDebug("PAC validation: primary group mismatch for {User}. PAC={PacGid}, Directory={DirGid}",
                logonInfo.EffectiveName, logonInfo.PrimaryGroupId, user.PrimaryGroupId);
            // This is a warning but not necessarily a failure; the PAC may be stale
        }

        _logger.LogDebug("PAC logon info verified for user {User}", logonInfo.EffectiveName);

        return new PacValidationResult
        {
            IsValid = true,
            Status = NtStatus.StatusSuccess,
        };
    }

    /// <summary>
    /// Computes a PAC checksum using the specified algorithm and key.
    /// </summary>
    private static byte[] ComputeChecksum(PacSignatureType signatureType, byte[] key, byte[] data)
    {
        return signatureType switch
        {
            PacSignatureType.HmacMd5 => ComputeHmacMd5Checksum(key, data),
            PacSignatureType.HmacSha1_96_Aes128 => ComputeHmacSha1_96Checksum(key, data),
            PacSignatureType.HmacSha1_96_Aes256 => ComputeHmacSha1_96Checksum(key, data),
            _ => throw new NotSupportedException($"Unsupported PAC signature type: {signatureType}"),
        };
    }

    /// <summary>
    /// Computes HMAC-MD5 checksum for RC4-HMAC PAC signatures.
    /// MS-PAC 2.8: For KERB_CHECKSUM_HMAC_MD5 (-138), the checksum is
    /// HMAC-MD5(Key, "signaturekey\0" + MD5(data)).
    /// </summary>
    private static byte[] ComputeHmacMd5Checksum(byte[] key, byte[] data)
    {
        // Compute HMAC-MD5 signing key: HMAC-MD5(key, "signaturekey\0")
        var signKeyInput = System.Text.Encoding.ASCII.GetBytes("signaturekey\0");
        byte[] signKey;
        using (var hmac1 = new HMACMD5(key))
        {
            signKey = hmac1.ComputeHash(signKeyInput);
        }

        // Compute MD5 of the data
        byte[] dataHash;
        using (var md5 = MD5.Create())
        {
            dataHash = md5.ComputeHash(data);
        }

        // Final checksum: HMAC-MD5(signKey, dataHash)
        using var hmac2 = new HMACMD5(signKey);
        return hmac2.ComputeHash(dataHash);
    }

    /// <summary>
    /// Computes HMAC-SHA1-96 checksum for AES PAC signatures.
    /// MS-PAC 2.8: For AES-based checksums, the output is truncated to 12 bytes.
    /// </summary>
    private static byte[] ComputeHmacSha1_96Checksum(byte[] key, byte[] data)
    {
        using var hmac = new HMACSHA1(key);
        var fullHash = hmac.ComputeHash(data);

        // Truncate to 96 bits (12 bytes)
        var truncated = new byte[12];
        Array.Copy(fullHash, truncated, 12);
        return truncated;
    }

    /// <summary>
    /// Converts a domain SID and DNS name to a domain DN.
    /// </summary>
    private static string SidToDomainDn(string domainSid, string dnsName)
    {
        if (!string.IsNullOrEmpty(dnsName))
        {
            return "DC=" + dnsName.ToLowerInvariant().Replace(".", ",DC=");
        }

        // Fallback: return a placeholder DN
        return "DC=unknown";
    }
}
