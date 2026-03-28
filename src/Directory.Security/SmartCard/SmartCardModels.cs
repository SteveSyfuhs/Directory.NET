namespace Directory.Security.SmartCard;

public class SmartCardMapping
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserDn { get; set; } = "";
    public string CertificateSubject { get; set; } = "";
    public string CertificateIssuer { get; set; } = "";
    public string CertificateThumbprint { get; set; } = "";
    public string Upn { get; set; }  // From SAN
    public MappingType Type { get; set; }
    public DateTimeOffset MappedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool IsEnabled { get; set; } = true;
}

public enum MappingType
{
    ExplicitMapping,           // Admin manually maps cert to user
    SubjectMapping,            // Match cert subject to user DN
    IssuerAndSubjectMapping,   // Match issuer + subject
    UpnMapping,                // Match UPN in SAN to user UPN
    SubjectAlternativeNameMapping // Match SAN email to user email
}

public class SmartCardSettings
{
    public bool Enabled { get; set; }
    public MappingType DefaultMappingType { get; set; } = MappingType.UpnMapping;
    public bool RequireSmartCardLogon { get; set; }
    public bool ValidateCertificateChain { get; set; } = true;
    public bool CheckRevocation { get; set; } = true;
    public List<string> TrustedCAs { get; set; } = new(); // Thumbprints of trusted CA certs
    public DateTimeOffset ModifiedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Certificate data sent by the client for smart card authentication.
/// Contains the DER-encoded or PEM certificate data.
/// </summary>
public class CertificateAuthRequest
{
    public string CertificateData { get; set; } = ""; // Base64-encoded DER certificate
}

public class SmartCardAuthResult
{
    public bool Success { get; set; }
    public string UserDn { get; set; }
    public string Error { get; set; }
    public string MappingType { get; set; }
}

public class CreateMappingRequest
{
    public string UserDn { get; set; } = "";
    public string CertificateData { get; set; } = ""; // Base64-encoded DER certificate
    public MappingType? MappingType { get; set; }
}
