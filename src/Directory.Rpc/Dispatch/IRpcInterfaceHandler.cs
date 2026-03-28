namespace Directory.Rpc.Dispatch;

/// <summary>
/// Implemented by each RPC interface (e.g., MS-SAMR, MS-LSAD, MS-NRPC)
/// to handle incoming operation requests on that interface.
/// </summary>
public interface IRpcInterfaceHandler
{
    /// <summary>
    /// The DCE/RPC interface UUID (e.g., SAMR = 12345778-1234-abcd-ef00-0123456789ac).
    /// </summary>
    Guid InterfaceId { get; }

    /// <summary>
    /// Major version of the interface.
    /// </summary>
    ushort MajorVersion { get; }

    /// <summary>
    /// Minor version of the interface.
    /// </summary>
    ushort MinorVersion { get; }

    /// <summary>
    /// Dispatches an RPC operation by opnum, deserializing stub data and returning
    /// the serialized response stub data.
    /// </summary>
    Task<byte[]> HandleRequestAsync(
        ushort opnum,
        ReadOnlyMemory<byte> stubData,
        RpcCallContext context,
        CancellationToken ct);
}
