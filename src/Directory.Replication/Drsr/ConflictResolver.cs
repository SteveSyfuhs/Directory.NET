using System.Collections.Concurrent;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Microsoft.Extensions.Logging;

namespace Directory.Replication.Drsr;

/// <summary>
/// Resolves replication conflicts per [MS-DRSR] section 4.1.10.6.
///
/// Active Directory uses multi-master replication where the same object can be
/// modified on different DCs simultaneously. When conflicts arise, they are
/// resolved deterministically using these rules:
///
/// 1. Attribute conflicts: last-writer-wins (highest version, then timestamp, then DSA GUID).
/// 2. Name conflicts: loser gets RDN mangled to "CNF:&lt;objectGuid&gt;".
/// 3. Tombstone conflicts: resurrection loses to deletion in most cases.
/// </summary>
public class ConflictResolver
{
    private readonly ISchemaService _schemaService;
    private readonly ILogger<ConflictResolver> _logger;

    /// <summary>
    /// Lazily-built OID→attribute name lookup from the schema service.
    /// Falls back to the static well-known OID map if the schema service is unavailable.
    /// </summary>
    private ConcurrentDictionary<string, string> _oidToNameCache;

    public ConflictResolver(ISchemaService schemaService, ILogger<ConflictResolver> logger)
    {
        _schemaService = schemaService;
        _logger = logger;
    }

    /// <summary>
    /// Resolves an attribute-level conflict using last-writer-wins semantics.
    /// Returns the winning metadata entry.
    ///
    /// Resolution order per [MS-DRSR]:
    /// 1. Higher version number wins.
    /// 2. If versions are equal, later timestamp wins.
    /// 3. If timestamps are equal, higher originating DSA GUID wins (lexicographic).
    /// </summary>
    public PropertyMetaDataEntry ResolveAttributeConflict(
        PropertyMetaDataEntry local,
        PropertyMetaDataEntry incoming)
    {
        // Compare version numbers
        if (incoming.Version > local.Version)
        {
            _logger.LogDebug(
                "Attribute conflict resolved: incoming version {InVer} > local {LocVer} for attr {Attr}",
                incoming.Version, local.Version, incoming.AttributeId);
            return incoming;
        }

        if (incoming.Version < local.Version)
        {
            _logger.LogDebug(
                "Attribute conflict resolved: local version {LocVer} > incoming {InVer} for attr {Attr}",
                local.Version, incoming.Version, local.AttributeId);
            return local;
        }

        // Versions equal — compare timestamps
        if (incoming.OriginatingTime > local.OriginatingTime)
        {
            _logger.LogDebug(
                "Attribute conflict resolved by timestamp: incoming wins for attr {Attr}",
                incoming.AttributeId);
            return incoming;
        }

        if (incoming.OriginatingTime < local.OriginatingTime)
        {
            _logger.LogDebug(
                "Attribute conflict resolved by timestamp: local wins for attr {Attr}",
                local.AttributeId);
            return local;
        }

        // Timestamps equal — compare originating DSA GUIDs (higher wins)
        int guidCompare = incoming.OriginatingDsaGuid.CompareTo(local.OriginatingDsaGuid);
        if (guidCompare > 0)
        {
            _logger.LogDebug(
                "Attribute conflict resolved by DSA GUID: incoming wins for attr {Attr}",
                incoming.AttributeId);
            return incoming;
        }

        _logger.LogDebug(
            "Attribute conflict resolved by DSA GUID: local wins for attr {Attr}",
            local.AttributeId);
        return local;
    }

    /// <summary>
    /// Resolves a naming conflict where two objects have the same RDN under the same parent.
    /// The loser's RDN is mangled to include "CNF:" and its objectGUID, making it unique.
    ///
    /// Per [MS-DRSR] section 4.1.10.6.3:
    /// - The object with the higher objectGUID (compared as a string) wins and keeps its name.
    /// - The loser's RDN is changed to: originalRDN + '\n' + "CNF:" + objectGUID
    ///
    /// Returns (winnerDn, loserDn, loserNewDn).
    /// </summary>
    public (string WinnerDn, string LoserDn, string LoserNewDn) ResolveNameConflict(
        string dn1, string objectGuid1, long timestamp1,
        string dn2, string objectGuid2, long timestamp2)
    {
        // Determine winner: later timestamp wins, then higher GUID breaks ties
        bool firstWins;

        if (timestamp1 > timestamp2)
            firstWins = true;
        else if (timestamp2 > timestamp1)
            firstWins = false;
        else
            firstWins = string.Compare(objectGuid1, objectGuid2, StringComparison.OrdinalIgnoreCase) > 0;

        string winnerDn, loserDn, loserGuid;

        if (firstWins)
        {
            winnerDn = dn1;
            loserDn = dn2;
            loserGuid = objectGuid2;
        }
        else
        {
            winnerDn = dn2;
            loserDn = dn1;
            loserGuid = objectGuid1;
        }

        // Mangle the loser's RDN
        var loserNewDn = MangleRdn(loserDn, loserGuid);

        _logger.LogWarning(
            "Name conflict resolved: winner={Winner}, loser renamed from {LoserOld} to {LoserNew}",
            winnerDn, loserDn, loserNewDn);

        return (winnerDn, loserDn, loserNewDn);
    }

    /// <summary>
    /// Resolves a conflict between a tombstoned (deleted) object and a live modification.
    ///
    /// Per [MS-DRSR] section 4.1.10.6.4:
    /// - If the delete has a higher version/timestamp, deletion wins (object stays deleted).
    /// - If the modification has a higher version/timestamp, the object is resurrected
    ///   only if it was also modified after the delete on the originating DC.
    ///
    /// In practice, AD almost always lets the deletion win to maintain consistency.
    /// </summary>
    public TombstoneResolution ResolveTombstoneConflict(
        PropertyMetaDataEntry deleteMetadata,
        PropertyMetaDataEntry modifyMetadata,
        bool objectIsCurrentlyDeleted)
    {
        // Compare the isDeleted attribute metadata using standard LWW
        var winner = ResolveAttributeConflict(deleteMetadata, modifyMetadata);

        if (winner == deleteMetadata)
        {
            _logger.LogDebug("Tombstone conflict: deletion wins");
            return TombstoneResolution.DeletionWins;
        }

        // Modification wins — object should be resurrected if currently deleted
        if (objectIsCurrentlyDeleted)
        {
            _logger.LogWarning("Tombstone conflict: modification wins, object will be resurrected");
            return TombstoneResolution.ResurrectObject;
        }

        _logger.LogDebug("Tombstone conflict: modification wins, object already alive");
        return TombstoneResolution.ModificationWins;
    }

    /// <summary>
    /// Merges a set of replicated attribute changes into a local DirectoryObject.
    /// For each attribute, uses last-writer-wins to decide whether to apply the incoming value.
    /// Returns the list of attributes that were actually updated.
    /// </summary>
    public IReadOnlyList<uint> MergeReplicationEntries(
        DirectoryObject localObject,
        ENTINF incomingEntry,
        PROPERTY_META_DATA_EXT_VECTOR incomingMetadata,
        PropertyMetaDataVector localMetadata,
        SchemaPrefixTable prefixTable)
    {
        var updatedAttributes = new List<uint>();

        if (incomingMetadata == null)
        {
            // No metadata — apply all attributes unconditionally (initial sync)
            foreach (var attr in incomingEntry.AttrBlock.PAttr)
            {
                ApplyAttribute(localObject, attr, prefixTable);
                updatedAttributes.Add(attr.AttrTyp.Value);
            }

            return updatedAttributes;
        }

        // Match each attribute with its metadata and resolve conflicts
        for (int i = 0; i < incomingEntry.AttrBlock.PAttr.Count; i++)
        {
            var attr = incomingEntry.AttrBlock.PAttr[i];
            var attrId = attr.AttrTyp.Value;

            // Get incoming metadata for this attribute
            PropertyMetaDataEntry incomingMeta = null;
            if (i < incomingMetadata.RgMetaData.Count)
            {
                incomingMeta = PropertyMetaDataEntry.FromWireFormat(attrId, incomingMetadata.RgMetaData[i]);
            }

            // Get local metadata for this attribute
            var localMeta = localMetadata.GetEntry(attrId);

            if (localMeta == null || incomingMeta == null)
            {
                // No local metadata — accept incoming unconditionally
                ApplyAttribute(localObject, attr, prefixTable);
                if (incomingMeta != null)
                    localMetadata.SetEntry(attrId, incomingMeta);
                updatedAttributes.Add(attrId);
                continue;
            }

            // Resolve conflict
            var winner = ResolveAttributeConflict(localMeta, incomingMeta);
            if (winner == incomingMeta)
            {
                ApplyAttribute(localObject, attr, prefixTable);
                localMetadata.SetEntry(attrId, incomingMeta);
                updatedAttributes.Add(attrId);
            }
        }

        return updatedAttributes;
    }

    /// <summary>
    /// Mangles an RDN for a naming conflict loser.
    /// Format: "CN=originalValue\nCNF:objectGuid,parentDn"
    /// </summary>
    internal static string MangleRdn(string dn, string objectGuid)
    {
        // Parse the RDN and parent from the DN
        var firstComma = dn.IndexOf(',');
        string rdn, parentDn;

        if (firstComma >= 0)
        {
            rdn = dn[..firstComma];
            parentDn = dn[(firstComma + 1)..];
        }
        else
        {
            rdn = dn;
            parentDn = string.Empty;
        }

        // Extract the RDN value (after the '=')
        var eqPos = rdn.IndexOf('=');
        string rdnType, rdnValue;

        if (eqPos >= 0)
        {
            rdnType = rdn[..eqPos];
            rdnValue = rdn[(eqPos + 1)..];
        }
        else
        {
            rdnType = "CN";
            rdnValue = rdn;
        }

        // Construct mangled RDN: "CN=value\nCNF:guid"
        var mangledRdn = $"{rdnType}={rdnValue}\nCNF:{objectGuid}";

        if (!string.IsNullOrEmpty(parentDn))
            return $"{mangledRdn},{parentDn}";

        return mangledRdn;
    }

    /// <summary>
    /// Applies a single attribute value from replication to a local DirectoryObject.
    /// </summary>
    private void ApplyAttribute(DirectoryObject obj, ATTR attr, SchemaPrefixTable prefixTable)
    {
        var oid = prefixTable.AttrTypToOid(attr.AttrTyp);
        if (oid == null)
            return;

        // Map OID to attribute name — first checks the schema service, then falls back to well-known OIDs
        var attrName = ResolveOidToAttributeName(oid);
        if (attrName == null)
            return;

        var values = new List<object>();
        foreach (var val in attr.AttrVal.PVal)
        {
            if (val.PVal.Length > 0)
            {
                // Try to interpret as Unicode string; fall back to raw bytes
                try
                {
                    var str = System.Text.Encoding.Unicode.GetString(val.PVal).TrimEnd('\0');
                    values.Add(str);
                }
                catch
                {
                    values.Add(val.PVal);
                }
            }
        }

        if (values.Count > 0)
        {
            obj.SetAttribute(attrName, new DirectoryAttribute(attrName, [.. values]));
        }
    }

    /// <summary>
    /// Resolves an OID to an attribute name using the schema service with a well-known fallback map.
    /// The schema service is queried once to build a complete OID→name lookup; subsequent calls
    /// use the cached dictionary for O(1) resolution.
    /// </summary>
    private string ResolveOidToAttributeName(string oid)
    {
        if (_oidToNameCache != null)
        {
            return _oidToNameCache.TryGetValue(oid, out var cached) ? cached : null;
        }

        // Build the OID→name cache from the schema service
        var cache = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

        // Seed with well-known OIDs as a baseline
        foreach (var (wellKnownOid, name) in WellKnownOids)
            cache[wellKnownOid] = name;

        // Overlay with schema-defined attributes — covers all custom and standard attributes
        try
        {
            var allAttrs = _schemaService.GetAllAttributes();
            if (allAttrs != null)
            {
                foreach (var attr in allAttrs)
                {
                    if (!string.IsNullOrEmpty(attr.Oid) && !string.IsNullOrEmpty(attr.LdapDisplayName))
                    {
                        cache[attr.Oid] = attr.LdapDisplayName;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load schema attributes for OID resolution; using well-known OIDs only");
        }

        _oidToNameCache = cache;
        return cache.TryGetValue(oid, out var result) ? result : null;
    }

    /// <summary>
    /// Well-known OID→attribute name mappings as a fast-path fallback.
    /// </summary>
    private static readonly KeyValuePair<string, string>[] WellKnownOids =
    [
        new("2.5.4.3", "cn"),
        new("2.5.4.6", "c"),
        new("2.5.4.7", "l"),
        new("2.5.4.8", "st"),
        new("2.5.4.10", "o"),
        new("2.5.4.11", "ou"),
        new("2.5.4.4", "sn"),
        new("2.5.4.42", "givenName"),
        new("2.5.4.12", "title"),
        new("2.5.4.13", "description"),
        new("2.5.4.20", "telephoneNumber"),
        new("1.2.840.113556.1.4.221", "sAMAccountName"),
        new("1.2.840.113556.1.4.656", "userPrincipalName"),
        new("1.2.840.113556.1.4.222", "memberOf"),
        new("1.2.840.113556.1.4.223", "member"),
        new("1.2.840.113556.1.4.8", "userAccountControl"),
        new("1.2.840.113556.1.4.146", "objectSid"),
        new("1.2.840.113556.1.4.148", "objectGUID"),
        new("1.2.840.113556.1.4.220", "displayName"),
        new("1.2.840.113556.1.4.771", "servicePrincipalName"),
        new("1.2.840.113556.1.2.102", "objectClass"),
        new("0.9.2342.19200300.100.1.25", "dc"),
    ];
}

/// <summary>
/// Result of a tombstone conflict resolution.
/// </summary>
public enum TombstoneResolution
{
    /// <summary>
    /// The deletion wins — object remains deleted.
    /// </summary>
    DeletionWins,

    /// <summary>
    /// The modification wins — object should be un-deleted (resurrected).
    /// </summary>
    ResurrectObject,

    /// <summary>
    /// The modification wins and the object is already alive.
    /// </summary>
    ModificationWins,
}
