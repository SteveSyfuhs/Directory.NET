namespace Directory.Rpc.Samr;

/// <summary>
/// Constants for the MS-SAMR (Security Account Manager Remote) RPC interface.
/// Reference: [MS-SAMR] https://docs.microsoft.com/en-us/openspecs/windows_protocols/ms-samr
/// </summary>
public static class SamrConstants
{
    /// <summary>
    /// SAMR interface UUID: 12345778-1234-ABCD-EF00-0123456789AC
    /// </summary>
    public static readonly Guid InterfaceId = new("12345778-1234-abcd-ef00-0123456789ac");

    public const ushort MajorVersion = 1;
    public const ushort MinorVersion = 0;

    // ──────────────── Server access masks ────────────────
    public const uint SamServerConnect = 0x00000001;
    public const uint SamServerShutdown = 0x00000002;
    public const uint SamServerInitialize = 0x00000004;
    public const uint SamServerCreateDomain = 0x00000008;
    public const uint SamServerEnumerateDomains = 0x00000010;
    public const uint SamServerLookupDomain = 0x00000020;

    // ──────────────── Domain access masks ────────────────
    public const uint DomainReadPasswordParameters = 0x00000001;
    public const uint DomainWritePasswordParams = 0x00000002;
    public const uint DomainReadOtherParameters = 0x00000004;
    public const uint DomainWriteOtherParameters = 0x00000008;
    public const uint DomainCreateUser = 0x00000010;
    public const uint DomainGetAliasMembership = 0x00000020;
    public const uint DomainCreateAlias = 0x00000040;
    public const uint DomainListAccounts = 0x00000100;
    public const uint DomainLookup = 0x00000200;

    // ──────────────── User access masks ────────────────
    public const uint UserReadGeneral = 0x00000001;
    public const uint UserReadPreferences = 0x00000002;
    public const uint UserWritePreferences = 0x00000004;
    public const uint UserReadLogon = 0x00000008;
    public const uint UserReadAccount = 0x00000010;
    public const uint UserWriteAccount = 0x00000020;
    public const uint UserChangePassword = 0x00000040;
    public const uint UserForcePasswordChange = 0x00000080;
    public const uint UserListGroups = 0x00000100;
    public const uint UserReadGroupInformation = 0x00000200;
    public const uint UserWriteGroupInformation = 0x00000400;
    public const uint UserAllAccess = 0x000F07FF;

    // ──────────────── Group access masks ────────────────
    public const uint GroupReadInformation = 0x00000001;
    public const uint GroupWriteAccount = 0x00000002;
    public const uint GroupAddMember = 0x00000004;
    public const uint GroupRemoveMember = 0x00000008;
    public const uint GroupListMembers = 0x00000010;
    public const uint GroupAllAccess = 0x000F001F;

    // ──────────────── Alias access masks ────────────────
    public const uint AliasAddMember = 0x00000001;
    public const uint AliasRemoveMember = 0x00000002;
    public const uint AliasListMembers = 0x00000004;
    public const uint AliasReadInformation = 0x00000008;
    public const uint AliasWriteAccount = 0x00000010;
    public const uint AliasAllAccess = 0x000F001F;

    // ──────────────── Group information classes ────────────────
    public const ushort GroupGeneralInformation = 1;
    public const ushort GroupNameInformation = 2;
    public const ushort GroupAttributeInformation = 3;
    public const ushort GroupAdminCommentInformation = 4;

    // ──────────────── Alias information classes ────────────────
    public const ushort AliasGeneralInformation = 1;
    public const ushort AliasNameInformation = 2;
    public const ushort AliasAdminCommentInformation = 3;

    // ──────────────── Group attributes ────────────────
    public const uint SeGroupMandatory = 0x00000001;
    public const uint SeGroupEnabledByDefault = 0x00000002;
    public const uint SeGroupEnabled = 0x00000004;
    public const uint SeGroupDefaultAttributes = SeGroupMandatory | SeGroupEnabledByDefault | SeGroupEnabled;

    // ──────────────── Status codes (additional) ────────────────
    public const uint StatusMemberInGroup = 0xC0000068;
    public const uint StatusMemberNotInGroup = 0xC0000069;
    public const uint StatusMemberInAlias = 0xC0000153;
    public const uint StatusMemberNotInAlias = 0xC0000152;

    // ──────────────── User information classes ────────────────
    public const ushort UserGeneralInformation = 1;
    public const ushort UserPreferencesInformation = 2;
    public const ushort UserLogonInformation = 3;
    public const ushort UserLogonHoursInformation = 4;
    public const ushort UserAccountInformation = 5;
    public const ushort UserNameInformation = 6;
    public const ushort UserAccountNameInformation = 7;
    public const ushort UserFullNameInformation = 8;
    public const ushort UserPrimaryGroupInformation = 9;
    public const ushort UserHomeInformation = 10;
    public const ushort UserScriptInformation = 11;
    public const ushort UserProfileInformation = 12;
    public const ushort UserControlInformation = 13;
    public const ushort UserExpiresInformation = 17;
    public const ushort UserAllInformation = 21;
    public const ushort UserInternal4Information = 23;
    public const ushort UserInternal5Information = 24;
    public const ushort UserInternal4InformationNew = 25;
    public const ushort UserInternal5InformationNew = 26;

    // ──────────────── Domain information classes ────────────────
    public const ushort DomainPasswordInformation = 1;
    public const ushort DomainGeneralInformation = 2;
    public const ushort DomainLogoffInformation = 3;
    public const ushort DomainOemInformation = 4;
    public const ushort DomainNameInformation = 5;
    public const ushort DomainGeneralInformation2 = 11;

    // ──────────────── User Account Control flags (SAMR style) ────────────────
    public const uint UfAccountDisable = 0x00000001;
    public const uint UfHomedirRequired = 0x00000002;
    public const uint UfPasswdNotreqd = 0x00000004;
    public const uint UfTempDuplicateAccount = 0x00000008;
    public const uint UfNormalAccount = 0x00000200;
    public const uint UfInterdomainTrustAccount = 0x00000800;
    public const uint UfWorkstationTrustAccount = 0x00001000;
    public const uint UfServerTrustAccount = 0x00002000;
    public const uint UfDontExpirePasswd = 0x00010000;
    public const uint UfEncryptedTextPasswordAllowed = 0x00000080;
    public const uint UfSmartcardRequired = 0x00040000;
    public const uint UfTrustedForDelegation = 0x00080000;
    public const uint UfNotDelegated = 0x00100000;
    public const uint UfUseDesKeyOnly = 0x00200000;
    public const uint UfDontRequirePreauth = 0x00400000;
    public const uint UfPasswordExpired = 0x00800000;
    public const uint UfTrustedToAuthenticateForDelegation = 0x01000000;

    // ──────────────── SAM_ACCOUNT_TYPE values ────────────────
    public const int SamDomainObject = 0x00000000;
    public const int SamGroupObject = 0x10000000;
    public const int SamNonSecurityGroupObject = 0x10000001;
    public const int SamAliasObject = 0x20000000;
    public const int SamNonSecurityAliasObject = 0x20000001;
    public const int SamUserObject = 0x30000000;
    public const int SamMachineAccount = 0x30000001;
    public const int SamTrustAccount = 0x30000002;

    // ──────────────── Well-known RIDs ────────────────
    public const uint RidDomainUsers = 513;
    public const uint RidDomainComputers = 515;

    // ──────────────── SID use types (SID_NAME_USE) ────────────────
    public const uint SidTypeUser = 1;
    public const uint SidTypeGroup = 2;
    public const uint SidTypeDomain = 3;
    public const uint SidTypeAlias = 4;
    public const uint SidTypeWellKnownGroup = 5;
    public const uint SidTypeDeletedAccount = 6;
    public const uint SidTypeInvalid = 7;
    public const uint SidTypeUnknown = 8;

    // ──────────────── NT_STATUS codes ────────────────
    public const uint StatusSuccess = 0x00000000;
    public const uint StatusMoreEntries = 0x00000105;
    public const uint StatusNoSuchDomain = 0xC00000DF;
    public const uint StatusInvalidHandle = 0xC0000008;
    public const uint StatusAccessDenied = 0xC0000022;
    public const uint StatusObjectNameCollision = 0xC0000035;
    public const uint StatusNoSuchUser = 0xC0000064;
    public const uint StatusUserExists = 0xC0000063;
    public const uint StatusInvalidParameter = 0xC000000D;
    public const uint StatusWrongPassword = 0xC000006A;
    public const uint StatusPasswordRestriction = 0xC000006C;
    public const uint StatusNoMemory = 0xC0000017;
    public const uint StatusNoSuchGroup = 0xC0000066;
    public const uint StatusNoSuchAlias = 0xC0000151;
    public const uint StatusNoneMapped = 0xC0000073;
    public const uint StatusSomeMapped = 0x00000107;
    public const uint StatusInvalidServerState = 0xC00000DC;
    public const uint StatusInvalidDomainState = 0xC00000DD;

    // ──────────────── SAMR Opnum constants ────────────────
    public const ushort OpSamrConnect = 0;
    public const ushort OpSamrCloseHandle = 1;
    public const ushort OpSamrLookupDomainInSamServer = 5;
    public const ushort OpSamrEnumerateDomainsInSamServer = 6;
    public const ushort OpSamrOpenDomain = 7;
    public const ushort OpSamrEnumerateUsersInDomain = 13;
    public const ushort OpSamrEnumerateGroupsInDomain = 11;
    public const ushort OpSamrEnumerateAliasesInDomain = 15;
    public const ushort OpSamrGetAliasMembership = 16;
    public const ushort OpSamrLookupNamesInDomain = 17;
    public const ushort OpSamrLookupIdsInDomain = 18;
    public const ushort OpSamrOpenGroup = 19;
    public const ushort OpSamrQueryInformationGroup = 20;
    public const ushort OpSamrSetInformationGroup = 21;
    public const ushort OpSamrAddMemberToGroup = 22;
    public const ushort OpSamrRemoveMemberFromGroup = 23;
    public const ushort OpSamrGetMembersInGroup = 25;
    public const ushort OpSamrOpenAlias = 26;
    public const ushort OpSamrQueryInformationAlias = 27;
    public const ushort OpSamrAddMemberToAlias = 28;
    public const ushort OpSamrRemoveMemberFromAlias = 32;
    public const ushort OpSamrGetMembersInAlias = 33;
    public const ushort OpSamrOpenUser = 34;
    public const ushort OpSamrChangePasswordUser = 38;
    public const ushort OpSamrGetGroupsForUser = 39;
    public const ushort OpSamrQueryInformationDomain2 = 46;
    public const ushort OpSamrQueryInformationUser2 = 47;
    public const ushort OpSamrCreateUser2InDomain = 50;
    public const ushort OpSamrSetInformationUser2 = 58;
    public const ushort OpSamrQuerySecurityObject = 3;
    public const ushort OpSamrSetSecurityObject = 4;
    public const ushort OpSamrCreateGroupInDomain = 10;
    public const ushort OpSamrCreateUserInDomain = 12;
    public const ushort OpSamrCreateAliasInDomain = 14;
    public const ushort OpSamrDeleteGroup = 24;
    public const ushort OpSamrSetInformationAlias = 29;
    public const ushort OpSamrDeleteUser = 35;
    public const ushort OpSamrQueryDisplayInformation = 40;
    public const ushort OpSamrGetDisplayEnumerationIndex = 41;
    public const ushort OpSamrUnicodeChangePasswordUser2 = 55;
    public const ushort OpSamrGetDomainPasswordInformation = 56;
    public const ushort OpSamrConnect5 = 64;
    public const ushort OpSamrRidToSid = 65;
    public const ushort OpSamrValidatePassword = 67;

    // ──────────────── Display information classes ────────────────
    public const ushort DomainDisplayUser = 1;
    public const ushort DomainDisplayMachine = 2;
    public const ushort DomainDisplayGroup = 3;

    // ──────────────── Password validation types ────────────────
    public const ushort SamValidateAuthentication = 1;
    public const ushort SamValidatePasswordChange = 2;
    public const ushort SamValidatePasswordReset = 3;

    // ──────────────── Password properties flags ────────────────
    public const uint DomainPasswordComplex = 0x00000001;
    public const uint DomainPasswordNoAnonChange = 0x00000002;

    // ──────────────── Security Information masks ────────────────
    public const uint OwnerSecurityInformation = 0x00000001;
    public const uint GroupSecurityInformation = 0x00000002;
    public const uint DaclSecurityInformation = 0x00000004;
    public const uint SaclSecurityInformation = 0x00000008;

    // ──────────────── Additional status codes ────────────────
    public const uint StatusNoMoreEntries = 0x8000001A;
    public const uint StatusSpecialAccount = 0xC0000124;
}
