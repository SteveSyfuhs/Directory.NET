using Directory.Core;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Core.Telemetry;
using Directory.Web.Models;

namespace Directory.Web.Endpoints;

public record UpdateDelegationRequest(string DelegationType, List<string> AllowedServices);
public record UpdateLogonHoursRequest(string Hours);
public record UpdateLogonWorkstationsRequest(List<string> Workstations);

public static class UserEndpoints
{
    public static RouteGroupBuilder MapUserEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/", async (
            CreateUserRequest request,
            IDirectoryStore store,
            IRidAllocator ridAllocator,
            IPasswordPolicy passwordPolicy,
            INamingContextService ncService,
            IUserAccountControlService uacService,
            IAuditService audit,
            HttpContext context) =>
        {
            var validation =
                ValidationHelper.ValidateRequired(request.SAMAccountName, "sAMAccountName") ??
                ValidationHelper.ValidateMaxLength(request.SAMAccountName, "sAMAccountName", maxLength: 20) ??
                ValidationHelper.ValidateRequired(request.Cn, "cn") ??
                ValidationHelper.ValidateMaxLength(request.Cn, "cn", maxLength: 64) ??
                ValidationHelper.ValidateDn(request.ContainerDn, "containerDn") ??
                ValidationHelper.ValidateRequired(request.Password, "password") ??
                ValidationHelper.ValidateMaxLength(request.Password, "password", ValidationHelper.MaxPasswordLength) ??
                ValidationHelper.ValidateMaxLength(request.Description, "description", ValidationHelper.MaxDescriptionLength) ??
                ValidationHelper.ValidateMaxLength(request.DisplayName, "displayName") ??
                ValidationHelper.ValidateMaxLength(request.Mail, "mail") ??
                ValidationHelper.ValidateMaxLength(request.Title, "title") ??
                ValidationHelper.ValidateMaxLength(request.Department, "department") ??
                ValidationHelper.ValidateMaxLength(request.Company, "company");
            if (validation != null) return validation;

            var domainDn = ncService.GetDomainNc().Dn;
            var dn = $"CN={request.Cn},{request.ContainerDn}";

            // Check if object already exists
            var existing = await store.GetByDnAsync(DirectoryConstants.DefaultTenantId, dn);
            if (existing != null)
                return Results.Problem(statusCode: 409, detail: $"Object already exists at {dn}");

            // Allocate SID
            var objectSid = await ridAllocator.GenerateObjectSidAsync(DirectoryConstants.DefaultTenantId, domainDn);

            // Determine UAC
            var uac = uacService.GetDefaultUac("user"); // 0x200 = NORMAL_ACCOUNT
            if (!request.Enabled)
                uac |= 0x2; // ACCOUNTDISABLE

            var now = DateTimeOffset.UtcNow;
            var usn = await store.GetNextUsnAsync(DirectoryConstants.DefaultTenantId, domainDn);

            var obj = new DirectoryObject
            {
                Id = dn.ToLowerInvariant(),
                TenantId = DirectoryConstants.DefaultTenantId,
                DomainDn = domainDn,
                DistinguishedName = dn,
                ObjectGuid = Guid.NewGuid().ToString(),
                ObjectSid = objectSid,
                ObjectClass = ["top", "person", "organizationalPerson", "user"],
                ObjectCategory = "person",
                Cn = request.Cn,
                SAMAccountName = request.SAMAccountName,
                UserPrincipalName = request.UserPrincipalName,
                DisplayName = request.DisplayName ?? request.Cn,
                Description = request.Description,
                GivenName = request.GivenName,
                Sn = request.Sn,
                Mail = request.Mail,
                Title = request.Title,
                Department = request.Department,
                Company = request.Company,
                UserAccountControl = uac,
                PrimaryGroupId = 513, // Domain Users
                ParentDn = request.ContainerDn,
                WhenCreated = now,
                WhenChanged = now,
                USNCreated = usn,
                USNChanged = usn,
                SAMAccountType = 0x30000000, // SAM_USER_OBJECT
            };

            await store.CreateAsync(obj);

            // Set password
            await passwordPolicy.SetPasswordAsync(DirectoryConstants.DefaultTenantId, dn, request.Password);

            DirectoryMetrics.ObjectsCreated.Add(1, new KeyValuePair<string, object>("objectClass", "user"));

            await audit.LogAsync(new AuditEntry
            {
                TenantId = DirectoryConstants.DefaultTenantId,
                Action = "Create",
                TargetDn = obj.DistinguishedName,
                TargetObjectClass = "user",
                SourceIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                Details = new() { ["cn"] = request.Cn, ["sAMAccountName"] = request.SAMAccountName }
            });

            return Results.Created($"/api/v1/objects/{obj.ObjectGuid}", ObjectDetailDto.FromDirectoryObject(obj));
        })
        .WithName("CreateUser")
        .WithTags("Users");

        group.MapPut("/{guid}/enable", async (string guid, IDirectoryStore store, IAuditService audit, HttpContext context) =>
        {
            var obj = await store.GetByGuidAsync(DirectoryConstants.DefaultTenantId, guid);
            if (obj == null || obj.IsDeleted)
                return Results.NotFound();

            obj.UserAccountControl &= ~0x2; // Clear ACCOUNTDISABLE
            obj.WhenChanged = DateTimeOffset.UtcNow;
            await store.UpdateAsync(obj);

            DirectoryMetrics.ObjectsModified.Add(1, new KeyValuePair<string, object>("objectClass", "user"));

            await audit.LogAsync(new AuditEntry
            {
                TenantId = DirectoryConstants.DefaultTenantId,
                Action = "Enable",
                TargetDn = obj.DistinguishedName,
                TargetObjectClass = "user",
                SourceIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown"
            });

            return Results.Ok(ObjectDetailDto.FromDirectoryObject(obj));
        })
        .WithName("EnableUser")
        .WithTags("Users");

        group.MapPut("/{guid}/disable", async (string guid, IDirectoryStore store, IAuditService audit, HttpContext context) =>
        {
            var obj = await store.GetByGuidAsync(DirectoryConstants.DefaultTenantId, guid);
            if (obj == null || obj.IsDeleted)
                return Results.NotFound();

            obj.UserAccountControl |= 0x2; // Set ACCOUNTDISABLE
            obj.WhenChanged = DateTimeOffset.UtcNow;
            await store.UpdateAsync(obj);

            DirectoryMetrics.ObjectsModified.Add(1, new KeyValuePair<string, object>("objectClass", "user"));

            await audit.LogAsync(new AuditEntry
            {
                TenantId = DirectoryConstants.DefaultTenantId,
                Action = "Disable",
                TargetDn = obj.DistinguishedName,
                TargetObjectClass = "user",
                SourceIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown"
            });

            return Results.Ok(ObjectDetailDto.FromDirectoryObject(obj));
        })
        .WithName("DisableUser")
        .WithTags("Users");

        group.MapPost("/{guid}/reset-password", async (
            string guid,
            ResetPasswordRequest request,
            IDirectoryStore store,
            IPasswordPolicy passwordPolicy,
            IAuditService audit,
            HttpContext context) =>
        {
            var pwdValidation = ValidationHelper.ValidateRequired(request.Password, "password") ??
                ValidationHelper.ValidateMaxLength(request.Password, "password", ValidationHelper.MaxPasswordLength);
            if (pwdValidation != null) return pwdValidation;

            var obj = await store.GetByGuidAsync(DirectoryConstants.DefaultTenantId, guid);
            if (obj == null || obj.IsDeleted)
                return Results.NotFound();

            if (!passwordPolicy.MeetsComplexityRequirements(request.Password, obj.SAMAccountName))
                return Results.Problem(statusCode: 400, detail: "Password does not meet complexity requirements");

            await passwordPolicy.SetPasswordAsync(DirectoryConstants.DefaultTenantId, obj.DistinguishedName, request.Password);

            if (request.MustChangeAtNextLogon)
            {
                obj.PwdLastSet = 0;
                obj.WhenChanged = DateTimeOffset.UtcNow;
                await store.UpdateAsync(obj);
            }

            await audit.LogAsync(new AuditEntry
            {
                TenantId = DirectoryConstants.DefaultTenantId,
                Action = "PasswordReset",
                TargetDn = obj.DistinguishedName,
                TargetObjectClass = "user",
                SourceIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                Details = new() { ["mustChangeAtNextLogon"] = request.MustChangeAtNextLogon.ToString() }
            });

            return Results.NoContent();
        })
        .WithName("ResetPassword")
        .WithTags("Users");

        group.MapPut("/{guid}/unlock", async (string guid, IDirectoryStore store, IAuditService audit, HttpContext context) =>
        {
            var obj = await store.GetByGuidAsync(DirectoryConstants.DefaultTenantId, guid);
            if (obj == null || obj.IsDeleted)
                return Results.NotFound();

            // Clear lockout
            obj.Attributes.Remove("lockoutTime");
            obj.BadPwdCount = 0;
            obj.WhenChanged = DateTimeOffset.UtcNow;
            await store.UpdateAsync(obj);

            await audit.LogAsync(new AuditEntry
            {
                TenantId = DirectoryConstants.DefaultTenantId,
                Action = "Unlock",
                TargetDn = obj.DistinguishedName,
                TargetObjectClass = "user",
                SourceIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown"
            });

            return Results.Ok(ObjectDetailDto.FromDirectoryObject(obj));
        })
        .WithName("UnlockUser")
        .WithTags("Users");

        group.MapGet("/{guid}/groups", async (string guid, IDirectoryStore store) =>
        {
            var obj = await store.GetByGuidAsync(DirectoryConstants.DefaultTenantId, guid);
            if (obj == null || obj.IsDeleted)
                return Results.NotFound();

            var groups = new List<ObjectSummaryDto>();
            foreach (var groupDn in obj.MemberOf)
            {
                var groupObj = await store.GetByDnAsync(DirectoryConstants.DefaultTenantId, groupDn);
                if (groupObj != null && !groupObj.IsDeleted)
                {
                    groups.Add(DashboardEndpoints.MapToSummary(groupObj));
                }
            }

            return Results.Ok(groups);
        })
        .WithName("GetUserGroups")
        .WithTags("Users");

        group.MapPut("/{guid}/delegation", async (string guid, UpdateDelegationRequest request, IDirectoryStore store) =>
        {
            var obj = await store.GetByGuidAsync(DirectoryConstants.DefaultTenantId, guid);
            if (obj == null || obj.IsDeleted)
                return Results.NotFound();

            // Clear delegation-related UAC flags first
            obj.UserAccountControl &= ~0x80000;  // NOT_DELEGATED
            obj.UserAccountControl &= ~0x80000;   // clear
            obj.UserAccountControl &= ~0x80;       // TRUSTED_FOR_DELEGATION
            obj.UserAccountControl &= ~0x1000000;  // TRUSTED_TO_AUTHENTICATE_FOR_DELEGATION

            switch (request.DelegationType)
            {
                case "none":
                    obj.UserAccountControl |= 0x100000; // NOT_DELEGATED
                    obj.MsDsAllowedToDelegateTo = [];
                    break;
                case "unconstrained":
                    obj.UserAccountControl |= 0x80; // TRUSTED_FOR_DELEGATION
                    obj.MsDsAllowedToDelegateTo = [];
                    break;
                case "constrained":
                    obj.MsDsAllowedToDelegateTo = request.AllowedServices ?? [];
                    break;
                case "protocol_transition":
                    obj.UserAccountControl |= 0x1000000; // TRUSTED_TO_AUTHENTICATE_FOR_DELEGATION
                    obj.MsDsAllowedToDelegateTo = request.AllowedServices ?? [];
                    break;
            }

            obj.WhenChanged = DateTimeOffset.UtcNow;
            await store.UpdateAsync(obj);

            return Results.Ok(ObjectDetailDto.FromDirectoryObject(obj));
        })
        .WithName("UpdateDelegation")
        .WithTags("Users");

        group.MapPut("/{guid}/logon-hours", async (string guid, UpdateLogonHoursRequest request, IDirectoryStore store) =>
        {
            var obj = await store.GetByGuidAsync(DirectoryConstants.DefaultTenantId, guid);
            if (obj == null || obj.IsDeleted)
                return Results.NotFound();

            if (!string.IsNullOrEmpty(request.Hours))
            {
                var bytes = Convert.FromBase64String(request.Hours);
                obj.Attributes["logonHours"] = new DirectoryAttribute("logonHours", bytes);
            }
            else
            {
                obj.Attributes.Remove("logonHours");
            }

            obj.WhenChanged = DateTimeOffset.UtcNow;
            await store.UpdateAsync(obj);

            return Results.Ok(ObjectDetailDto.FromDirectoryObject(obj));
        })
        .WithName("UpdateLogonHours")
        .WithTags("Users");

        group.MapPut("/{guid}/logon-workstations", async (string guid, UpdateLogonWorkstationsRequest request, IDirectoryStore store) =>
        {
            var obj = await store.GetByGuidAsync(DirectoryConstants.DefaultTenantId, guid);
            if (obj == null || obj.IsDeleted)
                return Results.NotFound();

            if (request.Workstations != null && request.Workstations.Count > 0)
            {
                var value = string.Join(",", request.Workstations);
                obj.Attributes["userWorkstations"] = new DirectoryAttribute("userWorkstations", value);
            }
            else
            {
                obj.Attributes.Remove("userWorkstations");
            }

            obj.WhenChanged = DateTimeOffset.UtcNow;
            await store.UpdateAsync(obj);

            return Results.Ok(ObjectDetailDto.FromDirectoryObject(obj));
        })
        .WithName("UpdateLogonWorkstations")
        .WithTags("Users");

        group.MapGet("/{guid}/direct-reports", async (string guid, IDirectoryStore store, INamingContextService ncService) =>
        {
            var obj = await store.GetByGuidAsync(DirectoryConstants.DefaultTenantId, guid);
            if (obj == null || obj.IsDeleted)
                return Results.NotFound();

            var domainDn = ncService.GetDomainNc().Dn;

            // Search for users whose manager matches this user's DN
            var managerFilter = new EqualityFilterNode("manager", obj.DistinguishedName);
            var result = await store.SearchAsync(DirectoryConstants.DefaultTenantId, domainDn, SearchScope.WholeSubtree, managerFilter, null, pageSize: 100);

            var reports = result.Entries
                .Where(e => !e.IsDeleted)
                .Select(DashboardEndpoints.MapToSummary)
                .ToList();

            return Results.Ok(reports);
        })
        .WithName("GetDirectReports")
        .WithTags("Users");

        return group;
    }
}
