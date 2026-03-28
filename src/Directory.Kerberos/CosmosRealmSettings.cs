using Kerberos.NET.Server;

namespace Directory.Kerberos;

public class CosmosRealmSettings : IRealmSettings
{
    private readonly KerberosOptions _options;

    public CosmosRealmSettings(KerberosOptions options)
    {
        _options = options;
    }

    public TimeSpan MaximumSkew => _options.MaximumSkew;
    public TimeSpan SessionLifetime => _options.SessionLifetime;
    public TimeSpan MaximumRenewalWindow => _options.MaximumRenewalWindow;

    public KerberosCompatibilityFlags Compatibility => KerberosCompatibilityFlags.None;
}
