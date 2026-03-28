using Directory.Core;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Security;

namespace Directory.Web.Endpoints;

public static class PasswordPolicyEndpoints
{
    public static RouteGroupBuilder MapPasswordPolicyEndpoints(this RouteGroupBuilder group)
    {
        // GET /api/v1/password-policies — list all PSOs
        group.MapGet("/", async (IDirectoryStore store, INamingContextService ncService) =>
        {
            var domainDn = ncService.GetDomainNc().Dn;
            var containerDn = $"CN=Password Settings Container,CN=System,{domainDn}";
            var filter = new EqualityFilterNode("objectClass", "msDS-PasswordSettings");

            var result = await store.SearchAsync(DirectoryConstants.DefaultTenantId, containerDn,
                SearchScope.SingleLevel, filter, null, pageSize: 200);

            return Results.Ok(result.Entries
                .Where(e => !e.IsDeleted)
                .Select(MapPsoResponse));
        })
        .WithName("ListPasswordPolicies")
        .WithTags("PasswordPolicies");

        // POST /api/v1/password-policies — create PSO
        group.MapPost("/", async (IDirectoryStore store, INamingContextService ncService, CreatePsoRequest request) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.Problem(statusCode: 400, detail: "Name is required");

            var domainDn = ncService.GetDomainNc().Dn;
            var containerDn = $"CN=Password Settings Container,CN=System,{domainDn}";
            var dn = $"CN={request.Name},{containerDn}";

            var existing = await store.GetByDnAsync(DirectoryConstants.DefaultTenantId, dn);
            if (existing is not null && !existing.IsDeleted)
                return Results.Problem(statusCode: 409, detail: $"PSO '{request.Name}' already exists");

            // Ensure the container exists
            await EnsurePsoContainerAsync(store, domainDn, containerDn);

            var now = DateTimeOffset.UtcNow;
            var obj = new DirectoryObject
            {
                Id = dn.ToLowerInvariant(),
                TenantId = DirectoryConstants.DefaultTenantId,
                DistinguishedName = dn,
                Cn = request.Name,
                DomainDn = domainDn,
                ObjectCategory = "msDS-PasswordSettings",
                ObjectClass = ["top", "msDS-PasswordSettings"],
                ParentDn = containerDn,
                WhenCreated = now,
                WhenChanged = now,
            };

            SetPsoAttributes(obj, request.Precedence, request.MinPasswordLength, request.PasswordHistoryLength,
                request.ComplexityEnabled, request.ReversibleEncryptionEnabled,
                request.MinPasswordAgeDays, request.MaxPasswordAgeDays,
                request.LockoutThreshold, request.LockoutDurationMinutes, request.LockoutObservationWindowMinutes);

            if (request.Description is not null)
                obj.Description = request.Description;

            await store.CreateAsync(obj);
            return Results.Created($"/api/v1/password-policies/{obj.ObjectGuid}", MapPsoResponse(obj));
        })
        .WithName("CreatePasswordPolicy")
        .WithTags("PasswordPolicies");

        // GET /api/v1/password-policies/{id} — get PSO details
        group.MapGet("/{id}", async (IDirectoryStore store, string id) =>
        {
            var obj = await store.GetByGuidAsync(DirectoryConstants.DefaultTenantId, id);
            if (obj is null || obj.IsDeleted || obj.ObjectCategory != "msDS-PasswordSettings")
                return Results.Problem(statusCode: 404, detail: "Password policy not found");

            return Results.Ok(MapPsoResponse(obj));
        })
        .WithName("GetPasswordPolicy_PSO")
        .WithTags("PasswordPolicies");

        // PUT /api/v1/password-policies/{id} — update PSO
        group.MapPut("/{id}", async (IDirectoryStore store, string id, UpdatePsoRequest request) =>
        {
            var obj = await store.GetByGuidAsync(DirectoryConstants.DefaultTenantId, id);
            if (obj is null || obj.IsDeleted || obj.ObjectCategory != "msDS-PasswordSettings")
                return Results.Problem(statusCode: 404, detail: "Password policy not found");

            if (request.Name is not null)
                obj.Cn = request.Name;
            if (request.Description is not null)
                obj.Description = request.Description;

            SetPsoAttributes(obj,
                request.Precedence ?? ParseInt(obj, "msDS-PasswordSettingsPrecedence", 1),
                request.MinPasswordLength ?? ParseInt(obj, "msDS-MinimumPasswordLength", 7),
                request.PasswordHistoryLength ?? ParseInt(obj, "msDS-PasswordHistoryLength", 24),
                request.ComplexityEnabled ?? ParseBool(obj, "msDS-PasswordComplexityEnabled", true),
                request.ReversibleEncryptionEnabled ?? ParseBool(obj, "msDS-PasswordReversibleEncryptionEnabled"),
                request.MinPasswordAgeDays ?? TicksToDays(ParseLong(obj, "msDS-MinimumPasswordAge", -TimeSpan.FromDays(1).Ticks)),
                request.MaxPasswordAgeDays ?? TicksToDays(ParseLong(obj, "msDS-MaximumPasswordAge", -TimeSpan.FromDays(42).Ticks)),
                request.LockoutThreshold ?? ParseInt(obj, "msDS-LockoutThreshold", 0),
                request.LockoutDurationMinutes ?? TicksToMinutes(ParseLong(obj, "msDS-LockoutDuration", -TimeSpan.FromMinutes(30).Ticks)),
                request.LockoutObservationWindowMinutes ?? TicksToMinutes(ParseLong(obj, "msDS-LockoutObservationWindow", -TimeSpan.FromMinutes(30).Ticks)));

            obj.WhenChanged = DateTimeOffset.UtcNow;
            await store.UpdateAsync(obj);
            return Results.Ok(MapPsoResponse(obj));
        })
        .WithName("UpdatePasswordPolicy_PSO")
        .WithTags("PasswordPolicies");

        // DELETE /api/v1/password-policies/{id}
        group.MapDelete("/{id}", async (IDirectoryStore store, string id) =>
        {
            var obj = await store.GetByGuidAsync(DirectoryConstants.DefaultTenantId, id);
            if (obj is null || obj.IsDeleted || obj.ObjectCategory != "msDS-PasswordSettings")
                return Results.Problem(statusCode: 404, detail: "Password policy not found");

            await store.DeleteAsync(DirectoryConstants.DefaultTenantId, obj.DistinguishedName, hardDelete: true);
            return Results.NoContent();
        })
        .WithName("DeletePasswordPolicy_PSO")
        .WithTags("PasswordPolicies");

        // POST /api/v1/password-policies/{id}/apply — link PSO to user/group DN
        group.MapPost("/{id}/apply", async (IDirectoryStore store, string id, ApplyPsoRequest request) =>
        {
            if (string.IsNullOrWhiteSpace(request.TargetDn))
                return Results.Problem(statusCode: 400, detail: "targetDn is required");

            var obj = await store.GetByGuidAsync(DirectoryConstants.DefaultTenantId, id);
            if (obj is null || obj.IsDeleted || obj.ObjectCategory != "msDS-PasswordSettings")
                return Results.Problem(statusCode: 404, detail: "Password policy not found");

            // Verify target exists
            var target = await store.GetByDnAsync(DirectoryConstants.DefaultTenantId, request.TargetDn);
            if (target is null || target.IsDeleted)
                return Results.Problem(statusCode: 404, detail: $"Target '{request.TargetDn}' not found");

            var currentLinks = obj.GetAttribute("msDS-PSOAppliedTo")?.GetStrings().ToList() ?? [];
            if (!currentLinks.Any(dn => string.Equals(dn, request.TargetDn, StringComparison.OrdinalIgnoreCase)))
            {
                currentLinks.Add(request.TargetDn);
                obj.SetAttribute("msDS-PSOAppliedTo", new DirectoryAttribute("msDS-PSOAppliedTo",
                    [.. currentLinks.Cast<object>()]));
                obj.WhenChanged = DateTimeOffset.UtcNow;
                await store.UpdateAsync(obj);
            }

            return Results.Ok(MapPsoResponse(obj));
        })
        .WithName("ApplyPasswordPolicy")
        .WithTags("PasswordPolicies");

        // DELETE /api/v1/password-policies/{id}/apply/{dn} — unlink PSO from user/group
        group.MapDelete("/{id}/apply/{*dn}", async (IDirectoryStore store, string id, string dn) =>
        {
            var obj = await store.GetByGuidAsync(DirectoryConstants.DefaultTenantId, id);
            if (obj is null || obj.IsDeleted || obj.ObjectCategory != "msDS-PasswordSettings")
                return Results.Problem(statusCode: 404, detail: "Password policy not found");

            var decodedDn = Uri.UnescapeDataString(dn);
            var currentLinks = obj.GetAttribute("msDS-PSOAppliedTo")?.GetStrings().ToList() ?? [];
            var removed = currentLinks.RemoveAll(d => string.Equals(d, decodedDn, StringComparison.OrdinalIgnoreCase));

            if (removed > 0)
            {
                if (currentLinks.Count > 0)
                    obj.SetAttribute("msDS-PSOAppliedTo", new DirectoryAttribute("msDS-PSOAppliedTo",
                        [.. currentLinks.Cast<object>()]));
                else
                    obj.Attributes.Remove("msDS-PSOAppliedTo");

                obj.WhenChanged = DateTimeOffset.UtcNow;
                await store.UpdateAsync(obj);
            }

            return Results.NoContent();
        })
        .WithName("RemovePasswordPolicyLink")
        .WithTags("PasswordPolicies");

        // GET /api/v1/password-policies/effective/{userDn} — get effective policy for a user
        group.MapGet("/effective/{*userDn}", async (
            IDirectoryStore store,
            FineGrainedPasswordPolicyService fgppService,
            string userDn) =>
        {
            var decodedDn = Uri.UnescapeDataString(userDn);
            var user = await store.GetByDnAsync(DirectoryConstants.DefaultTenantId, decodedDn);
            if (user is null || user.IsDeleted)
                return Results.Problem(statusCode: 404, detail: $"User '{decodedDn}' not found");

            var pso = await fgppService.GetResultantPsoAsync(user, DirectoryConstants.DefaultTenantId);

            if (pso is null)
            {
                return Results.Ok(new
                {
                    Source = "Domain Default Policy",
                    PsoDn = (string)null,
                    Precedence = (int?)null,
                    MinPasswordLength = 7,
                    PasswordHistoryLength = 24,
                    ComplexityEnabled = true,
                    ReversibleEncryptionEnabled = false,
                    MinPasswordAgeDays = 1.0,
                    MaxPasswordAgeDays = 42.0,
                    LockoutThreshold = 0,
                    LockoutDurationMinutes = 30.0,
                    LockoutObservationWindowMinutes = 30.0,
                });
            }

            return Results.Ok(new
            {
                Source = "Fine-Grained Password Policy",
                PsoDn = pso.Dn,
                Precedence = (int?)pso.Precedence,
                MinPasswordLength = pso.MinimumPasswordLength,
                PasswordHistoryLength = pso.PasswordHistoryLength,
                ComplexityEnabled = pso.ComplexityEnabled,
                ReversibleEncryptionEnabled = pso.ReversibleEncryption,
                MinPasswordAgeDays = Math.Abs(TimeSpan.FromTicks(pso.MinimumPasswordAge).TotalDays),
                MaxPasswordAgeDays = Math.Abs(TimeSpan.FromTicks(pso.MaximumPasswordAge).TotalDays),
                LockoutThreshold = pso.LockoutThreshold,
                LockoutDurationMinutes = Math.Abs(TimeSpan.FromTicks(pso.LockoutDuration).TotalMinutes),
                LockoutObservationWindowMinutes = Math.Abs(TimeSpan.FromTicks(pso.LockoutObservationWindow).TotalMinutes),
            });
        })
        .WithName("GetEffectivePasswordPolicy")
        .WithTags("PasswordPolicies");

        return group;
    }

    private static async Task EnsurePsoContainerAsync(IDirectoryStore store, string domainDn, string containerDn)
    {
        var container = await store.GetByDnAsync(DirectoryConstants.DefaultTenantId, containerDn);
        if (container is not null) return;

        // Ensure CN=System exists first
        var systemDn = $"CN=System,{domainDn}";
        var systemContainer = await store.GetByDnAsync(DirectoryConstants.DefaultTenantId, systemDn);
        if (systemContainer is null)
        {
            systemContainer = new DirectoryObject
            {
                Id = systemDn.ToLowerInvariant(),
                TenantId = DirectoryConstants.DefaultTenantId,
                DistinguishedName = systemDn,
                Cn = "System",
                DomainDn = domainDn,
                ObjectCategory = "container",
                ObjectClass = ["top", "container"],
                ParentDn = domainDn,
                WhenCreated = DateTimeOffset.UtcNow,
                WhenChanged = DateTimeOffset.UtcNow,
            };
            await store.CreateAsync(systemContainer);
        }

        container = new DirectoryObject
        {
            Id = containerDn.ToLowerInvariant(),
            TenantId = DirectoryConstants.DefaultTenantId,
            DistinguishedName = containerDn,
            Cn = "Password Settings Container",
            DomainDn = domainDn,
            ObjectCategory = "msDS-PasswordSettingsContainer",
            ObjectClass = ["top", "msDS-PasswordSettingsContainer"],
            ParentDn = systemDn,
            WhenCreated = DateTimeOffset.UtcNow,
            WhenChanged = DateTimeOffset.UtcNow,
        };
        await store.CreateAsync(container);
    }

    private static void SetPsoAttributes(DirectoryObject obj, int precedence, int minLength,
        int historyLength, bool complexity, bool reversible,
        double minAgeDays, double maxAgeDays,
        int lockoutThreshold, double lockoutDurationMinutes, double lockoutWindowMinutes)
    {
        obj.SetAttribute("msDS-PasswordSettingsPrecedence", new DirectoryAttribute("msDS-PasswordSettingsPrecedence", precedence.ToString()));
        obj.SetAttribute("msDS-MinimumPasswordLength", new DirectoryAttribute("msDS-MinimumPasswordLength", minLength.ToString()));
        obj.SetAttribute("msDS-PasswordHistoryLength", new DirectoryAttribute("msDS-PasswordHistoryLength", historyLength.ToString()));
        obj.SetAttribute("msDS-PasswordComplexityEnabled", new DirectoryAttribute("msDS-PasswordComplexityEnabled", complexity ? "TRUE" : "FALSE"));
        obj.SetAttribute("msDS-PasswordReversibleEncryptionEnabled", new DirectoryAttribute("msDS-PasswordReversibleEncryptionEnabled", reversible ? "TRUE" : "FALSE"));
        obj.SetAttribute("msDS-MinimumPasswordAge", new DirectoryAttribute("msDS-MinimumPasswordAge", (-TimeSpan.FromDays(minAgeDays).Ticks).ToString()));
        obj.SetAttribute("msDS-MaximumPasswordAge", new DirectoryAttribute("msDS-MaximumPasswordAge", (-TimeSpan.FromDays(maxAgeDays).Ticks).ToString()));
        obj.SetAttribute("msDS-LockoutThreshold", new DirectoryAttribute("msDS-LockoutThreshold", lockoutThreshold.ToString()));
        obj.SetAttribute("msDS-LockoutDuration", new DirectoryAttribute("msDS-LockoutDuration", (-TimeSpan.FromMinutes(lockoutDurationMinutes).Ticks).ToString()));
        obj.SetAttribute("msDS-LockoutObservationWindow", new DirectoryAttribute("msDS-LockoutObservationWindow", (-TimeSpan.FromMinutes(lockoutWindowMinutes).Ticks).ToString()));
    }

    private static object MapPsoResponse(DirectoryObject obj)
    {
        var appliedTo = obj.GetAttribute("msDS-PSOAppliedTo")?.GetStrings().ToList() ?? [];

        return new
        {
            obj.ObjectGuid,
            Name = obj.Cn ?? "",
            Description = obj.Description ?? "",
            obj.DistinguishedName,
            Precedence = ParseInt(obj, "msDS-PasswordSettingsPrecedence", int.MaxValue),
            MinPasswordLength = ParseInt(obj, "msDS-MinimumPasswordLength", 7),
            PasswordHistoryLength = ParseInt(obj, "msDS-PasswordHistoryLength", 24),
            ComplexityEnabled = ParseBool(obj, "msDS-PasswordComplexityEnabled", true),
            ReversibleEncryptionEnabled = ParseBool(obj, "msDS-PasswordReversibleEncryptionEnabled"),
            MinPasswordAgeDays = TicksToDays(ParseLong(obj, "msDS-MinimumPasswordAge", -TimeSpan.FromDays(1).Ticks)),
            MaxPasswordAgeDays = TicksToDays(ParseLong(obj, "msDS-MaximumPasswordAge", -TimeSpan.FromDays(42).Ticks)),
            LockoutThreshold = ParseInt(obj, "msDS-LockoutThreshold", 0),
            LockoutDurationMinutes = TicksToMinutes(ParseLong(obj, "msDS-LockoutDuration", -TimeSpan.FromMinutes(30).Ticks)),
            LockoutObservationWindowMinutes = TicksToMinutes(ParseLong(obj, "msDS-LockoutObservationWindow", -TimeSpan.FromMinutes(30).Ticks)),
            AppliedTo = appliedTo,
            AppliedToCount = appliedTo.Count,
            obj.WhenCreated,
            obj.WhenChanged,
        };
    }

    private static double TicksToDays(long ticks) => Math.Round(Math.Abs(TimeSpan.FromTicks(ticks).TotalDays), 2);
    private static double TicksToMinutes(long ticks) => Math.Round(Math.Abs(TimeSpan.FromTicks(ticks).TotalMinutes), 2);

    private static int ParseInt(DirectoryObject obj, string attr, int defaultValue = 0)
    {
        var val = obj.GetAttribute(attr)?.GetFirstString();
        return val is not null && int.TryParse(val, out var result) ? result : defaultValue;
    }

    private static long ParseLong(DirectoryObject obj, string attr, long defaultValue = 0)
    {
        var val = obj.GetAttribute(attr)?.GetFirstString();
        return val is not null && long.TryParse(val, out var result) ? result : defaultValue;
    }

    private static bool ParseBool(DirectoryObject obj, string attr, bool defaultValue = false)
    {
        var val = obj.GetAttribute(attr)?.GetFirstString();
        if (val is null) return defaultValue;
        return val.Equals("TRUE", StringComparison.OrdinalIgnoreCase) || val == "1";
    }
}

public record CreatePsoRequest(
    string Name,
    string Description = null,
    int Precedence = 1,
    int MinPasswordLength = 7,
    int PasswordHistoryLength = 24,
    bool ComplexityEnabled = true,
    bool ReversibleEncryptionEnabled = false,
    double MinPasswordAgeDays = 1,
    double MaxPasswordAgeDays = 42,
    int LockoutThreshold = 0,
    double LockoutDurationMinutes = 30,
    double LockoutObservationWindowMinutes = 30);

public record UpdatePsoRequest(
    string Name = null,
    string Description = null,
    int? Precedence = null,
    int? MinPasswordLength = null,
    int? PasswordHistoryLength = null,
    bool? ComplexityEnabled = null,
    bool? ReversibleEncryptionEnabled = null,
    double? MinPasswordAgeDays = null,
    double? MaxPasswordAgeDays = null,
    int? LockoutThreshold = null,
    double? LockoutDurationMinutes = null,
    double? LockoutObservationWindowMinutes = null);

public record ApplyPsoRequest(string TargetDn);
