using Directory.Ldap.Server;

namespace Directory.Web.Endpoints;

public static class RateLimitEndpoints
{
    public static RouteGroupBuilder MapRateLimitEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/stats", (PerBindDnRateLimiter limiter) =>
        {
            return Results.Ok(limiter.GetStats());
        })
        .WithName("GetRateLimitStats")
        .WithTags("RateLimiting");

        group.MapPost("/stats/reset", (PerBindDnRateLimiter limiter) =>
        {
            limiter.ResetStats();
            return Results.Ok(new { message = "Statistics reset." });
        })
        .WithName("ResetRateLimitStats")
        .WithTags("RateLimiting");

        group.MapGet("/limits", (PerBindDnRateLimiter limiter) =>
        {
            return Results.Ok(new
            {
                defaultPermitLimit = limiter.DefaultPermitLimit,
                defaultWindowSeconds = (int)limiter.DefaultWindow.TotalSeconds,
                customLimits = limiter.GetCustomLimits(),
                exemptions = limiter.GetExemptions()
            });
        })
        .WithName("GetRateLimits")
        .WithTags("RateLimiting");

        group.MapPost("/limits", (DnRateLimitRequest request, PerBindDnRateLimiter limiter) =>
        {
            var window = request.WindowSeconds > 0
                ? TimeSpan.FromSeconds(request.WindowSeconds)
                : (TimeSpan?)null;
            limiter.SetDnLimit(request.Dn, request.PermitLimit, window);
            return Results.Ok(new { message = $"Rate limit set for {request.Dn}." });
        })
        .WithName("SetDnRateLimit")
        .WithTags("RateLimiting");

        group.MapDelete("/limits/{dn}", (string dn, PerBindDnRateLimiter limiter) =>
        {
            return limiter.RemoveDnLimit(dn) ? Results.NoContent() : Results.NotFound();
        })
        .WithName("RemoveDnRateLimit")
        .WithTags("RateLimiting");

        group.MapPost("/defaults", (DefaultRateLimitRequest request, PerBindDnRateLimiter limiter) =>
        {
            if (request.PermitLimit > 0)
                limiter.DefaultPermitLimit = request.PermitLimit;
            if (request.WindowSeconds > 0)
                limiter.DefaultWindow = TimeSpan.FromSeconds(request.WindowSeconds);
            return Results.Ok(new
            {
                defaultPermitLimit = limiter.DefaultPermitLimit,
                defaultWindowSeconds = (int)limiter.DefaultWindow.TotalSeconds
            });
        })
        .WithName("SetDefaultRateLimit")
        .WithTags("RateLimiting");

        group.MapPost("/exemptions", (DnExemptionRequest request, PerBindDnRateLimiter limiter) =>
        {
            limiter.AddExemption(request.Dn);
            return Results.Ok(new { message = $"Exemption added for {request.Dn}." });
        })
        .WithName("AddRateLimitExemption")
        .WithTags("RateLimiting");

        group.MapDelete("/exemptions/{dn}", (string dn, PerBindDnRateLimiter limiter) =>
        {
            return limiter.RemoveExemption(dn) ? Results.NoContent() : Results.NotFound();
        })
        .WithName("RemoveRateLimitExemption")
        .WithTags("RateLimiting");

        return group;
    }
}

public record DnRateLimitRequest(string Dn, int PermitLimit, int WindowSeconds = 60);
public record DefaultRateLimitRequest(int PermitLimit, int WindowSeconds);
public record DnExemptionRequest(string Dn);
