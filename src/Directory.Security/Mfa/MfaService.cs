using System.Security.Cryptography;
using System.Text.Json;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Microsoft.Extensions.Logging;

namespace Directory.Security.Mfa;

/// <summary>
/// Manages MFA enrollment, validation, and lifecycle for directory users.
/// Stores MFA data as a custom attribute (msDS-MfaEnrollment) on the user DirectoryObject.
/// </summary>
public class MfaService
{
    private const string MfaAttributeName = "msDS-MfaEnrollment";
    private const int RecoveryCodeCount = 8;
    private const int RecoveryCodeLength = 8;

    private readonly IDirectoryStore _store;
    private readonly INamingContextService _ncService;
    private readonly TotpService _totpService;
    private readonly ILogger<MfaService> _logger;

    public MfaService(
        IDirectoryStore store,
        INamingContextService ncService,
        TotpService totpService,
        ILogger<MfaService> logger)
    {
        _store = store;
        _ncService = ncService;
        _totpService = totpService;
        _logger = logger;
    }

    /// <summary>
    /// Begins MFA enrollment by generating a TOTP secret and provisioning URI.
    /// The secret is stored but MFA is not yet enabled until verification completes.
    /// </summary>
    public async Task<MfaEnrollmentResult> BeginEnrollment(string dn, CancellationToken ct = default)
    {
        var obj = await ResolveUserAsync(dn, ct);
        if (obj is null)
            throw new InvalidOperationException($"User not found: {dn}");

        var secret = _totpService.GenerateSecret();
        var accountName = obj.UserPrincipalName ?? obj.SAMAccountName ?? obj.Cn ?? dn;

        // Store pending enrollment (not yet enabled)
        var enrollment = new MfaEnrollment
        {
            DistinguishedName = obj.DistinguishedName,
            Secret = secret,
            IsEnabled = false,
            EnrolledAt = default,
            RecoveryCodes = [],
        };

        await SaveEnrollmentAsync(obj, enrollment, ct);

        var provisioningUri = _totpService.GenerateProvisioningUri(secret, accountName);

        _logger.LogInformation("MFA enrollment started for {DN}", obj.DistinguishedName);

        return new MfaEnrollmentResult
        {
            Secret = secret,
            ProvisioningUri = provisioningUri,
            AccountName = accountName,
        };
    }

    /// <summary>
    /// Completes MFA enrollment by verifying the user can generate a valid TOTP code.
    /// Generates recovery codes and enables MFA.
    /// </summary>
    public async Task<MfaEnrollmentCompleteResult> CompleteEnrollment(string dn, string verificationCode, CancellationToken ct = default)
    {
        var obj = await ResolveUserAsync(dn, ct);
        if (obj is null)
            throw new InvalidOperationException($"User not found: {dn}");

        var enrollment = GetEnrollment(obj);
        if (enrollment is null || string.IsNullOrEmpty(enrollment.Secret))
            throw new InvalidOperationException("No pending MFA enrollment found. Call BeginEnrollment first.");

        if (enrollment.IsEnabled)
            throw new InvalidOperationException("MFA is already enabled for this user.");

        // Verify the code
        if (!_totpService.ValidateCode(enrollment.Secret, verificationCode))
        {
            return new MfaEnrollmentCompleteResult { Success = false, RecoveryCodes = [] };
        }

        // Generate recovery codes
        var recoveryCodes = GenerateRecoveryCodes();

        enrollment.IsEnabled = true;
        enrollment.EnrolledAt = DateTimeOffset.UtcNow;
        enrollment.RecoveryCodes = recoveryCodes;

        await SaveEnrollmentAsync(obj, enrollment, ct);

        _logger.LogInformation("MFA enrollment completed for {DN}", obj.DistinguishedName);

        return new MfaEnrollmentCompleteResult
        {
            Success = true,
            RecoveryCodes = recoveryCodes,
        };
    }

    /// <summary>
    /// Validates a TOTP code or recovery code for the given user.
    /// </summary>
    public async Task<MfaValidationResult> ValidateCode(string dn, string code, CancellationToken ct = default)
    {
        var obj = await ResolveUserAsync(dn, ct);
        if (obj is null)
            throw new InvalidOperationException($"User not found: {dn}");

        var enrollment = GetEnrollment(obj);
        if (enrollment is null || !enrollment.IsEnabled)
        {
            return new MfaValidationResult { IsValid = false, UsedRecoveryCode = false };
        }

        // Try TOTP first
        if (_totpService.ValidateCode(enrollment.Secret, code))
        {
            _logger.LogDebug("TOTP code validated for {DN}", obj.DistinguishedName);
            return new MfaValidationResult { IsValid = true, UsedRecoveryCode = false };
        }

        // Try recovery codes
        var normalizedCode = code.Replace("-", "").Trim().ToUpperInvariant();
        var matchIndex = enrollment.RecoveryCodes.FindIndex(
            rc => rc.Replace("-", "").ToUpperInvariant() == normalizedCode);

        if (matchIndex >= 0)
        {
            // Remove used recovery code
            enrollment.RecoveryCodes.RemoveAt(matchIndex);
            await SaveEnrollmentAsync(obj, enrollment, ct);

            _logger.LogInformation("Recovery code used for {DN}. {Remaining} codes remaining.",
                obj.DistinguishedName, enrollment.RecoveryCodes.Count);

            return new MfaValidationResult { IsValid = true, UsedRecoveryCode = true };
        }

        _logger.LogDebug("MFA validation failed for {DN}", obj.DistinguishedName);
        return new MfaValidationResult { IsValid = false, UsedRecoveryCode = false };
    }

    /// <summary>
    /// Disables MFA for the given user and removes enrollment data.
    /// </summary>
    public async Task DisableMfa(string dn, CancellationToken ct = default)
    {
        var obj = await ResolveUserAsync(dn, ct);
        if (obj is null)
            throw new InvalidOperationException($"User not found: {dn}");

        if (obj.Attributes.ContainsKey(MfaAttributeName))
        {
            obj.Attributes.Remove(MfaAttributeName);
            obj.WhenChanged = DateTimeOffset.UtcNow;
            await _store.UpdateAsync(obj, ct);
        }

        _logger.LogInformation("MFA disabled for {DN}", obj.DistinguishedName);
    }

    /// <summary>
    /// Regenerates recovery codes for the given user.
    /// </summary>
    public async Task<MfaRecoveryCodesResult> RegenerateRecoveryCodes(string dn, CancellationToken ct = default)
    {
        var obj = await ResolveUserAsync(dn, ct);
        if (obj is null)
            throw new InvalidOperationException($"User not found: {dn}");

        var enrollment = GetEnrollment(obj);
        if (enrollment is null || !enrollment.IsEnabled)
            throw new InvalidOperationException("MFA is not enabled for this user.");

        var recoveryCodes = GenerateRecoveryCodes();
        enrollment.RecoveryCodes = recoveryCodes;

        await SaveEnrollmentAsync(obj, enrollment, ct);

        _logger.LogInformation("Recovery codes regenerated for {DN}", obj.DistinguishedName);

        return new MfaRecoveryCodesResult { RecoveryCodes = recoveryCodes };
    }

    /// <summary>
    /// Gets the MFA status for the given user.
    /// </summary>
    public async Task<MfaStatus> GetMfaStatus(string dn, CancellationToken ct = default)
    {
        var obj = await ResolveUserAsync(dn, ct);
        if (obj is null)
            throw new InvalidOperationException($"User not found: {dn}");

        var enrollment = GetEnrollment(obj);

        return new MfaStatus
        {
            IsEnrolled = enrollment is not null,
            IsEnabled = enrollment?.IsEnabled ?? false,
            EnrolledAt = enrollment?.IsEnabled == true ? enrollment.EnrolledAt : null,
            RecoveryCodesRemaining = enrollment?.RecoveryCodes.Count ?? 0,
        };
    }

    private async Task<DirectoryObject> ResolveUserAsync(string dn, CancellationToken ct)
    {
        // Try by DN first
        var obj = await _store.GetByDnAsync("default", dn, ct);

        // Try by UPN
        if (obj is null)
            obj = await _store.GetByUpnAsync("default", dn, ct);

        // Try by sAMAccountName
        if (obj is null)
        {
            var domainDn = _ncService.GetDomainNc().Dn;
            obj = await _store.GetBySamAccountNameAsync("default", domainDn, dn, ct);
        }

        return obj;
    }

    private static MfaEnrollment GetEnrollment(DirectoryObject obj)
    {
        if (!obj.Attributes.TryGetValue(MfaAttributeName, out var attr))
            return null;

        var json = attr.Values.FirstOrDefault()?.ToString();
        if (string.IsNullOrEmpty(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<MfaEnrollment>(json);
        }
        catch
        {
            return null;
        }
    }

    private async Task SaveEnrollmentAsync(DirectoryObject obj, MfaEnrollment enrollment, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(enrollment);

        obj.Attributes[MfaAttributeName] = new DirectoryAttribute
        {
            Name = MfaAttributeName,
            Values = [json],
        };

        obj.WhenChanged = DateTimeOffset.UtcNow;
        await _store.UpdateAsync(obj, ct);
    }

    private static List<string> GenerateRecoveryCodes()
    {
        var codes = new List<string>(RecoveryCodeCount);
        for (var i = 0; i < RecoveryCodeCount; i++)
        {
            var bytes = RandomNumberGenerator.GetBytes(RecoveryCodeLength / 2 + 1);
            var hex = Convert.ToHexString(bytes)[..RecoveryCodeLength].ToUpperInvariant();
            // Format as XXXX-XXXX for readability
            codes.Add($"{hex[..4]}-{hex[4..]}");
        }
        return codes;
    }
}
