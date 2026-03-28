using Microsoft.Extensions.Logging;

namespace Directory.Core.Telemetry;

/// <summary>
/// Well-known event IDs for structured Windows Event Log entries.
/// </summary>
public static class EventIds
{
    // Service lifecycle (1000-1099)
    public static readonly EventId ServiceStarting = new(1000, "ServiceStarting");
    public static readonly EventId ServiceStarted = new(1001, "ServiceStarted");
    public static readonly EventId ServiceStopping = new(1002, "ServiceStopping");
    public static readonly EventId ServiceStopped = new(1003, "ServiceStopped");

    // Protocol servers (1100-1199)
    public static readonly EventId ProtocolServerStarted = new(1100, "ProtocolServerStarted");
    public static readonly EventId ProtocolServerFailed = new(1101, "ProtocolServerFailed");
    public static readonly EventId PortConflict = new(1102, "PortConflict");

    // Authentication (2000-2099)
    public static readonly EventId AuthSuccess = new(2000, "AuthenticationSuccess");
    public static readonly EventId AuthFailure = new(2001, "AuthenticationFailure");
    public static readonly EventId AccountLocked = new(2002, "AccountLocked");

    // Directory operations (3000-3099)
    public static readonly EventId ObjectCreated = new(3000, "ObjectCreated");
    public static readonly EventId ObjectModified = new(3001, "ObjectModified");
    public static readonly EventId ObjectDeleted = new(3002, "ObjectDeleted");
    public static readonly EventId ObjectMoved = new(3003, "ObjectMoved");
    public static readonly EventId ObjectRestored = new(3004, "ObjectRestored");

    // Replication (4000-4099)
    public static readonly EventId ReplicationStarted = new(4000, "ReplicationStarted");
    public static readonly EventId ReplicationCompleted = new(4001, "ReplicationCompleted");
    public static readonly EventId ReplicationFailed = new(4002, "ReplicationFailed");

    // Configuration (5000-5099)
    public static readonly EventId ConfigurationChanged = new(5000, "ConfigurationChanged");
    public static readonly EventId SchemaModified = new(5001, "SchemaModified");

    // Health (6000-6099)
    public static readonly EventId HealthCheckPassed = new(6000, "HealthCheckPassed");
    public static readonly EventId HealthCheckFailed = new(6001, "HealthCheckFailed");
    public static readonly EventId CosmosDbLatencyHigh = new(6002, "CosmosDbLatencyHigh");
}
