using Directory.Ldap.Protocol;
using Directory.Ldap.Protocol.Messages;
using Directory.Ldap.Server;
using Microsoft.Extensions.Logging;

namespace Directory.Ldap.Handlers;

public class AbandonHandler : ILdapOperationHandler
{
    private readonly ILogger<AbandonHandler> _logger;

    public LdapOperation Operation => LdapOperation.AbandonRequest;

    public AbandonHandler(ILogger<AbandonHandler> logger)
    {
        _logger = logger;
    }

    public Task HandleAsync(LdapMessage request, ILdapResponseWriter writer, LdapConnectionState state, CancellationToken ct)
    {
        var abandonReq = AbandonRequest.Decode(request.ProtocolOpData);

        _logger.LogDebug("Abandon request for messageId={Id}", abandonReq.MessageIdToAbandon);

        state.TryAbandonOperation(abandonReq.MessageIdToAbandon);

        // Abandon has no response per RFC 4511
        return Task.CompletedTask;
    }
}
