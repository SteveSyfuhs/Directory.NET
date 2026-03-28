using Directory.Web.Services;

namespace Directory.Web.Endpoints;

public static class WebhookEndpoints
{
    public static RouteGroupBuilder MapWebhookEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", async (WebhookService svc) =>
        {
            return Results.Ok(await svc.GetAllSubscriptions());
        })
        .WithName("GetWebhookSubscriptions")
        .WithTags("Webhooks");

        group.MapGet("/events", () =>
        {
            return Results.Ok(WebhookService.AvailableEventTypes);
        })
        .WithName("GetWebhookEventTypes")
        .WithTags("Webhooks");

        group.MapGet("/{id}", async (string id, WebhookService svc) =>
        {
            var sub = await svc.GetSubscription(id);
            return sub is null ? Results.NotFound() : Results.Ok(sub);
        })
        .WithName("GetWebhookSubscription")
        .WithTags("Webhooks");

        group.MapPost("/", async (WebhookSubscription subscription, WebhookService svc) =>
        {
            var created = await svc.CreateSubscription(subscription);
            return Results.Created($"/api/v1/webhooks/{created.Id}", created);
        })
        .WithName("CreateWebhookSubscription")
        .WithTags("Webhooks");

        group.MapPut("/{id}", async (string id, WebhookSubscription subscription, WebhookService svc) =>
        {
            var updated = await svc.UpdateSubscription(id, subscription);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        })
        .WithName("UpdateWebhookSubscription")
        .WithTags("Webhooks");

        group.MapDelete("/{id}", async (string id, WebhookService svc) =>
        {
            var deleted = await svc.DeleteSubscription(id);
            return deleted ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteWebhookSubscription")
        .WithTags("Webhooks");

        group.MapPost("/{id}/test", async (string id, WebhookService svc) =>
        {
            try
            {
                var record = await svc.SendTestEvent(id);
                return Results.Ok(record);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        })
        .WithName("TestWebhook")
        .WithTags("Webhooks");

        group.MapGet("/{id}/deliveries", (string id, WebhookService svc) =>
        {
            return Results.Ok(svc.GetDeliveries(id));
        })
        .WithName("GetWebhookDeliveries")
        .WithTags("Webhooks");

        return group;
    }
}
