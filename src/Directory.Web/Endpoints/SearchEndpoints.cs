using System.Text;
using Directory.Core;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Core.Telemetry;
using Directory.Web.Models;
using Directory.Web.Services;

namespace Directory.Web.Endpoints;

public static class SearchEndpoints
{
    // In-memory saved searches (per-session; production would persist these)
    private static readonly List<SavedSearch> SavedSearches = [];

    public static RouteGroupBuilder MapSearchEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/", async (AdvancedSearchRequest request, IDirectoryStore store, INamingContextService ncService) =>
        {
            var searchValidation =
                ValidationHelper.ValidateMaxLength(request.Filter, "filter", ValidationHelper.MaxStringLength) ??
                (request.BaseDn != null ? ValidationHelper.ValidateDn(request.BaseDn, "baseDn") : null);
            if (searchValidation != null) return searchValidation;

            // Validate filter syntax
            FilterNode filterNode;
            try
            {
                filterNode = FilterBuilder.Parse(request.Filter);
            }
            catch (FormatException ex)
            {
                return Results.Problem(statusCode: 400, detail: $"Invalid LDAP filter: {ex.Message}");
            }

            var baseDn = string.IsNullOrWhiteSpace(request.BaseDn)
                ? ncService.GetDomainNc().Dn
                : request.BaseDn;

            var scope = request.Scope?.ToLowerInvariant() switch
            {
                "base" or "baseobject" => SearchScope.BaseObject,
                "onelevel" or "singlelevel" or "one" => SearchScope.SingleLevel,
                "subtree" or "wholesubtree" or "sub" => SearchScope.WholeSubtree,
                _ => SearchScope.WholeSubtree
            };

            var maxResults = Math.Clamp(request.MaxResults ?? 1000, 1, 10000);

            var result = await store.SearchAsync(
                DirectoryConstants.DefaultTenantId,
                baseDn,
                scope,
                filterNode,
                request.Attributes?.ToArray(),
                sizeLimit: maxResults,
                pageSize: maxResults);

            var items = result.Entries
                .Where(e => !e.IsDeleted)
                .Select(e => MapToSearchResult(e, request.Attributes))
                .ToList();

            DirectoryMetrics.SearchOperations.Add(1);

            return Results.Ok(new AdvancedSearchResult(items, result.TotalEstimate));
        })
        .WithName("AdvancedSearch")
        .WithTags("Search");

        group.MapPost("/export", async (AdvancedSearchRequest request, IDirectoryStore store, INamingContextService ncService) =>
        {
            var exportValidation =
                ValidationHelper.ValidateMaxLength(request.Filter, "filter", ValidationHelper.MaxStringLength) ??
                (request.BaseDn != null ? ValidationHelper.ValidateDn(request.BaseDn, "baseDn") : null);
            if (exportValidation != null) return exportValidation;

            FilterNode filterNode;
            try
            {
                filterNode = FilterBuilder.Parse(request.Filter);
            }
            catch (FormatException ex)
            {
                return Results.Problem(statusCode: 400, detail: $"Invalid LDAP filter: {ex.Message}");
            }

            var baseDn = string.IsNullOrWhiteSpace(request.BaseDn)
                ? ncService.GetDomainNc().Dn
                : request.BaseDn;

            var scope = request.Scope?.ToLowerInvariant() switch
            {
                "base" or "baseobject" => SearchScope.BaseObject,
                "onelevel" or "singlelevel" or "one" => SearchScope.SingleLevel,
                _ => SearchScope.WholeSubtree
            };

            var maxResults = Math.Clamp(request.MaxResults ?? 5000, 1, 50000);

            var result = await store.SearchAsync(
                DirectoryConstants.DefaultTenantId,
                baseDn,
                scope,
                filterNode,
                request.Attributes?.ToArray(),
                sizeLimit: maxResults,
                pageSize: maxResults);

            var entries = result.Entries.Where(e => !e.IsDeleted).ToList();

            // Determine columns: requested attributes or default set
            var columns = request.Attributes?.Count > 0
                ? request.Attributes
                : new List<string> { "distinguishedName", "cn", "objectClass", "sAMAccountName", "description" };

            var csv = new StringBuilder();
            csv.AppendLine(string.Join(",", columns.Select(EscapeCsv)));

            foreach (var entry in entries)
            {
                var row = columns.Select(col =>
                {
                    var attr = entry.GetAttribute(col);
                    if (attr == null) return "";
                    return string.Join("; ", attr.GetStrings());
                });
                csv.AppendLine(string.Join(",", row.Select(EscapeCsv)));
            }

            return Results.Text(csv.ToString(), "text/csv");
        })
        .WithName("ExportSearch")
        .WithTags("Search");

        group.MapGet("/saved", () =>
        {
            return Results.Ok(SavedSearches);
        })
        .WithName("GetSavedSearches")
        .WithTags("Search");

        group.MapPost("/saved", (SavedSearch search) =>
        {
            search.Id = Guid.NewGuid().ToString();
            search.CreatedAt = DateTimeOffset.UtcNow;
            SavedSearches.Add(search);
            return Results.Ok(search);
        })
        .WithName("SaveSearch")
        .WithTags("Search");

        group.MapDelete("/saved/{id}", (string id) =>
        {
            var removed = SavedSearches.RemoveAll(s => s.Id == id);
            return removed > 0 ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteSavedSearch")
        .WithTags("Search");

        return group;
    }

    private static AdvancedSearchResultItem MapToSearchResult(
        Directory.Core.Models.DirectoryObject obj,
        List<string> requestedAttributes)
    {
        var attributes = new Dictionary<string, List<string>>();

        // Always include core attributes
        attributes["distinguishedName"] = [obj.DistinguishedName];
        if (obj.Cn != null) attributes["cn"] = [obj.Cn];
        attributes["objectClass"] = obj.ObjectClass;
        if (obj.SAMAccountName != null) attributes["sAMAccountName"] = [obj.SAMAccountName];
        if (obj.DisplayName != null) attributes["displayName"] = [obj.DisplayName];
        if (obj.Description != null) attributes["description"] = [obj.Description];

        // Include requested attributes
        if (requestedAttributes != null)
        {
            foreach (var attrName in requestedAttributes)
            {
                if (attributes.ContainsKey(attrName)) continue;

                var attr = obj.GetAttribute(attrName);
                if (attr != null)
                {
                    attributes[attrName] = attr.GetStrings().ToList();
                }
            }
        }
        else
        {
            // Include all non-sensitive attributes from the Attributes dictionary
            foreach (var kvp in obj.Attributes)
            {
                var key = kvp.Key.ToLowerInvariant();
                if (key is "nthash" or "kerberoskeys" or "unicodepwd" or "dbcspwd" or "supplementalcredentials")
                    continue;
                if (attributes.ContainsKey(kvp.Key)) continue;
                attributes[kvp.Key] = kvp.Value.GetStrings().ToList();
            }
        }

        return new AdvancedSearchResultItem(
            Dn: obj.DistinguishedName,
            ObjectGuid: obj.ObjectGuid,
            ObjectClass: obj.ObjectClass.LastOrDefault() ?? "top",
            Name: obj.DisplayName ?? obj.Cn,
            Attributes: attributes
        );
    }

    private static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value)) return "\"\"";
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
        return $"\"{value}\"";
    }
}

public record AdvancedSearchRequest(
    string BaseDn,
    string Scope,
    string Filter,
    List<string> Attributes,
    int? MaxResults
);

public record AdvancedSearchResult(
    List<AdvancedSearchResultItem> Items,
    int TotalCount
);

public record AdvancedSearchResultItem(
    string Dn,
    string ObjectGuid,
    string ObjectClass,
    string Name,
    Dictionary<string, List<string>> Attributes
);

public class SavedSearch
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string BaseDn { get; set; } = "";
    public string Scope { get; set; } = "subtree";
    public string Filter { get; set; } = "";
    public List<string> Attributes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
