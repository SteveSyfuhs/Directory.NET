using Directory.Ldap.Proxy;

namespace Directory.Web.Endpoints;

public static class LdapProxyEndpoints
{
    public static RouteGroupBuilder MapLdapProxyEndpoints(this RouteGroupBuilder group)
    {
        // Backend management
        group.MapGet("/backends", (LdapProxyService svc) =>
        {
            return Results.Ok(svc.GetBackends());
        })
        .WithName("GetProxyBackends")
        .WithTags("LdapProxy");

        group.MapPost("/backends", (LdapProxyBackend backend, LdapProxyService svc) =>
        {
            var created = svc.AddBackend(backend);
            return Results.Created($"/api/v1/ldap-proxy/backends/{created.Id}", created);
        })
        .WithName("CreateProxyBackend")
        .WithTags("LdapProxy");

        group.MapPut("/backends/{id}", (string id, LdapProxyBackend backend, LdapProxyService svc) =>
        {
            var updated = svc.UpdateBackend(id, backend);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        })
        .WithName("UpdateProxyBackend")
        .WithTags("LdapProxy");

        group.MapDelete("/backends/{id}", (string id, LdapProxyService svc) =>
        {
            return svc.DeleteBackend(id) ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteProxyBackend")
        .WithTags("LdapProxy");

        group.MapPost("/backends/{id}/test", async (string id, LdapProxyService svc) =>
        {
            var result = await svc.TestBackend(id);
            return Results.Ok(result);
        })
        .WithName("TestProxyBackend")
        .WithTags("LdapProxy");

        // Route management
        group.MapGet("/routes", (LdapProxyService svc) =>
        {
            return Results.Ok(svc.GetRoutes());
        })
        .WithName("GetProxyRoutes")
        .WithTags("LdapProxy");

        group.MapPost("/routes", (ProxyRoute route, LdapProxyService svc) =>
        {
            var created = svc.AddRoute(route);
            return Results.Created($"/api/v1/ldap-proxy/routes/{created.Id}", created);
        })
        .WithName("CreateProxyRoute")
        .WithTags("LdapProxy");

        group.MapPut("/routes/{id}", (string id, ProxyRoute route, LdapProxyService svc) =>
        {
            var updated = svc.UpdateRoute(id, route);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        })
        .WithName("UpdateProxyRoute")
        .WithTags("LdapProxy");

        group.MapDelete("/routes/{id}", (string id, LdapProxyService svc) =>
        {
            return svc.DeleteRoute(id) ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteProxyRoute")
        .WithTags("LdapProxy");

        return group;
    }
}
