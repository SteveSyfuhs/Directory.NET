using System.Text.Json;
using System.Text.Json.Serialization;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Microsoft.Extensions.Logging;

namespace Directory.Security.CertificateAuthority;

/// <summary>
/// Reads certificate template objects from the directory and validates enrollment requests
/// against template constraints.
/// </summary>
public class CertificateTemplateService
{
    private readonly IDirectoryStore _store;
    private readonly INamingContextService _ncService;
    private readonly ILogger<CertificateTemplateService> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public CertificateTemplateService(
        IDirectoryStore store,
        INamingContextService ncService,
        ILogger<CertificateTemplateService> logger)
    {
        _store = store;
        _ncService = ncService;
        _logger = logger;
    }

    /// <summary>
    /// Load a certificate template by name from the configuration partition.
    /// </summary>
    public async Task<TemplateSettings> GetTemplateAsync(string templateName, CancellationToken ct = default)
    {
        var configDn = _ncService.GetConfigurationDn();
        var dn = $"CN={templateName},CN=Certificate Templates,CN=Public Key Services,CN=Services,{configDn}";

        var obj = await _store.GetByDnAsync("default", dn, ct);
        if (obj == null || obj.IsDeleted) return null;

        return ExtractSettings(obj);
    }

    /// <summary>
    /// List all certificate templates.
    /// </summary>
    public async Task<List<TemplateSettings>> ListTemplatesAsync(CancellationToken ct = default)
    {
        var configDn = _ncService.GetConfigurationDn();
        var templatesDn = $"CN=Certificate Templates,CN=Public Key Services,CN=Services,{configDn}";

        try
        {
            var filter = new EqualityFilterNode("objectClass", "pKICertificateTemplate");
            var result = await _store.SearchAsync("default", templatesDn, SearchScope.SingleLevel, filter,
                null, pageSize: 1000, ct: ct);

            return result.Entries
                .Where(e => !e.IsDeleted)
                .Select(ExtractSettings)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Get templates that have auto-enrollment enabled.
    /// </summary>
    public async Task<List<TemplateSettings>> GetAutoEnrollTemplatesAsync(CancellationToken ct = default)
    {
        var all = await ListTemplatesAsync(ct);
        return all.Where(t => t.AutoEnroll).ToList();
    }

    /// <summary>
    /// Validate that a certificate issuance request satisfies the template constraints.
    /// </summary>
    public TemplateValidationResult ValidateRequest(CertificateIssuanceRequest request, TemplateSettings template)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(request.SubjectDn))
            errors.Add("Subject DN is required");

        var keySize = request.KeySizeOverride ?? template.MinimumKeySize;
        if (keySize < template.MinimumKeySize)
            errors.Add($"Key size {keySize} is below the template minimum of {template.MinimumKeySize}");

        return new TemplateValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors,
            EffectiveKeySize = keySize,
            ValidityDays = template.ValidityPeriodDays,
            KeyUsage = template.KeyUsage,
            EnhancedKeyUsage = template.EnhancedKeyUsage,
            RequiresApproval = template.RequireApproval,
        };
    }

    private static TemplateSettings ExtractSettings(DirectoryObject obj)
    {
        var json = obj.GetAttribute("certTemplateData")?.GetFirstString();
        CertTemplateDataInternal data = null;

        if (!string.IsNullOrEmpty(json))
        {
            try { data = JsonSerializer.Deserialize<CertTemplateDataInternal>(json, JsonOpts); }
            catch { /* use defaults */ }
        }

        data ??= new CertTemplateDataInternal();

        return new TemplateSettings
        {
            Name = obj.Cn ?? "",
            DisplayName = obj.DisplayName ?? obj.Cn ?? "",
            ValidityPeriodDays = data.ValidityPeriodDays,
            RenewalPeriodDays = data.RenewalPeriodDays,
            KeyUsage = data.KeyUsage,
            EnhancedKeyUsage = data.EnhancedKeyUsage,
            MinimumKeySize = data.MinimumKeySize,
            AutoEnroll = data.AutoEnroll,
            RequireApproval = data.RequireApproval,
        };
    }

    // Internal deserialization model matching the existing CertTemplateData format
    private class CertTemplateDataInternal
    {
        [JsonPropertyName("validityPeriodDays")]
        public int ValidityPeriodDays { get; set; } = 365;

        [JsonPropertyName("renewalPeriodDays")]
        public int RenewalPeriodDays { get; set; } = 42;

        [JsonPropertyName("keyUsage")]
        public int KeyUsage { get; set; } = 0xA0;

        [JsonPropertyName("enhancedKeyUsage")]
        public List<string> EnhancedKeyUsage { get; set; } = [];

        [JsonPropertyName("minimumKeySize")]
        public int MinimumKeySize { get; set; } = 2048;

        [JsonPropertyName("autoEnroll")]
        public bool AutoEnroll { get; set; }

        [JsonPropertyName("requireApproval")]
        public bool RequireApproval { get; set; }
    }
}

public class TemplateSettings
{
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public int ValidityPeriodDays { get; set; } = 365;
    public int RenewalPeriodDays { get; set; } = 42;
    public int KeyUsage { get; set; } = 0xA0;
    public List<string> EnhancedKeyUsage { get; set; } = [];
    public int MinimumKeySize { get; set; } = 2048;
    public bool AutoEnroll { get; set; }
    public bool RequireApproval { get; set; }
}

public class TemplateValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = [];
    public int EffectiveKeySize { get; set; }
    public int ValidityDays { get; set; }
    public int KeyUsage { get; set; }
    public List<string> EnhancedKeyUsage { get; set; } = [];
    public bool RequiresApproval { get; set; }
}
