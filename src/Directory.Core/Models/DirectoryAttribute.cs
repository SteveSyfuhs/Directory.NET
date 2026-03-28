namespace Directory.Core.Models;

/// <summary>
/// Represents a multi-valued directory attribute.
/// </summary>
public class DirectoryAttribute
{
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Attribute values. Can be strings, byte arrays, integers, etc.
    /// </summary>
    public List<object> Values { get; set; } = [];

    /// <summary>
    /// OID of the attribute syntax (e.g., "2.5.5.12" for Unicode String).
    /// </summary>
    public string Syntax { get; set; }

    public DirectoryAttribute() { }

    public DirectoryAttribute(string name, params object[] values)
    {
        Name = name;
        Values = [.. values];
    }

    public string GetFirstString() => Values.Count > 0 ? Values[0]?.ToString() : null;

    public IEnumerable<string> GetStrings() => Values.Select(v => v?.ToString() ?? string.Empty);

    public byte[] GetFirstBytes() => Values.Count > 0 ? Values[0] as byte[] : null;
}
