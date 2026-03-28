using Directory.Ldap.Protocol;
using Directory.Ldap.Protocol.Messages;
using Directory.Ldap.Server;
using Microsoft.Extensions.Options;

namespace Directory.Ldap.Handlers;

/// <summary>
/// Provides the RootDSE entry returned for base-scoped searches with empty base DN.
/// </summary>
public class RootDseProvider
{
    private readonly LdapServerOptions _options;

    public RootDseProvider(IOptions<LdapServerOptions> options)
    {
        _options = options.Value;
    }

    public SearchResultEntry GetRootDse(List<string> requestedAttributes = null)
    {
        var domainDn = _options.DefaultDomain;
        var configDn = $"CN=Configuration,{domainDn}";
        var schemaDn = $"CN=Schema,{configDn}";
        var domainDns = GetDomainDnsName();
        var domainDnsUpper = domainDns.ToUpperInvariant();
        var machineName = Environment.MachineName;
        var siteName = "Default-First-Site-Name";
        var serverDn = $"CN={machineName},CN=Servers,CN={siteName},CN=Sites,{configDn}";
        var ntdsDsaDn = $"CN=NTDS Settings,{serverDn}";

        var allAttrs = new Dictionary<string, List<object>>(StringComparer.OrdinalIgnoreCase)
        {
            // Naming contexts — Windows clients enumerate these to find the domain
            ["namingContexts"] = [domainDn, configDn, schemaDn],
            ["defaultNamingContext"] = [domainDn],
            ["schemaNamingContext"] = [schemaDn],
            ["configurationNamingContext"] = [configDn],
            ["rootDomainNamingContext"] = [domainDn],

            // LDAP version and policies
            ["supportedLDAPVersion"] = ["3"],
            ["supportedLDAPPolicies"] = [
                "MaxPageSize", "MaxValRange", "MaxReceiveBuffer", "MaxPoolThreads",
                "MaxDatagramRecv", "MaxNotificationPerConn", "MaxQueryDuration",
                "MaxTempTableSize", "MaxResultSetSize", "MaxConnIdleTime",
                "MaxActiveQueries", "InitRecvTimeout", "MaxConnections"
            ],

            // Controls — advertise all we support, Windows clients check these
            ["supportedControl"] =
            [
                LdapConstants.OidPagedResults,          // Paged results
                LdapConstants.OidServerSort,            // Server-side sort
                LdapConstants.OidShowDeleted,           // Show tombstones
                LdapConstants.OidTreeDelete,            // Recursive delete
                LdapConstants.OidDirSync,               // DirSync replication
                LdapConstants.OidSdFlags,               // Security descriptor flags
                LdapConstants.OidLazyCommit,            // Lazy commit
                LdapConstants.OidNotificationControl,   // Change notifications
                LdapConstants.OidExtendedDn,            // Extended DN format
                LdapConstants.OidAsq,                   // Attribute scoped query
                LdapConstants.OidVlv,                   // Virtual list view
                LdapConstants.OidPermissiveModify,      // Permissive modify
                LdapConstants.OidDomainScope,           // Domain scope
                LdapConstants.OidSearchOptions,         // Search options (phantom root)
                LdapConstants.OidRangeRetrieval,        // Range retrieval
                LdapConstants.OidVerifyName,            // Verify name
                LdapConstants.OidInputDn,               // Input DN
                LdapConstants.OidManageDsaIT,           // Manage DSA IT (suppress referrals)
                LdapConstants.OidPersistentSearch,       // Persistent search
            ],

            // Extended operations
            ["supportedExtension"] =
            [
                LdapConstants.OidStartTls,
                LdapConstants.OidWhoAmI,
                LdapConstants.OidFastBind,
                LdapConstants.OidPasswordModify,
                LdapConstants.OidTtlRefresh,
            ],

            // SASL mechanisms — Windows uses GSS-SPNEGO primarily
            ["supportedSASLMechanisms"] =
            [
                LdapConstants.SaslGssApi,
                LdapConstants.SaslNtlmSsp,
                LdapConstants.SaslPlain,
                LdapConstants.SaslExternal,
            ],

            // AD Capabilities — CRITICAL: Windows checks these to confirm this is AD
            ["supportedCapabilities"] =
            [
                LdapConstants.OidActiveDirectory,              // This IS Active Directory
                LdapConstants.OidActiveDirectoryV51,            // W2K3+
                LdapConstants.OidActiveDirectoryV60,            // W2K8
                LdapConstants.OidActiveDirectoryV61,            // W2K8R2
                LdapConstants.OidActiveDirectoryW8,             // 2012+
                LdapConstants.OidActiveDirectoryLdapInteg,      // LDAP signing support
            ],

            // DC identity — required for domain join
            ["dsServiceName"] = [ntdsDsaDn],
            ["serverName"] = [serverDn],
            ["subschemaSubentry"] = [$"CN=Aggregate,{schemaDn}"],
            ["dnsHostName"] = [$"{machineName.ToLowerInvariant()}.{domainDns}"],
            ["ldapServiceName"] = [$"{domainDns}:{machineName.ToLowerInvariant()}$@{domainDnsUpper}"],

            // Functional levels — 7 = Windows Server 2016+
            ["domainFunctionality"] = ["7"],
            ["forestFunctionality"] = ["7"],
            ["domainControllerFunctionality"] = ["7"],

            // Global catalog and misc
            ["isGlobalCatalogReady"] = ["TRUE"],
            ["isSynchronized"] = ["TRUE"],
            ["highestCommittedUSN"] = ["1000"],
            ["currentTime"] = [DateTime.UtcNow.ToString("yyyyMMddHHmmss.0Z")],
        };

        var entry = new SearchResultEntry
        {
            ObjectName = string.Empty,
        };

        // If specific attributes requested, only return those
        var attrsToReturn = requestedAttributes is { Count: > 0 }
            ? requestedAttributes
            : [.. allAttrs.Keys];

        // Handle "*" for all
        if (attrsToReturn.Contains("*") || attrsToReturn.Contains("+"))
            attrsToReturn = [.. allAttrs.Keys];

        foreach (var attrName in attrsToReturn)
        {
            if (allAttrs.TryGetValue(attrName, out var values))
            {
                entry.Attributes.Add((attrName, values));
            }
        }

        return entry;
    }

    private string GetDomainDnsName()
    {
        // Convert "DC=directory,DC=local" to "directory.local"
        var parts = _options.DefaultDomain.Split(',');
        var dnsLabels = parts
            .Select(p => p.Trim())
            .Where(p => p.StartsWith("DC=", StringComparison.OrdinalIgnoreCase))
            .Select(p => p[3..]);
        return string.Join(".", dnsLabels);
    }
}
