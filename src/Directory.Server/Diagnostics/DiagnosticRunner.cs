using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;

namespace Directory.Server.Diagnostics;

public static class DiagnosticRunner
{
    public static async Task<int> RunAsync(string[] args)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n╔══════════════════════════════════════════════╗");
        Console.WriteLine("║   Directory.NET Diagnostics                  ║");
        Console.WriteLine("╚══════════════════════════════════════════════╝\n");
        Console.ResetColor();

        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        var hasFailure = false;

        // 1. System info
        WriteSection("System Information");
        WriteOk($"Machine: {Environment.MachineName}");
        WriteOk($"OS: {Environment.OSVersion}");
        WriteOk($".NET: {Environment.Version}");
        WriteOk($"Processors: {Environment.ProcessorCount}");
        WriteOk($"64-bit: {Environment.Is64BitOperatingSystem}");

        // 2. Configuration check
        WriteSection("Configuration");
        var cosmosConn = config["CosmosDb:ConnectionString"];
        if (string.IsNullOrEmpty(cosmosConn))
        {
            WriteErr("CosmosDb:ConnectionString is not configured");
            hasFailure = true;
        }
        else
        {
            WriteOk($"CosmosDb:ConnectionString is configured ({(cosmosConn.Contains("localhost") ? "local emulator" : "cloud")})");
        }

        var domainDn = config["NamingContexts:DomainDn"];
        if (string.IsNullOrEmpty(domainDn))
            WriteWarn("NamingContexts:DomainDn is not configured (will be set during setup)");
        else
            WriteOk($"Domain DN: {domainDn}");

        // 3. Cosmos DB connectivity
        WriteSection("Cosmos DB Connectivity");
        if (!string.IsNullOrEmpty(cosmosConn))
        {
            try
            {
                var sw = Stopwatch.StartNew();
                using var client = new CosmosClient(cosmosConn, new CosmosClientOptions
                {
                    ConnectionMode = ConnectionMode.Direct,
                    RequestTimeout = TimeSpan.FromSeconds(5)
                });
                var account = await client.ReadAccountAsync();
                sw.Stop();
                WriteOk($"Connected successfully ({sw.ElapsedMilliseconds}ms)");
                WriteOk($"Consistency: {account.Consistency?.DefaultConsistencyLevel}");

                // Check database
                var dbName = config["CosmosDb:DatabaseName"] ?? "DirectoryService";
                try
                {
                    var db = client.GetDatabase(dbName);
                    var dbResponse = await db.ReadAsync();
                    WriteOk($"Database '{dbName}' exists");

                    // Check containers
                    foreach (var container in new[] { "DirectoryObjects", "ChangeLog", "ConfigurationData", "AuditLog" })
                    {
                        try
                        {
                            await db.GetContainer(container).ReadContainerAsync();
                            WriteOk($"  Container '{container}' exists");
                        }
                        catch
                        {
                            WriteWarn($"  Container '{container}' not found (will be created on first run)");
                        }
                    }
                }
                catch
                {
                    WriteWarn($"Database '{dbName}' not found (will be created during setup)");
                }
            }
            catch (Exception ex)
            {
                WriteErr($"Failed to connect: {ex.Message}");
                hasFailure = true;

                if (cosmosConn.Contains("localhost:8081"))
                {
                    Console.ForegroundColor = ConsoleColor.Gray;
                    Console.WriteLine("    Tip: Ensure the Cosmos DB Emulator is running.");
                    Console.WriteLine("    Download: https://aka.ms/cosmosdb-emulator");
                    Console.ResetColor();
                }
            }
        }

        // 4. Port availability
        WriteSection("Port Availability");
        var ports = new (int Port, string Proto, string Name)[]
        {
            (config.GetValue("Ldap:Port", 389), "TCP", "LDAP"),
            (config.GetValue("Ldap:TlsPort", 636), "TCP", "LDAPS"),
            (config.GetValue("Kerberos:Port", 88), "TCP", "Kerberos"),
            (config.GetValue("Dns:Port", 53), "TCP", "DNS"),
            (config.GetValue("Dns:Port", 53), "UDP", "DNS"),
            (config.GetValue("RpcServer:EndpointMapperPort", 1135), "TCP", "RPC EPM"),
            (config.GetValue("RpcServer:ServicePort", 49664), "TCP", "RPC Service"),
            (config.GetValue("Ldap:GcPort", 3268), "TCP", "Global Catalog"),
            (config.GetValue("Ldap:GcTlsPort", 3269), "TCP", "Global Catalog TLS"),
            (464, "TCP", "Kpasswd"),
            (config.GetValue("Replication:HttpPort", 9389), "TCP", "DRS HTTP"),
        };

        foreach (var (port, proto, name) in ports)
        {
            if (IsPortAvailable(port, proto))
                WriteOk($"{name} ({port}/{proto}) is available");
            else
            {
                WriteWarn($"{name} ({port}/{proto}) is in use");
                if (port == 135)
                    Console.WriteLine("      Windows RPC Endpoint Mapper occupies this port. Use port 1135 instead.");
            }
        }

        // 5. TLS Certificate
        WriteSection("TLS Certificate");
        var certPath = config["Ldap:CertificatePath"];
        if (!string.IsNullOrEmpty(certPath))
        {
            if (File.Exists(certPath))
            {
                try
                {
                    var certPassword = config["Ldap:CertificatePassword"] ?? "";
                    var cert = X509CertificateLoader.LoadPkcs12FromFile(certPath, certPassword);
                    WriteOk($"Certificate loaded: {cert.Subject}");
                    WriteOk($"  Issuer: {cert.Issuer}");
                    WriteOk($"  Valid: {cert.NotBefore:yyyy-MM-dd} to {cert.NotAfter:yyyy-MM-dd}");

                    if (cert.NotAfter < DateTime.Now)
                        WriteErr("  Certificate has EXPIRED!");
                    else if (cert.NotAfter < DateTime.Now.AddDays(30))
                        WriteWarn($"  Certificate expires in {(cert.NotAfter - DateTime.Now).Days} days");
                    else
                        WriteOk($"  Expires in {(cert.NotAfter - DateTime.Now).Days} days");
                }
                catch (Exception ex)
                {
                    WriteErr($"Failed to load certificate: {ex.Message}");
                    hasFailure = true;
                }
            }
            else
            {
                WriteErr($"Certificate file not found: {certPath}");
                hasFailure = true;
            }
        }
        else
        {
            WriteWarn("No TLS certificate configured (Ldap:CertificatePath)");
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine("    LDAPS and Global Catalog TLS will not be available.");
            Console.ResetColor();
        }

        // 6. DNS resolution test
        WriteSection("DNS Resolution");
        var forestDns = config["NamingContexts:ForestDnsName"];
        if (!string.IsNullOrEmpty(forestDns))
        {
            try
            {
                var addresses = await System.Net.Dns.GetHostAddressesAsync(forestDns);
                if (addresses.Length > 0)
                    WriteOk($"{forestDns} resolves to {string.Join(", ", addresses.Select(a => a.ToString()))}");
                else
                    WriteWarn($"{forestDns} did not resolve to any addresses");
            }
            catch
            {
                WriteWarn($"{forestDns} could not be resolved (expected if DNS is not yet configured)");
            }
        }

        try
        {
            var hostname = System.Net.Dns.GetHostName();
            var hostAddresses = await System.Net.Dns.GetHostAddressesAsync(hostname);
            WriteOk($"Local hostname: {hostname}");
            WriteOk($"Local addresses: {string.Join(", ", hostAddresses.Where(a => a.AddressFamily == AddressFamily.InterNetwork).Select(a => a.ToString()))}");
        }
        catch (Exception ex)
        {
            WriteWarn($"Could not resolve local hostname: {ex.Message}");
        }

        // 7. Redis connectivity (if configured)
        WriteSection("Cache (Redis)");
        var redisConn = config["Cache:RedisConnectionString"];
        if (!string.IsNullOrEmpty(redisConn))
        {
            WriteOk($"Redis configured: {redisConn.Split(',')[0]}");
            // Don't actually connect - just confirm it's configured
        }
        else
        {
            WriteOk("Redis not configured — using in-memory cache (single-instance mode)");
        }

        // Summary
        Console.WriteLine();
        if (hasFailure)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("  ✗ Diagnostics completed with errors. Review issues above.");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("  ✓ All diagnostics passed.");
        }
        Console.ResetColor();
        Console.WriteLine();

        return hasFailure ? 1 : 0;
    }

    // Helper methods for colored output
    private static void WriteSection(string title)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\n▶ {title}");
        Console.ResetColor();
    }

    private static void WriteOk(string msg)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write("  ✓ ");
        Console.ResetColor();
        Console.WriteLine(msg);
    }

    private static void WriteWarn(string msg)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write("  ⚠ ");
        Console.ResetColor();
        Console.WriteLine(msg);
    }

    private static void WriteErr(string msg)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Write("  ✗ ");
        Console.ResetColor();
        Console.WriteLine(msg);
    }

    private static bool IsPortAvailable(int port, string proto)
    {
        try
        {
            if (proto == "UDP")
            {
                using var client = new UdpClient(port);
                client.Close();
            }
            else
            {
                using var listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                listener.Stop();
            }
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }
}
