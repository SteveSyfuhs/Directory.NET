using System.Text;
using Directory.Security.OAuth;

namespace Directory.Web.Endpoints;

public static class OAuthEndpoints
{
    /// <summary>
    /// Maps OAuth2/OIDC protocol endpoints (authorize, token, userinfo, revoke, end-session).
    /// </summary>
    public static RouteGroupBuilder MapOAuthProtocolEndpoints(this RouteGroupBuilder group)
    {
        // GET /api/v1/oauth/authorize — Authorization endpoint
        group.MapGet("/authorize", async (
            HttpContext ctx,
            OAuthService oauthService,
            string response_type,
            string client_id,
            string redirect_uri,
            string scope,
            string state,
            string code_challenge,
            string code_challenge_method,
            string nonce) =>
        {
            if (response_type != "code")
                return Results.BadRequest(new { error = "unsupported_response_type" });

            if (string.IsNullOrEmpty(client_id) || string.IsNullOrEmpty(redirect_uri))
                return Results.BadRequest(new { error = "invalid_request", error_description = "client_id and redirect_uri are required" });

            var client = oauthService.GetClient(client_id);
            if (client is null || !client.IsEnabled)
                return Results.BadRequest(new { error = "invalid_client" });

            if (!client.RedirectUris.Contains(redirect_uri))
                return Results.BadRequest(new { error = "invalid_redirect_uri" });

            if (client.RequirePkce && string.IsNullOrEmpty(code_challenge))
                return Results.BadRequest(new { error = "invalid_request", error_description = "PKCE code_challenge is required" });

            var scopes = (scope ?? "openid").Split(' ')
                .Where(s => client.AllowedScopes.Contains(s))
                .ToList();

            // Check for Basic auth header or query params for user credentials
            // In a real implementation, this would render a login/consent page.
            // For API-driven flow, accept credentials via Authorization header.
            string username = null;
            string password = null;

            var authHeader = ctx.Request.Headers.Authorization.FirstOrDefault();
            if (authHeader?.StartsWith("Basic ") == true)
            {
                var decoded = Encoding.UTF8.GetString(
                    Convert.FromBase64String(authHeader["Basic ".Length..]));
                var parts = decoded.Split(':', 2);
                if (parts.Length == 2)
                {
                    username = parts[0];
                    password = parts[1];
                }
            }

            // Also check query params for username/password (for programmatic use)
            username ??= ctx.Request.Query["username"].FirstOrDefault();
            password ??= ctx.Request.Query["password"].FirstOrDefault();

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                // Return a minimal HTML login form
                var loginHtml = GenerateLoginForm(client_id, redirect_uri, scope ?? "openid",
                    state, code_challenge, code_challenge_method, nonce);
                return Results.Content(loginHtml, "text/html");
            }

            var user = await oauthService.AuthenticateUser(username, password);
            if (user is null)
            {
                var loginHtml = GenerateLoginForm(client_id, redirect_uri, scope ?? "openid",
                    state, code_challenge, code_challenge_method, nonce, error: "Invalid credentials");
                return Results.Content(loginHtml, "text/html");
            }

            var code = oauthService.CreateAuthorizationCode(
                client_id, user.DistinguishedName, redirect_uri,
                scopes, code_challenge, code_challenge_method ?? "S256", nonce);

            var redirectUrl = $"{redirect_uri}?code={Uri.EscapeDataString(code)}";
            if (!string.IsNullOrEmpty(state))
                redirectUrl += $"&state={Uri.EscapeDataString(state)}";

            return Results.Redirect(redirectUrl);
        })
        .WithName("OAuthAuthorize")
        .WithTags("OAuth");

        // POST /api/v1/oauth/authorize — Handle login form submission
        group.MapPost("/authorize", async (
            HttpContext ctx,
            OAuthService oauthService) =>
        {
            var form = await ctx.Request.ReadFormAsync();
            var clientId = form["client_id"].FirstOrDefault() ?? "";
            var redirectUri = form["redirect_uri"].FirstOrDefault() ?? "";
            var scope = form["scope"].FirstOrDefault() ?? "openid";
            var state = form["state"].FirstOrDefault();
            var codeChallenge = form["code_challenge"].FirstOrDefault();
            var codeChallengeMethod = form["code_challenge_method"].FirstOrDefault();
            var nonce = form["nonce"].FirstOrDefault();
            var username = form["username"].FirstOrDefault() ?? "";
            var password = form["password"].FirstOrDefault() ?? "";

            var client = oauthService.GetClient(clientId);
            if (client is null || !client.IsEnabled)
                return Results.BadRequest(new { error = "invalid_client" });

            if (!client.RedirectUris.Contains(redirectUri))
                return Results.BadRequest(new { error = "invalid_redirect_uri" });

            var user = await oauthService.AuthenticateUser(username, password);
            if (user is null)
            {
                var loginHtml = GenerateLoginForm(clientId, redirectUri, scope,
                    state, codeChallenge, codeChallengeMethod, nonce, error: "Invalid credentials");
                return Results.Content(loginHtml, "text/html");
            }

            var scopes = scope.Split(' ')
                .Where(s => client.AllowedScopes.Contains(s))
                .ToList();

            var code = oauthService.CreateAuthorizationCode(
                clientId, user.DistinguishedName, redirectUri,
                scopes, codeChallenge, codeChallengeMethod ?? "S256", nonce);

            var redirectUrl = $"{redirectUri}?code={Uri.EscapeDataString(code)}";
            if (!string.IsNullOrEmpty(state))
                redirectUrl += $"&state={Uri.EscapeDataString(state)}";

            return Results.Redirect(redirectUrl);
        })
        .WithName("OAuthAuthorizePost")
        .WithTags("OAuth");

        // POST /api/v1/oauth/token — Token endpoint
        group.MapPost("/token", async (
            HttpContext ctx,
            OAuthService oauthService) =>
        {
            var issuer = $"{ctx.Request.Scheme}://{ctx.Request.Host}";

            // Parse form-encoded body
            var form = await ctx.Request.ReadFormAsync();
            var grantType = form["grant_type"].FirstOrDefault();

            // Extract client credentials from Basic auth or form body
            string clientId = form["client_id"].FirstOrDefault();
            string clientSecret = form["client_secret"].FirstOrDefault();

            var authHeader = ctx.Request.Headers.Authorization.FirstOrDefault();
            if (authHeader?.StartsWith("Basic ") == true)
            {
                var decoded = Encoding.UTF8.GetString(
                    Convert.FromBase64String(authHeader["Basic ".Length..]));
                var parts = decoded.Split(':', 2);
                if (parts.Length == 2)
                {
                    clientId = parts[0];
                    clientSecret = parts[1];
                }
            }

            if (string.IsNullOrEmpty(clientId))
                return Results.Json(new { error = "invalid_client" }, statusCode: 401);

            TokenResponse response = grantType switch
            {
                "authorization_code" => await oauthService.ExchangeCodeForTokens(
                    form["code"].FirstOrDefault() ?? "",
                    clientId,
                    form["redirect_uri"].FirstOrDefault() ?? "",
                    form["code_verifier"].FirstOrDefault(),
                    issuer),

                "client_credentials" => await oauthService.HandleClientCredentials(
                    clientId,
                    clientSecret ?? "",
                    form["scope"].FirstOrDefault(),
                    issuer),

                "refresh_token" => await oauthService.HandleRefreshToken(
                    form["refresh_token"].FirstOrDefault() ?? "",
                    clientId,
                    issuer),

                _ => null,
            };

            if (response is null)
                return Results.Json(new { error = "invalid_grant" }, statusCode: 400);

            return Results.Json(new
            {
                access_token = response.AccessToken,
                token_type = response.TokenType,
                expires_in = response.ExpiresIn,
                refresh_token = response.RefreshToken,
                id_token = response.IdToken,
                scope = response.Scope,
            });
        })
        .WithName("OAuthToken")
        .WithTags("OAuth");

        // GET /api/v1/oauth/userinfo — UserInfo endpoint
        group.MapGet("/userinfo", async (HttpContext ctx, OAuthService oauthService) =>
        {
            var authHeader = ctx.Request.Headers.Authorization.FirstOrDefault();
            if (authHeader is null || !authHeader.StartsWith("Bearer "))
                return Results.Json(new { error = "invalid_token" }, statusCode: 401);

            var token = authHeader["Bearer ".Length..];
            var userInfo = await oauthService.GetUserInfo(token);
            if (userInfo is null)
                return Results.Json(new { error = "invalid_token" }, statusCode: 401);

            return Results.Json(userInfo);
        })
        .WithName("OAuthUserInfo")
        .WithTags("OAuth");

        // POST /api/v1/oauth/revoke — Token revocation
        group.MapPost("/revoke", async (HttpContext ctx, OAuthService oauthService) =>
        {
            var form = await ctx.Request.ReadFormAsync();
            var token = form["token"].FirstOrDefault();
            if (string.IsNullOrEmpty(token))
                return Results.BadRequest(new { error = "invalid_request" });

            oauthService.RevokeToken(token);
            return Results.Ok();
        })
        .WithName("OAuthRevoke")
        .WithTags("OAuth");

        // GET /api/v1/oauth/end-session — Logout endpoint
        group.MapGet("/end-session", (
            string post_logout_redirect_uri,
            string id_token_hint,
            string state) =>
        {
            // In a real implementation, this would clear the session.
            if (!string.IsNullOrEmpty(post_logout_redirect_uri))
            {
                var redirectUrl = post_logout_redirect_uri;
                if (!string.IsNullOrEmpty(state))
                    redirectUrl += $"?state={Uri.EscapeDataString(state)}";
                return Results.Redirect(redirectUrl);
            }

            return Results.Ok(new { message = "Logged out successfully" });
        })
        .WithName("OAuthEndSession")
        .WithTags("OAuth");

        return group;
    }

    /// <summary>
    /// Maps OIDC well-known discovery endpoints.
    /// </summary>
    public static IEndpointRouteBuilder MapOidcDiscoveryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/.well-known/openid-configuration", (HttpContext ctx, OAuthService oauthService) =>
        {
            var issuer = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
            return Results.Json(oauthService.GetDiscoveryDocument(issuer));
        })
        .WithName("OidcDiscovery")
        .WithTags("OAuth")
        .ExcludeFromDescription();

        app.MapGet("/.well-known/jwks.json", (OAuthService oauthService) =>
        {
            return Results.Json(oauthService.GetJwks());
        })
        .WithName("OidcJwks")
        .WithTags("OAuth")
        .ExcludeFromDescription();

        return app;
    }

    /// <summary>
    /// Maps OAuth client management (CRUD) endpoints.
    /// </summary>
    public static RouteGroupBuilder MapOAuthClientEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", (OAuthService oauthService) =>
        {
            var clients = oauthService.GetAllClients().Select(c => new
            {
                c.ClientId,
                c.ClientName,
                c.RedirectUris,
                c.AllowedScopes,
                c.AllowedGrantTypes,
                c.LogoUri,
                c.AccessTokenLifetimeMinutes,
                c.RefreshTokenLifetimeDays,
                c.RequirePkce,
                c.IsEnabled,
                c.CreatedAt,
            });
            return Results.Ok(clients);
        })
        .WithName("GetOAuthClients")
        .WithTags("OAuthClients");

        group.MapPost("/", (OAuthClient template, OAuthService oauthService) =>
        {
            var (client, plainSecret) = oauthService.CreateClient(template);
            return Results.Created($"/api/v1/oauth/clients/{client.ClientId}", new
            {
                client.ClientId,
                client.ClientName,
                clientSecret = plainSecret, // Shown once
                client.RedirectUris,
                client.AllowedScopes,
                client.AllowedGrantTypes,
                client.LogoUri,
                client.AccessTokenLifetimeMinutes,
                client.RefreshTokenLifetimeDays,
                client.RequirePkce,
                client.IsEnabled,
                client.CreatedAt,
            });
        })
        .WithName("CreateOAuthClient")
        .WithTags("OAuthClients");

        group.MapGet("/{id}", (string id, OAuthService oauthService) =>
        {
            var client = oauthService.GetClient(id);
            if (client is null) return Results.NotFound();
            return Results.Ok(new
            {
                client.ClientId,
                client.ClientName,
                client.RedirectUris,
                client.AllowedScopes,
                client.AllowedGrantTypes,
                client.LogoUri,
                client.AccessTokenLifetimeMinutes,
                client.RefreshTokenLifetimeDays,
                client.RequirePkce,
                client.IsEnabled,
                client.CreatedAt,
            });
        })
        .WithName("GetOAuthClient")
        .WithTags("OAuthClients");

        group.MapPut("/{id}", (string id, OAuthClient updates, OAuthService oauthService) =>
        {
            var updated = oauthService.UpdateClient(id, updates);
            if (updated is null) return Results.NotFound();
            return Results.Ok(new
            {
                updated.ClientId,
                updated.ClientName,
                updated.RedirectUris,
                updated.AllowedScopes,
                updated.AllowedGrantTypes,
                updated.LogoUri,
                updated.AccessTokenLifetimeMinutes,
                updated.RefreshTokenLifetimeDays,
                updated.RequirePkce,
                updated.IsEnabled,
                updated.CreatedAt,
            });
        })
        .WithName("UpdateOAuthClient")
        .WithTags("OAuthClients");

        group.MapDelete("/{id}", (string id, OAuthService oauthService) =>
        {
            return oauthService.DeleteClient(id) ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteOAuthClient")
        .WithTags("OAuthClients");

        group.MapPost("/{id}/secret", (string id, OAuthService oauthService) =>
        {
            try
            {
                var newSecret = oauthService.RegenerateClientSecret(id);
                return Results.Ok(new { clientSecret = newSecret });
            }
            catch (InvalidOperationException)
            {
                return Results.NotFound();
            }
        })
        .WithName("RegenerateOAuthClientSecret")
        .WithTags("OAuthClients");

        return group;
    }

    private static string GenerateLoginForm(
        string clientId, string redirectUri, string scope,
        string state, string codeChallenge, string codeChallengeMethod,
        string nonce, string error = null)
    {
        var errorHtml = error is not null
            ? $"<div style=\"color:#dc3545;background:#fee;border:1px solid #fcc;padding:0.5rem;border-radius:4px;margin-bottom:1rem\">{error}</div>"
            : "";

        return $@"<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <title>Sign In — Directory.NET</title>
    <style>
        body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; background: #f5f5f5; display: flex; justify-content: center; align-items: center; min-height: 100vh; margin: 0; }}
        .login-card {{ background: #fff; border-radius: 8px; box-shadow: 0 2px 12px rgba(0,0,0,0.1); padding: 2rem; width: 380px; }}
        h2 {{ margin: 0 0 1.5rem; font-size: 1.25rem; color: #333; }}
        label {{ display: block; font-size: 0.875rem; font-weight: 600; color: #555; margin-bottom: 0.25rem; }}
        input[type=text], input[type=password] {{ width: 100%; padding: 0.5rem; border: 1px solid #ddd; border-radius: 4px; font-size: 0.875rem; box-sizing: border-box; margin-bottom: 1rem; }}
        button {{ width: 100%; padding: 0.625rem; background: #6366f1; color: #fff; border: none; border-radius: 4px; font-size: 0.875rem; font-weight: 600; cursor: pointer; }}
        button:hover {{ background: #4f46e5; }}
    </style>
</head>
<body>
    <div class=""login-card"">
        <h2>Sign In</h2>
        {errorHtml}
        <form method=""POST"" action=""/api/v1/oauth/authorize"">
            <input type=""hidden"" name=""client_id"" value=""{clientId}"" />
            <input type=""hidden"" name=""redirect_uri"" value=""{redirectUri}"" />
            <input type=""hidden"" name=""scope"" value=""{scope}"" />
            <input type=""hidden"" name=""state"" value=""{state ?? ""}"" />
            <input type=""hidden"" name=""code_challenge"" value=""{codeChallenge ?? ""}"" />
            <input type=""hidden"" name=""code_challenge_method"" value=""{codeChallengeMethod ?? ""}"" />
            <input type=""hidden"" name=""nonce"" value=""{nonce ?? ""}"" />
            <label>Username</label>
            <input type=""text"" name=""username"" autocomplete=""username"" required />
            <label>Password</label>
            <input type=""password"" name=""password"" autocomplete=""current-password"" required />
            <button type=""submit"">Sign In</button>
        </form>
    </div>
</body>
</html>";
    }
}
