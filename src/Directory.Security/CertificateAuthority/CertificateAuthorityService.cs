using System.Formats.Asn1;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;

namespace Directory.Security.CertificateAuthority;

/// <summary>
/// Core Certificate Authority service. Generates a root CA key pair, issues and revokes
/// X.509 certificates, and produces CRLs. Uses System.Security.Cryptography built into .NET 9.
/// </summary>
public class CertificateAuthorityService
{
    private readonly CertificateStore _certStore;
    private readonly CertificateTemplateService _templateService;
    private readonly ILogger<CertificateAuthorityService> _logger;

    private X509Certificate2 _caCertificate;
    private CaPersistedState _caState;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private const string PfxPassword = "DirectoryNET-CA-Internal";

    public CertificateAuthorityService(
        CertificateStore certStore,
        CertificateTemplateService templateService,
        ILogger<CertificateAuthorityService> logger)
    {
        _certStore = certStore;
        _templateService = templateService;
        _logger = logger;
    }

    public bool IsInitialized => _caCertificate != null && _caState != null;

    /// <summary>
    /// Try to load existing CA state from the directory on startup.
    /// </summary>
    public async Task TryLoadAsync(CancellationToken ct = default)
    {
        try
        {
            var state = await _certStore.LoadCaStateAsync(ct);
            if (state != null && !string.IsNullOrEmpty(state.PfxBase64))
            {
                var pfxBytes = Convert.FromBase64String(state.PfxBase64);
                _caCertificate = X509CertificateLoader.LoadPkcs12(pfxBytes, PfxPassword, X509KeyStorageFlags.Exportable);
                _caState = state;
                _logger.LogInformation("CA loaded from directory: {Subject}", state.Subject);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load CA state from directory");
        }
    }

    /// <summary>
    /// Initialize the CA: generate root key pair and self-signed certificate.
    /// </summary>
    public async Task<CaInfo> InitializeAsync(CaInitRequest request)
    {
        await _initLock.WaitAsync();
        try
        {
            if (IsInitialized)
                throw new InvalidOperationException("CA is already initialized");

            _logger.LogInformation("Initializing Certificate Authority: CN={CN}, KeySize={KeySize}",
                request.CommonName, request.KeySizeInBits);

            // Build subject DN
            var subjectParts = new List<string> { $"CN={request.CommonName}" };
            if (!string.IsNullOrWhiteSpace(request.Organization))
                subjectParts.Add($"O={request.Organization}");
            if (!string.IsNullOrWhiteSpace(request.Country))
                subjectParts.Add($"C={request.Country}");
            var subjectDn = string.Join(", ", subjectParts);

            // Generate RSA key pair
            using var rsa = RSA.Create(request.KeySizeInBits);

            // Build the self-signed CA certificate
            var distinguishedName = new X500DistinguishedName(subjectDn);
            var certRequest = new System.Security.Cryptography.X509Certificates.CertificateRequest(
                distinguishedName, rsa, request.HashAlgorithm, RSASignaturePadding.Pkcs1);

            // Basic Constraints: CA=true
            certRequest.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(
                    certificateAuthority: true,
                    hasPathLengthConstraint: true,
                    pathLengthConstraint: 1,
                    critical: true));

            // Key Usage: Key Cert Sign + CRL Sign
            certRequest.CertificateExtensions.Add(
                new X509KeyUsageExtension(
                    X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign | X509KeyUsageFlags.DigitalSignature,
                    critical: true));

            // Subject Key Identifier
            certRequest.CertificateExtensions.Add(
                new X509SubjectKeyIdentifierExtension(certRequest.PublicKey, false));

            // Generate serial number
            var serialBytes = new byte[16];
            RandomNumberGenerator.Fill(serialBytes);
            serialBytes[0] &= 0x7F; // Ensure positive
            var serialHex = Convert.ToHexString(serialBytes);

            var notBefore = DateTimeOffset.UtcNow;
            var notAfter = notBefore.AddYears(request.ValidityYears);

            var cert = certRequest.CreateSelfSigned(notBefore, notAfter);

            // Export as PFX for storage
            var pfxBytes = cert.Export(X509ContentType.Pfx, PfxPassword);

            // Re-import so we have a usable cert with the private key
            _caCertificate = X509CertificateLoader.LoadPkcs12(pfxBytes, PfxPassword, X509KeyStorageFlags.Exportable);

            // Build PEM for public distribution
            var certPem = _caCertificate.ExportCertificatePem();

            // Persist CA state
            _caState = new CaPersistedState
            {
                CommonName = request.CommonName,
                Subject = _caCertificate.Subject,
                SerialNumber = _caCertificate.SerialNumber,
                Thumbprint = _caCertificate.Thumbprint,
                NotBefore = notBefore,
                NotAfter = notAfter,
                KeySize = request.KeySizeInBits,
                HashAlgorithm = request.HashAlgorithm.Name ?? "SHA256",
                CertificatePem = certPem,
                PfxBase64 = Convert.ToBase64String(pfxBytes),
                NextSerialNumber = 1,
            };

            await _certStore.SaveCaStateAsync(_caState);

            _logger.LogInformation("CA initialized successfully: {Subject}, Thumbprint={Thumbprint}",
                _caState.Subject, _caState.Thumbprint);

            return GetCaInfo();
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// Get current CA information.
    /// </summary>
    public CaInfo GetCaInfo()
    {
        if (_caState == null || _caCertificate == null)
        {
            return new CaInfo { IsInitialized = false };
        }

        return new CaInfo
        {
            CommonName = _caState.CommonName,
            Subject = _caState.Subject,
            SerialNumber = _caState.SerialNumber,
            Thumbprint = _caState.Thumbprint,
            NotBefore = _caState.NotBefore,
            NotAfter = _caState.NotAfter,
            PublicKeyAlgorithm = "RSA",
            KeySize = _caState.KeySize,
            IsInitialized = true,
            CertificatePem = _caState.CertificatePem,
        };
    }

    /// <summary>
    /// Issue a certificate from a template.
    /// </summary>
    public async Task<IssuedCertificate> IssueCertificateAsync(CertificateIssuanceRequest request)
    {
        if (!IsInitialized)
            throw new InvalidOperationException("CA is not initialized");

        // Load template
        var template = await _templateService.GetTemplateAsync(request.TemplateName);
        if (template == null)
            throw new InvalidOperationException($"Certificate template not found: {request.TemplateName}");

        // Validate request
        var validation = _templateService.ValidateRequest(request, template);
        if (!validation.IsValid)
            throw new InvalidOperationException(
                $"Certificate request validation failed: {string.Join("; ", validation.Errors)}");

        // If template requires approval, create a pending request instead of issuing immediately
        if (validation.RequiresApproval)
        {
            var pending = new PendingCertificateRequest
            {
                RequestId = Guid.NewGuid().ToString("N"),
                TemplateName = request.TemplateName,
                SubjectDn = request.SubjectDn,
                SubjectAlternativeNames = request.SubjectAlternativeNames,
                RequestedBy = request.RequestedBy ?? "",
                KeySizeOverride = request.KeySizeOverride,
                CsrBase64 = request.Csr != null && request.Csr.Length > 0
                    ? Convert.ToBase64String(request.Csr)
                    : null,
                SubmittedAt = DateTimeOffset.UtcNow,
                Status = "Pending",
            };

            await _certStore.SavePendingRequestAsync(pending);

            _logger.LogInformation(
                "Certificate request pending approval: RequestId={RequestId}, Subject={Subject}, Template={Template}",
                pending.RequestId, pending.SubjectDn, pending.TemplateName);

            // Return a placeholder indicating the request is pending
            return new IssuedCertificate
            {
                SerialNumber = pending.RequestId,
                Subject = request.SubjectDn,
                TemplateName = request.TemplateName,
                Status = "Pending",
                RequestedBy = request.RequestedBy ?? "",
                SubjectAlternativeNames = request.SubjectAlternativeNames,
            };
        }

        // Generate key pair for the issued certificate (server-side generation)
        string privateKeyPem = null;
        RSA subjectKey;

        if (request.Csr != null && request.Csr.Length > 0)
        {
            // External CSR — extract public key
            subjectKey = ExtractPublicKeyFromCsr(request.Csr);
        }
        else
        {
            // Server-side key generation
            subjectKey = RSA.Create(validation.EffectiveKeySize);
            privateKeyPem = subjectKey.ExportRSAPrivateKeyPem();
        }

        try
        {
            var subjectName = new X500DistinguishedName(request.SubjectDn);
            var certRequest = new System.Security.Cryptography.X509Certificates.CertificateRequest(
                subjectName, subjectKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            // Basic Constraints: not a CA
            certRequest.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(false, false, 0, true));

            // Key Usage from template
            var kuFlags = MapKeyUsageFlags(validation.KeyUsage);
            certRequest.CertificateExtensions.Add(
                new X509KeyUsageExtension(kuFlags, true));

            // Enhanced Key Usage from template
            if (validation.EnhancedKeyUsage.Count > 0)
            {
                var ekuCollection = new OidCollection();
                foreach (var ekuOid in validation.EnhancedKeyUsage)
                {
                    ekuCollection.Add(new Oid(ekuOid));
                }
                certRequest.CertificateExtensions.Add(
                    new X509EnhancedKeyUsageExtension(ekuCollection, false));
            }

            // Subject Alternative Names
            if (request.SubjectAlternativeNames.Count > 0)
            {
                var sanBuilder = new SubjectAlternativeNameBuilder();
                foreach (var san in request.SubjectAlternativeNames)
                {
                    if (san.StartsWith("dns:", StringComparison.OrdinalIgnoreCase))
                        sanBuilder.AddDnsName(san[4..]);
                    else if (san.StartsWith("ip:", StringComparison.OrdinalIgnoreCase) &&
                             IPAddress.TryParse(san[3..], out var ip))
                        sanBuilder.AddIpAddress(ip);
                    else if (san.StartsWith("email:", StringComparison.OrdinalIgnoreCase))
                        sanBuilder.AddEmailAddress(san[6..]);
                    else
                        sanBuilder.AddDnsName(san); // Default to DNS name
                }
                certRequest.CertificateExtensions.Add(sanBuilder.Build());
            }

            // Subject Key Identifier
            certRequest.CertificateExtensions.Add(
                new X509SubjectKeyIdentifierExtension(certRequest.PublicKey, false));

            // Authority Key Identifier
            var akiExtension = X509AuthorityKeyIdentifierExtension.CreateFromCertificate(
                _caCertificate, includeKeyIdentifier: true, includeIssuerAndSerial: false);
            certRequest.CertificateExtensions.Add(akiExtension);

            // Serial number
            var serialNumber = await GetNextSerialNumberAsync();
            var serialBytes = serialNumber.ToString("X16", CultureInfo.InvariantCulture);

            var notBefore = DateTimeOffset.UtcNow;
            var notAfter = notBefore.AddDays(validation.ValidityDays);

            // Sign with CA private key
            using var caKey = _caCertificate.GetRSAPrivateKey();
            var serialBytesArray = Convert.FromHexString(serialBytes.PadLeft(16, '0'));

            var issuedCert = certRequest.Create(
                _caCertificate.SubjectName,
                X509SignatureGenerator.CreateForRSA(caKey, RSASignaturePadding.Pkcs1),
                notBefore,
                notAfter,
                serialBytesArray);

            var certPem = issuedCert.ExportCertificatePem();

            // Build key usage description
            var kuDesc = kuFlags.ToString();
            var ekuList = validation.EnhancedKeyUsage;

            var issued = new IssuedCertificate
            {
                SerialNumber = issuedCert.SerialNumber,
                Subject = issuedCert.Subject,
                Issuer = issuedCert.Issuer,
                TemplateName = request.TemplateName,
                NotBefore = notBefore,
                NotAfter = notAfter,
                Thumbprint = issuedCert.Thumbprint,
                Status = "Active",
                RequestedBy = request.RequestedBy ?? "",
                CertificatePem = certPem,
                PrivateKeyPem = privateKeyPem,
                SubjectAlternativeNames = request.SubjectAlternativeNames,
                KeyUsage = kuDesc,
                EnhancedKeyUsage = ekuList,
            };

            // Persist
            var record = new IssuedCertificateRecord
            {
                SerialNumber = issued.SerialNumber,
                Subject = issued.Subject,
                Issuer = issued.Issuer,
                TemplateName = issued.TemplateName,
                NotBefore = issued.NotBefore,
                NotAfter = issued.NotAfter,
                Thumbprint = issued.Thumbprint,
                Status = "Active",
                RequestedBy = issued.RequestedBy,
                CertificatePem = certPem,
                SubjectAlternativeNames = issued.SubjectAlternativeNames,
                KeyUsage = issued.KeyUsage,
                EnhancedKeyUsage = issued.EnhancedKeyUsage,
            };

            await _certStore.SaveIssuedCertificateAsync(record);

            _logger.LogInformation("Certificate issued: Serial={Serial}, Subject={Subject}, Template={Template}",
                issued.SerialNumber, issued.Subject, issued.TemplateName);

            return issued;
        }
        finally
        {
            if (request.Csr == null || request.Csr.Length == 0)
            {
                subjectKey.Dispose();
            }
        }
    }

    /// <summary>
    /// Issues a certificate directly given an already-validated request and validation result.
    /// Shared by both <see cref="IssueCertificateAsync"/> and <see cref="ApprovePendingRequestAsync"/>.
    /// </summary>
    private async Task<IssuedCertificate> IssueCertificateDirectAsync(
        CertificateIssuanceRequest request, TemplateValidationResult validation)
    {
        // Generate key pair for the issued certificate (server-side generation)
        string privateKeyPem = null;
        RSA subjectKey;

        if (request.Csr != null && request.Csr.Length > 0)
        {
            subjectKey = ExtractPublicKeyFromCsr(request.Csr);
        }
        else
        {
            subjectKey = RSA.Create(validation.EffectiveKeySize);
            privateKeyPem = subjectKey.ExportRSAPrivateKeyPem();
        }

        try
        {
            var subjectName = new X500DistinguishedName(request.SubjectDn);
            var certRequest = new System.Security.Cryptography.X509Certificates.CertificateRequest(
                subjectName, subjectKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            certRequest.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(false, false, 0, true));

            var kuFlags = MapKeyUsageFlags(validation.KeyUsage);
            certRequest.CertificateExtensions.Add(
                new X509KeyUsageExtension(kuFlags, true));

            if (validation.EnhancedKeyUsage.Count > 0)
            {
                var ekuCollection = new OidCollection();
                foreach (var ekuOid in validation.EnhancedKeyUsage)
                    ekuCollection.Add(new Oid(ekuOid));
                certRequest.CertificateExtensions.Add(
                    new X509EnhancedKeyUsageExtension(ekuCollection, false));
            }

            if (request.SubjectAlternativeNames.Count > 0)
            {
                var sanBuilder = new SubjectAlternativeNameBuilder();
                foreach (var san in request.SubjectAlternativeNames)
                {
                    if (san.StartsWith("dns:", StringComparison.OrdinalIgnoreCase))
                        sanBuilder.AddDnsName(san[4..]);
                    else if (san.StartsWith("ip:", StringComparison.OrdinalIgnoreCase) &&
                             IPAddress.TryParse(san[3..], out var ip))
                        sanBuilder.AddIpAddress(ip);
                    else if (san.StartsWith("email:", StringComparison.OrdinalIgnoreCase))
                        sanBuilder.AddEmailAddress(san[6..]);
                    else
                        sanBuilder.AddDnsName(san);
                }
                certRequest.CertificateExtensions.Add(sanBuilder.Build());
            }

            certRequest.CertificateExtensions.Add(
                new X509SubjectKeyIdentifierExtension(certRequest.PublicKey, false));

            var akiExtension = X509AuthorityKeyIdentifierExtension.CreateFromCertificate(
                _caCertificate, includeKeyIdentifier: true, includeIssuerAndSerial: false);
            certRequest.CertificateExtensions.Add(akiExtension);

            var serialNumber = await GetNextSerialNumberAsync();
            var serialBytes = serialNumber.ToString("X16", CultureInfo.InvariantCulture);

            var notBefore = DateTimeOffset.UtcNow;
            var notAfter = notBefore.AddDays(validation.ValidityDays);

            using var caKey = _caCertificate.GetRSAPrivateKey();
            var serialBytesArray = Convert.FromHexString(serialBytes.PadLeft(16, '0'));

            var issuedCert = certRequest.Create(
                _caCertificate.SubjectName,
                X509SignatureGenerator.CreateForRSA(caKey, RSASignaturePadding.Pkcs1),
                notBefore,
                notAfter,
                serialBytesArray);

            var certPem = issuedCert.ExportCertificatePem();
            var kuDesc = kuFlags.ToString();

            var issued = new IssuedCertificate
            {
                SerialNumber = issuedCert.SerialNumber,
                Subject = issuedCert.Subject,
                Issuer = issuedCert.Issuer,
                TemplateName = request.TemplateName,
                NotBefore = notBefore,
                NotAfter = notAfter,
                Thumbprint = issuedCert.Thumbprint,
                Status = "Active",
                RequestedBy = request.RequestedBy ?? "",
                CertificatePem = certPem,
                PrivateKeyPem = privateKeyPem,
                SubjectAlternativeNames = request.SubjectAlternativeNames,
                KeyUsage = kuDesc,
                EnhancedKeyUsage = validation.EnhancedKeyUsage,
            };

            var record = new IssuedCertificateRecord
            {
                SerialNumber = issued.SerialNumber,
                Subject = issued.Subject,
                Issuer = issued.Issuer,
                TemplateName = issued.TemplateName,
                NotBefore = issued.NotBefore,
                NotAfter = issued.NotAfter,
                Thumbprint = issued.Thumbprint,
                Status = "Active",
                RequestedBy = issued.RequestedBy,
                CertificatePem = certPem,
                SubjectAlternativeNames = issued.SubjectAlternativeNames,
                KeyUsage = issued.KeyUsage,
                EnhancedKeyUsage = issued.EnhancedKeyUsage,
            };

            await _certStore.SaveIssuedCertificateAsync(record);

            _logger.LogInformation("Certificate issued: Serial={Serial}, Subject={Subject}, Template={Template}",
                issued.SerialNumber, issued.Subject, issued.TemplateName);

            return issued;
        }
        finally
        {
            if (request.Csr == null || request.Csr.Length == 0)
                subjectKey.Dispose();
        }
    }

    /// <summary>
    /// Revoke a certificate.
    /// </summary>
    public async Task RevokeCertificateAsync(string serialNumber, RevocationReason reason)
    {
        if (!IsInitialized)
            throw new InvalidOperationException("CA is not initialized");

        var record = await _certStore.GetIssuedCertificateAsync(serialNumber);
        if (record == null)
            throw new InvalidOperationException($"Certificate not found: {serialNumber}");

        if (record.Status == "Revoked")
            throw new InvalidOperationException($"Certificate is already revoked: {serialNumber}");

        record.Status = "Revoked";
        record.RevocationReason = reason;
        record.RevokedAt = DateTimeOffset.UtcNow;

        await _certStore.SaveIssuedCertificateAsync(record);

        _logger.LogInformation("Certificate revoked: Serial={Serial}, Reason={Reason}",
            serialNumber, reason);
    }

    /// <summary>
    /// Generate a Certificate Revocation List (CRL) in DER format.
    /// </summary>
    public byte[] GenerateCrl()
    {
        if (!IsInitialized || _caCertificate == null)
            throw new InvalidOperationException("CA is not initialized");

        // Build CRL using ASN.1
        // The .NET CRL builder is available via the CertificateRevocationListBuilder
        var crlBuilder = new CertificateRevocationListBuilder();

        // Load all revoked certificates synchronously (we cache them)
        var records = _certStore.ListIssuedCertificatesAsync().GetAwaiter().GetResult();
        foreach (var record in records.Where(r => r.Status == "Revoked"))
        {
            var serialBytes = Convert.FromHexString(record.SerialNumber);
            var revokedAt = record.RevokedAt ?? DateTimeOffset.UtcNow;
            crlBuilder.AddEntry(serialBytes, revokedAt);
        }

        using var caKey = _caCertificate.GetRSAPrivateKey()!;
        var nextUpdate = DateTimeOffset.UtcNow.AddDays(7);
        var crlNumber = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var crlBytes = crlBuilder.Build(
            _caCertificate.SubjectName,
            X509SignatureGenerator.CreateForRSA(caKey, RSASignaturePadding.Pkcs1),
            crlNumber,
            nextUpdate,
            HashAlgorithmName.SHA256,
            X509AuthorityKeyIdentifierExtension.CreateFromCertificate(
                _caCertificate, includeKeyIdentifier: true, includeIssuerAndSerial: false),
            thisUpdate: DateTimeOffset.UtcNow);

        return crlBytes;
    }

    /// <summary>
    /// List all issued certificates.
    /// </summary>
    public async Task<List<IssuedCertificate>> ListIssuedCertificatesAsync()
    {
        var records = await _certStore.ListIssuedCertificatesAsync();
        return records.Select(MapToIssuedCertificate).ToList();
    }

    /// <summary>
    /// Get a certificate by serial number.
    /// </summary>
    public async Task<IssuedCertificate> GetCertificateAsync(string serialNumber)
    {
        var record = await _certStore.GetIssuedCertificateAsync(serialNumber);
        return record != null ? MapToIssuedCertificate(record) : null;
    }

    /// <summary>
    /// List all pending certificate requests awaiting CA manager approval.
    /// </summary>
    public async Task<List<PendingCertificateRequest>> ListPendingRequestsAsync(CancellationToken ct = default)
    {
        var all = await _certStore.ListPendingRequestsAsync(ct);
        return all.Where(r => r.Status == "Pending").ToList();
    }

    /// <summary>
    /// Approve a pending certificate request. Issues the certificate using the stored request details.
    /// </summary>
    public async Task<IssuedCertificate> ApprovePendingRequestAsync(
        string requestId, string approvedBy, CancellationToken ct = default)
    {
        if (!IsInitialized)
            throw new InvalidOperationException("CA is not initialized");

        var pending = await _certStore.GetPendingRequestAsync(requestId, ct);
        if (pending == null)
            throw new InvalidOperationException($"Pending request not found: {requestId}");

        if (pending.Status != "Pending")
            throw new InvalidOperationException($"Request is not in Pending status: {pending.Status}");

        // Mark as approved
        pending.Status = "Approved";
        pending.ResolvedBy = approvedBy;
        pending.ResolvedAt = DateTimeOffset.UtcNow;
        await _certStore.SavePendingRequestAsync(pending, ct);

        // Reconstruct the issuance request from the pending record
        byte[] csr = null;
        if (!string.IsNullOrEmpty(pending.CsrBase64))
            csr = Convert.FromBase64String(pending.CsrBase64);

        var issuanceRequest = new CertificateIssuanceRequest
        {
            TemplateName = pending.TemplateName,
            SubjectDn = pending.SubjectDn,
            SubjectAlternativeNames = pending.SubjectAlternativeNames,
            RequestedBy = pending.RequestedBy,
            KeySizeOverride = pending.KeySizeOverride,
            Csr = csr,
        };

        // Load template and validate without the approval check
        var template = await _templateService.GetTemplateAsync(issuanceRequest.TemplateName, ct);
        if (template == null)
            throw new InvalidOperationException($"Certificate template not found: {issuanceRequest.TemplateName}");

        var validation = _templateService.ValidateRequest(issuanceRequest, template);
        if (!validation.IsValid)
            throw new InvalidOperationException(
                $"Certificate request validation failed: {string.Join("; ", validation.Errors)}");

        // Issue the certificate directly, bypassing the approval check since we are the approval
        var issued = await IssueCertificateDirectAsync(issuanceRequest, validation);

        _logger.LogInformation(
            "Pending request approved: RequestId={RequestId}, Serial={Serial}, ApprovedBy={ApprovedBy}",
            requestId, issued.SerialNumber, approvedBy);

        return issued;
    }

    /// <summary>
    /// Deny a pending certificate request.
    /// </summary>
    public async Task DenyPendingRequestAsync(
        string requestId, string deniedBy, string reason, CancellationToken ct = default)
    {
        var pending = await _certStore.GetPendingRequestAsync(requestId, ct);
        if (pending == null)
            throw new InvalidOperationException($"Pending request not found: {requestId}");

        if (pending.Status != "Pending")
            throw new InvalidOperationException($"Request is not in Pending status: {pending.Status}");

        pending.Status = "Denied";
        pending.ResolvedBy = deniedBy;
        pending.ResolvedAt = DateTimeOffset.UtcNow;
        pending.DenialReason = reason ?? "";

        await _certStore.SavePendingRequestAsync(pending, ct);

        _logger.LogInformation(
            "Pending request denied: RequestId={RequestId}, DeniedBy={DeniedBy}, Reason={Reason}",
            requestId, deniedBy, reason);
    }

    /// <summary>
    /// Renew a certificate: issue a new one with the same subject/template/SANs.
    /// </summary>
    public async Task<IssuedCertificate> RenewCertificateAsync(string serialNumber)
    {
        var record = await _certStore.GetIssuedCertificateAsync(serialNumber);
        if (record == null)
            throw new InvalidOperationException($"Certificate not found: {serialNumber}");

        if (record.Status == "Revoked")
            throw new InvalidOperationException("Cannot renew a revoked certificate");

        // Issue a new certificate with the same parameters
        var request = new CertificateIssuanceRequest
        {
            TemplateName = record.TemplateName,
            SubjectDn = record.Subject,
            SubjectAlternativeNames = record.SubjectAlternativeNames,
            RequestedBy = record.RequestedBy,
        };

        // Supersede the old certificate
        record.Status = "Revoked";
        record.RevocationReason = CertificateAuthority.RevocationReason.Superseded;
        record.RevokedAt = DateTimeOffset.UtcNow;
        await _certStore.SaveIssuedCertificateAsync(record);

        return await IssueCertificateAsync(request);
    }

    // ── Private helpers ──────────────────────────────────

    private async Task<long> GetNextSerialNumberAsync()
    {
        if (_caState == null) throw new InvalidOperationException("CA not initialized");

        var serial = _caState.NextSerialNumber++;
        await _certStore.SaveCaStateAsync(_caState);
        return serial;
    }

    private static X509KeyUsageFlags MapKeyUsageFlags(int templateKeyUsage)
    {
        var flags = X509KeyUsageFlags.None;

        if ((templateKeyUsage & 0x80) != 0) flags |= X509KeyUsageFlags.DigitalSignature;
        if ((templateKeyUsage & 0x40) != 0) flags |= X509KeyUsageFlags.NonRepudiation;
        if ((templateKeyUsage & 0x20) != 0) flags |= X509KeyUsageFlags.KeyEncipherment;
        if ((templateKeyUsage & 0x10) != 0) flags |= X509KeyUsageFlags.DataEncipherment;
        if ((templateKeyUsage & 0x08) != 0) flags |= X509KeyUsageFlags.KeyAgreement;
        if ((templateKeyUsage & 0x04) != 0) flags |= X509KeyUsageFlags.KeyCertSign;
        if ((templateKeyUsage & 0x02) != 0) flags |= X509KeyUsageFlags.CrlSign;

        // Default if nothing set
        if (flags == X509KeyUsageFlags.None)
            flags = X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment;

        return flags;
    }

    private static RSA ExtractPublicKeyFromCsr(byte[] csrBytes)
    {
        // Parse PKCS#10 CSR to extract the public key
        // .NET 9 doesn't have a built-in CSR parser, so we use a basic approach:
        // Load via CertificateRequest.LoadSigningRequestPem if available,
        // or fall back to creating a key from the CSR data
        try
        {
            var pem = System.Text.Encoding.UTF8.GetString(csrBytes);
            if (pem.Contains("BEGIN CERTIFICATE REQUEST"))
            {
                var info = System.Security.Cryptography.X509Certificates.CertificateRequest
                    .LoadSigningRequestPem(pem, HashAlgorithmName.SHA256, CertificateRequestLoadOptions.Default,
                        RSASignaturePadding.Pkcs1);
                // Extract the public key from the loaded request
                var pubKeyBytes = info.PublicKey.ExportSubjectPublicKeyInfo();
                var rsa = RSA.Create();
                rsa.ImportSubjectPublicKeyInfo(pubKeyBytes, out _);
                return rsa;
            }
        }
        catch
        {
            // Fall through to DER attempt
        }

        // Try DER-encoded CSR
        throw new InvalidOperationException("Unable to parse the provided CSR. Provide a PEM-encoded PKCS#10 CSR.");
    }

    private static IssuedCertificate MapToIssuedCertificate(IssuedCertificateRecord record)
    {
        // Determine effective status (check expiry)
        var status = record.Status;
        if (status == "Active" && record.NotAfter < DateTimeOffset.UtcNow)
            status = "Expired";

        return new IssuedCertificate
        {
            SerialNumber = record.SerialNumber,
            Subject = record.Subject,
            Issuer = record.Issuer,
            TemplateName = record.TemplateName,
            NotBefore = record.NotBefore,
            NotAfter = record.NotAfter,
            Thumbprint = record.Thumbprint,
            Status = status,
            RevocationReason = record.RevocationReason,
            RevokedAt = record.RevokedAt,
            RequestedBy = record.RequestedBy,
            CertificatePem = record.CertificatePem,
            SubjectAlternativeNames = record.SubjectAlternativeNames,
            KeyUsage = record.KeyUsage,
            EnhancedKeyUsage = record.EnhancedKeyUsage,
        };
    }
}
