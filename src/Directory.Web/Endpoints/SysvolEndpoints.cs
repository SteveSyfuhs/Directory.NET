using System.Text.Json;
using Directory.Core;
using Directory.CosmosDb.Configuration;
using Directory.Replication;
using Microsoft.AspNetCore.Mvc;

namespace Directory.Web.Endpoints;

/// <summary>
/// Configuration model for SYSVOL share paths. Stored in CosmosConfigurationStore
/// under scope "domain", section "sysvol".
/// </summary>
public record SysvolConfig
{
    public string SysvolSharePath { get; init; } = ""; // e.g., \\dc01.contoso.com\SYSVOL
    public string NetlogonSharePath { get; init; } = ""; // e.g., \\dc01.contoso.com\NETLOGON
    public string DfsNamespace { get; init; } = ""; // optional DFS-N path
    public bool UseDfsReplication { get; init; } = false;
    public string SmbServerHostname { get; init; } = ""; // the Windows file server hosting SYSVOL
}

public static class SysvolEndpoints
{
    private const string ConfigScope = "domain";
    private const string ConfigSection = "sysvol";

    public static RouteGroupBuilder MapSysvolEndpoints(this RouteGroupBuilder group)
    {
        // ── SYSVOL Share Configuration ────────────────────────────────────

        // GET /api/v1/sysvol/config — get current SYSVOL share configuration
        group.MapGet("/config", async (CosmosConfigurationStore configStore, CancellationToken ct) =>
        {
            var doc = await configStore.GetSectionAsync(DirectoryConstants.DefaultTenantId, ConfigScope, ConfigSection, ct);
            var config = MapToSysvolConfig(doc);
            return Results.Ok(config);
        })
        .WithName("GetSysvolConfig")
        .WithTags("Sysvol", "Configuration");

        // PUT /api/v1/sysvol/config — update SYSVOL share configuration
        group.MapPut("/config", async (
            SysvolConfig config,
            CosmosConfigurationStore configStore,
            Directory.Ldap.Handlers.GroupPolicyService gpService,
            CancellationToken ct) =>
        {
            // Validate UNC paths if provided
            if (!string.IsNullOrEmpty(config.SysvolSharePath) && !config.SysvolSharePath.StartsWith(@"\\"))
                return Results.Problem(statusCode: 400, detail: "SYSVOL share path must be a UNC path starting with \\\\");

            if (!string.IsNullOrEmpty(config.NetlogonSharePath) && !config.NetlogonSharePath.StartsWith(@"\\"))
                return Results.Problem(statusCode: 400, detail: "NETLOGON share path must be a UNC path starting with \\\\");

            if (!string.IsNullOrEmpty(config.DfsNamespace) && !config.DfsNamespace.StartsWith(@"\\"))
                return Results.Problem(statusCode: 400, detail: "DFS namespace must be a UNC path starting with \\\\");

            // Load existing or create new
            var existing = await configStore.GetSectionAsync(DirectoryConstants.DefaultTenantId, ConfigScope, ConfigSection, ct);
            var doc = existing ?? new ConfigurationDocument
            {
                Id = $"{ConfigScope}::{ConfigSection}",
                TenantId = DirectoryConstants.DefaultTenantId,
                Scope = ConfigScope,
                Section = ConfigSection,
                Version = 1,
            };

            doc.Values = new Dictionary<string, JsonElement>
            {
                ["SysvolSharePath"] = JsonSerializer.SerializeToElement(config.SysvolSharePath),
                ["NetlogonSharePath"] = JsonSerializer.SerializeToElement(config.NetlogonSharePath),
                ["DfsNamespace"] = JsonSerializer.SerializeToElement(config.DfsNamespace),
                ["UseDfsReplication"] = JsonSerializer.SerializeToElement(config.UseDfsReplication),
                ["SmbServerHostname"] = JsonSerializer.SerializeToElement(config.SmbServerHostname),
            };
            doc.ModifiedBy = "admin";
            if (existing != null) doc.Version++;

            var saved = await configStore.UpsertSectionAsync(doc, ct);

            // When SYSVOL config changes, update gPCFileSysPath on all existing GPOs
            try
            {
                await gpService.UpdateAllGpoFilePathsAsync(ct);
            }
            catch (Exception ex)
            {
                // Log but don't fail the config save
                _ = ex; // suppress unused warning; logging handled inside service
            }

            return Results.Ok(MapToSysvolConfig(saved));
        })
        .WithName("UpdateSysvolConfig")
        .WithTags("Sysvol", "Configuration");

        // POST /api/v1/sysvol/config/validate — validate format of configured paths
        group.MapPost("/config/validate", (SysvolConfig config) =>
        {
            var errors = new List<string>();

            if (!string.IsNullOrEmpty(config.SysvolSharePath) && !config.SysvolSharePath.StartsWith(@"\\"))
                errors.Add("SYSVOL share path must start with \\\\");

            if (!string.IsNullOrEmpty(config.NetlogonSharePath) && !config.NetlogonSharePath.StartsWith(@"\\"))
                errors.Add("NETLOGON share path must start with \\\\");

            if (!string.IsNullOrEmpty(config.DfsNamespace) && !config.DfsNamespace.StartsWith(@"\\"))
                errors.Add("DFS namespace must start with \\\\");

            if (config.UseDfsReplication && string.IsNullOrEmpty(config.DfsNamespace))
                errors.Add("DFS namespace is required when DFS replication is enabled");

            if (!string.IsNullOrEmpty(config.SysvolSharePath) && string.IsNullOrEmpty(config.SmbServerHostname))
                errors.Add("SMB server hostname is required when SYSVOL share path is configured");

            var isValid = errors.Count == 0;
            return Results.Ok(new
            {
                IsValid = isValid,
                Errors = errors,
                Message = isValid
                    ? "Configuration is valid. Note: actual SMB connectivity cannot be verified from the web server."
                    : "Configuration has validation errors.",
            });
        })
        .WithName("ValidateSysvolConfig")
        .WithTags("Sysvol", "Configuration");

        // ── SYSVOL File Browser ──────────────────────────────────────────

        // GET /api/v1/sysvol — list top-level SYSVOL contents
        group.MapGet("/", async (SysvolService sysvol, CancellationToken ct) =>
        {
            var files = await sysvol.ListFilesAsync("", ct);

            // Return unique top-level directories and root files
            var items = new List<object>();
            var seenDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in files)
            {
                var slashIdx = file.Path.IndexOf('/');
                if (slashIdx > 0)
                {
                    var dir = file.Path[..slashIdx];
                    if (seenDirs.Add(dir))
                    {
                        items.Add(new
                        {
                            Name = dir,
                            Path = dir,
                            IsDirectory = true,
                            Size = (long?)null,
                            LastModified = (DateTimeOffset?)null,
                            Version = (long?)null,
                        });
                    }
                }
                else
                {
                    items.Add(new
                    {
                        Name = file.Path,
                        file.Path,
                        IsDirectory = false,
                        Size = (long?)file.SizeBytes,
                        LastModified = (DateTimeOffset?)file.LastModified,
                        Version = (long?)file.Version,
                    });
                }
            }

            return Results.Ok(items);
        })
        .WithName("ListSysvolRoot")
        .WithTags("Sysvol");

        // GET /api/v1/sysvol/browse/{*path} — list contents at path
        group.MapGet("/browse/{*path}", async (string path, SysvolService sysvol, CancellationToken ct) =>
        {
            var files = await sysvol.ListFilesAsync(path, ct);
            var normalizedPath = path.Replace('\\', '/').Trim('/');
            var prefix = normalizedPath + "/";

            var items = new List<object>();
            var seenDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in files)
            {
                // Get relative path from the browse path
                if (!file.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                var relative = file.Path[prefix.Length..];
                var slashIdx = relative.IndexOf('/');

                if (slashIdx > 0)
                {
                    var dir = relative[..slashIdx];
                    if (seenDirs.Add(dir))
                    {
                        items.Add(new
                        {
                            Name = dir,
                            Path = normalizedPath + "/" + dir,
                            IsDirectory = true,
                            Size = (long?)null,
                            LastModified = (DateTimeOffset?)null,
                            Version = (long?)null,
                            ContentType = (string)null,
                            ModifiedBy = (string)null,
                        });
                    }
                }
                else if (!string.IsNullOrEmpty(relative))
                {
                    items.Add(new
                    {
                        Name = relative,
                        Path = file.Path,
                        IsDirectory = false,
                        Size = (long?)file.SizeBytes,
                        LastModified = (DateTimeOffset?)file.LastModified,
                        Version = (long?)file.Version,
                        ContentType = (string)file.ContentType,
                        ModifiedBy = (string)file.ModifiedBy,
                    });
                }
            }

            return Results.Ok(items);
        })
        .WithName("BrowseSysvol")
        .WithTags("Sysvol");

        // GET /api/v1/sysvol/file/{*path} — download a file
        group.MapGet("/file/{*path}", async (string path, SysvolService sysvol, CancellationToken ct) =>
        {
            var file = await sysvol.GetFileAsync(path, ct);
            if (file == null || file.Content == null)
                return Results.NotFound(new { Detail = $"File not found: {path}" });

            return Results.File(file.Content, file.ContentType, Path.GetFileName(path));
        })
        .WithName("DownloadSysvolFile")
        .WithTags("Sysvol");

        // PUT /api/v1/sysvol/file/{*path} — upload/update a file
        group.MapPut("/file/{*path}", async (string path, HttpRequest request, SysvolService sysvol, CancellationToken ct) =>
        {
            using var ms = new MemoryStream();
            await request.Body.CopyToAsync(ms, ct);
            var content = ms.ToArray();

            if (content.Length == 0)
                return Results.BadRequest(new { Detail = "File content is empty" });

            var contentType = request.ContentType ?? "application/octet-stream";
            var modifiedBy = request.Headers["X-Modified-By"].FirstOrDefault() ?? "admin";

            var entry = await sysvol.PutFileAsync(path, content, contentType, modifiedBy, ct);

            return Results.Ok(new
            {
                entry.Path,
                entry.Version,
                entry.ContentHash,
                entry.SizeBytes,
                entry.LastModified,
                entry.ModifiedBy,
            });
        })
        .WithName("UploadSysvolFile")
        .WithTags("Sysvol");

        // DELETE /api/v1/sysvol/file/{*path} — delete a file
        group.MapDelete("/file/{*path}", async (string path, SysvolService sysvol, CancellationToken ct) =>
        {
            var deleted = await sysvol.DeleteFileAsync(path, ct);
            if (!deleted)
                return Results.NotFound(new { Detail = $"File not found: {path}" });

            return Results.Ok(new { Path = path, Deleted = true });
        })
        .WithName("DeleteSysvolFile")
        .WithTags("Sysvol");

        // GET /api/v1/sysvol/replication/status — replication status
        group.MapGet("/replication/status", async (SysvolService sysvol, CancellationToken ct) =>
        {
            var status = await sysvol.GetReplicationStatusAsync(ct);
            return Results.Ok(status);
        })
        .WithName("SysvolReplicationStatus")
        .WithTags("Sysvol", "Replication");

        // GET /api/v1/sysvol/replication/conflicts — list conflicts
        group.MapGet("/replication/conflicts", (SysvolService sysvol) =>
        {
            var conflicts = sysvol.GetReplicationConflicts();
            return Results.Ok(conflicts);
        })
        .WithName("SysvolReplicationConflicts")
        .WithTags("Sysvol", "Replication");

        return group;
    }

    /// <summary>
    /// Maps a CosmosDB configuration document to a SysvolConfig record.
    /// Returns defaults if the document is null or missing values.
    /// </summary>
    internal static SysvolConfig MapToSysvolConfig(ConfigurationDocument doc)
    {
        if (doc == null) return new SysvolConfig();

        return new SysvolConfig
        {
            SysvolSharePath = GetStringValue(doc, "SysvolSharePath"),
            NetlogonSharePath = GetStringValue(doc, "NetlogonSharePath"),
            DfsNamespace = GetStringValue(doc, "DfsNamespace"),
            UseDfsReplication = GetBoolValue(doc, "UseDfsReplication"),
            SmbServerHostname = GetStringValue(doc, "SmbServerHostname"),
        };
    }

    private static string GetStringValue(ConfigurationDocument doc, string key) =>
        doc.Values.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString() ?? ""
            : "";

    private static bool GetBoolValue(ConfigurationDocument doc, string key) =>
        doc.Values.TryGetValue(key, out var el) && el.ValueKind is JsonValueKind.True or JsonValueKind.False
            && el.GetBoolean();
}
