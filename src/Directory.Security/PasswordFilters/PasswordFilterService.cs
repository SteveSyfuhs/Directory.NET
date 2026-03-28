using Microsoft.Extensions.Logging;

namespace Directory.Security.PasswordFilters;

/// <summary>
/// Manages registered password filters and runs validation through all enabled filters.
/// This is analogous to the Windows AD LSA password filter notification mechanism
/// where registered DLLs are called during password changes.
/// </summary>
public class PasswordFilterService
{
    private readonly List<IPasswordFilter> _filters;
    private readonly ILogger<PasswordFilterService> _logger;

    public PasswordFilterService(IEnumerable<IPasswordFilter> filters, ILogger<PasswordFilterService> logger)
    {
        _filters = filters.OrderBy(f => f.Order).ToList();
        _logger = logger;
    }

    /// <summary>
    /// Get all registered password filters with their current state.
    /// </summary>
    public IReadOnlyList<IPasswordFilter> GetFilters() => _filters.AsReadOnly();

    /// <summary>
    /// Enable or disable a specific filter by name.
    /// </summary>
    public bool SetFilterEnabled(string filterName, bool enabled)
    {
        var filter = _filters.FirstOrDefault(f =>
            f.Name.Equals(filterName, StringComparison.OrdinalIgnoreCase));

        if (filter == null)
            return false;

        filter.IsEnabled = enabled;
        _logger.LogInformation("Password filter '{Name}' {State}",
            filterName, enabled ? "enabled" : "disabled");
        return true;
    }

    /// <summary>
    /// Run all enabled password filters in order.
    /// Returns an aggregate result with all validation messages.
    /// </summary>
    public async Task<PasswordFilterAggregateResult> ValidatePasswordAsync(
        string dn, string newPassword, string oldPassword = null)
    {
        var result = new PasswordFilterAggregateResult();

        foreach (var filter in _filters.Where(f => f.IsEnabled))
        {
            try
            {
                var filterResult = await filter.ValidatePasswordAsync(dn, newPassword, oldPassword);
                result.FilterResults.Add(new FilterResultEntry
                {
                    FilterName = filter.Name,
                    IsValid = filterResult.IsValid,
                    Message = filterResult.Message,
                });

                if (!filterResult.IsValid)
                {
                    _logger.LogDebug("Password rejected by filter '{Filter}' for {DN}: {Message}",
                        filter.Name, dn, filterResult.Message);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Password filter '{Filter}' threw an exception for {DN}", filter.Name, dn);
                result.FilterResults.Add(new FilterResultEntry
                {
                    FilterName = filter.Name,
                    IsValid = true, // Don't block on filter errors
                    Message = "Filter encountered an error; password allowed by default.",
                });
            }
        }

        result.IsValid = result.FilterResults.All(r => r.IsValid);
        result.Message = result.IsValid
            ? "Password passed all filters."
            : string.Join(" ", result.FilterResults.Where(r => !r.IsValid).Select(r => r.Message));

        return result;
    }
}

/// <summary>
/// Aggregate result from all password filters.
/// </summary>
public class PasswordFilterAggregateResult
{
    public bool IsValid { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<FilterResultEntry> FilterResults { get; set; } = [];
}

/// <summary>
/// Individual filter result within an aggregate.
/// </summary>
public class FilterResultEntry
{
    public string FilterName { get; set; } = string.Empty;
    public bool IsValid { get; set; }
    public string Message { get; set; } = string.Empty;
}
