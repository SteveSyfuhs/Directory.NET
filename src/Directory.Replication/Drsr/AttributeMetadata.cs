using System.Collections.Concurrent;
using System.Text.Json;
using Directory.Core.Models;

namespace Directory.Replication.Drsr;

/// <summary>
/// Per-attribute replication metadata entry.
/// Tracks the version, originating DC, USN, and timestamp for each attribute
/// on a directory object, per [MS-DRSR] section 5.119.
/// </summary>
public class PropertyMetaDataEntry
{
    /// <summary>
    /// The attribute OID or ATTRTYP identifier this metadata applies to.
    /// </summary>
    public uint AttributeId { get; set; }

    /// <summary>
    /// Monotonically increasing version number. Incremented each time the attribute is written.
    /// </summary>
    public uint Version { get; set; }

    /// <summary>
    /// GUID of the DC where the change originated.
    /// </summary>
    public Guid OriginatingDsaGuid { get; set; }

    /// <summary>
    /// The USN on the originating DC when the change was made.
    /// </summary>
    public long OriginatingUsn { get; set; }

    /// <summary>
    /// UTC timestamp when the change was made on the originating DC.
    /// Stored as Windows FILETIME (100-nanosecond intervals since 1601-01-01).
    /// </summary>
    public long OriginatingTime { get; set; }

    /// <summary>
    /// Creates a PROPERTY_META_DATA_EXT wire structure from this entry.
    /// </summary>
    public PROPERTY_META_DATA_EXT ToWireFormat()
    {
        return new PROPERTY_META_DATA_EXT
        {
            DwVersion = Version,
            TimeChanged = OriginatingTime,
            UuidDsaOriginating = OriginatingDsaGuid,
            UsnOriginating = OriginatingUsn,
        };
    }

    /// <summary>
    /// Creates a metadata entry from a wire format structure.
    /// </summary>
    public static PropertyMetaDataEntry FromWireFormat(uint attributeId, PROPERTY_META_DATA_EXT wire)
    {
        return new PropertyMetaDataEntry
        {
            AttributeId = attributeId,
            Version = wire.DwVersion,
            OriginatingDsaGuid = wire.UuidDsaOriginating,
            OriginatingUsn = wire.UsnOriginating,
            OriginatingTime = wire.TimeChanged,
        };
    }
}

/// <summary>
/// Vector of per-attribute metadata for a single directory object.
/// Each attribute that has ever been written has an entry tracking its replication state.
/// </summary>
public class PropertyMetaDataVector
{
    private readonly ConcurrentDictionary<uint, PropertyMetaDataEntry> _entries = new();

    /// <summary>
    /// All metadata entries indexed by attribute ID.
    /// </summary>
    public IReadOnlyDictionary<uint, PropertyMetaDataEntry> Entries => _entries;

    /// <summary>
    /// Gets the metadata entry for a specific attribute.
    /// </summary>
    public PropertyMetaDataEntry GetEntry(uint attributeId)
    {
        _entries.TryGetValue(attributeId, out var entry);
        return entry;
    }

    /// <summary>
    /// Sets or updates the metadata entry for a specific attribute.
    /// </summary>
    public void SetEntry(uint attributeId, PropertyMetaDataEntry entry)
    {
        _entries[attributeId] = entry;
    }

    /// <summary>
    /// Stamps a new version for an attribute being modified locally.
    /// Increments the version and updates the originating DC/USN/time.
    /// </summary>
    public PropertyMetaDataEntry StampNewVersion(
        uint attributeId,
        Guid localDsaGuid,
        long localUsn)
    {
        var existing = GetEntry(attributeId);
        var newVersion = (existing?.Version ?? 0) + 1;

        var entry = new PropertyMetaDataEntry
        {
            AttributeId = attributeId,
            Version = newVersion,
            OriginatingDsaGuid = localDsaGuid,
            OriginatingUsn = localUsn,
            OriginatingTime = DateTimeOffset.UtcNow.ToFileTime(),
        };

        _entries[attributeId] = entry;
        return entry;
    }

    /// <summary>
    /// Applies a replicated metadata entry. Only updates if the incoming version
    /// is higher than the existing one (or if no existing entry).
    /// Returns true if the entry was applied.
    /// </summary>
    public bool ApplyReplicatedEntry(uint attributeId, PropertyMetaDataEntry incoming)
    {
        return _entries.AddOrUpdate(
            attributeId,
            _ => incoming,
            (_, existing) =>
            {
                // Last-writer-wins: compare version, then time, then GUID
                if (incoming.Version > existing.Version)
                    return incoming;
                if (incoming.Version == existing.Version)
                {
                    if (incoming.OriginatingTime > existing.OriginatingTime)
                        return incoming;
                    if (incoming.OriginatingTime == existing.OriginatingTime &&
                        incoming.OriginatingDsaGuid.CompareTo(existing.OriginatingDsaGuid) > 0)
                        return incoming;
                }
                return existing;
            }) == incoming;
    }

    /// <summary>
    /// Builds the wire format PROPERTY_META_DATA_EXT_VECTOR.
    /// </summary>
    public PROPERTY_META_DATA_EXT_VECTOR ToWireFormat()
    {
        var vector = new PROPERTY_META_DATA_EXT_VECTOR
        {
            CNumProps = (uint)_entries.Count,
            RgMetaData = [],
        };

        foreach (var entry in _entries.Values)
        {
            vector.RgMetaData.Add(entry.ToWireFormat());
        }

        return vector;
    }

    /// <summary>
    /// Creates a vector from wire format.
    /// </summary>
    public static PropertyMetaDataVector FromWireFormat(
        PROPERTY_META_DATA_EXT_VECTOR wire,
        IReadOnlyList<uint> attributeIds)
    {
        var vector = new PropertyMetaDataVector();

        for (int i = 0; i < wire.RgMetaData.Count && i < attributeIds.Count; i++)
        {
            var entry = PropertyMetaDataEntry.FromWireFormat(attributeIds[i], wire.RgMetaData[i]);
            vector._entries[attributeIds[i]] = entry;
        }

        return vector;
    }

    /// <summary>
    /// Serializes the metadata vector to JSON for storage in a DirectoryObject attribute.
    /// </summary>
    public string Serialize()
    {
        var list = _entries.Values.Select(e => new
        {
            e.AttributeId,
            e.Version,
            DsaGuid = e.OriginatingDsaGuid.ToString(),
            e.OriginatingUsn,
            e.OriginatingTime,
        }).ToList();

        return JsonSerializer.Serialize(list);
    }

    /// <summary>
    /// Deserializes a metadata vector from a JSON string stored in a DirectoryObject attribute.
    /// </summary>
    public static PropertyMetaDataVector Deserialize(string json)
    {
        var vector = new PropertyMetaDataVector();

        if (string.IsNullOrEmpty(json))
            return vector;

        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                var entry = new PropertyMetaDataEntry
                {
                    AttributeId = element.GetProperty("AttributeId").GetUInt32(),
                    Version = element.GetProperty("Version").GetUInt32(),
                    OriginatingDsaGuid = Guid.Parse(element.GetProperty("DsaGuid").GetString()),
                    OriginatingUsn = element.GetProperty("OriginatingUsn").GetInt64(),
                    OriginatingTime = element.GetProperty("OriginatingTime").GetInt64(),
                };

                vector._entries[entry.AttributeId] = entry;
            }
        }
        catch (JsonException)
        {
            // Return empty vector if deserialization fails
        }

        return vector;
    }

    /// <summary>
    /// Retrieves the metadata vector from a DirectoryObject's replPropertyMetaData attribute.
    /// </summary>
    public static PropertyMetaDataVector FromDirectoryObject(DirectoryObject obj)
    {
        var attr = obj.GetAttribute("replPropertyMetaData");
        if (attr == null)
            return new PropertyMetaDataVector();

        var json = attr.GetFirstString();
        if (string.IsNullOrEmpty(json))
            return new PropertyMetaDataVector();

        return Deserialize(json);
    }

    /// <summary>
    /// Stores the metadata vector back into a DirectoryObject's replPropertyMetaData attribute.
    /// </summary>
    public void SaveToDirectoryObject(DirectoryObject obj)
    {
        var json = Serialize();
        obj.SetAttribute("replPropertyMetaData", new DirectoryAttribute("replPropertyMetaData", json));
    }
}
