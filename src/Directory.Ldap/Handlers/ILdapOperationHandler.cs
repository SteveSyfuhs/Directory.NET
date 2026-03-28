using Directory.Ldap.Protocol;
using Directory.Ldap.Server;

namespace Directory.Ldap.Handlers;

/// <summary>
/// Handles a specific LDAP operation type.
/// </summary>
public interface ILdapOperationHandler
{
    LdapOperation Operation { get; }
    Task HandleAsync(LdapMessage request, ILdapResponseWriter writer, LdapConnectionState state, CancellationToken ct);
}

/// <summary>
/// Dispatches LDAP messages to the appropriate handler.
/// </summary>
public interface ILdapOperationDispatcher
{
    Task DispatchAsync(LdapMessage message, ILdapResponseWriter writer, LdapConnectionState state, CancellationToken ct);
}
