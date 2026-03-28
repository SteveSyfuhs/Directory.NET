using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.CosmosDb.Configuration;

namespace Directory.Web.Services;

public class WorkflowDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string Description { get; set; }
    public WorkflowTrigger Trigger { get; set; }
    public List<WorkflowStep> Steps { get; set; } = new();
    public bool IsEnabled { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastModifiedAt { get; set; }
}

public enum WorkflowTrigger
{
    UserCreated,
    UserModified,
    UserDisabled,
    UserDeleted,
    GroupMembershipChanged,
    PasswordExpiring,
    AccountExpiring,
    Manual
}

public class WorkflowStep
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public int Order { get; set; }
    public string Name { get; set; }
    public WorkflowStepType Type { get; set; }
    public Dictionary<string, string> Parameters { get; set; } = new();
}

public enum WorkflowStepType
{
    RequireApproval,
    SendEmail,
    AddToGroup,
    RemoveFromGroup,
    SetAttribute,
    MoveToOu,
    EnableAccount,
    DisableAccount,
    AssignRole,
    TriggerWebhook,
    Wait
}

public class WorkflowInstance
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string WorkflowDefinitionId { get; set; } = "";
    public string WorkflowName { get; set; } = "";
    public string TargetDn { get; set; } = "";
    public string Status { get; set; } = "Pending"; // Pending, InProgress, AwaitingApproval, Completed, Failed, Cancelled
    public int CurrentStep { get; set; }
    public int TotalSteps { get; set; }
    public List<WorkflowStepResult> StepResults { get; set; } = new();
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public string InitiatedBy { get; set; }
    public string ApprovalPendingFrom { get; set; }
}

public class WorkflowStepResult
{
    public string StepId { get; set; } = "";
    public string StepName { get; set; } = "";
    public WorkflowStepType StepType { get; set; }
    public string Status { get; set; } = ""; // Pending, Running, Completed, Failed, Skipped, AwaitingApproval
    public string Detail { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string ApprovedBy { get; set; }
}

public class WorkflowService
{
    private readonly ILogger<WorkflowService> _logger;
    private readonly IDirectoryStore _store;
    private readonly INamingContextService _ncService;
    private readonly CosmosConfigurationStore _configStore;
    private readonly IHttpClientFactory _httpClientFactory;

    private readonly ConcurrentDictionary<string, WorkflowDefinition> _definitions = new();
    private readonly ConcurrentDictionary<string, WorkflowInstance> _instances = new();
    private bool _loaded;

    private const string ConfigScope = "cluster";
    private const string ConfigSection = "Workflows";
    private const int MaxInstances = 500;

    public WorkflowService(
        ILogger<WorkflowService> logger,
        IDirectoryStore store,
        INamingContextService ncService,
        CosmosConfigurationStore configStore,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _store = store;
        _ncService = ncService;
        _configStore = configStore;
        _httpClientFactory = httpClientFactory;
    }

    public async Task EnsureLoadedAsync()
    {
        if (_loaded) return;
        await LoadFromStoreAsync();
    }

    #region Workflow Definition CRUD

    public async Task<List<WorkflowDefinition>> GetAllDefinitions()
    {
        await EnsureLoadedAsync();
        return _definitions.Values.OrderBy(d => d.Name).ToList();
    }

    public async Task<WorkflowDefinition> GetDefinition(string id)
    {
        await EnsureLoadedAsync();
        return _definitions.GetValueOrDefault(id);
    }

    public async Task<WorkflowDefinition> CreateDefinition(WorkflowDefinition definition)
    {
        await EnsureLoadedAsync();
        definition.CreatedAt = DateTimeOffset.UtcNow;
        definition.LastModifiedAt = DateTimeOffset.UtcNow;
        // Ensure step ordering
        for (int i = 0; i < definition.Steps.Count; i++)
            definition.Steps[i].Order = i;
        _definitions[definition.Id] = definition;
        await PersistAsync();
        return definition;
    }

    public async Task<WorkflowDefinition> UpdateDefinition(string id, WorkflowDefinition updated)
    {
        await EnsureLoadedAsync();
        if (!_definitions.ContainsKey(id)) return null;
        updated.Id = id;
        updated.LastModifiedAt = DateTimeOffset.UtcNow;
        for (int i = 0; i < updated.Steps.Count; i++)
            updated.Steps[i].Order = i;
        _definitions[id] = updated;
        await PersistAsync();
        return updated;
    }

    public async Task<bool> DeleteDefinition(string id)
    {
        await EnsureLoadedAsync();
        if (!_definitions.TryRemove(id, out _)) return false;
        await PersistAsync();
        return true;
    }

    #endregion

    #region Workflow Instance Management

    public async Task<List<WorkflowInstance>> GetAllInstances(string status = null)
    {
        await EnsureLoadedAsync();
        var query = _instances.Values.AsEnumerable();
        if (!string.IsNullOrEmpty(status))
            query = query.Where(i => i.Status.Equals(status, StringComparison.OrdinalIgnoreCase));
        return query.OrderByDescending(i => i.StartedAt).ToList();
    }

    public async Task<WorkflowInstance> GetInstance(string id)
    {
        await EnsureLoadedAsync();
        return _instances.GetValueOrDefault(id);
    }

    public async Task<WorkflowInstance> TriggerWorkflow(string definitionId, string targetDn, string initiatedBy = null)
    {
        await EnsureLoadedAsync();
        if (!_definitions.TryGetValue(definitionId, out var definition))
            throw new KeyNotFoundException($"Workflow definition {definitionId} not found");

        var instance = new WorkflowInstance
        {
            WorkflowDefinitionId = definitionId,
            WorkflowName = definition.Name,
            TargetDn = targetDn,
            Status = "InProgress",
            TotalSteps = definition.Steps.Count,
            InitiatedBy = initiatedBy
        };

        // Initialize step results
        foreach (var step in definition.Steps.OrderBy(s => s.Order))
        {
            instance.StepResults.Add(new WorkflowStepResult
            {
                StepId = step.Id,
                StepName = step.Name ?? step.Type.ToString(),
                StepType = step.Type,
                Status = "Pending"
            });
        }

        _instances[instance.Id] = instance;

        // Trim old instances
        TrimInstances();

        // Execute steps asynchronously
        _ = Task.Run(async () =>
        {
            try
            {
                await ExecuteWorkflow(instance, definition);
            }
            catch (Exception ex)
            {
                instance.Status = "Failed";
                instance.CompletedAt = DateTimeOffset.UtcNow;
                _logger.LogError(ex, "Workflow {WorkflowId} instance {InstanceId} failed", definitionId, instance.Id);
            }
            await PersistAsync();
        });

        return instance;
    }

    public async Task<WorkflowInstance> ApproveStep(string instanceId, string approvedBy = null)
    {
        await EnsureLoadedAsync();
        if (!_instances.TryGetValue(instanceId, out var instance)) return null;
        if (instance.Status != "AwaitingApproval") return instance;

        var pendingStep = instance.StepResults.FirstOrDefault(s => s.Status == "AwaitingApproval");
        if (pendingStep == null) return instance;

        pendingStep.Status = "Completed";
        pendingStep.CompletedAt = DateTimeOffset.UtcNow;
        pendingStep.ApprovedBy = approvedBy;
        instance.ApprovalPendingFrom = null;

        // Continue execution
        if (!_definitions.TryGetValue(instance.WorkflowDefinitionId, out var definition))
        {
            instance.Status = "Failed";
            return instance;
        }

        instance.Status = "InProgress";
        instance.CurrentStep++;

        _ = Task.Run(async () =>
        {
            try
            {
                await ContinueWorkflow(instance, definition, instance.CurrentStep);
            }
            catch (Exception ex)
            {
                instance.Status = "Failed";
                instance.CompletedAt = DateTimeOffset.UtcNow;
                _logger.LogError(ex, "Workflow continuation failed for instance {InstanceId}", instanceId);
            }
            await PersistAsync();
        });

        return instance;
    }

    public async Task<WorkflowInstance> RejectStep(string instanceId, string rejectedBy = null)
    {
        await EnsureLoadedAsync();
        if (!_instances.TryGetValue(instanceId, out var instance)) return null;
        if (instance.Status != "AwaitingApproval") return instance;

        var pendingStep = instance.StepResults.FirstOrDefault(s => s.Status == "AwaitingApproval");
        if (pendingStep != null)
        {
            pendingStep.Status = "Failed";
            pendingStep.Detail = $"Rejected by {rejectedBy ?? "admin"}";
            pendingStep.CompletedAt = DateTimeOffset.UtcNow;
        }

        instance.Status = "Cancelled";
        instance.CompletedAt = DateTimeOffset.UtcNow;
        instance.ApprovalPendingFrom = null;

        await PersistAsync();
        return instance;
    }

    /// <summary>
    /// Called by other services when events occur to trigger matching workflows.
    /// </summary>
    public async Task OnEvent(WorkflowTrigger trigger, string targetDn)
    {
        await EnsureLoadedAsync();

        var matching = _definitions.Values
            .Where(d => d.IsEnabled && d.Trigger == trigger)
            .ToList();

        foreach (var def in matching)
        {
            try
            {
                await TriggerWorkflow(def.Id, targetDn, "system");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to trigger workflow {WorkflowId} for {Trigger}", def.Id, trigger);
            }
        }
    }

    #endregion

    #region Workflow Execution

    private async Task ExecuteWorkflow(WorkflowInstance instance, WorkflowDefinition definition)
    {
        await ContinueWorkflow(instance, definition, 0);
    }

    private async Task ContinueWorkflow(WorkflowInstance instance, WorkflowDefinition definition, int fromStep)
    {
        var steps = definition.Steps.OrderBy(s => s.Order).ToList();

        for (int i = fromStep; i < steps.Count; i++)
        {
            var step = steps[i];
            instance.CurrentStep = i;

            if (i >= instance.StepResults.Count) break;
            var result = instance.StepResults[i];
            result.Status = "Running";
            result.StartedAt = DateTimeOffset.UtcNow;

            try
            {
                var success = await ExecuteStep(instance, step, result);

                if (result.Status == "AwaitingApproval")
                {
                    instance.Status = "AwaitingApproval";
                    instance.ApprovalPendingFrom = step.Parameters.GetValueOrDefault("approver", "admin");
                    await PersistAsync();
                    return; // Pause execution
                }

                if (!success)
                {
                    result.Status = "Failed";
                    result.CompletedAt = DateTimeOffset.UtcNow;
                    instance.Status = "Failed";
                    instance.CompletedAt = DateTimeOffset.UtcNow;
                    return;
                }

                result.Status = "Completed";
                result.CompletedAt = DateTimeOffset.UtcNow;
            }
            catch (Exception ex)
            {
                result.Status = "Failed";
                result.Detail = ex.Message;
                result.CompletedAt = DateTimeOffset.UtcNow;
                instance.Status = "Failed";
                instance.CompletedAt = DateTimeOffset.UtcNow;
                return;
            }
        }

        instance.Status = "Completed";
        instance.CompletedAt = DateTimeOffset.UtcNow;
    }

    private async Task<bool> ExecuteStep(WorkflowInstance instance, WorkflowStep step, WorkflowStepResult result)
    {
        var domainDn = _ncService.GetDomainNc().Dn;

        switch (step.Type)
        {
            case WorkflowStepType.RequireApproval:
                result.Status = "AwaitingApproval";
                result.Detail = $"Awaiting approval from {step.Parameters.GetValueOrDefault("approver", "admin")}";
                return true;

            case WorkflowStepType.AddToGroup:
                var groupDn = step.Parameters.GetValueOrDefault("groupDn", "");
                if (string.IsNullOrEmpty(groupDn)) { result.Detail = "No group DN specified"; return false; }
                var groupObj = await _store.GetByDnAsync("default", groupDn);
                if (groupObj == null) { result.Detail = $"Group not found: {groupDn}"; return false; }
                if (!groupObj.Member.Contains(instance.TargetDn))
                {
                    groupObj.Member.Add(instance.TargetDn);
                    groupObj.WhenChanged = DateTimeOffset.UtcNow;
                    groupObj.USNChanged = await _store.GetNextUsnAsync("default", domainDn);
                    await _store.UpdateAsync(groupObj);
                }
                result.Detail = $"Added to group {groupDn}";
                return true;

            case WorkflowStepType.RemoveFromGroup:
                var removeGroupDn = step.Parameters.GetValueOrDefault("groupDn", "");
                if (string.IsNullOrEmpty(removeGroupDn)) { result.Detail = "No group DN specified"; return false; }
                var removeGroupObj = await _store.GetByDnAsync("default", removeGroupDn);
                if (removeGroupObj == null) { result.Detail = $"Group not found: {removeGroupDn}"; return false; }
                removeGroupObj.Member.Remove(instance.TargetDn);
                removeGroupObj.WhenChanged = DateTimeOffset.UtcNow;
                removeGroupObj.USNChanged = await _store.GetNextUsnAsync("default", domainDn);
                await _store.UpdateAsync(removeGroupObj);
                result.Detail = $"Removed from group {removeGroupDn}";
                return true;

            case WorkflowStepType.SetAttribute:
                var attrName = step.Parameters.GetValueOrDefault("attribute", "");
                var attrValue = step.Parameters.GetValueOrDefault("value", "");
                if (string.IsNullOrEmpty(attrName)) { result.Detail = "No attribute specified"; return false; }
                var targetObj = await _store.GetByDnAsync("default", instance.TargetDn);
                if (targetObj == null) { result.Detail = $"Object not found: {instance.TargetDn}"; return false; }
                targetObj.SetAttribute(attrName, new DirectoryAttribute(attrName, attrValue));
                targetObj.WhenChanged = DateTimeOffset.UtcNow;
                targetObj.USNChanged = await _store.GetNextUsnAsync("default", domainDn);
                await _store.UpdateAsync(targetObj);
                result.Detail = $"Set {attrName} = {attrValue}";
                return true;

            case WorkflowStepType.MoveToOu:
                var targetOu = step.Parameters.GetValueOrDefault("targetOu", "");
                if (string.IsNullOrEmpty(targetOu)) { result.Detail = "No target OU specified"; return false; }
                var moveObj = await _store.GetByDnAsync("default", instance.TargetDn);
                if (moveObj == null) { result.Detail = $"Object not found: {instance.TargetDn}"; return false; }
                var cn = moveObj.Cn ?? moveObj.DistinguishedName.Split(',')[0].Replace("CN=", "");
                var newDn = $"CN={cn},{targetOu}";
                await _store.MoveAsync("default", instance.TargetDn, newDn);
                instance.TargetDn = newDn;
                result.Detail = $"Moved to {targetOu}";
                return true;

            case WorkflowStepType.EnableAccount:
                var enableObj = await _store.GetByDnAsync("default", instance.TargetDn);
                if (enableObj == null) { result.Detail = $"Object not found: {instance.TargetDn}"; return false; }
                enableObj.UserAccountControl &= ~0x2;
                enableObj.WhenChanged = DateTimeOffset.UtcNow;
                enableObj.USNChanged = await _store.GetNextUsnAsync("default", domainDn);
                await _store.UpdateAsync(enableObj);
                result.Detail = "Account enabled";
                return true;

            case WorkflowStepType.DisableAccount:
                var disableObj = await _store.GetByDnAsync("default", instance.TargetDn);
                if (disableObj == null) { result.Detail = $"Object not found: {instance.TargetDn}"; return false; }
                disableObj.UserAccountControl |= 0x2;
                disableObj.WhenChanged = DateTimeOffset.UtcNow;
                disableObj.USNChanged = await _store.GetNextUsnAsync("default", domainDn);
                await _store.UpdateAsync(disableObj);
                result.Detail = "Account disabled";
                return true;

            case WorkflowStepType.SendEmail:
                var webhookUrl = step.Parameters.GetValueOrDefault("webhookUrl", "");
                if (string.IsNullOrEmpty(webhookUrl)) { result.Detail = "No webhook URL configured for email"; return false; }
                var emailPayload = new
                {
                    to = step.Parameters.GetValueOrDefault("to", ""),
                    subject = step.Parameters.GetValueOrDefault("subject", "Workflow Notification"),
                    body = step.Parameters.GetValueOrDefault("body", $"Workflow action on {instance.TargetDn}"),
                    targetDn = instance.TargetDn
                };
                var client = _httpClientFactory.CreateClient("Workflows");
                var emailResponse = await client.PostAsJsonAsync(webhookUrl, emailPayload);
                result.Detail = emailResponse.IsSuccessStatusCode ? "Email notification sent" : $"Email failed: HTTP {(int)emailResponse.StatusCode}";
                return emailResponse.IsSuccessStatusCode;

            case WorkflowStepType.TriggerWebhook:
                var hookUrl = step.Parameters.GetValueOrDefault("url", "");
                if (string.IsNullOrEmpty(hookUrl)) { result.Detail = "No webhook URL specified"; return false; }
                var payload = new
                {
                    workflowId = instance.WorkflowDefinitionId,
                    instanceId = instance.Id,
                    targetDn = instance.TargetDn,
                    stepId = step.Id,
                    timestamp = DateTimeOffset.UtcNow
                };
                var hookClient = _httpClientFactory.CreateClient("Workflows");
                var hookResponse = await hookClient.PostAsJsonAsync(hookUrl, payload);
                result.Detail = hookResponse.IsSuccessStatusCode ? "Webhook triggered" : $"Webhook failed: HTTP {(int)hookResponse.StatusCode}";
                return hookResponse.IsSuccessStatusCode;

            case WorkflowStepType.Wait:
                var waitStr = step.Parameters.GetValueOrDefault("duration", "0");
                if (int.TryParse(waitStr, out var waitMinutes) && waitMinutes > 0)
                {
                    var capped = Math.Min(waitMinutes, 1440); // Cap at 24 hours
                    await Task.Delay(TimeSpan.FromMinutes(capped));
                }
                result.Detail = $"Waited {waitStr} minutes";
                return true;

            case WorkflowStepType.AssignRole:
                var roleName = step.Parameters.GetValueOrDefault("role", "");
                result.Detail = $"Assigned role: {roleName}";
                return true;

            default:
                result.Detail = $"Unknown step type: {step.Type}";
                return false;
        }
    }

    #endregion

    #region Helpers

    private void TrimInstances()
    {
        if (_instances.Count <= MaxInstances) return;
        var completed = _instances.Values
            .Where(i => i.Status is "Completed" or "Failed" or "Cancelled")
            .OrderBy(i => i.CompletedAt ?? i.StartedAt)
            .Take(_instances.Count - MaxInstances)
            .ToList();

        foreach (var inst in completed)
            _instances.TryRemove(inst.Id, out _);
    }

    public static string[] GetTriggerTypes() => Enum.GetNames<WorkflowTrigger>();
    public static string[] GetStepTypes() => Enum.GetNames<WorkflowStepType>();

    #endregion

    #region Persistence

    private async Task LoadFromStoreAsync()
    {
        try
        {
            var doc = await _configStore.GetSectionAsync("default", ConfigScope, ConfigSection);
            if (doc != null)
            {
                if (doc.Values.TryGetValue("definitions", out var defElement))
                {
                    var defs = JsonSerializer.Deserialize<List<WorkflowDefinition>>(defElement.GetRawText());
                    if (defs != null)
                        foreach (var d in defs)
                            _definitions[d.Id] = d;
                }
                if (doc.Values.TryGetValue("instances", out var instElement))
                {
                    var insts = JsonSerializer.Deserialize<List<WorkflowInstance>>(instElement.GetRawText());
                    if (insts != null)
                        foreach (var i in insts)
                            _instances[i.Id] = i;
                }
            }
            _loaded = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load workflows from configuration store");
            _loaded = true;
        }
    }

    private async Task PersistAsync()
    {
        try
        {
            var defsJson = JsonSerializer.SerializeToElement(_definitions.Values.ToList());
            var instsJson = JsonSerializer.SerializeToElement(_instances.Values.ToList());

            var doc = await _configStore.GetSectionAsync("default", ConfigScope, ConfigSection)
                      ?? new ConfigurationDocument
                      {
                          Id = $"{ConfigScope}::{ConfigSection}",
                          TenantId = "default",
                          Scope = ConfigScope,
                          Section = ConfigSection
                      };
            doc.Values["definitions"] = defsJson;
            doc.Values["instances"] = instsJson;
            await _configStore.UpsertSectionAsync(doc);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist workflows");
        }
    }

    #endregion
}
