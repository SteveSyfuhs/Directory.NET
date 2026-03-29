using System.Security.Cryptography;
using System.Text;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Microsoft.Extensions.Logging;

namespace Directory.Security.Apds;

/// <summary>
/// Result of a Digest authentication validation.
/// </summary>
public class DigestValidationResult
{
    /// <summary>
    /// Whether the Digest response was valid.
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// The NT status code.
    /// </summary>
    public NtStatus Status { get; set; }

    /// <summary>
    /// The session key derived from the Digest authentication.
    /// </summary>
    public byte[] SessionKey { get; set; }
}

/// <summary>
/// Implements HTTP Digest (WDigest) authentication validation as defined in MS-APDS section 3.1.5.3.
///
/// WDigest uses 29 pre-computed hashes stored on the DC, each representing a different
/// combination of username, realm, and password. This allows the DC to validate HTTP Digest
/// responses without storing the cleartext password.
///
/// Reference: [MS-APDS] 3.1.5.3, [MS-SAMR] WDigest credential computation.
/// </summary>
public class DigestValidation
{
    private readonly IPasswordPolicy _passwordPolicy;
    private readonly IDirectoryStore _store;
    private readonly ILogger<DigestValidation> _logger;

    /// <summary>
    /// Number of WDigest hashes computed and stored per user.
    /// </summary>
    public const int WDigestHashCount = 29;

    public DigestValidation(
        IPasswordPolicy passwordPolicy,
        IDirectoryStore store,
        ILogger<DigestValidation> logger)
    {
        _passwordPolicy = passwordPolicy;
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// Validates an HTTP Digest authentication response.
    /// MS-APDS 3.1.5.3: The DC computes the expected Digest response using
    /// the stored WDigest hash that matches the client's parameters and compares
    /// it with the received response.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="username">The username from the Digest challenge.</param>
    /// <param name="realm">The Digest realm (usually the domain name).</param>
    /// <param name="nonce">The server nonce.</param>
    /// <param name="uri">The requested URI.</param>
    /// <param name="method">The HTTP method (GET, POST, etc.).</param>
    /// <param name="nc">The nonce count.</param>
    /// <param name="cnonce">The client nonce.</param>
    /// <param name="qop">The quality of protection ("auth" or "auth-int").</param>
    /// <param name="response">The client's Digest response hash.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Validation result.</returns>
    public async Task<DigestValidationResult> ValidateDigestResponseAsync(
        string tenantId,
        string username,
        string realm,
        string nonce,
        string uri,
        string method,
        string nc,
        string cnonce,
        string qop,
        string response,
        CancellationToken ct = default)
    {
        _logger.LogDebug("Validating Digest response for {User}@{Realm}", username, realm);

        // Resolve the domain DN from the realm
        string domainDn = "DC=" + realm.ToLowerInvariant().Replace(".", ",DC=");

        var user = await _store.GetBySamAccountNameAsync(tenantId, domainDn, username, ct);
        if (user is null)
        {
            // Try by UPN
            user = await _store.GetByUpnAsync(tenantId, $"{username}@{realm}", ct);
        }

        if (user is null)
        {
            return new DigestValidationResult
            {
                IsValid = false,
                Status = NtStatus.StatusNoSuchUser,
            };
        }

        // Try to find stored WDigest hashes
        var wdigestAttr = user.GetAttribute("msDS-WDigestHashes");
        byte[][] storedHashes = null;

        if (wdigestAttr?.GetFirstBytes() is { } hashData && hashData.Length >= 16 * WDigestHashCount)
        {
            storedHashes = ParseWDigestHashes(hashData);
        }

        // If we don't have stored hashes, we can't validate
        // In practice the DC would have these pre-computed at password set time
        if (storedHashes is null || storedHashes.Length == 0)
        {
            // Digest authentication requires pre-stored WDigest hashes.
            // NT hash-based fallback has been removed in favour of Kerberos authentication.
            _logger.LogWarning(
                "No stored WDigest hashes for {User}; Digest authentication is deprecated in favour of Kerberos",
                username);
            return new DigestValidationResult
            {
                IsValid = false,
                Status = NtStatus.StatusNotSupported,
            };
        }

        // Try each stored hash as HA1 to find one that produces a matching response
        for (int i = 0; i < storedHashes.Length; i++)
        {
            var ha1Hex = Convert.ToHexString(storedHashes[i]).ToLowerInvariant();
            string expectedResponse = ComputeDigestResponse(ha1Hex, nonce, nc, cnonce, qop, method, uri);

            if (string.Equals(response, expectedResponse, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Digest auth succeeded for {User} using WDigest hash index {Index}",
                    username, i);

                // Derive a session key from HA1
                var sessionKey = storedHashes[i];

                return new DigestValidationResult
                {
                    IsValid = true,
                    Status = NtStatus.StatusSuccess,
                    SessionKey = sessionKey,
                };
            }
        }

        _logger.LogDebug("Digest auth failed for {User}: no matching WDigest hash", username);
        return new DigestValidationResult
        {
            IsValid = false,
            Status = NtStatus.StatusLogonFailure,
        };
    }

    /// <summary>
    /// Computes all 29 WDigest credential hashes for a user.
    /// These are stored in the msDS-WDigestHashes attribute when the password is set.
    ///
    /// The 29 hashes represent different combinations of:
    /// - Username: sAMAccountName, UPN, DOMAIN\user
    /// - Realm: NetBIOS domain, DNS domain, blank
    /// - Case variations: lowercase, uppercase, original
    ///
    /// Each hash is MD5(username:realm:password).
    ///
    /// Reference: [MS-SAMR] 2.2.12.3, WDigest credentials.
    /// </summary>
    /// <param name="user">The user's directory object.</param>
    /// <param name="password">The cleartext password.</param>
    /// <param name="netbiosDomain">The NetBIOS domain name (e.g., "CORP").</param>
    /// <param name="dnsDomain">The DNS domain name (e.g., "corp.example.com").</param>
    /// <returns>Array of 29 MD5 hashes, each 16 bytes.</returns>
    public byte[][] ComputeWDigestHashes(
        DirectoryObject user,
        string password,
        string netbiosDomain,
        string dnsDomain)
    {
        string samName = user.SAMAccountName ?? "";
        string upn = user.UserPrincipalName ?? $"{samName}@{dnsDomain}";

        // The 29 WDigest hashes cover various identity/realm combinations.
        // Reference: MS-SAMR 3.1.1.8.11.3 and empirical analysis.
        var combinations = new (string User, string Realm)[]
        {
            // Hash 1-7: sAMAccountName with various realm forms
            (samName, netbiosDomain),                                        // 1
            (samName.ToLowerInvariant(), netbiosDomain.ToLowerInvariant()),  // 2
            (samName.ToUpperInvariant(), netbiosDomain.ToUpperInvariant()),  // 3
            (samName, dnsDomain),                                            // 4
            (samName.ToLowerInvariant(), dnsDomain.ToLowerInvariant()),      // 5
            (samName.ToUpperInvariant(), dnsDomain.ToUpperInvariant()),      // 6
            (samName, ""),                                                    // 7

            // Hash 8-14: UPN with various realm forms
            (upn, netbiosDomain),                                            // 8
            (upn.ToLowerInvariant(), netbiosDomain.ToLowerInvariant()),      // 9
            (upn.ToUpperInvariant(), netbiosDomain.ToUpperInvariant()),      // 10
            (upn, dnsDomain),                                                // 11
            (upn.ToLowerInvariant(), dnsDomain.ToLowerInvariant()),          // 12
            (upn.ToUpperInvariant(), dnsDomain.ToUpperInvariant()),          // 13
            (upn, ""),                                                        // 14

            // Hash 15-21: DOMAIN\user with various realm forms
            ($"{netbiosDomain}\\{samName}", netbiosDomain),                  // 15
            ($"{netbiosDomain}\\{samName}".ToLowerInvariant(), netbiosDomain.ToLowerInvariant()), // 16
            ($"{netbiosDomain}\\{samName}".ToUpperInvariant(), netbiosDomain.ToUpperInvariant()), // 17
            ($"{netbiosDomain}\\{samName}", dnsDomain),                      // 18
            ($"{netbiosDomain}\\{samName}".ToLowerInvariant(), dnsDomain.ToLowerInvariant()),     // 19
            ($"{netbiosDomain}\\{samName}".ToUpperInvariant(), dnsDomain.ToUpperInvariant()),     // 20
            ($"{netbiosDomain}\\{samName}", ""),                              // 21

            // Hash 22-29: Additional combinations for compatibility
            (samName, netbiosDomain.ToUpperInvariant()),                      // 22
            (samName, dnsDomain.ToLowerInvariant()),                          // 23
            (samName.ToLowerInvariant(), netbiosDomain),                      // 24
            (samName.ToLowerInvariant(), dnsDomain),                          // 25
            (samName.ToUpperInvariant(), netbiosDomain),                      // 26
            (samName.ToUpperInvariant(), dnsDomain),                          // 27
            (upn.ToLowerInvariant(), ""),                                      // 28
            (upn.ToUpperInvariant(), ""),                                      // 29
        };

        var hashes = new byte[WDigestHashCount][];
        for (int i = 0; i < WDigestHashCount; i++)
        {
            var (u, r) = combinations[i];
            hashes[i] = ComputeDigestHA1(u, r, password);
        }

        return hashes;
    }

    /// <summary>
    /// Serializes WDigest hashes into a byte array for storage in the directory.
    /// Each hash is 16 bytes (MD5), total = 29 * 16 = 464 bytes.
    /// </summary>
    public static byte[] SerializeWDigestHashes(byte[][] hashes)
    {
        var result = new byte[hashes.Length * 16];
        for (int i = 0; i < hashes.Length; i++)
        {
            Array.Copy(hashes[i], 0, result, i * 16, 16);
        }
        return result;
    }

    /// <summary>
    /// Computes the HA1 portion of HTTP Digest authentication.
    /// HA1 = MD5(username:realm:password)
    /// </summary>
    private static byte[] ComputeDigestHA1(string username, string realm, string password)
    {
        var input = $"{username}:{realm}:{password}";
        return MD5.HashData(Encoding.UTF8.GetBytes(input));
    }

    /// <summary>
    /// Computes the expected HTTP Digest response given the HA1 and other parameters.
    ///
    /// For qop="auth":
    ///   HA2 = MD5(method:uri)
    ///   response = MD5(HA1:nonce:nc:cnonce:qop:HA2)
    ///
    /// For no qop:
    ///   response = MD5(HA1:nonce:HA2)
    /// </summary>
    private static string ComputeDigestResponse(
        string ha1Hex,
        string nonce,
        string nc,
        string cnonce,
        string qop,
        string method,
        string uri)
    {
        // Compute HA2
        var ha2Input = $"{method}:{uri}";
        var ha2 = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(ha2Input))).ToLowerInvariant();

        // Compute response
        string responseInput;
        if (!string.IsNullOrEmpty(qop))
        {
            responseInput = $"{ha1Hex}:{nonce}:{nc}:{cnonce}:{qop}:{ha2}";
        }
        else
        {
            responseInput = $"{ha1Hex}:{nonce}:{ha2}";
        }

        return Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(responseInput))).ToLowerInvariant();
    }

    /// <summary>
    /// Parses stored WDigest hash data back into individual 16-byte hashes.
    /// </summary>
    private static byte[][] ParseWDigestHashes(byte[] data)
    {
        int hashCount = data.Length / 16;
        if (hashCount == 0)
            return null;

        var hashes = new byte[hashCount][];
        for (int i = 0; i < hashCount; i++)
        {
            hashes[i] = new byte[16];
            Array.Copy(data, i * 16, hashes[i], 0, 16);
        }
        return hashes;
    }
}
