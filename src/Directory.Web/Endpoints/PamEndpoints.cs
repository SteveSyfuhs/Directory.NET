using Directory.Security.Pam;

namespace Directory.Web.Endpoints;

public static class PamEndpoints
{
    public static RouteGroupBuilder MapPamEndpoints(this RouteGroupBuilder group)
    {
        // ── Privileged Roles ─────────────────────────────────────

        group.MapGet("/roles", async (PamService svc, CancellationToken ct) =>
        {
            var roles = await svc.GetPrivilegedRolesAsync(ct);
            return Results.Ok(roles);
        })
        .WithName("ListPrivilegedRoles")
        .WithTags("PAM");

        group.MapPost("/roles", async (PrivilegedRole role, PamService svc, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(role.Name))
                return Results.Problem(statusCode: 400, detail: "Name is required");
            if (string.IsNullOrWhiteSpace(role.GroupDn))
                return Results.Problem(statusCode: 400, detail: "GroupDn is required");

            try
            {
                var created = await svc.CreateRoleAsync(role, ct);
                return Results.Created($"/api/v1/pam/roles/{created.Id}", created);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(statusCode: 409, detail: ex.Message);
            }
        })
        .WithName("CreatePrivilegedRole")
        .WithTags("PAM");

        group.MapPut("/roles/{id}", async (string id, PrivilegedRole role, PamService svc, CancellationToken ct) =>
        {
            try
            {
                var updated = await svc.UpdateRoleAsync(id, role, ct);
                return Results.Ok(updated);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(statusCode: 404, detail: ex.Message);
            }
        })
        .WithName("UpdatePrivilegedRole")
        .WithTags("PAM");

        group.MapDelete("/roles/{id}", async (string id, PamService svc, CancellationToken ct) =>
        {
            var ok = await svc.DeleteRoleAsync(id, ct);
            return ok ? Results.Ok() : Results.NotFound();
        })
        .WithName("DeletePrivilegedRole")
        .WithTags("PAM");

        // ── Activations ──────────────────────────────────────────

        group.MapPost("/activate", async (ActivationRequest req, PamService svc, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.UserDn))
                return Results.Problem(statusCode: 400, detail: "UserDn is required");
            if (string.IsNullOrWhiteSpace(req.RoleId))
                return Results.Problem(statusCode: 400, detail: "RoleId is required");

            try
            {
                var activation = await svc.RequestActivationAsync(req.UserDn, req.RoleId, req.Justification ?? "", req.Hours, ct);
                return Results.Ok(activation);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(statusCode: 400, detail: ex.Message);
            }
        })
        .WithName("RequestActivation")
        .WithTags("PAM");

        group.MapPost("/activations/{id}/approve", async (string id, ApprovalRequest req, PamService svc, CancellationToken ct) =>
        {
            try
            {
                var activation = await svc.ApproveActivationAsync(id, req.ApproverDn, ct);
                return Results.Ok(activation);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(statusCode: 400, detail: ex.Message);
            }
        })
        .WithName("ApproveActivation")
        .WithTags("PAM");

        group.MapPost("/activations/{id}/deny", async (string id, DenyRequest req, PamService svc, CancellationToken ct) =>
        {
            try
            {
                var activation = await svc.DenyActivationAsync(id, req.DenierDn, req.Reason ?? "", ct);
                return Results.Ok(activation);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(statusCode: 400, detail: ex.Message);
            }
        })
        .WithName("DenyActivation")
        .WithTags("PAM");

        group.MapPost("/activations/{id}/deactivate", async (string id, PamService svc, CancellationToken ct) =>
        {
            try
            {
                var activation = await svc.DeactivateAsync(id, ct);
                return Results.Ok(activation);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(statusCode: 400, detail: ex.Message);
            }
        })
        .WithName("DeactivateActivation")
        .WithTags("PAM");

        group.MapGet("/activations", async (PamService svc, CancellationToken ct) =>
        {
            var activations = await svc.GetActivationHistoryAsync(ct);
            return Results.Ok(activations);
        })
        .WithName("ListActivations")
        .WithTags("PAM");

        group.MapGet("/activations/active", async (PamService svc, CancellationToken ct) =>
        {
            var activations = await svc.GetActiveActivationsAsync(ct);
            return Results.Ok(activations);
        })
        .WithName("ListActiveActivations")
        .WithTags("PAM");

        group.MapGet("/activations/pending", async (PamService svc, CancellationToken ct) =>
        {
            var activations = await svc.GetPendingActivationsAsync(ct);
            return Results.Ok(activations);
        })
        .WithName("ListPendingActivations")
        .WithTags("PAM");

        // ── Break-Glass ──────────────────────────────────────────

        group.MapGet("/break-glass", async (PamService svc, CancellationToken ct) =>
        {
            var accounts = await svc.GetBreakGlassAccountsAsync(ct);
            return Results.Ok(accounts);
        })
        .WithName("ListBreakGlassAccounts")
        .WithTags("PAM");

        group.MapPost("/break-glass", async (SealAccountRequest req, PamService svc, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.AccountDn))
                return Results.Problem(statusCode: 400, detail: "AccountDn is required");

            try
            {
                var account = await svc.SealAccountAsync(req.AccountDn, req.Description ?? "", ct);
                return Results.Created($"/api/v1/pam/break-glass/{account.Id}", account);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(statusCode: 409, detail: ex.Message);
            }
        })
        .WithName("SealBreakGlassAccount")
        .WithTags("PAM");

        group.MapPost("/break-glass/{id}/access", async (string id, BreakGlassAccessRequest req, PamService svc, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Reason))
                return Results.Problem(statusCode: 400, detail: "Reason is required");

            try
            {
                var password = await svc.BreakGlassAsync(id, req.Reason, req.AccessedBy ?? "web-console", ct);
                return Results.Ok(new { password });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(statusCode: 400, detail: ex.Message);
            }
        })
        .WithName("BreakGlassAccess")
        .WithTags("PAM");

        group.MapPost("/break-glass/{id}/reseal", async (string id, PamService svc, CancellationToken ct) =>
        {
            try
            {
                var account = await svc.ResealAccountAsync(id, ct);
                return Results.Ok(account);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(statusCode: 400, detail: ex.Message);
            }
        })
        .WithName("ResealBreakGlassAccount")
        .WithTags("PAM");

        return group;
    }
}

public record ActivationRequest(string UserDn, string RoleId, string Justification, int Hours = 4);
public record ApprovalRequest(string ApproverDn);
public record DenyRequest(string DenierDn, string Reason);
public record SealAccountRequest(string AccountDn, string Description);
public record BreakGlassAccessRequest(string Reason, string AccessedBy);
