using Directory.Core.Interfaces;

namespace Directory.Schema;

/// <summary>
/// Full Active Directory schema classes per [MS-ADSC] (Windows Server 2012 R2+).
/// Contains all ~250 object class definitions from the AD schema specification.
/// Reference: https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-adsc/
/// </summary>
public static class SchemaClasses_Full
{
    public static IEnumerable<ObjectClassSchemaEntry> GetObjectClasses() => [

        // =====================================================================
        // ABSTRACT CLASSES
        // =====================================================================

        ObjClass("top", null, ObjectClassType.Abstract, "2.5.6.0",
            must: ["objectClass"],
            may: ["adminDescription", "adminDisplayName", "allowedAttributes", "allowedAttributesEffective",
                  "allowedChildClasses", "allowedChildClassesEffective", "bridgeheadServerListBL",
                  "canonicalName", "cn", "createTimeStamp", "description", "displayName",
                  "displayNamePrintable", "distinguishedName", "dSASignature", "dSCorePropagationData",
                  "extensionName", "flags", "fromEntry", "frsComputerReferenceBL", "fRSMemberReferenceBL",
                  "fSMORoleOwner", "instanceType", "isCriticalSystemObject", "isDeleted", "isRecycled",
                  "isPrivilegeHolder", "lastKnownParent", "managedObjects", "masteredBy", "memberOf",
                  "modifyTimeStamp", "msComPartitionSetLink", "msComUserLink",
                  "msDFSR-ComputerReferenceBL", "msDFSR-MemberReferenceBL",
                  "msDS-Approx-Immed-Subordinates", "msDS-AuthenticatedToAccountlist",
                  "msDS-ConsistencyChildCount", "msDS-ConsistencyGuid",
                  "msDS-EnabledFeatureBL", "msDS-HostServiceAccountBL",
                  "msDS-IsDomainFor", "msDS-IsFullReplicaFor", "msDS-IsPartialReplicaFor",
                  "msDS-IsPrimaryComputerFor", "msDS-KrbTgtLinkBL", "msDS-LastKnownRDN",
                  "msDS-LocalEffectiveDeletionTime", "msDS-LocalEffectiveRecycleTime",
                  "msDS-MasteredBy", "msDS-MembersForAzRoleBL",
                  "msDS-MembersOfResourcePropertyListBL", "msDS-NCReplCursors",
                  "msDS-NCReplInboundNeighbors", "msDS-NCReplOutboundNeighbors",
                  "msDS-NC-RO-Replica-Locations-BL", "msDS-NCType",
                  "msDS-NonMembersBL", "msDS-ObjectReferenceBL", "msDS-OIDToGroupLinkBL",
                  "msDS-OperationsForAzRoleBL", "msDS-OperationsForAzTaskBL",
                  "msDS-PrincipalName", "msDS-PSOApplied",
                  "msDS-ReplAttributeMetaData", "msDS-ReplValueMetaData",
                  "msDS-RevealedDSAs", "msDS-RevealedListBL",
                  "msDS-TasksForAzRoleBL", "msDS-TasksForAzTaskBL",
                  "msDS-TDOEgressBL", "msDS-TDOIngressBL", "msDS-ValueTypeReferenceBL",
                  "msDS-ClaimSharesPossibleValuesWithBL",
                  "name", "netbootSCPBL", "nonSecurityMemberBL",
                  "nTSecurityDescriptor", "objectCategory", "objectGUID", "objectVersion",
                  "otherWellKnownObjects", "ownerBL", "partialAttributeDeletionList",
                  "partialAttributeSet", "possibleInferiors", "proxiedObjectName", "proxyAddresses",
                  "queryPolicyBL", "replPropertyMetaData", "replUpToDateVector",
                  "directReports", "repsFrom", "repsTo", "revision", "sDRightsEffective",
                  "serverReferenceBL", "showInAdvancedViewOnly", "siteObjectBL",
                  "structuralObjectClass", "subRefs", "subSchemaSubEntry", "systemFlags",
                  "uSNChanged", "uSNCreated", "uSNDSALastObjRemoved", "uSNIntersite",
                  "uSNLastObjRem", "uSNSource", "wbemPath", "wellKnownObjects",
                  "whenChanged", "whenCreated", "wWWHomePage", "url",
                  "msSFU30PosixMemberOf"],
            possSuperiors: ["lostAndFound"],
            description: "The top level class from which all classes are derived."),

        ObjClass("applicationSettings", "top", ObjectClassType.Abstract, "1.2.840.113556.1.5.151",
            must: ["cn"],
            may: ["applicationName", "notificationList"],
            description: "Stores configuration for an application."),

        ObjClass("connectionPoint", "leaf", ObjectClassType.Abstract, "1.2.840.113556.1.5.94",
            must: ["cn"],
            may: ["keywords", "managedBy"],
            description: "An abstract class for connection points."),

        ObjClass("device", "top", ObjectClassType.Abstract, "2.5.6.14",
            must: ["cn"],
            may: ["l", "o", "ou", "owner", "seeAlso", "serialNumber"],
            description: "Represents a network device."),

        ObjClass("leaf", "top", ObjectClassType.Abstract, "1.2.840.113556.1.5.24",
            must: [],
            may: [],
            description: "Leaf objects cannot contain other objects."),

        ObjClass("securityObject", "top", ObjectClassType.Abstract, "1.2.840.113556.1.5.6",
            must: [],
            may: [],
            description: "Contains security information for an object."),

        // =====================================================================
        // AUXILIARY CLASSES
        // =====================================================================

        ObjClass("mailRecipient", null, ObjectClassType.Auxiliary, "1.2.840.113556.1.3.46",
            must: [],
            may: ["authOrig", "autoReply", "autoReplyMessage", "dLMemDefault", "dLMemRejectPerms",
                  "dLMemSubmitPerms", "expirationTime", "extensionData", "garbageCollPeriod",
                  "labeledURI", "mail", "mAPIID", "displayName", "displayNamePrintable",
                  "legacyExchangeDN", "msExchAssistantName", "msExchLabeledURI",
                  "publicDelegates", "publicDelegatesBL", "telephoneNumber",
                  "textEncodedORAddress", "unauthOrig"],
            description: "Mail recipient attributes."),

        ObjClass("securityPrincipal", null, ObjectClassType.Auxiliary, "1.2.840.113556.1.5.6",
            must: [],
            may: ["accountNameHistory", "altSecurityIdentities", "nTSecurityDescriptor",
                  "objectSid", "rid", "sAMAccountName", "sAMAccountType",
                  "securityIdentifier", "sIDHistory", "supplementalCredentials",
                  "tokenGroups", "tokenGroupsGlobalAndUniversal", "tokenGroupsNoGCAcceptable"],
            description: "Security principal attributes."),

        ObjClass("posixAccount", null, ObjectClassType.Auxiliary, "1.3.6.1.1.1.2.0",
            must: ["cn", "gidNumber", "homeDirectory", "uid", "uidNumber"],
            may: ["description", "gecos", "loginShell", "unixHomeDirectory", "userPassword"],
            description: "POSIX account attributes per RFC 2307."),

        ObjClass("posixGroup", null, ObjectClassType.Auxiliary, "1.3.6.1.1.1.2.2",
            must: ["cn", "gidNumber"],
            may: ["description", "memberUid", "unixUserPassword", "userPassword"],
            description: "POSIX group attributes per RFC 2307."),

        ObjClass("shadowAccount", null, ObjectClassType.Auxiliary, "1.3.6.1.1.1.2.1",
            must: ["uid"],
            may: ["description", "shadowExpire", "shadowFlag", "shadowInactive",
                  "shadowLastChange", "shadowMax", "shadowMin", "shadowWarning",
                  "userPassword"],
            description: "Shadow password attributes per RFC 2307."),

        ObjClass("samDomainBase", null, ObjectClassType.Auxiliary, "1.2.840.113556.1.5.6",
            must: [],
            may: ["auditingPolicy", "creationTime", "domainReplica", "forceLogoff",
                  "lockOutObservationWindow", "lockoutDuration", "lockoutThreshold",
                  "maxPwdAge", "minPwdAge", "minPwdLength", "modifiedCount",
                  "modifiedCountAtLastProm", "nETBIOSName", "nextRid",
                  "nTMixedDomain", "objectSid", "oEMInformation",
                  "pwdHistoryLength", "pwdProperties", "replicaSource",
                  "serverRole", "serverState", "uASCompat"],
            description: "SAM domain base attributes."),

        ObjClass("samDomain", null, ObjectClassType.Auxiliary, "1.2.840.113556.1.5.1",
            must: [],
            may: ["auditingPolicy", "builtinCreationTime", "builtinModifiedCount",
                  "creationTime", "desktopProfile", "domainReplica", "forceLogoff",
                  "fSMORoleOwner", "lockOutObservationWindow", "lockoutDuration",
                  "lockoutThreshold", "maxPwdAge", "minPwdAge", "minPwdLength",
                  "modifiedCount", "modifiedCountAtLastProm", "ms-DS-MachineAccountQuota",
                  "nETBIOSName", "nextRid", "nTMixedDomain", "objectSid",
                  "oEMInformation", "pwdHistoryLength", "pwdProperties",
                  "replicaSource", "rIDManagerReference", "serverRole",
                  "serverState", "treeName", "uASCompat"],
            description: "SAM domain auxiliary class used by domainDNS."),

        ObjClass("ipHost", null, ObjectClassType.Auxiliary, "1.3.6.1.1.1.2.6",
            must: ["cn", "ipHostNumber"],
            may: ["l", "description", "manager", "owner"],
            description: "IP host auxiliary class for computers."),

        ObjClass("ieee802Device", null, ObjectClassType.Auxiliary, "1.3.6.1.1.1.2.11",
            must: ["cn"],
            may: ["macAddress"],
            description: "IEEE 802 device (MAC address) auxiliary class."),

        ObjClass("bootableDevice", null, ObjectClassType.Auxiliary, "1.3.6.1.1.1.2.12",
            must: ["cn"],
            may: ["bootFile", "bootParameter"],
            description: "Bootable device auxiliary class."),

        ObjClass("domainRelatedObject", null, ObjectClassType.Auxiliary, "0.9.2342.19200300.100.4.17",
            must: ["associatedDomain"],
            may: [],
            description: "Object related to a DNS domain."),

        // =====================================================================
        // STRUCTURAL CLASSES - Core Directory
        // =====================================================================

        ObjClass("account", "top", ObjectClassType.Structural, "0.9.2342.19200300.100.4.5",
            must: [],
            may: ["description", "host", "l", "o", "ou", "seeAlso", "uid"],
            possSuperiors: ["container", "organizationalUnit"],
            description: "Defines entries that represent computer accounts."),

        ObjClass("person", "top", ObjectClassType.Structural, "2.5.6.6",
            must: ["cn"],
            may: ["seeAlso", "sn", "telephoneNumber", "userPassword",
                  "attributeCertificateAttribute"],
            description: "Contains personal information about a user."),

        ObjClass("organizationalPerson", "person", ObjectClassType.Structural, "2.5.6.7",
            must: [],
            may: ["assistant", "c", "co", "comment", "company", "countryCode",
                  "department", "destinationIndicator", "division", "employeeID",
                  "facsimileTelephoneNumber", "generationQualifier", "givenName",
                  "homePhone", "homePostalAddress", "info", "initials",
                  "internationalISDNNumber", "l", "mail", "manager", "middleName",
                  "mobile", "o", "otherFacsimileTelephoneNumber", "otherHomePhone",
                  "otherIpPhone", "otherMailbox", "otherMobile", "otherPager",
                  "otherTelephone", "ou", "pager", "personalTitle",
                  "physicalDeliveryOfficeName", "postalAddress", "postalCode",
                  "postOfficeBox", "preferredDeliveryMethod",
                  "registeredAddress", "st", "street", "streetAddress",
                  "teletexTerminalIdentifier", "telexNumber",
                  "thumbnailLogo", "thumbnailPhoto", "title",
                  "userSharedFolder", "userSharedFolderOther",
                  "x121Address"],
            description: "Organizational information about a user."),

        ObjClass("user", "organizationalPerson", ObjectClassType.Structural, "1.2.840.113556.1.5.9",
            must: [],
            may: ["accountExpires", "aCSPolicyName", "adminCount", "badPasswordTime",
                  "badPwdCount", "codePage", "controlAccessRights",
                  "dBCSPwd", "defaultClassStore", "desktopProfile",
                  "dynamicLDAPServer", "groupMembershipSAM", "groupPriority",
                  "groupsToIgnore", "homeDirectory", "homeDrive",
                  "lastLogoff", "lastLogon", "lastLogonTimestamp",
                  "lmPwdHistory", "localeID", "lockoutTime",
                  "logonCount", "logonHours", "logonWorkstation",
                  "maxStorage", "msDS-AllowedToActOnBehalfOfOtherIdentity",
                  "msDS-AllowedToDelegateTo", "msDS-AuthenticatedAtDC",
                  "msDS-User-Account-Control-Computed",
                  "msDS-UserPasswordExpiryTimeComputed",
                  "msDS-ResultantPSO", "msDS-PrimaryComputer",
                  "msDS-SupportedEncryptionTypes", "mSMQDigests",
                  "mSMQDigestsMig", "mSMQSignCertificates", "mSMQSignCertificatesMig",
                  "networkAddress", "ntPwdHistory", "o",
                  "objectSid", "operatorCount", "otherLoginWorkstations",
                  "ou", "preferredOU", "primaryGroupID",
                  "profilePath", "pwdLastSet", "sAMAccountName",
                  "sAMAccountType", "scriptPath", "servicePrincipalName",
                  "unicodePwd", "userAccountControl",
                  "userCertificate", "userParameters",
                  "userPrincipalName", "userWorkstations"],
            possSuperiors: ["domainDNS", "organizationalUnit", "builtinDomain"],
            auxiliaryClasses: ["mailRecipient", "securityPrincipal"],
            description: "Stores information about a network user."),

        ObjClass("computer", "user", ObjectClassType.Structural, "1.2.840.113556.1.3.30",
            must: [],
            may: ["catalogs", "defaultLocalPolicyObject", "dNSHostName",
                  "localPolicyFlags", "location", "machineRole",
                  "managedBy", "netbootGUID", "netbootInitialization",
                  "netbootMachineFilePath", "netbootMirrorDataFile", "netbootSIFFile",
                  "operatingSystem", "operatingSystemHotfix",
                  "operatingSystemServicePack", "operatingSystemVersion",
                  "physicalLocationObject", "policyReplicationFlags",
                  "rIDSetReferences", "siteGUID", "volumeCount",
                  "msDS-AdditionalDnsHostName", "msDS-AdditionalSamAccountName",
                  "msDS-AuthenticatedAtDC", "msDS-ExecuteScriptPassword",
                  "msDS-isGC", "msDS-isRODC", "msDS-KrbTgtLink",
                  "msDS-NeverRevealGroup", "msDS-RevealedList",
                  "msDS-RevealOnDemandGroup", "msDS-SiteName",
                  "msTPM-OwnerInformation", "msTPM-TpmInformationForComputer"],
            possSuperiors: ["domainDNS", "organizationalUnit", "container"],
            auxiliaryClasses: ["ipHost"],
            description: "Stores information about a computer in the network."),

        ObjClass("contact", "organizationalPerson", ObjectClassType.Structural, "1.2.840.113556.1.5.15",
            must: [],
            may: ["notes", "msDS-SourceObjectDN"],
            possSuperiors: ["domainDNS", "organizationalUnit"],
            auxiliaryClasses: ["mailRecipient"],
            description: "Contains information about a person not in the directory."),

        ObjClass("group", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.8",
            must: ["groupType"],
            may: ["adminCount", "controlAccessRights", "desktopProfile",
                  "groupAttributes", "groupMembershipSAM",
                  "mail", "managedBy", "member", "msDS-AzApplicationData",
                  "msDS-AzBizRule", "msDS-AzBizRuleLanguage",
                  "msDS-AzGenericData", "msDS-AzLDAPQuery",
                  "msDS-AzObjectGuid", "msDS-NonMembers",
                  "nTGroupMembers", "operatorCount", "oOFReplyToOriginator",
                  "primaryGroupToken", "reportToOriginator", "reportToOwner",
                  "sAMAccountName", "sAMAccountType"],
            possSuperiors: ["domainDNS", "organizationalUnit", "builtinDomain",
                            "msDFSR-GlobalSettings", "msDS-AzApplication",
                            "msDS-AzScope"],
            auxiliaryClasses: ["mailRecipient", "securityPrincipal"],
            description: "Stores a list of user names for applying security."),

        ObjClass("inetOrgPerson", "user", ObjectClassType.Structural, "2.16.840.1.113730.3.2.2",
            must: [],
            may: ["audio", "businessCategory", "carLicense", "departmentNumber",
                  "displayName", "employeeNumber", "employeeType", "givenName",
                  "homePhone", "homePostalAddress", "initials", "jpegPhoto",
                  "labeledURI", "mail", "manager", "mobile", "o", "pager",
                  "photo", "preferredLanguage", "roomNumber", "secretary",
                  "uid", "userCertificate", "userPKCS12",
                  "userSMIMECertificate", "x500uniqueIdentifier"],
            possSuperiors: ["container", "organizationalUnit", "domainDNS"],
            description: "Represents an Internet organizational person."),

        ObjClass("organizationalUnit", "top", ObjectClassType.Structural, "2.5.6.5",
            must: ["ou"],
            may: ["businessCategory", "c", "co", "countryCode",
                  "defaultGroup", "desktopProfile", "dSASignature",
                  "facsimileTelephoneNumber", "gPLink", "gPOptions",
                  "internationalISDNNumber", "l", "managedBy",
                  "msDS-AllowedToActOnBehalfOfOtherIdentity",
                  "physicalDeliveryOfficeName", "postalAddress", "postalCode",
                  "postOfficeBox", "preferredDeliveryMethod",
                  "registeredAddress", "searchGuide", "seeAlso",
                  "st", "street", "telephoneNumber",
                  "teletexTerminalIdentifier", "telexNumber",
                  "thumbnailLogo", "uPNSuffixes",
                  "userPassword", "x121Address"],
            possSuperiors: ["domainDNS", "organizationalUnit"],
            description: "A container for storing users, computers, and other account objects."),

        ObjClass("container", "top", ObjectClassType.Structural, "2.5.6.11",
            must: ["cn"],
            may: ["defaultClassStore", "schemaVersion"],
            possSuperiors: ["container", "domainDNS", "organizationalUnit",
                            "configuration", "dMD", "nTDSService"],
            description: "A generic container for other objects."),

        ObjClass("domain", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.1",
            must: ["dc"],
            may: ["creationTime", "desktopProfile", "forceLogoff",
                  "gPLink", "gPOptions", "lockOutObservationWindow",
                  "lockoutDuration", "lockoutThreshold", "managedBy",
                  "maxPwdAge", "minPwdAge", "minPwdLength",
                  "modifiedCount", "modifiedCountAtLastProm",
                  "ms-DS-MachineAccountQuota", "nTMixedDomain", "oEMInformation",
                  "pwdHistoryLength", "pwdProperties", "replTopologyStayOfExecution",
                  "uASCompat"],
            description: "Contains information about a domain."),

        ObjClass("domainDNS", "domain", ObjectClassType.Structural, "1.2.840.113556.1.5.67",
            must: [],
            may: ["managedBy", "msDS-AllowedDNSSuffixes",
                  "msDS-Behavior-Version", "msDS-EnabledFeature",
                  "msDS-USNLastSyncSuccess"],
            possSuperiors: ["domainDNS"],
            auxiliaryClasses: ["samDomain"],
            description: "DNS domain naming context."),

        ObjClass("builtinDomain", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.67",
            must: ["cn"],
            may: ["creationTime", "forceLogoff", "lockOutObservationWindow",
                  "lockoutDuration", "lockoutThreshold", "maxPwdAge",
                  "minPwdAge", "minPwdLength", "modifiedCount",
                  "modifiedCountAtLastProm", "nTMixedDomain",
                  "objectSid", "oEMInformation", "pwdHistoryLength",
                  "pwdProperties", "serverRole", "serverState", "uASCompat"],
            description: "Container for built-in security principals."),

        ObjClass("configuration", "top", ObjectClassType.Structural, "1.2.840.113556.1.3.11",
            must: ["cn"],
            may: ["gPLink", "gPOptions", "managedBy", "msDS-EnabledFeature",
                  "msDS-USNLastSyncSuccess"],
            possSuperiors: ["configuration"],
            description: "Holds the configuration information for a domain."),

        ObjClass("lostAndFound", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.intId.35",
            must: ["cn"],
            may: [],
            possSuperiors: ["domainDNS", "configuration"],
            description: "Container for orphaned objects."),

        ObjClass("foreignSecurityPrincipal", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.234",
            must: ["cn"],
            may: ["foreignIdentifier"],
            possSuperiors: ["container", "domainDNS"],
            auxiliaryClasses: ["securityPrincipal"],
            description: "A security principal from a trusted external domain."),

        ObjClass("groupPolicyContainer", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.157",
            must: ["cn"],
            may: ["displayName", "flags", "gPCFileSysPath",
                  "gPCFunctionalityVersion", "gPCMachineExtensionNames",
                  "gPCUserExtensionNames", "gPCWQLFilter",
                  "versionNumber"],
            possSuperiors: ["domainDNS"],
            description: "Group Policy container object."),

        // =====================================================================
        // STRUCTURAL CLASSES - Schema Definition
        // =====================================================================

        ObjClass("attributeSchema", "top", ObjectClassType.Structural, "1.2.840.113556.1.3.14",
            must: ["attributeID", "attributeSyntax", "cn", "isSingleValued",
                   "lDAPDisplayName", "oMSyntax"],
            may: ["attributeSecurityGUID", "classDisplayName",
                  "extendedCharsAllowed", "isDefunct", "isEphemeral",
                  "isMemberOfPartialAttributeSet", "linkID",
                  "mAPIID", "msDS-IntId", "oMObjectClass",
                  "rangeLower", "rangeUpper", "schemaFlagsEx",
                  "schemaIDGUID", "searchFlags", "systemOnly"],
            possSuperiors: ["dMD"],
            description: "Defines an attribute in the schema."),

        ObjClass("classSchema", "top", ObjectClassType.Structural, "1.2.840.113556.1.3.13",
            must: ["cn", "defaultObjectCategory", "governsID",
                   "lDAPDisplayName", "objectClassCategory", "subClassOf"],
            may: ["auxiliaryClass", "classDisplayName", "defaultHidingValue",
                  "defaultSecurityDescriptor", "isDefunct",
                  "msDS-IntId", "mustContain", "mayContain",
                  "possSuperiors", "rDNAttID", "schemaFlagsEx",
                  "schemaIDGUID", "systemAuxiliaryClass",
                  "systemFlags", "systemMayContain", "systemMustContain",
                  "systemOnly", "systemPossSuperiors"],
            possSuperiors: ["dMD"],
            description: "Defines an object class in the schema."),

        ObjClass("subSchema", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.20",
            must: [],
            may: ["attributeTypes", "dITContentRules", "extendedAttributeInfo",
                  "extendedClassInfo", "modifyTimeStamp", "objectClasses"],
            possSuperiors: ["dMD"],
            description: "Contains the published schema info for the directory."),

        ObjClass("dMD", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.54",
            must: ["cn"],
            may: ["dmdName", "msDS-IntId", "prefixMap",
                  "schemaInfo", "schemaUpdate"],
            possSuperiors: ["configuration"],
            description: "Directory Management Domain (schema container)."),

        // =====================================================================
        // STRUCTURAL CLASSES - Security & Policy
        // =====================================================================

        ObjClass("secret", "leaf", ObjectClassType.Structural, "1.2.840.113556.1.5.22",
            must: ["cn"],
            may: ["currentValue", "lastSetTime", "priorSetTime", "priorValue"],
            possSuperiors: ["domainDNS"],
            description: "Stores a secret (e.g. LSA secret)."),

        ObjClass("trustedDomain", "leaf", ObjectClassType.Structural, "1.2.840.113556.1.5.30",
            must: ["flatName", "trustPartner"],
            may: ["additionalTrustedServiceNames", "domainCrossRef",
                  "domainIdentifier", "initialAuthIncoming",
                  "initialAuthOutgoing", "msDS-EgressClaimsTransformationPolicy",
                  "msDS-IngressClaimsTransformationPolicy",
                  "msDS-SupportedEncryptionTypes", "msDS-TrustForestTrustInfo",
                  "securityIdentifier", "trustAttributes", "trustAuthIncoming",
                  "trustAuthOutgoing", "trustDirection", "trustPosixOffset",
                  "trustType"],
            possSuperiors: ["domainDNS"],
            description: "A domain trust relationship."),

        ObjClass("controlAccessRight", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.153",
            must: ["cn"],
            may: ["appliesTo", "displayName", "localizationDisplayId",
                  "rightsGuid", "validAccesses"],
            possSuperiors: ["container"],
            description: "Defines an extended access control right."),

        ObjClass("msDS-PasswordSettings", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.270",
            must: ["cn", "msDS-LockoutDuration", "msDS-LockoutObservationWindow",
                   "msDS-LockoutThreshold", "msDS-MaximumPasswordAge",
                   "msDS-MinimumPasswordAge", "msDS-MinimumPasswordLength",
                   "msDS-PasswordComplexityEnabled",
                   "msDS-PasswordHistoryLength",
                   "msDS-PasswordReversibleEncryptionEnabled",
                   "msDS-PasswordSettingsPrecedence"],
            may: ["msDS-PSOAppliesTo"],
            possSuperiors: ["msDS-PasswordSettingsContainer"],
            description: "Fine-Grained Password Policy object."),

        ObjClass("msDS-PasswordSettingsContainer", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.271",
            must: ["cn"],
            may: [],
            possSuperiors: ["domainDNS"],
            description: "Container for fine-grained password policy objects."),

        ObjClass("msDS-QuotaContainer", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.231",
            must: ["cn"],
            may: ["msDS-DefaultQuota", "msDS-QuotaAmount",
                  "msDS-QuotaTrustee", "msDS-TombstoneQuotaFactor"],
            possSuperiors: ["domainDNS", "configuration"],
            description: "Container for directory quota objects."),

        ObjClass("msDS-QuotaControl", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.232",
            must: ["cn", "msDS-QuotaAmount", "msDS-QuotaTrustee"],
            may: [],
            possSuperiors: ["msDS-QuotaContainer"],
            description: "A directory quota control object."),

        // =====================================================================
        // STRUCTURAL CLASSES - Site Topology & Replication
        // =====================================================================

        ObjClass("site", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.26",
            must: ["cn"],
            may: ["gPLink", "gPOptions", "location", "managedBy",
                  "msDS-BridgeHeadServersUsed", "notificationList",
                  "schedulePriorities"],
            possSuperiors: ["sitesContainer"],
            description: "An Active Directory site."),

        ObjClass("subnet", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.28",
            must: ["cn"],
            may: ["location", "siteObject"],
            possSuperiors: ["subnetContainer"],
            description: "Represents an IP subnet associated with a site."),

        ObjClass("subnetContainer", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.164",
            must: ["cn"],
            may: [],
            possSuperiors: ["configuration"],
            description: "Container for subnet objects."),

        ObjClass("siteLink", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.147",
            must: ["cn", "siteList"],
            may: ["cost", "options", "replInterval", "schedule"],
            possSuperiors: ["interSiteTransport"],
            description: "A site link defining replication connectivity."),

        ObjClass("siteLinkBridge", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.148",
            must: ["cn", "siteLinkList"],
            may: [],
            possSuperiors: ["interSiteTransport"],
            description: "A site link bridge for transitive replication."),

        ObjClass("sitesContainer", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.165",
            must: ["cn"],
            may: [],
            possSuperiors: ["configuration"],
            description: "Container for site objects."),

        ObjClass("interSiteTransport", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.146",
            must: ["cn"],
            may: ["options", "transportAddressAttribute",
                  "transportDLLName"],
            possSuperiors: ["interSiteTransportContainer"],
            description: "Defines a replication transport (IP/SMTP)."),

        ObjClass("interSiteTransportContainer", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.161",
            must: ["cn"],
            may: [],
            possSuperiors: ["configuration"],
            description: "Container for inter-site transport objects."),

        ObjClass("server", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.23",
            must: ["cn"],
            may: ["bridgeheadTransportList", "dNSHostName",
                  "mailAddress", "managedBy", "serverReference",
                  "msDS-HasDomainNCs", "msDS-HasInstantiatedNCs",
                  "msDS-isGC", "msDS-isRODC", "msDS-SiteName"],
            possSuperiors: ["serversContainer"],
            description: "Represents a server in a site."),

        ObjClass("serversContainer", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.27",
            must: ["cn"],
            may: [],
            possSuperiors: ["site"],
            description: "Container for server objects in a site."),

        ObjClass("nTDSConnection", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.7000.47",
            must: ["cn"],
            may: ["enabledConnection", "fromServer", "generatedConnection",
                  "mS-DS-ReplicatesNCReason", "options", "schedule",
                  "transportType"],
            possSuperiors: ["nTDSDSA"],
            description: "A replication connection between directory service agents."),

        ObjClass("nTDSDSA", "applicationSettings", ObjectClassType.Structural, "1.2.840.113556.1.5.7000.47",
            must: [],
            may: ["dMDLocation", "hasMasterNCs", "hasPartialReplicaNCs",
                  "invocationId", "lastBackupRestorationTime",
                  "msDS-Behavior-Version", "msDS-EnabledFeature",
                  "msDS-HasDomainNCs", "msDS-HasFullReplicaNCs",
                  "msDS-HasInstantiatedNCs",
                  "msDS-hasMasterNCs", "msDS-isGC", "msDS-isRODC",
                  "msDS-NeverRevealGroup", "msDS-ReplicationEpoch",
                  "msDS-RetiredReplNCSignatures",
                  "msDS-RevealedList", "msDS-RevealOnDemandGroup",
                  "options", "queryPolicyObject", "retiredReplDSASignatures",
                  "serverReferenceBL", "systemFlags"],
            possSuperiors: ["server"],
            description: "Directory Service Agent settings on a server."),

        ObjClass("nTDSDSARO", "nTDSDSA", ObjectClassType.Structural, "1.2.840.113556.1.5.7000.48",
            must: [],
            may: ["managedBy", "msDS-KrbTgtLink",
                  "msDS-RevealedUsers"],
            possSuperiors: ["server"],
            description: "Read-only domain controller DSA settings."),

        ObjClass("nTDSService", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.7000.46",
            must: ["cn"],
            may: ["dSHeuristics", "garbageCollPeriod",
                  "msDS-Other-Settings", "replTopologyStayOfExecution",
                  "sPNMappings", "tombstoneLifetime"],
            possSuperiors: ["configuration"],
            description: "Directory service settings."),

        ObjClass("nTDSSiteSettings", "applicationSettings", ObjectClassType.Structural, "1.2.840.113556.1.5.7000.49",
            must: [],
            may: ["interSiteTopologyFailover", "interSiteTopologyGenerator",
                  "interSiteTopologyRenew", "managedBy",
                  "msDS-PreferredGCSite", "options", "queryPolicyObject",
                  "schedule"],
            possSuperiors: ["site"],
            description: "Site-level directory service settings."),

        // =====================================================================
        // STRUCTURAL CLASSES - RID Management
        // =====================================================================

        ObjClass("rIDManager", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.7000.51",
            must: ["cn"],
            may: ["fSMORoleOwner", "rIDAvailablePool"],
            possSuperiors: ["domainDNS"],
            description: "Manages the allocation of RID pools."),

        ObjClass("rIDSet", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.7000.52",
            must: ["cn"],
            may: ["rIDAllocationPool", "rIDNextRID",
                  "rIDPreviousAllocationPool", "rIDUsedPool"],
            possSuperiors: ["computer"],
            description: "Stores the RID pool allocated to a DC."),

        // =====================================================================
        // STRUCTURAL CLASSES - Cross Reference & Naming
        // =====================================================================

        ObjClass("crossRef", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.16",
            must: ["cn", "nCName"],
            may: ["dNSRoot", "dnsRecord", "enabled",
                  "msDS-Behavior-Version", "msDS-DnsRootAlias",
                  "msDS-EnabledFeature", "msDS-NC-Replica-Locations",
                  "msDS-NC-RO-Replica-Locations", "msDS-Replication-Notify-First-DSA-Delay",
                  "msDS-Replication-Notify-Subsequent-DSA-Delay",
                  "msDS-SDReferenceDomain", "msDS-SiteName",
                  "nETBIOSName", "systemFlags", "trustParent"],
            possSuperiors: ["crossRefContainer"],
            description: "A naming context cross-reference."),

        ObjClass("crossRefContainer", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.250",
            must: ["cn"],
            may: ["msDS-Behavior-Version", "msDS-EnabledFeature",
                  "msDS-SPNSuffixes", "msDS-UpdateScript",
                  "uPNSuffixes"],
            possSuperiors: ["configuration"],
            description: "Container for cross-reference objects."),

        // =====================================================================
        // STRUCTURAL CLASSES - DNS
        // =====================================================================

        ObjClass("dnsZone", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.99",
            must: ["cn"],
            may: ["dnsProperty", "managedBy"],
            possSuperiors: ["domainDNS", "dnsZone", "container"],
            description: "Represents a DNS zone stored in AD."),

        ObjClass("dnsNode", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.100",
            must: [],
            may: ["dc", "dnsRecord", "dNSTombstoned", "dnsProperty"],
            possSuperiors: ["dnsZone", "domainDNS"],
            description: "A DNS resource record node."),

        // =====================================================================
        // STRUCTURAL CLASSES - Certificate Services
        // =====================================================================

        ObjClass("certificationAuthority", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.19",
            must: ["cn"],
            may: ["authorityRevocationList", "cACertificate",
                  "cACertificateDN", "cAConnect", "cAUsages",
                  "cAWEBURL", "certificateRevocationList",
                  "certificateTemplates", "crossCertificatePair",
                  "currentParentCA", "deltaRevocationList",
                  "dNSHostName", "domainID", "domainPolicyObject",
                  "enrollmentProviders", "parentCA",
                  "parentCACertificateChain", "pendingCACertificates",
                  "pendingParentCA", "previousCACertificates",
                  "previousParentCA", "searchGuide",
                  "signatureAlgorithms", "supportedApplicationContext",
                  "teletexTerminalIdentifier"],
            possSuperiors: ["container"],
            description: "A certification authority object."),

        ObjClass("pKICertificateTemplate", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.173",
            must: ["cn"],
            may: ["displayName", "flags", "msPKI-Cert-Template-OID",
                  "msPKI-Certificate-Application-Policy",
                  "msPKI-Certificate-Name-Flag",
                  "msPKI-Certificate-Policy",
                  "msPKI-Enrollment-Flag", "msPKI-Minimal-Key-Size",
                  "msPKI-OID-Attribute", "msPKI-OID-CPS",
                  "msPKI-Private-Key-Flag", "msPKI-RA-Application-Policies",
                  "msPKI-RA-Policies", "msPKI-RA-Signature",
                  "msPKI-Supersede-Templates", "msPKI-Template-Minor-Revision",
                  "msPKI-Template-Schema-Version",
                  "pKICriticalExtensions", "pKIDefaultCSPs",
                  "pKIDefaultKeySpec", "pKIEnrollmentAccess",
                  "pKIExpirationPeriod", "pKIExtendedKeyUsage",
                  "pKIKeyUsage", "pKIMaxIssuingDepth",
                  "pKIOverlapPeriod", "revision"],
            possSuperiors: ["container"],
            description: "A PKI certificate template."),

        ObjClass("pKIEnrollmentService", "connectionPoint", ObjectClassType.Structural, "1.2.840.113556.1.5.178",
            must: ["cn"],
            may: ["cACertificate", "cACertificateDN",
                  "certificateTemplates", "dNSHostName",
                  "enrollmentProviders",
                  "msPKI-Enrollment-Servers", "msPKI-Site-Name",
                  "signatureAlgorithms"],
            possSuperiors: ["container"],
            description: "A PKI enrollment service."),

        ObjClass("msPKI-Enterprise-Oid", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.196",
            must: ["cn"],
            may: ["displayName", "flags", "msPKI-Cert-Template-OID",
                  "msPKI-OID-Attribute", "msPKI-OID-CPS",
                  "msPKI-OID-LocalizedName", "msPKI-OID-User-Notice"],
            possSuperiors: ["container"],
            description: "Enterprise OID for PKI."),

        ObjClass("msPKI-Key-Recovery-Agent", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.195",
            must: ["cn"],
            may: ["userCertificate"],
            possSuperiors: ["container"],
            description: "A key recovery agent."),

        ObjClass("msPKI-PrivateKeyRecoveryAgent", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.195",
            must: ["cn"],
            may: ["userCertificate"],
            possSuperiors: ["container"],
            description: "Private key recovery agent."),

        // =====================================================================
        // STRUCTURAL CLASSES - ACS (Admission Control Service)
        // =====================================================================

        ObjClass("aCSPolicy", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.112",
            must: ["cn"],
            may: ["aCSAggregateTokenRatePerUser", "aCSDirection",
                  "aCSMaxAggregatePeakRatePerUser",
                  "aCSMaxDurationPerFlow", "aCSMaxNoOfAccountFiles",
                  "aCSMaxNoOfLogFiles", "aCSMaxPeakBandwidth",
                  "aCSMaxPeakBandwidthPerFlow", "aCSMaxSizeOfRSVPAccountFile",
                  "aCSMaxSizeOfRSVPLogFile", "aCSMaxTokenBucketPerFlow",
                  "aCSMaxTokenRatePerFlow", "aCSMaximumSDUSize",
                  "aCSMinimumDelayVariation", "aCSMinimumLatency",
                  "aCSMinimumPolicedSize", "aCSNonReservedMaxSDUSize",
                  "aCSNonReservedMinPolicedSize", "aCSNonReservedPeakRate",
                  "aCSNonReservedTokenSize", "aCSNonReservedTxLimit",
                  "aCSNonReservedTxSize", "aCSPermissionBits",
                  "aCSPriority", "aCSServiceType", "aCSTimeOfDay"],
            possSuperiors: ["aCSSubnet"],
            description: "QoS admission control policy."),

        ObjClass("aCSResourceLimits", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.115",
            must: ["cn"],
            may: ["aCSAllocableRSVPBandwidth", "aCSMaxAggregatePeakRatePerUser",
                  "aCSMaxNoOfAccountFiles", "aCSMaxNoOfLogFiles",
                  "aCSMaxPeakBandwidth", "aCSMaxSizeOfRSVPAccountFile",
                  "aCSMaxSizeOfRSVPLogFile"],
            possSuperiors: ["aCSSubnet"],
            description: "ACS resource limits."),

        ObjClass("aCSSubnet", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.114",
            must: ["cn"],
            may: ["aCSEnableACSService", "aCSEventLogLevel",
                  "aCSIdentityName", "aCSServerList"],
            possSuperiors: ["container"],
            description: "ACS subnet service."),

        // =====================================================================
        // STRUCTURAL CLASSES - Address Book & Exchange
        // =====================================================================

        ObjClass("addressBookContainer", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.144",
            must: ["cn", "displayName"],
            may: ["purportedSearch"],
            possSuperiors: ["addressBookContainer", "container"],
            description: "Address book container for Exchange."),

        ObjClass("addressTemplate", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.14",
            must: ["cn"],
            may: ["addressEntryDisplayTable", "addressEntryDisplayTableMSDOS",
                  "addressSyntax", "addressType",
                  "helpData16", "helpData32", "helpFileName",
                  "perMsgDialogDisplayTable", "perRecipDialogDisplayTable",
                  "proxyGenerationEnabled"],
            possSuperiors: ["container"],
            description: "Address template for messaging."),

        ObjClass("msExchConfigurationContainer", "container", ObjectClassType.Structural, "1.2.840.113556.1.5.222",
            must: [],
            may: [],
            possSuperiors: ["configuration"],
            description: "Exchange configuration container."),

        // =====================================================================
        // STRUCTURAL CLASSES - Application & Class Store
        // =====================================================================

        ObjClass("applicationEntity", "top", ObjectClassType.Structural, "2.5.6.12",
            must: ["cn", "presentationAddress"],
            may: ["l", "o", "ou", "seeAlso", "supportedApplicationContext"],
            possSuperiors: ["container", "organizationalUnit"],
            description: "Represents an OSI application entity."),

        ObjClass("applicationProcess", "top", ObjectClassType.Structural, "2.5.6.11",
            must: ["cn"],
            may: ["l", "ou", "seeAlso"],
            possSuperiors: ["container", "organizationalUnit", "domainDNS"],
            description: "Represents an OSI application process."),

        ObjClass("applicationSiteSettings", "applicationSettings", ObjectClassType.Structural, "1.2.840.113556.1.5.152",
            must: [],
            may: ["notificationList"],
            possSuperiors: ["site"],
            description: "Application-specific site settings."),

        ObjClass("applicationVersion", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.108",
            must: ["cn"],
            may: ["appSchemaVersion"],
            possSuperiors: ["container"],
            description: "Application version information."),

        ObjClass("categoryRegistration", "leaf", ObjectClassType.Structural, "1.2.840.113556.1.5.20",
            must: ["cn"],
            may: ["categoryId", "localizedDescription",
                  "managedBy"],
            possSuperiors: ["classStore"],
            description: "A component category registration."),

        ObjClass("classRegistration", "leaf", ObjectClassType.Structural, "1.2.840.113556.1.5.20",
            must: ["cn"],
            may: ["cOMCLSID", "cOMInterfaceID", "cOMOtherProgId",
                  "cOMProgID", "cOMTreatAsClassId", "cOMTypelibId",
                  "cOMUniqueLIBID", "implementedCategories",
                  "requiredCategories"],
            possSuperiors: ["classStore", "container"],
            description: "A COM class registration."),

        ObjClass("classStore", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.110",
            must: ["cn"],
            may: ["appSchemaVersion", "lastUpdateSequence",
                  "nextLevelStore", "versionNumber"],
            possSuperiors: ["container", "organizationalUnit", "domainDNS"],
            description: "Software installation class store."),

        ObjClass("packageRegistration", "leaf", ObjectClassType.Structural, "1.2.840.113556.1.5.109",
            must: ["cn"],
            may: ["canUpgradeScript", "categories", "cOMClassID",
                  "cOMTypelibId", "fileExtPriority",
                  "iconPath", "installUiLevel",
                  "lastUpdateSequence", "localeID",
                  "machineArchitecture", "managedBy",
                  "msiFileList", "msiScript", "msiScriptName",
                  "msiScriptPath", "msiScriptSize",
                  "packageFlags", "packageName", "packageType",
                  "productCode", "setupCommand",
                  "upgradeProductCode", "url",
                  "vendor", "versionNumberHi", "versionNumberLo"],
            possSuperiors: ["classStore"],
            description: "A software package registration."),

        ObjClass("typeLibrary", "leaf", ObjectClassType.Structural, "1.2.840.113556.1.5.20",
            must: ["cn"],
            may: ["cOMUniqueLIBID"],
            possSuperiors: ["classStore", "container"],
            description: "A COM type library."),

        // =====================================================================
        // STRUCTURAL CLASSES - COM Partition
        // =====================================================================

        ObjClass("msCOM-Partition", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.217",
            must: ["cn"],
            may: ["msCOM-DefaultPartitionLink", "msCOM-ObjectId",
                  "msCOM-PartitionLink", "msCOM-UserPartitionSetLink"],
            possSuperiors: ["container"],
            description: "A COM+ partition."),

        ObjClass("msCOM-PartitionSet", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.218",
            must: ["cn"],
            may: ["msCOM-DefaultPartitionLink", "msCOM-ObjectId",
                  "msCOM-PartitionLink", "msCOM-PartitionSetLink",
                  "msCOM-UserPartitionSetLink"],
            possSuperiors: ["container"],
            description: "A COM+ partition set."),

        ObjClass("comConnectionPoint", "connectionPoint", ObjectClassType.Structural, "1.2.840.113556.1.5.94",
            must: ["cn"],
            may: ["msCOM-ObjectId"],
            possSuperiors: ["container"],
            description: "A COM connection point."),

        // =====================================================================
        // STRUCTURAL CLASSES - DHCP
        // =====================================================================

        ObjClass("dHCPClass", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.175",
            must: ["cn"],
            may: ["dHCPClasses", "dHCPFlags", "dHCPIdentification",
                  "dHCPMask", "dHCPMaxKey", "dHCPObjDescription",
                  "dHCPObjName", "dHCPProperties", "dHCPRanges",
                  "dHCPReservations", "dHCPServers", "dHCPSites",
                  "dHCPState", "dHCPSubnets", "dHCPType",
                  "dHCPUniqueKey", "dHCPUpdateTime",
                  "mscopeid", "mscope-id", "optionDescription",
                  "optionsLocation", "superScopeDescription",
                  "superScopes"],
            possSuperiors: ["container", "dHCPClass"],
            description: "A DHCP class object."),

        // =====================================================================
        // STRUCTURAL CLASSES - DFS
        // =====================================================================

        ObjClass("dfsConfiguration", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.176",
            must: ["cn"],
            may: [],
            possSuperiors: ["domainDNS"],
            description: "DFS configuration container."),

        ObjClass("fTDfs", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.43",
            must: ["cn"],
            may: ["pKT", "pKTGuid", "remoteServerName"],
            possSuperiors: ["dfsConfiguration"],
            description: "Fault-tolerant DFS root."),

        ObjClass("msDFS-DeletedLinkv2", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.266",
            must: ["cn"],
            may: ["msDFS-GenerationGUIDv2", "msDFS-LastModifiedv2",
                  "msDFS-LinkIdentityGUIDv2", "msDFS-LinkPathv2",
                  "msDFS-NamespaceLinkv2", "msDFS-TargetListv2",
                  "msDFS-Ttlv2"],
            possSuperiors: ["msDFS-Namespacev2"],
            description: "Deleted DFS link v2."),

        ObjClass("msDFS-Linkv2", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.265",
            must: ["cn"],
            may: ["msDFS-CommentV2", "msDFS-GenerationGUIDv2",
                  "msDFS-LastModifiedv2", "msDFS-LinkIdentityGUIDv2",
                  "msDFS-LinkPathv2", "msDFS-NamespaceLinkv2",
                  "msDFS-Propertiesv2", "msDFS-TargetListv2",
                  "msDFS-Ttlv2"],
            possSuperiors: ["msDFS-Namespacev2"],
            description: "DFS link v2."),

        ObjClass("msDFS-NamespaceAnchor", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.268",
            must: ["cn"],
            may: ["msDFS-SchemaVersion"],
            possSuperiors: ["domainDNS"],
            description: "DFS namespace anchor."),

        ObjClass("msDFS-Namespacev2", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.264",
            must: ["cn"],
            may: ["msDFS-CommentV2", "msDFS-GenerationGUIDv2",
                  "msDFS-LastModifiedv2", "msDFS-Propertiesv2",
                  "msDFS-SchemaMajorVersion", "msDFS-SchemaMinorVersion",
                  "msDFS-TargetListv2", "msDFS-Ttlv2"],
            possSuperiors: ["msDFS-NamespaceAnchor"],
            description: "DFS namespace v2."),

        // =====================================================================
        // STRUCTURAL CLASSES - DFSR (DFS Replication)
        // =====================================================================

        ObjClass("msDFSR-Connection", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.241",
            must: ["cn"],
            may: ["msDFSR-Enabled", "msDFSR-Keywords", "msDFSR-Options",
                  "msDFSR-RdcEnabled", "msDFSR-RdcMinFileSizeInKb",
                  "msDFSR-Schedule", "fromServer"],
            possSuperiors: ["msDFSR-Member"],
            description: "DFSR connection."),

        ObjClass("msDFSR-Content", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.244",
            must: ["cn"],
            may: [],
            possSuperiors: ["msDFSR-LocalSettings"],
            description: "DFSR content container."),

        ObjClass("msDFSR-ContentSet", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.243",
            must: ["cn"],
            may: ["msDFSR-ConflictPath", "msDFSR-DirectoryFilter",
                  "msDFSR-Enabled", "msDFSR-FileFilter",
                  "msDFSR-Flags", "msDFSR-Options",
                  "msDFSR-RootPath", "msDFSR-RootSizeInMb",
                  "msDFSR-StagingPath", "msDFSR-StagingSizeInMb"],
            possSuperiors: ["msDFSR-ReplicationGroup"],
            description: "DFSR content set."),

        ObjClass("msDFSR-GlobalSettings", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.238",
            must: ["cn"],
            may: [],
            possSuperiors: ["container", "domainDNS"],
            description: "DFSR global settings container."),

        ObjClass("msDFSR-LocalSettings", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.245",
            must: ["cn"],
            may: ["msDFSR-Flags", "msDFSR-Version"],
            possSuperiors: ["computer"],
            description: "DFSR local settings."),

        ObjClass("msDFSR-Member", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.240",
            must: ["cn"],
            may: ["msDFSR-ComputerReference", "msDFSR-Keywords",
                  "msDFSR-Options", "serverReference"],
            possSuperiors: ["msDFSR-ReplicationGroup"],
            description: "DFSR member."),

        ObjClass("msDFSR-ReplicationGroup", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.239",
            must: ["cn"],
            may: ["msDFSR-ConflictSizeInMb", "msDFSR-DefaultCompressionExclusionFilter",
                  "msDFSR-DeletedSizeInMb", "msDFSR-DirectoryFilter",
                  "msDFSR-FileFilter", "msDFSR-Flags",
                  "msDFSR-OnDemandExclusionDirectoryFilter",
                  "msDFSR-OnDemandExclusionFileFilter",
                  "msDFSR-Options", "msDFSR-Schedule",
                  "msDFSR-TombstoneExpiryInMin"],
            possSuperiors: ["msDFSR-GlobalSettings"],
            description: "DFSR replication group."),

        ObjClass("msDFSR-Subscriber", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.246",
            must: ["cn"],
            may: ["msDFSR-MemberReference", "msDFSR-ReplicationGroupGuid"],
            possSuperiors: ["msDFSR-Content"],
            description: "DFSR subscriber."),

        ObjClass("msDFSR-Subscription", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.247",
            must: ["cn"],
            may: ["msDFSR-CachePolicy", "msDFSR-ConflictPath",
                  "msDFSR-ContentSetGuid", "msDFSR-Enabled",
                  "msDFSR-Extensions", "msDFSR-Flags",
                  "msDFSR-MaxAgeInCacheInMin", "msDFSR-MinDurationCacheInMin",
                  "msDFSR-Options", "msDFSR-ReadOnly",
                  "msDFSR-RootFence", "msDFSR-RootPath",
                  "msDFSR-RootSizeInMb",
                  "msDFSR-StagingPath", "msDFSR-StagingSizeInMb"],
            possSuperiors: ["msDFSR-Subscriber"],
            description: "DFSR subscription."),

        ObjClass("msDFSR-Topology", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.242",
            must: ["cn"],
            may: ["msDFSR-Flags", "msDFSR-Options"],
            possSuperiors: ["msDFSR-ReplicationGroup"],
            description: "DFSR topology."),

        // =====================================================================
        // STRUCTURAL CLASSES - FRS (File Replication Service)
        // =====================================================================

        ObjClass("nTFRSMember", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.118",
            must: ["cn"],
            may: ["fRSComputerReference", "fRSControlDataCreation",
                  "fRSControlInboundBacklog", "fRSControlOutboundBacklog",
                  "fRSExtensions", "fRSFlags",
                  "fRSPartnerAuthLevel", "fRSRootSecurity",
                  "fRSServiceCommand", "fRSUpdateTimeout",
                  "serverReference"],
            possSuperiors: ["nTFRSReplicaSet"],
            description: "FRS member."),

        ObjClass("nTFRSReplicaSet", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.119",
            must: ["cn"],
            may: ["fRSDSPoll", "fRSExtensions", "fRSFileFilter",
                  "fRSFlags", "fRSLevelLimit", "fRSPartnerAuthLevel",
                  "fRSPrimaryMember", "fRSReplicaSetGUID",
                  "fRSReplicaSetType", "fRSRootPath",
                  "fRSServiceCommand", "fRSStagingPath",
                  "fRSVersionGUID", "managedBy", "schedule"],
            possSuperiors: ["nTFRSSettings", "container"],
            description: "FRS replica set."),

        ObjClass("nTFRSSettings", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.116",
            must: ["cn"],
            may: ["fRSExtensions"],
            possSuperiors: ["domainDNS", "server", "computer"],
            description: "FRS settings."),

        ObjClass("nTFRSSubscriber", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.120",
            must: ["cn"],
            may: ["fRSExtensions", "fRSFaultCondition",
                  "fRSMemberReference", "fRSReplicaSetGUID",
                  "fRSRootPath", "fRSServiceCommand",
                  "fRSStagingPath", "fRSUpdateTimeout",
                  "schedule", "serverReference"],
            possSuperiors: ["nTFRSSubscriptions"],
            description: "FRS subscriber."),

        ObjClass("nTFRSSubscriptions", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.121",
            must: ["cn"],
            may: ["fRSExtensions", "fRSVersion", "fRSWorkingPath"],
            possSuperiors: ["computer", "server", "domainDNS"],
            description: "FRS subscriptions."),

        // =====================================================================
        // STRUCTURAL CLASSES - Display & UI
        // =====================================================================

        ObjClass("displaySpecifier", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.84",
            must: ["cn"],
            may: ["adminContextMenu", "adminMultiselectPropertyPages",
                  "adminPropertyPages", "attributeDisplayNames",
                  "classDisplayName", "contextMenu",
                  "createDialog", "createWizardExt",
                  "extraColumns", "iconPath",
                  "queryFilter", "scopeFlags",
                  "shellContextMenu", "shellPropertyPages",
                  "treatAsLeaf"],
            possSuperiors: ["container"],
            description: "Localized display information for a class."),

        ObjClass("displayTemplate", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.17",
            must: ["cn"],
            may: ["addressEntryDisplayTable",
                  "addressEntryDisplayTableMSDOS",
                  "helpData16", "helpData32", "helpFileName",
                  "originalDisplayTable", "originalDisplayTableMSDOS"],
            possSuperiors: ["container"],
            description: "Display template for messaging."),

        ObjClass("dSUISettings", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.180",
            must: ["cn"],
            may: ["dSUIAdminMaximum", "dSUIAdminNotification",
                  "dSUIShellMaximum", "msDS-FilterContainers",
                  "msDS-Non-Security-Group-Extra-Classes"],
            possSuperiors: ["container", "domainDNS", "organizationalUnit"],
            description: "Directory service UI settings."),

        // =====================================================================
        // STRUCTURAL CLASSES - Dynamic Objects
        // =====================================================================

        ObjClass("dynamicObject", "top", ObjectClassType.Structural, "1.3.6.1.4.1.1466.101.119",
            must: [],
            may: ["entryTTL", "msDS-Entry-Time-To-Die"],
            description: "A dynamic object with a time-to-live."),

        // =====================================================================
        // STRUCTURAL CLASSES - File Link Tracking
        // =====================================================================

        ObjClass("fileLinkTracking", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.42",
            must: ["cn"],
            may: [],
            possSuperiors: ["domainDNS"],
            description: "File link tracking configuration."),

        ObjClass("fileLinkTrackingEntry", "leaf", ObjectClassType.Structural, "1.2.840.113556.1.5.44",
            must: ["cn"],
            may: [],
            possSuperiors: ["fileLinkTracking"],
            description: "File link tracking entry."),

        ObjClass("linkTrackObjectMoveTable", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.45",
            must: ["cn"],
            may: [],
            possSuperiors: ["domainDNS"],
            description: "Link tracking object move table."),

        ObjClass("linkTrackOMTEntry", "leaf", ObjectClassType.Structural, "1.2.840.113556.1.5.46",
            must: ["cn"],
            may: [],
            possSuperiors: ["linkTrackObjectMoveTable"],
            description: "Link tracking OMT entry."),

        ObjClass("linkTrackVolEntry", "leaf", ObjectClassType.Structural, "1.2.840.113556.1.5.48",
            must: ["cn"],
            may: [],
            possSuperiors: ["linkTrackVolumeTable"],
            description: "Link tracking volume entry."),

        ObjClass("linkTrackVolumeTable", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.47",
            must: ["cn"],
            may: [],
            possSuperiors: ["domainDNS"],
            description: "Link tracking volume table."),

        // =====================================================================
        // STRUCTURAL CLASSES - IPSec
        // =====================================================================

        ObjClass("ipsecBase", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.156",
            must: ["cn", "ipsecID"],
            may: ["ipsecDataType", "ipsecData", "ipsecName",
                  "ipsecOwnersReference"],
            possSuperiors: ["container"],
            description: "Base class for IPSec policy objects."),

        ObjClass("ipsecBundle", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.156",
            must: ["cn"],
            may: ["ipsecData", "ipsecDataType", "ipsecID",
                  "ipsecName", "ipsecNegotiationPolicyReference",
                  "ipsecOwnersReference"],
            possSuperiors: ["container"],
            description: "IPSec bundle."),

        ObjClass("ipsecFilter", "ipsecBase", ObjectClassType.Structural, "1.2.840.113556.1.5.156",
            must: [],
            may: [],
            possSuperiors: ["container"],
            description: "IPSec filter."),

        ObjClass("ipsecISAKMPPolicy", "ipsecBase", ObjectClassType.Structural, "1.2.840.113556.1.5.156",
            must: [],
            may: [],
            possSuperiors: ["container"],
            description: "IPSec ISAKMP policy."),

        ObjClass("ipsecNFA", "ipsecBase", ObjectClassType.Structural, "1.2.840.113556.1.5.156",
            must: [],
            may: ["ipsecFilterReference", "ipsecNegotiationPolicyReference"],
            possSuperiors: ["container"],
            description: "IPSec NFA (negotiation filter action)."),

        ObjClass("ipsecNegotiationPolicy", "ipsecBase", ObjectClassType.Structural, "1.2.840.113556.1.5.156",
            must: [],
            may: [],
            possSuperiors: ["container"],
            description: "IPSec negotiation policy."),

        ObjClass("ipsecPolicy", "ipsecBase", ObjectClassType.Structural, "1.2.840.113556.1.5.156",
            must: [],
            may: ["ipsecISAKMPReference", "ipsecNFAReference"],
            possSuperiors: ["container", "domainDNS", "organizationalUnit"],
            description: "IPSec policy."),

        // =====================================================================
        // STRUCTURAL CLASSES - X.500 / Directory Standard
        // =====================================================================

        ObjClass("country", "top", ObjectClassType.Structural, "2.5.6.2",
            must: ["c"],
            may: ["co", "searchGuide"],
            possSuperiors: ["configuration", "domainDNS"],
            description: "Represents a country."),

        ObjClass("friendlyCountry", "country", ObjectClassType.Structural, "1.2.840.113556.1.5.3",
            must: ["co"],
            may: [],
            possSuperiors: ["configuration", "domainDNS"],
            description: "A country with a friendly name."),

        ObjClass("locality", "top", ObjectClassType.Structural, "2.5.6.3",
            must: [],
            may: ["l", "searchGuide", "seeAlso", "st", "street"],
            possSuperiors: ["container", "domainDNS", "organizationalUnit",
                            "country", "locality"],
            description: "Represents a geographical locality."),

        ObjClass("organization", "top", ObjectClassType.Structural, "2.5.6.4",
            must: ["o"],
            may: ["businessCategory", "destinationIndicator",
                  "facsimileTelephoneNumber", "internationalISDNNumber",
                  "l", "physicalDeliveryOfficeName", "postalAddress",
                  "postalCode", "postOfficeBox", "preferredDeliveryMethod",
                  "registeredAddress", "searchGuide", "seeAlso",
                  "st", "street", "telephoneNumber",
                  "teletexTerminalIdentifier", "telexNumber",
                  "userPassword", "x121Address"],
            possSuperiors: ["container", "domainDNS", "organizationalUnit",
                            "country", "locality"],
            description: "Represents an organization."),

        ObjClass("organizationalRole", "top", ObjectClassType.Structural, "2.5.6.8",
            must: ["cn"],
            may: ["description", "destinationIndicator",
                  "facsimileTelephoneNumber", "internationalISDNNumber",
                  "l", "ou", "physicalDeliveryOfficeName", "postalAddress",
                  "postalCode", "postOfficeBox", "preferredDeliveryMethod",
                  "registeredAddress", "roleOccupant",
                  "seeAlso", "st", "street", "telephoneNumber",
                  "teletexTerminalIdentifier", "telexNumber",
                  "x121Address"],
            possSuperiors: ["container", "organizationalUnit", "domainDNS"],
            description: "An organizational role filled by a person."),

        ObjClass("residentialPerson", "person", ObjectClassType.Structural, "2.5.6.10",
            must: ["l"],
            may: ["businessCategory", "destinationIndicator",
                  "facsimileTelephoneNumber", "internationalISDNNumber",
                  "physicalDeliveryOfficeName", "postalAddress",
                  "postalCode", "postOfficeBox",
                  "preferredDeliveryMethod", "registeredAddress",
                  "st", "street", "teletexTerminalIdentifier",
                  "telexNumber", "x121Address"],
            description: "Represents a person who resides at an address."),

        ObjClass("groupOfNames", "top", ObjectClassType.Structural, "2.5.6.9",
            must: ["cn"],
            may: ["businessCategory", "description", "member",
                  "o", "ou", "owner", "seeAlso"],
            possSuperiors: ["container", "organizationalUnit", "domainDNS"],
            description: "A named group of object entries."),

        ObjClass("groupOfUniqueNames", "top", ObjectClassType.Structural, "2.5.6.17",
            must: ["cn"],
            may: ["businessCategory", "description", "o", "ou",
                  "owner", "seeAlso", "uniqueMember"],
            possSuperiors: ["container", "organizationalUnit", "domainDNS"],
            description: "A named group with unique member names."),

        ObjClass("simpleSecurityObject", "top", ObjectClassType.Structural, "0.9.2342.19200300.100.4.19",
            must: [],
            may: ["userPassword"],
            description: "A simple object with a password."),

        ObjClass("documentSeries", "top", ObjectClassType.Structural, "0.9.2342.19200300.100.4.9",
            must: ["cn"],
            may: ["description", "l", "o", "ou", "seeAlso",
                  "telephoneNumber"],
            possSuperiors: ["container", "organizationalUnit", "domainDNS"],
            description: "A series of documents."),

        // =====================================================================
        // STRUCTURAL CLASSES - UNIX Integration (NIS/RFC2307)
        // =====================================================================

        ObjClass("oncRpc", "top", ObjectClassType.Structural, "1.3.6.1.1.1.2.5",
            must: ["cn", "oncRpcNumber"],
            may: ["description"],
            possSuperiors: ["container", "organizationalUnit", "domainDNS"],
            description: "ONC RPC mapping."),

        ObjClass("ipService", "top", ObjectClassType.Structural, "1.3.6.1.1.1.2.3",
            must: ["cn", "ipServicePort", "ipServiceProtocol"],
            may: ["description"],
            possSuperiors: ["container", "organizationalUnit", "domainDNS"],
            description: "IP service mapping."),

        ObjClass("ipProtocol", "top", ObjectClassType.Structural, "1.3.6.1.1.1.2.4",
            must: ["cn", "ipProtocolNumber"],
            may: ["description"],
            possSuperiors: ["container", "organizationalUnit", "domainDNS"],
            description: "IP protocol mapping."),

        ObjClass("ipNetwork", "top", ObjectClassType.Structural, "1.3.6.1.1.1.2.7",
            must: ["cn", "ipNetworkNumber"],
            may: ["description", "l", "manager"],
            possSuperiors: ["container", "organizationalUnit", "domainDNS"],
            description: "IP network mapping."),

        ObjClass("ipHost", "device", ObjectClassType.Structural, "1.3.6.1.1.1.2.6",
            must: ["cn", "ipHostNumber"],
            may: ["description", "l", "manager", "owner"],
            possSuperiors: ["container", "organizationalUnit", "domainDNS"],
            description: "IP host structural class."),

        ObjClass("nisMap", "top", ObjectClassType.Structural, "1.3.6.1.1.1.2.9",
            must: ["cn", "nisMapName"],
            may: [],
            possSuperiors: ["container", "organizationalUnit", "domainDNS"],
            description: "NIS map."),

        ObjClass("nisNetgroup", "top", ObjectClassType.Structural, "1.3.6.1.1.1.2.8",
            must: ["cn"],
            may: ["description", "memberNisNetgroup", "nisNetgroupTriple"],
            possSuperiors: ["container", "organizationalUnit", "domainDNS"],
            description: "NIS netgroup."),

        ObjClass("nisObject", "top", ObjectClassType.Structural, "1.3.6.1.1.1.2.10",
            must: ["cn", "nisMapEntry", "nisMapName"],
            may: ["description"],
            possSuperiors: ["container", "organizationalUnit", "domainDNS",
                            "nisMap"],
            description: "NIS object."),

        // =====================================================================
        // STRUCTURAL CLASSES - SFU (Services for UNIX)
        // =====================================================================

        ObjClass("msSFU30DomainInfo", "top", ObjectClassType.Structural, "1.2.840.113556.1.6.18.2.210",
            must: ["cn"],
            may: ["msSFU30KeyValues", "msSFU30MasterServerName",
                  "msSFU30MaxGidNumber", "msSFU30MaxUidNumber",
                  "msSFU30OrderNumber", "msSFU30SearchAttributes",
                  "msSFU30SearchContainer"],
            possSuperiors: ["container"],
            description: "SFU domain information."),

        ObjClass("msSFU30NetId", "top", ObjectClassType.Structural, "1.2.840.113556.1.6.18.2.211",
            must: ["cn"],
            may: ["msSFU30Name", "msSFU30NetIdKey",
                  "msSFU30NisDomain"],
            possSuperiors: ["container"],
            description: "SFU NIS network ID."),

        ObjClass("msSFU30NetworkLoginProfile", "top", ObjectClassType.Structural, "1.2.840.113556.1.6.18.2.212",
            must: ["cn"],
            may: [],
            possSuperiors: ["container"],
            description: "SFU network login profile."),

        // =====================================================================
        // STRUCTURAL CLASSES - Service Connection Points
        // =====================================================================

        ObjClass("serviceConnectionPoint", "connectionPoint", ObjectClassType.Structural, "1.2.840.113556.1.5.94",
            must: ["cn"],
            may: ["appSchemaVersion", "keywords", "managedBy",
                  "serviceBindingInformation", "serviceClassName",
                  "serviceDNSName", "serviceDNSNameType"],
            possSuperiors: ["computer", "container", "organizationalUnit",
                            "domainDNS", "server"],
            description: "A service connection point for service discovery."),

        ObjClass("serviceAdministrationPoint", "connectionPoint", ObjectClassType.Structural, "1.2.840.113556.1.5.94",
            must: ["cn"],
            may: ["keywords", "managedBy"],
            possSuperiors: ["computer", "container", "organizationalUnit",
                            "domainDNS"],
            description: "Service administration point."),

        ObjClass("serviceClass", "leaf", ObjectClassType.Structural, "1.2.840.113556.1.5.20",
            must: ["cn"],
            may: ["serviceClassID", "serviceClassInfo"],
            possSuperiors: ["container"],
            description: "A service class definition."),

        ObjClass("serviceInstance", "connectionPoint", ObjectClassType.Structural, "1.2.840.113556.1.5.94",
            must: ["cn"],
            may: ["keywords", "managedBy",
                  "serviceBindingInformation", "serviceClassName",
                  "serviceDNSName", "serviceDNSNameType"],
            possSuperiors: ["container", "computer"],
            description: "An instance of a service."),

        // =====================================================================
        // STRUCTURAL CLASSES - RPC
        // =====================================================================

        ObjClass("rpcContainer", "container", ObjectClassType.Structural, "1.2.840.113556.1.5.61",
            must: [],
            may: [],
            possSuperiors: ["container", "configuration"],
            description: "Container for RPC services."),

        ObjClass("rpcEntry", "connectionPoint", ObjectClassType.Structural, "1.2.840.113556.1.5.61",
            must: ["cn"],
            may: ["rpcNsAnnotation", "rpcNsCodeset",
                  "rpcNsObjectID"],
            possSuperiors: ["rpcContainer"],
            description: "An RPC entry."),

        ObjClass("rpcGroup", "rpcEntry", ObjectClassType.Structural, "1.2.840.113556.1.5.61",
            must: [],
            may: ["rpcNsGroup"],
            possSuperiors: ["rpcContainer"],
            description: "An RPC group."),

        ObjClass("rpcProfile", "rpcEntry", ObjectClassType.Structural, "1.2.840.113556.1.5.61",
            must: [],
            may: ["rpcNsProfileEntry"],
            possSuperiors: ["rpcContainer"],
            description: "An RPC profile."),

        ObjClass("rpcProfileElement", "connectionPoint", ObjectClassType.Structural, "1.2.840.113556.1.5.61",
            must: ["cn"],
            may: ["rpcNsAnnotation", "rpcNsInterfaceID",
                  "rpcNsObjectID", "rpcNsPriority",
                  "rpcNsProfileEntry"],
            possSuperiors: ["rpcProfile"],
            description: "An RPC profile element."),

        ObjClass("rpcServer", "rpcEntry", ObjectClassType.Structural, "1.2.840.113556.1.5.61",
            must: [],
            may: ["rpcNsBindings"],
            possSuperiors: ["rpcContainer"],
            description: "An RPC server."),

        ObjClass("rpcServerElement", "rpcEntry", ObjectClassType.Structural, "1.2.840.113556.1.5.61",
            must: [],
            may: ["rpcNsBindings", "rpcNsInterfaceID",
                  "rpcNsObjectID", "rpcNsTransferSyntax"],
            possSuperiors: ["rpcServer"],
            description: "An RPC server element."),

        // =====================================================================
        // STRUCTURAL CLASSES - RRAS / Intellimirror
        // =====================================================================

        ObjClass("rRASAdministrationConnectionPoint", "serviceAdministrationPoint", ObjectClassType.Structural, "1.2.840.113556.1.5.150",
            must: [],
            may: ["msRRASAttribute"],
            possSuperiors: ["computer", "container"],
            description: "RRAS administration connection point."),

        ObjClass("rRASAdministrationDictionary", "leaf", ObjectClassType.Structural, "1.2.840.113556.1.5.155",
            must: [],
            may: ["msRRASAttribute"],
            possSuperiors: ["container"],
            description: "RRAS administration dictionary."),

        ObjClass("intellimirrorGroup", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.139",
            must: ["cn"],
            may: [],
            possSuperiors: ["container", "organizationalUnit", "domainDNS"],
            description: "Intellimirror group."),

        ObjClass("intellimirrorSCP", "serviceConnectionPoint", ObjectClassType.Structural, "1.2.840.113556.1.5.140",
            must: [],
            may: ["netbootSCPBL"],
            possSuperiors: ["computer", "container"],
            description: "Intellimirror service connection point."),

        // =====================================================================
        // STRUCTURAL CLASSES - Index Server
        // =====================================================================

        ObjClass("indexServerCatalog", "connectionPoint", ObjectClassType.Structural, "1.2.840.113556.1.5.138",
            must: ["cn"],
            may: ["friendlyNames", "indexedScopes", "queryPoint",
                  "uNCName"],
            possSuperiors: ["computer", "container"],
            description: "Index Server catalog."),

        // =====================================================================
        // STRUCTURAL CLASSES - Licensing
        // =====================================================================

        ObjClass("licensingSiteSettings", "applicationSettings", ObjectClassType.Structural, "1.2.840.113556.1.5.162",
            must: [],
            may: ["siteServer"],
            possSuperiors: ["site"],
            description: "Licensing site settings."),

        // =====================================================================
        // STRUCTURAL CLASSES - Storage / Volume / Print
        // =====================================================================

        ObjClass("storage", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.20",
            must: ["cn"],
            may: ["iconPath"],
            possSuperiors: ["container"],
            description: "A storage object."),

        ObjClass("volume", "connectionPoint", ObjectClassType.Structural, "1.2.840.113556.1.5.36",
            must: ["cn"],
            may: ["keywords", "managedBy", "uNCName"],
            possSuperiors: ["computer", "container"],
            description: "A shared volume."),

        ObjClass("printQueue", "connectionPoint", ObjectClassType.Structural, "1.2.840.113556.1.5.23",
            must: ["cn"],
            may: ["assetNumber", "bytesPerMinute", "defaultPriority",
                  "driverName", "driverVersion",
                  "keywords", "location", "managedBy",
                  "operatingSystem", "operatingSystemHotfix",
                  "operatingSystemServicePack", "operatingSystemVersion",
                  "physicalLocationObject",
                  "portName", "printAttributes",
                  "printBinNames", "printCollate",
                  "printColor", "printDuplexSupported",
                  "printEndTime", "printFormName",
                  "printKeepPrintedJobs", "printLanguage",
                  "printMACAddress", "printMaxCopies",
                  "printMaxResolutionSupported", "printMaxXExtent",
                  "printMaxYExtent", "printMediaReady",
                  "printMediaSupported", "printMemory",
                  "printMinXExtent", "printMinYExtent",
                  "printNetworkAddress", "printNotify",
                  "printNumberUp", "printOrientationsSupported",
                  "printOwner", "printPagesPerMinute",
                  "printRate", "printRateUnit",
                  "printSeparatorFile", "printShareName",
                  "printSpooling", "printStaplingSupported",
                  "printStartTime", "printStatus",
                  "priority", "serverName",
                  "shortServerName", "uNCName",
                  "url", "versionNumber"],
            possSuperiors: ["computer", "container", "domainDNS",
                            "organizationalUnit"],
            description: "A print queue."),

        // =====================================================================
        // STRUCTURAL CLASSES - Physical Location
        // =====================================================================

        ObjClass("physicalLocation", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.171",
            must: ["cn"],
            may: ["managedBy"],
            possSuperiors: ["container", "physicalLocation"],
            description: "Physical location hierarchy."),

        // =====================================================================
        // STRUCTURAL CLASSES - SAM
        // =====================================================================

        ObjClass("sAMDomain", "top", ObjectClassType.Structural, "1.2.840.113556.1.3.6",
            must: [],
            may: [],
            possSuperiors: ["container"],
            description: "SAM domain object."),

        ObjClass("sAMServer", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.7000.53",
            must: ["cn"],
            may: [],
            possSuperiors: ["domainDNS"],
            description: "SAM server object."),

        // =====================================================================
        // STRUCTURAL CLASSES - Meeting
        // =====================================================================

        ObjClass("meetingBanner", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.20",
            must: ["cn"],
            may: [],
            possSuperiors: ["container"],
            description: "Meeting banner."),

        // =====================================================================
        // STRUCTURAL CLASSES - Domain Policy / DSA
        // =====================================================================

        ObjClass("domainPolicy", "leaf", ObjectClassType.Structural, "1.2.840.113556.1.5.23",
            must: ["cn"],
            may: ["authenticationOptions", "defaultLocalPolicyObject",
                  "domainCAs", "domainPolicyObject",
                  "eFSPolicy", "ipsecPolicyReference",
                  "managedBy", "qualityOfService"],
            possSuperiors: ["domainDNS"],
            description: "Domain-wide policy settings."),

        ObjClass("domainRelax", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.23",
            must: ["cn"],
            may: [],
            possSuperiors: ["container"],
            description: "Domain relaxation settings."),

        ObjClass("dSA", "applicationEntity", ObjectClassType.Structural, "2.5.6.13",
            must: [],
            may: ["knowledgeInformation"],
            possSuperiors: ["container", "organizationalUnit"],
            description: "An X.500 Directory System Agent."),

        // =====================================================================
        // STRUCTURAL CLASSES - Infrastructure
        // =====================================================================

        ObjClass("infrastructureUpdate", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.7000.55",
            must: ["cn"],
            may: ["dNReferenceUpdate", "fSMORoleOwner"],
            possSuperiors: ["domainDNS"],
            description: "Infrastructure master object."),

        // =====================================================================
        // STRUCTURAL CLASSES - CRL
        // =====================================================================

        ObjClass("cRLDistributionPoint", "top", ObjectClassType.Structural, "2.5.6.19",
            must: ["cn"],
            may: ["authorityRevocationList", "certificateRevocationList",
                  "deltaRevocationList"],
            possSuperiors: ["container"],
            description: "CRL distribution point."),

        // =====================================================================
        // STRUCTURAL CLASSES - CMHTTP
        // =====================================================================

        ObjClass("cMHTTPConfiguration", "connectionPoint", ObjectClassType.Structural, "1.2.840.113556.1.5.20",
            must: ["cn"],
            may: [],
            possSuperiors: ["container"],
            description: "Connection Manager HTTP configuration."),

        // =====================================================================
        // STRUCTURAL CLASSES - msDS Extensions
        // =====================================================================

        ObjClass("msDS-AppConfiguration", "applicationSettings", ObjectClassType.Structural, "1.2.840.113556.1.5.184",
            must: [],
            may: ["msDS-AppData", "msDS-Settings"],
            possSuperiors: ["container"],
            description: "Application configuration."),

        ObjClass("msDS-AppData", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.185",
            must: ["cn"],
            may: ["msDS-AppData", "msDS-Settings"],
            possSuperiors: ["msDS-AppConfiguration"],
            description: "Application data."),

        ObjClass("msDS-AuthNPolicySilo", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.285",
            must: ["cn"],
            may: ["msDS-AssignedAuthNPolicy", "msDS-AssignedAuthNPolicySilo",
                  "msDS-AuthNPolicySiloEnforced",
                  "msDS-AuthNPolicySiloMembers",
                  "msDS-ComputerAuthNPolicy", "msDS-ServiceAuthNPolicy",
                  "msDS-UserAuthNPolicy"],
            possSuperiors: ["msDS-AuthNPolicySilos"],
            description: "Authentication policy silo."),

        ObjClass("msDS-AuthNPolicySilos", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.284",
            must: ["cn"],
            may: [],
            possSuperiors: ["container", "configuration"],
            description: "Container for AuthN policy silos."),

        ObjClass("msDS-ClaimType", "msDS-ClaimTypePropertyBase", ObjectClassType.Structural, "1.2.840.113556.1.5.275",
            must: [],
            may: ["msDS-ClaimAttributeSource", "msDS-ClaimIsSingleValued",
                  "msDS-ClaimIsValueSpaceRestricted",
                  "msDS-ClaimPossibleValues", "msDS-ClaimSourceType",
                  "msDS-ClaimTypeAppliesToClass",
                  "msDS-ClaimValueType"],
            possSuperiors: ["msDS-ClaimTypes"],
            description: "A claims-based authentication claim type."),

        ObjClass("msDS-ClaimTypePropertyBase", "top", ObjectClassType.Abstract, "1.2.840.113556.1.5.274",
            must: ["cn"],
            may: ["displayName", "enabled", "msDS-ClaimSharesPossibleValuesWith"],
            description: "Abstract base for claim type and resource property."),

        ObjClass("msDS-ClaimTypes", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.273",
            must: ["cn"],
            may: [],
            possSuperiors: ["container", "configuration"],
            description: "Container for claim types."),

        ObjClass("msDS-CloudExtensions", "top", ObjectClassType.Auxiliary, "1.2.840.113556.1.5.283",
            must: [],
            may: ["msDS-CloudAnchor", "msDS-CloudIsEnabled",
                  "msDS-CloudIssuerPublicCertificates",
                  "msDS-CloudProxyTrust"],
            description: "Cloud extension attributes."),

        ObjClass("msDS-Device", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.286",
            must: ["cn"],
            may: ["displayName", "msDS-CloudAnchor", "msDS-CloudIsEnabled",
                  "msDS-DeviceID", "msDS-DeviceLocation",
                  "msDS-DeviceOSType", "msDS-DeviceOSVersion",
                  "msDS-DevicePhysicalIDs", "msDS-IsEnabled",
                  "msDS-RegisteredOwner", "msDS-RegisteredUsers"],
            possSuperiors: ["msDS-DeviceContainer"],
            description: "A registered device."),

        ObjClass("msDS-DeviceContainer", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.287",
            must: ["cn"],
            may: [],
            possSuperiors: ["domainDNS"],
            description: "Container for device objects."),

        ObjClass("msDS-DeviceRegistrationService", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.288",
            must: ["cn"],
            may: ["msDS-DeviceRegistrationServiceMaximumQuota",
                  "msDS-IsEnabled"],
            possSuperiors: ["msDS-DeviceRegistrationServiceContainer"],
            description: "Device registration service settings."),

        ObjClass("msDS-DeviceRegistrationServiceContainer", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.289",
            must: ["cn"],
            may: [],
            possSuperiors: ["container", "configuration"],
            description: "Container for device registration service."),

        ObjClass("msDS-GroupManagedServiceAccount", "computer", ObjectClassType.Structural, "1.2.840.113556.1.5.262",
            must: [],
            may: ["msDS-GroupMSAMembership", "msDS-ManagedPasswordId",
                  "msDS-ManagedPasswordInterval",
                  "msDS-ManagedPasswordPreviousId"],
            possSuperiors: ["container", "organizationalUnit", "domainDNS"],
            description: "Group managed service account."),

        ObjClass("msDS-KeyCredential", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.290",
            must: ["cn"],
            may: ["msDS-KeyCredentialLink"],
            possSuperiors: ["container"],
            description: "Key credential object."),

        ObjClass("msDS-ManagedServiceAccount", "computer", ObjectClassType.Structural, "1.2.840.113556.1.5.258",
            must: [],
            may: ["msDS-HostServiceAccountBL", "msDS-ManagedPasswordId",
                  "msDS-ManagedPasswordInterval",
                  "msDS-ManagedPasswordPreviousId"],
            possSuperiors: ["container", "organizationalUnit", "domainDNS"],
            description: "Managed service account."),

        ObjClass("msDS-OptionalFeature", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.262",
            must: ["cn", "msDS-OptionalFeatureFlags", "msDS-OptionalFeatureGUID"],
            may: ["msDS-EnabledFeatureBL", "msDS-RequiredDomainBehaviorVersion",
                  "msDS-RequiredForestBehaviorVersion"],
            possSuperiors: ["container"],
            description: "Optional directory feature."),

        ObjClass("msDS-ResourceProperties", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.277",
            must: ["cn"],
            may: [],
            possSuperiors: ["container", "configuration"],
            description: "Container for resource properties."),

        ObjClass("msDS-ResourceProperty", "msDS-ClaimTypePropertyBase", ObjectClassType.Structural, "1.2.840.113556.1.5.276",
            must: [],
            may: ["msDS-AppliesToResourceTypes", "msDS-IsUsedAsResourceSecurityAttribute",
                  "msDS-MembersOfResourcePropertyListBL",
                  "msDS-ResourcePropertyValueType"],
            possSuperiors: ["msDS-ResourceProperties"],
            description: "A resource property for DAC."),

        ObjClass("msDS-ResourcePropertyList", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.278",
            must: ["cn"],
            may: ["msDS-MembersOfResourcePropertyList"],
            possSuperiors: ["container", "configuration"],
            description: "A resource property list."),

        ObjClass("msDS-ShadowPrincipal", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.291",
            must: ["cn"],
            may: ["member", "msDS-ShadowPrincipalSid"],
            possSuperiors: ["msDS-ShadowPrincipalContainer"],
            auxiliaryClasses: ["securityPrincipal"],
            description: "Shadow principal for PAM."),

        ObjClass("msDS-ShadowPrincipalContainer", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.292",
            must: ["cn"],
            may: [],
            possSuperiors: ["container", "domainDNS"],
            description: "Container for shadow principals."),

        ObjClass("msDS-ValueType", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.272",
            must: ["cn"],
            may: ["msDS-ClaimIsValueSpaceRestricted",
                  "msDS-ClaimPossibleValues",
                  "msDS-ClaimValueType",
                  "msDS-IsPossibleValuesPresent",
                  "msDS-ValueTypeReference"],
            possSuperiors: ["container", "configuration"],
            description: "Value type for resource properties."),

        // =====================================================================
        // STRUCTURAL CLASSES - Authorization (msAuthz)
        // =====================================================================

        ObjClass("msAuthz-CentralAccessPolicies", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.281",
            must: ["cn"],
            may: [],
            possSuperiors: ["container", "configuration"],
            description: "Container for central access policies."),

        ObjClass("msAuthz-CentralAccessPolicy", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.279",
            must: ["cn"],
            may: ["msAuthz-CentralAccessPolicyID",
                  "msAuthz-MemberRulesInCentralAccessPolicy"],
            possSuperiors: ["msAuthz-CentralAccessPolicies"],
            description: "Central access policy for DAC."),

        ObjClass("msAuthz-CentralAccessRule", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.280",
            must: ["cn"],
            may: ["msAuthz-EffectiveSecurityPolicy",
                  "msAuthz-LastEffectiveSecurityPolicy",
                  "msAuthz-MemberRulesInCentralAccessPolicy",
                  "msAuthz-ProposedSecurityPolicy",
                  "msAuthz-ResourceCondition"],
            possSuperiors: ["msAuthz-CentralAccessRules"],
            description: "Central access rule for DAC."),

        ObjClass("msAuthz-CentralAccessRules", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.282",
            must: ["cn"],
            may: [],
            possSuperiors: ["container", "configuration"],
            description: "Container for central access rules."),

        // =====================================================================
        // STRUCTURAL CLASSES - FVE (BitLocker)
        // =====================================================================

        ObjClass("msFVE-RecoveryInformation", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.253",
            must: ["cn"],
            may: ["msFVE-KeyPackage", "msFVE-RecoveryGuid",
                  "msFVE-RecoveryPassword", "msFVE-VolumeGuid"],
            possSuperiors: ["computer"],
            description: "BitLocker recovery information."),

        // =====================================================================
        // STRUCTURAL CLASSES - IEEE 802.11
        // =====================================================================

        ObjClass("msieee80211-Policy", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.240",
            must: ["cn"],
            may: ["msieee80211-Data", "msieee80211-DataType",
                  "msieee80211-ID"],
            possSuperiors: ["container", "domainDNS", "organizationalUnit"],
            description: "IEEE 802.11 wireless policy."),

        // =====================================================================
        // STRUCTURAL CLASSES - Imaging
        // =====================================================================

        ObjClass("msImaging-PostScanProcess", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.256",
            must: ["cn"],
            may: ["msImaging-HashAlgorithm", "msImaging-ThumbprintHash"],
            possSuperiors: ["container"],
            description: "Post-scan process configuration."),

        ObjClass("msImaging-PSPNetworkFolder", "msImaging-PostScanProcess", ObjectClassType.Structural, "1.2.840.113556.1.5.257",
            must: [],
            may: [],
            possSuperiors: ["container"],
            description: "Post-scan process network folder."),

        // =====================================================================
        // STRUCTURAL CLASSES - Print Connection Policy
        // =====================================================================

        ObjClass("msPrint-ConnectionPolicy", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.261",
            must: ["cn"],
            may: ["msPrint-ConnectionPolicyFilter",
                  "printerName", "printAttributes",
                  "serverName", "uNCName"],
            possSuperiors: ["container"],
            description: "Print connection policy."),

        // =====================================================================
        // STRUCTURAL CLASSES - SPP Activation
        // =====================================================================

        ObjClass("msSPP-ActivationObject", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.263",
            must: ["cn"],
            may: ["msSPP-ConfigLicense", "msSPP-CSVLKPartialProductKey",
                  "msSPP-CSVLKPid", "msSPP-CSVLKSkuId",
                  "msSPP-InstallationId", "msSPP-IssuanceLicense",
                  "msSPP-KMSIds", "msSPP-OnlineLicense",
                  "msSPP-PhoneActivationData"],
            possSuperiors: ["msSPP-ActivationObjectsContainer"],
            description: "Software protection activation object."),

        ObjClass("msSPP-ActivationObjectsContainer", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.264",
            must: ["cn"],
            may: [],
            possSuperiors: ["container", "domainDNS"],
            description: "Container for activation objects."),

        // =====================================================================
        // STRUCTURAL CLASSES - TAPI
        // =====================================================================

        ObjClass("msTAPI-RtConference", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.216",
            must: ["cn"],
            may: ["msTAPI-ConferenceBlob", "msTAPI-ProtocolId",
                  "msTAPI-uid"],
            possSuperiors: ["container"],
            description: "TAPI conference."),

        ObjClass("msTAPI-RtPerson", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.215",
            must: ["cn"],
            may: ["msTAPI-IpAddress", "msTAPI-ProtocolId",
                  "msTAPI-uid"],
            possSuperiors: ["container"],
            description: "TAPI person."),

        // =====================================================================
        // STRUCTURAL CLASSES - TPM
        // =====================================================================

        ObjClass("msTPM-InformationObject", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.260",
            must: ["cn"],
            may: ["msTPM-OwnerInformation"],
            possSuperiors: ["msTPM-InformationObjectsContainer"],
            description: "TPM information object."),

        ObjClass("msTPM-InformationObjectsContainer", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.259",
            must: ["cn"],
            may: [],
            possSuperiors: ["domainDNS"],
            description: "Container for TPM information objects."),

        // =====================================================================
        // STRUCTURAL CLASSES - Terminal Services
        // =====================================================================

        ObjClass("msTSProfile", "top", ObjectClassType.Auxiliary, "1.2.840.113556.1.5.20",
            must: [],
            may: ["msTSAllowLogon", "msTSBrokenConnectionAction",
                  "msTSConnectClientDrives", "msTSConnectPrinterDrives",
                  "msTSDefaultToMainPrinter", "msTSEndpointData",
                  "msTSEndpointPlugin", "msTSEndpointType",
                  "msTSHomeDirectory", "msTSHomeDrive",
                  "msTSInitialProgram", "msTSMaxConnectionTime",
                  "msTSMaxDisconnectionTime", "msTSMaxIdleTime",
                  "msTSPrimaryDesktop", "msTSProfilePath",
                  "msTSProperty01", "msTSProperty02",
                  "msTSReconnectionAction", "msTSRemoteControl",
                  "msTSSecondaryDesktops", "msTSWorkDirectory"],
            description: "Terminal Services profile attributes."),

        // =====================================================================
        // STRUCTURAL CLASSES - RADIUS
        // =====================================================================

        ObjClass("msRADIUS-Profile", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.200",
            must: ["cn"],
            may: ["msNPAllowDialin", "msNPCallingStationID",
                  "msNPSavedCallingStationID", "msRADIUSCallbackNumber",
                  "msRADIUSFramedIPAddress", "msRADIUSFramedRoute",
                  "msRADIUSServiceType"],
            possSuperiors: ["container"],
            description: "RADIUS profile."),

        // =====================================================================
        // STRUCTURAL CLASSES - WMI Policy
        // =====================================================================

        ObjClass("msWMI-IntRangeParam", "msWMI-RangeParam", ObjectClassType.Structural, "1.2.840.113556.1.5.205",
            must: [],
            may: ["msWMI-IntDefault", "msWMI-IntMax", "msWMI-IntMin",
                  "msWMI-IntValidValues"],
            possSuperiors: ["msWMI-MergeablePolicyTemplate",
                            "msWMI-SimplePolicyTemplate"],
            description: "WMI integer range parameter."),

        ObjClass("msWMI-IntSetParam", "msWMI-RangeParam", ObjectClassType.Structural, "1.2.840.113556.1.5.204",
            must: [],
            may: ["msWMI-IntDefault", "msWMI-IntValidValues"],
            possSuperiors: ["msWMI-MergeablePolicyTemplate",
                            "msWMI-SimplePolicyTemplate"],
            description: "WMI integer set parameter."),

        ObjClass("msWMI-MergeablePolicyTemplate", "msWMI-PolicyTemplate", ObjectClassType.Structural, "1.2.840.113556.1.5.206",
            must: [],
            may: [],
            possSuperiors: ["container", "domainDNS", "organizationalUnit"],
            description: "WMI mergeable policy template."),

        ObjClass("msWMI-ObjectEncoding", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.211",
            must: ["cn"],
            may: ["msWMI-TargetNameSpace", "msWMI-TargetObject"],
            possSuperiors: ["msWMI-MergeablePolicyTemplate",
                            "msWMI-SimplePolicyTemplate"],
            description: "WMI object encoding."),

        ObjClass("msWMI-PolicyTemplate", "top", ObjectClassType.Abstract, "1.2.840.113556.1.5.207",
            must: ["cn"],
            may: ["msWMI-Author", "msWMI-ChangeDate", "msWMI-ClassDefinition",
                  "msWMI-CreationDate", "msWMI-ID", "msWMI-Name",
                  "msWMI-NormalizedClass", "msWMI-Parm1", "msWMI-Parm2",
                  "msWMI-Parm3", "msWMI-Parm4",
                  "msWMI-SourceOrganization", "msWMI-TargetClass",
                  "msWMI-TargetNameSpace", "msWMI-TargetPath",
                  "msWMI-TargetType"],
            description: "Abstract WMI policy template."),

        ObjClass("msWMI-PolicyType", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.208",
            must: ["cn"],
            may: ["msWMI-Author", "msWMI-ChangeDate", "msWMI-CreationDate",
                  "msWMI-ID", "msWMI-Name", "msWMI-SourceOrganization"],
            possSuperiors: ["container", "domainDNS", "organizationalUnit"],
            description: "WMI policy type."),

        ObjClass("msWMI-RangeParam", "top", ObjectClassType.Abstract, "1.2.840.113556.1.5.203",
            must: ["cn"],
            may: ["msWMI-ID", "msWMI-Name", "msWMI-PropertyName",
                  "msWMI-TargetClass", "msWMI-TargetNameSpace",
                  "msWMI-TargetPath", "msWMI-TargetType"],
            description: "Abstract WMI range parameter."),

        ObjClass("msWMI-RealRangeParam", "msWMI-RangeParam", ObjectClassType.Structural, "1.2.840.113556.1.5.212",
            must: [],
            may: ["msWMI-IntDefault", "msWMI-IntMax", "msWMI-IntMin",
                  "msWMI-IntValidValues"],
            possSuperiors: ["msWMI-MergeablePolicyTemplate",
                            "msWMI-SimplePolicyTemplate"],
            description: "WMI real range parameter."),

        ObjClass("msWMI-Rule", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.214",
            must: ["cn"],
            may: ["msWMI-Author", "msWMI-ChangeDate", "msWMI-CreationDate",
                  "msWMI-ID", "msWMI-Name", "msWMI-Query",
                  "msWMI-QueryLanguage", "msWMI-SourceOrganization",
                  "msWMI-TargetNameSpace"],
            possSuperiors: ["container", "domainDNS", "organizationalUnit"],
            description: "WMI rule."),

        ObjClass("msWMI-ShadowCopy", "msWMI-PolicyTemplate", ObjectClassType.Structural, "1.2.840.113556.1.5.209",
            must: [],
            may: [],
            possSuperiors: ["container", "domainDNS", "organizationalUnit"],
            description: "WMI shadow copy."),

        ObjClass("msWMI-SimplePolicyTemplate", "msWMI-PolicyTemplate", ObjectClassType.Structural, "1.2.840.113556.1.5.210",
            must: [],
            may: [],
            possSuperiors: ["container", "domainDNS", "organizationalUnit"],
            description: "WMI simple policy template."),

        ObjClass("msWMI-Som", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.213",
            must: ["cn"],
            may: ["msWMI-Author", "msWMI-ChangeDate", "msWMI-CreationDate",
                  "msWMI-ID", "msWMI-Name", "msWMI-SourceOrganization"],
            possSuperiors: ["container", "domainDNS", "organizationalUnit"],
            description: "WMI scope of management."),

        ObjClass("msWMI-StringSetParam", "msWMI-RangeParam", ObjectClassType.Structural, "1.2.840.113556.1.5.202",
            must: [],
            may: ["msWMI-StringDefault", "msWMI-StringValidValues"],
            possSuperiors: ["msWMI-MergeablePolicyTemplate",
                            "msWMI-SimplePolicyTemplate"],
            description: "WMI string set parameter."),

        ObjClass("msWMI-UintRangeParam", "msWMI-RangeParam", ObjectClassType.Structural, "1.2.840.113556.1.5.201",
            must: [],
            may: ["msWMI-IntDefault", "msWMI-IntMax", "msWMI-IntMin",
                  "msWMI-IntValidValues"],
            possSuperiors: ["msWMI-MergeablePolicyTemplate",
                            "msWMI-SimplePolicyTemplate"],
            description: "WMI unsigned integer range parameter."),

        ObjClass("msWMI-UintSetParam", "msWMI-RangeParam", ObjectClassType.Structural, "1.2.840.113556.1.5.200",
            must: [],
            may: ["msWMI-IntDefault", "msWMI-IntValidValues"],
            possSuperiors: ["msWMI-MergeablePolicyTemplate",
                            "msWMI-SimplePolicyTemplate"],
            description: "WMI unsigned integer set parameter."),

        ObjClass("msWMI-UnknownRangeParam", "msWMI-RangeParam", ObjectClassType.Structural, "1.2.840.113556.1.5.199",
            must: [],
            may: [],
            possSuperiors: ["msWMI-MergeablePolicyTemplate",
                            "msWMI-SimplePolicyTemplate"],
            description: "WMI unknown range parameter."),

        ObjClass("msWMI-WMIGPO", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.198",
            must: ["cn"],
            may: ["msWMI-Author", "msWMI-ChangeDate", "msWMI-CreationDate",
                  "msWMI-ID", "msWMI-Name", "msWMI-Parm1", "msWMI-Parm2",
                  "msWMI-Parm3", "msWMI-Parm4",
                  "msWMI-SourceOrganization"],
            possSuperiors: ["container", "domainDNS", "organizationalUnit"],
            description: "WMI GPO link."),

        // =====================================================================
        // STRUCTURAL CLASSES - Query Policy
        // =====================================================================

        ObjClass("queryPolicy", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.157",
            must: ["cn"],
            may: ["lDAPAdminLimits", "lDAPIPDenyList"],
            possSuperiors: ["container", "nTDSService"],
            description: "LDAP query policy."),

        // =====================================================================
        // STRUCTURAL CLASSES - Remote Storage / Remote Mail
        // =====================================================================

        ObjClass("remoteStorageServicePoint", "connectionPoint", ObjectClassType.Structural, "1.2.840.113556.1.5.149",
            must: ["cn"],
            may: ["remoteStorageGUID"],
            possSuperiors: ["computer", "container"],
            description: "Remote storage service point."),

        ObjClass("remoteMail-Recipient", "top", ObjectClassType.Structural, "1.2.840.113556.1.3.46",
            must: ["cn"],
            may: [],
            possSuperiors: ["container", "organizationalUnit", "domainDNS"],
            description: "Remote mail recipient."),

        // =====================================================================
        // STRUCTURAL CLASSES - Kds (Key Distribution Service)
        // =====================================================================

        ObjClass("msKds-ProvRootKey", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.269",
            must: ["cn"],
            may: ["msKds-CreateTime", "msKds-DomainID",
                  "msKds-KDFAlgorithmID", "msKds-KDFParam",
                  "msKds-PrivateKeyLength", "msKds-PublicKeyLength",
                  "msKds-RootKeyData",
                  "msKds-SecretAgreementAlgorithmID",
                  "msKds-SecretAgreementParam",
                  "msKds-UseStartTime", "msKds-Version"],
            possSuperiors: ["msKds-ProvServerConfiguration"],
            description: "KDS root key."),

        ObjClass("msKds-ProvServerConfiguration", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.268",
            must: ["cn"],
            may: ["msKds-KDFAlgorithmID", "msKds-KDFParam",
                  "msKds-SecretAgreementAlgorithmID",
                  "msKds-SecretAgreementParam",
                  "msKds-Version"],
            possSuperiors: ["container"],
            description: "KDS server configuration."),

        // =====================================================================
        // STRUCTURAL CLASSES - DNS Server Settings
        // =====================================================================

        ObjClass("msDNS-ServerSettings", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.267",
            must: ["cn"],
            may: ["msDNS-IsSigned", "msDNS-KeymasterZones",
                  "msDNS-NSEC3CurrentSalt", "msDNS-NSEC3HashAlgorithm",
                  "msDNS-NSEC3Iterations", "msDNS-NSEC3OptOut",
                  "msDNS-NSEC3RandomSaltLength",
                  "msDNS-NSEC3UserSalt",
                  "msDNS-ParentalControlEnabled",
                  "msDNS-PropagationTime",
                  "msDNS-SecureDelegationPollingPeriod",
                  "msDNS-ServerSettings",
                  "msDNS-SignatureInceptionOffset",
                  "msDNS-SigningKeyDescriptors",
                  "msDNS-SigningKeys",
                  "msDNS-SignWithNSEC3"],
            possSuperiors: ["container", "dnsZone"],
            description: "DNS server settings."),

        // =====================================================================
        // STRUCTURAL CLASSES - DS Claims Transformation
        // =====================================================================

        ObjClass("msDS-ClaimsTransformationPolicies", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.283",
            must: ["cn"],
            may: [],
            possSuperiors: ["container", "configuration"],
            description: "Container for claims transformation policies."),

        ObjClass("msDS-ClaimsTransformationPolicyType", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.282",
            must: ["cn"],
            may: ["msDS-TransformationRules",
                  "msDS-TransformationRulesCompiled"],
            possSuperiors: ["msDS-ClaimsTransformationPolicies"],
            description: "Claims transformation policy type."),

        // =====================================================================
        // STRUCTURAL CLASSES - DS Az (Authorization Manager)
        // =====================================================================

        ObjClass("msDS-AzAdminManager", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.227",
            must: ["cn"],
            may: ["msDS-AzApplicationData", "msDS-AzDomainTimeout",
                  "msDS-AzGenerateAudits", "msDS-AzGenericData",
                  "msDS-AzMajorVersion", "msDS-AzMinorVersion",
                  "msDS-AzObjectGuid", "msDS-AzScriptEngineCacheMax",
                  "msDS-AzScriptTimeout"],
            possSuperiors: ["container", "organizationalUnit", "domainDNS"],
            description: "Authorization Manager admin manager."),

        ObjClass("msDS-AzApplication", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.228",
            must: ["cn"],
            may: ["msDS-AzApplicationData", "msDS-AzApplicationName",
                  "msDS-AzApplicationVersion", "msDS-AzDomainTimeout",
                  "msDS-AzGenerateAudits", "msDS-AzGenericData",
                  "msDS-AzObjectGuid", "msDS-AzScriptEngineCacheMax",
                  "msDS-AzScriptTimeout"],
            possSuperiors: ["msDS-AzAdminManager"],
            description: "Authorization Manager application."),

        ObjClass("msDS-AzOperation", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.229",
            must: ["cn"],
            may: ["msDS-AzApplicationData", "msDS-AzGenericData",
                  "msDS-AzObjectGuid", "msDS-AzOperationID"],
            possSuperiors: ["msDS-AzApplication"],
            description: "Authorization Manager operation."),

        ObjClass("msDS-AzRole", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.232",
            must: ["cn"],
            may: ["msDS-AzApplicationData", "msDS-AzGenericData",
                  "msDS-AzObjectGuid", "msDS-MembersForAzRole",
                  "msDS-OperationsForAzRole", "msDS-TasksForAzRole"],
            possSuperiors: ["msDS-AzApplication", "msDS-AzScope"],
            description: "Authorization Manager role."),

        ObjClass("msDS-AzScope", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.231",
            must: ["cn"],
            may: ["msDS-AzApplicationData", "msDS-AzGenericData",
                  "msDS-AzObjectGuid", "msDS-AzScopeName"],
            possSuperiors: ["msDS-AzApplication"],
            description: "Authorization Manager scope."),

        ObjClass("msDS-AzTask", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.230",
            must: ["cn"],
            may: ["msDS-AzApplicationData", "msDS-AzBizRule",
                  "msDS-AzBizRuleLanguage", "msDS-AzGenericData",
                  "msDS-AzLDAPQuery", "msDS-AzObjectGuid",
                  "msDS-AzTaskIsRoleDefinition",
                  "msDS-OperationsForAzTask", "msDS-TasksForAzTask"],
            possSuperiors: ["msDS-AzApplication", "msDS-AzScope"],
            description: "Authorization Manager task."),

        // =====================================================================
        // STRUCTURAL CLASSES - Bindable/Proxy Objects
        // =====================================================================

        ObjClass("msDS-BindableObject", "top", ObjectClassType.Abstract, "1.2.840.113556.1.5.244",
            must: [],
            may: ["msDS-ObjectSoa"],
            description: "Abstract bindable object."),

        ObjClass("msDS-BindProxy", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.245",
            must: ["cn", "objectSid"],
            may: ["sidHistory"],
            possSuperiors: ["container"],
            description: "Bind proxy for ADAM/LDS."),

        // =====================================================================
        // STRUCTURAL CLASSES - Service Connection Point Publication
        // =====================================================================

        ObjClass("msDS-ServiceConnectionPointPublicationService", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.255",
            must: ["cn"],
            may: ["msDS-SCPContainer"],
            possSuperiors: ["computer", "container"],
            description: "SCP publication service."),

        // =====================================================================
        // STRUCTURAL CLASSES - MSMQ
        // =====================================================================

        ObjClass("mSMQConfiguration", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.162",
            must: ["cn"],
            may: ["mSMQComputerType", "mSMQComputerTypeEx",
                  "mSMQCSPName", "mSMQDependentClientService",
                  "mSMQDependentClientServices", "mSMQDigests",
                  "mSMQDigestsMig", "mSMQDsService",
                  "mSMQDsServices", "mSMQEncryptKey",
                  "mSMQForeign", "mSMQInRoutingServers",
                  "mSMQInterval1", "mSMQInterval2", "mSMQJournal",
                  "mSMQMigrated", "mSMQNameStyle",
                  "mSMQNt4Flags", "mSMQNt4Stub",
                  "mSMQOperatingSystem", "mSMQOutRoutingServers",
                  "mSMQOwnerID", "mSMQQMID", "mSMQQuota",
                  "mSMQRoutingService", "mSMQRoutingServices",
                  "mSMQServiceType", "mSMQSignCertificates",
                  "mSMQSignCertificatesMig", "mSMQSite1",
                  "mSMQSite2", "mSMQSiteForeign",
                  "mSMQSiteGates", "mSMQSiteGatesMig",
                  "mSMQSiteID", "mSMQSiteName",
                  "mSMQSiteNameEx", "mSMQSites",
                  "mSMQTransactional", "mSMQUserSid"],
            possSuperiors: ["computer"],
            description: "MSMQ configuration."),

        ObjClass("mSMQQueue", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.163",
            must: ["cn"],
            may: ["mSMQAuthenticate", "mSMQBasePriority",
                  "mSMQEncryptKey", "mSMQJournal", "mSMQJournalQuota",
                  "mSMQLabelEx", "mSMQMulticastAddress",
                  "mSMQOwnerID", "mSMQPrivacyLevel",
                  "mSMQQueueJournalQuota", "mSMQQueueNameExt",
                  "mSMQQueueQuota", "mSMQQueueType",
                  "mSMQRoutingService", "mSMQTransactional"],
            possSuperiors: ["mSMQConfiguration", "computer"],
            description: "MSMQ queue."),

        ObjClass("mSMQSettings", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.164",
            must: ["cn"],
            may: ["mSMQNameStyle", "mSMQNt4Stub",
                  "mSMQSiteNameEx", "mSMQVersion"],
            possSuperiors: ["site"],
            description: "MSMQ site settings."),

        ObjClass("mSMQEnterpriseSettings", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.165",
            must: ["cn"],
            may: ["mSMQCSPName", "mSMQLongLived",
                  "mSMQNameStyle", "mSMQVersion"],
            possSuperiors: ["configuration"],
            description: "MSMQ enterprise settings."),

        ObjClass("mSMQMigratedUser", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.166",
            must: ["cn"],
            may: ["mSMQDigests", "mSMQDigestsMig",
                  "mSMQOwnerID", "mSMQSignCertificates",
                  "mSMQSignCertificatesMig", "mSMQUserSid"],
            possSuperiors: ["container"],
            description: "MSMQ migrated user."),

        ObjClass("mSMQ-Custom-Recipient", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.167",
            must: ["cn"],
            may: ["mSMQRecipientFormatName"],
            possSuperiors: ["container", "organizationalUnit", "domainDNS"],
            auxiliaryClasses: ["mailRecipient"],
            description: "MSMQ custom recipient."),

        ObjClass("mSMQ-Group", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.168",
            must: ["cn"],
            may: [],
            possSuperiors: ["container", "organizationalUnit", "domainDNS"],
            description: "MSMQ group."),

        ObjClass("mSMQSiteLink", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.169",
            must: ["cn"],
            may: ["cost", "mSMQSite1", "mSMQSite2",
                  "mSMQSiteGates", "mSMQSiteGatesMig"],
            possSuperiors: ["configuration"],
            description: "MSMQ site link."),

        // =====================================================================
        // STRUCTURAL CLASSES - Network Policy (802.11/802.3)
        // =====================================================================

        ObjClass("ms-net-ieee-80211-GroupPolicy", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.251",
            must: ["cn"],
            may: ["ms-net-ieee-80211-GP-PolicyData",
                  "ms-net-ieee-80211-GP-PolicyGUID",
                  "ms-net-ieee-80211-GP-PolicyReserved"],
            possSuperiors: ["container", "domainDNS", "organizationalUnit"],
            description: "IEEE 802.11 group policy."),

        ObjClass("ms-net-ieee-8023-GroupPolicy", "top", ObjectClassType.Structural, "1.2.840.113556.1.5.252",
            must: ["cn"],
            may: ["ms-net-ieee-8023-GP-PolicyData",
                  "ms-net-ieee-8023-GP-PolicyGUID",
                  "ms-net-ieee-8023-GP-PolicyReserved"],
            possSuperiors: ["container", "domainDNS", "organizationalUnit"],
            description: "IEEE 802.3 group policy."),

        // =====================================================================
        // STRUCTURAL CLASSES - SFU Mail / NIS Map Config
        // =====================================================================

        ObjClass("msSFU30MailAliases", "top", ObjectClassType.Structural, "1.2.840.113556.1.6.18.2.213",
            must: ["cn"],
            may: ["msSFU30Name", "msSFU30NisDomain"],
            possSuperiors: ["container"],
            description: "SFU mail aliases."),

        ObjClass("msSFU30NetworkUser", "top", ObjectClassType.Structural, "1.2.840.113556.1.6.18.2.214",
            must: ["cn"],
            may: ["msSFU30FieldSeparator", "msSFU30IntraFieldSeparator",
                  "msSFU30KeyAttributes", "msSFU30NetIdKey",
                  "msSFU30NisDomain", "msSFU30SearchAttributes",
                  "msSFU30SearchContainer"],
            possSuperiors: ["container"],
            description: "SFU network user."),

        ObjClass("msSFU30NISMapConfig", "top", ObjectClassType.Structural, "1.2.840.113556.1.6.18.2.215",
            must: ["cn"],
            may: ["msSFU30FieldSeparator", "msSFU30IntraFieldSeparator",
                  "msSFU30KeyAttributes", "msSFU30Name",
                  "msSFU30NisDomain", "msSFU30SearchAttributes",
                  "msSFU30SearchContainer"],
            possSuperiors: ["container"],
            description: "SFU NIS map configuration."),

        // =====================================================================
        // STRUCTURAL CLASSES - SQL Server
        // =====================================================================

        ObjClass("mS-SQL-SQLServer", "serviceConnectionPoint", ObjectClassType.Structural, "1.2.840.113556.1.5.184",
            must: [],
            may: ["mS-SQL-Build", "mS-SQL-CharacterSet",
                  "mS-SQL-Clustered", "mS-SQL-Contact",
                  "mS-SQL-GPSHeight", "mS-SQL-GPSLatitude",
                  "mS-SQL-GPSLongitude", "mS-SQL-InformationDirectory",
                  "mS-SQL-Language", "mS-SQL-Memory",
                  "mS-SQL-Name", "mS-SQL-NamedPipe",
                  "mS-SQL-RegisteredOwner", "mS-SQL-ServiceAccount",
                  "mS-SQL-SortOrder", "mS-SQL-SPX",
                  "mS-SQL-Status", "mS-SQL-TCPAddress",
                  "mS-SQL-UnicodeSortOrder", "mS-SQL-Vines"],
            possSuperiors: ["computer", "container"],
            description: "SQL Server instance."),

        ObjClass("mS-SQL-SQLDatabase", "serviceConnectionPoint", ObjectClassType.Structural, "1.2.840.113556.1.5.185",
            must: [],
            may: ["mS-SQL-Alias", "mS-SQL-Contact",
                  "mS-SQL-CreationDate", "mS-SQL-Database",
                  "mS-SQL-GPSHeight", "mS-SQL-GPSLatitude",
                  "mS-SQL-GPSLongitude", "mS-SQL-InformationURL",
                  "mS-SQL-LastUpdatedDate", "mS-SQL-Name",
                  "mS-SQL-Size", "mS-SQL-Status"],
            possSuperiors: ["mS-SQL-SQLServer"],
            description: "SQL Server database."),

        ObjClass("mS-SQL-SQLPublication", "serviceConnectionPoint", ObjectClassType.Structural, "1.2.840.113556.1.5.186",
            must: [],
            may: ["mS-SQL-AllowAnonymousSubscription",
                  "mS-SQL-Contact", "mS-SQL-Database",
                  "mS-SQL-Description", "mS-SQL-GPSHeight",
                  "mS-SQL-GPSLatitude", "mS-SQL-GPSLongitude",
                  "mS-SQL-Name", "mS-SQL-Publisher",
                  "mS-SQL-Status", "mS-SQL-ThirdParty",
                  "mS-SQL-Type"],
            possSuperiors: ["mS-SQL-SQLDatabase"],
            description: "SQL Server publication."),

        ObjClass("mS-SQL-SQLRepository", "serviceConnectionPoint", ObjectClassType.Structural, "1.2.840.113556.1.5.187",
            must: [],
            may: ["mS-SQL-Contact", "mS-SQL-GPSHeight",
                  "mS-SQL-GPSLatitude", "mS-SQL-GPSLongitude",
                  "mS-SQL-Name", "mS-SQL-Status"],
            possSuperiors: ["mS-SQL-SQLServer"],
            description: "SQL Server repository."),

        ObjClass("mS-SQL-OLAPServer", "serviceConnectionPoint", ObjectClassType.Structural, "1.2.840.113556.1.5.188",
            must: [],
            may: ["mS-SQL-Contact", "mS-SQL-GPSHeight",
                  "mS-SQL-GPSLatitude", "mS-SQL-GPSLongitude",
                  "mS-SQL-Name", "mS-SQL-Status"],
            possSuperiors: ["computer", "container"],
            description: "SQL OLAP server."),

        ObjClass("mS-SQL-OLAPDatabase", "serviceConnectionPoint", ObjectClassType.Structural, "1.2.840.113556.1.5.189",
            must: [],
            may: ["mS-SQL-Contact", "mS-SQL-GPSHeight",
                  "mS-SQL-GPSLatitude", "mS-SQL-GPSLongitude",
                  "mS-SQL-Name", "mS-SQL-Status"],
            possSuperiors: ["mS-SQL-OLAPServer"],
            description: "SQL OLAP database."),

        ObjClass("mS-SQL-OLAPCube", "serviceConnectionPoint", ObjectClassType.Structural, "1.2.840.113556.1.5.190",
            must: [],
            may: ["mS-SQL-Contact", "mS-SQL-GPSHeight",
                  "mS-SQL-GPSLatitude", "mS-SQL-GPSLongitude",
                  "mS-SQL-Name", "mS-SQL-Status"],
            possSuperiors: ["mS-SQL-OLAPDatabase"],
            description: "SQL OLAP cube."),

        // =====================================================================
        // STRUCTURAL CLASSES - RFC 822 / Room
        // =====================================================================

        ObjClass("rFC822LocalPart", "domain", ObjectClassType.Structural, "0.9.2342.19200300.100.4.14",
            must: [],
            may: ["cn", "description", "destinationIndicator",
                  "facsimileTelephoneNumber",
                  "internationalISDNNumber", "physicalDeliveryOfficeName",
                  "postalAddress", "postalCode", "postOfficeBox",
                  "preferredDeliveryMethod", "registeredAddress",
                  "seeAlso", "sn", "st", "street",
                  "telephoneNumber", "teletexTerminalIdentifier",
                  "telexNumber", "x121Address"],
            possSuperiors: ["container", "domainDNS"],
            description: "RFC 822 local part."),

        ObjClass("room", "top", ObjectClassType.Structural, "0.9.2342.19200300.100.4.7",
            must: ["cn"],
            may: ["description", "l", "roomNumber", "seeAlso",
                  "telephoneNumber"],
            possSuperiors: ["container", "organizationalUnit", "domainDNS"],
            description: "A room resource."),

        // =====================================================================
        // STRUCTURAL CLASSES - document
        // =====================================================================

        ObjClass("document", "top", ObjectClassType.Structural, "0.9.2342.19200300.100.4.6",
            must: ["documentIdentifier"],
            may: ["cn", "description", "documentAuthor",
                  "documentLocation", "documentPublisher",
                  "documentTitle", "documentVersion",
                  "l", "o", "ou", "seeAlso"],
            possSuperiors: ["container", "organizationalUnit", "domainDNS"],
            description: "A document."),
    ];

    private static ObjectClassSchemaEntry ObjClass(
        string name, string superiorClass, ObjectClassType classType, string oid,
        List<string> must, List<string> may, List<string> possSuperiors = null,
        List<string> auxiliaryClasses = null, string description = null)
        => new()
        {
            Name = name, LdapDisplayName = name, Oid = oid,
            SuperiorClass = superiorClass, ClassType = classType,
            MustHaveAttributes = must, MayHaveAttributes = may,
            PossibleSuperiors = possSuperiors ?? [],
            AuxiliaryClasses = auxiliaryClasses ?? [],
            Description = description,
        };
}
