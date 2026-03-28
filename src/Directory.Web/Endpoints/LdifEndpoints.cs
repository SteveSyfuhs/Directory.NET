using Directory.Core.Ldif;
using Directory.Core.Models;
using Directory.Web.Services;

namespace Directory.Web.Endpoints;

public static class LdifEndpoints
{
    public static RouteGroupBuilder MapLdifEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/export", async (LdifExportRequest request, LdifService ldifService, HttpContext context) =>
        {
            var options = new LdifExportOptions
            {
                BaseDn = request.BaseDn,
                Filter = request.Filter,
                Scope = request.Scope switch
                {
                    "base" or "baseObject" => SearchScope.BaseObject,
                    "one" or "oneLevel" or "singleLevel" => SearchScope.SingleLevel,
                    _ => SearchScope.WholeSubtree
                },
                Attributes = request.Attributes,
                IncludeOperationalAttributes = request.IncludeOperationalAttributes
            };

            var ldif = await ldifService.ExportAsync(options);

            context.Response.ContentType = "text/plain; charset=utf-8";
            context.Response.Headers.ContentDisposition =
                $"attachment; filename=\"ldif-export-{DateTime.UtcNow:yyyyMMdd-HHmmss}.ldif\"";

            await context.Response.WriteAsync(ldif);
        })
        .WithName("LdifExport")
        .WithTags("LDIF");

        group.MapPost("/import", async (HttpRequest request, LdifService ldifService) =>
        {
            if (!request.HasFormContentType)
            {
                return Results.Problem(statusCode: 400, detail: "Request must be multipart/form-data with an LDIF file.");
            }

            var form = await request.ReadFormAsync();
            var file = form.Files.GetFile("file");

            if (file == null || file.Length == 0)
            {
                return Results.Problem(statusCode: 400, detail: "No LDIF file provided. Upload a file with field name 'file'.");
            }

            using var stream = file.OpenReadStream();
            var result = await ldifService.ImportAsync(stream, dryRun: false);

            return Results.Ok(result);
        })
        .DisableAntiforgery()
        .WithName("LdifImport")
        .WithTags("LDIF");

        group.MapPost("/validate", async (HttpRequest request, LdifService ldifService) =>
        {
            if (!request.HasFormContentType)
            {
                return Results.Problem(statusCode: 400, detail: "Request must be multipart/form-data with an LDIF file.");
            }

            var form = await request.ReadFormAsync();
            var file = form.Files.GetFile("file");

            if (file == null || file.Length == 0)
            {
                return Results.Problem(statusCode: 400, detail: "No LDIF file provided. Upload a file with field name 'file'.");
            }

            using var stream = file.OpenReadStream();
            var result = await ldifService.ImportAsync(stream, dryRun: true);

            return Results.Ok(result);
        })
        .DisableAntiforgery()
        .WithName("LdifValidate")
        .WithTags("LDIF");

        return group;
    }
}

public record LdifExportRequest(
    string BaseDn,
    string Filter,
    string Scope,
    List<string> Attributes,
    bool IncludeOperationalAttributes
);
