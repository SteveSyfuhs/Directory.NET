using System.Text.Json;
using System.Text.Json.Serialization;
using Directory.Core;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Security.CertificateAuthority;

namespace Directory.Web.Endpoints;

public static class CertificateEndpoints
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static RouteGroupBuilder MapCertificateEndpoints(this RouteGroupBuilder group)
    {
        // ═══════════════════════════════════════════════════════════
        // Certificate Templates (unchanged — existing functionality)
        // ═══════════════════════════════════════════════════════════

        group.MapGet("/templates", async (IDirectoryStore store, INamingContextService ncService, CancellationToken ct) =>
        {
            var configDn = ncService.GetConfigurationDn();
            var templatesDn = $"CN=Certificate Templates,CN=Public Key Services,CN=Services,{configDn}";

            var filter = new EqualityFilterNode("objectClass", "pKICertificateTemplate");

            try
            {
                var result = await store.SearchAsync(DirectoryConstants.DefaultTenantId, templatesDn, SearchScope.SingleLevel, filter,
                    null, pageSize: 1000, ct: ct);

                var templates = result.Entries.Where(e => !e.IsDeleted)
                    .Select(e => MapToTemplate(e))
                    .ToList();

                return Results.Ok(templates);
            }
            catch
            {
                return Results.Ok(Array.Empty<CertificateTemplate>());
            }
        })
        .WithName("ListCertificateTemplates")
        .WithTags("Certificates");

        group.MapPost("/templates", async (
            CreateTemplateRequest request,
            IDirectoryStore store,
            INamingContextService ncService,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.Problem(statusCode: 400, detail: "Name is required");

            var configDn = ncService.GetConfigurationDn();
            var templatesDn = $"CN=Certificate Templates,CN=Public Key Services,CN=Services,{configDn}";
            var dn = $"CN={request.Name},{templatesDn}";

            var existing = await store.GetByDnAsync(DirectoryConstants.DefaultTenantId, dn, ct);
            if (existing != null)
                return Results.Problem(statusCode: 409, detail: $"Template already exists: {request.Name}");

            var domainDn = ncService.GetDomainNc().Dn;
            var now = DateTimeOffset.UtcNow;
            var usn = await store.GetNextUsnAsync(DirectoryConstants.DefaultTenantId, domainDn, ct);

            var obj = new DirectoryObject
            {
                Id = dn.ToLowerInvariant(),
                TenantId = DirectoryConstants.DefaultTenantId,
                DistinguishedName = dn,
                DomainDn = domainDn,
                ObjectCategory = "pKICertificateTemplate",
                ObjectClass = ["top", "pKICertificateTemplate"],
                Cn = request.Name,
                DisplayName = request.DisplayName ?? request.Name,
                ParentDn = templatesDn,
                ObjectGuid = Guid.NewGuid().ToString(),
                WhenCreated = now,
                WhenChanged = now,
                USNCreated = usn,
                USNChanged = usn,
            };

            var templateData = new CertTemplateData
            {
                ValidityPeriodDays = request.ValidityPeriodDays ?? 365,
                RenewalPeriodDays = request.RenewalPeriodDays ?? 42,
                KeyUsage = request.KeyUsage ?? 0xA0,
                EnhancedKeyUsage = request.EnhancedKeyUsage ?? [],
                MinimumKeySize = request.MinimumKeySize ?? 2048,
                AutoEnroll = request.AutoEnroll ?? false,
                RequireApproval = request.RequireApproval ?? false,
                PublishToDs = request.PublishToDs ?? false,
            };

            var json = JsonSerializer.Serialize(templateData, JsonOpts);
            obj.SetAttribute("certTemplateData", new DirectoryAttribute("certTemplateData", json));

            obj.SetAttribute("pKIMaxIssuingDepth", new DirectoryAttribute("pKIMaxIssuingDepth", 0));
            obj.SetAttribute("pKIDefaultKeySpec", new DirectoryAttribute("pKIDefaultKeySpec", 1));
            obj.SetAttribute("msPKI-Certificate-Name-Flag", new DirectoryAttribute("msPKI-Certificate-Name-Flag", 1));
            obj.SetAttribute("msPKI-Enrollment-Flag", new DirectoryAttribute("msPKI-Enrollment-Flag", request.AutoEnroll == true ? 32 : 0));

            if (request.EnrollmentPermissions != null)
            {
                var permJson = JsonSerializer.Serialize(request.EnrollmentPermissions, JsonOpts);
                obj.SetAttribute("certEnrollmentPermissions", new DirectoryAttribute("certEnrollmentPermissions", permJson));
            }

            await store.CreateAsync(obj, ct);

            return Results.Created($"/api/v1/certificates/templates/{request.Name}", MapToTemplate(obj));
        })
        .WithName("CreateCertificateTemplate")
        .WithTags("Certificates");

        group.MapPut("/templates/{name}", async (string name, UpdateTemplateRequest request, IDirectoryStore store, INamingContextService ncService, CancellationToken ct) =>
        {
            var configDn = ncService.GetConfigurationDn();
            var dn = $"CN={name},CN=Certificate Templates,CN=Public Key Services,CN=Services,{configDn}";

            var obj = await store.GetByDnAsync(DirectoryConstants.DefaultTenantId, dn, ct);
            if (obj == null || obj.IsDeleted) return Results.NotFound();

            if (request.DisplayName != null)
                obj.DisplayName = request.DisplayName;

            var existingData = ExtractTemplateData(obj);

            if (request.ValidityPeriodDays.HasValue) existingData.ValidityPeriodDays = request.ValidityPeriodDays.Value;
            if (request.RenewalPeriodDays.HasValue) existingData.RenewalPeriodDays = request.RenewalPeriodDays.Value;
            if (request.KeyUsage.HasValue) existingData.KeyUsage = request.KeyUsage.Value;
            if (request.EnhancedKeyUsage != null) existingData.EnhancedKeyUsage = request.EnhancedKeyUsage;
            if (request.MinimumKeySize.HasValue) existingData.MinimumKeySize = request.MinimumKeySize.Value;
            if (request.AutoEnroll.HasValue) existingData.AutoEnroll = request.AutoEnroll.Value;
            if (request.RequireApproval.HasValue) existingData.RequireApproval = request.RequireApproval.Value;
            if (request.PublishToDs.HasValue) existingData.PublishToDs = request.PublishToDs.Value;

            var json = JsonSerializer.Serialize(existingData, JsonOpts);
            obj.SetAttribute("certTemplateData", new DirectoryAttribute("certTemplateData", json));

            if (request.EnrollmentPermissions != null)
            {
                var permJson = JsonSerializer.Serialize(request.EnrollmentPermissions, JsonOpts);
                obj.SetAttribute("certEnrollmentPermissions", new DirectoryAttribute("certEnrollmentPermissions", permJson));
            }

            obj.WhenChanged = DateTimeOffset.UtcNow;
            await store.UpdateAsync(obj, ct);

            return Results.Ok(MapToTemplate(obj));
        })
        .WithName("UpdateCertificateTemplate")
        .WithTags("Certificates");

        group.MapDelete("/templates/{name}", async (string name, IDirectoryStore store, INamingContextService ncService, CancellationToken ct) =>
        {
            var configDn = ncService.GetConfigurationDn();
            var dn = $"CN={name},CN=Certificate Templates,CN=Public Key Services,CN=Services,{configDn}";

            var obj = await store.GetByDnAsync(DirectoryConstants.DefaultTenantId, dn, ct);
            if (obj == null || obj.IsDeleted) return Results.NotFound();

            await store.DeleteAsync(DirectoryConstants.DefaultTenantId, dn, hardDelete: true, ct);
            return Results.Ok();
        })
        .WithName("DeleteCertificateTemplate")
        .WithTags("Certificates");

        group.MapGet("/templates/{name}/security", async (string name, IDirectoryStore store, INamingContextService ncService, CancellationToken ct) =>
        {
            var configDn = ncService.GetConfigurationDn();
            var dn = $"CN={name},CN=Certificate Templates,CN=Public Key Services,CN=Services,{configDn}";

            var obj = await store.GetByDnAsync(DirectoryConstants.DefaultTenantId, dn, ct);
            if (obj == null || obj.IsDeleted) return Results.NotFound();

            var permJson = obj.GetAttribute("certEnrollmentPermissions")?.GetFirstString();
            if (string.IsNullOrEmpty(permJson))
                return Results.Ok(Array.Empty<EnrollmentPermission>());

            var perms = JsonSerializer.Deserialize<List<EnrollmentPermission>>(permJson, JsonOpts) ?? [];
            return Results.Ok(perms);
        })
        .WithName("GetTemplateSecurityPermissions")
        .WithTags("Certificates");

        group.MapPut("/templates/{name}/security", async (string name, List<EnrollmentPermission> permissions, IDirectoryStore store, INamingContextService ncService, CancellationToken ct) =>
        {
            var configDn = ncService.GetConfigurationDn();
            var dn = $"CN={name},CN=Certificate Templates,CN=Public Key Services,CN=Services,{configDn}";

            var obj = await store.GetByDnAsync(DirectoryConstants.DefaultTenantId, dn, ct);
            if (obj == null || obj.IsDeleted) return Results.NotFound();

            var json = JsonSerializer.Serialize(permissions, JsonOpts);
            obj.SetAttribute("certEnrollmentPermissions", new DirectoryAttribute("certEnrollmentPermissions", json));
            obj.WhenChanged = DateTimeOffset.UtcNow;
            await store.UpdateAsync(obj, ct);

            return Results.Ok(permissions);
        })
        .WithName("UpdateTemplateSecurityPermissions")
        .WithTags("Certificates");

        // ═══════════════════════════════════════════════════════════
        // Certificate Authority
        // ═══════════════════════════════════════════════════════════

        group.MapGet("/ca", (CertificateAuthorityService caService) =>
        {
            return Results.Ok(caService.GetCaInfo());
        })
        .WithName("GetCaInfo")
        .WithTags("Certificates");

        group.MapPost("/ca/initialize", async (CaInitializeRequest request, CertificateAuthorityService caService) =>
        {
            try
            {
                var initRequest = new CaInitRequest
                {
                    CommonName = request.CommonName ?? "Directory.NET Certificate Authority",
                    Organization = request.Organization,
                    Country = request.Country,
                    ValidityYears = request.ValidityYears ?? 10,
                    KeySizeInBits = request.KeySizeInBits ?? 4096,
                    HashAlgorithm = request.HashAlgorithm switch
                    {
                        "SHA384" => System.Security.Cryptography.HashAlgorithmName.SHA384,
                        "SHA512" => System.Security.Cryptography.HashAlgorithmName.SHA512,
                        _ => System.Security.Cryptography.HashAlgorithmName.SHA256,
                    },
                };

                var info = await caService.InitializeAsync(initRequest);
                return Results.Ok(info);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(statusCode: 400, detail: ex.Message);
            }
        })
        .WithName("InitializeCa")
        .WithTags("Certificates");

        group.MapGet("/ca/certificate", (CertificateAuthorityService caService) =>
        {
            var info = caService.GetCaInfo();
            if (!info.IsInitialized || string.IsNullOrEmpty(info.CertificatePem))
                return Results.Problem(statusCode: 404, detail: "CA not initialized");

            return Results.Text(info.CertificatePem, "application/x-pem-file");
        })
        .WithName("DownloadCaCertificate")
        .WithTags("Certificates");

        group.MapGet("/ca/crl", (CertificateAuthorityService caService) =>
        {
            try
            {
                var crlBytes = caService.GenerateCrl();
                return Results.Bytes(crlBytes, "application/pkix-crl", "crl.crl");
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(statusCode: 400, detail: ex.Message);
            }
        })
        .WithName("DownloadCrl")
        .WithTags("Certificates");

        // ═══════════════════════════════════════════════════════════
        // Enrolled Certificates (real issuance via CA)
        // ═══════════════════════════════════════════════════════════

        group.MapGet("/enrolled", async (CertificateAuthorityService caService) =>
        {
            try
            {
                var certs = await caService.ListIssuedCertificatesAsync();
                return Results.Ok(certs.Select(MapToEnrolledDto).ToList());
            }
            catch
            {
                return Results.Ok(Array.Empty<EnrolledCertificateDto>());
            }
        })
        .WithName("ListEnrolledCertificates")
        .WithTags("Certificates");

        group.MapGet("/enrolled/{serialNumber}", async (string serialNumber, CertificateAuthorityService caService) =>
        {
            var cert = await caService.GetCertificateAsync(serialNumber);
            if (cert == null) return Results.NotFound();

            return Results.Ok(MapToIssuedDto(cert));
        })
        .WithName("GetEnrolledCertificate")
        .WithTags("Certificates");

        group.MapGet("/enrolled/{serialNumber}/download", async (string serialNumber, CertificateAuthorityService caService) =>
        {
            var cert = await caService.GetCertificateAsync(serialNumber);
            if (cert == null) return Results.NotFound();

            return Results.Text(cert.CertificatePem, "application/x-pem-file");
        })
        .WithName("DownloadEnrolledCertificate")
        .WithTags("Certificates");

        // Enroll — issue certificate from template
        group.MapPost("/enroll", async (EnrollRequest request, CertificateAuthorityService caService) =>
        {
            if (string.IsNullOrWhiteSpace(request.TemplateName))
                return Results.Problem(statusCode: 400, detail: "TemplateName is required");
            if (string.IsNullOrWhiteSpace(request.SubjectDn))
                return Results.Problem(statusCode: 400, detail: "SubjectDn is required");

            try
            {
                var issuanceRequest = new CertificateIssuanceRequest
                {
                    TemplateName = request.TemplateName,
                    SubjectDn = request.SubjectDn,
                    SubjectAlternativeNames = request.SanEntries ?? [],
                    Csr = !string.IsNullOrEmpty(request.Csr)
                        ? Convert.FromBase64String(request.Csr)
                        : null,
                };

                var issued = await caService.IssueCertificateAsync(issuanceRequest);

                return Results.Created($"/api/v1/certificates/enrolled/{issued.SerialNumber}",
                    MapToIssuedDto(issued));
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(statusCode: 400, detail: ex.Message);
            }
        })
        .WithName("EnrollCertificate")
        .WithTags("Certificates");

        // Revoke
        group.MapPost("/revoke/{serialNumber}", async (
            string serialNumber,
            RevokeRequest request,
            CertificateAuthorityService caService) =>
        {
            try
            {
                var reason = request?.Reason ?? RevocationReason.Unspecified;
                await caService.RevokeCertificateAsync(serialNumber, reason);
                return Results.Ok(new { Message = "Certificate revoked" });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(statusCode: 400, detail: ex.Message);
            }
        })
        .WithName("RevokeCertificate")
        .WithTags("Certificates");

        // Renew
        group.MapPost("/renew/{serialNumber}", async (string serialNumber, CertificateAuthorityService caService) =>
        {
            try
            {
                var renewed = await caService.RenewCertificateAsync(serialNumber);
                return Results.Ok(MapToIssuedDto(renewed));
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(statusCode: 400, detail: ex.Message);
            }
        })
        .WithName("RenewCertificate")
        .WithTags("Certificates");

        return group;
    }

    // ── Mapping helpers ──────────────────────────────────

    private static EnrolledCertificateDto MapToEnrolledDto(IssuedCertificate cert)
    {
        return new EnrolledCertificateDto
        {
            SerialNumber = cert.SerialNumber,
            Subject = cert.Subject,
            TemplateName = cert.TemplateName,
            IssuedDate = cert.NotBefore,
            ExpiryDate = cert.NotAfter,
            Status = cert.Status,
            Issuer = cert.Issuer,
            SanEntries = cert.SubjectAlternativeNames,
            Thumbprint = cert.Thumbprint,
            KeyUsage = cert.KeyUsage,
            EnhancedKeyUsage = cert.EnhancedKeyUsage,
            RequestedBy = cert.RequestedBy,
            RevokedAt = cert.RevokedAt,
            RevocationReason = cert.RevocationReason?.ToString(),
        };
    }

    private static IssuedCertificateDto MapToIssuedDto(IssuedCertificate cert)
    {
        return new IssuedCertificateDto
        {
            SerialNumber = cert.SerialNumber,
            Subject = cert.Subject,
            Issuer = cert.Issuer,
            TemplateName = cert.TemplateName,
            NotBefore = cert.NotBefore,
            NotAfter = cert.NotAfter,
            Thumbprint = cert.Thumbprint,
            Status = cert.Status,
            RevocationReason = cert.RevocationReason?.ToString(),
            RevokedAt = cert.RevokedAt,
            RequestedBy = cert.RequestedBy,
            CertificatePem = cert.CertificatePem,
            PrivateKeyPem = cert.PrivateKeyPem,
            SubjectAlternativeNames = cert.SubjectAlternativeNames,
            KeyUsage = cert.KeyUsage,
            EnhancedKeyUsage = cert.EnhancedKeyUsage,
        };
    }

    private static CertificateTemplate MapToTemplate(DirectoryObject obj)
    {
        var data = ExtractTemplateData(obj);
        var permJson = obj.GetAttribute("certEnrollmentPermissions")?.GetFirstString();
        var perms = new List<EnrollmentPermission>();
        if (!string.IsNullOrEmpty(permJson))
        {
            try { perms = JsonSerializer.Deserialize<List<EnrollmentPermission>>(permJson, JsonOpts) ?? []; } catch (JsonException) { }
        }

        return new CertificateTemplate
        {
            Name = obj.Cn ?? "",
            DisplayName = obj.DisplayName ?? obj.Cn ?? "",
            Dn = obj.DistinguishedName,
            ValidityPeriodDays = data.ValidityPeriodDays,
            RenewalPeriodDays = data.RenewalPeriodDays,
            KeyUsage = data.KeyUsage,
            EnhancedKeyUsage = data.EnhancedKeyUsage,
            MinimumKeySize = data.MinimumKeySize,
            AutoEnroll = data.AutoEnroll,
            RequireApproval = data.RequireApproval,
            PublishToDs = data.PublishToDs,
            EnrollmentPermissions = perms,
            WhenCreated = obj.WhenCreated,
            WhenChanged = obj.WhenChanged,
        };
    }

    private static CertTemplateData ExtractTemplateData(DirectoryObject obj)
    {
        var json = obj.GetAttribute("certTemplateData")?.GetFirstString();
        if (string.IsNullOrEmpty(json)) return new CertTemplateData();
        try { return JsonSerializer.Deserialize<CertTemplateData>(json, JsonOpts) ?? new CertTemplateData(); }
        catch { return new CertTemplateData(); }
    }
}

// ═══════════════════════════════════════════════════════════
// Request/Response DTOs
// ═══════════════════════════════════════════════════════════

public record CreateTemplateRequest(
    string Name,
    string DisplayName = null,
    int? ValidityPeriodDays = null,
    int? RenewalPeriodDays = null,
    int? KeyUsage = null,
    List<string> EnhancedKeyUsage = null,
    int? MinimumKeySize = null,
    bool? AutoEnroll = null,
    bool? RequireApproval = null,
    bool? PublishToDs = null,
    List<EnrollmentPermission> EnrollmentPermissions = null
);

public record UpdateTemplateRequest(
    string DisplayName = null,
    int? ValidityPeriodDays = null,
    int? RenewalPeriodDays = null,
    int? KeyUsage = null,
    List<string> EnhancedKeyUsage = null,
    int? MinimumKeySize = null,
    bool? AutoEnroll = null,
    bool? RequireApproval = null,
    bool? PublishToDs = null,
    List<EnrollmentPermission> EnrollmentPermissions = null
);

public record EnrollRequest(
    string TemplateName,
    string SubjectDn,
    List<string> SanEntries = null,
    string Csr = null
);

public record CaInitializeRequest(
    string CommonName = null,
    string Organization = null,
    string Country = null,
    int? ValidityYears = null,
    int? KeySizeInBits = null,
    string HashAlgorithm = null
);

public record RevokeRequest(RevocationReason Reason = RevocationReason.Unspecified);

// Models
public class CertTemplateData
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

    [JsonPropertyName("publishToDs")]
    public bool PublishToDs { get; set; }
}

public class EnrollmentPermission
{
    [JsonPropertyName("principalDn")]
    public string PrincipalDn { get; set; } = "";

    [JsonPropertyName("principalName")]
    public string PrincipalName { get; set; }

    [JsonPropertyName("canEnroll")]
    public bool CanEnroll { get; set; }

    [JsonPropertyName("canAutoEnroll")]
    public bool CanAutoEnroll { get; set; }

    [JsonPropertyName("canManage")]
    public bool CanManage { get; set; }
}

public class CertificateTemplate
{
    public string Name { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Dn { get; init; } = "";
    public int ValidityPeriodDays { get; init; }
    public int RenewalPeriodDays { get; init; }
    public int KeyUsage { get; init; }
    public List<string> EnhancedKeyUsage { get; init; } = [];
    public int MinimumKeySize { get; init; }
    public bool AutoEnroll { get; init; }
    public bool RequireApproval { get; init; }
    public bool PublishToDs { get; init; }
    public List<EnrollmentPermission> EnrollmentPermissions { get; init; } = [];
    public DateTimeOffset WhenCreated { get; init; }
    public DateTimeOffset WhenChanged { get; init; }
}

public class EnrolledCertificateDto
{
    public string SerialNumber { get; init; } = "";
    public string Subject { get; init; } = "";
    public string TemplateName { get; init; } = "";
    public DateTimeOffset IssuedDate { get; init; }
    public DateTimeOffset ExpiryDate { get; init; }
    public string Status { get; init; } = "";
    public string Issuer { get; init; } = "";
    public List<string> SanEntries { get; init; } = [];
    public string Thumbprint { get; init; } = "";
    public string KeyUsage { get; init; } = "";
    public List<string> EnhancedKeyUsage { get; init; } = [];
    public string RequestedBy { get; init; } = "";
    public DateTimeOffset? RevokedAt { get; init; }
    public string RevocationReason { get; init; }
}

public class IssuedCertificateDto
{
    public string SerialNumber { get; init; } = "";
    public string Subject { get; init; } = "";
    public string Issuer { get; init; } = "";
    public string TemplateName { get; init; } = "";
    public DateTimeOffset NotBefore { get; init; }
    public DateTimeOffset NotAfter { get; init; }
    public string Thumbprint { get; init; } = "";
    public string Status { get; init; } = "";
    public string RevocationReason { get; init; }
    public DateTimeOffset? RevokedAt { get; init; }
    public string RequestedBy { get; init; } = "";
    public string CertificatePem { get; init; } = "";
    public string PrivateKeyPem { get; init; }
    public List<string> SubjectAlternativeNames { get; init; } = [];
    public string KeyUsage { get; init; } = "";
    public List<string> EnhancedKeyUsage { get; init; } = [];
}
