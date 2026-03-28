using System.Security.Cryptography.X509Certificates;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Security.Apds;
using Kerberos.NET;
using Kerberos.NET.Crypto;
using Kerberos.NET.Entities;
using Kerberos.NET.Entities.Pac;
using Kerberos.NET.Server;
using Microsoft.Extensions.Logging;

// Disambiguate types that exist in both Directory.Security.Apds and Kerberos.NET
using NtStatus = Directory.Security.Apds.NtStatus;
using PacSignature = Kerberos.NET.Entities.Pac.PacSignature;

namespace Directory.Kerberos;

public class CosmosKerberosPrincipal : IKerberosPrincipal
{
    private readonly DirectoryObject _obj;
    private readonly IDirectoryStore _store;
    private readonly IPasswordPolicy _passwordPolicy;
    private readonly KerberosOptions _options;
    private readonly ILogger _logger;
    private readonly PacGenerator _pacGenerator;
    private readonly AccountRestrictions _accountRestrictions;

    public CosmosKerberosPrincipal(
        DirectoryObject obj,
        IDirectoryStore store,
        IPasswordPolicy passwordPolicy,
        KerberosOptions options,
        ILogger logger,
        PacGenerator pacGenerator = null,
        AccountRestrictions accountRestrictions = null)
    {
        _obj = obj;
        _store = store;
        _passwordPolicy = passwordPolicy;
        _options = options;
        _logger = logger;
        _pacGenerator = pacGenerator;
        _accountRestrictions = accountRestrictions;
    }

    public IEnumerable<PaDataType> SupportedPreAuthenticationTypes { get; set; } =
    [
        PaDataType.PA_ENC_TIMESTAMP,
        PaDataType.PA_PAC_REQUEST,
    ];

    public SupportedEncryptionTypes SupportedEncryptionTypes { get; set; } =
        SupportedEncryptionTypes.Aes128CtsHmacSha196 |
        SupportedEncryptionTypes.Aes256CtsHmacSha196 |
        SupportedEncryptionTypes.Rc4Hmac;

    public PrincipalType Type => _obj.ObjectClass.Contains("computer")
        ? PrincipalType.Service
        : PrincipalType.User;

    // IKerberosPrincipal.PrincipalName is string
    public string PrincipalName => _obj.SAMAccountName ?? _obj.Cn ?? "unknown";

    public DateTimeOffset? Expires => _obj.AccountExpires > 0 && _obj.AccountExpires < long.MaxValue
        ? DateTimeOffset.FromFileTime(_obj.AccountExpires)
        : null;

    public KerberosKey RetrieveLongTermCredential()
    {
        ValidateAccountRestrictions();

        if (_obj.KerberosKeys.Count > 0)
        {
            foreach (var keyData in _obj.KerberosKeys)
            {
                var parts = keyData.Split(':');
                if (parts.Length != 2) continue;
                if (!int.TryParse(parts[0], out var etype)) continue;
                var keyBytes = Convert.FromBase64String(parts[1]);

                return new KerberosKey(
                    keyBytes,
                    principal: new PrincipalName(
                        PrincipalNameType.NT_PRINCIPAL,
                        _options.DefaultRealm,
                        [_obj.SAMAccountName ?? _obj.Cn ?? "unknown"]),
                    etype: (EncryptionType)etype);
            }
        }

        if (!string.IsNullOrEmpty(_obj.NTHash))
        {
            var ntHash = Convert.FromHexString(_obj.NTHash);
            return new KerberosKey(
                ntHash,
                principal: new PrincipalName(
                    PrincipalNameType.NT_PRINCIPAL,
                    _options.DefaultRealm,
                    [_obj.SAMAccountName ?? _obj.Cn ?? "unknown"]),
                etype: EncryptionType.RC4_HMAC_NT);
        }

        throw new InvalidOperationException($"No credentials for: {_obj.DistinguishedName}");
    }

    public KerberosKey RetrieveLongTermCredential(EncryptionType etype)
    {
        if (_obj.KerberosKeys.Count > 0)
        {
            foreach (var keyData in _obj.KerberosKeys)
            {
                var parts = keyData.Split(':');
                if (parts.Length != 2) continue;
                if (!int.TryParse(parts[0], out var storedEtype)) continue;
                if (storedEtype != (int)etype) continue;

                var keyBytes = Convert.FromBase64String(parts[1]);
                return new KerberosKey(
                    keyBytes,
                    principal: new PrincipalName(
                        PrincipalNameType.NT_PRINCIPAL,
                        _options.DefaultRealm,
                        [_obj.SAMAccountName ?? _obj.Cn ?? "unknown"]),
                    etype: etype);
            }
        }

        return RetrieveLongTermCredential();
    }

    public PrivilegedAttributeCertificate GeneratePac()
    {
        if (_pacGenerator is not null)
        {
            try
            {
                var domainDn = !string.IsNullOrEmpty(_obj.DomainDn)
                    ? _obj.DomainDn
                    : DistinguishedName.Parse(_obj.DistinguishedName).GetDomainDn();
                var tenantId = _obj.TenantId ?? "default";

                return _pacGenerator.GenerateAsync(_obj, domainDn, tenantId)
                    .GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "PacGenerator failed for {User}, falling back to minimal PAC",
                    _obj.SAMAccountName ?? _obj.Cn);
            }
        }

        // Fallback: minimal PAC without claims.
        // ServerSignature and KdcSignature are initialized as placeholders;
        // Kerberos.NET computes the actual checksums during ticket encoding
        // via PrivilegedAttributeCertificate.Encode(kdcKey, serverKey).
        return new PrivilegedAttributeCertificate
        {
            LogonInfo = new PacLogonInfo
            {
                DomainName = GetDomainNetBiosName(),
                UserName = _obj.SAMAccountName ?? _obj.Cn ?? "unknown",
            },
            ServerSignature = new PacSignature(PacType.SERVER_CHECKSUM, EncryptionType.NULL),
            KdcSignature = new PacSignature(PacType.PRIVILEGE_SERVER_CHECKSUM, EncryptionType.NULL),
        };
    }

    public void Validate(X509Certificate2Collection certificates) { }

    /// <summary>
    /// Checks account restrictions (disabled, locked, expired, password expired, logon hours)
    /// and throws <see cref="KerberosValidationException"/> with the appropriate Kerberos error
    /// so the KDC returns the correct error code to the client.
    /// </summary>
    private void ValidateAccountRestrictions()
    {
        if (_accountRestrictions is null)
            return;

        var status = _accountRestrictions.CheckAccountRestrictions(_obj, LogonType.Network);

        switch (status)
        {
            case NtStatus.StatusSuccess:
                return;

            case NtStatus.StatusAccountDisabled:
                _logger.LogWarning("Kerberos AS-REQ denied: account {Sam} is disabled", _obj.SAMAccountName);
                throw new KerberosValidationException(
                    $"Account {_obj.SAMAccountName} is disabled (KDC_ERR_CLIENT_REVOKED)");

            case NtStatus.StatusAccountLockedOut:
                _logger.LogWarning("Kerberos AS-REQ denied: account {Sam} is locked out", _obj.SAMAccountName);
                throw new KerberosValidationException(
                    $"Account {_obj.SAMAccountName} is locked out (KDC_ERR_CLIENT_REVOKED)");

            case NtStatus.StatusAccountExpired:
                _logger.LogWarning("Kerberos AS-REQ denied: account {Sam} has expired", _obj.SAMAccountName);
                throw new KerberosValidationException(
                    $"Account {_obj.SAMAccountName} has expired (KDC_ERR_CLIENT_REVOKED)");

            case NtStatus.StatusInvalidLogonHours:
                _logger.LogWarning("Kerberos AS-REQ denied: account {Sam} logon hours restriction", _obj.SAMAccountName);
                throw new KerberosValidationException(
                    $"Account {_obj.SAMAccountName} outside permitted logon hours (KDC_ERR_CLIENT_REVOKED)");

            case NtStatus.StatusPasswordExpired:
            case NtStatus.StatusPasswordMustChange:
                _logger.LogWarning("Kerberos AS-REQ denied: account {Sam} password expired", _obj.SAMAccountName);
                throw new KerberosValidationException(
                    $"Account {_obj.SAMAccountName} password has expired (KDC_ERR_KEY_EXPIRED)");

            default:
                _logger.LogWarning("Kerberos AS-REQ denied: account {Sam} restriction check failed with {Status}",
                    _obj.SAMAccountName, status);
                throw new KerberosValidationException(
                    $"Account {_obj.SAMAccountName} failed restriction check: {status}");
        }
    }

    private string GetDomainNetBiosName()
    {
        var parsed = DistinguishedName.Parse(_obj.DistinguishedName);
        var dnsName = parsed.GetDomainDnsName();
        return dnsName.Split('.')[0].ToUpperInvariant();
    }
}
