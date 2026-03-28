namespace Directory.Server;

public class DcNodeOptions
{
    public const string SectionName = "DcNode";
    public string Hostname { get; set; } = Environment.MachineName;
    public string SiteName { get; set; } = "Default-First-Site-Name";
    public string TenantId { get; set; } = "default";
    public string DomainDn { get; set; } = ""; // Required - which domain this DC serves
    public string[] BindAddresses { get; set; } = ["0.0.0.0"];
}
