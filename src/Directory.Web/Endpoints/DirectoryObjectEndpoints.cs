using Directory.Core;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Web.Models;
using Directory.Web.Services;

namespace Directory.Web.Endpoints;

public static class DirectoryObjectEndpoints
{
    public static RouteGroupBuilder MapObjectEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/search", async (
            string baseDn,
            string filter,
            string scope,
            int? pageSize,
            string continuationToken,
            IDirectoryStore store,
            INamingContextService ncService) =>
        {
            var searchBaseDn = baseDn ?? ncService.GetDomainNc().Dn;
            var searchScope = ParseScope(scope);
            var filterNode = FilterBuilder.Parse(filter);
            var (size, decodedToken) = PaginationHelper.ExtractParams(pageSize, continuationToken, maxPageSize: 1000);

            var result = await store.SearchAsync(
                DirectoryConstants.DefaultTenantId,
                searchBaseDn,
                searchScope,
                filterNode,
                null,
                pageSize: size,
                continuationToken: decodedToken);

            var items = result.Entries
                .Where(e => !e.IsDeleted)
                .Select(DashboardEndpoints.MapToSummary)
                .ToList();

            return Results.Ok(PaginationHelper.BuildResponse(items, result.ContinuationToken, size, result.TotalEstimate));
        })
        .WithName("SearchObjects")
        .WithTags("Objects");

        group.MapGet("/{guid}", async (string guid, IDirectoryStore store) =>
        {
            var obj = await store.GetByGuidAsync(DirectoryConstants.DefaultTenantId, guid);
            if (obj == null || obj.IsDeleted)
                return Results.NotFound();

            return Results.Ok(ObjectDetailDto.FromDirectoryObject(obj));
        })
        .WithName("GetObjectByGuid")
        .WithTags("Objects");

        group.MapGet("/by-dn", async (string dn, IDirectoryStore store) =>
        {
            var dnValidation = ValidationHelper.ValidateDn(dn, "dn");
            if (dnValidation != null) return dnValidation;

            var obj = await store.GetByDnAsync(DirectoryConstants.DefaultTenantId, dn);
            if (obj == null || obj.IsDeleted)
                return Results.NotFound();

            return Results.Ok(ObjectDetailDto.FromDirectoryObject(obj));
        })
        .WithName("GetObjectByDn")
        .WithTags("Objects");

        group.MapPut("/{guid}", async (string guid, UpdateObjectRequest request, IDirectoryStore store, IAuditService audit, HttpContext context) =>
        {
            if (request.Attributes.Count > 1000)
                return Results.Problem(statusCode: 400, detail: "Too many attributes in a single update (max 1000)");

            var obj = await store.GetByGuidAsync(DirectoryConstants.DefaultTenantId, guid);
            if (obj == null || obj.IsDeleted)
                return Results.NotFound();

            // Apply attribute updates
            foreach (var (key, value) in request.Attributes)
            {
                // Never allow setting sensitive credential attributes via API
                var keyLower = key.ToLowerInvariant();
                if (keyLower is "nthash" or "kerberoskeys" or "unicodepwd" or "dbcspwd" or "supplementalcredentials")
                    continue;

                if (value == null)
                {
                    // Remove the attribute
                    obj.Attributes.Remove(key);
                }
                else
                {
                    var strValue = value.ToString() ?? "";
                    obj.SetAttribute(key, new DirectoryAttribute(key, strValue));
                }
            }

            obj.WhenChanged = DateTimeOffset.UtcNow;
            await store.UpdateAsync(obj);

            await audit.LogAsync(new AuditEntry
            {
                TenantId = DirectoryConstants.DefaultTenantId,
                Action = "Update",
                TargetDn = obj.DistinguishedName,
                TargetObjectClass = obj.ObjectClass.LastOrDefault() ?? "unknown",
                SourceIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                Details = new() { ["attributeCount"] = request.Attributes.Count.ToString() }
            });

            return Results.Ok(ObjectDetailDto.FromDirectoryObject(obj));
        })
        .WithName("UpdateObject")
        .WithTags("Objects");

        group.MapDelete("/{guid}", async (string guid, IDirectoryStore store) =>
        {
            var obj = await store.GetByGuidAsync(DirectoryConstants.DefaultTenantId, guid);
            if (obj == null || obj.IsDeleted)
                return Results.NotFound();

            await store.DeleteAsync(DirectoryConstants.DefaultTenantId, obj.DistinguishedName);
            return Results.NoContent();
        })
        .WithName("DeleteObject")
        .WithTags("Objects");

        group.MapPost("/{guid}/move", async (string guid, MoveRequest request, IDirectoryStore store, IAuditService audit, HttpContext context) =>
        {
            var moveValidation = ValidationHelper.ValidateDn(request.NewParentDn, "newParentDn");
            if (moveValidation != null) return moveValidation;

            var obj = await store.GetByGuidAsync(DirectoryConstants.DefaultTenantId, guid);
            if (obj == null || obj.IsDeleted)
                return Results.NotFound();

            var oldDn = obj.DistinguishedName;
            var rdn = obj.DistinguishedName.Split(',')[0];
            var newDn = $"{rdn},{request.NewParentDn}";
            await store.MoveAsync(DirectoryConstants.DefaultTenantId, obj.DistinguishedName, newDn);

            await audit.LogAsync(new AuditEntry
            {
                TenantId = DirectoryConstants.DefaultTenantId,
                Action = "Move",
                TargetDn = newDn,
                TargetObjectClass = obj.ObjectClass.LastOrDefault() ?? "unknown",
                SourceIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                Details = new() { ["oldDn"] = oldDn, ["newDn"] = newDn }
            });

            return Results.Ok(new { NewDn = newDn });
        })
        .WithName("MoveObject")
        .WithTags("Objects");

        group.MapGet("/resolve", async (string dn, IDirectoryStore store) =>
        {
            var obj = await store.GetByDnAsync(DirectoryConstants.DefaultTenantId, dn);
            if (obj == null || obj.IsDeleted)
                return Results.NotFound();

            // Return thumbnailPhoto if present
            string thumbnailPhoto = null;
            if (obj.Attributes.TryGetValue("thumbnailPhoto", out var photoAttr))
            {
                var bytes = photoAttr.GetFirstBytes();
                if (bytes != null)
                    thumbnailPhoto = Convert.ToBase64String(bytes);
            }
            else if (obj.Attributes.TryGetValue("jpegPhoto", out var jpegAttr))
            {
                var bytes = jpegAttr.GetFirstBytes();
                if (bytes != null)
                    thumbnailPhoto = Convert.ToBase64String(bytes);
            }

            return Results.Ok(new ResolveResult(
                obj.DistinguishedName,
                obj.DisplayName ?? obj.Cn,
                obj.ObjectClass.LastOrDefault() ?? "top",
                thumbnailPhoto
            ));
        })
        .WithName("ResolveObject")
        .WithTags("Objects");

        // ── Security Descriptor Endpoints ──

        group.MapGet("/by-dn/security", async (string dn, IDirectoryStore store) =>
        {
            var obj = await store.GetByDnAsync(DirectoryConstants.DefaultTenantId, dn);
            if (obj == null || obj.IsDeleted)
                return Results.NotFound();

            if (obj.NTSecurityDescriptor == null || obj.NTSecurityDescriptor.Length == 0)
                return Results.Ok(new SecurityDescriptorInfo());

            var parser = new SecurityDescriptorParser();
            var info = parser.Parse(obj.NTSecurityDescriptor);

            // Attempt to resolve principal names from directory
            await ResolveSecurityNames(info, store);

            return Results.Ok(info);
        })
        .WithName("GetObjectSecurity")
        .WithTags("Objects", "Security");

        group.MapGet("/by-dn/effective-permissions", async (string dn, string principalDn, IDirectoryStore store) =>
        {
            var obj = await store.GetByDnAsync(DirectoryConstants.DefaultTenantId, dn);
            if (obj == null || obj.IsDeleted)
                return Results.Problem(statusCode: 404, detail: "Object not found");

            var principal = await store.GetByDnAsync(DirectoryConstants.DefaultTenantId, principalDn);
            if (principal == null || principal.IsDeleted)
                return Results.Problem(statusCode: 404, detail: "Principal not found");

            if (obj.NTSecurityDescriptor == null || obj.NTSecurityDescriptor.Length == 0)
                return Results.Ok(new { permissions = new List<string>() });

            var parser = new SecurityDescriptorParser();
            var sd = parser.Parse(obj.NTSecurityDescriptor);

            // Build set of SIDs for the principal
            var principalSids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (principal.ObjectSid != null)
                principalSids.Add(principal.ObjectSid);

            principalSids.Add("S-1-1-0"); // Everyone
            principalSids.Add("S-1-5-11"); // Authenticated Users

            foreach (var sid in principal.TokenGroupsSids)
                principalSids.Add(sid);

            var allowed = new HashSet<string>();
            var denied = new HashSet<string>();

            foreach (var ace in sd.Dacl)
            {
                if (!principalSids.Contains(ace.Principal))
                    continue;

                if (ace.Type.StartsWith("Deny", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var perm in ace.Permissions)
                        denied.Add(perm);
                }
                else if (ace.Type.StartsWith("Allow", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var perm in ace.Permissions)
                        allowed.Add(perm);
                }
            }

            allowed.ExceptWith(denied);

            return Results.Ok(new { permissions = allowed.ToList() });
        })
        .WithName("GetEffectivePermissions")
        .WithTags("Objects", "Security");

        // ── Attribute Editor Endpoints ──

        group.MapGet("/by-dn/attributes", async (
            string dn,
            string filter,
            bool? showAll,
            IDirectoryStore store,
            ISchemaService schema,
            IConstructedAttributeService constructed,
            AttributeFormatter formatter) =>
        {
            var obj = await store.GetByDnAsync(DirectoryConstants.DefaultTenantId, dn);
            if (obj == null || obj.IsDeleted)
                return Results.NotFound();

            var filterMode = filter?.ToLowerInvariant() ?? "all";
            var includeUnset = showAll == true;
            var results = new List<FormattedAttribute>();

            // Collect all attribute names present on the object
            var allAttrNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Top-level well-known properties that GetAttribute() handles
            var wellKnownNames = new[]
            {
                "distinguishedName", "objectGUID", "objectSid", "sAMAccountName",
                "userPrincipalName", "cn", "displayName", "objectClass", "objectCategory",
                "memberOf", "member", "servicePrincipalName", "userAccountControl",
                "mail", "description", "whenCreated", "whenChanged", "uSNCreated",
                "uSNChanged", "isDeleted", "pwdLastSet", "lastLogon", "badPwdCount",
                "primaryGroupId", "groupType", "nTSecurityDescriptor", "systemFlags",
                "searchFlags", "msDS-AllowedToActOnBehalfOfOtherIdentity",
                "msDS-AllowedToDelegateTo", "dNSHostName", "operatingSystem",
                "operatingSystemVersion", "givenName", "sn", "title", "department",
                "company", "manager", "gPLink", "gPOptions", "sAMAccountType",
                "badPasswordTime", "accountExpires",
            };

            foreach (var name in wellKnownNames)
            {
                var attr = obj.GetAttribute(name);
                if (attr != null && attr.Values.Count > 0)
                    allAttrNames.Add(name);
            }

            // Attributes dictionary entries
            foreach (var key in obj.Attributes.Keys)
                allAttrNames.Add(key);

            // Include constructed attributes if filter is "all" or "set"
            if (filterMode is "all" or "set")
            {
                foreach (var cName in constructed.GetConstructedAttributeNames())
                {
                    var cAttr = await constructed.ComputeAttributeAsync(cName, obj, DirectoryConstants.DefaultTenantId);
                    if (cAttr != null && cAttr.Values.Count > 0)
                        allAttrNames.Add(cName);
                }
            }

            // Determine which attributes have values (for isValueSet tracking)
            var setAttrNames = new HashSet<string>(allAttrNames, StringComparer.OrdinalIgnoreCase);

            // If showAll=true, add all schema attributes for the object's class(es)
            if (includeUnset && filterMode is "all" or "schema" or "writable")
            {
                var objectClasses = obj.ObjectClass;
                foreach (var oc in objectClasses)
                {
                    var allowed = schema.GetAllAllowedAttributes(oc);
                    foreach (var a in allowed)
                        allAttrNames.Add(a);
                }
            }

            // Determine required attributes for the object's class
            var requiredAttrs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (includeUnset)
            {
                foreach (var oc in obj.ObjectClass)
                {
                    var req = schema.GetAllRequiredAttributes(oc);
                    foreach (var r in req)
                        requiredAttrs.Add(r);
                }
            }

            foreach (var attrName in allAttrNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                // Never expose sensitive attributes
                if (AttributeFormatter.IsSensitiveAttribute(attrName))
                    continue;

                var schemaDef = schema.GetAttribute(attrName);

                // Apply filter
                switch (filterMode)
                {
                    case "writable":
                        if (schemaDef == null || schemaDef.IsSystemOnly || constructed.IsConstructedAttribute(attrName))
                            continue;
                        break;
                    case "backlink":
                        // Backlink attributes have even LinkId values
                        if (schemaDef?.LinkId == null || schemaDef.LinkId % 2 != 0)
                            continue;
                        break;
                    case "set":
                        if (!setAttrNames.Contains(attrName))
                            continue;
                        break;
                    case "all":
                    default:
                        break;
                }

                // Get the attribute value (try constructed first, then regular)
                DirectoryAttribute attr = null;
                if (constructed.IsConstructedAttribute(attrName))
                    attr = await constructed.ComputeAttributeAsync(attrName, obj, DirectoryConstants.DefaultTenantId);
                attr ??= obj.GetAttribute(attrName);

                bool hasValue = attr != null && attr.Values.Count > 0;

                if (!hasValue && !includeUnset)
                    continue;

                if (hasValue)
                {
                    var formatted = formatter.FormatAttribute(attrName, attr);
                    formatted.IsValueSet = true;
                    formatted.IsMustContain = requiredAttrs.Contains(attrName);
                    formatted.RangeLower = schemaDef?.RangeLower;
                    formatted.RangeUpper = schemaDef?.RangeUpper;
                    results.Add(formatted);
                }
                else
                {
                    // Unset attribute — include schema metadata with no values
                    results.Add(formatter.FormatSchemaAttribute(attrName, schemaDef, requiredAttrs.Contains(attrName)));
                }
            }

            return Results.Ok(results);
        })
        .WithName("GetObjectAttributes")
        .WithTags("Objects", "Attributes");

        group.MapPut("/by-dn/attributes/{attributeName}", async (
            string dn,
            string attributeName,
            AttributeValuesRequest request,
            IDirectoryStore store,
            ISchemaService schema,
            IConstructedAttributeService constructed) =>
        {
            var dnValidation = ValidationHelper.ValidateDn(dn, "dn");
            if (dnValidation != null) return dnValidation;

            var obj = await store.GetByDnAsync(DirectoryConstants.DefaultTenantId, dn);
            if (obj == null || obj.IsDeleted)
                return Results.NotFound();

            // Block sensitive attributes
            if (AttributeFormatter.IsSensitiveAttribute(attributeName))
                return Results.Problem(statusCode: 400, detail: $"Cannot modify sensitive attribute: {attributeName}");

            // Block system-only attributes
            var schemaDef = schema.GetAttribute(attributeName);
            if (schemaDef is { IsSystemOnly: true })
                return Results.Problem(statusCode: 400, detail: $"Cannot modify system-only attribute: {attributeName}");

            // Block constructed attributes
            if (constructed.IsConstructedAttribute(attributeName))
                return Results.Problem(statusCode: 400, detail: $"Cannot modify constructed attribute: {attributeName}");

            // Validate single-valued constraint
            if (schemaDef is { IsSingleValued: true } && request.Values.Count > 1)
                return Results.Problem(statusCode: 400, detail: $"Attribute {attributeName} is single-valued but {request.Values.Count} values were provided");

            // Set the attribute
            var values = request.Values.Select(v => (object)v).ToList();
            obj.SetAttribute(attributeName, new DirectoryAttribute(attributeName, [.. values]));

            obj.WhenChanged = DateTimeOffset.UtcNow;
            await store.UpdateAsync(obj);

            return Results.Ok(ObjectDetailDto.FromDirectoryObject(obj));
        })
        .WithName("SetAttributeValues")
        .WithTags("Objects", "Attributes");

        group.MapDelete("/by-dn/attributes/{attributeName}", async (
            string dn,
            string attributeName,
            IDirectoryStore store,
            ISchemaService schema,
            IConstructedAttributeService constructed) =>
        {
            var obj = await store.GetByDnAsync(DirectoryConstants.DefaultTenantId, dn);
            if (obj == null || obj.IsDeleted)
                return Results.NotFound();

            // Block sensitive attributes
            if (AttributeFormatter.IsSensitiveAttribute(attributeName))
                return Results.Problem(statusCode: 400, detail: $"Cannot modify sensitive attribute: {attributeName}");

            // Block system-only attributes
            var schemaDef = schema.GetAttribute(attributeName);
            if (schemaDef is { IsSystemOnly: true })
                return Results.Problem(statusCode: 400, detail: $"Cannot clear system-only attribute: {attributeName}");

            // Block constructed attributes
            if (constructed.IsConstructedAttribute(attributeName))
                return Results.Problem(statusCode: 400, detail: $"Cannot clear constructed attribute: {attributeName}");

            // Clear the attribute
            obj.SetAttribute(attributeName, new DirectoryAttribute(attributeName));
            obj.Attributes.Remove(attributeName);

            obj.WhenChanged = DateTimeOffset.UtcNow;
            await store.UpdateAsync(obj);

            return Results.NoContent();
        })
        .WithName("ClearAttribute")
        .WithTags("Objects", "Attributes");

        group.MapPost("/by-dn/attributes/{attributeName}/values", async (
            string dn,
            string attributeName,
            AttributeValuesRequest request,
            IDirectoryStore store,
            ISchemaService schema,
            IConstructedAttributeService constructed) =>
        {
            var obj = await store.GetByDnAsync(DirectoryConstants.DefaultTenantId, dn);
            if (obj == null || obj.IsDeleted)
                return Results.NotFound();

            // Block sensitive attributes
            if (AttributeFormatter.IsSensitiveAttribute(attributeName))
                return Results.Problem(statusCode: 400, detail: $"Cannot modify sensitive attribute: {attributeName}");

            // Block system-only attributes
            var schemaDef = schema.GetAttribute(attributeName);
            if (schemaDef is { IsSystemOnly: true })
                return Results.Problem(statusCode: 400, detail: $"Cannot modify system-only attribute: {attributeName}");

            if (constructed.IsConstructedAttribute(attributeName))
                return Results.Problem(statusCode: 400, detail: $"Cannot modify constructed attribute: {attributeName}");

            // Must be multi-valued
            if (schemaDef is { IsSingleValued: true })
                return Results.Problem(statusCode: 400, detail: $"Cannot add values to single-valued attribute: {attributeName}");

            // Get existing values and add new ones
            var existing = obj.GetAttribute(attributeName);
            var currentValues = existing?.Values.Select(v => v?.ToString() ?? "").ToList() ?? [];

            foreach (var newVal in request.Values)
            {
                if (!currentValues.Contains(newVal, StringComparer.OrdinalIgnoreCase))
                    currentValues.Add(newVal);
            }

            obj.SetAttribute(attributeName, new DirectoryAttribute(attributeName, [.. currentValues.Cast<object>()]));

            obj.WhenChanged = DateTimeOffset.UtcNow;
            await store.UpdateAsync(obj);

            return Results.Ok(ObjectDetailDto.FromDirectoryObject(obj));
        })
        .WithName("AddAttributeValues")
        .WithTags("Objects", "Attributes");

        group.MapDelete("/by-dn/attributes/{attributeName}/values", async (
            string dn,
            string attributeName,
            [Microsoft.AspNetCore.Mvc.FromBody] AttributeValuesRequest request,
            IDirectoryStore store,
            ISchemaService schema,
            IConstructedAttributeService constructed) =>
        {
            var obj = await store.GetByDnAsync(DirectoryConstants.DefaultTenantId, dn);
            if (obj == null || obj.IsDeleted)
                return Results.NotFound();

            // Block sensitive attributes
            if (AttributeFormatter.IsSensitiveAttribute(attributeName))
                return Results.Problem(statusCode: 400, detail: $"Cannot modify sensitive attribute: {attributeName}");

            // Block system-only attributes
            var schemaDef = schema.GetAttribute(attributeName);
            if (schemaDef is { IsSystemOnly: true })
                return Results.Problem(statusCode: 400, detail: $"Cannot clear system-only attribute: {attributeName}");

            // Block constructed attributes
            if (constructed.IsConstructedAttribute(attributeName))
                return Results.Problem(statusCode: 400, detail: $"Cannot clear constructed attribute: {attributeName}");

            // Get existing values and remove specified ones
            var existing = obj.GetAttribute(attributeName);
            if (existing == null || existing.Values.Count == 0)
                return Results.Problem(statusCode: 404, detail: $"Attribute {attributeName} has no values");

            var remainingValues = existing.Values
                .Where(v => !request.Values.Contains(v?.ToString() ?? "", StringComparer.OrdinalIgnoreCase))
                .ToList();

            if (remainingValues.Count == 0)
            {
                // All values removed — clear the attribute
                obj.SetAttribute(attributeName, new DirectoryAttribute(attributeName));
                obj.Attributes.Remove(attributeName);
            }
            else
            {
                obj.SetAttribute(attributeName, new DirectoryAttribute(attributeName, [.. remainingValues]));
            }

            obj.WhenChanged = DateTimeOffset.UtcNow;
            await store.UpdateAsync(obj);

            return Results.Ok(ObjectDetailDto.FromDirectoryObject(obj));
        })
        .WithName("RemoveAttributeValues")
        .WithTags("Objects", "Attributes");

        // ── Security Editing Endpoints ──

        group.MapPut("/by-dn/security/owner", async (
            [Microsoft.AspNetCore.Mvc.FromBody] UpdateOwnerRequest request,
            IDirectoryStore store) =>
        {
            var obj = await store.GetByDnAsync(DirectoryConstants.DefaultTenantId, request.Dn);
            if (obj == null || obj.IsDeleted)
                return Results.Problem(statusCode: 404, detail: "Object not found");

            if (obj.NTSecurityDescriptor == null || obj.NTSecurityDescriptor.Length == 0)
                return Results.Problem(statusCode: 400, detail: "Object has no security descriptor");

            try
            {
                obj.NTSecurityDescriptor = SecurityDescriptorBuilder.ReplaceOwner(obj.NTSecurityDescriptor, request.OwnerSid);
                obj.WhenChanged = DateTimeOffset.UtcNow;
                await store.UpdateAsync(obj);

                var parser = new SecurityDescriptorParser();
                var info = parser.Parse(obj.NTSecurityDescriptor);
                await ResolveSecurityNames(info, store);
                return Results.Ok(info);
            }
            catch (Exception ex)
            {
                return Results.Problem(statusCode: 400, detail: ex.Message);
            }
        })
        .WithName("UpdateSecurityOwner")
        .WithTags("Objects", "Security");

        group.MapPut("/by-dn/security/dacl", async (
            [Microsoft.AspNetCore.Mvc.FromBody] UpdateDaclRequest request,
            IDirectoryStore store) =>
        {
            var obj = await store.GetByDnAsync(DirectoryConstants.DefaultTenantId, request.Dn);
            if (obj == null || obj.IsDeleted)
                return Results.Problem(statusCode: 404, detail: "Object not found");

            if (obj.NTSecurityDescriptor == null || obj.NTSecurityDescriptor.Length == 0)
                return Results.Problem(statusCode: 400, detail: "Object has no security descriptor");

            try
            {
                obj.NTSecurityDescriptor = SecurityDescriptorBuilder.ReplaceDacl(obj.NTSecurityDescriptor, request.Aces);
                obj.WhenChanged = DateTimeOffset.UtcNow;
                await store.UpdateAsync(obj);

                var parser = new SecurityDescriptorParser();
                var info = parser.Parse(obj.NTSecurityDescriptor);
                await ResolveSecurityNames(info, store);
                return Results.Ok(info);
            }
            catch (Exception ex)
            {
                return Results.Problem(statusCode: 400, detail: ex.Message);
            }
        })
        .WithName("UpdateSecurityDacl")
        .WithTags("Objects", "Security");

        group.MapPost("/by-dn/security/dacl/ace", async (
            [Microsoft.AspNetCore.Mvc.FromBody] AddAceRequest request,
            IDirectoryStore store) =>
        {
            var obj = await store.GetByDnAsync(DirectoryConstants.DefaultTenantId, request.Dn);
            if (obj == null || obj.IsDeleted)
                return Results.Problem(statusCode: 404, detail: "Object not found");

            if (obj.NTSecurityDescriptor == null || obj.NTSecurityDescriptor.Length == 0)
                return Results.Problem(statusCode: 400, detail: "Object has no security descriptor");

            try
            {
                obj.NTSecurityDescriptor = SecurityDescriptorBuilder.AddDaclAce(obj.NTSecurityDescriptor, request.Ace);
                obj.WhenChanged = DateTimeOffset.UtcNow;
                await store.UpdateAsync(obj);

                var parser = new SecurityDescriptorParser();
                var info = parser.Parse(obj.NTSecurityDescriptor);
                await ResolveSecurityNames(info, store);
                return Results.Ok(info);
            }
            catch (Exception ex)
            {
                return Results.Problem(statusCode: 400, detail: ex.Message);
            }
        })
        .WithName("AddSecurityDaclAce")
        .WithTags("Objects", "Security");

        group.MapDelete("/by-dn/security/dacl/ace", async (
            [Microsoft.AspNetCore.Mvc.FromBody] RemoveAceRequest request,
            IDirectoryStore store) =>
        {
            var obj = await store.GetByDnAsync(DirectoryConstants.DefaultTenantId, request.Dn);
            if (obj == null || obj.IsDeleted)
                return Results.Problem(statusCode: 404, detail: "Object not found");

            if (obj.NTSecurityDescriptor == null || obj.NTSecurityDescriptor.Length == 0)
                return Results.Problem(statusCode: 400, detail: "Object has no security descriptor");

            try
            {
                obj.NTSecurityDescriptor = SecurityDescriptorBuilder.RemoveDaclAce(obj.NTSecurityDescriptor, request.AceIndex);
                obj.WhenChanged = DateTimeOffset.UtcNow;
                await store.UpdateAsync(obj);

                var parser = new SecurityDescriptorParser();
                var info = parser.Parse(obj.NTSecurityDescriptor);
                await ResolveSecurityNames(info, store);
                return Results.Ok(info);
            }
            catch (Exception ex)
            {
                return Results.Problem(statusCode: 400, detail: ex.Message);
            }
        })
        .WithName("RemoveSecurityDaclAce")
        .WithTags("Objects", "Security");

        group.MapPut("/by-dn/security/inherit", async (
            [Microsoft.AspNetCore.Mvc.FromBody] SetInheritanceRequest request,
            IDirectoryStore store) =>
        {
            var obj = await store.GetByDnAsync(DirectoryConstants.DefaultTenantId, request.Dn);
            if (obj == null || obj.IsDeleted)
                return Results.Problem(statusCode: 404, detail: "Object not found");

            if (obj.NTSecurityDescriptor == null || obj.NTSecurityDescriptor.Length == 0)
                return Results.Problem(statusCode: 400, detail: "Object has no security descriptor");

            try
            {
                obj.NTSecurityDescriptor = SecurityDescriptorBuilder.SetInheritance(obj.NTSecurityDescriptor, request.Enabled);
                obj.WhenChanged = DateTimeOffset.UtcNow;
                await store.UpdateAsync(obj);

                var parser = new SecurityDescriptorParser();
                var info = parser.Parse(obj.NTSecurityDescriptor);
                await ResolveSecurityNames(info, store);
                return Results.Ok(info);
            }
            catch (Exception ex)
            {
                return Results.Problem(statusCode: 400, detail: ex.Message);
            }
        })
        .WithName("SetSecurityInheritance")
        .WithTags("Objects", "Security");

        group.MapPost("/by-dn/security/propagate", async (
            [Microsoft.AspNetCore.Mvc.FromBody] PropagateInheritanceRequest request,
            IDirectoryStore store) =>
        {
            var obj = await store.GetByDnAsync(DirectoryConstants.DefaultTenantId, request.Dn);
            if (obj == null || obj.IsDeleted)
                return Results.Problem(statusCode: 404, detail: "Object not found");

            if (obj.NTSecurityDescriptor == null || obj.NTSecurityDescriptor.Length == 0)
                return Results.Problem(statusCode: 400, detail: "Object has no security descriptor");

            try
            {
                var parser = new SecurityDescriptorParser();
                var parentSd = parser.Parse(obj.NTSecurityDescriptor);

                // Find inheritable ACEs from parent's DACL
                var inheritableAces = parentSd.Dacl
                    .Where(a => a.Flags.Contains("CONTAINER_INHERIT") || a.Flags.Contains("OBJECT_INHERIT"))
                    .ToList();

                // Search for direct children
                var childFilter = new EqualityFilterNode("objectClass", "*");
                var children = await store.SearchAsync(DirectoryConstants.DefaultTenantId, request.Dn, SearchScope.SingleLevel, childFilter, null, sizeLimit: 10000);

                int propagated = 0;
                foreach (var child in children.Entries.Where(e => !e.IsDeleted))
                {
                    if (child.NTSecurityDescriptor == null || child.NTSecurityDescriptor.Length == 0)
                        continue;

                    var childSd = parser.Parse(child.NTSecurityDescriptor);

                    // Skip if child has DaclProtected
                    if (childSd.Control.HasFlag(SecurityDescriptorControl.DaclProtected))
                        continue;

                    // Rebuild child DACL: keep explicit ACEs, replace inherited ACEs from parent
                    var explicitAces = childSd.Dacl.Where(a => !a.IsInherited).ToList();
                    var newInheritedAces = inheritableAces.Select(a => new AceDto
                    {
                        Type = a.Type.StartsWith("Deny", StringComparison.OrdinalIgnoreCase) ? "deny" : "allow",
                        PrincipalSid = a.Principal,
                        AccessMask = Convert.ToUInt32(a.AccessMask, 16),
                        Flags = new List<string>(a.Flags) { "INHERITED_ACE" },
                        ObjectType = a.ObjectType,
                        InheritedObjectType = a.InheritedObjectType,
                        IsObjectAce = a.ObjectType != null || a.InheritedObjectType != null,
                    }).ToList();

                    var explicitDtos = explicitAces.Select(a => new AceDto
                    {
                        Type = a.Type.StartsWith("Deny", StringComparison.OrdinalIgnoreCase) ? "deny" : "allow",
                        PrincipalSid = a.Principal,
                        AccessMask = Convert.ToUInt32(a.AccessMask, 16),
                        Flags = new List<string>(a.Flags),
                        ObjectType = a.ObjectType,
                        InheritedObjectType = a.InheritedObjectType,
                        IsObjectAce = a.ObjectType != null || a.InheritedObjectType != null,
                    }).ToList();

                    var combinedAces = new List<AceDto>();
                    combinedAces.AddRange(newInheritedAces);
                    combinedAces.AddRange(explicitDtos);

                    child.NTSecurityDescriptor = SecurityDescriptorBuilder.ReplaceDacl(child.NTSecurityDescriptor, combinedAces);
                    child.WhenChanged = DateTimeOffset.UtcNow;
                    await store.UpdateAsync(child);
                    propagated++;
                }

                return Results.Ok(new { Propagated = propagated });
            }
            catch (Exception ex)
            {
                return Results.Problem(statusCode: 400, detail: ex.Message);
            }
        })
        .WithName("PropagateSecurityInheritance")
        .WithTags("Objects", "Security");

        return group;
    }

    private static async Task ResolveSecurityNames(SecurityDescriptorInfo info, IDirectoryStore store)
    {
        var sidsToResolve = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(info.Owner) && info.OwnerName == null)
            sidsToResolve.Add(info.Owner);
        if (!string.IsNullOrEmpty(info.Group) && info.GroupName == null)
            sidsToResolve.Add(info.Group);

        foreach (var ace in info.Dacl.Concat(info.Sacl))
        {
            if (!string.IsNullOrEmpty(ace.Principal) && ace.PrincipalName == null)
                sidsToResolve.Add(ace.Principal);
        }

        // Search directory for objects matching these SIDs
        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sid in sidsToResolve)
        {
            try
            {
                var filter = new EqualityFilterNode("objectSid", sid);
                var result = await store.SearchAsync(DirectoryConstants.DefaultTenantId, "", SearchScope.WholeSubtree, filter, null, sizeLimit: 1);
                var match = result.Entries.FirstOrDefault();
                if (match != null)
                    resolved[sid] = match.DisplayName ?? match.Cn ?? match.SAMAccountName ?? match.DistinguishedName;
            }
            catch
            {
                // Best-effort resolution
            }
        }

        if (resolved.TryGetValue(info.Owner, out var ownerName))
            info.OwnerName = ownerName;
        if (resolved.TryGetValue(info.Group, out var groupName))
            info.GroupName = groupName;

        foreach (var ace in info.Dacl.Concat(info.Sacl))
        {
            if (ace.PrincipalName == null && resolved.TryGetValue(ace.Principal, out var name))
                ace.PrincipalName = name;
        }
    }

    private static SearchScope ParseScope(string scope) => scope?.ToLowerInvariant() switch
    {
        "base" or "baseobject" => SearchScope.BaseObject,
        "one" or "singlelevel" => SearchScope.SingleLevel,
        "sub" or "wholesubtree" => SearchScope.WholeSubtree,
        _ => SearchScope.WholeSubtree
    };
}

public record MoveRequest(string NewParentDn);
public record ResolveResult(string Dn, string DisplayName, string ObjectClass, string ThumbnailPhoto);
public record AttributeValuesRequest(List<string> Values);

// Security editing request types
public record UpdateOwnerRequest(string Dn, string OwnerSid);
public record UpdateDaclRequest(string Dn, List<AceDto> Aces);
public record AddAceRequest(string Dn, AceDto Ace);
public record RemoveAceRequest(string Dn, int AceIndex);
public record SetInheritanceRequest(string Dn, bool Enabled);
public record PropagateInheritanceRequest(string Dn);
