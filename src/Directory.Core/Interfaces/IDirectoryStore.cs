using Directory.Core.Models;

namespace Directory.Core.Interfaces;

/// <summary>
/// Primary data store interface for directory objects.
/// </summary>
public interface IDirectoryStore
{
    Task<DirectoryObject> GetByDnAsync(string tenantId, string dn, CancellationToken ct = default);

    Task<DirectoryObject> GetByGuidAsync(string tenantId, string guid, CancellationToken ct = default);

    Task<DirectoryObject> GetBySamAccountNameAsync(string tenantId, string domainDn, string samAccountName, CancellationToken ct = default);

    Task<DirectoryObject> GetByUpnAsync(string tenantId, string upn, CancellationToken ct = default);

    Task<SearchResult> SearchAsync(
        string tenantId,
        string baseDn,
        SearchScope scope,
        FilterNode filter,
        string[] attributes,
        int sizeLimit = 0,
        int timeLimitSeconds = 0,
        string continuationToken = null,
        int pageSize = 1000,
        bool includeDeleted = false,
        CancellationToken ct = default);

    Task CreateAsync(DirectoryObject obj, CancellationToken ct = default);

    Task UpdateAsync(DirectoryObject obj, CancellationToken ct = default);

    Task DeleteAsync(string tenantId, string dn, bool hardDelete = false, CancellationToken ct = default);

    Task MoveAsync(string tenantId, string oldDn, string newDn, CancellationToken ct = default);

    Task<long> GetNextUsnAsync(string tenantId, string domainDn, CancellationToken ct = default);

    /// <summary>
    /// Atomically claims a RID pool block for a DC instance. Uses optimistic concurrency
    /// (etag) to prevent two DCs from claiming overlapping pools.
    /// Returns the start of the claimed pool. The pool covers [returnValue, returnValue + poolSize - 1].
    /// </summary>
    Task<long> ClaimRidPoolAsync(string tenantId, string domainDn, int poolSize, CancellationToken ct = default);

    Task<IReadOnlyList<DirectoryObject>> GetChildrenAsync(string tenantId, string parentDn, CancellationToken ct = default);

    Task<IReadOnlyList<DirectoryObject>> GetByServicePrincipalNameAsync(string tenantId, string spn, CancellationToken ct = default);

    /// <summary>
    /// Batch read multiple objects by DN. Optimized implementations can use batch/parallel operations.
    /// </summary>
    Task<IReadOnlyList<DirectoryObject>> GetByDnsAsync(string tenantId, IEnumerable<string> dns, CancellationToken ct = default);
}

/// <summary>
/// Optional interface for stores that support streaming search results.
/// When implemented, the LDAP search handler streams entries to clients as they are
/// read from the database, keeping memory usage at O(page_size) instead of O(total_results).
/// </summary>
public interface IStreamingDirectoryStore
{
    /// <summary>
    /// Streams search results without loading all into memory.
    /// </summary>
    IAsyncEnumerable<DirectoryObject> SearchStreamAsync(
        string tenantId,
        string baseDn,
        SearchScope scope,
        FilterNode filter,
        string[] attributes,
        int sizeLimit = 0,
        int timeLimitSeconds = 0,
        bool includeDeleted = false,
        CancellationToken ct = default);
}

/// <summary>
/// Encapsulates search results with optional continuation token for paging.
/// </summary>
public class SearchResult
{
    public IReadOnlyList<DirectoryObject> Entries { get; init; } = [];
    public string ContinuationToken { get; init; }
    public int TotalEstimate { get; init; }
}

/// <summary>
/// Base class for LDAP filter AST nodes.
/// </summary>
public abstract class FilterNode
{
    public abstract T Accept<T>(IFilterVisitor<T> visitor);
}

public class AndFilterNode(List<FilterNode> children) : FilterNode
{
    public List<FilterNode> Children { get; } = children;
    public override T Accept<T>(IFilterVisitor<T> visitor) => visitor.VisitAnd(this);
}

public class OrFilterNode(List<FilterNode> children) : FilterNode
{
    public List<FilterNode> Children { get; } = children;
    public override T Accept<T>(IFilterVisitor<T> visitor) => visitor.VisitOr(this);
}

public class NotFilterNode(FilterNode child) : FilterNode
{
    public FilterNode Child { get; } = child;
    public override T Accept<T>(IFilterVisitor<T> visitor) => visitor.VisitNot(this);
}

public class EqualityFilterNode(string attribute, string value) : FilterNode
{
    public string Attribute { get; } = attribute;
    public string Value { get; } = value;
    public override T Accept<T>(IFilterVisitor<T> visitor) => visitor.VisitEquality(this);
}

public class SubstringFilterNode(string attribute, string initial, List<string> any, string final) : FilterNode
{
    public string Attribute { get; } = attribute;
    public string Initial { get; } = initial;
    public List<string> Any { get; } = any;
    public string Final { get; } = final;
    public override T Accept<T>(IFilterVisitor<T> visitor) => visitor.VisitSubstring(this);
}

public class GreaterOrEqualFilterNode(string attribute, string value) : FilterNode
{
    public string Attribute { get; } = attribute;
    public string Value { get; } = value;
    public override T Accept<T>(IFilterVisitor<T> visitor) => visitor.VisitGreaterOrEqual(this);
}

public class LessOrEqualFilterNode(string attribute, string value) : FilterNode
{
    public string Attribute { get; } = attribute;
    public string Value { get; } = value;
    public override T Accept<T>(IFilterVisitor<T> visitor) => visitor.VisitLessOrEqual(this);
}

public class PresenceFilterNode(string attribute) : FilterNode
{
    public string Attribute { get; } = attribute;
    public override T Accept<T>(IFilterVisitor<T> visitor) => visitor.VisitPresence(this);
}

public class ApproxMatchFilterNode(string attribute, string value) : FilterNode
{
    public string Attribute { get; } = attribute;
    public string Value { get; } = value;
    public override T Accept<T>(IFilterVisitor<T> visitor) => visitor.VisitApproxMatch(this);
}

public class ExtensibleMatchFilterNode(string matchingRule, string attribute, string value, bool dnAttributes) : FilterNode
{
    public string MatchingRule { get; } = matchingRule;
    public string Attribute { get; } = attribute;
    public string Value { get; } = value;
    public bool DnAttributes { get; } = dnAttributes;
    public override T Accept<T>(IFilterVisitor<T> visitor) => visitor.VisitExtensibleMatch(this);
}

/// <summary>
/// Visitor pattern for traversing LDAP filter ASTs.
/// </summary>
public interface IFilterVisitor<out T>
{
    T VisitAnd(AndFilterNode node);
    T VisitOr(OrFilterNode node);
    T VisitNot(NotFilterNode node);
    T VisitEquality(EqualityFilterNode node);
    T VisitSubstring(SubstringFilterNode node);
    T VisitGreaterOrEqual(GreaterOrEqualFilterNode node);
    T VisitLessOrEqual(LessOrEqualFilterNode node);
    T VisitPresence(PresenceFilterNode node);
    T VisitApproxMatch(ApproxMatchFilterNode node);
    T VisitExtensibleMatch(ExtensibleMatchFilterNode node);
}
