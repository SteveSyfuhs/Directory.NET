using Directory.Core.Interfaces;
using Directory.Core.Models;
using Microsoft.Extensions.Logging;

namespace Directory.Security.Claims;

/// <summary>
/// Generates claims from directory objects per MS-CTA section 3.1.
/// </summary>
public interface IClaimsProvider
{
    /// <summary>
    /// Generates a claims set for a user principal from their AD attributes.
    /// </summary>
    Task<ClaimsSet> GenerateUserClaimsAsync(DirectoryObject user, CancellationToken ct = default);

    /// <summary>
    /// Generates a claims set for a computer/device principal from its AD attributes.
    /// </summary>
    Task<ClaimsSet> GenerateDeviceClaimsAsync(DirectoryObject computer, CancellationToken ct = default);
}

/// <summary>
/// Implements claims generation by reading AD attributes from a <see cref="DirectoryObject"/>
/// and mapping them through enabled <see cref="ClaimTypeDefinition"/> entries.
/// Per MS-ADTS section 3.1.1.11, user and device claims are populated from the source
/// attributes specified in msDS-ClaimType objects.
/// </summary>
public class ClaimsProvider : IClaimsProvider
{
    private readonly IClaimTypeStore _claimTypeStore;
    private readonly IDirectoryStore _store;
    private readonly ILogger<ClaimsProvider> _logger;

    /// <summary>
    /// Default attribute-to-claim mappings used when no msDS-ClaimType objects are present.
    /// Maps AD attribute names to their claim IDs.
    /// </summary>
    private static readonly Dictionary<string, string> DefaultAttributeMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["department"] = "ad://ext/department",
        ["company"] = "ad://ext/company",
        ["title"] = "ad://ext/title",
        ["givenName"] = "ad://ext/givenName",
        ["sn"] = "ad://ext/sn",
        ["mail"] = "ad://ext/mail",
        ["displayName"] = "ad://ext/displayName",
        ["manager"] = "ad://ext/manager"
    };

    /// <summary>
    /// Device-specific attribute-to-claim mappings.
    /// </summary>
    private static readonly Dictionary<string, string> DeviceAttributeMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["operatingSystem"] = "ad://ext/operatingSystem",
        ["operatingSystemVersion"] = "ad://ext/operatingSystemVersion",
        ["dNSHostName"] = "ad://ext/dNSHostName"
    };

    public ClaimsProvider(IClaimTypeStore claimTypeStore, IDirectoryStore store, ILogger<ClaimsProvider> logger)
    {
        _claimTypeStore = claimTypeStore;
        _store = store;
        _logger = logger;
    }

    public async Task<ClaimsSet> GenerateUserClaimsAsync(DirectoryObject user, CancellationToken ct = default)
    {
        _logger.LogDebug("Generating user claims for {Dn}", user.DistinguishedName);

        var claimsSet = new ClaimsSet();
        var adArray = claimsSet.GetOrCreateArray(ClaimSourceType.AD);

        var claimTypes = await _claimTypeStore.GetAllClaimTypesAsync(user.TenantId, user.DomainDn, ct);

        foreach (var claimType in claimTypes)
        {
            // Skip device-only claim types
            if (claimType.AppliesToClasses.Count > 0
                && !claimType.AppliesToClasses.Any(c =>
                    c.Contains("user", StringComparison.OrdinalIgnoreCase)
                    || c.Contains("person", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var entry = ExtractClaimFromObject(user, claimType);
            if (entry is not null)
            {
                adArray.ClaimEntries.Add(entry);
            }
        }

        // Also apply any default mappings that weren't covered by claim type definitions
        ApplyDefaultMappings(user, adArray, DefaultAttributeMappings);

        _logger.LogDebug("Generated {Count} user claims for {Dn}",
            adArray.ClaimEntries.Count, user.DistinguishedName);

        return claimsSet;
    }

    public async Task<ClaimsSet> GenerateDeviceClaimsAsync(DirectoryObject computer, CancellationToken ct = default)
    {
        _logger.LogDebug("Generating device claims for {Dn}", computer.DistinguishedName);

        var claimsSet = new ClaimsSet();
        var adArray = claimsSet.GetOrCreateArray(ClaimSourceType.AD);

        var claimTypes = await _claimTypeStore.GetAllClaimTypesAsync(computer.TenantId, computer.DomainDn, ct);

        foreach (var claimType in claimTypes)
        {
            // Skip user-only claim types (those that explicitly apply to user but not computer)
            if (claimType.AppliesToClasses.Count > 0
                && !claimType.AppliesToClasses.Any(c =>
                    c.Contains("computer", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var entry = ExtractClaimFromObject(computer, claimType);
            if (entry is not null)
            {
                adArray.ClaimEntries.Add(entry);
            }
        }

        // Apply device-specific default mappings
        ApplyDefaultMappings(computer, adArray, DeviceAttributeMappings);
        ApplyDefaultMappings(computer, adArray, DefaultAttributeMappings);

        _logger.LogDebug("Generated {Count} device claims for {Dn}",
            adArray.ClaimEntries.Count, computer.DistinguishedName);

        return claimsSet;
    }

    /// <summary>
    /// Extracts a claim entry from a directory object using the specified claim type definition.
    /// </summary>
    private static ClaimEntry ExtractClaimFromObject(DirectoryObject obj, ClaimTypeDefinition claimType)
    {
        var attr = obj.GetAttribute(claimType.SourceAttribute);
        if (attr is null || attr.Values.Count == 0)
            return null;

        var entry = new ClaimEntry
        {
            Id = claimType.ClaimId,
            Type = claimType.ValueType
        };

        switch (claimType.ValueType)
        {
            case ClaimValueType.STRING:
                entry.StringValues = attr.GetStrings().ToList();
                break;

            case ClaimValueType.INT64:
                entry.Int64Values = [];
                foreach (var val in attr.Values)
                {
                    if (val is long l)
                        entry.Int64Values.Add(l);
                    else if (long.TryParse(val?.ToString(), out var parsed))
                        entry.Int64Values.Add(parsed);
                }
                if (entry.Int64Values.Count == 0)
                    return null;
                break;

            case ClaimValueType.UINT64:
                entry.UInt64Values = [];
                foreach (var val in attr.Values)
                {
                    if (val is ulong u)
                        entry.UInt64Values.Add(u);
                    else if (ulong.TryParse(val?.ToString(), out var parsed))
                        entry.UInt64Values.Add(parsed);
                }
                if (entry.UInt64Values.Count == 0)
                    return null;
                break;

            case ClaimValueType.BOOLEAN:
                entry.BooleanValues = [];
                foreach (var val in attr.Values)
                {
                    if (val is bool b)
                        entry.BooleanValues.Add(b);
                    else if (bool.TryParse(val?.ToString(), out var parsed))
                        entry.BooleanValues.Add(parsed);
                }
                if (entry.BooleanValues.Count == 0)
                    return null;
                break;

            case ClaimValueType.SID:
                entry.SidValues = [];
                foreach (var val in attr.Values)
                {
                    if (val is byte[] bytes)
                        entry.SidValues.Add(bytes);
                }
                if (entry.SidValues.Count == 0)
                    return null;
                break;

            default:
                return null;
        }

        return entry;
    }

    /// <summary>
    /// Applies default attribute-to-claim mappings for attributes not already present.
    /// </summary>
    private static void ApplyDefaultMappings(
        DirectoryObject obj,
        ClaimsArray array,
        Dictionary<string, string> mappings)
    {
        foreach (var (attributeName, claimId) in mappings)
        {
            // Skip if this claim ID is already present
            if (array.ClaimEntries.Any(e => string.Equals(e.Id, claimId, StringComparison.OrdinalIgnoreCase)))
                continue;

            var attr = obj.GetAttribute(attributeName);
            if (attr is null || attr.Values.Count == 0)
                continue;

            var strings = attr.GetStrings().Where(s => !string.IsNullOrEmpty(s)).ToList();
            if (strings.Count == 0)
                continue;

            array.ClaimEntries.Add(new ClaimEntry
            {
                Id = claimId,
                Type = ClaimValueType.STRING,
                StringValues = strings
            });
        }
    }
}
