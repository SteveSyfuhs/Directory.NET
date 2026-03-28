using Directory.Core.Interfaces;
using Directory.Core.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Directory.Security;

/// <summary>
/// Tracks failed authentication attempts and enforces account lockout policy.
/// Uses an in-memory ConcurrentDictionary for fast lookups and persists
/// AD-compatible attributes (lockoutTime, badPwdCount, badPasswordTime) on user objects.
/// </summary>
public class AccountLockoutService
{
    private readonly ConcurrentDictionary<string, LockoutState> _lockoutStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly IDirectoryStore _store;
    private readonly ILogger<AccountLockoutService> _logger;

    public AccountLockoutPolicy Policy { get; set; } = new();

    public AccountLockoutService(IDirectoryStore store, ILogger<AccountLockoutService> logger)
    {
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// Records a failed authentication attempt. If the threshold is reached the account is locked out.
    /// </summary>
    public async Task RecordFailedAttempt(string dn)
    {
        if (!Policy.LockoutEnabled || Policy.LockoutThreshold <= 0)
            return;

        var now = DateTimeOffset.UtcNow;
        var state = _lockoutStates.GetOrAdd(NormalizeKey(dn), _ => new LockoutState());

        lock (state)
        {
            // If inside the observation window, increment; otherwise reset to 1
            if (state.LastFailedAttempt.HasValue &&
                now - state.LastFailedAttempt.Value > Policy.LockoutObservationWindow)
            {
                state.FailedAttemptCount = 0;
            }

            state.FailedAttemptCount++;
            state.LastFailedAttempt = now;

            if (state.FailedAttemptCount >= Policy.LockoutThreshold)
            {
                state.LockoutTime = now;
                _logger.LogWarning("Account locked out: {DN} after {Count} failed attempts", dn, state.FailedAttemptCount);
            }
        }

        await PersistLockoutAttributesAsync(dn, state);
    }

    /// <summary>
    /// Records a successful login, resetting all lockout counters.
    /// </summary>
    public async Task RecordSuccessfulLogin(string dn)
    {
        var key = NormalizeKey(dn);

        if (_lockoutStates.TryRemove(key, out _))
        {
            _logger.LogDebug("Lockout state cleared for {DN} on successful login", dn);
        }

        await ClearLockoutAttributesAsync(dn);
    }

    /// <summary>
    /// Checks whether the account is currently locked out.
    /// Automatically unlocks accounts whose lockout duration has expired.
    /// </summary>
    public bool IsLockedOut(string dn)
    {
        if (!Policy.LockoutEnabled)
            return false;

        var key = NormalizeKey(dn);
        if (!_lockoutStates.TryGetValue(key, out var state))
            return false;

        lock (state)
        {
            if (!state.LockoutTime.HasValue)
                return false;

            // Duration of Zero means locked until admin unlocks
            if (Policy.LockoutDuration == TimeSpan.Zero)
                return true;

            if (DateTimeOffset.UtcNow - state.LockoutTime.Value >= Policy.LockoutDuration)
            {
                // Auto-unlock
                state.LockoutTime = null;
                state.FailedAttemptCount = 0;
                state.LastFailedAttempt = null;
                return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Administrative unlock of a specific account.
    /// </summary>
    public async Task UnlockAccount(string dn)
    {
        var key = NormalizeKey(dn);

        if (_lockoutStates.TryRemove(key, out _))
        {
            _logger.LogInformation("Account unlocked by admin: {DN}", dn);
        }

        await ClearLockoutAttributesAsync(dn);
    }

    /// <summary>
    /// Returns current lockout information for an account.
    /// </summary>
    public LockoutInfo GetLockoutInfo(string dn)
    {
        var key = NormalizeKey(dn);
        var isLocked = IsLockedOut(dn);

        if (_lockoutStates.TryGetValue(key, out var state))
        {
            lock (state)
            {
                return new LockoutInfo
                {
                    DistinguishedName = dn,
                    FailedAttemptCount = state.FailedAttemptCount,
                    LockoutTime = state.LockoutTime,
                    LastFailedAttempt = state.LastFailedAttempt,
                    IsLockedOut = isLocked,
                };
            }
        }

        return new LockoutInfo
        {
            DistinguishedName = dn,
            FailedAttemptCount = 0,
            LockoutTime = null,
            LastFailedAttempt = null,
            IsLockedOut = false,
        };
    }

    /// <summary>
    /// Returns all accounts that are currently locked out.
    /// </summary>
    public List<LockoutInfo> GetLockedAccounts()
    {
        var result = new List<LockoutInfo>();

        foreach (var kvp in _lockoutStates)
        {
            var info = GetLockoutInfo(kvp.Key);
            if (info.IsLockedOut)
                result.Add(info);
        }

        return result;
    }

    private static string NormalizeKey(string dn) => dn.Trim();

    private async Task PersistLockoutAttributesAsync(string dn, LockoutState state)
    {
        try
        {
            var obj = await _store.GetByDnAsync("default", dn);
            if (obj is null) return;

            lock (state)
            {
                obj.SetAttribute("badPwdCount",
                    new DirectoryAttribute("badPwdCount", state.FailedAttemptCount.ToString()));

                if (state.LastFailedAttempt.HasValue)
                {
                    obj.SetAttribute("badPasswordTime",
                        new DirectoryAttribute("badPasswordTime", state.LastFailedAttempt.Value.ToFileTime().ToString()));
                }

                if (state.LockoutTime.HasValue)
                {
                    obj.SetAttribute("lockoutTime",
                        new DirectoryAttribute("lockoutTime", state.LockoutTime.Value.ToFileTime().ToString()));
                }
            }

            obj.WhenChanged = DateTimeOffset.UtcNow;
            await _store.UpdateAsync(obj);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist lockout attributes for {DN}", dn);
        }
    }

    private async Task ClearLockoutAttributesAsync(string dn)
    {
        try
        {
            var obj = await _store.GetByDnAsync("default", dn);
            if (obj is null) return;

            obj.SetAttribute("badPwdCount", new DirectoryAttribute("badPwdCount", "0"));
            obj.SetAttribute("lockoutTime", new DirectoryAttribute("lockoutTime", "0"));
            obj.Attributes.Remove("badPasswordTime");

            obj.WhenChanged = DateTimeOffset.UtcNow;
            await _store.UpdateAsync(obj);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear lockout attributes for {DN}", dn);
        }
    }

    private class LockoutState
    {
        public int FailedAttemptCount { get; set; }
        public DateTimeOffset? LockoutTime { get; set; }
        public DateTimeOffset? LastFailedAttempt { get; set; }
    }
}

/// <summary>
/// Configuration for account lockout behavior.
/// </summary>
public class AccountLockoutPolicy
{
    /// <summary>
    /// Number of failed attempts before the account is locked. 0 = never lock.
    /// </summary>
    public int LockoutThreshold { get; set; } = 5;

    /// <summary>
    /// How long the account remains locked. TimeSpan.Zero = until admin unlocks.
    /// </summary>
    public TimeSpan LockoutDuration { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Time window in which failed attempts are counted.
    /// </summary>
    public TimeSpan LockoutObservationWindow { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Whether lockout enforcement is enabled.
    /// </summary>
    public bool LockoutEnabled { get; set; } = true;
}

/// <summary>
/// Read-only view of an account's lockout status.
/// </summary>
public class LockoutInfo
{
    public string DistinguishedName { get; set; } = string.Empty;
    public int FailedAttemptCount { get; set; }
    public DateTimeOffset? LockoutTime { get; set; }
    public DateTimeOffset? LastFailedAttempt { get; set; }
    public bool IsLockedOut { get; set; }
}
