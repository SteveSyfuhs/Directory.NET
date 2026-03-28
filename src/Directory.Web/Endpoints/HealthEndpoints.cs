using System.Diagnostics;
using System.Net.Sockets;
using Directory.Core;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Web.Models;
using Directory.Web.Services;

namespace Directory.Web.Endpoints;

public static class HealthEndpoints
{
    public static RouteGroupBuilder MapHealthEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", async (
            IDirectoryStore store,
            IConfiguration config,
            SetupStateService setupState,
            DomainConfiguration domainConfig) =>
        {
            var result = new HealthReport();

            // 1. Service uptime and identity
            result.Uptime = DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime();
            result.Version = typeof(HealthEndpoints).Assembly.GetName().Version?.ToString() ?? "1.0.0";
            result.MachineName = Environment.MachineName;
            result.DotNetVersion = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
            result.Timestamp = DateTimeOffset.UtcNow;
            result.DatabaseConfigured = setupState.IsDatabaseConfigured;
            result.DomainProvisioned = setupState.IsProvisioned;
            result.DomainDn = !string.IsNullOrEmpty(domainConfig.DomainDn) ? domainConfig.DomainDn : null;
            result.DnsName = !string.IsNullOrEmpty(domainConfig.DomainDnsName) ? domainConfig.DomainDnsName : null;

            // 2. Cosmos DB health - try a lightweight query
            var cosmosStatus = new ComponentHealth { Name = "CosmosDB" };
            try
            {
                var sw = Stopwatch.StartNew();
                var filter = new EqualityFilterNode("objectClass", "domainDNS");
                await store.SearchAsync(
                    DirectoryConstants.DefaultTenantId,
                    config.GetValue<string>("NamingContexts:DomainDn") ?? "DC=directory,DC=local",
                    SearchScope.BaseObject,
                    filter,
                    new[] { "cn" },
                    sizeLimit: 1);
                sw.Stop();
                cosmosStatus.Status = "healthy";
                cosmosStatus.LatencyMs = sw.ElapsedMilliseconds;
            }
            catch (Exception ex)
            {
                cosmosStatus.Status = "unhealthy";
                cosmosStatus.Error = ex.Message;
            }
            result.Components.Add(cosmosStatus);

            // 3. Protocol server port checks - try connecting to each configured port
            var ports = new[]
            {
                new { Name = "LDAP", Port = config.GetValue("Ldap:Port", 389), Proto = "TCP" },
                new { Name = "LDAPS", Port = config.GetValue("Ldap:TlsPort", 636), Proto = "TCP" },
                new { Name = "Kerberos", Port = config.GetValue("Kerberos:Port", 88), Proto = "TCP" },
                new { Name = "DNS", Port = config.GetValue("Dns:Port", 53), Proto = "TCP" },
                new { Name = "RPC", Port = config.GetValue("RpcServer:EndpointMapperPort", 1135), Proto = "TCP" },
                new { Name = "GlobalCatalog", Port = config.GetValue("Ldap:GcPort", 3268), Proto = "TCP" },
                new { Name = "DRS", Port = config.GetValue("Replication:HttpPort", 9389), Proto = "TCP" },
            };

            foreach (var p in ports)
            {
                var portHealth = new ComponentHealth { Name = $"{p.Name} ({p.Port}/{p.Proto})" };
                try
                {
                    using var client = new TcpClient();
                    var connectTask = client.ConnectAsync("127.0.0.1", p.Port);
                    if (await Task.WhenAny(connectTask, Task.Delay(2000)) == connectTask)
                    {
                        await connectTask; // propagate any exception
                        portHealth.Status = "healthy";
                    }
                    else
                    {
                        portHealth.Status = "timeout";
                        portHealth.Error = "Connection timed out after 2s";
                    }
                }
                catch (Exception ex)
                {
                    portHealth.Status = "unhealthy";
                    portHealth.Error = ex.Message;
                }
                result.Components.Add(portHealth);
            }

            // 4. Memory/resource usage
            var process = Process.GetCurrentProcess();
            result.Resources = new ResourceInfo
            {
                WorkingSetMb = process.WorkingSet64 / (1024.0 * 1024.0),
                GcTotalMemoryMb = GC.GetTotalMemory(false) / (1024.0 * 1024.0),
                ThreadCount = process.Threads.Count,
                HandleCount = process.HandleCount,
            };

            // 5. Overall status
            result.Status = result.Components.All(c => c.Status == "healthy") ? "healthy" :
                            result.Components.Any(c => c.Status == "unhealthy") ? "degraded" : "partial";

            return Results.Ok(result);
        })
        .WithName("HealthCheck")
        .WithTags("Health")
        .RequireAuthorization();

        group.MapGet("/ping", () => Results.Ok(new { status = "ok" }))
            .WithName("HealthPing")
            .WithTags("Health")
            .AllowAnonymous();

        return group;
    }
}

public class HealthReport
{
    public string Status { get; set; } = "unknown";
    public string Version { get; set; } = "";
    public string MachineName { get; set; } = "";
    public string DotNetVersion { get; set; } = "";
    public TimeSpan Uptime { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public bool DatabaseConfigured { get; set; }
    public bool DomainProvisioned { get; set; }
    public string DomainDn { get; set; }
    public string DnsName { get; set; }
    public List<ComponentHealth> Components { get; set; } = new();
    public ResourceInfo Resources { get; set; }
}

public class ComponentHealth
{
    public string Name { get; set; } = "";
    public string Status { get; set; } = "unknown";
    public long? LatencyMs { get; set; }
    public string Error { get; set; }
}

public class ResourceInfo
{
    public double WorkingSetMb { get; set; }
    public double GcTotalMemoryMb { get; set; }
    public int ThreadCount { get; set; }
    public int HandleCount { get; set; }
}
