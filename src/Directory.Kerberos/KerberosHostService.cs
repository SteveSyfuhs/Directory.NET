using Directory.Core.Interfaces;
using Directory.Kerberos.S4U;
using Directory.Security.Apds;
using Kerberos.NET.Server;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Directory.Kerberos;

/// <summary>
/// Hosted service that runs the Kerberos KDC on port 88 using Kerberos.NET.
/// </summary>
public class KerberosHostService : IHostedService, IDisposable
{
    private readonly IDirectoryStore _store;
    private readonly IPasswordPolicy _passwordPolicy;
    private readonly KerberosOptions _options;
    private readonly PacGenerator _pacGenerator;
    private readonly AccountRestrictions _accountRestrictions;
    private readonly SpnService _spnService;
    private readonly S4UDelegationProcessor _s4uProcessor;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<KerberosHostService> _logger;
    private readonly TrustedRealmService _trustedRealmService;
    private KdcServiceListener _listener;

    public KerberosHostService(
        IDirectoryStore store,
        IPasswordPolicy passwordPolicy,
        IOptions<KerberosOptions> options,
        PacGenerator pacGenerator,
        AccountRestrictions accountRestrictions,
        SpnService spnService,
        S4UDelegationProcessor s4uProcessor,
        TrustedRealmService trustedRealmService,
        ILoggerFactory loggerFactory,
        ILogger<KerberosHostService> logger)
    {
        _store = store;
        _passwordPolicy = passwordPolicy;
        _options = options.Value;
        _pacGenerator = pacGenerator;
        _accountRestrictions = accountRestrictions;
        _spnService = spnService;
        _s4uProcessor = s4uProcessor;
        _trustedRealmService = trustedRealmService;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Load trust relationships from CosmosDB before starting the KDC
        await _trustedRealmService.LoadTrustsAsync(cancellationToken);

        var kdcOptions = new ListenerOptions
        {
            DefaultRealm = _options.DefaultRealm,
            RealmLocator = realm => CreateRealmService(realm),
            Log = _loggerFactory,
        };

        _listener = new KdcServiceListener(kdcOptions);
        _ = _listener.Start();

        _logger.LogInformation("Kerberos KDC started on port {Port}, realm={Realm}",
            _options.Port, _options.DefaultRealm);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Kerberos KDC stopping");
        _listener?.Stop();
        return Task.CompletedTask;
    }

    private IRealmService CreateRealmService(string realm)
    {
        return new CosmosRealmService(
            _store,
            _passwordPolicy,
            Options.Create(_options with { DefaultRealm = realm }),
            _pacGenerator,
            _accountRestrictions,
            _spnService,
            _s4uProcessor,
            _trustedRealmService,
            _loggerFactory);
    }

    public void Dispose()
    {
        _listener?.Dispose();
    }
}
