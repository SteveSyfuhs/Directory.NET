using Directory.Core.Models;
using Directory.Dns;
using Directory.Web.Models;
using Microsoft.Extensions.Options;

namespace Directory.Web.Endpoints;

public static class DnsEndpoints
{
    private const string DefaultTenantId = "default";

    public static RouteGroupBuilder MapDnsEndpoints(this RouteGroupBuilder group)
    {
        // ── Zones ──────────────────────────────────────────────────────────

        group.MapGet("/zones", (IOptions<DnsOptions> options, DomainConfiguration domainConfig) =>
        {
            var opts = options.Value;
            var zones = new List<object>();

            // Collect all known domains: from config + from the provisioned domain
            var allDomains = new HashSet<string>(opts.Domains, StringComparer.OrdinalIgnoreCase);

            // Add the actual provisioned domain DNS name (this is the primary zone)
            if (!string.IsNullOrEmpty(domainConfig.DomainDnsName))
                allDomains.Add(domainConfig.DomainDnsName);
            if (!string.IsNullOrEmpty(domainConfig.ForestDnsName))
                allDomains.Add(domainConfig.ForestDnsName);

            // Also add _msdcs subdomain zone (standard AD DNS)
            if (!string.IsNullOrEmpty(domainConfig.ForestDnsName))
                allDomains.Add($"_msdcs.{domainConfig.ForestDnsName}");

            foreach (var domain in allDomains)
            {
                zones.Add(new
                {
                    Name = domain,
                    Type = "primary",
                    IsReverse = false,
                    Status = "Running",
                    DynamicUpdate = "Secure",
                });
            }

            // Add standard reverse zones derived from server IPs
            foreach (var ip in opts.ServerIpAddresses)
            {
                if (System.Net.IPAddress.TryParse(ip, out var addr) &&
                    addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    var octets = ip.Split('.');
                    if (octets.Length == 4)
                    {
                        var reverseZone = $"{octets[2]}.{octets[1]}.{octets[0]}.in-addr.arpa";
                        zones.Add(new
                        {
                            Name = reverseZone,
                            Type = "primary",
                            IsReverse = true,
                            Status = "Running",
                            DynamicUpdate = "Secure",
                        });
                    }
                }
            }

            return Results.Ok(zones);
        })
        .WithName("ListDnsZones")
        .WithTags("DNS");

        group.MapPost("/zones", (CreateZoneRequest request, IOptions<DnsOptions> options) =>
        {
            var opts = options.Value;

            var zoneName = request.Name;
            if (request.ReverseZone && !zoneName.EndsWith(".in-addr.arpa", StringComparison.OrdinalIgnoreCase))
            {
                // Auto-format as reverse zone
                var parts = zoneName.Split('.');
                Array.Reverse(parts);
                zoneName = string.Join(".", parts) + ".in-addr.arpa";
            }

            // Check if zone already exists
            if (opts.Domains.Contains(zoneName, StringComparer.OrdinalIgnoreCase))
                return Results.Problem(statusCode: 409, detail: $"Zone '{zoneName}' already exists");

            opts.Domains.Add(zoneName);

            return Results.Created($"/api/v1/dns/zones/{zoneName}", new
            {
                Name = zoneName,
                Type = request.Type,
                IsReverse = request.ReverseZone,
                Status = "Running",
                DynamicUpdate = request.DynamicUpdate ?? "Secure",
            });
        })
        .WithName("CreateDnsZone")
        .WithTags("DNS");

        group.MapDelete("/zones/{zoneName}", (string zoneName, IOptions<DnsOptions> options) =>
        {
            var opts = options.Value;
            var removed = opts.Domains.RemoveAll(d =>
                d.Equals(zoneName, StringComparison.OrdinalIgnoreCase));

            return removed > 0
                ? Results.NoContent()
                : Results.Problem(statusCode: 404, detail: $"Zone '{zoneName}' not found");
        })
        .WithName("DeleteDnsZone")
        .WithTags("DNS");

        group.MapGet("/zones/{zoneName}/properties", (string zoneName, IOptions<DnsOptions> options) =>
        {
            var opts = options.Value;

            return Results.Ok(new
            {
                Name = zoneName,
                Type = "primary",
                Status = "Running",
                DynamicUpdate = "Secure",
                Soa = new
                {
                    PrimaryServer = opts.ServerHostname,
                    ResponsiblePerson = $"hostmaster.{zoneName}",
                    Serial = (uint)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60),
                    Refresh = 900,
                    Retry = 600,
                    Expire = 86400,
                    MinimumTtl = 60,
                },
                NameServers = new[] { opts.ServerHostname },
                ZoneTransfers = new
                {
                    AllowTransfer = "None",
                    NotifyServers = Array.Empty<string>(),
                },
                Aging = new
                {
                    AgingEnabled = false,
                    NoRefreshInterval = TimeSpan.FromDays(7).TotalHours,
                    RefreshInterval = TimeSpan.FromDays(7).TotalHours,
                },
            });
        })
        .WithName("GetDnsZoneProperties")
        .WithTags("DNS");

        group.MapPut("/zones/{zoneName}/properties", (string zoneName, UpdateZonePropertiesRequest request) =>
        {
            return Results.Ok(new
            {
                Name = zoneName,
                Type = "primary",
                Status = "Running",
                DynamicUpdate = request.DynamicUpdate ?? "Secure",
                Updated = true,
            });
        })
        .WithName("UpdateDnsZoneProperties")
        .WithTags("DNS");

        // ── Records ────────────────────────────────────────────────────────

        group.MapGet("/zones/{zoneName}/records", async (
            string zoneName, string type, DnsZoneStore zoneStore, IOptions<DnsOptions> options) =>
        {
            var opts = options.Value;
            var records = new List<DnsRecordDto>();

            // Determine record type filter
            DnsRecordType typeFilter = 0;
            if (!string.IsNullOrEmpty(type) && Enum.TryParse<DnsRecordType>(type, true, out var parsed))
                typeFilter = parsed;

            if (typeFilter != 0)
            {
                var zoneRecords = await zoneStore.GetAllRecordsAsync(DefaultTenantId, zoneName, typeFilter);
                foreach (var r in zoneRecords)
                {
                    records.Add(new DnsRecordDto
                    {
                        Id = $"{r.Name}_{r.Type}_{r.Data.GetHashCode():x8}",
                        Name = r.Name,
                        Type = r.Type.ToString(),
                        Data = r.Data,
                        Ttl = r.Ttl,
                    });
                }
            }
            else
            {
                // Get all record types
                foreach (var rt in new[] { DnsRecordType.A, DnsRecordType.AAAA, DnsRecordType.CNAME,
                    DnsRecordType.MX, DnsRecordType.NS, DnsRecordType.PTR, DnsRecordType.SRV, DnsRecordType.TXT })
                {
                    var zoneRecords = await zoneStore.GetAllRecordsAsync(DefaultTenantId, zoneName, rt);
                    foreach (var r in zoneRecords)
                    {
                        records.Add(new DnsRecordDto
                        {
                            Id = $"{r.Name}_{r.Type}_{r.Data.GetHashCode():x8}",
                            Name = r.Name,
                            Type = r.Type.ToString(),
                            Data = r.Data,
                            Ttl = r.Ttl,
                        });
                    }
                }

                // Add SOA record for zone apex
                records.Insert(0, new DnsRecordDto
                {
                    Id = $"{zoneName}_SOA",
                    Name = zoneName,
                    Type = "SOA",
                    Data = $"{opts.ServerHostname} hostmaster.{zoneName}",
                    Ttl = opts.DefaultTtl,
                });

                // Add NS record
                records.Insert(1, new DnsRecordDto
                {
                    Id = $"{zoneName}_NS",
                    Name = zoneName,
                    Type = "NS",
                    Data = opts.ServerHostname,
                    Ttl = opts.DefaultTtl,
                });

                // Add server A record(s)
                foreach (var ip in opts.ServerIpAddresses)
                {
                    if (System.Net.IPAddress.TryParse(ip, out var addr))
                    {
                        var recType = addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? "A" : "AAAA";
                        records.Add(new DnsRecordDto
                        {
                            Id = $"{opts.ServerHostname}_{recType}_{ip}",
                            Name = opts.ServerHostname,
                            Type = recType,
                            Data = ip,
                            Ttl = opts.DefaultTtl,
                        });
                    }
                }
            }

            return Results.Ok(records);
        })
        .WithName("ListDnsRecords")
        .WithTags("DNS");

        group.MapPost("/zones/{zoneName}/records", async (
            string zoneName, CreateRecordRequest request, DnsZoneStore zoneStore) =>
        {
            if (!Enum.TryParse<DnsRecordType>(request.Type, true, out var recordType))
                return Results.Problem(statusCode: 400, detail: $"Invalid record type: {request.Type}");

            var record = new DnsRecord
            {
                Name = request.Name,
                Type = recordType,
                Ttl = request.Ttl ?? 600,
                Data = request.Data,
            };

            await zoneStore.UpsertRecordAsync(DefaultTenantId, zoneName, record);

            return Results.Created($"/api/v1/dns/zones/{zoneName}/records/{record.Name}_{record.Type}", new DnsRecordDto
            {
                Id = $"{record.Name}_{record.Type}",
                Name = record.Name,
                Type = record.Type.ToString(),
                Data = record.Data,
                Ttl = record.Ttl,
            });
        })
        .WithName("CreateDnsRecord")
        .WithTags("DNS");

        group.MapPut("/zones/{zoneName}/records/{recordId}", async (
            string zoneName, string recordId, UpdateRecordRequest request, DnsZoneStore zoneStore) =>
        {
            if (!Enum.TryParse<DnsRecordType>(request.Type, true, out var recordType))
                return Results.Problem(statusCode: 400, detail: $"Invalid record type: {request.Type}");

            var record = new DnsRecord
            {
                Name = request.Name,
                Type = recordType,
                Ttl = request.Ttl ?? 600,
                Data = request.Data,
            };

            await zoneStore.UpsertRecordAsync(DefaultTenantId, zoneName, record);

            return Results.Ok(new DnsRecordDto
            {
                Id = $"{record.Name}_{record.Type}",
                Name = record.Name,
                Type = record.Type.ToString(),
                Data = record.Data,
                Ttl = record.Ttl,
            });
        })
        .WithName("UpdateDnsRecord")
        .WithTags("DNS");

        group.MapDelete("/zones/{zoneName}/records/{recordId}", async (
            string zoneName, string recordId, DnsZoneStore zoneStore) =>
        {
            // recordId format: "name_TYPE"
            var lastUnderscore = recordId.LastIndexOf('_');
            if (lastUnderscore < 0)
                return Results.Problem(statusCode: 400, detail: "Invalid record ID format");

            var name = recordId[..lastUnderscore];
            await zoneStore.DeleteRecordAsync(DefaultTenantId, zoneName, name);
            return Results.NoContent();
        })
        .WithName("DeleteDnsRecord")
        .WithTags("DNS");

        // ── Forwarders ─────────────────────────────────────────────────────

        group.MapGet("/forwarders", (IOptions<DnsOptions> options) =>
        {
            var opts = options.Value;
            var forwarders = opts.Forwarders.Select(f => new
            {
                Domain = f.ZoneName,
                Servers = f.ForwarderAddresses,
            }).ToList();

            if (!string.IsNullOrEmpty(opts.DefaultForwarder))
            {
                forwarders.Insert(0, new
                {
                    Domain = ".",
                    Servers = new List<string> { opts.DefaultForwarder },
                });
            }

            return Results.Ok(forwarders);
        })
        .WithName("ListDnsForwarders")
        .WithTags("DNS");

        group.MapPost("/forwarders", (CreateForwarderRequest request, IOptions<DnsOptions> options) =>
        {
            var opts = options.Value;

            if (request.Domain == "." || string.IsNullOrEmpty(request.Domain))
            {
                opts.DefaultForwarder = request.Servers.FirstOrDefault();
            }
            else
            {
                var existing = opts.Forwarders.FirstOrDefault(f =>
                    f.ZoneName.Equals(request.Domain, StringComparison.OrdinalIgnoreCase));

                if (existing != null)
                {
                    existing.ForwarderAddresses = request.Servers;
                }
                else
                {
                    opts.Forwarders.Add(new DnsForwarderEntry
                    {
                        ZoneName = request.Domain,
                        ForwarderAddresses = request.Servers,
                    });
                }
            }

            return Results.Created($"/api/v1/dns/forwarders/{request.Domain}", new
            {
                request.Domain,
                request.Servers,
            });
        })
        .WithName("CreateDnsForwarder")
        .WithTags("DNS");

        group.MapDelete("/forwarders/{domain}", (string domain, IOptions<DnsOptions> options) =>
        {
            var opts = options.Value;

            if (domain == ".")
            {
                opts.DefaultForwarder = null;
                return Results.NoContent();
            }

            var removed = opts.Forwarders.RemoveAll(f =>
                f.ZoneName.Equals(domain, StringComparison.OrdinalIgnoreCase));

            return removed > 0
                ? Results.NoContent()
                : Results.Problem(statusCode: 404, detail: $"Forwarder for '{domain}' not found");
        })
        .WithName("DeleteDnsForwarder")
        .WithTags("DNS");

        // ── Scavenging ─────────────────────────────────────────────────────

        group.MapPost("/scavenging", () =>
        {
            return Results.Ok(new
            {
                Status = "Scavenging initiated",
                StartedAt = DateTimeOffset.UtcNow,
            });
        })
        .WithName("TriggerDnsScavenging")
        .WithTags("DNS");

        group.MapGet("/scavenging/settings", () =>
        {
            return Results.Ok(new ScavengingSettings
            {
                Enabled = false,
                NoRefreshIntervalHours = 168,
                RefreshIntervalHours = 168,
            });
        })
        .WithName("GetDnsScavengingSettings")
        .WithTags("DNS");

        group.MapPut("/scavenging/settings", (ScavengingSettings request) =>
        {
            return Results.Ok(request);
        })
        .WithName("UpdateDnsScavengingSettings")
        .WithTags("DNS");

        // ── Statistics ─────────────────────────────────────────────────────

        group.MapGet("/statistics", async (DnsZoneStore zoneStore, IOptions<DnsOptions> options, DomainConfiguration domainConfig) =>
        {
            var opts = options.Value;
            var totalRecords = 0;

            var allDomains = new HashSet<string>(opts.Domains, StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(domainConfig.DomainDnsName))
                allDomains.Add(domainConfig.DomainDnsName);
            if (!string.IsNullOrEmpty(domainConfig.ForestDnsName))
                allDomains.Add(domainConfig.ForestDnsName);

            foreach (var domain in allDomains)
            {
                foreach (var rt in new[] { DnsRecordType.A, DnsRecordType.AAAA, DnsRecordType.CNAME,
                    DnsRecordType.MX, DnsRecordType.NS, DnsRecordType.PTR, DnsRecordType.SRV, DnsRecordType.TXT })
                {
                    var records = await zoneStore.GetAllRecordsAsync(DefaultTenantId, domain, rt);
                    totalRecords += records.Count;
                }
            }

            return Results.Ok(new
            {
                ServerHostname = opts.ServerHostname,
                Port = opts.Port,
                ZoneCount = allDomains.Count,
                ForwarderCount = opts.Forwarders.Count,
                RecordCount = totalRecords,
                Uptime = "Running",
                ServerIpAddresses = opts.ServerIpAddresses,
            });
        })
        .WithName("GetDnsStatistics")
        .WithTags("DNS");

        // ── Diagnostics ──────────────────────────────────────────────────────

        group.MapGet("/diagnostics", async (
            DnsZoneStore zoneStore,
            IOptions<DnsOptions> options,
            DomainConfiguration domainConfig,
            Directory.Core.Interfaces.IDirectoryStore store) =>
        {
            var opts = options.Value;
            var diagnostics = new List<object>();

            var allDomains = new HashSet<string>(opts.Domains, StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(domainConfig.DomainDnsName))
                allDomains.Add(domainConfig.DomainDnsName);
            if (!string.IsNullOrEmpty(domainConfig.ForestDnsName))
                allDomains.Add(domainConfig.ForestDnsName);

            foreach (var domain in allDomains)
            {
                var parts = domain.Split('.');
                var domainDn = string.Join(",", parts.Select(p => $"DC={p}"));
                var zoneContainerDn = $"DC={domain},CN=MicrosoftDNS,DC=DomainDnsZones,{domainDn}";
                var microsoftDnsDn = $"CN=MicrosoftDNS,DC=DomainDnsZones,{domainDn}";
                var appPartDn = $"DC=DomainDnsZones,{domainDn}";

                // Check if container hierarchy exists
                var appPartExists = await store.GetByDnAsync(DefaultTenantId, appPartDn) != null;
                var msDnsExists = await store.GetByDnAsync(DefaultTenantId, microsoftDnsDn) != null;
                var zoneExists = await store.GetByDnAsync(DefaultTenantId, zoneContainerDn) != null;

                // Count records
                var recordCount = 0;
                foreach (var rt in new[] { DnsRecordType.A, DnsRecordType.AAAA, DnsRecordType.SRV,
                    DnsRecordType.CNAME, DnsRecordType.MX, DnsRecordType.NS, DnsRecordType.PTR, DnsRecordType.TXT })
                {
                    var records = await zoneStore.GetAllRecordsAsync(DefaultTenantId, domain, rt);
                    recordCount += records.Count;
                }

                diagnostics.Add(new
                {
                    Zone = domain,
                    DomainDn = domainDn,
                    ZoneContainerDn = zoneContainerDn,
                    AppPartitionExists = appPartExists,
                    MicrosoftDnsContainerExists = msDnsExists,
                    ZoneContainerExists = zoneExists,
                    RecordCount = recordCount,
                });
            }

            return Results.Ok(new
            {
                ConfiguredDomains = opts.Domains,
                TenantId = DefaultTenantId,
                Zones = diagnostics,
            });
        })
        .WithName("GetDnsDiagnostics")
        .WithTags("DNS");

        // ── SRV Record Registration ──────────────────────────────────────────
        // Ensures all standard AD SRV records exist in the zone store.
        // Normally these are created by DcRegistrationService in Directory.Server,
        // but if only Directory.Web is running we need a way to populate them.

        group.MapPost("/register-srv", async (
            DnsZoneStore zoneStore,
            DomainConfiguration domainConfig,
            IOptions<DnsOptions> dnsOpts,
            IOptions<DcNodeOptions> dcOpts,
            Directory.Core.Interfaces.IDirectoryStore directoryStore) =>
        {
            var domainDnsName = domainConfig.DomainDnsName;
            var forestDnsName = domainConfig.ForestDnsName;
            if (string.IsNullOrEmpty(domainDnsName))
                return Results.Problem(statusCode: 400, detail: "Domain DNS name is not configured. Complete setup first.");

            if (string.IsNullOrEmpty(forestDnsName))
                forestDnsName = domainDnsName;

            var dc = dcOpts.Value;
            var dns = dnsOpts.Value;
            var hostname = dc.Hostname;
            var siteName = dc.SiteName;
            var dcFqdn = $"{hostname}.{domainDnsName}";
            var dcIp = dns.ServerIpAddresses.FirstOrDefault() ?? "127.0.0.1";

            // Generate domain GUID
            var domainDn = domainConfig.DomainDn ?? "";
            var domainGuidBytes = System.Security.Cryptography.MD5.HashData(
                System.Text.Encoding.UTF8.GetBytes(domainDn.ToLowerInvariant()));
            var domainGuid = new Guid(domainGuidBytes).ToString();

            // Ensure zone containers exist
            await EnsureZoneContainersAsync(directoryStore, domainDn, domainDnsName);
            if (!forestDnsName.Equals(domainDnsName, StringComparison.OrdinalIgnoreCase))
                await EnsureZoneContainersAsync(directoryStore, domainDn, forestDnsName);

            var records = new List<(string zone, DnsRecord record)>();

            void AddSrv(string zone, string name, ushort port)
            {
                records.Add((zone, new DnsRecord
                {
                    Name = name, Type = DnsRecordType.SRV, Ttl = 600,
                    Data = $"0 100 {port} {dcFqdn}"
                }));
            }

            // LDAP SRV
            AddSrv(domainDnsName, $"_ldap._tcp.{domainDnsName}", 389);
            AddSrv(domainDnsName, $"_ldap._tcp.{siteName}._sites.{domainDnsName}", 389);
            AddSrv(domainDnsName, $"_ldap._tcp.dc._msdcs.{domainDnsName}", 389);
            AddSrv(domainDnsName, $"_ldap._tcp.{siteName}._sites.dc._msdcs.{domainDnsName}", 389);
            AddSrv(forestDnsName, $"_ldap._tcp.{domainGuid}.domains._msdcs.{forestDnsName}", 389);

            // Kerberos SRV
            AddSrv(domainDnsName, $"_kerberos._tcp.{domainDnsName}", 88);
            AddSrv(domainDnsName, $"_kerberos._tcp.{siteName}._sites.{domainDnsName}", 88);
            AddSrv(domainDnsName, $"_kerberos._tcp.dc._msdcs.{domainDnsName}", 88);
            AddSrv(domainDnsName, $"_kerberos._udp.{domainDnsName}", 88);

            // Kpasswd SRV
            AddSrv(domainDnsName, $"_kpasswd._tcp.{domainDnsName}", 464);
            AddSrv(domainDnsName, $"_kpasswd._udp.{domainDnsName}", 464);

            // Global Catalog SRV
            AddSrv(forestDnsName, $"_gc._tcp.{forestDnsName}", 3268);
            AddSrv(forestDnsName, $"_gc._tcp.{siteName}._sites.{forestDnsName}", 3268);

            // PDC Emulator
            AddSrv(domainDnsName, $"_ldap._tcp.pdc._msdcs.{domainDnsName}", 389);

            // A records
            records.Add((domainDnsName, new DnsRecord
            {
                Name = dcFqdn, Type = DnsRecordType.A, Ttl = 600, Data = dcIp
            }));
            records.Add((forestDnsName, new DnsRecord
            {
                Name = $"gc._msdcs.{forestDnsName}", Type = DnsRecordType.A, Ttl = 600, Data = dcIp
            }));

            var created = 0;
            var errors = new List<string>();
            foreach (var (zone, record) in records)
            {
                try
                {
                    await zoneStore.UpsertRecordAsync(DefaultTenantId, zone, record);
                    created++;
                }
                catch (Exception ex)
                {
                    errors.Add($"{record.Name}: {ex.Message}");
                }
            }

            return Results.Ok(new { registered = created, total = records.Count, errors });
        })
        .WithName("RegisterDnsSrvRecords")
        .WithTags("DNS");

        return group;
    }

    /// <summary>
    /// Ensures DNS zone container hierarchy exists in Cosmos DB.
    /// </summary>
    private static async Task EnsureZoneContainersAsync(
        Directory.Core.Interfaces.IDirectoryStore store, string domainDn, string zoneName)
    {
        if (string.IsNullOrEmpty(domainDn)) return;

        var tenantId = DefaultTenantId;
        var schemaDn = $"CN=Schema,CN=Configuration,{domainDn}";

        var dnsZonesDn = $"DC=DomainDnsZones,{domainDn}";
        if (await store.GetByDnAsync(tenantId, dnsZonesDn) == null)
        {
            var obj = new DirectoryObject
            {
                Id = dnsZonesDn.ToLowerInvariant(),
                TenantId = tenantId,
                DomainDn = domainDn,
                DistinguishedName = dnsZonesDn,
                ObjectGuid = Guid.NewGuid().ToString(),
                ObjectClass = ["top", "domainDNS"],
                ObjectCategory = $"CN=Domain-DNS,{schemaDn}",
                ParentDn = domainDn,
                Cn = "DomainDnsZones",
                WhenCreated = DateTimeOffset.UtcNow,
                WhenChanged = DateTimeOffset.UtcNow,
            };
            await store.CreateAsync(obj);
        }

        var microsoftDnsDn = $"CN=MicrosoftDNS,{dnsZonesDn}";
        if (await store.GetByDnAsync(tenantId, microsoftDnsDn) == null)
        {
            var obj = new DirectoryObject
            {
                Id = microsoftDnsDn.ToLowerInvariant(),
                TenantId = tenantId,
                DomainDn = domainDn,
                DistinguishedName = microsoftDnsDn,
                ObjectGuid = Guid.NewGuid().ToString(),
                ObjectClass = ["top", "container"],
                ObjectCategory = $"CN=Container,{schemaDn}",
                ParentDn = dnsZonesDn,
                Cn = "MicrosoftDNS",
                WhenCreated = DateTimeOffset.UtcNow,
                WhenChanged = DateTimeOffset.UtcNow,
            };
            await store.CreateAsync(obj);
        }

        var zoneContainerDn = $"DC={zoneName},{microsoftDnsDn}";
        if (await store.GetByDnAsync(tenantId, zoneContainerDn) == null)
        {
            var obj = new DirectoryObject
            {
                Id = zoneContainerDn.ToLowerInvariant(),
                TenantId = tenantId,
                DomainDn = domainDn,
                DistinguishedName = zoneContainerDn,
                ObjectGuid = Guid.NewGuid().ToString(),
                ObjectClass = ["top", "dnsZone"],
                ObjectCategory = $"CN=Dns-Zone,{schemaDn}",
                ParentDn = microsoftDnsDn,
                Cn = zoneName,
                WhenCreated = DateTimeOffset.UtcNow,
                WhenChanged = DateTimeOffset.UtcNow,
            };
            obj.SetAttribute("dc", new DirectoryAttribute("dc", zoneName));
            await store.CreateAsync(obj);
        }
    }
}

// ── Request/Response DTOs ──────────────────────────────────────────────────

public record CreateZoneRequest(
    string Name,
    string Type = "primary",
    bool ReverseZone = false,
    string DynamicUpdate = "Secure"
);

public record UpdateZonePropertiesRequest(
    string DynamicUpdate = null,
    ZoneSoaRequest Soa = null,
    string[] NameServers = null,
    ZoneTransferRequest ZoneTransfers = null,
    ZoneAgingRequest Aging = null
);

public record ZoneSoaRequest(
    string PrimaryServer = null,
    string ResponsiblePerson = null,
    int? Refresh = null,
    int? Retry = null,
    int? Expire = null,
    int? MinimumTtl = null
);

public record ZoneTransferRequest(
    string AllowTransfer = null,
    string[] NotifyServers = null
);

public record ZoneAgingRequest(
    bool? AgingEnabled = null,
    double? NoRefreshInterval = null,
    double? RefreshInterval = null
);

public class DnsRecordDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string Data { get; set; } = "";
    public int Ttl { get; set; } = 600;
}

public record CreateRecordRequest(
    string Name,
    string Type,
    string Data,
    int? Ttl = 600
);

public record UpdateRecordRequest(
    string Name,
    string Type,
    string Data,
    int? Ttl = 600
);

public record CreateForwarderRequest(
    string Domain,
    List<string> Servers
);

public class ScavengingSettings
{
    public bool Enabled { get; set; }
    public double NoRefreshIntervalHours { get; set; } = 168;
    public double RefreshIntervalHours { get; set; } = 168;
}
