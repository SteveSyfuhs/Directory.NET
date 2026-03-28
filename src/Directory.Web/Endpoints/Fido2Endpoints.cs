using Directory.Security.Fido2;

namespace Directory.Web.Endpoints;

public static class Fido2Endpoints
{
    public static RouteGroupBuilder MapFido2Endpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/register/begin", async (Fido2BeginRequest request, Fido2Service fido2Service, CancellationToken ct) =>
        {
            try
            {
                var options = await fido2Service.BeginRegistration(request.UserDn, ct);
                return Results.Ok(options);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(statusCode: 400, detail: ex.Message);
            }
        })
        .WithName("BeginFido2Registration")
        .WithTags("FIDO2");

        group.MapPost("/register/complete", async (Fido2CompleteRegistrationRequest request, Fido2Service fido2Service, CancellationToken ct) =>
        {
            try
            {
                var result = await fido2Service.CompleteRegistration(request.UserDn, request.Attestation, ct);
                return Results.Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(statusCode: 400, detail: ex.Message);
            }
        })
        .WithName("CompleteFido2Registration")
        .WithTags("FIDO2");

        group.MapPost("/authenticate/begin", async (Fido2BeginRequest request, Fido2Service fido2Service, CancellationToken ct) =>
        {
            try
            {
                var options = await fido2Service.BeginAuthentication(request.UserDn, ct);
                return Results.Ok(options);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(statusCode: 400, detail: ex.Message);
            }
        })
        .WithName("BeginFido2Authentication")
        .WithTags("FIDO2");

        group.MapPost("/authenticate/complete", async (Fido2CompleteAuthenticationRequest request, Fido2Service fido2Service, CancellationToken ct) =>
        {
            try
            {
                var result = await fido2Service.CompleteAuthentication(request.UserDn, request.Assertion, ct);
                return Results.Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(statusCode: 400, detail: ex.Message);
            }
        })
        .WithName("CompleteFido2Authentication")
        .WithTags("FIDO2");

        group.MapGet("/credentials/{dn}", async (string dn, Fido2Service fido2Service, CancellationToken ct) =>
        {
            try
            {
                var credentials = await fido2Service.ListCredentials(dn, ct);
                return Results.Ok(credentials);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(statusCode: 404, detail: ex.Message);
            }
        })
        .WithName("ListFido2Credentials")
        .WithTags("FIDO2");

        group.MapDelete("/credentials/{dn}/{credentialId}", async (string dn, string credentialId, Fido2Service fido2Service, CancellationToken ct) =>
        {
            try
            {
                await fido2Service.DeleteCredential(dn, credentialId, ct);
                return Results.Ok(new { message = "Credential deleted." });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(statusCode: 404, detail: ex.Message);
            }
        })
        .WithName("DeleteFido2Credential")
        .WithTags("FIDO2");

        group.MapPut("/credentials/{dn}/{credentialId}", async (string dn, string credentialId, RenameCredentialRequest request, Fido2Service fido2Service, CancellationToken ct) =>
        {
            try
            {
                await fido2Service.RenameCredential(dn, credentialId, request.Name, ct);
                return Results.Ok(new { message = "Credential renamed." });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(statusCode: 404, detail: ex.Message);
            }
        })
        .WithName("RenameFido2Credential")
        .WithTags("FIDO2");

        return group;
    }
}

public record Fido2BeginRequest(string UserDn);

public record Fido2CompleteRegistrationRequest(string UserDn, AttestationResponse Attestation);

public record Fido2CompleteAuthenticationRequest(string UserDn, AssertionResponse Assertion);
