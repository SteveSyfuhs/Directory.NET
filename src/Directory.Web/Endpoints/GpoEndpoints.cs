using Directory.Ldap.Handlers;

namespace Directory.Web.Endpoints;

public static class GpoEndpoints
{
    public static RouteGroupBuilder MapGpoEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", async (GroupPolicyService gpService, CancellationToken ct) =>
        {
            var gpos = await gpService.GetAllGposAsync(ct);
            return Results.Ok(gpos);
        })
        .WithName("ListGpos")
        .WithTags("GPO");

        group.MapPost("/", async (CreateGpoRequest request, GroupPolicyService gpService, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.DisplayName))
                return Results.Problem(statusCode: 400, detail: "DisplayName is required");

            var gpo = await gpService.CreateGpoAsync(request.DisplayName, request.PolicySettings, ct);
            return Results.Created($"/api/v1/gpos/{gpo.ObjectGuid}", gpo);
        })
        .WithName("CreateGpo")
        .WithTags("GPO");

        group.MapGet("/{id}", async (string id, GroupPolicyService gpService, CancellationToken ct) =>
        {
            var gpo = await gpService.GetGpoAsync(id, ct);
            if (gpo == null)
                return Results.NotFound();

            return Results.Ok(gpo);
        })
        .WithName("GetGpo")
        .WithTags("GPO");

        group.MapPut("/{id}", async (string id, UpdateGpoRequest request, GroupPolicyService gpService, CancellationToken ct) =>
        {
            var gpo = await gpService.UpdateGpoAsync(id, request.DisplayName, request.Flags, request.PolicySettings, ct);
            if (gpo == null)
                return Results.NotFound();

            return Results.Ok(gpo);
        })
        .WithName("UpdateGpo")
        .WithTags("GPO");

        group.MapDelete("/{id}", async (string id, GroupPolicyService gpService, CancellationToken ct) =>
        {
            var ok = await gpService.DeleteGpoAsync(id, ct);
            if (!ok)
                return Results.NotFound();

            return Results.Ok();
        })
        .WithName("DeleteGpo")
        .WithTags("GPO");

        group.MapPost("/{id}/link", async (string id, LinkGpoRequest request, GroupPolicyService gpService, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.TargetDn))
                return Results.Problem(statusCode: 400, detail: "TargetDn is required");

            var ok = await gpService.LinkGpoAsync(id, request.TargetDn, request.Enforced, ct);
            if (!ok)
                return Results.NotFound();

            return Results.Ok();
        })
        .WithName("LinkGpo")
        .WithTags("GPO");

        group.MapDelete("/{id}/link/{*target}", async (string id, string target, GroupPolicyService gpService, CancellationToken ct) =>
        {
            // The target DN comes URL-encoded via the catch-all route parameter
            var targetDn = Uri.UnescapeDataString(target);
            var ok = await gpService.UnlinkGpoAsync(id, targetDn, ct);
            if (!ok)
                return Results.NotFound();

            return Results.Ok();
        })
        .WithName("UnlinkGpo")
        .WithTags("GPO");

        group.MapGet("/rsop", async (string user, string computer, GroupPolicyService gpService, CancellationToken ct) =>
        {
            if (string.IsNullOrEmpty(user) && string.IsNullOrEmpty(computer))
                return Results.Problem(statusCode: 400, detail: "At least one of 'user' or 'computer' query parameters is required");

            var rsop = await gpService.GetResultantSetOfPolicyAsync(user, computer, ct);
            return Results.Ok(rsop);
        })
        .WithName("GetRsop")
        .WithTags("GPO");

        // GPO Settings endpoints
        group.MapGet("/{id}/settings", async (string id, GroupPolicyService gpService, CancellationToken ct) =>
        {
            var settings = await gpService.GetPolicySettingsAsync(id, ct);
            if (settings == null)
                return Results.NotFound();
            return Results.Ok(settings);
        })
        .WithName("GetGpoSettings")
        .WithTags("GPO");

        group.MapPut("/{id}/settings", async (string id, GpoPolicySettings settings, GroupPolicyService gpService, CancellationToken ct) =>
        {
            var result = await gpService.UpdateGpoAsync(id, null, null, settings, ct);
            if (result == null)
                return Results.NotFound();
            return Results.Ok(result.PolicySettings);
        })
        .WithName("UpdateGpoSettings")
        .WithTags("GPO");

        group.MapGet("/{id}/settings/computer", async (string id, GroupPolicyService gpService, CancellationToken ct) =>
        {
            var settings = await gpService.GetPolicySettingsAsync(id, ct);
            if (settings == null)
                return Results.NotFound();
            // Return computer-relevant settings
            return Results.Ok(new
            {
                settings.PasswordPolicy,
                settings.AccountLockout,
                settings.AuditPolicy,
                settings.UserRights,
                settings.SecurityOptions,
                settings.SoftwareRestriction,
                settings.StartupScripts,
                settings.ShutdownScripts,
            });
        })
        .WithName("GetGpoComputerSettings")
        .WithTags("GPO");

        group.MapGet("/{id}/settings/user", async (string id, GroupPolicyService gpService, CancellationToken ct) =>
        {
            var settings = await gpService.GetPolicySettingsAsync(id, ct);
            if (settings == null)
                return Results.NotFound();
            // Return user-relevant settings
            return Results.Ok(new
            {
                settings.LogonScripts,
                settings.LogoffScripts,
                settings.DriveMappings,
            });
        })
        .WithName("GetGpoUserSettings")
        .WithTags("GPO");

        // Security Filtering
        group.MapGet("/{id}/security-filtering", async (string id, GroupPolicyService gpService, CancellationToken ct) =>
        {
            var entries = await gpService.GetSecurityFilteringAsync(id, ct);
            return Results.Ok(entries);
        })
        .WithName("GetSecurityFiltering")
        .WithTags("GPO");

        group.MapPost("/{id}/security-filtering", async (string id, AddSecurityFilterRequest request, GroupPolicyService gpService, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.PrincipalDn))
                return Results.Problem(statusCode: 400, detail: "PrincipalDn is required");

            var ok = await gpService.AddSecurityFilterAsync(id, request.PrincipalDn, ct);
            if (!ok)
                return Results.NotFound();
            return Results.Ok();
        })
        .WithName("AddSecurityFilter")
        .WithTags("GPO");

        group.MapDelete("/{id}/security-filtering/{sid}", async (string id, string sid, GroupPolicyService gpService, CancellationToken ct) =>
        {
            var decodedSid = Uri.UnescapeDataString(sid);
            var ok = await gpService.RemoveSecurityFilterAsync(id, decodedSid, ct);
            if (!ok)
                return Results.NotFound();
            return Results.Ok();
        })
        .WithName("RemoveSecurityFilter")
        .WithTags("GPO");

        // Backup/Restore
        group.MapPost("/{id}/backup", async (string id, CreateBackupRequest request, GroupPolicyService gpService, CancellationToken ct) =>
        {
            try
            {
                var backup = await gpService.CreateBackupAsync(id, request.Description, ct);
                return Results.Created($"/api/v1/gpos/backups/{backup.BackupId}", backup);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(statusCode: 404, detail: ex.Message);
            }
        })
        .WithName("CreateGpoBackup")
        .WithTags("GPO");

        group.MapGet("/backups", async (GroupPolicyService gpService, CancellationToken ct) =>
        {
            var backups = await gpService.ListBackupsAsync(ct);
            return Results.Ok(backups);
        })
        .WithName("ListGpoBackups")
        .WithTags("GPO");

        group.MapPost("/backups/{backupId}/restore", async (string backupId, GroupPolicyService gpService, CancellationToken ct) =>
        {
            var result = await gpService.RestoreBackupAsync(backupId, ct);
            if (result == null)
                return Results.NotFound();
            return Results.Ok(result);
        })
        .WithName("RestoreGpoBackup")
        .WithTags("GPO");

        // WMI Filters
        group.MapGet("/wmi-filters", async (GroupPolicyService gpService, CancellationToken ct) =>
        {
            var filters = await gpService.ListWmiFiltersAsync(ct);
            return Results.Ok(filters);
        })
        .WithName("ListWmiFilters")
        .WithTags("GPO");

        group.MapPost("/wmi-filters", async (CreateWmiFilterRequest request, GroupPolicyService gpService, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.Problem(statusCode: 400, detail: "Name is required");
            if (string.IsNullOrWhiteSpace(request.Query))
                return Results.Problem(statusCode: 400, detail: "Query is required");

            var filter = await gpService.CreateWmiFilterAsync(request.Name, request.Description ?? "", request.Query, ct);
            return Results.Created($"/api/v1/gpos/wmi-filters/{filter.Id}", filter);
        })
        .WithName("CreateWmiFilter")
        .WithTags("GPO");

        group.MapPut("/wmi-filters/{filterId}", async (string filterId, UpdateWmiFilterRequest request, GroupPolicyService gpService, CancellationToken ct) =>
        {
            var result = await gpService.UpdateWmiFilterAsync(filterId, request.Name, request.Description, request.Query, ct);
            if (result == null)
                return Results.NotFound();
            return Results.Ok(result);
        })
        .WithName("UpdateWmiFilter")
        .WithTags("GPO");

        group.MapDelete("/wmi-filters/{filterId}", async (string filterId, GroupPolicyService gpService, CancellationToken ct) =>
        {
            var ok = await gpService.DeleteWmiFilterAsync(filterId, ct);
            if (!ok)
                return Results.NotFound();
            return Results.Ok();
        })
        .WithName("DeleteWmiFilter")
        .WithTags("GPO");

        group.MapPut("/{id}/wmi-filter", async (string id, SetWmiFilterRequest request, GroupPolicyService gpService, CancellationToken ct) =>
        {
            var ok = await gpService.SetGpoWmiFilterAsync(id, request.FilterId, ct);
            if (!ok)
                return Results.NotFound();
            return Results.Ok();
        })
        .WithName("SetGpoWmiFilter")
        .WithTags("GPO");

        return group;
    }
}

public record CreateGpoRequest(string DisplayName, GpoPolicySettings PolicySettings = null);

public record UpdateGpoRequest(string DisplayName = null, int? Flags = null, GpoPolicySettings PolicySettings = null);

public record LinkGpoRequest(string TargetDn, bool Enforced = false);

public record AddSecurityFilterRequest(string PrincipalDn);

public record CreateBackupRequest(string Description = null);

public record CreateWmiFilterRequest(string Name, string Description, string Query);

public record UpdateWmiFilterRequest(string Name = null, string Description = null, string Query = null);

public record SetWmiFilterRequest(string FilterId = null);
