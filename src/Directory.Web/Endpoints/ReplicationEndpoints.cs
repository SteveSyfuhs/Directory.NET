using Directory.Core;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Web.Models;

namespace Directory.Web.Endpoints;

public static class ReplicationEndpoints
{
    public static RouteGroupBuilder MapReplicationEndpoints(this RouteGroupBuilder group)
    {
        // Replication status summary
        group.MapGet("/status", async (IDirectoryStore store, INamingContextService ncService) =>
        {
            var configDn = ncService.GetConfigurationNc().Dn;
            var filter = new EqualityFilterNode("objectClass", "nTDSDSA");

            var result = await store.SearchAsync(DirectoryConstants.DefaultTenantId, configDn,
                SearchScope.WholeSubtree, filter,
                ["distinguishedName", "invocationId", "whenChanged"], pageSize: 50);

            var dcs = result.Entries
                .Where(e => !e.IsDeleted)
                .Select(ntds => new
                {
                    ntds.ObjectGuid,
                    ntds.DistinguishedName,
                    InvocationId = ntds.GetAttribute("invocationId")?.GetFirstString() ?? "",
                    LastUpdated = ntds.WhenChanged,
                }).ToList();

            return Results.Ok(new
            {
                DcCount = dcs.Count,
                DomainControllers = dcs,
                HealthStatus = dcs.Count > 0 ? "Healthy" : "NoControllers",
            });
        })
        .WithName("ReplicationStatus")
        .WithTags("Replication");

        // Get FSMO role holders
        group.MapGet("/fsmo", async (IDirectoryStore store, INamingContextService ncService, DomainConfiguration domainConfig) =>
        {
            var roles = new Dictionary<string, string>();

            // Check domain root for PDC Emulator
            var domainRoot = await store.GetByDnAsync(DirectoryConstants.DefaultTenantId, domainConfig.DomainDn);
            if (domainRoot is not null)
            {
                var fsmoRole = domainRoot.GetAttribute("fSMORoleOwner")?.GetFirstString();
                if (fsmoRole is not null)
                    roles["PDC Emulator"] = fsmoRole;
            }

            // Search for objects with fSMORoleOwner attribute
            var filter = new PresenceFilterNode("fSMORoleOwner");
            var result = await store.SearchAsync(DirectoryConstants.DefaultTenantId, "",
                SearchScope.WholeSubtree, filter,
                ["distinguishedName", "fSMORoleOwner", "name"], pageSize: 10);

            foreach (var obj in result.Entries.Where(e => !e.IsDeleted))
            {
                var roleOwner = obj.GetAttribute("fSMORoleOwner")?.GetFirstString() ?? "";
                var dn = obj.DistinguishedName;

                if (dn.Contains("RID Manager", StringComparison.OrdinalIgnoreCase))
                    roles["RID Master"] = roleOwner;
                else if (dn.Contains("Infrastructure", StringComparison.OrdinalIgnoreCase))
                    roles["Infrastructure Master"] = roleOwner;
                else if (dn.Contains("CN=Partitions", StringComparison.OrdinalIgnoreCase))
                    roles["Domain Naming Master"] = roleOwner;
                else if (dn.Contains("CN=Schema", StringComparison.OrdinalIgnoreCase))
                    roles["Schema Master"] = roleOwner;
            }

            return Results.Ok(roles);
        })
        .WithName("FsmoRoles")
        .WithTags("Replication");

        return group;
    }
}
