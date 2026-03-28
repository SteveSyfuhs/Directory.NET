using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Security.Mfa;
using Microsoft.Extensions.Logging;

namespace Directory.Web.Services;

#region Models

public class SsprSettings
{
    public bool Enabled { get; set; } = true;
    public bool RequireMfa { get; set; } = true;
    public bool RequireSecurityQuestions { get; set; }
    public int MinSecurityQuestions { get; set; } = 3;
    public int ResetTokenExpiryMinutes { get; set; } = 15;
    public int MaxResetAttemptsPerHour { get; set; } = 5;
    public List<string> SecurityQuestionOptions { get; set; } = new()
    {
        "What is your mother's maiden name?",
        "What was the name of your first pet?",
        "What city were you born in?",
        "What was the name of your first school?",
        "What is your favorite movie?",
        "What street did you grow up on?",
        "What was your childhood nickname?",
        "What is the name of your favorite teacher?"
    };
}

public class SsprRegistration
{
    public string UserDn { get; set; } = "";
    public List<SecurityQuestionAnswer> SecurityAnswers { get; set; } = new();
    public string RecoveryEmail { get; set; }
    public string RecoveryPhone { get; set; }
    public bool MfaRegistered { get; set; }
    public DateTimeOffset RegisteredAt { get; set; }
}

public class SecurityQuestionAnswer
{
    public string Question { get; set; } = "";
    public string AnswerHash { get; set; } = ""; // Store hashed, not plaintext
}

public class PasswordResetToken
{
    public string Token { get; set; } = "";
    public string UserDn { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public bool Used { get; set; }
    public bool SecurityQuestionsVerified { get; set; }
    public bool MfaVerified { get; set; }
}

#endregion

/// <summary>
/// Self-Service Password Reset (SSPR) service.
/// Stores registration data as a custom attribute (msDS-SsprRegistration) on the user DirectoryObject.
/// Manages password reset tokens in memory with expiry and rate limiting.
/// </summary>
public class SelfServicePasswordService
{
    private const string SsprAttributeName = "msDS-SsprRegistration";
    private const string SettingsAttributeName = "msDS-SsprSettings";

    private readonly IDirectoryStore _store;
    private readonly INamingContextService _ncService;
    private readonly IPasswordPolicy _passwordService;
    private readonly MfaService _mfaService;
    private readonly ILogger<SelfServicePasswordService> _logger;

    private SsprSettings _settings = new();
    private readonly ConcurrentDictionary<string, PasswordResetToken> _tokens = new();
    private readonly ConcurrentDictionary<string, List<DateTimeOffset>> _rateLimits = new();

    public SelfServicePasswordService(
        IDirectoryStore store,
        INamingContextService ncService,
        IPasswordPolicy passwordService,
        MfaService mfaService,
        ILogger<SelfServicePasswordService> logger)
    {
        _store = store;
        _ncService = ncService;
        _passwordService = passwordService;
        _mfaService = mfaService;
        _logger = logger;
    }

    #region Settings

    public SsprSettings GetSsprSettings() => _settings;

    public SsprSettings UpdateSsprSettings(SsprSettings settings)
    {
        _settings = settings;
        _logger.LogInformation("SSPR settings updated. Enabled={Enabled}, RequireMfa={RequireMfa}, RequireSecurityQuestions={RequireSecurityQuestions}",
            settings.Enabled, settings.RequireMfa, settings.RequireSecurityQuestions);
        return _settings;
    }

    #endregion

    #region Registration

    /// <summary>
    /// Register a user for SSPR by storing their security question answers and recovery info.
    /// </summary>
    public async Task<SsprRegistration> RegisterForSspr(
        string userDn,
        List<SecurityQuestionAnswerInput> answers,
        string recoveryEmail,
        string recoveryPhone,
        CancellationToken ct = default)
    {
        var obj = await ResolveUserAsync(userDn, ct)
            ?? throw new InvalidOperationException($"User not found: {userDn}");

        if (_settings.RequireSecurityQuestions && answers.Count < _settings.MinSecurityQuestions)
            throw new InvalidOperationException($"At least {_settings.MinSecurityQuestions} security questions are required.");

        // Hash the answers
        var hashedAnswers = answers.Select(a => new SecurityQuestionAnswer
        {
            Question = a.Question,
            AnswerHash = HashAnswer(a.Answer),
        }).ToList();

        // Check MFA status
        var mfaStatus = await _mfaService.GetMfaStatus(obj.DistinguishedName, ct);

        var registration = new SsprRegistration
        {
            UserDn = obj.DistinguishedName,
            SecurityAnswers = hashedAnswers,
            RecoveryEmail = recoveryEmail,
            RecoveryPhone = recoveryPhone,
            MfaRegistered = mfaStatus.IsEnabled,
            RegisteredAt = DateTimeOffset.UtcNow,
        };

        await SaveRegistrationAsync(obj, registration, ct);

        _logger.LogInformation("SSPR registration completed for {DN}", obj.DistinguishedName);
        return registration;
    }

    /// <summary>
    /// Check if a user is registered for SSPR.
    /// </summary>
    public async Task<SsprRegistrationStatus> GetRegistrationStatus(string username, CancellationToken ct = default)
    {
        var obj = await ResolveUserAsync(username, ct);
        if (obj is null)
            return new SsprRegistrationStatus { IsRegistered = false };

        var registration = GetRegistration(obj);
        if (registration is null)
            return new SsprRegistrationStatus { IsRegistered = false, UserDn = obj.DistinguishedName };

        return new SsprRegistrationStatus
        {
            IsRegistered = true,
            UserDn = obj.DistinguishedName,
            HasSecurityQuestions = registration.SecurityAnswers.Count > 0,
            HasRecoveryEmail = !string.IsNullOrEmpty(registration.RecoveryEmail),
            HasRecoveryPhone = !string.IsNullOrEmpty(registration.RecoveryPhone),
            HasMfa = registration.MfaRegistered,
            RegisteredAt = registration.RegisteredAt,
        };
    }

    /// <summary>
    /// List all users that have registered for SSPR.
    /// </summary>
    public async Task<List<SsprRegistrationSummary>> GetRegistrations(CancellationToken ct = default)
    {
        var tenantId = "default";
        var domainDn = _ncService.GetDomainNc().Dn;

        var results = new List<SsprRegistrationSummary>();

        // Search for users with SSPR registration attribute
        var searchResult = await _store.SearchAsync(
            tenantId, domainDn,
            SearchScope.WholeSubtree,
            new PresenceFilterNode(SsprAttributeName),
            ["distinguishedName", "sAMAccountName", "userPrincipalName", SsprAttributeName],
            ct: ct);

        foreach (var user in searchResult.Entries)
        {
            var registration = GetRegistration(user);
            if (registration is null) continue;

            results.Add(new SsprRegistrationSummary
            {
                UserDn = user.DistinguishedName,
                SamAccountName = user.SAMAccountName ?? "",
                UserPrincipalName = user.UserPrincipalName ?? "",
                HasSecurityQuestions = registration.SecurityAnswers.Count > 0,
                HasMfa = registration.MfaRegistered,
                RegisteredAt = registration.RegisteredAt,
            });
        }

        return results;
    }

    #endregion

    #region Password Reset Flow

    /// <summary>
    /// Step 1: Initiate a password reset. Returns a token and the verification methods required.
    /// </summary>
    public async Task<SsprInitiateResult> InitiateReset(string username, CancellationToken ct = default)
    {
        if (!_settings.Enabled)
            throw new InvalidOperationException("Self-service password reset is not enabled.");

        var obj = await ResolveUserAsync(username, ct)
            ?? throw new InvalidOperationException("User not found or not registered for SSPR.");

        var registration = GetRegistration(obj)
            ?? throw new InvalidOperationException("User not found or not registered for SSPR.");

        // Rate limit check
        if (IsRateLimited(obj.DistinguishedName))
            throw new InvalidOperationException("Too many reset attempts. Please try again later.");

        RecordAttempt(obj.DistinguishedName);

        // Generate token
        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var tokenHex = Convert.ToHexString(tokenBytes).ToLowerInvariant();

        var token = new PasswordResetToken
        {
            Token = tokenHex,
            UserDn = obj.DistinguishedName,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(_settings.ResetTokenExpiryMinutes),
            Used = false,
            SecurityQuestionsVerified = !_settings.RequireSecurityQuestions,
            MfaVerified = !_settings.RequireMfa || !registration.MfaRegistered,
        };

        _tokens[tokenHex] = token;

        // Build the list of security questions (just the question text, not answers)
        var questions = registration.SecurityAnswers.Select(a => a.Question).ToList();

        _logger.LogInformation("SSPR reset initiated for {DN}", obj.DistinguishedName);

        return new SsprInitiateResult
        {
            Token = tokenHex,
            RequireSecurityQuestions = _settings.RequireSecurityQuestions && registration.SecurityAnswers.Count > 0,
            RequireMfa = _settings.RequireMfa && registration.MfaRegistered,
            SecurityQuestions = _settings.RequireSecurityQuestions ? questions : [],
            ExpiresAt = token.ExpiresAt,
        };
    }

    /// <summary>
    /// Step 2: Validate security question answers.
    /// </summary>
    public async Task<SsprVerifyResult> ValidateSecurityAnswers(string tokenValue, List<SecurityQuestionAnswerInput> answers, CancellationToken ct = default)
    {
        var token = GetValidToken(tokenValue);

        var obj = await _store.GetByDnAsync("default", token.UserDn, ct)
            ?? throw new InvalidOperationException("User not found.");

        var registration = GetRegistration(obj)
            ?? throw new InvalidOperationException("SSPR registration not found.");

        // Verify each answer
        var allCorrect = true;
        foreach (var answer in answers)
        {
            var stored = registration.SecurityAnswers.FirstOrDefault(
                a => a.Question.Equals(answer.Question, StringComparison.OrdinalIgnoreCase));

            if (stored is null || stored.AnswerHash != HashAnswer(answer.Answer))
            {
                allCorrect = false;
                break;
            }
        }

        if (!allCorrect)
        {
            _logger.LogWarning("SSPR security question verification failed for {DN}", token.UserDn);
            return new SsprVerifyResult { Success = false, Message = "One or more answers are incorrect." };
        }

        token.SecurityQuestionsVerified = true;

        _logger.LogInformation("SSPR security questions verified for {DN}", token.UserDn);
        return new SsprVerifyResult
        {
            Success = true,
            Message = "Security questions verified.",
            RequireMfa = _settings.RequireMfa && registration.MfaRegistered && !token.MfaVerified,
        };
    }

    /// <summary>
    /// Step 3: Validate MFA (TOTP) code.
    /// </summary>
    public async Task<SsprVerifyResult> ValidateMfaCode(string tokenValue, string code, CancellationToken ct = default)
    {
        var token = GetValidToken(tokenValue);

        if (!token.SecurityQuestionsVerified && _settings.RequireSecurityQuestions)
            throw new InvalidOperationException("Security questions must be verified first.");

        var result = await _mfaService.ValidateCode(token.UserDn, code, ct);

        if (!result.IsValid)
        {
            _logger.LogWarning("SSPR MFA verification failed for {DN}", token.UserDn);
            return new SsprVerifyResult { Success = false, Message = "Invalid MFA code." };
        }

        token.MfaVerified = true;

        _logger.LogInformation("SSPR MFA verified for {DN}", token.UserDn);
        return new SsprVerifyResult { Success = true, Message = "MFA code verified." };
    }

    /// <summary>
    /// Step 4: Complete the password reset after all verifications pass.
    /// </summary>
    public async Task<SsprResetResult> CompleteReset(string tokenValue, string newPassword, CancellationToken ct = default)
    {
        var token = GetValidToken(tokenValue);

        if (!token.SecurityQuestionsVerified)
            throw new InvalidOperationException("Security questions have not been verified.");

        if (!token.MfaVerified)
            throw new InvalidOperationException("MFA has not been verified.");

        // Validate password complexity
        var obj = await _store.GetByDnAsync("default", token.UserDn, ct)
            ?? throw new InvalidOperationException("User not found.");

        if (!_passwordService.MeetsComplexityRequirements(newPassword, obj.SAMAccountName))
            throw new InvalidOperationException("Password does not meet complexity requirements. Use at least 7 characters with 3 of: uppercase, lowercase, digit, special character.");

        // Set the new password
        await _passwordService.SetPasswordAsync("default", token.UserDn, newPassword, ct);

        // Mark token as used
        token.Used = true;

        _logger.LogInformation("SSPR password reset completed for {DN}", token.UserDn);
        return new SsprResetResult { Success = true, Message = "Password has been reset successfully." };
    }

    #endregion

    #region Helpers

    private PasswordResetToken GetValidToken(string tokenValue)
    {
        if (!_tokens.TryGetValue(tokenValue, out var token))
            throw new InvalidOperationException("Invalid or expired reset token.");

        if (token.Used)
            throw new InvalidOperationException("This reset token has already been used.");

        if (token.ExpiresAt < DateTimeOffset.UtcNow)
        {
            _tokens.TryRemove(tokenValue, out _);
            throw new InvalidOperationException("Reset token has expired.");
        }

        return token;
    }

    /// <summary>
    /// Remove all password reset tokens that expired before the given cutoff.
    /// Called by the data retention service.
    /// </summary>
    public int PurgeExpiredTokens(DateTimeOffset cutoff)
    {
        var toRemove = _tokens
            .Where(kvp => kvp.Value.ExpiresAt < cutoff || kvp.Value.Used)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in toRemove)
            _tokens.TryRemove(key, out _);

        return toRemove.Count;
    }

    private bool IsRateLimited(string userDn)
    {
        if (!_rateLimits.TryGetValue(userDn, out var attempts))
            return false;

        var cutoff = DateTimeOffset.UtcNow.AddHours(-1);
        var recentAttempts = attempts.Count(a => a > cutoff);
        return recentAttempts >= _settings.MaxResetAttemptsPerHour;
    }

    private void RecordAttempt(string userDn)
    {
        var attempts = _rateLimits.GetOrAdd(userDn, _ => new List<DateTimeOffset>());

        lock (attempts)
        {
            // Clean up old entries
            var cutoff = DateTimeOffset.UtcNow.AddHours(-1);
            attempts.RemoveAll(a => a <= cutoff);
            attempts.Add(DateTimeOffset.UtcNow);
        }
    }

    private static string HashAnswer(string answer)
    {
        var normalized = answer.Trim().ToLowerInvariant();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private async Task<DirectoryObject> ResolveUserAsync(string identifier, CancellationToken ct)
    {
        // Try by DN first
        var obj = await _store.GetByDnAsync("default", identifier, ct);

        // Try by UPN
        if (obj is null)
            obj = await _store.GetByUpnAsync("default", identifier, ct);

        // Try by sAMAccountName
        if (obj is null)
        {
            var domainDn = _ncService.GetDomainNc().Dn;
            obj = await _store.GetBySamAccountNameAsync("default", domainDn, identifier, ct);
        }

        return obj;
    }

    private static SsprRegistration GetRegistration(DirectoryObject obj)
    {
        if (!obj.Attributes.TryGetValue(SsprAttributeName, out var attr))
            return null;

        var json = attr.Values.FirstOrDefault()?.ToString();
        if (string.IsNullOrEmpty(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<SsprRegistration>(json);
        }
        catch
        {
            return null;
        }
    }

    private async Task SaveRegistrationAsync(DirectoryObject obj, SsprRegistration registration, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(registration);

        obj.Attributes[SsprAttributeName] = new DirectoryAttribute
        {
            Name = SsprAttributeName,
            Values = [json],
        };

        obj.WhenChanged = DateTimeOffset.UtcNow;
        await _store.UpdateAsync(obj, ct);
    }

    #endregion
}

#region DTOs

public class SecurityQuestionAnswerInput
{
    public string Question { get; set; } = "";
    public string Answer { get; set; } = "";
}

public class SsprRegistrationStatus
{
    public bool IsRegistered { get; set; }
    public string UserDn { get; set; }
    public bool HasSecurityQuestions { get; set; }
    public bool HasRecoveryEmail { get; set; }
    public bool HasRecoveryPhone { get; set; }
    public bool HasMfa { get; set; }
    public DateTimeOffset? RegisteredAt { get; set; }
}

public class SsprRegistrationSummary
{
    public string UserDn { get; set; } = "";
    public string SamAccountName { get; set; } = "";
    public string UserPrincipalName { get; set; } = "";
    public bool HasSecurityQuestions { get; set; }
    public bool HasMfa { get; set; }
    public DateTimeOffset RegisteredAt { get; set; }
}

public class SsprInitiateResult
{
    public string Token { get; set; } = "";
    public bool RequireSecurityQuestions { get; set; }
    public bool RequireMfa { get; set; }
    public List<string> SecurityQuestions { get; set; } = new();
    public DateTimeOffset ExpiresAt { get; set; }
}

public class SsprVerifyResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public bool RequireMfa { get; set; }
}

public class SsprResetResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
}

#endregion
