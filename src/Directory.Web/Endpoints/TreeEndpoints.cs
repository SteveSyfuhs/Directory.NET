using Directory.Core;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Web.Models;

namespace Directory.Web.Endpoints;

public static class TreeEndpoints
{
    private static readonly HashSet<string> ContainerClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "container", "organizationalUnit", "builtinDomain", "domain", "domainDNS", "configuration", "dMD"
    };

    public static RouteGroupBuilder MapTreeEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/roots", async (INamingContextService ncService, IDirectoryStore store) =>
        {
            var ncs = ncService.GetAllNamingContexts();
            var nodes = new List<TreeNodeDto>();

            foreach (var nc in ncs)
            {
                var obj = await store.GetByDnAsync(DirectoryConstants.DefaultTenantId, nc.Dn);
                var name = obj?.Cn ?? nc.Dn;
                var objectClass = obj?.ObjectClass.LastOrDefault() ?? "domain";

                nodes.Add(new TreeNodeDto(
                    Dn: nc.Dn,
                    Name: name,
                    ObjectClass: objectClass,
                    ObjectGuid: obj?.ObjectGuid,
                    HasChildren: true,
                    Icon: GetIcon(objectClass)
                ));
            }

            return Results.Ok(nodes);
        })
        .WithName("GetTreeRoots")
        .WithTags("Tree");

        group.MapGet("/children", async (string parentDn, IDirectoryStore store) =>
        {
            var children = await store.GetChildrenAsync(DirectoryConstants.DefaultTenantId, parentDn);
            var nodes = children
                .Where(c => !c.IsDeleted)
                .Select(c =>
                {
                    var objectClass = c.ObjectClass.LastOrDefault() ?? "top";
                    var hasChildren = c.ObjectClass.Any(oc => ContainerClasses.Contains(oc));

                    return new TreeNodeDto(
                        Dn: c.DistinguishedName,
                        Name: c.Cn ?? c.DistinguishedName,
                        ObjectClass: objectClass,
                        ObjectGuid: c.ObjectGuid,
                        HasChildren: hasChildren,
                        Icon: GetIcon(objectClass)
                    );
                })
                .OrderBy(n => !n.HasChildren) // Containers first
                .ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return Results.Ok(nodes);
        })
        .WithName("GetTreeChildren")
        .WithTags("Tree");

        return group;
    }

    private static string GetIcon(string objectClass) => objectClass.ToLowerInvariant() switch
    {
        "organizationalunit" => "pi-folder-open",
        "container" or "builtindomain" => "pi-box",
        "user" => "pi-user",
        "computer" => "pi-desktop",
        "group" => "pi-users",
        "grouppolicycontainer" => "pi-file-edit",
        _ => "pi-circle",
    };
}
