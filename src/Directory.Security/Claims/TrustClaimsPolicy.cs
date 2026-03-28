using Directory.Core.Interfaces;
using Directory.Core.Models;
using Microsoft.Extensions.Logging;

namespace Directory.Security.Claims;

/// <summary>
/// Manages per-trust claims transformation policies per MS-CTA section 3.1.
/// </summary>
public interface ITrustClaimsPolicy
{
    /// <summary>
    /// Gets the ingress (inbound) transformation rule set for a trust.
    /// </summary>
    Task<TransformationRuleSet> GetIngressPolicyAsync(string tenantId, string trustDn, CancellationToken ct = default);

    /// <summary>
    /// Gets the egress (outbound) transformation rule set for a trust.
    /// </summary>
    Task<TransformationRuleSet> GetEgressPolicyAsync(string tenantId, string trustDn, CancellationToken ct = default);

    /// <summary>
    /// Transforms inbound claims from a trusted domain using the ingress policy.
    /// </summary>
    Task<ClaimsSet> TransformInboundClaimsAsync(ClaimsSet claims, string tenantId, string trustDn, CancellationToken ct = default);

    /// <summary>
    /// Transforms outbound claims to a trusted domain using the egress policy.
    /// </summary>
    Task<ClaimsSet> TransformOutboundClaimsAsync(ClaimsSet claims, string tenantId, string trustDn, CancellationToken ct = default);
}

/// <summary>
/// Implements per-trust claims transformation by loading policies from
/// msDS-IngressClaimsTransformationPolicy and msDS-EgressClaimsTransformationPolicy
/// attributes on trusted domain objects, and applying them via <see cref="ClaimTransformationEngine"/>.
/// Per MS-ADTS section 6.1.6.9, each trust can have independent ingress and egress policies.
/// </summary>
public class TrustClaimsPolicy : ITrustClaimsPolicy
{
    private readonly IDirectoryStore _store;
    private readonly ClaimTransformationEngine _engine;
    private readonly ILogger<TrustClaimsPolicy> _logger;

    public TrustClaimsPolicy(
        IDirectoryStore store,
        ClaimTransformationEngine engine,
        ILogger<TrustClaimsPolicy> logger)
    {
        _store = store;
        _engine = engine;
        _logger = logger;
    }

    public async Task<TransformationRuleSet> GetIngressPolicyAsync(
        string tenantId, string trustDn, CancellationToken ct = default)
    {
        return await LoadPolicyAsync(tenantId, trustDn, "msDS-IngressClaimsTransformationPolicy", ct);
    }

    public async Task<TransformationRuleSet> GetEgressPolicyAsync(
        string tenantId, string trustDn, CancellationToken ct = default)
    {
        return await LoadPolicyAsync(tenantId, trustDn, "msDS-EgressClaimsTransformationPolicy", ct);
    }

    public async Task<ClaimsSet> TransformInboundClaimsAsync(
        ClaimsSet claims, string tenantId, string trustDn, CancellationToken ct = default)
    {
        var rules = await GetIngressPolicyAsync(tenantId, trustDn, ct);

        _logger.LogDebug("Transforming inbound claims for trust {TrustDn} with {Count} rules (default: {Default})",
            trustDn, rules.Rules.Count, rules.DefaultAction);

        return _engine.TransformClaims(claims, rules);
    }

    public async Task<ClaimsSet> TransformOutboundClaimsAsync(
        ClaimsSet claims, string tenantId, string trustDn, CancellationToken ct = default)
    {
        var rules = await GetEgressPolicyAsync(tenantId, trustDn, ct);

        _logger.LogDebug("Transforming outbound claims for trust {TrustDn} with {Count} rules (default: {Default})",
            trustDn, rules.Rules.Count, rules.DefaultAction);

        return _engine.TransformClaims(claims, rules);
    }

    /// <summary>
    /// Loads a transformation policy from a trust object's policy attribute.
    /// The attribute contains a DN pointing to an msDS-ClaimsTransformationPolicyType object
    /// whose msDS-TransformationRules attribute holds the XML rule definition.
    /// </summary>
    private async Task<TransformationRuleSet> LoadPolicyAsync(
        string tenantId, string trustDn, string policyAttributeName, CancellationToken ct)
    {
        try
        {
            // Load the trust object
            var trustObj = await _store.GetByDnAsync(tenantId, trustDn, ct);
            if (trustObj is null)
            {
                _logger.LogWarning("Trust object not found: {TrustDn}", trustDn);
                return new TransformationRuleSet { DefaultAction = DefaultRuleAction.AllowAll };
            }

            // Get the policy DN from the trust object
            var policyDnAttr = trustObj.GetAttribute(policyAttributeName);
            var policyDn = policyDnAttr?.GetFirstString();

            if (string.IsNullOrEmpty(policyDn))
            {
                _logger.LogDebug("No {Attribute} configured on trust {TrustDn}, defaulting to AllowAll",
                    policyAttributeName, trustDn);
                return new TransformationRuleSet { DefaultAction = DefaultRuleAction.AllowAll };
            }

            // Load the policy object
            var policyObj = await _store.GetByDnAsync(tenantId, policyDn, ct);
            if (policyObj is null)
            {
                _logger.LogWarning("Claims transformation policy object not found: {PolicyDn}", policyDn);
                return new TransformationRuleSet { DefaultAction = DefaultRuleAction.AllowAll };
            }

            // Get the transformation rules XML
            var rulesAttr = policyObj.GetAttribute("msDS-TransformationRules");
            var rulesXml = rulesAttr?.GetFirstString();

            if (string.IsNullOrEmpty(rulesXml))
            {
                _logger.LogDebug("Empty msDS-TransformationRules on {PolicyDn}", policyDn);
                return new TransformationRuleSet { DefaultAction = DefaultRuleAction.AllowAll };
            }

            return _engine.ParseRules(rulesXml);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load claims transformation policy {Attribute} for trust {TrustDn}",
                policyAttributeName, trustDn);
            // On failure, deny all claims as a safety measure
            return new TransformationRuleSet { DefaultAction = DefaultRuleAction.DenyAll };
        }
    }
}
