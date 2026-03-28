using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Directory.CosmosDb.Configuration;

namespace Directory.Web.Services;

public class WebhookSubscription
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string Secret { get; set; } = "";
    public List<string> Events { get; set; } = new();
    public bool IsEnabled { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastDeliveryAt { get; set; }
    public string LastDeliveryStatus { get; set; } = "";
    public int FailureCount { get; set; }
}

public class WebhookEvent
{
    public string EventType { get; set; } = "";
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public string ActorDn { get; set; } = "";
    public string TargetDn { get; set; } = "";
    public Dictionary<string, object> Data { get; set; } = new();
}

public class WebhookDeliveryRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SubscriptionId { get; set; } = "";
    public string EventType { get; set; } = "";
    public DateTimeOffset Timestamp { get; set; }
    public int StatusCode { get; set; }
    public string Status { get; set; } = "";
    public string ErrorMessage { get; set; } = "";
    public int Attempt { get; set; }
}

public class WebhookService
{
    private readonly ILogger<WebhookService> _logger;
    private readonly CosmosConfigurationStore _configStore;
    private readonly IHttpClientFactory _httpClientFactory;

    private readonly ConcurrentDictionary<string, WebhookSubscription> _subscriptions = new();
    private readonly ConcurrentDictionary<string, List<WebhookDeliveryRecord>> _deliveries = new();
    private bool _loaded;

    private const string ConfigScope = "cluster";
    private const string ConfigSection = "Webhooks";
    private const int MaxDeliveriesPerSubscription = 100;
    private const int MaxRetries = 3;

    public static readonly Dictionary<string, string[]> AvailableEventTypes = new()
    {
        ["User"] = new[]
        {
            "user.created", "user.modified", "user.deleted",
            "user.locked", "user.unlocked", "user.passwordChanged"
        },
        ["Group"] = new[]
        {
            "group.created", "group.modified", "group.deleted",
            "group.memberAdded", "group.memberRemoved"
        },
        ["Computer"] = new[] { "computer.created", "computer.deleted" },
        ["OU"] = new[] { "ou.created", "ou.deleted" },
        ["DNS"] = new[] { "dns.zoneCreated", "dns.recordChanged" },
        ["Certificate"] = new[] { "certificate.expiring", "certificate.issued" },
        ["Backup"] = new[] { "backup.completed", "backup.failed" }
    };

    public WebhookService(
        ILogger<WebhookService> logger,
        CosmosConfigurationStore configStore,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _configStore = configStore;
        _httpClientFactory = httpClientFactory;
    }

    public async Task EnsureLoadedAsync()
    {
        if (_loaded) return;
        await LoadFromStoreAsync();
    }

    // --- Subscription CRUD ---

    public async Task<List<WebhookSubscription>> GetAllSubscriptions()
    {
        await EnsureLoadedAsync();
        return _subscriptions.Values.OrderBy(s => s.Name).ToList();
    }

    public async Task<WebhookSubscription> GetSubscription(string id)
    {
        await EnsureLoadedAsync();
        return _subscriptions.GetValueOrDefault(id);
    }

    public async Task<WebhookSubscription> CreateSubscription(WebhookSubscription subscription)
    {
        await EnsureLoadedAsync();
        subscription.CreatedAt = DateTimeOffset.UtcNow;
        if (string.IsNullOrEmpty(subscription.Secret))
            subscription.Secret = GenerateSecret();
        _subscriptions[subscription.Id] = subscription;
        await PersistAsync();
        return subscription;
    }

    public async Task<WebhookSubscription> UpdateSubscription(string id, WebhookSubscription updated)
    {
        await EnsureLoadedAsync();
        if (!_subscriptions.ContainsKey(id)) return null;
        updated.Id = id;
        _subscriptions[id] = updated;
        await PersistAsync();
        return updated;
    }

    public async Task<bool> DeleteSubscription(string id)
    {
        await EnsureLoadedAsync();
        if (!_subscriptions.TryRemove(id, out _)) return false;
        _deliveries.TryRemove(id, out _);
        await PersistAsync();
        return true;
    }

    public List<WebhookDeliveryRecord> GetDeliveries(string subscriptionId)
    {
        return _deliveries.TryGetValue(subscriptionId, out var list)
            ? list.OrderByDescending(d => d.Timestamp).ToList()
            : new();
    }

    /// <summary>
    /// Remove delivery records older than the specified cutoff across all subscriptions.
    /// Called by the data retention service.
    /// </summary>
    public int PurgeDeliveryRecordsBefore(DateTimeOffset cutoff)
    {
        int total = 0;
        foreach (var kvp in _deliveries)
        {
            var list = kvp.Value;
            lock (list)
            {
                var removed = list.RemoveAll(r => r.Timestamp < cutoff);
                total += removed;
            }
        }
        return total;
    }

    // --- Event Dispatch ---

    /// <summary>
    /// Fire an event to all matching subscriptions. Call this from other services when events occur.
    /// </summary>
    public async Task DispatchEventAsync(WebhookEvent webhookEvent)
    {
        await EnsureLoadedAsync();

        var matching = _subscriptions.Values
            .Where(s => s.IsEnabled && s.Events.Contains(webhookEvent.EventType))
            .ToList();

        foreach (var sub in matching)
        {
            _ = Task.Run(async () =>
            {
                await DeliverWithRetry(sub, webhookEvent);
            });
        }
    }

    /// <summary>
    /// Send a test event to a specific subscription.
    /// </summary>
    public async Task<WebhookDeliveryRecord> SendTestEvent(string subscriptionId)
    {
        await EnsureLoadedAsync();

        if (!_subscriptions.TryGetValue(subscriptionId, out var sub))
            throw new KeyNotFoundException($"Subscription {subscriptionId} not found");

        var testEvent = new WebhookEvent
        {
            EventType = "test.ping",
            Timestamp = DateTimeOffset.UtcNow,
            ActorDn = "CN=System,DC=directory,DC=net",
            TargetDn = "",
            Data = new() { ["message"] = "Test webhook delivery" }
        };

        return await DeliverWithRetry(sub, testEvent);
    }

    // --- Delivery ---

    private async Task<WebhookDeliveryRecord> DeliverWithRetry(WebhookSubscription sub, WebhookEvent evt)
    {
        WebhookDeliveryRecord lastRecord = null;

        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            lastRecord = await DeliverOnce(sub, evt, attempt);
            AddDeliveryRecord(sub.Id, lastRecord);

            if (lastRecord.Status == "Success")
            {
                sub.LastDeliveryAt = lastRecord.Timestamp;
                sub.LastDeliveryStatus = "Success";
                sub.FailureCount = 0;
                await PersistAsync();
                return lastRecord;
            }

            if (attempt < MaxRetries)
            {
                // Exponential backoff: 1s, 4s, 9s
                await Task.Delay(TimeSpan.FromSeconds(attempt * attempt));
            }
        }

        sub.LastDeliveryAt = DateTimeOffset.UtcNow;
        sub.LastDeliveryStatus = "Failed";
        sub.FailureCount++;
        await PersistAsync();

        return lastRecord;
    }

    private async Task<WebhookDeliveryRecord> DeliverOnce(WebhookSubscription sub, WebhookEvent evt, int attempt)
    {
        var record = new WebhookDeliveryRecord
        {
            SubscriptionId = sub.Id,
            EventType = evt.EventType,
            Timestamp = DateTimeOffset.UtcNow,
            Attempt = attempt
        };

        try
        {
            var client = _httpClientFactory.CreateClient("Webhooks");
            client.Timeout = TimeSpan.FromSeconds(10);

            var body = JsonSerializer.Serialize(evt, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            var request = new HttpRequestMessage(HttpMethod.Post, sub.Url)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };

            // Compute HMAC-SHA256 signature
            if (!string.IsNullOrEmpty(sub.Secret))
            {
                var signature = ComputeHmacSignature(body, sub.Secret);
                request.Headers.Add("X-Webhook-Signature", signature);
            }

            request.Headers.Add("X-Webhook-Event", evt.EventType);

            var response = await client.SendAsync(request);
            record.StatusCode = (int)response.StatusCode;
            record.Status = response.IsSuccessStatusCode ? "Success" : "Failed";
            if (!response.IsSuccessStatusCode)
                record.ErrorMessage = $"HTTP {record.StatusCode}";
        }
        catch (Exception ex)
        {
            record.Status = "Failed";
            record.ErrorMessage = ex.Message;
            _logger.LogWarning(ex, "Webhook delivery to {Url} failed (attempt {Attempt})", sub.Url, attempt);
        }

        return record;
    }

    private static string ComputeHmacSignature(string payload, string secret)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var hash = HMACSHA256.HashData(keyBytes, payloadBytes);
        return $"sha256={Convert.ToHexStringLower(hash)}";
    }

    public static string GenerateSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes);
    }

    // --- Delivery History ---

    private void AddDeliveryRecord(string subscriptionId, WebhookDeliveryRecord record)
    {
        var list = _deliveries.GetOrAdd(subscriptionId, _ => new List<WebhookDeliveryRecord>());
        lock (list)
        {
            list.Add(record);
            if (list.Count > MaxDeliveriesPerSubscription)
                list.RemoveRange(0, list.Count - MaxDeliveriesPerSubscription);
        }
    }

    // --- Persistence ---

    private async Task LoadFromStoreAsync()
    {
        try
        {
            var doc = await _configStore.GetSectionAsync("default", ConfigScope, ConfigSection);
            if (doc != null && doc.Values.TryGetValue("subscriptions", out var subsElement))
            {
                var subs = JsonSerializer.Deserialize<List<WebhookSubscription>>(subsElement.GetRawText());
                if (subs != null)
                {
                    foreach (var s in subs)
                        _subscriptions[s.Id] = s;
                }
            }
            _loaded = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load webhook subscriptions from configuration store");
            _loaded = true;
        }
    }

    private async Task PersistAsync()
    {
        try
        {
            var subsJson = JsonSerializer.SerializeToElement(_subscriptions.Values.ToList());

            var doc = await _configStore.GetSectionAsync("default", ConfigScope, ConfigSection)
                      ?? new ConfigurationDocument
                      {
                          Id = $"{ConfigScope}::{ConfigSection}",
                          TenantId = "default",
                          Scope = ConfigScope,
                          Section = ConfigSection
                      };

            doc.Values["subscriptions"] = subsJson;
            await _configStore.UpsertSectionAsync(doc);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist webhook subscriptions");
        }
    }
}
