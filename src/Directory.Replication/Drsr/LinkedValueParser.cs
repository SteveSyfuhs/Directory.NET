using System.Text;
using Directory.Core.Interfaces;
using Directory.Rpc.Ndr;
using Microsoft.Extensions.Logging;

namespace Directory.Replication.Drsr;

/// <summary>
/// Parses linked value replication data (REPLVALINF_V3 structures) from GetNCChanges responses.
/// Linked attributes like 'member' are replicated separately from regular attributes using
/// linked value replication (LVR) per MS-DRSR section 4.1.10.2.11.
/// </summary>
public class LinkedValueParser
{
    /// <summary>
    /// Parse a list of already-deserialized REPLVALINF_V3 structures into
    /// <see cref="LinkedValueChange"/> objects, resolving ATTRTYPs to attribute names
    /// using the given prefix table resolver.
    /// </summary>
    /// <param name="replValues">The REPLVALINF_V3 list from a GetNCChanges reply.</param>
    /// <param name="prefixTable">The prefix table resolver for mapping ATTRTYPs to names.</param>
    /// <returns>A list of parsed linked value changes.</returns>
    public static List<LinkedValueChange> ParseLinkedValues(
        List<REPLVALINF_V3> replValues,
        PrefixTableResolver prefixTable)
    {
        var results = new List<LinkedValueChange>(replValues.Count);

        foreach (var val in replValues)
        {
            var attrName = prefixTable.ResolveToName(val.AttrTyp.Value);

            // The value data is typically a DSNAME (the DN of the linked object)
            string valueDn = string.Empty;
            Guid valueGuid = Guid.Empty;

            if (val.AttrVal.PVal.Length > 0)
            {
                // Try to parse as a DSNAME structure or as a UTF-16 DN string
                try
                {
                    valueDn = ParseDnFromAttrVal(val.AttrVal.PVal, out valueGuid);
                }
                catch
                {
                    // Fallback: treat as raw UTF-16 string
                    try
                    {
                        valueDn = Encoding.Unicode.GetString(val.AttrVal.PVal).TrimEnd('\0');
                    }
                    catch
                    {
                        // Could not parse the value
                    }
                }
            }

            results.Add(new LinkedValueChange
            {
                ObjectDn = val.PObject.StringName,
                ObjectGuid = val.PObject.Guid,
                AttributeName = attrName ?? $"unknown-{val.AttrTyp.Value:X8}",
                ValueDn = valueDn,
                ValueGuid = valueGuid,
                IsPresent = val.FIsPresent,
                TimeChanged = val.MetaData.TimeChanged != 0
                    ? DateTime.FromFileTimeUtc(val.MetaData.TimeChanged)
                    : DateTime.MinValue,
                Version = (int)val.MetaData.DwVersion,
                OriginatingDsa = val.MetaData.UuidDsaOriginating,
                OriginatingUsn = val.MetaData.UsnOriginating,
            });
        }

        return results;
    }

    /// <summary>
    /// Parse a list of REPLVALINF_V3 from raw NDR-encoded bytes (for standalone parsing
    /// outside of the normal GetNCChanges flow).
    /// </summary>
    /// <param name="ndrData">The NDR-encoded byte array containing REPLVALINF_V3 structures.</param>
    /// <param name="count">The number of REPLVALINF_V3 entries to read.</param>
    /// <param name="prefixTable">The prefix table resolver.</param>
    /// <returns>A list of parsed linked value changes.</returns>
    public static List<LinkedValueChange> ParseLinkedValuesFromNdr(
        byte[] ndrData,
        int count,
        PrefixTableResolver prefixTable)
    {
        var reader = new NdrReader(ndrData);
        var replValues = new List<REPLVALINF_V3>(count);

        for (int i = 0; i < count; i++)
        {
            replValues.Add(ReadReplValInfFromNdr(reader));
        }

        return ParseLinkedValues(replValues, prefixTable);
    }

    /// <summary>
    /// Apply linked value changes to the directory store. For each change,
    /// adds or removes the linked value (e.g., group member) on the target object.
    /// </summary>
    /// <param name="store">The directory store to update.</param>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="changes">The linked value changes to apply.</param>
    /// <param name="logger">Optional logger for diagnostics.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of changes applied.</returns>
    public static async Task<int> ApplyLinkedValuesAsync(
        IDirectoryStore store,
        string tenantId,
        List<LinkedValueChange> changes,
        ILogger logger = null,
        CancellationToken ct = default)
    {
        int appliedCount = 0;

        foreach (var change in changes)
        {
            ct.ThrowIfCancellationRequested();

            // Find the target object by GUID first, then by DN
            var obj = await store.GetByGuidAsync(tenantId, change.ObjectGuid.ToString(), ct)
                   ?? await store.GetByDnAsync(tenantId, change.ObjectDn, ct);

            if (obj == null)
            {
                logger?.LogWarning(
                    "LinkedValueParser: target object not found for linked value change: " +
                    "ObjectDN={ObjectDn}, ObjectGuid={ObjectGuid}, Attr={Attr}",
                    change.ObjectDn, change.ObjectGuid, change.AttributeName);
                continue;
            }

            var attr = obj.GetAttribute(change.AttributeName);
            var currentValues = attr?.Values.Select(v => v?.ToString() ?? string.Empty).ToList()
                             ?? [];

            if (change.IsPresent)
            {
                // Add the linked value if not already present
                if (!currentValues.Contains(change.ValueDn, StringComparer.OrdinalIgnoreCase))
                {
                    currentValues.Add(change.ValueDn);
                    obj.SetAttribute(change.AttributeName,
                        new Core.Models.DirectoryAttribute(change.AttributeName, currentValues.Cast<object>().ToList()));

                    await store.UpdateAsync(obj, ct);
                    appliedCount++;

                    logger?.LogDebug(
                        "LinkedValueParser: added linked value {ValueDn} to {Attr} on {ObjectDn}",
                        change.ValueDn, change.AttributeName, change.ObjectDn);
                }
            }
            else
            {
                // Remove the linked value if present
                int removed = currentValues.RemoveAll(v =>
                    string.Equals(v, change.ValueDn, StringComparison.OrdinalIgnoreCase));

                if (removed > 0)
                {
                    obj.SetAttribute(change.AttributeName,
                        new Core.Models.DirectoryAttribute(change.AttributeName, currentValues.Cast<object>().ToList()));

                    await store.UpdateAsync(obj, ct);
                    appliedCount++;

                    logger?.LogDebug(
                        "LinkedValueParser: removed linked value {ValueDn} from {Attr} on {ObjectDn}",
                        change.ValueDn, change.AttributeName, change.ObjectDn);
                }
            }
        }

        return appliedCount;
    }

    /// <summary>
    /// Parse a DN and optional GUID from the attribute value bytes.
    /// The value may be a raw DSNAME structure or a UTF-16 DN string.
    /// </summary>
    private static string ParseDnFromAttrVal(byte[] data, out Guid guid)
    {
        guid = Guid.Empty;

        // A DSNAME starts with structLen (4 bytes) + sidLen (4 bytes) + GUID (16 bytes) + ...
        // Minimum DSNAME is 28 bytes header
        if (data.Length >= 28)
        {
            uint structLen = BitConverter.ToUInt32(data, 0);
            uint sidLen = BitConverter.ToUInt32(data, 4);

            // Sanity check: structLen should be close to data.Length
            if (structLen > 0 && structLen <= data.Length + 8)
            {
                guid = new Guid(data.AsSpan(8, 16));
                int nameOffset = 28 + (int)sidLen;

                if (nameOffset < data.Length)
                {
                    uint nameLen = BitConverter.ToUInt32(data, 24);
                    if (nameLen > 0 && nameOffset + (nameLen + 1) * 2 <= data.Length)
                    {
                        return Encoding.Unicode
                            .GetString(data, nameOffset, (int)(nameLen + 1) * 2)
                            .TrimEnd('\0');
                    }
                }
            }
        }

        // Fallback: treat entire data as UTF-16 DN
        return Encoding.Unicode.GetString(data).TrimEnd('\0');
    }

    /// <summary>
    /// Read a single REPLVALINF_V3 from NDR data.
    /// </summary>
    private static REPLVALINF_V3 ReadReplValInfFromNdr(NdrReader reader)
    {
        var val = new REPLVALINF_V3();

        // pObject (DSNAME)
        var structLen = reader.ReadUInt32();
        var sidLen = reader.ReadUInt32();
        var guidBytes = reader.ReadBytes(16);
        val.PObject = new DSNAME
        {
            StructLen = structLen,
            SidLen = sidLen,
            Guid = new Guid(guidBytes.Span),
        };

        if (sidLen > 0)
        {
            val.PObject.Sid = reader.ReadBytes((int)sidLen).ToArray();
        }

        var nameLen = reader.ReadUInt32();
        val.PObject.NameLen = nameLen;
        if (nameLen > 0)
        {
            var nameBytes = reader.ReadBytes((int)(nameLen + 1) * 2);
            val.PObject.StringName = Encoding.Unicode.GetString(nameBytes.Span).TrimEnd('\0');
        }

        // attrTyp
        val.AttrTyp = new ATTRTYP(reader.ReadUInt32());

        // Aval: valLen + pointer to data
        val.AttrVal = new ATTRVAL { ValLen = reader.ReadUInt32() };
        var dataPointer = reader.ReadUInt32();
        if (dataPointer != 0)
        {
            var dataLen = reader.ReadUInt32();
            val.AttrVal.PVal = reader.ReadBytes((int)dataLen).ToArray();
        }

        // fIsPresent
        val.FIsPresent = reader.ReadBoolean();

        // VALUE_META_DATA_EXT
        val.MetaData = new VALUE_META_DATA_EXT
        {
            TimeCreated = reader.ReadInt64(),
            DwVersion = reader.ReadUInt32(),
            TimeChanged = reader.ReadInt64(),
            UuidDsaOriginating = new Guid(reader.ReadBytes(16).Span),
            UsnOriginating = reader.ReadInt64(),
        };

        return val;
    }
}

/// <summary>
/// Represents a single linked value change from DRS replication.
/// Linked attributes (like 'member') are replicated individually rather than
/// as a complete attribute value set.
/// </summary>
public class LinkedValueChange
{
    /// <summary>DN of the object being modified (e.g., the group DN).</summary>
    public string ObjectDn { get; init; } = "";

    /// <summary>GUID of the object being modified.</summary>
    public Guid ObjectGuid { get; init; }

    /// <summary>Attribute name (e.g., "member").</summary>
    public string AttributeName { get; init; } = "";

    /// <summary>DN of the linked value (e.g., the member being added/removed).</summary>
    public string ValueDn { get; init; } = "";

    /// <summary>GUID of the linked value object.</summary>
    public Guid ValueGuid { get; init; }

    /// <summary>True if the value is being added; false if it is being removed (tombstoned).</summary>
    public bool IsPresent { get; init; }

    /// <summary>When the linked value was last changed.</summary>
    public DateTime TimeChanged { get; init; }

    /// <summary>Replication version counter for this linked value.</summary>
    public int Version { get; init; }

    /// <summary>GUID of the DC that originated this change.</summary>
    public Guid OriginatingDsa { get; init; }

    /// <summary>USN on the originating DC.</summary>
    public long OriginatingUsn { get; init; }
}
