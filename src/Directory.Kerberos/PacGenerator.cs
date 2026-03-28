using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Security.Claims;
using Kerberos.NET.Crypto;
using Kerberos.NET.Entities;
using Kerberos.NET.Entities.Pac;
using Microsoft.Extensions.Logging;

// Aliases to disambiguate our claims types from Kerberos.NET's PAC claims types
using OurClaimsSet = Directory.Security.Claims.ClaimsSet;
using OurClaimValueType = Directory.Security.Claims.ClaimValueType;
using KerbClaimsSetMetadata = Kerberos.NET.Entities.ClaimsSetMetadata;
using KerbClaimsSet = Kerberos.NET.Entities.Pac.ClaimsSet;
using KerbClaimsArray = Kerberos.NET.Entities.Pac.ClaimsArray;
using KerbClaimEntry = Kerberos.NET.Entities.Pac.ClaimEntry;
using KerbClaimType = Kerberos.NET.Entities.Pac.ClaimType;
using KerbClaimSourceType = Kerberos.NET.Entities.Pac.ClaimSourceType;

namespace Directory.Kerberos;

/// <summary>
/// Generates a complete Privilege Attribute Certificate (PAC) per MS-PAC.
/// Populates logon info, client info, group memberships, extra SIDs,
/// UPN/DNS information, and user claims (per MS-CTA) for inclusion in Kerberos tickets.
/// Claims are set directly on the PAC via Kerberos.NET's native ClientClaims property,
/// which handles the wire-format (NDR) encoding.
///
/// PAC Checksums (SERVER_CHECKSUM / KDC_CHECKSUM):
/// The ServerSignature and KdcSignature are computed automatically by Kerberos.NET
/// when the PAC is encoded into a ticket. The library's PrivilegedAttributeCertificate.Encode()
/// method accepts both the KDC (krbtgt) key and the service principal key, then internally
/// calls CollectElements() and SignPac() to produce the HMAC checksums per MS-PAC section 2.8.
/// We initialize empty PacSignature objects here so that HasRequiredFields is satisfied,
/// but Kerberos.NET overwrites them with computed values during ticket generation.
/// </summary>
public class PacGenerator
{
    private readonly IDirectoryStore _store;
    private readonly IClaimsProvider _claimsProvider;
    private readonly ILogger<PacGenerator> _logger;

    public PacGenerator(
        IDirectoryStore store,
        IClaimsProvider claimsProvider,
        ILogger<PacGenerator> logger)
    {
        _store = store;
        _claimsProvider = claimsProvider;
        _logger = logger;
    }

    /// <summary>
    /// Generate a full PAC for the given user in the specified domain,
    /// including user claims per MS-CTA.
    /// Claims are assigned directly to the PAC's ClientClaims property so that
    /// Kerberos.NET encodes them in the wire format automatically.
    /// </summary>
    public async Task<PrivilegedAttributeCertificate> GenerateAsync(
        DirectoryObject user, string domainDn, string tenantId, CancellationToken ct = default)
    {
        var domainNetBiosName = GetNetBiosName(domainDn);
        var domainSid = user.ObjectSid != null ? GetDomainSidFromUserSid(user.ObjectSid) : "";
        var userRid = user.ObjectSid != null ? GetRidFromSid(user.ObjectSid) : 0;

        // Expand group memberships (transitive)
        var groupSids = await ExpandGroupMembershipsAsync(user, tenantId, ct);

        // Add primary group if not already present
        if (user.PrimaryGroupId > 0 && !string.IsNullOrEmpty(domainSid))
        {
            var primaryGroupSid = $"{domainSid}-{user.PrimaryGroupId}";
            if (!groupSids.Any(g => g.Sid.Equals(primaryGroupSid, StringComparison.OrdinalIgnoreCase)))
            {
                groupSids.Insert(0, new GroupSidInfo
                {
                    Sid = primaryGroupSid,
                    Attributes = SidAttributes.SE_GROUP_MANDATORY |
                                 SidAttributes.SE_GROUP_ENABLED_BY_DEFAULT |
                                 SidAttributes.SE_GROUP_ENABLED,
                });
            }
        }

        var pac = new PrivilegedAttributeCertificate
        {
            LogonInfo = BuildLogonInfo(user, domainNetBiosName, domainSid, userRid, groupSids),
            ClientInformation = new PacClientInfo
            {
                ClientId = DateTimeOffset.UtcNow,
                Name = user.SAMAccountName ?? user.Cn ?? "unknown",
            },
            // Initialize signature placeholders. Kerberos.NET's Encode() method computes
            // the actual SERVER_CHECKSUM and KDC_CHECKSUM (MS-PAC 2.8) using the service
            // key and krbtgt key respectively during ticket generation.
            ServerSignature = new PacSignature(PacType.SERVER_CHECKSUM, EncryptionType.AES256_CTS_HMAC_SHA1_96),
            KdcSignature = new PacSignature(PacType.PRIVILEGE_SERVER_CHECKSUM, EncryptionType.AES256_CTS_HMAC_SHA1_96),
        };

        // UPN/DNS information — always populated per MS-PAC 2.10.
        // If UPN is not set, construct from sAMAccountName@domain.
        var dnsName = GetDnsNameFromDn(domainDn);
        var upn = !string.IsNullOrEmpty(user.UserPrincipalName)
            ? user.UserPrincipalName
            : $"{user.SAMAccountName ?? user.Cn ?? "unknown"}@{dnsName}";

        pac.UpnDomainInformation = new UpnDomainInfo
        {
            Upn = upn,
            Domain = dnsName,
        };

        // Generate user claims per MS-CTA and assign to PAC via native Kerberos.NET property.
        // Kerberos.NET handles the NDR wire-format encoding when the PAC is serialized.
        try
        {
            var userClaims = await _claimsProvider.GenerateUserClaimsAsync(user, ct);
            if (userClaims.ClaimsArrays.Count > 0)
            {
                pac.ClientClaims = ConvertToKerberosClaimsSetMetadata(userClaims);

                _logger.LogDebug("Included {ClaimCount} claim arrays in PAC for {User}",
                    userClaims.ClaimsArrays.Count, user.SAMAccountName ?? user.Cn);
            }
        }
        catch (Exception ex)
        {
            // Claims generation failure should not prevent PAC creation
            _logger.LogWarning(ex, "Failed to generate claims for PAC for user {User}", user.SAMAccountName ?? user.Cn);
        }

        _logger.LogDebug(
            "Generated PAC for {User} with {GroupCount} group SIDs",
            user.SAMAccountName ?? user.Cn,
            groupSids.Count);

        return pac;
    }

    /// <summary>
    /// Default maximum password age (42 days), matching AccountRestrictions.
    /// </summary>
    private static readonly long DefaultMaxPasswordAge = TimeSpan.FromDays(42).Ticks;

    /// <summary>
    /// Default minimum password age (1 day).
    /// </summary>
    private static readonly long DefaultMinPasswordAge = TimeSpan.FromDays(1).Ticks;

    /// <summary>
    /// UAC flag for DONT_EXPIRE_PASSWORD (0x00010000).
    /// </summary>
    private const int UacDontExpirePassword = 0x00010000;

    private static PacLogonInfo BuildLogonInfo(
        DirectoryObject user,
        string domainNetBiosName,
        string domainSid,
        int userRid,
        List<GroupSidInfo> groupSids)
    {
        // Resolve user attributes for PAC fields
        string logonScript = user.GetAttribute("scriptPath")?.GetFirstString() ?? "";
        string profilePath = user.GetAttribute("profilePath")?.GetFirstString() ?? "";
        string homeDir = user.GetAttribute("homeDirectory")?.GetFirstString() ?? "";
        string homeDrive = user.GetAttribute("homeDrive")?.GetFirstString() ?? "";

        var logonCountAttr = user.GetAttribute("logonCount");
        ushort logonCount = 0;
        if (logonCountAttr?.GetFirstString() is { } lcStr && ushort.TryParse(lcStr, out var lc))
            logonCount = lc;

        // Compute password time boundaries
        bool dontExpirePassword = (user.UserAccountControl & UacDontExpirePassword) != 0;

        DateTimeOffset passwordLastSet = user.PwdLastSet > 0
            ? DateTimeOffset.FromFileTime(user.PwdLastSet)
            : DateTimeOffset.UtcNow;

        DateTimeOffset passwordCanChange = user.PwdLastSet > 0
            ? passwordLastSet.AddTicks(DefaultMinPasswordAge)
            : passwordLastSet;

        DateTimeOffset passwordMustChange = dontExpirePassword || user.PwdLastSet <= 0
            ? DateTimeOffset.MaxValue
            : passwordLastSet.AddTicks(DefaultMaxPasswordAge);

        // KickOffTime: from account expiry, or never
        DateTimeOffset kickOffTime = DateTimeOffset.MaxValue;
        if (user.AccountExpires > 0 && user.AccountExpires < long.MaxValue)
        {
            try { kickOffTime = DateTimeOffset.FromFileTime(user.AccountExpires); }
            catch (ArgumentOutOfRangeException) { /* keep MaxValue */ }
        }

        // Build ExtraIds: well-known SIDs per MS-PAC / MS-APDS
        var extraIds = new List<RpcSidAttributes>();

        // S-1-18-1 Authentication Authority Asserted Identity (Kerberos pre-auth passed)
        extraIds.Add(MakeExtraSid("S-1-18-1"));
        // S-1-5-11 Authenticated Users
        extraIds.Add(MakeExtraSid("S-1-5-11"));
        // S-1-18-2 Key Trust Identity (Kerberos authentication)
        extraIds.Add(MakeExtraSid("S-1-18-2"));
        // S-1-2-0 Local (interactive logon context)
        extraIds.Add(MakeExtraSid("S-1-2-0"));

        // Add cross-domain group SIDs as extra SIDs
        foreach (var g in groupSids.Where(g => !g.Sid.StartsWith(domainSid, StringComparison.OrdinalIgnoreCase)))
        {
            extraIds.Add(new RpcSidAttributes
            {
                Sid = ParseRpcSid(g.Sid),
                Attributes = g.Attributes,
            });
        }

        // Compute UserFlags
        UserFlags userFlags = 0;
        if (extraIds.Count > 0)
            userFlags |= UserFlags.LOGON_EXTRA_SIDS;

        var logonInfo = new PacLogonInfo
        {
            DomainName = domainNetBiosName,
            UserName = user.SAMAccountName ?? user.Cn ?? "unknown",
            UserDisplayName = user.DisplayName ?? user.Cn ?? "",
            UserId = (uint)userRid,
            GroupId = (uint)user.PrimaryGroupId,
            UserAccountControl = (UserAccountControlFlags)user.UserAccountControl,
            UserFlags = userFlags,
            LogonCount = (short)logonCount,
            BadPasswordCount = (short)user.BadPwdCount,
            LogonTime = DateTimeOffset.UtcNow,
            LogoffTime = DateTimeOffset.MaxValue,
            KickOffTime = kickOffTime,
            PwdLastChangeTime = passwordLastSet,
            PwdCanChangeTime = passwordCanChange,
            PwdMustChangeTime = passwordMustChange,
            LogonScript = logonScript,
            ProfilePath = profilePath,
            HomeDirectory = homeDir,
            HomeDrive = homeDrive,
            ServerName = Environment.MachineName,
            DomainId = string.IsNullOrEmpty(domainSid) ? default : ParseRpcSid(domainSid),
            ExtraIds = extraIds.ToArray(),
        };

        // Build GroupIds from domain-local group SIDs
        logonInfo.GroupIds = groupSids
            .Where(g => g.Sid.StartsWith(domainSid, StringComparison.OrdinalIgnoreCase))
            .Select(g => new GroupMembership
            {
                RelativeId = (uint)GetRidFromSid(g.Sid),
                Attributes = g.Attributes,
            })
            .ToArray();

        return logonInfo;
    }

    private static RpcSidAttributes MakeExtraSid(string sid)
    {
        return new RpcSidAttributes
        {
            Sid = ParseRpcSid(sid),
            Attributes = SidAttributes.SE_GROUP_MANDATORY |
                         SidAttributes.SE_GROUP_ENABLED_BY_DEFAULT |
                         SidAttributes.SE_GROUP_ENABLED,
        };
    }

    /// <summary>
    /// Parses a SID string (e.g. "S-1-5-21-...") into a Kerberos.NET RpcSid.
    /// </summary>
    private static RpcSid ParseRpcSid(string sidString)
    {
        // Parse SID string: S-{revision}-{authority}-{sub1}-{sub2}-...
        var parts = sidString.Split('-');
        if (parts.Length < 3 || parts[0] != "S")
            throw new ArgumentException($"Invalid SID string: {sidString}");

        byte revision = byte.Parse(parts[1]);
        long authority = long.Parse(parts[2]);

        var subAuthorities = new uint[parts.Length - 3];
        for (int i = 3; i < parts.Length; i++)
            subAuthorities[i - 3] = uint.Parse(parts[i]);

        // RpcSidIdentifierAuthority is stored as 6-byte big-endian
        var authorityBytes = new byte[6];
        for (int i = 0; i < 6; i++)
            authorityBytes[i] = (byte)((authority >> (8 * (5 - i))) & 0xFF);

        return new RpcSid
        {
            Revision = revision,
            SubAuthorityCount = (byte)subAuthorities.Length,
            IdentifierAuthority = new RpcSidIdentifierAuthority { IdentifierAuthority = authorityBytes },
            SubAuthority = subAuthorities,
        };
    }

    private async Task<List<GroupSidInfo>> ExpandGroupMembershipsAsync(
        DirectoryObject user, string tenantId, CancellationToken ct)
    {
        // Fast path: use pre-computed materialized view if available and fresh
        if (user.TokenGroupsSids.Count > 0 && user.TokenGroupsLastComputed.HasValue)
        {
            var age = DateTimeOffset.UtcNow - user.TokenGroupsLastComputed.Value;
            if (age < TimeSpan.FromMinutes(30)) // Accept materialized data up to 30 min old
            {
                return user.TokenGroupsSids
                    .Select(sid =>
                    {
                        var parts = sid.Split('-');
                        uint rid = parts.Length > 0 && uint.TryParse(parts[^1], out var r) ? r : 0;
                        return new GroupSidInfo
                        {
                            Sid = sid,
                            Attributes = SidAttributes.SE_GROUP_MANDATORY |
                                         SidAttributes.SE_GROUP_ENABLED_BY_DEFAULT |
                                         SidAttributes.SE_GROUP_ENABLED,
                        };
                    })
                    .Where(g => !string.IsNullOrEmpty(g.Sid))
                    .ToList();
            }
        }

        // Slow path: compute on the fly (existing batched BFS code)
        var result = new List<GroupSidInfo>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var toProcess = new Queue<string>(user.MemberOf);

        while (toProcess.Count > 0)
        {
            // Collect all unvisited DNs for this BFS level
            var batch = new List<string>();
            while (toProcess.Count > 0)
            {
                var dn = toProcess.Dequeue();
                if (visited.Add(dn))
                    batch.Add(dn);
            }

            if (batch.Count == 0) break;

            // Batch load all groups at this level
            var groups = await _store.GetByDnsAsync(tenantId, batch, ct);

            foreach (var group in groups)
            {
                if (group == null)
                {
                    _logger.LogDebug("Group not found during PAC expansion");
                    continue;
                }

                if (!string.IsNullOrEmpty(group.ObjectSid))
                {
                    result.Add(new GroupSidInfo
                    {
                        Sid = group.ObjectSid,
                        Attributes = SidAttributes.SE_GROUP_MANDATORY |
                                     SidAttributes.SE_GROUP_ENABLED_BY_DEFAULT |
                                     SidAttributes.SE_GROUP_ENABLED,
                    });
                }

                // Queue nested group memberships for next BFS level
                foreach (var nestedDn in group.MemberOf)
                {
                    if (!visited.Contains(nestedDn))
                        toProcess.Enqueue(nestedDn);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Converts our claims model to Kerberos.NET's native PAC claims types.
    /// This ensures claims are properly encoded in the PAC wire format by the library.
    /// </summary>
    private static KerbClaimsSetMetadata ConvertToKerberosClaimsSetMetadata(OurClaimsSet ourClaims)
    {
        var kerbArrays = new List<KerbClaimsArray>();

        foreach (var ourArray in ourClaims.ClaimsArrays)
        {
            var kerbEntries = new List<KerbClaimEntry>();

            foreach (var ourEntry in ourArray.ClaimEntries)
            {
                // Convert typed values to IList<object> for the Kerberos.NET ClaimEntry
                IList<object> values = ourEntry.Type switch
                {
                    OurClaimValueType.STRING =>
                        ourEntry.StringValues?.Cast<object>().ToList() ?? new List<object>(),
                    OurClaimValueType.INT64 =>
                        ourEntry.Int64Values?.Cast<object>().ToList() ?? new List<object>(),
                    OurClaimValueType.UINT64 =>
                        ourEntry.UInt64Values?.Cast<object>().ToList() ?? new List<object>(),
                    OurClaimValueType.BOOLEAN =>
                        ourEntry.BooleanValues?.Cast<object>().ToList() ?? new List<object>(),
                    _ => new List<object>(),
                };

                kerbEntries.Add(new KerbClaimEntry
                {
                    Id = ourEntry.Id,
                    Type = (KerbClaimType)(int)ourEntry.Type,
                    Values = values,
                });
            }

            kerbArrays.Add(new KerbClaimsArray
            {
                ClaimSource = (KerbClaimSourceType)(int)ourArray.ClaimSourceType,
                ClaimEntries = kerbEntries,
            });
        }

        return new KerbClaimsSetMetadata
        {
            ClaimsSet = new KerbClaimsSet
            {
                ClaimsArray = kerbArrays,
            },
        };
    }

    // SID parsing is handled inline where needed

    private static string GetNetBiosName(string domainDn)
    {
        var parsed = DistinguishedName.Parse(domainDn);
        return parsed.GetDomainDnsName().Split('.')[0].ToUpperInvariant();
    }

    private static string GetDnsNameFromDn(string domainDn)
    {
        var parsed = DistinguishedName.Parse(domainDn);
        return parsed.GetDomainDnsName();
    }

    private static string GetDomainSidFromUserSid(string userSid)
    {
        var lastDash = userSid.LastIndexOf('-');
        return lastDash > 0 ? userSid[..lastDash] : userSid;
    }

    private static int GetRidFromSid(string sid)
    {
        var lastDash = sid.LastIndexOf('-');
        if (lastDash > 0 && int.TryParse(sid[(lastDash + 1)..], out var rid))
            return rid;
        return 0;
    }
}

internal class GroupSidInfo
{
    public string Sid { get; set; } = "";
    public SidAttributes Attributes { get; set; }
}
