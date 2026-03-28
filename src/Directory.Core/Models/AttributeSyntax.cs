using System.Security.Principal;
using System.Text.RegularExpressions;

namespace Directory.Core.Models;

public enum AttributeSyntaxType
{
    Boolean,
    Integer,
    LargeInteger,
    OctetString,
    UnicodeString,
    GeneralizedTime,
    DnString,
    Sid,
    SecurityDescriptor,
    NtSecurityDescriptor,
    Enumeration,
    ObjectIdentifier,
    CaseIgnoreString,
    DistinguishedName,
    PrintableString,
    NumericString,
    Int64,
    ReplicaLink,
    CaseSensitiveString,
    DnBinary
}

public static class AttributeSyntaxValidator
{
    private static readonly Regex DnPattern = new(
        @"^(?:(?:CN|OU|DC|O|C|L|ST)=[^,+""\\<>;]+,)*(?:CN|OU|DC|O|C|L|ST)=[^,+""\\<>;]+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex OidPattern = new(
        @"^\d+(\.\d+)+$",
        RegexOptions.Compiled);

    private static readonly Regex PrintablePattern = new(
        @"^[A-Za-z0-9 '()+,\-./:=?]+$",
        RegexOptions.Compiled);

    private static readonly Regex NumericPattern = new(
        @"^[0-9 ]+$",
        RegexOptions.Compiled);

    private static readonly Regex GeneralizedTimePattern = new(
        @"^\d{14}\.\d+Z$",
        RegexOptions.Compiled);

    private static readonly Regex DnBinaryPattern = new(
        @"^B:\d+:[0-9a-fA-F]+:.+$",
        RegexOptions.Compiled);

    private static readonly Dictionary<string, AttributeSyntaxType> OidToSyntax = new()
    {
        ["2.5.5.8"] = AttributeSyntaxType.Boolean,
        ["2.5.5.9"] = AttributeSyntaxType.Integer,
        ["2.5.5.16"] = AttributeSyntaxType.LargeInteger,
        ["2.5.5.10"] = AttributeSyntaxType.OctetString,
        ["2.5.5.12"] = AttributeSyntaxType.UnicodeString,
        ["2.5.5.11"] = AttributeSyntaxType.GeneralizedTime,
        ["2.5.5.1"] = AttributeSyntaxType.DistinguishedName,
        ["2.5.5.17"] = AttributeSyntaxType.Sid,
        ["2.5.5.15"] = AttributeSyntaxType.NtSecurityDescriptor,
        ["2.5.5.2"] = AttributeSyntaxType.ObjectIdentifier,
        ["2.5.5.5"] = AttributeSyntaxType.PrintableString,
        ["2.5.5.4"] = AttributeSyntaxType.CaseIgnoreString,
        ["2.5.5.14"] = AttributeSyntaxType.ReplicaLink,
        ["2.5.5.3"] = AttributeSyntaxType.CaseSensitiveString,
        ["2.5.5.6"] = AttributeSyntaxType.NumericString,
        ["2.5.5.7"] = AttributeSyntaxType.DnBinary
    };

    public static (bool IsValid, string Error) Validate(string syntaxOid, object value)
    {
        if (value is null)
            return (false, "Value cannot be null.");

        if (!OidToSyntax.TryGetValue(syntaxOid, out var syntaxType))
            return (false, $"Unknown syntax OID: {syntaxOid}");

        return syntaxType switch
        {
            AttributeSyntaxType.Boolean => ValidateBoolean(value),
            AttributeSyntaxType.Integer => ValidateInteger(value),
            AttributeSyntaxType.LargeInteger => ValidateLargeInteger(value),
            AttributeSyntaxType.OctetString => ValidateOctetString(value),
            AttributeSyntaxType.UnicodeString => ValidateString(value),
            AttributeSyntaxType.GeneralizedTime => ValidateGeneralizedTime(value),
            AttributeSyntaxType.DistinguishedName => ValidateDn(value),
            AttributeSyntaxType.Sid => ValidateSid(value),
            AttributeSyntaxType.NtSecurityDescriptor => ValidateOctetString(value),
            AttributeSyntaxType.ObjectIdentifier => ValidateOid(value),
            AttributeSyntaxType.PrintableString => ValidatePrintableString(value),
            AttributeSyntaxType.CaseIgnoreString => ValidateString(value),
            AttributeSyntaxType.ReplicaLink => ValidateOctetString(value),
            AttributeSyntaxType.CaseSensitiveString => ValidateString(value),
            AttributeSyntaxType.NumericString => ValidateNumericString(value),
            AttributeSyntaxType.DnBinary => ValidateDnBinary(value),
            _ => (false, $"Unsupported syntax type: {syntaxType}")
        };
    }

    private static (bool, string) ValidateBoolean(object value) => value switch
    {
        bool => (true, null),
        string s when bool.TryParse(s, out _) => (true, null),
        string s when s is "TRUE" or "FALSE" => (true, null),
        _ => (false, "Value must be a boolean (TRUE/FALSE).")
    };

    private static (bool, string) ValidateInteger(object value) => value switch
    {
        int => (true, null),
        long l when l is >= int.MinValue and <= int.MaxValue => (true, null),
        string s when int.TryParse(s, out _) => (true, null),
        _ => (false, "Value must be a 32-bit integer.")
    };

    private static (bool, string) ValidateLargeInteger(object value) => value switch
    {
        long => (true, null),
        int => (true, null),
        string s when long.TryParse(s, out _) => (true, null),
        _ => (false, "Value must be a 64-bit integer.")
    };

    private static (bool, string) ValidateOctetString(object value) => value switch
    {
        byte[] => (true, null),
        string s when s.Length % 2 == 0 && s.All(c => Uri.IsHexDigit(c)) => (true, null),
        _ => (false, "Value must be a byte array or hex string.")
    };

    private static (bool, string) ValidateString(object value) => value switch
    {
        string s when s.Length <= 1_048_576 => (true, null),
        string => (false, "String value exceeds maximum length of 1048576."),
        _ => (false, "Value must be a string.")
    };

    private static (bool, string) ValidateGeneralizedTime(object value) => value switch
    {
        DateTime => (true, null),
        DateTimeOffset => (true, null),
        string s when GeneralizedTimePattern.IsMatch(s) => (true, null),
        string s when DateTime.TryParse(s, out _) => (true, null),
        _ => (false, "Value must be a valid GeneralizedTime (YYYYMMDDHHMMSS.0Z).")
    };

    private static (bool, string) ValidateDn(object value) => value switch
    {
        string s when DnPattern.IsMatch(s) => (true, null),
        string { Length: 0 } => (true, null), // Root DN
        _ => (false, "Value must be a valid distinguished name.")
    };

    private static (bool, string) ValidateSid(object value) => value switch
    {
        byte[] bytes when bytes.Length >= 8 && bytes[0] == 1 => (true, null),
        SecurityIdentifier => (true, null),
        string s when s.StartsWith("S-1-", StringComparison.Ordinal) => (true, null),
        _ => (false, "Value must be a valid SID (byte[], SecurityIdentifier, or S-1-... string).")
    };

    private static (bool, string) ValidateOid(object value) => value switch
    {
        string s when OidPattern.IsMatch(s) => (true, null),
        _ => (false, "Value must be a valid OID (e.g., 1.2.840.113556.1.4.1).")
    };

    private static (bool, string) ValidatePrintableString(object value) => value switch
    {
        string s when PrintablePattern.IsMatch(s) => (true, null),
        string => (false, "Value contains characters not allowed in PrintableString/IA5String."),
        _ => (false, "Value must be a printable string.")
    };

    private static (bool, string) ValidateNumericString(object value) => value switch
    {
        string s when NumericPattern.IsMatch(s) => (true, null),
        int => (true, null),
        long => (true, null),
        _ => (false, "Value must contain only digits and spaces.")
    };

    private static (bool, string) ValidateDnBinary(object value) => value switch
    {
        string s when DnBinaryPattern.IsMatch(s) => (true, null),
        _ => (false, "Value must be in DN-Binary format (B:<count>:<hex>:<dn>).")
    };
}
