using Directory.Core.Interfaces;
using Directory.Core.Models;
using Microsoft.Extensions.Logging;

namespace Directory.Kerberos;

/// <summary>
/// Service-for-User (S4U) protocol extension support per MS-SFU.
/// S4U2Self: A service obtains a service ticket to itself on behalf of a user
///           (protocol transition — allows a service to authenticate a user via non-Kerberos
///           mechanisms and then obtain a Kerberos ticket for that user).
/// S4U2Proxy: A service uses a user's forwarded ticket to request a ticket to another service
///            on behalf of that user (constrained delegation).
/// </summary>
/// <remarks>
/// INTEGRATION GAP: This service is registered in DI but is not currently called from
/// the Kerberos.NET KDC pipeline. Kerberos.NET's KdcServiceListener handles S4U2Self
/// (PA_FOR_USER) and S4U2Proxy TGS requests internally — it resolves the target principal
/// via IPrincipalService.Find but does NOT delegate policy checks (e.g., UAC flag validation
/// for TRUSTED_TO_AUTH_FOR_DELEGATION, NOT_DELEGATED, or msDS-AllowedToDelegateTo /
/// msDS-AllowedToActOnBehalfOfOtherIdentity RBCD checks) to the service implementation.
///
/// To fully enforce AD delegation policy, one of the following approaches is needed:
/// 1. Register a custom KDC TGS-REQ handler via Krb5KdcDefaults.RegisterDefaultTgsReqHandler = false
///    and implement a handler that calls this service's validation methods before issuing S4U tickets.
/// 2. Override the principal resolution in CosmosPrincipalService.Find to reject S4U requests
///    where delegation is not authorized (requires detecting S4U context from the request).
/// 3. Wait for Kerberos.NET to expose S4U validation hooks in IKerberosPrincipal or IRealmService.
///
/// Until one of these approaches is implemented, S4U2Self and S4U2Proxy requests will succeed
/// for any principal the KDC can resolve, without enforcing constrained delegation policy.
/// </remarks>
public class S4UService
{
    private readonly IDirectoryStore _store;
    private readonly ILogger<S4UService> _logger;

    // User Account Control flag values per MS-ADTS
    private const int UAC_ACCOUNT_DISABLED = 0x0002;
    private const int UAC_TRUSTED_FOR_DELEGATION = 0x80000;
    private const int UAC_NOT_DELEGATED = 0x100000;
    private const int UAC_TRUSTED_TO_AUTH_FOR_DELEGATION = 0x1000000;

    public S4UService(IDirectoryStore store, ILogger<S4UService> logger)
    {
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// Validate an S4U2Self request: the requesting service must be trusted for protocol transition
    /// (TRUSTED_TO_AUTH_FOR_DELEGATION flag in userAccountControl).
    /// Returns the resolved client principal if allowed.
    /// </summary>
    public async Task<S4UValidationResult> ValidateS4U2SelfAsync(
        string tenantId, string serviceDn, string clientPrincipalName, CancellationToken ct = default)
    {
        var service = await _store.GetByDnAsync(tenantId, serviceDn, ct);
        if (service == null)
            return S4UValidationResult.Denied("Service account not found");

        // Service must have TRUSTED_TO_AUTH_FOR_DELEGATION (protocol transition)
        if ((service.UserAccountControl & UAC_TRUSTED_TO_AUTH_FOR_DELEGATION) == 0)
        {
            _logger.LogWarning(
                "S4U2Self denied: {Service} does not have TRUSTED_TO_AUTH_FOR_DELEGATION",
                serviceDn);
            return S4UValidationResult.Denied("Service not trusted for protocol transition");
        }

        // Resolve the client principal
        var client = await ResolveClientPrincipalAsync(tenantId, service.DomainDn, clientPrincipalName, ct);
        if (client == null)
            return S4UValidationResult.Denied("Client principal not found");

        // Check that the client account is not disabled
        if ((client.UserAccountControl & UAC_ACCOUNT_DISABLED) != 0)
            return S4UValidationResult.Denied("Client account is disabled");

        // Check that the client is not marked as NOT_DELEGATED (sensitive account)
        if ((client.UserAccountControl & UAC_NOT_DELEGATED) != 0)
        {
            _logger.LogWarning(
                "S4U2Self denied: client {Client} is marked as sensitive (NOT_DELEGATED)",
                clientPrincipalName);
            return S4UValidationResult.Denied("Client account is sensitive and cannot be delegated");
        }

        _logger.LogInformation(
            "S4U2Self approved: {Service} acting as {Client}",
            serviceDn, clientPrincipalName);
        return S4UValidationResult.Allowed(client);
    }

    /// <summary>
    /// Validate an S4U2Proxy request: the requesting service must be allowed to delegate
    /// to the target service, either via traditional constrained delegation
    /// (msDS-AllowedToDelegateTo) or resource-based constrained delegation (RBCD,
    /// msDS-AllowedToActOnBehalfOfOtherIdentity on the target).
    /// </summary>
    public async Task<S4UValidationResult> ValidateS4U2ProxyAsync(
        string tenantId, string serviceDn, string targetSpn, CancellationToken ct = default)
    {
        var service = await _store.GetByDnAsync(tenantId, serviceDn, ct);
        if (service == null)
            return S4UValidationResult.Denied("Service account not found");

        // Check traditional constrained delegation (msDS-AllowedToDelegateTo)
        if (service.MsDsAllowedToDelegateTo.Any(spn =>
            spn.Equals(targetSpn, StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogInformation(
                "S4U2Proxy approved via constrained delegation: {Service} -> {Target}",
                serviceDn, targetSpn);
            return S4UValidationResult.Allowed(null);
        }

        // Check resource-based constrained delegation (RBCD)
        // The TARGET service must have msDS-AllowedToActOnBehalfOfOtherIdentity
        // containing the requesting service's SID in its DACL
        var rbcdResult = await CheckResourceBasedConstrainedDelegationAsync(
            tenantId, service, targetSpn, ct);
        if (rbcdResult)
        {
            _logger.LogInformation(
                "S4U2Proxy approved via RBCD: {Service} -> {Target}",
                serviceDn, targetSpn);
            return S4UValidationResult.Allowed(null);
        }

        _logger.LogWarning(
            "S4U2Proxy denied: {Service} not allowed to delegate to {Target}",
            serviceDn, targetSpn);
        return S4UValidationResult.Denied($"Service not allowed to delegate to {targetSpn}");
    }

    private async Task<bool> CheckResourceBasedConstrainedDelegationAsync(
        string tenantId, DirectoryObject service, string targetSpn, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(service.ObjectSid))
            return false;

        var targetResults = await _store.GetByServicePrincipalNameAsync(tenantId, targetSpn, ct);
        if (targetResults.Count == 0)
            return false;

        var targetService = targetResults[0];
        if (targetService.MsDsAllowedToActOnBehalf == null)
            return false;

        // Parse the security descriptor stored in msDS-AllowedToActOnBehalfOfOtherIdentity
        // and check if the requesting service's SID appears in an AccessAllowed ACE
        try
        {
            var sd = SecurityDescriptor.Deserialize(targetService.MsDsAllowedToActOnBehalf);
            if (sd?.Dacl == null)
                return false;

            foreach (var ace in sd.Dacl.Aces)
            {
                if (ace.Type == AceType.AccessAllowed &&
                    ace.TrusteeSid.Equals(service.ObjectSid, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to parse security descriptor for RBCD check on {Target}", targetSpn);
        }

        return false;
    }

    private async Task<DirectoryObject> ResolveClientPrincipalAsync(
        string tenantId, string domainDn, string clientPrincipalName, CancellationToken ct)
    {
        // Try SAM account name first (name@realm -> extract name)
        var parts = clientPrincipalName.Split('@');
        var samName = parts[0];

        var client = await _store.GetBySamAccountNameAsync(tenantId, domainDn, samName, ct);
        if (client != null) return client;

        // Try UPN match
        if (parts.Length > 1)
        {
            client = await _store.GetByUpnAsync(tenantId, clientPrincipalName, ct);
            if (client != null) return client;
        }

        return null;
    }
}

/// <summary>
/// Result of an S4U validation check.
/// </summary>
public class S4UValidationResult
{
    public bool IsAllowed { get; init; }
    public string DeniedReason { get; init; }

    /// <summary>
    /// The resolved client principal (for S4U2Self), or null for S4U2Proxy.
    /// </summary>
    public DirectoryObject ClientPrincipal { get; init; }

    public static S4UValidationResult Allowed(DirectoryObject client)
        => new() { IsAllowed = true, ClientPrincipal = client };

    public static S4UValidationResult Denied(string reason)
        => new() { IsAllowed = false, DeniedReason = reason };
}
