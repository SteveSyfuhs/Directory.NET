using System.Security.Cryptography;
using Directory.Core;
using Directory.Core.Interfaces;
using Directory.Core.Models;

namespace Directory.Web.Endpoints;

public static class TrustEndpoints
{
    public static RouteGroupBuilder MapTrustEndpoints(this RouteGroupBuilder group)
    {
        // GET /api/v1/trusts - List all trust relationships
        group.MapGet("/", async (IDirectoryStore store, INamingContextService ncService) =>
        {
            var domainDn = ncService.GetDomainNc().Dn;
            var systemDn = $"CN=System,{domainDn}";
            var filter = new EqualityFilterNode("objectCategory", "trustedDomain");

            var result = await store.SearchAsync(DirectoryConstants.DefaultTenantId, systemDn,
                SearchScope.SingleLevel, filter,
                null, pageSize: 500);

            return Results.Ok(result.Entries
                .Where(e => !e.IsDeleted)
                .Select(MapTrustResponse));
        })
        .WithName("ListTrusts")
        .WithTags("Trusts");

        // GET /api/v1/trusts/{id} - Get a specific trust by objectGuid
        group.MapGet("/{id}", async (IDirectoryStore store, string id) =>
        {
            var obj = await store.GetByGuidAsync(DirectoryConstants.DefaultTenantId, id);
            if (obj is null || obj.IsDeleted || obj.ObjectCategory != "trustedDomain")
                return Results.Problem(statusCode: 404, detail: "Trust not found");

            return Results.Ok(MapTrustResponse(obj));
        })
        .WithName("GetTrust")
        .WithTags("Trusts");

        // POST /api/v1/trusts - Create a new trust relationship
        group.MapPost("/", async (IDirectoryStore store, INamingContextService ncService, CreateTrustRequest request) =>
        {
            if (string.IsNullOrWhiteSpace(request.TrustPartner))
                return Results.Problem(statusCode: 400, detail: "trustPartner is required");

            var domainDn = ncService.GetDomainNc().Dn;
            var trustPartnerUpper = request.TrustPartner.Trim().ToUpperInvariant();
            var dn = $"CN={trustPartnerUpper},CN=System,{domainDn}";

            var existing = await store.GetByDnAsync(DirectoryConstants.DefaultTenantId, dn);
            if (existing is not null && !existing.IsDeleted)
                return Results.Problem(statusCode: 409, detail: $"Trust already exists for {trustPartnerUpper}");

            // Derive inter-realm key from shared secret
            byte[] interRealmKey = null;
            if (!string.IsNullOrEmpty(request.SharedSecret))
            {
                var domainDnsName = DomainDnToDnsName(domainDn);
                interRealmKey = DeriveInterRealmKey(request.SharedSecret, domainDnsName, trustPartnerUpper);
            }

            var now = DateTimeOffset.UtcNow;
            var trustDirection = request.TrustDirection ?? 3; // Default: Bidirectional
            var trustType = request.TrustType ?? 2; // Default: Uplevel
            var trustAttributes = request.TrustAttributes ?? 0;

            var obj = new DirectoryObject
            {
                Id = dn.ToLowerInvariant(),
                TenantId = DirectoryConstants.DefaultTenantId,
                DistinguishedName = dn,
                Cn = trustPartnerUpper,
                DomainDn = domainDn,
                ObjectCategory = "trustedDomain",
                ObjectClass = ["top", "leaf", "trustedDomain"],
                ParentDn = $"CN=System,{domainDn}",
                WhenCreated = now,
                WhenChanged = now,
            };

            obj.SetAttribute("trustPartner", new DirectoryAttribute("trustPartner", trustPartnerUpper));
            obj.SetAttribute("flatName", new DirectoryAttribute("flatName", request.FlatName?.ToUpperInvariant() ?? GetNetBiosFromDns(trustPartnerUpper)));
            obj.SetAttribute("trustDirection", new DirectoryAttribute("trustDirection", trustDirection.ToString()));
            obj.SetAttribute("trustType", new DirectoryAttribute("trustType", trustType.ToString()));
            obj.SetAttribute("trustAttributes", new DirectoryAttribute("trustAttributes", trustAttributes.ToString()));
            obj.SetAttribute("securityIdentifier", new DirectoryAttribute("securityIdentifier", request.SecurityIdentifier ?? ""));

            if (interRealmKey is not null)
                obj.SetAttribute("interRealmKey", new DirectoryAttribute("interRealmKey", Convert.ToBase64String(interRealmKey)));

            await store.CreateAsync(obj);

            return Results.Created($"/api/v1/trusts/{obj.ObjectGuid}", MapTrustResponse(obj));
        })
        .WithName("CreateTrust")
        .WithTags("Trusts");

        // PUT /api/v1/trusts/{id} - Update trust properties
        group.MapPut("/{id}", async (IDirectoryStore store, INamingContextService ncService, string id, UpdateTrustRequest request) =>
        {
            var obj = await store.GetByGuidAsync(DirectoryConstants.DefaultTenantId, id);
            if (obj is null || obj.IsDeleted || obj.ObjectCategory != "trustedDomain")
                return Results.Problem(statusCode: 404, detail: "Trust not found");

            if (request.TrustDirection.HasValue)
                obj.SetAttribute("trustDirection", new DirectoryAttribute("trustDirection", request.TrustDirection.Value.ToString()));

            if (request.TrustType.HasValue)
                obj.SetAttribute("trustType", new DirectoryAttribute("trustType", request.TrustType.Value.ToString()));

            if (request.TrustAttributes.HasValue)
                obj.SetAttribute("trustAttributes", new DirectoryAttribute("trustAttributes", request.TrustAttributes.Value.ToString()));

            if (request.FlatName is not null)
                obj.SetAttribute("flatName", new DirectoryAttribute("flatName", request.FlatName.ToUpperInvariant()));

            if (request.SecurityIdentifier is not null)
                obj.SetAttribute("securityIdentifier", new DirectoryAttribute("securityIdentifier", request.SecurityIdentifier));

            if (!string.IsNullOrEmpty(request.SharedSecret))
            {
                var domainDn = ncService.GetDomainNc().Dn;
                var domainDnsName = DomainDnToDnsName(domainDn);
                var trustPartner = obj.GetAttribute("trustPartner")?.GetFirstString() ?? obj.Cn ?? "";
                var interRealmKey = DeriveInterRealmKey(request.SharedSecret, domainDnsName, trustPartner);
                obj.SetAttribute("interRealmKey", new DirectoryAttribute("interRealmKey", Convert.ToBase64String(interRealmKey)));
            }

            await store.UpdateAsync(obj);
            return Results.Ok(MapTrustResponse(obj));
        })
        .WithName("UpdateTrust")
        .WithTags("Trusts");

        // DELETE /api/v1/trusts/{id} - Remove a trust
        group.MapDelete("/{id}", async (IDirectoryStore store, string id) =>
        {
            var obj = await store.GetByGuidAsync(DirectoryConstants.DefaultTenantId, id);
            if (obj is null || obj.IsDeleted || obj.ObjectCategory != "trustedDomain")
                return Results.Problem(statusCode: 404, detail: "Trust not found");

            await store.DeleteAsync(DirectoryConstants.DefaultTenantId, obj.DistinguishedName, hardDelete: true);
            return Results.NoContent();
        })
        .WithName("DeleteTrust")
        .WithTags("Trusts");

        // POST /api/v1/trusts/{id}/verify - Validate trust is working
        group.MapPost("/{id}/verify", async (IDirectoryStore store, string id) =>
        {
            var obj = await store.GetByGuidAsync(DirectoryConstants.DefaultTenantId, id);
            if (obj is null || obj.IsDeleted || obj.ObjectCategory != "trustedDomain")
                return Results.Problem(statusCode: 404, detail: "Trust not found");

            var trustPartner = obj.GetAttribute("trustPartner")?.GetFirstString() ?? "";
            var directionStr = obj.GetAttribute("trustDirection")?.GetFirstString();
            var direction = int.TryParse(directionStr, out var d) ? d : 0;
            var hasKey = !string.IsNullOrEmpty(obj.GetAttribute("interRealmKey")?.GetFirstString());

            var issues = new List<string>();

            if (string.IsNullOrEmpty(trustPartner))
                issues.Add("Trust partner DNS name is not configured");

            if (direction == 0)
                issues.Add("Trust direction is disabled");

            if (!hasKey)
                issues.Add("Inter-realm key is not configured; referral tickets cannot be generated");

            var isOperational = issues.Count == 0;

            return Results.Ok(new
            {
                IsOperational = isOperational,
                TrustPartner = trustPartner,
                Direction = direction,
                HasInterRealmKey = hasKey,
                Issues = issues,
                Message = isOperational ? "Trust is operational" : "Trust has configuration issues",
                VerifiedAt = DateTimeOffset.UtcNow,
            });
        })
        .WithName("VerifyTrust")
        .WithTags("Trusts");

        return group;
    }

    private static object MapTrustResponse(DirectoryObject obj)
    {
        var trustPartner = obj.GetAttribute("trustPartner")?.GetFirstString() ?? obj.Cn ?? "";
        var flatName = obj.GetAttribute("flatName")?.GetFirstString() ?? "";
        var directionStr = obj.GetAttribute("trustDirection")?.GetFirstString();
        var direction = int.TryParse(directionStr, out var d) ? d : 0;
        var typeStr = obj.GetAttribute("trustType")?.GetFirstString();
        var trustType = int.TryParse(typeStr, out var t) ? t : 0;
        var attrsStr = obj.GetAttribute("trustAttributes")?.GetFirstString();
        var trustAttributes = int.TryParse(attrsStr, out var a) ? a : 0;
        var sid = obj.GetAttribute("securityIdentifier")?.GetFirstString() ?? "";
        var hasKey = !string.IsNullOrEmpty(obj.GetAttribute("interRealmKey")?.GetFirstString());

        return new
        {
            obj.ObjectGuid,
            TrustPartner = trustPartner,
            FlatName = flatName,
            TrustDirection = direction,
            TrustDirectionName = direction switch
            {
                0 => "Disabled",
                1 => "Inbound",
                2 => "Outbound",
                3 => "Bidirectional",
                _ => "Unknown",
            },
            TrustType = trustType,
            TrustTypeName = trustType switch
            {
                1 => "Downlevel",
                2 => "Uplevel",
                3 => "MIT",
                4 => "DCE",
                5 => "External",
                6 => "Forest",
                7 => "ParentChild",
                8 => "CrossLink",
                9 => "Realm",
                _ => "Unknown",
            },
            TrustAttributes = trustAttributes,
            SecurityIdentifier = sid,
            HasInterRealmKey = hasKey,
            obj.WhenCreated,
            obj.WhenChanged,
            obj.DistinguishedName,
        };
    }

    private static string DomainDnToDnsName(string domainDn)
    {
        var parts = domainDn.Split(',', StringSplitOptions.TrimEntries)
            .Where(p => p.StartsWith("DC=", StringComparison.OrdinalIgnoreCase))
            .Select(p => p[3..]);
        return string.Join(".", parts).ToUpperInvariant();
    }

    private static string GetNetBiosFromDns(string dnsName)
    {
        var parts = dnsName.Split('.');
        return parts.Length > 0 ? parts[0].ToUpperInvariant() : dnsName.ToUpperInvariant();
    }

    private static byte[] DeriveInterRealmKey(string sharedSecret, string sourceRealm, string targetRealm)
    {
        var salt = $"krbtgt/{targetRealm.ToUpperInvariant()}@{sourceRealm.ToUpperInvariant()}";
        var saltBytes = System.Text.Encoding.UTF8.GetBytes(salt);
        return Rfc2898DeriveBytes.Pbkdf2(
            System.Text.Encoding.UTF8.GetBytes(sharedSecret),
            saltBytes,
            4096,
            HashAlgorithmName.SHA256,
            32);
    }
}

public record CreateTrustRequest(
    string TrustPartner,
    string FlatName = null,
    int? TrustDirection = null,
    int? TrustType = null,
    int? TrustAttributes = null,
    string SecurityIdentifier = null,
    string SharedSecret = null);

public record UpdateTrustRequest(
    string FlatName = null,
    int? TrustDirection = null,
    int? TrustType = null,
    int? TrustAttributes = null,
    string SecurityIdentifier = null,
    string SharedSecret = null);
