using Directory.Core.Interfaces;
using Directory.Core.Models;
using Microsoft.Extensions.Logging;

namespace Directory.Kerberos.S4U;

/// <summary>
/// Implements Service-for-User-to-Self (S4U2Self) per MS-SFU section 3.1.5.1.1.1.
///
/// S4U2Self allows a service to obtain a service ticket to itself on behalf of a user,
/// enabling protocol transition. The requesting service must have the
/// TRUSTED_TO_AUTH_FOR_DELEGATION flag set in its userAccountControl attribute.
///
/// The handler validates:
/// 1. The requesting service exists and has TRUSTED_TO_AUTH_FOR_DELEGATION set
/// 2. The target user exists and is not disabled
/// 3. The target user is not marked as sensitive (NOT_DELEGATED / "Account is sensitive
///    and cannot be delegated")
///
/// Input context: A TGS-REQ containing PA-FOR-USER padata with the target user's name.
/// Output: An <see cref="S4USelfResult"/> indicating whether a service ticket for the
///         requesting service with the user's identity should be issued.
/// </summary>
public class S4USelfHandler
{
    private readonly IDirectoryStore _store;
    private readonly ILogger<S4USelfHandler> _logger;

    // User Account Control flag values per MS-ADTS 2.2.16
    private const int UAC_ACCOUNT_DISABLED = 0x0002;
    private const int UAC_NOT_DELEGATED = 0x100000;
    private const int UAC_TRUSTED_TO_AUTH_FOR_DELEGATION = 0x1000000;

    public S4USelfHandler(IDirectoryStore store, ILogger<S4USelfHandler> logger)
    {
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// Validate an S4U2Self request: the requesting service must have the
    /// TRUSTED_TO_AUTH_FOR_DELEGATION flag, and the target client must be
    /// a valid, non-sensitive, non-disabled account.
    /// </summary>
    /// <param name="tenantId">Tenant identifier.</param>
    /// <param name="serviceDn">DN of the requesting service principal.</param>
    /// <param name="clientPrincipalName">
    /// The target user name from PA-FOR-USER padata (user@REALM or bare sAMAccountName).
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// An <see cref="S4USelfResult"/> indicating whether the request is allowed
    /// and containing the resolved client identity on success.
    /// </returns>
    public async Task<S4USelfResult> ValidateAsync(
        string tenantId,
        string serviceDn,
        string clientPrincipalName,
        CancellationToken ct = default)
    {
        // ── 1. Resolve and validate the requesting service ──────────────
        var service = await _store.GetByDnAsync(tenantId, serviceDn, ct);
        if (service == null)
        {
            _logger.LogWarning("S4U2Self denied: service {ServiceDn} not found", serviceDn);
            return S4USelfResult.Denied("Service account not found");
        }

        if ((service.UserAccountControl & UAC_TRUSTED_TO_AUTH_FOR_DELEGATION) == 0)
        {
            _logger.LogWarning(
                "S4U2Self denied: {ServiceDn} does not have TRUSTED_TO_AUTH_FOR_DELEGATION",
                serviceDn);
            return S4USelfResult.Denied(
                "Service is not trusted for protocol transition (TRUSTED_TO_AUTH_FOR_DELEGATION not set)");
        }

        // ── 2. Resolve the target client principal ──────────────────────
        var client = await ResolveClientAsync(tenantId, service.DomainDn, clientPrincipalName, ct);
        if (client == null)
        {
            _logger.LogWarning("S4U2Self denied: client {Client} not found", clientPrincipalName);
            return S4USelfResult.Denied("Client principal not found");
        }

        // ── 3. Account restriction checks ───────────────────────────────
        if ((client.UserAccountControl & UAC_ACCOUNT_DISABLED) != 0)
        {
            _logger.LogWarning("S4U2Self denied: client {Client} is disabled", clientPrincipalName);
            return S4USelfResult.Denied("Client account is disabled");
        }

        if ((client.UserAccountControl & UAC_NOT_DELEGATED) != 0)
        {
            _logger.LogWarning(
                "S4U2Self denied: client {Client} is marked as sensitive (NOT_DELEGATED)",
                clientPrincipalName);
            return S4USelfResult.Denied(
                "Client account is sensitive and cannot be delegated (NOT_DELEGATED flag set)");
        }

        // ── 4. Approved ─────────────────────────────────────────────────
        _logger.LogInformation(
            "S4U2Self approved: service {Service} may act as {Client}",
            serviceDn, client.DistinguishedName);

        return S4USelfResult.Allowed(
            client.SAMAccountName ?? client.Cn ?? clientPrincipalName,
            client.DistinguishedName,
            client.ObjectSid);
    }

    /// <summary>
    /// Resolve a client principal by sAMAccountName or UPN.
    /// </summary>
    private async Task<DirectoryObject> ResolveClientAsync(
        string tenantId, string domainDn, string clientPrincipalName, CancellationToken ct)
    {
        var parts = clientPrincipalName.Split('@');
        var samName = parts[0];

        // Try sAMAccountName first
        var client = await _store.GetBySamAccountNameAsync(tenantId, domainDn, samName, ct);
        if (client != null) return client;

        // Try UPN if the name contains a realm component
        if (parts.Length > 1)
        {
            client = await _store.GetByUpnAsync(tenantId, clientPrincipalName, ct);
            if (client != null) return client;
        }

        return null;
    }
}
