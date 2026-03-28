using Directory.Core;
using Directory.Core.Interfaces;
using Directory.Core.Models;

namespace Directory.Web.Endpoints;

public static class SiteEndpoints
{
    public static RouteGroupBuilder MapSiteEndpoints(this RouteGroupBuilder group)
    {
        // GET /api/v1/sites — list all sites
        group.MapGet("/", async (IDirectoryStore store, INamingContextService ncService) =>
        {
            var configDn = ncService.GetConfigurationNc().Dn;
            var sitesDn = $"CN=Sites,{configDn}";
            var filter = new EqualityFilterNode("objectClass", "site");

            var result = await store.SearchAsync(DirectoryConstants.DefaultTenantId, sitesDn,
                SearchScope.SingleLevel, filter,
                ["distinguishedName", "name", "description", "whenCreated", "whenChanged"], pageSize: 100);

            return Results.Ok(result.Entries
                .Where(e => !e.IsDeleted)
                .Select(s => new
                {
                    s.ObjectGuid,
                    Name = s.Cn ?? "",
                    Description = s.Description ?? "",
                    s.DistinguishedName,
                    s.WhenCreated,
                    s.WhenChanged,
                }));
        })
        .WithName("ListSites")
        .WithTags("Sites");

        // POST /api/v1/sites — create site
        group.MapPost("/", async (IDirectoryStore store, INamingContextService ncService, CreateSiteRequest request) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.Problem(statusCode: 400, detail: "Name is required");

            var configDn = ncService.GetConfigurationNc().Dn;
            var sitesDn = $"CN=Sites,{configDn}";
            var dn = $"CN={request.Name},{sitesDn}";

            var existing = await store.GetByDnAsync(DirectoryConstants.DefaultTenantId, dn);
            if (existing is not null && !existing.IsDeleted)
                return Results.Problem(statusCode: 409, detail: $"Site '{request.Name}' already exists");

            var now = DateTimeOffset.UtcNow;
            var site = new DirectoryObject
            {
                Id = dn.ToLowerInvariant(),
                TenantId = DirectoryConstants.DefaultTenantId,
                DistinguishedName = dn,
                Cn = request.Name,
                DomainDn = OuEndpoints.ExtractDomainDn(dn),
                ObjectCategory = "site",
                ObjectClass = ["top", "site"],
                ParentDn = sitesDn,
                Description = request.Description,
                WhenCreated = now,
                WhenChanged = now,
            };

            await store.CreateAsync(site);

            // Create the Servers container inside the site
            var serversDn = $"CN=Servers,{dn}";
            var serversContainer = new DirectoryObject
            {
                Id = serversDn.ToLowerInvariant(),
                TenantId = DirectoryConstants.DefaultTenantId,
                DistinguishedName = serversDn,
                Cn = "Servers",
                DomainDn = OuEndpoints.ExtractDomainDn(dn),
                ObjectCategory = "serversContainer",
                ObjectClass = ["top", "serversContainer"],
                ParentDn = dn,
                WhenCreated = now,
                WhenChanged = now,
            };
            await store.CreateAsync(serversContainer);

            return Results.Created($"/api/v1/sites/{site.ObjectGuid}", new
            {
                site.ObjectGuid,
                Name = site.Cn,
                site.Description,
                site.DistinguishedName,
            });
        })
        .WithName("CreateSite")
        .WithTags("Sites");

        // PUT /api/v1/sites/{name} — update site
        group.MapPut("/{name}", async (IDirectoryStore store, INamingContextService ncService, string name, UpdateSiteRequest request) =>
        {
            var configDn = ncService.GetConfigurationNc().Dn;
            var dn = $"CN={name},CN=Sites,{configDn}";
            var obj = await store.GetByDnAsync(DirectoryConstants.DefaultTenantId, dn);
            if (obj is null || obj.IsDeleted)
                return Results.Problem(statusCode: 404, detail: "Site not found");

            if (request.Description is not null)
                obj.Description = request.Description;

            obj.WhenChanged = DateTimeOffset.UtcNow;
            await store.UpdateAsync(obj);
            return Results.Ok(new { obj.ObjectGuid, Name = obj.Cn, obj.Description, obj.DistinguishedName });
        })
        .WithName("UpdateSite")
        .WithTags("Sites");

        // DELETE /api/v1/sites/{name} — delete site
        group.MapDelete("/{name}", async (IDirectoryStore store, INamingContextService ncService, string name) =>
        {
            var configDn = ncService.GetConfigurationNc().Dn;
            var dn = $"CN={name},CN=Sites,{configDn}";
            var obj = await store.GetByDnAsync(DirectoryConstants.DefaultTenantId, dn);
            if (obj is null || obj.IsDeleted)
                return Results.Problem(statusCode: 404, detail: "Site not found");

            await store.DeleteAsync(DirectoryConstants.DefaultTenantId, dn, hardDelete: true);
            return Results.NoContent();
        })
        .WithName("DeleteSite")
        .WithTags("Sites");

        // GET /api/v1/sites/{siteName}/subnets — list subnets for a site
        group.MapGet("/{siteName}/subnets", async (IDirectoryStore store, INamingContextService ncService, string siteName) =>
        {
            var configDn = ncService.GetConfigurationNc().Dn;
            var subnetsDn = $"CN=Subnets,CN=Sites,{configDn}";
            var filter = new PresenceFilterNode("siteObject");

            var result = await store.SearchAsync(DirectoryConstants.DefaultTenantId, subnetsDn,
                SearchScope.SingleLevel, filter,
                ["distinguishedName", "name", "siteObject", "description", "location"], pageSize: 500);

            var subnets = result.Entries
                .Where(s => !s.IsDeleted &&
                    (s.GetAttribute("siteObject")?.GetFirstString() ?? "").Contains(siteName, StringComparison.OrdinalIgnoreCase))
                .Select(s => new
                {
                    s.ObjectGuid,
                    Name = s.Cn ?? "",
                    Description = s.Description ?? "",
                    SiteObject = s.GetAttribute("siteObject")?.GetFirstString() ?? "",
                    Location = s.GetAttribute("location")?.GetFirstString() ?? "",
                    s.DistinguishedName,
                });

            return Results.Ok(subnets);
        })
        .WithName("ListSiteSubnets")
        .WithTags("Sites");

        // GET /api/v1/sites/{siteName}/servers — list DCs in a site
        group.MapGet("/{siteName}/servers", async (IDirectoryStore store, INamingContextService ncService, string siteName) =>
        {
            var configDn = ncService.GetConfigurationNc().Dn;
            var serversDn = $"CN=Servers,CN={siteName},CN=Sites,{configDn}";
            var filter = new EqualityFilterNode("objectClass", "server");

            var result = await store.SearchAsync(DirectoryConstants.DefaultTenantId, serversDn,
                SearchScope.SingleLevel, filter,
                ["distinguishedName", "name", "serverReference", "dNSHostName"], pageSize: 100);

            var servers = result.Entries
                .Where(s => !s.IsDeleted)
                .Select(s => new
                {
                    s.ObjectGuid,
                    Name = s.Cn ?? "",
                    DnsHostName = s.DnsHostName ?? s.GetAttribute("dNSHostName")?.GetFirstString() ?? "",
                    ServerReference = s.GetAttribute("serverReference")?.GetFirstString() ?? "",
                    s.DistinguishedName,
                });

            return Results.Ok(servers);
        })
        .WithName("ListSiteServers")
        .WithTags("Sites");

        // POST /api/v1/sites/subnets — create a new subnet
        group.MapPost("/subnets", async (IDirectoryStore store, INamingContextService ncService, CreateSubnetRequest request) =>
        {
            var configDn = ncService.GetConfigurationNc().Dn;
            var subnetsDn = $"CN=Subnets,CN=Sites,{configDn}";
            var dn = $"CN={request.SubnetAddress},{subnetsDn}";

            var existing = await store.GetByDnAsync(DirectoryConstants.DefaultTenantId, dn);
            if (existing != null)
                return Results.Problem(statusCode: 409, detail: $"Subnet already exists at {dn}");

            var now = DateTimeOffset.UtcNow;

            var subnet = new DirectoryObject
            {
                Id = dn.ToLowerInvariant(),
                TenantId = DirectoryConstants.DefaultTenantId,
                DistinguishedName = dn,
                Cn = request.SubnetAddress,
                DomainDn = OuEndpoints.ExtractDomainDn(dn),
                ObjectCategory = "subnet",
                ObjectClass = ["top", "subnet"],
                ParentDn = subnetsDn,
                WhenCreated = now,
                WhenChanged = now,
            };

            if (!string.IsNullOrEmpty(request.SiteDn))
                subnet.SetAttribute("siteObject", new DirectoryAttribute("siteObject", request.SiteDn));
            if (!string.IsNullOrEmpty(request.Description))
                subnet.Description = request.Description;
            if (!string.IsNullOrEmpty(request.Location))
                subnet.SetAttribute("location", new DirectoryAttribute("location", request.Location));

            await store.CreateAsync(subnet);
            return Results.Created($"/api/v1/sites/subnets/{subnet.ObjectGuid}", new { subnet.ObjectGuid });
        })
        .WithName("CreateSubnet")
        .WithTags("Sites");

        // PUT /api/v1/sites/subnets/{id} — update subnet
        group.MapPut("/subnets/{id}", async (IDirectoryStore store, string id, UpdateSubnetRequest request) =>
        {
            var obj = await store.GetByGuidAsync(DirectoryConstants.DefaultTenantId, id);
            if (obj is null || obj.IsDeleted || obj.ObjectCategory != "subnet")
                return Results.Problem(statusCode: 404, detail: "Subnet not found");

            if (request.SiteDn is not null)
                obj.SetAttribute("siteObject", new DirectoryAttribute("siteObject", request.SiteDn));
            if (request.Description is not null)
                obj.Description = request.Description;
            if (request.Location is not null)
                obj.SetAttribute("location", new DirectoryAttribute("location", request.Location));

            obj.WhenChanged = DateTimeOffset.UtcNow;
            await store.UpdateAsync(obj);
            return Results.Ok(new { obj.ObjectGuid, Name = obj.Cn });
        })
        .WithName("UpdateSubnet")
        .WithTags("Sites");

        // DELETE /api/v1/sites/subnets/{id} — delete subnet
        group.MapDelete("/subnets/{id}", async (IDirectoryStore store, string id) =>
        {
            var obj = await store.GetByGuidAsync(DirectoryConstants.DefaultTenantId, id);
            if (obj is null || obj.IsDeleted || obj.ObjectCategory != "subnet")
                return Results.Problem(statusCode: 404, detail: "Subnet not found");

            await store.DeleteAsync(DirectoryConstants.DefaultTenantId, obj.DistinguishedName, hardDelete: true);
            return Results.NoContent();
        })
        .WithName("DeleteSubnet")
        .WithTags("Sites");

        // GET /api/v1/sites/site-links — list site links
        group.MapGet("/site-links", async (IDirectoryStore store, INamingContextService ncService) =>
        {
            var configDn = ncService.GetConfigurationNc().Dn;
            var ipDn = $"CN=IP,CN=Inter-Site Transports,CN=Sites,{configDn}";
            var filter = new EqualityFilterNode("objectClass", "siteLink");

            var result = await store.SearchAsync(DirectoryConstants.DefaultTenantId, ipDn,
                SearchScope.SingleLevel, filter, null, pageSize: 200);

            return Results.Ok(result.Entries
                .Where(e => !e.IsDeleted)
                .Select(MapSiteLinkResponse));
        })
        .WithName("ListSiteLinks")
        .WithTags("Sites");

        // POST /api/v1/sites/site-links — create site link
        group.MapPost("/site-links", async (IDirectoryStore store, INamingContextService ncService, CreateSiteLinkRequest request) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.Problem(statusCode: 400, detail: "Name is required");
            if (request.Sites is null || request.Sites.Count < 2)
                return Results.Problem(statusCode: 400, detail: "At least 2 sites are required");

            var configDn = ncService.GetConfigurationNc().Dn;
            var ipDn = $"CN=IP,CN=Inter-Site Transports,CN=Sites,{configDn}";
            var dn = $"CN={request.Name},{ipDn}";

            var existing = await store.GetByDnAsync(DirectoryConstants.DefaultTenantId, dn);
            if (existing is not null && !existing.IsDeleted)
                return Results.Problem(statusCode: 409, detail: $"Site link '{request.Name}' already exists");

            // Ensure the IP transport container exists
            await EnsureTransportContainerAsync(store, configDn);

            var now = DateTimeOffset.UtcNow;
            var obj = new DirectoryObject
            {
                Id = dn.ToLowerInvariant(),
                TenantId = DirectoryConstants.DefaultTenantId,
                DistinguishedName = dn,
                Cn = request.Name,
                DomainDn = OuEndpoints.ExtractDomainDn(dn),
                ObjectCategory = "siteLink",
                ObjectClass = ["top", "siteLink"],
                ParentDn = ipDn,
                WhenCreated = now,
                WhenChanged = now,
            };

            obj.SetAttribute("siteList", new DirectoryAttribute("siteList", [.. request.Sites.Cast<object>()]));
            obj.SetAttribute("cost", new DirectoryAttribute("cost", (request.Cost ?? 100).ToString()));
            obj.SetAttribute("replInterval", new DirectoryAttribute("replInterval", (request.ReplInterval ?? 180).ToString()));
            if (request.Description is not null)
                obj.Description = request.Description;
            if (request.Schedule is not null)
                obj.SetAttribute("schedule", new DirectoryAttribute("schedule", request.Schedule));

            await store.CreateAsync(obj);
            return Results.Created($"/api/v1/sites/site-links/{obj.ObjectGuid}", MapSiteLinkResponse(obj));
        })
        .WithName("CreateSiteLink")
        .WithTags("Sites");

        // PUT /api/v1/sites/site-links/{name} — update site link
        group.MapPut("/site-links/{name}", async (IDirectoryStore store, INamingContextService ncService, string name, UpdateSiteLinkRequest request) =>
        {
            var configDn = ncService.GetConfigurationNc().Dn;
            var dn = $"CN={name},CN=IP,CN=Inter-Site Transports,CN=Sites,{configDn}";
            var obj = await store.GetByDnAsync(DirectoryConstants.DefaultTenantId, dn);
            if (obj is null || obj.IsDeleted)
                return Results.Problem(statusCode: 404, detail: "Site link not found");

            if (request.Sites is not null)
                obj.SetAttribute("siteList", new DirectoryAttribute("siteList", [.. request.Sites.Cast<object>()]));
            if (request.Cost.HasValue)
                obj.SetAttribute("cost", new DirectoryAttribute("cost", request.Cost.Value.ToString()));
            if (request.ReplInterval.HasValue)
                obj.SetAttribute("replInterval", new DirectoryAttribute("replInterval", request.ReplInterval.Value.ToString()));
            if (request.Description is not null)
                obj.Description = request.Description;
            if (request.Schedule is not null)
                obj.SetAttribute("schedule", new DirectoryAttribute("schedule", request.Schedule));

            obj.WhenChanged = DateTimeOffset.UtcNow;
            await store.UpdateAsync(obj);
            return Results.Ok(MapSiteLinkResponse(obj));
        })
        .WithName("UpdateSiteLink")
        .WithTags("Sites");

        // DELETE /api/v1/sites/site-links/{name}
        group.MapDelete("/site-links/{name}", async (IDirectoryStore store, INamingContextService ncService, string name) =>
        {
            var configDn = ncService.GetConfigurationNc().Dn;
            var dn = $"CN={name},CN=IP,CN=Inter-Site Transports,CN=Sites,{configDn}";
            var obj = await store.GetByDnAsync(DirectoryConstants.DefaultTenantId, dn);
            if (obj is null || obj.IsDeleted)
                return Results.Problem(statusCode: 404, detail: "Site link not found");

            await store.DeleteAsync(DirectoryConstants.DefaultTenantId, dn, hardDelete: true);
            return Results.NoContent();
        })
        .WithName("DeleteSiteLink")
        .WithTags("Sites");

        // GET /api/v1/sites/site-link-bridges — list bridges
        group.MapGet("/site-link-bridges", async (IDirectoryStore store, INamingContextService ncService) =>
        {
            var configDn = ncService.GetConfigurationNc().Dn;
            var ipDn = $"CN=IP,CN=Inter-Site Transports,CN=Sites,{configDn}";
            var filter = new EqualityFilterNode("objectClass", "siteLinkBridge");

            var result = await store.SearchAsync(DirectoryConstants.DefaultTenantId, ipDn,
                SearchScope.SingleLevel, filter, null, pageSize: 200);

            return Results.Ok(result.Entries
                .Where(e => !e.IsDeleted)
                .Select(e => new
                {
                    e.ObjectGuid,
                    Name = e.Cn ?? "",
                    SiteLinkList = e.GetAttribute("siteLinkList")?.GetStrings().ToList() ?? [],
                    e.DistinguishedName,
                }));
        })
        .WithName("ListSiteLinkBridges")
        .WithTags("Sites");

        // POST /api/v1/sites/site-link-bridges — create bridge
        group.MapPost("/site-link-bridges", async (IDirectoryStore store, INamingContextService ncService, CreateSiteLinkBridgeRequest request) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.Problem(statusCode: 400, detail: "Name is required");

            var configDn = ncService.GetConfigurationNc().Dn;
            var ipDn = $"CN=IP,CN=Inter-Site Transports,CN=Sites,{configDn}";
            var dn = $"CN={request.Name},{ipDn}";

            var existing = await store.GetByDnAsync(DirectoryConstants.DefaultTenantId, dn);
            if (existing is not null && !existing.IsDeleted)
                return Results.Problem(statusCode: 409, detail: $"Site link bridge '{request.Name}' already exists");

            var now = DateTimeOffset.UtcNow;
            var obj = new DirectoryObject
            {
                Id = dn.ToLowerInvariant(),
                TenantId = DirectoryConstants.DefaultTenantId,
                DistinguishedName = dn,
                Cn = request.Name,
                DomainDn = OuEndpoints.ExtractDomainDn(dn),
                ObjectCategory = "siteLinkBridge",
                ObjectClass = ["top", "siteLinkBridge"],
                ParentDn = ipDn,
                WhenCreated = now,
                WhenChanged = now,
            };

            if (request.SiteLinks is not null)
                obj.SetAttribute("siteLinkList", new DirectoryAttribute("siteLinkList", [.. request.SiteLinks.Cast<object>()]));

            await store.CreateAsync(obj);
            return Results.Created($"/api/v1/sites/site-link-bridges/{obj.ObjectGuid}", new { obj.ObjectGuid, Name = obj.Cn });
        })
        .WithName("CreateSiteLinkBridge")
        .WithTags("Sites");

        // DELETE /api/v1/sites/site-link-bridges/{name}
        group.MapDelete("/site-link-bridges/{name}", async (IDirectoryStore store, INamingContextService ncService, string name) =>
        {
            var configDn = ncService.GetConfigurationNc().Dn;
            var dn = $"CN={name},CN=IP,CN=Inter-Site Transports,CN=Sites,{configDn}";
            var obj = await store.GetByDnAsync(DirectoryConstants.DefaultTenantId, dn);
            if (obj is null || obj.IsDeleted)
                return Results.Problem(statusCode: 404, detail: "Site link bridge not found");

            await store.DeleteAsync(DirectoryConstants.DefaultTenantId, dn, hardDelete: true);
            return Results.NoContent();
        })
        .WithName("DeleteSiteLinkBridge")
        .WithTags("Sites");

        // GET /api/v1/sites/{siteName}/servers/{serverName}/connections — replication connections
        group.MapGet("/{siteName}/servers/{serverName}/connections", async (
            IDirectoryStore store, INamingContextService ncService, string siteName, string serverName) =>
        {
            var configDn = ncService.GetConfigurationNc().Dn;
            // NTDS Settings is under the server object
            var ntdsDn = $"CN=NTDS Settings,CN={serverName},CN=Servers,CN={siteName},CN=Sites,{configDn}";
            var filter = new EqualityFilterNode("objectClass", "nTDSConnection");

            var result = await store.SearchAsync(DirectoryConstants.DefaultTenantId, ntdsDn,
                SearchScope.SingleLevel, filter, null, pageSize: 100);

            return Results.Ok(result.Entries
                .Where(e => !e.IsDeleted)
                .Select(c => new
                {
                    c.ObjectGuid,
                    Name = c.Cn ?? "",
                    FromServer = c.GetAttribute("fromServer")?.GetFirstString() ?? "",
                    TransportType = c.GetAttribute("transportType")?.GetFirstString() ?? "IP",
                    Schedule = c.GetAttribute("schedule")?.GetFirstString() ?? "",
                    Enabled = !string.Equals(c.GetAttribute("enabledConnection")?.GetFirstString(), "FALSE", StringComparison.OrdinalIgnoreCase),
                    c.DistinguishedName,
                }));
        })
        .WithName("ListReplicationConnections")
        .WithTags("Sites");

        // POST /api/v1/sites/{siteName}/servers/{serverName}/connections — create connection
        group.MapPost("/{siteName}/servers/{serverName}/connections", async (
            IDirectoryStore store, INamingContextService ncService,
            string siteName, string serverName, CreateConnectionRequest request) =>
        {
            if (string.IsNullOrWhiteSpace(request.FromServer))
                return Results.Problem(statusCode: 400, detail: "fromServer is required");

            var configDn = ncService.GetConfigurationNc().Dn;
            var ntdsDn = $"CN=NTDS Settings,CN={serverName},CN=Servers,CN={siteName},CN=Sites,{configDn}";
            var connName = request.Name ?? $"Connection-{Guid.NewGuid():N}"[..20];
            var dn = $"CN={connName},{ntdsDn}";

            var now = DateTimeOffset.UtcNow;
            var obj = new DirectoryObject
            {
                Id = dn.ToLowerInvariant(),
                TenantId = DirectoryConstants.DefaultTenantId,
                DistinguishedName = dn,
                Cn = connName,
                DomainDn = OuEndpoints.ExtractDomainDn(dn),
                ObjectCategory = "nTDSConnection",
                ObjectClass = ["top", "nTDSConnection"],
                ParentDn = ntdsDn,
                WhenCreated = now,
                WhenChanged = now,
            };

            obj.SetAttribute("fromServer", new DirectoryAttribute("fromServer", request.FromServer));
            obj.SetAttribute("enabledConnection", new DirectoryAttribute("enabledConnection", "TRUE"));
            if (request.TransportType is not null)
                obj.SetAttribute("transportType", new DirectoryAttribute("transportType", request.TransportType));
            if (request.Schedule is not null)
                obj.SetAttribute("schedule", new DirectoryAttribute("schedule", request.Schedule));

            await store.CreateAsync(obj);
            return Results.Created($"/api/v1/sites/{siteName}/servers/{serverName}/connections/{obj.ObjectGuid}",
                new { obj.ObjectGuid, Name = obj.Cn });
        })
        .WithName("CreateReplicationConnection")
        .WithTags("Sites");

        // DELETE /api/v1/sites/{siteName}/servers/{serverName}/connections/{connName}
        group.MapDelete("/{siteName}/servers/{serverName}/connections/{connName}", async (
            IDirectoryStore store, INamingContextService ncService,
            string siteName, string serverName, string connName) =>
        {
            // Try by GUID first, then by name
            var obj = await store.GetByGuidAsync(DirectoryConstants.DefaultTenantId, connName);
            if (obj is null || obj.IsDeleted)
            {
                var configDn = ncService.GetConfigurationNc().Dn;
                var dn = $"CN={connName},CN=NTDS Settings,CN={serverName},CN=Servers,CN={siteName},CN=Sites,{configDn}";
                obj = await store.GetByDnAsync(DirectoryConstants.DefaultTenantId, dn);
            }

            if (obj is null || obj.IsDeleted)
                return Results.Problem(statusCode: 404, detail: "Connection not found");

            await store.DeleteAsync(DirectoryConstants.DefaultTenantId, obj.DistinguishedName, hardDelete: true);
            return Results.NoContent();
        })
        .WithName("DeleteReplicationConnection")
        .WithTags("Sites");

        // POST /api/v1/sites/{siteName}/servers/{serverName}/kcc — trigger KCC
        group.MapPost("/{siteName}/servers/{serverName}/kcc", (string siteName, string serverName) =>
        {
            // In a real AD, this triggers the Knowledge Consistency Checker.
            // For this implementation, we acknowledge the request.
            return Results.Ok(new
            {
                Message = $"KCC triggered for {serverName} in site {siteName}",
                TriggeredAt = DateTimeOffset.UtcNow,
            });
        })
        .WithName("TriggerKcc")
        .WithTags("Sites");

        // POST /api/v1/sites/{siteName}/servers/{serverName}/move — move to different site
        group.MapPost("/{siteName}/servers/{serverName}/move", async (
            IDirectoryStore store, INamingContextService ncService,
            string siteName, string serverName, MoveServerRequest request) =>
        {
            if (string.IsNullOrWhiteSpace(request.TargetSite))
                return Results.Problem(statusCode: 400, detail: "targetSite is required");

            var configDn = ncService.GetConfigurationNc().Dn;
            var oldDn = $"CN={serverName},CN=Servers,CN={siteName},CN=Sites,{configDn}";
            var newDn = $"CN={serverName},CN=Servers,CN={request.TargetSite},CN=Sites,{configDn}";

            var server = await store.GetByDnAsync(DirectoryConstants.DefaultTenantId, oldDn);
            if (server is null || server.IsDeleted)
                return Results.Problem(statusCode: 404, detail: "Server not found");

            // Verify target site exists
            var targetSiteDn = $"CN={request.TargetSite},CN=Sites,{configDn}";
            var targetSite = await store.GetByDnAsync(DirectoryConstants.DefaultTenantId, targetSiteDn);
            if (targetSite is null || targetSite.IsDeleted)
                return Results.Problem(statusCode: 404, detail: $"Target site '{request.TargetSite}' not found");

            await store.MoveAsync(DirectoryConstants.DefaultTenantId, oldDn, newDn);
            return Results.Ok(new { Message = $"Server {serverName} moved to site {request.TargetSite}" });
        })
        .WithName("MoveServer")
        .WithTags("Sites");

        // GET /api/v1/sites/transports — inter-site transports
        group.MapGet("/transports", async (IDirectoryStore store, INamingContextService ncService) =>
        {
            var configDn = ncService.GetConfigurationNc().Dn;
            var transportsDn = $"CN=Inter-Site Transports,CN=Sites,{configDn}";
            var filter = new EqualityFilterNode("objectClass", "interSiteTransport");

            var result = await store.SearchAsync(DirectoryConstants.DefaultTenantId, transportsDn,
                SearchScope.SingleLevel, filter, null, pageSize: 10);

            return Results.Ok(result.Entries
                .Where(e => !e.IsDeleted)
                .Select(t => new
                {
                    t.ObjectGuid,
                    Name = t.Cn ?? "",
                    t.Description,
                    t.DistinguishedName,
                }));
        })
        .WithName("ListTransports")
        .WithTags("Sites");

        // GET /api/v1/sites/all-subnets — list all subnets
        group.MapGet("/all-subnets", async (IDirectoryStore store, INamingContextService ncService) =>
        {
            var configDn = ncService.GetConfigurationNc().Dn;
            var subnetsDn = $"CN=Subnets,CN=Sites,{configDn}";
            var filter = new EqualityFilterNode("objectClass", "subnet");

            var result = await store.SearchAsync(DirectoryConstants.DefaultTenantId, subnetsDn,
                SearchScope.SingleLevel, filter, null, pageSize: 500);

            return Results.Ok(result.Entries
                .Where(e => !e.IsDeleted)
                .Select(s => new
                {
                    s.ObjectGuid,
                    Name = s.Cn ?? "",
                    Description = s.Description ?? "",
                    SiteObject = s.GetAttribute("siteObject")?.GetFirstString() ?? "",
                    Location = s.GetAttribute("location")?.GetFirstString() ?? "",
                    s.DistinguishedName,
                }));
        })
        .WithName("ListAllSubnets")
        .WithTags("Sites");

        return group;
    }

    private static object MapSiteLinkResponse(DirectoryObject obj)
    {
        var costStr = obj.GetAttribute("cost")?.GetFirstString();
        var replStr = obj.GetAttribute("replInterval")?.GetFirstString();
        return new
        {
            obj.ObjectGuid,
            Name = obj.Cn ?? "",
            Description = obj.Description ?? "",
            Sites = obj.GetAttribute("siteList")?.GetStrings().ToList() ?? [],
            Cost = costStr is not null && int.TryParse(costStr, out var c) ? c : 100,
            ReplInterval = replStr is not null && int.TryParse(replStr, out var r) ? r : 180,
            Schedule = obj.GetAttribute("schedule")?.GetFirstString() ?? "",
            obj.DistinguishedName,
        };
    }

    private static async Task EnsureTransportContainerAsync(IDirectoryStore store, string configDn)
    {
        var transportsDn = $"CN=Inter-Site Transports,CN=Sites,{configDn}";
        var transports = await store.GetByDnAsync(DirectoryConstants.DefaultTenantId, transportsDn);
        if (transports is null)
        {
            var sitesDn = $"CN=Sites,{configDn}";
            transports = new DirectoryObject
            {
                Id = transportsDn.ToLowerInvariant(),
                TenantId = DirectoryConstants.DefaultTenantId,
                DistinguishedName = transportsDn,
                Cn = "Inter-Site Transports",
                DomainDn = OuEndpoints.ExtractDomainDn(transportsDn),
                ObjectCategory = "interSiteTransportContainer",
                ObjectClass = ["top", "interSiteTransportContainer"],
                ParentDn = sitesDn,
                WhenCreated = DateTimeOffset.UtcNow,
                WhenChanged = DateTimeOffset.UtcNow,
            };
            await store.CreateAsync(transports);
        }

        var ipDn = $"CN=IP,{transportsDn}";
        var ip = await store.GetByDnAsync(DirectoryConstants.DefaultTenantId, ipDn);
        if (ip is null)
        {
            ip = new DirectoryObject
            {
                Id = ipDn.ToLowerInvariant(),
                TenantId = DirectoryConstants.DefaultTenantId,
                DistinguishedName = ipDn,
                Cn = "IP",
                DomainDn = OuEndpoints.ExtractDomainDn(ipDn),
                ObjectCategory = "interSiteTransport",
                ObjectClass = ["top", "interSiteTransport"],
                ParentDn = transportsDn,
                WhenCreated = DateTimeOffset.UtcNow,
                WhenChanged = DateTimeOffset.UtcNow,
            };
            await store.CreateAsync(ip);
        }
    }
}

public record CreateSiteRequest(string Name, string Description = null);
public record UpdateSiteRequest(string Description = null);
public record CreateSubnetRequest(string SubnetAddress, string SiteDn = null, string Description = null, string Location = null);
public record UpdateSubnetRequest(string SiteDn = null, string Description = null, string Location = null);
public record CreateSiteLinkRequest(string Name, List<string> Sites, int? Cost = null, int? ReplInterval = null, string Description = null, string Schedule = null);
public record UpdateSiteLinkRequest(List<string> Sites = null, int? Cost = null, int? ReplInterval = null, string Description = null, string Schedule = null);
public record CreateSiteLinkBridgeRequest(string Name, List<string> SiteLinks = null);
public record CreateConnectionRequest(string FromServer, string Name = null, string TransportType = null, string Schedule = null);
public record MoveServerRequest(string TargetSite);
