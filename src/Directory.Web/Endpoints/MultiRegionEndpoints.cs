using Directory.Web.Services;

namespace Directory.Web.Endpoints;

public static class MultiRegionEndpoints
{
    public static RouteGroupBuilder MapMultiRegionEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", async (MultiRegionService svc) =>
        {
            return Results.Ok(await svc.GetRegions());
        })
        .WithName("GetRegions")
        .WithTags("MultiRegion");

        group.MapGet("/{id}", async (string id, MultiRegionService svc) =>
        {
            var region = await svc.GetRegion(id);
            return region is null ? Results.NotFound() : Results.Ok(region);
        })
        .WithName("GetRegion")
        .WithTags("MultiRegion");

        group.MapPost("/", async (RegionConfiguration region, MultiRegionService svc) =>
        {
            var created = await svc.CreateRegion(region);
            return Results.Created($"/api/v1/regions/{created.Id}", created);
        })
        .WithName("CreateRegion")
        .WithTags("MultiRegion");

        group.MapPut("/{id}", async (string id, RegionConfiguration region, MultiRegionService svc) =>
        {
            var updated = await svc.UpdateRegion(id, region);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        })
        .WithName("UpdateRegion")
        .WithTags("MultiRegion");

        group.MapDelete("/{id}", async (string id, MultiRegionService svc) =>
        {
            return await svc.DeleteRegion(id) ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteRegion")
        .WithTags("MultiRegion");

        group.MapPost("/{id}/set-primary", async (string id, MultiRegionService svc) =>
        {
            var region = await svc.SetPrimary(id);
            return region is null ? Results.NotFound() : Results.Ok(region);
        })
        .WithName("SetPrimaryRegion")
        .WithTags("MultiRegion");

        group.MapPost("/health-check", async (MultiRegionService svc) =>
        {
            var results = await svc.CheckHealth();
            return Results.Ok(results);
        })
        .WithName("CheckRegionHealth")
        .WithTags("MultiRegion");

        return group;
    }
}
