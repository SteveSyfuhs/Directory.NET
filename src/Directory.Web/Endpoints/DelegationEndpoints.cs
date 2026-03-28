using Directory.Security.Delegation;
using Directory.Web.Models;

namespace Directory.Web.Endpoints;

public static class DelegationEndpoints
{
    public static RouteGroupBuilder MapDelegationEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/roles", async (DelegationService svc, CancellationToken ct) =>
        {
            var roles = await svc.GetAllRolesAsync(ct);
            return Results.Ok(roles);
        })
        .WithName("ListDelegationRoles")
        .WithTags("Delegation");

        group.MapGet("/roles/{id}", async (string id, DelegationService svc, CancellationToken ct) =>
        {
            var role = await svc.GetRoleAsync(id, ct);
            if (role == null)
                return Results.NotFound();

            return Results.Ok(role);
        })
        .WithName("GetDelegationRole")
        .WithTags("Delegation");

        group.MapPost("/roles", async (AdminRole role, DelegationService svc, CancellationToken ct) =>
        {
            var roleValidation =
                ValidationHelper.ValidateRequired(role.Name, "name") ??
                ValidationHelper.ValidateMaxLength(role.Name, "name");
            if (roleValidation != null) return roleValidation;

            var created = await svc.CreateRoleAsync(role, ct);
            return Results.Created($"/api/v1/delegation/roles/{created.Id}", created);
        })
        .WithName("CreateDelegationRole")
        .WithTags("Delegation");

        group.MapPut("/roles/{id}", async (string id, AdminRole role, DelegationService svc, CancellationToken ct) =>
        {
            var updated = await svc.UpdateRoleAsync(id, role, ct);
            if (updated == null)
                return Results.NotFound();

            return Results.Ok(updated);
        })
        .WithName("UpdateDelegationRole")
        .WithTags("Delegation");

        group.MapDelete("/roles/{id}", async (string id, DelegationService svc, CancellationToken ct) =>
        {
            var ok = await svc.DeleteRoleAsync(id, ct);
            if (!ok)
                return Results.Problem(statusCode: 400, detail: "Role not found or is a built-in role");

            return Results.Ok();
        })
        .WithName("DeleteDelegationRole")
        .WithTags("Delegation");

        group.MapGet("/permissions", (DelegationService svc) =>
        {
            return Results.Ok(svc.GetAvailablePermissions());
        })
        .WithName("ListDelegationPermissions")
        .WithTags("Delegation");

        group.MapGet("/effective/{*dn}", async (string dn, DelegationService svc, CancellationToken ct) =>
        {
            var decodedDn = Uri.UnescapeDataString(dn);
            var effective = await svc.GetPermissionsForUserAsync(decodedDn, ct);
            return Results.Ok(effective);
        })
        .WithName("GetEffectiveDelegation")
        .WithTags("Delegation");

        group.MapPost("/roles/{id}/assign", async (string id, AssignMemberRequest request, DelegationService svc, CancellationToken ct) =>
        {
            var assignValidation = ValidationHelper.ValidateDn(request.MemberDn, "memberDn");
            if (assignValidation != null) return assignValidation;

            var role = await svc.AssignMemberAsync(id, request.MemberDn, ct);
            if (role == null)
                return Results.NotFound();

            return Results.Ok(role);
        })
        .WithName("AssignDelegationMember")
        .WithTags("Delegation");

        group.MapPost("/roles/{id}/remove", async (string id, RemoveMemberRequest request, DelegationService svc, CancellationToken ct) =>
        {
            var removeValidation = ValidationHelper.ValidateDn(request.MemberDn, "memberDn");
            if (removeValidation != null) return removeValidation;

            var role = await svc.RemoveMemberAsync(id, request.MemberDn, ct);
            if (role == null)
                return Results.NotFound();

            return Results.Ok(role);
        })
        .WithName("RemoveDelegationMember")
        .WithTags("Delegation");

        return group;
    }
}

public record AssignMemberRequest(string MemberDn);

public record RemoveMemberRequest(string MemberDn);
