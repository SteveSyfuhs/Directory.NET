using Directory.Core.Interfaces;
using Directory.Core.Models;
using Microsoft.Extensions.Logging;

namespace Directory.Web.Services;

/// <summary>
/// Read-Only Domain Controller (RODC) mode service.
/// When enabled, write operations are blocked and password caching is restricted.
/// </summary>
public class RodcService
{
    private readonly ILogger<RodcService> _logger;
    private readonly IDirectoryStore _store;

    /// <summary>
    /// Attributes that are part of the RODC filtered attribute set (FAS).
    /// These are never replicated to RODCs unless the user is in the Password Replication Policy.
    /// </summary>
    private static readonly HashSet<string> CredentialAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "ntHash", "kerberosKeys", "unicodePwd", "dbcsPwd",
        "supplementalCredentials", "passwordHistory",
    };

    public RodcSettings Settings { get; } = new();

    // Track cached password principals
    private readonly HashSet<string> _cachedPasswords = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Tracks the highest USN that has been replicated from the writable DC.
    /// Used to pull only changes since the last replication cycle.
    /// </summary>
    private long _lastReplicatedUsn;

    public RodcService(IDirectoryStore store, ILogger<RodcService> logger)
    {
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// Check whether the DC is in read-only mode.
    /// </summary>
    public bool IsReadOnly => Settings.IsRodc;

    /// <summary>
    /// Update RODC settings (enable/disable RODC mode).
    /// </summary>
    public RodcSettings UpdateSettings(
        bool? isRodc = null,
        string fullDcEndpoint = null,
        List<string> passwordReplicationAllowed = null,
        List<string> passwordReplicationDenied = null)
    {
        if (isRodc.HasValue)
        {
            Settings.IsRodc = isRodc.Value;
            _logger.LogInformation("RODC mode {State}", isRodc.Value ? "enabled" : "disabled");
        }

        if (fullDcEndpoint != null)
            Settings.FullDcEndpoint = fullDcEndpoint;

        if (passwordReplicationAllowed != null)
            Settings.PasswordReplicationAllowed = passwordReplicationAllowed;

        if (passwordReplicationDenied != null)
            Settings.PasswordReplicationDenied = passwordReplicationDenied;

        return Settings;
    }

    /// <summary>
    /// Add a principal (DN or group) to the password replication allowed list.
    /// </summary>
    public void AddPasswordReplicationAllowed(string principal)
    {
        if (!Settings.PasswordReplicationAllowed.Contains(principal, StringComparer.OrdinalIgnoreCase))
        {
            Settings.PasswordReplicationAllowed.Add(principal);
            _logger.LogInformation("Added principal to RODC password replication allowed list: {Principal}", principal);
        }
    }

    /// <summary>
    /// Remove a principal from the password replication allowed list.
    /// </summary>
    public void RemovePasswordReplicationAllowed(string principal)
    {
        Settings.PasswordReplicationAllowed.RemoveAll(p =>
            p.Equals(principal, StringComparison.OrdinalIgnoreCase));
        _logger.LogInformation("Removed principal from RODC password replication allowed list: {Principal}", principal);
    }

    /// <summary>
    /// Add a principal to the password replication denied list.
    /// </summary>
    public void AddPasswordReplicationDenied(string principal)
    {
        if (!Settings.PasswordReplicationDenied.Contains(principal, StringComparer.OrdinalIgnoreCase))
        {
            Settings.PasswordReplicationDenied.Add(principal);
            _logger.LogInformation("Added principal to RODC password replication denied list: {Principal}", principal);
        }
    }

    /// <summary>
    /// Remove a principal from the password replication denied list.
    /// </summary>
    public void RemovePasswordReplicationDenied(string principal)
    {
        Settings.PasswordReplicationDenied.RemoveAll(p =>
            p.Equals(principal, StringComparison.OrdinalIgnoreCase));
        _logger.LogInformation("Removed principal from RODC password replication denied list: {Principal}", principal);
    }

    /// <summary>
    /// Check whether a principal is allowed to have their password cached on this RODC.
    /// Denied list takes priority over allowed list.
    /// </summary>
    public bool IsPasswordCachingAllowed(string principalDn)
    {
        // Deny list takes precedence
        if (Settings.PasswordReplicationDenied.Any(d =>
            d.Equals(principalDn, StringComparison.OrdinalIgnoreCase)))
            return false;

        // Check allow list
        return Settings.PasswordReplicationAllowed.Any(a =>
            a.Equals(principalDn, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Get list of principals whose passwords are currently cached.
    /// </summary>
    public IReadOnlyList<string> GetCachedPasswordPrincipals()
    {
        return _cachedPasswords.ToList();
    }

    /// <summary>
    /// Cache a principal's password (only if allowed by policy).
    /// </summary>
    public bool CachePassword(string principalDn)
    {
        if (!IsPasswordCachingAllowed(principalDn))
        {
            _logger.LogWarning("Password caching denied for principal: {Principal}", principalDn);
            return false;
        }

        _cachedPasswords.Add(principalDn);
        return true;
    }

    /// <summary>
    /// Pulls replication changes from the writable DC partition using USN-based queries.
    /// Respects the RODC filtered attribute set: credential attributes (ntHash, kerberosKeys, etc.)
    /// are stripped from replicated objects unless the object's DN is in the Password Replication Policy.
    /// </summary>
    public async Task<RodcReplicationResult> TriggerReplicationAsync(CancellationToken ct = default)
    {
        if (!Settings.IsRodc)
            throw new InvalidOperationException("Replication pull is only available in RODC mode.");

        if (string.IsNullOrEmpty(Settings.FullDcEndpoint))
            throw new InvalidOperationException("No writable DC endpoint configured for replication.");

        _logger.LogInformation(
            "Triggering pull replication from {Endpoint}, USN since {Usn}",
            Settings.FullDcEndpoint, _lastReplicatedUsn);

        try
        {
            // Query the writable DC partition for all objects changed since our last replicated USN.
            // The FullDcEndpoint identifies the tenant/partition; we use "default" as the tenant.
            var filter = new GreaterOrEqualFilterNode("uSNChanged", (_lastReplicatedUsn + 1).ToString());
            var searchResult = await _store.SearchAsync(
                "default",
                Settings.FullDcEndpoint,
                SearchScope.WholeSubtree,
                filter,
                null,
                sizeLimit: 0,
                timeLimitSeconds: 0,
                continuationToken: null,
                pageSize: 10000,
                includeDeleted: true,
                ct: ct);

            int objectsReplicated = 0;
            long highestUsn = _lastReplicatedUsn;

            foreach (var entry in searchResult.Entries)
            {
                // Apply the RODC filtered attribute set: strip credential attributes
                // unless the object is in the Password Replication Policy (allowed list).
                bool passwordAllowed = IsPasswordCachingAllowed(entry.DistinguishedName);

                if (!passwordAllowed)
                {
                    // Remove credential attributes from the replicated object — these must
                    // not be stored on the RODC unless explicitly allowed by PRP.
                    foreach (var credAttr in CredentialAttributes)
                    {
                        entry.Attributes.Remove(credAttr);
                    }

                    // Also clear the top-level credential properties
                    entry.NTHash = null;
                    entry.KerberosKeys = new List<string>();
                }

                // Apply the change locally by updating the store
                await _store.UpdateAsync(entry, ct);
                objectsReplicated++;

                if (entry.USNChanged > highestUsn)
                    highestUsn = entry.USNChanged;
            }

            _lastReplicatedUsn = highestUsn;
            Settings.LastReplicationTime = DateTimeOffset.UtcNow;

            _logger.LogInformation(
                "Pull replication completed: {Count} objects replicated, highest USN={Usn}",
                objectsReplicated, highestUsn);

            return new RodcReplicationResult
            {
                Success = true,
                SourceDc = Settings.FullDcEndpoint,
                ReplicationTime = Settings.LastReplicationTime.Value,
                ObjectsReplicated = objectsReplicated,
                Message = $"Pull replication completed successfully. {objectsReplicated} objects replicated.",
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pull replication from {Endpoint} failed", Settings.FullDcEndpoint);

            return new RodcReplicationResult
            {
                Success = false,
                SourceDc = Settings.FullDcEndpoint,
                ReplicationTime = DateTimeOffset.UtcNow,
                ObjectsReplicated = 0,
                Message = $"Pull replication failed: {ex.Message}",
            };
        }
    }
}

// ── Data Models ────────────────────────────────────────────────────────

public class RodcSettings
{
    public bool IsRodc { get; set; }
    public List<string> PasswordReplicationAllowed { get; set; } = new();
    public List<string> PasswordReplicationDenied { get; set; } = new();
    public string FullDcEndpoint { get; set; } = "";
    public DateTimeOffset? LastReplicationTime { get; set; }
}

public class RodcReplicationResult
{
    public bool Success { get; set; }
    public string SourceDc { get; set; } = "";
    public DateTimeOffset ReplicationTime { get; set; }
    public int ObjectsReplicated { get; set; }
    public string Message { get; set; } = "";
}
