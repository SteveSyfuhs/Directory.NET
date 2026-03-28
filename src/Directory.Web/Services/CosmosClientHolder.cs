using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using Directory.CosmosDb;

namespace Directory.Web.Services;

/// <summary>
/// Holds the CosmosClient instance, supporting lazy initialization.
/// On first run the connection string may be blank — the holder starts empty and is
/// configured later by the setup wizard. Once configured, all services that depend on
/// CosmosClient resolve the real instance through this holder.
/// </summary>
public sealed class CosmosClientHolder : IDisposable
{
    private readonly object _lock = new();
    private CosmosClient _client;
    private string _connectionString;
    private string _databaseName;
    private readonly ILogger<CosmosClientHolder> _logger;

    public CosmosClientHolder(IOptions<CosmosDbOptions> options, ILogger<CosmosClientHolder> logger)
    {
        _logger = logger;

        var connStr = options.Value.ConnectionString;
        _databaseName = options.Value.DatabaseName;

        // Only create the client if we have a real connection string
        if (!string.IsNullOrWhiteSpace(connStr) && !connStr.Contains("localhost:8081"))
        {
            TryInitialize(connStr, _databaseName);
        }
        else
        {
            // Also try to connect to the emulator — it may or may not be running
            TryInitialize(connStr, _databaseName);
        }
    }

    /// <summary>
    /// Whether a CosmosClient has been successfully created.
    /// </summary>
    public bool IsConfigured
    {
        get { lock (_lock) return _client != null; }
    }

    public string DatabaseName
    {
        get { lock (_lock) return _databaseName; }
    }

    /// <summary>
    /// Gets the CosmosClient. Throws if not yet configured.
    /// </summary>
    public CosmosClient Client
    {
        get
        {
            lock (_lock)
            {
                return _client ?? throw new InvalidOperationException(
                    "Cosmos DB is not configured. Complete the setup wizard to configure the database connection.");
            }
        }
    }

    /// <summary>
    /// Returns the CosmosClient or null if not yet configured.
    /// </summary>
    public CosmosClient ClientOrDefault
    {
        get { lock (_lock) return _client; }
    }

    /// <summary>
    /// Configure (or reconfigure) the CosmosClient with a new connection string.
    /// Called by the setup wizard after the user provides and validates connection info.
    /// </summary>
    public void Configure(string connectionString, string databaseName)
    {
        lock (_lock)
        {
            // Dispose the old client if any
            _client?.Dispose();
            _client = null;

            _connectionString = connectionString;
            _databaseName = databaseName;

            _client = new CosmosClient(connectionString, new CosmosClientOptions
            {
                SerializerOptions = new CosmosSerializationOptions
                {
                    PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase,
                },
                ConnectionMode = ConnectionMode.Direct,
                MaxRetryAttemptsOnRateLimitedRequests = 9,
                MaxRetryWaitTimeOnRateLimitedRequests = TimeSpan.FromSeconds(30),
            });

            _logger.LogInformation("CosmosClient configured for database {Database}", databaseName);
        }
    }

    private void TryInitialize(string connectionString, string databaseName)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        try
        {
            _client = new CosmosClient(connectionString, new CosmosClientOptions
            {
                SerializerOptions = new CosmosSerializationOptions
                {
                    PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase,
                },
                ConnectionMode = ConnectionMode.Direct,
                MaxRetryAttemptsOnRateLimitedRequests = 9,
                MaxRetryWaitTimeOnRateLimitedRequests = TimeSpan.FromSeconds(30),
            });
            _connectionString = connectionString;
            _databaseName = databaseName;
            _logger.LogInformation("CosmosClient initialized from configuration");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not initialize CosmosClient from configuration — database setup required");
            _client = null;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _client?.Dispose();
            _client = null;
        }
    }
}
