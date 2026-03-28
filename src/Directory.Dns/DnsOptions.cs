namespace Directory.Dns;

public class DnsOptions
{
    public const string SectionName = "Dns";

    public int Port { get; set; } = 53;
    public string ServerHostname { get; set; } = "dc1.directory.local";
    public List<string> ServerIpAddresses { get; set; } = ["127.0.0.1"];
    public List<string> Domains { get; set; } = ["directory.local"];

    /// <summary>
    /// Forest DNS name for Global Catalog SRV record resolution.
    /// When set and different from the domain, forest-level zones
    /// (e.g., _gc._tcp.{forest}, _ldap._tcp.gc._msdcs.{forest}) are also
    /// treated as local authoritative zones.
    /// If empty, defaults to the first domain in <see cref="Domains"/>.
    /// </summary>
    public string ForestDnsName { get; set; }

    public int DefaultTtl { get; set; } = 600;

    /// <summary>
    /// EDNS0 advertised UDP payload size. Standard is 4096.
    /// </summary>
    public ushort EdnsUdpPayloadSize { get; set; } = 4096;

    /// <summary>
    /// Conditional forwarders for zones not hosted locally.
    /// </summary>
    public List<DnsForwarderEntry> Forwarders { get; set; } = [];

    /// <summary>
    /// Default upstream DNS server for queries that don't match any local zone or conditional forwarder.
    /// If empty, NXDOMAIN is returned for unmatched queries.
    /// </summary>
    public string DefaultForwarder { get; set; }
}

/// <summary>
/// A conditional forwarder entry mapping a DNS zone to one or more upstream DNS server addresses.
/// </summary>
public class DnsForwarderEntry
{
    /// <summary>
    /// The DNS zone name that triggers forwarding (e.g., "contoso.com").
    /// </summary>
    public string ZoneName { get; set; } = string.Empty;

    /// <summary>
    /// IP addresses of the upstream DNS servers to forward queries to.
    /// </summary>
    public List<string> ForwarderAddresses { get; set; } = [];
}
