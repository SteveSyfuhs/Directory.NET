using Directory.Core.Interfaces;
using Directory.Core.Models;
using Kerberos.NET.Crypto;
using Kerberos.NET.Entities;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;

namespace Directory.Security;

/// <summary>
/// Implements password validation, NT hash computation, and Kerberos key derivation
/// using Kerberos.NET library primitives.
/// </summary>
public class PasswordService : IPasswordPolicy
{
    private readonly IDirectoryStore _store;
    private readonly ILogger<PasswordService> _logger;

    public PasswordService(IDirectoryStore store, ILogger<PasswordService> logger)
    {
        _store = store;
        _logger = logger;
    }

    public async Task<bool> ValidatePasswordAsync(string tenantId, string dn, string password, CancellationToken ct = default)
    {
        var obj = await _store.GetByDnAsync(tenantId, dn, ct);
        if (obj is null)
        {
            // Try by UPN
            obj = await _store.GetByUpnAsync(tenantId, dn, ct);
        }

        if (obj is null)
        {
            _logger.LogDebug("User not found for password validation: {DN}", dn);
            return false;
        }

        if (string.IsNullOrEmpty(obj.NTHash))
        {
            _logger.LogDebug("No NT hash stored for {DN}", dn);
            return false;
        }

        var computedHash = ComputeNTHash(password);
        var storedHash = Convert.FromHexString(obj.NTHash);

        return CryptographicOperations.FixedTimeEquals(computedHash, storedHash);
    }

    public async Task SetPasswordAsync(string tenantId, string dn, string password, CancellationToken ct = default)
    {
        var obj = await _store.GetByDnAsync(tenantId, dn, ct);
        if (obj is null)
            throw new InvalidOperationException($"User not found: {dn}");

        // Compute NT hash
        var ntHash = ComputeNTHash(password);
        obj.NTHash = Convert.ToHexString(ntHash);

        // Derive Kerberos keys
        var principalName = obj.UserPrincipalName ?? obj.SAMAccountName ?? obj.Cn ?? "unknown";
        var parsed = DistinguishedName.Parse(dn);
        var realm = parsed.GetDomainDnsName().ToUpperInvariant();

        var keys = DeriveKerberosKeys(principalName, password, realm);
        obj.KerberosKeys = keys.Select(k => $"{k.EncryptionType}:{Convert.ToBase64String(k.KeyValue)}").ToList();

        obj.PwdLastSet = DateTimeOffset.UtcNow.ToFileTime();

        await _store.UpdateAsync(obj, ct);

        _logger.LogInformation("Password set for {DN}", dn);
    }

    public bool MeetsComplexityRequirements(string password, string samAccountName = null)
    {
        if (password.Length < 7)
            return false;

        var categories = 0;
        if (password.Any(char.IsUpper)) categories++;
        if (password.Any(char.IsLower)) categories++;
        if (password.Any(char.IsDigit)) categories++;
        if (password.Any(c => !char.IsLetterOrDigit(c))) categories++;

        if (categories < 3)
            return false;

        // Must not contain sAMAccountName
        if (samAccountName is not null && password.Contains(samAccountName, StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    /// <summary>
    /// Compute NT hash: MD4(UTF-16LE(password)).
    /// Uses a managed MD4 implementation for cross-platform compatibility,
    /// since Kerberos.NET's LinuxCryptoPal does not support MD4.
    /// </summary>
    public byte[] ComputeNTHash(string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            // Well-known NT hash for empty password: MD4 of zero-length UTF-16LE input
            // This is a fixed value: 31d6cfe0d16ae931b73c59d7e0c089c0
            return Convert.FromHexString("31D6CFE0D16AE931B73C59D7E0C089C0");
        }

        return Md4.ComputeNTHash(password);
    }

    /// <summary>
    /// Derive Kerberos long-term keys from a password using Kerberos.NET.
    /// Supports AES256-CTS-HMAC-SHA1-96, AES128-CTS-HMAC-SHA1-96, and RC4-HMAC.
    /// </summary>
    public List<KerberosKeyData> DeriveKerberosKeys(string principalName, string password, string realm)
    {
        var principal = new PrincipalName(PrincipalNameType.NT_PRINCIPAL, realm, new[] { principalName });

        var etypes = new[]
        {
            EncryptionType.AES256_CTS_HMAC_SHA1_96,
            EncryptionType.AES128_CTS_HMAC_SHA1_96,
            EncryptionType.RC4_HMAC_NT,
        };

        return etypes.Select(etype =>
        {
            byte[] keyBytes;

            if (etype == EncryptionType.RC4_HMAC_NT)
            {
                // RC4-HMAC key is just the NT hash (MD4 of UTF-16LE password).
                // Use managed MD4 directly to avoid PlatformNotSupportedException on Linux.
                keyBytes = Md4.ComputeNTHash(password);
            }
            else
            {
                var key = new KerberosKey(password, principalName: principal, etype: etype);
                keyBytes = key.GetKey().ToArray();
            }

            return new KerberosKeyData
            {
                EncryptionType = (int)etype,
                KeyValue = keyBytes,
                EncryptionTypeName = etype.ToString(),
            };
        }).ToList();
    }
}
