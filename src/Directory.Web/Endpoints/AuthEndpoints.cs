using System.Security.Claims;
using Directory.Core;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Directory.Web.Endpoints;

public record LoginRequest(string Username, string Password);

public record LoginResponse(
    string Username,
    string DisplayName,
    string Dn,
    IReadOnlyList<string> Groups,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions);

public record MeResponse(
    string Username,
    string DisplayName,
    string Upn,
    string Dn,
    IReadOnlyList<string> Groups,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions);

public static class AuthEndpoints
{
    private static readonly HashSet<string> AllPermissions =
    [
        "users:read", "users:write", "groups:read", "groups:write",
        "ous:manage", "gpo:manage", "dns:manage", "schema:manage",
        "sites:manage", "config:manage", "backup:manage", "audit:read",
        "certificates:manage", "compliance:manage"
    ];

    private static (List<string> Roles, List<string> Permissions) ResolveRolesAndPermissions(
        IReadOnlyList<string> groupDns, string samAccountName, string domainDn)
    {
        var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Check well-known group memberships
        var domainAdmins = $"CN=Domain Admins,CN=Users,{domainDn}";
        var enterpriseAdmins = $"CN=Enterprise Admins,CN=Users,{domainDn}";
        var accountOperators = $"CN=Account Operators,CN=Builtin,{domainDn}";
        var serverOperators = $"CN=Server Operators,CN=Builtin,{domainDn}";
        var backupOperators = $"CN=Backup Operators,CN=Builtin,{domainDn}";

        foreach (var dn in groupDns)
        {
            if (string.Equals(dn, domainAdmins, StringComparison.OrdinalIgnoreCase))
                roles.Add("DomainAdmin");
            else if (string.Equals(dn, enterpriseAdmins, StringComparison.OrdinalIgnoreCase))
                roles.Add("EnterpriseAdmin");
            else if (string.Equals(dn, accountOperators, StringComparison.OrdinalIgnoreCase))
                roles.Add("AccountOperator");
            else if (string.Equals(dn, serverOperators, StringComparison.OrdinalIgnoreCase))
                roles.Add("ServerOperator");
            else if (string.Equals(dn, backupOperators, StringComparison.OrdinalIgnoreCase))
                roles.Add("BackupOperator");
        }

        // Built-in Administrator account gets DomainAdmin
        if (string.Equals(samAccountName, "Administrator", StringComparison.OrdinalIgnoreCase))
            roles.Add("DomainAdmin");

        // Map roles to permissions
        var permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (roles.Contains("DomainAdmin") || roles.Contains("EnterpriseAdmin"))
        {
            // Full access
            foreach (var p in AllPermissions)
                permissions.Add(p);
        }
        else
        {
            // Default: read-only for authenticated users
            permissions.Add("users:read");
            permissions.Add("groups:read");

            if (roles.Contains("AccountOperator"))
            {
                permissions.Add("users:write");
                permissions.Add("groups:write");
            }

            if (roles.Contains("ServerOperator"))
            {
                permissions.Add("config:manage");
                permissions.Add("dns:manage");
                permissions.Add("sites:manage");
            }

            if (roles.Contains("BackupOperator"))
            {
                permissions.Add("backup:manage");
                permissions.Add("audit:read");
            }
        }

        return (roles.ToList(), permissions.ToList());
    }

    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder group)
    {
        // POST /api/v1/auth/login
        group.MapPost("/login", async (
            LoginRequest request,
            HttpContext ctx,
            IDirectoryStore store,
            IPasswordPolicy passwordPolicy,
            INamingContextService ncService,
            SetupStateService setupState,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("Directory.Web.Endpoints.AuthEndpoints");

            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
                return Results.Problem(statusCode: 400, detail: "Username and password are required.");

            // Require the domain to be provisioned before allowing login
            if (!setupState.IsProvisioned)
                return Results.Problem(statusCode: 503, detail: "Domain is not yet provisioned.");

            var domainDn = ncService.GetDomainNc().Dn;

            // Look up user by sAMAccountName
            DirectoryObject user = null;
            try
            {
                user = await store.GetBySamAccountNameAsync(DirectoryConstants.DefaultTenantId, domainDn, request.Username);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to look up user {Username} — store may not be available", request.Username);
            }

            if (user is null)
                return Results.Problem(statusCode: 401, detail: "Invalid username or password.");

            // Validate password against stored credentials
            bool valid;
            try
            {
                valid = await passwordPolicy.ValidatePasswordAsync(DirectoryConstants.DefaultTenantId, user.DistinguishedName, request.Password);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Password validation failed for {DN}", user.DistinguishedName);
                valid = false;
            }

            if (!valid)
                return Results.Problem(statusCode: 401, detail: "Invalid username or password.");

            // Build claims principal
            var claims = new List<Claim>
            {
                new(ClaimTypes.Name, user.SAMAccountName ?? request.Username),
                new(ClaimTypes.NameIdentifier, user.DistinguishedName),
                new("dn", user.DistinguishedName),
            };

            if (!string.IsNullOrEmpty(user.DisplayName))
                claims.Add(new Claim("displayName", user.DisplayName));

            if (!string.IsNullOrEmpty(user.UserPrincipalName))
                claims.Add(new Claim(ClaimTypes.Upn, user.UserPrincipalName));

            foreach (var groupDn in user.MemberOf)
                claims.Add(new Claim(ClaimTypes.Role, groupDn));

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
                new AuthenticationProperties
                {
                    IsPersistent = true,
                    AllowRefresh = true,
                });

            var (roles, permissions) = ResolveRolesAndPermissions(
                user.MemberOf, user.SAMAccountName, domainDn);

            return Results.Ok(new LoginResponse(
                user.SAMAccountName ?? request.Username,
                user.DisplayName ?? user.SAMAccountName ?? request.Username,
                user.DistinguishedName,
                user.MemberOf,
                roles,
                permissions
            ));
        })
        .WithName("Login")
        .WithTags("Auth")
        .AllowAnonymous();

        // POST /api/v1/auth/logout
        group.MapPost("/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Ok(new { message = "Logged out successfully." });
        })
        .WithName("Logout")
        .WithTags("Auth");

        // GET /api/v1/auth/me
        group.MapGet("/me", (HttpContext ctx, INamingContextService ncService) =>
        {
            if (ctx.User?.Identity?.IsAuthenticated != true)
                return Results.Unauthorized();

            var name = ctx.User.FindFirstValue(ClaimTypes.Name) ?? string.Empty;
            var dn = ctx.User.FindFirstValue("dn") ?? ctx.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            var displayName = ctx.User.FindFirstValue("displayName") ?? name;
            var upn = ctx.User.FindFirstValue(ClaimTypes.Upn) ?? string.Empty;
            var groups = ctx.User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();

            var domainDn = ncService.GetDomainNc().Dn;
            var (roles, permissions) = ResolveRolesAndPermissions(groups, name, domainDn);

            return Results.Ok(new MeResponse(
                name,
                displayName,
                upn,
                dn,
                groups,
                roles,
                permissions
            ));
        })
        .WithName("GetCurrentUser")
        .WithTags("Auth")
        .AllowAnonymous();

        return group;
    }
}
