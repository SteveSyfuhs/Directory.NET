namespace Directory.Replication;

/// <summary>
/// Reports progress during a replication operation, including the current phase,
/// object counts, byte transfer statistics, and elapsed time.
/// </summary>
public class ReplicationProgress
{
    /// <summary>
    /// The current phase of the replication operation (e.g., "Binding", "Pulling changes", "Applying batch").
    /// </summary>
    public string Phase { get; init; } = string.Empty;

    /// <summary>
    /// The distinguished name of the naming context being replicated.
    /// </summary>
    public string NamingContext { get; init; } = string.Empty;

    /// <summary>
    /// The number of objects processed so far in this replication cycle.
    /// </summary>
    public int ObjectsProcessed { get; init; }

    /// <summary>
    /// The estimated total number of objects to replicate, if known.
    /// Null when the server does not provide NC size information.
    /// </summary>
    public int? ObjectsTotal { get; init; }

    /// <summary>
    /// The cumulative number of bytes transferred from the replication partner.
    /// </summary>
    public long BytesTransferred { get; init; }

    /// <summary>
    /// The elapsed wall-clock time since the replication operation started.
    /// </summary>
    public TimeSpan ElapsedTime { get; init; }

    /// <summary>
    /// A human-readable message describing the current state of the operation.
    /// </summary>
    public string Message { get; init; } = string.Empty;
}
