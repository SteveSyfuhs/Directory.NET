using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;

namespace Directory.Web.Middleware;

/// <summary>
/// Catches unhandled exceptions and returns RFC 7807 ProblemDetails JSON responses.
/// Maps well-known exception types to appropriate HTTP status codes.
/// </summary>
public class ProblemDetailsMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ProblemDetailsMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public ProblemDetailsMiddleware(
        RequestDelegate next,
        ILogger<ProblemDetailsMiddleware> logger,
        IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception processing {Method} {Path}", context.Request.Method, context.Request.Path);

            if (context.Response.HasStarted)
            {
                _logger.LogWarning("Response has already started, cannot write ProblemDetails");
                throw;
            }

            var (statusCode, title) = MapException(ex);
            var traceId = Activity.Current?.Id ?? context.TraceIdentifier;

            var problemDetails = new ProblemDetails
            {
                Status = statusCode,
                Title = title,
                Detail = GetDetail(ex, statusCode),
                Instance = context.Request.Path,
                Type = $"https://httpstatuses.io/{statusCode}",
            };

            problemDetails.Extensions["traceId"] = traceId;

            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/problem+json";

            await JsonSerializer.SerializeAsync(context.Response.Body, problemDetails, JsonOptions);
        }
    }

    private static (int StatusCode, string Title) MapException(Exception ex) => ex switch
    {
        CosmosException cosmos => cosmos.StatusCode switch
        {
            System.Net.HttpStatusCode.NotFound => (404, "Not Found"),
            System.Net.HttpStatusCode.Conflict => (409, "Conflict"),
            System.Net.HttpStatusCode.TooManyRequests => (429, "Too Many Requests"),
            _ => (500, "Internal Server Error"),
        },
        ArgumentException or ArgumentNullException => (400, "Bad Request"),
        UnauthorizedAccessException => (403, "Forbidden"),
        KeyNotFoundException => (404, "Not Found"),
        _ => (500, "Internal Server Error"),
    };

    private string GetDetail(Exception ex, int statusCode)
    {
        // In production, don't leak internal exception details for 500 errors
        if (statusCode == 500 && !_environment.IsDevelopment())
        {
            return "An unexpected error occurred. Please try again later.";
        }

        return ex.Message;
    }
}
