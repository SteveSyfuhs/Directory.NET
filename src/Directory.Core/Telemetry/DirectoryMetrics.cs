using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Directory.Core.Telemetry;

public static class DirectoryMetrics
{
    public static readonly string ServiceName = "Directory.NET";
    public static readonly Meter Meter = new(ServiceName, "1.0.0");
    public static readonly ActivitySource ActivitySource = new(ServiceName);

    // Counters
    public static readonly Counter<long> ObjectsCreated = Meter.CreateCounter<long>(
        "directory.objects.created", "objects", "Number of directory objects created");
    public static readonly Counter<long> ObjectsModified = Meter.CreateCounter<long>(
        "directory.objects.modified", "objects", "Number of directory objects modified");
    public static readonly Counter<long> ObjectsDeleted = Meter.CreateCounter<long>(
        "directory.objects.deleted", "objects", "Number of directory objects deleted");
    public static readonly Counter<long> AuthAttempts = Meter.CreateCounter<long>(
        "directory.auth.attempts", "attempts", "Number of authentication attempts");
    public static readonly Counter<long> SearchOperations = Meter.CreateCounter<long>(
        "directory.search.operations", "operations", "Number of search operations");

    // Histograms
    public static readonly Histogram<double> CosmosLatency = Meter.CreateHistogram<double>(
        "directory.cosmos.latency", "ms", "Cosmos DB operation latency in milliseconds");
    public static readonly Histogram<double> ApiRequestDuration = Meter.CreateHistogram<double>(
        "directory.api.duration", "ms", "API request duration in milliseconds");
}
