using Directory.Web.Services;

namespace Directory.Web.Endpoints;

public static class DataRetentionEndpoints
{
    public static RouteGroupBuilder MapDataRetentionEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/policies", async (DataRetentionService svc) =>
        {
            var policies = await svc.GetAllPoliciesAsync();
            return Results.Ok(policies);
        })
        .WithName("ListRetentionPolicies")
        .WithTags("DataRetention");

        group.MapPost("/policies", async (RetentionPolicy policy, DataRetentionService svc) =>
        {
            var created = await svc.CreatePolicyAsync(policy);
            return Results.Created($"/api/v1/retention/policies/{created.Id}", created);
        })
        .WithName("CreateRetentionPolicy")
        .WithTags("DataRetention");

        group.MapGet("/policies/{id}", async (string id, DataRetentionService svc) =>
        {
            var policy = await svc.GetPolicyAsync(id);
            return policy is null ? Results.NotFound() : Results.Ok(policy);
        })
        .WithName("GetRetentionPolicy")
        .WithTags("DataRetention");

        group.MapPut("/policies/{id}", async (string id, RetentionPolicy policy, DataRetentionService svc) =>
        {
            var updated = await svc.UpdatePolicyAsync(id, policy);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        })
        .WithName("UpdateRetentionPolicy")
        .WithTags("DataRetention");

        group.MapDelete("/policies/{id}", async (string id, DataRetentionService svc) =>
        {
            var deleted = await svc.DeletePolicyAsync(id);
            return deleted ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteRetentionPolicy")
        .WithTags("DataRetention");

        group.MapPost("/policies/{id}/run", async (string id, DataRetentionService svc) =>
        {
            try
            {
                var result = await svc.ApplyPolicyAsync(id);
                return Results.Ok(result);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        })
        .WithName("RunRetentionPolicy")
        .WithTags("DataRetention");

        group.MapGet("/preview/{id}", async (string id, DataRetentionService svc) =>
        {
            try
            {
                var preview = await svc.PreviewPolicyAsync(id);
                return Results.Ok(preview);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        })
        .WithName("PreviewRetentionPolicy")
        .WithTags("DataRetention");

        return group;
    }
}
