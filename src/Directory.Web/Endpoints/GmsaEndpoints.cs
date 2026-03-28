using Directory.Security;

namespace Directory.Web.Endpoints;

public static class GmsaEndpoints
{
    public static RouteGroupBuilder MapGmsaEndpoints(this RouteGroupBuilder group)
    {
        // ── gMSA Account Endpoints ──────────────────────────────────

        group.MapGet("/", async (GmsaService svc, CancellationToken ct) =>
        {
            var accounts = await svc.GetGmsaAccountsAsync(ct);
            return Results.Ok(accounts);
        })
        .WithName("ListGmsaAccounts")
        .WithTags("gMSA");

        group.MapGet("/{name}", async (string name, GmsaService svc, CancellationToken ct) =>
        {
            var account = await svc.GetGmsaAccountAsync(name, ct);
            if (account == null)
                return Results.NotFound();

            return Results.Ok(account);
        })
        .WithName("GetGmsaAccount")
        .WithTags("gMSA");

        group.MapPost("/", async (CreateGmsaRequest request, GmsaService svc, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.Problem(statusCode: 400, detail: "Name is required");

            var account = new GmsaAccount
            {
                Name = request.Name,
                DnsHostName = request.DnsHostName ?? "",
                ServicePrincipalNames = request.ServicePrincipalNames ?? [],
                PrincipalsAllowedToRetrievePassword = request.PrincipalsAllowedToRetrievePassword ?? [],
                ManagedPasswordIntervalInDays = request.ManagedPasswordIntervalInDays ?? 30,
            };

            try
            {
                var created = await svc.CreateGmsaAccountAsync(account, ct);
                return Results.Created($"/api/v1/gmsa/{created.Name}", created);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(statusCode: 409, detail: ex.Message);
            }
        })
        .WithName("CreateGmsaAccount")
        .WithTags("gMSA");

        group.MapDelete("/{name}", async (string name, GmsaService svc, CancellationToken ct) =>
        {
            var ok = await svc.DeleteGmsaAccountAsync(name, ct);
            if (!ok)
                return Results.NotFound();

            return Results.Ok();
        })
        .WithName("DeleteGmsaAccount")
        .WithTags("gMSA");

        group.MapPost("/{name}/rotate", async (string name, GmsaService svc, CancellationToken ct) =>
        {
            var ok = await svc.RotatePasswordAsync(name, ct);
            if (!ok)
                return Results.NotFound();

            // Return the updated account
            var account = await svc.GetGmsaAccountAsync(name, ct);
            return Results.Ok(account);
        })
        .WithName("RotateGmsaPassword")
        .WithTags("gMSA");

        // ── KDS Root Key Endpoints ──────────────────────────────────

        group.MapGet("/kds-root-keys", async (GmsaService svc, CancellationToken ct) =>
        {
            var keys = await svc.GetKdsRootKeysAsync(ct);
            return Results.Ok(keys);
        })
        .WithName("ListKdsRootKeys")
        .WithTags("gMSA");

        group.MapPost("/kds-root-keys", async (GmsaService svc, CancellationToken ct) =>
        {
            var key = await svc.CreateKdsRootKeyAsync(ct);
            // Redact key material in response
            return Results.Created($"/api/v1/gmsa/kds-root-keys", key with { KeyValue = [] });
        })
        .WithName("CreateKdsRootKey")
        .WithTags("gMSA");

        return group;
    }
}

public record CreateGmsaRequest(
    string Name,
    string DnsHostName = null,
    List<string> ServicePrincipalNames = null,
    List<string> PrincipalsAllowedToRetrievePassword = null,
    int? ManagedPasswordIntervalInDays = null
);
