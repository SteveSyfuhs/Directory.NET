namespace Directory.Web.Models;

/// <summary>
/// Domain-level configuration populated from the directory at startup.
/// This is the Web project's own copy to avoid coupling to Directory.Server.
/// </summary>
public class DomainConfiguration
{
    // Domain identity
    public string DomainDn { get; set; } = "";
    public string DomainDnsName { get; set; } = "";
    public string NetBiosName { get; set; } = "";
    public string DomainSid { get; set; } = "";
    public string ForestDnsName { get; set; } = "";
    public string KerberosRealm { get; set; } = "";

    // Password policy
    public int MinPwdLength { get; set; } = 7;
    public int PwdHistoryLength { get; set; } = 24;
    public int LockoutThreshold { get; set; }
    public long MaxPwdAge { get; set; }
    public long MinPwdAge { get; set; }
    public int PwdProperties { get; set; }
    public long LockoutDuration { get; set; }
    public long LockoutObservationWindow { get; set; }

    // Functional level
    public int DomainFunctionalLevel { get; set; } = 7; // Windows Server 2016
}
