using Directory.Core;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Core.Telemetry;

namespace Directory.Web.Endpoints;

public static class RecycleBinEndpoints
{
    public static RouteGroupBuilder MapRecycleBinEndpoints(this RouteGroupBuilder group)
    {
        // List deleted objects
        group.MapGet("/", async (IDirectoryStore store, INamingContextService ncService, int? limit) =>
        {
            var domainDn = ncService.GetDomainNc().Dn;
            var maxResults = Math.Min(limit ?? 100, 1000);

            // Search for deleted objects (isDeleted=TRUE)
            var filter = new EqualityFilterNode("isDeleted", "TRUE");
            var result = await store.SearchAsync(DirectoryConstants.DefaultTenantId, domainDn,
                SearchScope.WholeSubtree, filter,
                ["distinguishedName", "name", "objectClass", "objectGuid", "whenChanged", "isDeleted", "lastKnownParent"],
                pageSize: maxResults,
                includeDeleted: true);

            return Results.Ok(result.Entries.Select(obj => new
            {
                obj.ObjectGuid,
                Name = obj.Cn ?? obj.GetAttribute("name")?.GetFirstString() ?? "Unknown",
                obj.DistinguishedName,
                ObjectClass = obj.ObjectClass.LastOrDefault() ?? "unknown",
                DeletedTime = obj.DeletedTime ?? obj.WhenChanged,
                LastKnownParent = obj.LastKnownParent ?? "",
                obj.IsRecycled,
            }));
        })
        .WithName("ListDeletedObjects")
        .WithTags("RecycleBin");

        // Restore a deleted object
        group.MapPost("/{guid}/restore", async (IDirectoryStore store, string guid, RestoreRequest request, IAuditService audit, HttpContext context) =>
        {
            var obj = await store.GetByGuidAsync(DirectoryConstants.DefaultTenantId, guid);
            if (obj is null) return Results.Problem(statusCode: 404, detail: "Object not found");
            if (!obj.IsDeleted) return Results.Problem(statusCode: 400, detail: "Object is not deleted");
            if (obj.IsRecycled) return Results.Problem(statusCode: 400, detail: "Recycled objects cannot be restored; they have been permanently stripped of most attributes");

            // Determine target DN
            string targetDn;
            if (!string.IsNullOrEmpty(request?.NewParentDn))
            {
                // Restore to a different container
                var rdn = obj.DistinguishedName.Split(',')[0]; // Keep original RDN
                targetDn = $"{rdn},{request.NewParentDn}";
            }
            else if (!string.IsNullOrEmpty(obj.LastKnownParent))
            {
                // Restore to original location
                var rdnName = obj.Cn ?? obj.GetAttribute("name")?.GetFirstString() ?? "restored";
                targetDn = $"CN={rdnName},{obj.LastKnownParent}";
            }
            else
            {
                return Results.Problem(statusCode: 400, detail: "Cannot determine restore location. Provide newParentDn.");
            }

            // Verify target parent exists
            var parentDn = targetDn[(targetDn.IndexOf(',') + 1)..];
            var parent = await store.GetByDnAsync(DirectoryConstants.DefaultTenantId, parentDn);
            if (parent is null || parent.IsDeleted)
                return Results.Problem(statusCode: 400, detail: $"Target parent container does not exist: {parentDn}");

            var originalDn = obj.DistinguishedName;

            // Restore the object
            obj.IsDeleted = false;
            obj.IsRecycled = false;
            obj.DistinguishedName = targetDn;
            obj.LastKnownParent = null;
            obj.DeletedTime = null;
            obj.WhenChanged = DateTimeOffset.UtcNow;

            await store.UpdateAsync(obj);

            await audit.LogAsync(new AuditEntry
            {
                TenantId = DirectoryConstants.DefaultTenantId,
                Action = "Restore",
                TargetDn = targetDn,
                TargetObjectClass = obj.ObjectClass.LastOrDefault() ?? "unknown",
                SourceIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                Details = new() { ["originalDn"] = originalDn, ["restoredDn"] = targetDn }
            });

            return Results.Ok(new { obj.ObjectGuid, RestoredDn = targetDn });
        })
        .WithName("RestoreDeletedObject")
        .WithTags("RecycleBin");

        // Permanently purge a deleted object
        group.MapDelete("/{guid}", async (IDirectoryStore store, string guid, IAuditService audit, HttpContext context) =>
        {
            var obj = await store.GetByGuidAsync(DirectoryConstants.DefaultTenantId, guid);
            if (obj is null) return Results.NotFound();
            if (!obj.IsDeleted) return Results.Problem(statusCode: 400, detail: "Object is not deleted; delete it first");

            var dn = obj.DistinguishedName;
            var objectClass = obj.ObjectClass.LastOrDefault() ?? "unknown";

            await store.DeleteAsync(DirectoryConstants.DefaultTenantId, obj.DistinguishedName, hardDelete: true);

            DirectoryMetrics.ObjectsDeleted.Add(1, new KeyValuePair<string, object>("objectClass", objectClass));

            await audit.LogAsync(new AuditEntry
            {
                TenantId = DirectoryConstants.DefaultTenantId,
                Action = "Purge",
                TargetDn = dn,
                TargetObjectClass = objectClass,
                SourceIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown"
            });

            return Results.Ok();
        })
        .WithName("PurgeDeletedObject")
        .WithTags("RecycleBin");

        return group;
    }
}

public record RestoreRequest(string NewParentDn = null);
