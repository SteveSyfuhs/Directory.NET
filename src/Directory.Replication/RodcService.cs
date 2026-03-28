using System.Collections.Concurrent;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Microsoft.Extensions.Logging;

namespace Directory.Replication;

/// <summary>
/// Read-Only Domain Controller (RODC) service. Manages Password Replication Policy (PRP)
/// and credential caching for RODC deployments per MS-ADTS section 3.1.1.10.
///
/// The PRP determines which accounts' passwords are allowed to be cached on the RODC:
/// - <c>msDS-RevealOnDemandGroup</c> (Allowed RODC Password Replication Group)
/// - <c>msDS-NeverRevealGroup</c> (Denied RODC Password Replication Group)
///
/// Deny takes precedence over allow. Accounts not in either list are denied by default.
/// </summary>
public class RodcService
{
    private readonly IDirectoryStore _store;
    private readonly INamingContextService _ncService;
    private readonly DcInstanceInfo _dcInfo;
    private readonly ILogger<RodcService> _logger;

    /// <summary>
    /// Distinguished names of accounts in the Allowed RODC Password Replication Group.
    /// Corresponds to <c>msDS-RevealOnDemandGroup</c> on the RODC's computer object.
    /// </summary>
    private readonly ConcurrentDictionary<string, bool> _allowedList = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Distinguished names of accounts in the Denied RODC Password Replication Group.
    /// Corresponds to <c>msDS-NeverRevealGroup</c> on the RODC's computer object.
    /// </summary>
    private readonly ConcurrentDictionary<string, bool> _deniedList = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Accounts whose credentials have been cached on this RODC.
    /// Corresponds to <c>msDS-RevealedList</c> / <c>msDS-RevealedUsers</c>.
    /// </summary>
    private readonly ConcurrentDictionary<string, CachedCredentialEntry> _cachedPasswords = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether this DC instance is operating as a Read-Only Domain Controller.
    /// </summary>
    public bool IsRodc { get; set; }

    public RodcService(
        IDirectoryStore store,
        INamingContextService ncService,
        DcInstanceInfo dcInfo,
        ILogger<RodcService> logger)
    {
        _store = store;
        _ncService = ncService;
        _dcInfo = dcInfo;
        _logger = logger;
    }

    /// <summary>
    /// Gets the current allowed list (msDS-RevealOnDemandGroup).
    /// </summary>
    public IReadOnlyCollection<string> AllowedList => _allowedList.Keys.ToList().AsReadOnly();

    /// <summary>
    /// Gets the current denied list (msDS-NeverRevealGroup).
    /// </summary>
    public IReadOnlyCollection<string> DeniedList => _deniedList.Keys.ToList().AsReadOnly();

    /// <summary>
    /// Adds an account DN to the Allowed RODC Password Replication Group.
    /// </summary>
    public void AddToAllowedList(string accountDn)
    {
        _allowedList[accountDn] = true;
        _logger.LogInformation("Added {AccountDn} to RODC allowed password replication list", accountDn);
    }

    /// <summary>
    /// Removes an account DN from the Allowed RODC Password Replication Group.
    /// </summary>
    public void RemoveFromAllowedList(string accountDn)
    {
        _allowedList.TryRemove(accountDn, out _);
        _logger.LogInformation("Removed {AccountDn} from RODC allowed password replication list", accountDn);
    }

    /// <summary>
    /// Adds an account DN to the Denied RODC Password Replication Group.
    /// </summary>
    public void AddToDeniedList(string accountDn)
    {
        _deniedList[accountDn] = true;
        _logger.LogInformation("Added {AccountDn} to RODC denied password replication list", accountDn);
    }

    /// <summary>
    /// Removes an account DN from the Denied RODC Password Replication Group.
    /// </summary>
    public void RemoveFromDeniedList(string accountDn)
    {
        _deniedList.TryRemove(accountDn, out _);
        _logger.LogInformation("Removed {AccountDn} from RODC denied password replication list", accountDn);
    }

    /// <summary>
    /// Determines whether a given account's password is allowed to be cached on this RODC
    /// according to the Password Replication Policy.
    ///
    /// The evaluation order per MS-ADTS 3.1.1.10.4:
    /// 1. If the account is in the Denied list, caching is NOT allowed (deny wins).
    /// 2. If the account is in the Allowed list, caching IS allowed.
    /// 3. Otherwise, caching is NOT allowed (default deny).
    /// </summary>
    /// <param name="userDn">The distinguished name of the user/computer account.</param>
    /// <returns>True if the password may be cached on this RODC; false otherwise.</returns>
    public bool IsPasswordCacheable(string userDn)
    {
        if (string.IsNullOrEmpty(userDn))
            return false;

        // Deny list takes precedence
        if (_deniedList.ContainsKey(userDn))
        {
            _logger.LogDebug("Password caching denied for {UserDn} — in denied list", userDn);
            return false;
        }

        // Check allowed list
        if (_allowedList.ContainsKey(userDn))
        {
            _logger.LogDebug("Password caching allowed for {UserDn} — in allowed list", userDn);
            return true;
        }

        // Default: deny
        _logger.LogDebug("Password caching denied for {UserDn} — not in any PRP list (default deny)", userDn);
        return false;
    }

    /// <summary>
    /// Returns a list of accounts whose credentials are currently cached on this RODC.
    /// Corresponds to the <c>msDS-RevealedList</c> attribute.
    /// </summary>
    public IReadOnlyList<CachedCredentialEntry> GetCachedPasswords()
    {
        return _cachedPasswords.Values.ToList().AsReadOnly();
    }

    /// <summary>
    /// Pre-caches a user's password on this RODC if the Password Replication Policy allows it.
    /// This operation simulates the RODC requesting credentials from a writable DC via
    /// DRS GetNCChanges with EXOP_REPL_SECRETS.
    ///
    /// Steps per MS-ADTS 3.1.1.10.4:
    /// 1. Verify the user's DN is in the allowed list (msDS-RevealOnDemandGroup)
    /// 2. Verify the user's DN is NOT in the denied list (msDS-NeverRevealGroup) — deny wins
    /// 3. Fetch the user's credential attributes (NTHash, KerberosKeys) from the writable DC via store
    /// 4. Cache them locally for this RODC
    /// </summary>
    /// <param name="userDn">Distinguished name of the account to pre-cache.</param>
    /// <param name="tenantId">Tenant identifier for multi-tenant deployments.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the password was successfully cached; false if denied by PRP or account not found.</returns>
    public async Task<bool> PrecachePasswordAsync(string userDn, string tenantId, CancellationToken ct = default)
    {
        if (!IsRodc)
        {
            _logger.LogWarning("PrecachePassword called but this DC is not an RODC");
            return false;
        }

        // Step 1 & 2: Check PRP — denied list takes precedence over allowed list
        if (!IsPasswordCacheable(userDn))
        {
            _logger.LogWarning("Cannot pre-cache password for {UserDn} — denied by PRP", userDn);
            return false;
        }

        // Step 3: Fetch the account from the writable DC partition via the directory store.
        // In a real AD deployment, this would be a DRS GetNCChanges call with
        // EXOP_REPL_SECRETS to the writable DC. Here we read from the shared store.
        var account = await _store.GetByDnAsync(tenantId, userDn, ct);
        if (account is null)
        {
            _logger.LogWarning("Cannot pre-cache password for {UserDn} — account not found", userDn);
            return false;
        }

        // Verify the account has credential data to cache
        var hasNtHash = !string.IsNullOrEmpty(account.NTHash);
        var hasKerberosKeys = account.KerberosKeys != null && account.KerberosKeys.Count > 0;

        if (!hasNtHash && !hasKerberosKeys)
        {
            _logger.LogWarning(
                "Cannot pre-cache password for {UserDn} — no credential attributes found on the account",
                userDn);
            return false;
        }

        // Also check the objectSid if available — this provides an additional
        // cross-reference for PRP evaluation by SID in addition to DN.
        if (!string.IsNullOrEmpty(account.ObjectSid))
        {
            if (_deniedList.ContainsKey(account.ObjectSid))
            {
                _logger.LogWarning(
                    "Cannot pre-cache password for {UserDn} — SID {Sid} is in denied list",
                    userDn, account.ObjectSid);
                return false;
            }
        }

        // Step 4: Cache the credentials locally on this RODC
        var entry = new CachedCredentialEntry
        {
            AccountDn = userDn,
            SamAccountName = account.SAMAccountName ?? "",
            ObjectSid = account.ObjectSid ?? "",
            NTHash = account.NTHash ?? "",
            HasKerberosKeys = hasKerberosKeys,
            CachedAt = DateTimeOffset.UtcNow,
        };

        _cachedPasswords[userDn] = entry;

        _logger.LogInformation(
            "Pre-cached credentials for {AccountDn} (SAMAccountName={Sam}, SID={Sid}) on RODC. " +
            "NTHash={HasNtHash}, KerberosKeys={HasKerberos}",
            userDn, entry.SamAccountName, entry.ObjectSid, hasNtHash, hasKerberosKeys);

        return true;
    }

    /// <summary>
    /// Checks whether an account's credentials are currently cached on this RODC.
    /// </summary>
    public bool IsCredentialCached(string userDn)
    {
        return _cachedPasswords.ContainsKey(userDn);
    }

    /// <summary>
    /// Removes a cached credential entry (e.g., after password change notification).
    /// </summary>
    public void InvalidateCachedCredential(string userDn)
    {
        if (_cachedPasswords.TryRemove(userDn, out _))
        {
            _logger.LogInformation("Invalidated cached credentials for {UserDn} on RODC", userDn);
        }
    }

    /// <summary>
    /// Loads the PRP lists from the RODC's nTDSDSA object attributes in the directory.
    /// Reads <c>msDS-RevealOnDemandGroup</c> and <c>msDS-NeverRevealGroup</c>.
    /// </summary>
    public async Task LoadPolicyFromDirectoryAsync(string tenantId, CancellationToken ct = default)
    {
        var domainNc = _ncService.GetDomainNc();
        var ntdsDn = _dcInfo.NtdsSettingsDn(domainNc.Dn);

        var ntdsObj = await _store.GetByDnAsync(tenantId, ntdsDn, ct);
        if (ntdsObj is null)
        {
            _logger.LogDebug("NTDS Settings object not found at {Dn}, skipping PRP load", ntdsDn);
            return;
        }

        // Load allowed list from msDS-RevealOnDemandGroup
        if (ntdsObj.Attributes.TryGetValue("msDS-RevealOnDemandGroup", out var allowedAttr))
        {
            _allowedList.Clear();
            foreach (var val in allowedAttr.Values)
            {
                if (val is string dn && !string.IsNullOrEmpty(dn))
                    _allowedList[dn] = true;
            }
            _logger.LogInformation("Loaded {Count} entries into RODC allowed PRP list", _allowedList.Count);
        }

        // Load denied list from msDS-NeverRevealGroup
        if (ntdsObj.Attributes.TryGetValue("msDS-NeverRevealGroup", out var deniedAttr))
        {
            _deniedList.Clear();
            foreach (var val in deniedAttr.Values)
            {
                if (val is string dn && !string.IsNullOrEmpty(dn))
                    _deniedList[dn] = true;
            }
            _logger.LogInformation("Loaded {Count} entries into RODC denied PRP list", _deniedList.Count);
        }
    }
}

/// <summary>
/// Represents an account whose credentials are cached on an RODC.
/// Corresponds to an entry in the <c>msDS-RevealedList</c> attribute.
/// </summary>
public class CachedCredentialEntry
{
    /// <summary>
    /// Distinguished name of the cached account.
    /// </summary>
    public string AccountDn { get; init; } = "";

    /// <summary>
    /// SAM account name for display/lookup purposes.
    /// </summary>
    public string SamAccountName { get; init; } = "";

    /// <summary>
    /// Security Identifier of the cached account.
    /// </summary>
    public string ObjectSid { get; init; } = "";

    /// <summary>
    /// The cached NT hash (hex-encoded). Empty if the account had no NT hash.
    /// </summary>
    public string NTHash { get; init; } = "";

    /// <summary>
    /// Whether Kerberos long-term keys were cached for this account.
    /// </summary>
    public bool HasKerberosKeys { get; init; }

    /// <summary>
    /// When the credentials were cached.
    /// </summary>
    public DateTimeOffset CachedAt { get; init; }
}
