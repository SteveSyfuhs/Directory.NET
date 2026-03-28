namespace Directory.CosmosDb;

public class CosmosDbOptions
{
    public const string SectionName = "CosmosDb";

    public string ConnectionString { get; set; } = "AccountEndpoint=https://localhost:8081;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";
    public string DatabaseName { get; set; } = "DirectoryService";
    public int DefaultThroughput { get; set; } = 400;

    /// <summary>Maximum RU/s for autoscale. Default 4000.</summary>
    public int MaxAutoscaleThroughput { get; set; } = 4000;

    /// <summary>Enable server-side retries for throttled requests.</summary>
    public bool EnableServerRetry { get; set; } = true;

    /// <summary>Preferred regions for multi-region reads.</summary>
    public List<string> PreferredRegions { get; set; } = [];
}
