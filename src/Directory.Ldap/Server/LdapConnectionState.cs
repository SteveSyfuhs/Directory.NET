using System.Collections.Concurrent;
using System.Security.Cryptography.X509Certificates;
using Directory.Ldap.Handlers;

namespace Directory.Ldap.Server;

/// <summary>
/// Per-connection state for an LDAP session.
/// </summary>
public class LdapConnectionState
{
    public BindState BindStatus { get; set; } = BindState.Anonymous;
    public string BoundDn { get; set; }
    public string BoundSid { get; set; }
    public string TenantId { get; set; } = "default";
    public string DomainDn { get; set; } = string.Empty;
    public bool IsTls { get; set; }
    public bool IsGlobalCatalog { get; set; }

    /// <summary>
    /// The client certificate presented during TLS handshake (mutual TLS).
    /// Used by the EXTERNAL SASL mechanism to map the certificate identity to a directory user.
    /// </summary>
    public X509Certificate2 ClientCertificate { get; set; }
    public string RemoteEndPoint { get; set; } = string.Empty;

    /// <summary>
    /// Cached transitive group SIDs for the bound principal, including well-known SIDs.
    /// Populated at bind time and reused for all ACL checks on this connection.
    /// </summary>
    public IReadOnlySet<string> GroupSids { get; set; }

    /// <summary>
    /// When true, the connection is in FastBind mode (1.2.840.113556.1.4.1781).
    /// Subsequent binds only validate credentials without full session setup.
    /// Used by applications that just need to verify passwords.
    /// </summary>
    public bool FastBindMode { get; set; }

    /// <summary>
    /// Server challenge bytes for in-progress NTLM authentication.
    /// </summary>
    public byte[] NtlmChallenge { get; set; }

    /// <summary>
    /// State for an in-progress GSSAPI SASL multi-step exchange.
    /// Non-null between the initial AP-REQ validation and security layer negotiation completion.
    /// </summary>
    public GssapiSessionState GssapiState { get; set; }

    /// <summary>
    /// Active operations by message ID for Abandon support.
    /// </summary>
    public ConcurrentDictionary<int, CancellationTokenSource> ActiveOperations { get; } = new();

    /// <summary>
    /// Continuation tokens for paged result searches, keyed by a cookie identifier.
    /// </summary>
    public ConcurrentDictionary<string, string> PagedSearchTokens { get; } = new();

    public CancellationTokenSource RegisterOperation(int messageId)
    {
        var cts = new CancellationTokenSource();
        ActiveOperations[messageId] = cts;
        return cts;
    }

    public void CompleteOperation(int messageId)
    {
        if (ActiveOperations.TryRemove(messageId, out var cts))
            cts.Dispose();
    }

    public bool TryAbandonOperation(int messageId)
    {
        if (ActiveOperations.TryRemove(messageId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
            return true;
        }
        return false;
    }
}

public enum BindState
{
    Anonymous,
    SimpleBound,
    SaslBound,
}
