using Directory.Core;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Core.Telemetry;
using Directory.Web.Models;

namespace Directory.Web.Endpoints;

public static class ComputerEndpoints
{
    public static RouteGroupBuilder MapComputerEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", async (
            string baseDn,
            int? pageSize,
            string continuationToken,
            IDirectoryStore store,
            INamingContextService ncService) =>
        {
            if (baseDn != null)
            {
                var dnValidation = ValidationHelper.ValidateDn(baseDn, "baseDn");
                if (dnValidation != null) return dnValidation;
            }

            var searchBaseDn = baseDn ?? ncService.GetDomainNc().Dn;
            var filter = new EqualityFilterNode("objectClass", "computer");
            var (size, decodedToken) = PaginationHelper.ExtractParams(pageSize, continuationToken, maxPageSize: 1000);

            var result = await store.SearchAsync(
                DirectoryConstants.DefaultTenantId,
                searchBaseDn,
                SearchScope.WholeSubtree,
                filter,
                null,
                pageSize: size,
                continuationToken: decodedToken);

            var items = result.Entries
                .Where(e => !e.IsDeleted)
                .Select(DashboardEndpoints.MapToSummary)
                .ToList();

            return Results.Ok(PaginationHelper.BuildResponse(items, result.ContinuationToken, size, result.TotalEstimate));
        })
        .WithName("ListComputers")
        .WithTags("Computers");

        group.MapPost("/{guid}/reset", async (
            string guid,
            IDirectoryStore store,
            IPasswordPolicy passwordPolicy,
            IAuditService audit,
            HttpContext context) =>
        {
            var obj = await store.GetByGuidAsync(DirectoryConstants.DefaultTenantId, guid);
            if (obj == null || obj.IsDeleted)
                return Results.NotFound();

            if (!obj.ObjectClass.Contains("computer"))
                return Results.Problem(statusCode: 400, detail: "Object is not a computer account");

            // Reset computer password to a random value (the computer will set its own)
            var randomPassword = Guid.NewGuid().ToString("N") + "Aa1!";
            await passwordPolicy.SetPasswordAsync(DirectoryConstants.DefaultTenantId, obj.DistinguishedName, randomPassword);

            obj.PwdLastSet = DateTimeOffset.UtcNow.ToFileTime();
            obj.WhenChanged = DateTimeOffset.UtcNow;
            await store.UpdateAsync(obj);

            DirectoryMetrics.ObjectsModified.Add(1, new KeyValuePair<string, object>("objectClass", "computer"));

            await audit.LogAsync(new AuditEntry
            {
                TenantId = DirectoryConstants.DefaultTenantId,
                Action = "PasswordReset",
                TargetDn = obj.DistinguishedName,
                TargetObjectClass = "computer",
                SourceIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown"
            });

            return Results.NoContent();
        })
        .WithName("ResetComputerPassword")
        .WithTags("Computers");

        return group;
    }
}
