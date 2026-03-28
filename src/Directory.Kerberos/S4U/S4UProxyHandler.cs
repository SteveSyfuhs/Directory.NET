using Directory.Core.Interfaces;
using Directory.Core.Models;
using Microsoft.Extensions.Logging;

namespace Directory.Kerberos.S4U;

/// <summary>
/// Implements Service-for-User-to-Proxy (S4U2Proxy) per MS-SFU section 3.1.5.1.2.
///
/// S4U2Proxy allows a service to obtain a service ticket to another service on behalf
/// of a user, using an evidence ticket (typically obtained via S4U2Self) as proof of
/// the user's identity.
///
/// The handler validates traditional constrained delegation by checking whether the
/// target SPN appears in the requesting service's msDS-AllowedToDelegateTo list.
///
/// Input context: A TGS-REQ with additional-tickets containing the evidence ticket
///                from S4U2Self, targeted at a different service.
/// Output: An <see cref="S4UProxyResult"/> indicating whether the delegation is permitted.
///
/// Resource-based constrained delegation (RBCD) is handled by <see cref="RbcdHandler"/>
/// and should be tried as a fallback when this handler denies the request.
/// </summary>
public class S4UProxyHandler
{
    private readonly IDirectoryStore _store;
    private readonly ILogger<S4UProxyHandler> _logger;

    public S4UProxyHandler(IDirectoryStore store, ILogger<S4UProxyHandler> logger)
    {
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// Validate an S4U2Proxy request using traditional constrained delegation.
    /// The requesting service must have the target SPN listed in its
    /// msDS-AllowedToDelegateTo attribute.
    /// </summary>
    /// <param name="tenantId">Tenant identifier.</param>
    /// <param name="serviceDn">DN of the requesting service (the middle-tier).</param>
    /// <param name="targetSpn">
    /// The SPN of the back-end service the requesting service wants to delegate to.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// An <see cref="S4UProxyResult"/> indicating whether delegation to the target is allowed.
    /// </returns>
    public async Task<S4UProxyResult> ValidateAsync(
        string tenantId,
        string serviceDn,
        string targetSpn,
        CancellationToken ct = default)
    {
        // ── 1. Resolve the requesting service ───────────────────────────
        var service = await _store.GetByDnAsync(tenantId, serviceDn, ct);
        if (service == null)
        {
            _logger.LogWarning("S4U2Proxy denied: service {ServiceDn} not found", serviceDn);
            return S4UProxyResult.Denied("Service account not found");
        }

        // ── 2. Check msDS-AllowedToDelegateTo ──────────────────────────
        if (service.MsDsAllowedToDelegateTo.Count == 0)
        {
            _logger.LogDebug(
                "S4U2Proxy: service {ServiceDn} has no constrained delegation targets configured",
                serviceDn);
            return S4UProxyResult.Denied(
                $"Service has no constrained delegation targets (msDS-AllowedToDelegateTo is empty)");
        }

        // Case-insensitive match per AD SPN comparison rules
        var allowed = service.MsDsAllowedToDelegateTo.Any(spn =>
            spn.Equals(targetSpn, StringComparison.OrdinalIgnoreCase));

        if (!allowed)
        {
            _logger.LogWarning(
                "S4U2Proxy denied: {ServiceDn} not allowed to delegate to {TargetSpn}. " +
                "Allowed targets: [{Targets}]",
                serviceDn, targetSpn,
                string.Join(", ", service.MsDsAllowedToDelegateTo));
            return S4UProxyResult.Denied(
                $"Target SPN '{targetSpn}' is not in the service's msDS-AllowedToDelegateTo list");
        }

        // ── 3. Approved via traditional constrained delegation ──────────
        _logger.LogInformation(
            "S4U2Proxy approved via constrained delegation: {Service} -> {Target}",
            serviceDn, targetSpn);

        return S4UProxyResult.Allowed(targetSpn, S4UProxyGrantType.ConstrainedDelegation);
    }
}
