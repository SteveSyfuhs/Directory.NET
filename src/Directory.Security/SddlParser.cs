using System.Text;
using Directory.Core.Models;

namespace Directory.Security;

/// <summary>
/// Parses SDDL (Security Descriptor Definition Language) strings into SecurityDescriptor objects
/// and formats SecurityDescriptor objects back to SDDL.
/// Supports domain-relative SID aliases that are resolved using a provided domain SID.
/// </summary>
public static class SddlParser
{
    /// <summary>
    /// Parse an SDDL string into a SecurityDescriptor, resolving domain-relative SID aliases.
    /// </summary>
    /// <param name="sddl">The SDDL string (e.g., "O:DAG:DAD:PAI(A;;GA;;;DA)(A;;GA;;;SY)")</param>
    /// <param name="domainSid">The domain SID (e.g., "S-1-5-21-xxx-yyy-zzz") used to resolve DA, DU, etc.</param>
    public static SecurityDescriptor Parse(string sddl, string domainSid)
    {
        var sd = new SecurityDescriptor();
        var remaining = sddl.AsSpan();

        while (remaining.Length > 0)
        {
            if (remaining.StartsWith("O:"))
            {
                remaining = remaining[2..];
                sd.OwnerSid = ReadSid(ref remaining, domainSid);
            }
            else if (remaining.StartsWith("G:"))
            {
                remaining = remaining[2..];
                sd.GroupSid = ReadSid(ref remaining, domainSid);
            }
            else if (remaining.StartsWith("D:"))
            {
                remaining = remaining[2..];
                sd.Dacl = ReadAcl(ref remaining, domainSid, out var daclFlags);
                sd.Control |= SdControlFlags.DaclPresent;
                if ((daclFlags & AclFlags.Protected) != 0)
                    sd.Control |= SdControlFlags.DaclProtected;
                if ((daclFlags & AclFlags.AutoInherited) != 0)
                    sd.Control |= SdControlFlags.DaclAutoInherited;
            }
            else if (remaining.StartsWith("S:"))
            {
                remaining = remaining[2..];
                sd.Sacl = ReadAcl(ref remaining, domainSid, out var saclFlags);
                sd.Control |= SdControlFlags.SaclPresent;
                if ((saclFlags & AclFlags.Protected) != 0)
                    sd.Control |= SdControlFlags.SaclProtected;
                if ((saclFlags & AclFlags.AutoInherited) != 0)
                    sd.Control |= SdControlFlags.SaclAutoInherited;
            }
            else
            {
                // Skip unknown characters
                remaining = remaining[1..];
            }
        }

        return sd;
    }

    /// <summary>
    /// Format a SecurityDescriptor back to SDDL string representation.
    /// </summary>
    /// <param name="sd">The security descriptor to format.</param>
    /// <param name="domainSid">The domain SID for reverse-mapping domain-relative SIDs to aliases.</param>
    public static string Format(SecurityDescriptor sd, string domainSid)
    {
        var sb = new StringBuilder();

        if (sd.OwnerSid is not null)
        {
            sb.Append("O:");
            sb.Append(SidToAlias(sd.OwnerSid, domainSid));
        }

        if (sd.GroupSid is not null)
        {
            sb.Append("G:");
            sb.Append(SidToAlias(sd.GroupSid, domainSid));
        }

        if (sd.Dacl is not null && sd.Control.HasFlag(SdControlFlags.DaclPresent))
        {
            sb.Append("D:");
            if (sd.Control.HasFlag(SdControlFlags.DaclProtected))
                sb.Append('P');
            if (sd.Control.HasFlag(SdControlFlags.DaclAutoInherited))
                sb.Append("AI");
            foreach (var ace in sd.Dacl.Aces)
                sb.Append(FormatAce(ace, domainSid));
        }

        if (sd.Sacl is not null && sd.Control.HasFlag(SdControlFlags.SaclPresent))
        {
            sb.Append("S:");
            if (sd.Control.HasFlag(SdControlFlags.SaclProtected))
                sb.Append('P');
            if (sd.Control.HasFlag(SdControlFlags.SaclAutoInherited))
                sb.Append("AI");
            foreach (var ace in sd.Sacl.Aces)
                sb.Append(FormatAce(ace, domainSid));
        }

        return sb.ToString();
    }

    // ── Private parsing helpers ──────────────────────────────────────────

    [Flags]
    private enum AclFlags
    {
        None = 0,
        Protected = 1,
        AutoInherited = 2,
    }

    private static string ReadSid(ref ReadOnlySpan<char> span, string domainSid)
    {
        // SID alias is everything up to the next component marker (D:, G:, S:, O:, or end)
        int end = FindComponentEnd(span);
        var sidStr = span[..end].ToString();
        span = span[end..];
        return ResolveSidAlias(sidStr, domainSid);
    }

    private static AccessControlList ReadAcl(ref ReadOnlySpan<char> span, string domainSid, out AclFlags flags)
    {
        flags = AclFlags.None;
        var acl = new AccessControlList();

        // Read optional flags before the first ACE (before first '(')
        while (span.Length > 0 && span[0] != '(' && !IsComponentStart(span))
        {
            if (span[0] == 'P')
            {
                flags |= AclFlags.Protected;
                span = span[1..];
            }
            else if (span.Length >= 2 && span[0] == 'A' && span[1] == 'I')
            {
                flags |= AclFlags.AutoInherited;
                span = span[2..];
            }
            else if (span.Length >= 2 && span[0] == 'A' && span[1] == 'R')
            {
                // AR = Auto-Inherit Required, skip
                span = span[2..];
            }
            else
            {
                span = span[1..];
            }
        }

        // Read ACE strings: (ace_type;ace_flags;rights;object_guid;inherit_object_guid;account_sid)
        while (span.Length > 0 && span[0] == '(')
        {
            int close = span.IndexOf(')');
            if (close < 0) break;

            var aceStr = span[1..close].ToString();
            span = span[(close + 1)..];

            var ace = ParseAce(aceStr, domainSid);
            if (ace is not null)
                acl.Aces.Add(ace);
        }

        return acl;
    }

    private static AccessControlEntry ParseAce(string aceStr, string domainSid)
    {
        var parts = aceStr.Split(';');
        if (parts.Length < 6) return null;

        var ace = new AccessControlEntry
        {
            Type = ParseAceType(parts[0]),
            Flags = ParseAceFlags(parts[1]),
            Mask = ParseAccessRights(parts[2]),
        };

        // Object GUID (parts[3])
        if (!string.IsNullOrEmpty(parts[3]) && Guid.TryParse(parts[3], out var objectType))
        {
            ace.ObjectType = objectType;
            // If we have an object type, upgrade to object ACE type
            if (ace.Type == AceType.AccessAllowed)
                ace.Type = AceType.AccessAllowedObject;
            else if (ace.Type == AceType.AccessDenied)
                ace.Type = AceType.AccessDeniedObject;
        }

        // Inherited object GUID (parts[4])
        if (!string.IsNullOrEmpty(parts[4]) && Guid.TryParse(parts[4], out var inheritedObjectType))
        {
            ace.InheritedObjectType = inheritedObjectType;
            if (ace.Type == AceType.AccessAllowed)
                ace.Type = AceType.AccessAllowedObject;
            else if (ace.Type == AceType.AccessDenied)
                ace.Type = AceType.AccessDeniedObject;
        }

        // Account SID (parts[5])
        ace.TrusteeSid = ResolveSidAlias(parts[5], domainSid);

        return ace;
    }

    private static AceType ParseAceType(string s) => s.Trim() switch
    {
        "A" => AceType.AccessAllowed,
        "D" => AceType.AccessDenied,
        "OA" => AceType.AccessAllowedObject,
        "OD" => AceType.AccessDeniedObject,
        "AU" => AceType.SystemAudit,
        _ => AceType.AccessAllowed,
    };

    private static AceFlags ParseAceFlags(string s)
    {
        var flags = AceFlags.None;
        var span = s.AsSpan();

        while (span.Length > 0)
        {
            if (span.Length >= 2)
            {
                var pair = span[..2].ToString();
                switch (pair)
                {
                    case "CI": flags |= AceFlags.ContainerInherit; span = span[2..]; continue;
                    case "OI": flags |= AceFlags.ObjectInherit; span = span[2..]; continue;
                    case "NP": flags |= AceFlags.NoPropagateInherit; span = span[2..]; continue;
                    case "IO": flags |= AceFlags.InheritOnly; span = span[2..]; continue;
                    case "ID": flags |= AceFlags.Inherited; span = span[2..]; continue;
                }
            }
            span = span[1..];
        }

        return flags;
    }

    private static int ParseAccessRights(string s)
    {
        // Check if it's a hex number (0x...) or decimal
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(s[2..], System.Globalization.NumberStyles.HexNumber, null, out var hex))
                return hex;
        }
        if (int.TryParse(s, out var dec))
            return dec;

        // Parse as a sequence of 2-letter right aliases
        int mask = 0;
        var span = s.AsSpan();

        while (span.Length >= 2)
        {
            var pair = span[..2].ToString();
            mask |= pair switch
            {
                "GA" => AccessMask.GenericAll,
                "GR" => AccessMask.GenericRead,
                "GW" => AccessMask.GenericWrite,
                "GX" => AccessMask.GenericExecute,
                "RC" => AccessMask.ReadControl,
                "SD" => AccessMask.DeleteObject,
                "WD" => AccessMask.WriteDacl,
                "WO" => AccessMask.WriteOwner,
                "RP" => AccessMask.ReadProperty,
                "WP" => AccessMask.WriteProperty,
                "CC" => AccessMask.CreateChild,
                "DC" => AccessMask.DeleteChild,
                "LC" => AccessMask.ListContents,
                "SW" => 0x00000008, // Self Write
                "LO" => AccessMask.ListObject,
                "DT" => AccessMask.DeleteTree,
                "CR" => AccessMask.ControlAccess,
                _ => 0,
            };
            span = span[2..];
        }

        return mask;
    }

    private static int FindComponentEnd(ReadOnlySpan<char> span)
    {
        for (int i = 0; i < span.Length; i++)
        {
            if (i + 1 < span.Length && span[i + 1] == ':')
            {
                char c = span[i];
                if (c is 'O' or 'G' or 'D' or 'S')
                    return i;
            }
        }
        return span.Length;
    }

    private static bool IsComponentStart(ReadOnlySpan<char> span)
    {
        if (span.Length < 2) return false;
        return span[1] == ':' && span[0] is 'O' or 'G' or 'D' or 'S';
    }

    /// <summary>
    /// Resolve a well-known SDDL SID alias to an actual SID string.
    /// Domain-relative aliases (DA, DU, etc.) require a valid domainSid.
    /// </summary>
    public static string ResolveSidAlias(string alias, string domainSid)
    {
        // If it already looks like a SID string, return as-is
        if (alias.StartsWith("S-", StringComparison.OrdinalIgnoreCase))
            return alias;

        return alias switch
        {
            // Domain-relative SIDs (require domainSid)
            "DA" => $"{domainSid}-512",   // Domain Admins
            "DU" => $"{domainSid}-513",   // Domain Users
            "DG" => $"{domainSid}-514",   // Domain Guests
            "DC" => $"{domainSid}-515",   // Domain Computers
            "DD" => $"{domainSid}-516",   // Domain Controllers
            "CA" => $"{domainSid}-517",   // Cert Publishers
            "SA" => $"{domainSid}-518",   // Schema Admins
            "EA" => $"{domainSid}-519",   // Enterprise Admins
            "PA" => $"{domainSid}-520",   // Group Policy Creator Owners
            "RO" => $"{domainSid}-521",   // Read-Only Domain Controllers
            "LA" => $"{domainSid}-500",   // Local Administrator (domain context)
            "LG" => $"{domainSid}-501",   // Local Guest (domain context)

            // Well-known built-in SIDs
            "WD" => "S-1-1-0",           // Everyone (World)
            "CO" => "S-1-3-0",           // Creator Owner
            "CG" => "S-1-3-1",           // Creator Group
            "AN" => "S-1-5-7",           // Anonymous
            "AU" => "S-1-5-11",          // Authenticated Users
            "SY" => "S-1-5-18",          // Local System
            "PS" => "S-1-5-10",          // Principal Self
            "ED" => "S-1-5-9",           // Enterprise Domain Controllers
            "RC" => "S-1-5-12",          // Restricted Code

            // Built-in aliases (S-1-5-32-xxx)
            "BA" => "S-1-5-32-544",      // Builtin Administrators
            "BU" => "S-1-5-32-545",      // Builtin Users
            "BG" => "S-1-5-32-546",      // Builtin Guests
            "PU" => "S-1-5-32-547",      // Power Users
            "AO" => "S-1-5-32-548",      // Account Operators
            "SO" => "S-1-5-32-549",      // Server Operators
            "PO" => "S-1-5-32-550",      // Print Operators
            "BO" => "S-1-5-32-551",      // Backup Operators
            "RE" => "S-1-5-32-552",      // Replicator
            "RU" => "S-1-5-32-554",      // Pre-Windows 2000 Compatible Access
            "RD" => "S-1-5-32-555",      // Remote Desktop Users
            "NO" => "S-1-5-32-556",      // Network Configuration Operators
            "MU" => "S-1-5-32-558",      // Performance Monitor Users
            "LU" => "S-1-5-32-559",      // Performance Log Users
            "IS" => "S-1-5-32-568",      // IIS_IUSRS
            "CY" => "S-1-5-32-569",      // Crypto Operators
            "ER" => "S-1-5-32-573",      // Event Log Readers
            "CD" => "S-1-5-32-574",      // Certificate Service DCOM Access
            "RA" => "S-1-5-32-575",      // RDS Remote Access Servers
            "ES" => "S-1-5-32-576",      // RDS Endpoint Servers
            "MS" => "S-1-5-32-577",      // RDS Management Servers
            "HA" => "S-1-5-32-578",      // Hyper-V Admins
            "AA" => "S-1-5-32-579",      // Access Control Assistance Operators
            "RM" => "S-1-5-32-580",      // Remote Management Users

            // If we don't recognize it, return as-is (might be a literal SID)
            _ => alias,
        };
    }

    /// <summary>
    /// Reverse-map a SID string to the best matching SDDL alias.
    /// Returns the SID string itself if no alias matches.
    /// </summary>
    private static string SidToAlias(string sid, string domainSid)
    {
        // Check domain-relative SIDs first
        if (!string.IsNullOrEmpty(domainSid) && sid.StartsWith(domainSid, StringComparison.OrdinalIgnoreCase))
        {
            var ridStr = sid[(domainSid.Length + 1)..];
            if (int.TryParse(ridStr, out var rid))
            {
                var alias = rid switch
                {
                    500 => "LA",
                    501 => "LG",
                    512 => "DA",
                    513 => "DU",
                    514 => "DG",
                    515 => "DC",
                    516 => "DD",
                    517 => "CA",
                    518 => "SA",
                    519 => "EA",
                    520 => "PA",
                    521 => "RO",
                    _ => (string)null,
                };
                if (alias is not null) return alias;
            }
        }

        return sid switch
        {
            "S-1-1-0" => "WD",
            "S-1-3-0" => "CO",
            "S-1-3-1" => "CG",
            "S-1-5-7" => "AN",
            "S-1-5-9" => "ED",
            "S-1-5-10" => "PS",
            "S-1-5-11" => "AU",
            "S-1-5-12" => "RC",
            "S-1-5-18" => "SY",
            "S-1-5-32-544" => "BA",
            "S-1-5-32-545" => "BU",
            "S-1-5-32-546" => "BG",
            "S-1-5-32-547" => "PU",
            "S-1-5-32-548" => "AO",
            "S-1-5-32-549" => "SO",
            "S-1-5-32-550" => "PO",
            "S-1-5-32-551" => "BO",
            "S-1-5-32-552" => "RE",
            "S-1-5-32-554" => "RU",
            "S-1-5-32-555" => "RD",
            "S-1-5-32-556" => "NO",
            "S-1-5-32-558" => "MU",
            "S-1-5-32-559" => "LU",
            "S-1-5-32-560" => sid, // Windows Authorization Access Group - no standard alias
            "S-1-5-32-568" => "IS",
            "S-1-5-32-569" => "CY",
            "S-1-5-32-573" => "ER",
            "S-1-5-32-574" => "CD",
            "S-1-5-32-575" => "RA",
            "S-1-5-32-576" => "ES",
            "S-1-5-32-577" => "MS",
            "S-1-5-32-578" => "HA",
            "S-1-5-32-579" => "AA",
            "S-1-5-32-580" => "RM",
            _ => sid,
        };
    }

    private static string FormatAce(AccessControlEntry ace, string domainSid)
    {
        var sb = new StringBuilder();
        sb.Append('(');

        // ACE type
        bool isObject = ace.Type is AceType.AccessAllowedObject or AceType.AccessDeniedObject;
        sb.Append(ace.Type switch
        {
            AceType.AccessAllowed => "A",
            AceType.AccessDenied => "D",
            AceType.AccessAllowedObject => "OA",
            AceType.AccessDeniedObject => "OD",
            AceType.SystemAudit => "AU",
            _ => "A",
        });

        sb.Append(';');

        // ACE flags
        if ((ace.Flags & AceFlags.ContainerInherit) != 0) sb.Append("CI");
        if ((ace.Flags & AceFlags.ObjectInherit) != 0) sb.Append("OI");
        if ((ace.Flags & AceFlags.NoPropagateInherit) != 0) sb.Append("NP");
        if ((ace.Flags & AceFlags.InheritOnly) != 0) sb.Append("IO");
        if ((ace.Flags & AceFlags.Inherited) != 0) sb.Append("ID");

        sb.Append(';');

        // Access rights
        sb.Append(FormatAccessRights(ace.Mask));

        sb.Append(';');

        // Object type GUID
        if (ace.ObjectType.HasValue)
            sb.Append(ace.ObjectType.Value.ToString("D"));

        sb.Append(';');

        // Inherited object type GUID
        if (ace.InheritedObjectType.HasValue)
            sb.Append(ace.InheritedObjectType.Value.ToString("D"));

        sb.Append(';');

        // Trustee SID
        sb.Append(SidToAlias(ace.TrusteeSid, domainSid));

        sb.Append(')');
        return sb.ToString();
    }

    private static string FormatAccessRights(int mask)
    {
        if (mask == AccessMask.GenericAll) return "GA";
        if (mask == AccessMask.GenericRead) return "GR";
        if (mask == AccessMask.GenericWrite) return "GW";
        if (mask == AccessMask.GenericExecute) return "GX";

        var sb = new StringBuilder();

        if ((mask & AccessMask.ReadProperty) != 0) sb.Append("RP");
        if ((mask & AccessMask.WriteProperty) != 0) sb.Append("WP");
        if ((mask & AccessMask.CreateChild) != 0) sb.Append("CC");
        if ((mask & AccessMask.DeleteChild) != 0) sb.Append("DC");
        if ((mask & AccessMask.ListContents) != 0) sb.Append("LC");
        if ((mask & 0x00000008) != 0) sb.Append("SW"); // Self Write
        if ((mask & AccessMask.ListObject) != 0) sb.Append("LO");
        if ((mask & AccessMask.DeleteTree) != 0) sb.Append("DT");
        if ((mask & AccessMask.ControlAccess) != 0) sb.Append("CR");
        if ((mask & AccessMask.DeleteObject) != 0) sb.Append("SD");
        if ((mask & AccessMask.ReadControl) != 0) sb.Append("RC");
        if ((mask & AccessMask.WriteDacl) != 0) sb.Append("WD");
        if ((mask & AccessMask.WriteOwner) != 0) sb.Append("WO");

        return sb.Length > 0 ? sb.ToString() : $"0x{mask:X8}";
    }
}
