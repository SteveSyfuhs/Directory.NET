namespace Directory.Web.Models;

public static class ValidationHelper
{
    public const int MaxStringLength = 1024;
    public const int MaxDnLength = 2048;
    public const int MaxPasswordLength = 256;
    public const int MaxDescriptionLength = 4096;
    public const int MaxPageSize = 200;

    public static IResult ValidateRequired(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Results.Problem(statusCode: 400, detail: $"{fieldName} is required");
        return null;
    }

    public static IResult ValidateMaxLength(string value, string fieldName, int maxLength = MaxStringLength)
    {
        if (value != null && value.Length > maxLength)
            return Results.Problem(statusCode: 400, detail: $"{fieldName} exceeds maximum length of {maxLength}");
        return null;
    }

    public static IResult ValidateDn(string dn, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(dn))
            return Results.Problem(statusCode: 400, detail: $"{fieldName} is required");
        if (dn.Length > MaxDnLength)
            return Results.Problem(statusCode: 400, detail: $"{fieldName} exceeds maximum length of {MaxDnLength}");
        if (!dn.Contains('='))
            return Results.Problem(statusCode: 400, detail: $"{fieldName} is not a valid distinguished name");
        return null;
    }

    public static IResult ValidatePageSize(int? pageSize)
    {
        if (pageSize.HasValue && (pageSize.Value < 1 || pageSize.Value > MaxPageSize))
            return Results.Problem(statusCode: 400, detail: $"pageSize must be between 1 and {MaxPageSize}");
        return null;
    }
}
