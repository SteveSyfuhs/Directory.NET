using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Ldap.Protocol;
using Directory.Ldap.Protocol.Messages;
using Directory.Ldap.Server;
using Microsoft.Extensions.Logging;

namespace Directory.Ldap.Handlers;

public class ModifyHandler : ILdapOperationHandler
{
    private readonly IDirectoryStore _store;
    private readonly ISchemaService _schema;
    private readonly ILinkedAttributeService _linkedAttributes;
    private readonly IAccessControlService _acl;
    private readonly LdapControlHandler _controlHandler;
    private readonly PasswordModifyHandler _passwordModifyHandler;
    private readonly ILogger<ModifyHandler> _logger;

    public LdapOperation Operation => LdapOperation.ModifyRequest;

    public ModifyHandler(IDirectoryStore store, ISchemaService schema, ILinkedAttributeService linkedAttributes, IAccessControlService acl, LdapControlHandler controlHandler, PasswordModifyHandler passwordModifyHandler, ILogger<ModifyHandler> logger)
    {
        _store = store;
        _schema = schema;
        _linkedAttributes = linkedAttributes;
        _acl = acl;
        _controlHandler = controlHandler;
        _passwordModifyHandler = passwordModifyHandler;
        _logger = logger;
    }

    /// <summary>
    /// Check whether this is a rootDSE schemaUpdateNow trigger.
    /// AD clients request schema reload by writing schemaUpdateNow=1 to the rootDSE (empty DN).
    /// </summary>
    private static bool IsSchemaUpdateNowRequest(ModifyRequest modReq)
    {
        if (!string.IsNullOrEmpty(modReq.Object))
            return false;

        return modReq.Changes.Any(c =>
            c.AttributeName.Equals("schemaUpdateNow", StringComparison.OrdinalIgnoreCase));
    }

    public async Task HandleAsync(LdapMessage request, ILdapResponseWriter writer, LdapConnectionState state, CancellationToken ct)
    {
        var modReq = ModifyRequest.Decode(request.ProtocolOpData);

        _logger.LogDebug("Modify request: {Object}, {Count} changes", modReq.Object, modReq.Changes.Count);

        // Process request controls
        var controls = _controlHandler.ProcessRequestControls(request.Controls);
        bool permissiveModify = controls.PermissiveModify;

        try
        {
            // Handle rootDSE schemaUpdateNow trigger (MS-ADTS 3.1.1.3.3.13)
            if (IsSchemaUpdateNowRequest(modReq))
            {
                _logger.LogInformation("schemaUpdateNow rootDSE trigger received — reloading schema");
                await _schema.ReloadSchemaAsync(ct);
                await SendResponse(writer, request.MessageId, LdapResultCode.Success, string.Empty, ct);
                return;
            }

            var obj = await _store.GetByDnAsync(state.TenantId, modReq.Object, ct);
            if (obj is null)
            {
                await SendResponse(writer, request.MessageId, LdapResultCode.NoSuchObject, "Object not found", ct);
                return;
            }

            // Access control: resolve caller's group SIDs (use cached from connection state)
            var callerSid = state.BoundSid ?? string.Empty;
            var callerGroups = state.GroupSids ?? (IReadOnlySet<string>)new HashSet<string>();

            if (!_acl.CheckAccess(callerSid, callerGroups, obj, AccessMask.WriteProperty))
            {
                _logger.LogWarning("Access denied: {Sid} cannot write to {Dn}", callerSid, modReq.Object);
                await SendResponse(writer, request.MessageId, LdapResultCode.InsufficientAccessRights,
                    "Insufficient access rights to modify this object", ct);
                return;
            }

            // Access control: check WRITE_PROPERTY per attribute being modified,
            // with protected attribute enforcement (Part 4) and object-specific ACE support (Part 5)
            foreach (var change in modReq.Changes)
            {
                if (!_acl.CheckAttributeAccess(callerSid, callerGroups, obj, change.AttributeName, isWrite: true, _schema))
                {
                    _logger.LogWarning("Access denied: {Sid} cannot write attribute {Attr} on {Dn}",
                        callerSid, change.AttributeName, modReq.Object);
                    await SendResponse(writer, request.MessageId, LdapResultCode.InsufficientAccessRights,
                        $"Insufficient access rights to modify attribute '{change.AttributeName}'", ct);
                    return;
                }
            }

            // --- Schema enforcement on modifications ---
            var structuralClassName = obj.ObjectClass.Count > 0 ? obj.ObjectClass[^1] : null;
            HashSet<string> allowedAttributes = null;
            HashSet<string> requiredAttributes = null;

            if (structuralClassName is not null)
            {
                allowedAttributes = _schema.GetAllAllowedAttributes(structuralClassName);
                requiredAttributes = _schema.GetAllRequiredAttributes(structuralClassName);
            }

            // System attributes that are always allowed in modifications
            var systemAttributes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "objectClass", "distinguishedName", "objectGUID", "objectSid",
                "objectCategory", "name", "whenCreated", "whenChanged",
                "uSNCreated", "uSNChanged", "instanceType", "structuralObjectClass",
                "nTSecurityDescriptor"
            };

            foreach (var change in modReq.Changes)
            {
                // Validate attribute exists in schema
                if (!systemAttributes.Contains(change.AttributeName))
                {
                    var attrSchema = _schema.GetAttribute(change.AttributeName);
                    if (attrSchema is null)
                    {
                        await SendResponse(writer, request.MessageId, LdapResultCode.UndefinedAttributeType,
                            $"Attribute '{change.AttributeName}' is not defined in schema", ct);
                        return;
                    }

                    // Validate attribute is allowed on this object class
                    if (allowedAttributes is not null && !allowedAttributes.Contains(change.AttributeName))
                    {
                        await SendResponse(writer, request.MessageId, LdapResultCode.ObjectClassViolation,
                            $"Attribute '{change.AttributeName}' is not allowed on object class '{structuralClassName}'", ct);
                        return;
                    }

                    // Validate single-valued attributes aren't given multiple values
                    if (attrSchema.IsSingleValued && change.Operation != ModifyOperation.Delete)
                    {
                        if (change.Operation == ModifyOperation.Replace && change.Values.Count > 1)
                        {
                            await SendResponse(writer, request.MessageId, LdapResultCode.ConstraintViolation,
                                $"Attribute '{change.AttributeName}' is single-valued but {change.Values.Count} values were provided", ct);
                            return;
                        }

                        if (change.Operation == ModifyOperation.Add)
                        {
                            var existing = obj.GetAttribute(change.AttributeName);
                            var currentCount = existing?.Values.Count ?? 0;
                            if (currentCount + change.Values.Count > 1)
                            {
                                await SendResponse(writer, request.MessageId, LdapResultCode.ConstraintViolation,
                                    $"Attribute '{change.AttributeName}' is single-valued; cannot add additional values", ct);
                                return;
                            }
                        }
                    }
                }

                // Validate MUST attributes aren't being deleted
                if (change.Operation == ModifyOperation.Delete && change.Values.Count == 0)
                {
                    if (requiredAttributes is not null && requiredAttributes.Contains(change.AttributeName))
                    {
                        await SendResponse(writer, request.MessageId, LdapResultCode.ObjectClassViolation,
                            $"Cannot delete required attribute '{change.AttributeName}'", ct);
                        return;
                    }
                }

                // Attribute syntax validation on write
                if (change.Operation != ModifyOperation.Delete)
                {
                    var syntaxError = ValidateAttributeSyntax(change.AttributeName, change.Values, _schema);
                    if (syntaxError != null)
                    {
                        await SendResponse(writer, request.MessageId, LdapResultCode.InvalidAttributeSyntax,
                            syntaxError, ct);
                        return;
                    }
                }
            }

            // --- Password policy enforcement for unicodePwd modifications ---
            var unicodePwdChange = modReq.Changes.FirstOrDefault(c =>
                c.AttributeName.Equals("unicodePwd", StringComparison.OrdinalIgnoreCase));

            if (unicodePwdChange is not null)
            {
                // Require encrypted connection per MS-ADTS 3.1.1.3.1.5
                bool isEncrypted = state.IsTls || state.BindStatus == BindState.SaslBound;
                if (!isEncrypted)
                {
                    await SendResponse(writer, request.MessageId, LdapResultCode.ConfidentialityRequired,
                        "Password modifications require an encrypted connection (TLS/SSL or SASL)", ct);
                    return;
                }

                // Build password modify operations from the modify changes
                var pwdOps = modReq.Changes
                    .Where(c => c.AttributeName.Equals("unicodePwd", StringComparison.OrdinalIgnoreCase))
                    .Select(c => new PasswordModifyOperation
                    {
                        Operation = c.Operation switch
                        {
                            ModifyOperation.Add => PasswordModifyOperationType.Add,
                            ModifyOperation.Delete => PasswordModifyOperationType.Delete,
                            ModifyOperation.Replace => PasswordModifyOperationType.Replace,
                            _ => PasswordModifyOperationType.Replace
                        },
                        Value = c.Values.Count > 0 ? System.Text.Encoding.Unicode.GetBytes(c.Values[0]) : null
                    })
                    .ToList();

                var pwdResult = await _passwordModifyHandler.HandleAsync(
                    state.TenantId, modReq.Object, pwdOps, state.BoundDn, isEncrypted, ct);

                if (!pwdResult.IsSuccess)
                {
                    var resultCode = pwdResult.ResultCode ?? LdapResultCode.ConstraintViolation;
                    await SendResponse(writer, request.MessageId, resultCode,
                        pwdResult.ErrorMessage ?? "Password policy violation", ct);
                    return;
                }

                // Password was handled by PasswordModifyHandler (which calls SetPasswordAsync),
                // so remove unicodePwd changes from the regular modification list
                modReq.Changes.RemoveAll(c =>
                    c.AttributeName.Equals("unicodePwd", StringComparison.OrdinalIgnoreCase));

                // Re-fetch the object since PasswordModifyHandler updated it (pwdLastSet, etc.)
                obj = (await _store.GetByDnAsync(state.TenantId, modReq.Object, ct));

                // If no other changes remain, we're done
                if (modReq.Changes.Count == 0)
                {
                    // Update USN
                    var parsedDn = DistinguishedName.Parse(modReq.Object);
                    obj.USNChanged = await _store.GetNextUsnAsync(state.TenantId, parsedDn.GetDomainDn(), ct);
                    await _store.UpdateAsync(obj, ct);
                    await SendResponse(writer, request.MessageId, LdapResultCode.Success, string.Empty, ct);
                    return;
                }
            }

            foreach (var change in modReq.Changes)
            {
                // Capture old values for linked attributes before modification
                // so we can properly remove stale back-links on Replace/Delete-all
                List<string> oldLinkedValues = null;
                bool isForwardLink = _linkedAttributes.IsForwardLink(change.AttributeName);

                if (isForwardLink &&
                    (change.Operation == ModifyOperation.Replace ||
                     (change.Operation == ModifyOperation.Delete && change.Values.Count == 0)))
                {
                    oldLinkedValues = obj.GetAttribute(change.AttributeName)?.GetStrings().ToList();
                }

                ApplyModification(obj, change, permissiveModify);

                // Maintain linked attribute back-links (e.g. member -> memberOf)
                if (isForwardLink)
                {
                    switch (change.Operation)
                    {
                        case ModifyOperation.Add:
                            foreach (var targetDn in change.Values)
                            {
                                await _linkedAttributes.UpdateForwardLinkAsync(
                                    state.TenantId, obj, change.AttributeName, targetDn, add: true, ct);
                            }
                            break;

                        case ModifyOperation.Delete:
                            if (change.Values.Count == 0 && oldLinkedValues is not null)
                            {
                                // Entire attribute deleted — remove back-links for all previous values
                                foreach (var targetDn in oldLinkedValues)
                                {
                                    await _linkedAttributes.UpdateForwardLinkAsync(
                                        state.TenantId, obj, change.AttributeName, targetDn, add: false, ct);
                                }
                            }
                            else
                            {
                                // Specific values deleted
                                foreach (var targetDn in change.Values)
                                {
                                    await _linkedAttributes.UpdateForwardLinkAsync(
                                        state.TenantId, obj, change.AttributeName, targetDn, add: false, ct);
                                }
                            }
                            break;

                        case ModifyOperation.Replace:
                            // Remove back-links for old values that are no longer present
                            if (oldLinkedValues is not null)
                            {
                                var newValues = new HashSet<string>(change.Values, StringComparer.OrdinalIgnoreCase);
                                foreach (var oldDn in oldLinkedValues)
                                {
                                    if (!newValues.Contains(oldDn))
                                    {
                                        await _linkedAttributes.UpdateForwardLinkAsync(
                                            state.TenantId, obj, change.AttributeName, oldDn, add: false, ct);
                                    }
                                }
                            }
                            // Add back-links for new values that weren't previously present
                            var oldSet = oldLinkedValues is not null
                                ? new HashSet<string>(oldLinkedValues, StringComparer.OrdinalIgnoreCase)
                                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            foreach (var targetDn in change.Values)
                            {
                                if (!oldSet.Contains(targetDn))
                                {
                                    await _linkedAttributes.UpdateForwardLinkAsync(
                                        state.TenantId, obj, change.AttributeName, targetDn, add: true, ct);
                                }
                            }
                            break;
                    }
                }
            }

            // Update USN
            var parsed = DistinguishedName.Parse(modReq.Object);
            obj.USNChanged = await _store.GetNextUsnAsync(state.TenantId, parsed.GetDomainDn(), ct);

            await _store.UpdateAsync(obj, ct);

            await SendResponse(writer, request.MessageId, LdapResultCode.Success, string.Empty, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error modifying {Object}", modReq.Object);
            await SendResponse(writer, request.MessageId, LdapResultCode.OperationsError, ex.Message, ct);
        }
    }

    private static void ApplyModification(DirectoryObject obj, ModifyChange change, bool permissive)
    {
        var existing = obj.GetAttribute(change.AttributeName);

        switch (change.Operation)
        {
            case ModifyOperation.Add:
                if (existing is not null)
                {
                    if (permissive)
                    {
                        // In permissive mode, skip values that already exist
                        var existingStrings = new HashSet<string>(
                            existing.Values.Select(v => v?.ToString() ?? string.Empty),
                            StringComparer.OrdinalIgnoreCase);
                        var newValues = change.Values
                            .Where(v => !existingStrings.Contains(v))
                            .Cast<object>()
                            .ToList();
                        if (newValues.Count > 0)
                        {
                            existing.Values.AddRange(newValues);
                            obj.SetAttribute(change.AttributeName, existing);
                        }
                    }
                    else
                    {
                        existing.Values.AddRange(change.Values.Cast<object>());
                        obj.SetAttribute(change.AttributeName, existing);
                    }
                }
                else
                {
                    obj.SetAttribute(change.AttributeName, new DirectoryAttribute(change.AttributeName, [.. change.Values.Cast<object>()]));
                }
                break;

            case ModifyOperation.Delete:
                if (change.Values.Count == 0)
                {
                    // Delete the entire attribute
                    if (!permissive || existing is not null)
                    {
                        obj.Attributes.Remove(change.AttributeName);
                    }
                }
                else if (existing is not null)
                {
                    // Delete specific values
                    foreach (var val in change.Values)
                    {
                        existing.Values.RemoveAll(v => string.Equals(v?.ToString(), val, StringComparison.OrdinalIgnoreCase));
                    }
                    obj.SetAttribute(change.AttributeName, existing);
                }
                else if (!permissive)
                {
                    // Non-permissive: deleting from non-existent attribute is fine (no-op)
                    // but we keep the same behavior as before
                }
                // In permissive mode, deleting values from a non-existent attribute silently succeeds
                break;

            case ModifyOperation.Replace:
                obj.SetAttribute(change.AttributeName, new DirectoryAttribute(change.AttributeName, [.. change.Values.Cast<object>()]));
                break;
        }
    }

    /// <summary>
    /// Validates attribute values against the schema-defined syntax OID.
    /// Returns an error message if validation fails, or null if values are valid.
    /// </summary>
    internal static string ValidateAttributeSyntax(string attributeName, List<string> values, ISchemaService schema)
    {
        var attrSchema = schema.GetAttribute(attributeName);
        if (attrSchema is null)
            return null; // Unknown attributes pass through

        foreach (var value in values)
        {
            var error = ValidateValueForSyntax(attributeName, value, attrSchema.Syntax);
            if (error != null)
                return error;
        }

        return null;
    }

    private static string ValidateValueForSyntax(string attributeName, string value, string syntaxOid)
    {
        switch (syntaxOid)
        {
            case "2.5.5.1": // DN syntax
                try
                {
                    if (string.IsNullOrWhiteSpace(value))
                        return $"Attribute '{attributeName}' requires a valid DN value";
                    DistinguishedName.Parse(value);
                }
                catch (FormatException)
                {
                    return $"Attribute '{attributeName}' has invalid DN syntax: '{value}'";
                }
                break;

            case "2.5.5.9": // Integer
                if (!int.TryParse(value, out _) && !long.TryParse(value, out _))
                    return $"Attribute '{attributeName}' requires an integer value, got: '{value}'";
                break;

            case "2.5.5.8": // Boolean
                if (!string.Equals(value, "TRUE", StringComparison.Ordinal) &&
                    !string.Equals(value, "FALSE", StringComparison.Ordinal))
                    return $"Attribute '{attributeName}' requires a boolean value (TRUE or FALSE), got: '{value}'";
                break;

            case "2.5.5.11": // GeneralizedTime
                if (!IsValidGeneralizedTime(value))
                    return $"Attribute '{attributeName}' requires a valid GeneralizedTime value, got: '{value}'";
                break;

            case "2.5.5.2": // OID
                if (!IsValidOid(value))
                    return $"Attribute '{attributeName}' requires a valid OID (dotted-decimal format), got: '{value}'";
                break;
        }

        return null;
    }

    private static bool IsValidGeneralizedTime(string value)
    {
        // GeneralizedTime format: YYYYMMDDHHmmss.fZ or YYYYMMDDHHmmssZ etc.
        if (value.Length < 10)
            return false;

        // Must start with digits for the date/time portion
        var digitPart = value.TakeWhile(c => char.IsDigit(c) || c == '.').Count();
        if (digitPart < 10)
            return false;

        // Try to parse the date portion (first 8 digits as YYYYMMDD)
        if (value.Length >= 8 &&
            int.TryParse(value[..4], out var year) &&
            int.TryParse(value[4..6], out var month) &&
            int.TryParse(value[6..8], out var day))
        {
            if (month < 1 || month > 12 || day < 1 || day > 31 || year < 1900)
                return false;
        }
        else
        {
            return false;
        }

        return true;
    }

    private static bool IsValidOid(string value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        // OID must be dotted-decimal format: digits separated by dots
        var parts = value.Split('.');
        if (parts.Length < 2)
            return false;

        foreach (var part in parts)
        {
            if (string.IsNullOrEmpty(part) || !part.All(char.IsDigit))
                return false;
        }

        return true;
    }

    private static async Task SendResponse(ILdapResponseWriter writer, int messageId, LdapResultCode code, string message, CancellationToken ct)
    {
        var response = new ModifyResponse
        {
            ResultCode = code,
            DiagnosticMessage = message,
        };
        await writer.WriteMessageAsync(messageId, response.Encode(), ct: ct);
    }
}
