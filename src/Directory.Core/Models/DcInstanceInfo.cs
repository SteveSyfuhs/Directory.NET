namespace Directory.Core.Models;

public class DcInstanceInfo
{
    public string InstanceId { get; set; } = Guid.NewGuid().ToString("N")[..12]; // Short unique ID
    public string Hostname { get; set; } = Environment.MachineName;
    public string SiteName { get; set; } = "Default-First-Site-Name";
    public string IpAddress { get; set; } = "0.0.0.0";
    public string InvocationId { get; set; } = Guid.NewGuid().ToString();
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Whether this DC is a Read-Only Domain Controller (RODC).
    /// When true, write operations are refused and the DC reports itself as non-writable.
    /// </summary>
    public bool IsRodc { get; set; }

    // DN of this DC's NTDS Settings object
    public string NtdsSettingsDn(string domainDn) =>
        $"CN=NTDS Settings,CN={Hostname},CN=Servers,CN={SiteName},CN=Sites,CN=Configuration,{domainDn}";

    // DN of this DC's server object
    public string ServerDn(string domainDn) =>
        $"CN={Hostname},CN=Servers,CN={SiteName},CN=Sites,CN=Configuration,{domainDn}";

    // FQDN
    public string Fqdn(string domainName) => $"{Hostname.ToLowerInvariant()}.{domainName}";
}
