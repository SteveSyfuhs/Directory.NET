using Directory.Rpc.Dispatch;
using Directory.Rpc.Protocol;

namespace Directory.Rpc.Nrpc;

/// <summary>
/// MS-NRPC interface handler. Routes incoming DCE/RPC requests by opnum
/// to the corresponding method in <see cref="NrpcOperations"/>.
/// </summary>
public class NrpcInterfaceHandler : IRpcInterfaceHandler
{
    private readonly NrpcOperations _ops;

    public NrpcInterfaceHandler(NrpcOperations ops)
    {
        _ops = ops;
    }

    public Guid InterfaceId => NrpcConstants.InterfaceId;
    public ushort MajorVersion => NrpcConstants.MajorVersion;
    public ushort MinorVersion => NrpcConstants.MinorVersion;

    public Task<byte[]> HandleRequestAsync(
        ushort opnum,
        ReadOnlyMemory<byte> stubData,
        RpcCallContext context,
        CancellationToken ct)
    {
        return opnum switch
        {
            NrpcConstants.OpNetrLogonSamLogon => _ops.NetrLogonSamLogonAsync(stubData, context, ct),
            NrpcConstants.OpNetrLogonSamLogoff => _ops.NetrLogonSamLogoffAsync(stubData, context, ct),
            NrpcConstants.OpNetrServerReqChallenge => _ops.NetrServerReqChallengeAsync(stubData, context, ct),
            NrpcConstants.OpNetrServerAuthenticate => _ops.NetrServerAuthenticateAsync(stubData, context, ct),
            NrpcConstants.OpNetrGetDCName => _ops.NetrGetDCNameAsync(stubData, context, ct),
            NrpcConstants.OpNetrLogonControl => _ops.NetrLogonControlAsync(stubData, context, ct),
            NrpcConstants.OpNetrLogonControl2 => _ops.NetrLogonControl2Async(stubData, context, ct),
            NrpcConstants.OpNetrServerAuthenticate2 => _ops.NetrServerAuthenticate2Async(stubData, context, ct),
            NrpcConstants.OpNetrLogonControl2Ex => _ops.NetrLogonControl2ExAsync(stubData, context, ct),
            NrpcConstants.OpNetrLogonGetCapabilities => _ops.NetrLogonGetCapabilitiesAsync(stubData, context, ct),
            NrpcConstants.OpNetrServerAuthenticate3 => _ops.NetrServerAuthenticate3Async(stubData, context, ct),
            NrpcConstants.OpDsrGetSiteName => _ops.DsrGetSiteNameAsync(stubData, context, ct),
            NrpcConstants.OpNetrLogonGetDomainInfo => _ops.NetrLogonGetDomainInfoAsync(stubData, context, ct),
            NrpcConstants.OpNetrServerPasswordSet2 => _ops.NetrServerPasswordSet2Async(stubData, context, ct),
            NrpcConstants.OpDsrAddressToSiteNamesW => _ops.DsrAddressToSiteNamesWAsync(stubData, context, ct),
            NrpcConstants.OpDsrGetDcNameEx2 => _ops.DsrGetDcNameEx2Async(stubData, context, ct),
            NrpcConstants.OpNetrLogonSamLogonEx => _ops.NetrLogonSamLogonExAsync(stubData, context, ct),
            NrpcConstants.OpDsrDeregisterDnsHostRecords => _ops.DsrDeregisterDnsHostRecordsAsync(stubData, context, ct),
            NrpcConstants.OpNetrServerTrustPasswordsGet => _ops.NetrServerTrustPasswordsGetAsync(stubData, context, ct),
            NrpcConstants.OpDsrGetForestTrustInformation => _ops.DsrGetForestTrustInformationAsync(stubData, context, ct),
            NrpcConstants.OpNetrLogonSamLogonWithFlags => _ops.NetrLogonSamLogonWithFlagsAsync(stubData, context, ct),
            _ => throw new RpcFaultException(RpcConstants.NcaOpRangeError, $"NRPC opnum {opnum} is not supported.")
        };
    }
}
