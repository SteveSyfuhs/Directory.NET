using Directory.Ldap.Protocol;

namespace Directory.Ldap.Server;

/// <summary>
/// Abstraction for writing LDAP response messages to the connection.
/// </summary>
public interface ILdapResponseWriter
{
    /// <summary>
    /// Write a complete LDAP message response.
    /// </summary>
    Task WriteMessageAsync(int messageId, byte[] protocolOpData, List<LdapControl> controls = null, CancellationToken ct = default);

    /// <summary>
    /// Write raw bytes to the connection.
    /// </summary>
    Task WriteBytesAsync(byte[] data, CancellationToken ct = default);
}

/// <summary>
/// Implemented by connection handlers that support in-place TLS upgrade (StartTLS).
/// After the ExtendedResponse has been flushed, the handler upgrades the underlying
/// TCP stream to TLS and sets IsTls = true on the connection state.
/// </summary>
public interface IStartTlsUpgradeable
{
    /// <summary>
    /// Perform TLS upgrade on the connection.
    /// Returns false if no server certificate is configured.
    /// </summary>
    Task<bool> UpgradeToTlsAsync(CancellationToken ct);
}
