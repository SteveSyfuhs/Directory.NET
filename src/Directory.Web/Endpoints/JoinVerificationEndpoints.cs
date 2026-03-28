using Directory.Web.Services;

namespace Directory.Web.Endpoints;

public static class JoinVerificationEndpoints
{
    public static RouteGroupBuilder MapJoinVerificationEndpoints(this RouteGroupBuilder group)
    {
        // ── Verify ───────────��──────────────────────────────────────────

        group.MapPost("/verify/{computerName}", async (
            string computerName,
            JoinVerificationService verificationService,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(computerName))
                return Results.Problem(statusCode: 400, detail: "Computer name is required.");

            var result = await verificationService.VerifyAsync(computerName, ct);
            return Results.Ok(result);
        })
        .WithName("VerifyDomainJoin")
        .WithTags("DomainJoinVerification");

        // ── Diagnose ────────────────────────────────────────────────────

        group.MapPost("/diagnose/{computerName}", async (
            string computerName,
            JoinDiagnosticsService diagnosticsService,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(computerName))
                return Results.Problem(statusCode: 400, detail: "Computer name is required.");

            var result = await diagnosticsService.DiagnoseAsync(computerName, ct);
            return Results.Ok(result);
        })
        .WithName("DiagnoseDomainJoin")
        .WithTags("DomainJoinVerification");

        // ── Health ──────────���───────────────────────────��───────────────

        group.MapGet("/health", (JoinDiagnosticsService diagnosticsService) =>
        {
            var summary = diagnosticsService.GetHealthSummary();
            return Results.Ok(summary);
        })
        .WithName("GetDomainJoinHealth")
        .WithTags("DomainJoinVerification");

        // ── Repair ──────────���──────────────────────────────────��────────

        group.MapPost("/repair/{computerName}", async (
            string computerName,
            JoinVerificationService verificationService,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(computerName))
                return Results.Problem(statusCode: 400, detail: "Computer name is required.");

            var result = await verificationService.RepairAsync(computerName, ct);
            return Results.Ok(result);
        })
        .WithName("RepairDomainJoin")
        .WithTags("DomainJoinVerification");

        return group;
    }
}
