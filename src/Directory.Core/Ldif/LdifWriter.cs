using System.Text;
using Directory.Core.Models;

namespace Directory.Core.Ldif;

/// <summary>
/// Writes directory objects to LDIF format per RFC 2849.
/// Supports both content (dump) and change (add/modify/delete) records.
/// </summary>
public static class LdifWriter
{
    private const int MaxLineLength = 76;

    /// <summary>
    /// Sensitive attributes that must never appear in LDIF exports.
    /// </summary>
    private static readonly HashSet<string> SensitiveAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "nthash", "kerberoskeys", "unicodepwd", "dbcspwd", "supplementalcredentials"
    };

    /// <summary>
    /// Write a list of directory objects as LDIF content records.
    /// </summary>
    public static string WriteContentRecords(IEnumerable<DirectoryObject> entries)
    {
        var sb = new StringBuilder();
        sb.AppendLine("version: 1");
        sb.AppendLine();

        foreach (var entry in entries)
        {
            WriteContentRecord(sb, entry);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Write a single directory object as an LDIF content record.
    /// </summary>
    public static void WriteContentRecord(StringBuilder sb, DirectoryObject entry)
    {
        WriteAttributeValue(sb, "dn", entry.DistinguishedName);

        // objectClass values
        foreach (var oc in entry.ObjectClass)
        {
            WriteAttributeValue(sb, "objectClass", oc);
        }

        // Well-known top-level properties
        WriteIfNotEmpty(sb, "cn", entry.Cn);
        WriteIfNotEmpty(sb, "sAMAccountName", entry.SAMAccountName);
        WriteIfNotEmpty(sb, "userPrincipalName", entry.UserPrincipalName);
        WriteIfNotEmpty(sb, "displayName", entry.DisplayName);
        WriteIfNotEmpty(sb, "description", entry.Description);
        WriteIfNotEmpty(sb, "mail", entry.Mail);
        WriteIfNotEmpty(sb, "objectGUID", entry.ObjectGuid);
        WriteIfNotEmpty(sb, "objectSid", entry.ObjectSid);
        WriteIfNotEmpty(sb, "objectCategory", entry.ObjectCategory);
        WriteIfNotEmpty(sb, "givenName", entry.GivenName);
        WriteIfNotEmpty(sb, "sn", entry.Sn);
        WriteIfNotEmpty(sb, "title", entry.Title);
        WriteIfNotEmpty(sb, "department", entry.Department);
        WriteIfNotEmpty(sb, "company", entry.Company);
        WriteIfNotEmpty(sb, "manager", entry.Manager);
        WriteIfNotEmpty(sb, "dNSHostName", entry.DnsHostName);
        WriteIfNotEmpty(sb, "operatingSystem", entry.OperatingSystem);
        WriteIfNotEmpty(sb, "operatingSystemVersion", entry.OperatingSystemVersion);

        if (entry.UserAccountControl != 0)
            WriteAttributeValue(sb, "userAccountControl", entry.UserAccountControl.ToString());

        if (entry.GroupType != 0)
            WriteAttributeValue(sb, "groupType", entry.GroupType.ToString());

        if (entry.PrimaryGroupId != 0)
            WriteAttributeValue(sb, "primaryGroupID", entry.PrimaryGroupId.ToString());

        // Multi-valued top-level lists
        foreach (var m in entry.MemberOf)
            WriteAttributeValue(sb, "memberOf", m);

        foreach (var m in entry.Member)
            WriteAttributeValue(sb, "member", m);

        foreach (var spn in entry.ServicePrincipalName)
            WriteAttributeValue(sb, "servicePrincipalName", spn);

        // Extra attributes from the dictionary
        foreach (var kvp in entry.Attributes)
        {
            if (SensitiveAttributes.Contains(kvp.Key))
                continue;

            foreach (var val in kvp.Value.Values)
            {
                if (val is byte[] bytes)
                {
                    WriteBase64Value(sb, kvp.Key, bytes);
                }
                else
                {
                    WriteAttributeValue(sb, kvp.Key, val?.ToString() ?? string.Empty);
                }
            }
        }
    }

    /// <summary>
    /// Write LDIF change records (add operations) for a list of directory objects.
    /// </summary>
    public static string WriteAddRecords(IEnumerable<DirectoryObject> entries)
    {
        var sb = new StringBuilder();
        sb.AppendLine("version: 1");
        sb.AppendLine();

        foreach (var entry in entries)
        {
            WriteAttributeValue(sb, "dn", entry.DistinguishedName);
            sb.AppendLine("changetype: add");
            WriteContentBody(sb, entry);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Write a changetype: modify record.
    /// </summary>
    public static string WriteModifyRecord(string dn, IEnumerable<LdifModification> modifications)
    {
        var sb = new StringBuilder();
        WriteAttributeValue(sb, "dn", dn);
        sb.AppendLine("changetype: modify");

        foreach (var mod in modifications)
        {
            sb.AppendLine($"{mod.Operation}: {mod.AttributeName}");
            foreach (var val in mod.Values)
            {
                WriteAttributeValue(sb, mod.AttributeName, val);
            }
            sb.AppendLine("-");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Write a changetype: delete record.
    /// </summary>
    public static string WriteDeleteRecord(string dn)
    {
        var sb = new StringBuilder();
        WriteAttributeValue(sb, "dn", dn);
        sb.AppendLine("changetype: delete");
        return sb.ToString();
    }

    private static void WriteContentBody(StringBuilder sb, DirectoryObject entry)
    {
        foreach (var oc in entry.ObjectClass)
            WriteAttributeValue(sb, "objectClass", oc);

        WriteIfNotEmpty(sb, "cn", entry.Cn);
        WriteIfNotEmpty(sb, "sAMAccountName", entry.SAMAccountName);
        WriteIfNotEmpty(sb, "userPrincipalName", entry.UserPrincipalName);
        WriteIfNotEmpty(sb, "displayName", entry.DisplayName);
        WriteIfNotEmpty(sb, "description", entry.Description);
        WriteIfNotEmpty(sb, "mail", entry.Mail);

        if (entry.UserAccountControl != 0)
            WriteAttributeValue(sb, "userAccountControl", entry.UserAccountControl.ToString());

        foreach (var m in entry.MemberOf)
            WriteAttributeValue(sb, "memberOf", m);
        foreach (var m in entry.Member)
            WriteAttributeValue(sb, "member", m);

        foreach (var kvp in entry.Attributes)
        {
            if (SensitiveAttributes.Contains(kvp.Key))
                continue;

            foreach (var val in kvp.Value.Values)
            {
                if (val is byte[] bytes)
                    WriteBase64Value(sb, kvp.Key, bytes);
                else
                    WriteAttributeValue(sb, kvp.Key, val?.ToString() ?? string.Empty);
            }
        }
    }

    /// <summary>
    /// Write an attribute: value line, using base64 encoding if the value contains
    /// non-safe characters per RFC 2849.
    /// </summary>
    internal static void WriteAttributeValue(StringBuilder sb, string name, string value)
    {
        if (NeedsBase64Encoding(value))
        {
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
            FoldLine(sb, $"{name}:: {encoded}");
        }
        else
        {
            FoldLine(sb, $"{name}: {value}");
        }
    }

    /// <summary>
    /// Write a binary attribute value as base64.
    /// </summary>
    internal static void WriteBase64Value(StringBuilder sb, string name, byte[] data)
    {
        var encoded = Convert.ToBase64String(data);
        FoldLine(sb, $"{name}:: {encoded}");
    }

    /// <summary>
    /// Determines whether a string value needs base64 encoding per RFC 2849.
    /// Values must be base64-encoded if they:
    /// - Start with a space, colon, or less-than sign
    /// - Contain NUL, LF, or CR characters
    /// - Contain any character outside the ASCII safe range (0x20-0x7E)
    /// </summary>
    internal static bool NeedsBase64Encoding(string value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        // Must encode if starts with space, colon, or less-than
        var first = value[0];
        if (first == ' ' || first == ':' || first == '<')
            return true;

        // Must encode if ends with space
        if (value[^1] == ' ')
            return true;

        // Check each character for non-safe values
        foreach (var c in value)
        {
            if (c == '\0' || c == '\n' || c == '\r')
                return true;
            if (c > 0x7E)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Fold a line at MaxLineLength characters per RFC 2849.
    /// Continuation lines start with a single space.
    /// </summary>
    internal static void FoldLine(StringBuilder sb, string line)
    {
        if (line.Length <= MaxLineLength)
        {
            sb.AppendLine(line);
            return;
        }

        sb.AppendLine(line[..MaxLineLength]);
        var remaining = line.AsSpan(MaxLineLength);

        while (remaining.Length > 0)
        {
            // Continuation lines: space + up to (MaxLineLength - 1) characters
            var chunkSize = Math.Min(remaining.Length, MaxLineLength - 1);
            sb.Append(' ');
            sb.AppendLine(remaining[..chunkSize].ToString());
            remaining = remaining[chunkSize..];
        }
    }

    private static void WriteIfNotEmpty(StringBuilder sb, string name, string value)
    {
        if (!string.IsNullOrEmpty(value))
            WriteAttributeValue(sb, name, value);
    }
}
