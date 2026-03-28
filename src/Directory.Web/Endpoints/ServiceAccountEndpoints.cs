using Directory.Core;
using Directory.Core.Interfaces;
using Directory.Core.Models;

namespace Directory.Web.Endpoints;

public static class ServiceAccountEndpoints
{
    public static RouteGroupBuilder MapServiceAccountEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", async (IDirectoryStore store, INamingContextService ncService, CancellationToken ct) =>
        {
            var domainDn = ncService.GetDomainNc().Dn;

            // Search for MSAs and gMSAs
            var msaFilter = new EqualityFilterNode("objectClass", "msDS-GroupManagedServiceAccount");
            var msaResult = await store.SearchAsync(DirectoryConstants.DefaultTenantId, domainDn, SearchScope.WholeSubtree, msaFilter,
                null, pageSize: 1000, ct: ct);

            var msaFilter2 = new EqualityFilterNode("objectClass", "msDS-ManagedServiceAccount");
            var msaResult2 = await store.SearchAsync(DirectoryConstants.DefaultTenantId, domainDn, SearchScope.WholeSubtree, msaFilter2,
                null, pageSize: 1000, ct: ct);

            var all = msaResult.Entries.Concat(msaResult2.Entries)
                .Where(e => !e.IsDeleted)
                .DistinctBy(e => e.DistinguishedName)
                .Select(e => MapToSummary(e))
                .ToList();

            return Results.Ok(all);
        })
        .WithName("ListServiceAccounts")
        .WithTags("ServiceAccounts");

        group.MapPost("/", async (
            CreateServiceAccountRequest request,
            IDirectoryStore store,
            IRidAllocator ridAllocator,
            INamingContextService ncService,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.Problem(statusCode: 400, detail: "Name is required");

            var domainDn = ncService.GetDomainNc().Dn;
            var containerDn = $"CN=Managed Service Accounts,{domainDn}";
            var dn = $"CN={request.Name},{containerDn}";

            var existing = await store.GetByDnAsync(DirectoryConstants.DefaultTenantId, dn, ct);
            if (existing != null)
                return Results.Problem(statusCode: 409, detail: $"Service account already exists at {dn}");

            var objectSid = await ridAllocator.GenerateObjectSidAsync(DirectoryConstants.DefaultTenantId, domainDn);
            var now = DateTimeOffset.UtcNow;
            var usn = await store.GetNextUsnAsync(DirectoryConstants.DefaultTenantId, domainDn, ct);

            var isGmsa = string.Equals(request.Type, "gmsa", StringComparison.OrdinalIgnoreCase);
            var objectClass = isGmsa
                ? new List<string> { "top", "person", "organizationalPerson", "user", "computer", "msDS-GroupManagedServiceAccount" }
                : new List<string> { "top", "person", "organizationalPerson", "user", "computer", "msDS-ManagedServiceAccount" };

            var obj = new DirectoryObject
            {
                Id = dn.ToLowerInvariant(),
                TenantId = DirectoryConstants.DefaultTenantId,
                DistinguishedName = dn,
                DomainDn = domainDn,
                ObjectCategory = isGmsa ? "msDS-GroupManagedServiceAccount" : "msDS-ManagedServiceAccount",
                ObjectClass = objectClass,
                Cn = request.Name,
                SAMAccountName = request.Name + "$",
                DisplayName = request.Name,
                DnsHostName = request.DnsHostName,
                ParentDn = containerDn,
                ObjectGuid = Guid.NewGuid().ToString(),
                ObjectSid = objectSid,
                UserAccountControl = 0x1000, // WORKSTATION_TRUST_ACCOUNT
                WhenCreated = now,
                WhenChanged = now,
                USNCreated = usn,
                USNChanged = usn,
                SAMAccountType = 0x30000001, // SAM_MACHINE_ACCOUNT
            };

            // Set gMSA-specific attributes
            var passwordInterval = request.PasswordInterval ?? 30;
            obj.SetAttribute("msDS-ManagedPasswordInterval",
                new DirectoryAttribute("msDS-ManagedPasswordInterval", passwordInterval));

            if (request.Principals != null && request.Principals.Count > 0)
            {
                obj.SetAttribute("msDS-GroupMSAMembership",
                    new DirectoryAttribute("msDS-GroupMSAMembership", [.. request.Principals.Cast<object>()]));
            }

            if (request.ServicePrincipalNames != null)
            {
                obj.ServicePrincipalName = request.ServicePrincipalNames;
            }

            await store.CreateAsync(obj, ct);

            return Results.Created($"/api/v1/service-accounts/{obj.ObjectGuid}", MapToDetail(obj));
        })
        .WithName("CreateServiceAccount")
        .WithTags("ServiceAccounts");

        group.MapGet("/{id}", async (string id, IDirectoryStore store, CancellationToken ct) =>
        {
            var obj = await store.GetByGuidAsync(DirectoryConstants.DefaultTenantId, id, ct);
            if (obj == null || obj.IsDeleted) return Results.NotFound();

            return Results.Ok(MapToDetail(obj));
        })
        .WithName("GetServiceAccount")
        .WithTags("ServiceAccounts");

        group.MapPut("/{id}", async (string id, UpdateServiceAccountRequest request, IDirectoryStore store, CancellationToken ct) =>
        {
            var obj = await store.GetByGuidAsync(DirectoryConstants.DefaultTenantId, id, ct);
            if (obj == null || obj.IsDeleted) return Results.NotFound();

            if (request.DnsHostName != null)
                obj.DnsHostName = request.DnsHostName;

            if (request.Description != null)
                obj.Description = request.Description;

            if (request.PasswordInterval.HasValue)
                obj.SetAttribute("msDS-ManagedPasswordInterval",
                    new DirectoryAttribute("msDS-ManagedPasswordInterval", request.PasswordInterval.Value));

            if (request.ServicePrincipalNames != null)
                obj.ServicePrincipalName = request.ServicePrincipalNames;

            obj.WhenChanged = DateTimeOffset.UtcNow;
            await store.UpdateAsync(obj, ct);

            return Results.Ok(MapToDetail(obj));
        })
        .WithName("UpdateServiceAccount")
        .WithTags("ServiceAccounts");

        group.MapDelete("/{id}", async (string id, IDirectoryStore store, CancellationToken ct) =>
        {
            var obj = await store.GetByGuidAsync(DirectoryConstants.DefaultTenantId, id, ct);
            if (obj == null || obj.IsDeleted) return Results.NotFound();

            await store.DeleteAsync(DirectoryConstants.DefaultTenantId, obj.DistinguishedName, hardDelete: true, ct);
            return Results.Ok();
        })
        .WithName("DeleteServiceAccount")
        .WithTags("ServiceAccounts");

        group.MapPost("/{id}/principals", async (string id, AddPrincipalRequest request, IDirectoryStore store, CancellationToken ct) =>
        {
            var obj = await store.GetByGuidAsync(DirectoryConstants.DefaultTenantId, id, ct);
            if (obj == null || obj.IsDeleted) return Results.NotFound();

            var attr = obj.GetAttribute("msDS-GroupMSAMembership");
            var existing = attr?.GetStrings().ToList() ?? [];

            if (!existing.Any(e => string.Equals(e, request.PrincipalDn, StringComparison.OrdinalIgnoreCase)))
            {
                existing.Add(request.PrincipalDn);
                obj.SetAttribute("msDS-GroupMSAMembership",
                    new DirectoryAttribute("msDS-GroupMSAMembership", [.. existing.Cast<object>()]));
                obj.WhenChanged = DateTimeOffset.UtcNow;
                await store.UpdateAsync(obj, ct);
            }

            return Results.Ok();
        })
        .WithName("AddServiceAccountPrincipal")
        .WithTags("ServiceAccounts");

        group.MapDelete("/{id}/principals/{*dn}", async (string id, string dn, IDirectoryStore store, CancellationToken ct) =>
        {
            var obj = await store.GetByGuidAsync(DirectoryConstants.DefaultTenantId, id, ct);
            if (obj == null || obj.IsDeleted) return Results.NotFound();

            var principalDn = Uri.UnescapeDataString(dn);
            var attr = obj.GetAttribute("msDS-GroupMSAMembership");
            var existing = attr?.GetStrings().ToList() ?? [];

            var toRemove = existing.FirstOrDefault(e =>
                string.Equals(e, principalDn, StringComparison.OrdinalIgnoreCase));
            if (toRemove == null) return Results.NotFound();

            existing.Remove(toRemove);
            if (existing.Count > 0)
                obj.SetAttribute("msDS-GroupMSAMembership",
                    new DirectoryAttribute("msDS-GroupMSAMembership", [.. existing.Cast<object>()]));
            else
                obj.Attributes.Remove("msDS-GroupMSAMembership");

            obj.WhenChanged = DateTimeOffset.UtcNow;
            await store.UpdateAsync(obj, ct);

            return Results.Ok();
        })
        .WithName("RemoveServiceAccountPrincipal")
        .WithTags("ServiceAccounts");

        group.MapPut("/{id}/enable", async (string id, IDirectoryStore store, CancellationToken ct) =>
        {
            var obj = await store.GetByGuidAsync(DirectoryConstants.DefaultTenantId, id, ct);
            if (obj == null || obj.IsDeleted) return Results.NotFound();

            obj.UserAccountControl &= ~0x2;
            obj.WhenChanged = DateTimeOffset.UtcNow;
            await store.UpdateAsync(obj, ct);

            return Results.Ok(MapToDetail(obj));
        })
        .WithName("EnableServiceAccount")
        .WithTags("ServiceAccounts");

        group.MapPut("/{id}/disable", async (string id, IDirectoryStore store, CancellationToken ct) =>
        {
            var obj = await store.GetByGuidAsync(DirectoryConstants.DefaultTenantId, id, ct);
            if (obj == null || obj.IsDeleted) return Results.NotFound();

            obj.UserAccountControl |= 0x2;
            obj.WhenChanged = DateTimeOffset.UtcNow;
            await store.UpdateAsync(obj, ct);

            return Results.Ok(MapToDetail(obj));
        })
        .WithName("DisableServiceAccount")
        .WithTags("ServiceAccounts");

        return group;
    }

    private static ServiceAccountSummary MapToSummary(DirectoryObject obj)
    {
        var isGmsa = obj.ObjectClass.Contains("msDS-GroupManagedServiceAccount");
        var principals = obj.GetAttribute("msDS-GroupMSAMembership")?.GetStrings().ToList() ?? [];
        var interval = 30;
        var intervalAttr = obj.GetAttribute("msDS-ManagedPasswordInterval");
        if (intervalAttr != null && int.TryParse(intervalAttr.GetFirstString(), out var parsed))
            interval = parsed;

        return new ServiceAccountSummary
        {
            ObjectGuid = obj.ObjectGuid,
            Name = obj.Cn ?? obj.SAMAccountName ?? "",
            Dn = obj.DistinguishedName,
            Type = isGmsa ? "gMSA" : "MSA",
            DnsHostName = obj.DnsHostName ?? "",
            PasswordInterval = interval,
            PrincipalCount = principals.Count,
            Enabled = (obj.UserAccountControl & 0x2) == 0,
            WhenCreated = obj.WhenCreated,
            WhenChanged = obj.WhenChanged,
        };
    }

    private static ServiceAccountDetail MapToDetail(DirectoryObject obj)
    {
        var isGmsa = obj.ObjectClass.Contains("msDS-GroupManagedServiceAccount");
        var principals = obj.GetAttribute("msDS-GroupMSAMembership")?.GetStrings().ToList() ?? [];
        var interval = 30;
        var intervalAttr = obj.GetAttribute("msDS-ManagedPasswordInterval");
        if (intervalAttr != null && int.TryParse(intervalAttr.GetFirstString(), out var parsed))
            interval = parsed;

        return new ServiceAccountDetail
        {
            ObjectGuid = obj.ObjectGuid,
            Name = obj.Cn ?? obj.SAMAccountName ?? "",
            Dn = obj.DistinguishedName,
            SamAccountName = obj.SAMAccountName ?? "",
            Type = isGmsa ? "gMSA" : "MSA",
            DnsHostName = obj.DnsHostName ?? "",
            PasswordInterval = interval,
            Principals = principals,
            ServicePrincipalNames = obj.ServicePrincipalName,
            Description = obj.Description ?? "",
            Enabled = (obj.UserAccountControl & 0x2) == 0,
            ObjectSid = obj.ObjectSid ?? "",
            WhenCreated = obj.WhenCreated,
            WhenChanged = obj.WhenChanged,
        };
    }
}

public record CreateServiceAccountRequest(
    string Name,
    string Type = "gmsa",
    string DnsHostName = null,
    List<string> Principals = null,
    int? PasswordInterval = null,
    List<string> ServicePrincipalNames = null
);

public record UpdateServiceAccountRequest(
    string DnsHostName = null,
    string Description = null,
    int? PasswordInterval = null,
    List<string> ServicePrincipalNames = null
);

public record AddPrincipalRequest(string PrincipalDn);

public class ServiceAccountSummary
{
    public string ObjectGuid { get; init; } = "";
    public string Name { get; init; } = "";
    public string Dn { get; init; } = "";
    public string Type { get; init; } = "";
    public string DnsHostName { get; init; } = "";
    public int PasswordInterval { get; init; }
    public int PrincipalCount { get; init; }
    public bool Enabled { get; init; }
    public DateTimeOffset WhenCreated { get; init; }
    public DateTimeOffset WhenChanged { get; init; }
}

public class ServiceAccountDetail : ServiceAccountSummary
{
    public string SamAccountName { get; init; } = "";
    public List<string> Principals { get; init; } = [];
    public List<string> ServicePrincipalNames { get; init; } = [];
    public string Description { get; init; } = "";
    public string ObjectSid { get; init; } = "";
}
