using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Ldap.Protocol;
using Directory.Ldap.Protocol.Messages;
using Directory.Ldap.Server;
using Directory.Schema;
using Microsoft.Extensions.Logging;

namespace Directory.Ldap.Handlers;

public class AddHandler : ILdapOperationHandler
{
    private readonly IDirectoryStore _store;
    private readonly ISchemaService _schema;
    private readonly ILinkedAttributeService _linkedAttributes;
    private readonly IAccessControlService _acl;
    private readonly INamingContextService _namingContext;
    private readonly SchemaModificationService _schemaMod;
    private readonly ILogger<AddHandler> _logger;

    public LdapOperation Operation => LdapOperation.AddRequest;

    public AddHandler(IDirectoryStore store, ISchemaService schema, ILinkedAttributeService linkedAttributes, IAccessControlService acl, INamingContextService namingContext, SchemaModificationService schemaMod, ILogger<AddHandler> logger)
    {
        _store = store;
        _schema = schema;
        _linkedAttributes = linkedAttributes;
        _acl = acl;
        _namingContext = namingContext;
        _schemaMod = schemaMod;
        _logger = logger;
    }

    public async Task HandleAsync(LdapMessage request, ILdapResponseWriter writer, LdapConnectionState state, CancellationToken ct)
    {
        var addReq = AddRequest.Decode(request.ProtocolOpData);

        _logger.LogDebug("Add request: {Entry}", addReq.Entry);

        try
        {
            var parsed = DistinguishedName.Parse(addReq.Entry);
            var parentDn = parsed.Parent().ToString();
            var callerSid = state.BoundSid ?? string.Empty;
            var callerGroups = state.GroupSids ?? (IReadOnlySet<string>)new HashSet<string>();

            // Access control: check CREATE_CHILD permission on the parent container
            var parentObj = await _store.GetByDnAsync(state.TenantId, parentDn, ct);
            if (parentObj is not null && !_acl.CheckAccess(callerSid, callerGroups, parentObj, AccessMask.CreateChild))
            {
                _logger.LogWarning("Access denied: {Sid} cannot create child in {Dn}", callerSid, parentDn);
                var denied = new AddResponse
                {
                    ResultCode = LdapResultCode.InsufficientAccessRights,
                    DiagnosticMessage = "Insufficient access rights to create a child object in the parent container",
                };
                await writer.WriteMessageAsync(request.MessageId, denied.Encode(), ct: ct);
                return;
            }

            // --- Schema extension: intercept adds under CN=Schema,CN=Configuration ---
            var isSchemaAdd = IsSchemaContainerDn(addReq.Entry);
            if (isSchemaAdd)
            {
                await HandleSchemaAddAsync(addReq, request, writer, state, parsed, ct);
                return;
            }

            var obj = new DirectoryObject
            {
                DistinguishedName = addReq.Entry,
                TenantId = state.TenantId,
                Cn = parsed.Rdn.Value,
            };

            // Apply attributes from the request
            foreach (var (name, values) in addReq.Attributes)
            {
                var attr = new DirectoryAttribute(name, [.. values.Cast<object>()]);
                obj.SetAttribute(name, attr);
            }

            // --- Schema enforcement: validate objectClass ---
            if (obj.ObjectClass.Count == 0)
            {
                var resp = new AddResponse
                {
                    ResultCode = LdapResultCode.ObjectClassViolation,
                    DiagnosticMessage = "objectClass attribute is required",
                };
                await writer.WriteMessageAsync(request.MessageId, resp.Encode(), ct: ct);
                return;
            }

            var structuralClassName = obj.ObjectClass[^1];
            var structuralClass = _schema.GetObjectClass(structuralClassName);
            if (structuralClass is null)
            {
                var resp = new AddResponse
                {
                    ResultCode = LdapResultCode.ObjectClassViolation,
                    DiagnosticMessage = $"Unknown object class: {structuralClassName}",
                };
                await writer.WriteMessageAsync(request.MessageId, resp.Encode(), ct: ct);
                return;
            }

            // Collect allowed and required attributes from the full class hierarchy
            var allowedAttributes = _schema.GetAllAllowedAttributes(structuralClassName);
            var requiredAttributes = _schema.GetAllRequiredAttributes(structuralClassName);

            // System-generated attributes that should never be rejected
            var systemAttributes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "objectClass", "distinguishedName", "objectGUID", "objectSid",
                "objectCategory", "name", "whenCreated", "whenChanged",
                "uSNCreated", "uSNChanged", "instanceType", "structuralObjectClass"
            };

            // Validate all provided attributes exist in schema and are allowed on this class
            foreach (var (attrName, attrValues) in addReq.Attributes)
            {
                if (systemAttributes.Contains(attrName))
                    continue;

                var attrSchema = _schema.GetAttribute(attrName);
                if (attrSchema is null)
                {
                    var resp = new AddResponse
                    {
                        ResultCode = LdapResultCode.UndefinedAttributeType,
                        DiagnosticMessage = $"Attribute '{attrName}' is not defined in schema",
                    };
                    await writer.WriteMessageAsync(request.MessageId, resp.Encode(), ct: ct);
                    return;
                }

                if (!allowedAttributes.Contains(attrName) && !systemAttributes.Contains(attrName))
                {
                    var resp = new AddResponse
                    {
                        ResultCode = LdapResultCode.ObjectClassViolation,
                        DiagnosticMessage = $"Attribute '{attrName}' is not allowed on object class '{structuralClassName}'",
                    };
                    await writer.WriteMessageAsync(request.MessageId, resp.Encode(), ct: ct);
                    return;
                }

                // Validate single-valued constraint
                if (attrSchema.IsSingleValued && attrValues.Count > 1)
                {
                    var resp = new AddResponse
                    {
                        ResultCode = LdapResultCode.ConstraintViolation,
                        DiagnosticMessage = $"Attribute '{attrName}' is single-valued but {attrValues.Count} values were provided",
                    };
                    await writer.WriteMessageAsync(request.MessageId, resp.Encode(), ct: ct);
                    return;
                }

                // Attribute syntax validation on write
                var syntaxError = ModifyHandler.ValidateAttributeSyntax(attrName, attrValues, _schema);
                if (syntaxError != null)
                {
                    var resp = new AddResponse
                    {
                        ResultCode = LdapResultCode.InvalidAttributeSyntax,
                        DiagnosticMessage = syntaxError,
                    };
                    await writer.WriteMessageAsync(request.MessageId, resp.Encode(), ct: ct);
                    return;
                }
            }

            // Set objectCategory from objectClass if not explicitly set
            if (string.IsNullOrEmpty(obj.ObjectCategory) && obj.ObjectClass.Count > 0)
            {
                obj.ObjectCategory = obj.ObjectClass[^1]; // Most specific class
            }

            // Generate objectGUID and objectSid
            obj.ObjectGuid = Guid.NewGuid().ToString();
            obj.ObjectSid = GenerateObjectSid(state.TenantId);

            // Generate USN
            var usn = await _store.GetNextUsnAsync(state.TenantId, parsed.GetDomainDn(), ct);
            obj.USNCreated = usn;
            obj.USNChanged = usn;

            // Validate required attributes are present
            var missingRequired = new List<string>();
            foreach (var required in requiredAttributes)
            {
                if (required.Equals("objectClass", StringComparison.OrdinalIgnoreCase))
                    continue;

                var attrVal = obj.GetAttribute(required);
                if (attrVal is null || attrVal.Values.Count == 0)
                {
                    missingRequired.Add(required);
                }
            }

            if (missingRequired.Count > 0)
            {
                var resp = new AddResponse
                {
                    ResultCode = LdapResultCode.ObjectClassViolation,
                    DiagnosticMessage = $"Missing required attribute(s): {string.Join(", ", missingRequired)}",
                };
                await writer.WriteMessageAsync(request.MessageId, resp.Encode(), ct: ct);
                return;
            }

            // Schema validation passed — create the object
            {
                await _store.CreateAsync(obj, ct);

                // Set nTSecurityDescriptor: generate default SD and inherit ACEs from parent
                var nc = _namingContext.GetNamingContext(addReq.Entry);
                var domainSid = nc?.DomainSid ?? string.Empty;
                var objectClassName = obj.ObjectClass.Count > 0 ? obj.ObjectClass[^1] : "top";
                var defaultSd = _acl.GetDefaultSecurityDescriptor(objectClassName, callerSid, domainSid);

                if (parentObj is not null)
                {
                    var parentSdAttr = parentObj.GetAttribute("nTSecurityDescriptor");
                    if (parentSdAttr?.GetFirstBytes() is { } parentSdBytes)
                    {
                        var parentSd = SecurityDescriptor.Deserialize(parentSdBytes);
                        defaultSd = _acl.InheritAces(parentSd, defaultSd, objectClassName, _schema);
                    }
                }

                obj.SetAttribute("nTSecurityDescriptor",
                    new DirectoryAttribute("nTSecurityDescriptor", [defaultSd.Serialize()]));
                await _store.UpdateAsync(obj, ct);

                // Process linked attributes set during creation (e.g. group created with initial members)
                foreach (var (name, values) in addReq.Attributes)
                {
                    if (_linkedAttributes.IsForwardLink(name))
                    {
                        foreach (var targetDn in values)
                        {
                            await _linkedAttributes.UpdateForwardLinkAsync(
                                state.TenantId, obj, name, targetDn, add: true, ct);
                        }
                    }
                }

                var response = new AddResponse { ResultCode = LdapResultCode.Success };
                await writer.WriteMessageAsync(request.MessageId, response.Encode(), ct: ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding {Entry}", addReq.Entry);

            var response = new AddResponse
            {
                ResultCode = LdapResultCode.OperationsError,
                DiagnosticMessage = ex.Message,
            };
            await writer.WriteMessageAsync(request.MessageId, response.Encode(), ct: ct);
        }
    }

    private static string GenerateObjectSid(string tenantId)
    {
        // Generate a pseudo-random SID: S-1-5-21-{random}-{random}-{random}-{RID}
        var r = Random.Shared;
        return $"S-1-5-21-{r.Next(100000000, 999999999)}-{r.Next(100000000, 999999999)}-{r.Next(100000000, 999999999)}-{r.Next(1000, 99999)}";
    }

    /// <summary>
    /// Detect whether the target DN is under the Schema naming context
    /// (e.g., CN=myAttr,CN=Schema,CN=Configuration,DC=...).
    /// </summary>
    private static bool IsSchemaContainerDn(string dn)
    {
        return dn.Contains("CN=Schema,CN=Configuration", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Handle LDAP Add operations that create attributeSchema or classSchema objects
    /// under the Schema container. This is how enterprise apps like Exchange extend the AD schema.
    /// </summary>
    private async Task HandleSchemaAddAsync(
        AddRequest addReq, LdapMessage request, ILdapResponseWriter writer,
        LdapConnectionState state, DistinguishedName parsed, CancellationToken ct)
    {
        _logger.LogInformation("Schema modification request: adding {Entry}", addReq.Entry);

        // Build a temporary DirectoryObject from the request attributes
        var tempObj = new DirectoryObject
        {
            DistinguishedName = addReq.Entry,
            TenantId = state.TenantId,
            Cn = parsed.Rdn.Value,
        };

        foreach (var (name, values) in addReq.Attributes)
        {
            var attr = new DirectoryAttribute(name, [.. values.Cast<object>()]);
            tempObj.SetAttribute(name, attr);
        }

        // Determine if this is an attributeSchema or classSchema based on objectClass
        var objectClasses = tempObj.ObjectClass;
        var isAttributeSchema = objectClasses.Any(oc => oc.Equals("attributeSchema", StringComparison.OrdinalIgnoreCase));
        var isClassSchema = objectClasses.Any(oc => oc.Equals("classSchema", StringComparison.OrdinalIgnoreCase));

        if (!isAttributeSchema && !isClassSchema)
        {
            var resp = new AddResponse
            {
                ResultCode = LdapResultCode.ObjectClassViolation,
                DiagnosticMessage = "Objects added under CN=Schema,CN=Configuration must have objectClass attributeSchema or classSchema",
            };
            await writer.WriteMessageAsync(request.MessageId, resp.Encode(), ct: ct);
            return;
        }

        // Extract the schema container DN (parent of the new object)
        var schemaDn = parsed.Parent().ToString();

        SchemaModificationResult result;

        if (isAttributeSchema)
        {
            var attrEntry = SchemaModificationService.ParseAttributeSchemaFromDirectoryObject(tempObj);
            result = await _schemaMod.RegisterAttributeExtensionAsync(attrEntry, schemaDn, ct);
        }
        else
        {
            var classEntry = SchemaModificationService.ParseClassSchemaFromDirectoryObject(tempObj);
            result = await _schemaMod.RegisterClassExtensionAsync(classEntry, schemaDn, ct);
        }

        if (result.IsSuccess)
        {
            _logger.LogInformation("Schema extension added successfully: {Entry}", addReq.Entry);
            var response = new AddResponse { ResultCode = LdapResultCode.Success };
            await writer.WriteMessageAsync(request.MessageId, response.Encode(), ct: ct);
        }
        else
        {
            _logger.LogWarning("Schema extension validation failed for {Entry}: {Errors}",
                addReq.Entry, string.Join("; ", result.Errors));
            var response = new AddResponse
            {
                ResultCode = LdapResultCode.ConstraintViolation,
                DiagnosticMessage = string.Join("; ", result.Errors),
            };
            await writer.WriteMessageAsync(request.MessageId, response.Encode(), ct: ct);
        }
    }
}
