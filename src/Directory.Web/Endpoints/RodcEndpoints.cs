using Directory.Web.Services;

namespace Directory.Web.Endpoints;

public static class RodcEndpoints
{
    public static RouteGroupBuilder MapRodcEndpoints(this RouteGroupBuilder group)
    {
        // ── Settings ───────────────────────────────────────────────────────

        group.MapGet("/settings", (RodcService svc) =>
        {
            var s = svc.Settings;
            return Results.Ok(new RodcSettingsDto
            {
                IsRodc = s.IsRodc,
                FullDcEndpoint = s.FullDcEndpoint,
                LastReplicationTime = s.LastReplicationTime?.ToString("o"),
                PasswordReplicationAllowed = s.PasswordReplicationAllowed,
                PasswordReplicationDenied = s.PasswordReplicationDenied,
            });
        })
        .WithName("GetRodcSettings")
        .WithTags("RODC");

        group.MapPut("/settings", (UpdateRodcSettingsRequest request, RodcService svc) =>
        {
            svc.UpdateSettings(
                isRodc: request.IsRodc,
                fullDcEndpoint: request.FullDcEndpoint,
                passwordReplicationAllowed: request.PasswordReplicationAllowed,
                passwordReplicationDenied: request.PasswordReplicationDenied);

            var s = svc.Settings;
            return Results.Ok(new RodcSettingsDto
            {
                IsRodc = s.IsRodc,
                FullDcEndpoint = s.FullDcEndpoint,
                LastReplicationTime = s.LastReplicationTime?.ToString("o"),
                PasswordReplicationAllowed = s.PasswordReplicationAllowed,
                PasswordReplicationDenied = s.PasswordReplicationDenied,
            });
        })
        .WithName("UpdateRodcSettings")
        .WithTags("RODC");

        // ── Password Cache ─────────────────────────────────────────────────

        group.MapGet("/password-cache", (RodcService svc) =>
        {
            return Results.Ok(new
            {
                CachedPrincipals = svc.GetCachedPasswordPrincipals(),
                AllowedPrincipals = svc.Settings.PasswordReplicationAllowed,
                DeniedPrincipals = svc.Settings.PasswordReplicationDenied,
            });
        })
        .WithName("GetRodcPasswordCache")
        .WithTags("RODC");

        group.MapPost("/password-cache/add", (PasswordCachePrincipalRequest request, RodcService svc) =>
        {
            if (string.IsNullOrWhiteSpace(request.Principal))
                return Results.Problem(statusCode: 400, detail: "Principal DN is required.");

            if (request.List?.Equals("denied", StringComparison.OrdinalIgnoreCase) == true)
            {
                svc.AddPasswordReplicationDenied(request.Principal);
            }
            else
            {
                svc.AddPasswordReplicationAllowed(request.Principal);
            }

            return Results.Ok(new
            {
                AllowedPrincipals = svc.Settings.PasswordReplicationAllowed,
                DeniedPrincipals = svc.Settings.PasswordReplicationDenied,
            });
        })
        .WithName("AddRodcPasswordCachePrincipal")
        .WithTags("RODC");

        group.MapPost("/password-cache/remove", (PasswordCachePrincipalRequest request, RodcService svc) =>
        {
            if (string.IsNullOrWhiteSpace(request.Principal))
                return Results.Problem(statusCode: 400, detail: "Principal DN is required.");

            if (request.List?.Equals("denied", StringComparison.OrdinalIgnoreCase) == true)
            {
                svc.RemovePasswordReplicationDenied(request.Principal);
            }
            else
            {
                svc.RemovePasswordReplicationAllowed(request.Principal);
            }

            return Results.Ok(new
            {
                AllowedPrincipals = svc.Settings.PasswordReplicationAllowed,
                DeniedPrincipals = svc.Settings.PasswordReplicationDenied,
            });
        })
        .WithName("RemoveRodcPasswordCachePrincipal")
        .WithTags("RODC");

        // ── Replication ────────────────────────────────────────────────────

        group.MapPost("/replicate", async (RodcService svc, CancellationToken ct) =>
        {
            try
            {
                var result = await svc.TriggerReplicationAsync(ct);
                return Results.Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(statusCode: 400, detail: ex.Message);
            }
        })
        .WithName("TriggerRodcReplication")
        .WithTags("RODC");

        return group;
    }
}

// ── DTOs ───────────────────────────────────────────────────────────────────

public class RodcSettingsDto
{
    public bool IsRodc { get; set; }
    public string FullDcEndpoint { get; set; } = "";
    public string LastReplicationTime { get; set; }
    public List<string> PasswordReplicationAllowed { get; set; } = new();
    public List<string> PasswordReplicationDenied { get; set; } = new();
}

public record UpdateRodcSettingsRequest(
    bool? IsRodc = null,
    string FullDcEndpoint = null,
    List<string> PasswordReplicationAllowed = null,
    List<string> PasswordReplicationDenied = null
);

public record PasswordCachePrincipalRequest(
    string Principal,
    string List = "allowed" // "allowed" or "denied"
);
