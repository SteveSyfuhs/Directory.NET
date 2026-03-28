using Directory.Rpc.Dispatch;

namespace Directory.Rpc.Lsa;

/// <summary>
/// MS-LSAD / MS-LSAT RPC interface handler.
/// Dispatches LSA opnums to LsaOperations and related operation classes.
/// </summary>
public class LsaInterfaceHandler : IRpcInterfaceHandler
{
    private readonly LsaOperations _ops;
    private readonly LsaSecretOperations _secretOps;
    private readonly LsaTrustedDomainOperations _tdOps;
    private readonly LsaAccountRightsOperations _rightsOps;

    public LsaInterfaceHandler(
        LsaOperations ops,
        LsaSecretOperations secretOps,
        LsaTrustedDomainOperations tdOps,
        LsaAccountRightsOperations rightsOps)
    {
        _ops = ops;
        _secretOps = secretOps;
        _tdOps = tdOps;
        _rightsOps = rightsOps;
    }

    public Guid InterfaceId => LsaConstants.InterfaceId;
    public ushort MajorVersion => LsaConstants.MajorVersion;
    public ushort MinorVersion => LsaConstants.MinorVersion;

    public Task<byte[]> HandleRequestAsync(ushort opnum, ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        return opnum switch
        {
            // Core policy operations
            0 => _ops.LsarCloseAsync(stubData, context, ct),
            2 => _ops.LsarEnumeratePrivilegesAsync(stubData, context, ct),
            3 => _ops.LsarQuerySecurityObjectAsync(stubData, context, ct),
            4 => _ops.LsarSetSecurityObjectAsync(stubData, context, ct),
            7 => _ops.LsarQueryInformationPolicyAsync(stubData, context, ct),
            8 => _ops.LsarSetInformationPolicyAsync(stubData, context, ct),
            11 => _ops.LsarEnumerateAccountsAsync(stubData, context, ct),

            // Trusted domain operations
            12 => _tdOps.LsarCreateTrustedDomainAsync(stubData, context, ct),
            25 => _tdOps.LsarOpenTrustedDomainAsync(stubData, context, ct),
            26 => _tdOps.LsarQueryInfoTrustedDomainAsync(stubData, context, ct),
            27 => _tdOps.LsarSetInformationTrustedDomainAsync(stubData, context, ct),

            // Name/SID lookup
            14 => _ops.LsarLookupNamesAsync(stubData, context, ct),
            15 => _ops.LsarLookupSidsAsync(stubData, context, ct),

            // Secret operations
            16 => _secretOps.LsarCreateSecretAsync(stubData, context, ct),
            28 => _secretOps.LsarOpenSecretAsync(stubData, context, ct),
            29 => _secretOps.LsarQuerySecretAsync(stubData, context, ct),
            30 => _secretOps.LsarSetSecretAsync(stubData, context, ct),

            // Account privilege operations
            18 => _ops.LsarOpenAccountAsync(stubData, context, ct),
            19 => _ops.LsarEnumeratePrivilegesAccountAsync(stubData, context, ct),
            20 => _ops.LsarAddPrivilegesToAccountAsync(stubData, context, ct),
            21 => _ops.LsarRemovePrivilegesFromAccountAsync(stubData, context, ct),

            // Privilege lookup (MS-LSAD opnums 31-33)
            31 => _ops.LsarLookupPrivilegeValueAsync(stubData, context, ct),
            32 => _ops.LsarLookupPrivilegeNameAsync(stubData, context, ct),
            33 => _ops.LsarLookupPrivilegeDisplayNameAsync(stubData, context, ct),

            // Account rights operations (MS-LSAD opnums 35-38)
            35 => _rightsOps.LsarEnumerateAccountsWithUserRightAsync(stubData, context, ct),
            36 => _rightsOps.LsarEnumerateAccountRightsAsync(stubData, context, ct),
            37 => _rightsOps.LsarAddAccountRightsAsync(stubData, context, ct),
            38 => _rightsOps.LsarRemoveAccountRightsAsync(stubData, context, ct),

            // Policy v2 operations
            44 => _ops.LsarOpenPolicy2Async(stubData, context, ct),
            46 => _ops.LsarQueryInformationPolicy2Async(stubData, context, ct),
            47 => _ops.LsarSetInformationPolicy2Async(stubData, context, ct),

            // Trusted domain enumeration
            50 => _ops.LsarEnumerateTrustedDomainsExAsync(stubData, context, ct),

            // Enhanced name/SID lookup
            57 => _ops.LsarLookupSids2Async(stubData, context, ct),
            58 => _ops.LsarLookupNames2Async(stubData, context, ct),
            68 => _ops.LsarLookupNames3Async(stubData, context, ct),
            76 => _ops.LsarLookupSids3Async(stubData, context, ct),

            _ => throw new RpcFaultException(0x1C010002, $"LSA opnum {opnum} not implemented"),
        };
    }
}
