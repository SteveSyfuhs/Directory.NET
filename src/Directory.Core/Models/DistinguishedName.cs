using System.Text;
using System.Text.RegularExpressions;

namespace Directory.Core.Models;

/// <summary>
/// Represents a parsed LDAP Distinguished Name with RDN components.
/// </summary>
public partial class DistinguishedName : IEquatable<DistinguishedName>
{
    public IReadOnlyList<RelativeDistinguishedName> Components { get; }

    public DistinguishedName(IEnumerable<RelativeDistinguishedName> components)
    {
        Components = components.ToList().AsReadOnly();
    }

    /// <summary>
    /// Parse a DN string like "CN=User1,OU=Sales,DC=corp,DC=com".
    /// </summary>
    public static DistinguishedName Parse(string dn)
    {
        if (string.IsNullOrWhiteSpace(dn))
            return new DistinguishedName([]);

        var components = new List<RelativeDistinguishedName>();
        var parts = SplitDn(dn);

        foreach (var part in parts)
        {
            var eqIdx = part.IndexOf('=');
            if (eqIdx < 0)
                throw new FormatException($"Invalid RDN component: '{part}'");

            var type = part[..eqIdx].Trim();
            var value = UnescapeValue(part[(eqIdx + 1)..].Trim());
            components.Add(new RelativeDistinguishedName(type, value));
        }

        return new DistinguishedName(components);
    }

    /// <summary>
    /// Returns the parent DN (removes the leftmost RDN).
    /// </summary>
    public DistinguishedName Parent()
    {
        if (Components.Count <= 1)
            return new DistinguishedName([]);

        return new DistinguishedName(Components.Skip(1));
    }

    /// <summary>
    /// Returns the leftmost RDN component.
    /// </summary>
    public RelativeDistinguishedName Rdn => Components.Count > 0
        ? Components[0]
        : throw new InvalidOperationException("Empty DN has no RDN");

    /// <summary>
    /// Returns true if this DN is a descendant of the given ancestor.
    /// </summary>
    public bool IsDescendantOf(DistinguishedName ancestor)
    {
        if (ancestor.Components.Count >= Components.Count)
            return false;

        var offset = Components.Count - ancestor.Components.Count;

        for (var i = 0; i < ancestor.Components.Count; i++)
        {
            if (!Components[i + offset].Equals(ancestor.Components[i]))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Returns true if this DN is equal to or a descendant of the given ancestor.
    /// </summary>
    public bool IsEqualOrDescendantOf(DistinguishedName ancestor)
    {
        return Equals(ancestor) || IsDescendantOf(ancestor);
    }

    /// <summary>
    /// Returns true if this DN is a direct child of the given parent.
    /// </summary>
    public bool IsDirectChildOf(DistinguishedName parent)
    {
        return Components.Count == parent.Components.Count + 1 && IsDescendantOf(parent);
    }

    /// <summary>
    /// Well-known AD application partition names that are DC= components
    /// but not part of the actual domain DN.
    /// </summary>
    private static readonly HashSet<string> AppPartitionNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "DomainDnsZones",
        "ForestDnsZones",
    };

    /// <summary>
    /// Gets the domain DN portion — the trailing consecutive DC= components
    /// that represent the actual domain name, stopping at well-known
    /// application partition boundaries (e.g., DC=DomainDnsZones).
    ///
    /// For "DC=_ldap._tcp.corp.com,DC=corp.com,CN=MicrosoftDNS,DC=DomainDnsZones,DC=corp,DC=com"
    /// returns "DC=corp,DC=com" (not all DC= components).
    /// </summary>
    public string GetDomainDn()
    {
        var dcParts = new List<string>();

        for (int i = Components.Count - 1; i >= 0; i--)
        {
            var c = Components[i];
            if (c.Type.Equals("DC", StringComparison.OrdinalIgnoreCase))
            {
                if (AppPartitionNames.Contains(c.Value))
                    break;
                dcParts.Insert(0, $"{c.Type}={c.Value}");
            }
            else
            {
                break;
            }
        }

        return string.Join(",", dcParts);
    }

    /// <summary>
    /// Gets the domain DNS name (e.g., "corp.example.com" from DC=corp,DC=example,DC=com).
    /// Uses only the trailing domain DC= components, excluding application partitions.
    /// </summary>
    public string GetDomainDnsName()
    {
        var labels = new List<string>();

        for (int i = Components.Count - 1; i >= 0; i--)
        {
            var c = Components[i];
            if (c.Type.Equals("DC", StringComparison.OrdinalIgnoreCase))
            {
                if (AppPartitionNames.Contains(c.Value))
                    break;
                labels.Insert(0, c.Value);
            }
            else
            {
                break;
            }
        }

        return string.Join(".", labels);
    }

    public bool IsEmpty => Components.Count == 0;

    public override string ToString()
    {
        return string.Join(",", Components.Select(c => c.ToString()));
    }

    public string ToLowerInvariant() => ToString().ToLowerInvariant();

    public bool Equals(DistinguishedName other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (Components.Count != other.Components.Count) return false;

        for (var i = 0; i < Components.Count; i++)
        {
            if (!Components[i].Equals(other.Components[i]))
                return false;
        }

        return true;
    }

    public override bool Equals(object obj) => Equals(obj as DistinguishedName);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var component in Components)
            hash.Add(component);
        return hash.ToHashCode();
    }

    public static bool operator ==(DistinguishedName left, DistinguishedName right)
        => Equals(left, right);

    public static bool operator !=(DistinguishedName left, DistinguishedName right)
        => !Equals(left, right);

    private static List<string> SplitDn(string dn)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        var escaped = false;

        for (var i = 0; i < dn.Length; i++)
        {
            var c = dn[i];

            if (escaped)
            {
                current.Append(c);
                escaped = false;
                continue;
            }

            if (c == '\\')
            {
                current.Append(c);
                escaped = true;
                continue;
            }

            if (c == ',')
            {
                var part = current.ToString().Trim();
                if (part.Length > 0)
                    parts.Add(part);
                current.Clear();
                continue;
            }

            current.Append(c);
        }

        var last = current.ToString().Trim();
        if (last.Length > 0)
            parts.Add(last);

        return parts;
    }

    private static string UnescapeValue(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        return EscapedHexRegex().Replace(value, m =>
        {
            var hex = m.Groups[1].Value;
            var b = Convert.ToByte(hex, 16);
            return ((char)b).ToString();
        }).Replace("\\,", ",")
          .Replace("\\+", "+")
          .Replace("\\\"", "\"")
          .Replace("\\\\", "\\")
          .Replace("\\<", "<")
          .Replace("\\>", ">")
          .Replace("\\;", ";");
    }

    [GeneratedRegex(@"\\([0-9a-fA-F]{2})")]
    private static partial Regex EscapedHexRegex();
}

/// <summary>
/// A single RDN component (e.g., CN=User1).
/// </summary>
public class RelativeDistinguishedName : IEquatable<RelativeDistinguishedName>
{
    public string Type { get; }
    public string Value { get; }

    public RelativeDistinguishedName(string type, string value)
    {
        Type = type;
        Value = value;
    }

    public override string ToString()
    {
        var escaped = Value
            .Replace("\\", "\\\\")
            .Replace(",", "\\,")
            .Replace("+", "\\+")
            .Replace("\"", "\\\"")
            .Replace("<", "\\<")
            .Replace(">", "\\>")
            .Replace(";", "\\;");

        return $"{Type}={escaped}";
    }

    public bool Equals(RelativeDistinguishedName other)
    {
        if (other is null) return false;
        return Type.Equals(other.Type, StringComparison.OrdinalIgnoreCase)
            && Value.Equals(other.Value, StringComparison.OrdinalIgnoreCase);
    }

    public override bool Equals(object obj) => Equals(obj as RelativeDistinguishedName);

    public override int GetHashCode()
        => HashCode.Combine(
            Type.ToUpperInvariant(),
            Value.ToUpperInvariant());
}
