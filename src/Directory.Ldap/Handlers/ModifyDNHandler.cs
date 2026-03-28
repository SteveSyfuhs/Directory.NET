using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Ldap.Protocol;
using Directory.Ldap.Protocol.Messages;
using Directory.Ldap.Server;
using Microsoft.Extensions.Logging;

namespace Directory.Ldap.Handlers;

public class ModifyDNHandler : ILdapOperationHandler
{
    private readonly IDirectoryStore _store;
    private readonly ILogger<ModifyDNHandler> _logger;

    public LdapOperation Operation => LdapOperation.ModifyDNRequest;

    public ModifyDNHandler(IDirectoryStore store, ILogger<ModifyDNHandler> logger)
    {
        _store = store;
        _logger = logger;
    }

    public async Task HandleAsync(LdapMessage request, ILdapResponseWriter writer, LdapConnectionState state, CancellationToken ct)
    {
        var modDnReq = ModifyDNRequest.Decode(request.ProtocolOpData);

        _logger.LogDebug("ModifyDN request: {Entry} -> newRdn={NewRdn}, newSuperior={NewSuperior}",
            modDnReq.Entry, modDnReq.NewRdn, modDnReq.NewSuperior);

        try
        {
            var obj = await _store.GetByDnAsync(state.TenantId, modDnReq.Entry, ct);
            if (obj is null)
            {
                await SendResponse(writer, request.MessageId, LdapResultCode.NoSuchObject, "Object not found", ct);
                return;
            }

            // Build the new DN
            var oldParsed = DistinguishedName.Parse(modDnReq.Entry);
            var parentDn = modDnReq.NewSuperior ?? oldParsed.Parent().ToString();
            var newDn = $"{modDnReq.NewRdn},{parentDn}";

            // Move the object
            await _store.MoveAsync(state.TenantId, modDnReq.Entry, newDn, ct);

            // If the object is a container, update all descendant DNs
            var children = await _store.GetChildrenAsync(state.TenantId, modDnReq.Entry, ct);
            foreach (var child in children)
            {
                var childOldDn = child.DistinguishedName;
                var childNewDn = childOldDn.Replace(modDnReq.Entry, newDn, StringComparison.OrdinalIgnoreCase);
                await _store.MoveAsync(state.TenantId, childOldDn, childNewDn, ct);
            }

            await SendResponse(writer, request.MessageId, LdapResultCode.Success, string.Empty, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error modifying DN for {Entry}", modDnReq.Entry);
            await SendResponse(writer, request.MessageId, LdapResultCode.OperationsError, ex.Message, ct);
        }
    }

    private static async Task SendResponse(ILdapResponseWriter writer, int messageId, LdapResultCode code, string message, CancellationToken ct)
    {
        var response = new ModifyDNResponse
        {
            ResultCode = code,
            DiagnosticMessage = message,
        };
        await writer.WriteMessageAsync(messageId, response.Encode(), ct: ct);
    }
}
