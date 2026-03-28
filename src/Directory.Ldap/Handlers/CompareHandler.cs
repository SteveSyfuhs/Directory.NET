using Directory.Core.Interfaces;
using Directory.Ldap.Protocol;
using Directory.Ldap.Protocol.Messages;
using Directory.Ldap.Server;
using Microsoft.Extensions.Logging;

namespace Directory.Ldap.Handlers;

public class CompareHandler : ILdapOperationHandler
{
    private readonly IDirectoryStore _store;
    private readonly ILogger<CompareHandler> _logger;

    public LdapOperation Operation => LdapOperation.CompareRequest;

    public CompareHandler(IDirectoryStore store, ILogger<CompareHandler> logger)
    {
        _store = store;
        _logger = logger;
    }

    public async Task HandleAsync(LdapMessage request, ILdapResponseWriter writer, LdapConnectionState state, CancellationToken ct)
    {
        var compareReq = CompareRequest.Decode(request.ProtocolOpData);

        try
        {
            var obj = await _store.GetByDnAsync(state.TenantId, compareReq.Entry, ct);
            if (obj is null)
            {
                await SendResponse(writer, request.MessageId, LdapResultCode.NoSuchObject, "Object not found", ct);
                return;
            }

            var attr = obj.GetAttribute(compareReq.AttributeDesc);
            if (attr is null)
            {
                await SendResponse(writer, request.MessageId, LdapResultCode.CompareFalse, string.Empty, ct);
                return;
            }

            var matches = attr.Values.Any(v =>
                string.Equals(v?.ToString(), compareReq.AssertionValue, StringComparison.OrdinalIgnoreCase));

            var resultCode = matches ? LdapResultCode.CompareTrue : LdapResultCode.CompareFalse;
            await SendResponse(writer, request.MessageId, resultCode, string.Empty, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error comparing {Entry}.{Attr}", compareReq.Entry, compareReq.AttributeDesc);
            await SendResponse(writer, request.MessageId, LdapResultCode.OperationsError, ex.Message, ct);
        }
    }

    private static async Task SendResponse(ILdapResponseWriter writer, int messageId, LdapResultCode code, string message, CancellationToken ct)
    {
        var response = new CompareResponse
        {
            ResultCode = code,
            DiagnosticMessage = message,
        };
        await writer.WriteMessageAsync(messageId, response.Encode(), ct: ct);
    }
}
