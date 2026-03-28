using Directory.Core.Models;

namespace Directory.Core.Interfaces;

/// <summary>
/// Service for evaluating and enforcing User Account Control (UAC) flags
/// on directory objects, as defined in MS-ADTS 2.2.16.
/// </summary>
public interface IUserAccountControlService
{
    /// <summary>
    /// Returns true if the ACCOUNTDISABLE (0x0002) flag is set.
    /// </summary>
    bool IsAccountDisabled(DirectoryObject obj);

    /// <summary>
    /// Returns true if the account is locked out, considering both the LOCKOUT flag
    /// and the lockoutTime vs lockoutDuration policy.
    /// </summary>
    bool IsAccountLockedOut(DirectoryObject obj);

    /// <summary>
    /// Returns true if the password has expired, checking pwdLastSet against maxPwdAge
    /// and respecting the DONT_EXPIRE_PASSWORD flag.
    /// </summary>
    bool IsPasswordExpired(DirectoryObject obj);

    /// <summary>
    /// Performs a comprehensive pre-authentication validation: checks disabled, locked out,
    /// password expired, and account type validity. Returns null on success, or an error message.
    /// </summary>
    string ValidateLogon(DirectoryObject obj);

    /// <summary>
    /// Returns the default UserAccountControl value for a given objectClass.
    /// User = 0x200, Computer = 0x1200, Domain Controller = 0x2200.
    /// </summary>
    int GetDefaultUac(string objectClass);

    /// <summary>
    /// Sets the proper UAC flags on a computer object for domain join
    /// (WORKSTATION_TRUST_ACCOUNT | NORMAL_ACCOUNT).
    /// </summary>
    void SetComputerAccountFlags(DirectoryObject obj);
}
