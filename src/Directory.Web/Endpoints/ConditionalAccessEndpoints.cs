using Directory.Security.Mfa;

namespace Directory.Web.Endpoints;

public static class ConditionalAccessEndpoints
{
    public static RouteGroupBuilder MapConditionalAccessEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/policies", async (ConditionalMfaService service, CancellationToken ct) =>
        {
            var policies = await service.GetPolicies(ct);
            return Results.Ok(policies);
        })
        .WithName("ListConditionalAccessPolicies")
        .WithTags("ConditionalAccess");

        group.MapPost("/policies", async (ConditionalAccessPolicy policy, ConditionalMfaService service, CancellationToken ct) =>
        {
            try
            {
                var created = await service.CreatePolicy(policy, ct);
                return Results.Ok(created);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(statusCode: 400, detail: ex.Message);
            }
        })
        .WithName("CreateConditionalAccessPolicy")
        .WithTags("ConditionalAccess");

        group.MapPut("/policies/{id}", async (string id, ConditionalAccessPolicy policy, ConditionalMfaService service, CancellationToken ct) =>
        {
            try
            {
                policy.Id = id;
                var updated = await service.UpdatePolicy(policy, ct);
                return Results.Ok(updated);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(statusCode: 404, detail: ex.Message);
            }
        })
        .WithName("UpdateConditionalAccessPolicy")
        .WithTags("ConditionalAccess");

        group.MapDelete("/policies/{id}", async (string id, ConditionalMfaService service, CancellationToken ct) =>
        {
            try
            {
                await service.DeletePolicy(id, ct);
                return Results.Ok(new { message = "Policy deleted." });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(statusCode: 404, detail: ex.Message);
            }
        })
        .WithName("DeleteConditionalAccessPolicy")
        .WithTags("ConditionalAccess");

        group.MapPost("/evaluate", async (AccessEvaluationRequest request, ConditionalMfaService service, CancellationToken ct) =>
        {
            try
            {
                var result = await service.EvaluateAccess(request, ct);
                return Results.Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(statusCode: 400, detail: ex.Message);
            }
        })
        .WithName("EvaluateConditionalAccess")
        .WithTags("ConditionalAccess");

        group.MapGet("/sign-in-log", async (ConditionalMfaService service, CancellationToken ct, int count = 100) =>
        {
            var log = await service.GetSignInLog(count, ct);
            return Results.Ok(log);
        })
        .WithName("GetConditionalAccessSignInLog")
        .WithTags("ConditionalAccess");

        return group;
    }
}
