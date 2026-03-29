using System.Security.Cryptography;
using System.Text;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Ldap.Protocol;
using Microsoft.Extensions.Logging;

namespace Directory.Ldap.Handlers;

/// <summary>
/// Handles Active Directory password change semantics per MS-ADTS section 3.1.1.3.1.5.
/// Password changes in AD use the unicodePwd attribute (BER-encoded in double quotes).
///
/// Password Change (user changing own password):
///   Modify request with delete of old unicodePwd + add of new unicodePwd
///   Both values are UTF-16LE encoded in double quotes: "password"
///
/// Password Reset (admin resetting):
///   Modify request with replace of unicodePwd
///   Value is UTF-16LE encoded in double quotes
///
/// Requires SSL/TLS or SASL encryption per MS-ADTS 3.1.1.3.1.5.
/// Returns LDAP error 13 (confidentialityRequired) if connection is not encrypted.
/// </summary>
public class PasswordModifyHandler
{
    private readonly IPasswordPolicy _passwordPolicy;
    private readonly IDirectoryStore _store;
    private readonly ILogger<PasswordModifyHandler> _logger;

    /// <summary>
    /// LDAP result code returned when the connection is not encrypted.
    /// Callers that go through the LDAP modify path should map this to the wire result code.
    /// </summary>
    public const int ConfidentialityRequiredCode = 13;

    public PasswordModifyHandler(
        IPasswordPolicy passwordPolicy,
        IDirectoryStore store,
        ILogger<PasswordModifyHandler> logger)
    {
        _passwordPolicy = passwordPolicy;
        _store = store;
        _logger = logger;
    }

    public async Task<PasswordModifyResult> HandleAsync(
        string tenantId, string targetDn, List<PasswordModifyOperation> operations,
        string callerDn, bool isEncrypted, CancellationToken ct)
    {
        // MS-ADTS 3.1.1.3.1.5: password operations require encrypted transport
        if (!isEncrypted)
        {
            return PasswordModifyResult.ErrorWithCode(
                "Password modifications require an encrypted connection (TLS/SSL or SASL)",
                LdapResultCode.ConfidentialityRequired);
        }

        if (operations.Count == 0)
            return PasswordModifyResult.Error("No password operations specified");

        // Determine if this is a change (old+new) or reset (replace)
        var hasDelete = operations.Any(o => o.Operation == PasswordModifyOperationType.Delete);
        var hasAdd = operations.Any(o => o.Operation == PasswordModifyOperationType.Add);
        var hasReplace = operations.Any(o => o.Operation == PasswordModifyOperationType.Replace);

        if (hasReplace)
        {
            // Password reset by admin
            return await HandlePasswordResetAsync(tenantId, targetDn, operations.First(o => o.Operation == PasswordModifyOperationType.Replace), ct);
        }

        if (hasDelete && hasAdd)
        {
            // Password change by user
            var oldPwdOp = operations.First(o => o.Operation == PasswordModifyOperationType.Delete);
            var newPwdOp = operations.First(o => o.Operation == PasswordModifyOperationType.Add);
            return await HandlePasswordChangeAsync(tenantId, targetDn, oldPwdOp, newPwdOp, callerDn, ct);
        }

        return PasswordModifyResult.Error("Invalid unicodePwd modification: must be delete+add (change) or replace (reset)");
    }

    private async Task<PasswordModifyResult> HandlePasswordResetAsync(
        string tenantId, string targetDn, PasswordModifyOperation op, CancellationToken ct)
    {
        var password = DecodeUnicodePassword(op.Value);
        if (password == null)
            return PasswordModifyResult.Error("Invalid unicodePwd encoding");

        var user = await _store.GetByDnAsync(tenantId, targetDn, ct);
        if (user is null)
            return PasswordModifyResult.Error($"User not found: {targetDn}");

        if (!_passwordPolicy.MeetsComplexityRequirements(password, user.SAMAccountName))
            return PasswordModifyResult.Error("Password does not meet complexity requirements");

        // Check password history
        var historyError = CheckPasswordHistory(user, password);
        if (historyError is not null)
            return historyError;

        await _passwordPolicy.SetPasswordAsync(tenantId, targetDn, password, ct);
        _logger.LogInformation("Password reset for {DN}", targetDn);
        return PasswordModifyResult.Success();
    }

    private async Task<PasswordModifyResult> HandlePasswordChangeAsync(
        string tenantId, string targetDn, PasswordModifyOperation oldPwdOp,
        PasswordModifyOperation newPwdOp, string callerDn, CancellationToken ct)
    {
        var oldPassword = DecodeUnicodePassword(oldPwdOp.Value);
        var newPassword = DecodeUnicodePassword(newPwdOp.Value);

        if (oldPassword == null || newPassword == null)
            return PasswordModifyResult.Error("Invalid unicodePwd encoding");

        // Verify old password
        var isValid = await _passwordPolicy.ValidatePasswordAsync(tenantId, targetDn, oldPassword, ct);
        if (!isValid)
            return PasswordModifyResult.Error("Current password is incorrect");

        var user = await _store.GetByDnAsync(tenantId, targetDn, ct);
        if (user is null)
            return PasswordModifyResult.Error($"User not found: {targetDn}");

        // Check new password meets complexity
        if (!_passwordPolicy.MeetsComplexityRequirements(newPassword, user.SAMAccountName))
            return PasswordModifyResult.Error("New password does not meet complexity requirements");

        // Check minimum password age (pwdLastSet + minPwdAge vs current time)
        var minAgeError = await CheckMinimumPasswordAgeAsync(tenantId, user, ct);
        if (minAgeError is not null)
            return minAgeError;

        // Check password history
        var historyError = CheckPasswordHistory(user, newPassword);
        if (historyError is not null)
            return historyError;

        await _passwordPolicy.SetPasswordAsync(tenantId, targetDn, newPassword, ct);
        _logger.LogInformation("Password changed for {DN}", targetDn);
        return PasswordModifyResult.Success();
    }

    /// <summary>
    /// Check if the new password matches any entry in the user's password history.
    /// The passwordHistory attribute stores SHA-256 hashes of previous passwords as hex strings.
    /// </summary>
    private PasswordModifyResult CheckPasswordHistory(DirectoryObject user, string newPassword)
    {
        var historyAttr = user.GetAttribute("passwordHistory");
        if (historyAttr is null || historyAttr.Values.Count == 0)
            return null;

        var newHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(newPassword)));

        foreach (var historyEntry in historyAttr.GetStrings())
        {
            if (string.Equals(newHash, historyEntry, StringComparison.OrdinalIgnoreCase))
            {
                return PasswordModifyResult.Error("Password was recently used and cannot be reused (password history policy)");
            }
        }

        return null;
    }

    /// <summary>
    /// Check whether the minimum password age has elapsed since the last password change.
    /// Reads minPwdAge from the domain object (stored as a negative FILETIME interval in 100ns ticks).
    /// </summary>
    private async Task<PasswordModifyResult> CheckMinimumPasswordAgeAsync(
        string tenantId, DirectoryObject user, CancellationToken ct)
    {
        if (user.PwdLastSet == 0)
            return null; // Password has never been set or was flagged for "must change at next logon"

        // Find domain object to get minPwdAge
        var dn = DistinguishedName.Parse(user.DistinguishedName);
        var domainDn = dn.GetDomainDn();
        var domainObj = await _store.GetByDnAsync(tenantId, domainDn, ct);

        if (domainObj is null)
            return null;

        var minPwdAgeAttr = domainObj.GetAttribute("minPwdAge");
        if (minPwdAgeAttr is null)
            return null;

        var minPwdAgeStr = minPwdAgeAttr.GetFirstString();
        if (minPwdAgeStr is null || !long.TryParse(minPwdAgeStr, out var minPwdAgeTicks))
            return null;

        // minPwdAge is stored as a negative value in 100-nanosecond intervals
        // A value of 0 means no minimum age
        if (minPwdAgeTicks == 0)
            return null;

        // Convert to positive ticks for comparison
        var minAgeTicks = Math.Abs(minPwdAgeTicks);
        var pwdLastSetTime = DateTimeOffset.FromFileTime(user.PwdLastSet);
        var minAgeSpan = TimeSpan.FromTicks(minAgeTicks);
        var earliestChangeTime = pwdLastSetTime + minAgeSpan;

        if (DateTimeOffset.UtcNow < earliestChangeTime)
        {
            return PasswordModifyResult.Error(
                $"Minimum password age has not elapsed. Password cannot be changed until {earliestChangeTime:u}");
        }

        return null;
    }

    /// <summary>
    /// AD encodes unicodePwd values as UTF-16LE with surrounding double quotes.
    /// The BER value contains the raw bytes of "password" in UTF-16LE.
    /// </summary>
    public static string DecodeUnicodePassword(byte[] value)
    {
        if (value == null || value.Length < 4) return null;

        var str = System.Text.Encoding.Unicode.GetString(value);

        // Strip surrounding double quotes
        if (str.StartsWith('"') && str.EndsWith('"'))
            return str[1..^1];

        return str;
    }
}

public class PasswordModifyOperation
{
    public PasswordModifyOperationType Operation { get; init; }
    public byte[] Value { get; init; }
}

public enum PasswordModifyOperationType
{
    Add = 0,
    Delete = 1,
    Replace = 2
}

public class PasswordModifyResult
{
    public bool IsSuccess { get; init; }
    public string ErrorMessage { get; init; }

    /// <summary>
    /// Optional specific LDAP result code for the error. If null, callers should use
    /// ConstraintViolation (19) as the default for password policy failures.
    /// </summary>
    public LdapResultCode? ResultCode { get; init; }

    public static PasswordModifyResult Success() => new() { IsSuccess = true };
    public static PasswordModifyResult Error(string message) => new() { IsSuccess = false, ErrorMessage = message };
    public static PasswordModifyResult ErrorWithCode(string message, LdapResultCode code) =>
        new() { IsSuccess = false, ErrorMessage = message, ResultCode = code };
}
