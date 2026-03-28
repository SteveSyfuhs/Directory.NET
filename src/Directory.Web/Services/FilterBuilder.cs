using Directory.Core.Interfaces;

namespace Directory.Web.Services;

/// <summary>
/// Converts simple string filter expressions to FilterNode AST trees.
/// Supports: "cn=John" (equality), "cn=John*" (substring), "objectClass=*" (presence),
/// combined with "&amp;" for AND.
/// </summary>
public static class FilterBuilder
{
    public static FilterNode Parse(string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return null;

        filter = filter.Trim();

        // Handle LDAP-style parenthesized filters: (objectClass=group), (&(a=b)(c=d)), etc.
        if (filter.StartsWith('(') && filter.EndsWith(')'))
        {
            // Check if this is a compound filter like (&(a=b)(c=d)) or (|(a=b)(c=d))
            if (filter.Length > 2 && filter[1] is '&' or '|' or '!')
            {
                return ParseLdapFilter(filter);
            }

            // Simple parenthesized filter like (objectClass=group) — strip parens
            filter = filter[1..^1];
        }

        // Split on '&' for AND combinations
        var parts = filter.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length == 0)
            return null;

        var nodes = new List<FilterNode>();

        foreach (var part in parts)
        {
            var node = ParseSingle(part);
            if (node != null)
                nodes.Add(node);
        }

        if (nodes.Count == 0)
            return null;

        if (nodes.Count == 1)
            return nodes[0];

        return new AndFilterNode(nodes);
    }

    private static FilterNode ParseSingle(string expression)
    {
        // Handle negation prefix "!"
        if (expression.StartsWith('!'))
        {
            var inner = ParseSingle(expression[1..]);
            return inner != null ? new NotFilterNode(inner) : null;
        }

        var eqIndex = expression.IndexOf('=');
        if (eqIndex < 0)
            return null;

        var attribute = expression[..eqIndex].Trim();
        var value = expression[(eqIndex + 1)..].Trim();

        if (string.IsNullOrEmpty(attribute))
            return null;

        // Presence filter: "attribute=*"
        if (value == "*")
            return new PresenceFilterNode(attribute);

        // Substring filter: contains '*'
        if (value.Contains('*'))
        {
            return ParseSubstring(attribute, value);
        }

        // Greater-or-equal: "attribute>=value"
        if (attribute.EndsWith('>'))
        {
            return new GreaterOrEqualFilterNode(attribute[..^1].Trim(), value);
        }

        // Less-or-equal: "attribute<=value"
        if (attribute.EndsWith('<'))
        {
            return new LessOrEqualFilterNode(attribute[..^1].Trim(), value);
        }

        // Simple equality
        return new EqualityFilterNode(attribute, value);
    }

    private static SubstringFilterNode ParseSubstring(string attribute, string value)
    {
        var parts = value.Split('*');

        string initial = null;
        string final = null;
        var any = new List<string>();

        for (int i = 0; i < parts.Length; i++)
        {
            if (string.IsNullOrEmpty(parts[i]))
                continue;

            if (i == 0)
                initial = parts[i];
            else if (i == parts.Length - 1)
                final = parts[i];
            else
                any.Add(parts[i]);
        }

        return new SubstringFilterNode(attribute, initial, any, final);
    }

    /// <summary>
    /// Parse RFC 4515 LDAP filter syntax: (&amp;(a=b)(c=d)), (|(a=b)(c=d)), (!(a=b)), (a=b)
    /// </summary>
    private static FilterNode ParseLdapFilter(string filter)
    {
        filter = filter.Trim();

        if (!filter.StartsWith('(') || !filter.EndsWith(')'))
            return ParseSingle(filter);

        // Strip outer parens
        var inner = filter[1..^1];

        if (inner.Length == 0)
            return null;

        // Compound operators
        if (inner[0] == '&')
        {
            var children = SplitLdapFilterChildren(inner[1..]);
            var nodes = children.Select(ParseLdapFilter).Where(n => n != null).Cast<FilterNode>().ToList();
            return nodes.Count == 1 ? nodes[0] : new AndFilterNode(nodes);
        }

        if (inner[0] == '|')
        {
            var children = SplitLdapFilterChildren(inner[1..]);
            var nodes = children.Select(ParseLdapFilter).Where(n => n != null).Cast<FilterNode>().ToList();
            return nodes.Count == 1 ? nodes[0] : new OrFilterNode(nodes);
        }

        if (inner[0] == '!')
        {
            var child = ParseLdapFilter(inner[1..]);
            return child != null ? new NotFilterNode(child) : null;
        }

        // Simple filter like objectClass=group
        return ParseSingle(inner);
    }

    /// <summary>
    /// Split a string of consecutive LDAP filter components like "(a=b)(c=d)" into individual filters.
    /// </summary>
    private static List<string> SplitLdapFilterChildren(string s)
    {
        var result = new List<string>();
        int depth = 0;
        int start = -1;

        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '(')
            {
                if (depth == 0) start = i;
                depth++;
            }
            else if (s[i] == ')')
            {
                depth--;
                if (depth == 0 && start >= 0)
                {
                    result.Add(s[start..(i + 1)]);
                    start = -1;
                }
            }
        }

        return result;
    }
}
