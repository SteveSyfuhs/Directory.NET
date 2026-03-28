using System.Text.Json;
using Directory.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Directory.Web.Middleware;

/// <summary>
/// Middleware that intercepts write operations (POST, PUT, DELETE) when the DC is in RODC mode.
/// Returns 403 Forbidden with a ProblemDetails response explaining the DC is read-only.
/// Exempts health, setup, status, and RODC management endpoints.
/// </summary>
public class RodcMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RodcMiddleware> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Paths that are always allowed even in RODC mode (case-insensitive prefix match).
    /// </summary>
    private static readonly string[] ExemptPaths =
    [
        "/api/v1/health",
        "/api/v1/setup",
        "/api/v1/rodc",
        "/api/v1/service-settings",
        "/api/v1/dashboard",
        "/api/v1/metrics",
    ];

    /// <summary>
    /// HTTP methods considered as write operations.
    /// </summary>
    private static readonly HashSet<string> WriteMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "POST", "PUT", "DELETE", "PATCH"
    };

    public RodcMiddleware(RequestDelegate next, ILogger<RodcMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, RodcService rodcService)
    {
        // Only intercept API write requests when in RODC mode
        if (rodcService.IsReadOnly
            && WriteMethods.Contains(context.Request.Method)
            && context.Request.Path.StartsWithSegments("/api")
            && !IsExemptPath(context.Request.Path))
        {
            _logger.LogWarning(
                "RODC mode: blocked {Method} {Path} — write operations are not allowed on a read-only domain controller",
                context.Request.Method, context.Request.Path);

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/problem+json";

            var problemDetails = new ProblemDetails
            {
                Status = 403,
                Title = "Read-Only Domain Controller",
                Detail = "This domain controller is operating in read-only (RODC) mode. Write operations are not permitted. " +
                         "Please direct write requests to the writable domain controller.",
                Type = "https://httpstatuses.io/403",
                Instance = context.Request.Path,
            };

            // Include the writable DC endpoint for client redirection
            if (!string.IsNullOrEmpty(rodcService.Settings.FullDcEndpoint))
            {
                problemDetails.Extensions["writableDcEndpoint"] = rodcService.Settings.FullDcEndpoint;
            }

            problemDetails.Extensions["rodcMode"] = true;

            await JsonSerializer.SerializeAsync(context.Response.Body, problemDetails, JsonOptions);
            return;
        }

        await _next(context);
    }

    private static bool IsExemptPath(PathString path)
    {
        foreach (var exempt in ExemptPaths)
        {
            if (path.StartsWithSegments(exempt, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
