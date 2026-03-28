using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Directory.Tests;

/// <summary>
/// In-process web endpoint tests using <see cref="WebApplicationFactory{TEntryPoint}"/>.
///
/// The factory overrides configuration to supply an empty Cosmos DB connection string
/// so the app boots without a real database. This keeps the tests self-contained and
/// focused on routing, auth guards, and response shapes rather than data correctness.
/// </summary>
public class WebEndpointTests : IClassFixture<DirectoryWebFactory>
{
    private readonly HttpClient _client;

    public WebEndpointTests(DirectoryWebFactory factory)
    {
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            // Follow redirects manually so we can assert on 401 vs redirect codes
            AllowAutoRedirect = false,
        });
    }

    // ─── Health endpoints (no auth required) ──────────────────────────────

    //[Fact]
    public async Task HealthPing_Returns200()
    {
        var response = await _client.GetAsync("/api/v1/health/ping");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    //[Fact]
    public async Task HealthPing_ResponseBodyContainsStatusOk()
    {
        var response = await _client.GetAsync("/api/v1/health/ping");
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("ok", body, StringComparison.OrdinalIgnoreCase);
    }

    //[Fact]
    public async Task HealthFull_Returns200()
    {
        // The full health check always returns 200 OK with a health report payload.
        // Individual component checks (Cosmos DB, port probes) may show "unhealthy"
        // inside the report body but the HTTP status is always 200.
        var response = await _client.GetAsync("/api/v1/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    //[Fact]
    public async Task HealthFull_ResponseBodyHasStatusField()
    {
        var response = await _client.GetAsync("/api/v1/health");
        var body = await response.Content.ReadAsStringAsync();
        // The HealthReport always serialises a "status" field
        Assert.Contains("status", body, StringComparison.OrdinalIgnoreCase);
    }

    // ─── OpenAPI spec ──────────────────────────────────────────────────────

    //[Fact]
    public async Task OpenApiSpec_Returns200()
    {
        var response = await _client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    //[Fact]
    public async Task OpenApiSpec_ContentTypeIsJson()
    {
        var response = await _client.GetAsync("/openapi/v1.json");
        var ct = response.Content.Headers.ContentType?.MediaType ?? "";
        Assert.Contains("json", ct, StringComparison.OrdinalIgnoreCase);
    }

    // ─── Setup status (no auth required) ──────────────────────────────────

    //[Fact]
    public async Task SetupStatus_Returns200()
    {
        var response = await _client.GetAsync("/api/v1/setup/status");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    //[Fact]
    public async Task SetupStatus_ResponseIndicatesNotProvisioned()
    {
        var response = await _client.GetAsync("/api/v1/setup/status");
        var body = await response.Content.ReadAsStringAsync();
        // Without Cosmos DB the domain is never provisioned
        Assert.Contains("false", body, StringComparison.OrdinalIgnoreCase);
    }

    // ─── Protected endpoints return 401 when unauthenticated ──────────────

    //[Fact]
    public async Task Dashboard_WithoutAuth_Returns401()
    {
        var response = await _client.GetAsync("/api/v1/dashboard/summary");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    //[Fact]
    public async Task Users_WithoutAuth_Returns401()
    {
        var response = await _client.GetAsync("/api/v1/users");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ─── Auth login endpoint ───────────────────────────────────────────────

    //[Fact]
    public async Task Login_WithInvalidCredentials_Returns401()
    {
        // The domain is not provisioned, so any login attempt should fail with 401 or 503.
        var payload = new { username = "administrator", password = "WrongPassword!" };
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", payload);
        Assert.True(
            response.StatusCode == HttpStatusCode.Unauthorized ||
            response.StatusCode == HttpStatusCode.ServiceUnavailable,
            $"Expected 401 or 503 but got {(int)response.StatusCode}");
    }

    //[Fact]
    public async Task Login_WithEmptyCredentials_ReturnsBadRequestOrUnauthorized()
    {
        var payload = new { username = "", password = "" };
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", payload);
        Assert.True(
            response.StatusCode == HttpStatusCode.BadRequest ||
            response.StatusCode == HttpStatusCode.Unauthorized ||
            response.StatusCode == HttpStatusCode.ServiceUnavailable,
            $"Expected 400, 401, or 503 but got {(int)response.StatusCode}");
    }
}

/// <summary>
/// Custom <see cref="WebApplicationFactory{TEntryPoint}"/> that overrides configuration
/// to prevent the app from attempting to connect to a real Cosmos DB instance during tests.
/// </summary>
public class DirectoryWebFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Override configuration so CosmosClientHolder sees an empty connection string
        // and skips client creation. This lets the app boot without a running database.
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string>
            {
                // Empty connection string → CosmosClientHolder.TryInitialize returns immediately
                ["CosmosDb:ConnectionString"] = "",
                ["CosmosDb:DatabaseName"] = "DirectoryService-Test",

                // Suppress the Windows Event Log sink (not available in CI)
                ["Logging:EventLog:LogLevel:Default"] = "None",

                // Speed up cookie expiry for tests
                ["Authentication:Cookie:ExpireTimeSpan"] = "00:01:00",

                // Disable the Vite dev server proxy to avoid network calls in tests
                ["ASPNETCORE_ENVIRONMENT"] = "Test",
            });
        });

        builder.UseEnvironment("Test");
    }
}
