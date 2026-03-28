namespace Directory.Web.Models;

public record ObjectSummaryDto(
    string Dn,
    string ObjectGuid,
    string Name,
    string ObjectClass,
    string Description,
    string SAMAccountName,
    bool? Enabled,
    DateTimeOffset? WhenCreated,
    DateTimeOffset? WhenChanged
);
