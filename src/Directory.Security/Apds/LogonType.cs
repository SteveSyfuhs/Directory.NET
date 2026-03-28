namespace Directory.Security.Apds;

/// <summary>
/// Logon types as defined in MS-APDS section 3.1.5.
/// These map to the SECURITY_LOGON_TYPE values used in Windows authentication.
/// </summary>
public enum LogonType : uint
{
    /// <summary>
    /// Interactive logon (local console or RDP with NLA).
    /// The user provides credentials at the logon screen.
    /// </summary>
    Interactive = 2,

    /// <summary>
    /// Network logon (SMB, LDAP bind, etc.).
    /// Credentials are verified but not cached on the target machine.
    /// </summary>
    Network = 3,

    /// <summary>
    /// Batch logon (scheduled tasks).
    /// Used when a process runs on behalf of a user without their direct interaction.
    /// </summary>
    Batch = 4,

    /// <summary>
    /// Service logon.
    /// Used when a service starts under a specific user account.
    /// </summary>
    Service = 5,

    /// <summary>
    /// Unlock workstation logon.
    /// Used when the user unlocks a previously locked workstation.
    /// </summary>
    Unlock = 7,

    /// <summary>
    /// Network cleartext logon (HTTP Basic, LDAP simple bind).
    /// The password is sent in cleartext over the network.
    /// </summary>
    NetworkCleartext = 8,

    /// <summary>
    /// New credentials logon (RunAs with /netonly).
    /// The current token is cloned with new credentials for outbound network connections.
    /// </summary>
    NewCredentials = 9,

    /// <summary>
    /// Remote interactive logon (Terminal Services / RDP).
    /// Similar to Interactive but specifically for remote desktop sessions.
    /// </summary>
    RemoteInteractive = 10,

    /// <summary>
    /// Cached interactive logon.
    /// Uses cached credentials when a domain controller is not available.
    /// </summary>
    CachedInteractive = 11,
}
