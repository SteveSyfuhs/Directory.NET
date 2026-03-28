using System.Text;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Microsoft.Extensions.Logging;

namespace Directory.Replication.Drsr;

/// <summary>
/// Core replication engine implementing IDL_DRSGetNCChanges per [MS-DRSR] section 4.1.10.
///
/// Processes GetNCChanges requests with:
/// - Full USN watermark tracking (USN_VECTOR)
/// - Up-to-dateness vector filtering (skips changes already seen by requester)
/// - Object and byte count limits with MORE_DATA continuation
/// - Attribute-level metadata tracking (PROPERTY_META_DATA_EXT)
/// - Partial Attribute Set filtering for Global Catalog (GC) replication
/// - Schema prefix table construction and mapping
/// </summary>
public class ReplicationEngine
{
    private readonly IDirectoryStore _store;
    private readonly DcInstanceInfo _dcInfo;
    private readonly SchemaPrefixTable _prefixTable;
    private readonly LinkedValueReplication _linkedValueReplication;
    private readonly ConflictResolver _conflictResolver;
    private readonly ILogger<ReplicationEngine> _logger;

    /// <summary>
    /// Maximum objects to return per response if not otherwise specified.
    /// </summary>
    private const uint DefaultMaxObjects = 1000;

    /// <summary>
    /// Maximum bytes to return per response if not otherwise specified (~10 MB).
    /// </summary>
    private const uint DefaultMaxBytes = 10_485_760;

    public ReplicationEngine(
        IDirectoryStore store,
        DcInstanceInfo dcInfo,
        SchemaPrefixTable prefixTable,
        LinkedValueReplication linkedValueReplication,
        ConflictResolver conflictResolver,
        ILogger<ReplicationEngine> logger)
    {
        _store = store;
        _dcInfo = dcInfo;
        _prefixTable = prefixTable;
        _linkedValueReplication = linkedValueReplication;
        _conflictResolver = conflictResolver;
        _logger = logger;
    }

    /// <summary>
    /// Processes a GetNCChanges request, returning objects changed since the
    /// caller's USN watermark, filtered by the caller's up-to-dateness vector.
    /// </summary>
    public async Task<DRS_MSG_GETCHGREPLY_V6> ProcessGetNCChangesAsync(
        DRS_MSG_GETCHGREQ_V8 request,
        string tenantId,
        CancellationToken ct = default)
    {
        var ncDn = request.PNC?.StringName ?? string.Empty;
        var usnFrom = request.UsnvecFrom;
        var maxObjects = request.CMaxObjects > 0 ? request.CMaxObjects : DefaultMaxObjects;
        var maxBytes = request.CMaxBytes > 0 ? request.CMaxBytes : DefaultMaxBytes;

        _logger.LogInformation(
            "GetNCChanges: NC={NC}, UsnFrom={Usn}, MaxObj={MaxObj}, MaxBytes={MaxBytes}, Flags={Flags}",
            ncDn, usnFrom.UsnHighObjUpdate, maxObjects, maxBytes, request.UlFlags);

        // Build the up-to-dateness vector for filtering
        var partnerUtdVector = request.PUpToDateVecDest;

        // Query objects changed since the USN watermark
        var filter = new GreaterOrEqualFilterNode("uSNChanged", (usnFrom.UsnHighObjUpdate + 1).ToString());
        var includeDeleted = true; // Replication must include tombstones

        var searchResult = await _store.SearchAsync(
            tenantId, ncDn, SearchScope.WholeSubtree,
            filter, null, 0, 0, null,
            (int)maxObjects * 2, // Fetch extra to account for UTD filtering
            includeDeleted, ct);

        // Build the response
        var reply = new DRS_MSG_GETCHGREPLY_V6
        {
            UuidDsaObjSrc = Guid.TryParse(_dcInfo.InvocationId, out var invGuid) ? invGuid : Guid.NewGuid(),
            UuidInvocIdSrc = Guid.TryParse(_dcInfo.InvocationId, out var inv2) ? inv2 : Guid.NewGuid(),
            PNC = request.PNC,
            UsnvecFrom = usnFrom,
            PrefixTableSrc = _prefixTable.ToWireFormat(),
        };

        // Process objects
        REPLENTINFLIST firstEntry = null;
        REPLENTINFLIST lastEntry = null;
        uint objectCount = 0;
        uint totalBytes = 0;
        long highestObjUsn = usnFrom.UsnHighObjUpdate;
        long highestPropUsn = usnFrom.UsnHighPropUpdate;
        var linkedValues = new List<REPLVALINF_V3>();

        // Sort by USN to ensure consistent ordering
        var sortedObjects = searchResult.Entries
            .OrderBy(o => o.USNChanged)
            .ToList();

        foreach (var obj in sortedObjects)
        {
            ct.ThrowIfCancellationRequested();

            // Filter by up-to-dateness vector
            if (ShouldFilterByUtdVector(obj, partnerUtdVector))
            {
                _logger.LogDebug("Skipping {DN} — already seen by partner (UTD filter)", obj.DistinguishedName);
                continue;
            }

            // Build the ENTINF for this object
            var entinf = BuildEntInf(obj, request.PPartialAttrSet);

            // Estimate size
            uint entrySize = EstimateEntrySize(entinf);
            if (objectCount > 0 && totalBytes + entrySize > maxBytes)
            {
                // Byte limit reached — signal MORE_DATA
                reply.FMoreData = true;
                break;
            }

            // Build metadata
            var metaVector = PropertyMetaDataVector.FromDirectoryObject(obj);

            // Create the linked list entry
            var replEntry = new REPLENTINFLIST
            {
                Entinf = entinf,
                FIsNCPrefix = obj.DistinguishedName.Equals(ncDn, StringComparison.OrdinalIgnoreCase),
                PMetaDataExt = metaVector.ToWireFormat(),
            };

            if (firstEntry == null)
            {
                firstEntry = replEntry;
                lastEntry = replEntry;
            }
            else
            {
                lastEntry.PNextEntInf = replEntry;
                lastEntry = replEntry;
            }

            objectCount++;
            totalBytes += entrySize;

            // Track highest USN
            if (obj.USNChanged > highestObjUsn)
                highestObjUsn = obj.USNChanged;
            if (obj.USNChanged > highestPropUsn)
                highestPropUsn = obj.USNChanged;

            // Collect linked value changes for this object
            CollectLinkedValues(obj, usnFrom.UsnHighPropUpdate, linkedValues);

            // Check object limit
            if (objectCount >= maxObjects)
            {
                // Check if there are more objects
                if (sortedObjects.IndexOf(obj) < sortedObjects.Count - 1)
                    reply.FMoreData = true;
                break;
            }
        }

        reply.PObjects = firstEntry;
        reply.CNumObjects = objectCount;
        reply.CNumBytes = totalBytes;
        reply.RgValues = linkedValues;
        reply.CNumNcSizeValues = (uint)linkedValues.Count;

        // Set USN vector for "to" watermark
        reply.UsnvecTo = new USN_VECTOR
        {
            UsnHighObjUpdate = highestObjUsn,
            UsnHighPropUpdate = highestPropUsn,
        };

        // Build up-to-dateness vector
        reply.PUpToDateVecSrc = BuildUpToDateVector(tenantId);

        // NC size information if requested
        if (request.UlFlags.HasFlag(DrsGetNcChangesFlags.DRS_GET_NC_SIZE))
        {
            reply.CNumNcSizeObjects = (uint)searchResult.TotalEstimate;
        }

        _logger.LogInformation(
            "GetNCChanges reply: {Count} objects, {Bytes} bytes, MoreData={More}, " +
            "UsnTo=({ObjUsn},{PropUsn}), LinkedValues={LV}",
            objectCount, totalBytes, reply.FMoreData,
            highestObjUsn, highestPropUsn, linkedValues.Count);

        return reply;
    }

    /// <summary>
    /// Applies incoming replication data (from a GetNCChanges response) to the local store.
    /// </summary>
    public async Task<int> ApplyIncomingChangesAsync(
        DRS_MSG_GETCHGREPLY_V6 response,
        string tenantId,
        CancellationToken ct = default)
    {
        int appliedCount = 0;
        var remotePrefixTable = SchemaPrefixTable.FromWireFormat(response.PrefixTableSrc);

        // Process object entries
        var current = response.PObjects;
        while (current != null)
        {
            ct.ThrowIfCancellationRequested();

            var entinf = current.Entinf;
            var dn = entinf.PName.StringName;
            var objectGuid = entinf.PName.Guid.ToString();

            // Try to find existing local object
            var localObj = await _store.GetByGuidAsync(tenantId, objectGuid, ct)
                        ?? await _store.GetByDnAsync(tenantId, dn, ct);

            if (localObj == null)
            {
                // New object — create it
                localObj = CreateObjectFromEntInf(entinf, tenantId, remotePrefixTable);
                var usn = await _store.GetNextUsnAsync(tenantId, localObj.DomainDn, ct);
                localObj.USNChanged = usn;
                localObj.USNCreated = usn;

                await _store.CreateAsync(localObj, ct);
                appliedCount++;
            }
            else
            {
                // Existing object — merge with conflict resolution
                var localMetadata = PropertyMetaDataVector.FromDirectoryObject(localObj);
                var updatedAttrs = _conflictResolver.MergeReplicationEntries(
                    localObj, entinf, current.PMetaDataExt, localMetadata, remotePrefixTable);

                if (updatedAttrs.Count > 0)
                {
                    localMetadata.SaveToDirectoryObject(localObj);
                    var usn = await _store.GetNextUsnAsync(tenantId, localObj.DomainDn, ct);
                    localObj.USNChanged = usn;
                    localObj.WhenChanged = DateTimeOffset.UtcNow;

                    await _store.UpdateAsync(localObj, ct);
                    appliedCount++;
                }
            }

            current = current.PNextEntInf;
        }

        // Process linked value changes
        if (response.RgValues.Count > 0)
        {
            var localUsn = 0L;
            if (response.PNC != null)
            {
                localUsn = await _store.GetNextUsnAsync(tenantId, response.PNC.StringName, ct);
            }

            _linkedValueReplication.MergeLinkedValueChanges(response.RgValues, localUsn);
        }

        _logger.LogInformation("Applied {Count} incoming replication changes", appliedCount);
        return appliedCount;
    }

    /// <summary>
    /// Checks whether an object should be filtered out based on the partner's
    /// up-to-dateness vector. If the partner has already seen the change from
    /// the originating DC, we skip it.
    /// </summary>
    private bool ShouldFilterByUtdVector(DirectoryObject obj, UPTODATE_VECTOR_V2_EXT partnerUtd)
    {
        if (partnerUtd == null || partnerUtd.RgCursors.Count == 0)
            return false;

        // Get the originating invocation ID and USN from the object's metadata
        var metaAttr = obj.GetAttribute("replPropertyMetaData");
        if (metaAttr == null)
            return false;

        var metaStr = metaAttr.GetFirstString();
        if (string.IsNullOrEmpty(metaStr))
            return false;

        // Check if the metadata format is "invocationId|usn" (simple format from existing code)
        if (metaStr.Contains('|'))
        {
            var parts = metaStr.Split('|');
            if (parts.Length >= 2 &&
                Guid.TryParse(parts[0], out var origInvocId) &&
                long.TryParse(parts[1], out var origUsn))
            {
                // Check if the partner has already seen this change
                var cursor = partnerUtd.RgCursors.FirstOrDefault(
                    c => c.UuidDsa == origInvocId);

                if (cursor != null && origUsn <= cursor.UsnHighPropUpdate)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Builds an ENTINF structure from a DirectoryObject, optionally filtering
    /// to a partial attribute set (for GC replication).
    /// </summary>
    private ENTINF BuildEntInf(DirectoryObject obj, PARTIAL_ATTR_VECTOR partialAttrSet)
    {
        var dsname = DSNAME.FromDn(
            obj.DistinguishedName,
            Guid.TryParse(obj.ObjectGuid, out var g) ? g : Guid.Empty,
            obj.ObjectSid != null ? Encoding.ASCII.GetBytes(obj.ObjectSid) : null);

        var attrBlock = new ATTRBLOCK();
        var partialAttrIds = partialAttrSet?.RgPartialAttr.Select(a => a.Value).ToHashSet();

        // Build attribute block from the DirectoryObject
        foreach (var kvp in obj.Attributes)
        {
            var attrTyp = _prefixTable.OidToAttrTyp(MapAttributeNameToOid(kvp.Key) ?? $"1.2.840.113556.1.4.{kvp.Key.GetHashCode() & 0xFFFF}");

            // If partial attribute set is specified, only include matching attributes
            if (partialAttrIds != null && !partialAttrIds.Contains(attrTyp.Value))
                continue;

            var attr = new ATTR
            {
                AttrTyp = attrTyp,
                AttrVal = BuildAttrValBlock(kvp.Value),
            };

            attrBlock.PAttr.Add(attr);
        }

        // Add well-known top-level attributes
        AddWellKnownAttributes(obj, attrBlock, partialAttrIds);

        attrBlock.AttrCount = (uint)attrBlock.PAttr.Count;

        return new ENTINF
        {
            PName = dsname,
            UlFlags = obj.IsDeleted ? 1u : 0u,
            AttrBlock = attrBlock,
        };
    }

    /// <summary>
    /// Creates a DirectoryObject from an incoming ENTINF.
    /// </summary>
    private DirectoryObject CreateObjectFromEntInf(
        ENTINF entinf, string tenantId, SchemaPrefixTable remotePrefixTable)
    {
        var obj = new DirectoryObject
        {
            Id = entinf.PName.StringName.ToLowerInvariant(),
            TenantId = tenantId,
            DistinguishedName = entinf.PName.StringName,
            ObjectGuid = entinf.PName.Guid != Guid.Empty
                ? entinf.PName.Guid.ToString()
                : Guid.NewGuid().ToString(),
            IsDeleted = (entinf.UlFlags & 1) != 0,
            WhenCreated = DateTimeOffset.UtcNow,
            WhenChanged = DateTimeOffset.UtcNow,
        };

        // Extract domain DN from the object's DN
        var parts = entinf.PName.StringName.Split(',');
        var dcParts = parts
            .Where(p => p.TrimStart().StartsWith("DC=", StringComparison.OrdinalIgnoreCase))
            .ToList();
        obj.DomainDn = string.Join(',', dcParts);

        // Extract parent DN
        var firstComma = entinf.PName.StringName.IndexOf(',');
        if (firstComma >= 0)
            obj.ParentDn = entinf.PName.StringName[(firstComma + 1)..];

        // Apply attributes
        foreach (var attr in entinf.AttrBlock.PAttr)
        {
            var oid = remotePrefixTable.AttrTypToOid(attr.AttrTyp);
            var attrName = oid != null ? MapOidToAttributeName(oid) : null;

            if (attrName == null)
                continue;

            var values = new List<object>();
            foreach (var val in attr.AttrVal.PVal)
            {
                if (val.PVal.Length > 0)
                {
                    try
                    {
                        values.Add(Encoding.Unicode.GetString(val.PVal).TrimEnd('\0'));
                    }
                    catch
                    {
                        values.Add(val.PVal);
                    }
                }
            }

            if (values.Count > 0)
                obj.SetAttribute(attrName, new DirectoryAttribute(attrName, [.. values]));
        }

        return obj;
    }

    /// <summary>
    /// Collects linked value changes for an object since a given USN.
    /// </summary>
    private void CollectLinkedValues(
        DirectoryObject obj, long sinceUsn, List<REPLVALINF_V3> results)
    {
        // Check DN-valued multi-valued attributes that use LVR
        var linkedAttrs = new[] { "member", "memberOf" };
        var dsname = DSNAME.FromDn(obj.DistinguishedName,
            Guid.TryParse(obj.ObjectGuid, out var g) ? g : Guid.Empty);

        foreach (var attrName in linkedAttrs)
        {
            var attrTyp = _prefixTable.OidToAttrTyp(
                MapAttributeNameToOid(attrName) ?? $"1.2.840.113556.1.4.{attrName.GetHashCode() & 0xFFFF}");

            var changes = _linkedValueReplication.GetLinkedValueChanges(sinceUsn);
            foreach (var (objectGuid, attributeId, meta) in changes)
            {
                if (objectGuid == obj.ObjectGuid && attributeId == attrTyp.Value)
                {
                    results.Add(meta.ToWireFormat(dsname, attrTyp));
                }
            }
        }
    }

    /// <summary>
    /// Builds the local up-to-dateness vector.
    /// </summary>
    private UPTODATE_VECTOR_V2_EXT BuildUpToDateVector(string tenantId)
    {
        var localGuid = Guid.TryParse(_dcInfo.InvocationId, out var g) ? g : Guid.NewGuid();

        var vector = new UPTODATE_VECTOR_V2_EXT
        {
            DwVersion = 2,
            CNumCursors = 1,
            RgCursors =
            [
                new UPTODATE_CURSOR_V2
                {
                    UuidDsa = localGuid,
                    UsnHighPropUpdate = 0, // Would be filled from actual USN tracking
                    FtimeLastSyncSuccess = DateTimeOffset.UtcNow.ToFileTime(),
                },
            ],
        };

        return vector;
    }

    /// <summary>
    /// Estimates the wire size of a REPLENTINFLIST entry.
    /// </summary>
    private static uint EstimateEntrySize(ENTINF entinf)
    {
        uint size = 64; // Base overhead for DSNAME, pointers, etc.

        size += (uint)(entinf.PName.StringName.Length * 2); // UTF-16 DN
        size += entinf.PName.SidLen;

        foreach (var attr in entinf.AttrBlock.PAttr)
        {
            size += 8; // ATTRTYP + overhead
            foreach (var val in attr.AttrVal.PVal)
            {
                size += 4 + val.ValLen; // length prefix + data
            }
        }

        return size;
    }

    private static ATTRVALBLOCK BuildAttrValBlock(DirectoryAttribute attr)
    {
        var block = new ATTRVALBLOCK();

        foreach (var val in attr.Values)
        {
            byte[] bytes;
            if (val is byte[] b)
                bytes = b;
            else
                bytes = Encoding.Unicode.GetBytes(val.ToString() ?? string.Empty);

            block.PVal.Add(new ATTRVAL
            {
                ValLen = (uint)bytes.Length,
                PVal = bytes,
            });
        }

        block.ValCount = (uint)block.PVal.Count;
        return block;
    }

    /// <summary>
    /// Adds well-known top-level DirectoryObject properties as ATTR entries.
    /// </summary>
    private void AddWellKnownAttributes(
        DirectoryObject obj,
        ATTRBLOCK block,
        HashSet<uint> partialAttrIds)
    {
        void TryAdd(string attrName, string value)
        {
            if (value == null) return;

            var oid = MapAttributeNameToOid(attrName);
            if (oid == null) return;

            var attrTyp = _prefixTable.OidToAttrTyp(oid);
            if (partialAttrIds != null && !partialAttrIds.Contains(attrTyp.Value)) return;

            var bytes = Encoding.Unicode.GetBytes(value);
            block.PAttr.Add(new ATTR
            {
                AttrTyp = attrTyp,
                AttrVal = new ATTRVALBLOCK
                {
                    ValCount = 1,
                    PVal = [new ATTRVAL { ValLen = (uint)bytes.Length, PVal = bytes }],
                },
            });
        }

        TryAdd("cn", obj.Cn);
        TryAdd("displayName", obj.DisplayName);
        TryAdd("sAMAccountName", obj.SAMAccountName);
        TryAdd("userPrincipalName", obj.UserPrincipalName);
        TryAdd("distinguishedName", obj.DistinguishedName);
        TryAdd("objectSid", obj.ObjectSid);

        // Object class (multi-valued)
        if (obj.ObjectClass.Count > 0)
        {
            var oid = MapAttributeNameToOid("objectClass");
            if (oid != null)
            {
                var attrTyp = _prefixTable.OidToAttrTyp(oid);
                if (partialAttrIds == null || partialAttrIds.Contains(attrTyp.Value))
                {
                    var valBlock = new ATTRVALBLOCK();
                    foreach (var oc in obj.ObjectClass)
                    {
                        var bytes = Encoding.Unicode.GetBytes(oc);
                        valBlock.PVal.Add(new ATTRVAL { ValLen = (uint)bytes.Length, PVal = bytes });
                    }
                    valBlock.ValCount = (uint)valBlock.PVal.Count;

                    block.PAttr.Add(new ATTR { AttrTyp = attrTyp, AttrVal = valBlock });
                }
            }
        }
    }

    /// <summary>
    /// Maps well-known attribute names to OID strings.
    /// </summary>
    private static string MapAttributeNameToOid(string name)
    {
        return name.ToLowerInvariant() switch
        {
            "cn" => "2.5.4.3",
            "sn" => "2.5.4.4",
            "c" => "2.5.4.6",
            "l" => "2.5.4.7",
            "st" => "2.5.4.8",
            "o" => "2.5.4.10",
            "ou" => "2.5.4.11",
            "title" => "2.5.4.12",
            "description" => "2.5.4.13",
            "telephonenumber" => "2.5.4.20",
            "givenname" => "2.5.4.42",
            "objectclass" => "1.2.840.113556.1.2.102",
            "samaccountname" => "1.2.840.113556.1.4.221",
            "userprincipalname" => "1.2.840.113556.1.4.656",
            "memberof" => "1.2.840.113556.1.4.222",
            "member" => "1.2.840.113556.1.4.223",
            "useraccountcontrol" => "1.2.840.113556.1.4.8",
            "objectsid" => "1.2.840.113556.1.4.146",
            "objectguid" => "1.2.840.113556.1.4.148",
            "displayname" => "1.2.840.113556.1.4.220",
            "serviceprincipalname" => "1.2.840.113556.1.4.771",
            "distinguishedname" => "2.5.4.49",
            "dc" => "0.9.2342.19200300.100.1.25",
            _ => null,
        };
    }

    /// <summary>
    /// Maps OIDs back to attribute names (reverse of MapAttributeNameToOid).
    /// </summary>
    private static string MapOidToAttributeName(string oid)
    {
        return oid switch
        {
            "2.5.4.3" => "cn",
            "2.5.4.4" => "sn",
            "2.5.4.6" => "c",
            "2.5.4.7" => "l",
            "2.5.4.8" => "st",
            "2.5.4.10" => "o",
            "2.5.4.11" => "ou",
            "2.5.4.12" => "title",
            "2.5.4.13" => "description",
            "2.5.4.20" => "telephoneNumber",
            "2.5.4.42" => "givenName",
            "2.5.4.49" => "distinguishedName",
            "1.2.840.113556.1.2.102" => "objectClass",
            "1.2.840.113556.1.4.8" => "userAccountControl",
            "1.2.840.113556.1.4.146" => "objectSid",
            "1.2.840.113556.1.4.148" => "objectGUID",
            "1.2.840.113556.1.4.220" => "displayName",
            "1.2.840.113556.1.4.221" => "sAMAccountName",
            "1.2.840.113556.1.4.222" => "memberOf",
            "1.2.840.113556.1.4.223" => "member",
            "1.2.840.113556.1.4.656" => "userPrincipalName",
            "1.2.840.113556.1.4.771" => "servicePrincipalName",
            "0.9.2342.19200300.100.1.25" => "dc",
            _ => null,
        };
    }
}
