using Directory.Web.Services;

namespace Directory.Web.Endpoints;

public static class DcManagementEndpoints
{
    public static RouteGroupBuilder MapDcManagementEndpoints(this RouteGroupBuilder group)
    {
        // ── Demotion ────────────────────────────────────────────────────────

        group.MapPost("/demote", (
            DemotionRequest request,
            SetupStateService state,
            DcDemotionService demotionService) =>
        {
            if (state.IsProvisioning)
                return Results.Conflict(new { Error = "An operation is already in progress." });

            // Kick off demotion in a background task
            _ = Task.Run(async () =>
            {
                try
                {
                    await demotionService.DemoteAsync(request);
                }
                catch (Exception)
                {
                    // Errors are captured inside DemoteAsync via SetupStateService
                }
            });

            return Results.Ok(new { Success = true, Message = "Demotion started." });
        })
        .WithName("StartDcDemotion")
        .WithTags("DC Management");

        group.MapGet("/demote/status", (SetupStateService state) =>
        {
            return Results.Ok(new
            {
                IsInProgress = state.IsProvisioning,
                Progress = state.ProvisioningProgress,
                Phase = state.ProvisioningPhase,
                Error = state.ProvisioningError,
                IsComplete = state.ProvisioningProgress >= 100 && !state.IsProvisioning,
            });
        })
        .WithName("GetDemotionStatus")
        .WithTags("DC Management");

        group.MapPost("/demote/validate", async (
            DemotionRequest request,
            DcDemotionService demotionService,
            CancellationToken ct) =>
        {
            var result = await demotionService.ValidateAsync(request, ct);
            return Results.Ok(result);
        })
        .WithName("ValidateDcDemotion")
        .WithTags("DC Management");

        // ── FSMO Roles ──────────────────────────────────────────────────────

        group.MapGet("/fsmo", async (FsmoRoleService fsmoService, CancellationToken ct) =>
        {
            var roles = await fsmoService.GetAllRoleHoldersAsync(ct);
            var dcs = await fsmoService.GetAllDcsAsync(ct);
            return Results.Ok(new { Roles = roles, DomainControllers = dcs });
        })
        .WithName("GetFsmoRoles")
        .WithTags("DC Management");

        group.MapPost("/fsmo/transfer", async (
            FsmoRoleTransferRequest request,
            FsmoRoleService fsmoService,
            CancellationToken ct) =>
        {
            if (!Enum.TryParse<FsmoRole>(request.Role, ignoreCase: true, out var role))
            {
                return Results.BadRequest(new { Error = $"Invalid FSMO role: {request.Role}" });
            }

            var result = await fsmoService.TransferRoleAsync(role, request.TargetNtdsSettingsDn, ct);
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        })
        .WithName("TransferFsmoRole")
        .WithTags("DC Management");

        group.MapPost("/fsmo/seize", async (
            FsmoRoleTransferRequest request,
            FsmoRoleService fsmoService,
            CancellationToken ct) =>
        {
            if (!Enum.TryParse<FsmoRole>(request.Role, ignoreCase: true, out var role))
            {
                return Results.BadRequest(new { Error = $"Invalid FSMO role: {request.Role}" });
            }

            var result = await fsmoService.SeizeRoleAsync(role, request.TargetNtdsSettingsDn, ct);
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        })
        .WithName("SeizeFsmoRole")
        .WithTags("DC Management");

        // ── Functional Levels ───────────────────────────────────────────────

        group.MapGet("/functional-levels", async (
            FunctionalLevelService flService,
            CancellationToken ct) =>
        {
            var status = await flService.GetStatusAsync(ct);
            return Results.Ok(status);
        })
        .WithName("GetFunctionalLevels")
        .WithTags("DC Management");

        group.MapPost("/functional-levels/domain", async (
            RaiseFunctionalLevelRequest request,
            FunctionalLevelService flService,
            CancellationToken ct) =>
        {
            if (!Enum.TryParse<DomainFunctionalLevel>(request.TargetLevel.ToString(), out var target))
            {
                return Results.BadRequest(new { Error = $"Invalid domain functional level: {request.TargetLevel}" });
            }

            var result = await flService.RaiseDomainFunctionalLevelAsync(target, ct);
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        })
        .WithName("RaiseDomainFunctionalLevel")
        .WithTags("DC Management");

        group.MapPost("/functional-levels/forest", async (
            RaiseFunctionalLevelRequest request,
            FunctionalLevelService flService,
            CancellationToken ct) =>
        {
            if (!Enum.TryParse<ForestFunctionalLevel>(request.TargetLevel.ToString(), out var target))
            {
                return Results.BadRequest(new { Error = $"Invalid forest functional level: {request.TargetLevel}" });
            }

            var result = await flService.RaiseForestFunctionalLevelAsync(target, ct);
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        })
        .WithName("RaiseForestFunctionalLevel")
        .WithTags("DC Management");

        return group;
    }
}

// ── Request DTOs ────────────────────────────────────────────────────────────

public record FsmoRoleTransferRequest(string Role, string TargetNtdsSettingsDn);
public record RaiseFunctionalLevelRequest(int TargetLevel);
