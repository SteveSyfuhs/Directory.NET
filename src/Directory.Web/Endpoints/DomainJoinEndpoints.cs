using Directory.Web.Models;
using Directory.Web.Services;

namespace Directory.Web.Endpoints;

public static class DomainJoinEndpoints
{
    public static RouteGroupBuilder MapDomainJoinEndpoints(this RouteGroupBuilder group)
    {
        // ── Join ─────────────────────────────────────────────────────────

        group.MapPost("/join", async (
            DomainJoinRequest request,
            DomainJoinService joinService,
            HttpContext context) =>
        {
            var joinValidation =
                ValidationHelper.ValidateRequired(request.ComputerName, "computerName") ??
                ValidationHelper.ValidateMaxLength(request.ComputerName, "computerName", maxLength: 15);
            if (joinValidation != null) return joinValidation;

            var sourceIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var result = await joinService.JoinAsync(request, sourceIp, context.RequestAborted);
            return result.Success ? Results.Ok(result) : Results.Problem(statusCode: 400, detail: result.ErrorMessage);
        })
        .WithName("JoinComputer")
        .WithTags("DomainJoin");

        // ── Rejoin ───────────────────────────────────────────────────────

        group.MapPost("/rejoin", async (
            RejoinRequest request,
            DomainJoinService joinService,
            HttpContext context) =>
        {
            var rejoinValidation =
                ValidationHelper.ValidateRequired(request.ComputerName, "computerName") ??
                ValidationHelper.ValidateMaxLength(request.ComputerName, "computerName", maxLength: 15) ??
                ValidationHelper.ValidateDn(request.AdminUserDn, "adminUserDn");
            if (rejoinValidation != null) return rejoinValidation;

            var sourceIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var result = await joinService.RejoinAsync(request.ComputerName, request.AdminUserDn, sourceIp, context.RequestAborted);
            return result.Success ? Results.Ok(result) : Results.Problem(statusCode: 400, detail: result.ErrorMessage);
        })
        .WithName("RejoinComputer")
        .WithTags("DomainJoin");

        // ── Unjoin ───────────────────────────────────────────────────────

        group.MapPost("/unjoin", async (
            UnjoinRequest request,
            DomainJoinService joinService,
            HttpContext context) =>
        {
            var unjoinValidation =
                ValidationHelper.ValidateRequired(request.ComputerName, "computerName") ??
                ValidationHelper.ValidateMaxLength(request.ComputerName, "computerName", maxLength: 15) ??
                ValidationHelper.ValidateDn(request.AdminUserDn, "adminUserDn");
            if (unjoinValidation != null) return unjoinValidation;

            var sourceIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var result = await joinService.UnjoinAsync(request.ComputerName, request.AdminUserDn, sourceIp, context.RequestAborted);
            return result.Success ? Results.Ok(result) : Results.Problem(statusCode: 400, detail: result.ErrorMessage);
        })
        .WithName("UnjoinComputer")
        .WithTags("DomainJoin");

        // ── Info ─────────────────────────────────────────────────────────

        group.MapGet("/info", (DomainJoinService joinService) =>
        {
            return Results.Ok(joinService.GetDomainInfo());
        })
        .WithName("GetDomainJoinInfo")
        .WithTags("DomainJoin");

        // ── Validate (dry run) ───────────────────────────────────────────

        group.MapPost("/validate", async (
            DomainJoinRequest request,
            DomainJoinService joinService,
            HttpContext context) =>
        {
            var validateValidation =
                ValidationHelper.ValidateRequired(request.ComputerName, "computerName") ??
                ValidationHelper.ValidateMaxLength(request.ComputerName, "computerName", maxLength: 15);
            if (validateValidation != null) return validateValidation;

            var validation = await joinService.ValidateAsync(request, context.RequestAborted);
            return Results.Ok(validation);
        })
        .WithName("ValidateDomainJoin")
        .WithTags("DomainJoin");

        // ── History ──────────────────────────────────────────────────────

        group.MapGet("/history", (DomainJoinService joinService) =>
        {
            return Results.Ok(joinService.GetHistory());
        })
        .WithName("GetDomainJoinHistory")
        .WithTags("DomainJoin");

        // ── Offline Domain Join ────────────────────────────────────────

        group.MapPost("/offline/provision", async (
            DjoinProvisionRequest request,
            OfflineDomainJoinService djoinService,
            CancellationToken ct) =>
        {
            var result = await djoinService.ProvisionOfflineJoinAsync(request, ct);
            return result.Success ? Results.Ok(result) : Results.Problem(statusCode: 400, detail: result.ErrorMessage);
        })
        .WithName("ProvisionOfflineJoin")
        .WithTags("OfflineDomainJoin");

        group.MapPost("/offline/validate", async (
            DjoinValidateRequest request,
            OfflineDomainJoinService djoinService,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Blob))
                return Results.Problem(statusCode: 400, detail: "Blob is required");

            var result = await djoinService.ValidateBlobAsync(request.Blob, ct);
            return Results.Ok(result);
        })
        .WithName("ValidateOfflineJoinBlob")
        .WithTags("OfflineDomainJoin");

        group.MapPost("/offline/revoke", async (
            DjoinRevokeRequest request,
            OfflineDomainJoinService djoinService,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.ComputerName))
                return Results.Problem(statusCode: 400, detail: "Computer name is required");

            var revoked = await djoinService.RevokeBlobAsync(request.ComputerName, ct);
            if (!revoked) return Results.NotFound();

            return Results.Ok(new { message = $"Offline join provision for {request.ComputerName} has been revoked" });
        })
        .WithName("RevokeOfflineJoin")
        .WithTags("OfflineDomainJoin");

        // ── Computer Pre-staging ──────────────────────────────────────

        group.MapPost("/prestage", async (
            PrestagingRequest request,
            ComputerPrestagingService prestagingService,
            CancellationToken ct) =>
        {
            var prestageValidation =
                ValidationHelper.ValidateRequired(request.ComputerName, "computerName") ??
                ValidationHelper.ValidateMaxLength(request.ComputerName, "computerName", maxLength: 15);
            if (prestageValidation != null) return prestageValidation;

            var result = await prestagingService.PrestageComputerAsync(request, ct);
            return result.Success
                ? Results.Created($"/api/v1/domain-join/prestage/{result.SamAccountName}", result)
                : Results.Problem(statusCode: 400, detail: result.ErrorMessage);
        })
        .WithName("PrestageComputer")
        .WithTags("ComputerPrestaging");

        group.MapPost("/prestage/bulk", async (
            List<PrestagingRequest> requests,
            ComputerPrestagingService prestagingService,
            CancellationToken ct) =>
        {
            var results = await prestagingService.BulkPrestageAsync(requests, ct);
            return Results.Ok(results);
        })
        .WithName("BulkPrestageComputers")
        .WithTags("ComputerPrestaging");

        group.MapGet("/prestage", async (
            ComputerPrestagingService prestagingService,
            CancellationToken ct) =>
        {
            var computers = await prestagingService.GetPrestagedComputersAsync(ct);
            return Results.Ok(computers);
        })
        .WithName("ListPrestagedComputers")
        .WithTags("ComputerPrestaging");

        group.MapDelete("/prestage/{name}", async (
            string name,
            ComputerPrestagingService prestagingService,
            CancellationToken ct) =>
        {
            var deleted = await prestagingService.DeletePrestagedComputerAsync(name, ct);
            return deleted ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeletePrestagedComputer")
        .WithTags("ComputerPrestaging");

        return group;
    }

    // ── Request DTOs ─────────────────────────────────────────────────

    public record RejoinRequest(string ComputerName, string AdminUserDn);

    public record UnjoinRequest(string ComputerName, string AdminUserDn);

    public record DjoinValidateRequest(string Blob);

    public record DjoinRevokeRequest(string ComputerName);
}
