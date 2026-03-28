namespace Directory.Web.Models;

/// <summary>
/// Standard paginated response wrapper for cursor-based pagination.
/// </summary>
public class PaginatedResponse<T>
{
    public IReadOnlyList<T> Items { get; set; } = Array.Empty<T>();
    public string ContinuationToken { get; set; } // null if no more pages
    public int TotalCount { get; set; } = -1; // -1 if unknown
    public int PageSize { get; set; }
    public bool HasMore => ContinuationToken != null;
}

/// <summary>
/// Standard pagination request parameters.
/// </summary>
public class PaginationRequest
{
    public int PageSize { get; set; } = 50;
    public string ContinuationToken { get; set; }
}
