namespace Directory.Web.Services;

/// <summary>
/// Static definitions for known flag/bitmask attributes in Active Directory.
/// Used by AttributeFormatter to decompose integer values into named flags.
/// </summary>
public static class FlagDefinitions
{
    public static readonly Dictionary<string, Dictionary<int, string>> KnownFlags = new(StringComparer.OrdinalIgnoreCase)
    {
        ["userAccountControl"] = new()
        {
            [0x0001] = "SCRIPT",
            [0x0002] = "ACCOUNTDISABLE",
            [0x0008] = "HOMEDIR_REQUIRED",
            [0x0010] = "LOCKOUT",
            [0x0020] = "PASSWD_NOTREQD",
            [0x0040] = "PASSWD_CANT_CHANGE",
            [0x0080] = "ENCRYPTED_TEXT_PASSWORD_ALLOWED",
            [0x0100] = "TEMP_DUPLICATE_ACCOUNT",
            [0x0200] = "NORMAL_ACCOUNT",
            [0x0800] = "INTERDOMAIN_TRUST_ACCOUNT",
            [0x1000] = "WORKSTATION_TRUST_ACCOUNT",
            [0x2000] = "SERVER_TRUST_ACCOUNT",
            [0x10000] = "DONT_EXPIRE_PASSWD",
            [0x20000] = "MNS_LOGON_ACCOUNT",
            [0x40000] = "SMARTCARD_REQUIRED",
            [0x80000] = "TRUSTED_FOR_DELEGATION",
            [0x100000] = "NOT_DELEGATED",
            [0x200000] = "USE_DES_KEY_ONLY",
            [0x400000] = "DONT_REQUIRE_PREAUTH",
            [0x800000] = "PASSWORD_EXPIRED",
            [0x1000000] = "TRUSTED_TO_AUTHENTICATE_FOR_DELEGATION",
            [0x4000000] = "PARTIAL_SECRETS_ACCOUNT",
        },
        ["groupType"] = new()
        {
            [1] = "BUILTIN_LOCAL_GROUP",
            [2] = "ACCOUNT_GROUP (Global)",
            [4] = "RESOURCE_GROUP (Domain Local)",
            [8] = "UNIVERSAL_GROUP",
            [unchecked((int)0x80000000)] = "SECURITY_ENABLED",
        },
        ["sAMAccountType"] = new()
        {
            [0x0] = "DOMAIN_OBJECT",
            [0x10000000] = "GROUP_OBJECT",
            [0x10000001] = "NON_SECURITY_GROUP_OBJECT",
            [0x20000000] = "ALIAS_OBJECT",
            [0x20000001] = "NON_SECURITY_ALIAS_OBJECT",
            [0x30000000] = "NORMAL_USER_ACCOUNT",
            [0x30000001] = "MACHINE_ACCOUNT",
            [0x30000002] = "TRUST_ACCOUNT",
            [0x40000000] = "APP_BASIC_GROUP",
            [0x40000001] = "APP_QUERY_GROUP",
        },
        ["systemFlags"] = new()
        {
            [0x00000001] = "FLAG_ATTR_NOT_REPLICATED",
            [0x00000002] = "FLAG_ATTR_REQ_PARTIAL_SET_MEMBER",
            [0x00000004] = "FLAG_ATTR_IS_CONSTRUCTED",
            [0x00000010] = "FLAG_ATTR_IS_OPERATIONAL",
            [0x01000000] = "FLAG_DOMAIN_DISALLOW_MOVE",
            [0x02000000] = "FLAG_DOMAIN_DISALLOW_RENAME",
            [0x04000000] = "FLAG_CONFIG_ALLOW_LIMITED_MOVE",
            [0x08000000] = "FLAG_CONFIG_ALLOW_MOVE",
            [0x10000000] = "FLAG_CONFIG_ALLOW_RENAME",
            [0x40000000] = "FLAG_DISALLOW_DELETE",
        },
        ["searchFlags"] = new()
        {
            [0x0001] = "fATTINDEX",
            [0x0002] = "fPDNTATTINDEX",
            [0x0004] = "fANR",
            [0x0008] = "fPRESERVEONDELETE",
            [0x0010] = "fCOPY",
            [0x0020] = "fTUPLEINDEX",
            [0x0040] = "fSUBTREEATTINDEX",
            [0x0080] = "fCONFIDENTIAL",
            [0x0100] = "fNEVERVALUEAUDIT",
            [0x0200] = "fRODCFilteredAttribute",
        },
    };

    /// <summary>
    /// Decompose an integer value into its constituent named flags for a known attribute.
    /// For sAMAccountType (which is an enumeration, not a bitmask), returns the matching enum name.
    /// </summary>
    public static List<string> DecomposeFlags(string attributeName, int value)
    {
        var result = new List<string>();

        if (!KnownFlags.TryGetValue(attributeName, out var flagMap))
            return result;

        // sAMAccountType is an enumeration, not a bitmask
        if (attributeName.Equals("sAMAccountType", StringComparison.OrdinalIgnoreCase))
        {
            if (flagMap.TryGetValue(value, out var enumName))
                result.Add(enumName);
            return result;
        }

        foreach (var (flag, name) in flagMap)
        {
            if (flag == 0)
                continue;

            if ((value & flag) == flag)
                result.Add(name);
        }

        return result;
    }
}
