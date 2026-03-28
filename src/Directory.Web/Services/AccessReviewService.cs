using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.CosmosDb.Configuration;
using SearchScope = Directory.Core.Models.SearchScope;

namespace Directory.Web.Services;

public class AccessReview
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public AccessReviewScope Scope { get; set; } = new();
    public string ReviewerDn { get; set; } = "";
    public AccessReviewFrequency Frequency { get; set; }
    public int DurationDays { get; set; } = 14;
    public bool AutoRemoveOnDeny { get; set; }
    public AccessReviewStatus Status { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? DueDate { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class AccessReviewScope
{
    public string Type { get; set; } = "Group"; // Group, Role, OU, Application
    public string TargetDn { get; set; } = "";
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AccessReviewFrequency { OneTime, Monthly, Quarterly, SemiAnnual, Annual }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AccessReviewStatus { NotStarted, InProgress, Completed, Expired }

public class AccessReviewDecision
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ReviewId { get; set; } = "";
    public string UserDn { get; set; } = "";
    public string UserDisplayName { get; set; } = "";
    public string Decision { get; set; } = "NotReviewed"; // Approve, Deny, NotReviewed
    public string Justification { get; set; }
    public string ReviewerDn { get; set; } = "";
    public DateTimeOffset? DecidedAt { get; set; }
}

public class AccessReviewService
{
    private readonly ILogger<AccessReviewService> _logger;
    private readonly CosmosConfigurationStore _configStore;
    private readonly IDirectoryStore _directoryStore;
    private readonly IAuditService _auditService;

    private readonly ConcurrentDictionary<string, AccessReview> _reviews = new();
    private readonly ConcurrentDictionary<string, List<AccessReviewDecision>> _decisions = new();
    private bool _loaded;

    private const string ConfigScope = "cluster";
    private const string ConfigSection = "AccessReviews";
    private const string TenantId = "default";

    public AccessReviewService(
        ILogger<AccessReviewService> logger,
        CosmosConfigurationStore configStore,
        IDirectoryStore directoryStore,
        IAuditService auditService)
    {
        _logger = logger;
        _configStore = configStore;
        _directoryStore = directoryStore;
        _auditService = auditService;
    }

    public async Task EnsureLoadedAsync()
    {
        if (_loaded) return;
        await LoadFromStoreAsync();
        _loaded = true;
    }

    private async Task LoadFromStoreAsync()
    {
        try
        {
            var doc = await _configStore.GetSectionAsync(TenantId, ConfigScope, ConfigSection);
            if (doc != null)
            {
                if (doc.Values.TryGetValue("reviews", out var reviewsEl))
                {
                    var reviews = JsonSerializer.Deserialize<List<AccessReview>>(reviewsEl.GetRawText());
                    if (reviews != null)
                        foreach (var r in reviews) _reviews[r.Id] = r;
                }
                if (doc.Values.TryGetValue("decisions", out var decisionsEl))
                {
                    var decisions = JsonSerializer.Deserialize<Dictionary<string, List<AccessReviewDecision>>>(decisionsEl.GetRawText());
                    if (decisions != null)
                        foreach (var (k, v) in decisions) _decisions[k] = v;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load access reviews from store");
        }
    }

    private async Task PersistAsync()
    {
        try
        {
            var doc = await _configStore.GetSectionAsync(TenantId, ConfigScope, ConfigSection)
                      ?? new ConfigurationDocument
                      {
                          Id = $"{ConfigScope}::{ConfigSection}",
                          TenantId = TenantId, Scope = ConfigScope, Section = ConfigSection
                      };
            doc.Values["reviews"] = JsonSerializer.SerializeToElement(_reviews.Values.ToList());
            doc.Values["decisions"] = JsonSerializer.SerializeToElement(
                _decisions.ToDictionary(kv => kv.Key, kv => kv.Value));
            await _configStore.UpsertSectionAsync(doc);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to persist access reviews"); }
    }

    // --- CRUD ---

    public async Task<List<AccessReview>> GetAllReviewsAsync()
    {
        await EnsureLoadedAsync();
        return _reviews.Values.OrderByDescending(r => r.CreatedAt).ToList();
    }

    public async Task<AccessReview> GetReviewAsync(string id)
    {
        await EnsureLoadedAsync();
        return _reviews.GetValueOrDefault(id);
    }

    public async Task<AccessReview> CreateReviewAsync(AccessReview review)
    {
        await EnsureLoadedAsync();
        review.Status = AccessReviewStatus.NotStarted;
        review.CreatedAt = DateTimeOffset.UtcNow;
        _reviews[review.Id] = review;
        await PersistAsync();
        return review;
    }

    // --- Lifecycle ---

    public async Task<AccessReview> StartReviewAsync(string id)
    {
        await EnsureLoadedAsync();
        if (!_reviews.TryGetValue(id, out var review)) return null;
        if (review.Status != AccessReviewStatus.NotStarted) return review;

        review.Status = AccessReviewStatus.InProgress;
        review.StartedAt = DateTimeOffset.UtcNow;
        review.DueDate = DateTimeOffset.UtcNow.AddDays(review.DurationDays);

        // Populate decisions from current group/OU membership
        var decisions = await BuildDecisionsForScope(review);
        _decisions[id] = decisions;

        await PersistAsync();

        await _auditService.LogAsync(new AuditEntry
        {
            TenantId = TenantId, Action = "AccessReviewStarted",
            TargetDn = review.Scope.TargetDn,
            Details = new Dictionary<string, string> { ["reviewId"] = id, ["reviewName"] = review.Name }
        });

        return review;
    }

    public async Task<List<AccessReviewDecision>> GetDecisionsAsync(string reviewId)
    {
        await EnsureLoadedAsync();
        return _decisions.TryGetValue(reviewId, out var list)
            ? list.OrderBy(d => d.UserDn).ToList()
            : new();
    }

    public async Task<AccessReviewDecision> SubmitDecisionAsync(string reviewId, AccessReviewDecision decision)
    {
        await EnsureLoadedAsync();
        if (!_reviews.TryGetValue(reviewId, out var review)) return null;
        if (review.Status != AccessReviewStatus.InProgress) return null;

        if (!_decisions.TryGetValue(reviewId, out var decisions)) return null;

        var existing = decisions.FirstOrDefault(d => d.UserDn == decision.UserDn);
        if (existing == null) return null;

        existing.Decision = decision.Decision;
        existing.Justification = decision.Justification;
        existing.ReviewerDn = decision.ReviewerDn;
        existing.DecidedAt = DateTimeOffset.UtcNow;

        await PersistAsync();
        return existing;
    }

    public async Task<AccessReview> CompleteReviewAsync(string id)
    {
        await EnsureLoadedAsync();
        if (!_reviews.TryGetValue(id, out var review)) return null;
        if (review.Status != AccessReviewStatus.InProgress) return review;

        review.Status = AccessReviewStatus.Completed;
        review.CompletedAt = DateTimeOffset.UtcNow;

        // Apply deny decisions if auto-remove is enabled
        if (review.AutoRemoveOnDeny && _decisions.TryGetValue(id, out var decisions))
        {
            foreach (var d in decisions.Where(d => d.Decision == "Deny"))
            {
                await RemoveAccessAsync(review, d);
            }
        }

        await PersistAsync();

        await _auditService.LogAsync(new AuditEntry
        {
            TenantId = TenantId, Action = "AccessReviewCompleted",
            TargetDn = review.Scope.TargetDn,
            Details = new Dictionary<string, string> { ["reviewId"] = id, ["reviewName"] = review.Name }
        });

        return review;
    }

    public async Task<List<AccessReview>> GetPendingReviewsAsync(string reviewerDn)
    {
        await EnsureLoadedAsync();
        return _reviews.Values
            .Where(r => r.Status == AccessReviewStatus.InProgress &&
                        r.ReviewerDn.Equals(reviewerDn, StringComparison.OrdinalIgnoreCase))
            .OrderBy(r => r.DueDate)
            .ToList();
    }

    // --- Helpers ---

    private async Task<List<AccessReviewDecision>> BuildDecisionsForScope(AccessReview review)
    {
        var decisions = new List<AccessReviewDecision>();

        if (review.Scope.Type == "Group")
        {
            var group = await _directoryStore.GetByDnAsync(TenantId, review.Scope.TargetDn);
            if (group != null)
            {
                foreach (var memberDn in group.Member)
                {
                    var member = await _directoryStore.GetByDnAsync(TenantId, memberDn);
                    decisions.Add(new AccessReviewDecision
                    {
                        ReviewId = review.Id,
                        UserDn = memberDn,
                        UserDisplayName = member?.DisplayName ?? member?.SAMAccountName ?? memberDn,
                    });
                }
            }
        }
        else if (review.Scope.Type == "OU")
        {
            var children = await _directoryStore.GetChildrenAsync(TenantId, review.Scope.TargetDn);
            foreach (var child in children.Where(c => c.ObjectClass.Contains("user")))
            {
                decisions.Add(new AccessReviewDecision
                {
                    ReviewId = review.Id,
                    UserDn = child.DistinguishedName,
                    UserDisplayName = child.DisplayName ?? child.SAMAccountName ?? child.DistinguishedName,
                });
            }
        }

        return decisions;
    }

    private async Task RemoveAccessAsync(AccessReview review, AccessReviewDecision decision)
    {
        try
        {
            if (review.Scope.Type == "Group")
            {
                var group = await _directoryStore.GetByDnAsync(TenantId, review.Scope.TargetDn);
                if (group != null && group.Member.Contains(decision.UserDn))
                {
                    group.Member.Remove(decision.UserDn);
                    await _directoryStore.UpdateAsync(group);

                    await _auditService.LogAsync(new AuditEntry
                    {
                        TenantId = TenantId, Action = "AccessReviewDeny",
                        TargetDn = decision.UserDn,
                        Details = new Dictionary<string, string>
                        {
                            ["reviewId"] = review.Id,
                            ["groupDn"] = review.Scope.TargetDn,
                            ["justification"] = decision.Justification ?? ""
                        }
                    });

                    _logger.LogInformation("Access review {ReviewId}: removed {User} from {Group}",
                        review.Id, decision.UserDn, review.Scope.TargetDn);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove access for {User} in review {Review}",
                decision.UserDn, review.Id);
        }
    }
}
