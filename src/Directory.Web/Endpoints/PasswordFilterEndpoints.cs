using Directory.Security.PasswordFilters;
using Microsoft.AspNetCore.Mvc;

namespace Directory.Web.Endpoints;

public static class PasswordFilterEndpoints
{
    public static RouteGroupBuilder MapPasswordFilterEndpoints(this RouteGroupBuilder group)
    {
        // GET /api/v1/password-filters — list all registered filters
        group.MapGet("/", (PasswordFilterService filterService) =>
        {
            var filters = filterService.GetFilters();
            return Results.Ok(filters.Select(f => new
            {
                f.Name,
                f.Description,
                f.IsEnabled,
                f.Order,
            }));
        })
        .WithName("ListPasswordFilters")
        .WithTags("PasswordFilters");

        // PUT /api/v1/password-filters/{name}/enable — enable a filter
        group.MapPut("/{name}/enable", (string name, PasswordFilterService filterService) =>
        {
            var success = filterService.SetFilterEnabled(name, true);
            if (!success)
                return Results.NotFound(new { Detail = $"Password filter '{name}' not found" });

            return Results.Ok(new { Name = name, IsEnabled = true });
        })
        .WithName("EnablePasswordFilter")
        .WithTags("PasswordFilters");

        // PUT /api/v1/password-filters/{name}/disable — disable a filter
        group.MapPut("/{name}/disable", (string name, PasswordFilterService filterService) =>
        {
            var success = filterService.SetFilterEnabled(name, false);
            if (!success)
                return Results.NotFound(new { Detail = $"Password filter '{name}' not found" });

            return Results.Ok(new { Name = name, IsEnabled = false });
        })
        .WithName("DisablePasswordFilter")
        .WithTags("PasswordFilters");

        // POST /api/v1/password-filters/test — test a password against all filters
        group.MapPost("/test", async ([FromBody] TestPasswordRequest request, PasswordFilterService filterService) =>
        {
            if (string.IsNullOrEmpty(request.Password))
                return Results.BadRequest(new { Detail = "Password is required" });

            var result = await filterService.ValidatePasswordAsync(
                request.Dn ?? "CN=TestUser,OU=Users",
                request.Password,
                request.OldPassword);

            return Results.Ok(new
            {
                result.IsValid,
                result.Message,
                FilterResults = result.FilterResults.Select(r => new
                {
                    r.FilterName,
                    r.IsValid,
                    r.Message,
                }),
            });
        })
        .WithName("TestPasswordFilters")
        .WithTags("PasswordFilters");

        return group;
    }
}

public record TestPasswordRequest
{
    public string Dn { get; init; }
    public string Password { get; init; } = string.Empty;
    public string OldPassword { get; init; }
}
