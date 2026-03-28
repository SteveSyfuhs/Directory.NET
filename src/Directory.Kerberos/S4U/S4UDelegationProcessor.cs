using Microsoft.Extensions.Logging;

namespace Directory.Kerberos.S4U;

/// <summary>
/// Orchestrates the complete S4U delegation flow by coordinating the individual handlers.
///
/// For S4U2Self: validates via <see cref="S4USelfHandler"/>.
/// For S4U2Proxy: first attempts traditional constrained delegation via <see cref="S4UProxyHandler"/>,
/// then falls back to resource-based constrained delegation via <see cref="RbcdHandler"/>.
///
/// This processor is intended to be called from the TGS-REQ pipeline when S4U padata
/// is detected. It replaces the policy-free behavior documented in the integration gap
/// on the existing <see cref="S4UService"/>.
/// </summary>
public class S4UDelegationProcessor
{
    private readonly S4USelfHandler _selfHandler;
    private readonly S4UProxyHandler _proxyHandler;
    private readonly RbcdHandler _rbcdHandler;
    private readonly ILogger<S4UDelegationProcessor> _logger;

    public S4UDelegationProcessor(
        S4USelfHandler selfHandler,
        S4UProxyHandler proxyHandler,
        RbcdHandler rbcdHandler,
        ILogger<S4UDelegationProcessor> logger)
    {
        _selfHandler = selfHandler;
        _proxyHandler = proxyHandler;
        _rbcdHandler = rbcdHandler;
        _logger = logger;
    }

    /// <summary>
    /// Process an S4U2Self request (protocol transition).
    /// A service requests a ticket to itself on behalf of a user identified by
    /// the PA-FOR-USER padata in the TGS-REQ.
    /// </summary>
    /// <param name="tenantId">Tenant identifier.</param>
    /// <param name="serviceDn">DN of the requesting service principal.</param>
    /// <param name="clientPrincipalName">
    /// User name from PA-FOR-USER (e.g., "user@REALM" or bare sAMAccountName).
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    public Task<S4USelfResult> ProcessS4U2SelfAsync(
        string tenantId,
        string serviceDn,
        string clientPrincipalName,
        CancellationToken ct = default)
    {
        _logger.LogDebug(
            "Processing S4U2Self: service={Service}, client={Client}",
            serviceDn, clientPrincipalName);

        return _selfHandler.ValidateAsync(tenantId, serviceDn, clientPrincipalName, ct);
    }

    /// <summary>
    /// Process an S4U2Proxy request (constrained delegation).
    /// A service uses an evidence ticket to request a ticket to another service
    /// on behalf of a user.
    ///
    /// First attempts traditional constrained delegation (msDS-AllowedToDelegateTo),
    /// then falls back to RBCD (msDS-AllowedToActOnBehalfOfOtherIdentity on the target).
    /// </summary>
    /// <param name="tenantId">Tenant identifier.</param>
    /// <param name="serviceDn">DN of the requesting service (middle-tier).</param>
    /// <param name="targetSpn">SPN of the target back-end service.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<S4UProxyResult> ProcessS4U2ProxyAsync(
        string tenantId,
        string serviceDn,
        string targetSpn,
        CancellationToken ct = default)
    {
        _logger.LogDebug(
            "Processing S4U2Proxy: service={Service}, target={Target}",
            serviceDn, targetSpn);

        // Try traditional constrained delegation first
        var proxyResult = await _proxyHandler.ValidateAsync(tenantId, serviceDn, targetSpn, ct);
        if (proxyResult.Success)
            return proxyResult;

        _logger.LogDebug(
            "Traditional constrained delegation denied, trying RBCD: service={Service}, target={Target}",
            serviceDn, targetSpn);

        // Fall back to resource-based constrained delegation
        var rbcdResult = await _rbcdHandler.ValidateAsync(tenantId, serviceDn, targetSpn, ct);
        if (rbcdResult.Success)
            return rbcdResult;

        // Both denied — return the RBCD result which has the more specific reason
        _logger.LogWarning(
            "S4U2Proxy denied by both constrained delegation and RBCD: {Service} -> {Target}",
            serviceDn, targetSpn);

        return S4UProxyResult.Denied(
            $"Delegation to '{targetSpn}' denied: not in msDS-AllowedToDelegateTo and not authorized via RBCD");
    }

    /// <summary>
    /// Retrieve the delegation constraints for a service account.
    /// Useful for displaying delegation configuration in the management UI.
    /// </summary>
    public Task<DelegationConstraints> GetDelegationConstraintsAsync(
        string tenantId,
        string serviceDn,
        CancellationToken ct = default)
    {
        return _rbcdHandler.GetConstraintsAsync(tenantId, serviceDn, ct);
    }
}
