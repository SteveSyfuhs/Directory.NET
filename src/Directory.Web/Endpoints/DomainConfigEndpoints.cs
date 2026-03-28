using Directory.Core;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Web.Models;

namespace Directory.Web.Endpoints;

public static class DomainConfigEndpoints
{
    public static RouteGroupBuilder MapDomainConfigEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/config", (DomainConfiguration config) =>
        {
            return Results.Ok(new
            {
                config.DomainDn,
                config.DomainDnsName,
                config.NetBiosName,
                config.DomainSid,
                config.ForestDnsName,
                config.KerberosRealm,
                config.DomainFunctionalLevel,
            });
        })
        .WithName("GetDomainConfig")
        .WithTags("Domain");

        group.MapGet("/password-policy", (DomainConfiguration config) =>
        {
            return Results.Ok(new PasswordPolicyDto(
                MinPwdLength: config.MinPwdLength,
                PwdHistoryLength: config.PwdHistoryLength,
                MaxPwdAge: config.MaxPwdAge,
                MinPwdAge: config.MinPwdAge,
                PwdProperties: config.PwdProperties,
                LockoutThreshold: config.LockoutThreshold,
                LockoutDuration: config.LockoutDuration,
                LockoutObservationWindow: config.LockoutObservationWindow
            ));
        })
        .WithName("GetPasswordPolicy")
        .WithTags("Domain");

        group.MapPut("/password-policy", async (
            PasswordPolicyDto policy,
            IDirectoryStore store,
            INamingContextService ncService,
            DomainConfiguration config) =>
        {
            var domainDn = ncService.GetDomainNc().Dn;
            var domainRoot = await store.GetByDnAsync(DirectoryConstants.DefaultTenantId, domainDn);
            if (domainRoot == null)
                return Results.NotFound();

            // Update password policy attributes on domain root
            domainRoot.SetAttribute("minPwdLength", new DirectoryAttribute("minPwdLength", policy.MinPwdLength));
            domainRoot.SetAttribute("pwdHistoryLength", new DirectoryAttribute("pwdHistoryLength", policy.PwdHistoryLength));
            domainRoot.SetAttribute("lockoutThreshold", new DirectoryAttribute("lockoutThreshold", policy.LockoutThreshold));
            domainRoot.Attributes["maxPwdAge"] = new DirectoryAttribute("maxPwdAge", policy.MaxPwdAge);
            domainRoot.Attributes["minPwdAge"] = new DirectoryAttribute("minPwdAge", policy.MinPwdAge);
            domainRoot.Attributes["pwdProperties"] = new DirectoryAttribute("pwdProperties", policy.PwdProperties);
            domainRoot.Attributes["lockoutDuration"] = new DirectoryAttribute("lockoutDuration", policy.LockoutDuration);
            domainRoot.Attributes["lockoutObservationWindow"] = new DirectoryAttribute("lockoutObservationWindow", policy.LockoutObservationWindow);

            domainRoot.WhenChanged = DateTimeOffset.UtcNow;
            await store.UpdateAsync(domainRoot);

            // Update in-memory config
            config.MinPwdLength = policy.MinPwdLength;
            config.PwdHistoryLength = policy.PwdHistoryLength;
            config.MaxPwdAge = policy.MaxPwdAge;
            config.MinPwdAge = policy.MinPwdAge;
            config.PwdProperties = policy.PwdProperties;
            config.LockoutThreshold = policy.LockoutThreshold;
            config.LockoutDuration = policy.LockoutDuration;
            config.LockoutObservationWindow = policy.LockoutObservationWindow;

            return Results.Ok(policy);
        })
        .WithName("UpdatePasswordPolicy")
        .WithTags("Domain");

        group.MapGet("/naming-contexts", (INamingContextService ncService) =>
        {
            var ncs = ncService.GetAllNamingContexts();
            return Results.Ok(ncs.Select(nc => new
            {
                nc.Type,
                nc.Dn,
                nc.DnsName,
            }));
        })
        .WithName("GetNamingContexts")
        .WithTags("Domain");

        // GET /api/v1/domain/functional-level
        group.MapGet("/functional-level", async (
            IDirectoryStore store,
            INamingContextService ncService,
            DomainConfiguration config) =>
        {
            var domainDn = ncService.GetDomainNc().Dn;
            var domainRoot = await store.GetByDnAsync(DirectoryConstants.DefaultTenantId, domainDn);
            var domainLevel = config.DomainFunctionalLevel;

            // Try to read forest level from the Partitions container
            var configDn = ncService.GetConfigurationNc().Dn;
            var partitionsDn = $"CN=Partitions,{configDn}";
            var partitions = await store.GetByDnAsync(DirectoryConstants.DefaultTenantId, partitionsDn);
            var forestLevelStr = partitions?.GetAttribute("msDS-Behavior-Version")?.GetFirstString();
            var forestLevel = forestLevelStr is not null && int.TryParse(forestLevelStr, out var fl) ? fl : domainLevel;

            var possibleDomainLevels = GetPossibleLevels(domainLevel);
            var possibleForestLevels = GetPossibleLevels(forestLevel);

            return Results.Ok(new
            {
                DomainLevel = domainLevel,
                ForestLevel = forestLevel,
                PossibleDomainLevels = possibleDomainLevels,
                PossibleForestLevels = possibleForestLevels,
            });
        })
        .WithName("GetFunctionalLevel")
        .WithTags("Domain");

        // POST /api/v1/domain/raise-domain-level
        group.MapPost("/raise-domain-level", async (
            RaiseLevelRequest request,
            IDirectoryStore store,
            INamingContextService ncService,
            DomainConfiguration config) =>
        {
            var currentLevel = config.DomainFunctionalLevel;
            if (request.TargetLevel <= currentLevel)
                return Results.Problem(statusCode: 400, detail: $"Target level {request.TargetLevel} must be higher than current level {currentLevel}");

            if (!IsValidLevel(request.TargetLevel))
                return Results.Problem(statusCode: 400, detail: $"Invalid functional level: {request.TargetLevel}");

            var domainDn = ncService.GetDomainNc().Dn;
            var domainRoot = await store.GetByDnAsync(DirectoryConstants.DefaultTenantId, domainDn);
            if (domainRoot is null)
                return Results.Problem(statusCode: 404, detail: "Domain root not found");

            domainRoot.SetAttribute("msDS-Behavior-Version", new DirectoryAttribute("msDS-Behavior-Version", request.TargetLevel.ToString()));
            domainRoot.WhenChanged = DateTimeOffset.UtcNow;
            await store.UpdateAsync(domainRoot);

            config.DomainFunctionalLevel = request.TargetLevel;

            return Results.Ok(new
            {
                Message = $"Domain functional level raised to {request.TargetLevel}",
                NewLevel = request.TargetLevel,
            });
        })
        .WithName("RaiseDomainLevel")
        .WithTags("Domain");

        // POST /api/v1/domain/raise-forest-level
        group.MapPost("/raise-forest-level", async (
            RaiseLevelRequest request,
            IDirectoryStore store,
            INamingContextService ncService,
            DomainConfiguration config) =>
        {
            var configDn = ncService.GetConfigurationNc().Dn;
            var partitionsDn = $"CN=Partitions,{configDn}";
            var partitions = await store.GetByDnAsync(DirectoryConstants.DefaultTenantId, partitionsDn);

            var forestLevelStr = partitions?.GetAttribute("msDS-Behavior-Version")?.GetFirstString();
            var currentForest = forestLevelStr is not null && int.TryParse(forestLevelStr, out var fl) ? fl : config.DomainFunctionalLevel;

            if (request.TargetLevel <= currentForest)
                return Results.Problem(statusCode: 400, detail: $"Target level {request.TargetLevel} must be higher than current forest level {currentForest}");

            if (!IsValidLevel(request.TargetLevel))
                return Results.Problem(statusCode: 400, detail: $"Invalid functional level: {request.TargetLevel}");

            if (request.TargetLevel > config.DomainFunctionalLevel)
                return Results.Problem(statusCode: 400, detail: "Forest functional level cannot exceed domain functional level");

            if (partitions is null)
            {
                // Create the Partitions container if it does not exist
                partitions = new DirectoryObject
                {
                    Id = partitionsDn.ToLowerInvariant(),
                    TenantId = DirectoryConstants.DefaultTenantId,
                    DistinguishedName = partitionsDn,
                    Cn = "Partitions",
                    DomainDn = OuEndpoints.ExtractDomainDn(partitionsDn),
                    ObjectCategory = "crossRefContainer",
                    ObjectClass = ["top", "crossRefContainer"],
                    ParentDn = configDn,
                    WhenCreated = DateTimeOffset.UtcNow,
                    WhenChanged = DateTimeOffset.UtcNow,
                };
                partitions.SetAttribute("msDS-Behavior-Version", new DirectoryAttribute("msDS-Behavior-Version", request.TargetLevel.ToString()));
                await store.CreateAsync(partitions);
            }
            else
            {
                partitions.SetAttribute("msDS-Behavior-Version", new DirectoryAttribute("msDS-Behavior-Version", request.TargetLevel.ToString()));
                partitions.WhenChanged = DateTimeOffset.UtcNow;
                await store.UpdateAsync(partitions);
            }

            return Results.Ok(new
            {
                Message = $"Forest functional level raised to {request.TargetLevel}",
                NewLevel = request.TargetLevel,
            });
        })
        .WithName("RaiseForestLevel")
        .WithTags("Domain");

        // GET /api/v1/domain/upn-suffixes — list alternative UPN suffixes
        group.MapGet("/upn-suffixes", async (
            IDirectoryStore store,
            INamingContextService ncService,
            DomainConfiguration config) =>
        {
            var configDn = ncService.GetConfigurationNc().Dn;
            var partitionsDn = $"CN=Partitions,{configDn}";
            var partitions = await store.GetByDnAsync(DirectoryConstants.DefaultTenantId, partitionsDn);

            var suffixes = partitions?.GetAttribute("uPNSuffixes")?.GetStrings().ToList() ?? [];
            var defaultSuffix = config.DomainDnsName;

            return Results.Ok(new
            {
                DefaultSuffix = defaultSuffix,
                AlternativeSuffixes = suffixes,
            });
        })
        .WithName("GetUpnSuffixes")
        .WithTags("Domain");

        // POST /api/v1/domain/upn-suffixes — add UPN suffix
        group.MapPost("/upn-suffixes", async (
            AddUpnSuffixRequest request,
            IDirectoryStore store,
            INamingContextService ncService) =>
        {
            if (string.IsNullOrWhiteSpace(request.Suffix))
                return Results.Problem(statusCode: 400, detail: "Suffix is required");

            var suffix = request.Suffix.Trim().ToLowerInvariant();

            // Basic domain name validation
            if (!suffix.Contains('.') || suffix.StartsWith('.') || suffix.EndsWith('.'))
                return Results.Problem(statusCode: 400, detail: "Invalid domain name format");

            var configDn = ncService.GetConfigurationNc().Dn;
            var partitionsDn = $"CN=Partitions,{configDn}";
            var partitions = await store.GetByDnAsync(DirectoryConstants.DefaultTenantId, partitionsDn);

            if (partitions is null)
            {
                partitions = new DirectoryObject
                {
                    Id = partitionsDn.ToLowerInvariant(),
                    TenantId = DirectoryConstants.DefaultTenantId,
                    DistinguishedName = partitionsDn,
                    Cn = "Partitions",
                    DomainDn = OuEndpoints.ExtractDomainDn(partitionsDn),
                    ObjectCategory = "crossRefContainer",
                    ObjectClass = ["top", "crossRefContainer"],
                    ParentDn = configDn,
                    WhenCreated = DateTimeOffset.UtcNow,
                    WhenChanged = DateTimeOffset.UtcNow,
                };
                partitions.SetAttribute("uPNSuffixes", new DirectoryAttribute("uPNSuffixes", suffix));
                await store.CreateAsync(partitions);
            }
            else
            {
                var existing = partitions.GetAttribute("uPNSuffixes")?.GetStrings().ToList() ?? [];
                if (existing.Any(s => string.Equals(s, suffix, StringComparison.OrdinalIgnoreCase)))
                    return Results.Problem(statusCode: 409, detail: $"UPN suffix '{suffix}' already exists");

                existing.Add(suffix);
                partitions.SetAttribute("uPNSuffixes", new DirectoryAttribute("uPNSuffixes", [.. existing.Cast<object>()]));
                partitions.WhenChanged = DateTimeOffset.UtcNow;
                await store.UpdateAsync(partitions);
            }

            return Results.Created($"/api/v1/domain/upn-suffixes", new { Suffix = suffix });
        })
        .WithName("AddUpnSuffix")
        .WithTags("Domain");

        // DELETE /api/v1/domain/upn-suffixes/{suffix} — remove UPN suffix
        group.MapDelete("/upn-suffixes/{suffix}", async (
            string suffix,
            IDirectoryStore store,
            INamingContextService ncService) =>
        {
            var decodedSuffix = Uri.UnescapeDataString(suffix);
            var configDn = ncService.GetConfigurationNc().Dn;
            var partitionsDn = $"CN=Partitions,{configDn}";
            var partitions = await store.GetByDnAsync(DirectoryConstants.DefaultTenantId, partitionsDn);

            if (partitions is null)
                return Results.Problem(statusCode: 404, detail: "No UPN suffixes configured");

            var existing = partitions.GetAttribute("uPNSuffixes")?.GetStrings().ToList() ?? [];
            var removed = existing.RemoveAll(s => string.Equals(s, decodedSuffix, StringComparison.OrdinalIgnoreCase));

            if (removed == 0)
                return Results.Problem(statusCode: 404, detail: $"UPN suffix '{decodedSuffix}' not found");

            if (existing.Count > 0)
                partitions.SetAttribute("uPNSuffixes", new DirectoryAttribute("uPNSuffixes", [.. existing.Cast<object>()]));
            else
                partitions.Attributes.Remove("uPNSuffixes");

            partitions.WhenChanged = DateTimeOffset.UtcNow;
            await store.UpdateAsync(partitions);

            return Results.NoContent();
        })
        .WithName("DeleteUpnSuffix")
        .WithTags("Domain");

        return group;
    }

    private static readonly int[] ValidLevels = [0, 1, 2, 3, 4, 5, 6, 7, 10];

    private static bool IsValidLevel(int level) => ValidLevels.Contains(level);

    private static object[] GetPossibleLevels(int currentLevel) =>
        ValidLevels
            .Where(l => l > currentLevel)
            .Select(l => new { Level = l, Name = LevelName(l) })
            .ToArray<object>();

    private static string LevelName(int level) => level switch
    {
        0 => "Windows 2000",
        1 => "Windows Server 2003 (interim)",
        2 => "Windows Server 2003",
        3 => "Windows Server 2008",
        4 => "Windows Server 2008 R2",
        5 => "Windows Server 2012",
        6 => "Windows Server 2012 R2",
        7 => "Windows Server 2016",
        10 => "Windows Server 2025",
        _ => $"Level {level}",
    };
}

public record RaiseLevelRequest(int TargetLevel);
public record AddUpnSuffixRequest(string Suffix);
