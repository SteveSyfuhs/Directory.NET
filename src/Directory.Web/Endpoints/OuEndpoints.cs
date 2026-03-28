using Directory.Core;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Core.Telemetry;
using Directory.Web.Models;

namespace Directory.Web.Endpoints;

public static class OuEndpoints
{
    public static RouteGroupBuilder MapOuEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", async (IDirectoryStore store, INamingContextService ncService, string parentDn) =>
        {
            var baseDn = parentDn ?? ncService.GetDomainNc().Dn;
            var filter = new EqualityFilterNode("objectClass", "organizationalUnit");

            var result = await store.SearchAsync(DirectoryConstants.DefaultTenantId, baseDn, SearchScope.SingleLevel, filter,
                ["distinguishedName", "name", "description", "ou", "whenCreated", "whenChanged"], pageSize: 500);

            return Results.Ok(result.Entries
                .Where(e => !e.IsDeleted)
                .Select(ou => new
                {
                    ou.DistinguishedName,
                    Name = ou.Cn ?? ou.GetAttribute("ou")?.GetFirstString() ?? "",
                    Description = ou.Description ?? "",
                    ou.WhenCreated,
                    ou.WhenChanged,
                    ou.ObjectGuid,
                }));
        })
        .WithName("ListOUs")
        .WithTags("OUs");

        group.MapPost("/", async (IDirectoryStore store, INamingContextService ncService, CreateOuRequest request, IAuditService audit, HttpContext context) =>
        {
            var validation =
                ValidationHelper.ValidateRequired(request.Name, "name") ??
                ValidationHelper.ValidateMaxLength(request.Name, "name") ??
                ValidationHelper.ValidateDn(request.ParentDn, "parentDn") ??
                ValidationHelper.ValidateMaxLength(request.Description, "description", ValidationHelper.MaxDescriptionLength);
            if (validation != null) return validation;

            var domainDn = ncService.GetDomainNc().Dn;
            var dn = $"OU={request.Name},{request.ParentDn}";

            // Check if object already exists
            var existing = await store.GetByDnAsync(DirectoryConstants.DefaultTenantId, dn);
            if (existing != null)
                return Results.Problem(statusCode: 409, detail: $"Object already exists at {dn}");

            var now = DateTimeOffset.UtcNow;
            var usn = await store.GetNextUsnAsync(DirectoryConstants.DefaultTenantId, domainDn);

            var ou = new DirectoryObject
            {
                Id = dn.ToLowerInvariant(),
                TenantId = DirectoryConstants.DefaultTenantId,
                DistinguishedName = dn,
                DomainDn = ExtractDomainDn(dn),
                ObjectCategory = "organizationalUnit",
                ObjectClass = ["top", "organizationalUnit"],
                Cn = request.Name,
                ParentDn = request.ParentDn,
                WhenCreated = now,
                WhenChanged = now,
                USNCreated = usn,
                USNChanged = usn,
            };

            ou.SetAttribute("ou", new DirectoryAttribute("ou", request.Name));
            if (!string.IsNullOrEmpty(request.Description))
                ou.Description = request.Description;
            if (!string.IsNullOrEmpty(request.Street))
                ou.SetAttribute("street", new DirectoryAttribute("street", request.Street));
            if (!string.IsNullOrEmpty(request.City))
                ou.SetAttribute("l", new DirectoryAttribute("l", request.City));
            if (!string.IsNullOrEmpty(request.State))
                ou.SetAttribute("st", new DirectoryAttribute("st", request.State));
            if (!string.IsNullOrEmpty(request.PostalCode))
                ou.SetAttribute("postalCode", new DirectoryAttribute("postalCode", request.PostalCode));
            if (!string.IsNullOrEmpty(request.Country))
                ou.SetAttribute("c", new DirectoryAttribute("c", request.Country));

            await store.CreateAsync(ou);

            DirectoryMetrics.ObjectsCreated.Add(1, new KeyValuePair<string, object>("objectClass", "organizationalUnit"));

            await audit.LogAsync(new AuditEntry
            {
                TenantId = DirectoryConstants.DefaultTenantId,
                Action = "Create",
                TargetDn = ou.DistinguishedName,
                TargetObjectClass = "organizationalUnit",
                SourceIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                Details = new() { ["name"] = request.Name }
            });

            return Results.Created($"/api/v1/ous/{ou.ObjectGuid}", new { ou.ObjectGuid, ou.DistinguishedName });
        })
        .WithName("CreateOU")
        .WithTags("OUs");

        group.MapPut("/{guid}", async (IDirectoryStore store, string guid, UpdateOuRequest request, IAuditService audit, HttpContext context) =>
        {
            var obj = await store.GetByGuidAsync(DirectoryConstants.DefaultTenantId, guid);
            if (obj == null || obj.IsDeleted)
                return Results.NotFound();

            if (request.Description is not null)
                obj.Description = request.Description;
            if (request.Street is not null)
                obj.SetAttribute("street", new DirectoryAttribute("street", request.Street));
            if (request.City is not null)
                obj.SetAttribute("l", new DirectoryAttribute("l", request.City));
            if (request.State is not null)
                obj.SetAttribute("st", new DirectoryAttribute("st", request.State));
            if (request.PostalCode is not null)
                obj.SetAttribute("postalCode", new DirectoryAttribute("postalCode", request.PostalCode));
            if (request.Country is not null)
                obj.SetAttribute("c", new DirectoryAttribute("c", request.Country));
            if (request.ManagedBy is not null)
                obj.SetAttribute("managedBy", new DirectoryAttribute("managedBy", request.ManagedBy));

            obj.WhenChanged = DateTimeOffset.UtcNow;
            await store.UpdateAsync(obj);

            await audit.LogAsync(new AuditEntry
            {
                TenantId = DirectoryConstants.DefaultTenantId,
                Action = "Update",
                TargetDn = obj.DistinguishedName,
                TargetObjectClass = "organizationalUnit",
                SourceIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown"
            });

            return Results.Ok();
        })
        .WithName("UpdateOU")
        .WithTags("OUs");

        group.MapDelete("/{guid}", async (IDirectoryStore store, string guid, bool? recursive, IAuditService audit, HttpContext context) =>
        {
            var obj = await store.GetByGuidAsync(DirectoryConstants.DefaultTenantId, guid);
            if (obj == null || obj.IsDeleted)
                return Results.NotFound();

            if (recursive == true)
            {
                // Tree delete: remove all children first
                var children = await store.SearchAsync(DirectoryConstants.DefaultTenantId, obj.DistinguishedName,
                    SearchScope.WholeSubtree, null, ["distinguishedName"], pageSize: 10000,
                    includeDeleted: false);

                // Delete children deepest first (reverse DN length order), skip the OU itself
                var sorted = children.Entries
                    .Where(e => !string.Equals(e.DistinguishedName, obj.DistinguishedName, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(e => e.DistinguishedName.Length)
                    .ToList();

                foreach (var child in sorted)
                {
                    await store.DeleteAsync(DirectoryConstants.DefaultTenantId, child.DistinguishedName);
                }
            }

            await store.DeleteAsync(DirectoryConstants.DefaultTenantId, obj.DistinguishedName);

            await audit.LogAsync(new AuditEntry
            {
                TenantId = DirectoryConstants.DefaultTenantId,
                Action = "Delete",
                TargetDn = obj.DistinguishedName,
                TargetObjectClass = "organizationalUnit",
                SourceIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                Details = new() { ["recursive"] = (recursive == true).ToString() }
            });

            return Results.Ok();
        })
        .WithName("DeleteOU")
        .WithTags("OUs");

        return group;
    }

    internal static string ExtractDomainDn(string dn)
    {
        var components = dn.Split(',', StringSplitOptions.TrimEntries);
        var dcParts = components.Where(c => c.StartsWith("DC=", StringComparison.OrdinalIgnoreCase));
        return string.Join(",", dcParts);
    }
}

public record CreateOuRequest(string Name, string ParentDn, string Description = null,
    string Street = null, string City = null, string State = null,
    string PostalCode = null, string Country = null);

public record UpdateOuRequest(string Description = null, string Street = null,
    string City = null, string State = null, string PostalCode = null,
    string Country = null, string ManagedBy = null);
