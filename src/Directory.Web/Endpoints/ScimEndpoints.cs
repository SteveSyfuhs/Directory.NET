using System.Text.Json;
using Directory.Web.Models;
using Directory.Web.Services;

namespace Directory.Web.Endpoints;

public static class ScimEndpoints
{
    public static RouteGroupBuilder MapScimEndpoints(this RouteGroupBuilder group)
    {
        // SCIM Discovery
        group.MapGet("/ServiceProviderConfig", (ScimService svc) =>
        {
            return Results.Json(svc.GetServiceProviderConfig(), contentType: "application/scim+json");
        })
        .WithName("ScimServiceProviderConfig")
        .WithTags("SCIM")
        .AllowAnonymous();

        group.MapGet("/Schemas", (ScimService svc) =>
        {
            return Results.Json(svc.GetSchemas(), contentType: "application/scim+json");
        })
        .WithName("ScimSchemas")
        .WithTags("SCIM")
        .AllowAnonymous();

        group.MapGet("/ResourceTypes", (ScimService svc) =>
        {
            return Results.Json(svc.GetResourceTypes(), contentType: "application/scim+json");
        })
        .WithName("ScimResourceTypes")
        .WithTags("SCIM")
        .AllowAnonymous();

        // SCIM Users
        group.MapGet("/Users", async (
            ScimService svc,
            HttpContext ctx,
            string filter,
            int? startIndex,
            int? count) =>
        {
            var integrationId = await AuthenticateScim(svc, ctx);
            if (integrationId == null) return Results.Json(new ScimError { Status = "401", Detail = "Unauthorized" }, statusCode: 401);

            var result = await svc.ListUsers(filter, startIndex ?? 1, count ?? 100);
            return Results.Json(result, contentType: "application/scim+json");
        })
        .WithName("ScimListUsers")
        .WithTags("SCIM");

        group.MapGet("/Users/{id}", async (string id, ScimService svc, HttpContext ctx) =>
        {
            var integrationId = await AuthenticateScim(svc, ctx);
            if (integrationId == null) return Results.Json(new ScimError { Status = "401", Detail = "Unauthorized" }, statusCode: 401);

            var user = await svc.GetUser(id);
            return user is null
                ? Results.Json(new ScimError { Status = "404", Detail = "User not found" }, statusCode: 404, contentType: "application/scim+json")
                : Results.Json(user, contentType: "application/scim+json");
        })
        .WithName("ScimGetUser")
        .WithTags("SCIM");

        group.MapPost("/Users", async (ScimUser scimUser, ScimService svc, HttpContext ctx) =>
        {
            var integrationId = await AuthenticateScim(svc, ctx);
            if (integrationId == null) return Results.Json(new ScimError { Status = "401", Detail = "Unauthorized" }, statusCode: 401);

            var userValidation =
                ValidationHelper.ValidateRequired(scimUser.UserName, "userName") ??
                ValidationHelper.ValidateMaxLength(scimUser.UserName, "userName") ??
                ValidationHelper.ValidateMaxLength(scimUser.DisplayName, "displayName");
            if (userValidation != null) return userValidation;

            try
            {
                var created = await svc.CreateUser(scimUser, integrationId);
                return Results.Json(created, statusCode: 201, contentType: "application/scim+json");
            }
            catch (InvalidOperationException ex)
            {
                return Results.Json(new ScimError { Status = "409", ScimType = "uniqueness", Detail = ex.Message }, statusCode: 409, contentType: "application/scim+json");
            }
        })
        .WithName("ScimCreateUser")
        .WithTags("SCIM");

        group.MapPut("/Users/{id}", async (string id, ScimUser scimUser, ScimService svc, HttpContext ctx) =>
        {
            var integrationId = await AuthenticateScim(svc, ctx);
            if (integrationId == null) return Results.Json(new ScimError { Status = "401", Detail = "Unauthorized" }, statusCode: 401);

            var userReplaceValidation =
                ValidationHelper.ValidateRequired(scimUser.UserName, "userName") ??
                ValidationHelper.ValidateMaxLength(scimUser.UserName, "userName") ??
                ValidationHelper.ValidateMaxLength(scimUser.DisplayName, "displayName");
            if (userReplaceValidation != null) return userReplaceValidation;

            var updated = await svc.ReplaceUser(id, scimUser, integrationId);
            return updated is null
                ? Results.Json(new ScimError { Status = "404", Detail = "User not found" }, statusCode: 404, contentType: "application/scim+json")
                : Results.Json(updated, contentType: "application/scim+json");
        })
        .WithName("ScimReplaceUser")
        .WithTags("SCIM");

        group.MapPatch("/Users/{id}", async (string id, ScimPatchRequest patch, ScimService svc, HttpContext ctx) =>
        {
            var integrationId = await AuthenticateScim(svc, ctx);
            if (integrationId == null) return Results.Json(new ScimError { Status = "401", Detail = "Unauthorized" }, statusCode: 401);

            var updated = await svc.PatchUser(id, patch, integrationId);
            return updated is null
                ? Results.Json(new ScimError { Status = "404", Detail = "User not found" }, statusCode: 404, contentType: "application/scim+json")
                : Results.Json(updated, contentType: "application/scim+json");
        })
        .WithName("ScimPatchUser")
        .WithTags("SCIM");

        group.MapDelete("/Users/{id}", async (string id, ScimService svc, HttpContext ctx) =>
        {
            var integrationId = await AuthenticateScim(svc, ctx);
            if (integrationId == null) return Results.Json(new ScimError { Status = "401", Detail = "Unauthorized" }, statusCode: 401);

            var deleted = await svc.DeleteUser(id, integrationId);
            return deleted
                ? Results.NoContent()
                : Results.Json(new ScimError { Status = "404", Detail = "User not found" }, statusCode: 404, contentType: "application/scim+json");
        })
        .WithName("ScimDeleteUser")
        .WithTags("SCIM");

        // SCIM Groups
        group.MapGet("/Groups", async (
            ScimService svc,
            HttpContext ctx,
            string filter,
            int? startIndex,
            int? count) =>
        {
            var integrationId = await AuthenticateScim(svc, ctx);
            if (integrationId == null) return Results.Json(new ScimError { Status = "401", Detail = "Unauthorized" }, statusCode: 401);

            var result = await svc.ListGroups(filter, startIndex ?? 1, count ?? 100);
            return Results.Json(result, contentType: "application/scim+json");
        })
        .WithName("ScimListGroups")
        .WithTags("SCIM");

        group.MapGet("/Groups/{id}", async (string id, ScimService svc, HttpContext ctx) =>
        {
            var integrationId = await AuthenticateScim(svc, ctx);
            if (integrationId == null) return Results.Json(new ScimError { Status = "401", Detail = "Unauthorized" }, statusCode: 401);

            var grp = await svc.GetGroup(id);
            return grp is null
                ? Results.Json(new ScimError { Status = "404", Detail = "Group not found" }, statusCode: 404, contentType: "application/scim+json")
                : Results.Json(grp, contentType: "application/scim+json");
        })
        .WithName("ScimGetGroup")
        .WithTags("SCIM");

        group.MapPost("/Groups", async (ScimGroup scimGroup, ScimService svc, HttpContext ctx) =>
        {
            var integrationId = await AuthenticateScim(svc, ctx);
            if (integrationId == null) return Results.Json(new ScimError { Status = "401", Detail = "Unauthorized" }, statusCode: 401);

            var groupValidation =
                ValidationHelper.ValidateRequired(scimGroup.DisplayName, "displayName") ??
                ValidationHelper.ValidateMaxLength(scimGroup.DisplayName, "displayName");
            if (groupValidation != null) return groupValidation;

            try
            {
                var created = await svc.CreateGroup(scimGroup, integrationId);
                return Results.Json(created, statusCode: 201, contentType: "application/scim+json");
            }
            catch (InvalidOperationException ex)
            {
                return Results.Json(new ScimError { Status = "409", ScimType = "uniqueness", Detail = ex.Message }, statusCode: 409, contentType: "application/scim+json");
            }
        })
        .WithName("ScimCreateGroup")
        .WithTags("SCIM");

        group.MapPut("/Groups/{id}", async (string id, ScimGroup scimGroup, ScimService svc, HttpContext ctx) =>
        {
            var integrationId = await AuthenticateScim(svc, ctx);
            if (integrationId == null) return Results.Json(new ScimError { Status = "401", Detail = "Unauthorized" }, statusCode: 401);

            var groupReplaceValidation =
                ValidationHelper.ValidateRequired(scimGroup.DisplayName, "displayName") ??
                ValidationHelper.ValidateMaxLength(scimGroup.DisplayName, "displayName");
            if (groupReplaceValidation != null) return groupReplaceValidation;

            var updated = await svc.ReplaceGroup(id, scimGroup, integrationId);
            return updated is null
                ? Results.Json(new ScimError { Status = "404", Detail = "Group not found" }, statusCode: 404, contentType: "application/scim+json")
                : Results.Json(updated, contentType: "application/scim+json");
        })
        .WithName("ScimReplaceGroup")
        .WithTags("SCIM");

        group.MapPatch("/Groups/{id}", async (string id, ScimPatchRequest patch, ScimService svc, HttpContext ctx) =>
        {
            var integrationId = await AuthenticateScim(svc, ctx);
            if (integrationId == null) return Results.Json(new ScimError { Status = "401", Detail = "Unauthorized" }, statusCode: 401);

            var updated = await svc.PatchGroup(id, patch, integrationId);
            return updated is null
                ? Results.Json(new ScimError { Status = "404", Detail = "Group not found" }, statusCode: 404, contentType: "application/scim+json")
                : Results.Json(updated, contentType: "application/scim+json");
        })
        .WithName("ScimPatchGroup")
        .WithTags("SCIM");

        group.MapDelete("/Groups/{id}", async (string id, ScimService svc, HttpContext ctx) =>
        {
            var integrationId = await AuthenticateScim(svc, ctx);
            if (integrationId == null) return Results.Json(new ScimError { Status = "401", Detail = "Unauthorized" }, statusCode: 401);

            var deleted = await svc.DeleteGroup(id, integrationId);
            return deleted
                ? Results.NoContent()
                : Results.Json(new ScimError { Status = "404", Detail = "Group not found" }, statusCode: 404, contentType: "application/scim+json");
        })
        .WithName("ScimDeleteGroup")
        .WithTags("SCIM");

        // Bulk operations
        group.MapPost("/Bulk", async (ScimBulkRequest request, ScimService svc, HttpContext ctx) =>
        {
            var integrationId = await AuthenticateScim(svc, ctx);
            if (integrationId == null) return Results.Json(new ScimError { Status = "401", Detail = "Unauthorized" }, statusCode: 401);

            if (request.Operations == null || request.Operations.Count == 0)
                return Results.Json(new ScimError { Status = "400", Detail = "Operations is required and must not be empty" }, statusCode: 400, contentType: "application/scim+json");
            if (request.Operations.Count > 1000)
                return Results.Json(new ScimError { Status = "400", Detail = "Operations exceeds maximum batch size of 1000" }, statusCode: 400, contentType: "application/scim+json");

            var result = await svc.ProcessBulk(request, integrationId);
            return Results.Json(result, contentType: "application/scim+json");
        })
        .WithName("ScimBulk")
        .WithTags("SCIM");

        return group;
    }

    public static RouteGroupBuilder MapScimManagementEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", async (ScimService svc) =>
        {
            return Results.Ok(await svc.GetAllIntegrations());
        })
        .WithName("GetScimIntegrations")
        .WithTags("SCIM Management");

        group.MapGet("/{id}", async (string id, ScimService svc) =>
        {
            var integration = await svc.GetIntegration(id);
            return integration is null ? Results.NotFound() : Results.Ok(integration);
        })
        .WithName("GetScimIntegration")
        .WithTags("SCIM Management");

        group.MapPost("/", async (ScimIntegration integration, ScimService svc) =>
        {
            var created = await svc.CreateIntegration(integration);
            return Results.Created($"/api/v1/scim-integrations/{created.Id}", created);
        })
        .WithName("CreateScimIntegration")
        .WithTags("SCIM Management");

        group.MapPut("/{id}", async (string id, ScimIntegration integration, ScimService svc) =>
        {
            var updated = await svc.UpdateIntegration(id, integration);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        })
        .WithName("UpdateScimIntegration")
        .WithTags("SCIM Management");

        group.MapDelete("/{id}", async (string id, ScimService svc) =>
        {
            var deleted = await svc.DeleteIntegration(id);
            return deleted ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteScimIntegration")
        .WithTags("SCIM Management");

        group.MapGet("/{id}/logs", (string id, ScimService svc) =>
        {
            return Results.Ok(svc.GetOperationLogs(id));
        })
        .WithName("GetScimOperationLogs")
        .WithTags("SCIM Management");

        group.MapGet("/attribute-mapping/defaults", () =>
        {
            return Results.Ok(ScimService.DefaultAttributeMapping);
        })
        .WithName("GetDefaultScimAttributeMapping")
        .WithTags("SCIM Management");

        return group;
    }

    private static async Task<string> AuthenticateScim(ScimService svc, HttpContext ctx)
    {
        var authHeader = ctx.Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return null;

        var token = authHeader["Bearer ".Length..].Trim();
        return await svc.ValidateBearerToken(token);
    }
}
