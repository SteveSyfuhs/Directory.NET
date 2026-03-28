namespace Directory.Web.Models;

public record SearchResultDto(List<ObjectSummaryDto> Items, int TotalCount, string ContinuationToken);
