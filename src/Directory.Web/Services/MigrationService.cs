using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json.Serialization;
using Directory.Core.Interfaces;
using Directory.Core.Ldif;
using Directory.Core.Models;
using Directory.CosmosDb.Configuration;
using Directory.Ldap.Client;

namespace Directory.Web.Services;

public class MigrationSource
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public MigrationSourceType Type { get; set; }
    public string Host { get; set; } = "";
    public int Port { get; set; } = 389;
    public bool UseSsl { get; set; }
    public string BindDn { get; set; } = "";
    public string BindPassword { get; set; } = "";
    public string BaseDn { get; set; } = "";
    public string Filter { get; set; }
}

public enum MigrationSourceType { ActiveDirectory, OpenLDAP, FreeIPA, GenericLDAP, LdifFile }

public class MigrationPlan
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SourceId { get; set; } = "";
    public string TargetBaseDn { get; set; } = "";
    public List<MigrationMapping> AttributeMappings { get; set; } = new();
    public MigrationOptions Options { get; set; } = new();
    public MigrationPreview Preview { get; set; }
}

public class MigrationMapping
{
    public string SourceAttribute { get; set; } = "";
    public string TargetAttribute { get; set; } = "";
    public string TransformRule { get; set; }
}

public class MigrationOptions
{
    public bool MigrateUsers { get; set; } = true;
    public bool MigrateGroups { get; set; } = true;
    public bool MigrateOUs { get; set; } = true;
    public bool MigrateComputers { get; set; }
    public bool PreserveSidHistory { get; set; }
    public bool PreservePasswords { get; set; }
    public bool MigrateGroupMemberships { get; set; } = true;
    public bool DryRun { get; set; }
    public ConflictResolution OnConflict { get; set; } = ConflictResolution.Skip;
}

public enum ConflictResolution { Skip, Overwrite, Merge, Rename }

public class MigrationPreview
{
    public int Users { get; set; }
    public int Groups { get; set; }
    public int OUs { get; set; }
    public int Computers { get; set; }
    public int Conflicts { get; set; }
    public List<string> Warnings { get; set; } = new();
}

public class MigrationResult
{
    public string PlanId { get; set; } = "";
    public int TotalProcessed { get; set; }
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
    public List<MigrationError> Errors { get; set; } = new();
    public TimeSpan Duration { get; set; }
    public MigrationStatus Status { get; set; } = MigrationStatus.Pending;
    public double ProgressPercent { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public class MigrationError
{
    public string Dn { get; set; } = "";
    public string ObjectClass { get; set; } = "";
    public string Message { get; set; } = "";
}

public enum MigrationStatus { Pending, Running, Completed, Failed, Cancelled }

public class SchemaDiscoveryResult
{
    public List<string> ObjectClasses { get; set; } = new();
    public List<string> Attributes { get; set; } = new();
    public int EstimatedObjectCount { get; set; }
}

public class MigrationHistoryEntry
{
    public string PlanId { get; set; } = "";
    public string SourceName { get; set; } = "";
    public MigrationSourceType SourceType { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public MigrationStatus Status { get; set; }
    public int TotalProcessed { get; set; }
    public int Created { get; set; }
    public int Failed { get; set; }
}

public class MigrationService
{
    private readonly ILogger<MigrationService> _logger;
    private readonly CosmosConfigurationStore _configStore;
    private readonly IDirectoryStore _directoryStore;

    private readonly ConcurrentDictionary<string, MigrationSource> _sources = new();
    private readonly ConcurrentDictionary<string, MigrationResult> _activeResults = new();
    private readonly ConcurrentDictionary<string, MigrationHistoryEntry> _history = new();
    private bool _loaded;

    private const string ConfigScope = "cluster";
    private const string ConfigSection = "Migration";

    public MigrationService(
        ILogger<MigrationService> logger,
        CosmosConfigurationStore configStore,
        IDirectoryStore directoryStore)
    {
        _logger = logger;
        _configStore = configStore;
        _directoryStore = directoryStore;
    }

    private async Task EnsureLoadedAsync()
    {
        if (_loaded) return;
        try
        {
            var sources = await _configStore.GetAsync<List<MigrationSource>>(ConfigScope, ConfigSection, "Sources");
            if (sources != null)
            {
                foreach (var s in sources)
                    _sources[s.Id] = s;
            }

            var history = await _configStore.GetAsync<List<MigrationHistoryEntry>>(ConfigScope, ConfigSection, "History");
            if (history != null)
            {
                foreach (var h in history)
                    _history[h.PlanId] = h;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load migration config from Cosmos DB");
        }
        _loaded = true;
    }

    private async Task SaveSourcesAsync()
    {
        await _configStore.SetAsync(ConfigScope, ConfigSection, "Sources", _sources.Values.ToList());
    }

    private async Task SaveHistoryAsync()
    {
        await _configStore.SetAsync(ConfigScope, ConfigSection, "History", _history.Values.ToList());
    }

    public async Task<ConnectionTestResult> TestConnection(MigrationSource source)
    {
        await Task.CompletedTask;
        _logger.LogInformation("Testing connection to {Host}:{Port} ({Type})", source.Host, source.Port, source.Type);

        if (source.Type == MigrationSourceType.LdifFile)
        {
            return new ConnectionTestResult
            {
                Success = true,
                Message = "LDIF file source does not require a connection test. Provide the LDIF content during migration."
            };
        }

        try
        {
            if (string.IsNullOrWhiteSpace(source.Host))
                return new ConnectionTestResult { Success = false, Message = "Host is required." };

            if (source.Port <= 0 || source.Port > 65535)
                return new ConnectionTestResult { Success = false, Message = "Invalid port number." };

            if (string.IsNullOrWhiteSpace(source.BaseDn))
                return new ConnectionTestResult { Success = false, Message = "Base DN is required." };

            var tlsOptions = new LdapClientTlsOptions
            {
                SkipCertificateValidation = true // migration sources may use untrusted certs
            };

            await using var ldapClient = new LdapClient(tlsOptions, logger: _logger);

            using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            try
            {
                await ldapClient.ConnectAsync(source.Host, source.Port, source.UseSsl, connectCts.Token);
            }
            catch (OperationCanceledException)
            {
                return new ConnectionTestResult { Success = false, Message = $"Connection to {source.Host}:{source.Port} timed out after 10 seconds." };
            }
            catch (System.Net.Sockets.SocketException ex)
            {
                return new ConnectionTestResult { Success = false, Message = $"Connection refused or unreachable: {ex.Message}" };
            }

            // Attempt bind if credentials are provided
            if (!string.IsNullOrWhiteSpace(source.BindDn))
            {
                var bindResult = await ldapClient.BindAsync(source.BindDn, source.BindPassword ?? "", connectCts.Token);
                if (!bindResult.Success)
                {
                    return new ConnectionTestResult
                    {
                        Success = false,
                        Message = $"LDAP bind failed (result code {bindResult.ResultCode}): {bindResult.DiagnosticMessage}. Check credentials and bind DN."
                    };
                }
            }

            // Read RootDSE to verify the server is responding to LDAP queries
            var rootDse = await ldapClient.ReadRootDseAsync(connectCts.Token);
            var serverType = source.Type.ToString();

            if (rootDse.TryGetValue("vendorName", out var vendorNames) && vendorNames.Count > 0)
            {
                serverType = $"{source.Type} ({vendorNames[0]})";
            }
            else if (rootDse.TryGetValue("forestFunctionality", out _))
            {
                serverType = $"{source.Type} (Active Directory)";
            }

            return new ConnectionTestResult
            {
                Success = true,
                Message = $"Successfully connected and authenticated to {source.Host}:{source.Port}. RootDSE returned {rootDse.Count} attributes.",
                ServerType = serverType
            };
        }
        catch (System.Security.Authentication.AuthenticationException ex)
        {
            return new ConnectionTestResult
            {
                Success = false,
                Message = $"TLS handshake failed: {ex.Message}. Check SSL/TLS settings and server certificate."
            };
        }
        catch (Exception ex)
        {
            return new ConnectionTestResult
            {
                Success = false,
                Message = $"Connection failed: {ex.Message}"
            };
        }
    }

    public async Task<SchemaDiscoveryResult> DiscoverSchema(MigrationSource source, string ldifContent = null)
    {
        _logger.LogInformation("Discovering schema from {Host}:{Port} ({Type})", source.Host, source.Port, source.Type);

        if (source.Type == MigrationSourceType.LdifFile)
        {
            return await DiscoverSchemaFromLdif(ldifContent);
        }

        // For LDAP-based sources, query the target directory store for existing schema
        // and return known attributes for the given source type.
        // Live LDAP schema enumeration is not performed here (requires System.DirectoryServices.Protocols).
        var result = await DiscoverSchemaFromDirectoryStore();

        // Merge in well-known attributes based on source type
        if (source.Type == MigrationSourceType.ActiveDirectory)
        {
            MergeList(result.ObjectClasses, new[]
            {
                "user", "group", "organizationalUnit", "computer",
                "contact", "printQueue", "volume", "container"
            });
            MergeList(result.Attributes, new[]
            {
                "cn", "sn", "givenName", "displayName", "sAMAccountName",
                "userPrincipalName", "mail", "telephoneNumber", "title",
                "department", "company", "manager", "memberOf", "member",
                "description", "distinguishedName", "objectGUID", "objectSid",
                "whenCreated", "whenChanged", "userAccountControl",
                "pwdLastSet", "lastLogonTimestamp", "accountExpires",
                "homeDirectory", "homeDrive", "scriptPath", "profilePath"
            });
        }
        else if (source.Type == MigrationSourceType.OpenLDAP || source.Type == MigrationSourceType.FreeIPA)
        {
            MergeList(result.ObjectClasses, new[]
            {
                "inetOrgPerson", "posixAccount", "posixGroup",
                "organizationalUnit", "groupOfNames", "groupOfUniqueNames"
            });
            MergeList(result.Attributes, new[]
            {
                "cn", "sn", "givenName", "displayName", "uid",
                "mail", "telephoneNumber", "title", "description",
                "uidNumber", "gidNumber", "homeDirectory", "loginShell",
                "memberUid", "uniqueMember", "member"
            });
        }
        else
        {
            MergeList(result.ObjectClasses, new[] { "top", "person", "organizationalUnit", "groupOfNames" });
            MergeList(result.Attributes, new[] { "cn", "sn", "description", "member", "objectClass" });
        }

        return result;
    }

    private async Task<SchemaDiscoveryResult> DiscoverSchemaFromDirectoryStore()
    {
        var result = new SchemaDiscoveryResult();
        try
        {
            // Query the local directory store for existing object classes
            var searchResult = await _directoryStore.SearchAsync(
                TenantId,
                "",
                SearchScope.WholeSubtree,
                null,
                new[] { "objectClass" },
                sizeLimit: 1000);

            var objectClassSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in searchResult.Entries)
            {
                foreach (var oc in entry.ObjectClass)
                    objectClassSet.Add(oc);
            }

            result.ObjectClasses = objectClassSet.OrderBy(c => c).ToList();
            result.EstimatedObjectCount = searchResult.Entries.Count;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query directory store for schema discovery");
        }
        return result;
    }

    private async Task<SchemaDiscoveryResult> DiscoverSchemaFromLdif(string ldifContent)
    {
        var result = new SchemaDiscoveryResult();

        if (string.IsNullOrWhiteSpace(ldifContent))
        {
            result.ObjectClasses = new List<string> { "top", "person", "organizationalUnit", "groupOfNames" };
            result.Attributes = new List<string> { "cn", "sn", "description", "member", "objectClass" };
            return result;
        }

        try
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(ldifContent);
            using var stream = new MemoryStream(bytes);
            var records = await LdifReader.ParseAsync(stream);

            var objectClassSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var attributeSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var record in records)
            {
                foreach (var kvp in record.Attributes)
                {
                    attributeSet.Add(kvp.Key);
                    if (kvp.Key.Equals("objectClass", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var val in kvp.Value)
                            objectClassSet.Add(val);
                    }
                }
            }

            result.ObjectClasses = objectClassSet.OrderBy(c => c).ToList();
            result.Attributes = attributeSet.OrderBy(a => a).ToList();
            result.EstimatedObjectCount = records.Count;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse LDIF content for schema discovery");
        }

        return result;
    }

    private static void MergeList(List<string> target, IEnumerable<string> additions)
    {
        var existing = new HashSet<string>(target, StringComparer.OrdinalIgnoreCase);
        foreach (var item in additions)
        {
            if (existing.Add(item))
                target.Add(item);
        }
    }

    public async Task<MigrationPreview> PreviewMigration(MigrationPlan plan, string ldifContent = null)
    {
        await EnsureLoadedAsync();
        _logger.LogInformation("Previewing migration for plan {PlanId}", plan.Id);

        var preview = new MigrationPreview();

        if (!_sources.TryGetValue(plan.SourceId, out var source))
        {
            preview.Warnings.Add("Source not found. Please configure a migration source first.");
            return preview;
        }

        if (source.Type == MigrationSourceType.LdifFile)
        {
            // Parse the LDIF to get real counts
            if (!string.IsNullOrWhiteSpace(ldifContent))
            {
                try
                {
                    var bytes = System.Text.Encoding.UTF8.GetBytes(ldifContent);
                    using var stream = new MemoryStream(bytes);
                    var records = await LdifReader.ParseAsync(stream);

                    foreach (var record in records)
                    {
                        var objectClasses = record.Attributes.TryGetValue("objectClass", out var ocs) ? ocs : new List<string>();
                        if (objectClasses.Any(c => c.Equals("user", StringComparison.OrdinalIgnoreCase) ||
                                                    c.Equals("inetOrgPerson", StringComparison.OrdinalIgnoreCase) ||
                                                    c.Equals("person", StringComparison.OrdinalIgnoreCase)))
                            preview.Users++;
                        else if (objectClasses.Any(c => c.Equals("group", StringComparison.OrdinalIgnoreCase) ||
                                                         c.Equals("groupOfNames", StringComparison.OrdinalIgnoreCase) ||
                                                         c.Equals("posixGroup", StringComparison.OrdinalIgnoreCase)))
                            preview.Groups++;
                        else if (objectClasses.Any(c => c.Equals("organizationalUnit", StringComparison.OrdinalIgnoreCase)))
                            preview.OUs++;
                        else if (objectClasses.Any(c => c.Equals("computer", StringComparison.OrdinalIgnoreCase)))
                            preview.Computers++;
                    }

                    preview.Warnings.Add($"LDIF file contains {records.Count} total records.");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse LDIF content for preview");
                    preview.Warnings.Add("Failed to parse LDIF content. Verify the file format.");
                }
            }
            else
            {
                preview.Warnings.Add("No LDIF content provided. Provide LDIF content to preview object counts.");
            }
        }
        else
        {
            // For LDAP-based sources, connect to the source and count objects by category
            var schema = await DiscoverSchemaFromDirectoryStore();
            preview.Warnings.Add($"Target directory contains {schema.EstimatedObjectCount} existing objects.");

            try
            {
                var tlsOptions = new LdapClientTlsOptions { SkipCertificateValidation = true };
                await using var ldapClient = new LdapClient(tlsOptions, logger: _logger);
                await ldapClient.ConnectAsync(source.Host, source.Port, source.UseSsl);

                if (!string.IsNullOrWhiteSpace(source.BindDn))
                {
                    var bindResult = await ldapClient.BindAsync(source.BindDn, source.BindPassword ?? "");
                    if (!bindResult.Success)
                    {
                        preview.Warnings.Add($"Could not bind to source server: {bindResult.DiagnosticMessage}. Counts will be zero.");
                        preview.Users = 0;
                        preview.Groups = 0;
                        preview.OUs = 0;
                        preview.Computers = 0;
                    }
                    else
                    {
                        await CountSourceObjectsAsync(ldapClient, source, preview);
                    }
                }
                else
                {
                    // Anonymous bind — try counting anyway
                    await CountSourceObjectsAsync(ldapClient, source, preview);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to connect to source LDAP server for preview");
                preview.Warnings.Add($"Could not connect to {source.Host}:{source.Port} for preview: {ex.Message}");
                preview.Users = 0;
                preview.Groups = 0;
                preview.OUs = 0;
                preview.Computers = 0;
            }
        }

        if (!plan.Options.MigrateUsers) preview.Users = 0;
        if (!plan.Options.MigrateGroups) preview.Groups = 0;
        if (!plan.Options.MigrateOUs) preview.OUs = 0;
        if (!plan.Options.MigrateComputers) preview.Computers = 0;

        plan.Preview = preview;
        return preview;
    }

    public async Task<MigrationResult> ExecuteMigration(MigrationPlan plan, string ldifContent = null)
    {
        await EnsureLoadedAsync();
        _logger.LogInformation("Executing migration for plan {PlanId}", plan.Id);

        var result = new MigrationResult
        {
            PlanId = plan.Id,
            Status = MigrationStatus.Running,
            StartedAt = DateTimeOffset.UtcNow
        };

        _activeResults[plan.Id] = result;

        if (!_sources.TryGetValue(plan.SourceId, out var source))
        {
            result.Status = MigrationStatus.Failed;
            result.Errors.Add(new MigrationError { Message = "Migration source not found." });
            result.CompletedAt = DateTimeOffset.UtcNow;
            result.Duration = result.CompletedAt.Value - result.StartedAt;
            return result;
        }

        var sw = Stopwatch.StartNew();

        try
        {
            if (source.Type == MigrationSourceType.LdifFile)
            {
                await ExecuteLdifMigration(plan, source, ldifContent, result);
            }
            else
            {
                await ExecuteLiveLdapMigration(plan, source, result);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Migration failed for plan {PlanId}", plan.Id);
            result.Status = MigrationStatus.Failed;
            result.Errors.Add(new MigrationError { Message = ex.Message });
        }

        sw.Stop();
        result.Duration = sw.Elapsed;
        result.CompletedAt = DateTimeOffset.UtcNow;

        if (result.Status == MigrationStatus.Running)
            result.Status = MigrationStatus.Completed;

        // Record in history
        _history[plan.Id] = new MigrationHistoryEntry
        {
            PlanId = plan.Id,
            SourceName = source.Name,
            SourceType = source.Type,
            StartedAt = result.StartedAt,
            CompletedAt = result.CompletedAt,
            Status = result.Status,
            TotalProcessed = result.TotalProcessed,
            Created = result.Created,
            Failed = result.Failed,
        };

        await SaveHistoryAsync();
        return result;
    }

    private async Task ExecuteLdifMigration(MigrationPlan plan, MigrationSource source, string ldifContent, MigrationResult result)
    {
        if (string.IsNullOrWhiteSpace(ldifContent))
        {
            result.Errors.Add(new MigrationError { Message = "No LDIF content provided for LDIF file migration." });
            result.Status = MigrationStatus.Failed;
            return;
        }

        List<LdifRecord> records;
        try
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(ldifContent);
            using var stream = new MemoryStream(bytes);
            records = await LdifReader.ParseAsync(stream);
        }
        catch (Exception ex)
        {
            result.Errors.Add(new MigrationError { Message = $"Failed to parse LDIF content: {ex.Message}" });
            result.Status = MigrationStatus.Failed;
            return;
        }

        // Sort: OUs first, then users, then groups, then computers, then rest
        var ordered = records.OrderBy(r => GetMigrationPriority(r)).ToList();
        var total = ordered.Count;
        if (total == 0)
        {
            result.Status = MigrationStatus.Completed;
            return;
        }

        // Build attribute mapping lookup
        var attrMap = plan.AttributeMappings.ToDictionary(
            m => m.SourceAttribute,
            m => m.TargetAttribute,
            StringComparer.OrdinalIgnoreCase);

        foreach (var record in ordered)
        {
            result.TotalProcessed++;
            result.ProgressPercent = (double)result.TotalProcessed / total * 100;

            try
            {
                var objectClasses = record.Attributes.TryGetValue("objectClass", out var ocs) ? ocs : new List<string>();
                var isUser = objectClasses.Any(c => c.Equals("user", StringComparison.OrdinalIgnoreCase) ||
                                                    c.Equals("inetOrgPerson", StringComparison.OrdinalIgnoreCase) ||
                                                    c.Equals("person", StringComparison.OrdinalIgnoreCase));
                var isGroup = objectClasses.Any(c => c.Equals("group", StringComparison.OrdinalIgnoreCase) ||
                                                      c.Equals("groupOfNames", StringComparison.OrdinalIgnoreCase) ||
                                                      c.Equals("posixGroup", StringComparison.OrdinalIgnoreCase));
                var isOU = objectClasses.Any(c => c.Equals("organizationalUnit", StringComparison.OrdinalIgnoreCase));
                var isComputer = objectClasses.Any(c => c.Equals("computer", StringComparison.OrdinalIgnoreCase));

                if (isUser && !plan.Options.MigrateUsers) { result.Skipped++; continue; }
                if (isGroup && !plan.Options.MigrateGroups) { result.Skipped++; continue; }
                if (isOU && !plan.Options.MigrateOUs) { result.Skipped++; continue; }
                if (isComputer && !plan.Options.MigrateComputers) { result.Skipped++; continue; }

                // Compute target DN
                var targetDn = string.IsNullOrWhiteSpace(plan.TargetBaseDn)
                    ? record.Dn
                    : ReparentDn(record.Dn, source.BaseDn, plan.TargetBaseDn);

                if (plan.Options.DryRun)
                {
                    result.Created++;
                    continue;
                }

                // Check for existing object
                var existing = await _directoryStore.GetByDnAsync(TenantId, targetDn);

                if (existing != null)
                {
                    switch (plan.Options.OnConflict)
                    {
                        case ConflictResolution.Skip:
                            result.Skipped++;
                            continue;
                        case ConflictResolution.Rename:
                            targetDn = RenameWithSuffix(targetDn);
                            existing = null;
                            break;
                        case ConflictResolution.Overwrite:
                        case ConflictResolution.Merge:
                            // Fall through to update
                            break;
                    }
                }

                var obj = existing ?? new DirectoryObject();
                obj.DistinguishedName = targetDn;
                obj.Id = targetDn.ToLowerInvariant();
                obj.TenantId = TenantId;

                var commaIndex = targetDn.IndexOf(',');
                obj.ParentDn = commaIndex >= 0 ? targetDn[(commaIndex + 1)..] : string.Empty;

                var dcParts = targetDn.Split(',')
                    .Where(p => p.TrimStart().StartsWith("DC=", StringComparison.OrdinalIgnoreCase));
                obj.DomainDn = string.Join(",", dcParts);

                // Apply attributes, honouring attribute mapping
                foreach (var kvp in record.Attributes)
                {
                    var targetAttr = attrMap.TryGetValue(kvp.Key, out var mapped) ? mapped : kvp.Key;

                    if (targetAttr.Equals("objectClass", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var val in kvp.Value)
                        {
                            if (!obj.ObjectClass.Contains(val, StringComparer.OrdinalIgnoreCase))
                                obj.ObjectClass.Add(val);
                        }
                        continue;
                    }

                    if (kvp.Value.Count > 0)
                        obj.SetAttribute(targetAttr, new DirectoryAttribute(targetAttr, kvp.Value[0]));
                }

                obj.WhenChanged = DateTimeOffset.UtcNow;

                if (existing != null && plan.Options.OnConflict != ConflictResolution.Rename)
                {
                    obj.ETag = existing.ETag;
                    await _directoryStore.UpdateAsync(obj);
                    result.Updated++;
                }
                else
                {
                    obj.WhenCreated = DateTimeOffset.UtcNow;
                    await _directoryStore.CreateAsync(obj);
                    result.Created++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to migrate record {Dn}", record.Dn);
                result.Failed++;
                result.Errors.Add(new MigrationError
                {
                    Dn = record.Dn,
                    Message = ex.Message
                });
            }
        }

        result.ProgressPercent = 100;
    }

    private async Task ExecuteLiveLdapMigration(MigrationPlan plan, MigrationSource source, MigrationResult result)
    {
        _logger.LogInformation("Starting live LDAP migration from {Host}:{Port} ({Type})", source.Host, source.Port, source.Type);

        var tlsOptions = new LdapClientTlsOptions
        {
            SkipCertificateValidation = true
        };

        await using var ldapClient = new LdapClient(tlsOptions, logger: _logger);

        // Connect to the source LDAP server
        try
        {
            await ldapClient.ConnectAsync(source.Host, source.Port, source.UseSsl);
        }
        catch (Exception ex)
        {
            result.Errors.Add(new MigrationError { Message = $"Failed to connect to {source.Host}:{source.Port}: {ex.Message}" });
            result.Status = MigrationStatus.Failed;
            return;
        }

        // Bind with configured credentials
        if (!string.IsNullOrWhiteSpace(source.BindDn))
        {
            var bindResult = await ldapClient.BindAsync(source.BindDn, source.BindPassword ?? "");
            if (!bindResult.Success)
            {
                result.Errors.Add(new MigrationError
                {
                    Message = $"LDAP bind failed (result code {bindResult.ResultCode}): {bindResult.DiagnosticMessage}"
                });
                result.Status = MigrationStatus.Failed;
                return;
            }
        }

        // Build the LDAP search filter based on migration options
        var filterParts = new List<string>();

        if (plan.Options.MigrateUsers)
            filterParts.Add("(|(objectClass=user)(objectClass=inetOrgPerson)(objectClass=person))");
        if (plan.Options.MigrateGroups)
            filterParts.Add("(|(objectClass=group)(objectClass=groupOfNames)(objectClass=posixGroup))");
        if (plan.Options.MigrateOUs)
            filterParts.Add("(objectClass=organizationalUnit)");
        if (plan.Options.MigrateComputers)
            filterParts.Add("(objectClass=computer)");

        if (filterParts.Count == 0)
        {
            result.Status = MigrationStatus.Completed;
            return;
        }

        var searchFilter = filterParts.Count == 1
            ? filterParts[0]
            : $"(|{string.Join("", filterParts)})";

        // Override with source-configured filter if present
        if (!string.IsNullOrWhiteSpace(source.Filter))
            searchFilter = source.Filter;

        // Search the source directory
        LdapSearchResult searchResult;
        try
        {
            searchResult = await ldapClient.SearchAsync(
                source.BaseDn,
                SearchScope.WholeSubtree,
                searchFilter,
                timeLimitSeconds: 120);
        }
        catch (Exception ex)
        {
            result.Errors.Add(new MigrationError { Message = $"LDAP search failed: {ex.Message}" });
            result.Status = MigrationStatus.Failed;
            return;
        }

        if (searchResult.ResultCode != 0)
        {
            result.Errors.Add(new MigrationError
            {
                Message = $"LDAP search returned error code {searchResult.ResultCode}: {searchResult.DiagnosticMessage}"
            });
            result.Status = MigrationStatus.Failed;
            return;
        }

        var entries = searchResult.Entries;
        if (entries.Count == 0)
        {
            _logger.LogInformation("No entries found matching filter in {BaseDn}", source.BaseDn);
            result.Status = MigrationStatus.Completed;
            return;
        }

        // Sort entries: OUs first, then users, then groups, then computers, then rest
        var ordered = entries.OrderBy(e => GetLdapEntryMigrationPriority(e)).ToList();
        var total = ordered.Count;

        // Build attribute mapping lookup
        var attrMap = plan.AttributeMappings.ToDictionary(
            m => m.SourceAttribute,
            m => m.TargetAttribute,
            StringComparer.OrdinalIgnoreCase);

        foreach (var entry in ordered)
        {
            result.TotalProcessed++;
            result.ProgressPercent = (double)result.TotalProcessed / total * 100;

            try
            {
                var objectClasses = entry.Attributes.TryGetValue("objectClass", out var ocs) ? ocs : new List<string>();
                var isUser = objectClasses.Any(c => c.Equals("user", StringComparison.OrdinalIgnoreCase) ||
                                                    c.Equals("inetOrgPerson", StringComparison.OrdinalIgnoreCase) ||
                                                    c.Equals("person", StringComparison.OrdinalIgnoreCase));
                var isGroup = objectClasses.Any(c => c.Equals("group", StringComparison.OrdinalIgnoreCase) ||
                                                      c.Equals("groupOfNames", StringComparison.OrdinalIgnoreCase) ||
                                                      c.Equals("posixGroup", StringComparison.OrdinalIgnoreCase));
                var isOU = objectClasses.Any(c => c.Equals("organizationalUnit", StringComparison.OrdinalIgnoreCase));
                var isComputer = objectClasses.Any(c => c.Equals("computer", StringComparison.OrdinalIgnoreCase));

                if (isUser && !plan.Options.MigrateUsers) { result.Skipped++; continue; }
                if (isGroup && !plan.Options.MigrateGroups) { result.Skipped++; continue; }
                if (isOU && !plan.Options.MigrateOUs) { result.Skipped++; continue; }
                if (isComputer && !plan.Options.MigrateComputers) { result.Skipped++; continue; }

                // Compute target DN
                var targetDn = string.IsNullOrWhiteSpace(plan.TargetBaseDn)
                    ? entry.DistinguishedName
                    : ReparentDn(entry.DistinguishedName, source.BaseDn, plan.TargetBaseDn);

                if (plan.Options.DryRun)
                {
                    result.Created++;
                    continue;
                }

                // Check for existing object
                var existing = await _directoryStore.GetByDnAsync(TenantId, targetDn);

                if (existing != null)
                {
                    switch (plan.Options.OnConflict)
                    {
                        case ConflictResolution.Skip:
                            result.Skipped++;
                            continue;
                        case ConflictResolution.Rename:
                            targetDn = RenameWithSuffix(targetDn);
                            existing = null;
                            break;
                        case ConflictResolution.Overwrite:
                        case ConflictResolution.Merge:
                            break;
                    }
                }

                var obj = existing ?? new DirectoryObject();
                obj.DistinguishedName = targetDn;
                obj.Id = targetDn.ToLowerInvariant();
                obj.TenantId = TenantId;

                var commaIndex = targetDn.IndexOf(',');
                obj.ParentDn = commaIndex >= 0 ? targetDn[(commaIndex + 1)..] : string.Empty;

                var dcParts = targetDn.Split(',')
                    .Where(p => p.TrimStart().StartsWith("DC=", StringComparison.OrdinalIgnoreCase));
                obj.DomainDn = string.Join(",", dcParts);

                // Apply attributes, honouring attribute mapping
                foreach (var kvp in entry.Attributes)
                {
                    var targetAttr = attrMap.TryGetValue(kvp.Key, out var mapped) ? mapped : kvp.Key;

                    if (targetAttr.Equals("objectClass", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var val in kvp.Value)
                        {
                            if (!obj.ObjectClass.Contains(val, StringComparer.OrdinalIgnoreCase))
                                obj.ObjectClass.Add(val);
                        }
                        continue;
                    }

                    if (kvp.Value.Count > 0)
                        obj.SetAttribute(targetAttr, new DirectoryAttribute(targetAttr, kvp.Value[0]));
                }

                obj.WhenChanged = DateTimeOffset.UtcNow;

                if (existing != null && plan.Options.OnConflict != ConflictResolution.Rename)
                {
                    obj.ETag = existing.ETag;
                    await _directoryStore.UpdateAsync(obj);
                    result.Updated++;
                }
                else
                {
                    obj.WhenCreated = DateTimeOffset.UtcNow;
                    await _directoryStore.CreateAsync(obj);
                    result.Created++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to migrate LDAP entry {Dn}", entry.DistinguishedName);
                result.Failed++;
                result.Errors.Add(new MigrationError
                {
                    Dn = entry.DistinguishedName,
                    ObjectClass = string.Join(", ",
                        entry.Attributes.TryGetValue("objectClass", out var ocs) ? ocs : new List<string>()),
                    Message = ex.Message
                });
            }
        }

        result.ProgressPercent = 100;
    }

    /// <summary>
    /// Connects to a source LDAP server and counts objects by category for migration preview.
    /// </summary>
    private async Task CountSourceObjectsAsync(LdapClient ldapClient, MigrationSource source, MigrationPreview preview)
    {
        var baseDn = source.BaseDn;

        // Count users
        try
        {
            var userResult = await ldapClient.SearchAsync(
                baseDn, SearchScope.WholeSubtree,
                "(|(objectClass=user)(objectClass=inetOrgPerson)(objectClass=person))",
                attributes: new[] { "dn" },
                timeLimitSeconds: 30);
            preview.Users = userResult.ResultCode == 0 ? userResult.Entries.Count : 0;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to count users from source");
            preview.Users = 0;
        }

        // Count groups
        try
        {
            var groupResult = await ldapClient.SearchAsync(
                baseDn, SearchScope.WholeSubtree,
                "(|(objectClass=group)(objectClass=groupOfNames)(objectClass=posixGroup))",
                attributes: new[] { "dn" },
                timeLimitSeconds: 30);
            preview.Groups = groupResult.ResultCode == 0 ? groupResult.Entries.Count : 0;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to count groups from source");
            preview.Groups = 0;
        }

        // Count OUs
        try
        {
            var ouResult = await ldapClient.SearchAsync(
                baseDn, SearchScope.WholeSubtree,
                "(objectClass=organizationalUnit)",
                attributes: new[] { "dn" },
                timeLimitSeconds: 30);
            preview.OUs = ouResult.ResultCode == 0 ? ouResult.Entries.Count : 0;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to count OUs from source");
            preview.OUs = 0;
        }

        // Count computers
        try
        {
            var computerResult = await ldapClient.SearchAsync(
                baseDn, SearchScope.WholeSubtree,
                "(objectClass=computer)",
                attributes: new[] { "dn" },
                timeLimitSeconds: 30);
            preview.Computers = computerResult.ResultCode == 0 ? computerResult.Entries.Count : 0;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to count computers from source");
            preview.Computers = 0;
        }

        preview.Warnings.Add(
            $"Source directory contains approximately {preview.Users} users, {preview.Groups} groups, " +
            $"{preview.OUs} OUs, and {preview.Computers} computers.");
    }

    private static int GetLdapEntryMigrationPriority(LdapSearchEntry entry)
    {
        var ocs = entry.Attributes.TryGetValue("objectClass", out var v) ? v : new List<string>();
        if (ocs.Any(c => c.Equals("organizationalUnit", StringComparison.OrdinalIgnoreCase))) return 0;
        if (ocs.Any(c => c.Equals("user", StringComparison.OrdinalIgnoreCase) ||
                          c.Equals("inetOrgPerson", StringComparison.OrdinalIgnoreCase) ||
                          c.Equals("person", StringComparison.OrdinalIgnoreCase))) return 1;
        if (ocs.Any(c => c.Equals("group", StringComparison.OrdinalIgnoreCase) ||
                          c.Equals("groupOfNames", StringComparison.OrdinalIgnoreCase) ||
                          c.Equals("posixGroup", StringComparison.OrdinalIgnoreCase))) return 2;
        if (ocs.Any(c => c.Equals("computer", StringComparison.OrdinalIgnoreCase))) return 3;
        return 4;
    }

    private static int GetMigrationPriority(LdifRecord record)
    {
        var ocs = record.Attributes.TryGetValue("objectClass", out var v) ? v : new List<string>();
        if (ocs.Any(c => c.Equals("organizationalUnit", StringComparison.OrdinalIgnoreCase))) return 0;
        if (ocs.Any(c => c.Equals("user", StringComparison.OrdinalIgnoreCase) ||
                          c.Equals("inetOrgPerson", StringComparison.OrdinalIgnoreCase) ||
                          c.Equals("person", StringComparison.OrdinalIgnoreCase))) return 1;
        if (ocs.Any(c => c.Equals("group", StringComparison.OrdinalIgnoreCase) ||
                          c.Equals("groupOfNames", StringComparison.OrdinalIgnoreCase) ||
                          c.Equals("posixGroup", StringComparison.OrdinalIgnoreCase))) return 2;
        if (ocs.Any(c => c.Equals("computer", StringComparison.OrdinalIgnoreCase))) return 3;
        return 4;
    }

    private static string ReparentDn(string dn, string sourceDn, string targetDn)
    {
        if (string.IsNullOrEmpty(sourceDn)) return dn;
        if (dn.EndsWith(sourceDn, StringComparison.OrdinalIgnoreCase))
        {
            var prefix = dn[..^sourceDn.Length].TrimEnd(',');
            return string.IsNullOrEmpty(prefix) ? targetDn : $"{prefix},{targetDn}";
        }
        return dn;
    }

    private static string RenameWithSuffix(string dn)
    {
        var commaIdx = dn.IndexOf(',');
        var rdn = commaIdx >= 0 ? dn[..commaIdx] : dn;
        var rest = commaIdx >= 0 ? dn[(commaIdx + 1)..] : string.Empty;
        var suffix = $"-migrated-{DateTime.UtcNow:yyyyMMddHHmmss}";

        var eqIdx = rdn.IndexOf('=');
        if (eqIdx >= 0)
            rdn = $"{rdn[..(eqIdx + 1)]}{rdn[(eqIdx + 1)..]}{suffix}";
        else
            rdn = $"{rdn}{suffix}";

        return string.IsNullOrEmpty(rest) ? rdn : $"{rdn},{rest}";
    }

    private const string TenantId = "default";

    public Task<MigrationResult> GetMigrationStatus(string planId)
    {
        _activeResults.TryGetValue(planId, out var result);
        return Task.FromResult(result);
    }

    public async Task<List<MigrationHistoryEntry>> GetMigrationHistory()
    {
        await EnsureLoadedAsync();
        return _history.Values.OrderByDescending(h => h.StartedAt).ToList();
    }

    public async Task<List<MigrationSource>> GetSources()
    {
        await EnsureLoadedAsync();
        return _sources.Values.ToList();
    }

    public async Task<MigrationSource> AddSource(MigrationSource source)
    {
        await EnsureLoadedAsync();
        source.Id = Guid.NewGuid().ToString();
        _sources[source.Id] = source;
        await SaveSourcesAsync();
        return source;
    }

    public async Task<MigrationSource> UpdateSource(string id, MigrationSource source)
    {
        await EnsureLoadedAsync();
        if (!_sources.ContainsKey(id)) return null;
        source.Id = id;
        _sources[id] = source;
        await SaveSourcesAsync();
        return source;
    }

    public async Task<bool> DeleteSource(string id)
    {
        await EnsureLoadedAsync();
        if (!_sources.TryRemove(id, out _)) return false;
        await SaveSourcesAsync();
        return true;
    }
}

public class ConnectionTestResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public string ServerType { get; set; }
}
