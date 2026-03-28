using System.Diagnostics;

namespace Directory.Web.Services;

/// <summary>
/// Development-only middleware that launches Vite and reverse-proxies non-API requests to it.
/// Only proxies requests that don't start with /api — API endpoints are always handled by ASP.NET Core.
/// </summary>
public sealed class ViteDevMiddleware : IDisposable
{
    private readonly RequestDelegate _next;
    private readonly HttpClient _httpClient;
    private readonly Uri _viteBaseUri;
    private readonly ILogger<ViteDevMiddleware> _logger;
    private Process _viteProcess;
    private bool _viteReady;

    public ViteDevMiddleware(RequestDelegate next, Uri viteBaseUri, ILogger<ViteDevMiddleware> logger)
    {
        _next = next;
        _viteBaseUri = viteBaseUri;
        _logger = logger;
        _httpClient = new HttpClient(new HttpClientHandler
        {
            // Don't follow redirects — forward them to the browser
            AllowAutoRedirect = false,
        })
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Never proxy API requests — always let ASP.NET Core handle them
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            await _next(context);
            return;
        }

        // Ensure Vite is running
        if (!_viteReady)
        {
            await EnsureViteRunning(context.RequestServices);
        }

        // Proxy the request to Vite
        try
        {
            var targetUri = new Uri(_viteBaseUri, context.Request.Path + context.Request.QueryString);

            using var requestMessage = new HttpRequestMessage(new HttpMethod(context.Request.Method), targetUri);

            // Copy request headers (except Host)
            foreach (var header in context.Request.Headers)
            {
                if (string.Equals(header.Key, "Host", StringComparison.OrdinalIgnoreCase))
                    continue;
                requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }

            // Copy request body for non-GET
            if (context.Request.ContentLength > 0 || context.Request.Headers.ContainsKey("Transfer-Encoding"))
            {
                requestMessage.Content = new StreamContent(context.Request.Body);
                if (context.Request.ContentType != null)
                    requestMessage.Content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(context.Request.ContentType);
            }

            using var responseMessage = await _httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);

            context.Response.StatusCode = (int)responseMessage.StatusCode;

            // Copy response headers
            foreach (var header in responseMessage.Headers)
            {
                context.Response.Headers[header.Key] = header.Value.ToArray();
            }
            foreach (var header in responseMessage.Content.Headers)
            {
                context.Response.Headers[header.Key] = header.Value.ToArray();
            }

            // Remove transfer-encoding since Kestrel handles its own chunking
            context.Response.Headers.Remove("transfer-encoding");

            await responseMessage.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
        }
        catch (HttpRequestException)
        {
            // Vite is down — fall through to the next middleware (which will 404,
            // but at least we won't crash API calls)
            _viteReady = false;
            _logger.LogWarning("Vite dev server is not responding. Attempting restart on next request.");
            await _next(context);
        }
    }

    private async Task EnsureViteRunning(IServiceProvider services)
    {
        // Check if Vite is already listening
        if (await IsListening())
        {
            _viteReady = true;
            return;
        }

        // Kill stale process if it died
        if (_viteProcess is { HasExited: true })
        {
            _viteProcess.Dispose();
            _viteProcess = null;
        }

        // Launch Vite if we haven't already
        if (_viteProcess == null)
        {
            var env = services.GetRequiredService<IWebHostEnvironment>();
            var clientAppPath = Path.Combine(env.ContentRootPath, "ClientApp");

            _logger.LogInformation("Starting Vite dev server in {Path}...", clientAppPath);

            var psi = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "cmd" : "sh",
                Arguments = OperatingSystem.IsWindows() ? "/c npm run dev" : "-c \"npm run dev\"",
                WorkingDirectory = clientAppPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            _viteProcess = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start Vite dev server.");

            // Log Vite output for debugging
            _viteProcess.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    _logger.LogDebug("[vite] {Line}", e.Data);
            };
            _viteProcess.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    _logger.LogDebug("[vite] {Line}", e.Data);
            };
            _viteProcess.BeginOutputReadLine();
            _viteProcess.BeginErrorReadLine();

            // Clean up Vite when the app shuts down
            var lifetime = services.GetRequiredService<IHostApplicationLifetime>();
            lifetime.ApplicationStopping.Register(() =>
            {
                if (_viteProcess is { HasExited: false })
                {
                    _logger.LogInformation("Stopping Vite dev server...");
                    _viteProcess.Kill(entireProcessTree: true);
                }
            });
        }

        // Poll until Vite is ready (up to 30 seconds)
        var timeout = TimeSpan.FromSeconds(30);
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            if (await IsListening())
            {
                _viteReady = true;
                _logger.LogInformation("Vite dev server is ready at {Uri}", _viteBaseUri);
                return;
            }
            await Task.Delay(250);
        }

        _logger.LogWarning("Vite dev server did not start within {Timeout}s", timeout.TotalSeconds);
    }

    private async Task<bool> IsListening()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            using var response = await _httpClient.GetAsync(_viteBaseUri, cts.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        if (_viteProcess is { HasExited: false })
        {
            _viteProcess.Kill(entireProcessTree: true);
        }
        _viteProcess?.Dispose();
    }
}

public static class ViteDevMiddlewareExtensions
{
    /// <summary>
    /// In development, launches Vite and reverse-proxies non-API requests to it.
    /// API requests (/api/*) always pass through to ASP.NET Core endpoint routing.
    /// </summary>
    public static IApplicationBuilder UseViteDevelopmentServer(this IApplicationBuilder app, int port = 6173)
    {
        return app.UseMiddleware<ViteDevMiddleware>(new Uri($"http://localhost:{port}"));
    }
}
