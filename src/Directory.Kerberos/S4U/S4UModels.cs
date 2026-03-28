namespace Directory.Kerberos.S4U;

/// <summary>
/// Holds delegation constraint attributes for a service principal,
/// used by S4U2Self, S4U2Proxy, and RBCD validation.
/// </summary>
public class DelegationConstraints
{
    /// <summary>
    /// When true the service may use S4U2Self (protocol transition).
    /// Corresponds to the TRUSTED_TO_AUTH_FOR_DELEGATION UAC flag (0x1000000).
    /// </summary>
    public bool TrustedToAuthForDelegation { get; set; }

    /// <summary>
    /// Traditional constrained delegation SPN allow-list (msDS-AllowedToDelegateTo).
    /// The service may use S4U2Proxy to obtain tickets only to these SPNs.
    /// </summary>
    public List<string> AllowedToDelegateTo { get; set; } = new();

    /// <summary>
    /// Resource-based constrained delegation SID allow-list.
    /// Derived from msDS-AllowedToActOnBehalfOfOtherIdentity on the target resource.
    /// Contains the SIDs of services that are permitted to delegate to the target.
    /// </summary>
    public List<string> AllowedToActOnBehalf { get; set; } = new();
}

/// <summary>
/// Result of an S4U2Self protocol transition request.
/// </summary>
public class S4USelfResult
{
    public bool Success { get; init; }
    public string ErrorMessage { get; init; }

    /// <summary>
    /// The resolved client principal name (user on whose behalf the ticket was requested).
    /// </summary>
    public string ClientPrincipalName { get; init; }

    /// <summary>
    /// The distinguished name of the resolved client object.
    /// </summary>
    public string ClientDn { get; init; }

    /// <summary>
    /// The SID of the resolved client object, used for PAC generation.
    /// </summary>
    public string ClientSid { get; init; }

    public static S4USelfResult Allowed(string principalName, string dn, string sid) => new()
    {
        Success = true,
        ClientPrincipalName = principalName,
        ClientDn = dn,
        ClientSid = sid,
    };

    public static S4USelfResult Denied(string reason) => new()
    {
        Success = false,
        ErrorMessage = reason,
    };
}

/// <summary>
/// Result of an S4U2Proxy constrained delegation request.
/// </summary>
public class S4UProxyResult
{
    public bool Success { get; init; }
    public string ErrorMessage { get; init; }

    /// <summary>
    /// The target SPN that was authorized for delegation.
    /// </summary>
    public string TargetSpn { get; init; }

    /// <summary>
    /// Whether authorization was granted via traditional constrained delegation
    /// or resource-based constrained delegation.
    /// </summary>
    public S4UProxyGrantType GrantType { get; init; }

    public static S4UProxyResult Allowed(string targetSpn, S4UProxyGrantType grantType) => new()
    {
        Success = true,
        TargetSpn = targetSpn,
        GrantType = grantType,
    };

    public static S4UProxyResult Denied(string reason) => new()
    {
        Success = false,
        ErrorMessage = reason,
    };
}

public enum S4UProxyGrantType
{
    None = 0,

    /// <summary>Traditional constrained delegation (msDS-AllowedToDelegateTo).</summary>
    ConstrainedDelegation,

    /// <summary>Resource-based constrained delegation (msDS-AllowedToActOnBehalfOfOtherIdentity).</summary>
    ResourceBased,
}

/// <summary>
/// Identifies the type of S4U operation being processed.
/// </summary>
public enum S4UOperationType
{
    /// <summary>
    /// S4U2Self (protocol transition): a service requests a ticket to itself
    /// on behalf of a user via PA-FOR-USER padata.
    /// </summary>
    S4U2Self,

    /// <summary>
    /// S4U2Proxy (constrained delegation): a service uses an evidence ticket
    /// to request a ticket to another service on behalf of a user.
    /// </summary>
    S4U2Proxy,
}

/// <summary>
/// Ambient context for S4U operations, flowed via <c>AsyncLocal</c> through
/// <see cref="CosmosPrincipalService.SetS4UContext"/> during TGS-REQ processing.
///
/// Because the Kerberos.NET KDC pipeline calls <c>IPrincipalService.Find()</c> without
/// passing S4U metadata, this context carries the requesting service's identity and
/// delegation flags so that <see cref="CosmosPrincipalService"/> can enforce policy
/// (TRUSTED_TO_AUTH_FOR_DELEGATION, NOT_DELEGATED, msDS-AllowedToDelegateTo, RBCD)
/// at principal resolution time.
/// </summary>
public class S4URequestContext
{
    /// <summary>The type of S4U operation (Self or Proxy).</summary>
    public S4UOperationType OperationType { get; init; }

    /// <summary>
    /// The Kerberos principal name of the requesting service (e.g., "HTTP/web.contoso.com").
    /// Used for logging and diagnostics.
    /// </summary>
    public string ServicePrincipalName { get; init; }

    /// <summary>
    /// The distinguished name of the requesting service account in the directory.
    /// Used to look up delegation attributes (msDS-AllowedToDelegateTo, RBCD).
    /// </summary>
    public string ServiceDn { get; init; }

    /// <summary>
    /// Whether the requesting service has the TRUSTED_TO_AUTH_FOR_DELEGATION UAC flag (0x1000000).
    /// Pre-computed when the S4U context is created to avoid redundant directory lookups.
    /// </summary>
    public bool ServiceHasTrustedToAuthForDelegation { get; init; }
}
