using Directory.Security.SmartCard;

namespace Directory.Web.Endpoints;

public static class SmartCardEndpoints
{
    public static RouteGroupBuilder MapSmartCardEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/authenticate", async (CertificateAuthRequest request, SmartCardService service, CancellationToken ct) =>
        {
            try
            {
                var result = await service.AuthenticateWithCertificate(request.CertificateData, ct);
                return Results.Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(statusCode: 400, detail: ex.Message);
            }
        })
        .WithName("SmartCardAuthenticate")
        .WithTags("SmartCard");

        group.MapGet("/mappings/{dn}", async (string dn, SmartCardService service, CancellationToken ct) =>
        {
            try
            {
                var mappings = await service.GetMappingsForUser(dn, ct);
                return Results.Ok(mappings);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(statusCode: 404, detail: ex.Message);
            }
        })
        .WithName("GetSmartCardMappings")
        .WithTags("SmartCard");

        group.MapPost("/mappings", async (CreateMappingRequest request, SmartCardService service, CancellationToken ct) =>
        {
            try
            {
                var mapping = await service.MapCertificateToUser(
                    request.UserDn, request.CertificateData, request.MappingType, ct);
                return Results.Ok(mapping);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(statusCode: 400, detail: ex.Message);
            }
        })
        .WithName("CreateSmartCardMapping")
        .WithTags("SmartCard");

        group.MapDelete("/mappings/{dn}/{id}", async (string dn, string id, SmartCardService service, CancellationToken ct) =>
        {
            try
            {
                await service.DeleteMapping(dn, id, ct);
                return Results.Ok(new { message = "Mapping deleted." });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(statusCode: 404, detail: ex.Message);
            }
        })
        .WithName("DeleteSmartCardMapping")
        .WithTags("SmartCard");

        group.MapGet("/settings", async (SmartCardService service, CancellationToken ct) =>
        {
            var settings = await service.GetSmartCardSettings(ct);
            return Results.Ok(settings);
        })
        .WithName("GetSmartCardSettings")
        .WithTags("SmartCard");

        group.MapPut("/settings", async (SmartCardSettings settings, SmartCardService service, CancellationToken ct) =>
        {
            try
            {
                var updated = await service.UpdateSmartCardSettings(settings, ct);
                return Results.Ok(updated);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(statusCode: 400, detail: ex.Message);
            }
        })
        .WithName("UpdateSmartCardSettings")
        .WithTags("SmartCard");

        return group;
    }
}
