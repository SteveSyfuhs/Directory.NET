using Directory.Web.Services;

namespace Directory.Web.Endpoints;

public static class ComplianceEndpoints
{
    public static RouteGroupBuilder MapComplianceEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/reports", async (ComplianceReportService svc) =>
        {
            var reports = await svc.GetAllReportsAsync();
            return Results.Ok(reports);
        })
        .WithName("ListComplianceReports")
        .WithTags("Compliance");

        group.MapPost("/reports/{id}/run", async (string id, ComplianceReportService svc) =>
        {
            try
            {
                var result = await svc.RunReportAsync(id);
                return Results.Ok(result);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        })
        .WithName("RunComplianceReport")
        .WithTags("Compliance");

        group.MapGet("/reports/{id}/result", async (string id, ComplianceReportService svc) =>
        {
            var result = await svc.GetLastResultAsync(id);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("GetComplianceReportResult")
        .WithTags("Compliance");

        group.MapPost("/reports/{id}/export", async (string id, ComplianceReportService svc) =>
        {
            var result = await svc.GetLastResultAsync(id);
            if (result is null) return Results.NotFound();

            var csv = svc.ExportToCsv(result);
            return Results.Text(csv, "text/csv");
        })
        .WithName("ExportComplianceReport")
        .WithTags("Compliance");

        group.MapGet("/dashboard", async (ComplianceReportService svc) =>
        {
            var dashboard = await svc.GetDashboardAsync();
            return Results.Ok(dashboard);
        })
        .WithName("GetComplianceDashboard")
        .WithTags("Compliance");

        group.MapPost("/reports/custom", async (ComplianceReport report, ComplianceReportService svc) =>
        {
            var created = await svc.CreateCustomReportAsync(report);
            return Results.Created($"/api/v1/compliance/reports/{created.Id}", created);
        })
        .WithName("CreateCustomComplianceReport")
        .WithTags("Compliance");

        return group;
    }
}
