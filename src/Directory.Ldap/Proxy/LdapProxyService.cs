using System.Collections.Concurrent;
using Directory.Core.Models;
using Directory.Ldap.Client;
using Microsoft.Extensions.Logging;

namespace Directory.Ldap.Proxy;

public class LdapProxyBackend
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 389;
    public bool UseSsl { get; set; }
    public string BindDn { get; set; }
    public string BindPassword { get; set; }
    public string BaseDn { get; set; } = "";
    public List<AttributeMapping> AttributeMappings { get; set; } = new();
    public bool IsEnabled { get; set; } = true;
    public int Priority { get; set; }
    public int TimeoutMs { get; set; } = 5000;
}

public class AttributeMapping
{
    public string LocalName { get; set; } = "";
    public string RemoteName { get; set; } = "";
    public string TransformExpression { get; set; }
}

public class ProxyRoute
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string BaseDn { get; set; } = "";
    public string BackendId { get; set; } = "";
    public ProxyMode Mode { get; set; }
}

public enum ProxyMode { PassThrough, ReadOnly, WriteThrough, Cache }

public class ProxySearchResult
{
    public string Dn { get; set; } = "";
    public Dictionary<string, List<string>> Attributes { get; set; } = new();
    public string BackendId { get; set; } = "";
}

public class BackendTestResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public int LatencyMs { get; set; }
}

/// <summary>
/// Virtual directory / LDAP proxy service that routes searches to backend LDAP servers
/// based on DN subtree routing rules with attribute mapping support.
/// </summary>
public class LdapProxyService
{
    private readonly ILogger<LdapProxyService> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ConcurrentDictionary<string, LdapProxyBackend> _backends = new();
    private readonly ConcurrentDictionary<string, ProxyRoute> _routes = new();

    public LdapProxyService(ILogger<LdapProxyService> logger, ILoggerFactory loggerFactory)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    // Backend management

    public List<LdapProxyBackend> GetBackends()
    {
        return _backends.Values.OrderBy(b => b.Priority).ToList();
    }

    public LdapProxyBackend GetBackend(string id)
    {
        _backends.TryGetValue(id, out var backend);
        return backend;
    }

    public LdapProxyBackend AddBackend(LdapProxyBackend backend)
    {
        backend.Id = Guid.NewGuid().ToString();
        _backends[backend.Id] = backend;
        _logger.LogInformation("Added proxy backend {Name} ({Host}:{Port})", backend.Name, backend.Host, backend.Port);
        return backend;
    }

    public LdapProxyBackend UpdateBackend(string id, LdapProxyBackend backend)
    {
        if (!_backends.ContainsKey(id)) return null;
        backend.Id = id;
        _backends[id] = backend;
        _logger.LogInformation("Updated proxy backend {Id}", id);
        return backend;
    }

    public bool DeleteBackend(string id)
    {
        if (!_backends.TryRemove(id, out _)) return false;
        // Also remove routes pointing to this backend
        var routesToRemove = _routes.Values.Where(r => r.BackendId == id).Select(r => r.Id).ToList();
        foreach (var routeId in routesToRemove)
            _routes.TryRemove(routeId, out _);
        _logger.LogInformation("Deleted proxy backend {Id} and {RouteCount} associated routes", id, routesToRemove.Count);
        return true;
    }

    // Route management

    public List<ProxyRoute> GetRoutes()
    {
        return _routes.Values.ToList();
    }

    public ProxyRoute GetRoute(string id)
    {
        _routes.TryGetValue(id, out var route);
        return route;
    }

    public ProxyRoute AddRoute(ProxyRoute route)
    {
        route.Id = Guid.NewGuid().ToString();
        _routes[route.Id] = route;
        _logger.LogInformation("Added proxy route for {BaseDn} -> backend {BackendId}", route.BaseDn, route.BackendId);
        return route;
    }

    public ProxyRoute UpdateRoute(string id, ProxyRoute route)
    {
        if (!_routes.ContainsKey(id)) return null;
        route.Id = id;
        _routes[id] = route;
        return route;
    }

    public bool DeleteRoute(string id)
    {
        return _routes.TryRemove(id, out _);
    }

    // Proxy operations

    /// <summary>
    /// Find the route that matches the given base DN (longest suffix match).
    /// </summary>
    public ProxyRoute FindRoute(string baseDn)
    {
        if (string.IsNullOrEmpty(baseDn)) return null;

        var normalizedDn = baseDn.ToLowerInvariant();
        ProxyRoute bestMatch = null;
        int bestLength = -1;

        foreach (var route in _routes.Values)
        {
            var routeDn = route.BaseDn.ToLowerInvariant();
            if (normalizedDn.EndsWith(routeDn) && routeDn.Length > bestLength)
            {
                bestMatch = route;
                bestLength = routeDn.Length;
            }
        }

        return bestMatch;
    }

    /// <summary>
    /// Forward a search request to the appropriate backend based on routing rules.
    /// Returns null if no route matches (the request should be handled locally).
    /// Tries all enabled backends matching the route by priority, falling back on connection failure.
    /// </summary>
    public async Task<List<ProxySearchResult>> ProxySearch(
        string baseDn,
        string filter,
        IEnumerable<string> requestedAttributes = null,
        SearchScope scope = SearchScope.WholeSubtree,
        CancellationToken ct = default)
    {
        var route = FindRoute(baseDn);
        if (route == null) return null;

        // Build list of candidate backends: the routed backend first, then others by priority as fallbacks
        var candidates = new List<LdapProxyBackend>();

        if (_backends.TryGetValue(route.BackendId, out var primary) && primary.IsEnabled)
        {
            candidates.Add(primary);
        }

        // Add remaining enabled backends ordered by priority as fallbacks
        foreach (var fallback in _backends.Values
            .Where(b => b.IsEnabled && b.Id != route.BackendId)
            .OrderBy(b => b.Priority))
        {
            candidates.Add(fallback);
        }

        if (candidates.Count == 0)
        {
            _logger.LogWarning("No enabled backends available for route {BaseDn}", route.BaseDn);
            return null;
        }

        var attributeList = requestedAttributes as IList<string>
            ?? (requestedAttributes != null ? requestedAttributes.ToList() : null);

        foreach (var backend in candidates)
        {
            try
            {
                var results = await ExecuteBackendSearch(backend, baseDn, scope, filter, attributeList, ct);
                return results;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Proxy search failed for backend {Backend} ({Host}:{Port}), trying next",
                    backend.Name, backend.Host, backend.Port);
            }
        }

        _logger.LogError("All proxy backends failed for search on {BaseDn}", baseDn);
        return new List<ProxySearchResult>();
    }

    /// <summary>
    /// Execute a search against a single backend server using LdapClient.
    /// </summary>
    private async Task<List<ProxySearchResult>> ExecuteBackendSearch(
        LdapProxyBackend backend,
        string baseDn,
        SearchScope scope,
        string filter,
        IList<string> requestedAttributes,
        CancellationToken ct)
    {
        _logger.LogDebug("Proxying search to {Backend} ({Host}:{Port}) for {BaseDn}",
            backend.Name, backend.Host, backend.Port, baseDn);

        // Map requested attributes from local names to remote names
        var mappedAttributes = MapAttributesLocalToRemote(requestedAttributes, backend.AttributeMappings);
        var remoteAttrs = mappedAttributes.Count > 0 ? mappedAttributes.ToArray() : null;

        var clientLogger = _loggerFactory.CreateLogger<LdapClient>();

        await using var client = new LdapClient(clientLogger);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(backend.TimeoutMs);
        var linkedToken = timeoutCts.Token;

        // Connect with TLS support
        bool useTls = backend.UseSsl || backend.Port == 636;
        await client.ConnectAsync(backend.Host, backend.Port, useTls, linkedToken);

        // Bind if credentials are configured
        if (!string.IsNullOrEmpty(backend.BindDn) && !string.IsNullOrEmpty(backend.BindPassword))
        {
            var bindResult = await client.BindAsync(backend.BindDn, backend.BindPassword, linkedToken);

            if (!bindResult.Success)
            {
                _logger.LogWarning(
                    "Bind to backend {Backend} failed with code {Code}: {Message}",
                    backend.Name, bindResult.ResultCode, bindResult.DiagnosticMessage);
                throw new InvalidOperationException(
                    $"LDAP bind to {backend.Name} failed (code {bindResult.ResultCode}): {bindResult.DiagnosticMessage}");
            }
        }

        // Execute the search
        var searchResult = await client.SearchAsync(
            baseDn: baseDn,
            scope: scope,
            filter: filter,
            attributes: remoteAttrs,
            ct: linkedToken);

        if (searchResult.ResultCode != 0)
        {
            _logger.LogWarning(
                "Search on backend {Backend} returned code {Code}: {Message}",
                backend.Name, searchResult.ResultCode, searchResult.DiagnosticMessage);
        }

        // Map results back from remote attribute names to local names
        var results = new List<ProxySearchResult>(searchResult.Entries.Count);

        foreach (var entry in searchResult.Entries)
        {
            var mapped = MapEntryRemoteToLocal(entry, backend.AttributeMappings);
            mapped.BackendId = backend.Id;
            results.Add(mapped);
        }

        _logger.LogDebug("Backend {Backend} returned {Count} entries for {BaseDn}",
            backend.Name, results.Count, baseDn);

        return results;
    }

    /// <summary>
    /// Map a remote LDAP entry back to local attribute names, applying any configured transforms.
    /// </summary>
    private ProxySearchResult MapEntryRemoteToLocal(LdapSearchEntry entry, List<AttributeMapping> mappings)
    {
        var result = new ProxySearchResult
        {
            Dn = entry.DistinguishedName,
            Attributes = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        };

        foreach (var (remoteAttrName, values) in entry.Attributes)
        {
            // Find mapping where remote name matches
            var mapping = mappings.FirstOrDefault(m =>
                string.Equals(m.RemoteName, remoteAttrName, StringComparison.OrdinalIgnoreCase));

            var localName = mapping != null ? mapping.LocalName : remoteAttrName;

            // Apply transform if configured
            List<string> mappedValues;

            if (mapping != null && !string.IsNullOrEmpty(mapping.TransformExpression))
            {
                mappedValues = new List<string>(values.Count);
                foreach (var value in values)
                {
                    mappedValues.Add(ApplyTransform(value, mapping.TransformExpression));
                }
            }
            else
            {
                mappedValues = values;
            }

            result.Attributes[localName] = mappedValues;
        }

        return result;
    }

    /// <summary>
    /// Test connectivity to a backend LDAP server.
    /// </summary>
    public async Task<BackendTestResult> TestBackend(string backendId)
    {
        if (!_backends.TryGetValue(backendId, out var backend))
            return new BackendTestResult { Success = false, Message = "Backend not found." };

        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            using var tcpClient = new System.Net.Sockets.TcpClient();
            var connectTask = tcpClient.ConnectAsync(backend.Host, backend.Port);
            if (await Task.WhenAny(connectTask, Task.Delay(backend.TimeoutMs)) != connectTask)
            {
                return new BackendTestResult
                {
                    Success = false,
                    Message = $"Connection timed out after {backend.TimeoutMs}ms.",
                    LatencyMs = (int)sw.ElapsedMilliseconds
                };
            }

            await connectTask;
            sw.Stop();

            return new BackendTestResult
            {
                Success = true,
                Message = $"Successfully connected to {backend.Host}:{backend.Port}.",
                LatencyMs = (int)sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new BackendTestResult
            {
                Success = false,
                Message = $"Connection failed: {ex.Message}",
                LatencyMs = (int)sw.ElapsedMilliseconds
            };
        }
    }

    /// <summary>
    /// Check if a given DN should be proxied (has a matching route).
    /// </summary>
    public bool ShouldProxy(string baseDn)
    {
        return FindRoute(baseDn) != null;
    }

    // Attribute mapping helpers

    private List<string> MapAttributesLocalToRemote(IEnumerable<string> localAttributes, List<AttributeMapping> mappings)
    {
        if (localAttributes == null) return new List<string>();

        var mapped = new List<string>();
        foreach (var attr in localAttributes)
        {
            var mapping = mappings.FirstOrDefault(m =>
                string.Equals(m.LocalName, attr, StringComparison.OrdinalIgnoreCase));
            mapped.Add(mapping != null ? mapping.RemoteName : attr);
        }
        return mapped;
    }

    private string ApplyTransform(string value, string transformExpression)
    {
        if (string.IsNullOrEmpty(transformExpression)) return value;

        return transformExpression.ToLowerInvariant() switch
        {
            "uppercase" => value.ToUpperInvariant(),
            "lowercase" => value.ToLowerInvariant(),
            _ when transformExpression.StartsWith("prefix:") =>
                transformExpression.Substring(7) + value,
            _ when transformExpression.StartsWith("suffix:") =>
                value + transformExpression.Substring(7),
            _ => value
        };
    }
}
