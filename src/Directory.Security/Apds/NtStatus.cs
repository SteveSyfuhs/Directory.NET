namespace Directory.Security.Apds;

/// <summary>
/// NT status codes used throughout the MS-APDS authentication protocol.
/// Reference: [MS-ERREF] 2.3.1, [MS-APDS] 3.1.5.
/// </summary>
public enum NtStatus : uint
{
    /// <summary>
    /// The operation completed successfully.
    /// </summary>
    StatusSuccess = 0x00000000,

    /// <summary>
    /// More data is available.
    /// </summary>
    StatusMoreEntries = 0x00000105,

    /// <summary>
    /// Some SIDs could not be resolved.
    /// </summary>
    StatusSomeMapped = 0x00000107,

    /// <summary>
    /// {Buffer Too Small} The buffer is too small to contain the entry.
    /// </summary>
    StatusBufferTooSmall = 0xC0000023,

    /// <summary>
    /// An invalid parameter was passed to a service or function.
    /// </summary>
    StatusInvalidParameter = 0xC000000D,

    /// <summary>
    /// Not enough memory resources are available to complete the operation.
    /// </summary>
    StatusNoMemory = 0xC0000017,

    /// <summary>
    /// Access is denied.
    /// </summary>
    StatusAccessDenied = 0xC0000022,

    /// <summary>
    /// Object Name already exists.
    /// </summary>
    StatusObjectNameCollision = 0xC0000035,

    /// <summary>
    /// The logon attempt failed. Returned when the combination of username
    /// and authentication information is invalid.
    /// </summary>
    StatusLogonFailure = 0xC000006D,

    /// <summary>
    /// The user account has been disabled.
    /// </summary>
    StatusAccountDisabled = 0xC0000072,

    /// <summary>
    /// No SIDs could be resolved.
    /// </summary>
    StatusNoneMapped = 0xC0000073,

    /// <summary>
    /// The referenced account is currently locked out and may not be logged on to.
    /// </summary>
    StatusAccountLockedOut = 0xC0000234,

    /// <summary>
    /// The user's password has expired. MS-APDS 3.1.5: password expiration check.
    /// </summary>
    StatusPasswordExpired = 0xC0000071,

    /// <summary>
    /// The user's account has expired.
    /// </summary>
    StatusAccountExpired = 0xC0000193,

    /// <summary>
    /// The user account has time restrictions and may not be logged on at this time.
    /// </summary>
    StatusInvalidLogonHours = 0xC000006F,

    /// <summary>
    /// The user account is restricted so that it may not be used to log on
    /// from the source workstation.
    /// </summary>
    StatusInvalidWorkstation = 0xC0000070,

    /// <summary>
    /// The user must change their password before they can log on for the first time.
    /// </summary>
    StatusPasswordMustChange = 0xC0000224,

    /// <summary>
    /// The attempted logon is invalid due to a bad username.
    /// </summary>
    StatusNoSuchUser = 0xC0000064,

    /// <summary>
    /// When trying to update a password, this status indicates that the
    /// value provided as the current password is not correct.
    /// </summary>
    StatusWrongPassword = 0xC000006A,

    /// <summary>
    /// The password does not meet complexity/history requirements.
    /// </summary>
    StatusPasswordRestriction = 0xC000006C,

    /// <summary>
    /// A specified domain did not exist.
    /// </summary>
    StatusNoSuchDomain = 0xC00000DF,

    /// <summary>
    /// The SAM database does not have a computer account for this workstation.
    /// </summary>
    StatusNoTrustSamAccount = 0xC000018B,

    /// <summary>
    /// The trust relationship between the primary domain and the trusted domain failed.
    /// </summary>
    StatusTrustedDomainFailure = 0xC000018C,

    /// <summary>
    /// The trust relationship between this workstation and the primary domain failed.
    /// </summary>
    StatusTrustedRelationshipFailure = 0xC000018D,

    /// <summary>
    /// The network logon failed. Used for NTLM/Digest network authentication failures.
    /// </summary>
    StatusLogonTypeNotGranted = 0xC000015B,

    /// <summary>
    /// An internal error occurred.
    /// </summary>
    StatusInternalError = 0xC00000E5,

    /// <summary>
    /// The domain controller is not available.
    /// </summary>
    StatusDomainControllerNotFound = 0xC0000233,

    /// <summary>
    /// Indicates the SAM server was in the wrong state to perform the desired operation.
    /// </summary>
    StatusInvalidServerState = 0xC00000DC,

    /// <summary>
    /// Indicates the domain was in the wrong state to perform the desired operation.
    /// </summary>
    StatusInvalidDomainState = 0xC00000DD,

    /// <summary>
    /// The RPC server does not support the requested operation.
    /// </summary>
    StatusNotSupported = 0xC00000BB,

    /// <summary>
    /// An invalid handle was specified.
    /// </summary>
    StatusInvalidHandle = 0xC0000008,

    /// <summary>
    /// No such group exists.
    /// </summary>
    StatusNoSuchGroup = 0xC0000066,

    /// <summary>
    /// No such alias exists.
    /// </summary>
    StatusNoSuchAlias = 0xC0000151,

    /// <summary>
    /// The specified user already exists.
    /// </summary>
    StatusUserExists = 0xC0000063,

    /// <summary>
    /// Insufficient system resources.
    /// </summary>
    StatusInsufficientResources = 0xC000009A,

    /// <summary>
    /// Object name not found.
    /// </summary>
    StatusObjectNameNotFound = 0xC0000034,

    /// <summary>
    /// No more entries are available from an enumeration operation.
    /// </summary>
    StatusNoMoreEntries = 0x8000001A,

    /// <summary>
    /// The security context is not valid.
    /// </summary>
    StatusInvalidSecurityContext = 0xC0000265,
}
