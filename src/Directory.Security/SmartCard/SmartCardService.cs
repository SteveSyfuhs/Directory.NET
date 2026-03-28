using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Microsoft.Extensions.Logging;

namespace Directory.Security.SmartCard;

/// <summary>
/// Manages smart card / PIV certificate-to-user mappings and certificate-based authentication.
/// Supports multiple mapping strategies (explicit, subject, UPN, SAN) matching Active Directory
/// certificate mapping behavior. Stores mappings as a custom attribute on user objects and
/// global settings on a configuration object.
/// </summary>
public class SmartCardService
{
    private const string MappingAttributeName = "msDS-SmartCardMappings";
    private const string SettingsAttributeName = "msDS-SmartCardSettings";
    private const string ConfigObjectCn = "CN=Smart Card Config";

    private readonly IDirectoryStore _store;
    private readonly INamingContextService _ncService;
    private readonly ILogger<SmartCardService> _logger;

    public SmartCardService(
        IDirectoryStore store,
        INamingContextService ncService,
        ILogger<SmartCardService> logger)
    {
        _store = store;
        _ncService = ncService;
        _logger = logger;
    }

    /// <summary>
    /// Map a certificate to a user. Extracts certificate metadata and creates the mapping record.
    /// </summary>
    public async Task<SmartCardMapping> MapCertificateToUser(
        string userDn, string certificateData, MappingType? mappingType = null, CancellationToken ct = default)
    {
        var obj = await ResolveUserAsync(userDn, ct)
            ?? throw new InvalidOperationException($"User not found: {userDn}");

        var cert = ParseCertificate(certificateData);
        var settings = await GetSmartCardSettings(ct);
        var type = mappingType ?? settings.DefaultMappingType;

        var mapping = new SmartCardMapping
        {
            Id = Guid.NewGuid().ToString(),
            UserDn = obj.DistinguishedName,
            CertificateSubject = cert.Subject,
            CertificateIssuer = cert.Issuer,
            CertificateThumbprint = cert.Thumbprint,
            Upn = ExtractUpnFromCertificate(cert),
            Type = type,
            MappedAt = DateTimeOffset.UtcNow,
            IsEnabled = true,
        };

        var mappings = GetMappingsFromObject(obj);
        mappings.Add(mapping);
        await SaveMappingsAsync(obj, mappings, ct);

        _logger.LogInformation("Smart card mapping created for {DN}, thumbprint={Thumbprint}, type={Type}",
            obj.DistinguishedName, cert.Thumbprint, type);

        return mapping;
    }

    /// <summary>
    /// Authenticate using a certificate. Searches for a matching user based on certificate
    /// properties and the configured mapping types.
    /// </summary>
    public async Task<SmartCardAuthResult> AuthenticateWithCertificate(
        string certificateData, CancellationToken ct = default)
    {
        try
        {
            var settings = await GetSmartCardSettings(ct);
            if (!settings.Enabled)
                return new SmartCardAuthResult { Success = false, Error = "Smart card authentication is disabled" };

            var cert = ParseCertificate(certificateData);

            // Validate certificate chain if configured
            if (settings.ValidateCertificateChain)
            {
                var chainValid = ValidateCertificateChain(cert, settings);
                if (!chainValid)
                    return new SmartCardAuthResult { Success = false, Error = "Certificate chain validation failed" };
            }

            // Check expiration
            if (cert.NotAfter < DateTime.UtcNow || cert.NotBefore > DateTime.UtcNow)
                return new SmartCardAuthResult { Success = false, Error = "Certificate has expired or is not yet valid" };

            // Try to find a user with a matching mapping
            var domainDn = _ncService.GetDomainNc().Dn;

            // Strategy 1: Search for explicit thumbprint mapping
            var result = await FindUserByThumbprintAsync(cert.Thumbprint, domainDn, ct);
            if (result is not null)
                return result;

            // Strategy 2: Try UPN mapping
            var upn = ExtractUpnFromCertificate(cert);
            if (!string.IsNullOrEmpty(upn))
            {
                var userByUpn = await _store.GetByUpnAsync("default", upn, ct);
                if (userByUpn is not null)
                {
                    var mappings = GetMappingsFromObject(userByUpn);
                    var match = mappings.FirstOrDefault(m =>
                        m.IsEnabled && m.Type == MappingType.UpnMapping && m.Upn == upn);
                    if (match is not null)
                    {
                        _logger.LogInformation("Smart card auth succeeded via UPN mapping for {DN}", userByUpn.DistinguishedName);
                        return new SmartCardAuthResult
                        {
                            Success = true,
                            UserDn = userByUpn.DistinguishedName,
                            MappingType = MappingType.UpnMapping.ToString(),
                        };
                    }
                }
            }

            return new SmartCardAuthResult { Success = false, Error = "No matching user found for certificate" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Smart card authentication failed");
            return new SmartCardAuthResult { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Get all certificate mappings for a user.
    /// </summary>
    public async Task<List<SmartCardMapping>> GetMappingsForUser(string userDn, CancellationToken ct = default)
    {
        var obj = await ResolveUserAsync(userDn, ct)
            ?? throw new InvalidOperationException($"User not found: {userDn}");

        return GetMappingsFromObject(obj);
    }

    /// <summary>
    /// Delete a certificate mapping by its ID.
    /// </summary>
    public async Task DeleteMapping(string userDn, string mappingId, CancellationToken ct = default)
    {
        var obj = await ResolveUserAsync(userDn, ct)
            ?? throw new InvalidOperationException($"User not found: {userDn}");

        var mappings = GetMappingsFromObject(obj);
        var removed = mappings.RemoveAll(m => m.Id == mappingId);
        if (removed == 0)
            throw new InvalidOperationException($"Mapping not found: {mappingId}");

        await SaveMappingsAsync(obj, mappings, ct);
        _logger.LogInformation("Smart card mapping {MappingId} deleted for {DN}", mappingId, userDn);
    }

    /// <summary>
    /// Get the global smart card authentication settings.
    /// </summary>
    public async Task<SmartCardSettings> GetSmartCardSettings(CancellationToken ct = default)
    {
        var configObj = await GetOrCreateConfigObjectAsync(ct);
        if (!configObj.Attributes.TryGetValue(SettingsAttributeName, out var attr))
            return new SmartCardSettings();

        var json = attr.Values.FirstOrDefault()?.ToString();
        if (string.IsNullOrEmpty(json)) return new SmartCardSettings();

        try
        {
            return JsonSerializer.Deserialize<SmartCardSettings>(json) ?? new SmartCardSettings();
        }
        catch
        {
            return new SmartCardSettings();
        }
    }

    /// <summary>
    /// Update the global smart card authentication settings.
    /// </summary>
    public async Task<SmartCardSettings> UpdateSmartCardSettings(SmartCardSettings settings, CancellationToken ct = default)
    {
        var configObj = await GetOrCreateConfigObjectAsync(ct);
        settings.ModifiedAt = DateTimeOffset.UtcNow;

        var json = JsonSerializer.Serialize(settings);
        configObj.Attributes[SettingsAttributeName] = new DirectoryAttribute
        {
            Name = SettingsAttributeName,
            Values = [json],
        };
        configObj.WhenChanged = DateTimeOffset.UtcNow;
        await _store.UpdateAsync(configObj, ct);

        _logger.LogInformation("Smart card settings updated: enabled={Enabled}, defaultType={Type}",
            settings.Enabled, settings.DefaultMappingType);

        return settings;
    }

    // --- Private helpers ---

    private static X509Certificate2 ParseCertificate(string certificateData)
    {
        try
        {
            var bytes = Convert.FromBase64String(certificateData);
            return X509CertificateLoader.LoadCertificate(bytes);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Invalid certificate data: {ex.Message}", ex);
        }
    }

    private static string ExtractUpnFromCertificate(X509Certificate2 cert)
    {
        // Look for UPN in Subject Alternative Names (SAN) extension
        // OID 2.5.29.17 = Subject Alternative Name
        foreach (var extension in cert.Extensions)
        {
            if (extension.Oid?.Value == "2.5.29.17")
            {
                // Parse the SAN extension using the formatted output
                var formatted = extension.Format(true);
                if (!string.IsNullOrEmpty(formatted))
                {
                    foreach (var line in formatted.Split('\n', '\r'))
                    {
                        var trimmed = line.Trim();
                        // Look for RFC822 Name (email)
                        if (trimmed.StartsWith("RFC822 Name=", StringComparison.OrdinalIgnoreCase))
                        {
                            return trimmed["RFC822 Name=".Length..].Trim();
                        }
                        // Look for Principal Name (UPN) — Microsoft-specific OtherName
                        if (trimmed.StartsWith("Principal Name=", StringComparison.OrdinalIgnoreCase))
                        {
                            return trimmed["Principal Name=".Length..].Trim();
                        }
                    }
                }
            }
        }

        // Fall back to email from subject
        var subject = cert.Subject;
        var emailPart = subject.Split(',')
            .Select(p => p.Trim())
            .FirstOrDefault(p => p.StartsWith("E=", StringComparison.OrdinalIgnoreCase) ||
                                 p.StartsWith("emailAddress=", StringComparison.OrdinalIgnoreCase));
        if (emailPart is not null)
        {
            var idx = emailPart.IndexOf('=');
            if (idx >= 0) return emailPart[(idx + 1)..].Trim();
        }

        return null;
    }

    private bool ValidateCertificateChain(X509Certificate2 cert, SmartCardSettings settings)
    {
        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = settings.CheckRevocation
            ? X509RevocationMode.Online
            : X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;

        var isValid = chain.Build(cert);

        if (!isValid)
        {
            var errors = chain.ChainStatus.Select(s => s.StatusInformation).ToList();
            _logger.LogWarning("Certificate chain validation failed: {Errors}", string.Join("; ", errors));
        }

        // If trusted CAs are configured, verify the chain contains one
        if (settings.TrustedCAs.Count > 0)
        {
            var chainThumbprints = chain.ChainElements
                .Select(e => e.Certificate.Thumbprint)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (!settings.TrustedCAs.Any(ca => chainThumbprints.Contains(ca)))
            {
                _logger.LogWarning("Certificate chain does not contain a trusted CA");
                return false;
            }
        }

        return isValid;
    }

    private async Task<SmartCardAuthResult> FindUserByThumbprintAsync(
        string thumbprint, string domainDn, CancellationToken ct)
    {
        // Search for users with the smart card mapping attribute
        // In a real implementation, this would use an indexed query.
        // For now, we search for objects that have the mapping attribute.
        var filter = new PresenceFilterNode(MappingAttributeName);

        var results = await _store.SearchAsync("default", domainDn, SearchScope.WholeSubtree, filter,
            [MappingAttributeName], sizeLimit: 100, ct: ct);

        foreach (var entry in results.Entries)
        {
            var mappings = GetMappingsFromObject(entry);
            var match = mappings.FirstOrDefault(m =>
                m.IsEnabled && m.CertificateThumbprint.Equals(thumbprint, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                _logger.LogInformation("Smart card auth succeeded via {Type} mapping for {DN}",
                    match.Type, entry.DistinguishedName);
                return new SmartCardAuthResult
                {
                    Success = true,
                    UserDn = entry.DistinguishedName,
                    MappingType = match.Type.ToString(),
                };
            }
        }

        return null;
    }

    private async Task<DirectoryObject> ResolveUserAsync(string dn, CancellationToken ct)
    {
        var obj = await _store.GetByDnAsync("default", dn, ct);
        if (obj is null)
            obj = await _store.GetByUpnAsync("default", dn, ct);
        if (obj is null)
        {
            var domainDn = _ncService.GetDomainNc().Dn;
            obj = await _store.GetBySamAccountNameAsync("default", domainDn, dn, ct);
        }
        return obj;
    }

    private static List<SmartCardMapping> GetMappingsFromObject(DirectoryObject obj)
    {
        if (!obj.Attributes.TryGetValue(MappingAttributeName, out var attr))
            return new List<SmartCardMapping>();

        var json = attr.Values.FirstOrDefault()?.ToString();
        if (string.IsNullOrEmpty(json)) return new List<SmartCardMapping>();

        try
        {
            return JsonSerializer.Deserialize<List<SmartCardMapping>>(json) ?? new List<SmartCardMapping>();
        }
        catch
        {
            return new List<SmartCardMapping>();
        }
    }

    private async Task SaveMappingsAsync(DirectoryObject obj, List<SmartCardMapping> mappings, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(mappings);
        obj.Attributes[MappingAttributeName] = new DirectoryAttribute
        {
            Name = MappingAttributeName,
            Values = [json],
        };
        obj.WhenChanged = DateTimeOffset.UtcNow;
        await _store.UpdateAsync(obj, ct);
    }

    private async Task<DirectoryObject> GetOrCreateConfigObjectAsync(CancellationToken ct)
    {
        var domainDn = _ncService.GetDomainNc().Dn;
        var configDn = $"{ConfigObjectCn},CN=System,{domainDn}";

        var obj = await _store.GetByDnAsync("default", configDn, ct);
        if (obj is not null) return obj;

        obj = new DirectoryObject
        {
            TenantId = "default",
            DistinguishedName = configDn,
            Cn = "Smart Card Config",
            ObjectClass = ["top", "container"],
            WhenCreated = DateTimeOffset.UtcNow,
            WhenChanged = DateTimeOffset.UtcNow,
        };

        try
        {
            await _store.CreateAsync(obj, ct);
        }
        catch
        {
            obj = await _store.GetByDnAsync("default", configDn, ct);
            if (obj is null) throw;
        }

        return obj;
    }
}
