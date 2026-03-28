using Directory.Ldap.Protocol;
using Directory.Ldap.Server;
using Microsoft.Extensions.Logging;

namespace Directory.Ldap.Handlers;

/// <summary>
/// Handles UnbindRequest — signals connection close. No response is sent.
/// </summary>
public class UnbindHandler : ILdapOperationHandler
{
    private readonly ILogger<UnbindHandler> _logger;

    public LdapOperation Operation => LdapOperation.UnbindRequest;

    public UnbindHandler(ILogger<UnbindHandler> logger)
    {
        _logger = logger;
    }

    public Task HandleAsync(LdapMessage request, ILdapResponseWriter writer, LdapConnectionState state, CancellationToken ct)
    {
        _logger.LogDebug("Unbind request from {Endpoint}", state.RemoteEndPoint);

        // Reset state — the connection handler will close the connection
        state.BindStatus = BindState.Anonymous;
        state.BoundDn = null;

        // UnbindRequest has no response per RFC 4511
        return Task.CompletedTask;
    }
}
