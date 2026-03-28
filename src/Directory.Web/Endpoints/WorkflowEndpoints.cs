using Directory.Web.Services;

namespace Directory.Web.Endpoints;

public static class WorkflowEndpoints
{
    public static RouteGroupBuilder MapWorkflowEndpoints(this RouteGroupBuilder group)
    {
        // Workflow Definitions
        group.MapGet("/", async (WorkflowService svc) =>
        {
            return Results.Ok(await svc.GetAllDefinitions());
        })
        .WithName("GetWorkflowDefinitions")
        .WithTags("Workflows");

        group.MapGet("/{id}", async (string id, WorkflowService svc) =>
        {
            var def = await svc.GetDefinition(id);
            return def is null ? Results.NotFound() : Results.Ok(def);
        })
        .WithName("GetWorkflowDefinition")
        .WithTags("Workflows");

        group.MapPost("/", async (WorkflowDefinition definition, WorkflowService svc) =>
        {
            var created = await svc.CreateDefinition(definition);
            return Results.Created($"/api/v1/workflows/{created.Id}", created);
        })
        .WithName("CreateWorkflowDefinition")
        .WithTags("Workflows");

        group.MapPut("/{id}", async (string id, WorkflowDefinition definition, WorkflowService svc) =>
        {
            var updated = await svc.UpdateDefinition(id, definition);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        })
        .WithName("UpdateWorkflowDefinition")
        .WithTags("Workflows");

        group.MapDelete("/{id}", async (string id, WorkflowService svc) =>
        {
            var deleted = await svc.DeleteDefinition(id);
            return deleted ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteWorkflowDefinition")
        .WithTags("Workflows");

        // Manual trigger
        group.MapPost("/{id}/trigger", async (string id, WorkflowTriggerRequest request, WorkflowService svc) =>
        {
            try
            {
                var instance = await svc.TriggerWorkflow(id, request.TargetDn, request.InitiatedBy);
                return Results.Ok(instance);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        })
        .WithName("TriggerWorkflow")
        .WithTags("Workflows");

        // Workflow Instances
        group.MapGet("/instances", async (string status, WorkflowService svc) =>
        {
            return Results.Ok(await svc.GetAllInstances(status));
        })
        .WithName("GetWorkflowInstances")
        .WithTags("Workflows");

        group.MapGet("/instances/{id}", async (string id, WorkflowService svc) =>
        {
            var inst = await svc.GetInstance(id);
            return inst is null ? Results.NotFound() : Results.Ok(inst);
        })
        .WithName("GetWorkflowInstance")
        .WithTags("Workflows");

        group.MapPost("/instances/{id}/approve", async (string id, WorkflowApprovalRequest request, WorkflowService svc) =>
        {
            var inst = await svc.ApproveStep(id, request?.ApprovedBy);
            return inst is null ? Results.NotFound() : Results.Ok(inst);
        })
        .WithName("ApproveWorkflowStep")
        .WithTags("Workflows");

        group.MapPost("/instances/{id}/reject", async (string id, WorkflowApprovalRequest request, WorkflowService svc) =>
        {
            var inst = await svc.RejectStep(id, request?.ApprovedBy);
            return inst is null ? Results.NotFound() : Results.Ok(inst);
        })
        .WithName("RejectWorkflowStep")
        .WithTags("Workflows");

        // Metadata
        group.MapGet("/triggers", () =>
        {
            return Results.Ok(WorkflowService.GetTriggerTypes());
        })
        .WithName("GetWorkflowTriggerTypes")
        .WithTags("Workflows");

        group.MapGet("/step-types", () =>
        {
            return Results.Ok(WorkflowService.GetStepTypes());
        })
        .WithName("GetWorkflowStepTypes")
        .WithTags("Workflows");

        return group;
    }
}

public record WorkflowTriggerRequest(string TargetDn, string InitiatedBy = null);
public record WorkflowApprovalRequest(string ApprovedBy = null);
