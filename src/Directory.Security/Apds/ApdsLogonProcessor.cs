using System.Security.Cryptography;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Microsoft.Extensions.Logging;

namespace Directory.Security.Apds;

/// <summary>
/// Result of a logon processing operation per MS-APDS 3.1.5.
/// </summary>
public class LogonResult
{
    /// <summary>
    /// The NT status code indicating the result of authentication.
    /// </summary>
    public NtStatus Status { get; set; }

    /// <summary>
    /// The directory object of the authenticated user (null on failure).
    /// </summary>
    public DirectoryObject User { get; set; }

    /// <summary>
    /// Validation information populated on successful authentication.
    /// </summary>
    public NetlogonValidationSamInfo4 ValidationInfo { get; set; }

    /// <summary>
    /// The computed session key for the authenticated session (null on failure).
    /// </summary>
    public byte[] SessionKey { get; set; }

    /// <summary>
    /// Authorization data including group memberships and extra SIDs.
    /// </summary>
    public AuthorizationData Authorization { get; set; }
}

/// <summary>
/// Authorization data derived from a successful logon, including all group
/// memberships and extra SIDs from SID history and universal groups.
/// </summary>
public class AuthorizationData
{
    /// <summary>
    /// The user's SID.
    /// </summary>
    public string UserSid { get; set; } = string.Empty;

    /// <summary>
    /// All group SIDs the user is a member of (direct and transitive).
    /// </summary>
    public List<SidAndAttributes> GroupSids { get; set; } = [];

    /// <summary>
    /// Extra SIDs from SID history, universal groups, etc.
    /// </summary>
    public List<SidAndAttributes> ExtraSids { get; set; } = [];
}

/// <summary>
/// Main logon processing engine implementing MS-APDS section 3.1.5.
/// Handles interactive, network, batch, service, and remote interactive logon types
/// with full account restriction checking, bad password tracking, and validation info generation.
/// </summary>
public class ApdsLogonProcessor
{
    private readonly IDirectoryStore _store;
    private readonly IPasswordPolicy _passwordPolicy;
    private readonly INamingContextService _ncService;
    private readonly AccountRestrictions _accountRestrictions;
    private readonly NtlmPassThrough _ntlmPassThrough;
    private readonly ILogger<ApdsLogonProcessor> _logger;

    public ApdsLogonProcessor(
        IDirectoryStore store,
        IPasswordPolicy passwordPolicy,
        INamingContextService ncService,
        AccountRestrictions accountRestrictions,
        NtlmPassThrough ntlmPassThrough,
        ILogger<ApdsLogonProcessor> logger)
    {
        _store = store;
        _passwordPolicy = passwordPolicy;
        _ncService = ncService;
        _accountRestrictions = accountRestrictions;
        _ntlmPassThrough = ntlmPassThrough;
        _logger = logger;
    }

    /// <summary>
    /// Processes an interactive logon (cleartext password).
    /// MS-APDS 3.1.5: Interactive logon uses KERB_INTERACTIVE_LOGON or
    /// MSV1_0_INTERACTIVE_LOGON to validate cleartext credentials.
    /// </summary>
    public async Task<LogonResult> ProcessInteractiveLogonAsync(
        string tenantId,
        string domain,
        string username,
        string password,
        CancellationToken ct = default)
    {
        return await ProcessPasswordLogonAsync(
            tenantId, domain, username, password, LogonType.Interactive, ct);
    }

    /// <summary>
    /// Processes a network logon (NTLM challenge/response).
    /// MS-APDS 3.1.5: Network logon validates NTLM responses against the server challenge.
    /// </summary>
    public async Task<LogonResult> ProcessNetworkLogonAsync(
        string tenantId,
        string domain,
        string username,
        byte[] ntResponse,
        byte[] lmResponse,
        byte[] challenge,
        CancellationToken ct = default)
    {
        _logger.LogDebug("Processing network logon for {Domain}\\{User}", domain, username);

        var user = await ResolveUserAsync(tenantId, domain, username, ct);
        if (user is null)
        {
            return new LogonResult { Status = NtStatus.StatusNoSuchUser };
        }

        // Check account restrictions
        var restrictionStatus = _accountRestrictions.CheckAccountRestrictions(user, LogonType.Network);
        if (restrictionStatus != NtStatus.StatusSuccess)
        {
            return new LogonResult { Status = restrictionStatus };
        }

        // Validate NTLM response
        if (string.IsNullOrEmpty(user.NTHash))
        {
            return new LogonResult { Status = NtStatus.StatusWrongPassword };
        }

        var validationResult = _ntlmPassThrough.ValidateNtlmResponse(
            user, domain, username, challenge, ntResponse, lmResponse);

        if (!validationResult.IsValid)
        {
            // Bad password: update count and possibly lock out
            _accountRestrictions.UpdateBadPasswordCount(user);
            await _store.UpdateAsync(user, ct);

            _logger.LogDebug("Network logon failed for {User}: bad credentials", username);
            return new LogonResult { Status = NtStatus.StatusLogonFailure };
        }

        // Successful authentication
        _accountRestrictions.ResetBadPasswordCount(user);
        user.LastLogon = DateTimeOffset.UtcNow.ToFileTime();
        await _store.UpdateAsync(user, ct);

        var validationInfo = await BuildValidationInfoAsync(tenantId, user, domain, ct);
        validationInfo.UserSessionKey = validationResult.SessionKey ?? new byte[16];

        return new LogonResult
        {
            Status = NtStatus.StatusSuccess,
            User = user,
            ValidationInfo = validationInfo,
            SessionKey = validationResult.SessionKey,
            Authorization = BuildAuthorizationData(user, validationInfo),
        };
    }

    /// <summary>
    /// Processes a service logon (cleartext password).
    /// MS-APDS 3.1.5: Service logon follows the same flow as interactive
    /// but uses LogonType.Service for policy checks.
    /// </summary>
    public async Task<LogonResult> ProcessServiceLogonAsync(
        string tenantId,
        string domain,
        string username,
        string password,
        CancellationToken ct = default)
    {
        return await ProcessPasswordLogonAsync(
            tenantId, domain, username, password, LogonType.Service, ct);
    }

    /// <summary>
    /// Processes a batch logon (cleartext password for scheduled tasks).
    /// MS-APDS 3.1.5: Batch logon uses the same validation as interactive
    /// but with LogonType.Batch.
    /// </summary>
    public async Task<LogonResult> ProcessBatchLogonAsync(
        string tenantId,
        string domain,
        string username,
        string password,
        CancellationToken ct = default)
    {
        return await ProcessPasswordLogonAsync(
            tenantId, domain, username, password, LogonType.Batch, ct);
    }

    /// <summary>
    /// Processes a remote interactive logon (RDP/Terminal Services).
    /// MS-APDS 3.1.5: Remote interactive logon follows the interactive flow
    /// but uses LogonType.RemoteInteractive for workstation restriction checks.
    /// </summary>
    public async Task<LogonResult> ProcessRemoteInteractiveLogonAsync(
        string tenantId,
        string domain,
        string username,
        string password,
        CancellationToken ct = default)
    {
        return await ProcessPasswordLogonAsync(
            tenantId, domain, username, password, LogonType.RemoteInteractive, ct);
    }

    /// <summary>
    /// Common implementation for password-based logon types (interactive, service, batch, remote).
    /// </summary>
    private async Task<LogonResult> ProcessPasswordLogonAsync(
        string tenantId,
        string domain,
        string username,
        string password,
        LogonType logonType,
        CancellationToken ct)
    {
        _logger.LogDebug("Processing {LogonType} logon for {Domain}\\{User}", logonType, domain, username);

        var user = await ResolveUserAsync(tenantId, domain, username, ct);
        if (user is null)
        {
            return new LogonResult { Status = NtStatus.StatusNoSuchUser };
        }

        // Check account restrictions
        var restrictionStatus = _accountRestrictions.CheckAccountRestrictions(user, logonType);
        if (restrictionStatus != NtStatus.StatusSuccess)
        {
            return new LogonResult { Status = restrictionStatus };
        }

        // Validate password
        if (string.IsNullOrEmpty(user.NTHash))
        {
            return new LogonResult { Status = NtStatus.StatusWrongPassword };
        }

        var computedHash = _passwordPolicy.ComputeNTHash(password);
        var storedHash = Convert.FromHexString(user.NTHash);

        if (!CryptographicOperations.FixedTimeEquals(computedHash, storedHash))
        {
            // Bad password
            _accountRestrictions.UpdateBadPasswordCount(user);
            await _store.UpdateAsync(user, ct);

            _logger.LogDebug("{LogonType} logon failed for {User}: wrong password", logonType, username);
            return new LogonResult { Status = NtStatus.StatusLogonFailure };
        }

        // Successful authentication
        _accountRestrictions.ResetBadPasswordCount(user);
        user.LastLogon = DateTimeOffset.UtcNow.ToFileTime();
        await _store.UpdateAsync(user, ct);

        var validationInfo = await BuildValidationInfoAsync(tenantId, user, domain, ct);

        // For interactive logons, generate a random session key
        var sessionKey = RandomNumberGenerator.GetBytes(16);
        validationInfo.UserSessionKey = sessionKey;

        return new LogonResult
        {
            Status = NtStatus.StatusSuccess,
            User = user,
            ValidationInfo = validationInfo,
            SessionKey = sessionKey,
            Authorization = BuildAuthorizationData(user, validationInfo),
        };
    }

    /// <summary>
    /// Resolves a user from the directory by domain and username.
    /// Supports resolution by sAMAccountName or UPN.
    /// </summary>
    private async Task<DirectoryObject> ResolveUserAsync(
        string tenantId, string domain, string username, CancellationToken ct)
    {
        // Try to determine the domain DN
        string domainDn = ResolveDomainDn(domain);

        // First try by sAMAccountName
        var user = await _store.GetBySamAccountNameAsync(tenantId, domainDn, username, ct);
        if (user is not null)
            return user;

        // Try by UPN (username@domain)
        string upn = username.Contains('@') ? username : $"{username}@{domain}";
        user = await _store.GetByUpnAsync(tenantId, upn, ct);
        return user;
    }

    /// <summary>
    /// Converts a domain name (NetBIOS or DNS) to a distinguished name.
    /// </summary>
    private string ResolveDomainDn(string domain)
    {
        if (string.IsNullOrEmpty(domain))
        {
            return _ncService.GetDomainNc().Dn;
        }

        // If it already looks like a DN
        if (domain.StartsWith("DC=", StringComparison.OrdinalIgnoreCase))
        {
            return domain;
        }

        // Convert DNS-style domain name to DN
        return "DC=" + domain.ToLowerInvariant().Replace(".", ",DC=");
    }

    /// <summary>
    /// Builds the NETLOGON_VALIDATION_SAM_INFO4 structure for the authenticated user.
    /// Populates group memberships, SID history, and domain information.
    /// </summary>
    private async Task<NetlogonValidationSamInfo4> BuildValidationInfoAsync(
        string tenantId, DirectoryObject user, string domain, CancellationToken ct)
    {
        var domainNc = _ncService.GetDomainNc();
        var domainSid = domainNc.DomainSid ?? "S-1-5-21-0-0-0";

        // Extract RID from user SID
        uint userId = ExtractRid(user.ObjectSid ?? "");

        // Build group memberships
        var groupMemberships = new List<GroupMembership>();
        var extraSids = new List<SidAndAttributes>();

        // Add primary group
        groupMemberships.Add(new GroupMembership
        {
            RelativeId = (uint)user.PrimaryGroupId,
            Attributes = SeGroupAttributes.DefaultGroup,
        });

        // Enumerate direct group memberships
        foreach (var groupDn in user.MemberOf)
        {
            var group = await _store.GetByDnAsync(tenantId, groupDn, ct);
            if (group?.ObjectSid is null)
                continue;

            uint groupRid = ExtractRid(group.ObjectSid);

            // Domain-local and global groups go into GroupIds
            // Universal groups and cross-domain SIDs go into ExtraSids
            if (IsSameDomain(group.ObjectSid, domainSid))
            {
                groupMemberships.Add(new GroupMembership
                {
                    RelativeId = groupRid,
                    Attributes = SeGroupAttributes.DefaultGroup,
                });
            }
            else
            {
                extraSids.Add(new SidAndAttributes
                {
                    Sid = group.ObjectSid,
                    Attributes = SeGroupAttributes.DefaultGroup,
                });
            }
        }

        // Add well-known SIDs per MS-APDS
        // "Authenticated Users" (S-1-5-11)
        extraSids.Add(new SidAndAttributes
        {
            Sid = "S-1-5-11",
            Attributes = SeGroupAttributes.DefaultGroup,
        });

        // "Everyone" (S-1-1-0)
        extraSids.Add(new SidAndAttributes
        {
            Sid = "S-1-1-0",
            Attributes = SeGroupAttributes.DefaultGroup,
        });

        // Derive logon script, profile path, home directory from attributes
        string logonScript = user.GetAttribute("scriptPath")?.GetFirstString() ?? "";
        string profilePath = user.GetAttribute("profilePath")?.GetFirstString() ?? "";
        string homeDir = user.GetAttribute("homeDirectory")?.GetFirstString() ?? "";
        string homeDrive = user.GetAttribute("homeDrive")?.GetFirstString() ?? "";

        // Determine logon count
        var logonCountAttr = user.GetAttribute("logonCount");
        ushort logonCount = 0;
        if (logonCountAttr?.GetFirstString() is { } lcStr && ushort.TryParse(lcStr, out var lc))
            logonCount = lc;

        uint userFlags = 0;
        if (extraSids.Count > 0)
            userFlags |= 0x0020; // LOGON_EXTRA_SIDS

        var info = new NetlogonValidationSamInfo4
        {
            LogonTime = DateTimeOffset.UtcNow.ToFileTime(),
            LogoffTime = 0x7FFFFFFFFFFFFFFF,
            KickOffTime = 0x7FFFFFFFFFFFFFFF,
            PasswordLastSet = user.PwdLastSet,
            PasswordCanChange = user.PwdLastSet, // Simplified; would use minPwdAge
            PasswordMustChange = 0x7FFFFFFFFFFFFFFF,
            EffectiveName = user.SAMAccountName ?? "",
            FullName = user.DisplayName ?? user.Cn ?? "",
            LogonScript = logonScript,
            ProfilePath = profilePath,
            HomeDirectory = homeDir,
            HomeDirectoryDrive = homeDrive,
            LogonCount = logonCount,
            BadPasswordCount = (ushort)user.BadPwdCount,
            UserId = userId,
            PrimaryGroupId = (uint)user.PrimaryGroupId,
            GroupIds = groupMemberships,
            UserFlags = userFlags,
            LogonServer = Environment.MachineName,
            LogonDomainName = domainNc.DnsName.Split('.')[0].ToUpperInvariant(),
            LogonDomainId = domainSid,
            ExtraSids = extraSids,
            DnsLogonDomainName = domainNc.DnsName,
            Upn = user.UserPrincipalName ?? "",
        };

        return info;
    }

    /// <summary>
    /// Builds authorization data from the validation info for use in access checks.
    /// </summary>
    private static AuthorizationData BuildAuthorizationData(
        DirectoryObject user, NetlogonValidationSamInfo4 validationInfo)
    {
        var groupSids = new List<SidAndAttributes>();

        // Convert group RIDs to full SIDs
        foreach (var group in validationInfo.GroupIds)
        {
            string groupSid = $"{validationInfo.LogonDomainId}-{group.RelativeId}";
            groupSids.Add(new SidAndAttributes
            {
                Sid = groupSid,
                Attributes = group.Attributes,
            });
        }

        return new AuthorizationData
        {
            UserSid = user.ObjectSid ?? "",
            GroupSids = groupSids,
            ExtraSids = validationInfo.ExtraSids,
        };
    }

    /// <summary>
    /// Extracts the RID (last sub-authority) from a SID string.
    /// </summary>
    private static uint ExtractRid(string sid)
    {
        if (string.IsNullOrEmpty(sid))
            return 0;

        int lastDash = sid.LastIndexOf('-');
        if (lastDash < 0)
            return 0;

        return uint.TryParse(sid.AsSpan(lastDash + 1), out uint rid) ? rid : 0;
    }

    /// <summary>
    /// Returns true if the given SID belongs to the same domain as the domain SID.
    /// </summary>
    private static bool IsSameDomain(string objectSid, string domainSid)
    {
        if (string.IsNullOrEmpty(objectSid) || string.IsNullOrEmpty(domainSid))
            return false;

        // The domain SID is everything up to the last sub-authority
        int lastDash = objectSid.LastIndexOf('-');
        if (lastDash < 0)
            return false;

        string objectDomainPart = objectSid[..lastDash];
        return string.Equals(objectDomainPart, domainSid, StringComparison.OrdinalIgnoreCase);
    }
}
