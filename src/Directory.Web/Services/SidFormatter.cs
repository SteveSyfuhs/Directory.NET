namespace Directory.Web.Services;

/// <summary>
/// Formats binary SIDs to string representation and resolves well-known SIDs.
/// </summary>
public static class SidFormatter
{
    private static readonly Dictionary<string, string> WellKnownSids = new(StringComparer.OrdinalIgnoreCase)
    {
        ["S-1-0-0"] = "Nobody",
        ["S-1-1-0"] = "Everyone",
        ["S-1-2-0"] = "LOCAL",
        ["S-1-2-1"] = "CONSOLE LOGON",
        ["S-1-3-0"] = "CREATOR OWNER",
        ["S-1-3-1"] = "CREATOR GROUP",
        ["S-1-5-1"] = "NT AUTHORITY\\DIALUP",
        ["S-1-5-2"] = "NT AUTHORITY\\NETWORK",
        ["S-1-5-3"] = "NT AUTHORITY\\BATCH",
        ["S-1-5-4"] = "NT AUTHORITY\\INTERACTIVE",
        ["S-1-5-6"] = "NT AUTHORITY\\SERVICE",
        ["S-1-5-7"] = "NT AUTHORITY\\ANONYMOUS LOGON",
        ["S-1-5-9"] = "NT AUTHORITY\\ENTERPRISE DOMAIN CONTROLLERS",
        ["S-1-5-10"] = "NT AUTHORITY\\SELF",
        ["S-1-5-11"] = "NT AUTHORITY\\Authenticated Users",
        ["S-1-5-12"] = "NT AUTHORITY\\RESTRICTED",
        ["S-1-5-13"] = "NT AUTHORITY\\TERMINAL SERVER USER",
        ["S-1-5-14"] = "NT AUTHORITY\\REMOTE INTERACTIVE LOGON",
        ["S-1-5-15"] = "NT AUTHORITY\\This Organization",
        ["S-1-5-17"] = "NT AUTHORITY\\IUSR",
        ["S-1-5-18"] = "NT AUTHORITY\\SYSTEM",
        ["S-1-5-19"] = "NT AUTHORITY\\LOCAL SERVICE",
        ["S-1-5-20"] = "NT AUTHORITY\\NETWORK SERVICE",
        ["S-1-5-32-544"] = "BUILTIN\\Administrators",
        ["S-1-5-32-545"] = "BUILTIN\\Users",
        ["S-1-5-32-546"] = "BUILTIN\\Guests",
        ["S-1-5-32-547"] = "BUILTIN\\Power Users",
        ["S-1-5-32-548"] = "BUILTIN\\Account Operators",
        ["S-1-5-32-549"] = "BUILTIN\\Server Operators",
        ["S-1-5-32-550"] = "BUILTIN\\Print Operators",
        ["S-1-5-32-551"] = "BUILTIN\\Backup Operators",
        ["S-1-5-32-552"] = "BUILTIN\\Replicator",
        ["S-1-5-32-554"] = "BUILTIN\\Pre-Windows 2000 Compatible Access",
        ["S-1-5-32-555"] = "BUILTIN\\Remote Desktop Users",
        ["S-1-5-32-556"] = "BUILTIN\\Network Configuration Operators",
        ["S-1-5-32-557"] = "BUILTIN\\Incoming Forest Trust Builders",
        ["S-1-5-32-558"] = "BUILTIN\\Performance Monitor Users",
        ["S-1-5-32-559"] = "BUILTIN\\Performance Log Users",
        ["S-1-5-32-560"] = "BUILTIN\\Windows Authorization Access Group",
        ["S-1-5-32-561"] = "BUILTIN\\Terminal Server License Servers",
        ["S-1-5-32-562"] = "BUILTIN\\Distributed COM Users",
        ["S-1-5-32-568"] = "BUILTIN\\IIS_IUSRS",
        ["S-1-5-32-569"] = "BUILTIN\\Cryptographic Operators",
        ["S-1-5-32-573"] = "BUILTIN\\Event Log Readers",
        ["S-1-5-32-574"] = "BUILTIN\\Certificate Service DCOM Access",
        ["S-1-5-32-575"] = "BUILTIN\\RDS Remote Access Servers",
        ["S-1-5-32-576"] = "BUILTIN\\RDS Endpoint Servers",
        ["S-1-5-32-577"] = "BUILTIN\\RDS Management Servers",
        ["S-1-5-32-578"] = "BUILTIN\\Hyper-V Administrators",
        ["S-1-5-32-579"] = "BUILTIN\\Access Control Assistance Operators",
        ["S-1-5-32-580"] = "BUILTIN\\Remote Management Users",
    };

    /// <summary>
    /// Format a binary SID (byte[]) to its string representation (S-1-5-21-...).
    /// </summary>
    public static string Format(byte[] sid)
    {
        if (sid == null || sid.Length < 8)
            return Convert.ToHexString(sid ?? []);

        try
        {
            byte revision = sid[0];
            byte subAuthorityCount = sid[1];

            // 6-byte big-endian authority
            long authority = 0;
            for (int i = 2; i < 8; i++)
                authority = (authority << 8) | sid[i];

            var result = $"S-{revision}-{authority}";

            for (int i = 0; i < subAuthorityCount && (8 + i * 4 + 3) < sid.Length; i++)
            {
                uint subAuthority = BitConverter.ToUInt32(sid, 8 + i * 4);
                result += $"-{subAuthority}";
            }

            return result;
        }
        catch
        {
            return Convert.ToHexString(sid);
        }
    }

    /// <summary>
    /// Format a SID value. If already a string representation, passes through.
    /// If byte[], converts to string representation.
    /// </summary>
    public static string Format(object value)
    {
        if (value is byte[] bytes)
            return Format(bytes);

        if (value is string s)
        {
            if (s.StartsWith("S-", StringComparison.OrdinalIgnoreCase))
                return s;

            // Try interpreting as hex
            try
            {
                var decoded = Convert.FromHexString(s);
                return Format(decoded);
            }
            catch
            {
                return s;
            }
        }

        return value?.ToString() ?? "";
    }

    /// <summary>
    /// Try to resolve a SID string to a well-known name.
    /// </summary>
    public static string TryResolveWellKnown(string sid)
    {
        WellKnownSids.TryGetValue(sid, out var name);
        return name;
    }
}
