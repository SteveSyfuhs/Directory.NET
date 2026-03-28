using Directory.Core.Interfaces;
using Directory.Core.Ldif;
using Directory.Core.Models;

namespace Directory.Web.Services;

/// <summary>
/// Service for importing and exporting directory data in LDIF format.
/// </summary>
public class LdifService
{
    private readonly IDirectoryStore _store;
    private readonly INamingContextService _ncService;

    /// <summary>
    /// Sensitive attributes that must never be imported.
    /// </summary>
    private static readonly HashSet<string> SensitiveAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "nthash", "kerberoskeys", "unicodepwd", "dbcspwd", "supplementalcredentials"
    };

    public LdifService(IDirectoryStore store, INamingContextService ncService)
    {
        _store = store;
        _ncService = ncService;
    }

    /// <summary>
    /// Export directory objects as LDIF content records.
    /// </summary>
    public async Task<string> ExportAsync(LdifExportOptions options)
    {
        var searchBase = string.IsNullOrWhiteSpace(options.BaseDn)
            ? _ncService.GetDomainNc().Dn
            : options.BaseDn;

        var filterExpr = string.IsNullOrWhiteSpace(options.Filter)
            ? "(objectClass=*)"
            : options.Filter;

        var filterNode = FilterBuilder.Parse(filterExpr);

        string[] attrs = options.Attributes?.Count > 0
            ? options.Attributes.ToArray()
            : null;

        var result = await _store.SearchAsync(
            "default",
            searchBase,
            options.Scope,
            filterNode,
            attributes: attrs,
            sizeLimit: 50000,
            pageSize: 10000);

        var entries = result.Entries.Where(e => !e.IsDeleted).ToList();
        return LdifWriter.WriteContentRecords(entries);
    }

    /// <summary>
    /// Import an LDIF file, processing each record independently.
    /// If dryRun is true, validates without persisting changes.
    /// </summary>
    public async Task<LdifImportResult> ImportAsync(Stream ldifStream, bool dryRun)
    {
        var records = await LdifReader.ParseAsync(ldifStream);
        var result = new LdifImportResult { TotalRecords = records.Count };

        foreach (var record in records)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(record.Dn))
                {
                    result.Failed++;
                    result.Errors.Add("Record with empty DN skipped.");
                    continue;
                }

                switch (record.ChangeType)
                {
                    case LdifChangeType.Content:
                    case LdifChangeType.Add:
                        await ProcessAddRecord(record, dryRun, result);
                        break;

                    case LdifChangeType.Modify:
                        await ProcessModifyRecord(record, dryRun, result);
                        break;

                    case LdifChangeType.Delete:
                        await ProcessDeleteRecord(record, dryRun, result);
                        break;

                    case LdifChangeType.ModDn:
                        await ProcessModDnRecord(record, dryRun, result);
                        break;

                    default:
                        result.Skipped++;
                        break;
                }
            }
            catch (Exception ex)
            {
                result.Failed++;
                result.Errors.Add($"{record.Dn}: {ex.Message}");
            }
        }

        return result;
    }

    private async Task ProcessAddRecord(LdifRecord record, bool dryRun, LdifImportResult result)
    {
        // Check if the object already exists
        var existing = await _store.GetByDnAsync("default", record.Dn);

        var obj = existing ?? new DirectoryObject();
        obj.DistinguishedName = record.Dn;
        obj.Id = record.Dn.ToLowerInvariant();
        obj.TenantId = "default";

        // Compute parent DN
        var commaIndex = record.Dn.IndexOf(',');
        obj.ParentDn = commaIndex >= 0 ? record.Dn[(commaIndex + 1)..] : string.Empty;

        // Compute domain DN from the DN components
        var dnParts = record.Dn.Split(',');
        var dcParts = dnParts.Where(p => p.TrimStart().StartsWith("DC=", StringComparison.OrdinalIgnoreCase));
        obj.DomainDn = string.Join(",", dcParts);

        if (string.IsNullOrEmpty(obj.DomainDn))
            obj.DomainDn = _ncService.GetDomainNc().Dn;

        // Apply attributes from the record
        foreach (var kvp in record.Attributes)
        {
            if (SensitiveAttributes.Contains(kvp.Key))
                continue;

            foreach (var val in kvp.Value)
            {
                ApplyAttribute(obj, kvp.Key, val);
            }
        }

        if (!dryRun)
        {
            if (existing != null)
            {
                obj.WhenChanged = DateTimeOffset.UtcNow;
                obj.ETag = existing.ETag;
                await _store.UpdateAsync(obj);
            }
            else
            {
                obj.WhenCreated = DateTimeOffset.UtcNow;
                obj.WhenChanged = DateTimeOffset.UtcNow;
                await _store.CreateAsync(obj);
            }
        }

        result.Imported++;
    }

    private async Task ProcessModifyRecord(LdifRecord record, bool dryRun, LdifImportResult result)
    {
        var obj = await _store.GetByDnAsync("default", record.Dn);
        if (obj == null || obj.IsDeleted)
        {
            result.Failed++;
            result.Errors.Add($"{record.Dn}: Object not found for modification.");
            return;
        }

        if (record.Modifications == null || record.Modifications.Count == 0)
        {
            result.Skipped++;
            return;
        }

        foreach (var mod in record.Modifications)
        {
            if (SensitiveAttributes.Contains(mod.AttributeName))
                continue;

            switch (mod.Operation)
            {
                case "add":
                    foreach (var val in mod.Values)
                        ApplyAttribute(obj, mod.AttributeName, val);
                    break;

                case "replace":
                    // Clear existing then set new values
                    ClearAttribute(obj, mod.AttributeName);
                    foreach (var val in mod.Values)
                        ApplyAttribute(obj, mod.AttributeName, val);
                    break;

                case "delete":
                    if (mod.Values.Count == 0)
                    {
                        ClearAttribute(obj, mod.AttributeName);
                    }
                    else
                    {
                        RemoveAttributeValues(obj, mod.AttributeName, mod.Values);
                    }
                    break;
            }
        }

        if (!dryRun)
        {
            obj.WhenChanged = DateTimeOffset.UtcNow;
            await _store.UpdateAsync(obj);
        }

        result.Imported++;
    }

    private async Task ProcessDeleteRecord(LdifRecord record, bool dryRun, LdifImportResult result)
    {
        var obj = await _store.GetByDnAsync("default", record.Dn);
        if (obj == null)
        {
            result.Skipped++;
            return;
        }

        if (!dryRun)
        {
            await _store.DeleteAsync("default", record.Dn);
        }

        result.Imported++;
    }

    private async Task ProcessModDnRecord(LdifRecord record, bool dryRun, LdifImportResult result)
    {
        if (string.IsNullOrEmpty(record.NewRdn))
        {
            result.Failed++;
            result.Errors.Add($"{record.Dn}: ModDN record missing newrdn.");
            return;
        }

        var obj = await _store.GetByDnAsync("default", record.Dn);
        if (obj == null || obj.IsDeleted)
        {
            result.Failed++;
            result.Errors.Add($"{record.Dn}: Object not found for rename.");
            return;
        }

        // Build new DN
        string newDn;
        if (!string.IsNullOrEmpty(record.NewSuperior))
        {
            newDn = $"{record.NewRdn},{record.NewSuperior}";
        }
        else
        {
            var commaIndex = record.Dn.IndexOf(',');
            var parentDn = commaIndex >= 0 ? record.Dn[(commaIndex + 1)..] : string.Empty;
            newDn = $"{record.NewRdn},{parentDn}";
        }

        if (!dryRun)
        {
            await _store.MoveAsync("default", record.Dn, newDn);
        }

        result.Imported++;
    }

    /// <summary>
    /// Apply a single attribute value to a DirectoryObject, handling well-known properties.
    /// </summary>
    private static void ApplyAttribute(DirectoryObject obj, string attrName, string value)
    {
        var lower = attrName.ToLowerInvariant();

        // Handle objectClass as a special multi-valued property
        if (lower == "objectclass")
        {
            if (!obj.ObjectClass.Contains(value, StringComparer.OrdinalIgnoreCase))
                obj.ObjectClass.Add(value);
            return;
        }

        // Handle member/memberof as multi-valued
        if (lower == "memberof")
        {
            if (!obj.MemberOf.Contains(value, StringComparer.OrdinalIgnoreCase))
                obj.MemberOf.Add(value);
            return;
        }

        if (lower == "member")
        {
            if (!obj.Member.Contains(value, StringComparer.OrdinalIgnoreCase))
                obj.Member.Add(value);
            return;
        }

        if (lower == "serviceprincipalname")
        {
            if (!obj.ServicePrincipalName.Contains(value, StringComparer.OrdinalIgnoreCase))
                obj.ServicePrincipalName.Add(value);
            return;
        }

        // Use SetAttribute for everything else (handles well-known props and the Attributes dict)
        obj.SetAttribute(attrName, new DirectoryAttribute(attrName, value));
    }

    /// <summary>
    /// Clear an attribute from a DirectoryObject.
    /// </summary>
    private static void ClearAttribute(DirectoryObject obj, string attrName)
    {
        var lower = attrName.ToLowerInvariant();

        switch (lower)
        {
            case "objectclass": obj.ObjectClass.Clear(); break;
            case "memberof": obj.MemberOf.Clear(); break;
            case "member": obj.Member.Clear(); break;
            case "serviceprincipalname": obj.ServicePrincipalName.Clear(); break;
            default:
                obj.SetAttribute(attrName, new DirectoryAttribute(attrName));
                obj.Attributes.Remove(attrName);
                break;
        }
    }

    /// <summary>
    /// Remove specific values from a multi-valued attribute.
    /// </summary>
    private static void RemoveAttributeValues(DirectoryObject obj, string attrName, List<string> values)
    {
        var lower = attrName.ToLowerInvariant();
        var removeSet = new HashSet<string>(values, StringComparer.OrdinalIgnoreCase);

        switch (lower)
        {
            case "objectclass":
                obj.ObjectClass.RemoveAll(v => removeSet.Contains(v));
                break;
            case "memberof":
                obj.MemberOf.RemoveAll(v => removeSet.Contains(v));
                break;
            case "member":
                obj.Member.RemoveAll(v => removeSet.Contains(v));
                break;
            case "serviceprincipalname":
                obj.ServicePrincipalName.RemoveAll(v => removeSet.Contains(v));
                break;
            default:
                var existing = obj.GetAttribute(attrName);
                if (existing != null)
                {
                    var remaining = existing.Values.Where(v => !removeSet.Contains(v?.ToString() ?? "")).ToList();
                    if (remaining.Count == 0)
                    {
                        obj.Attributes.Remove(attrName);
                    }
                    else
                    {
                        obj.SetAttribute(attrName, new DirectoryAttribute(attrName, [.. remaining]));
                    }
                }
                break;
        }
    }
}
