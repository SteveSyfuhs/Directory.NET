namespace Directory.Rpc.Protocol;

public static class RpcConstants
{
    public const byte VersionMajor = 5;
    public const byte VersionMinor = 0;

    // PDU types
    public const byte PTypeRequest = 0;
    public const byte PTypeResponse = 2;
    public const byte PTypeFault = 3;
    public const byte PTypeBind = 11;
    public const byte PTypeBindAck = 12;
    public const byte PTypeAlterContext = 14;
    public const byte PTypeAlterContextResp = 15;
    public const byte PTypeAuth3 = 16;

    // PDU flags
    public const byte PfcFirstFrag = 0x01;
    public const byte PfcLastFrag = 0x02;
    public const byte PfcPendingCancel = 0x04;
    public const byte PfcConcurrentMultiplexing = 0x10;
    public const byte PfcDidNotExecute = 0x20;
    public const byte PfcMaybe = 0x40;
    public const byte PfcObjectUuid = 0x80;

    // Auth types
    public const byte AuthTypeNone = 0;
    public const byte AuthTypeNtlm = 0x0A;
    public const byte AuthTypeSpnego = 0x09;

    // Auth levels
    public const byte AuthLevelNone = 1;
    public const byte AuthLevelConnect = 2;
    public const byte AuthLevelCall = 3;
    public const byte AuthLevelPkt = 4;
    public const byte AuthLevelIntegrity = 5;
    public const byte AuthLevelPrivacy = 6;

    // Transfer syntax (NDR)
    public static readonly Guid NdrSyntaxId = new("8a885d04-1ceb-11c9-9fe8-08002b104860");
    public const ushort NdrSyntaxVersion = 2;

    // NDR64 transfer syntax
    public static readonly Guid Ndr64SyntaxId = new("71710533-beba-4937-8319-b5dbef9ccc36");

    // Endpoint mapper interface
    public static readonly Guid EpmInterfaceId = new("e1af8308-5d1f-11c9-91a4-08002b14a0fa");
    public const ushort EpmMajorVersion = 3;

    // Bind result codes
    public const ushort BindResultAcceptance = 0;
    public const ushort BindResultProviderRejection = 2;
    public const ushort BindReasonNotSpecified = 0;
    public const ushort BindReasonAbstractSyntaxNotSupported = 1;
    public const ushort BindReasonTransferSyntaxNotSupported = 2;

    // Fault status codes
    public const uint NcaUnspecifiedReject = 0x1c000009;
    public const uint NcaOpRangeError = 0x1c010002;
    public const uint NcaProtocolError = 0x1c01000b;
    public const uint StatusAccessDenied = 0x00000005;
}
