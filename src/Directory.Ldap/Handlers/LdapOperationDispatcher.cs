using Directory.Ldap.Protocol;
using Directory.Ldap.Server;
using Microsoft.Extensions.Logging;

namespace Directory.Ldap.Handlers;

/// <summary>
/// Routes incoming LDAP messages to the appropriate operation handler.
/// </summary>
public class LdapOperationDispatcher : ILdapOperationDispatcher
{
    private readonly Dictionary<LdapOperation, ILdapOperationHandler> _handlers;
    private readonly ILogger<LdapOperationDispatcher> _logger;

    public LdapOperationDispatcher(IEnumerable<ILdapOperationHandler> handlers, ILogger<LdapOperationDispatcher> logger)
    {
        _handlers = handlers.ToDictionary(h => h.Operation);
        _logger = logger;
    }

    public async Task DispatchAsync(LdapMessage message, ILdapResponseWriter writer, LdapConnectionState state, CancellationToken ct)
    {
        if (_handlers.TryGetValue(message.Operation, out var handler))
        {
            var cts = state.RegisterOperation(message.MessageId);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, cts.Token);

            try
            {
                await handler.HandleAsync(message, writer, state, linkedCts.Token);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                _logger.LogDebug("Operation {Op} messageId={Id} was abandoned", message.Operation, message.MessageId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling {Op} messageId={Id}", message.Operation, message.MessageId);
            }
            finally
            {
                state.CompleteOperation(message.MessageId);
            }
        }
        else
        {
            _logger.LogWarning("No handler for operation {Op}", message.Operation);
        }
    }
}
