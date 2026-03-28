namespace Directory.Web.Services;

/// <summary>
/// Tracks whether the domain has been provisioned and whether the database is configured.
/// Checked at startup and exposed via API for the frontend.
/// </summary>
public class SetupStateService
{
    /// <summary>
    /// Whether a valid Cosmos DB connection has been established.
    /// False on first run when the user hasn't configured a connection string yet.
    /// </summary>
    public bool IsDatabaseConfigured { get; set; }

    /// <summary>
    /// Whether the domain has been fully provisioned with directory objects.
    /// </summary>
    public bool IsProvisioned { get; set; }

    public bool IsProvisioning { get; set; }
    public string ProvisioningError { get; set; }
    public int ProvisioningProgress { get; set; } // 0-100
    public string ProvisioningPhase { get; set; }  // e.g., "Creating domain groups..."

    // ── Replica provisioning state ──────────────────────────────────────────

    /// <summary>
    /// Whether the current provisioning run is a replica (join existing domain) rather than a new forest.
    /// </summary>
    public bool IsReplicaMode { get; set; }

    /// <summary>
    /// The naming context currently being replicated (e.g., "Schema", "Configuration", "Domain").
    /// </summary>
    public string ReplicationCurrentNc { get; set; }

    /// <summary>
    /// Number of directory objects replicated so far in the current naming context.
    /// </summary>
    public int ReplicationObjectsProcessed { get; set; }

    /// <summary>
    /// Estimated total objects in the current naming context, if known.
    /// </summary>
    public int? ReplicationObjectsTotal { get; set; }

    /// <summary>
    /// Cumulative bytes transferred during the current replication run.
    /// </summary>
    public long ReplicationBytesTransferred { get; set; }
}
