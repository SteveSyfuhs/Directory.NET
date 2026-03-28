namespace Directory.Security.Apds;

/// <summary>
/// Represents a SID with associated attributes (SE_GROUP_xxx flags).
/// Maps to the KERB_SID_AND_ATTRIBUTES structure from MS-PAC 2.2.1.
/// </summary>
public class SidAndAttributes
{
    /// <summary>
    /// The SID string (e.g., "S-1-5-21-...").
    /// </summary>
    public string Sid { get; set; } = string.Empty;

    /// <summary>
    /// Attributes flags for this SID.
    /// SE_GROUP_MANDATORY (0x01), SE_GROUP_ENABLED_BY_DEFAULT (0x02),
    /// SE_GROUP_ENABLED (0x04), etc.
    /// </summary>
    public uint Attributes { get; set; }
}

/// <summary>
/// Represents a group membership entry containing a RID and attributes.
/// Maps to GROUP_MEMBERSHIP from MS-NRPC 2.2.1.4.8.
/// </summary>
public class GroupMembership
{
    /// <summary>
    /// The relative identifier of the group within the domain.
    /// </summary>
    public uint RelativeId { get; set; }

    /// <summary>
    /// Group attributes (SE_GROUP_xxx flags).
    /// </summary>
    public uint Attributes { get; set; }
}

/// <summary>
/// SE_GROUP attribute constants for SID_AND_ATTRIBUTES and GROUP_MEMBERSHIP.
/// </summary>
public static class SeGroupAttributes
{
    public const uint Mandatory = 0x00000001;
    public const uint EnabledByDefault = 0x00000002;
    public const uint Enabled = 0x00000004;
    public const uint Owner = 0x00000008;
    public const uint UseForDenyOnly = 0x00000010;
    public const uint Integrity = 0x00000020;
    public const uint IntegrityEnabled = 0x00000040;
    public const uint Resource = 0x20000000;
    public const uint LogonId = 0xC0000000;

    /// <summary>
    /// Default attributes for a normal group membership.
    /// </summary>
    public const uint DefaultGroup = Mandatory | EnabledByDefault | Enabled;
}

/// <summary>
/// USER_ALL information flags controlling which fields are present.
/// </summary>
[Flags]
public enum UserAllFlags : uint
{
    UserAllUsername = 0x00000001,
    UserAllFullname = 0x00000002,
    UserAllUserid = 0x00000004,
    UserAllPrimarygroupid = 0x00000008,
    UserAllAdmincomment = 0x00000010,
    UserAllUsercomment = 0x00000020,
    UserAllHomedirectory = 0x00000040,
    UserAllHomedirectoryDrive = 0x00000080,
    UserAllScriptpath = 0x00000100,
    UserAllProfilepath = 0x00000200,
    UserAllWorkstations = 0x00000400,
    UserAllLastlogon = 0x00000800,
    UserAllLastlogoff = 0x00001000,
    UserAllLogonhours = 0x00002000,
    UserAllBadpasswordcount = 0x00004000,
    UserAllLogoncount = 0x00008000,
    UserAllPasswordcanchange = 0x00010000,
    UserAllPasswordmustchange = 0x00020000,
    UserAllPasswordlastset = 0x00040000,
    UserAllAccountexpires = 0x00080000,
    UserAllUseraccountcontrol = 0x00100000,
    UserAllParameters = 0x00200000,
    UserAllCountrycode = 0x00400000,
    UserAllCodepage = 0x00800000,
    UserAllNtpasswordpresent = 0x01000000,
    UserAllLmpasswordpresent = 0x02000000,
    UserAllPrivatedata = 0x04000000,
    UserAllPasswordexpired = 0x08000000,
    UserAllSecuritydescriptor = 0x10000000,
}

/// <summary>
/// NETLOGON_VALIDATION_SAM_INFO (validation level 2) as defined in MS-NRPC 2.2.1.4.11.
/// Contains the core logon validation information returned by the domain controller
/// after a successful network logon.
/// </summary>
public class NetlogonValidationSamInfo
{
    /// <summary>Last interactive logon time (FILETIME).</summary>
    public long LogonTime { get; set; }

    /// <summary>Last logoff time (FILETIME). Usually 0x7FFFFFFFFFFFFFFF.</summary>
    public long LogoffTime { get; set; } = 0x7FFFFFFFFFFFFFFF;

    /// <summary>Time the user must next change their password (FILETIME).</summary>
    public long KickOffTime { get; set; } = 0x7FFFFFFFFFFFFFFF;

    /// <summary>Time the password was last set (FILETIME).</summary>
    public long PasswordLastSet { get; set; }

    /// <summary>Earliest time the password can be changed (FILETIME).</summary>
    public long PasswordCanChange { get; set; }

    /// <summary>Latest time the password must be changed (FILETIME).</summary>
    public long PasswordMustChange { get; set; } = 0x7FFFFFFFFFFFFFFF;

    /// <summary>The user's account name (sAMAccountName).</summary>
    public string EffectiveName { get; set; } = string.Empty;

    /// <summary>The user's full name (displayName).</summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>The user's logon script path.</summary>
    public string LogonScript { get; set; } = string.Empty;

    /// <summary>The user's profile path.</summary>
    public string ProfilePath { get; set; } = string.Empty;

    /// <summary>The user's home directory path.</summary>
    public string HomeDirectory { get; set; } = string.Empty;

    /// <summary>The user's home directory drive letter.</summary>
    public string HomeDirectoryDrive { get; set; } = string.Empty;

    /// <summary>Number of times the user has logged on.</summary>
    public ushort LogonCount { get; set; }

    /// <summary>Number of bad password attempts since last successful logon.</summary>
    public ushort BadPasswordCount { get; set; }

    /// <summary>The user's RID (relative identifier within the domain).</summary>
    public uint UserId { get; set; }

    /// <summary>The user's primary group RID.</summary>
    public uint PrimaryGroupId { get; set; } = 513; // Domain Users

    /// <summary>Array of group RIDs the user belongs to in the account domain.</summary>
    public List<GroupMembership> GroupIds { get; set; } = [];

    /// <summary>
    /// User flags indicating which fields are populated.
    /// Bit 0x0020 = Extra SIDs present.
    /// </summary>
    public uint UserFlags { get; set; }

    /// <summary>The user's session key (16 bytes, usually zeroed for network logon).</summary>
    public byte[] UserSessionKey { get; set; } = new byte[16];

    /// <summary>The NetBIOS name of the logon server (domain controller).</summary>
    public string LogonServer { get; set; } = string.Empty;

    /// <summary>The NetBIOS name of the logon domain.</summary>
    public string LogonDomainName { get; set; } = string.Empty;

    /// <summary>The domain SID of the account domain.</summary>
    public string LogonDomainId { get; set; } = string.Empty;

    /// <summary>Reserved fields (2 x uint32). Must be zero.</summary>
    public uint[] Reserved { get; set; } = [0, 0];
}

/// <summary>
/// NETLOGON_VALIDATION_SAM_INFO2 (validation level 3) as defined in MS-NRPC 2.2.1.4.12.
/// Extends SAM_INFO with extra SIDs from universal group membership and SID history.
/// </summary>
public class NetlogonValidationSamInfo2 : NetlogonValidationSamInfo
{
    /// <summary>
    /// Extra SIDs from resource groups, SID history, universal groups in other domains, etc.
    /// These are included when the UserFlags has the EXTRA_SIDS flag (0x0020).
    /// </summary>
    public List<SidAndAttributes> ExtraSids { get; set; } = [];
}

/// <summary>
/// NETLOGON_VALIDATION_SAM_INFO4 (validation level 6) as defined in MS-NRPC 2.2.1.4.13.
/// Extends SAM_INFO2 with DNS domain information for cross-forest authentication.
/// </summary>
public class NetlogonValidationSamInfo4 : NetlogonValidationSamInfo2
{
    /// <summary>
    /// The DNS name of the logon domain (e.g., "corp.example.com").
    /// </summary>
    public string DnsLogonDomainName { get; set; } = string.Empty;

    /// <summary>
    /// The UPN (User Principal Name) of the user (e.g., "user@corp.example.com").
    /// </summary>
    public string Upn { get; set; } = string.Empty;

    /// <summary>
    /// Expansion room for future SIDs used in resource domain authorization.
    /// </summary>
    public List<SidAndAttributes> ResourceGroupDomainSid { get; set; } = [];

    /// <summary>
    /// Resource group memberships.
    /// </summary>
    public List<GroupMembership> ResourceGroupIds { get; set; } = [];
}
