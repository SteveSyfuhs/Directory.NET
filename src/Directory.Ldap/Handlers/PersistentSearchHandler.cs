using System.Collections.Concurrent;
using System.Threading.Channels;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Ldap.Protocol;
using Directory.Ldap.Protocol.Messages;
using Directory.Ldap.Server;
using Microsoft.Extensions.Logging;

namespace Directory.Ldap.Handlers;

/// <summary>
/// Implements LDAP Persistent Search (draft-ietf-ldapext-psearch / RFC-like).
///
/// When a search request includes the Persistent Search control (OID 2.16.840.1.113730.3.4.3),
/// the connection is held open after the initial results are sent, and subsequent changes
/// matching the search criteria are pushed to the client as additional SearchResultEntry messages.
///
/// Control value: SEQUENCE { changeTypes INTEGER, changesOnly BOOLEAN, returnECs BOOLEAN }
///   - changeTypes: bitmask of change types to monitor (add=1, delete=2, modify=4, modDN=8)
///   - changesOnly: if true, don't send initial results; only send changes
///   - returnECs: if true, include Entry Change Notification control (2.16.840.1.113730.3.4.7)
///     with each result indicating the change type
///
/// Uses an in-memory event bus (channel-based) to receive change notifications from the
/// directory store layer. Subscriptions are per-connection and cleaned up on disconnect.
/// </summary>
public class PersistentSearchHandler : IDisposable
{
    private readonly ILogger<PersistentSearchHandler> _logger;

    /// <summary>
    /// Active persistent search subscriptions, keyed by a unique subscription ID.
    /// </summary>
    private readonly ConcurrentDictionary<string, PersistentSearchSubscription> _subscriptions = new();

    public PersistentSearchHandler(ILogger<PersistentSearchHandler> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Register a persistent search subscription for the given connection.
    /// The subscription will receive change events matching the criteria.
    /// </summary>
    public PersistentSearchSubscription Subscribe(
        int messageId,
        string baseDn,
        SearchScope scope,
        FilterNode filter,
        PersistentSearchControl control,
        ILdapResponseWriter writer,
        LdapConnectionState state,
        CancellationToken ct)
    {
        var subscriptionId = $"{state.RemoteEndPoint}:{messageId}:{Guid.NewGuid():N}";

        var subscription = new PersistentSearchSubscription
        {
            Id = subscriptionId,
            MessageId = messageId,
            BaseDn = baseDn,
            Scope = scope,
            Filter = filter,
            ChangeTypes = control.ChangeTypes,
            ChangesOnly = control.ChangesOnly,
            ReturnEntryChangeControls = control.ReturnEntryChangeControls,
            Writer = writer,
            State = state,
        };

        _subscriptions[subscriptionId] = subscription;

        _logger.LogInformation(
            "Persistent search registered: id={Id}, base={Base}, scope={Scope}, changeTypes={Types}, changesOnly={ChangesOnly}",
            subscriptionId, baseDn, scope, control.ChangeTypes, control.ChangesOnly);

        // Start the background push loop for this subscription
        subscription.ProcessingTask = Task.Run(
            () => ProcessSubscriptionAsync(subscription, ct), ct);

        return subscription;
    }

    /// <summary>
    /// Remove a persistent search subscription (called on connection close or abandon).
    /// </summary>
    public void Unsubscribe(string subscriptionId)
    {
        if (_subscriptions.TryRemove(subscriptionId, out var subscription))
        {
            subscription.Cancel();
            _logger.LogDebug("Persistent search unsubscribed: {Id}", subscriptionId);
        }
    }

    /// <summary>
    /// Remove all subscriptions for a given connection endpoint.
    /// </summary>
    public void UnsubscribeAll(string remoteEndPoint)
    {
        var toRemove = _subscriptions
            .Where(kv => kv.Value.State.RemoteEndPoint == remoteEndPoint)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var id in toRemove)
        {
            Unsubscribe(id);
        }

        if (toRemove.Count > 0)
        {
            _logger.LogDebug("Removed {Count} persistent search subscriptions for {Endpoint}",
                toRemove.Count, remoteEndPoint);
        }
    }

    /// <summary>
    /// Publish a change event to all matching persistent search subscriptions.
    /// Called by the directory store or change notification layer when an object changes.
    /// </summary>
    public async Task PublishChangeAsync(ChangeNotification notification)
    {
        foreach (var (_, subscription) in _subscriptions)
        {
            // Check if the subscription monitors this change type
            if ((subscription.ChangeTypes & (int)notification.ChangeType) == 0)
                continue;

            // Check if the changed object is within the subscription's search scope
            if (!IsInScope(notification.ObjectDn, subscription.BaseDn, subscription.Scope))
                continue;

            // Enqueue the notification for processing
            try
            {
                subscription.NotificationChannel.Writer.TryWrite(notification);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to enqueue change notification for subscription {Id}",
                    subscription.Id);
            }
        }
    }

    /// <summary>
    /// Process queued change notifications for a single subscription.
    /// Runs as a background task for the lifetime of the subscription.
    /// </summary>
    private async Task ProcessSubscriptionAsync(
        PersistentSearchSubscription subscription,
        CancellationToken ct)
    {
        var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, subscription.CancellationToken);

        try
        {
            await foreach (var notification in subscription.NotificationChannel.Reader.ReadAllAsync(combinedCts.Token))
            {
                try
                {
                    await SendChangeEntryAsync(subscription, notification, combinedCts.Token);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error sending persistent search entry for subscription {Id}",
                        subscription.Id);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Persistent search processing ended for subscription {Id}", subscription.Id);
        }
        finally
        {
            _subscriptions.TryRemove(subscription.Id, out _);
            combinedCts.Dispose();
        }
    }

    /// <summary>
    /// Send a SearchResultEntry for a changed object, optionally with the
    /// Entry Change Notification control.
    /// </summary>
    private async Task SendChangeEntryAsync(
        PersistentSearchSubscription subscription,
        ChangeNotification notification,
        CancellationToken ct)
    {
        var entry = new SearchResultEntry
        {
            ObjectName = notification.ObjectDn,
        };

        // Add basic attributes from the notification
        if (notification.Attributes != null)
        {
            foreach (var (name, values) in notification.Attributes)
            {
                entry.Attributes.Add((name, values));
            }
        }

        // Build response controls
        List<LdapControl> controls = null;

        if (subscription.ReturnEntryChangeControls)
        {
            var ecnValue = LdapControlHandler.BuildEntryChangeNotification(
                (int)notification.ChangeType,
                notification.PreviousDn,
                notification.ChangeNumber);

            controls =
            [
                new LdapControl
                {
                    Oid = LdapConstants.OidEntryChangeNotification,
                    Value = ecnValue,
                }
            ];
        }

        await subscription.Writer.WriteMessageAsync(
            subscription.MessageId, entry.Encode(), controls, ct);

        _logger.LogTrace("Persistent search sent change entry: {DN}, type={Type}, subscription={Id}",
            notification.ObjectDn, notification.ChangeType, subscription.Id);
    }

    /// <summary>
    /// Check if a DN is within the scope of a search base.
    /// </summary>
    private static bool IsInScope(string objectDn, string baseDn, SearchScope scope)
    {
        if (string.IsNullOrEmpty(objectDn) || string.IsNullOrEmpty(baseDn))
            return false;

        return scope switch
        {
            SearchScope.BaseObject =>
                string.Equals(objectDn, baseDn, StringComparison.OrdinalIgnoreCase),

            SearchScope.SingleLevel =>
                objectDn.EndsWith($",{baseDn}", StringComparison.OrdinalIgnoreCase) &&
                !objectDn[..^(baseDn.Length + 1)].Contains(','),

            SearchScope.WholeSubtree =>
                string.Equals(objectDn, baseDn, StringComparison.OrdinalIgnoreCase) ||
                objectDn.EndsWith($",{baseDn}", StringComparison.OrdinalIgnoreCase),

            _ => false,
        };
    }

    /// <summary>
    /// Get the count of active persistent search subscriptions.
    /// </summary>
    public int ActiveSubscriptionCount => _subscriptions.Count;

    public void Dispose()
    {
        foreach (var (_, subscription) in _subscriptions)
        {
            subscription.Cancel();
        }
        _subscriptions.Clear();
    }
}

/// <summary>
/// Represents an active persistent search subscription tied to a client connection.
/// </summary>
public class PersistentSearchSubscription
{
    public string Id { get; init; } = string.Empty;
    public int MessageId { get; init; }
    public string BaseDn { get; init; } = string.Empty;
    public SearchScope Scope { get; init; }
    public FilterNode Filter { get; init; }
    public int ChangeTypes { get; init; }
    public bool ChangesOnly { get; init; }
    public bool ReturnEntryChangeControls { get; init; }
    public ILdapResponseWriter Writer { get; init; } = null;
    public LdapConnectionState State { get; init; } = null;
    public Task ProcessingTask { get; set; }

    /// <summary>
    /// Channel for queuing change notifications to this subscription.
    /// Bounded to prevent memory issues if the client is slow to consume.
    /// </summary>
    public Channel<ChangeNotification> NotificationChannel { get; } =
        Channel.CreateBounded<ChangeNotification>(new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

    private readonly CancellationTokenSource _cts = new();
    public CancellationToken CancellationToken => _cts.Token;

    public void Cancel()
    {
        _cts.Cancel();
        NotificationChannel.Writer.TryComplete();
    }
}

/// <summary>
/// Change types for persistent search, matching the bitmask values in the control.
/// </summary>
[Flags]
public enum ChangeType
{
    Add = 1,
    Delete = 2,
    Modify = 4,
    ModDN = 8,
}

/// <summary>
/// Represents a directory change event to be published to persistent search subscribers.
/// </summary>
public class ChangeNotification
{
    /// <summary>DN of the object that changed.</summary>
    public string ObjectDn { get; init; } = string.Empty;

    /// <summary>Type of change that occurred.</summary>
    public ChangeType ChangeType { get; init; }

    /// <summary>For modDN operations, the previous DN before the rename/move.</summary>
    public string PreviousDn { get; init; }

    /// <summary>Monotonically increasing change number (USN).</summary>
    public long? ChangeNumber { get; init; }

    /// <summary>
    /// Attributes of the changed object to include in the SearchResultEntry.
    /// If null, the subscriber should fetch the current object from the store.
    /// </summary>
    public List<(string Name, List<object> Values)> Attributes { get; init; }
}
