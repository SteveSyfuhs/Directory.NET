using Directory.Ldap.Auditing;

namespace Directory.Web.Endpoints;

public static class LdapAuditEndpoints
{
    public static RouteGroupBuilder MapLdapAuditEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", (
            string operation,
            string clientIp,
            string boundDn,
            string targetDn,
            DateTimeOffset? from,
            DateTimeOffset? to,
            int? limit,
            LdapAuditService auditService) =>
        {
            var maxResults = Math.Clamp(limit ?? 200, 1, 1000);

            var entries = auditService.Query(
                operation: operation,
                clientIp: clientIp,
                boundDn: boundDn,
                targetDn: targetDn,
                from: from,
                to: to,
                limit: maxResults);

            return Results.Ok(new { Items = entries });
        })
        .WithName("QueryLdapAudit")
        .WithTags("LDAP Audit");

        group.MapGet("/statistics", (LdapAuditService auditService) =>
        {
            var stats = auditService.GetStatistics();
            return Results.Ok(stats);
        })
        .WithName("GetLdapAuditStatistics")
        .WithTags("LDAP Audit");

        group.MapGet("/active-connections", (LdapAuditService auditService) =>
        {
            var connections = auditService.GetActiveConnections();
            return Results.Ok(connections);
        })
        .WithName("GetLdapActiveConnections")
        .WithTags("LDAP Audit");

        return group;
    }
}
