using Directory.Security.SshKeys;

namespace Directory.Web.Endpoints;

public static class SshKeyEndpoints
{
    public static RouteGroupBuilder MapSshKeyEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/{**dn}", async (string dn, SshKeyService svc, CancellationToken ct) =>
        {
            var decodedDn = Uri.UnescapeDataString(dn);
            var keys = await svc.ListKeysAsync(decodedDn, ct);
            return Results.Ok(keys);
        })
        .WithName("ListSshKeys")
        .WithTags("SSH Keys");

        group.MapPost("/{**dn}", async (string dn, AddSshKeyRequest req, SshKeyService svc, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.PublicKey))
                return Results.Problem(statusCode: 400, detail: "PublicKey is required");

            var decodedDn = Uri.UnescapeDataString(dn);

            try
            {
                var key = await svc.AddKeyAsync(decodedDn, req.PublicKey, ct);
                return Results.Created($"/api/v1/ssh-keys/{key.Id}", key);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(statusCode: 400, detail: ex.Message);
            }
        })
        .WithName("AddSshKey")
        .WithTags("SSH Keys");

        group.MapDelete("/key/{id}", async (string id, string userDn, SshKeyService svc, CancellationToken ct) =>
        {
            var decodedDn = Uri.UnescapeDataString(userDn);
            var ok = await svc.DeleteKeyAsync(decodedDn, id, ct);
            return ok ? Results.Ok() : Results.NotFound();
        })
        .WithName("DeleteSshKey")
        .WithTags("SSH Keys");

        group.MapGet("/authorized/{username}", async (string username, SshKeyService svc, CancellationToken ct) =>
        {
            var keys = await svc.GetAuthorizedKeysAsync(username, ct);
            return Results.Text(keys, "text/plain");
        })
        .WithName("GetAuthorizedKeys")
        .WithTags("SSH Keys");

        return group;
    }
}

public record AddSshKeyRequest(string PublicKey);
