using System.Collections.Concurrent;
using System.Text.Json;
using Directory.Core.Models;

namespace Directory.Replication.Drsr;

/// <summary>
/// Manages linked value replication (LVR) per [MS-DRSR] section 4.1.10.2.11.
///
/// LVR tracks individual add/remove operations on DN-valued multi-valued attributes
/// (e.g., member, memberOf) so that concurrent modifications on different DCs can be
/// merged without losing individual value changes.
///
/// Each value has its own metadata (version, originating DC, USN, timestamp, deleted flag).
/// </summary>
public class LinkedValueReplication
{
    /// <summary>
    /// Metadata for a single linked value (one member of a multi-valued DN attribute).
    /// </summary>
    public class ValueMetaData
    {
        /// <summary>
        /// The DN value being tracked (e.g., a group member DN).
        /// </summary>
        public string Value { get; set; } = string.Empty;

        /// <summary>
        /// GUID of the DC that originated this value change.
        /// </summary>
        public Guid OriginatingDsaGuid { get; set; }

        /// <summary>
        /// USN on the originating DC when the value was changed.
        /// </summary>
        public long OriginatingUsn { get; set; }

        /// <summary>
        /// UTC timestamp when the value was changed (Windows FILETIME).
        /// </summary>
        public long OriginatingTime { get; set; }

        /// <summary>
        /// Version number — incremented on each add/remove toggle.
        /// </summary>
        public uint Version { get; set; }

        /// <summary>
        /// True if this value has been removed (tombstoned); false if present.
        /// </summary>
        public bool IsDeleted { get; set; }

        /// <summary>
        /// The local USN when this value was last replicated in.
        /// </summary>
        public long LocalUsn { get; set; }

        /// <summary>
        /// Creates a REPLVALINF_V3 wire structure from this metadata.
        /// </summary>
        public REPLVALINF_V3 ToWireFormat(DSNAME objectDsName, ATTRTYP attrTyp)
        {
            return new REPLVALINF_V3
            {
                PObject = objectDsName,
                AttrTyp = attrTyp,
                AttrVal = new ATTRVAL
                {
                    ValLen = (uint)System.Text.Encoding.Unicode.GetByteCount(Value),
                    PVal = System.Text.Encoding.Unicode.GetBytes(Value),
                },
                FIsPresent = !IsDeleted,
                MetaData = new VALUE_META_DATA_EXT
                {
                    TimeCreated = OriginatingTime,
                    DwVersion = Version,
                    TimeChanged = OriginatingTime,
                    UuidDsaOriginating = OriginatingDsaGuid,
                    UsnOriginating = OriginatingUsn,
                },
            };
        }
    }

    /// <summary>
    /// Tracks all linked values for a single attribute on a single object.
    /// Key is the value string (DN), value is its metadata.
    /// </summary>
    public class LinkedAttributeValues
    {
        private readonly ConcurrentDictionary<string, ValueMetaData> _values =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// All tracked value metadata entries.
        /// </summary>
        public IReadOnlyDictionary<string, ValueMetaData> Values => _values;

        /// <summary>
        /// Records a local value addition.
        /// </summary>
        public ValueMetaData AddValue(string value, Guid localDsaGuid, long localUsn)
        {
            var existing = _values.GetValueOrDefault(value);
            var newVersion = (existing?.Version ?? 0) + 1;

            var meta = new ValueMetaData
            {
                Value = value,
                OriginatingDsaGuid = localDsaGuid,
                OriginatingUsn = localUsn,
                OriginatingTime = DateTimeOffset.UtcNow.ToFileTime(),
                Version = newVersion,
                IsDeleted = false,
                LocalUsn = localUsn,
            };

            _values[value] = meta;
            return meta;
        }

        /// <summary>
        /// Records a local value removal (tombstones the value).
        /// </summary>
        public ValueMetaData RemoveValue(string value, Guid localDsaGuid, long localUsn)
        {
            var existing = _values.GetValueOrDefault(value);
            var newVersion = (existing?.Version ?? 0) + 1;

            var meta = new ValueMetaData
            {
                Value = value,
                OriginatingDsaGuid = localDsaGuid,
                OriginatingUsn = localUsn,
                OriginatingTime = DateTimeOffset.UtcNow.ToFileTime(),
                Version = newVersion,
                IsDeleted = true,
                LocalUsn = localUsn,
            };

            _values[value] = meta;
            return meta;
        }

        /// <summary>
        /// Applies a replicated value change. Uses last-writer-wins conflict resolution:
        /// compare version, then timestamp, then originating DSA GUID.
        /// </summary>
        public bool ApplyReplicatedValue(ValueMetaData incoming)
        {
            return _values.AddOrUpdate(
                incoming.Value,
                _ => incoming,
                (_, existing) =>
                {
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
        /// Gets all values that have changed since the specified local USN.
        /// </summary>
        public IReadOnlyList<ValueMetaData> GetChangesSince(long sinceUsn)
        {
            return _values.Values
                .Where(v => v.LocalUsn > sinceUsn)
                .OrderBy(v => v.LocalUsn)
                .ToList();
        }

        /// <summary>
        /// Gets only the present (non-deleted) values.
        /// </summary>
        public IReadOnlyList<string> GetPresentValues()
        {
            return _values.Values
                .Where(v => !v.IsDeleted)
                .Select(v => v.Value)
                .ToList();
        }
    }

    /// <summary>
    /// Storage for all linked values across all objects.
    /// Key: (objectGuid, attributeId) tuple.
    /// </summary>
    private readonly ConcurrentDictionary<(string ObjectGuid, uint AttributeId), LinkedAttributeValues>
        _store = new();

    /// <summary>
    /// Gets or creates the linked attribute values tracker for a specific object + attribute.
    /// </summary>
    public LinkedAttributeValues GetOrCreate(string objectGuid, uint attributeId)
    {
        return _store.GetOrAdd((objectGuid, attributeId), _ => new LinkedAttributeValues());
    }

    /// <summary>
    /// Gets all linked value changes for a naming context since a given USN.
    /// Returns changes ordered by local USN for transmission via DRS_MSG_GETCHGREPLY_V6.rgValues.
    /// </summary>
    public IReadOnlyList<(string ObjectGuid, uint AttributeId, ValueMetaData Meta)>
        GetLinkedValueChanges(long sinceUsn, int maxValues = int.MaxValue)
    {
        var results = new List<(string, uint, ValueMetaData)>();

        foreach (var ((objectGuid, attrId), linkedValues) in _store)
        {
            var changes = linkedValues.GetChangesSince(sinceUsn);
            foreach (var change in changes)
            {
                results.Add((objectGuid, attrId, change));

                if (results.Count >= maxValues)
                    return results.OrderBy(r => r.Item3.LocalUsn).ToList();
            }
        }

        return results.OrderBy(r => r.Item3.LocalUsn).ToList();
    }

    /// <summary>
    /// Merges a set of incoming linked value changes from a replication partner.
    /// </summary>
    public int MergeLinkedValueChanges(IReadOnlyList<REPLVALINF_V3> incomingValues, long localUsn)
    {
        int appliedCount = 0;

        foreach (var incoming in incomingValues)
        {
            var objectGuid = incoming.PObject.Guid.ToString();
            var linkedValues = GetOrCreate(objectGuid, incoming.AttrTyp.Value);

            var valueDn = incoming.AttrVal.PVal.Length > 0
                ? System.Text.Encoding.Unicode.GetString(incoming.AttrVal.PVal).TrimEnd('\0')
                : string.Empty;

            var meta = new ValueMetaData
            {
                Value = valueDn,
                OriginatingDsaGuid = incoming.MetaData.UuidDsaOriginating,
                OriginatingUsn = incoming.MetaData.UsnOriginating,
                OriginatingTime = incoming.MetaData.TimeChanged,
                Version = incoming.MetaData.DwVersion,
                IsDeleted = !incoming.FIsPresent,
                LocalUsn = localUsn,
            };

            if (linkedValues.ApplyReplicatedValue(meta))
                appliedCount++;
        }

        return appliedCount;
    }

    /// <summary>
    /// Serializes linked value metadata for a specific object + attribute to JSON.
    /// </summary>
    public string SerializeForObject(string objectGuid, uint attributeId)
    {
        var linked = GetOrCreate(objectGuid, attributeId);
        var entries = linked.Values.Values.Select(v => new
        {
            v.Value,
            DsaGuid = v.OriginatingDsaGuid.ToString(),
            v.OriginatingUsn,
            v.OriginatingTime,
            v.Version,
            v.IsDeleted,
            v.LocalUsn,
        });
        return JsonSerializer.Serialize(entries);
    }

    /// <summary>
    /// Deserializes linked value metadata for a specific object + attribute from JSON.
    /// </summary>
    public void DeserializeForObject(string objectGuid, uint attributeId, string json)
    {
        if (string.IsNullOrEmpty(json))
            return;

        var linked = GetOrCreate(objectGuid, attributeId);

        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                var meta = new ValueMetaData
                {
                    Value = element.GetProperty("Value").GetString() ?? string.Empty,
                    OriginatingDsaGuid = Guid.Parse(element.GetProperty("DsaGuid").GetString()),
                    OriginatingUsn = element.GetProperty("OriginatingUsn").GetInt64(),
                    OriginatingTime = element.GetProperty("OriginatingTime").GetInt64(),
                    Version = element.GetProperty("Version").GetUInt32(),
                    IsDeleted = element.GetProperty("IsDeleted").GetBoolean(),
                    LocalUsn = element.GetProperty("LocalUsn").GetInt64(),
                };

                linked.ApplyReplicatedValue(meta);
            }
        }
        catch (JsonException)
        {
            // Silently ignore malformed data
        }
    }
}
