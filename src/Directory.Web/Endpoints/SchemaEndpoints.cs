using Directory.Core.Interfaces;
using Directory.Schema;
using Microsoft.AspNetCore.Mvc;

namespace Directory.Web.Endpoints;

public static class SchemaEndpoints
{
    public static RouteGroupBuilder MapSchemaEndpoints(this RouteGroupBuilder group)
    {
        // --- GET: List all attributes (built-in + custom) ---
        group.MapGet("/attributes", (ISchemaService schemaService) =>
        {
            var attributes = schemaService.GetAllAttributes();
            return Results.Ok(attributes);
        })
        .WithName("GetAllAttributes")
        .WithTags("Schema");

        // --- GET: Get a specific attribute by name ---
        group.MapGet("/attributes/{name}", (string name, ISchemaService schemaService) =>
        {
            var attribute = schemaService.GetAttribute(name);
            if (attribute == null)
                return Results.NotFound();

            return Results.Ok(attribute);
        })
        .WithName("GetAttribute")
        .WithTags("Schema");

        // --- POST: Register a new custom attribute ---
        group.MapPost("/attributes", async (
            [FromBody] CreateAttributeRequest request,
            SchemaModificationService schemaMod,
            ISchemaService schemaService,
            CancellationToken ct) =>
        {
            var entry = new AttributeSchemaEntry
            {
                Name = request.LdapDisplayName,
                LdapDisplayName = request.LdapDisplayName,
                Oid = request.Oid,
                Syntax = request.Syntax ?? "2.5.5.12",
                IsSingleValued = request.IsSingleValued,
                IsInGlobalCatalog = request.IsInGlobalCatalog,
                IsIndexed = request.IsIndexed,
                IsSystemOnly = request.IsSystemOnly,
                RangeLower = request.RangeLower,
                RangeUpper = request.RangeUpper,
                Description = request.Description,
            };

            // Use a default schema container DN
            var schemaDn = "CN=Schema,CN=Configuration";

            var result = await schemaMod.RegisterAttributeExtensionAsync(entry, schemaDn, ct);
            if (!result.IsSuccess)
                return Results.BadRequest(new { Errors = result.Errors });

            return Results.Created($"/api/v1/schema/attributes/{entry.LdapDisplayName}", new
            {
                entry.LdapDisplayName,
                entry.Oid,
                entry.Syntax,
                entry.IsSingleValued,
                entry.IsInGlobalCatalog,
                entry.IsIndexed,
                entry.IsBuiltIn,
                SchemaVersion = schemaService.SchemaVersion,
            });
        })
        .WithName("CreateAttribute")
        .WithTags("Schema");

        // --- PUT: Update an existing attribute (limited properties) ---
        group.MapPut("/attributes/{name}", (
            string name,
            [FromBody] UpdateAttributeRequest request,
            ISchemaService schemaService) =>
        {
            var existing = schemaService.GetAttribute(name);
            if (existing is null)
                return Results.Problem(statusCode: 404, detail: $"Attribute '{name}' not found");

            var updated = schemaService.UpdateAttribute(
                name,
                isInGlobalCatalog: request.IsInGlobalCatalog,
                isIndexed: request.IsIndexed,
                description: request.Description);

            if (!updated)
                return Results.Problem(statusCode: 400, detail: $"Failed to update attribute '{name}'");

            return Results.Ok(new
            {
                Name = name,
                Updated = true,
                SchemaVersion = schemaService.SchemaVersion,
            });
        })
        .WithName("UpdateAttribute")
        .WithTags("Schema");

        // --- GET: List all object classes (built-in + custom) ---
        group.MapGet("/classes", (ISchemaService schemaService) =>
        {
            var classes = schemaService.GetAllObjectClasses();
            return Results.Ok(classes);
        })
        .WithName("GetAllObjectClasses")
        .WithTags("Schema");

        // --- GET: Get a specific object class by name ---
        group.MapGet("/classes/{name}", (string name, ISchemaService schemaService) =>
        {
            var objectClass = schemaService.GetObjectClass(name);
            if (objectClass == null)
                return Results.NotFound();

            return Results.Ok(objectClass);
        })
        .WithName("GetObjectClass")
        .WithTags("Schema");

        // --- POST: Register a new custom object class ---
        group.MapPost("/classes", async (
            [FromBody] CreateClassRequest request,
            SchemaModificationService schemaMod,
            ISchemaService schemaService,
            CancellationToken ct) =>
        {
            var entry = new ObjectClassSchemaEntry
            {
                Name = request.LdapDisplayName,
                LdapDisplayName = request.LdapDisplayName,
                Oid = request.Oid,
                SuperiorClass = request.SuperiorClass ?? "top",
                ClassType = request.ClassType,
                MustHaveAttributes = request.MustHaveAttributes ?? [],
                MayHaveAttributes = request.MayHaveAttributes ?? [],
                AuxiliaryClasses = request.AuxiliaryClasses ?? [],
                PossibleSuperiors = request.PossibleSuperiors ?? [],
                Description = request.Description,
            };

            var schemaDn = "CN=Schema,CN=Configuration";

            var result = await schemaMod.RegisterClassExtensionAsync(entry, schemaDn, ct);
            if (!result.IsSuccess)
                return Results.BadRequest(new { Errors = result.Errors });

            return Results.Created($"/api/v1/schema/classes/{entry.LdapDisplayName}", new
            {
                entry.LdapDisplayName,
                entry.Oid,
                entry.SuperiorClass,
                entry.ClassType,
                entry.IsBuiltIn,
                SchemaVersion = schemaService.SchemaVersion,
            });
        })
        .WithName("CreateObjectClass")
        .WithTags("Schema");

        // --- POST: Trigger schema reload ---
        group.MapPost("/reload", async (
            ISchemaService schemaService,
            CancellationToken ct) =>
        {
            await schemaService.ReloadSchemaAsync(ct);

            return Results.Ok(new
            {
                Message = "Schema reloaded successfully",
                SchemaVersion = schemaService.SchemaVersion,
                AttributeCount = schemaService.GetAllAttributes().Count,
                ClassCount = schemaService.GetAllObjectClasses().Count,
            });
        })
        .WithName("ReloadSchema")
        .WithTags("Schema");

        // --- GET: Get all attributes for a class (must-contain + may-contain, inherited) ---
        group.MapGet("/classes/{name}/attributes", (string name, ISchemaService schemaService) =>
        {
            var objectClass = schemaService.GetObjectClass(name);
            if (objectClass == null)
                return Results.Problem(statusCode: 404, detail: $"Object class '{name}' not found");

            var requiredAttrs = schemaService.GetAllRequiredAttributes(name);
            var allowedAttrs = schemaService.GetAllAllowedAttributes(name);

            var syntaxNames = new Dictionary<string, string>
            {
                ["2.5.5.1"] = "Distinguished Name",
                ["2.5.5.2"] = "OID",
                ["2.5.5.3"] = "Case-Insensitive String",
                ["2.5.5.4"] = "Printable String",
                ["2.5.5.5"] = "IA5 String",
                ["2.5.5.6"] = "Numeric String",
                ["2.5.5.7"] = "DN+Binary",
                ["2.5.5.8"] = "Boolean",
                ["2.5.5.9"] = "Integer",
                ["2.5.5.10"] = "Octet String",
                ["2.5.5.11"] = "Generalized Time",
                ["2.5.5.12"] = "Unicode String",
                ["2.5.5.13"] = "Presentation Address",
                ["2.5.5.14"] = "DN+Unicode",
                ["2.5.5.15"] = "NT Security Descriptor",
                ["2.5.5.16"] = "Large Integer",
                ["2.5.5.17"] = "SID",
            };

            var result = new List<ClassAttributeInfo>();
            foreach (var attrName in allowedAttrs.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                var attr = schemaService.GetAttribute(attrName);
                if (attr == null) continue;

                var syntaxOid = attr.Syntax ?? "2.5.5.12";
                syntaxNames.TryGetValue(syntaxOid, out var syntaxLabel);

                result.Add(new ClassAttributeInfo
                {
                    Name = attr.LdapDisplayName ?? attr.Name,
                    Oid = attr.Oid ?? "",
                    SyntaxOid = syntaxOid,
                    SyntaxName = syntaxLabel ?? "Unknown",
                    IsSingleValued = attr.IsSingleValued,
                    IsSystemOnly = attr.IsSystemOnly,
                    IsIndexed = attr.IsIndexed,
                    IsInGlobalCatalog = attr.IsInGlobalCatalog,
                    IsMustContain = requiredAttrs.Contains(attrName),
                    RangeLower = attr.RangeLower,
                    RangeUpper = attr.RangeUpper,
                    Description = attr.Description,
                    PropertySet = attr.AttributeSecurityGUID?.ToString(),
                });
            }

            return Results.Ok(result);
        })
        .WithName("GetClassAttributes")
        .WithTags("Schema");

        // --- GET: Schema info (version, counts) ---
        group.MapGet("/info", (ISchemaService schemaService) =>
        {
            return Results.Ok(new
            {
                SchemaVersion = schemaService.SchemaVersion,
                SchemaUsn = schemaService.SchemaUsn,
                AttributeCount = schemaService.GetAllAttributes().Count,
                ClassCount = schemaService.GetAllObjectClasses().Count,
                GcAttributeCount = schemaService.GetGlobalCatalogAttributes().Count,
            });
        })
        .WithName("GetSchemaInfo")
        .WithTags("Schema");

        return group;
    }
}

// --- Request/Response DTOs ---

public record CreateAttributeRequest
{
    public string LdapDisplayName { get; init; } = string.Empty;
    public string Oid { get; init; } = string.Empty;
    public string Syntax { get; init; }
    public bool IsSingleValued { get; init; } = true;
    public bool IsInGlobalCatalog { get; init; }
    public bool IsIndexed { get; init; }
    public bool IsSystemOnly { get; init; }
    public int? RangeLower { get; init; }
    public int? RangeUpper { get; init; }
    public string Description { get; init; }
}

public record UpdateAttributeRequest
{
    public bool? IsInGlobalCatalog { get; init; }
    public bool? IsIndexed { get; init; }
    public string Description { get; init; }
}

public record CreateClassRequest
{
    public string LdapDisplayName { get; init; } = string.Empty;
    public string Oid { get; init; } = string.Empty;
    public string SuperiorClass { get; init; }
    public ObjectClassType ClassType { get; init; } = ObjectClassType.Structural;
    public List<string> MustHaveAttributes { get; init; }
    public List<string> MayHaveAttributes { get; init; }
    public List<string> AuxiliaryClasses { get; init; }
    public List<string> PossibleSuperiors { get; init; }
    public string Description { get; init; }
}

/// <summary>
/// Represents attribute metadata for a specific object class, including whether it is required.
/// </summary>
public record ClassAttributeInfo
{
    public string Name { get; init; } = string.Empty;
    public string Oid { get; init; } = string.Empty;
    public string SyntaxOid { get; init; } = string.Empty;
    public string SyntaxName { get; init; } = string.Empty;
    public bool IsSingleValued { get; init; }
    public bool IsSystemOnly { get; init; }
    public bool IsIndexed { get; init; }
    public bool IsInGlobalCatalog { get; init; }
    public bool IsMustContain { get; init; }
    public int? RangeLower { get; init; }
    public int? RangeUpper { get; init; }
    public string Description { get; init; }
    public string PropertySet { get; init; }
}
