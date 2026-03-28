namespace Directory.Ldap.Client;

/// <summary>
/// Configuration options for TLS certificate validation when connecting to LDAP servers.
/// Controls how the client validates the server certificate during the TLS handshake.
/// </summary>
public class LdapClientTlsOptions
{
    /// <summary>
    /// When true, allows self-signed certificates without chain validation.
    /// Intended for development and testing environments only.
    /// Default: false.
    /// </summary>
    public bool AllowSelfSignedCertificates { get; set; }

    /// <summary>
    /// When true, skips all certificate validation and accepts any server certificate.
    /// This is insecure and should only be used in isolated development environments.
    /// Default: false.
    /// </summary>
    public bool SkipCertificateValidation { get; set; }

    /// <summary>
    /// When true, checks certificate revocation status via CRL or OCSP if
    /// revocation information is available in the certificate. If revocation
    /// information is not available, validation continues without revocation checking.
    /// Default: true.
    /// </summary>
    public bool CheckRevocation { get; set; } = true;

    /// <summary>
    /// Additional trusted CA certificates in PEM format to use when building the
    /// certificate chain. These are used in addition to the system trust store.
    /// Typically populated from the directory's AD CS root CA certificate.
    /// </summary>
    public List<string> TrustedCaCertificatePems { get; set; } = new();
}
