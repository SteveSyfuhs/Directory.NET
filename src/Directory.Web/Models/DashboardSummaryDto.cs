namespace Directory.Web.Models;

public record DashboardSummaryDto(
    int UserCount,
    int ComputerCount,
    int GroupCount,
    int OuCount,
    int TotalObjects,
    string DomainName,
    string DomainDn,
    string DomainSid,
    int FunctionalLevel
);
