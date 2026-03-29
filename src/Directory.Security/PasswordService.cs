using Directory.Core.Interfaces;
using Directory.Core.Models;
using Kerberos.NET.Crypto;
using Kerberos.NET.Entities;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;

namespace Directory.Security;

/// <summary>
/// Implements password validation and Kerberos key derivation
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

        if (obj.KerberosKeys is null || obj.KerberosKeys.Count == 0)
        {
            _logger.LogDebug("No Kerberos keys stored for {DN}", dn);
            return false;
        }

        // Derive AES256 key from the candidate password
        var principalName = obj.UserPrincipalName ?? obj.SAMAccountName ?? obj.Cn ?? "unknown";
        var parsed = DistinguishedName.Parse(dn);
        var realm = parsed.GetDomainDnsName().ToUpperInvariant();
        var candidateKeys = DeriveKerberosKeys(principalName, password, realm);
        var candidateAes256 = candidateKeys.FirstOrDefault(k => k.EncryptionType == (int)EncryptionType.AES256_CTS_HMAC_SHA1_96);
        if (candidateAes256 is null) return false;

        // Find stored AES256 key
        var storedAes256Key = obj.KerberosKeys
            .Select(k => k.Split(':'))
            .Where(p => p.Length == 2 && p[0] == ((int)EncryptionType.AES256_CTS_HMAC_SHA1_96).ToString())
            .Select(p => Convert.FromBase64String(p[1]))
            .FirstOrDefault();
        if (storedAes256Key is null) return false;

        return CryptographicOperations.FixedTimeEquals(candidateAes256.KeyValue, storedAes256Key);
    }

    public async Task SetPasswordAsync(string tenantId, string dn, string password, CancellationToken ct = default)
    {
        var obj = await _store.GetByDnAsync(tenantId, dn, ct);
        if (obj is null)
            throw new InvalidOperationException($"User not found: {dn}");

        // Clear legacy NT hash
        obj.NTHash = null;

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
    /// Derive Kerberos long-term keys from a password using Kerberos.NET.
    /// Supports AES256-CTS-HMAC-SHA1-96 and AES128-CTS-HMAC-SHA1-96.
    /// </summary>
    public List<KerberosKeyData> DeriveKerberosKeys(string principalName, string password, string realm)
    {
        var principal = new PrincipalName(PrincipalNameType.NT_PRINCIPAL, realm, new[] { principalName });

        var etypes = new[]
        {
            EncryptionType.AES256_CTS_HMAC_SHA1_96,
            EncryptionType.AES128_CTS_HMAC_SHA1_96,
        };

        return etypes.Select(etype =>
        {
            var key = new KerberosKey(password, principalName: principal, etype: etype);
            var keyBytes = key.GetKey().ToArray();

            return new KerberosKeyData
            {
                EncryptionType = (int)etype,
                KeyValue = keyBytes,
                EncryptionTypeName = etype.ToString(),
            };
        }).ToList();
    }
}
