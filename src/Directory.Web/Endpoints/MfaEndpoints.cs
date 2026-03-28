using Directory.Security.Mfa;

namespace Directory.Web.Endpoints;

public record MfaVerifyRequest(string Code);
public record MfaValidateRequest(string Code);

public static class MfaEndpoints
{
    public static RouteGroupBuilder MapMfaEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/status/{dn}", async (string dn, MfaService mfaService, CancellationToken ct) =>
        {
            try
            {
                var status = await mfaService.GetMfaStatus(dn, ct);
                return Results.Ok(status);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(statusCode: 404, detail: ex.Message);
            }
        })
        .WithName("GetMfaStatus")
        .WithTags("MFA");

        group.MapPost("/enroll/{dn}", async (string dn, MfaService mfaService, CancellationToken ct) =>
        {
            try
            {
                var result = await mfaService.BeginEnrollment(dn, ct);
                return Results.Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(statusCode: 400, detail: ex.Message);
            }
        })
        .WithName("BeginMfaEnrollment")
        .WithTags("MFA");

        group.MapPost("/enroll/{dn}/verify", async (string dn, MfaVerifyRequest request, MfaService mfaService, CancellationToken ct) =>
        {
            try
            {
                var result = await mfaService.CompleteEnrollment(dn, request.Code, ct);
                return Results.Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(statusCode: 400, detail: ex.Message);
            }
        })
        .WithName("CompleteMfaEnrollment")
        .WithTags("MFA");

        group.MapPost("/validate/{dn}", async (string dn, MfaValidateRequest request, MfaService mfaService, CancellationToken ct) =>
        {
            try
            {
                var result = await mfaService.ValidateCode(dn, request.Code, ct);
                return Results.Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(statusCode: 404, detail: ex.Message);
            }
        })
        .WithName("ValidateMfaCode")
        .WithTags("MFA");

        group.MapDelete("/{dn}", async (string dn, MfaService mfaService, CancellationToken ct) =>
        {
            try
            {
                await mfaService.DisableMfa(dn, ct);
                return Results.Ok(new { message = "MFA disabled successfully." });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(statusCode: 404, detail: ex.Message);
            }
        })
        .WithName("DisableMfa")
        .WithTags("MFA");

        group.MapPost("/recovery-codes/{dn}", async (string dn, MfaService mfaService, CancellationToken ct) =>
        {
            try
            {
                var result = await mfaService.RegenerateRecoveryCodes(dn, ct);
                return Results.Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(statusCode: 400, detail: ex.Message);
            }
        })
        .WithName("RegenerateRecoveryCodes")
        .WithTags("MFA");

        return group;
    }
}
