using Directory.Core.Interfaces;
using Directory.Replication;

namespace Directory.Web.Endpoints;

public static class SchemaReplicationEndpoints
{
    public static RouteGroupBuilder MapSchemaReplicationEndpoints(this RouteGroupBuilder group)
    {
        // GET /api/v1/schema/replication/status — current replication status
        group.MapGet("/status", async (SchemaReplicationService replicationService, ISchemaService schema, CancellationToken ct) =>
        {
            var status = await replicationService.GetStatusAsync(ct);

            return Results.Ok(new
            {
                status.CurrentSchemaVersion,
                SchemaUsn = schema.SchemaUsn,
                LastSyncTime = status.LastSyncTime,
                status.OriginServer,
                status.PendingChanges,
                AttributeCount = schema.GetAllAttributes().Count,
                ClassCount = schema.GetAllObjectClasses().Count,
                Health = status.PendingChanges == 0 ? "Healthy" : status.PendingChanges < 5 ? "Warning" : "Error",
            });
        })
        .WithName("SchemaReplicationStatus")
        .WithTags("Schema", "Replication");

        // GET /api/v1/schema/replication/history — recent schema changes
        group.MapGet("/history", async (SchemaReplicationService replicationService, CancellationToken ct, int? count) =>
        {
            var changes = await replicationService.GetRecentChangesAsync(count ?? 50, ct);

            return Results.Ok(changes.Select(c => new
            {
                c.Id,
                c.ChangeType,
                c.ObjectName,
                c.SchemaVersion,
                c.Timestamp,
                c.OriginServer,
                c.Changes,
            }));
        })
        .WithName("SchemaReplicationHistory")
        .WithTags("Schema", "Replication");

        // POST /api/v1/schema/replication/sync — force schema sync from store
        group.MapPost("/sync", async (SchemaReplicationService replicationService, CancellationToken ct) =>
        {
            await replicationService.ForceSyncAsync(ct);

            return Results.Ok(new
            {
                Message = "Schema sync completed successfully",
                Timestamp = DateTimeOffset.UtcNow,
            });
        })
        .WithName("ForceSchemaSync")
        .WithTags("Schema", "Replication");

        return group;
    }
}
