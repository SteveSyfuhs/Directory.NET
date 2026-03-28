using Directory.Core.Interfaces;
using Directory.Core.Models;
using Microsoft.Extensions.Logging;

namespace Directory.Kerberos.S4U;

/// <summary>
/// Implements Resource-Based Constrained Delegation (RBCD) validation per MS-SFU / MS-ADTS.
///
/// Unlike traditional constrained delegation where the front-end service lists its allowed
/// targets, RBCD inverts the trust model: the back-end resource controls who may delegate
/// to it via its msDS-AllowedToActOnBehalfOfOtherIdentity attribute. This attribute stores
/// a security descriptor whose DACL contains AccessAllowed ACEs keyed by the front-end
/// service's SID.
///
/// RBCD is tried as a fallback when traditional S4U2Proxy (msDS-AllowedToDelegateTo) fails.
/// It does NOT require the requesting service to have TRUSTED_FOR_DELEGATION or
/// TRUSTED_TO_AUTH_FOR_DELEGATION flags — only that the target resource explicitly trusts it.
/// </summary>
public class RbcdHandler
{
    private readonly IDirectoryStore _store;
    private readonly ILogger<RbcdHandler> _logger;

    public RbcdHandler(IDirectoryStore store, ILogger<RbcdHandler> logger)
    {
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// Validate whether the requesting service is permitted to delegate to the target SPN
    /// via resource-based constrained delegation.
    /// </summary>
    /// <param name="tenantId">Tenant identifier.</param>
    /// <param name="serviceDn">DN of the requesting (front-end) service.</param>
    /// <param name="targetSpn">SPN of the back-end resource being delegated to.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An <see cref="S4UProxyResult"/> indicating whether RBCD delegation is allowed.</returns>
    public async Task<S4UProxyResult> ValidateAsync(
        string tenantId,
        string serviceDn,
        string targetSpn,
        CancellationToken ct = default)
    {
        // ── 1. Resolve the requesting service and obtain its SID ────────
        var service = await _store.GetByDnAsync(tenantId, serviceDn, ct);
        if (service == null)
        {
            _logger.LogWarning("RBCD denied: service {ServiceDn} not found", serviceDn);
            return S4UProxyResult.Denied("Service account not found");
        }

        if (string.IsNullOrEmpty(service.ObjectSid))
        {
            _logger.LogWarning("RBCD denied: service {ServiceDn} has no objectSid", serviceDn);
            return S4UProxyResult.Denied("Service account has no SID");
        }

        // ── 2. Resolve the target service by SPN ────────────────────────
        var targetResults = await _store.GetByServicePrincipalNameAsync(tenantId, targetSpn, ct);
        if (targetResults.Count == 0)
        {
            _logger.LogWarning("RBCD denied: target SPN {TargetSpn} not found", targetSpn);
            return S4UProxyResult.Denied($"Target SPN '{targetSpn}' not found in directory");
        }

        var target = targetResults[0];

        // ── 3. Check msDS-AllowedToActOnBehalfOfOtherIdentity ───────────
        if (target.MsDsAllowedToActOnBehalf == null || target.MsDsAllowedToActOnBehalf.Length == 0)
        {
            _logger.LogDebug(
                "RBCD denied: target {TargetSpn} has no msDS-AllowedToActOnBehalfOfOtherIdentity",
                targetSpn);
            return S4UProxyResult.Denied(
                $"Target resource has no RBCD configuration (msDS-AllowedToActOnBehalfOfOtherIdentity is empty)");
        }

        // Parse the security descriptor and check the DACL for the service's SID
        try
        {
            var sd = SecurityDescriptor.Deserialize(target.MsDsAllowedToActOnBehalf);
            if (sd?.Dacl == null)
            {
                _logger.LogDebug("RBCD denied: target {TargetSpn} RBCD SD has no DACL", targetSpn);
                return S4UProxyResult.Denied("Target RBCD security descriptor has no DACL");
            }

            foreach (var ace in sd.Dacl.Aces)
            {
                if (ace.Type == AceType.AccessAllowed &&
                    ace.TrusteeSid.Equals(service.ObjectSid, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation(
                        "S4U2Proxy approved via RBCD: {Service} (SID={Sid}) -> {Target}",
                        serviceDn, service.ObjectSid, targetSpn);

                    return S4UProxyResult.Allowed(targetSpn, S4UProxyGrantType.ResourceBased);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to parse RBCD security descriptor on {TargetSpn}", targetSpn);
            return S4UProxyResult.Denied(
                $"Failed to parse RBCD security descriptor on target: {ex.Message}");
        }

        _logger.LogWarning(
            "RBCD denied: service SID {Sid} not found in target {TargetSpn} RBCD ACL",
            service.ObjectSid, targetSpn);
        return S4UProxyResult.Denied(
            $"Service SID '{service.ObjectSid}' is not authorized in target's RBCD configuration");
    }

    /// <summary>
    /// Retrieve the delegation constraints for a given service account,
    /// combining its own attributes with the RBCD configuration of a specific target.
    /// </summary>
    public async Task<DelegationConstraints> GetConstraintsAsync(
        string tenantId,
        string serviceDn,
        CancellationToken ct = default)
    {
        var service = await _store.GetByDnAsync(tenantId, serviceDn, ct);
        if (service == null)
            return new DelegationConstraints();

        const int UAC_TRUSTED_TO_AUTH_FOR_DELEGATION = 0x1000000;

        return new DelegationConstraints
        {
            TrustedToAuthForDelegation =
                (service.UserAccountControl & UAC_TRUSTED_TO_AUTH_FOR_DELEGATION) != 0,
            AllowedToDelegateTo = service.MsDsAllowedToDelegateTo.ToList(),
            AllowedToActOnBehalf = ExtractRbcdSids(service.MsDsAllowedToActOnBehalf),
        };
    }

    /// <summary>
    /// Extract SIDs from a serialized security descriptor in msDS-AllowedToActOnBehalfOfOtherIdentity.
    /// </summary>
    private static List<string> ExtractRbcdSids(byte[] sdBytes)
    {
        var sids = new List<string>();
        if (sdBytes == null || sdBytes.Length == 0)
            return sids;

        try
        {
            var sd = SecurityDescriptor.Deserialize(sdBytes);
            if (sd?.Dacl == null) return sids;

            foreach (var ace in sd.Dacl.Aces)
            {
                if (ace.Type == AceType.AccessAllowed && !string.IsNullOrEmpty(ace.TrusteeSid))
                {
                    sids.Add(ace.TrusteeSid);
                }
            }
        }
        catch
        {
            // Return whatever we could extract
        }

        return sids;
    }
}
