using Directory.Core.Interfaces;

namespace Directory.Schema;

/// <summary>
/// Defines the built-in Active Directory schema — attribute definitions and object classes
/// matching real AD behavior.
/// </summary>
public static class BuiltInSchema
{
    public static IReadOnlyList<AttributeSchemaEntry> GetAttributes() =>
    [
        // Core identity attributes
        Attr("distinguishedName", "2.5.4.49", "2.5.5.12", singleValued: true, gc: true, systemOnly: true),
        Attr("objectGUID", "1.2.840.113556.1.4.2", "2.5.5.10", singleValued: true, gc: true, systemOnly: true, indexed: true),
        Attr("objectSid", "1.2.840.113556.1.4.146", "2.5.5.17", singleValued: true, gc: true, systemOnly: true, indexed: true),
        Attr("objectClass", "2.5.4.0", "2.5.5.2", singleValued: false, gc: true, systemOnly: true, indexed: true),
        Attr("objectCategory", "1.2.840.113556.1.4.782", "2.5.5.12", singleValued: true, gc: true, systemOnly: false, indexed: true),
        Attr("name", "2.5.4.41", "2.5.5.12", singleValued: true, gc: true, systemOnly: true),
        Attr("cn", "2.5.4.3", "2.5.5.12", singleValued: true, gc: true, indexed: true),
        Attr("displayName", "2.5.4.13", "2.5.5.12", singleValued: true, gc: true),
        Attr("description", "2.5.4.13", "2.5.5.12", singleValued: false, gc: true),

        // Account attributes
        Attr("sAMAccountName", "1.2.840.113556.1.4.221", "2.5.5.12", singleValued: true, gc: true, indexed: true),
        Attr("sAMAccountType", "1.2.840.113556.1.4.302", "2.5.5.9", singleValued: true, gc: true, systemOnly: true),
        Attr("userPrincipalName", "1.2.840.113556.1.4.656", "2.5.5.12", singleValued: true, gc: true, indexed: true),
        Attr("userAccountControl", "1.2.840.113556.1.4.8", "2.5.5.9", singleValued: true, gc: true),
        Attr("accountExpires", "1.2.840.113556.1.4.159", "2.5.5.16", singleValued: true, gc: true),
        Attr("pwdLastSet", "1.2.840.113556.1.4.96", "2.5.5.16", singleValued: true, gc: false),
        Attr("lastLogon", "1.2.840.113556.1.4.52", "2.5.5.16", singleValued: true, gc: false),
        Attr("lastLogonTimestamp", "1.2.840.113556.1.4.1696", "2.5.5.16", singleValued: true, gc: true),
        Attr("badPwdCount", "1.2.840.113556.1.4.12", "2.5.5.9", singleValued: true, gc: false),
        Attr("badPasswordTime", "1.2.840.113556.1.4.49", "2.5.5.16", singleValued: true, gc: false),
        Attr("logonCount", "1.2.840.113556.1.4.54", "2.5.5.9", singleValued: true, gc: false),
        Attr("primaryGroupId", "1.2.840.113556.1.4.98", "2.5.5.9", singleValued: true, gc: true),
        Attr("unicodePwd", "1.2.840.113556.1.4.90", "2.5.5.10", singleValued: true, gc: false),

        // Group attributes
        Attr("member", "2.5.4.31", "2.5.5.1", singleValued: false, gc: true, indexed: true),
        Attr("memberOf", "1.2.840.113556.1.4.222", "2.5.5.1", singleValued: false, gc: true, indexed: true),
        Attr("groupType", "1.2.840.113556.1.4.750", "2.5.5.9", singleValued: true, gc: true),

        // Contact/Person attributes — with property set GUIDs for ACL enforcement
        Attr("givenName", "2.5.4.42", "2.5.5.12", singleValued: true, gc: true, attributeSecurityGuid: PropertySetGuids.PersonalInformation),
        Attr("sn", "2.5.4.4", "2.5.5.12", singleValued: true, gc: true, attributeSecurityGuid: PropertySetGuids.PersonalInformation),
        Attr("initials", "2.5.4.43", "2.5.5.12", singleValued: true, gc: true, attributeSecurityGuid: PropertySetGuids.PersonalInformation),
        Attr("mail", "0.9.2342.19200300.100.1.3", "2.5.5.12", singleValued: true, gc: true, indexed: true, attributeSecurityGuid: PropertySetGuids.EmailInformation),
        Attr("telephoneNumber", "2.5.4.20", "2.5.5.12", singleValued: true, gc: true, attributeSecurityGuid: PropertySetGuids.GeneralInformation),
        Attr("physicalDeliveryOfficeName", "2.5.4.19", "2.5.5.12", singleValued: true, gc: true, attributeSecurityGuid: PropertySetGuids.GeneralInformation),
        Attr("title", "2.5.4.12", "2.5.5.12", singleValued: true, gc: true, attributeSecurityGuid: PropertySetGuids.PersonalInformation),
        Attr("department", "2.5.4.11", "2.5.5.12", singleValued: true, gc: true, attributeSecurityGuid: PropertySetGuids.PublicInformation),
        Attr("company", "2.5.4.15", "2.5.5.12", singleValued: true, gc: true, attributeSecurityGuid: PropertySetGuids.PublicInformation),
        Attr("streetAddress", "2.5.4.9", "2.5.5.12", singleValued: true, gc: false, attributeSecurityGuid: PropertySetGuids.PersonalInformation),
        Attr("l", "2.5.4.7", "2.5.5.12", singleValued: true, gc: true, description: "Locality (city)", attributeSecurityGuid: PropertySetGuids.PersonalInformation),
        Attr("st", "2.5.4.8", "2.5.5.12", singleValued: true, gc: true, description: "State or province", attributeSecurityGuid: PropertySetGuids.PersonalInformation),
        Attr("postalCode", "2.5.4.17", "2.5.5.12", singleValued: true, gc: true, attributeSecurityGuid: PropertySetGuids.PersonalInformation),
        Attr("co", "2.5.4.6", "2.5.5.12", singleValued: true, gc: true, description: "Country name", attributeSecurityGuid: PropertySetGuids.PersonalInformation),
        Attr("c", "2.5.4.6", "2.5.5.12", singleValued: true, gc: true, description: "Country code (2-letter)", attributeSecurityGuid: PropertySetGuids.PersonalInformation),
        Attr("homePhone", "0.9.2342.19200300.100.1.20", "2.5.5.12", singleValued: true, gc: false, attributeSecurityGuid: PropertySetGuids.PersonalInformation),
        Attr("mobile", "0.9.2342.19200300.100.1.41", "2.5.5.12", singleValued: true, gc: true, attributeSecurityGuid: PropertySetGuids.PersonalInformation),
        Attr("manager", "0.9.2342.19200300.100.1.10", "2.5.5.1", singleValued: true, gc: true, attributeSecurityGuid: PropertySetGuids.GeneralInformation),
        Attr("directReports", "1.2.840.113556.1.4.1397", "2.5.5.1", singleValued: false, gc: true),

        // Computer attributes
        Attr("dNSHostName", "1.2.840.113556.1.4.619", "2.5.5.12", singleValued: true, gc: true, indexed: true),
        Attr("operatingSystem", "1.2.840.113556.1.4.253", "2.5.5.12", singleValued: true, gc: true),
        Attr("operatingSystemVersion", "1.2.840.113556.1.4.254", "2.5.5.12", singleValued: true, gc: true),
        Attr("servicePrincipalName", "1.2.840.113556.1.4.771", "2.5.5.12", singleValued: false, gc: true, indexed: true),

        // Security attributes
        Attr("nTSecurityDescriptor", "1.2.840.113556.1.2.281", "2.5.5.15", singleValued: true, gc: false, systemOnly: false),
        Attr("tokenGroups", "1.2.840.113556.1.4.1301", "2.5.5.17", singleValued: false, gc: false, systemOnly: true),
        Attr("tokenGroupsGlobalAndUniversal", "1.2.840.113556.1.4.1418", "2.5.5.17", singleValued: false, gc: false, systemOnly: true),

        // Metadata attributes
        Attr("whenCreated", "1.2.840.113556.1.2.2", "2.5.5.11", singleValued: true, gc: true, systemOnly: true),
        Attr("whenChanged", "1.2.840.113556.1.2.3", "2.5.5.11", singleValued: true, gc: true, systemOnly: true),
        Attr("uSNCreated", "1.2.840.113556.1.2.19", "2.5.5.16", singleValued: true, gc: true, systemOnly: true),
        Attr("uSNChanged", "1.2.840.113556.1.2.120", "2.5.5.16", singleValued: true, gc: true, systemOnly: true),
        Attr("isDeleted", "1.2.840.113556.1.2.48", "2.5.5.8", singleValued: true, gc: false, systemOnly: true),

        // OU attributes
        Attr("ou", "2.5.4.11", "2.5.5.12", singleValued: true, gc: true),
        Attr("gPLink", "1.2.840.113556.1.4.891", "2.5.5.12", singleValued: true, gc: false),
        Attr("gPOptions", "1.2.840.113556.1.4.892", "2.5.5.9", singleValued: true, gc: false),

        // GPO attributes
        Attr("gPCFileSysPath", "1.2.840.113556.1.4.894", "2.5.5.12", singleValued: true, gc: false),
        Attr("gPCFunctionalityVersion", "1.2.840.113556.1.4.893", "2.5.5.9", singleValued: true, gc: false),
        Attr("versionNumber", "1.2.840.113556.1.4.895", "2.5.5.9", singleValued: true, gc: false),

        // Domain attributes
        Attr("dc", "0.9.2342.19200300.100.1.25", "2.5.5.12", singleValued: true, gc: true),
        Attr("maxPwdAge", "1.2.840.113556.1.4.74", "2.5.5.16", singleValued: true, gc: false),
        Attr("minPwdAge", "1.2.840.113556.1.4.78", "2.5.5.16", singleValued: true, gc: false),
        Attr("minPwdLength", "1.2.840.113556.1.4.79", "2.5.5.9", singleValued: true, gc: false),
        Attr("pwdHistoryLength", "1.2.840.113556.1.4.95", "2.5.5.9", singleValued: true, gc: false),
        Attr("pwdProperties", "1.2.840.113556.1.4.93", "2.5.5.9", singleValued: true, gc: false),
        Attr("lockoutThreshold", "1.2.840.113556.1.4.73", "2.5.5.9", singleValued: true, gc: false),
        Attr("lockoutDuration", "1.2.840.113556.1.4.60", "2.5.5.16", singleValued: true, gc: false),
        Attr("lockoutObservationWindow", "1.2.840.113556.1.4.61", "2.5.5.16", singleValued: true, gc: false),

        // Constructed attributes
        Attr("canonicalName", "1.2.840.113556.1.4.916", "2.5.5.12", singleValued: true, gc: true, systemOnly: true),
        Attr("allowedAttributes", "1.2.840.113556.1.4.913", "2.5.5.12", singleValued: false, gc: false, systemOnly: true),
        Attr("allowedChildClasses", "1.2.840.113556.1.4.911", "2.5.5.2", singleValued: false, gc: false, systemOnly: true),
        Attr("structuralObjectClass", "2.5.21.9", "2.5.5.2", singleValued: true, gc: true, systemOnly: true),
        Attr("createTimeStamp", "2.5.18.1", "2.5.5.11", singleValued: true, gc: true, systemOnly: true),
        Attr("modifyTimeStamp", "2.5.18.2", "2.5.5.11", singleValued: true, gc: true, systemOnly: true),
        Attr("subSchemaSubEntry", "2.5.18.10", "2.5.5.12", singleValued: true, gc: false, systemOnly: true),
        Attr("msDS-UserPasswordExpiryTimeComputed", "1.2.840.113556.1.4.2281", "2.5.5.16", singleValued: true, gc: false, systemOnly: true),
        Attr("msDS-User-Account-Control-Computed", "1.2.840.113556.1.4.2380", "2.5.5.9", singleValued: true, gc: false, systemOnly: true),

        // Delegation attributes
        Attr("msDS-AllowedToActOnBehalfOfOtherIdentity", "1.2.840.113556.1.4.2182", "2.5.5.15", singleValued: true, gc: false),
        Attr("msDS-AllowedToDelegateTo", "1.2.840.113556.1.4.1787", "2.5.5.12", singleValued: false, gc: false),

        // Additional metadata
        Attr("systemFlags", "1.2.840.113556.1.4.375", "2.5.5.9", singleValued: true, gc: false, systemOnly: true),
        Attr("searchFlags", "1.2.840.113556.1.2.334", "2.5.5.9", singleValued: true, gc: false, systemOnly: true),
        Attr("isCriticalSystemObject", "1.2.840.113556.1.4.868", "2.5.5.8", singleValued: true, gc: true, systemOnly: true),
        Attr("showInAdvancedViewOnly", "1.2.840.113556.1.2.169", "2.5.5.8", singleValued: true, gc: false),
        Attr("adminDescription", "1.2.840.113556.1.2.226", "2.5.5.12", singleValued: true, gc: false),
        Attr("instanceType", "1.2.840.113556.1.2.1", "2.5.5.9", singleValued: true, gc: true, systemOnly: true),
        Attr("wellKnownObjects", "1.2.840.113556.1.4.618", "2.5.5.7", singleValued: false, gc: false, systemOnly: true),
        Attr("otherWellKnownObjects", "1.2.840.113556.1.4.1717", "2.5.5.7", singleValued: false, gc: false),

        // Site topology
        Attr("siteObjectBL", "1.2.840.113556.1.4.513", "2.5.5.1", singleValued: false, gc: true, systemOnly: true),
        Attr("serverReferenceBL", "1.2.840.113556.1.4.516", "2.5.5.1", singleValued: false, gc: true, systemOnly: true),

        // Password policy
        Attr("msDS-PasswordReversibleEncryptionEnabled", "1.2.840.113556.1.4.2016", "2.5.5.8", singleValued: true, gc: false),
        Attr("msDS-PasswordComplexityEnabled", "1.2.840.113556.1.4.2017", "2.5.5.8", singleValued: true, gc: false),
        Attr("msDS-MinimumPasswordLength", "1.2.840.113556.1.4.2014", "2.5.5.9", singleValued: true, gc: false),
        Attr("msDS-MinimumPasswordAge", "1.2.840.113556.1.4.2013", "2.5.5.16", singleValued: true, gc: false),
        Attr("msDS-MaximumPasswordAge", "1.2.840.113556.1.4.2012", "2.5.5.16", singleValued: true, gc: false),
        Attr("msDS-LockoutDuration", "1.2.840.113556.1.4.2018", "2.5.5.16", singleValued: true, gc: false),
        Attr("msDS-LockoutThreshold", "1.2.840.113556.1.4.2019", "2.5.5.9", singleValued: true, gc: false),

        // Password history (stores NT hashes of previous passwords)
        Attr("passwordHistory", "1.2.840.113556.1.4.94", "2.5.5.10", singleValued: false, gc: false, systemOnly: true),

        // ── Additional Account attributes ──
        Attr("logonHours", "1.2.840.113556.1.4.64", "2.5.5.10", singleValued: true, gc: false, description: "Logon hours bitmap"),
        Attr("logonWorkstation", "1.2.840.113556.1.4.65", "2.5.5.10", singleValued: true, gc: false, description: "Workstations allowed to log on from"),
        Attr("userWorkstations", "1.2.840.113556.1.4.86", "2.5.5.12", singleValued: true, gc: false, description: "Workstations user can log on to"),
        Attr("scriptPath", "1.2.840.113556.1.4.62", "2.5.5.12", singleValued: true, gc: true, description: "Logon script path"),
        Attr("profilePath", "1.2.840.113556.1.4.139", "2.5.5.12", singleValued: true, gc: true, description: "Roaming profile path"),
        Attr("homeDirectory", "1.2.840.113556.1.4.44", "2.5.5.12", singleValued: true, gc: true, description: "Home directory path"),
        Attr("homeDrive", "1.2.840.113556.1.4.45", "2.5.5.12", singleValued: true, gc: true, description: "Home drive letter"),
        Attr("lockoutTime", "1.2.840.113556.1.4.662", "2.5.5.16", singleValued: true, gc: false, description: "Account lockout time"),
        Attr("codePage", "1.2.840.113556.1.4.16", "2.5.5.9", singleValued: true, gc: false),
        Attr("countryCode", "1.2.840.113556.1.4.25", "2.5.5.9", singleValued: true, gc: false),

        // ── Contact/Person additional attributes ──
        Attr("otherTelephone", "1.2.840.113556.1.4.20", "2.5.5.12", singleValued: false, gc: false, description: "Other telephone numbers"),
        Attr("otherMobile", "1.2.840.113556.1.4.647", "2.5.5.12", singleValued: false, gc: false, description: "Other mobile numbers"),
        Attr("facsimileTelephoneNumber", "2.5.4.23", "2.5.5.12", singleValued: true, gc: true, description: "Fax number"),
        Attr("otherFacsimileTelephoneNumber", "1.2.840.113556.1.4.646", "2.5.5.12", singleValued: false, gc: false, description: "Other fax numbers"),
        Attr("otherHomePhone", "1.2.840.113556.1.4.649", "2.5.5.12", singleValued: false, gc: false, description: "Other home phone numbers"),
        Attr("ipPhone", "1.2.840.113556.1.4.721", "2.5.5.12", singleValued: true, gc: true, description: "IP phone number"),
        Attr("otherIpPhone", "1.2.840.113556.1.4.722", "2.5.5.12", singleValued: false, gc: false, description: "Other IP phone numbers"),
        Attr("pager", "1.2.840.113556.1.4.723", "2.5.5.12", singleValued: true, gc: true, description: "Pager number"),
        Attr("otherPager", "1.2.840.113556.1.4.724", "2.5.5.12", singleValued: false, gc: false, description: "Other pager numbers"),
        Attr("url", "1.2.840.113556.1.4.749", "2.5.5.12", singleValued: false, gc: false, description: "URL list"),
        Attr("wWWHomePage", "1.2.840.113556.1.4.750.1", "2.5.5.12", singleValued: true, gc: true, description: "Web page URL"),
        Attr("info", "1.2.840.113556.1.4.81", "2.5.5.12", singleValued: true, gc: false, description: "Notes/comment"),
        Attr("postOfficeBox", "2.5.4.18", "2.5.5.12", singleValued: false, gc: true, description: "Post office box"),

        // ── Exchange-related attributes ──
        Attr("proxyAddresses", "1.2.840.113556.1.4.656.1", "2.5.5.12", singleValued: false, gc: true, indexed: true, description: "Proxy addresses (email)"),
        Attr("targetAddress", "1.2.840.113556.1.4.657", "2.5.5.12", singleValued: true, gc: true, description: "Target address for mail contacts"),
        Attr("msExchMailboxGuid", "1.2.840.113556.1.4.7000.102.50", "2.5.5.10", singleValued: true, gc: false, description: "Exchange mailbox GUID"),
        Attr("legacyExchangeDN", "1.2.840.113556.1.4.655", "2.5.5.12", singleValued: true, gc: true, indexed: true, description: "Legacy Exchange DN"),
        Attr("mailNickname", "1.2.840.113556.1.4.656.2", "2.5.5.12", singleValued: true, gc: true, description: "Exchange mail alias"),
        Attr("textEncodedORAddress", "1.2.840.113556.1.4.658", "2.5.5.12", singleValued: true, gc: false, description: "Text-encoded X.400 address"),

        // ── Security/Delegation additional attributes ──
        Attr("adminCount", "1.2.840.113556.1.4.150", "2.5.5.9", singleValued: true, gc: false, description: "Admin count (protected group membership)"),

        // ── Terminal Services attributes ──
        Attr("msTSProfilePath", "1.2.840.113556.1.4.1976", "2.5.5.12", singleValued: true, gc: false, description: "Terminal Services profile path"),
        Attr("msTSHomeDirectory", "1.2.840.113556.1.4.1977", "2.5.5.12", singleValued: true, gc: false, description: "Terminal Services home directory"),
        Attr("msTSHomeDrive", "1.2.840.113556.1.4.1978", "2.5.5.12", singleValued: true, gc: false, description: "Terminal Services home drive"),

        // ── RAS attributes ──
        Attr("msRADIUSCallbackNumber", "1.2.840.113556.1.4.1145", "2.5.5.12", singleValued: true, gc: false, description: "RADIUS callback number"),
        Attr("msRASSavedCallbackNumber", "1.2.840.113556.1.4.1189", "2.5.5.12", singleValued: true, gc: false, description: "RAS saved callback number"),

        // ── Employee/HR attributes ──
        Attr("employeeID", "1.2.840.113556.1.4.35", "2.5.5.12", singleValued: true, gc: true, description: "Employee identifier"),
        Attr("employeeNumber", "1.2.840.113556.1.4.36", "2.5.5.12", singleValued: true, gc: true, description: "Employee number"),
        Attr("employeeType", "1.2.840.113556.1.4.37", "2.5.5.12", singleValued: true, gc: false, description: "Employee type"),
        Attr("division", "1.2.840.113556.1.4.261", "2.5.5.12", singleValued: true, gc: false, description: "Division"),
        Attr("assistant", "1.2.840.113556.1.4.652", "2.5.5.1", singleValued: true, gc: false, description: "Assistant DN"),

        // ── Schema/System metadata ──
        Attr("objectVersion", "1.2.840.113556.1.4.76", "2.5.5.9", singleValued: true, gc: false, systemOnly: true),

        // ── Replication attributes ──
        Attr("replPropertyMetaData", "1.2.840.113556.1.4.3", "2.5.5.10", singleValued: true, gc: false, systemOnly: true, description: "Replication property metadata"),
        Attr("replUpToDateVector", "1.2.840.113556.1.4.6", "2.5.5.10", singleValued: true, gc: false, systemOnly: true, description: "Replication up-to-date vector"),

        // ── Additional common user attributes ──
        Attr("thumbnailPhoto", "2.16.840.1.113730.3.1.35", "2.5.5.10", singleValued: true, gc: true, description: "Thumbnail photo"),
        Attr("jpegPhoto", "0.9.2342.19200300.100.1.60", "2.5.5.10", singleValued: false, gc: false, description: "JPEG photo"),
        Attr("comment", "2.5.4.14", "2.5.5.12", singleValued: true, gc: false, description: "Comment"),
        Attr("personalTitle", "1.2.840.113556.1.4.266", "2.5.5.12", singleValued: true, gc: false, description: "Personal title (Mr., Ms., etc.)"),
        Attr("generationQualifier", "2.5.4.44", "2.5.5.12", singleValued: true, gc: true, description: "Generation qualifier (Jr., Sr., etc.)"),
        Attr("middleName", "1.2.840.113556.1.4.269", "2.5.5.12", singleValued: true, gc: false, description: "Middle name or initial"),
        Attr("distinguishedNamePrefix", "1.2.840.113556.1.4.636", "2.5.5.12", singleValued: true, gc: false, description: "DN prefix for cross-ref"),
        Attr("otherMailbox", "1.2.840.113556.1.4.651", "2.5.5.12", singleValued: false, gc: false, description: "Other mailbox addresses"),
        Attr("msDS-PrincipalName", "1.2.840.113556.1.4.1865", "2.5.5.12", singleValued: true, gc: false, systemOnly: true, description: "Computed principal name"),
        Attr("msDS-ResultantPSO", "1.2.840.113556.1.4.2060", "2.5.5.1", singleValued: true, gc: false, systemOnly: true, description: "Resultant Password Settings Object"),
        Attr("msDS-AuthenticatedAtDC", "1.2.840.113556.1.4.2053", "2.5.5.1", singleValued: false, gc: false, systemOnly: true, description: "Authenticated at DC"),
        Attr("msDS-SupportedEncryptionTypes", "1.2.840.113556.1.4.2066", "2.5.5.9", singleValued: true, gc: false, description: "Supported Kerberos encryption types"),
        Attr("msDS-RevealedDSAs", "1.2.840.113556.1.4.1930", "2.5.5.1", singleValued: false, gc: false, systemOnly: true),
        Attr("msDS-NeverRevealGroup", "1.2.840.113556.1.4.1926", "2.5.5.1", singleValued: false, gc: false),
        Attr("msDS-RevealOnDemandGroup", "1.2.840.113556.1.4.1928", "2.5.5.1", singleValued: false, gc: false),
        Attr("lastLogoff", "1.2.840.113556.1.4.51", "2.5.5.16", singleValued: true, gc: false, systemOnly: true, description: "Last logoff timestamp"),
        Attr("dSCorePropagationData", "1.2.840.113556.1.4.1357", "2.5.5.11", singleValued: false, gc: false, systemOnly: true),
    ];

    // ── Default Security Descriptor SDDL strings per object class ──────────
    // These use well-known SDDL SID aliases resolved at runtime:
    //   DA=Domain Admins, EA=Enterprise Admins, SY=SYSTEM, BA=Builtin Administrators,
    //   AU=Authenticated Users, CO=Creator Owner, AO=Account Operators, PS=Principal Self
    // Rights: RP=ReadProp, WP=WriteProp, CC=CreateChild, DC=DeleteChild, LC=ListChildren,
    //   LO=ListObject, RC=ReadControl, WD=WriteDacl, WO=WriteOwner, SD=Delete, DT=DeleteTree,
    //   SW=SelfWrite, CR=ControlAccess/ExtendedRight

    private const string SddlTop =
        "D:(A;;RPWPCRCCDCLCLORCWOWDSDDTSW;;;DA)(A;;RPWPCRCCDCLCLORCWOWDSDDTSW;;;SY)(A;;RPLCLORC;;;AU)";

    private const string SddlDomain =
        "D:PAI(A;;RPWPCRCCDCLCLORCWOWDSDDTSW;;;DA)(A;;RPWPCRCCDCLCLORCWOWDSDDTSW;;;EA)" +
        "(A;;RPWPCRCCDCLCLORCWOWDSDDTSW;;;SY)(A;;RPWPCRCCDCLCLORCWOWDSDDTSW;;;BA)" +
        "(A;;RPLCLORC;;;AU)" +
        "(OA;;CR;ab721a53-1e2f-11d0-9819-00aa0040529b;;AU)" +
        "(A;;RPWPCRCCDCLCLORCWOWDSDDTSW;;;CO)";

    private const string SddlOrganizationalUnit =
        "D:(A;;RPWPCRCCDCLCLORCWOWDSDDTSW;;;DA)(A;;RPWPCRCCDCLCLORCWOWDSDDTSW;;;SY)(A;;RPLCLORC;;;AU)";

    private const string SddlUser =
        "D:(A;;RPWPCRCCDCLCLORCWOWDSDDTSW;;;DA)(A;;RPWPCRCCDCLCLORCWOWDSDDTSW;;;SY)" +
        "(A;;RPWPCRCCDCLCLORCWOWDSDDTSW;;;AO)(A;;RPLCLORC;;;PS)" +
        "(OA;;CR;ab721a53-1e2f-11d0-9819-00aa0040529b;;PS)" +
        "(OA;;CR;ab721a54-1e2f-11d0-9819-00aa0040529b;;PS)" +
        "(OA;;RPWP;77B5B886-944A-11d1-AEBD-0000F80367C1;;PS)" +
        "(OA;;RPWP;E45795B2-9455-11d1-AEBD-0000F80367C1;;PS)" +
        "(OA;;RPWP;E45795B3-9455-11d1-AEBD-0000F80367C1;;PS)" +
        "(OA;;RP;59ba2f42-79a2-11d0-9020-00c04fc2d3cf;;AU)" +
        "(OA;;RP;77B5B886-944A-11d1-AEBD-0000F80367C1;;AU)" +
        "(OA;;RP;E45795B3-9455-11d1-AEBD-0000F80367C1;;AU)" +
        "(OA;;RP;e48d0154-bcf8-11d1-8702-00c04fb96050;;AU)" +
        "(A;;RPLCLORC;;;AU)";

    private const string SddlGroup =
        "D:(A;;RPWPCRCCDCLCLORCWOWDSDDTSW;;;DA)(A;;RPWPCRCCDCLCLORCWOWDSDDTSW;;;SY)" +
        "(A;;RPLCLORC;;;AU)(A;;RPWPCRCCDCLCLORCWOWDSDDTSW;;;AO)" +
        "(OA;;RP;46a9b11d-60ae-405a-b7e8-ff8a58d456d2;;S-1-5-32-560)";

    private const string SddlComputer =
        "D:(A;;RPWPCRCCDCLCLORCWOWDSDDTSW;;;DA)(A;;RPWPCRCCDCLCLORCWOWDSDDTSW;;;SY)" +
        "(A;;RPWPCRCCDCLCLORCWOWDSDDTSW;;;CO)(A;;RPLCLORC;;;AU)";

    private const string SddlContainer =
        "D:(A;;RPWPCRCCDCLCLORCWOWDSDDTSW;;;DA)(A;;RPWPCRCCDCLCLORCWOWDSDDTSW;;;SY)(A;;RPLCLORC;;;AU)";

    private const string SddlGroupPolicyContainer =
        "D:(A;;RPWPCRCCDCLCLORCWOWDSDDTSW;;;DA)(A;;RPWPCRCCDCLCLORCWOWDSDDTSW;;;EA)" +
        "(A;;RPWPCRCCDCLCLORCWOWDSDDTSW;;;SY)(A;;RPLCLORC;;;AU)";

    public static IReadOnlyList<ObjectClassSchemaEntry> GetObjectClasses() =>
    [
        ObjClass("top", null, ObjectClassType.Abstract, "2.5.6.0",
            must: ["objectClass"],
            may: ["distinguishedName", "objectGUID", "objectSid", "objectCategory", "name", "cn",
                  "displayName", "description", "whenCreated", "whenChanged", "uSNCreated", "uSNChanged",
                  "isDeleted", "nTSecurityDescriptor", "systemFlags", "showInAdvancedViewOnly",
                  "isCriticalSystemObject", "adminDescription", "instanceType",
                  "objectVersion", "replPropertyMetaData", "replUpToDateVector",
                  "dSCorePropagationData"],
            schemaIdGuid: new Guid("bf967a87-0de6-11d0-a285-00aa003049e2"),
            defaultSd: SddlTop),

        ObjClass("person", "top", ObjectClassType.Structural, "2.5.6.6",
            must: ["cn"],
            may: ["sn", "telephoneNumber", "userPrincipalName"],
            schemaIdGuid: new Guid("bf967aa8-0de6-11d0-a285-00aa003049e2")),

        ObjClass("organizationalPerson", "person", ObjectClassType.Structural, "2.5.6.7",
            must: [],
            may: ["givenName", "initials", "title", "department", "company", "mail",
                  "streetAddress", "l", "st", "postalCode", "co", "c", "manager",
                  "physicalDeliveryOfficeName", "homePhone", "mobile",
                  "facsimileTelephoneNumber", "otherTelephone", "otherMobile",
                  "otherFacsimileTelephoneNumber", "otherHomePhone",
                  "ipPhone", "otherIpPhone", "pager", "otherPager",
                  "postOfficeBox", "info", "personalTitle", "generationQualifier",
                  "middleName", "comment", "countryCode", "division", "assistant",
                  "otherMailbox"],
            schemaIdGuid: new Guid("bf967aa4-0de6-11d0-a285-00aa003049e2")),

        ObjClass("user", "organizationalPerson", ObjectClassType.Structural, "1.2.840.113556.1.5.9",
            must: [],
            may: ["sAMAccountName", "sAMAccountType", "userPrincipalName", "userAccountControl",
                  "accountExpires", "pwdLastSet", "lastLogon", "lastLogonTimestamp",
                  "badPwdCount", "badPasswordTime", "logonCount", "primaryGroupId",
                  "memberOf", "servicePrincipalName", "unicodePwd",
                  "tokenGroups", "tokenGroupsGlobalAndUniversal", "directReports",
                  "passwordHistory",
                  // Account/profile
                  "logonHours", "logonWorkstation", "userWorkstations",
                  "scriptPath", "profilePath", "homeDirectory", "homeDrive",
                  "lockoutTime", "codePage",
                  // Contact
                  "url", "wWWHomePage", "thumbnailPhoto", "jpegPhoto",
                  // Exchange-related
                  "proxyAddresses", "targetAddress", "msExchMailboxGuid",
                  "legacyExchangeDN", "mailNickname", "textEncodedORAddress",
                  // Security
                  "msDS-AllowedToDelegateTo", "msDS-AllowedToActOnBehalfOfOtherIdentity",
                  "adminCount", "adminDescription",
                  // Terminal Services
                  "msTSProfilePath", "msTSHomeDirectory", "msTSHomeDrive",
                  // RAS
                  "msRADIUSCallbackNumber", "msRASSavedCallbackNumber",
                  // Employee/HR
                  "employeeID", "employeeNumber", "employeeType",
                  // Encryption
                  "msDS-SupportedEncryptionTypes"],
            schemaIdGuid: new Guid("bf967aba-0de6-11d0-a285-00aa003049e2"),
            defaultSd: SddlUser),

        ObjClass("computer", "user", ObjectClassType.Structural, "1.2.840.113556.1.5.8",
            must: [],
            may: ["dNSHostName", "operatingSystem", "operatingSystemVersion", "servicePrincipalName"],
            schemaIdGuid: new Guid("bf967a86-0de6-11d0-a285-00aa003049e2"),
            defaultSd: SddlComputer),

        ObjClass("group", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.8",
            must: ["cn"],
            may: ["sAMAccountName", "sAMAccountType", "groupType", "member", "memberOf",
                  "displayName", "mail", "description"],
            schemaIdGuid: new Guid("bf967a9c-0de6-11d0-a285-00aa003049e2"),
            defaultSd: SddlGroup),

        ObjClass("organizationalUnit", "top", ObjectClassType.Structural, "2.5.6.5",
            must: ["ou"],
            may: ["displayName", "description", "gPLink", "gPOptions"],
            schemaIdGuid: new Guid("bf967aa5-0de6-11d0-a285-00aa003049e2"),
            defaultSd: SddlOrganizationalUnit),

        ObjClass("container", "top", ObjectClassType.Structural, "2.5.6.11",
            must: ["cn"],
            may: ["displayName", "description"],
            schemaIdGuid: new Guid("bf967a8b-0de6-11d0-a285-00aa003049e2"),
            defaultSd: SddlContainer),

        ObjClass("domain", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.1",
            must: ["dc"],
            may: ["maxPwdAge", "minPwdAge", "minPwdLength", "pwdHistoryLength", "pwdProperties",
                  "lockoutThreshold", "lockoutDuration", "lockoutObservationWindow",
                  "gPLink", "gPOptions"],
            schemaIdGuid: new Guid("19195a5b-6da0-11d0-afd3-00c04fd930c9"),
            defaultSd: SddlDomain),

        ObjClass("domainDNS", "domain", ObjectClassType.Structural, "1.2.840.113556.1.5.1",
            must: [],
            may: [],
            schemaIdGuid: new Guid("19195a5b-6da0-11d0-afd3-00c04fd930c9"),
            defaultSd: SddlDomain),

        ObjClass("groupPolicyContainer", "container", ObjectClassType.Structural, "1.2.840.113556.1.5.157",
            must: [],
            may: ["gPCFileSysPath", "gPCFunctionalityVersion", "versionNumber"],
            schemaIdGuid: new Guid("f30e3bc2-9ff0-11d1-b603-0000f80367c1"),
            defaultSd: SddlGroupPolicyContainer),

        ObjClass("foreignSecurityPrincipal", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.234",
            must: ["cn"],
            may: ["objectSid"]),

        ObjClass("contact", "organizationalPerson", ObjectClassType.Structural, "1.2.840.113556.1.5.15",
            must: [],
            may: ["mail"]),

        ObjClass("mailRecipient", null, ObjectClassType.Auxiliary, "1.2.840.113556.1.3.46",
            must: [], may: ["mail", "displayName"]),

        ObjClass("securityPrincipal", null, ObjectClassType.Auxiliary, "1.2.840.113556.1.5.6",
            must: [], may: ["sAMAccountName", "sAMAccountType", "objectSid", "memberOf", "tokenGroups"]),

        ObjClass("msDS-PasswordSettings", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.270",
            must: ["cn"], may: ["msDS-PasswordComplexityEnabled", "msDS-MinimumPasswordLength", "msDS-MinimumPasswordAge", "msDS-MaximumPasswordAge", "msDS-LockoutDuration", "msDS-LockoutThreshold"]),

        ObjClass("site", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.26",
            must: ["cn"], may: ["displayName", "description"],
            defaultSd: SddlContainer),

        ObjClass("subnet", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.28",
            must: ["cn"], may: ["displayName", "description", "siteObjectBL"],
            defaultSd: SddlContainer),

        ObjClass("server", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.23",
            must: ["cn"], may: ["dNSHostName", "serverReferenceBL"],
            defaultSd: SddlContainer),

        ObjClass("nTDSDSA", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.7000.47",
            must: ["cn"], may: ["systemFlags"],
            defaultSd: SddlContainer),

        ObjClass("crossRef", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.16",
            must: ["cn"], may: ["dNSHostName", "systemFlags"],
            defaultSd: SddlContainer),
    ];

    /// <summary>
    /// Well-known AD property set GUIDs used for object-specific ACEs.
    /// </summary>
    public static class PropertySetGuids
    {
        public static readonly Guid ChangePassword = new("ab721a53-1e2f-11d0-9819-00aa0040529b");
        public static readonly Guid ResetPassword = new("ab721a54-1e2f-11d0-9819-00aa0040529b");
        public static readonly Guid PersonalInformation = new("77B5B886-944A-11d1-AEBD-0000F80367C1");
        public static readonly Guid WebInformation = new("E45795B2-9455-11d1-AEBD-0000F80367C1");
        public static readonly Guid GeneralInformation = new("59ba2f42-79a2-11d0-9020-00c04fc2d3cf");
        public static readonly Guid PublicInformation = new("e48d0154-bcf8-11d1-8702-00c04fb96050");
        public static readonly Guid EmailInformation = new("E45795B3-9455-11d1-AEBD-0000F80367C1");
    }

    private static AttributeSchemaEntry Attr(
        string name, string oid, string syntax,
        bool singleValued = true, bool gc = false, bool systemOnly = false,
        bool indexed = false, string description = null,
        int? rangeLower = null, int? rangeUpper = null,
        Guid? schemaIdGuid = null, Guid? attributeSecurityGuid = null)
        => new()
        {
            Name = name,
            LdapDisplayName = name,
            Oid = oid,
            Syntax = syntax,
            IsSingleValued = singleValued,
            IsInGlobalCatalog = gc,
            IsSystemOnly = systemOnly,
            IsIndexed = indexed,
            Description = description,
            RangeLower = rangeLower,
            RangeUpper = rangeUpper,
            SchemaIDGUID = schemaIdGuid,
            AttributeSecurityGUID = attributeSecurityGuid,
        };

    private static ObjectClassSchemaEntry ObjClass(
        string name, string superiorClass, ObjectClassType classType, string oid,
        List<string> must, List<string> may, Guid? schemaIdGuid = null,
        string defaultSd = null)
        => new()
        {
            Name = name,
            LdapDisplayName = name,
            Oid = oid,
            SuperiorClass = superiorClass,
            ClassType = classType,
            MustHaveAttributes = must,
            MayHaveAttributes = may,
            SchemaIDGUID = schemaIdGuid,
            DefaultSecurityDescriptor = defaultSd,
        };
}
