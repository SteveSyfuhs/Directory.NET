using Directory.Web.Services;

namespace Directory.Web.Endpoints;

public record SsprRegisterRequest(
    string UserDn,
    List<SecurityQuestionAnswerInput> Answers,
    string RecoveryEmail = null,
    string RecoveryPhone = null);

public record SsprInitiateRequest(string Username);

public record SsprVerifyQuestionsRequest(string Token, List<SecurityQuestionAnswerInput> Answers);

public record SsprVerifyMfaRequest(string Token, string Code);

public record SsprResetRequest(string Token, string NewPassword);

public static class SsprEndpoints
{
    public static RouteGroupBuilder MapSsprEndpoints(this RouteGroupBuilder group)
    {
        // ── Admin endpoints ──

        group.MapGet("/settings", (SelfServicePasswordService ssprService) =>
        {
            return Results.Ok(ssprService.GetSsprSettings());
        })
        .WithName("GetSsprSettings")
        .WithTags("SSPR");

        group.MapPut("/settings", (SelfServicePasswordService ssprService, SsprSettings settings) =>
        {
            var updated = ssprService.UpdateSsprSettings(settings);
            return Results.Ok(updated);
        })
        .WithName("UpdateSsprSettings")
        .WithTags("SSPR");

        group.MapGet("/registrations", async (SelfServicePasswordService ssprService, CancellationToken ct) =>
        {
            var registrations = await ssprService.GetRegistrations(ct);
            return Results.Ok(registrations);
        })
        .WithName("GetSsprRegistrations")
        .WithTags("SSPR");

        // ── User-facing endpoints ──

        group.MapPost("/register", async (SsprRegisterRequest request, SelfServicePasswordService ssprService, CancellationToken ct) =>
        {
            try
            {
                var result = await ssprService.RegisterForSspr(
                    request.UserDn,
                    request.Answers,
                    request.RecoveryEmail,
                    request.RecoveryPhone,
                    ct);

                return Results.Ok(new
                {
                    message = "Successfully registered for self-service password reset.",
                    registeredAt = result.RegisteredAt,
                });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(statusCode: 400, detail: ex.Message);
            }
        })
        .WithName("RegisterForSspr")
        .WithTags("SSPR");

        group.MapGet("/status/{username}", async (string username, SelfServicePasswordService ssprService, CancellationToken ct) =>
        {
            var status = await ssprService.GetRegistrationStatus(Uri.UnescapeDataString(username), ct);
            return Results.Ok(status);
        })
        .WithName("GetSsprStatus")
        .WithTags("SSPR");

        group.MapPost("/initiate", async (SsprInitiateRequest request, SelfServicePasswordService ssprService, CancellationToken ct) =>
        {
            try
            {
                var result = await ssprService.InitiateReset(request.Username, ct);
                return Results.Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(statusCode: 400, detail: ex.Message);
            }
        })
        .WithName("InitiateSsprReset")
        .WithTags("SSPR");

        group.MapPost("/verify-questions", async (SsprVerifyQuestionsRequest request, SelfServicePasswordService ssprService, CancellationToken ct) =>
        {
            try
            {
                var result = await ssprService.ValidateSecurityAnswers(request.Token, request.Answers, ct);
                return Results.Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(statusCode: 400, detail: ex.Message);
            }
        })
        .WithName("VerifySsprQuestions")
        .WithTags("SSPR");

        group.MapPost("/verify-mfa", async (SsprVerifyMfaRequest request, SelfServicePasswordService ssprService, CancellationToken ct) =>
        {
            try
            {
                var result = await ssprService.ValidateMfaCode(request.Token, request.Code, ct);
                return Results.Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(statusCode: 400, detail: ex.Message);
            }
        })
        .WithName("VerifySsprMfa")
        .WithTags("SSPR");

        group.MapPost("/reset", async (SsprResetRequest request, SelfServicePasswordService ssprService, CancellationToken ct) =>
        {
            try
            {
                var result = await ssprService.CompleteReset(request.Token, request.NewPassword, ct);
                return Results.Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(statusCode: 400, detail: ex.Message);
            }
        })
        .WithName("CompleteSsprReset")
        .WithTags("SSPR");

        group.MapGet("/questions", (SelfServicePasswordService ssprService) =>
        {
            var settings = ssprService.GetSsprSettings();
            return Results.Ok(settings.SecurityQuestionOptions);
        })
        .WithName("GetSsprQuestions")
        .WithTags("SSPR");

        return group;
    }
}
