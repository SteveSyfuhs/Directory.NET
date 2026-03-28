using Directory.Core;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Core.Telemetry;
using Directory.Web.Models;

namespace Directory.Web.Endpoints;

public static class GroupEndpoints
{
    public static RouteGroupBuilder MapGroupEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/", async (
            CreateGroupRequest request,
            IDirectoryStore store,
            IRidAllocator ridAllocator,
            INamingContextService ncService,
            IAuditService audit,
            HttpContext context) =>
        {
            var validation =
                ValidationHelper.ValidateRequired(request.Cn, "cn") ??
                ValidationHelper.ValidateMaxLength(request.Cn, "cn") ??
                ValidationHelper.ValidateRequired(request.SAMAccountName, "sAMAccountName") ??
                ValidationHelper.ValidateMaxLength(request.SAMAccountName, "sAMAccountName") ??
                ValidationHelper.ValidateDn(request.ContainerDn, "containerDn") ??
                ValidationHelper.ValidateMaxLength(request.Description, "description", ValidationHelper.MaxDescriptionLength);
            if (validation != null) return validation;

            var domainDn = ncService.GetDomainNc().Dn;
            var dn = $"CN={request.Cn},{request.ContainerDn}";

            // Check if object already exists
            var existing = await store.GetByDnAsync(DirectoryConstants.DefaultTenantId, dn);
            if (existing != null)
                return Results.Problem(statusCode: 409, detail: $"Object already exists at {dn}");

            var objectSid = await ridAllocator.GenerateObjectSidAsync(DirectoryConstants.DefaultTenantId, domainDn);

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
                ObjectClass = ["top", "group"],
                ObjectCategory = "group",
                Cn = request.Cn,
                SAMAccountName = request.SAMAccountName,
                Description = request.Description,
                GroupType = request.GroupType,
                ParentDn = request.ContainerDn,
                WhenCreated = now,
                WhenChanged = now,
                USNCreated = usn,
                USNChanged = usn,
                SAMAccountType = 0x10000000, // SAM_GROUP_OBJECT
            };

            await store.CreateAsync(obj);

            DirectoryMetrics.ObjectsCreated.Add(1, new KeyValuePair<string, object>("objectClass", "group"));

            await audit.LogAsync(new AuditEntry
            {
                TenantId = DirectoryConstants.DefaultTenantId,
                Action = "Create",
                TargetDn = obj.DistinguishedName,
                TargetObjectClass = "group",
                SourceIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                Details = new() { ["cn"] = request.Cn }
            });

            return Results.Created($"/api/v1/objects/{obj.ObjectGuid}", ObjectDetailDto.FromDirectoryObject(obj));
        })
        .WithName("CreateGroup")
        .WithTags("Groups");

        group.MapGet("/{guid}/members", async (string guid, IDirectoryStore store) =>
        {
            var obj = await store.GetByGuidAsync(DirectoryConstants.DefaultTenantId, guid);
            if (obj == null || obj.IsDeleted)
                return Results.NotFound();

            var members = new List<ObjectSummaryDto>();
            foreach (var memberDn in obj.Member)
            {
                var member = await store.GetByDnAsync(DirectoryConstants.DefaultTenantId, memberDn);
                if (member != null && !member.IsDeleted)
                {
                    members.Add(DashboardEndpoints.MapToSummary(member));
                }
            }

            return Results.Ok(members);
        })
        .WithName("GetGroupMembers")
        .WithTags("Groups");

        group.MapPost("/{guid}/members", async (
            string guid,
            AddMemberRequest request,
            IDirectoryStore store,
            ILinkedAttributeService linkedAttrService,
            IAuditService audit,
            HttpContext context) =>
        {
            var memberValidation = ValidationHelper.ValidateDn(request.MemberDn, "memberDn");
            if (memberValidation != null) return memberValidation;

            var obj = await store.GetByGuidAsync(DirectoryConstants.DefaultTenantId, guid);
            if (obj == null || obj.IsDeleted)
                return Results.NotFound();

            // Verify the target member exists
            var memberObj = await store.GetByDnAsync(DirectoryConstants.DefaultTenantId, request.MemberDn);
            if (memberObj == null || memberObj.IsDeleted)
                return Results.Problem(statusCode: 400, detail: $"Member object not found: {request.MemberDn}");

            await linkedAttrService.UpdateForwardLinkAsync(DirectoryConstants.DefaultTenantId, obj, "member", request.MemberDn, add: true);

            DirectoryMetrics.ObjectsModified.Add(1, new KeyValuePair<string, object>("objectClass", "group"));

            await audit.LogAsync(new AuditEntry
            {
                TenantId = DirectoryConstants.DefaultTenantId,
                Action = "AddMember",
                TargetDn = obj.DistinguishedName,
                TargetObjectClass = "group",
                SourceIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                Details = new() { ["memberDn"] = request.MemberDn }
            });

            return Results.NoContent();
        })
        .WithName("AddGroupMember")
        .WithTags("Groups");

        group.MapDelete("/{guid}/members/{*memberDn}", async (
            string guid,
            string memberDn,
            IDirectoryStore store,
            ILinkedAttributeService linkedAttrService,
            IAuditService audit,
            HttpContext context) =>
        {
            var obj = await store.GetByGuidAsync(DirectoryConstants.DefaultTenantId, guid);
            if (obj == null || obj.IsDeleted)
                return Results.NotFound();

            await linkedAttrService.UpdateForwardLinkAsync(DirectoryConstants.DefaultTenantId, obj, "member", memberDn, add: false);

            DirectoryMetrics.ObjectsModified.Add(1, new KeyValuePair<string, object>("objectClass", "group"));

            await audit.LogAsync(new AuditEntry
            {
                TenantId = DirectoryConstants.DefaultTenantId,
                Action = "RemoveMember",
                TargetDn = obj.DistinguishedName,
                TargetObjectClass = "group",
                SourceIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                Details = new() { ["memberDn"] = memberDn }
            });

            return Results.NoContent();
        })
        .WithName("RemoveGroupMember")
        .WithTags("Groups");

        return group;
    }
}

public record AddMemberRequest(string MemberDn);
