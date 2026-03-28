using Directory.Web.Services;

namespace Directory.Web.Endpoints;

public static class MdmEndpoints
{
    public static RouteGroupBuilder MapMdmEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/integrations", async (MdmIntegrationService svc) =>
        {
            return Results.Ok(await svc.GetIntegrations());
        })
        .WithName("GetMdmIntegrations")
        .WithTags("MDM");

        group.MapGet("/integrations/{id}", async (string id, MdmIntegrationService svc) =>
        {
            var integration = await svc.GetIntegration(id);
            return integration is null ? Results.NotFound() : Results.Ok(integration);
        })
        .WithName("GetMdmIntegration")
        .WithTags("MDM");

        group.MapPost("/integrations", async (MdmIntegration integration, MdmIntegrationService svc) =>
        {
            var created = await svc.CreateIntegration(integration);
            return Results.Created($"/api/v1/mdm/integrations/{created.Id}", created);
        })
        .WithName("CreateMdmIntegration")
        .WithTags("MDM");

        group.MapPut("/integrations/{id}", async (string id, MdmIntegration integration, MdmIntegrationService svc) =>
        {
            var updated = await svc.UpdateIntegration(id, integration);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        })
        .WithName("UpdateMdmIntegration")
        .WithTags("MDM");

        group.MapDelete("/integrations/{id}", async (string id, MdmIntegrationService svc) =>
        {
            return await svc.DeleteIntegration(id) ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteMdmIntegration")
        .WithTags("MDM");

        group.MapGet("/devices", async (string integrationId, MdmIntegrationService svc) =>
        {
            return Results.Ok(await svc.GetDevices(integrationId));
        })
        .WithName("GetMdmDevices")
        .WithTags("MDM");

        group.MapPost("/devices/sync", async (string integrationId, MdmIntegrationService svc) =>
        {
            var result = await svc.SyncDevices(integrationId);
            return Results.Ok(result);
        })
        .WithName("SyncMdmDevices")
        .WithTags("MDM");

        return group;
    }
}
