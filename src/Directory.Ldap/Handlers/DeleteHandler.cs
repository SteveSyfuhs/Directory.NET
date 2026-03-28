using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Ldap.Protocol;
using Directory.Ldap.Protocol.Messages;
using Directory.Ldap.Server;
using Microsoft.Extensions.Logging;

namespace Directory.Ldap.Handlers;

public class DeleteHandler : ILdapOperationHandler
{
    private readonly IDirectoryStore _store;
    private readonly IAccessControlService _acl;
    private readonly ILogger<DeleteHandler> _logger;

    public LdapOperation Operation => LdapOperation.DelRequest;

    public DeleteHandler(IDirectoryStore store, IAccessControlService acl, ILogger<DeleteHandler> logger)
    {
        _store = store;
        _acl = acl;
        _logger = logger;
    }

    public async Task HandleAsync(LdapMessage request, ILdapResponseWriter writer, LdapConnectionState state, CancellationToken ct)
    {
        var deleteReq = DeleteRequest.Decode(request.ProtocolOpData);

        _logger.LogDebug("Delete request: {Entry}", deleteReq.Entry);

        try
        {
            var obj = await _store.GetByDnAsync(state.TenantId, deleteReq.Entry, ct);
            if (obj is null)
            {
                await SendResponse(writer, request.MessageId, LdapResultCode.NoSuchObject, "Object not found", ct);
                return;
            }

            // Access control: check DELETE permission on the object
            var callerSid = state.BoundSid ?? string.Empty;
            var callerGroups = state.GroupSids ?? (IReadOnlySet<string>)new HashSet<string>();
            if (!_acl.CheckAccess(callerSid, callerGroups, obj, AccessMask.DeleteObject))
            {
                _logger.LogWarning("Access denied: {Sid} cannot delete {Dn}", callerSid, deleteReq.Entry);
                await SendResponse(writer, request.MessageId, LdapResultCode.InsufficientAccessRights,
                    "Insufficient access rights to delete this object", ct);
                return;
            }

            // Access control: check DELETE_CHILD permission on the parent container
            var parsed = DistinguishedName.Parse(deleteReq.Entry);
            var parentDn = parsed.Parent().ToString();
            var parentObj = await _store.GetByDnAsync(state.TenantId, parentDn, ct);
            if (parentObj is not null && !_acl.CheckAccess(callerSid, callerGroups, parentObj, AccessMask.DeleteChild))
            {
                _logger.LogWarning("Access denied: {Sid} cannot delete child from {Dn}", callerSid, parentDn);
                await SendResponse(writer, request.MessageId, LdapResultCode.InsufficientAccessRights,
                    "Insufficient access rights to delete a child from the parent container", ct);
                return;
            }

            // Check for tree delete control
            var treeDelete = request.Controls?.Any(c => c.Oid == LdapConstants.OidTreeDelete) ?? false;

            if (!treeDelete)
            {
                // Check if the object has children
                var children = await _store.GetChildrenAsync(state.TenantId, deleteReq.Entry, ct);
                if (children.Count > 0)
                {
                    await SendResponse(writer, request.MessageId, LdapResultCode.NotAllowedOnNonLeaf,
                        "Object has children. Use tree delete control to delete subtree.", ct);
                    return;
                }
            }

            if (treeDelete)
            {
                await DeleteSubtreeAsync(state.TenantId, deleteReq.Entry, ct);
            }
            else
            {
                await _store.DeleteAsync(state.TenantId, deleteReq.Entry, ct: ct);
            }

            await SendResponse(writer, request.MessageId, LdapResultCode.Success, string.Empty, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting {Entry}", deleteReq.Entry);
            await SendResponse(writer, request.MessageId, LdapResultCode.OperationsError, ex.Message, ct);
        }
    }

    private async Task DeleteSubtreeAsync(string tenantId, string dn, CancellationToken ct)
    {
        // Recursively delete children first
        var children = await _store.GetChildrenAsync(tenantId, dn, ct);
        foreach (var child in children)
        {
            await DeleteSubtreeAsync(tenantId, child.DistinguishedName, ct);
        }

        await _store.DeleteAsync(tenantId, dn, ct: ct);
    }

    private static async Task SendResponse(ILdapResponseWriter writer, int messageId, LdapResultCode code, string message, CancellationToken ct)
    {
        var response = new DeleteResponse
        {
            ResultCode = code,
            DiagnosticMessage = message,
        };
        await writer.WriteMessageAsync(messageId, response.Encode(), ct: ct);
    }
}
