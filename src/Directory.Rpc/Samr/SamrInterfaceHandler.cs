using Directory.Rpc.Dispatch;
using Directory.Rpc.Protocol;

namespace Directory.Rpc.Samr;

/// <summary>
/// MS-SAMR interface handler. Routes incoming RPC requests by opnum to the
/// corresponding method in <see cref="SamrOperations"/>.
/// </summary>
public class SamrInterfaceHandler : IRpcInterfaceHandler
{
    private readonly SamrOperations _ops;

    public SamrInterfaceHandler(SamrOperations ops)
    {
        _ops = ops;
    }

    public Guid InterfaceId => SamrConstants.InterfaceId;
    public ushort MajorVersion => SamrConstants.MajorVersion;
    public ushort MinorVersion => SamrConstants.MinorVersion;

    public Task<byte[]> HandleRequestAsync(
        ushort opnum,
        ReadOnlyMemory<byte> stubData,
        RpcCallContext context,
        CancellationToken ct)
    {
        return opnum switch
        {
            SamrConstants.OpSamrConnect => _ops.SamrConnectAsync(stubData, context, ct),
            SamrConstants.OpSamrCloseHandle => _ops.SamrCloseHandleAsync(stubData, context, ct),
            SamrConstants.OpSamrQuerySecurityObject => _ops.SamrQuerySecurityObjectAsync(stubData, context, ct),
            SamrConstants.OpSamrSetSecurityObject => _ops.SamrSetSecurityObjectAsync(stubData, context, ct),
            SamrConstants.OpSamrLookupDomainInSamServer => _ops.SamrLookupDomainInSamServerAsync(stubData, context, ct),
            SamrConstants.OpSamrEnumerateDomainsInSamServer => _ops.SamrEnumerateDomainsInSamServerAsync(stubData, context, ct),
            SamrConstants.OpSamrOpenDomain => _ops.SamrOpenDomainAsync(stubData, context, ct),
            SamrConstants.OpSamrCreateGroupInDomain => _ops.SamrCreateGroupInDomainAsync(stubData, context, ct),
            SamrConstants.OpSamrEnumerateGroupsInDomain => _ops.SamrEnumerateGroupsInDomainAsync(stubData, context, ct),
            SamrConstants.OpSamrCreateUserInDomain => _ops.SamrCreateUserInDomainAsync(stubData, context, ct),
            SamrConstants.OpSamrEnumerateUsersInDomain => _ops.SamrEnumerateUsersInDomainAsync(stubData, context, ct),
            SamrConstants.OpSamrCreateAliasInDomain => _ops.SamrCreateAliasInDomainAsync(stubData, context, ct),
            SamrConstants.OpSamrEnumerateAliasesInDomain => _ops.SamrEnumerateAliasesInDomainAsync(stubData, context, ct),
            SamrConstants.OpSamrGetAliasMembership => _ops.SamrGetAliasMembershipAsync(stubData, context, ct),
            SamrConstants.OpSamrLookupNamesInDomain => _ops.SamrLookupNamesInDomainAsync(stubData, context, ct),
            SamrConstants.OpSamrLookupIdsInDomain => _ops.SamrLookupIdsInDomainAsync(stubData, context, ct),
            SamrConstants.OpSamrOpenGroup => _ops.SamrOpenGroupAsync(stubData, context, ct),
            SamrConstants.OpSamrQueryInformationGroup => _ops.SamrQueryInformationGroupAsync(stubData, context, ct),
            SamrConstants.OpSamrSetInformationGroup => _ops.SamrSetInformationGroupAsync(stubData, context, ct),
            SamrConstants.OpSamrAddMemberToGroup => _ops.SamrAddMemberToGroupAsync(stubData, context, ct),
            SamrConstants.OpSamrRemoveMemberFromGroup => _ops.SamrRemoveMemberFromGroupAsync(stubData, context, ct),
            SamrConstants.OpSamrDeleteGroup => _ops.SamrDeleteGroupAsync(stubData, context, ct),
            SamrConstants.OpSamrGetMembersInGroup => _ops.SamrGetMembersInGroupAsync(stubData, context, ct),
            SamrConstants.OpSamrOpenAlias => _ops.SamrOpenAliasAsync(stubData, context, ct),
            SamrConstants.OpSamrQueryInformationAlias => _ops.SamrQueryInformationAliasAsync(stubData, context, ct),
            SamrConstants.OpSamrAddMemberToAlias => _ops.SamrAddMemberToAliasAsync(stubData, context, ct),
            SamrConstants.OpSamrSetInformationAlias => _ops.SamrSetInformationAliasAsync(stubData, context, ct),
            SamrConstants.OpSamrRemoveMemberFromAlias => _ops.SamrRemoveMemberFromAliasAsync(stubData, context, ct),
            SamrConstants.OpSamrGetMembersInAlias => _ops.SamrGetMembersInAliasAsync(stubData, context, ct),
            SamrConstants.OpSamrOpenUser => _ops.SamrOpenUserAsync(stubData, context, ct),
            SamrConstants.OpSamrDeleteUser => _ops.SamrDeleteUserAsync(stubData, context, ct),
            SamrConstants.OpSamrChangePasswordUser => _ops.SamrChangePasswordUserAsync(stubData, context, ct),
            SamrConstants.OpSamrGetGroupsForUser => _ops.SamrGetGroupsForUserAsync(stubData, context, ct),
            SamrConstants.OpSamrQueryDisplayInformation => _ops.SamrQueryDisplayInformationAsync(stubData, context, ct),
            SamrConstants.OpSamrGetDisplayEnumerationIndex => _ops.SamrGetDisplayEnumerationIndexAsync(stubData, context, ct),
            SamrConstants.OpSamrQueryInformationDomain2 => _ops.SamrQueryInformationDomain2Async(stubData, context, ct),
            SamrConstants.OpSamrQueryInformationUser2 => _ops.SamrQueryInformationUser2Async(stubData, context, ct),
            SamrConstants.OpSamrCreateUser2InDomain => _ops.SamrCreateUser2InDomainAsync(stubData, context, ct),
            SamrConstants.OpSamrUnicodeChangePasswordUser2 => _ops.SamrUnicodeChangePasswordUser2Async(stubData, context, ct),
            SamrConstants.OpSamrGetDomainPasswordInformation => _ops.SamrGetDomainPasswordInformationAsync(stubData, context, ct),
            SamrConstants.OpSamrSetInformationUser2 => _ops.SamrSetInformationUser2Async(stubData, context, ct),
            SamrConstants.OpSamrConnect5 => _ops.SamrConnect5Async(stubData, context, ct),
            SamrConstants.OpSamrRidToSid => _ops.SamrRidToSidAsync(stubData, context, ct),
            SamrConstants.OpSamrValidatePassword => _ops.SamrValidatePasswordAsync(stubData, context, ct),
            _ => throw new RpcFaultException(RpcConstants.NcaOpRangeError, $"SAMR opnum {opnum} is not supported.")
        };
    }
}
