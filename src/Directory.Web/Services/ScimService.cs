using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.CosmosDb.Configuration;

namespace Directory.Web.Services;

#region SCIM Models

public class ScimListResponse<T>
{
    [JsonPropertyName("schemas")]
    public List<string> Schemas { get; set; } = ["urn:ietf:params:scim:api:messages:2.0:ListResponse"];

    [JsonPropertyName("totalResults")]
    public int TotalResults { get; set; }

    [JsonPropertyName("startIndex")]
    public int StartIndex { get; set; } = 1;

    [JsonPropertyName("itemsPerPage")]
    public int ItemsPerPage { get; set; }

    [JsonPropertyName("Resources")]
    public List<T> Resources { get; set; } = new();
}

public class ScimUser
{
    [JsonPropertyName("schemas")]
    public List<string> Schemas { get; set; } = ["urn:ietf:params:scim:schemas:core:2.0:User"];

    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonPropertyName("externalId")]
    public string ExternalId { get; set; }

    [JsonPropertyName("userName")]
    public string UserName { get; set; } = "";

    [JsonPropertyName("name")]
    public ScimName Name { get; set; }

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; }

    [JsonPropertyName("emails")]
    public List<ScimMultiValuedAttribute> Emails { get; set; }

    [JsonPropertyName("phoneNumbers")]
    public List<ScimMultiValuedAttribute> PhoneNumbers { get; set; }

    [JsonPropertyName("active")]
    public bool Active { get; set; } = true;

    [JsonPropertyName("title")]
    public string Title { get; set; }

    [JsonPropertyName("department")]
    public string Department { get; set; }

    [JsonPropertyName("meta")]
    public ScimMeta Meta { get; set; }

    [JsonPropertyName("groups")]
    public List<ScimGroupRef> Groups { get; set; }
}

public class ScimName
{
    [JsonPropertyName("formatted")]
    public string Formatted { get; set; }

    [JsonPropertyName("familyName")]
    public string FamilyName { get; set; }

    [JsonPropertyName("givenName")]
    public string GivenName { get; set; }
}

public class ScimMultiValuedAttribute
{
    [JsonPropertyName("value")]
    public string Value { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; }

    [JsonPropertyName("primary")]
    public bool Primary { get; set; }
}

public class ScimGroupRef
{
    [JsonPropertyName("value")]
    public string Value { get; set; }

    [JsonPropertyName("$ref")]
    public string Ref { get; set; }

    [JsonPropertyName("display")]
    public string Display { get; set; }
}

public class ScimGroup
{
    [JsonPropertyName("schemas")]
    public List<string> Schemas { get; set; } = ["urn:ietf:params:scim:schemas:core:2.0:Group"];

    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonPropertyName("externalId")]
    public string ExternalId { get; set; }

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("members")]
    public List<ScimMember> Members { get; set; }

    [JsonPropertyName("meta")]
    public ScimMeta Meta { get; set; }
}

public class ScimMember
{
    [JsonPropertyName("value")]
    public string Value { get; set; }

    [JsonPropertyName("$ref")]
    public string Ref { get; set; }

    [JsonPropertyName("display")]
    public string Display { get; set; }
}

public class ScimMeta
{
    [JsonPropertyName("resourceType")]
    public string ResourceType { get; set; }

    [JsonPropertyName("created")]
    public string Created { get; set; }

    [JsonPropertyName("lastModified")]
    public string LastModified { get; set; }

    [JsonPropertyName("location")]
    public string Location { get; set; }
}

public class ScimPatchRequest
{
    [JsonPropertyName("schemas")]
    public List<string> Schemas { get; set; } = new();

    [JsonPropertyName("Operations")]
    public List<ScimPatchOperation> Operations { get; set; } = new();
}

public class ScimPatchOperation
{
    [JsonPropertyName("op")]
    public string Op { get; set; } = "";

    [JsonPropertyName("path")]
    public string Path { get; set; }

    [JsonPropertyName("value")]
    public JsonElement? Value { get; set; }
}

public class ScimBulkRequest
{
    [JsonPropertyName("schemas")]
    public List<string> Schemas { get; set; } = new();

    [JsonPropertyName("Operations")]
    public List<ScimBulkOperation> Operations { get; set; } = new();
}

public class ScimBulkOperation
{
    [JsonPropertyName("method")]
    public string Method { get; set; } = "";

    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("bulkId")]
    public string BulkId { get; set; }

    [JsonPropertyName("data")]
    public JsonElement? Data { get; set; }
}

public class ScimBulkResponse
{
    [JsonPropertyName("schemas")]
    public List<string> Schemas { get; set; } = ["urn:ietf:params:scim:api:messages:2.0:BulkResponse"];

    [JsonPropertyName("Operations")]
    public List<ScimBulkOperationResponse> Operations { get; set; } = new();
}

public class ScimBulkOperationResponse
{
    [JsonPropertyName("method")]
    public string Method { get; set; } = "";

    [JsonPropertyName("bulkId")]
    public string BulkId { get; set; }

    [JsonPropertyName("location")]
    public string Location { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("response")]
    public object Response { get; set; }
}

public class ScimError
{
    [JsonPropertyName("schemas")]
    public List<string> Schemas { get; set; } = ["urn:ietf:params:scim:api:messages:2.0:Error"];

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("scimType")]
    public string ScimType { get; set; }

    [JsonPropertyName("detail")]
    public string Detail { get; set; }
}

public class ScimIntegration
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string Description { get; set; }
    public string BearerToken { get; set; } = "";
    public bool IsEnabled { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastSyncAt { get; set; }
    public string LastSyncStatus { get; set; }
    public int OperationCount { get; set; }
    public Dictionary<string, string> AttributeMapping { get; set; } = new();
}

public class ScimOperationLog
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string IntegrationId { get; set; } = "";
    public string Operation { get; set; } = "";
    public string ResourceType { get; set; } = "";
    public string ResourceId { get; set; }
    public string Status { get; set; } = "";
    public string Detail { get; set; }
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}

#endregion

public class ScimService
{
    private readonly ILogger<ScimService> _logger;
    private readonly IDirectoryStore _store;
    private readonly INamingContextService _ncService;
    private readonly IRidAllocator _ridAllocator;
    private readonly IUserAccountControlService _uacService;
    private readonly CosmosConfigurationStore _configStore;

    private readonly ConcurrentDictionary<string, ScimIntegration> _integrations = new();
    private readonly ConcurrentDictionary<string, List<ScimOperationLog>> _operationLogs = new();
    private bool _loaded;

    private const string ConfigScope = "cluster";
    private const string ConfigSection = "ScimIntegrations";
    private const int MaxLogsPerIntegration = 200;

    // Default SCIM → Directory attribute mapping
    public static readonly Dictionary<string, string> DefaultAttributeMapping = new()
    {
        ["userName"] = "sAMAccountName",
        ["name.givenName"] = "givenName",
        ["name.familyName"] = "sn",
        ["name.formatted"] = "displayName",
        ["emails[primary].value"] = "mail",
        ["title"] = "title",
        ["department"] = "department",
        ["phoneNumbers[work].value"] = "telephoneNumber",
        ["externalId"] = "extensionAttribute1"
    };

    public ScimService(
        ILogger<ScimService> logger,
        IDirectoryStore store,
        INamingContextService ncService,
        IRidAllocator ridAllocator,
        IUserAccountControlService uacService,
        CosmosConfigurationStore configStore)
    {
        _logger = logger;
        _store = store;
        _ncService = ncService;
        _ridAllocator = ridAllocator;
        _uacService = uacService;
        _configStore = configStore;
    }

    public async Task EnsureLoadedAsync()
    {
        if (_loaded) return;
        await LoadFromStoreAsync();
    }

    #region Integration Management

    public async Task<List<ScimIntegration>> GetAllIntegrations()
    {
        await EnsureLoadedAsync();
        return _integrations.Values.OrderBy(i => i.Name).ToList();
    }

    public async Task<ScimIntegration> GetIntegration(string id)
    {
        await EnsureLoadedAsync();
        return _integrations.GetValueOrDefault(id);
    }

    public async Task<ScimIntegration> CreateIntegration(ScimIntegration integration)
    {
        await EnsureLoadedAsync();
        integration.CreatedAt = DateTimeOffset.UtcNow;
        if (string.IsNullOrEmpty(integration.BearerToken))
            integration.BearerToken = GenerateBearerToken();
        if (integration.AttributeMapping.Count == 0)
            integration.AttributeMapping = new Dictionary<string, string>(DefaultAttributeMapping);
        _integrations[integration.Id] = integration;
        await PersistAsync();
        return integration;
    }

    public async Task<ScimIntegration> UpdateIntegration(string id, ScimIntegration updated)
    {
        await EnsureLoadedAsync();
        if (!_integrations.ContainsKey(id)) return null;
        updated.Id = id;
        _integrations[id] = updated;
        await PersistAsync();
        return updated;
    }

    public async Task<bool> DeleteIntegration(string id)
    {
        await EnsureLoadedAsync();
        if (!_integrations.TryRemove(id, out _)) return false;
        _operationLogs.TryRemove(id, out _);
        await PersistAsync();
        return true;
    }

    public async Task<string> ValidateBearerToken(string token)
    {
        await EnsureLoadedAsync();
        var integration = _integrations.Values.FirstOrDefault(i => i.IsEnabled && i.BearerToken == token);
        return integration?.Id;
    }

    public List<ScimOperationLog> GetOperationLogs(string integrationId)
    {
        return _operationLogs.TryGetValue(integrationId, out var list)
            ? list.OrderByDescending(l => l.Timestamp).ToList()
            : new();
    }

    #endregion

    #region SCIM User Operations

    public async Task<ScimListResponse<ScimUser>> ListUsers(string filter, int startIndex, int count)
    {
        var domainDn = _ncService.GetDomainNc().Dn;

        FilterNode filterNode = null;
        if (!string.IsNullOrEmpty(filter))
            filterNode = ParseScimFilter(filter);
        else
            filterNode = new EqualityFilterNode("objectCategory", "person");

        var result = await _store.SearchAsync("default", domainDn, SearchScope.WholeSubtree,
            filterNode, null, count, 0, null, count);

        var users = result.Entries
            .Where(e => e.ObjectClass.Contains("user"))
            .Select(MapToScimUser)
            .ToList();

        return new ScimListResponse<ScimUser>
        {
            TotalResults = result.TotalEstimate,
            StartIndex = startIndex,
            ItemsPerPage = users.Count,
            Resources = users
        };
    }

    public async Task<ScimUser> GetUser(string id)
    {
        var obj = await _store.GetByGuidAsync("default", id);
        if (obj == null || !obj.ObjectClass.Contains("user")) return null;
        return MapToScimUser(obj);
    }

    public async Task<ScimUser> CreateUser(ScimUser scimUser, string integrationId = null)
    {
        var domainDn = _ncService.GetDomainNc().Dn;
        var cn = scimUser.DisplayName ?? scimUser.Name?.Formatted ?? scimUser.UserName;
        var containerDn = $"CN=Users,{domainDn}";
        var dn = $"CN={cn},{containerDn}";

        var existing = await _store.GetByDnAsync("default", dn);
        if (existing != null)
            throw new InvalidOperationException($"User already exists: {dn}");

        var objectSid = await _ridAllocator.GenerateObjectSidAsync("default", domainDn);
        var uac = _uacService.GetDefaultUac("user");
        if (!scimUser.Active) uac |= 0x2;

        var now = DateTimeOffset.UtcNow;
        var usn = await _store.GetNextUsnAsync("default", domainDn);

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
            SAMAccountName = scimUser.UserName,
            UserPrincipalName = scimUser.UserName.Contains('@') ? scimUser.UserName : null,
            DisplayName = scimUser.DisplayName ?? scimUser.Name?.Formatted,
            GivenName = scimUser.Name?.GivenName,
            Sn = scimUser.Name?.FamilyName,
            Mail = scimUser.Emails?.FirstOrDefault(e => e.Primary)?.Value ?? scimUser.Emails?.FirstOrDefault()?.Value,
            Title = scimUser.Title,
            Department = scimUser.Department,
            UserAccountControl = uac,
            PrimaryGroupId = 513,
            ParentDn = containerDn,
            WhenCreated = now,
            WhenChanged = now,
            USNCreated = usn,
            USNChanged = usn,
            SAMAccountType = 0x30000000,
        };

        if (scimUser.ExternalId != null)
            obj.Attributes["extensionAttribute1"] = new DirectoryAttribute("extensionAttribute1", scimUser.ExternalId);

        if (scimUser.PhoneNumbers?.Count > 0)
            obj.Attributes["telephoneNumber"] = new DirectoryAttribute("telephoneNumber", scimUser.PhoneNumbers[0].Value ?? "");

        await _store.CreateAsync(obj);

        LogOperation(integrationId, "CREATE", "User", obj.ObjectGuid, "Success");

        scimUser.Id = obj.ObjectGuid;
        scimUser.Meta = new ScimMeta
        {
            ResourceType = "User",
            Created = now.ToString("o"),
            LastModified = now.ToString("o"),
            Location = $"/scim/v2/Users/{obj.ObjectGuid}"
        };

        return scimUser;
    }

    public async Task<ScimUser> ReplaceUser(string id, ScimUser scimUser, string integrationId = null)
    {
        var obj = await _store.GetByGuidAsync("default", id);
        if (obj == null || !obj.ObjectClass.Contains("user")) return null;

        var domainDn = _ncService.GetDomainNc().Dn;
        var usn = await _store.GetNextUsnAsync("default", domainDn);

        obj.SAMAccountName = scimUser.UserName;
        obj.DisplayName = scimUser.DisplayName ?? scimUser.Name?.Formatted;
        obj.GivenName = scimUser.Name?.GivenName;
        obj.Sn = scimUser.Name?.FamilyName;
        obj.Mail = scimUser.Emails?.FirstOrDefault(e => e.Primary)?.Value ?? scimUser.Emails?.FirstOrDefault()?.Value;
        obj.Title = scimUser.Title;
        obj.Department = scimUser.Department;
        obj.WhenChanged = DateTimeOffset.UtcNow;
        obj.USNChanged = usn;

        // Handle active/inactive
        if (!scimUser.Active)
            obj.UserAccountControl |= 0x2;
        else
            obj.UserAccountControl &= ~0x2;

        if (scimUser.ExternalId != null)
            obj.Attributes["extensionAttribute1"] = new DirectoryAttribute("extensionAttribute1", scimUser.ExternalId);

        if (scimUser.PhoneNumbers?.Count > 0)
            obj.Attributes["telephoneNumber"] = new DirectoryAttribute("telephoneNumber", scimUser.PhoneNumbers[0].Value ?? "");

        await _store.UpdateAsync(obj);
        LogOperation(integrationId, "REPLACE", "User", id, "Success");

        return MapToScimUser(obj);
    }

    public async Task<ScimUser> PatchUser(string id, ScimPatchRequest patch, string integrationId = null)
    {
        var obj = await _store.GetByGuidAsync("default", id);
        if (obj == null || !obj.ObjectClass.Contains("user")) return null;

        var domainDn = _ncService.GetDomainNc().Dn;
        var usn = await _store.GetNextUsnAsync("default", domainDn);

        foreach (var op in patch.Operations)
        {
            ApplyPatchOperation(obj, op);
        }

        obj.WhenChanged = DateTimeOffset.UtcNow;
        obj.USNChanged = usn;
        await _store.UpdateAsync(obj);
        LogOperation(integrationId, "PATCH", "User", id, "Success");

        return MapToScimUser(obj);
    }

    public async Task<bool> DeleteUser(string id, string integrationId = null)
    {
        var obj = await _store.GetByGuidAsync("default", id);
        if (obj == null || !obj.ObjectClass.Contains("user")) return false;

        // Soft-delete by disabling account
        obj.UserAccountControl |= 0x2;
        var domainDn = _ncService.GetDomainNc().Dn;
        var usn = await _store.GetNextUsnAsync("default", domainDn);
        obj.WhenChanged = DateTimeOffset.UtcNow;
        obj.USNChanged = usn;
        await _store.UpdateAsync(obj);
        LogOperation(integrationId, "DELETE", "User", id, "Success");

        return true;
    }

    #endregion

    #region SCIM Group Operations

    public async Task<ScimListResponse<ScimGroup>> ListGroups(string filter, int startIndex, int count)
    {
        var domainDn = _ncService.GetDomainNc().Dn;

        FilterNode filterNode = null;
        if (!string.IsNullOrEmpty(filter))
            filterNode = ParseScimFilter(filter);
        else
            filterNode = new EqualityFilterNode("objectCategory", "group");

        var result = await _store.SearchAsync("default", domainDn, SearchScope.WholeSubtree,
            filterNode, null, count, 0, null, count);

        var groups = result.Entries
            .Where(e => e.ObjectClass.Contains("group"))
            .Select(MapToScimGroup)
            .ToList();

        return new ScimListResponse<ScimGroup>
        {
            TotalResults = result.TotalEstimate,
            StartIndex = startIndex,
            ItemsPerPage = groups.Count,
            Resources = groups
        };
    }

    public async Task<ScimGroup> GetGroup(string id)
    {
        var obj = await _store.GetByGuidAsync("default", id);
        if (obj == null || !obj.ObjectClass.Contains("group")) return null;
        return MapToScimGroup(obj);
    }

    public async Task<ScimGroup> CreateGroup(ScimGroup scimGroup, string integrationId = null)
    {
        var domainDn = _ncService.GetDomainNc().Dn;
        var containerDn = $"CN=Users,{domainDn}";
        var dn = $"CN={scimGroup.DisplayName},{containerDn}";

        var existing = await _store.GetByDnAsync("default", dn);
        if (existing != null)
            throw new InvalidOperationException($"Group already exists: {dn}");

        var objectSid = await _ridAllocator.GenerateObjectSidAsync("default", domainDn);
        var now = DateTimeOffset.UtcNow;
        var usn = await _store.GetNextUsnAsync("default", domainDn);

        var obj = new DirectoryObject
        {
            Id = dn.ToLowerInvariant(),
            TenantId = "default",
            DomainDn = domainDn,
            DistinguishedName = dn,
            ObjectGuid = Guid.NewGuid().ToString(),
            ObjectSid = objectSid,
            ObjectClass = ["top", "group"],
            ObjectCategory = "group",
            Cn = scimGroup.DisplayName,
            DisplayName = scimGroup.DisplayName,
            GroupType = unchecked((int)0x80000002), // Global security group
            ParentDn = containerDn,
            WhenCreated = now,
            WhenChanged = now,
            USNCreated = usn,
            USNChanged = usn,
            SAMAccountName = scimGroup.DisplayName,
            SAMAccountType = 0x10000000,
        };

        await _store.CreateAsync(obj);
        LogOperation(integrationId, "CREATE", "Group", obj.ObjectGuid, "Success");

        scimGroup.Id = obj.ObjectGuid;
        scimGroup.Meta = new ScimMeta
        {
            ResourceType = "Group",
            Created = now.ToString("o"),
            LastModified = now.ToString("o"),
            Location = $"/scim/v2/Groups/{obj.ObjectGuid}"
        };

        return scimGroup;
    }

    public async Task<ScimGroup> ReplaceGroup(string id, ScimGroup scimGroup, string integrationId = null)
    {
        var obj = await _store.GetByGuidAsync("default", id);
        if (obj == null || !obj.ObjectClass.Contains("group")) return null;

        var domainDn = _ncService.GetDomainNc().Dn;
        var usn = await _store.GetNextUsnAsync("default", domainDn);

        obj.DisplayName = scimGroup.DisplayName;
        obj.Cn = scimGroup.DisplayName;
        obj.WhenChanged = DateTimeOffset.UtcNow;
        obj.USNChanged = usn;

        if (scimGroup.Members != null)
        {
            obj.Member = new List<string>();
            foreach (var m in scimGroup.Members)
            {
                if (m.Value != null)
                {
                    var memberObj = await _store.GetByGuidAsync("default", m.Value);
                    if (memberObj != null)
                        obj.Member.Add(memberObj.DistinguishedName);
                }
            }
        }

        await _store.UpdateAsync(obj);
        LogOperation(integrationId, "REPLACE", "Group", id, "Success");

        return MapToScimGroup(obj);
    }

    public async Task<ScimGroup> PatchGroup(string id, ScimPatchRequest patch, string integrationId = null)
    {
        var obj = await _store.GetByGuidAsync("default", id);
        if (obj == null || !obj.ObjectClass.Contains("group")) return null;

        var domainDn = _ncService.GetDomainNc().Dn;
        var usn = await _store.GetNextUsnAsync("default", domainDn);

        foreach (var op in patch.Operations)
        {
            await ApplyGroupPatchOperation(obj, op);
        }

        obj.WhenChanged = DateTimeOffset.UtcNow;
        obj.USNChanged = usn;
        await _store.UpdateAsync(obj);
        LogOperation(integrationId, "PATCH", "Group", id, "Success");

        return MapToScimGroup(obj);
    }

    public async Task<bool> DeleteGroup(string id, string integrationId = null)
    {
        var obj = await _store.GetByGuidAsync("default", id);
        if (obj == null || !obj.ObjectClass.Contains("group")) return false;

        await _store.DeleteAsync("default", obj.DistinguishedName);
        LogOperation(integrationId, "DELETE", "Group", id, "Success");
        return true;
    }

    #endregion

    #region SCIM Discovery Endpoints

    public object GetServiceProviderConfig()
    {
        return new
        {
            schemas = new[] { "urn:ietf:params:scim:schemas:core:2.0:ServiceProviderConfig" },
            documentationUri = "https://directory.net/docs/scim",
            patch = new { supported = true },
            bulk = new { supported = true, maxOperations = 100, maxPayloadSize = 1048576 },
            filter = new { supported = true, maxResults = 200 },
            changePassword = new { supported = false },
            sort = new { supported = false },
            etag = new { supported = false },
            authenticationSchemes = new[]
            {
                new
                {
                    type = "oauthbearertoken",
                    name = "Bearer Token",
                    description = "Authentication using a static bearer token per integration"
                }
            }
        };
    }

    public object GetSchemas()
    {
        return new[]
        {
            new
            {
                id = "urn:ietf:params:scim:schemas:core:2.0:User",
                name = "User",
                description = "User Account",
                attributes = new[]
                {
                    new { name = "userName", type = "string", multiValued = false, required = true, mutability = "readWrite" },
                    new { name = "name", type = "complex", multiValued = false, required = false, mutability = "readWrite" },
                    new { name = "displayName", type = "string", multiValued = false, required = false, mutability = "readWrite" },
                    new { name = "emails", type = "complex", multiValued = true, required = false, mutability = "readWrite" },
                    new { name = "active", type = "boolean", multiValued = false, required = false, mutability = "readWrite" },
                    new { name = "title", type = "string", multiValued = false, required = false, mutability = "readWrite" },
                    new { name = "department", type = "string", multiValued = false, required = false, mutability = "readWrite" },
                    new { name = "phoneNumbers", type = "complex", multiValued = true, required = false, mutability = "readWrite" },
                    new { name = "externalId", type = "string", multiValued = false, required = false, mutability = "readWrite" },
                }
            },
            new
            {
                id = "urn:ietf:params:scim:schemas:core:2.0:Group",
                name = "Group",
                description = "Group",
                attributes = new[]
                {
                    new { name = "displayName", type = "string", multiValued = false, required = true, mutability = "readWrite" },
                    new { name = "members", type = "complex", multiValued = true, required = false, mutability = "readWrite" },
                }
            }
        };
    }

    public object GetResourceTypes()
    {
        return new[]
        {
            new
            {
                schemas = new[] { "urn:ietf:params:scim:schemas:core:2.0:ResourceType" },
                id = "User",
                name = "User",
                endpoint = "/scim/v2/Users",
                schema = "urn:ietf:params:scim:schemas:core:2.0:User"
            },
            new
            {
                schemas = new[] { "urn:ietf:params:scim:schemas:core:2.0:ResourceType" },
                id = "Group",
                name = "Group",
                endpoint = "/scim/v2/Groups",
                schema = "urn:ietf:params:scim:schemas:core:2.0:Group"
            }
        };
    }

    #endregion

    #region Bulk Operations

    public async Task<ScimBulkResponse> ProcessBulk(ScimBulkRequest request, string integrationId = null)
    {
        var response = new ScimBulkResponse();

        foreach (var op in request.Operations)
        {
            var opResponse = new ScimBulkOperationResponse
            {
                Method = op.Method,
                BulkId = op.BulkId
            };

            try
            {
                var path = op.Path.TrimStart('/');
                if (path.StartsWith("scim/v2/")) path = path["scim/v2/".Length..];

                switch (op.Method.ToUpperInvariant())
                {
                    case "POST" when path.StartsWith("Users", StringComparison.OrdinalIgnoreCase):
                        var newUser = op.Data.HasValue
                            ? JsonSerializer.Deserialize<ScimUser>(op.Data.Value.GetRawText())
                            : null;
                        if (newUser != null)
                        {
                            var created = await CreateUser(newUser, integrationId);
                            opResponse.Location = $"/scim/v2/Users/{created.Id}";
                            opResponse.Status = "201";
                        }
                        break;

                    case "POST" when path.StartsWith("Groups", StringComparison.OrdinalIgnoreCase):
                        var newGroup = op.Data.HasValue
                            ? JsonSerializer.Deserialize<ScimGroup>(op.Data.Value.GetRawText())
                            : null;
                        if (newGroup != null)
                        {
                            var created = await CreateGroup(newGroup, integrationId);
                            opResponse.Location = $"/scim/v2/Groups/{created.Id}";
                            opResponse.Status = "201";
                        }
                        break;

                    case "DELETE":
                        var parts = path.Split('/');
                        if (parts.Length >= 2)
                        {
                            var resourceId = parts[1];
                            if (parts[0].Equals("Users", StringComparison.OrdinalIgnoreCase))
                                await DeleteUser(resourceId, integrationId);
                            else if (parts[0].Equals("Groups", StringComparison.OrdinalIgnoreCase))
                                await DeleteGroup(resourceId, integrationId);
                            opResponse.Status = "204";
                        }
                        break;

                    default:
                        opResponse.Status = "400";
                        break;
                }
            }
            catch (Exception ex)
            {
                opResponse.Status = "500";
                opResponse.Response = new ScimError { Status = "500", Detail = ex.Message };
            }

            response.Operations.Add(opResponse);
        }

        return response;
    }

    #endregion

    #region Helpers

    private ScimUser MapToScimUser(DirectoryObject obj)
    {
        var isDisabled = (obj.UserAccountControl & 0x2) != 0;
        var phone = obj.GetAttribute("telephoneNumber")?.GetFirstString();

        return new ScimUser
        {
            Id = obj.ObjectGuid,
            ExternalId = obj.GetAttribute("extensionAttribute1")?.GetFirstString(),
            UserName = obj.SAMAccountName ?? obj.UserPrincipalName ?? "",
            Name = new ScimName
            {
                GivenName = obj.GivenName,
                FamilyName = obj.Sn,
                Formatted = obj.DisplayName
            },
            DisplayName = obj.DisplayName,
            Emails = obj.Mail != null
                ? new List<ScimMultiValuedAttribute> { new() { Value = obj.Mail, Type = "work", Primary = true } }
                : null,
            PhoneNumbers = phone != null
                ? new List<ScimMultiValuedAttribute> { new() { Value = phone, Type = "work", Primary = true } }
                : null,
            Active = !isDisabled,
            Title = obj.Title,
            Department = obj.Department,
            Meta = new ScimMeta
            {
                ResourceType = "User",
                Created = obj.WhenCreated.ToString("o"),
                LastModified = obj.WhenChanged.ToString("o"),
                Location = $"/scim/v2/Users/{obj.ObjectGuid}"
            }
        };
    }

    private ScimGroup MapToScimGroup(DirectoryObject obj)
    {
        return new ScimGroup
        {
            Id = obj.ObjectGuid,
            DisplayName = obj.DisplayName ?? obj.Cn ?? "",
            Members = obj.Member.Select(dn => new ScimMember { Value = dn, Display = dn }).ToList(),
            Meta = new ScimMeta
            {
                ResourceType = "Group",
                Created = obj.WhenCreated.ToString("o"),
                LastModified = obj.WhenChanged.ToString("o"),
                Location = $"/scim/v2/Groups/{obj.ObjectGuid}"
            }
        };
    }

    private FilterNode ParseScimFilter(string filter)
    {
        // Parse SCIM filter expressions into LDAP filter nodes
        // Supports: eq, ne, co, sw, ew, gt, lt, ge, le, pr, and, or, not
        filter = filter.Trim();

        // Handle compound: and / or
        var andIdx = FindOperator(filter, " and ");
        if (andIdx >= 0)
        {
            var left = ParseScimFilter(filter[..andIdx]);
            var right = ParseScimFilter(filter[(andIdx + 5)..]);
            return new AndFilterNode(new List<FilterNode> { left, right });
        }

        var orIdx = FindOperator(filter, " or ");
        if (orIdx >= 0)
        {
            var left = ParseScimFilter(filter[..orIdx]);
            var right = ParseScimFilter(filter[(orIdx + 4)..]);
            return new OrFilterNode(new List<FilterNode> { left, right });
        }

        // Handle not
        if (filter.StartsWith("not ", StringComparison.OrdinalIgnoreCase))
        {
            var inner = ParseScimFilter(filter[4..].Trim().Trim('(', ')'));
            return new NotFilterNode(inner);
        }

        // Handle pr (presence)
        if (filter.EndsWith(" pr", StringComparison.OrdinalIgnoreCase))
        {
            var attr = MapScimAttrToLdap(filter[..^3].Trim());
            return new PresenceFilterNode(attr);
        }

        // Handle binary operators
        var operators = new[] { " eq ", " ne ", " co ", " sw ", " ew ", " gt ", " lt ", " ge ", " le " };
        foreach (var op in operators)
        {
            var idx = filter.IndexOf(op, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) continue;

            var attrName = MapScimAttrToLdap(filter[..idx].Trim());
            var value = filter[(idx + op.Length)..].Trim().Trim('"');

            return op.Trim() switch
            {
                "eq" => new EqualityFilterNode(attrName, value),
                "ne" => new NotFilterNode(new EqualityFilterNode(attrName, value)),
                "co" => new SubstringFilterNode(attrName, null, new List<string> { value }, null),
                "sw" => new SubstringFilterNode(attrName, value, new List<string>(), null),
                "ew" => new SubstringFilterNode(attrName, null, new List<string>(), value),
                "gt" or "ge" => new GreaterOrEqualFilterNode(attrName, value),
                "lt" or "le" => new LessOrEqualFilterNode(attrName, value),
                _ => new EqualityFilterNode(attrName, value)
            };
        }

        // Fallback: treat as equality on objectCategory
        return new EqualityFilterNode("objectCategory", "person");
    }

    private static int FindOperator(string filter, string op)
    {
        // Find operator outside of quoted strings
        var inQuote = false;
        var depth = 0;
        for (int i = 0; i < filter.Length - op.Length; i++)
        {
            if (filter[i] == '"') inQuote = !inQuote;
            if (filter[i] == '(') depth++;
            if (filter[i] == ')') depth--;
            if (!inQuote && depth == 0 && filter.Substring(i, op.Length).Equals(op, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    private static string MapScimAttrToLdap(string scimAttr)
    {
        return scimAttr.ToLowerInvariant() switch
        {
            "username" => "sAMAccountName",
            "name.givenname" => "givenName",
            "name.familyname" => "sn",
            "name.formatted" => "displayName",
            "displayname" => "displayName",
            "emails.value" or "emails[type eq \"work\"].value" => "mail",
            "active" => "userAccountControl",
            "title" => "title",
            "department" => "department",
            "externalid" => "extensionAttribute1",
            "id" => "objectGuid",
            _ => scimAttr
        };
    }

    private void ApplyPatchOperation(DirectoryObject obj, ScimPatchOperation op)
    {
        var path = op.Path?.ToLowerInvariant() ?? "";
        var valueStr = op.Value?.ValueKind == JsonValueKind.String ? op.Value.Value.GetString() : op.Value?.ToString();

        switch (op.Op.ToLowerInvariant())
        {
            case "replace":
                switch (path)
                {
                    case "username": obj.SAMAccountName = valueStr; break;
                    case "name.givenname": obj.GivenName = valueStr; break;
                    case "name.familyname": obj.Sn = valueStr; break;
                    case "displayname" or "name.formatted": obj.DisplayName = valueStr; break;
                    case "title": obj.Title = valueStr; break;
                    case "department": obj.Department = valueStr; break;
                    case "active":
                        var active = op.Value?.ValueKind == JsonValueKind.True ||
                                     (op.Value?.ValueKind == JsonValueKind.String && bool.TryParse(valueStr, out var b) && b);
                        if (active) obj.UserAccountControl &= ~0x2;
                        else obj.UserAccountControl |= 0x2;
                        break;
                    case "emails[type eq \"work\"].value" or "emails":
                        if (op.Value?.ValueKind == JsonValueKind.Array)
                        {
                            var emails = JsonSerializer.Deserialize<List<ScimMultiValuedAttribute>>(op.Value.Value.GetRawText());
                            obj.Mail = emails?.FirstOrDefault(e => e.Primary)?.Value ?? emails?.FirstOrDefault()?.Value;
                        }
                        else
                        {
                            obj.Mail = valueStr;
                        }
                        break;
                }
                break;

            case "add":
                // same as replace for simple attrs
                ApplyPatchOperation(obj, new ScimPatchOperation { Op = "replace", Path = op.Path, Value = op.Value });
                break;

            case "remove":
                switch (path)
                {
                    case "title": obj.Title = null; break;
                    case "department": obj.Department = null; break;
                    case "displayname": obj.DisplayName = null; break;
                }
                break;
        }
    }

    private async Task ApplyGroupPatchOperation(DirectoryObject group, ScimPatchOperation op)
    {
        var path = op.Path?.ToLowerInvariant() ?? "";

        switch (op.Op.ToLowerInvariant())
        {
            case "add" when path == "members":
                if (op.Value?.ValueKind == JsonValueKind.Array)
                {
                    var members = JsonSerializer.Deserialize<List<ScimMember>>(op.Value.Value.GetRawText());
                    if (members != null)
                    {
                        foreach (var m in members)
                        {
                            if (m.Value == null) continue;
                            var memberObj = await _store.GetByGuidAsync("default", m.Value);
                            if (memberObj != null && !group.Member.Contains(memberObj.DistinguishedName))
                                group.Member.Add(memberObj.DistinguishedName);
                        }
                    }
                }
                break;

            case "remove" when path.StartsWith("members[value eq"):
                // members[value eq "guid"]
                var guidStart = path.IndexOf('"') + 1;
                var guidEnd = path.IndexOf('"', guidStart);
                if (guidStart > 0 && guidEnd > guidStart)
                {
                    var guid = path[guidStart..guidEnd];
                    var memberObj = await _store.GetByGuidAsync("default", guid);
                    if (memberObj != null)
                        group.Member.Remove(memberObj.DistinguishedName);
                }
                break;

            case "replace" when path == "displayname":
                var val = op.Value?.ValueKind == JsonValueKind.String ? op.Value.Value.GetString() : null;
                if (val != null)
                {
                    group.DisplayName = val;
                    group.Cn = val;
                }
                break;
        }
    }

    private void LogOperation(string integrationId, string operation, string resourceType, string resourceId, string status)
    {
        if (integrationId == null) return;

        var log = new ScimOperationLog
        {
            IntegrationId = integrationId,
            Operation = operation,
            ResourceType = resourceType,
            ResourceId = resourceId,
            Status = status
        };

        var list = _operationLogs.GetOrAdd(integrationId, _ => new List<ScimOperationLog>());
        lock (list)
        {
            list.Add(log);
            if (list.Count > MaxLogsPerIntegration)
                list.RemoveRange(0, list.Count - MaxLogsPerIntegration);
        }

        // Update integration stats
        if (_integrations.TryGetValue(integrationId, out var integration))
        {
            integration.LastSyncAt = DateTimeOffset.UtcNow;
            integration.LastSyncStatus = status;
            integration.OperationCount++;
        }
    }

    public static string GenerateBearerToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).Replace("+", "").Replace("/", "").Replace("=", "");
    }

    #endregion

    #region Persistence

    private async Task LoadFromStoreAsync()
    {
        try
        {
            var doc = await _configStore.GetSectionAsync("default", ConfigScope, ConfigSection);
            if (doc != null && doc.Values.TryGetValue("integrations", out var intElement))
            {
                var integrations = JsonSerializer.Deserialize<List<ScimIntegration>>(intElement.GetRawText());
                if (integrations != null)
                {
                    foreach (var i in integrations)
                        _integrations[i.Id] = i;
                }
            }
            _loaded = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load SCIM integrations from configuration store");
            _loaded = true;
        }
    }

    private async Task PersistAsync()
    {
        try
        {
            var json = JsonSerializer.SerializeToElement(_integrations.Values.ToList());
            var doc = await _configStore.GetSectionAsync("default", ConfigScope, ConfigSection)
                      ?? new ConfigurationDocument
                      {
                          Id = $"{ConfigScope}::{ConfigSection}",
                          TenantId = "default",
                          Scope = ConfigScope,
                          Section = ConfigSection
                      };
            doc.Values["integrations"] = json;
            await _configStore.UpsertSectionAsync(doc);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist SCIM integrations");
        }
    }

    #endregion
}
