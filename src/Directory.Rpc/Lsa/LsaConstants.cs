namespace Directory.Rpc.Lsa;

public static class LsaConstants
{
    public static readonly Guid InterfaceId = new("12345778-1234-abcd-ef00-0123456789ab");
    public const ushort MajorVersion = 0;
    public const ushort MinorVersion = 0;

    // Policy access masks
    public const uint PolicyViewLocalInformation = 0x00000001;
    public const uint PolicyViewAuditInformation = 0x00000002;
    public const uint PolicyGetPrivateInformation = 0x00000004;
    public const uint PolicyTrustAdmin = 0x00000008;
    public const uint PolicyCreateAccount = 0x00000010;
    public const uint PolicyCreateSecret = 0x00000020;
    public const uint PolicyCreatePrivilege = 0x00000040;
    public const uint PolicySetDefaultQuotaLimits = 0x00000080;
    public const uint PolicySetAuditRequirements = 0x00000100;
    public const uint PolicyLookupNames = 0x00000800;

    // Policy information classes
    public const ushort PolicyAuditLogInformation = 1;
    public const ushort PolicyAuditEventsInformation = 2;
    public const ushort PolicyPrimaryDomainInformation = 3;
    public const ushort PolicyAccountDomainInformation = 5;
    public const ushort PolicyLsaServerRoleInformation = 6;
    public const ushort PolicyDnsDomainInformation = 12;
    public const ushort PolicyDnsDomainInformationInt = 13;

    // SID name use (SID_NAME_USE enum)
    public const ushort SidTypeUser = 1;
    public const ushort SidTypeGroup = 2;
    public const ushort SidTypeDomain = 3;
    public const ushort SidTypeAlias = 4;
    public const ushort SidTypeWellKnownGroup = 5;
    public const ushort SidTypeDeletedAccount = 6;
    public const ushort SidTypeInvalid = 7;
    public const ushort SidTypeUnknown = 8;
    public const ushort SidTypeComputer = 9;

    // LSA server roles
    public const ushort PolicyServerRoleBackup = 2;
    public const ushort PolicyServerRolePrimary = 3;

    // Policy information classes (additional)
    public const ushort PolicyModificationInformation = 9;
    public const ushort PolicyAuditFullSetInformation = 10;
    public const ushort PolicyAuditFullQueryInformation = 11;

    // Trust direction
    public const uint TrustDirectionDisabled = 0;
    public const uint TrustDirectionInbound = 1;
    public const uint TrustDirectionOutbound = 2;
    public const uint TrustDirectionBidirectional = 3;

    // Trust type
    public const uint TrustTypeDownlevel = 1;
    public const uint TrustTypeUplevel = 2;
    public const uint TrustTypeMit = 3;

    // Trust attributes
    public const uint TrustAttributeNonTransitive = 0x00000001;
    public const uint TrustAttributeUplevelOnly = 0x00000002;
    public const uint TrustAttributeFilterSids = 0x00000004;
    public const uint TrustAttributeForestTransitive = 0x00000008;

    // Trusted domain information classes
    public const ushort TrustedDomainNameInformation = 1;
    public const ushort TrustedControllersInformation = 2;
    public const ushort TrustedPosixOffsetInformation = 3;
    public const ushort TrustedPasswordInformation = 4;
    public const ushort TrustedDomainInformationBasic = 5;
    public const ushort TrustedDomainInformationEx = 6;
    public const ushort TrustedDomainAuthInformation = 7;
    public const ushort TrustedDomainFullInformation = 8;

    // Secret access masks
    public const uint SecretSetValue = 0x00000001;
    public const uint SecretQueryValue = 0x00000002;

    // Account access masks
    public const uint AccountView = 0x00000001;
    public const uint AccountAdjustPrivileges = 0x00000002;
    public const uint AccountAdjustQuotas = 0x00000004;
    public const uint AccountAdjustSystemAccess = 0x00000008;

    // NTSTATUS codes
    public const uint StatusSuccess = 0x00000000;
    public const uint StatusSomeMapped = 0x00000107;
    public const uint StatusNoMoreEntries = 0x8000001A;
    public const uint StatusNoneMapped = 0xC0000073;
    public const uint StatusAccessDenied = 0xC0000022;
    public const uint StatusInvalidHandle = 0xC0000008;
    public const uint StatusInvalidParameter = 0xC000000D;
    public const uint StatusObjectNameNotFound = 0xC0000034;
    public const uint StatusObjectNameCollision = 0xC0000035;
    public const uint StatusInsufficientResources = 0xC000009A;
    public const uint StatusNoSuchPrivilege = 0xC0000060;
    public const uint StatusObjectTypeMismatch = 0xC0000024;
}
