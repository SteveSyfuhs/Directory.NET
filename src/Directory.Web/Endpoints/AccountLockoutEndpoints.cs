using Directory.Security;

namespace Directory.Web.Endpoints;

public static class AccountLockoutEndpoints
{
    public static RouteGroupBuilder MapAccountLockoutEndpoints(this RouteGroupBuilder group)
    {
        // GET /api/v1/lockout/policy — get current lockout policy
        group.MapGet("/policy", (AccountLockoutService lockoutService) =>
        {
            var policy = lockoutService.Policy;
            return Results.Ok(new
            {
                policy.LockoutEnabled,
                policy.LockoutThreshold,
                LockoutDurationMinutes = policy.LockoutDuration.TotalMinutes,
                LockoutObservationWindowMinutes = policy.LockoutObservationWindow.TotalMinutes,
            });
        })
        .WithName("GetLockoutPolicy")
        .WithTags("AccountLockout");

        // PUT /api/v1/lockout/policy — update lockout policy
        group.MapPut("/policy", (AccountLockoutService lockoutService, UpdateLockoutPolicyRequest request) =>
        {
            var policy = lockoutService.Policy;

            if (request.LockoutEnabled.HasValue)
                policy.LockoutEnabled = request.LockoutEnabled.Value;
            if (request.LockoutThreshold.HasValue)
                policy.LockoutThreshold = request.LockoutThreshold.Value;
            if (request.LockoutDurationMinutes.HasValue)
                policy.LockoutDuration = TimeSpan.FromMinutes(request.LockoutDurationMinutes.Value);
            if (request.LockoutObservationWindowMinutes.HasValue)
                policy.LockoutObservationWindow = TimeSpan.FromMinutes(request.LockoutObservationWindowMinutes.Value);

            return Results.Ok(new
            {
                policy.LockoutEnabled,
                policy.LockoutThreshold,
                LockoutDurationMinutes = policy.LockoutDuration.TotalMinutes,
                LockoutObservationWindowMinutes = policy.LockoutObservationWindow.TotalMinutes,
            });
        })
        .WithName("UpdateLockoutPolicy")
        .WithTags("AccountLockout");

        // GET /api/v1/lockout/status/{*dn} — get lockout info for a user
        group.MapGet("/status/{*dn}", (AccountLockoutService lockoutService, string dn) =>
        {
            var decodedDn = Uri.UnescapeDataString(dn);
            var info = lockoutService.GetLockoutInfo(decodedDn);
            return Results.Ok(new
            {
                info.DistinguishedName,
                info.FailedAttemptCount,
                info.LockoutTime,
                info.LastFailedAttempt,
                info.IsLockedOut,
            });
        })
        .WithName("GetLockoutStatus")
        .WithTags("AccountLockout");

        // POST /api/v1/lockout/unlock/{*dn} — unlock a specific account
        group.MapPost("/unlock/{*dn}", async (AccountLockoutService lockoutService, string dn) =>
        {
            var decodedDn = Uri.UnescapeDataString(dn);
            await lockoutService.UnlockAccount(decodedDn);
            return Results.Ok(new { message = $"Account '{decodedDn}' has been unlocked." });
        })
        .WithName("UnlockAccount")
        .WithTags("AccountLockout");

        // GET /api/v1/lockout/locked-accounts — list all currently locked accounts
        group.MapGet("/locked-accounts", (AccountLockoutService lockoutService) =>
        {
            var locked = lockoutService.GetLockedAccounts();
            return Results.Ok(locked.Select(info => new
            {
                info.DistinguishedName,
                info.FailedAttemptCount,
                info.LockoutTime,
                info.LastFailedAttempt,
                info.IsLockedOut,
            }));
        })
        .WithName("ListLockedAccounts")
        .WithTags("AccountLockout");

        return group;
    }
}

public record UpdateLockoutPolicyRequest(
    bool? LockoutEnabled = null,
    int? LockoutThreshold = null,
    double? LockoutDurationMinutes = null,
    double? LockoutObservationWindowMinutes = null);
