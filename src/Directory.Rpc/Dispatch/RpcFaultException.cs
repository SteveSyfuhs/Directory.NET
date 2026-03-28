namespace Directory.Rpc.Dispatch;

/// <summary>
/// Thrown by RPC interface handlers to signal an RPC fault.
/// The status code is sent back to the client in a Fault PDU.
/// </summary>
public class RpcFaultException : Exception
{
    public uint StatusCode { get; }

    public RpcFaultException(uint statusCode, string message = null)
        : base(message ?? $"RPC fault 0x{statusCode:X8}")
    {
        StatusCode = statusCode;
    }
}
