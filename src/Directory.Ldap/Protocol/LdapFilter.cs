using System.Formats.Asn1;
using System.Text;
using Directory.Core.Interfaces;

namespace Directory.Ldap.Protocol;

/// <summary>
/// Parses LDAP search filters from both BER encoding (in SearchRequest) and
/// RFC 4515 text format. Produces a FilterNode AST.
///
/// BER filter CONTEXT-SPECIFIC tags:
///   AND=[0], OR=[1], NOT=[2], equalityMatch=[3], substrings=[4],
///   greaterOrEqual=[5], lessOrEqual=[6], present=[7], approxMatch=[8], extensibleMatch=[9]
/// </summary>
public static class LdapFilterParser
{
    // Context-specific tag values for filter components
    private static readonly Asn1Tag TagAnd = BerHelper.ContextTag(0, isConstructed: true);
    private static readonly Asn1Tag TagOr = BerHelper.ContextTag(1, isConstructed: true);
    private static readonly Asn1Tag TagNot = BerHelper.ContextTag(2, isConstructed: true);  // EXPLICIT for CHOICE
    private static readonly Asn1Tag TagEquality = BerHelper.ContextTag(3, isConstructed: true);
    private static readonly Asn1Tag TagSubstrings = BerHelper.ContextTag(4, isConstructed: true);
    private static readonly Asn1Tag TagGreaterOrEqual = BerHelper.ContextTag(5, isConstructed: true);
    private static readonly Asn1Tag TagLessOrEqual = BerHelper.ContextTag(6, isConstructed: true);
    private static readonly Asn1Tag TagPresent = BerHelper.ContextTag(7, isConstructed: false);
    private static readonly Asn1Tag TagApproxMatch = BerHelper.ContextTag(8, isConstructed: true);
    private static readonly Asn1Tag TagExtensibleMatch = BerHelper.ContextTag(9, isConstructed: true);

    // Substring component tags
    private static readonly Asn1Tag TagSubInitial = BerHelper.ContextTag(0);
    private static readonly Asn1Tag TagSubAny = BerHelper.ContextTag(1);
    private static readonly Asn1Tag TagSubFinal = BerHelper.ContextTag(2);

    #region BER Decoding

    /// <summary>
    /// Decode a BER-encoded LDAP filter from an AsnReader.
    /// </summary>
    public static FilterNode DecodeBer(AsnReader reader)
    {
        var tag = reader.PeekTag();

        if (tag == TagAnd)
            return DecodeAnd(reader);
        if (tag == TagOr)
            return DecodeOr(reader);
        if (tag == TagNot)
            return DecodeNot(reader);
        if (tag == TagEquality)
            return DecodeAttributeValueAssertion(reader, TagEquality, (a, v) => new EqualityFilterNode(a, v));
        if (tag == TagSubstrings)
            return DecodeSubstrings(reader);
        if (tag == TagGreaterOrEqual)
            return DecodeAttributeValueAssertion(reader, TagGreaterOrEqual, (a, v) => new GreaterOrEqualFilterNode(a, v));
        if (tag == TagLessOrEqual)
            return DecodeAttributeValueAssertion(reader, TagLessOrEqual, (a, v) => new LessOrEqualFilterNode(a, v));
        if (tag == TagPresent)
            return DecodePresent(reader);
        if (tag == TagApproxMatch)
            return DecodeAttributeValueAssertion(reader, TagApproxMatch, (a, v) => new ApproxMatchFilterNode(a, v));
        if (tag == TagExtensibleMatch)
            return DecodeExtensibleMatch(reader);

        throw new InvalidOperationException($"Unknown filter tag: {tag}");
    }

    private static FilterNode DecodeAnd(AsnReader reader)
    {
        var set = reader.ReadSetOf(TagAnd);
        var children = new List<FilterNode>();
        while (set.HasData)
            children.Add(DecodeBer(set));
        return new AndFilterNode(children);
    }

    private static FilterNode DecodeOr(AsnReader reader)
    {
        var set = reader.ReadSetOf(TagOr);
        var children = new List<FilterNode>();
        while (set.HasData)
            children.Add(DecodeBer(set));
        return new OrFilterNode(children);
    }

    private static FilterNode DecodeNot(AsnReader reader)
    {
        // NOT is EXPLICIT tag wrapping a CHOICE — read the constructed wrapper
        var inner = reader.ReadSequence(TagNot);
        var child = DecodeBer(inner);
        return new NotFilterNode(child);
    }

    private static FilterNode DecodeAttributeValueAssertion<T>(
        AsnReader reader, Asn1Tag tag, Func<string, string, T> factory) where T : FilterNode
    {
        var sequence = reader.ReadSequence(tag);
        var attribute = BerHelper.ReadLdapString(sequence);
        var value = BerHelper.ReadLdapString(sequence);
        return factory(attribute, value);
    }

    private static FilterNode DecodeSubstrings(AsnReader reader)
    {
        var sequence = reader.ReadSequence(TagSubstrings);
        var attribute = BerHelper.ReadLdapString(sequence);

        string initial = null;
        var any = new List<string>();
        string final = null;

        var substrs = sequence.ReadSequence();
        while (substrs.HasData)
        {
            var subTag = substrs.PeekTag();
            if (subTag == TagSubInitial)
                initial = BerHelper.ReadLdapString(substrs, TagSubInitial);
            else if (subTag == TagSubAny)
                any.Add(BerHelper.ReadLdapString(substrs, TagSubAny));
            else if (subTag == TagSubFinal)
                final = BerHelper.ReadLdapString(substrs, TagSubFinal);
            else
                substrs.ReadEncodedValue(); // skip unknown
        }

        return new SubstringFilterNode(attribute, initial, any, final);
    }

    private static FilterNode DecodePresent(AsnReader reader)
    {
        var bytes = reader.ReadOctetString(TagPresent);
        var attribute = bytes.ToUtf8String();
        return new PresenceFilterNode(attribute);
    }

    private static FilterNode DecodeExtensibleMatch(AsnReader reader)
    {
        var sequence = reader.ReadSequence(TagExtensibleMatch);
        string matchingRule = null;
        string attribute = null;
        string value = string.Empty;
        bool dnAttributes = false;

        while (sequence.HasData)
        {
            var tag = sequence.PeekTag();
            if (tag == BerHelper.ContextTag(1))
                matchingRule = BerHelper.ReadLdapString(sequence, BerHelper.ContextTag(1));
            else if (tag == BerHelper.ContextTag(2))
                attribute = BerHelper.ReadLdapString(sequence, BerHelper.ContextTag(2));
            else if (tag == BerHelper.ContextTag(3))
                value = BerHelper.ReadLdapString(sequence, BerHelper.ContextTag(3));
            else if (tag == BerHelper.ContextTag(4))
            {
                dnAttributes = sequence.ReadBoolean(BerHelper.ContextTag(4));
            }
            else
                sequence.ReadEncodedValue();
        }

        return new ExtensibleMatchFilterNode(matchingRule, attribute, value, dnAttributes);
    }

    #endregion

    #region RFC 4515 Text Format Parsing

    /// <summary>
    /// Parse an RFC 4515 LDAP filter string (e.g., "(&amp;(objectClass=user)(cn=John*))").
    /// </summary>
    public static FilterNode ParseText(string filterText)
    {
        if (string.IsNullOrWhiteSpace(filterText))
            return new PresenceFilterNode("objectClass"); // Default: match all

        var pos = 0;
        var result = ParseFilterText(filterText, ref pos);
        return result;
    }

    private static FilterNode ParseFilterText(string text, ref int pos)
    {
        SkipWhitespace(text, ref pos);

        if (pos >= text.Length)
            throw new FormatException("Unexpected end of filter");

        if (text[pos] != '(')
            throw new FormatException($"Expected '(' at position {pos}");

        pos++; // skip '('

        FilterNode node;

        if (pos >= text.Length)
            throw new FormatException("Unexpected end of filter");

        switch (text[pos])
        {
            case '&':
                pos++;
                node = ParseFilterList(text, ref pos, children => new AndFilterNode(children));
                break;
            case '|':
                pos++;
                node = ParseFilterList(text, ref pos, children => new OrFilterNode(children));
                break;
            case '!':
                pos++;
                var child = ParseFilterText(text, ref pos);
                node = new NotFilterNode(child);
                break;
            default:
                node = ParseSimpleFilter(text, ref pos);
                break;
        }

        SkipWhitespace(text, ref pos);

        if (pos >= text.Length || text[pos] != ')')
            throw new FormatException($"Expected ')' at position {pos}");

        pos++; // skip ')'
        return node;
    }

    private static FilterNode ParseFilterList(string text, ref int pos, Func<List<FilterNode>, FilterNode> factory)
    {
        var children = new List<FilterNode>();
        SkipWhitespace(text, ref pos);

        while (pos < text.Length && text[pos] == '(')
        {
            children.Add(ParseFilterText(text, ref pos));
            SkipWhitespace(text, ref pos);
        }

        return factory(children);
    }

    private static FilterNode ParseSimpleFilter(string text, ref int pos)
    {
        // Read attribute name
        var attrStart = pos;
        while (pos < text.Length && text[pos] != '=' && text[pos] != '>' && text[pos] != '<' && text[pos] != '~' && text[pos] != ')')
            pos++;

        if (pos >= text.Length)
            throw new FormatException("Unexpected end of filter in attribute name");

        var attribute = text[attrStart..pos].Trim();

        // Determine the filter type based on operator
        if (text[pos] == '>' && pos + 1 < text.Length && text[pos + 1] == '=')
        {
            pos += 2;
            var value = ReadFilterValue(text, ref pos);
            return new GreaterOrEqualFilterNode(attribute, value);
        }

        if (text[pos] == '<' && pos + 1 < text.Length && text[pos + 1] == '=')
        {
            pos += 2;
            var value = ReadFilterValue(text, ref pos);
            return new LessOrEqualFilterNode(attribute, value);
        }

        if (text[pos] == '~' && pos + 1 < text.Length && text[pos + 1] == '=')
        {
            pos += 2;
            var value = ReadFilterValue(text, ref pos);
            return new ApproxMatchFilterNode(attribute, value);
        }

        if (text[pos] == '=')
        {
            pos++;

            // Check for presence filter: attr=*
            if (pos < text.Length && text[pos] == '*' && (pos + 1 >= text.Length || text[pos + 1] == ')'))
            {
                pos++;
                return new PresenceFilterNode(attribute);
            }

            var value = ReadFilterValue(text, ref pos);

            // Check if it's a substring filter (contains *)
            if (value.Contains('*'))
                return ParseSubstringFilter(attribute, value);

            return new EqualityFilterNode(attribute, value);
        }

        throw new FormatException($"Unknown operator at position {pos}: '{text[pos]}'");
    }

    private static SubstringFilterNode ParseSubstringFilter(string attribute, string pattern)
    {
        var parts = pattern.Split('*');
        string initial = parts[0].Length > 0 ? UnescapeFilterValue(parts[0]) : null;
        string final = parts[^1].Length > 0 ? UnescapeFilterValue(parts[^1]) : null;
        var any = parts.Skip(1).Take(parts.Length - 2)
            .Where(p => p.Length > 0)
            .Select(UnescapeFilterValue)
            .ToList();

        return new SubstringFilterNode(attribute, initial, any, final);
    }

    private static string ReadFilterValue(string text, ref int pos)
    {
        var sb = new StringBuilder();
        while (pos < text.Length && text[pos] != ')')
        {
            if (text[pos] == '\\' && pos + 2 < text.Length)
            {
                // Escaped hex byte
                var hex = text.Substring(pos + 1, 2);
                if (byte.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var b))
                {
                    sb.Append((char)b);
                    pos += 3;
                    continue;
                }
            }
            sb.Append(text[pos]);
            pos++;
        }
        return sb.ToString();
    }

    private static string UnescapeFilterValue(string value)
    {
        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 2 < value.Length)
            {
                var hex = value.Substring(i + 1, 2);
                if (byte.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var b))
                {
                    sb.Append((char)b);
                    i += 2;
                    continue;
                }
            }
            sb.Append(value[i]);
        }
        return sb.ToString();
    }

    private static void SkipWhitespace(string text, ref int pos)
    {
        while (pos < text.Length && char.IsWhiteSpace(text[pos]))
            pos++;
    }

    #endregion
}
