using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Directory.Web.Endpoints;

public static class TlsCertificateEndpoints
{
    // In-memory store for TLS certificate metadata (in production this would be persisted)
    private static TlsCertificateInfo _currentCert;
    private static readonly object _lock = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static RouteGroupBuilder MapTlsCertificateEndpoints(this RouteGroupBuilder group)
    {
        // Get current TLS certificate info
        group.MapGet("/", () =>
        {
            lock (_lock)
            {
                if (_currentCert is null)
                    return Results.Ok(new { configured = false });

                return Results.Ok(new
                {
                    configured = true,
                    _currentCert.Subject,
                    _currentCert.Issuer,
                    _currentCert.NotBefore,
                    _currentCert.NotAfter,
                    _currentCert.Thumbprint,
                    _currentCert.SerialNumber,
                    _currentCert.KeyAlgorithm,
                    _currentCert.KeySize,
                    _currentCert.UploadedAt,
                });
            }
        })
        .WithName("GetTlsCertificateInfo")
        .WithTags("TlsCertificate");

        // Upload new TLS certificate (PFX)
        group.MapPost("/", async (HttpContext context) =>
        {
            if (!context.Request.HasFormContentType)
                return Results.Problem(statusCode: 400, detail: "Expected multipart/form-data");

            var form = await context.Request.ReadFormAsync();
            var file = form.Files.GetFile("certificate");
            if (file is null || file.Length == 0)
                return Results.Problem(statusCode: 400, detail: "No certificate file provided. Upload a PFX file with the field name 'certificate'.");

            var password = form["password"].FirstOrDefault() ?? "";

            try
            {
                using var stream = new MemoryStream();
                await file.CopyToAsync(stream);
                var pfxBytes = stream.ToArray();

                // Validate the PFX by loading it
                using var cert = X509CertificateLoader.LoadPkcs12(pfxBytes, password, X509KeyStorageFlags.EphemeralKeySet);

                var info = new TlsCertificateInfo
                {
                    Subject = cert.Subject,
                    Issuer = cert.Issuer,
                    NotBefore = cert.NotBefore.ToUniversalTime().ToString("o"),
                    NotAfter = cert.NotAfter.ToUniversalTime().ToString("o"),
                    Thumbprint = cert.Thumbprint,
                    SerialNumber = cert.SerialNumber,
                    KeyAlgorithm = cert.PublicKey.Oid.FriendlyName ?? cert.PublicKey.Oid.Value ?? "Unknown",
                    KeySize = cert.PublicKey.GetRSAPublicKey()?.KeySize
                             ?? cert.PublicKey.GetECDsaPublicKey()?.KeySize
                             ?? 0,
                    UploadedAt = DateTimeOffset.UtcNow.ToString("o"),
                };

                lock (_lock)
                {
                    _currentCert = info;
                }

                return Results.Ok(new
                {
                    message = "TLS certificate uploaded successfully",
                    info.Subject,
                    info.Issuer,
                    info.NotBefore,
                    info.NotAfter,
                    info.Thumbprint,
                });
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                return Results.Problem(statusCode: 400, detail: "Invalid PFX file or incorrect password");
            }
        })
        .WithName("UploadTlsCertificate")
        .WithTags("TlsCertificate")
        .DisableAntiforgery();

        // Remove custom TLS certificate
        group.MapDelete("/", () =>
        {
            lock (_lock)
            {
                if (_currentCert is null)
                    return Results.Problem(statusCode: 404, detail: "No custom TLS certificate is configured");

                _currentCert = null;
            }

            return Results.Ok(new { message = "Custom TLS certificate removed. Default certificate will be used." });
        })
        .WithName("RemoveTlsCertificate")
        .WithTags("TlsCertificate");

        // TLS status
        group.MapGet("/status", () =>
        {
            lock (_lock)
            {
                if (_currentCert is null)
                {
                    return Results.Ok(new TlsStatusResponse
                    {
                        Enabled = false,
                        Port = 636,
                        CertificateConfigured = false,
                        CertificateValid = false,
                        DaysUntilExpiry = null,
                        Subject = null,
                        Thumbprint = null,
                    });
                }

                var notAfter = DateTimeOffset.Parse(_currentCert.NotAfter);
                var daysUntilExpiry = (int)Math.Ceiling((notAfter - DateTimeOffset.UtcNow).TotalDays);
                var isValid = daysUntilExpiry > 0;

                return Results.Ok(new TlsStatusResponse
                {
                    Enabled = true,
                    Port = 636,
                    CertificateConfigured = true,
                    CertificateValid = isValid,
                    DaysUntilExpiry = daysUntilExpiry,
                    Subject = _currentCert.Subject,
                    Thumbprint = _currentCert.Thumbprint,
                });
            }
        })
        .WithName("GetTlsStatus")
        .WithTags("TlsCertificate");

        return group;
    }

    private class TlsCertificateInfo
    {
        public string Subject { get; set; } = "";
        public string Issuer { get; set; } = "";
        public string NotBefore { get; set; } = "";
        public string NotAfter { get; set; } = "";
        public string Thumbprint { get; set; } = "";
        public string SerialNumber { get; set; } = "";
        public string KeyAlgorithm { get; set; } = "";
        public int KeySize { get; set; }
        public string UploadedAt { get; set; } = "";
    }

    private class TlsStatusResponse
    {
        public bool Enabled { get; set; }
        public int Port { get; set; }
        public bool CertificateConfigured { get; set; }
        public bool CertificateValid { get; set; }
        public int? DaysUntilExpiry { get; set; }
        public string Subject { get; set; }
        public string Thumbprint { get; set; }
    }
}
