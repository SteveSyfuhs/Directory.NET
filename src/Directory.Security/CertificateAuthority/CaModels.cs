using System.Security.Cryptography;

namespace Directory.Security.CertificateAuthority;

public class CaInitRequest
{
    public string CommonName { get; set; } = "Directory.NET Certificate Authority";
    public string Organization { get; set; }
    public string Country { get; set; }
    public int ValidityYears { get; set; } = 10;
    public int KeySizeInBits { get; set; } = 4096;
    public HashAlgorithmName HashAlgorithm { get; set; } = HashAlgorithmName.SHA256;
}

public class CaInfo
{
    public string CommonName { get; set; } = "";
    public string Subject { get; set; } = "";
    public string SerialNumber { get; set; } = "";
    public string Thumbprint { get; set; } = "";
    public DateTimeOffset NotBefore { get; set; }
    public DateTimeOffset NotAfter { get; set; }
    public string PublicKeyAlgorithm { get; set; } = "";
    public int KeySize { get; set; }
    public bool IsInitialized { get; set; }
    public string CertificatePem { get; set; } = "";
}

public class CertificateIssuanceRequest
{
    public string TemplateName { get; set; } = "";
    public string SubjectDn { get; set; } = "";
    public List<string> SubjectAlternativeNames { get; set; } = [];
    public string RequestedBy { get; set; }
    public int? KeySizeOverride { get; set; }
    public byte[] Csr { get; set; }
}

public class IssuedCertificate
{
    public string SerialNumber { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Issuer { get; set; } = "";
    public string TemplateName { get; set; } = "";
    public DateTimeOffset NotBefore { get; set; }
    public DateTimeOffset NotAfter { get; set; }
    public string Thumbprint { get; set; } = "";
    public string Status { get; set; } = "Active";
    public RevocationReason? RevocationReason { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string RequestedBy { get; set; } = "";
    public string CertificatePem { get; set; } = "";
    public string PrivateKeyPem { get; set; }
    public List<string> SubjectAlternativeNames { get; set; } = [];
    public string KeyUsage { get; set; } = "";
    public List<string> EnhancedKeyUsage { get; set; } = [];
}

/// <summary>
/// Represents a certificate request pending CA manager approval.
/// Created when a template has RequireApproval=true.
/// </summary>
public class PendingCertificateRequest
{
    public string RequestId { get; set; } = "";
    public string TemplateName { get; set; } = "";
    public string SubjectDn { get; set; } = "";
    public List<string> SubjectAlternativeNames { get; set; } = [];
    public string RequestedBy { get; set; } = "";
    public int? KeySizeOverride { get; set; }
    public string CsrBase64 { get; set; }
    public DateTimeOffset SubmittedAt { get; set; }
    public string Status { get; set; } = "Pending"; // Pending, Approved, Denied
    public string ResolvedBy { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public string DenialReason { get; set; }
}

public enum RevocationReason
{
    Unspecified = 0,
    KeyCompromise = 1,
    CaCompromise = 2,
    AffiliationChanged = 3,
    Superseded = 4,
    CessationOfOperation = 5,
}
