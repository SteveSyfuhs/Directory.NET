namespace Directory.Core.Models;

public static class WellKnownGuids
{
    public const string Users = "a9d1ca15768811d1aded00c04fd8d5cd";
    public const string Computers = "aa312825768811d1aded00c04fd8d5cd";
    public const string DomainControllers = "a361b2ffffd211d1aa4b00c04fd7d83a";
    public const string ForeignSecurityPrincipals = "22b70c67d56e4efb91e9300fca3dc1aa";
    public const string DeletedObjects = "18e2ea80684f11d2b9aa00c04f79f805";
    public const string Infrastructure = "2fbac1870ade11d297c400c04fd8d5cd";
    public const string LostAndFound = "ab8153b7768811d1aded00c04fd8d5cd";
    public const string System = "ab1d30f3768811d1aded00c04fd8d5cd";
    public const string NtdsQuotas = "6227f0af1fc2410d8e3bb10615bb5b0f";

    private static readonly Dictionary<string, string> GuidToName = new(StringComparer.OrdinalIgnoreCase)
    {
        [Users] = "Users",
        [Computers] = "Computers",
        [DomainControllers] = "Domain Controllers",
        [ForeignSecurityPrincipals] = "ForeignSecurityPrincipals",
        [DeletedObjects] = "Deleted Objects",
        [Infrastructure] = "Infrastructure",
        [LostAndFound] = "LostAndFound",
        [System] = "System",
        [NtdsQuotas] = "NTDS Quotas"
    };

    public static string GetContainerName(string guid)
        => GuidToName.GetValueOrDefault(guid);

    public static string ResolveWkGuidDn(string wkGuid, string domainDn)
        => $"<WKGUID={wkGuid},{domainDn}>";
}
