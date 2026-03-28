using Directory.Web.Services;

namespace Directory.Web.Endpoints;

public static class MigrationEndpoints
{
    public static RouteGroupBuilder MapMigrationEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/sources", async (MigrationService svc) =>
        {
            return Results.Ok(await svc.GetSources());
        })
        .WithName("GetMigrationSources")
        .WithTags("Migration");

        group.MapPost("/sources", async (MigrationSource source, MigrationService svc) =>
        {
            var created = await svc.AddSource(source);
            return Results.Created($"/api/v1/migration/sources/{created.Id}", created);
        })
        .WithName("CreateMigrationSource")
        .WithTags("Migration");

        group.MapPut("/sources/{id}", async (string id, MigrationSource source, MigrationService svc) =>
        {
            var updated = await svc.UpdateSource(id, source);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        })
        .WithName("UpdateMigrationSource")
        .WithTags("Migration");

        group.MapDelete("/sources/{id}", async (string id, MigrationService svc) =>
        {
            return await svc.DeleteSource(id) ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteMigrationSource")
        .WithTags("Migration");

        group.MapPost("/sources/test", async (MigrationSource source, MigrationService svc) =>
        {
            var result = await svc.TestConnection(source);
            return Results.Ok(result);
        })
        .WithName("TestMigrationSource")
        .WithTags("Migration");

        group.MapPost("/sources/discover", async (MigrationSource source, MigrationService svc) =>
        {
            var result = await svc.DiscoverSchema(source);
            return Results.Ok(result);
        })
        .WithName("DiscoverMigrationSchema")
        .WithTags("Migration");

        group.MapPost("/preview", async (MigrationPlan plan, MigrationService svc) =>
        {
            var preview = await svc.PreviewMigration(plan);
            return Results.Ok(preview);
        })
        .WithName("PreviewMigration")
        .WithTags("Migration");

        group.MapPost("/execute", async (MigrationPlan plan, MigrationService svc) =>
        {
            var result = await svc.ExecuteMigration(plan);
            return Results.Ok(result);
        })
        .WithName("ExecuteMigration")
        .WithTags("Migration");

        group.MapGet("/status/{id}", async (string id, MigrationService svc) =>
        {
            var status = await svc.GetMigrationStatus(id);
            return status is null ? Results.NotFound() : Results.Ok(status);
        })
        .WithName("GetMigrationStatus")
        .WithTags("Migration");

        group.MapGet("/history", async (MigrationService svc) =>
        {
            return Results.Ok(await svc.GetMigrationHistory());
        })
        .WithName("GetMigrationHistory")
        .WithTags("Migration");

        return group;
    }
}
