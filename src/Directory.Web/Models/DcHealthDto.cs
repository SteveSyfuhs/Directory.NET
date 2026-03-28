namespace Directory.Web.Models;

public record DcHealthDto(
    string Hostname,
    string SiteName,
    string ServerDn,
    DateTimeOffset? LastHeartbeat,
    bool IsHealthy
);
