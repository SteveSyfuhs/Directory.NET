namespace Directory.Rpc.Nrpc;

/// <summary>
/// Constants for the MS-NRPC (Netlogon Remote Protocol) interface.
/// Reference: [MS-NRPC] sections 2.2, 3.1, 3.5.
/// </summary>
public static class NrpcConstants
{
    /// <summary>
    /// DCE/RPC interface UUID for the Netlogon service.
    /// </summary>
    public static readonly Guid InterfaceId = new("12345678-1234-abcd-ef00-01234567cffb");

    public const ushort MajorVersion = 1;
    public const ushort MinorVersion = 0;

    // ---- Opnums ----
    public const ushort OpNetrLogonSamLogon = 2;
    public const ushort OpNetrLogonSamLogoff = 3;
    public const ushort OpNetrServerReqChallenge = 4;
    public const ushort OpNetrServerAuthenticate = 5;
    public const ushort OpNetrGetDCName = 11;
    public const ushort OpNetrLogonControl = 12;
    public const ushort OpNetrLogonControl2 = 14;
    public const ushort OpNetrServerAuthenticate2 = 15;
    public const ushort OpNetrLogonControl2Ex = 18;
    public const ushort OpNetrLogonGetCapabilities = 21;
    public const ushort OpNetrServerAuthenticate3 = 26;
    public const ushort OpDsrGetSiteName = 28;
    public const ushort OpNetrLogonGetDomainInfo = 29;
    public const ushort OpNetrServerPasswordSet2 = 30;
    public const ushort OpDsrAddressToSiteNamesW = 33;
    public const ushort OpDsrGetDcNameEx2 = 34;
    public const ushort OpNetrLogonSamLogonEx = 39;
    public const ushort OpDsrDeregisterDnsHostRecords = 41;
    public const ushort OpNetrServerTrustPasswordsGet = 42;
    public const ushort OpDsrGetForestTrustInformation = 44;
    public const ushort OpNetrLogonSamLogonWithFlags = 45;

    // ---- NetrLogonControl function codes (MS-NRPC 2.2.1.7.1) ----
    public const uint ControlQuery = 1;
    public const uint ControlReplicate = 2;
    public const uint ControlSynchronize = 3;
    public const uint ControlPdcReplicate = 4;
    public const uint ControlRediscover = 5;
    public const uint ControlTcQuery = 6;
    public const uint ControlTcVerify = 10;

    // ---- NetrLogonControl flags (NETLOGON_INFO_1.Flags) ----
    public const uint NetlogonFlagDcReachable = 0x00000001;
    public const uint NetlogonFlagScActive = 0x00000002;

    // ---- NTSTATUS codes (additional) ----
    public const uint StatusNotImplemented = 0xC0000002;

    // ---- Negotiate flags (MS-NRPC 3.1.4.2) ----
    public const uint NegotiateStrongKeys = 0x00004000;
    public const uint Negotiate128Bit = 0x00004000; // same bit as strong keys
    public const uint NegotiateAes = 0x01000000;
    public const uint NegotiateSecureRpc = 0x40000000;

    /// <summary>
    /// Typical flags a DC advertises support for.
    /// </summary>
    public const uint NegotiateSupportedFlags = 0x610FFFFF;

    // ---- DC flags for DsrGetDcNameEx2 (MS-NRPC 2.2.1.2.3) ----
    public const uint DsFlagDc = 0x00000001;
    public const uint DsFlagGc = 0x00000004;
    public const uint DsFlagDs = 0x00000010;
    public const uint DsFlagKdc = 0x00000020;
    public const uint DsFlagClosest = 0x00000080;
    public const uint DsFlagWritable = 0x00000100;
    public const uint DsFlagDns = 0x20000000;

    // ---- Logon levels (MS-NRPC 2.2.1.4.16) ----
    public const ushort NetlogonInteractiveInformation = 1;
    public const ushort NetlogonNetworkInformation = 2;
    public const ushort NetlogonInteractiveTransitiveInformation = 5;
    public const ushort NetlogonNetworkTransitiveInformation = 6;

    // ---- Validation levels (MS-NRPC 2.2.1.4.13) ----
    public const ushort NetlogonValidationSamInfo = 2;
    public const ushort NetlogonValidationSamInfo2 = 3;
    public const ushort NetlogonValidationSamInfo4 = 6;

    // ---- Secure channel types (MS-NRPC 2.2.1.3.13) ----
    public const ushort SecureChannelWorkstation = 2;
    public const ushort SecureChannelTrustedDomain = 3;
    public const ushort SecureChannelServerSecureChannel = 4;

    // ---- NTSTATUS codes ----
    public const uint StatusSuccess = 0x00000000;
    public const uint StatusAccessDenied = 0xC0000022;
    public const uint StatusNoSuchUser = 0xC0000064;
    public const uint StatusWrongPassword = 0xC000006A;
    public const uint StatusAccountDisabled = 0xC0000072;
    public const uint StatusAccountLockedOut = 0xC0000234;
    public const uint StatusNoTrustSamAccount = 0xC000018B;
    public const uint StatusInvalidParameter = 0xC000000D;
    public const uint StatusNotSupported = 0xC00000BB;
}
