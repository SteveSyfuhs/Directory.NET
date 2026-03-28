namespace Directory.Core.Interfaces;

/// <summary>
/// Handles password validation, hashing, and Kerberos key derivation.
/// </summary>
public interface IPasswordPolicy
{
    /// <summary>
    /// Validate a password against the user's stored credentials.
    /// </summary>
    Task<bool> ValidatePasswordAsync(string tenantId, string dn, string password, CancellationToken ct = default);

    /// <summary>
    /// Set a new password for a user, computing NT hash and Kerberos keys.
    /// </summary>
    Task SetPasswordAsync(string tenantId, string dn, string password, CancellationToken ct = default);

    /// <summary>
    /// Validate that a password meets complexity requirements.
    /// </summary>
    bool MeetsComplexityRequirements(string password, string samAccountName = null);

    /// <summary>
    /// Compute the NT hash (MD4 of UTF-16LE) of a password.
    /// </summary>
    byte[] ComputeNTHash(string password);

    /// <summary>
    /// Derive Kerberos long-term credential keys from a password.
    /// </summary>
    List<KerberosKeyData> DeriveKerberosKeys(string principalName, string password, string realm);
}

public class KerberosKeyData
{
    public int EncryptionType { get; init; }
    public byte[] KeyValue { get; init; } = [];
    public string EncryptionTypeName { get; init; } = string.Empty;
}
