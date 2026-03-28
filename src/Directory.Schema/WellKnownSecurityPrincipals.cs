using Directory.Core.Interfaces;
using Directory.Core.Models;
using Microsoft.Extensions.Logging;

namespace Directory.Schema;

/// <summary>
/// Provisions the well-known security principals defined in [MS-ADTS] section 7.1.1.6.
/// These objects live in CN=WellKnown Security Principals,CN=Configuration,{domainDn}
/// and represent built-in SIDs such as Everyone, Authenticated Users, etc.
/// </summary>
public class WellKnownSecurityPrincipals
{
    private readonly IDirectoryStore _store;
    private readonly ILogger<WellKnownSecurityPrincipals> _logger;

    /// <summary>
    /// Well-known security principal definitions: (CN, SID, description).
    /// </summary>
    private static readonly (string Name, string Sid, string Description)[] Principals =
    [
        ("Anonymous Logon",               "S-1-5-7",       "A user who has connected without supplying credentials"),
        ("Authenticated Users",           "S-1-5-11",      "Users who have been authenticated"),
        ("Batch",                         "S-1-5-3",       "Users logged on via a batch queue facility"),
        ("Creator Owner",                 "S-1-3-0",       "A placeholder in an inheritable ACE replaced by the object creator's SID"),
        ("Dialup",                        "S-1-5-1",       "Users logged on via a dialup connection"),
        ("Digest Authentication",         "S-1-5-64-21",   "Clients authenticated using Digest authentication"),
        ("Enterprise Domain Controllers", "S-1-5-9",       "All domain controllers in the enterprise"),
        ("Everyone",                      "S-1-1-0",       "All users"),
        ("Interactive",                   "S-1-5-4",       "Users logged on interactively"),
        ("Local Service",                 "S-1-5-19",      "A service account for services that need minimum privileges on the local computer"),
        ("Network",                       "S-1-5-2",       "Users logged on via a network connection"),
        ("Network Service",              "S-1-5-20",      "A service account for services that need to access network resources"),
        ("NTLM Authentication",           "S-1-5-64-10",   "Clients authenticated using NTLM authentication"),
        ("Other Organization",            "S-1-5-1000",    "Users from another organization"),
        ("Remote Interactive Logon",      "S-1-5-14",      "Users logged on via Remote Desktop"),
        ("Restricted",                    "S-1-5-12",      "A restricted process-level security context"),
        ("SChannel Authentication",       "S-1-5-64-14",   "Clients authenticated using SChannel (TLS/SSL)"),
        ("Self",                          "S-1-5-10",      "A placeholder for the object itself in ACEs"),
        ("Service",                       "S-1-5-6",       "Users logged on as a service"),
        ("Terminal Server User",          "S-1-5-13",      "Users logged on to a Terminal Services server"),
        ("This Organization",             "S-1-5-15",      "Users in the same organization"),
    ];

    public WellKnownSecurityPrincipals(
        IDirectoryStore store,
        ILogger<WellKnownSecurityPrincipals> logger)
    {
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// Returns the list of well-known security principal definitions.
    /// </summary>
    public static IReadOnlyList<(string Name, string Sid, string Description)> GetDefinitions() => Principals;

    /// <summary>
    /// Provisions all well-known security principals under the Configuration naming context.
    /// Creates the container CN=WellKnown Security Principals,CN=Configuration if it does not exist,
    /// then creates each principal object if it does not already exist.
    /// </summary>
    public async Task ProvisionAsync(string tenantId, string domainDn, CancellationToken ct = default)
    {
        var configDn = $"CN=Configuration,{domainDn}";
        var containerDn = $"CN=WellKnown Security Principals,{configDn}";

        // Ensure the container exists
        var container = await _store.GetByDnAsync(tenantId, containerDn, ct);
        if (container == null)
        {
            container = CreateObject(
                tenantId, domainDn, containerDn, configDn,
                ["top", "container"],
                "WellKnown Security Principals");
            await _store.CreateAsync(container, ct);
            _logger.LogInformation("Created container: {Dn}", containerDn);
        }

        // Create each principal
        int created = 0;
        foreach (var (name, sid, description) in Principals)
        {
            var principalDn = $"CN={name},{containerDn}";
            var existing = await _store.GetByDnAsync(tenantId, principalDn, ct);
            if (existing != null)
                continue;

            var obj = CreateObject(
                tenantId, domainDn, principalDn, containerDn,
                ["top", "foreignSecurityPrincipal"],
                name);

            obj.ObjectSid = sid;
            obj.Description = description;

            await _store.CreateAsync(obj, ct);
            created++;
        }

        _logger.LogInformation(
            "Well-known security principals provisioned: {Created} created, {Skipped} already existed",
            created, Principals.Length - created);
    }

    private static DirectoryObject CreateObject(
        string tenantId, string domainDn, string dn, string parentDn,
        List<string> objectClass, string cn)
    {
        var now = DateTimeOffset.UtcNow;
        return new DirectoryObject
        {
            Id = dn.ToLowerInvariant(),
            TenantId = tenantId,
            DomainDn = domainDn,
            DistinguishedName = dn,
            ObjectGuid = Guid.NewGuid().ToString(),
            ObjectClass = objectClass,
            ParentDn = parentDn,
            Cn = cn,
            WhenCreated = now,
            WhenChanged = now,
        };
    }
}
