using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.CosmosDb.Configuration;

namespace Directory.Web.Services;

public class HrSyncConfiguration
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public HrSyncSourceType SourceType { get; set; }
    public string EndpointUrl { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public Dictionary<string, string> AttributeMapping { get; set; } = new();
    public string TargetOu { get; set; } = "";
    public bool AutoCreateUsers { get; set; } = true;
    public bool AutoDisableOnTermination { get; set; } = true;
    public bool AutoMoveOnDepartmentChange { get; set; }
    public string CronSchedule { get; set; } = "0 */6 * * *";
    public bool IsEnabled { get; set; }
    public DateTimeOffset? LastSyncAt { get; set; }
    public string LastSyncStatus { get; set; }
}

public enum HrSyncSourceType
{
    GenericApi,
    CsvUpload,
    Workday,
    BambooHR,
    SapSuccessFactors
}

public class HrSyncHistoryEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ConfigurationId { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public string Status { get; set; } = "Running";
    public int UsersCreated { get; set; }
    public int UsersUpdated { get; set; }
    public int UsersDisabled { get; set; }
    public int UsersMoved { get; set; }
    public int Errors { get; set; }
    public List<string> ErrorDetails { get; set; } = new();
}

public class HrSyncPreviewResult
{
    public List<HrSyncPreviewAction> Actions { get; set; } = new();
    public int TotalRecords { get; set; }
    public int NewUsers { get; set; }
    public int Updates { get; set; }
    public int Terminations { get; set; }
    public int Moves { get; set; }
    public int NoChange { get; set; }
}

public class HrSyncPreviewAction
{
    public string Action { get; set; } = "";
    public string EmployeeId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string CurrentDn { get; set; }
    public Dictionary<string, string> Changes { get; set; } = new();
}

public class HrSyncStatus
{
    public string ConfigurationId { get; set; } = "";
    public bool IsRunning { get; set; }
    public DateTimeOffset? LastSyncAt { get; set; }
    public string LastSyncStatus { get; set; }
    public HrSyncHistoryEntry CurrentRun { get; set; }
}

public class HrEmployee
{
    public string EmployeeId { get; set; } = "";
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string DisplayName { get; set; }
    public string Email { get; set; }
    public string Department { get; set; }
    public string Title { get; set; }
    public string Manager { get; set; }
    public string Company { get; set; }
    public string Status { get; set; }
    public Dictionary<string, string> CustomFields { get; set; } = new();
}

public class HrSyncService
{
    private readonly ILogger<HrSyncService> _logger;
    private readonly IDirectoryStore _store;
    private readonly INamingContextService _ncService;
    private readonly IRidAllocator _ridAllocator;
    private readonly IUserAccountControlService _uacService;
    private readonly CosmosConfigurationStore _configStore;
    private readonly IHttpClientFactory _httpClientFactory;

    private readonly ConcurrentDictionary<string, HrSyncConfiguration> _configurations = new();
    private readonly ConcurrentDictionary<string, List<HrSyncHistoryEntry>> _history = new();
    private readonly ConcurrentDictionary<string, HrSyncHistoryEntry> _runningSync = new();
    private bool _loaded;

    private const string ConfigScope = "cluster";
    private const string ConfigSection = "HrSync";
    private const int MaxHistoryPerConfig = 50;

    public static readonly Dictionary<string, string> DefaultAttributeMapping = new()
    {
        ["employeeId"] = "employeeID",
        ["firstName"] = "givenName",
        ["lastName"] = "sn",
        ["displayName"] = "displayName",
        ["email"] = "mail",
        ["department"] = "department",
        ["title"] = "title",
        ["manager"] = "manager",
        ["company"] = "company"
    };

    public HrSyncService(
        ILogger<HrSyncService> logger,
        IDirectoryStore store,
        INamingContextService ncService,
        IRidAllocator ridAllocator,
        IUserAccountControlService uacService,
        CosmosConfigurationStore configStore,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _store = store;
        _ncService = ncService;
        _ridAllocator = ridAllocator;
        _uacService = uacService;
        _configStore = configStore;
        _httpClientFactory = httpClientFactory;
    }

    public async Task EnsureLoadedAsync()
    {
        if (_loaded) return;
        await LoadFromStoreAsync();
    }

    #region Configuration CRUD

    public async Task<List<HrSyncConfiguration>> GetAllConfigurations()
    {
        await EnsureLoadedAsync();
        return _configurations.Values.OrderBy(c => c.Name).ToList();
    }

    public async Task<HrSyncConfiguration> GetConfiguration(string id)
    {
        await EnsureLoadedAsync();
        return _configurations.GetValueOrDefault(id);
    }

    public async Task<HrSyncConfiguration> CreateConfiguration(HrSyncConfiguration config)
    {
        await EnsureLoadedAsync();
        if (config.AttributeMapping.Count == 0)
            config.AttributeMapping = new Dictionary<string, string>(DefaultAttributeMapping);
        _configurations[config.Id] = config;
        await PersistAsync();
        return config;
    }

    public async Task<HrSyncConfiguration> UpdateConfiguration(string id, HrSyncConfiguration updated)
    {
        await EnsureLoadedAsync();
        if (!_configurations.ContainsKey(id)) return null;
        updated.Id = id;
        _configurations[id] = updated;
        await PersistAsync();
        return updated;
    }

    public async Task<bool> DeleteConfiguration(string id)
    {
        await EnsureLoadedAsync();
        if (!_configurations.TryRemove(id, out _)) return false;
        _history.TryRemove(id, out _);
        await PersistAsync();
        return true;
    }

    #endregion

    #region Sync Operations

    public async Task<HrSyncHistoryEntry> SyncNow(string configId)
    {
        await EnsureLoadedAsync();
        if (!_configurations.TryGetValue(configId, out var config))
            throw new KeyNotFoundException($"Configuration {configId} not found");

        if (_runningSync.ContainsKey(configId))
            throw new InvalidOperationException("Sync is already running for this configuration");

        var entry = new HrSyncHistoryEntry
        {
            ConfigurationId = configId,
            Status = "Running"
        };

        _runningSync[configId] = entry;

        // Run sync in background
        _ = Task.Run(async () =>
        {
            try
            {
                var employees = await FetchEmployees(config);
                await ProcessEmployees(config, employees, entry);
                entry.Status = "Completed";
                entry.CompletedAt = DateTimeOffset.UtcNow;

                config.LastSyncAt = entry.CompletedAt;
                config.LastSyncStatus = $"Success: {entry.UsersCreated} created, {entry.UsersUpdated} updated, {entry.UsersDisabled} disabled";
            }
            catch (Exception ex)
            {
                entry.Status = "Failed";
                entry.CompletedAt = DateTimeOffset.UtcNow;
                entry.ErrorDetails.Add(ex.Message);
                entry.Errors++;

                config.LastSyncAt = entry.CompletedAt;
                config.LastSyncStatus = $"Failed: {ex.Message}";
                _logger.LogError(ex, "HR sync failed for config {ConfigId}", configId);
            }
            finally
            {
                _runningSync.TryRemove(configId, out _);
                AddHistoryEntry(configId, entry);
                await PersistAsync();
            }
        });

        return entry;
    }

    public async Task<HrSyncStatus> GetSyncStatus(string configId)
    {
        await EnsureLoadedAsync();
        var config = _configurations.GetValueOrDefault(configId);
        _runningSync.TryGetValue(configId, out var currentRun);

        return new HrSyncStatus
        {
            ConfigurationId = configId,
            IsRunning = currentRun != null,
            LastSyncAt = config?.LastSyncAt,
            LastSyncStatus = config?.LastSyncStatus,
            CurrentRun = currentRun
        };
    }

    public async Task<List<HrSyncHistoryEntry>> GetSyncHistory(string configId)
    {
        await EnsureLoadedAsync();
        return _history.TryGetValue(configId, out var list)
            ? list.OrderByDescending(h => h.StartedAt).ToList()
            : new();
    }

    public async Task<HrSyncPreviewResult> PreviewSync(string configId)
    {
        await EnsureLoadedAsync();
        if (!_configurations.TryGetValue(configId, out var config))
            throw new KeyNotFoundException($"Configuration {configId} not found");

        var employees = await FetchEmployees(config);
        var preview = new HrSyncPreviewResult { TotalRecords = employees.Count };

        var domainDn = _ncService.GetDomainNc().Dn;

        foreach (var emp in employees)
        {
            var action = new HrSyncPreviewAction
            {
                EmployeeId = emp.EmployeeId,
                DisplayName = emp.DisplayName ?? $"{emp.FirstName} {emp.LastName}"
            };

            // Check if user already exists by employeeId in extensionAttribute
            var existing = await FindExistingUser(emp.EmployeeId, domainDn);

            if (existing == null)
            {
                if (emp.Status?.Equals("terminated", StringComparison.OrdinalIgnoreCase) == true)
                {
                    preview.NoChange++;
                    continue;
                }
                action.Action = "Create";
                action.Changes["status"] = "New user will be created";
                preview.NewUsers++;
            }
            else
            {
                action.CurrentDn = existing.DistinguishedName;

                if (emp.Status?.Equals("terminated", StringComparison.OrdinalIgnoreCase) == true)
                {
                    if ((existing.UserAccountControl & 0x2) == 0)
                    {
                        action.Action = "Disable";
                        action.Changes["userAccountControl"] = "Will be disabled (terminated)";
                        preview.Terminations++;
                    }
                    else
                    {
                        preview.NoChange++;
                        continue;
                    }
                }
                else
                {
                    var changes = DetectChanges(existing, emp, config.AttributeMapping);
                    if (changes.Count > 0)
                    {
                        action.Action = "Update";
                        action.Changes = changes;
                        preview.Updates++;
                    }
                    else
                    {
                        preview.NoChange++;
                        continue;
                    }
                }
            }

            preview.Actions.Add(action);
        }

        return preview;
    }

    #endregion

    #region HR Data Fetching

    private async Task<List<HrEmployee>> FetchEmployees(HrSyncConfiguration config)
    {
        switch (config.SourceType)
        {
            case HrSyncSourceType.GenericApi:
                return await FetchFromGenericApi(config);
            case HrSyncSourceType.BambooHR:
                return await FetchFromBambooHR(config);
            case HrSyncSourceType.Workday:
                return await FetchFromGenericApi(config); // Workday uses REST API too
            case HrSyncSourceType.SapSuccessFactors:
                return await FetchFromGenericApi(config);
            case HrSyncSourceType.CsvUpload:
                return await FetchFromCsvUpload(config);
            default:
                return new();
        }
    }

    private async Task<List<HrEmployee>> FetchFromGenericApi(HrSyncConfiguration config)
    {
        var client = _httpClientFactory.CreateClient("HrSync");
        client.Timeout = TimeSpan.FromSeconds(30);

        if (!string.IsNullOrEmpty(config.ApiKey))
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {config.ApiKey}");

        try
        {
            var response = await client.GetAsync(config.EndpointUrl);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            var employees = JsonSerializer.Deserialize<List<HrEmployee>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return employees ?? new();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch employees from {Url}", config.EndpointUrl);
            throw;
        }
    }

    private async Task<List<HrEmployee>> FetchFromBambooHR(HrSyncConfiguration config)
    {
        // BambooHR uses basic auth with API key as username
        var client = _httpClientFactory.CreateClient("HrSync");
        client.Timeout = TimeSpan.FromSeconds(30);

        var authBytes = System.Text.Encoding.UTF8.GetBytes($"{config.ApiKey}:x");
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(authBytes));
        client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            var response = await client.GetAsync(config.EndpointUrl);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();

            // BambooHR returns { "employees": [...] }
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("employees", out var empArray))
            {
                return JsonSerializer.Deserialize<List<HrEmployee>>(empArray.GetRawText(), new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? new();
            }

            return new();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch from BambooHR at {Url}", config.EndpointUrl);
            throw;
        }
    }

    private async Task<List<HrEmployee>> FetchFromCsvUpload(HrSyncConfiguration config)
    {
        // EndpointUrl is reused to store the CSV file path for CSV upload sources
        var csvPath = config.EndpointUrl;

        if (string.IsNullOrWhiteSpace(csvPath))
        {
            _logger.LogWarning("No CSV file path configured for HR sync configuration {ConfigId}", config.Id);
            return new();
        }

        if (!File.Exists(csvPath))
        {
            _logger.LogError("CSV file not found at {Path} for HR sync configuration {ConfigId}", csvPath, config.Id);
            throw new FileNotFoundException($"CSV file not found: {csvPath}");
        }

        var employees = new List<HrEmployee>();

        try
        {
            var lines = await File.ReadAllLinesAsync(csvPath);
            if (lines.Length < 2)
            {
                _logger.LogWarning("CSV file at {Path} has no data rows", csvPath);
                return employees;
            }

            // Parse header row to determine column indices
            var headers = ParseCsvLine(lines[0]);
            var columnMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < headers.Length; i++)
            {
                columnMap[headers[i].Trim()] = i;
            }

            // Build a reverse mapping: HR field name -> CSV column index
            // config.AttributeMapping maps HR field -> directory attribute,
            // but for CSV we use it as CSV column name -> HR field name when the key matches a CSV header.
            // If no mapping is configured, fall back to well-known column names.
            var csvToField = new Dictionary<int, string>();

            foreach (var kvp in config.AttributeMapping)
            {
                // kvp.Key is the HR field name (e.g. "employeeId", "firstName")
                // Check if the CSV has a column matching the key directly
                if (columnMap.TryGetValue(kvp.Key, out var idx))
                {
                    csvToField[idx] = kvp.Key;
                }
                // Also check if the CSV has a column matching the directory attribute name (the value)
                else if (columnMap.TryGetValue(kvp.Value, out var idx2))
                {
                    csvToField[idx2] = kvp.Key;
                }
            }

            // Fall back: if no mappings matched, try to map CSV columns by well-known names
            if (csvToField.Count == 0)
            {
                var wellKnown = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                {
                    ["employeeId"] = new[] { "employeeId", "employee_id", "empId", "id", "employeeID" },
                    ["firstName"] = new[] { "firstName", "first_name", "givenName", "given_name" },
                    ["lastName"] = new[] { "lastName", "last_name", "sn", "surname" },
                    ["displayName"] = new[] { "displayName", "display_name", "name", "fullName", "full_name" },
                    ["email"] = new[] { "email", "mail", "emailAddress", "email_address" },
                    ["department"] = new[] { "department", "dept" },
                    ["title"] = new[] { "title", "jobTitle", "job_title" },
                    ["manager"] = new[] { "manager", "managerId", "manager_id" },
                    ["company"] = new[] { "company", "organization", "org" },
                    ["status"] = new[] { "status", "employmentStatus", "employment_status" }
                };

                foreach (var kvp in wellKnown)
                {
                    foreach (var alias in kvp.Value)
                    {
                        if (columnMap.TryGetValue(alias, out var idx))
                        {
                            csvToField[idx] = kvp.Key;
                            break;
                        }
                    }
                }
            }

            // Parse data rows
            for (var row = 1; row < lines.Length; row++)
            {
                var line = lines[row];
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var fields = ParseCsvLine(line);
                var emp = new HrEmployee();
                var customFields = new Dictionary<string, string>();

                for (var col = 0; col < fields.Length; col++)
                {
                    var value = fields[col].Trim();
                    if (string.IsNullOrEmpty(value))
                        continue;

                    if (csvToField.TryGetValue(col, out var hrField))
                    {
                        switch (hrField.ToLowerInvariant())
                        {
                            case "employeeid": emp.EmployeeId = value; break;
                            case "firstname": emp.FirstName = value; break;
                            case "lastname": emp.LastName = value; break;
                            case "displayname": emp.DisplayName = value; break;
                            case "email": emp.Email = value; break;
                            case "department": emp.Department = value; break;
                            case "title": emp.Title = value; break;
                            case "manager": emp.Manager = value; break;
                            case "company": emp.Company = value; break;
                            case "status": emp.Status = value; break;
                            default:
                                customFields[hrField] = value;
                                break;
                        }
                    }
                    else if (col < headers.Length)
                    {
                        // Store unmapped columns as custom fields
                        customFields[headers[col].Trim()] = value;
                    }
                }

                if (customFields.Count > 0)
                    emp.CustomFields = customFields;

                // Skip rows without an employee ID
                if (string.IsNullOrWhiteSpace(emp.EmployeeId))
                {
                    _logger.LogWarning("Skipping CSV row {Row} — no employee ID found", row + 1);
                    continue;
                }

                employees.Add(emp);
            }

            _logger.LogInformation("Parsed {Count} employees from CSV file {Path}", employees.Count, csvPath);
        }
        catch (FileNotFoundException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse CSV file at {Path}", csvPath);
            throw;
        }

        return employees;
    }

    private static string[] ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    // Check for escaped quote (double quote)
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++; // skip the next quote
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            else
            {
                if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == ',')
                {
                    fields.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
        }

        fields.Add(current.ToString());
        return fields.ToArray();
    }

    #endregion

    #region Sync Processing

    private async Task ProcessEmployees(HrSyncConfiguration config, List<HrEmployee> employees, HrSyncHistoryEntry entry)
    {
        var domainDn = _ncService.GetDomainNc().Dn;

        foreach (var emp in employees)
        {
            try
            {
                var existing = await FindExistingUser(emp.EmployeeId, domainDn);

                if (emp.Status?.Equals("terminated", StringComparison.OrdinalIgnoreCase) == true)
                {
                    if (existing != null && config.AutoDisableOnTermination)
                    {
                        existing.UserAccountControl |= 0x2;
                        var usn = await _store.GetNextUsnAsync("default", domainDn);
                        existing.WhenChanged = DateTimeOffset.UtcNow;
                        existing.USNChanged = usn;
                        await _store.UpdateAsync(existing);
                        entry.UsersDisabled++;
                    }
                    continue;
                }

                if (existing == null && config.AutoCreateUsers)
                {
                    await CreateUserFromHr(emp, config, domainDn);
                    entry.UsersCreated++;
                }
                else if (existing != null)
                {
                    var updated = await UpdateUserFromHr(existing, emp, config, domainDn);
                    if (updated) entry.UsersUpdated++;
                }
            }
            catch (Exception ex)
            {
                entry.Errors++;
                entry.ErrorDetails.Add($"Employee {emp.EmployeeId}: {ex.Message}");
                _logger.LogWarning(ex, "Error processing employee {EmployeeId}", emp.EmployeeId);
            }
        }
    }

    private async Task<DirectoryObject> FindExistingUser(string employeeId, string domainDn)
    {
        // Search by extensionAttribute1 (employeeId) or employeeID attribute
        var filter = new OrFilterNode(new List<FilterNode>
        {
            new EqualityFilterNode("extensionAttribute1", employeeId),
            new EqualityFilterNode("employeeID", employeeId)
        });

        var result = await _store.SearchAsync("default", domainDn, SearchScope.WholeSubtree, filter, null, 1);
        return result.Entries.FirstOrDefault();
    }

    private async Task CreateUserFromHr(HrEmployee emp, HrSyncConfiguration config, string domainDn)
    {
        var cn = emp.DisplayName ?? $"{emp.FirstName} {emp.LastName}";
        var targetOu = !string.IsNullOrEmpty(config.TargetOu) ? config.TargetOu : $"CN=Users,{domainDn}";
        var dn = $"CN={cn},{targetOu}";

        var objectSid = await _ridAllocator.GenerateObjectSidAsync("default", domainDn);
        var uac = _uacService.GetDefaultUac("user");
        var now = DateTimeOffset.UtcNow;
        var usn = await _store.GetNextUsnAsync("default", domainDn);

        var samAccountName = GenerateSamAccountName(emp);

        var obj = new DirectoryObject
        {
            Id = dn.ToLowerInvariant(),
            TenantId = "default",
            DomainDn = domainDn,
            DistinguishedName = dn,
            ObjectGuid = Guid.NewGuid().ToString(),
            ObjectSid = objectSid,
            ObjectClass = ["top", "person", "organizationalPerson", "user"],
            ObjectCategory = "person",
            Cn = cn,
            SAMAccountName = samAccountName,
            DisplayName = cn,
            GivenName = emp.FirstName,
            Sn = emp.LastName,
            Mail = emp.Email,
            Title = emp.Title,
            Department = emp.Department,
            Company = emp.Company,
            UserAccountControl = uac,
            PrimaryGroupId = 513,
            ParentDn = targetOu,
            WhenCreated = now,
            WhenChanged = now,
            USNCreated = usn,
            USNChanged = usn,
            SAMAccountType = 0x30000000,
        };

        obj.Attributes["extensionAttribute1"] = new DirectoryAttribute("extensionAttribute1", emp.EmployeeId);
        obj.Attributes["employeeID"] = new DirectoryAttribute("employeeID", emp.EmployeeId);

        await _store.CreateAsync(obj);
    }

    private async Task<bool> UpdateUserFromHr(DirectoryObject existing, HrEmployee emp, HrSyncConfiguration config, string domainDn)
    {
        var changes = DetectChanges(existing, emp, config.AttributeMapping);
        if (changes.Count == 0) return false;

        var usn = await _store.GetNextUsnAsync("default", domainDn);

        if (emp.FirstName != null) existing.GivenName = emp.FirstName;
        if (emp.LastName != null) existing.Sn = emp.LastName;
        if (emp.DisplayName != null) existing.DisplayName = emp.DisplayName;
        if (emp.Email != null) existing.Mail = emp.Email;
        if (emp.Title != null) existing.Title = emp.Title;
        if (emp.Department != null) existing.Department = emp.Department;
        if (emp.Company != null) existing.Company = emp.Company;

        existing.WhenChanged = DateTimeOffset.UtcNow;
        existing.USNChanged = usn;

        await _store.UpdateAsync(existing);
        return true;
    }

    private Dictionary<string, string> DetectChanges(DirectoryObject existing, HrEmployee emp, Dictionary<string, string> mapping)
    {
        var changes = new Dictionary<string, string>();

        if (emp.FirstName != null && existing.GivenName != emp.FirstName)
            changes["givenName"] = $"{existing.GivenName} -> {emp.FirstName}";
        if (emp.LastName != null && existing.Sn != emp.LastName)
            changes["sn"] = $"{existing.Sn} -> {emp.LastName}";
        if (emp.DisplayName != null && existing.DisplayName != emp.DisplayName)
            changes["displayName"] = $"{existing.DisplayName} -> {emp.DisplayName}";
        if (emp.Email != null && existing.Mail != emp.Email)
            changes["mail"] = $"{existing.Mail} -> {emp.Email}";
        if (emp.Title != null && existing.Title != emp.Title)
            changes["title"] = $"{existing.Title} -> {emp.Title}";
        if (emp.Department != null && existing.Department != emp.Department)
            changes["department"] = $"{existing.Department} -> {emp.Department}";
        if (emp.Company != null && existing.Company != emp.Company)
            changes["company"] = $"{existing.Company} -> {emp.Company}";

        return changes;
    }

    private static string GenerateSamAccountName(HrEmployee emp)
    {
        var first = emp.FirstName?.ToLowerInvariant().Replace(" ", "") ?? "";
        var last = emp.LastName?.ToLowerInvariant().Replace(" ", "") ?? "";

        if (!string.IsNullOrEmpty(first) && !string.IsNullOrEmpty(last))
            return $"{first[0]}{last}"[..Math.Min(20, 1 + last.Length)];

        return emp.EmployeeId;
    }

    #endregion

    #region History

    private void AddHistoryEntry(string configId, HrSyncHistoryEntry entry)
    {
        var list = _history.GetOrAdd(configId, _ => new List<HrSyncHistoryEntry>());
        lock (list)
        {
            list.Add(entry);
            if (list.Count > MaxHistoryPerConfig)
                list.RemoveRange(0, list.Count - MaxHistoryPerConfig);
        }
    }

    #endregion

    #region Persistence

    private async Task LoadFromStoreAsync()
    {
        try
        {
            var doc = await _configStore.GetSectionAsync("default", ConfigScope, ConfigSection);
            if (doc != null && doc.Values.TryGetValue("configurations", out var cfgElement))
            {
                var configs = JsonSerializer.Deserialize<List<HrSyncConfiguration>>(cfgElement.GetRawText());
                if (configs != null)
                {
                    foreach (var c in configs)
                        _configurations[c.Id] = c;
                }
            }
            _loaded = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load HR sync configurations from configuration store");
            _loaded = true;
        }
    }

    private async Task PersistAsync()
    {
        try
        {
            var json = JsonSerializer.SerializeToElement(_configurations.Values.ToList());
            var doc = await _configStore.GetSectionAsync("default", ConfigScope, ConfigSection)
                      ?? new ConfigurationDocument
                      {
                          Id = $"{ConfigScope}::{ConfigSection}",
                          TenantId = "default",
                          Scope = ConfigScope,
                          Section = ConfigSection
                      };
            doc.Values["configurations"] = json;
            await _configStore.UpsertSectionAsync(doc);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist HR sync configurations");
        }
    }

    #endregion
}
