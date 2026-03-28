using Directory.Security.Radius;

namespace Directory.Web.Endpoints;

public static class RadiusEndpoints
{
    public static RouteGroupBuilder MapRadiusEndpoints(this RouteGroupBuilder group)
    {
        // ── Settings ─────────────────────────────────────────────

        group.MapGet("/settings", async (RadiusServer svc, CancellationToken ct) =>
        {
            var settings = await svc.GetSettingsAsync(ct);
            return Results.Ok(settings);
        })
        .WithName("GetRadiusSettings")
        .WithTags("RADIUS");

        group.MapPut("/settings", async (RadiusSettings settings, RadiusServer svc, CancellationToken ct) =>
        {
            var updated = await svc.UpdateSettingsAsync(settings, ct);
            return Results.Ok(updated);
        })
        .WithName("UpdateRadiusSettings")
        .WithTags("RADIUS");

        // ── Clients ──────────────────────────────────────────────

        group.MapGet("/clients", async (RadiusServer svc, CancellationToken ct) =>
        {
            var clients = await svc.GetClientsAsync(ct);
            return Results.Ok(clients);
        })
        .WithName("ListRadiusClients")
        .WithTags("RADIUS");

        group.MapPost("/clients", async (RadiusClient client, RadiusServer svc, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(client.Name))
                return Results.Problem(statusCode: 400, detail: "Name is required");
            if (string.IsNullOrWhiteSpace(client.IpAddress))
                return Results.Problem(statusCode: 400, detail: "IpAddress is required");
            if (string.IsNullOrWhiteSpace(client.SharedSecret))
                return Results.Problem(statusCode: 400, detail: "SharedSecret is required");

            var created = await svc.AddClientAsync(client, ct);
            return Results.Created($"/api/v1/radius/clients/{created.Id}", created);
        })
        .WithName("AddRadiusClient")
        .WithTags("RADIUS");

        group.MapPut("/clients/{id}", async (string id, RadiusClient client, RadiusServer svc, CancellationToken ct) =>
        {
            try
            {
                var updated = await svc.UpdateClientAsync(id, client, ct);
                return Results.Ok(updated);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(statusCode: 404, detail: ex.Message);
            }
        })
        .WithName("UpdateRadiusClient")
        .WithTags("RADIUS");

        group.MapDelete("/clients/{id}", async (string id, RadiusServer svc, CancellationToken ct) =>
        {
            var ok = await svc.DeleteClientAsync(id, ct);
            return ok ? Results.Ok() : Results.NotFound();
        })
        .WithName("DeleteRadiusClient")
        .WithTags("RADIUS");

        // ── Log ──────────────────────────────────────────────────

        group.MapGet("/log", async (RadiusServer svc, CancellationToken ct) =>
        {
            var log = await svc.GetLogAsync(ct);
            return Results.Ok(log);
        })
        .WithName("GetRadiusLog")
        .WithTags("RADIUS");

        return group;
    }
}
