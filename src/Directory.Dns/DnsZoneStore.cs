using Directory.Core.Interfaces;
using Directory.Core.Models;
using Microsoft.Extensions.Logging;

namespace Directory.Dns;

/// <summary>
/// AD-integrated DNS zone storage per [MS-DNSP].
/// DNS records are stored as dnsNode objects in the directory under
/// CN=MicrosoftDNS,DC=DomainDnsZones,{domainDn}
///
/// Each dnsNode can hold multiple record values in its multi-valued dnsRecord
/// attribute, supporting multi-DC SRV registration where several DCs register
/// the same record name with different targets.
/// </summary>
public class DnsZoneStore
{
    private readonly IDirectoryStore _store;
    private readonly ILogger<DnsZoneStore> _logger;

    public DnsZoneStore(IDirectoryStore store, ILogger<DnsZoneStore> logger)
    {
        _store = store;
        _logger = logger;
    }

    public async Task<DnsRecord> GetRecordAsync(
        string tenantId, string zoneName, string name, DnsRecordType type, CancellationToken ct = default)
    {
        var zoneContainerDn = GetZoneContainerDn(zoneName, tenantId);
        var recordDn = $"DC={name},{zoneContainerDn}";

        var obj = await _store.GetByDnAsync(tenantId, recordDn, ct);
        if (obj == null) return null;

        var records = ParseDnsRecords(obj, type);
        return records.Count > 0 ? records[0] : null;
    }

    /// <summary>
    /// Returns ALL records for a given name and type, supporting multi-valued
    /// dnsNode attributes (e.g., multiple SRV records from different DCs).
    /// </summary>
    public async Task<IReadOnlyList<DnsRecord>> GetRecordsByNameAsync(
        string tenantId, string zoneName, string name, DnsRecordType type, CancellationToken ct = default)
    {
        var zoneContainerDn = GetZoneContainerDn(zoneName, tenantId);
        var recordDn = $"DC={name},{zoneContainerDn}";

        var obj = await _store.GetByDnAsync(tenantId, recordDn, ct);
        if (obj == null) return [];

        return ParseDnsRecords(obj, type);
    }

    public async Task<IReadOnlyList<DnsRecord>> GetAllRecordsAsync(
        string tenantId, string zoneName, DnsRecordType type, CancellationToken ct = default)
    {
        var zoneContainerDn = GetZoneContainerDn(zoneName, tenantId);
        var results = new List<DnsRecord>();

        _logger.LogDebug("GetAllRecordsAsync: zone={Zone}, type={Type}, baseDn={BaseDn}",
            zoneName, type, zoneContainerDn);

        var searchResult = await _store.SearchAsync(
            tenantId, zoneContainerDn, SearchScope.SingleLevel,
            new EqualityFilterNode("objectClass", "dnsNode"),
            null, 0, 0, null, 1000, false, ct);

        _logger.LogDebug("GetAllRecordsAsync: found {Count} dnsNode entries under {BaseDn}",
            searchResult.Entries.Count, zoneContainerDn);

        foreach (var obj in searchResult.Entries)
        {
            var records = ParseDnsRecords(obj, type);
            results.AddRange(records);
        }

        return results;
    }

    /// <summary>
    /// Upserts a DNS record. For multi-valued support (e.g., multiple SRV records
    /// from different DCs for the same name), this adds a new value to the dnsNode
    /// if the exact type+data combination does not already exist, or updates
    /// the existing value if it does.
    /// </summary>
    public async Task UpsertRecordAsync(
        string tenantId, string zoneName, DnsRecord record, CancellationToken ct = default)
    {
        var zoneContainerDn = GetZoneContainerDn(zoneName, tenantId);
        var recordDn = $"DC={record.Name},{zoneContainerDn}";

        var existing = await _store.GetByDnAsync(tenantId, recordDn, ct);
        if (existing != null)
        {
            // Check if the existing record has the correct DomainDn partition key.
            // Records created before partition key fixes may have wrong DomainDn.
            var expectedDomainDn = GetDomainDn(zoneName, tenantId);
            if (!string.Equals(existing.DomainDn, expectedDomainDn, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation(
                    "DNS record {Name} has stale DomainDn '{OldDn}', expected '{NewDn}'. Recreating.",
                    record.Name, existing.DomainDn, expectedDomainDn);

                // Delete the old record (in wrong partition) and fall through to create a new one
                await _store.DeleteAsync(tenantId, recordDn, hardDelete: true, ct);
                existing = null;
            }
            else
            {
                var attr = existing.GetAttribute("dnsRecord");
                var serialized = SerializeDnsRecord(record);

                if (attr != null)
                {
                    // Check if this exact record (same type and data) already exists
                    var existingValues = attr.Values.Select(v => v?.ToString() ?? "").ToList();
                    var matchIndex = existingValues.FindIndex(v =>
                        MatchesRecordTypeAndData(v, record.Type, record.Data));

                    if (matchIndex >= 0)
                    {
                        // Update existing value (TTL may have changed)
                        attr.Values[matchIndex] = serialized;
                    }
                    else
                    {
                        // Add new value (different DC registering same name)
                        attr.Values.Add(serialized);
                    }

                    existing.SetAttribute("dnsRecord", attr);
                }
                else
                {
                    existing.SetAttribute("dnsRecord", new DirectoryAttribute("dnsRecord", serialized));
                }

                existing.WhenChanged = DateTimeOffset.UtcNow;
                await _store.UpdateAsync(existing, ct);
            }
        }

        if (existing == null)
        {
            // Create new dnsNode object
            var obj = new DirectoryObject
            {
                Id = recordDn.ToLowerInvariant(),
                TenantId = tenantId,
                DomainDn = GetDomainDn(zoneName, tenantId),
                ObjectCategory = "dnsNode",
                DistinguishedName = recordDn,
                ObjectClass = ["top", "dnsNode"],
                Cn = record.Name,
                ParentDn = zoneContainerDn,
                WhenCreated = DateTimeOffset.UtcNow,
                WhenChanged = DateTimeOffset.UtcNow,
            };
            obj.SetAttribute("dnsRecord", new DirectoryAttribute("dnsRecord", SerializeDnsRecord(record)));
            obj.SetAttribute("dc", new DirectoryAttribute("dc", record.Name));
            await _store.CreateAsync(obj, ct);
        }

        _logger.LogDebug("Upserted DNS record: {Name} {Type} {Data} in zone {Zone}",
            record.Name, record.Type, record.Data, zoneName);
    }

    /// <summary>
    /// Deletes a specific record value from a dnsNode. If the dnsNode has
    /// multiple values and only one matches, just that value is removed.
    /// If no values remain, the entire dnsNode is deleted.
    /// </summary>
    public async Task DeleteRecordByDataAsync(
        string tenantId, string zoneName, string name, DnsRecordType type, string data, CancellationToken ct = default)
    {
        var zoneContainerDn = GetZoneContainerDn(zoneName, tenantId);
        var recordDn = $"DC={name},{zoneContainerDn}";

        var existing = await _store.GetByDnAsync(tenantId, recordDn, ct);
        if (existing == null) return;

        var attr = existing.GetAttribute("dnsRecord");
        if (attr == null) return;

        var values = attr.Values.Select(v => v?.ToString() ?? "").ToList();
        var remaining = values.Where(v => !MatchesRecordTypeAndData(v, type, data)).ToList();

        if (remaining.Count == 0)
        {
            // No values left, delete the entire node
            await _store.DeleteAsync(tenantId, recordDn, hardDelete: true, ct);
        }
        else if (remaining.Count < values.Count)
        {
            // Some values removed, update
            var newAttr = new DirectoryAttribute("dnsRecord", remaining.Cast<object>().ToArray());
            existing.SetAttribute("dnsRecord", newAttr);
            existing.WhenChanged = DateTimeOffset.UtcNow;
            await _store.UpdateAsync(existing, ct);
        }

        _logger.LogDebug("Deleted DNS record value: {Name} {Type} {Data} from zone {Zone}",
            name, type, data, zoneName);
    }

    public async Task DeleteRecordAsync(
        string tenantId, string zoneName, string name, CancellationToken ct = default)
    {
        var zoneContainerDn = GetZoneContainerDn(zoneName, tenantId);
        var recordDn = $"DC={name},{zoneContainerDn}";
        await _store.DeleteAsync(tenantId, recordDn, hardDelete: true, ct);
    }

    private static string GetZoneContainerDn(string zoneName, string tenantId)
    {
        // Zones stored under CN=MicrosoftDNS,DC=DomainDnsZones,...
        var domainDn = GetDomainDn(zoneName, tenantId);
        return $"DC={zoneName},CN=MicrosoftDNS,DC=DomainDnsZones,{domainDn}";
    }

    private static string GetDomainDn(string zoneName, string tenantId)
    {
        var parts = zoneName.Split('.');
        return string.Join(",", parts.Select(p => $"DC={p}"));
    }

    /// <summary>
    /// Parses ALL record values from a dnsNode's multi-valued dnsRecord attribute.
    /// </summary>
    private static List<DnsRecord> ParseDnsRecords(DirectoryObject obj, DnsRecordType type)
    {
        var results = new List<DnsRecord>();
        var attr = obj.GetAttribute("dnsRecord");
        if (attr == null) return results;

        foreach (var value in attr.Values)
        {
            var data = value?.ToString();
            if (data == null) continue;

            var record = ParseSingleRecord(obj.Cn ?? "", data, type);
            if (record != null) results.Add(record);
        }

        return results;
    }

    private static DnsRecord ParseSingleRecord(string cn, string data, DnsRecordType type)
    {
        // Parse stored record format: "type|ttl|data"
        var parts = data.Split('|');
        if (parts.Length < 3) return null;

        if (!Enum.TryParse<DnsRecordType>(parts[0], true, out var recordType))
            return null;

        if (type != 0 && recordType != type) return null;

        int.TryParse(parts[1], out var ttl);

        return new DnsRecord
        {
            Name = cn,
            Type = recordType,
            Ttl = ttl,
            Data = parts[2],
        };
    }

    /// <summary>
    /// Checks whether a serialized record value matches a given type and data.
    /// </summary>
    private static bool MatchesRecordTypeAndData(string serialized, DnsRecordType type, string data)
    {
        var parts = serialized.Split('|');
        if (parts.Length < 3) return false;

        if (!Enum.TryParse<DnsRecordType>(parts[0], true, out var recordType))
            return false;

        return recordType == type && parts[2].Equals(data, StringComparison.OrdinalIgnoreCase);
    }

    private static string SerializeDnsRecord(DnsRecord record)
    {
        return $"{record.Type}|{record.Ttl}|{record.Data}";
    }
}

public class DnsRecord
{
    public string Name { get; set; } = "";
    public DnsRecordType Type { get; set; }
    public int Ttl { get; set; } = 600;
    public string Data { get; set; } = "";
}

// DnsRecordType is defined in DnsPacket.cs
