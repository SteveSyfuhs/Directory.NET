namespace Directory.Rpc.Dispatch;

/// <summary>
/// Routes RPC requests to the appropriate interface handler based on the interface UUID.
/// All registered handlers are collected via DI.
/// </summary>
public class RpcInterfaceDispatcher
{
    private readonly Dictionary<Guid, IRpcInterfaceHandler> _handlers;

    public RpcInterfaceDispatcher(IEnumerable<IRpcInterfaceHandler> handlers)
    {
        _handlers = new Dictionary<Guid, IRpcInterfaceHandler>();

        foreach (var handler in handlers)
        {
            _handlers[handler.InterfaceId] = handler;
        }
    }

    /// <summary>
    /// Returns the handler for the given interface UUID, or null if not registered.
    /// </summary>
    public IRpcInterfaceHandler GetHandler(Guid interfaceId)
    {
        _handlers.TryGetValue(interfaceId, out var handler);
        return handler;
    }

    /// <summary>
    /// Checks whether an interface is registered and matches the requested major version.
    /// </summary>
    public bool SupportsInterface(Guid interfaceId, ushort majorVersion)
    {
        if (_handlers.TryGetValue(interfaceId, out var handler))
        {
            return handler.MajorVersion == majorVersion;
        }

        return false;
    }

    /// <summary>
    /// Returns all registered interface UUIDs. Useful for endpoint mapper enumeration.
    /// </summary>
    public IReadOnlyCollection<Guid> RegisteredInterfaces => _handlers.Keys;
}
