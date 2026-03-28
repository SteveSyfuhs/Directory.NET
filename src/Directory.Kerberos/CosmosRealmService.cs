using Directory.Core.Interfaces;
using Directory.Kerberos.S4U;
using Directory.Security.Apds;
using Kerberos.NET.Entities;
using Kerberos.NET.Server;
using Kerberos.NET.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Directory.Kerberos;

public class CosmosRealmService : IRealmService
{
    private readonly KerberosOptions _options;
    private readonly CosmosPrincipalService _principalService;
    private readonly CosmosRealmSettings _settings;
    private readonly Krb5Config _configuration;
    private readonly ITrustedRealmService _trustedRealmService;

    public CosmosRealmService(
        IDirectoryStore store,
        IPasswordPolicy passwordPolicy,
        IOptions<KerberosOptions> options,
        PacGenerator pacGenerator,
        AccountRestrictions accountRestrictions,
        SpnService spnService,
        S4UDelegationProcessor s4uProcessor,
        TrustedRealmService trustedRealmService,
        ILoggerFactory loggerFactory)
    {
        _options = options.Value;
        _principalService = new CosmosPrincipalService(store, passwordPolicy, _options, pacGenerator, accountRestrictions, spnService, s4uProcessor, loggerFactory.CreateLogger<CosmosPrincipalService>());
        _settings = new CosmosRealmSettings(_options);
        _configuration = BuildKrb5Config(_options);
        _trustedRealmService = trustedRealmService;
    }

    public IRealmSettings Settings => _settings;
    public IPrincipalService Principals => _principalService;
    public ITrustedRealmService TrustedRealms => _trustedRealmService;
    public string Name => _options.DefaultRealm;
    public DateTimeOffset Now() => DateTimeOffset.UtcNow;
    public Krb5Config Configuration => _configuration;

    private static Krb5Config BuildKrb5Config(KerberosOptions options)
    {
        var config = Krb5Config.Default();

        config.Defaults.DefaultRealm = options.DefaultRealm;
        config.Defaults.ClockSkew = options.MaximumSkew;
        config.Defaults.TicketLifetime = options.SessionLifetime;
        config.Defaults.RenewLifetime = options.MaximumRenewalWindow;

        // Configure realm-specific KDC settings for renewals.
        // KdcDefaultPrincipalFlags defaults already include Renewable, Forwardable,
        // Proxiable, TgtBased, AllowTickets, and Service per Kerberos.NET defaults.
        var realmConfig = new Krb5RealmConfig
        {
            KdcMaxRenewableLifetime = options.MaximumRenewalWindow,
            KdcMaxTicketLifetime = options.SessionLifetime,
        };

        config.Realms[options.DefaultRealm] = realmConfig;

        return config;
    }
}
