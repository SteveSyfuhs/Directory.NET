using Directory.Core;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Web.Models;

namespace Directory.Web.Endpoints;

public static class BulkEndpoints
{
    public static RouteGroupBuilder MapBulkEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/modify", async (BulkModifyRequest request, IDirectoryStore store, IAuditService audit, HttpContext context) =>
        {
            if (request.Dns == null || request.Dns.Count == 0)
                return Results.Problem(statusCode: 400, detail: "dns is required and must not be empty");
            if (request.Dns.Count > 1000)
                return Results.Problem(statusCode: 400, detail: "dns exceeds maximum batch size of 1000");
            if (request.Modifications == null || request.Modifications.Count == 0)
                return Results.Problem(statusCode: 400, detail: "modifications is required and must not be empty");

            var results = new List<BulkOperationResult>();

            foreach (var dn in request.Dns)
            {
                try
                {
                    var obj = await store.GetByDnAsync(DirectoryConstants.DefaultTenantId, dn);
                    if (obj == null || obj.IsDeleted)
                    {
                        results.Add(new BulkOperationResult(dn, false, "Object not found"));
                        continue;
                    }

                    foreach (var mod in request.Modifications)
                    {
                        var attrLower = mod.Attribute.ToLowerInvariant();
                        if (attrLower is "nthash" or "kerberoskeys" or "unicodepwd" or "dbcspwd" or "supplementalcredentials")
                            continue;

                        switch (mod.Operation.ToLowerInvariant())
                        {
                            case "set":
                                if (mod.Values is { Count: > 0 })
                                {
                                    var vals = mod.Values.Select(v => (object)v.ToString()).ToList();
                                    obj.SetAttribute(mod.Attribute, new DirectoryAttribute(mod.Attribute, [.. vals]));
                                }
                                break;

                            case "add":
                                if (mod.Values is { Count: > 0 })
                                {
                                    var existing = obj.GetAttribute(mod.Attribute);
                                    var current = existing?.Values.Select(v => v?.ToString() ?? "").ToList() ?? [];
                                    foreach (var v in mod.Values)
                                    {
                                        var s = v.ToString();
                                        if (!current.Contains(s, StringComparer.OrdinalIgnoreCase))
                                            current.Add(s);
                                    }
                                    obj.SetAttribute(mod.Attribute, new DirectoryAttribute(mod.Attribute, [.. current.Cast<object>()]));
                                }
                                break;

                            case "remove":
                                if (mod.Values is { Count: > 0 })
                                {
                                    var existingAttr = obj.GetAttribute(mod.Attribute);
                                    if (existingAttr != null)
                                    {
                                        var removeSet = new HashSet<string>(mod.Values.Select(v => v.ToString()), StringComparer.OrdinalIgnoreCase);
                                        var remaining = existingAttr.Values.Where(v => !removeSet.Contains(v?.ToString() ?? "")).ToList();
                                        if (remaining.Count == 0)
                                        {
                                            obj.SetAttribute(mod.Attribute, new DirectoryAttribute(mod.Attribute));
                                            obj.Attributes.Remove(mod.Attribute);
                                        }
                                        else
                                        {
                                            obj.SetAttribute(mod.Attribute, new DirectoryAttribute(mod.Attribute, [.. remaining]));
                                        }
                                    }
                                }
                                break;

                            case "clear":
                                obj.SetAttribute(mod.Attribute, new DirectoryAttribute(mod.Attribute));
                                obj.Attributes.Remove(mod.Attribute);
                                break;
                        }
                    }

                    obj.WhenChanged = DateTimeOffset.UtcNow;
                    await store.UpdateAsync(obj);
                    results.Add(new BulkOperationResult(dn, true, null));
                }
                catch (Exception ex)
                {
                    results.Add(new BulkOperationResult(dn, false, ex.Message));
                }
            }

            var succeeded = results.Count(r => r.Success);
            await audit.LogAsync(new AuditEntry
            {
                TenantId = DirectoryConstants.DefaultTenantId,
                Action = "BulkModify",
                TargetDn = $"({request.Dns.Count} objects)",
                TargetObjectClass = "multiple",
                SourceIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                Details = new() { ["totalCount"] = request.Dns.Count.ToString(), ["succeededCount"] = succeeded.ToString() }
            });

            return Results.Ok(new BulkResponse(results));
        })
        .WithName("BulkModify")
        .WithTags("Bulk");

        group.MapPost("/move", async (BulkMoveRequest request, IDirectoryStore store, IAuditService audit, HttpContext context) =>
        {
            if (request.Dns == null || request.Dns.Count == 0)
                return Results.Problem(statusCode: 400, detail: "dns is required and must not be empty");
            if (request.Dns.Count > 1000)
                return Results.Problem(statusCode: 400, detail: "dns exceeds maximum batch size of 1000");
            var targetDnValidation = ValidationHelper.ValidateDn(request.TargetDn, "targetDn");
            if (targetDnValidation != null) return targetDnValidation;

            var results = new List<BulkOperationResult>();

            foreach (var dn in request.Dns)
            {
                try
                {
                    var obj = await store.GetByDnAsync(DirectoryConstants.DefaultTenantId, dn);
                    if (obj == null || obj.IsDeleted)
                    {
                        results.Add(new BulkOperationResult(dn, false, "Object not found"));
                        continue;
                    }

                    var rdn = dn.Split(',')[0];
                    var newDn = $"{rdn},{request.TargetDn}";
                    await store.MoveAsync(DirectoryConstants.DefaultTenantId, dn, newDn);
                    results.Add(new BulkOperationResult(dn, true, null));
                }
                catch (Exception ex)
                {
                    results.Add(new BulkOperationResult(dn, false, ex.Message));
                }
            }

            var succeeded = results.Count(r => r.Success);
            await audit.LogAsync(new AuditEntry
            {
                TenantId = DirectoryConstants.DefaultTenantId,
                Action = "BulkMove",
                TargetDn = request.TargetDn,
                TargetObjectClass = "multiple",
                SourceIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                Details = new() { ["totalCount"] = request.Dns.Count.ToString(), ["succeededCount"] = succeeded.ToString(), ["targetDn"] = request.TargetDn }
            });

            return Results.Ok(new BulkResponse(results));
        })
        .WithName("BulkMove")
        .WithTags("Bulk");

        group.MapPost("/enable", async (BulkDnRequest request, IDirectoryStore store, IAuditService audit, HttpContext context) =>
        {
            if (request.Dns == null || request.Dns.Count == 0)
                return Results.Problem(statusCode: 400, detail: "dns is required and must not be empty");
            if (request.Dns.Count > 1000)
                return Results.Problem(statusCode: 400, detail: "dns exceeds maximum batch size of 1000");

            var results = new List<BulkOperationResult>();

            foreach (var dn in request.Dns)
            {
                try
                {
                    var obj = await store.GetByDnAsync(DirectoryConstants.DefaultTenantId, dn);
                    if (obj == null || obj.IsDeleted)
                    {
                        results.Add(new BulkOperationResult(dn, false, "Object not found"));
                        continue;
                    }

                    obj.UserAccountControl &= ~0x2; // Clear ACCOUNTDISABLE
                    obj.WhenChanged = DateTimeOffset.UtcNow;
                    await store.UpdateAsync(obj);
                    results.Add(new BulkOperationResult(dn, true, null));
                }
                catch (Exception ex)
                {
                    results.Add(new BulkOperationResult(dn, false, ex.Message));
                }
            }

            var succeeded = results.Count(r => r.Success);
            await audit.LogAsync(new AuditEntry
            {
                TenantId = DirectoryConstants.DefaultTenantId,
                Action = "BulkEnable",
                TargetDn = $"({request.Dns.Count} objects)",
                TargetObjectClass = "multiple",
                SourceIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                Details = new() { ["totalCount"] = request.Dns.Count.ToString(), ["succeededCount"] = succeeded.ToString() }
            });

            return Results.Ok(new BulkResponse(results));
        })
        .WithName("BulkEnable")
        .WithTags("Bulk");

        group.MapPost("/disable", async (BulkDnRequest request, IDirectoryStore store, IAuditService audit, HttpContext context) =>
        {
            if (request.Dns == null || request.Dns.Count == 0)
                return Results.Problem(statusCode: 400, detail: "dns is required and must not be empty");
            if (request.Dns.Count > 1000)
                return Results.Problem(statusCode: 400, detail: "dns exceeds maximum batch size of 1000");

            var results = new List<BulkOperationResult>();

            foreach (var dn in request.Dns)
            {
                try
                {
                    var obj = await store.GetByDnAsync(DirectoryConstants.DefaultTenantId, dn);
                    if (obj == null || obj.IsDeleted)
                    {
                        results.Add(new BulkOperationResult(dn, false, "Object not found"));
                        continue;
                    }

                    obj.UserAccountControl |= 0x2; // Set ACCOUNTDISABLE
                    obj.WhenChanged = DateTimeOffset.UtcNow;
                    await store.UpdateAsync(obj);
                    results.Add(new BulkOperationResult(dn, true, null));
                }
                catch (Exception ex)
                {
                    results.Add(new BulkOperationResult(dn, false, ex.Message));
                }
            }

            var succeeded = results.Count(r => r.Success);
            await audit.LogAsync(new AuditEntry
            {
                TenantId = DirectoryConstants.DefaultTenantId,
                Action = "BulkDisable",
                TargetDn = $"({request.Dns.Count} objects)",
                TargetObjectClass = "multiple",
                SourceIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                Details = new() { ["totalCount"] = request.Dns.Count.ToString(), ["succeededCount"] = succeeded.ToString() }
            });

            return Results.Ok(new BulkResponse(results));
        })
        .WithName("BulkDisable")
        .WithTags("Bulk");

        group.MapPost("/delete", async (BulkDnRequest request, IDirectoryStore store, IAuditService audit, HttpContext context) =>
        {
            if (request.Dns == null || request.Dns.Count == 0)
                return Results.Problem(statusCode: 400, detail: "dns is required and must not be empty");
            if (request.Dns.Count > 1000)
                return Results.Problem(statusCode: 400, detail: "dns exceeds maximum batch size of 1000");

            var results = new List<BulkOperationResult>();

            foreach (var dn in request.Dns)
            {
                try
                {
                    await store.DeleteAsync(DirectoryConstants.DefaultTenantId, dn);
                    results.Add(new BulkOperationResult(dn, true, null));
                }
                catch (Exception ex)
                {
                    results.Add(new BulkOperationResult(dn, false, ex.Message));
                }
            }

            var succeeded = results.Count(r => r.Success);
            await audit.LogAsync(new AuditEntry
            {
                TenantId = DirectoryConstants.DefaultTenantId,
                Action = "BulkDelete",
                TargetDn = $"({request.Dns.Count} objects)",
                TargetObjectClass = "multiple",
                SourceIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                Details = new() { ["totalCount"] = request.Dns.Count.ToString(), ["succeededCount"] = succeeded.ToString() }
            });

            return Results.Ok(new BulkResponse(results));
        })
        .WithName("BulkDelete")
        .WithTags("Bulk");

        group.MapPost("/reset-password", async (BulkResetPasswordRequest request, IDirectoryStore store, IPasswordPolicy passwordService, IAuditService audit, HttpContext context) =>
        {
            if (request.Dns == null || request.Dns.Count == 0)
                return Results.Problem(statusCode: 400, detail: "dns is required and must not be empty");
            if (request.Dns.Count > 1000)
                return Results.Problem(statusCode: 400, detail: "dns exceeds maximum batch size of 1000");
            var pwdValidation =
                ValidationHelper.ValidateRequired(request.Password, "password") ??
                ValidationHelper.ValidateMaxLength(request.Password, "password", ValidationHelper.MaxPasswordLength);
            if (pwdValidation != null) return pwdValidation;

            var results = new List<BulkOperationResult>();

            foreach (var dn in request.Dns)
            {
                try
                {
                    var obj = await store.GetByDnAsync(DirectoryConstants.DefaultTenantId, dn);
                    if (obj == null || obj.IsDeleted)
                    {
                        results.Add(new BulkOperationResult(dn, false, "Object not found"));
                        continue;
                    }

                    await passwordService.SetPasswordAsync(DirectoryConstants.DefaultTenantId, obj.DistinguishedName, request.Password);

                    if (request.MustChangeAtNextLogon)
                    {
                        // Re-fetch after password set, since SetPasswordAsync may update the object
                        obj = await store.GetByDnAsync(DirectoryConstants.DefaultTenantId, dn);
                        if (obj != null)
                        {
                            obj.PwdLastSet = 0;
                            obj.WhenChanged = DateTimeOffset.UtcNow;
                            await store.UpdateAsync(obj);
                        }
                    }
                    results.Add(new BulkOperationResult(dn, true, null));
                }
                catch (Exception ex)
                {
                    results.Add(new BulkOperationResult(dn, false, ex.Message));
                }
            }

            var succeeded = results.Count(r => r.Success);
            await audit.LogAsync(new AuditEntry
            {
                TenantId = DirectoryConstants.DefaultTenantId,
                Action = "BulkPasswordReset",
                TargetDn = $"({request.Dns.Count} objects)",
                TargetObjectClass = "multiple",
                SourceIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                Details = new() { ["totalCount"] = request.Dns.Count.ToString(), ["succeededCount"] = succeeded.ToString() }
            });

            return Results.Ok(new BulkResponse(results));
        })
        .WithName("BulkResetPassword")
        .WithTags("Bulk");

        return group;
    }
}

public record BulkDnRequest(List<string> Dns);

public record BulkModifyRequest(
    List<string> Dns,
    List<BulkModification> Modifications
);

public record BulkModification(
    string Attribute,
    string Operation,
    List<object> Values
);

public record BulkMoveRequest(
    List<string> Dns,
    string TargetDn
);

public record BulkResetPasswordRequest(
    List<string> Dns,
    string Password,
    bool MustChangeAtNextLogon
);

public record BulkOperationResult(
    string Dn,
    bool Success,
    string Error
);

public record BulkResponse(List<BulkOperationResult> Results);
