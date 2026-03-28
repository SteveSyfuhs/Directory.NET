namespace Directory.Ldap.Protocol;

/// <summary>
/// Well-known LDAP OIDs and constants.
/// </summary>
public static class LdapConstants
{
    // Extended operation OIDs
    public const string OidStartTls = "1.3.6.1.4.1.1466.20037";
    public const string OidWhoAmI = "1.3.6.1.4.1.4203.1.11.3";
    public const string OidPasswordModify = "1.3.6.1.4.1.4203.1.11.1";

    // Control OIDs
    public const string OidPagedResults = "1.2.840.113556.1.4.319";
    public const string OidServerSort = "1.2.840.113556.1.4.473";
    public const string OidServerSortResponse = "1.2.840.113556.1.4.474";
    public const string OidShowDeleted = "1.2.840.113556.1.4.417";
    public const string OidTreeDelete = "1.2.840.113556.1.4.805";
    public const string OidDirSync = "1.2.840.113556.1.4.841";
    public const string OidSdFlags = "1.2.840.113556.1.4.801";
    public const string OidLazyCommit = "1.2.840.113556.1.4.619";
    public const string OidNotificationControl = "1.2.840.113556.1.4.528";
    public const string OidExtendedDn = "1.2.840.113556.1.4.529";
    public const string OidAsq = "1.2.840.113556.1.4.1504";
    public const string OidVlv = "2.16.840.1.113730.3.4.9";
    public const string OidVlvResponse = "2.16.840.1.113730.3.4.10";

    /// <summary>
    /// Nested class providing friendly names for control OIDs, used by LdapControlHandler.
    /// </summary>
    public static class Controls
    {
        public const string PagedResults = OidPagedResults;
        public const string ServerSort = OidServerSort;
        public const string ServerSortResponse = OidServerSortResponse;
        public const string ShowDeleted = OidShowDeleted;
        public const string TreeDelete = OidTreeDelete;
        public const string DirSync = OidDirSync;
        public const string SdFlags = OidSdFlags;
        public const string LazyCommit = OidLazyCommit;
        public const string Notification = OidNotificationControl;
        public const string ExtendedDn = OidExtendedDn;
        public const string Asq = OidAsq;
        public const string Vlv = OidVlv;
        public const string VlvResponse = OidVlvResponse;
        public const string PermissiveModify = OidPermissiveModify;
        public const string ManageDsaIT = OidManageDsaIT;
        public const string PersistentSearch = OidPersistentSearch;
        public const string EntryChangeNotification = OidEntryChangeNotification;
    }

    // AD Capability OIDs (supportedCapabilities) — Windows clients check these
    public const string OidActiveDirectory = "1.2.840.113556.1.4.800";           // LDAP_CAP_ACTIVE_DIRECTORY_OID
    public const string OidActiveDirectoryV51 = "1.2.840.113556.1.4.1670";       // LDAP_CAP_ACTIVE_DIRECTORY_V51_OID (W2K3+)
    public const string OidActiveDirectoryV60 = "1.2.840.113556.1.4.1935";       // LDAP_CAP_ACTIVE_DIRECTORY_V60_OID (W2K8)
    public const string OidActiveDirectoryV61 = "1.2.840.113556.1.4.2080";       // LDAP_CAP_ACTIVE_DIRECTORY_V61_OID (W2K8R2)
    public const string OidActiveDirectoryW8 = "1.2.840.113556.1.4.2237";        // LDAP_CAP_ACTIVE_DIRECTORY_W8_OID (2012)
    public const string OidActiveDirectoryPartialSecrets = "1.2.840.113556.1.4.1920"; // RODC support
    public const string OidActiveDirectoryLdapInteg = "1.2.840.113556.1.4.1791"; // LDAP_CAP_ACTIVE_DIRECTORY_LDAP_INTEG_OID (signing)

    // Additional control OIDs Windows clients use
    public const string OidPermissiveModify = "1.2.840.113556.1.4.1413";
    public const string OidDomainScope = "1.2.840.113556.1.4.1339";
    public const string OidSearchOptions = "1.2.840.113556.1.4.1340";
    public const string OidRangeRetrieval = "1.2.840.113556.1.4.802";
    public const string OidVerifyName = "1.2.840.113556.1.4.1338";
    public const string OidInputDn = "1.2.840.113556.1.4.2026";
    public const string OidBatchedRequest = "1.2.840.113556.1.4.2212";
    public const string OidManageDsaIT = "2.16.840.1.113730.3.4.2";
    public const string OidPersistentSearch = "2.16.840.1.113730.3.4.3";
    public const string OidEntryChangeNotification = "2.16.840.1.113730.3.4.7";

    // AD Extended Operations
    public const string OidFastBind = "1.2.840.113556.1.4.1781";
    public const string OidTtlRefresh = "1.3.6.1.4.1.1466.101.119.1";

    // SASL mechanism names
    public const string SaslGssApi = "GSSAPI";
    public const string SaslPlain = "PLAIN";
    public const string SaslExternal = "EXTERNAL";
    public const string SaslNtlmSsp = "GSS-SPNEGO";

    // LDAP protocol version
    public const int LdapVersion3 = 3;

    // Default ports
    public const int DefaultLdapPort = 389;
    public const int DefaultLdapsPort = 636;
    public const int DefaultGcPort = 3268;
    public const int DefaultGcsPort = 3269;
}
