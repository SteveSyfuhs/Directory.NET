using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.CosmosDb.Configuration;

namespace Directory.Web.Services;

public class MdmIntegration
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public MdmProvider Provider { get; set; }
    public string ApiEndpoint { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public bool SyncDeviceCompliance { get; set; }
    public bool IsEnabled { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastSyncAt { get; set; }
}

public enum MdmProvider { Intune, JamfPro, WorkspaceOne, MobileIron, Generic }

public class DeviceComplianceStatus
{
    public string DeviceId { get; set; } = "";
    public string UserDn { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string Platform { get; set; } = "";
    public bool IsCompliant { get; set; }
    public bool IsManaged { get; set; }
    public DateTimeOffset LastCheckIn { get; set; }
    public List<string> ComplianceIssues { get; set; } = new();
    public string IntegrationId { get; set; } = "";
}

public class MdmSyncResult
{
    public int DevicesSynced { get; set; }
    public int NewDevices { get; set; }
    public int UpdatedDevices { get; set; }
    public int Errors { get; set; }
    public DateTimeOffset SyncedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class MdmIntegrationService
{
    private readonly ILogger<MdmIntegrationService> _logger;
    private readonly CosmosConfigurationStore _configStore;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IDirectoryStore _directoryStore;

    private readonly ConcurrentDictionary<string, MdmIntegration> _integrations = new();
    private readonly ConcurrentDictionary<string, DeviceComplianceStatus> _devices = new();
    private bool _loaded;

    private const string ConfigScope = "cluster";
    private const string ConfigSection = "MdmIntegrations";
    private const string TenantId = "default";

    public MdmIntegrationService(
        ILogger<MdmIntegrationService> logger,
        CosmosConfigurationStore configStore,
        IHttpClientFactory httpClientFactory,
        IDirectoryStore directoryStore)
    {
        _logger = logger;
        _configStore = configStore;
        _httpClientFactory = httpClientFactory;
        _directoryStore = directoryStore;
    }

    private async Task EnsureLoadedAsync()
    {
        if (_loaded) return;
        try
        {
            var integrations = await _configStore.GetAsync<List<MdmIntegration>>(ConfigScope, ConfigSection, "Integrations");
            if (integrations != null)
            {
                foreach (var i in integrations)
                    _integrations[i.Id] = i;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load MDM integrations from Cosmos DB");
        }
        _loaded = true;
    }

    private async Task SaveIntegrationsAsync()
    {
        await _configStore.SetAsync(ConfigScope, ConfigSection, "Integrations", _integrations.Values.ToList());
    }

    public async Task<List<MdmIntegration>> GetIntegrations()
    {
        await EnsureLoadedAsync();
        return _integrations.Values.ToList();
    }

    public async Task<MdmIntegration> GetIntegration(string id)
    {
        await EnsureLoadedAsync();
        _integrations.TryGetValue(id, out var integration);
        return integration;
    }

    public async Task<MdmIntegration> CreateIntegration(MdmIntegration integration)
    {
        await EnsureLoadedAsync();
        integration.Id = Guid.NewGuid().ToString();
        integration.CreatedAt = DateTimeOffset.UtcNow;
        _integrations[integration.Id] = integration;
        await SaveIntegrationsAsync();
        _logger.LogInformation("Created MDM integration {Name} ({Provider})", integration.Name, integration.Provider);
        return integration;
    }

    public async Task<MdmIntegration> UpdateIntegration(string id, MdmIntegration integration)
    {
        await EnsureLoadedAsync();
        if (!_integrations.ContainsKey(id)) return null;
        integration.Id = id;
        _integrations[id] = integration;
        await SaveIntegrationsAsync();
        return integration;
    }

    public async Task<bool> DeleteIntegration(string id)
    {
        await EnsureLoadedAsync();
        if (!_integrations.TryRemove(id, out _)) return false;
        // Remove associated device records
        var toRemove = _devices.Values.Where(d => d.IntegrationId == id).Select(d => d.DeviceId).ToList();
        foreach (var deviceId in toRemove)
            _devices.TryRemove(deviceId, out _);
        await SaveIntegrationsAsync();
        return true;
    }

    public Task<List<DeviceComplianceStatus>> GetDevices(string integrationId = null)
    {
        var devices = _devices.Values.AsEnumerable();
        if (!string.IsNullOrEmpty(integrationId))
            devices = devices.Where(d => d.IntegrationId == integrationId);
        return Task.FromResult(devices.OrderBy(d => d.DeviceName).ToList());
    }

    public async Task<MdmSyncResult> SyncDevices(string integrationId = null)
    {
        await EnsureLoadedAsync();
        var result = new MdmSyncResult();

        var targets = integrationId != null
            ? _integrations.Values.Where(i => i.Id == integrationId && i.IsEnabled)
            : _integrations.Values.Where(i => i.IsEnabled && i.SyncDeviceCompliance);

        foreach (var integration in targets)
        {
            try
            {
                _logger.LogInformation("Syncing devices from {Provider} ({Name})", integration.Provider, integration.Name);

                var devices = await FetchDevicesFromProvider(integration);
                foreach (var device in devices)
                {
                    if (_devices.TryAdd(device.DeviceId, device))
                        result.NewDevices++;
                    else
                    {
                        _devices[device.DeviceId] = device;
                        result.UpdatedDevices++;
                    }
                    result.DevicesSynced++;

                    // Store device data as computer object attributes in the directory store
                    await StoreDeviceInDirectoryAsync(device);
                }

                integration.LastSyncAt = DateTimeOffset.UtcNow;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to sync devices from {Name}", integration.Name);
                result.Errors++;
            }
        }

        await SaveIntegrationsAsync();
        return result;
    }

    private async Task<List<DeviceComplianceStatus>> FetchDevicesFromProvider(MdmIntegration integration)
    {
        if (string.IsNullOrWhiteSpace(integration.ApiEndpoint))
        {
            _logger.LogWarning("MDM integration {Name} has no API endpoint configured", integration.Name);
            return new List<DeviceComplianceStatus>();
        }

        try
        {
            var client = _httpClientFactory.CreateClient("MdmIntegration");
            client.Timeout = TimeSpan.FromSeconds(30);

            // Set Bearer token auth using the API key
            if (!string.IsNullOrEmpty(integration.ApiKey))
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", integration.ApiKey);

            string requestUrl = integration.Provider switch
            {
                MdmProvider.Intune => $"{integration.ApiEndpoint.TrimEnd('/')}/deviceManagement/managedDevices",
                MdmProvider.JamfPro => $"{integration.ApiEndpoint.TrimEnd('/')}/api/v1/computers-inventory",
                _ => integration.ApiEndpoint.TrimEnd('/')
            };

            var response = await client.GetAsync(requestUrl);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("MDM provider {Name} returned HTTP {Status}", integration.Name, response.StatusCode);
                return new List<DeviceComplianceStatus>();
            }

            var json = await response.Content.ReadAsStringAsync();
            return ParseDeviceResponse(json, integration);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "HTTP error fetching devices from {Name}", integration.Name);
            return new List<DeviceComplianceStatus>();
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "Timeout fetching devices from {Name}", integration.Name);
            return new List<DeviceComplianceStatus>();
        }
    }

    private List<DeviceComplianceStatus> ParseDeviceResponse(string json, MdmIntegration integration)
    {
        var devices = new List<DeviceComplianceStatus>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Intune / Microsoft Graph: { "value": [ { "id": ..., "deviceName": ..., "complianceState": ... } ] }
            // Jamf: { "results": [ { "id": ..., "name": ..., "isManaged": ... } ] }
            // Generic: either an array or { "value": [...] } or { "results": [...] }

            JsonElement? arrayEl = null;

            if (root.ValueKind == JsonValueKind.Array)
                arrayEl = root;
            else if (root.TryGetProperty("value", out var val) && val.ValueKind == JsonValueKind.Array)
                arrayEl = val;
            else if (root.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
                arrayEl = results;

            if (arrayEl == null)
                return devices;

            foreach (var item in arrayEl.Value.EnumerateArray())
            {
                var device = new DeviceComplianceStatus
                {
                    IntegrationId = integration.Id,
                };

                // Try common field names across providers
                device.DeviceId = GetStringField(item, "id", "deviceId", "serial_number") ?? Guid.NewGuid().ToString();
                device.DeviceName = GetStringField(item, "deviceName", "name", "display_name") ?? device.DeviceId;
                device.Platform = GetStringField(item, "operatingSystem", "platform", "os") ?? "Unknown";
                device.IsManaged = GetBoolField(item, "isManaged", "managed") ?? true;

                // Compliance state: Intune uses "complianceState" = "compliant" / "noncompliant"
                var complianceState = GetStringField(item, "complianceState", "compliance_status", "isCompliant");
                device.IsCompliant = complianceState != null
                    ? complianceState.Equals("compliant", StringComparison.OrdinalIgnoreCase) ||
                      complianceState.Equals("true", StringComparison.OrdinalIgnoreCase)
                    : GetBoolField(item, "isCompliant") ?? false;

                // Last check-in
                var lastCheckInStr = GetStringField(item, "lastSyncDateTime", "last_check_in", "lastCheckIn");
                device.LastCheckIn = lastCheckInStr != null && DateTimeOffset.TryParse(lastCheckInStr, out var lci)
                    ? lci
                    : DateTimeOffset.UtcNow;

                // Non-compliance reasons
                var reasonsEl = GetArrayField(item, "complianceIssues", "noncompliance_reasons");
                if (reasonsEl.HasValue)
                {
                    foreach (var r in reasonsEl.Value.EnumerateArray())
                        device.ComplianceIssues.Add(r.GetString() ?? "");
                }

                devices.Add(device);
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse device response from {Name}", integration.Name);
        }

        return devices;
    }

    private static string GetStringField(JsonElement el, params string[] names)
    {
        foreach (var name in names)
        {
            if (el.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
                return prop.GetString();
        }
        return null;
    }

    private static bool? GetBoolField(JsonElement el, params string[] names)
    {
        foreach (var name in names)
        {
            if (el.TryGetProperty(name, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.True) return true;
                if (prop.ValueKind == JsonValueKind.False) return false;
                if (prop.ValueKind == JsonValueKind.String &&
                    bool.TryParse(prop.GetString(), out var b)) return b;
            }
        }
        return null;
    }

    private static JsonElement? GetArrayField(JsonElement el, params string[] names)
    {
        foreach (var name in names)
        {
            if (el.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Array)
                return prop;
        }
        return null;
    }

    private async Task StoreDeviceInDirectoryAsync(DeviceComplianceStatus device)
    {
        try
        {
            // Find or create a computer object for this device in the directory
            var dn = $"CN={device.DeviceName},OU=MDM-Devices,DC=directory,DC=local";
            var existing = await _directoryStore.GetByDnAsync(TenantId, dn);

            var obj = existing ?? new DirectoryObject
            {
                Id = dn.ToLowerInvariant(),
                TenantId = TenantId,
                DistinguishedName = dn,
                ParentDn = "OU=MDM-Devices,DC=directory,DC=local",
                DomainDn = "DC=directory,DC=local",
                WhenCreated = DateTimeOffset.UtcNow,
                ObjectCategory = "computer",
            };

            if (obj.ObjectClass.Count == 0)
                obj.ObjectClass.AddRange(new[] { "top", "computer" });

            obj.Cn = device.DeviceName;
            obj.DnsHostName = device.DeviceName;
            obj.OperatingSystem = device.Platform;
            obj.WhenChanged = DateTimeOffset.UtcNow;

            obj.Attributes["mdm-DeviceId"] = new DirectoryAttribute("mdm-DeviceId", device.DeviceId);
            obj.Attributes["mdm-IsCompliant"] = new DirectoryAttribute("mdm-IsCompliant", device.IsCompliant.ToString());
            obj.Attributes["mdm-IsManaged"] = new DirectoryAttribute("mdm-IsManaged", device.IsManaged.ToString());
            obj.Attributes["mdm-LastCheckIn"] = new DirectoryAttribute("mdm-LastCheckIn", device.LastCheckIn.ToString("o"));
            obj.Attributes["mdm-IntegrationId"] = new DirectoryAttribute("mdm-IntegrationId", device.IntegrationId);

            if (device.ComplianceIssues.Count > 0)
                obj.Attributes["mdm-ComplianceIssues"] = new DirectoryAttribute("mdm-ComplianceIssues",
                    string.Join("; ", device.ComplianceIssues));

            if (existing != null)
            {
                obj.ETag = existing.ETag;
                await _directoryStore.UpdateAsync(obj);
            }
            else
            {
                await _directoryStore.CreateAsync(obj);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to store device {DeviceName} in directory", device.DeviceName);
            // Non-fatal — sync result is still valid
        }
    }
}
