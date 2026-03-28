using System.Text;
using Directory.Security.Saml;

namespace Directory.Web.Endpoints;

public static class SamlEndpoints
{
    /// <summary>
    /// Maps SAML 2.0 IdP protocol endpoints (metadata, SSO, SLO).
    /// </summary>
    public static RouteGroupBuilder MapSamlProtocolEndpoints(this RouteGroupBuilder group)
    {
        // GET /api/v1/saml/metadata — IdP Metadata XML
        group.MapGet("/metadata", (HttpContext ctx, SamlService samlService) =>
        {
            var baseUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
            var entityId = $"{baseUrl}/api/v1/saml";
            var ssoUrl = $"{baseUrl}/api/v1/saml/sso";
            var sloUrl = $"{baseUrl}/api/v1/saml/slo";

            var metadata = samlService.GenerateIdPMetadata(entityId, ssoUrl, sloUrl);
            return Results.Content(metadata, "application/xml", Encoding.UTF8);
        })
        .WithName("SamlMetadata")
        .WithTags("SAML");

        // GET /api/v1/saml/sso — SSO via HTTP-Redirect binding
        group.MapGet("/sso", async (
            HttpContext ctx,
            SamlService samlService,
            string SAMLRequest,
            string RelayState) =>
        {
            if (string.IsNullOrEmpty(SAMLRequest))
            {
                return Results.BadRequest(new { error = "SAMLRequest parameter is required" });
            }

            var (issuer, requestId, acsUrl) = samlService.ParseAuthnRequest(SAMLRequest);

            // Find the SP by issuer
            SamlServiceProvider sp = null;
            if (issuer is not null)
                sp = samlService.GetServiceProviderByEntityId(issuer);

            if (sp is null || !sp.IsEnabled)
            {
                return Results.BadRequest(new { error = "Unknown or disabled service provider", issuer });
            }

            // Use ACS URL from request or SP config
            var destinationAcs = acsUrl ?? sp.AssertionConsumerServiceUrl;

            // Check for user credentials (Basic auth header)
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

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                // Return a login form
                var loginHtml = GenerateSamlLoginForm(sp.EntityId, requestId, RelayState, destinationAcs);
                return Results.Content(loginHtml, "text/html");
            }

            var user = await samlService.AuthenticateUser(username, password);
            if (user is null)
            {
                var loginHtml = GenerateSamlLoginForm(sp.EntityId, requestId, RelayState, destinationAcs, error: "Invalid credentials");
                return Results.Content(loginHtml, "text/html");
            }

            var baseUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
            var idpEntityId = $"{baseUrl}/api/v1/saml";

            var samlResponse = samlService.GenerateSamlResponse(user, sp, idpEntityId, requestId);
            var encodedResponse = Convert.ToBase64String(Encoding.UTF8.GetBytes(samlResponse));

            // Return auto-posting HTML form
            var postHtml = GenerateSamlPostForm(destinationAcs, encodedResponse, RelayState);
            return Results.Content(postHtml, "text/html");
        })
        .WithName("SamlSsoRedirect")
        .WithTags("SAML");

        // POST /api/v1/saml/sso — SSO via HTTP-POST binding
        group.MapPost("/sso", async (
            HttpContext ctx,
            SamlService samlService) =>
        {
            var form = await ctx.Request.ReadFormAsync();

            // Check if this is a login form submission
            var spEntityId = form["sp_entity_id"].FirstOrDefault();
            var requestId = form["request_id"].FirstOrDefault();
            var relayState = form["RelayState"].FirstOrDefault();
            var acsUrl = form["acs_url"].FirstOrDefault();
            var username = form["username"].FirstOrDefault();
            var password = form["password"].FirstOrDefault();

            // If we have username/password, this is a login form post
            if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password) && !string.IsNullOrEmpty(spEntityId))
            {
                var sp = samlService.GetServiceProviderByEntityId(spEntityId);
                if (sp is null || !sp.IsEnabled)
                    return Results.BadRequest(new { error = "Unknown or disabled service provider" });

                var user = await samlService.AuthenticateUser(username, password);
                if (user is null)
                {
                    var loginHtml = GenerateSamlLoginForm(spEntityId, requestId, relayState, acsUrl ?? sp.AssertionConsumerServiceUrl, error: "Invalid credentials");
                    return Results.Content(loginHtml, "text/html");
                }

                var baseUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
                var idpEntityId = $"{baseUrl}/api/v1/saml";
                var destination = acsUrl ?? sp.AssertionConsumerServiceUrl;

                var samlResponse = samlService.GenerateSamlResponse(user, sp, idpEntityId, requestId);
                var encodedResponse = Convert.ToBase64String(Encoding.UTF8.GetBytes(samlResponse));

                var postHtml = GenerateSamlPostForm(destination, encodedResponse, relayState);
                return Results.Content(postHtml, "text/html");
            }

            // Otherwise, this is a SAMLRequest from the SP
            var samlRequest = form["SAMLRequest"].FirstOrDefault();
            relayState ??= form["RelayState"].FirstOrDefault();

            if (string.IsNullOrEmpty(samlRequest))
                return Results.BadRequest(new { error = "SAMLRequest is required" });

            var (issuer, reqId, reqAcsUrl) = samlService.ParseAuthnRequest(samlRequest);

            SamlServiceProvider foundSp = null;
            if (issuer is not null)
                foundSp = samlService.GetServiceProviderByEntityId(issuer);

            if (foundSp is null || !foundSp.IsEnabled)
                return Results.BadRequest(new { error = "Unknown or disabled service provider" });

            var destinationAcs = reqAcsUrl ?? foundSp.AssertionConsumerServiceUrl;

            // Show login form
            var html = GenerateSamlLoginForm(foundSp.EntityId, reqId, relayState, destinationAcs);
            return Results.Content(html, "text/html");
        })
        .WithName("SamlSsoPost")
        .WithTags("SAML");

        // POST /api/v1/saml/slo — Single Logout
        group.MapPost("/slo", async (
            HttpContext ctx,
            SamlService samlService) =>
        {
            var form = await ctx.Request.ReadFormAsync();
            var samlRequest = form["SAMLRequest"].FirstOrDefault();
            var relayState = form["RelayState"].FirstOrDefault();

            var baseUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
            var idpEntityId = $"{baseUrl}/api/v1/saml";

            if (!string.IsNullOrEmpty(samlRequest))
            {
                var (issuer, requestId, _) = samlService.ParseAuthnRequest(samlRequest);
                var sp = issuer is not null ? samlService.GetServiceProviderByEntityId(issuer) : null;

                if (sp?.SingleLogoutServiceUrl is not null)
                {
                    var logoutResponse = samlService.GenerateLogoutResponse(idpEntityId, sp.SingleLogoutServiceUrl, requestId);
                    var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(logoutResponse));
                    var postHtml = GenerateSamlPostForm(sp.SingleLogoutServiceUrl, encoded, relayState, isLogout: true);
                    return Results.Content(postHtml, "text/html");
                }
            }

            return Results.Ok(new { message = "Logged out successfully" });
        })
        .WithName("SamlSlo")
        .WithTags("SAML");

        return group;
    }

    /// <summary>
    /// Maps SAML Service Provider management (CRUD) endpoints.
    /// </summary>
    public static RouteGroupBuilder MapSamlSpEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", (SamlService samlService) =>
        {
            return Results.Ok(samlService.GetAllServiceProviders());
        })
        .WithName("GetSamlServiceProviders")
        .WithTags("SamlServiceProviders");

        group.MapPost("/", (SamlServiceProvider template, SamlService samlService) =>
        {
            var sp = samlService.CreateServiceProvider(template);
            return Results.Created($"/api/v1/saml/service-providers/{sp.Id}", sp);
        })
        .WithName("CreateSamlServiceProvider")
        .WithTags("SamlServiceProviders");

        group.MapGet("/{id}", (string id, SamlService samlService) =>
        {
            var sp = samlService.GetServiceProvider(id);
            return sp is null ? Results.NotFound() : Results.Ok(sp);
        })
        .WithName("GetSamlServiceProvider")
        .WithTags("SamlServiceProviders");

        group.MapPut("/{id}", (string id, SamlServiceProvider updates, SamlService samlService) =>
        {
            var updated = samlService.UpdateServiceProvider(id, updates);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        })
        .WithName("UpdateSamlServiceProvider")
        .WithTags("SamlServiceProviders");

        group.MapDelete("/{id}", (string id, SamlService samlService) =>
        {
            return samlService.DeleteServiceProvider(id) ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteSamlServiceProvider")
        .WithTags("SamlServiceProviders");

        return group;
    }

    // ── HTML helpers ──────────────────────────────────────────

    private static string GenerateSamlLoginForm(
        string spEntityId, string requestId, string relayState,
        string acsUrl, string error = null)
    {
        var errorHtml = error is not null
            ? $"<div style=\"color:#dc3545;background:#fee;border:1px solid #fcc;padding:0.5rem;border-radius:4px;margin-bottom:1rem\">{error}</div>"
            : "";

        return $@"<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <title>Sign In — Directory.NET (SAML)</title>
    <style>
        body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; background: #f5f5f5; display: flex; justify-content: center; align-items: center; min-height: 100vh; margin: 0; }}
        .login-card {{ background: #fff; border-radius: 8px; box-shadow: 0 2px 12px rgba(0,0,0,0.1); padding: 2rem; width: 380px; }}
        h2 {{ margin: 0 0 1.5rem; font-size: 1.25rem; color: #333; }}
        .sp-info {{ font-size: 0.8rem; color: #666; margin-bottom: 1rem; }}
        label {{ display: block; font-size: 0.875rem; font-weight: 600; color: #555; margin-bottom: 0.25rem; }}
        input[type=text], input[type=password] {{ width: 100%; padding: 0.5rem; border: 1px solid #ddd; border-radius: 4px; font-size: 0.875rem; box-sizing: border-box; margin-bottom: 1rem; }}
        button {{ width: 100%; padding: 0.625rem; background: #6366f1; color: #fff; border: none; border-radius: 4px; font-size: 0.875rem; font-weight: 600; cursor: pointer; }}
        button:hover {{ background: #4f46e5; }}
    </style>
</head>
<body>
    <div class=""login-card"">
        <h2>SAML Sign In</h2>
        <div class=""sp-info"">Service Provider: {EscapeHtml(spEntityId)}</div>
        {errorHtml}
        <form method=""POST"" action=""/api/v1/saml/sso"">
            <input type=""hidden"" name=""sp_entity_id"" value=""{EscapeHtml(spEntityId)}"" />
            <input type=""hidden"" name=""request_id"" value=""{EscapeHtml(requestId ?? "")}"" />
            <input type=""hidden"" name=""RelayState"" value=""{EscapeHtml(relayState ?? "")}"" />
            <input type=""hidden"" name=""acs_url"" value=""{EscapeHtml(acsUrl)}"" />
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

    private static string GenerateSamlPostForm(
        string destination, string encodedResponse,
        string relayState, bool isLogout = false)
    {
        var fieldName = isLogout ? "SAMLResponse" : "SAMLResponse";
        var relayStateHtml = !string.IsNullOrEmpty(relayState)
            ? $"<input type=\"hidden\" name=\"RelayState\" value=\"{EscapeHtml(relayState)}\" />"
            : "";

        return $@"<!DOCTYPE html>
<html>
<head><title>Redirecting...</title></head>
<body onload=""document.forms[0].submit()"">
    <noscript><p>Redirecting to service provider... Please click Submit.</p></noscript>
    <form method=""POST"" action=""{EscapeHtml(destination)}"">
        <input type=""hidden"" name=""{fieldName}"" value=""{encodedResponse}"" />
        {relayStateHtml}
        <noscript><button type=""submit"">Submit</button></noscript>
    </form>
</body>
</html>";
    }

    private static string EscapeHtml(string value)
    {
        return value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&#39;");
    }
}
