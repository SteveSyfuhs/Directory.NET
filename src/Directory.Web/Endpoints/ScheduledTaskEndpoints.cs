using Directory.Web.Services;

namespace Directory.Web.Endpoints;

public static class ScheduledTaskEndpoints
{
    public static RouteGroupBuilder MapScheduledTaskEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", (ScheduledTaskService svc) =>
        {
            return Results.Ok(svc.GetAllTasks());
        })
        .WithName("GetScheduledTasks")
        .WithTags("ScheduledTasks");

        group.MapGet("/{id}", (string id, ScheduledTaskService svc) =>
        {
            var task = svc.GetTask(id);
            return task is null ? Results.NotFound() : Results.Ok(task);
        })
        .WithName("GetScheduledTask")
        .WithTags("ScheduledTasks");

        group.MapPost("/", async (ScheduledTask task, ScheduledTaskService svc) =>
        {
            var created = await svc.CreateTask(task);
            return Results.Created($"/api/v1/scheduled-tasks/{created.Id}", created);
        })
        .WithName("CreateScheduledTask")
        .WithTags("ScheduledTasks");

        group.MapPut("/{id}", async (string id, ScheduledTask task, ScheduledTaskService svc) =>
        {
            var updated = await svc.UpdateTask(id, task);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        })
        .WithName("UpdateScheduledTask")
        .WithTags("ScheduledTasks");

        group.MapDelete("/{id}", async (string id, ScheduledTaskService svc) =>
        {
            var deleted = await svc.DeleteTask(id);
            return deleted ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteScheduledTask")
        .WithTags("ScheduledTasks");

        group.MapPost("/{id}/run", async (string id, ScheduledTaskService svc) =>
        {
            try
            {
                var record = await svc.RunNow(id);
                return Results.Ok(record);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        })
        .WithName("RunScheduledTaskNow")
        .WithTags("ScheduledTasks");

        group.MapGet("/{id}/history", (string id, ScheduledTaskService svc) =>
        {
            var task = svc.GetTask(id);
            if (task is null) return Results.NotFound();
            return Results.Ok(svc.GetTaskHistory(id));
        })
        .WithName("GetScheduledTaskHistory")
        .WithTags("ScheduledTasks");

        return group;
    }
}
