namespace Directory.Kerberos;

public record KerberosOptions
{
    public const string SectionName = "Kerberos";

    public string DefaultRealm { get; set; } = "DIRECTORY.LOCAL";
    public int Port { get; set; } = 88;
    public TimeSpan MaximumSkew { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromHours(10);
    public TimeSpan MaximumRenewalWindow { get; set; } = TimeSpan.FromDays(7);

    /// <summary>Kpasswd (password change) service port. Default: 464.</summary>
    public int KpasswdPort { get; set; } = 464;
}
