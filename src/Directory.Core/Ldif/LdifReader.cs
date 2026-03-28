using System.Text;

namespace Directory.Core.Ldif;

/// <summary>
/// Parses LDIF files per RFC 2849.
/// Handles content records, change records, base64-decoded values (::),
/// URL references (&lt;), line continuations, and comments.
/// </summary>
public static class LdifReader
{
    /// <summary>
    /// Parse an LDIF stream into a list of records.
    /// </summary>
    public static async Task<List<LdifRecord>> ParseAsync(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var content = await reader.ReadToEndAsync();
        return Parse(content);
    }

    /// <summary>
    /// Parse an LDIF string into a list of records.
    /// </summary>
    public static List<LdifRecord> Parse(string ldifContent)
    {
        var records = new List<LdifRecord>();
        var lines = UnfoldLines(ldifContent);

        // Skip version line if present
        int startIndex = 0;
        if (lines.Count > 0)
        {
            var firstLine = lines[0].Trim();
            if (firstLine.StartsWith("version:", StringComparison.OrdinalIgnoreCase))
                startIndex = 1;
        }

        var currentLines = new List<string>();

        for (int i = startIndex; i < lines.Count; i++)
        {
            var line = lines[i];

            // Empty line signals end of a record
            if (string.IsNullOrWhiteSpace(line))
            {
                if (currentLines.Count > 0)
                {
                    var record = ParseRecord(currentLines);
                    if (record != null)
                        records.Add(record);
                    currentLines.Clear();
                }
                continue;
            }

            currentLines.Add(line);
        }

        // Handle last record (no trailing blank line)
        if (currentLines.Count > 0)
        {
            var record = ParseRecord(currentLines);
            if (record != null)
                records.Add(record);
        }

        return records;
    }

    /// <summary>
    /// Unfold lines: RFC 2849 continuation lines start with a single space.
    /// Also strips comment lines (starting with #).
    /// </summary>
    private static List<string> UnfoldLines(string content)
    {
        var result = new List<string>();
        var rawLines = content.Split('\n');

        StringBuilder current = null;

        foreach (var rawLine in rawLines)
        {
            // Trim trailing CR for Windows line endings
            var line = rawLine.TrimEnd('\r');

            // Skip comments
            if (line.StartsWith('#'))
                continue;

            // Continuation line: starts with a single space
            if (line.Length > 0 && line[0] == ' ')
            {
                if (current != null)
                {
                    current.Append(line.AsSpan(1));
                }
                continue;
            }

            // New line — flush previous
            if (current != null)
            {
                result.Add(current.ToString());
            }

            current = new StringBuilder(line);
        }

        // Flush last line
        if (current != null)
        {
            result.Add(current.ToString());
        }

        return result;
    }

    /// <summary>
    /// Parse a single record from its constituent lines.
    /// </summary>
    private static LdifRecord ParseRecord(List<string> lines)
    {
        if (lines.Count == 0)
            return null;

        // First line must be dn:
        var (dnAttr, dnValue) = ParseLine(lines[0]);
        if (!string.Equals(dnAttr, "dn", StringComparison.OrdinalIgnoreCase))
            return null;

        var record = new LdifRecord { Dn = dnValue };

        // Check if there's a changetype line
        int attrStart = 1;
        if (lines.Count > 1)
        {
            var (attr, val) = ParseLine(lines[1]);
            if (string.Equals(attr, "changetype", StringComparison.OrdinalIgnoreCase))
            {
                record.ChangeType = val.ToLowerInvariant() switch
                {
                    "add" => LdifChangeType.Add,
                    "modify" => LdifChangeType.Modify,
                    "delete" => LdifChangeType.Delete,
                    "moddn" or "modrdn" => LdifChangeType.ModDn,
                    _ => LdifChangeType.Content
                };
                attrStart = 2;
            }
        }

        switch (record.ChangeType)
        {
            case LdifChangeType.Modify:
                ParseModifyRecord(record, lines, attrStart);
                break;

            case LdifChangeType.ModDn:
                ParseModDnRecord(record, lines, attrStart);
                break;

            case LdifChangeType.Delete:
                // No additional data for delete records
                break;

            default:
                // Content and Add records have the same attribute format
                ParseAttributes(record, lines, attrStart);
                break;
        }

        return record;
    }

    /// <summary>
    /// Parse attribute lines into the record's Attributes dictionary.
    /// </summary>
    private static void ParseAttributes(LdifRecord record, List<string> lines, int startIndex)
    {
        for (int i = startIndex; i < lines.Count; i++)
        {
            var (attr, val) = ParseLine(lines[i]);
            if (string.IsNullOrEmpty(attr))
                continue;

            if (!record.Attributes.TryGetValue(attr, out var values))
            {
                values = [];
                record.Attributes[attr] = values;
            }
            values.Add(val);
        }
    }

    /// <summary>
    /// Parse a changetype: modify record's modifications.
    /// Format: operation: attrName, then values, separated by "-" lines.
    /// </summary>
    private static void ParseModifyRecord(LdifRecord record, List<string> lines, int startIndex)
    {
        record.Modifications = [];
        LdifModification current = null;

        for (int i = startIndex; i < lines.Count; i++)
        {
            var line = lines[i].Trim();

            // Separator between modification groups
            if (line == "-")
            {
                if (current != null)
                {
                    record.Modifications.Add(current);
                    current = null;
                }
                continue;
            }

            var (attr, val) = ParseLine(lines[i]);
            if (string.IsNullOrEmpty(attr))
                continue;

            if (current == null)
            {
                // This is the operation line: "add: attrName", "delete: attrName", "replace: attrName"
                current = new LdifModification
                {
                    Operation = attr.ToLowerInvariant(),
                    AttributeName = val
                };
            }
            else
            {
                // This is a value line
                current.Values.Add(val);
            }
        }

        // Add the last modification if not terminated with "-"
        if (current != null)
        {
            record.Modifications.Add(current);
        }
    }

    /// <summary>
    /// Parse a changetype: moddn/modrdn record.
    /// </summary>
    private static void ParseModDnRecord(LdifRecord record, List<string> lines, int startIndex)
    {
        for (int i = startIndex; i < lines.Count; i++)
        {
            var (attr, val) = ParseLine(lines[i]);
            switch (attr?.ToLowerInvariant())
            {
                case "newrdn":
                    record.NewRdn = val;
                    break;
                case "deleteoldrdn":
                    record.DeleteOldRdn = val == "1" || string.Equals(val, "true", StringComparison.OrdinalIgnoreCase);
                    break;
                case "newsuperior":
                    record.NewSuperior = val;
                    break;
            }
        }
    }

    /// <summary>
    /// Parse a single LDIF line into attribute name and value.
    /// Handles:
    /// - "attr: value" (plain text)
    /// - "attr:: base64value" (base64-encoded)
    /// - "attr:&lt; url" (URL reference -- decoded as the URL string)
    /// </summary>
    private static (string attr, string value) ParseLine(string line)
    {
        var colonIndex = line.IndexOf(':');
        if (colonIndex < 0)
            return (string.Empty, string.Empty);

        var attr = line[..colonIndex];
        var rest = line.AsSpan(colonIndex + 1);

        // Base64-encoded value: "attr:: base64"
        if (rest.Length > 0 && rest[0] == ':')
        {
            var base64 = rest[1..].Trim().ToString();
            try
            {
                var bytes = Convert.FromBase64String(base64);
                return (attr, Encoding.UTF8.GetString(bytes));
            }
            catch (FormatException)
            {
                // If base64 decode fails, return raw value
                return (attr, base64);
            }
        }

        // URL reference: "attr:< url"
        if (rest.Length > 0 && rest[0] == '<')
        {
            var url = rest[1..].Trim().ToString();
            return (attr, url);
        }

        // Plain text value
        var value = rest.TrimStart(' ').ToString();
        return (attr, value);
    }
}
