using Directory.Core;
using Directory.Core.Interfaces;
using Directory.Web.Models;

namespace Directory.Web.Endpoints;

public static class AuditEndpoints
{
    public static RouteGroupBuilder MapAuditEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", async (
            string action,
            string targetDn,
            DateTimeOffset? from,
            DateTimeOffset? to,
            int? pageSize,
            string continuationToken,
            IAuditService audit) =>
        {
            var (size, decodedToken) = PaginationHelper.ExtractParams(pageSize, continuationToken);
            var (items, nextToken) = await audit.QueryAsync(
                DirectoryConstants.DefaultTenantId, action, targetDn, from, to, size, decodedToken);

            return Results.Ok(PaginationHelper.BuildResponse(items, nextToken, size));
        })
        .WithName("QueryAuditLog")
        .WithTags("Audit");

        group.MapGet("/{id}", async (string id, IAuditService audit) =>
        {
            var entry = await audit.GetByIdAsync(DirectoryConstants.DefaultTenantId, id);
            if (entry == null)
                return Results.NotFound();

            return Results.Ok(entry);
        })
        .WithName("GetAuditEntry")
        .WithTags("Audit");

        return group;
    }
}
