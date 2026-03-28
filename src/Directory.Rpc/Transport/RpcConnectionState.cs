using Directory.Rpc.Dispatch;

namespace Directory.Rpc.Transport;

/// <summary>
/// Tracks the state of a single RPC client connection including authentication,
/// negotiated parameters, and bound presentation contexts.
/// </summary>
public class RpcConnectionState
{
    public string RemoteEndPoint { get; set; } = "";

    public RpcAuthState AuthState { get; set; } = RpcAuthState.Anonymous;

    public string AuthenticatedSid { get; set; }

    public string AuthenticatedUser { get; set; }

    public ushort MaxXmitFrag { get; set; } = 4280;

    public ushort MaxRecvFrag { get; set; } = 4280;

    public uint AssocGroupId { get; set; }

    /// <summary>
    /// Maps presentation context IDs to their bound interfaces.
    /// Populated during Bind and AlterContext.
    /// </summary>
    public Dictionary<ushort, BoundInterface> BoundContexts { get; } = new();

    /// <summary>
    /// The 8-byte NTLM challenge generated during Bind authentication.
    /// </summary>
    public byte[] NtlmChallenge { get; set; }

    /// <summary>
    /// The NTLM session key derived after successful authentication.
    /// </summary>
    public byte[] SessionKey { get; set; }

    /// <summary>
    /// Per-connection context handle table shared across all calls.
    /// </summary>
    public ContextHandleTable ContextHandles { get; } = new();

    /// <summary>
    /// Tracks in-progress fragment reassembly keyed by call_id.
    /// Used when a multi-fragment request PDU is received.
    /// </summary>
    public Dictionary<uint, FragmentReassemblyBuffer> FragmentBuffers { get; } = new();
}

/// <summary>
/// Accumulates stub data across multiple request PDU fragments for a single call.
/// </summary>
public class FragmentReassemblyBuffer
{
    public ushort Opnum { get; set; }
    public ushort ContextId { get; set; }
    public MemoryStream StubData { get; } = new();
}

public enum RpcAuthState
{
    Anonymous,
    NtlmNegotiating,
    Authenticated
}

public record BoundInterface(Guid InterfaceId, ushort MajorVersion);
