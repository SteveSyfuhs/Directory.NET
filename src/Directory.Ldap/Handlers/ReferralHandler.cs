using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Ldap.Protocol;
using Directory.Ldap.Protocol.Messages;
using Directory.Ldap.Server;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Directory.Ldap.Handlers;

/// <summary>
/// Handles LDAP referral generation for operations targeting DNs outside the local naming contexts.
///
/// Per RFC 4511 section 4.1.10, when a server cannot perform an operation because the target DN
/// is not within a local naming context, it returns resultCode=referral (10) with referral URIs
/// pointing to the appropriate server.
///
/// For search operations, subordinate naming contexts generate SearchResultReference (APPLICATION [19])
/// entries during result streaming.
///
/// The ManageDsaIT control (OID 2.16.840.1.113730.3.4.2) suppresses referral generation,
/// causing the server to treat referral objects as normal entries.
/// </summary>
public class ReferralHandler
{
    private readonly INamingContextService _namingContextService;
    private readonly IOptionsMonitor<LdapServerOptions> _optionsMonitor;
    private readonly ILogger<ReferralHandler> _logger;

    /// <summary>
    /// OID for the ManageDsaIT control per RFC 3296.
    /// When present, referral generation is suppressed.
    /// </summary>
    public const string OidManageDsaIT = "2.16.840.1.113730.3.4.2";

    public ReferralHandler(
        INamingContextService namingContextService,
        IOptionsMonitor<LdapServerOptions> optionsMonitor,
        ILogger<ReferralHandler> logger)
    {
        _namingContextService = namingContextService;
        _optionsMonitor = optionsMonitor;
        _logger = logger;
    }

    /// <summary>
    /// Check if the ManageDsaIT control is present in the request controls.
    /// When present, referral generation should be suppressed.
    /// </summary>
    public static bool HasManageDsaIT(IReadOnlyList<LdapControl> controls)
    {
        if (controls == null) return false;
        return controls.Any(c => c.Oid == OidManageDsaIT);
    }

    /// <summary>
    /// Determine if a DN is within any local naming context.
    /// Returns the matching naming context, or null if the DN is not local.
    /// </summary>
    public NamingContext GetLocalNamingContext(string dn)
    {
        if (string.IsNullOrEmpty(dn))
            return null;

        return _namingContextService.GetNamingContext(dn);
    }

    /// <summary>
    /// Check if a DN is within the local naming contexts.
    /// </summary>
    public bool IsDnLocal(string dn)
    {
        return GetLocalNamingContext(dn) != null;
    }

    /// <summary>
    /// Generate a referral response for an operation targeting a non-local DN.
    /// Returns the referral URIs and matched DN, or null if the DN is local.
    /// </summary>
    public ReferralInfo GenerateOperationReferral(string targetDn, IReadOnlyList<LdapControl> controls)
    {
        // ManageDsaIT suppresses referral generation
        if (HasManageDsaIT(controls))
            return null;

        if (string.IsNullOrEmpty(targetDn))
            return null;

        // Check if the DN is within a local naming context
        if (IsDnLocal(targetDn))
            return null;

        // The DN is not local. Try to determine which domain/server to refer to.
        var referralUri = BuildReferralUri(targetDn);
        if (referralUri == null)
            return null;

        // MatchedDN should be the longest prefix of the target DN that matches a local NC
        var matchedDn = FindMatchedDn(targetDn);

        _logger.LogDebug("Generating referral for non-local DN {DN} -> {Referral}", targetDn, referralUri);

        return new ReferralInfo
        {
            ReferralUris = [referralUri],
            MatchedDn = matchedDn,
        };
    }

    /// <summary>
    /// Generate SearchResultReference entries for subordinate naming contexts
    /// that are within the search scope but hosted on different servers.
    /// </summary>
    public List<SearchResultReference> GenerateSearchReferrals(
        string baseDn, SearchScope scope, IReadOnlyList<LdapControl> controls)
    {
        var referrals = new List<SearchResultReference>();

        // ManageDsaIT suppresses referral generation
        if (HasManageDsaIT(controls))
            return referrals;

        if (scope == SearchScope.BaseObject)
            return referrals; // No referrals for base-level searches

        var currentNc = _namingContextService.GetNamingContext(baseDn);
        if (currentNc == null)
            return referrals;

        var allNcs = _namingContextService.GetAllNamingContexts();
        foreach (var nc in allNcs)
        {
            // Skip the current NC
            if (string.Equals(nc.Dn, currentNc.Dn, StringComparison.OrdinalIgnoreCase))
                continue;

            // Check if this NC is subordinate to the search base
            if (!nc.Dn.EndsWith(baseDn, StringComparison.OrdinalIgnoreCase) ||
                nc.Dn.Length <= baseDn.Length)
                continue;

            // For one-level scope, only include immediate children
            if (scope == SearchScope.SingleLevel)
            {
                var parentDn = GetParentDn(nc.Dn);
                if (!string.Equals(parentDn, baseDn, StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            var referralUri = $"ldap://{nc.DnsName}/{nc.Dn}";
            referrals.Add(new SearchResultReference
            {
                Uris = [referralUri]
            });

            _logger.LogDebug("Search referral: {BaseDN} -> {Referral}", baseDn, referralUri);
        }

        return referrals;
    }

    /// <summary>
    /// Build a referral URI for a target DN based on known naming contexts and domain DNS names.
    /// Format: ldap://hostname/DN
    /// </summary>
    private string BuildReferralUri(string targetDn)
    {
        // Try to match the DN to a known (potentially remote) naming context
        // by extracting the DC components and building a DNS name
        var domainDn = ExtractDomainDn(targetDn);
        if (string.IsNullOrEmpty(domainDn))
            return null;

        var dnsName = DnToDnsName(domainDn);
        if (string.IsNullOrEmpty(dnsName))
            return null;

        return $"ldap://{dnsName}/{targetDn}";
    }

    /// <summary>
    /// Find the longest matching prefix DN from local naming contexts.
    /// Used as the matchedDN in referral responses.
    /// </summary>
    private string FindMatchedDn(string targetDn)
    {
        var allNcs = _namingContextService.GetAllNamingContexts();
        string bestMatch = string.Empty;

        foreach (var nc in allNcs)
        {
            if (targetDn.EndsWith(nc.Dn, StringComparison.OrdinalIgnoreCase) &&
                nc.Dn.Length > bestMatch.Length)
            {
                bestMatch = nc.Dn;
            }
        }

        return bestMatch;
    }

    /// <summary>
    /// Extract the domain DN (DC=...) components from a full DN.
    /// </summary>
    private static string ExtractDomainDn(string dn)
    {
        var components = dn.Split(',', StringSplitOptions.TrimEntries);
        var dcParts = components
            .Where(c => c.StartsWith("DC=", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return dcParts.Length > 0 ? string.Join(",", dcParts) : string.Empty;
    }

    /// <summary>
    /// Convert a domain DN (DC=contoso,DC=com) to a DNS name (contoso.com).
    /// </summary>
    private static string DnToDnsName(string domainDn)
    {
        var parts = domainDn.Split(',', StringSplitOptions.TrimEntries);
        var labels = parts
            .Where(p => p.StartsWith("DC=", StringComparison.OrdinalIgnoreCase))
            .Select(p => p[3..]);
        return string.Join(".", labels);
    }

    private static string GetParentDn(string dn)
    {
        var firstComma = dn.IndexOf(',');
        return firstComma >= 0 ? dn[(firstComma + 1)..] : string.Empty;
    }
}

/// <summary>
/// Contains referral information for an operation that cannot be serviced locally.
/// </summary>
public class ReferralInfo
{
    /// <summary>
    /// LDAP referral URIs pointing to the appropriate server(s).
    /// Format: ldap://hostname/DN
    /// </summary>
    public List<string> ReferralUris { get; init; } = [];

    /// <summary>
    /// The matched DN — the longest prefix of the target DN that the server recognizes.
    /// </summary>
    public string MatchedDn { get; init; } = string.Empty;
}
