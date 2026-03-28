namespace Directory.Rpc.Protocol;

/// <summary>
/// Parsed bind/alter-context data from a client PDU.
/// </summary>
public record BindData(
    ushort MaxXmitFrag,
    ushort MaxRecvFrag,
    uint AssocGroupId,
    PresentationContext[] Contexts
);

/// <summary>
/// A single presentation context from a bind request, associating an abstract syntax
/// (the RPC interface) with one or more transfer syntaxes (encoding formats).
/// </summary>
public record PresentationContext(
    ushort ContextId,
    RpcSyntaxId AbstractSyntax,
    RpcSyntaxId[] TransferSyntaxes
);

/// <summary>
/// UUID + version identifying an RPC interface or transfer syntax.
/// </summary>
public record RpcSyntaxId(Guid Uuid, uint Version);

/// <summary>
/// Parsed request PDU data containing the operation number and marshaled parameters.
/// </summary>
public record RequestData(
    uint AllocHint,
    ushort ContextId,
    ushort Opnum,
    ReadOnlyMemory<byte> StubData
);

/// <summary>
/// Result of evaluating a single presentation context during bind negotiation.
/// </summary>
public record BindContextResult(
    ushort Result,
    ushort Reason,
    RpcSyntaxId TransferSyntax
);

/// <summary>
/// Parsed auth_verifier from a PDU.
/// </summary>
public record AuthVerifier(
    byte AuthType,
    byte AuthLevel,
    byte AuthPadLength,
    uint AuthContextId,
    ReadOnlyMemory<byte> Credentials
);
