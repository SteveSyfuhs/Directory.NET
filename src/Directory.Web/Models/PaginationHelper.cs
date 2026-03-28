using System.Text;

namespace Directory.Web.Models;

/// <summary>
/// Helpers for cursor-based pagination with Cosmos DB continuation tokens.
/// Encodes/decodes tokens as base64 for URL safety.
/// </summary>
public static class PaginationHelper
{
    /// <summary>
    /// Extract pagination parameters from the query string.
    /// Returns clamped pageSize and decoded continuation token.
    /// </summary>
    public static (int pageSize, string continuationToken) ExtractParams(
        int? pageSize,
        string continuationToken,
        int defaultPageSize = 50,
        int maxPageSize = 200)
    {
        var size = Math.Clamp(pageSize ?? defaultPageSize, 1, maxPageSize);
        var token = DecodeContinuationToken(continuationToken);
        return (size, token);
    }

    /// <summary>
    /// Build a PaginatedResponse from a list of items and an optional raw Cosmos DB continuation token.
    /// </summary>
    public static PaginatedResponse<T> BuildResponse<T>(
        IReadOnlyList<T> items,
        string rawContinuationToken,
        int pageSize,
        int totalCount = -1)
    {
        return new PaginatedResponse<T>
        {
            Items = items,
            ContinuationToken = EncodeContinuationToken(rawContinuationToken),
            PageSize = pageSize,
            TotalCount = totalCount,
        };
    }

    /// <summary>
    /// Encode a raw Cosmos DB continuation token to a URL-safe base64 string.
    /// Returns null if the input is null or empty.
    /// </summary>
    public static string EncodeContinuationToken(string rawToken)
    {
        if (string.IsNullOrEmpty(rawToken))
            return null;

        var bytes = Encoding.UTF8.GetBytes(rawToken);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    /// <summary>
    /// Decode a URL-safe base64 continuation token back to the raw Cosmos DB token.
    /// Returns null if the input is null or empty.
    /// </summary>
    public static string DecodeContinuationToken(string encodedToken)
    {
        if (string.IsNullOrEmpty(encodedToken))
            return null;

        var base64 = encodedToken
            .Replace('-', '+')
            .Replace('_', '/');

        // Add padding if needed
        switch (base64.Length % 4)
        {
            case 2: base64 += "=="; break;
            case 3: base64 += "="; break;
        }

        try
        {
            var bytes = Convert.FromBase64String(base64);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (FormatException)
        {
            return null; // Invalid token — treat as no token
        }
    }
}
