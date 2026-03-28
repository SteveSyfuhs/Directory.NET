namespace Directory.Rpc.Dispatch;

/// <summary>
/// Per-request context passed to RPC interface handlers, containing authentication
/// state, tenant info, and the connection's context handle table.
/// </summary>
public class RpcCallContext
{
    /// <summary>
    /// The SID of the authenticated user, or null if anonymous.
    /// </summary>
    public string AuthenticatedSid { get; set; }

    /// <summary>
    /// The sAMAccountName of the authenticated user, or null if anonymous.
    /// </summary>
    public string AuthenticatedUser { get; set; }

    /// <summary>
    /// The tenant ID for multi-tenant directory isolation.
    /// </summary>
    public string TenantId { get; set; } = "default";

    /// <summary>
    /// The distinguished name of the domain partition (e.g., "DC=directory,DC=net").
    /// </summary>
    public string DomainDn { get; set; } = "";

    /// <summary>
    /// Per-connection context handle table shared across all calls on the same connection.
    /// </summary>
    public ContextHandleTable ContextHandles { get; set; } = new();

    /// <summary>
    /// The NTLM session key derived during authentication, used for sealing/signing.
    /// </summary>
    public byte[] SessionKey { get; set; }
}
