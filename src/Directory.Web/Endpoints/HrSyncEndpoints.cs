using Directory.Web.Services;

namespace Directory.Web.Endpoints;

public static class HrSyncEndpoints
{
    public static RouteGroupBuilder MapHrSyncEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", async (HrSyncService svc) =>
        {
            return Results.Ok(await svc.GetAllConfigurations());
        })
        .WithName("GetHrSyncConfigurations")
        .WithTags("HR Sync");

        group.MapGet("/{id}", async (string id, HrSyncService svc) =>
        {
            var config = await svc.GetConfiguration(id);
            return config is null ? Results.NotFound() : Results.Ok(config);
        })
        .WithName("GetHrSyncConfiguration")
        .WithTags("HR Sync");

        group.MapPost("/", async (HrSyncConfiguration config, HrSyncService svc) =>
        {
            var created = await svc.CreateConfiguration(config);
            return Results.Created($"/api/v1/hr-sync/{created.Id}", created);
        })
        .WithName("CreateHrSyncConfiguration")
        .WithTags("HR Sync");

        group.MapPut("/{id}", async (string id, HrSyncConfiguration config, HrSyncService svc) =>
        {
            var updated = await svc.UpdateConfiguration(id, config);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        })
        .WithName("UpdateHrSyncConfiguration")
        .WithTags("HR Sync");

        group.MapDelete("/{id}", async (string id, HrSyncService svc) =>
        {
            var deleted = await svc.DeleteConfiguration(id);
            return deleted ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteHrSyncConfiguration")
        .WithTags("HR Sync");

        group.MapPost("/{id}/sync", async (string id, HrSyncService svc) =>
        {
            try
            {
                var entry = await svc.SyncNow(id);
                return Results.Ok(entry);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(statusCode: 409, detail: ex.Message);
            }
        })
        .WithName("TriggerHrSync")
        .WithTags("HR Sync");

        group.MapGet("/{id}/status", async (string id, HrSyncService svc) =>
        {
            return Results.Ok(await svc.GetSyncStatus(id));
        })
        .WithName("GetHrSyncStatus")
        .WithTags("HR Sync");

        group.MapGet("/{id}/history", async (string id, HrSyncService svc) =>
        {
            return Results.Ok(await svc.GetSyncHistory(id));
        })
        .WithName("GetHrSyncHistory")
        .WithTags("HR Sync");

        group.MapPost("/{id}/preview", async (string id, HrSyncService svc) =>
        {
            try
            {
                var preview = await svc.PreviewSync(id);
                return Results.Ok(preview);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        })
        .WithName("PreviewHrSync")
        .WithTags("HR Sync");

        group.MapGet("/source-types", () =>
        {
            return Results.Ok(Enum.GetNames<HrSyncSourceType>());
        })
        .WithName("GetHrSyncSourceTypes")
        .WithTags("HR Sync");

        group.MapGet("/attribute-mapping/defaults", () =>
        {
            return Results.Ok(HrSyncService.DefaultAttributeMapping);
        })
        .WithName("GetDefaultHrAttributeMapping")
        .WithTags("HR Sync");

        return group;
    }
}
