using System.Security.Cryptography;
using Directory.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace Directory.Security;

/// <summary>
/// Handles NTLM authentication challenge/response validation.
/// </summary>
public class NtlmAuthenticator
{
    private readonly IDirectoryStore _store;
    private readonly IPasswordPolicy _passwordPolicy;
    private readonly ILogger<NtlmAuthenticator> _logger;

    public NtlmAuthenticator(IDirectoryStore store, IPasswordPolicy passwordPolicy, ILogger<NtlmAuthenticator> logger)
    {
        _store = store;
        _passwordPolicy = passwordPolicy;
        _logger = logger;
    }

    /// <summary>
    /// Generate an NTLM challenge (8 random bytes).
    /// </summary>
    public byte[] GenerateChallenge()
    {
        return RandomNumberGenerator.GetBytes(8);
    }

    /// <summary>
    /// Validate an NTLMv2 response against the stored NT hash.
    /// </summary>
    public async Task<bool> ValidateNtlmv2ResponseAsync(
        string tenantId, string domainDn, string username,
        byte[] challenge, byte[] ntlmResponse, byte[] clientChallenge,
        CancellationToken ct = default)
    {
        var user = await _store.GetBySamAccountNameAsync(tenantId, domainDn, username, ct);
        if (user?.NTHash is null)
            return false;

        var ntHash = Convert.FromHexString(user.NTHash);

        // NTLMv2: HMAC-MD5(NTHash, UPPERCASE(username) + UPPERCASE(NetBIOSDomain))
        // Per MS-NLMP 3.4.4.1, the identity is the NetBIOS domain name, not the full DN.
        var netBiosDomain = ExtractNetBiosDomain(domainDn);
        var identityBytes = System.Text.Encoding.Unicode.GetBytes(
            username.ToUpperInvariant() + netBiosDomain.ToUpperInvariant());

        var ntlmv2Hash = HMACMD5Hash(ntHash, identityBytes);

        // The NTLMv2 response is HMAC-MD5(NTLMv2Hash, challenge + clientChallenge)
        var serverData = new byte[challenge.Length + clientChallenge.Length];
        challenge.CopyTo(serverData, 0);
        clientChallenge.CopyTo(serverData, challenge.Length);

        var expectedResponse = HMACMD5Hash(ntlmv2Hash, serverData);

        return CryptographicOperations.FixedTimeEquals(
            ntlmResponse.AsSpan(0, 16),
            expectedResponse);
    }

    /// <summary>
    /// Retrieves the raw NT hash bytes for a user, or null if the user is not found.
    /// Used for session key derivation after successful NTLMv2 validation.
    /// </summary>
    public async Task<byte[]> GetUserNtHashAsync(
        string tenantId, string domainDn, string username,
        CancellationToken ct = default)
    {
        var user = await _store.GetBySamAccountNameAsync(tenantId, domainDn, username, ct);
        if (user?.NTHash is null)
            return null;

        return Convert.FromHexString(user.NTHash);
    }

    /// <summary>
    /// Extracts the NetBIOS domain name from a domain DN.
    /// E.g. "DC=contoso,DC=com" → "CONTOSO"
    /// </summary>
    private static string ExtractNetBiosDomain(string domainDn)
    {
        // Take the first DC= component as the NetBIOS name
        foreach (var part in domainDn.Split(','))
        {
            var trimmed = part.Trim();
            if (trimmed.StartsWith("DC=", StringComparison.OrdinalIgnoreCase))
                return trimmed[3..];
        }
        return domainDn;
    }

    private static byte[] HMACMD5Hash(byte[] key, byte[] data)
    {
        using var hmac = new HMACMD5(key);
        return hmac.ComputeHash(data);
    }
}
