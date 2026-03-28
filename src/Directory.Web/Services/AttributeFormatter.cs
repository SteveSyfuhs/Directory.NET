using Directory.Core.Interfaces;
using Directory.Core.Models;

namespace Directory.Web.Services;

/// <summary>
/// Formats directory attribute values based on LDAP syntax OID for display.
/// Maps each syntax to appropriate display types and human-readable representations.
/// </summary>
public class AttributeFormatter
{
    private readonly ISchemaService _schema;
    private readonly IConstructedAttributeService _constructed;

    /// <summary>
    /// Syntax OID → human-readable syntax name.
    /// </summary>
    private static readonly Dictionary<string, string> SyntaxNames = new()
    {
        ["2.5.5.1"] = "Distinguished Name",
        ["2.5.5.2"] = "OID",
        ["2.5.5.3"] = "Case-Insensitive String",
        ["2.5.5.4"] = "Printable String",
        ["2.5.5.5"] = "IA5 String",
        ["2.5.5.6"] = "Numeric String",
        ["2.5.5.7"] = "DN+Binary",
        ["2.5.5.8"] = "Boolean",
        ["2.5.5.9"] = "Integer",
        ["2.5.5.10"] = "Octet String",
        ["2.5.5.11"] = "Generalized Time",
        ["2.5.5.12"] = "Unicode String",
        ["2.5.5.13"] = "Presentation Address",
        ["2.5.5.14"] = "DN+Unicode",
        ["2.5.5.15"] = "NT Security Descriptor",
        ["2.5.5.16"] = "Large Integer",
        ["2.5.5.17"] = "SID",
    };

    /// <summary>
    /// Attributes whose Large Integer (2.5.5.16) values are Windows FILETIME timestamps.
    /// </summary>
    private static readonly HashSet<string> FileTimeAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "lastLogon", "lastLogonTimestamp", "pwdLastSet", "accountExpires",
        "badPasswordTime", "lockoutTime", "lastLogoff", "msDS-UserPasswordExpiryTimeComputed",
    };

    /// <summary>
    /// Attributes whose Large Integer (2.5.5.16) values are time intervals (negative 100ns ticks).
    /// </summary>
    private static readonly HashSet<string> IntervalAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "maxPwdAge", "minPwdAge", "lockoutDuration", "lockoutObservationWindow",
        "forceLogoff", "msDS-MaximumPasswordAge", "msDS-MinimumPasswordAge",
        "msDS-LockoutDuration",
    };

    /// <summary>
    /// Sensitive attributes that should never be exposed via the API.
    /// </summary>
    private static readonly HashSet<string> SensitiveAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "ntHash", "kerberosKeys", "unicodePwd", "dbcsPwd",
        "supplementalCredentials", "passwordHistory",
    };

    /// <summary>
    /// Windows epoch offset: ticks between 1601-01-01 and 0001-01-01 (DateTime.MinValue).
    /// </summary>
    private static readonly long WindowsEpochTicks = new DateTime(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;

    public AttributeFormatter(ISchemaService schema, IConstructedAttributeService constructed)
    {
        _schema = schema;
        _constructed = constructed;
    }

    /// <summary>
    /// Format an attribute value for display, including syntax-aware formatting.
    /// </summary>
    public FormattedAttribute FormatAttribute(string attributeName, DirectoryAttribute attr)
    {
        var schemaDef = _schema.GetAttribute(attributeName);
        var syntaxOid = schemaDef?.Syntax ?? attr.Syntax ?? "2.5.5.12";
        var syntaxName = SyntaxNames.GetValueOrDefault(syntaxOid, "Unknown");
        var displayType = GetDisplayType(syntaxOid, attributeName);

        var values = new List<FormattedValue>();
        foreach (var val in attr.Values)
        {
            var formatted = FormatValue(syntaxOid, attributeName, val);
            values.Add(formatted);
        }

        return new FormattedAttribute
        {
            Name = attributeName,
            SyntaxOid = syntaxOid,
            SyntaxName = syntaxName,
            DisplayType = displayType,
            Values = values,
            IsWritable = schemaDef != null && !schemaDef.IsSystemOnly && !_constructed.IsConstructedAttribute(attributeName),
            IsMultiValued = schemaDef != null && !schemaDef.IsSingleValued,
            IsConstructed = _constructed.IsConstructedAttribute(attributeName),
            IsSystemOnly = schemaDef?.IsSystemOnly ?? false,
        };
    }

    /// <summary>
    /// Determine the display type for a given syntax OID and attribute name.
    /// </summary>
    private static string GetDisplayType(string syntaxOid, string attributeName)
    {
        return syntaxOid switch
        {
            "2.5.5.1" => "dn",
            "2.5.5.7" => "dn",
            "2.5.5.14" => "dn",
            "2.5.5.8" => "bool",
            "2.5.5.9" => FlagDefinitions.KnownFlags.ContainsKey(attributeName) ? "flags" : "int",
            "2.5.5.10" => GetOctetStringDisplayType(attributeName),
            "2.5.5.11" => "datetime",
            "2.5.5.15" => "security_descriptor",
            "2.5.5.16" => GetLargeIntDisplayType(attributeName),
            "2.5.5.17" => "sid",
            _ => "string",
        };
    }

    private static string GetOctetStringDisplayType(string attributeName)
    {
        var lower = attributeName.ToLowerInvariant();
        return lower switch
        {
            "objectguid" => "guid",
            "objectsid" => "sid",
            "tokengroupssids" => "sid",
            "ntsecuritydescriptor" => "security_descriptor",
            "msds-allowedtoactonbehalfofotheridentity" => "security_descriptor",
            _ => "hex",
        };
    }

    private static string GetLargeIntDisplayType(string attributeName)
    {
        if (FileTimeAttributes.Contains(attributeName))
            return "datetime";
        if (IntervalAttributes.Contains(attributeName))
            return "interval";
        return "int";
    }

    /// <summary>
    /// Format a single value based on syntax OID and attribute name.
    /// </summary>
    private static FormattedValue FormatValue(string syntaxOid, string attributeName, object value)
    {
        return syntaxOid switch
        {
            "2.5.5.1" => FormatDn(value),
            "2.5.5.7" => FormatDnBinary(value),
            "2.5.5.8" => FormatBoolean(value),
            "2.5.5.9" => FormatInteger(attributeName, value),
            "2.5.5.10" => FormatOctetString(attributeName, value),
            "2.5.5.11" => FormatGeneralizedTime(value),
            "2.5.5.14" => FormatDn(value),
            "2.5.5.15" => FormatSecurityDescriptor(value),
            "2.5.5.16" => FormatLargeInteger(attributeName, value),
            "2.5.5.17" => FormatSid(value),
            _ => FormatString(value),
        };
    }

    private static FormattedValue FormatString(object value)
    {
        var str = value?.ToString() ?? "";
        return new FormattedValue { RawValue = value, DisplayValue = str };
    }

    private static FormattedValue FormatDn(object value)
    {
        var str = value?.ToString() ?? "";
        return new FormattedValue { RawValue = value, DisplayValue = str };
    }

    private static FormattedValue FormatDnBinary(object value)
    {
        var str = value?.ToString() ?? "";
        // DN+Binary format: B:<charCount>:<hexValue>:<dn>
        if (str.StartsWith("B:", StringComparison.OrdinalIgnoreCase) && str.Contains(':'))
        {
            var parts = str.Split(':', 4);
            if (parts.Length >= 4)
            {
                return new FormattedValue
                {
                    RawValue = value,
                    DisplayValue = $"{parts[3]} (binary: {parts[2]})",
                };
            }
        }
        return new FormattedValue { RawValue = value, DisplayValue = str };
    }

    private static FormattedValue FormatBoolean(object value)
    {
        var str = value?.ToString() ?? "";
        bool boolVal = str.Equals("TRUE", StringComparison.OrdinalIgnoreCase)
                       || str.Equals("True", StringComparison.Ordinal)
                       || str == "1";
        return new FormattedValue
        {
            RawValue = value,
            DisplayValue = boolVal ? "TRUE" : "FALSE",
        };
    }

    private static FormattedValue FormatInteger(string attributeName, object value)
    {
        if (!TryParseInt(value, out int intVal))
            return new FormattedValue { RawValue = value, DisplayValue = value?.ToString() ?? "" };

        if (FlagDefinitions.KnownFlags.ContainsKey(attributeName))
        {
            var flags = FlagDefinitions.DecomposeFlags(attributeName, intVal);
            var flagStr = flags.Count > 0 ? string.Join(" | ", flags) : "0";
            return new FormattedValue
            {
                RawValue = value,
                DisplayValue = $"{intVal} ({flagStr})",
            };
        }

        return new FormattedValue { RawValue = value, DisplayValue = intVal.ToString() };
    }

    private static FormattedValue FormatOctetString(string attributeName, object value)
    {
        var lower = attributeName.ToLowerInvariant();

        switch (lower)
        {
            case "objectguid":
                return FormatGuid(value);
            case "objectsid":
            case "tokengroupssids":
                return FormatSid(value);
            case "ntsecuritydescriptor":
            case "msds-allowedtoactonbehalfofotheridentity":
                return FormatSecurityDescriptor(value);
            default:
                return FormatHex(value);
        }
    }

    private static FormattedValue FormatGuid(object value)
    {
        if (value is byte[] bytes && bytes.Length == 16)
        {
            var guid = new Guid(bytes);
            return new FormattedValue
            {
                RawValue = value,
                DisplayValue = $"{{{guid}}}",
            };
        }

        if (value is string s)
        {
            if (Guid.TryParse(s, out var parsed))
            {
                return new FormattedValue
                {
                    RawValue = value,
                    DisplayValue = $"{{{parsed}}}",
                };
            }
        }

        return new FormattedValue { RawValue = value, DisplayValue = value?.ToString() ?? "" };
    }

    private static FormattedValue FormatSid(object value)
    {
        var sidString = SidFormatter.Format(value);
        var resolved = SidFormatter.TryResolveWellKnown(sidString);

        return new FormattedValue
        {
            RawValue = value,
            DisplayValue = sidString,
            ResolvedName = resolved,
        };
    }

    private static FormattedValue FormatSecurityDescriptor(object value)
    {
        if (value is byte[] bytes)
        {
            return new FormattedValue
            {
                RawValue = value,
                DisplayValue = $"<Security Descriptor ({bytes.Length} bytes)>",
            };
        }

        return new FormattedValue
        {
            RawValue = value,
            DisplayValue = "<Security Descriptor>",
        };
    }

    private static FormattedValue FormatHex(object value)
    {
        if (value is byte[] bytes)
        {
            var hex = Convert.ToHexString(bytes);
            var display = hex.Length > 64 ? $"{hex[..64]}... ({bytes.Length} bytes)" : hex;
            return new FormattedValue { RawValue = value, DisplayValue = display };
        }

        return new FormattedValue { RawValue = value, DisplayValue = value?.ToString() ?? "" };
    }

    private static FormattedValue FormatGeneralizedTime(object value)
    {
        var str = value?.ToString() ?? "";

        // Generalized Time: YYYYMMDDHHmmss.0Z
        if (str.Length >= 14)
        {
            try
            {
                var year = int.Parse(str[..4]);
                var month = int.Parse(str[4..6]);
                var day = int.Parse(str[6..8]);
                var hour = int.Parse(str[8..10]);
                var minute = int.Parse(str[10..12]);
                var second = int.Parse(str[12..14]);

                var dt = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc);
                return new FormattedValue
                {
                    RawValue = value,
                    DisplayValue = dt.ToString("yyyy-MM-dd HH:mm:ss UTC"),
                };
            }
            catch
            {
                // Fall through
            }
        }

        return new FormattedValue { RawValue = value, DisplayValue = str };
    }

    private static FormattedValue FormatLargeInteger(string attributeName, object value)
    {
        if (!TryParseLong(value, out long longVal))
            return new FormattedValue { RawValue = value, DisplayValue = value?.ToString() ?? "" };

        if (FileTimeAttributes.Contains(attributeName))
            return FormatFileTime(longVal, value);

        if (IntervalAttributes.Contains(attributeName))
            return FormatInterval(longVal, value);

        return new FormattedValue { RawValue = value, DisplayValue = longVal.ToString() };
    }

    private static FormattedValue FormatFileTime(long fileTime, object rawValue)
    {
        // Special values
        if (fileTime == 0)
            return new FormattedValue { RawValue = rawValue, DisplayValue = "Never (0)" };

        if (fileTime == long.MaxValue || fileTime == unchecked((long)0x7FFFFFFFFFFFFFFF))
            return new FormattedValue { RawValue = rawValue, DisplayValue = "Never" };

        try
        {
            // Windows FILETIME: 100ns intervals since 1601-01-01
            var dateTime = DateTime.FromFileTimeUtc(fileTime);
            return new FormattedValue
            {
                RawValue = rawValue,
                DisplayValue = dateTime.ToString("yyyy-MM-dd HH:mm:ss UTC"),
            };
        }
        catch
        {
            return new FormattedValue { RawValue = rawValue, DisplayValue = fileTime.ToString() };
        }
    }

    private static FormattedValue FormatInterval(long interval, object rawValue)
    {
        // Intervals are stored as negative 100ns ticks
        // Special values
        if (interval == 0)
            return new FormattedValue { RawValue = rawValue, DisplayValue = "None" };

        if (interval == long.MinValue || interval == unchecked((long)0x8000000000000000))
            return new FormattedValue { RawValue = rawValue, DisplayValue = "Never" };

        try
        {
            var ticks = Math.Abs(interval);
            var timeSpan = TimeSpan.FromTicks(ticks);

            var parts = new List<string>();
            if (timeSpan.Days > 0)
                parts.Add($"{timeSpan.Days} day{(timeSpan.Days != 1 ? "s" : "")}");
            if (timeSpan.Hours > 0)
                parts.Add($"{timeSpan.Hours} hour{(timeSpan.Hours != 1 ? "s" : "")}");
            if (timeSpan.Minutes > 0)
                parts.Add($"{timeSpan.Minutes} minute{(timeSpan.Minutes != 1 ? "s" : "")}");

            var display = parts.Count > 0 ? string.Join(", ", parts) : "0 minutes";
            return new FormattedValue { RawValue = rawValue, DisplayValue = display };
        }
        catch
        {
            return new FormattedValue { RawValue = rawValue, DisplayValue = interval.ToString() };
        }
    }

    private static bool TryParseInt(object value, out int result)
    {
        if (value is int i) { result = i; return true; }
        if (value is long l) { result = (int)l; return true; }
        return int.TryParse(value?.ToString(), out result);
    }

    private static bool TryParseLong(object value, out long result)
    {
        if (value is long l) { result = l; return true; }
        if (value is int i) { result = i; return true; }
        return long.TryParse(value?.ToString(), out result);
    }

    /// <summary>
    /// Format an unset schema attribute (no value) for display in "show all" mode.
    /// </summary>
    public FormattedAttribute FormatSchemaAttribute(string attributeName, AttributeSchemaEntry schemaDef, bool isMustContain)
    {
        var syntaxOid = schemaDef?.Syntax ?? "2.5.5.12";
        var syntaxName = SyntaxNames.GetValueOrDefault(syntaxOid, "Unknown");
        var displayType = GetDisplayType(syntaxOid, attributeName);

        return new FormattedAttribute
        {
            Name = attributeName,
            SyntaxOid = syntaxOid,
            SyntaxName = syntaxName,
            DisplayType = displayType,
            Values = [],
            IsWritable = schemaDef != null && !schemaDef.IsSystemOnly && !_constructed.IsConstructedAttribute(attributeName),
            IsMultiValued = schemaDef != null && !schemaDef.IsSingleValued,
            IsConstructed = _constructed.IsConstructedAttribute(attributeName),
            IsSystemOnly = schemaDef?.IsSystemOnly ?? false,
            IsValueSet = false,
            IsMustContain = isMustContain,
            RangeLower = schemaDef?.RangeLower,
            RangeUpper = schemaDef?.RangeUpper,
            Description = schemaDef?.Description,
            IsIndexed = schemaDef?.IsIndexed ?? false,
            IsInGlobalCatalog = schemaDef?.IsInGlobalCatalog ?? false,
        };
    }

    /// <summary>
    /// Check if an attribute name is sensitive and should not be exposed.
    /// </summary>
    public static bool IsSensitiveAttribute(string attributeName) =>
        SensitiveAttributes.Contains(attributeName);
}

/// <summary>
/// Represents a fully formatted attribute with metadata and display-ready values.
/// </summary>
public class FormattedAttribute
{
    public string Name { get; set; } = string.Empty;
    public string SyntaxOid { get; set; } = string.Empty;
    public string SyntaxName { get; set; } = string.Empty;

    /// <summary>
    /// Display type hint for the UI: "string", "dn", "sid", "datetime", "guid", "hex", "bool",
    /// "int", "flags", "interval", "security_descriptor".
    /// </summary>
    public string DisplayType { get; set; } = "string";

    public List<FormattedValue> Values { get; set; } = [];
    public bool IsWritable { get; set; }
    public bool IsMultiValued { get; set; }

    /// <summary>
    /// True if this is a constructed (computed) attribute that is not stored.
    /// </summary>
    public bool IsConstructed { get; set; }

    public bool IsSystemOnly { get; set; }

    /// <summary>
    /// True if the attribute currently has a value set on this object.
    /// False if showing an unset attribute from the schema.
    /// </summary>
    public bool IsValueSet { get; set; } = true;

    /// <summary>
    /// True if this attribute is required (mustContain) for the object's class.
    /// </summary>
    public bool IsMustContain { get; set; }

    /// <summary>
    /// Schema range constraints for validation.
    /// </summary>
    public int? RangeLower { get; set; }
    public int? RangeUpper { get; set; }

    /// <summary>
    /// Schema description of the attribute.
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// Whether the attribute is indexed in the directory.
    /// </summary>
    public bool IsIndexed { get; set; }

    /// <summary>
    /// Whether the attribute is replicated to the Global Catalog.
    /// </summary>
    public bool IsInGlobalCatalog { get; set; }
}

/// <summary>
/// Represents a single formatted attribute value with raw, display, and resolved representations.
/// </summary>
public class FormattedValue
{
    public object RawValue { get; set; }

    /// <summary>
    /// Human-readable display string.
    /// </summary>
    public string DisplayValue { get; set; } = string.Empty;

    /// <summary>
    /// Resolved name for SIDs, GUIDs, or DNs (e.g., "BUILTIN\Administrators").
    /// Null if not applicable or resolution failed.
    /// </summary>
    public string ResolvedName { get; set; }
}
