namespace Directory.Core.Models;

public enum NamingContextType
{
    Domain,
    Configuration,
    Schema,
    ApplicationPartition
}

public class NamingContext
{
    public required NamingContextType Type { get; set; }
    public required string Dn { get; set; }
    public required string DnsName { get; set; }
    public string DomainSid { get; set; }
    public List<FsmoRoleHolder> FsmoRoles { get; set; } = [];
    public bool IsGlobalCatalog { get; set; }
}
