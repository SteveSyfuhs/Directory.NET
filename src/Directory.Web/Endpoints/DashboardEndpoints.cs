using Directory.Core;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Web.Models;

namespace Directory.Web.Endpoints;

public static class DashboardEndpoints
{
    public static RouteGroupBuilder MapDashboardEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/summary", async (
            IDirectoryStore store,
            INamingContextService ncService,
            DomainConfiguration domainConfig) =>
        {
            var domainDn = ncService.GetDomainNc().Dn;

            // Search for users (objectClass=user but not computer)
            var userFilter = new AndFilterNode([
                new EqualityFilterNode("objectClass", "user"),
                new NotFilterNode(new EqualityFilterNode("objectClass", "computer"))
            ]);
            var users = await store.SearchAsync(DirectoryConstants.DefaultTenantId, domainDn, SearchScope.WholeSubtree, userFilter, ["objectGuid"], pageSize: 1);

            // Search for computers
            var computerFilter = new EqualityFilterNode("objectClass", "computer");
            var computers = await store.SearchAsync(DirectoryConstants.DefaultTenantId, domainDn, SearchScope.WholeSubtree, computerFilter, ["objectGuid"], pageSize: 1);

            // Search for groups
            var groupFilter = new EqualityFilterNode("objectClass", "group");
            var groups = await store.SearchAsync(DirectoryConstants.DefaultTenantId, domainDn, SearchScope.WholeSubtree, groupFilter, ["objectGuid"], pageSize: 1);

            // Search for OUs
            var ouFilter = new EqualityFilterNode("objectClass", "organizationalUnit");
            var ous = await store.SearchAsync(DirectoryConstants.DefaultTenantId, domainDn, SearchScope.WholeSubtree, ouFilter, ["objectGuid"], pageSize: 1);

            var summary = new DashboardSummaryDto(
                UserCount: users.TotalEstimate,
                ComputerCount: computers.TotalEstimate,
                GroupCount: groups.TotalEstimate,
                OuCount: ous.TotalEstimate,
                TotalObjects: users.TotalEstimate + computers.TotalEstimate + groups.TotalEstimate + ous.TotalEstimate,
                DomainName: domainConfig.DomainDnsName,
                DomainDn: domainConfig.DomainDn,
                DomainSid: domainConfig.DomainSid,
                FunctionalLevel: domainConfig.DomainFunctionalLevel
            );

            return Results.Ok(summary);
        })
        .WithName("GetDashboardSummary")
        .WithTags("Dashboard");

        group.MapGet("/dc-health", async (
            IDirectoryStore store,
            INamingContextService ncService) =>
        {
            var configDn = ncService.GetConfigurationDn();
            var sitesDn = $"CN=Sites,{configDn}";

            var serverFilter = new EqualityFilterNode("objectClass", "server");
            var result = await store.SearchAsync(DirectoryConstants.DefaultTenantId, sitesDn, SearchScope.WholeSubtree, serverFilter, null, pageSize: 100);

            var healthList = result.Entries.Select(server =>
            {
                var hostname = server.DnsHostName ?? server.Cn ?? "Unknown";
                var siteName = ExtractSiteName(server.DistinguishedName);
                var lastHeartbeat = server.WhenChanged;
                var isHealthy = (DateTimeOffset.UtcNow - lastHeartbeat).TotalMinutes <= 10;

                return new DcHealthDto(
                    Hostname: hostname,
                    SiteName: siteName,
                    ServerDn: server.DistinguishedName,
                    LastHeartbeat: lastHeartbeat,
                    IsHealthy: isHealthy
                );
            }).ToList();

            return Results.Ok(healthList);
        })
        .WithName("GetDcHealth")
        .WithTags("Dashboard");

        group.MapGet("/recent-changes", async (
            IDirectoryStore store,
            INamingContextService ncService) =>
        {
            var domainDn = ncService.GetDomainNc().Dn;

            // Search with no filter to get all objects, we'll sort by whenChanged
            var result = await store.SearchAsync(DirectoryConstants.DefaultTenantId, domainDn, SearchScope.WholeSubtree, null, null, pageSize: 20);

            var recentChanges = result.Entries
                .OrderByDescending(e => e.WhenChanged)
                .Take(20)
                .Select(e => MapToSummary(e))
                .ToList();

            return Results.Ok(recentChanges);
        })
        .WithName("GetRecentChanges")
        .WithTags("Dashboard");

        return group;
    }

    private static string ExtractSiteName(string serverDn)
    {
        // Server DN: CN=DC1,CN=Servers,CN=Default-First-Site-Name,CN=Sites,...
        var parts = serverDn.Split(',');
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (parts[i + 1].Equals("CN=Sites", StringComparison.OrdinalIgnoreCase) && i > 0)
            {
                var siteRdn = parts[i];
                return siteRdn.StartsWith("CN=", StringComparison.OrdinalIgnoreCase) ? siteRdn[3..] : siteRdn;
            }
        }
        return "Unknown";
    }

    internal static ObjectSummaryDto MapToSummary(DirectoryObject obj)
    {
        var isDisabled = (obj.UserAccountControl & 0x2) != 0;
        bool? enabled = obj.ObjectClass.Contains("user") || obj.ObjectClass.Contains("computer")
            ? !isDisabled
            : null;

        return new ObjectSummaryDto(
            Dn: obj.DistinguishedName,
            ObjectGuid: obj.ObjectGuid,
            Name: obj.DisplayName ?? obj.Cn,
            ObjectClass: obj.ObjectClass.LastOrDefault() ?? "top",
            Description: obj.Description,
            SAMAccountName: obj.SAMAccountName,
            Enabled: enabled,
            WhenCreated: obj.WhenCreated,
            WhenChanged: obj.WhenChanged
        );
    }
}
