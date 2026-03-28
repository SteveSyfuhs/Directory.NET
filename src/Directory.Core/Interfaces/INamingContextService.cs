using Directory.Core.Models;

namespace Directory.Core.Interfaces;

public interface INamingContextService
{
    NamingContext GetNamingContext(string dn);
    NamingContext GetDomainNc();
    NamingContext GetConfigurationNc();
    NamingContext GetSchemaNc();
    IReadOnlyList<NamingContext> GetAllNamingContexts();
    bool IsDnInNamingContext(string dn, NamingContextType type);
    string GetConfigurationDn();
    string GetSchemaDn();

    /// <summary>
    /// Reconfigures naming contexts at runtime (e.g., after domain provisioning).
    /// </summary>
    void Reconfigure(string domainDn, string forestDnsName, string domainSid = null);
}
