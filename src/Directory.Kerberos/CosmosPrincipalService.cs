using System.Collections.Concurrent;
using System.Security.Cryptography.X509Certificates;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Kerberos.S4U;
using Directory.Security.Apds;
using Kerberos.NET;
using Kerberos.NET.Crypto;
using Kerberos.NET.Entities;
using Kerberos.NET.Server;
using Microsoft.Extensions.Logging;

namespace Directory.Kerberos;

public class CosmosPrincipalService : IPrincipalService
{
    private readonly IDirectoryStore _store;
    private readonly IPasswordPolicy _passwordPolicy;
    private readonly KerberosOptions _options;
    private readonly PacGenerator _pacGenerator;
    private readonly AccountRestrictions _accountRestrictions;
    private readonly SpnService _spnService;
    private readonly S4UDelegationProcessor _s4uProcessor;
    private readonly ILogger<CosmosPrincipalService> _logger;

    // User Account Control flag values per MS-ADTS 2.2.16
    private const int UAC_NOT_DELEGATED = 0x100000;
    private const int UAC_TRUSTED_TO_AUTH_FOR_DELEGATION = 0x1000000;

    /// <summary>
    /// Cache for krbtgt objects per tenant+domain. The krbtgt account rarely changes,
    /// so caching it avoids a DB round-trip on every TGS request.
    /// </summary>
    private static readonly ConcurrentDictionary<string, (DirectoryObject obj, DateTime expires)> _krbtgtCache = new();

    /// <summary>
    /// Ambient S4U context set by <see cref="SetS4UContext"/> during TGS-REQ processing.
    /// The Kerberos.NET KDC pipeline calls <see cref="Find"/> synchronously and does not
    /// pass S4U metadata, so we flow delegation context via AsyncLocal to enforce policy
    /// at principal resolution time.
    /// </summary>
    private static readonly AsyncLocal<S4URequestContext> _s4uContext = new();

    public CosmosPrincipalService(
        IDirectoryStore store,
        IPasswordPolicy passwordPolicy,
        KerberosOptions options,
        PacGenerator pacGenerator,
        AccountRestrictions accountRestrictions,
        SpnService spnService,
        S4UDelegationProcessor s4uProcessor,
        ILogger<CosmosPrincipalService> logger)
    {
        _store = store;
        _passwordPolicy = passwordPolicy;
        _options = options;
        _pacGenerator = pacGenerator;
        _accountRestrictions = accountRestrictions;
        _spnService = spnService;
        _s4uProcessor = s4uProcessor;
        _logger = logger;
    }

    /// <summary>
    /// Set the ambient S4U context for the current async flow.
    /// Call this before the KDC pipeline invokes <see cref="Find"/> for an S4U TGS-REQ,
    /// so that delegation policy (TRUSTED_TO_AUTH_FOR_DELEGATION, NOT_DELEGATED,
    /// msDS-AllowedToDelegateTo, RBCD) can be enforced during principal resolution.
    /// </summary>
    public static void SetS4UContext(S4URequestContext context)
    {
        _s4uContext.Value = context;
    }

    /// <summary>
    /// Clear the ambient S4U context after TGS-REQ processing completes.
    /// </summary>
    public static void ClearS4UContext()
    {
        _s4uContext.Value = null;
    }

    public IKerberosPrincipal Find(KrbPrincipalName principalName, string realm)
    {
        // Synchronous wrapper — the IPrincipalService.Find is synchronous
        return FindAsync(principalName, realm).GetAwaiter().GetResult()
            ?? throw new global::Kerberos.NET.KerberosValidationException($"Principal not found: {principalName.FullyQualifiedName}");
    }

    public async Task<IKerberosPrincipal> FindAsync(KrbPrincipalName principalName, string realm)
    {
        var tenantId = "default";
        var domainDn = RealmToDomainDn(realm);
        var name = principalName.FullyQualifiedName;

        _logger.LogDebug("Finding principal: {Name} in realm {Realm}", name, realm);

        // Handle krbtgt service account — cached because it rarely changes
        if (name.Equals($"krbtgt/{realm}", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("krbtgt", StringComparison.OrdinalIgnoreCase))
        {
            var krbtgt = await GetKrbtgtCachedAsync(tenantId, domainDn);
            if (krbtgt is not null)
                return new CosmosKerberosPrincipal(krbtgt, _store, _passwordPolicy, _options, _logger, _pacGenerator);
        }

        // Resolve the directory object for this principal
        DirectoryObject resolved = null;

        // Try by SPN (exact match first, then HOST alias expansion via SpnService)
        var spnResults = await _store.GetByServicePrincipalNameAsync(tenantId, name);
        if (spnResults.Count > 0)
        {
            resolved = spnResults[0];
        }

        // Try SPN resolution with HOST alias expansion (e.g., CIFS/host -> HOST/host)
        if (resolved is null && name.Contains('/'))
        {
            var spnOwner = await _spnService.ResolveSpnOwnerAsync(tenantId, name);
            if (spnOwner is not null)
            {
                _logger.LogDebug("Resolved principal {Name} via SPN service to {Owner}", name, spnOwner.DistinguishedName);
                resolved = spnOwner;
            }
        }

        // Run sAMAccountName and UPN lookups in parallel — both are independent fallbacks
        if (resolved is null)
        {
            var samName = name.Contains('/') ? name : name.Split('@')[0];
            var samTask = _store.GetBySamAccountNameAsync(tenantId, domainDn, samName);
            var upnTask = _store.GetByUpnAsync(tenantId, name);

            await Task.WhenAll(samTask, upnTask);

            resolved = await samTask ?? await upnTask;
        }

        if (resolved is null)
        {
            _logger.LogWarning("Principal not found: {Name}", name);
            return null;
        }

        // ── S4U delegation policy enforcement ────────────────────────────
        // When an ambient S4U context is present, the KDC is processing an S4U TGS-REQ.
        // Validate delegation policy before returning the principal so that unauthorized
        // S4U2Self/S4U2Proxy requests fail at resolution time rather than silently succeeding.
        var s4u = _s4uContext.Value;
        if (s4u is not null)
        {
            ValidateS4UPolicy(s4u, resolved, tenantId, name);
        }

        return new CosmosKerberosPrincipal(resolved, _store, _passwordPolicy, _options, _logger, _pacGenerator, _accountRestrictions);
    }

    /// <summary>
    /// Enforce S4U delegation policy during principal resolution.
    /// Throws <see cref="KerberosValidationException"/> if the delegation is not authorized,
    /// causing the KDC to return an appropriate error to the client.
    /// </summary>
    private void ValidateS4UPolicy(S4URequestContext s4u, DirectoryObject resolved, string tenantId, string principalName)
    {
        if (s4u.OperationType == S4UOperationType.S4U2Self)
        {
            // The resolved principal is the target user (the one being impersonated).
            // The requesting service is identified in the S4U context.

            // 1. Check if the requesting service has TRUSTED_TO_AUTH_FOR_DELEGATION
            if (!s4u.ServiceHasTrustedToAuthForDelegation)
            {
                _logger.LogWarning(
                    "S4U2Self denied at principal resolution: requesting service {Service} " +
                    "does not have TRUSTED_TO_AUTH_FOR_DELEGATION",
                    s4u.ServicePrincipalName);
                throw new KerberosValidationException(
                    $"S4U2Self denied: service '{s4u.ServicePrincipalName}' is not trusted for protocol transition " +
                    "(TRUSTED_TO_AUTH_FOR_DELEGATION flag not set)");
            }

            // 2. Check if the target user is marked as NOT_DELEGATED (sensitive account)
            if ((resolved.UserAccountControl & UAC_NOT_DELEGATED) != 0)
            {
                _logger.LogWarning(
                    "S4U2Self denied at principal resolution: target user {User} is marked " +
                    "as sensitive (NOT_DELEGATED)",
                    principalName);
                throw new KerberosValidationException(
                    $"S4U2Self denied: user '{principalName}' is marked as sensitive and cannot be delegated " +
                    "(NOT_DELEGATED / 'Account is sensitive and cannot be delegated')");
            }

            _logger.LogInformation(
                "S4U2Self policy check passed: {Service} may impersonate {User}",
                s4u.ServicePrincipalName, principalName);
        }
        else if (s4u.OperationType == S4UOperationType.S4U2Proxy)
        {
            // The resolved principal is the target service (the back-end being delegated to).
            // Validate that the requesting service is authorized to delegate to this target.

            // Use the S4UDelegationProcessor for full validation (msDS-AllowedToDelegateTo + RBCD)
            var targetSpn = principalName;
            var result = _s4uProcessor.ProcessS4U2ProxyAsync(
                tenantId, s4u.ServiceDn, targetSpn).GetAwaiter().GetResult();

            if (!result.Success)
            {
                _logger.LogWarning(
                    "S4U2Proxy denied at principal resolution: {Service} -> {Target}: {Reason}",
                    s4u.ServicePrincipalName, targetSpn, result.ErrorMessage);
                throw new KerberosValidationException(
                    $"S4U2Proxy denied: {result.ErrorMessage}");
            }

            _logger.LogInformation(
                "S4U2Proxy policy check passed: {Service} -> {Target} (grant={GrantType})",
                s4u.ServicePrincipalName, targetSpn, result.GrantType);
        }
    }

    private async Task<DirectoryObject> GetKrbtgtCachedAsync(string tenantId, string domainDn)
    {
        var key = $"{tenantId}|{domainDn}";
        if (_krbtgtCache.TryGetValue(key, out var cached) && cached.expires > DateTime.UtcNow)
            return cached.obj;

        var krbtgt = await _store.GetBySamAccountNameAsync(tenantId, domainDn, "krbtgt");
        if (krbtgt is not null)
            _krbtgtCache[key] = (krbtgt, DateTime.UtcNow.AddMinutes(10));
        return krbtgt;
    }

    public X509Certificate2 RetrieveKdcCertificate() => null;

    public IExchangeKey RetrieveKeyCache(KeyAgreementAlgorithm algorithm) => null;

    public IExchangeKey CacheKey(IExchangeKey key) => key;

    private static string RealmToDomainDn(string realm)
    {
        var parts = realm.ToLowerInvariant().Split('.');
        return string.Join(",", parts.Select(p => $"DC={p}"));
    }
}
