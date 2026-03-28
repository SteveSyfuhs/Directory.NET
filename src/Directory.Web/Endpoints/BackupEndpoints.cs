using System.Text;
using System.Text.Json;
using Directory.Core;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Web.Services;

namespace Directory.Web.Endpoints;

public static class BackupEndpoints
{
    public static RouteGroupBuilder MapBackupEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/export", async (IDirectoryStore store, INamingContextService ncService,
            HttpContext context, string objectClass, string baseDn) =>
        {
            context.Response.ContentType = "application/json";
            context.Response.Headers.ContentDisposition =
                $"attachment; filename=\"directory-export-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json\"";

            var filterExpr = string.IsNullOrEmpty(objectClass)
                ? "(objectClass=*)"
                : $"(objectClass={objectClass})";

            var filterNode = FilterBuilder.Parse(filterExpr);

            var searchBase = string.IsNullOrWhiteSpace(baseDn)
                ? ncService.GetDomainNc().Dn
                : baseDn;

            var result = await store.SearchAsync(
                DirectoryConstants.DefaultTenantId,
                searchBase,
                SearchScope.WholeSubtree,
                filterNode,
                attributes: null,
                sizeLimit: 50000,
                pageSize: 10000);

            var entries = result.Entries.Where(e => !e.IsDeleted).ToList();

            await context.Response.WriteAsJsonAsync(entries);
        })
        .WithName("BackupExport")
        .WithTags("Backup");

        group.MapPost("/import", async (HttpContext context, IDirectoryStore store) =>
        {
            List<DirectoryObject> objects;
            try
            {
                objects = await context.Request.ReadFromJsonAsync<List<DirectoryObject>>();
            }
            catch (JsonException ex)
            {
                return Results.Problem(statusCode: 400, detail: $"Invalid JSON: {ex.Message}");
            }

            if (objects == null || objects.Count == 0)
            {
                return Results.Problem(statusCode: 400, detail: "Request body must be a non-empty JSON array of directory objects.");
            }

            int imported = 0, updated = 0, failed = 0;
            var errors = new List<string>();

            foreach (var obj in objects)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(obj.DistinguishedName))
                    {
                        failed++;
                        errors.Add("Object missing distinguishedName");
                        continue;
                    }

                    // Ensure Id is set from DN
                    obj.Id = obj.DistinguishedName.ToLowerInvariant();
                    if (string.IsNullOrEmpty(obj.TenantId))
                        obj.TenantId = DirectoryConstants.DefaultTenantId;

                    var existing = await store.GetByDnAsync(DirectoryConstants.DefaultTenantId, obj.DistinguishedName);

                    if (existing != null)
                    {
                        obj.WhenChanged = DateTimeOffset.UtcNow;
                        obj.ETag = existing.ETag;
                        await store.UpdateAsync(obj);
                        updated++;
                    }
                    else
                    {
                        obj.WhenCreated = DateTimeOffset.UtcNow;
                        obj.WhenChanged = DateTimeOffset.UtcNow;
                        await store.CreateAsync(obj);
                        imported++;
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    errors.Add($"{obj.DistinguishedName}: {ex.Message}");
                }
            }

            return Results.Ok(new BackupImportResult(imported, updated, failed, errors));
        })
        .WithName("BackupImport")
        .WithTags("Backup");

        group.MapGet("/export/ldif", async (IDirectoryStore store, INamingContextService ncService,
            HttpContext context, string objectClass, string baseDn) =>
        {
            context.Response.ContentType = "text/plain; charset=utf-8";
            context.Response.Headers.ContentDisposition =
                $"attachment; filename=\"directory-export-{DateTime.UtcNow:yyyyMMdd-HHmmss}.ldif\"";

            var filterExpr = string.IsNullOrEmpty(objectClass)
                ? "(objectClass=*)"
                : $"(objectClass={objectClass})";

            var filterNode = FilterBuilder.Parse(filterExpr);

            var searchBase = string.IsNullOrWhiteSpace(baseDn)
                ? ncService.GetDomainNc().Dn
                : baseDn;

            var result = await store.SearchAsync(
                DirectoryConstants.DefaultTenantId,
                searchBase,
                SearchScope.WholeSubtree,
                filterNode,
                attributes: null,
                sizeLimit: 50000,
                pageSize: 10000);

            var entries = result.Entries.Where(e => !e.IsDeleted).ToList();

            var sb = new StringBuilder();
            sb.AppendLine("version: 1");
            sb.AppendLine();

            foreach (var entry in entries)
            {
                sb.AppendLine($"dn: {entry.DistinguishedName}");

                foreach (var oc in entry.ObjectClass)
                {
                    sb.AppendLine($"objectClass: {oc}");
                }

                if (!string.IsNullOrEmpty(entry.Cn))
                    sb.AppendLine($"cn: {entry.Cn}");
                if (!string.IsNullOrEmpty(entry.SAMAccountName))
                    sb.AppendLine($"sAMAccountName: {entry.SAMAccountName}");
                if (!string.IsNullOrEmpty(entry.UserPrincipalName))
                    sb.AppendLine($"userPrincipalName: {entry.UserPrincipalName}");
                if (!string.IsNullOrEmpty(entry.DisplayName))
                    sb.AppendLine($"displayName: {entry.DisplayName}");
                if (!string.IsNullOrEmpty(entry.Description))
                    sb.AppendLine($"description: {entry.Description}");
                if (!string.IsNullOrEmpty(entry.Mail))
                    sb.AppendLine($"mail: {entry.Mail}");
                if (!string.IsNullOrEmpty(entry.ObjectGuid))
                    sb.AppendLine($"objectGUID: {entry.ObjectGuid}");
                if (!string.IsNullOrEmpty(entry.ObjectSid))
                    sb.AppendLine($"objectSid: {entry.ObjectSid}");
                if (!string.IsNullOrEmpty(entry.ObjectCategory))
                    sb.AppendLine($"objectCategory: {entry.ObjectCategory}");
                if (!string.IsNullOrEmpty(entry.GivenName))
                    sb.AppendLine($"givenName: {entry.GivenName}");
                if (!string.IsNullOrEmpty(entry.Sn))
                    sb.AppendLine($"sn: {entry.Sn}");
                if (!string.IsNullOrEmpty(entry.Title))
                    sb.AppendLine($"title: {entry.Title}");
                if (!string.IsNullOrEmpty(entry.Department))
                    sb.AppendLine($"department: {entry.Department}");
                if (!string.IsNullOrEmpty(entry.Company))
                    sb.AppendLine($"company: {entry.Company}");
                if (!string.IsNullOrEmpty(entry.Manager))
                    sb.AppendLine($"manager: {entry.Manager}");
                if (!string.IsNullOrEmpty(entry.DnsHostName))
                    sb.AppendLine($"dNSHostName: {entry.DnsHostName}");
                if (entry.UserAccountControl != 0)
                    sb.AppendLine($"userAccountControl: {entry.UserAccountControl}");
                if (entry.MemberOf.Count > 0)
                {
                    foreach (var m in entry.MemberOf)
                        sb.AppendLine($"memberOf: {m}");
                }
                if (entry.Member.Count > 0)
                {
                    foreach (var m in entry.Member)
                        sb.AppendLine($"member: {m}");
                }
                if (entry.ServicePrincipalName.Count > 0)
                {
                    foreach (var spn in entry.ServicePrincipalName)
                        sb.AppendLine($"servicePrincipalName: {spn}");
                }

                // Include extra attributes from the dictionary
                foreach (var kvp in entry.Attributes)
                {
                    var key = kvp.Key.ToLowerInvariant();
                    // Skip sensitive attributes
                    if (key is "nthash" or "kerberoskeys" or "unicodepwd" or "dbcspwd" or "supplementalcredentials")
                        continue;

                    foreach (var val in kvp.Value.GetStrings())
                    {
                        sb.AppendLine($"{kvp.Key}: {val}");
                    }
                }

                sb.AppendLine();
            }

            await context.Response.WriteAsync(sb.ToString());
        })
        .WithName("BackupExportLdif")
        .WithTags("Backup");

        return group;
    }
}

public record BackupImportResult(
    int Imported,
    int Updated,
    int Failed,
    List<string> Errors
);
