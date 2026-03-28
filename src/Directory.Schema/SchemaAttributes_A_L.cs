using Directory.Core.Interfaces;

namespace Directory.Schema;

/// <summary>
/// Comprehensive Active Directory schema attributes A-L per [MS-ADA1].
/// Reference: https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-ada1/
/// </summary>
public static class SchemaAttributes_A_L
{
    public static IEnumerable<AttributeSchemaEntry> GetAttributes()
    {
        // =====================================================================
        // A - Attributes
        // =====================================================================

        // Account-Expires
        yield return Attr("accountExpires", "1.2.840.113556.1.4.159", "2.5.5.16", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // Account-Name-History
        yield return Attr("accountNameHistory", "1.2.840.113556.1.4.1307", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: false);

        // ACS attributes (QoS Admission Control)
        yield return Attr("aCSAggregateTokenRatePerUser", "1.2.840.113556.1.4.760", "2.5.5.16", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("aCSAllocableRSVPBandwidth", "1.2.840.113556.1.4.774", "2.5.5.16", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("aCSCacheTimeout", "1.2.840.113556.1.4.761", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("aCSDirection", "1.2.840.113556.1.4.757", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("aCSDBSMDeadTime", "1.2.840.113556.1.4.770", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("aCSDBSMPriority", "1.2.840.113556.1.4.769", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("aCSDBSMRefresh", "1.2.840.113556.1.4.771", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("aCSEnableACSService", "1.2.840.113556.1.4.766", "2.5.5.8", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("aCSEnableRSVPAccounting", "1.2.840.113556.1.4.776", "2.5.5.8", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("aCSEnableRSVPMessageLogging", "1.2.840.113556.1.4.767", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("aCSEventLogLevel", "1.2.840.113556.1.4.768", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("aCSIdentityName", "1.2.840.113556.1.4.756", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("aCSMaxAggregatePeakRatePerUser", "1.2.840.113556.1.4.759", "2.5.5.16", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("aCSMaxDurationPerFlow", "1.2.840.113556.1.4.762", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("aCSMaximumSDUSize", "1.2.840.113556.1.4.788", "2.5.5.16", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("aCSMaxNoOfAccountFiles", "1.2.840.113556.1.4.778", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("aCSMaxNoOfLogFiles", "1.2.840.113556.1.4.773", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("aCSMaxPeakBandwidth", "1.2.840.113556.1.4.786", "2.5.5.16", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("aCSMaxPeakBandwidthPerFlow", "1.2.840.113556.1.4.758", "2.5.5.16", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("aCSMaxSizeOfRSVPAccountFile", "1.2.840.113556.1.4.779", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("aCSMaxSizeOfRSVPLogFile", "1.2.840.113556.1.4.772", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("aCSMaxTokenBucketPerFlow", "1.2.840.113556.1.4.789", "2.5.5.16", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("aCSMaxTokenRatePerFlow", "1.2.840.113556.1.4.763", "2.5.5.16", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("aCSMinimumDelayVariation", "1.2.840.113556.1.4.791", "2.5.5.16", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("aCSMinimumLatency", "1.2.840.113556.1.4.790", "2.5.5.16", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("aCSMinimumPolicedSize", "1.2.840.113556.1.4.792", "2.5.5.16", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("aCSNonReservedMaxSDUSize", "1.2.840.113556.1.4.784", "2.5.5.16", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("aCSNonReservedMinPolicedSize", "1.2.840.113556.1.4.785", "2.5.5.16", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("aCSNonReservedPeakRate", "1.2.840.113556.1.4.783", "2.5.5.16", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("aCSNonReservedTokenSize", "1.2.840.113556.1.4.782", "2.5.5.16", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("aCSNonReservedTxLimit", "1.2.840.113556.1.4.780", "2.5.5.16", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("aCSNonReservedTxSize", "1.2.840.113556.1.4.781", "2.5.5.16", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("aCSPermissionBits", "1.2.840.113556.1.4.764", "2.5.5.16", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("aCSPolicyName", "1.2.840.113556.1.4.755", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("aCSPriority", "1.2.840.113556.1.4.754", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("aCSRSVPAccountFilesLocation", "1.2.840.113556.1.4.777", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("aCSRSVPLogFilesLocation", "1.2.840.113556.1.4.775", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("aCSServerList", "1.2.840.113556.1.4.765", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: false);
        yield return Attr("aCSServiceType", "1.2.840.113556.1.4.787", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("aCSTimeOfDay", "1.2.840.113556.1.4.753", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("aCSTotalNoOfFlows", "1.2.840.113556.1.4.793", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);

        // Additional-Information (notes)
        yield return Attr("notes", "1.2.840.113556.1.2.218", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false, rangeLower: 0, rangeUpper: 1024);
        // Additional-Trusted-Service-Names
        yield return Attr("additionalTrustedServiceNames", "1.2.840.113556.1.4.889", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: false);
        // Address (streetAddress)
        yield return Attr("streetAddress", "2.5.4.9", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false, rangeLower: 1, rangeUpper: 1024);
        // Address-Book-Roots
        yield return Attr("addressBookRoots", "1.2.840.113556.1.4.1244", "2.5.5.1", singleValued: false, gc: false, systemOnly: false, indexed: false);
        // Address-Book-Roots2
        yield return Attr("addressBookRoots2", "1.2.840.113556.1.4.2046", "2.5.5.1", singleValued: false, gc: false, systemOnly: false, indexed: false);
        // Address-Entry-Display-Table
        yield return Attr("addressEntryDisplayTable", "1.2.840.113556.1.2.334", "2.5.5.10", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // Address-Entry-Display-Table-MSDOS
        yield return Attr("addressEntryDisplayTableMSDOS", "1.2.840.113556.1.2.400", "2.5.5.10", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // Address-Home (homePostalAddress)
        yield return Attr("homePostalAddress", "2.5.4.39", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: false);
        // Address-Syntax
        yield return Attr("addressSyntax", "1.2.840.113556.1.2.255", "2.5.5.10", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // Address-Type
        yield return Attr("addressType", "1.2.840.113556.1.2.349", "2.5.5.5", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // Admin-Context-Menu
        yield return Attr("adminContextMenu", "1.2.840.113556.1.4.614", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: false);
        // Admin-Count
        yield return Attr("adminCount", "1.2.840.113556.1.4.150", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // Admin-Description
        yield return Attr("adminDescription", "1.2.840.113556.1.2.226", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false, rangeLower: 0, rangeUpper: 1024);
        // Admin-Display-Name
        yield return Attr("adminDisplayName", "1.2.840.113556.1.2.194", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false, rangeLower: 0, rangeUpper: 256);
        // Admin-Multiselect-Property-Pages
        yield return Attr("adminMultiselectPropertyPages", "1.2.840.113556.1.4.616", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: false);
        // Admin-Property-Pages
        yield return Attr("adminPropertyPages", "1.2.840.113556.1.4.559", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: false);
        // Allowed-Attributes (constructed)
        yield return Attr("allowedAttributes", "1.2.840.113556.1.4.913", "2.5.5.12", singleValued: false, gc: false, systemOnly: true, indexed: false);
        // Allowed-Attributes-Effective (constructed)
        yield return Attr("allowedAttributesEffective", "1.2.840.113556.1.4.914", "2.5.5.12", singleValued: false, gc: false, systemOnly: true, indexed: false);
        // Allowed-Child-Classes (constructed)
        yield return Attr("allowedChildClasses", "1.2.840.113556.1.4.911", "2.5.5.2", singleValued: false, gc: false, systemOnly: true, indexed: false);
        // Allowed-Child-Classes-Effective (constructed)
        yield return Attr("allowedChildClassesEffective", "1.2.840.113556.1.4.912", "2.5.5.2", singleValued: false, gc: false, systemOnly: true, indexed: false);
        // Alt-Security-Identities
        yield return Attr("altSecurityIdentities", "1.2.840.113556.1.4.867", "2.5.5.12", singleValued: false, gc: true, systemOnly: false, indexed: true);
        // ANR (constructed)
        yield return Attr("aNR", "1.2.840.113556.1.4.1208", "2.5.5.12", singleValued: true, gc: false, systemOnly: true, indexed: false);
        // Application-Name
        yield return Attr("applicationName", "1.2.840.113556.1.4.218", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // Applies-To
        yield return Attr("appliesTo", "1.2.840.113556.1.4.341", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: false);
        // App-Schema-Version
        yield return Attr("appSchemaVersion", "1.2.840.113556.1.4.848", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // Asset-Number
        yield return Attr("assetNumber", "1.2.840.113556.1.4.283", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // Assistant
        yield return Attr("assistant", "1.2.840.113556.1.2.444", "2.5.5.1", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // associatedDomain
        yield return Attr("associatedDomain", "0.9.2342.19200300.100.1.37", "2.5.5.5", singleValued: false, gc: false, systemOnly: false, indexed: false);
        // associatedName
        yield return Attr("associatedName", "0.9.2342.19200300.100.1.38", "2.5.5.1", singleValued: false, gc: false, systemOnly: false, indexed: false);
        // Assoc-NT-Account
        yield return Attr("assocNTAccount", "1.2.840.113556.1.4.330", "2.5.5.10", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // attributeCertificateAttribute
        yield return Attr("attributeCertificateAttribute", "2.5.4.58", "2.5.5.10", singleValued: false, gc: false, systemOnly: false, indexed: false);
        // Attribute-Display-Names
        yield return Attr("attributeDisplayNames", "1.2.840.113556.1.4.748", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: false);
        // Attribute-ID
        yield return Attr("attributeID", "1.2.840.113556.1.2.30", "2.5.5.2", singleValued: true, gc: false, systemOnly: false, indexed: true);
        // Attribute-Security-GUID
        yield return Attr("attributeSecurityGUID", "1.2.840.113556.1.4.149", "2.5.5.10", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // Attribute-Syntax
        yield return Attr("attributeSyntax", "1.2.840.113556.1.2.32", "2.5.5.2", singleValued: true, gc: false, systemOnly: true, indexed: false);
        // Attribute-Types
        yield return Attr("attributeTypes", "2.5.21.5", "2.5.5.12", singleValued: false, gc: false, systemOnly: true, indexed: false);
        // audio
        yield return Attr("audio", "0.9.2342.19200300.100.1.55", "2.5.5.10", singleValued: false, gc: false, systemOnly: false, indexed: false);
        // Auditing-Policy
        yield return Attr("auditingPolicy", "1.2.840.113556.1.4.202", "2.5.5.10", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // Authentication-Options
        yield return Attr("authenticationOptions", "1.2.840.113556.1.4.10", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // Authority-Revocation-List
        yield return Attr("authorityRevocationList", "2.5.4.38", "2.5.5.10", singleValued: false, gc: false, systemOnly: false, indexed: false);
        // Auxiliary-Class
        yield return Attr("auxiliaryClass", "1.2.840.113556.1.2.351", "2.5.5.2", singleValued: false, gc: false, systemOnly: false, indexed: false);

        // =====================================================================
        // B - Attributes
        // =====================================================================

        // Bad-Password-Time
        yield return Attr("badPasswordTime", "1.2.840.113556.1.4.49", "2.5.5.16", singleValued: true, gc: false, systemOnly: true, indexed: false);
        // Bad-Pwd-Count
        yield return Attr("badPwdCount", "1.2.840.113556.1.4.12", "2.5.5.9", singleValued: true, gc: false, systemOnly: true, indexed: false);
        // Birth-Location
        yield return Attr("birthLocation", "1.2.840.113556.1.4.332", "2.5.5.10", singleValued: true, gc: false, systemOnly: true, indexed: false);
        // bootFile
        yield return Attr("bootFile", "1.3.6.1.1.1.1.24", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: false);
        // bootParameter
        yield return Attr("bootParameter", "1.3.6.1.1.1.1.23", "2.5.5.5", singleValued: false, gc: false, systemOnly: false, indexed: false);
        // Bridgehead-Server-List-BL
        yield return Attr("bridgeheadServerListBL", "1.2.840.113556.1.4.820", "2.5.5.1", singleValued: false, gc: false, systemOnly: true, indexed: false);
        // Bridgehead-Transport-List
        yield return Attr("bridgeheadTransportList", "1.2.840.113556.1.4.819", "2.5.5.1", singleValued: false, gc: false, systemOnly: false, indexed: false);
        // buildingName
        yield return Attr("buildingName", "0.9.2342.19200300.100.1.48", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: false);
        // Builtin-Creation-Time
        yield return Attr("builtinCreationTime", "1.2.840.113556.1.4.13", "2.5.5.16", singleValued: true, gc: false, systemOnly: true, indexed: false);
        // Builtin-Modified-Count
        yield return Attr("builtinModifiedCount", "1.2.840.113556.1.4.14", "2.5.5.16", singleValued: true, gc: false, systemOnly: true, indexed: false);
        // Business-Category
        yield return Attr("businessCategory", "2.5.4.15", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: false, rangeLower: 1, rangeUpper: 128);
        // Bytes-Per-Minute
        yield return Attr("bytesPerMinute", "1.2.840.113556.1.4.284", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);

        // =====================================================================
        // C - Attributes
        // =====================================================================

        // CA-Certificate
        yield return Attr("cACertificate", "2.5.4.37", "2.5.5.10", singleValued: false, gc: false, systemOnly: false, indexed: false);
        // CA-Certificate-DN
        yield return Attr("cACertificateDN", "1.2.840.113556.1.4.697", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // CA-Connect
        yield return Attr("cAConnect", "1.2.840.113556.1.4.687", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // Canonical-Name (constructed)
        yield return Attr("canonicalName", "1.2.840.113556.1.4.916", "2.5.5.12", singleValued: false, gc: true, systemOnly: true, indexed: false);
        // Can-Upgrade-Script
        yield return Attr("canUpgradeScript", "1.2.840.113556.1.4.815", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: false);
        // carLicense
        yield return Attr("carLicense", "2.16.840.1.113730.3.1.1", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: false);
        // Catalogs
        yield return Attr("catalogs", "1.2.840.113556.1.4.675", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: false);
        // Categories
        yield return Attr("categories", "1.2.840.113556.1.4.274", "2.5.5.10", singleValued: false, gc: false, systemOnly: false, indexed: false);
        // Category-Id
        yield return Attr("categoryId", "1.2.840.113556.1.4.322", "2.5.5.10", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // CA-Usages
        yield return Attr("cAUsages", "1.2.840.113556.1.4.690", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // CA-WEB-URL
        yield return Attr("cAWEBURL", "1.2.840.113556.1.4.688", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: false);
        // Certificate-Authority-Object
        yield return Attr("certificateAuthorityObject", "1.2.840.113556.1.4.684", "2.5.5.1", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // Certificate-Revocation-List
        yield return Attr("certificateRevocationList", "2.5.4.39", "2.5.5.10", singleValued: false, gc: false, systemOnly: false, indexed: false);
        // Certificate-Templates
        yield return Attr("certificateTemplates", "1.2.840.113556.1.4.823", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: false);
        // Class-Display-Name
        yield return Attr("classDisplayName", "1.2.840.113556.1.4.610", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // cn (Common-Name)
        yield return Attr("cn", "2.5.4.3", "2.5.5.12", singleValued: true, gc: true, systemOnly: false, indexed: true, rangeLower: 1, rangeUpper: 64);
        // Code-Page
        yield return Attr("codePage", "1.2.840.113556.1.4.16", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // COM-ClassID
        yield return Attr("cOMClassID", "1.2.840.113556.1.4.18", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // COM-CLSID
        yield return Attr("cOMCLSID", "1.2.840.113556.1.4.1420", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // COM-InterfaceID
        yield return Attr("cOMInterfaceID", "1.2.840.113556.1.4.17", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: false);
        // Comment (info)
        yield return Attr("info", "1.2.840.113556.1.2.81", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false, rangeLower: 0, rangeUpper: 1024);
        // COM-Other-Prog-Id
        yield return Attr("cOMOtherProgId", "1.2.840.113556.1.4.1421", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: false);
        // Company
        yield return Attr("company", "1.2.840.113556.1.2.146", "2.5.5.12", singleValued: true, gc: true, systemOnly: false, indexed: false, rangeLower: 1, rangeUpper: 64);
        // COM-ProgID
        yield return Attr("cOMProgID", "1.2.840.113556.1.4.19", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: false);
        // COM-Treat-As-Class-Id
        yield return Attr("cOMTreatAsClassId", "1.2.840.113556.1.4.20", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // COM-Typelib-Id
        yield return Attr("cOMTypelibId", "1.2.840.113556.1.4.21", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // COM-Unique-LIBID
        yield return Attr("cOMUniqueLIBID", "1.2.840.113556.1.4.22", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // Content-Indexing-Allowed
        yield return Attr("contentIndexingAllowed", "1.2.840.113556.1.4.1417", "2.5.5.8", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // Context-Menu
        yield return Attr("contextMenu", "1.2.840.113556.1.4.553", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: false);
        // Control-Access-Rights
        yield return Attr("controlAccessRights", "1.2.840.113556.1.4.200", "2.5.5.10", singleValued: false, gc: false, systemOnly: false, indexed: false);
        // Cost
        yield return Attr("cost", "1.2.840.113556.1.2.133", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // Country-Code
        yield return Attr("countryCode", "1.2.840.113556.1.4.25", "2.5.5.9", singleValued: true, gc: true, systemOnly: false, indexed: false);
        // Country-Name (c)
        yield return Attr("c", "2.5.4.6", "2.5.5.12", singleValued: true, gc: true, systemOnly: false, indexed: false, rangeLower: 0, rangeUpper: 2);
        // Create-Dialog
        yield return Attr("createDialog", "1.2.840.113556.1.4.812", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // Create-Time-Stamp (constructed)
        yield return Attr("createTimeStamp", "2.5.18.1", "2.5.5.11", singleValued: true, gc: true, systemOnly: true, indexed: false);
        // Create-Wizard-Ext
        yield return Attr("createWizardExt", "1.2.840.113556.1.4.813", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: false);
        // Creation-Time
        yield return Attr("creationTime", "1.2.840.113556.1.4.26", "2.5.5.16", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // Creation-Wizard
        yield return Attr("creationWizard", "1.2.840.113556.1.4.498", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // Creator
        yield return Attr("creator", "1.2.840.113556.1.4.668", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // CRL-Object
        yield return Attr("cRLObject", "1.2.840.113556.1.4.685", "2.5.5.1", singleValued: false, gc: false, systemOnly: false, indexed: false);
        // CRL-Partitioned-Revocation-List
        yield return Attr("cRLPartitionedRevocationList", "1.2.840.113556.1.4.720", "2.5.5.10", singleValued: false, gc: false, systemOnly: false, indexed: false);
        // Cross-Certificate-Pair
        yield return Attr("crossCertificatePair", "2.5.4.40", "2.5.5.10", singleValued: false, gc: false, systemOnly: false, indexed: false);
        // Current-Location
        yield return Attr("currentLocation", "1.2.840.113556.1.4.334", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // Current-Parent-CA
        yield return Attr("currentParentCA", "1.2.840.113556.1.4.696", "2.5.5.1", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // Current-Value
        yield return Attr("currentValue", "1.2.840.113556.1.4.27", "2.5.5.10", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // Curr-Machine-Id
        yield return Attr("currMachineId", "1.2.840.113556.1.4.338", "2.5.5.10", singleValued: true, gc: false, systemOnly: false, indexed: false);

        // =====================================================================
        // D - Attributes
        // =====================================================================

        // DBCS-Pwd
        yield return Attr("dBCSPwd", "1.2.840.113556.1.4.55", "2.5.5.10", singleValued: true, gc: false, systemOnly: true, indexed: false);
        // Default-Class-Store
        yield return Attr("defaultClassStore", "1.2.840.113556.1.4.213", "2.5.5.1", singleValued: false, gc: false, systemOnly: false, indexed: false);
        // Default-Group
        yield return Attr("defaultGroup", "1.2.840.113556.1.4.311", "2.5.5.1", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // Default-Hiding-Value
        yield return Attr("defaultHidingValue", "1.2.840.113556.1.4.518", "2.5.5.8", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // Default-Local-Policy-Object
        yield return Attr("defaultLocalPolicyObject", "1.2.840.113556.1.4.57", "2.5.5.1", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // Default-Object-Category
        yield return Attr("defaultObjectCategory", "1.2.840.113556.1.4.783", "2.5.5.1", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // Default-Priority
        yield return Attr("defaultPriority", "1.2.840.113556.1.4.273", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // Default-Security-Descriptor
        yield return Attr("defaultSecurityDescriptor", "1.2.840.113556.1.4.224", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // Delta-Revocation-List
        yield return Attr("deltaRevocationList", "2.5.4.53", "2.5.5.10", singleValued: false, gc: false, systemOnly: false, indexed: false);
        // Department
        yield return Attr("department", "2.5.4.11", "2.5.5.12", singleValued: true, gc: true, systemOnly: false, indexed: false, rangeLower: 1, rangeUpper: 64);
        // departmentNumber
        yield return Attr("departmentNumber", "2.16.840.1.113730.3.1.2", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: false);
        // Description
        yield return Attr("description", "2.5.4.13", "2.5.5.12", singleValued: false, gc: true, systemOnly: false, indexed: false, rangeLower: 0, rangeUpper: 1024);
        // Desktop-Profile
        yield return Attr("desktopProfile", "1.2.840.113556.1.4.346", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false, rangeLower: 1, rangeUpper: 260);
        // Destination-Indicator
        yield return Attr("destinationIndicator", "2.5.4.27", "2.5.5.5", singleValued: false, gc: false, systemOnly: false, indexed: false, rangeLower: 1, rangeUpper: 128);

        // DHCP attributes
        yield return Attr("dhcpClasses", "1.2.840.113556.1.4.714", "2.5.5.10", singleValued: false, gc: false, systemOnly: false, indexed: false);
        yield return Attr("dhcpFlags", "1.2.840.113556.1.4.700", "2.5.5.16", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("dhcpIdentification", "1.2.840.113556.1.4.699", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("dhcpMask", "1.2.840.113556.1.4.710", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: false);
        yield return Attr("dhcpMaxKey", "1.2.840.113556.1.4.717", "2.5.5.16", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("dhcpObjDescription", "1.2.840.113556.1.4.702", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("dhcpObjName", "1.2.840.113556.1.4.701", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("dhcpOptions", "1.2.840.113556.1.4.712", "2.5.5.10", singleValued: false, gc: false, systemOnly: false, indexed: false);
        yield return Attr("dhcpProperties", "1.2.840.113556.1.4.716", "2.5.5.10", singleValued: false, gc: false, systemOnly: false, indexed: false);
        yield return Attr("dhcpRanges", "1.2.840.113556.1.4.711", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: false);
        yield return Attr("dhcpReservations", "1.2.840.113556.1.4.713", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: false);
        yield return Attr("dhcpServers", "1.2.840.113556.1.4.705", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: false);
        yield return Attr("dhcpSites", "1.2.840.113556.1.4.706", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: false);
        yield return Attr("dhcpState", "1.2.840.113556.1.4.715", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: false);
        yield return Attr("dhcpSubnets", "1.2.840.113556.1.4.707", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: false);
        yield return Attr("dhcpType", "1.2.840.113556.1.4.703", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("dhcpUniqueKey", "1.2.840.113556.1.4.704", "2.5.5.16", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("dhcpUpdateTime", "1.2.840.113556.1.4.708", "2.5.5.16", singleValued: true, gc: false, systemOnly: false, indexed: false);

        // Direct-Reports
        yield return Attr("directReports", "1.2.840.113556.1.4.1397", "2.5.5.1", singleValued: false, gc: true, systemOnly: true, indexed: false);
        // Display-Name
        yield return Attr("displayName", "1.2.840.113556.1.2.13", "2.5.5.12", singleValued: true, gc: true, systemOnly: false, indexed: true, rangeLower: 0, rangeUpper: 256);
        // Display-Name-Printable
        yield return Attr("displayNamePrintable", "1.2.840.113556.1.2.353", "2.5.5.5", singleValued: true, gc: true, systemOnly: false, indexed: true, rangeLower: 1, rangeUpper: 256);
        // Distinguished-Name (constructed)
        yield return Attr("distinguishedName", "2.5.4.49", "2.5.5.1", singleValued: true, gc: true, systemOnly: true, indexed: false);
        // DIT-Content-Rules
        yield return Attr("dITContentRules", "2.5.21.2", "2.5.5.12", singleValued: false, gc: false, systemOnly: true, indexed: false);
        // Division
        yield return Attr("division", "1.2.840.113556.1.4.261", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false, rangeLower: 0, rangeUpper: 256);
        // DMD-Location
        yield return Attr("dMDLocation", "1.2.840.113556.1.2.36", "2.5.5.1", singleValued: true, gc: false, systemOnly: true, indexed: false);
        // DMD-Name
        yield return Attr("dMDName", "2.5.15.0", "2.5.5.12", singleValued: true, gc: false, systemOnly: true, indexed: false);
        // DN-Reference-Update
        yield return Attr("dNReferenceUpdate", "1.2.840.113556.1.4.1242", "2.5.5.1", singleValued: false, gc: false, systemOnly: true, indexed: false);

        // DNS attributes
        yield return Attr("dnsAllowDynamic", "1.2.840.113556.1.4.377", "2.5.5.8", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("dnsAllowXFR", "1.2.840.113556.1.4.378", "2.5.5.8", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("dNSHostName", "1.2.840.113556.1.4.619", "2.5.5.12", singleValued: true, gc: true, systemOnly: false, indexed: true, rangeLower: 0, rangeUpper: 2048);
        yield return Attr("dnsNotifySecondaries", "1.2.840.113556.1.4.379", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("dNSProperty", "1.2.840.113556.1.4.1306", "2.5.5.10", singleValued: false, gc: false, systemOnly: false, indexed: false);
        yield return Attr("dnsRecord", "1.2.840.113556.1.4.382", "2.5.5.10", singleValued: false, gc: false, systemOnly: false, indexed: false);
        yield return Attr("dnsRoot", "1.2.840.113556.1.4.28", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: true);
        yield return Attr("dnsSecureSecondaries", "1.2.840.113556.1.4.380", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("dNSTombstoned", "1.2.840.113556.1.4.383", "2.5.5.8", singleValued: true, gc: false, systemOnly: false, indexed: false);

        // Document attributes
        yield return Attr("documentAuthor", "0.9.2342.19200300.100.1.14", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: false);
        yield return Attr("documentIdentifier", "0.9.2342.19200300.100.1.11", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: false);
        yield return Attr("documentLocation", "0.9.2342.19200300.100.1.15", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: false);
        yield return Attr("documentPublisher", "0.9.2342.19200300.100.1.56", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("documentTitle", "0.9.2342.19200300.100.1.12", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: false);
        yield return Attr("documentVersion", "0.9.2342.19200300.100.1.13", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: false);

        // Domain attributes
        yield return Attr("domainCAs", "1.2.840.113556.1.4.672", "2.5.5.1", singleValued: false, gc: false, systemOnly: false, indexed: false);
        yield return Attr("dc", "0.9.2342.19200300.100.1.25", "2.5.5.12", singleValued: true, gc: true, systemOnly: false, indexed: true);
        yield return Attr("domainCrossRef", "1.2.840.113556.1.4.474", "2.5.5.1", singleValued: true, gc: false, systemOnly: true, indexed: false);
        yield return Attr("domainID", "1.2.840.113556.1.4.148", "2.5.5.1", singleValued: true, gc: false, systemOnly: true, indexed: false);
        yield return Attr("domainIdentifier", "1.2.840.113556.1.4.690", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("domainPolicyObject", "1.2.840.113556.1.4.32", "2.5.5.1", singleValued: true, gc: false, systemOnly: true, indexed: false);
        yield return Attr("domainPolicyReference", "1.2.840.113556.1.4.422", "2.5.5.1", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("domainReplica", "1.2.840.113556.1.4.33", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("domainWidePolicy", "1.2.840.113556.1.4.421", "2.5.5.10", singleValued: false, gc: false, systemOnly: false, indexed: false);
        // drink (favoriteDrink)
        yield return Attr("drink", "0.9.2342.19200300.100.1.5", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: false);
        // Driver-Name
        yield return Attr("driverName", "1.2.840.113556.1.4.272", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // Driver-Version
        yield return Attr("driverVersion", "1.2.840.113556.1.4.277", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // DSA-Signature
        yield return Attr("dSASignature", "1.2.840.113556.1.2.74", "2.5.5.10", singleValued: true, gc: false, systemOnly: true, indexed: false);
        // DS-Core-Propagation-Data
        yield return Attr("dSCorePropagationData", "1.2.840.113556.1.4.1357", "2.5.5.11", singleValued: false, gc: false, systemOnly: true, indexed: false);
        // DS-Heuristics
        yield return Attr("dSHeuristics", "1.2.840.113556.1.2.212", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // DS-UI-Admin-Maximum
        yield return Attr("dSUIAdminMaximum", "1.2.840.113556.1.4.1345", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // DS-UI-Admin-Notification
        yield return Attr("dSUIAdminNotification", "1.2.840.113556.1.4.1344", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: false);
        // DS-UI-Shell-Maximum
        yield return Attr("dSUIShellMaximum", "1.2.840.113556.1.4.1346", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // Dynamic-LDAP-Server
        yield return Attr("dynamicLDAPServer", "1.2.840.113556.1.4.537", "2.5.5.1", singleValued: true, gc: false, systemOnly: false, indexed: false);

        // =====================================================================
        // E - Attributes
        // =====================================================================

        // EFSPolicy
        yield return Attr("eFSPolicy", "1.2.840.113556.1.4.264", "2.5.5.10", singleValued: false, gc: false, systemOnly: false, indexed: false);
        // E-mail-Addresses (mail)
        yield return Attr("mail", "0.9.2342.19200300.100.1.3", "2.5.5.12", singleValued: true, gc: true, systemOnly: false, indexed: true, rangeLower: 0, rangeUpper: 256);
        // Employee-ID
        yield return Attr("employeeID", "1.2.840.113556.1.4.35", "2.5.5.12", singleValued: true, gc: true, systemOnly: false, indexed: false);
        // Employee-Number
        yield return Attr("employeeNumber", "2.16.840.1.113730.3.1.3", "2.5.5.12", singleValued: true, gc: true, systemOnly: false, indexed: false);
        // Employee-Type
        yield return Attr("employeeType", "2.16.840.1.113730.3.1.4", "2.5.5.12", singleValued: true, gc: true, systemOnly: false, indexed: false);
        // Enabled
        yield return Attr("enabled", "1.2.840.113556.1.4.10", "2.5.5.8", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // Enabled-Connection
        yield return Attr("enabledConnection", "1.2.840.113556.1.4.36", "2.5.5.8", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // Enrollment-Providers
        yield return Attr("enrollmentProviders", "1.2.840.113556.1.4.826", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: false);
        // Entry-TTL (constructed)
        yield return Attr("entryTTL", "1.3.6.1.4.1.1466.101.119.3", "2.5.5.9", singleValued: true, gc: false, systemOnly: true, indexed: false);
        // Extended-Attribute-Info (constructed)
        yield return Attr("extendedAttributeInfo", "1.2.840.113556.1.4.909", "2.5.5.12", singleValued: false, gc: false, systemOnly: true, indexed: false);
        // Extended-Chars-Allowed
        yield return Attr("extendedCharsAllowed", "1.2.840.113556.1.2.380", "2.5.5.8", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // Extended-Class-Info (constructed)
        yield return Attr("extendedClassInfo", "1.2.840.113556.1.4.908", "2.5.5.12", singleValued: false, gc: false, systemOnly: true, indexed: false);
        // Extension-Name
        yield return Attr("extensionName", "1.2.840.113556.1.2.227", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: false);
        // Extra-Columns
        yield return Attr("extraColumns", "1.2.840.113556.1.4.1602", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: false);

        // =====================================================================
        // F - Attributes
        // =====================================================================

        // Facsimile-Telephone-Number
        yield return Attr("facsimileTelephoneNumber", "2.5.4.23", "2.5.5.12", singleValued: true, gc: true, systemOnly: false, indexed: false, rangeLower: 1, rangeUpper: 64);
        // File-Ext-Priority
        yield return Attr("fileExtPriority", "1.2.840.113556.1.4.270", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: false);
        // Flags
        yield return Attr("flags", "1.2.840.113556.1.4.38", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // Flat-Name
        yield return Attr("flatName", "1.2.840.113556.1.4.509", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // Force-Logoff
        yield return Attr("forceLogoff", "1.2.840.113556.1.4.39", "2.5.5.16", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // Foreign-Identifier
        yield return Attr("foreignIdentifier", "1.2.840.113556.1.4.356", "2.5.5.10", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // Friendly-Names
        yield return Attr("friendlyNames", "1.2.840.113556.1.4.682", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: false);
        // From-Entry
        yield return Attr("fromEntry", "1.2.840.113556.1.4.910", "2.5.5.8", singleValued: true, gc: false, systemOnly: true, indexed: false);
        // From-Server
        yield return Attr("fromServer", "1.2.840.113556.1.4.40", "2.5.5.1", singleValued: true, gc: false, systemOnly: false, indexed: false);

        // FRS attributes
        yield return Attr("fRSComputerReference", "1.2.840.113556.1.4.869", "2.5.5.1", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("fRSComputerReferenceBL", "1.2.840.113556.1.4.870", "2.5.5.1", singleValued: false, gc: false, systemOnly: true, indexed: false);
        yield return Attr("fRSControlDataCreation", "1.2.840.113556.1.4.871", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("fRSControlInboundBacklog", "1.2.840.113556.1.4.872", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("fRSControlOutboundBacklog", "1.2.840.113556.1.4.873", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("fRSDirectoryFilter", "1.2.840.113556.1.4.484", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("fRSDSPoll", "1.2.840.113556.1.4.490", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("fRSExtensions", "1.2.840.113556.1.4.491", "2.5.5.10", singleValued: false, gc: false, systemOnly: false, indexed: false);
        yield return Attr("fRSFaultCondition", "1.2.840.113556.1.4.492", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("fRSFileFilter", "1.2.840.113556.1.4.483", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("fRSFlags", "1.2.840.113556.1.4.874", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("fRSLevelLimit", "1.2.840.113556.1.4.493", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("fRSMemberReference", "1.2.840.113556.1.4.875", "2.5.5.1", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("fRSMemberReferenceBL", "1.2.840.113556.1.4.876", "2.5.5.1", singleValued: false, gc: false, systemOnly: true, indexed: false);
        yield return Attr("fRSPartnerAuthLevel", "1.2.840.113556.1.4.494", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("fRSPrimaryMember", "1.2.840.113556.1.4.877", "2.5.5.1", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("fRSReplicaSetGUID", "1.2.840.113556.1.4.878", "2.5.5.10", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("fRSReplicaSetType", "1.2.840.113556.1.4.488", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("fRSRootPath", "1.2.840.113556.1.4.487", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("fRSRootSecurity", "1.2.840.113556.1.4.485", "2.5.5.15", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("fRSServiceCommand", "1.2.840.113556.1.4.495", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("fRSServiceCommandStatus", "1.2.840.113556.1.4.496", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("fRSStagingPath", "1.2.840.113556.1.4.486", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("fRSTimeLastCommand", "1.2.840.113556.1.4.497", "2.5.5.11", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("fRSTimeLastConfigChange", "1.2.840.113556.1.4.498", "2.5.5.11", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("fRSUpdateTimeout", "1.2.840.113556.1.4.489", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("fRSVersion", "1.2.840.113556.1.4.499", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("fRSVersionGUID", "1.2.840.113556.1.4.879", "2.5.5.10", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("fRSWorkingPath", "1.2.840.113556.1.4.481", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);

        // FSMO-Role-Owner
        yield return Attr("fSMORoleOwner", "1.2.840.113556.1.4.515", "2.5.5.1", singleValued: true, gc: false, systemOnly: true, indexed: false);

        // =====================================================================
        // G - Attributes
        // =====================================================================

        // Garbage-Coll-Period
        yield return Attr("garbageCollPeriod", "1.2.840.113556.1.4.42", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // gecos
        yield return Attr("gecos", "1.3.6.1.1.1.1.2", "2.5.5.5", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // Generated-Connection
        yield return Attr("generatedConnection", "1.2.840.113556.1.4.43", "2.5.5.8", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // Generation-Qualifier
        yield return Attr("generationQualifier", "2.5.4.44", "2.5.5.12", singleValued: true, gc: true, systemOnly: false, indexed: false, rangeLower: 1, rangeUpper: 128);
        // gidNumber
        yield return Attr("gidNumber", "1.3.6.1.1.1.1.1", "2.5.5.9", singleValued: true, gc: true, systemOnly: false, indexed: true);
        // Given-Name
        yield return Attr("givenName", "2.5.4.42", "2.5.5.12", singleValued: true, gc: true, systemOnly: false, indexed: true, rangeLower: 1, rangeUpper: 64);
        // Global-Address-List
        yield return Attr("globalAddressList", "1.2.840.113556.1.4.1245", "2.5.5.1", singleValued: false, gc: false, systemOnly: false, indexed: false);
        // Global-Address-List2
        yield return Attr("globalAddressList2", "1.2.840.113556.1.4.2047", "2.5.5.1", singleValued: false, gc: false, systemOnly: false, indexed: false);
        // Governs-ID
        yield return Attr("governsID", "1.2.840.113556.1.2.22", "2.5.5.2", singleValued: true, gc: false, systemOnly: true, indexed: true);
        // GPC-File-Sys-Path
        yield return Attr("gPCFileSysPath", "1.2.840.113556.1.4.894", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // GPC-Functionality-Version
        yield return Attr("gPCFunctionalityVersion", "1.2.840.113556.1.4.893", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // GPC-Machine-Extension-Names
        yield return Attr("gPCMachineExtensionNames", "1.2.840.113556.1.4.1348", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // GPC-User-Extension-Names
        yield return Attr("gPCUserExtensionNames", "1.2.840.113556.1.4.1349", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // GPC-WQL-Filter
        yield return Attr("gPCWQLFilter", "1.2.840.113556.1.4.1694", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // GP-Link
        yield return Attr("gPLink", "1.2.840.113556.1.4.891", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // GP-Options
        yield return Attr("gPOptions", "1.2.840.113556.1.4.892", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // Group-Attributes
        yield return Attr("groupAttributes", "1.2.840.113556.1.4.152", "2.5.5.9", singleValued: true, gc: false, systemOnly: true, indexed: false);
        // Group-Membership-SAM
        yield return Attr("groupMembershipSAM", "1.2.840.113556.1.4.166", "2.5.5.10", singleValued: true, gc: false, systemOnly: true, indexed: false);
        // Group-Priority
        yield return Attr("groupPriority", "1.2.840.113556.1.4.345", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: false);
        // Groups-to-Ignore
        yield return Attr("groupsToIgnore", "1.2.840.113556.1.4.344", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: false);
        // Group-Type
        yield return Attr("groupType", "1.2.840.113556.1.4.750", "2.5.5.9", singleValued: true, gc: true, systemOnly: false, indexed: true);

        // =====================================================================
        // H - Attributes
        // =====================================================================

        // Has-Master-NCs
        yield return Attr("hasMasterNCs", "1.2.840.113556.1.2.14", "2.5.5.1", singleValued: false, gc: false, systemOnly: true, indexed: false);
        // Has-Partial-Replica-NCs
        yield return Attr("hasPartialReplicaNCs", "1.2.840.113556.1.4.173", "2.5.5.1", singleValued: false, gc: false, systemOnly: true, indexed: false);
        // Help-Data16
        yield return Attr("helpData16", "1.2.840.113556.1.4.672", "2.5.5.10", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // Help-Data32
        yield return Attr("helpData32", "1.2.840.113556.1.4.673", "2.5.5.10", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // Help-File-Name
        yield return Attr("helpFileName", "1.2.840.113556.1.4.674", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // Hide-From-AB
        yield return Attr("hideFromAB", "1.2.840.113556.1.2.553", "2.5.5.8", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // Home-Directory
        yield return Attr("homeDirectory", "1.2.840.113556.1.4.44", "2.5.5.12", singleValued: true, gc: true, systemOnly: false, indexed: false, rangeLower: 0, rangeUpper: 260);
        // Home-Drive
        yield return Attr("homeDrive", "1.2.840.113556.1.4.45", "2.5.5.12", singleValued: true, gc: true, systemOnly: false, indexed: false, rangeLower: 0, rangeUpper: 3);
        // host
        yield return Attr("host", "0.9.2342.19200300.100.1.9", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: false);
        // houseIdentifier
        yield return Attr("houseIdentifier", "2.5.4.51", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: false, rangeLower: 1, rangeUpper: 32768);
        // Home-Phone
        yield return Attr("homePhone", "0.9.2342.19200300.100.1.20", "2.5.5.12", singleValued: true, gc: true, systemOnly: false, indexed: false, rangeLower: 1, rangeUpper: 64);

        // =====================================================================
        // I - Attributes
        // =====================================================================

        // Icon-Path
        yield return Attr("iconPath", "1.2.840.113556.1.4.556", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: false);
        // Implemented-Categories
        yield return Attr("implementedCategories", "1.2.840.113556.1.4.323", "2.5.5.10", singleValued: false, gc: false, systemOnly: false, indexed: false);
        // IndexedScopes
        yield return Attr("indexedScopes", "1.2.840.113556.1.4.676", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: false);
        // Initial-Auth-Incoming
        yield return Attr("initialAuthIncoming", "1.2.840.113556.1.4.46", "2.5.5.10", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // Initial-Auth-Outgoing
        yield return Attr("initialAuthOutgoing", "1.2.840.113556.1.4.47", "2.5.5.10", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // Initials
        yield return Attr("initials", "2.5.4.43", "2.5.5.12", singleValued: true, gc: true, systemOnly: false, indexed: false, rangeLower: 1, rangeUpper: 6);
        // Install-Ui-Level
        yield return Attr("installUiLevel", "1.2.840.113556.1.4.271", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // Instance-Type
        yield return Attr("instanceType", "1.2.840.113556.1.2.1", "2.5.5.9", singleValued: true, gc: true, systemOnly: true, indexed: false);
        // International-ISDN-Number
        yield return Attr("internationalISDNNumber", "2.5.4.25", "2.5.5.6", singleValued: false, gc: false, systemOnly: false, indexed: false, rangeLower: 1, rangeUpper: 16);
        // Inter-Site-Topology-Failover
        yield return Attr("interSiteTopologyFailover", "1.2.840.113556.1.4.1423", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // Inter-Site-Topology-Generator
        yield return Attr("interSiteTopologyGenerator", "1.2.840.113556.1.4.1244", "2.5.5.1", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // Inter-Site-Topology-Renew
        yield return Attr("interSiteTopologyRenew", "1.2.840.113556.1.4.1422", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // Invocation-Id
        yield return Attr("invocationId", "1.2.840.113556.1.4.48", "2.5.5.10", singleValued: true, gc: false, systemOnly: true, indexed: false);
        // ipHostNumber
        yield return Attr("ipHostNumber", "1.3.6.1.1.1.1.19", "2.5.5.5", singleValued: false, gc: false, systemOnly: false, indexed: false);
        // ipNetmaskNumber
        yield return Attr("ipNetmaskNumber", "1.3.6.1.1.1.1.21", "2.5.5.5", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // ipNetworkNumber
        yield return Attr("ipNetworkNumber", "1.3.6.1.1.1.1.20", "2.5.5.5", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // ipProtocolNumber
        yield return Attr("ipProtocolNumber", "1.3.6.1.1.1.1.17", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);

        // IPSec attributes
        yield return Attr("ipsecData", "1.2.840.113556.1.4.623", "2.5.5.10", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("ipsecDataType", "1.2.840.113556.1.4.624", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("ipsecFilterReference", "1.2.840.113556.1.4.625", "2.5.5.1", singleValued: false, gc: false, systemOnly: false, indexed: false);
        yield return Attr("ipsecID", "1.2.840.113556.1.4.626", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("ipsecISAKMPReference", "1.2.840.113556.1.4.627", "2.5.5.1", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("ipsecName", "1.2.840.113556.1.4.628", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("ipsecNegotiationPolicyAction", "1.2.840.113556.1.4.629", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("ipsecNegotiationPolicyReference", "1.2.840.113556.1.4.630", "2.5.5.1", singleValued: false, gc: false, systemOnly: false, indexed: false);
        yield return Attr("ipsecNegotiationPolicyType", "1.2.840.113556.1.4.631", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("ipsecNFAReference", "1.2.840.113556.1.4.632", "2.5.5.1", singleValued: false, gc: false, systemOnly: false, indexed: false);
        yield return Attr("ipsecOwnersReference", "1.2.840.113556.1.4.633", "2.5.5.1", singleValued: false, gc: false, systemOnly: false, indexed: false);
        yield return Attr("ipsecPolicyReference", "1.2.840.113556.1.4.519", "2.5.5.1", singleValued: true, gc: false, systemOnly: false, indexed: false);

        // ipServicePort
        yield return Attr("ipServicePort", "1.3.6.1.1.1.1.15", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // ipServiceProtocol
        yield return Attr("ipServiceProtocol", "1.3.6.1.1.1.1.16", "2.5.5.5", singleValued: false, gc: false, systemOnly: false, indexed: false);
        // ipPhone
        yield return Attr("ipPhone", "1.2.840.113556.1.4.721", "2.5.5.12", singleValued: true, gc: true, systemOnly: false, indexed: false, rangeLower: 1, rangeUpper: 64);
        // Is-Critical-System-Object
        yield return Attr("isCriticalSystemObject", "1.2.840.113556.1.4.868", "2.5.5.8", singleValued: true, gc: true, systemOnly: true, indexed: false);
        // Is-Defunct
        yield return Attr("isDefunct", "1.2.840.113556.1.4.661", "2.5.5.8", singleValued: true, gc: false, systemOnly: false, indexed: true);
        // Is-Deleted
        yield return Attr("isDeleted", "1.2.840.113556.1.2.48", "2.5.5.8", singleValued: true, gc: false, systemOnly: true, indexed: true);
        // Is-Ephemeral
        yield return Attr("isEphemeral", "1.2.840.113556.1.4.1212", "2.5.5.8", singleValued: true, gc: false, systemOnly: true, indexed: false);
        // Is-Member-Of-Partial-Attribute-Set
        yield return Attr("isMemberOfPartialAttributeSet", "1.2.840.113556.1.4.639", "2.5.5.8", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // Is-Privilege-Holder
        yield return Attr("isPrivilegeHolder", "1.2.840.113556.1.4.638", "2.5.5.1", singleValued: false, gc: false, systemOnly: true, indexed: false);
        // Is-Recycled
        yield return Attr("isRecycled", "1.2.840.113556.1.4.2058", "2.5.5.8", singleValued: true, gc: false, systemOnly: true, indexed: true);
        // Is-Single-Valued
        yield return Attr("isSingleValued", "1.2.840.113556.1.2.33", "2.5.5.8", singleValued: true, gc: false, systemOnly: true, indexed: false);

        // =====================================================================
        // J - Attributes
        // =====================================================================

        // jpegPhoto
        yield return Attr("jpegPhoto", "0.9.2342.19200300.100.1.60", "2.5.5.10", singleValued: false, gc: false, systemOnly: false, indexed: false);

        // =====================================================================
        // K - Attributes
        // =====================================================================

        // Keywords
        yield return Attr("keywords", "1.2.840.113556.1.4.48", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: true);
        // Knowledge-Information
        yield return Attr("knowledgeInformation", "2.5.4.2", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);

        // =====================================================================
        // L - Attributes
        // =====================================================================

        // labeledURI
        yield return Attr("labeledURI", "1.2.840.113556.1.4.1340", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: false);
        // Last-Backup-Restoration-Time
        yield return Attr("lastBackupRestorationTime", "1.2.840.113556.1.4.519", "2.5.5.11", singleValued: true, gc: false, systemOnly: true, indexed: false);
        // Last-Content-Indexed
        yield return Attr("lastContentIndexed", "1.2.840.113556.1.4.50", "2.5.5.11", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // Last-Known-Parent
        yield return Attr("lastKnownParent", "1.2.840.113556.1.4.781", "2.5.5.1", singleValued: true, gc: false, systemOnly: true, indexed: false);
        // Last-Logoff
        yield return Attr("lastLogoff", "1.2.840.113556.1.4.51", "2.5.5.16", singleValued: true, gc: false, systemOnly: true, indexed: false);
        // Last-Logon
        yield return Attr("lastLogon", "1.2.840.113556.1.4.52", "2.5.5.16", singleValued: true, gc: false, systemOnly: true, indexed: false);
        // Last-Logon-Timestamp
        yield return Attr("lastLogonTimestamp", "1.2.840.113556.1.4.1696", "2.5.5.16", singleValued: true, gc: true, systemOnly: true, indexed: true);
        // Last-Set-Time
        yield return Attr("lastSetTime", "1.2.840.113556.1.4.53", "2.5.5.16", singleValued: true, gc: false, systemOnly: true, indexed: false);
        // Last-Update-Sequence
        yield return Attr("lastUpdateSequence", "1.2.840.113556.1.4.331", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // LDAP-Admin-Limits
        yield return Attr("lDAPAdminLimits", "1.2.840.113556.1.4.843", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: false);
        // LDAP-Display-Name
        yield return Attr("lDAPDisplayName", "1.2.840.113556.1.2.460", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: true);
        // LDAP-IPDeny-List
        yield return Attr("lDAPIPDenyList", "1.2.840.113556.1.4.844", "2.5.5.10", singleValued: false, gc: false, systemOnly: false, indexed: false);
        // Legacy-Exchange-DN
        yield return Attr("legacyExchangeDN", "1.2.840.113556.1.4.655", "2.5.5.12", singleValued: true, gc: true, systemOnly: false, indexed: true);
        // Link-ID
        yield return Attr("linkID", "1.2.840.113556.1.2.50", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // Link-Track-Secret
        yield return Attr("linkTrackSecret", "1.2.840.113556.1.4.270", "2.5.5.10", singleValued: true, gc: false, systemOnly: true, indexed: false);
        // Lm-Pwd-History
        yield return Attr("lmPwdHistory", "1.2.840.113556.1.4.160", "2.5.5.10", singleValued: false, gc: false, systemOnly: true, indexed: false);
        // Locale-ID
        yield return Attr("localeID", "1.2.840.113556.1.4.56", "2.5.5.9", singleValued: false, gc: false, systemOnly: false, indexed: false);
        // Locality-Name (l)
        yield return Attr("l", "2.5.4.7", "2.5.5.12", singleValued: true, gc: true, systemOnly: false, indexed: false, rangeLower: 1, rangeUpper: 128);
        // Localization-Display-Id
        yield return Attr("localizationDisplayId", "1.2.840.113556.1.4.505", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // Localized-Description
        yield return Attr("localizedDescription", "1.2.840.113556.1.4.817", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: false);
        // Local-Policy-Flags
        yield return Attr("localPolicyFlags", "1.2.840.113556.1.4.57", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // Local-Policy-Reference
        yield return Attr("localPolicyReference", "1.2.840.113556.1.4.422", "2.5.5.1", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // Location
        yield return Attr("location", "1.2.840.113556.1.4.222", "2.5.5.12", singleValued: true, gc: true, systemOnly: false, indexed: false, rangeLower: 0, rangeUpper: 1024);
        // Lockout-Duration
        yield return Attr("lockoutDuration", "1.2.840.113556.1.4.60", "2.5.5.16", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // Lock-Out-Observation-Window
        yield return Attr("lockOutObservationWindow", "1.2.840.113556.1.4.61", "2.5.5.16", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // Lockout-Threshold
        yield return Attr("lockoutThreshold", "1.2.840.113556.1.4.73", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // Lockout-Time
        yield return Attr("lockoutTime", "1.2.840.113556.1.4.662", "2.5.5.16", singleValued: true, gc: false, systemOnly: false, indexed: true);
        // loginShell
        yield return Attr("loginShell", "1.3.6.1.1.1.1.4", "2.5.5.5", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // Logo (thumbnailLogo)
        yield return Attr("thumbnailLogo", "2.5.4.47", "2.5.5.10", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // Logon-Count
        yield return Attr("logonCount", "1.2.840.113556.1.4.54", "2.5.5.9", singleValued: true, gc: false, systemOnly: true, indexed: false);
        // Logon-Hours
        yield return Attr("logonHours", "1.2.840.113556.1.4.64", "2.5.5.10", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // Logon-Workstation
        yield return Attr("logonWorkstation", "1.2.840.113556.1.4.65", "2.5.5.10", singleValued: true, gc: false, systemOnly: false, indexed: false);
        // LSA-Creation-Time
        yield return Attr("lSACreationTime", "1.2.840.113556.1.4.66", "2.5.5.16", singleValued: true, gc: false, systemOnly: true, indexed: false);
        // LSA-Modified-Count
        yield return Attr("lSAModifiedCount", "1.2.840.113556.1.4.67", "2.5.5.16", singleValued: true, gc: false, systemOnly: true, indexed: false);

        // =====================================================================
        // ms-Authz attributes
        // =====================================================================
        yield return Attr("msAuthz-CentralAccessPolicyID", "1.2.840.113556.1.4.2155", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msAuthz-EffectiveSecurityPolicy", "1.2.840.113556.1.4.2152", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msAuthz-LastEffectiveSecurityPolicy", "1.2.840.113556.1.4.2153", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msAuthz-MemberRulesInCentralAccessPolicy", "1.2.840.113556.1.4.2154", "2.5.5.1", singleValued: false, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msAuthz-MemberRulesInCentralAccessPolicyBL", "1.2.840.113556.1.4.2156", "2.5.5.1", singleValued: false, gc: false, systemOnly: true, indexed: false);
        yield return Attr("msAuthz-ProposedSecurityPolicy", "1.2.840.113556.1.4.2151", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msAuthz-ResourceCondition", "1.2.840.113556.1.4.2150", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);

        // =====================================================================
        // ms-COM attributes
        // =====================================================================
        yield return Attr("msCOM-DefaultPartitionLink", "1.2.840.113556.1.4.1427", "2.5.5.1", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msCOM-ObjectId", "1.2.840.113556.1.4.1428", "2.5.5.10", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msCOM-PartitionLink", "1.2.840.113556.1.4.1424", "2.5.5.1", singleValued: false, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msCOM-PartitionSetLink", "1.2.840.113556.1.4.1425", "2.5.5.1", singleValued: false, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msCOM-UserLink", "1.2.840.113556.1.4.1426", "2.5.5.1", singleValued: false, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msCOM-UserPartitionSetLink", "1.2.840.113556.1.4.1429", "2.5.5.1", singleValued: false, gc: false, systemOnly: false, indexed: false);

        // =====================================================================
        // ms-DFS attributes
        // =====================================================================
        yield return Attr("msDFS-CommentV2", "1.2.840.113556.1.4.2034", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDFS-GenerationGUIDV2", "1.2.840.113556.1.4.2035", "2.5.5.10", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDFS-LastModifiedV2", "1.2.840.113556.1.4.2037", "2.5.5.11", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDFS-LinkIdentityGUIDV2", "1.2.840.113556.1.4.2039", "2.5.5.10", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDFS-LinkPathV2", "1.2.840.113556.1.4.2038", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDFS-LinkSecurityDescriptorV2", "1.2.840.113556.1.4.2040", "2.5.5.15", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDFS-NamespaceIdentityGUIDV2", "1.2.840.113556.1.4.2033", "2.5.5.10", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDFS-PropertiesV2", "1.2.840.113556.1.4.2036", "2.5.5.10", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDFS-SchemaMajorVersion", "1.2.840.113556.1.4.2031", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDFS-SchemaMinorVersion", "1.2.840.113556.1.4.2032", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDFS-ShortNameLinkPathV2", "1.2.840.113556.1.4.2043", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDFS-TargetListV2", "1.2.840.113556.1.4.2041", "2.5.5.10", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDFS-TtlV2", "1.2.840.113556.1.4.2042", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);

        // =====================================================================
        // ms-DFSR attributes
        // =====================================================================
        yield return Attr("msDFSR-CachePolicy", "1.2.840.113556.1.6.13.3.39", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDFSR-CommonStagingPath", "1.2.840.113556.1.6.13.3.40", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDFSR-CommonStagingSizeInMb", "1.2.840.113556.1.6.13.3.41", "2.5.5.16", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDFSR-ComputerReference", "1.2.840.113556.1.6.13.3.101", "2.5.5.1", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDFSR-ComputerReferenceBL", "1.2.840.113556.1.6.13.3.102", "2.5.5.1", singleValued: false, gc: false, systemOnly: true, indexed: false);
        yield return Attr("msDFSR-ConflictPath", "1.2.840.113556.1.6.13.3.4", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDFSR-ConflictSizeInMb", "1.2.840.113556.1.6.13.3.5", "2.5.5.16", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDFSR-ContentSetGuid", "1.2.840.113556.1.6.13.3.22", "2.5.5.10", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDFSR-DefaultCompressionExclusionFilter", "1.2.840.113556.1.6.13.3.38", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDFSR-DeletedPath", "1.2.840.113556.1.6.13.3.6", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDFSR-DeletedSizeInMb", "1.2.840.113556.1.6.13.3.7", "2.5.5.16", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDFSR-DfsLinkTarget", "1.2.840.113556.1.6.13.3.35", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDFSR-DfsPath", "1.2.840.113556.1.6.13.3.34", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDFSR-DirectoryFilter", "1.2.840.113556.1.6.13.3.8", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDFSR-DisablePacketPrivacy", "1.2.840.113556.1.6.13.3.36", "2.5.5.8", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDFSR-Enabled", "1.2.840.113556.1.6.13.3.9", "2.5.5.8", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDFSR-Extension", "1.2.840.113556.1.6.13.3.20", "2.5.5.10", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDFSR-FileFilter", "1.2.840.113556.1.6.13.3.10", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDFSR-Flags", "1.2.840.113556.1.6.13.3.19", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDFSR-Keywords", "1.2.840.113556.1.6.13.3.37", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDFSR-MaxAgeInCacheInMin", "1.2.840.113556.1.6.13.3.42", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDFSR-MemberReference", "1.2.840.113556.1.6.13.3.103", "2.5.5.1", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDFSR-MemberReferenceBL", "1.2.840.113556.1.6.13.3.104", "2.5.5.1", singleValued: false, gc: false, systemOnly: true, indexed: false);
        yield return Attr("msDFSR-MinDurationCacheInMin", "1.2.840.113556.1.6.13.3.43", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDFSR-OnDemandExclusionDirectoryFilter", "1.2.840.113556.1.6.13.3.44", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDFSR-OnDemandExclusionFileFilter", "1.2.840.113556.1.6.13.3.45", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDFSR-Options", "1.2.840.113556.1.6.13.3.23", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDFSR-Options2", "1.2.840.113556.1.6.13.3.46", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDFSR-Priority", "1.2.840.113556.1.6.13.3.24", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDFSR-RdcEnabled", "1.2.840.113556.1.6.13.3.25", "2.5.5.8", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDFSR-RdcMinFileSizeInKb", "1.2.840.113556.1.6.13.3.26", "2.5.5.16", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDFSR-ReadOnly", "1.2.840.113556.1.6.13.3.47", "2.5.5.8", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDFSR-ReplicationGroupGuid", "1.2.840.113556.1.6.13.3.1", "2.5.5.10", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDFSR-ReplicationGroupType", "1.2.840.113556.1.6.13.3.2", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDFSR-RootFence", "1.2.840.113556.1.6.13.3.11", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDFSR-RootPath", "1.2.840.113556.1.6.13.3.12", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDFSR-RootSizeInMb", "1.2.840.113556.1.6.13.3.13", "2.5.5.16", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDFSR-Schedule", "1.2.840.113556.1.6.13.3.14", "2.5.5.10", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDFSR-StagingCleanupTriggerInPercent", "1.2.840.113556.1.6.13.3.15", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDFSR-StagingPath", "1.2.840.113556.1.6.13.3.16", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDFSR-StagingSizeInMb", "1.2.840.113556.1.6.13.3.17", "2.5.5.16", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDFSR-TombstoneExpiryInMin", "1.2.840.113556.1.6.13.3.18", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDFSR-Version", "1.2.840.113556.1.6.13.3.21", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);

        // =====================================================================
        // ms-DNS attributes
        // =====================================================================
        yield return Attr("msDNS-DNSKEYRecords", "1.2.840.113556.1.4.2144", "2.5.5.10", singleValued: false, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDNS-DNSKEYRecordSetTTL", "1.2.840.113556.1.4.2139", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDNS-DSRecordAlgorithms", "1.2.840.113556.1.4.2140", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDNS-DSRecordSetTTL", "1.2.840.113556.1.4.2141", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDNS-IsSigned", "1.2.840.113556.1.4.2129", "2.5.5.8", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDNS-KeymasterZones", "1.2.840.113556.1.4.2128", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDNS-MaintainTrustAnchor", "1.2.840.113556.1.4.2145", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDNS-NSEC3CurrentSalt", "1.2.840.113556.1.4.2134", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDNS-NSEC3HashAlgorithm", "1.2.840.113556.1.4.2131", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDNS-NSEC3Iterations", "1.2.840.113556.1.4.2132", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDNS-NSEC3OptOut", "1.2.840.113556.1.4.2133", "2.5.5.8", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDNS-NSEC3RandomSaltLength", "1.2.840.113556.1.4.2135", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDNS-NSEC3UserSalt", "1.2.840.113556.1.4.2136", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDNS-ParentHasSecureDelegation", "1.2.840.113556.1.4.2143", "2.5.5.8", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDNS-PropagationTime", "1.2.840.113556.1.4.2137", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDNS-RFC5011KeyRollovers", "1.2.840.113556.1.4.2138", "2.5.5.8", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDNS-SecureDelegationPollingPeriod", "1.2.840.113556.1.4.2142", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDNS-SignatureInceptionOffset", "1.2.840.113556.1.4.2130", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDNS-SigningKeyDescriptors", "1.2.840.113556.1.4.2127", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDNS-SigningKeys", "1.2.840.113556.1.4.2126", "2.5.5.10", singleValued: false, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDNS-SignWithNSEC3", "1.2.840.113556.1.4.2146", "2.5.5.8", singleValued: true, gc: false, systemOnly: false, indexed: false);

        // MS-DRM-Identity-Certificate
        yield return Attr("msDRM-IdentityCertificate", "1.2.840.113556.1.4.1843", "2.5.5.10", singleValued: false, gc: false, systemOnly: false, indexed: false);

        // =====================================================================
        // ms-DS attributes (core AD DS)
        // =====================================================================
        yield return Attr("msDS-AdditionalDnsHostName", "1.2.840.113556.1.4.1717", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: true);
        yield return Attr("msDS-AdditionalSamAccountName", "1.2.840.113556.1.4.1718", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: true);
        yield return Attr("msDS-AllowedDNSSuffixes", "1.2.840.113556.1.4.1710", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-AllowedToActOnBehalfOfOtherIdentity", "1.2.840.113556.1.4.2182", "2.5.5.15", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-AllowedToDelegateTo", "1.2.840.113556.1.4.1787", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-AllUsersTrustQuota", "1.2.840.113556.1.4.1788", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-AppliesToResourceTypes", "1.2.840.113556.1.4.2103", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-Approx-Immed-Subordinates", "1.2.840.113556.1.4.1669", "2.5.5.9", singleValued: true, gc: false, systemOnly: true, indexed: false);
        yield return Attr("msDS-AuthenticatedAtDC", "1.2.840.113556.1.4.1958", "2.5.5.1", singleValued: false, gc: false, systemOnly: true, indexed: false);
        yield return Attr("msDS-AuthenticatedToAccountlist", "1.2.840.113556.1.4.1957", "2.5.5.1", singleValued: false, gc: false, systemOnly: true, indexed: false);
        yield return Attr("msDS-Auxiliary-Classes", "1.2.840.113556.1.4.1458", "2.5.5.2", singleValued: false, gc: false, systemOnly: true, indexed: false);

        // ms-DS-Az attributes (Authorization Manager)
        yield return Attr("msDS-AzApplicationData", "1.2.840.113556.1.4.1819", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-AzApplicationName", "1.2.840.113556.1.4.1798", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-AzApplicationVersion", "1.2.840.113556.1.4.1817", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-AzBizRule", "1.2.840.113556.1.4.1801", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-AzBizRuleLanguage", "1.2.840.113556.1.4.1802", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-AzClassId", "1.2.840.113556.1.4.1818", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-AzDomainTimeout", "1.2.840.113556.1.4.1794", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-AzGenerateAudits", "1.2.840.113556.1.4.1795", "2.5.5.8", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-AzGenericData", "1.2.840.113556.1.4.1820", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-AzLastImportedBizRulePath", "1.2.840.113556.1.4.1803", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-AzLDAPQuery", "1.2.840.113556.1.4.1792", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-AzMajorVersion", "1.2.840.113556.1.4.1824", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-AzMinorVersion", "1.2.840.113556.1.4.1825", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-AzObjectGuid", "1.2.840.113556.1.4.1949", "2.5.5.10", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-AzOperationID", "1.2.840.113556.1.4.1800", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-AzScopeName", "1.2.840.113556.1.4.1799", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-AzScriptEngineCacheMax", "1.2.840.113556.1.4.1793", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-AzScriptTimeout", "1.2.840.113556.1.4.1796", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-AzTaskIsRoleDefinition", "1.2.840.113556.1.4.1816", "2.5.5.8", singleValued: true, gc: false, systemOnly: false, indexed: false);

        // ms-DS core attributes continued
        yield return Attr("msDS-Behavior-Version", "1.2.840.113556.1.4.1459", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-BridgeHeadServersUsed", "1.2.840.113556.1.4.1460", "2.5.5.7", singleValued: false, gc: false, systemOnly: true, indexed: false);
        yield return Attr("msDS-ByteArray", "1.2.840.113556.1.4.1832", "2.5.5.10", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-Cached-Membership", "1.2.840.113556.1.4.1441", "2.5.5.10", singleValued: true, gc: false, systemOnly: true, indexed: false);
        yield return Attr("msDS-Cached-Membership-Time-Stamp", "1.2.840.113556.1.4.1442", "2.5.5.16", singleValued: true, gc: false, systemOnly: true, indexed: false);

        // ms-DS Claim attributes
        yield return Attr("msDS-ClaimAttributeSource", "1.2.840.113556.1.4.2098", "2.5.5.1", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-ClaimIsSingleValued", "1.2.840.113556.1.4.2099", "2.5.5.8", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-ClaimIsValueSpaceRestricted", "1.2.840.113556.1.4.2100", "2.5.5.8", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-ClaimPossibleValues", "1.2.840.113556.1.4.2097", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-ClaimSharesPossibleValuesWith", "1.2.840.113556.1.4.2101", "2.5.5.1", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-ClaimSharesPossibleValuesWithBL", "1.2.840.113556.1.4.2102", "2.5.5.1", singleValued: false, gc: false, systemOnly: true, indexed: false);
        yield return Attr("msDS-ClaimSource", "1.2.840.113556.1.4.2157", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-ClaimSourceType", "1.2.840.113556.1.4.2096", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-ClaimTypeAppliesToClass", "1.2.840.113556.1.4.2104", "2.5.5.1", singleValued: false, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-ClaimValueType", "1.2.840.113556.1.4.2095", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);

        // ms-DS Consistency/Creator/Date
        yield return Attr("mS-DS-ConsistencyChildCount", "1.2.840.113556.1.4.1363", "2.5.5.9", singleValued: true, gc: false, systemOnly: true, indexed: false);
        yield return Attr("mS-DS-ConsistencyGuid", "1.2.840.113556.1.4.1364", "2.5.5.10", singleValued: true, gc: false, systemOnly: true, indexed: false);
        yield return Attr("mS-DS-CreatorSID", "1.2.840.113556.1.4.1410", "2.5.5.17", singleValued: true, gc: false, systemOnly: true, indexed: false);
        yield return Attr("msDS-DateTime", "1.2.840.113556.1.4.1833", "2.5.5.11", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-DefaultQuota", "1.2.840.113556.1.4.1845", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-DeletedObjectLifetime", "1.2.840.113556.1.4.2060", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-DisableForInstances", "1.2.840.113556.1.4.2088", "2.5.5.1", singleValued: false, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-DisableForInstancesBL", "1.2.840.113556.1.4.2089", "2.5.5.1", singleValued: false, gc: false, systemOnly: true, indexed: false);
        yield return Attr("msDS-DnsRootAlias", "1.2.840.113556.1.4.1719", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-EgressClaimsTransformationPolicy", "1.2.840.113556.1.4.2185", "2.5.5.1", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-EnabledFeature", "1.2.840.113556.1.4.2061", "2.5.5.1", singleValued: false, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-EnabledFeatureBL", "1.2.840.113556.1.4.2069", "2.5.5.1", singleValued: false, gc: false, systemOnly: true, indexed: false);
        yield return Attr("msDS-Entry-Time-To-Die", "1.2.840.113556.1.4.1622", "2.5.5.11", singleValued: true, gc: false, systemOnly: true, indexed: true);
        yield return Attr("msDS-ExecuteScriptPassword", "1.2.840.113556.1.4.1783", "2.5.5.10", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-ExternalKey", "1.2.840.113556.1.4.1834", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-ExternalStore", "1.2.840.113556.1.4.1835", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-FailedInteractiveLogonCount", "1.2.840.113556.1.4.1907", "2.5.5.9", singleValued: true, gc: false, systemOnly: true, indexed: false);
        yield return Attr("msDS-FailedInteractiveLogonCountAtLastSuccessfulLogon", "1.2.840.113556.1.4.1908", "2.5.5.9", singleValued: true, gc: false, systemOnly: true, indexed: false);
        yield return Attr("msDS-FilterContainers", "1.2.840.113556.1.4.1713", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-GenerationId", "1.2.840.113556.1.4.2166", "2.5.5.10", singleValued: true, gc: false, systemOnly: true, indexed: false);
        yield return Attr("msDS-GeoCoordinatesAltitude", "1.2.840.113556.1.4.2183", "2.5.5.16", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-GeoCoordinatesLatitude", "1.2.840.113556.1.4.2184", "2.5.5.16", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-GeoCoordinatesLongitude", "1.2.840.113556.1.4.2185", "2.5.5.16", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-GroupMSAMembership", "1.2.840.113556.1.4.2200", "2.5.5.15", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-HABSeniorityIndex", "1.2.840.113556.1.4.1997", "2.5.5.9", singleValued: true, gc: true, systemOnly: false, indexed: true);
        yield return Attr("msDS-HasDomainNCs", "1.2.840.113556.1.4.1820", "2.5.5.1", singleValued: false, gc: false, systemOnly: true, indexed: false);
        yield return Attr("msDS-HasFullReplicaNCs", "1.2.840.113556.1.4.1925", "2.5.5.1", singleValued: false, gc: false, systemOnly: true, indexed: false);
        yield return Attr("msDS-HasInstantiatedNCs", "1.2.840.113556.1.4.1709", "2.5.5.7", singleValued: false, gc: false, systemOnly: true, indexed: false);
        yield return Attr("msDS-HasMasterNCs", "1.2.840.113556.1.4.1836", "2.5.5.1", singleValued: false, gc: false, systemOnly: true, indexed: false);
        yield return Attr("msDS-HostServiceAccount", "1.2.840.113556.1.4.2052", "2.5.5.1", singleValued: false, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-HostServiceAccountBL", "1.2.840.113556.1.4.2053", "2.5.5.1", singleValued: false, gc: false, systemOnly: true, indexed: false);
        yield return Attr("msDS-IngressClaimsTransformationPolicy", "1.2.840.113556.1.4.2186", "2.5.5.1", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-Integer", "1.2.840.113556.1.4.1837", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-IntId", "1.2.840.113556.1.4.1716", "2.5.5.9", singleValued: true, gc: false, systemOnly: true, indexed: true);
        yield return Attr("msDS-IsDomainFor", "1.2.840.113556.1.4.1838", "2.5.5.1", singleValued: false, gc: false, systemOnly: true, indexed: false);
        yield return Attr("msDS-IsFullReplicaFor", "1.2.840.113556.1.4.1839", "2.5.5.1", singleValued: false, gc: false, systemOnly: true, indexed: false);
        yield return Attr("msDS-isGC", "1.2.840.113556.1.4.1959", "2.5.5.8", singleValued: true, gc: false, systemOnly: true, indexed: false);
        yield return Attr("msDS-IsPartialReplicaFor", "1.2.840.113556.1.4.1840", "2.5.5.1", singleValued: false, gc: false, systemOnly: true, indexed: false);
        yield return Attr("msDS-IsPossibleValuesPresent", "1.2.840.113556.1.4.2105", "2.5.5.8", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-IsPrimaryComputerFor", "1.2.840.113556.1.4.2168", "2.5.5.1", singleValued: false, gc: false, systemOnly: true, indexed: false);
        yield return Attr("msDS-isRODC", "1.2.840.113556.1.4.1960", "2.5.5.8", singleValued: true, gc: false, systemOnly: true, indexed: false);
        yield return Attr("msDS-IsUsedAsResourceSecurityAttribute", "1.2.840.113556.1.4.2106", "2.5.5.8", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-IsUserCachableAtRodc", "1.2.840.113556.1.4.1961", "2.5.5.9", singleValued: true, gc: false, systemOnly: true, indexed: false);
        yield return Attr("msDS-KeyVersionNumber", "1.2.840.113556.1.4.1782", "2.5.5.9", singleValued: true, gc: false, systemOnly: true, indexed: false);
        yield return Attr("msDS-KrbTgtLink", "1.2.840.113556.1.4.1923", "2.5.5.1", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-KrbTgtLinkBL", "1.2.840.113556.1.4.1931", "2.5.5.1", singleValued: false, gc: false, systemOnly: true, indexed: false);
        yield return Attr("msDS-LastFailedInteractiveLogonTime", "1.2.840.113556.1.4.1909", "2.5.5.16", singleValued: true, gc: false, systemOnly: true, indexed: false);
        yield return Attr("msDS-LastKnownRDN", "1.2.840.113556.1.4.2067", "2.5.5.12", singleValued: true, gc: false, systemOnly: true, indexed: false);
        yield return Attr("msDS-LastSuccessfulInteractiveLogonTime", "1.2.840.113556.1.4.1910", "2.5.5.16", singleValued: true, gc: false, systemOnly: true, indexed: false);
        yield return Attr("msDS-LocalEffectiveDeletionTime", "1.2.840.113556.1.4.2059", "2.5.5.11", singleValued: true, gc: false, systemOnly: true, indexed: true);
        yield return Attr("msDS-LocalEffectiveRecycleTime", "1.2.840.113556.1.4.2062", "2.5.5.11", singleValued: true, gc: false, systemOnly: true, indexed: true);
        yield return Attr("msDS-LockoutDuration", "1.2.840.113556.1.4.2018", "2.5.5.16", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-LockoutObservationWindow", "1.2.840.113556.1.4.2020", "2.5.5.16", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-LockoutThreshold", "1.2.840.113556.1.4.2019", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-LogonTimeSyncInterval", "1.2.840.113556.1.4.1784", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);

        // ms-DS Password / PSO attributes
        yield return Attr("mS-DS-MachineAccountQuota", "1.2.840.113556.1.4.1411", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-ManagedPassword", "1.2.840.113556.1.4.2196", "2.5.5.10", singleValued: true, gc: false, systemOnly: true, indexed: false);
        yield return Attr("msDS-ManagedPasswordId", "1.2.840.113556.1.4.2197", "2.5.5.10", singleValued: true, gc: false, systemOnly: true, indexed: false);
        yield return Attr("msDS-ManagedPasswordInterval", "1.2.840.113556.1.4.2198", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-ManagedPasswordPreviousId", "1.2.840.113556.1.4.2199", "2.5.5.10", singleValued: true, gc: false, systemOnly: true, indexed: false);
        yield return Attr("msDS-MasteredBy", "1.2.840.113556.1.4.1837", "2.5.5.1", singleValued: false, gc: false, systemOnly: true, indexed: false);
        yield return Attr("msDS-MaximumPasswordAge", "1.2.840.113556.1.4.2012", "2.5.5.16", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-MaxValues", "1.2.840.113556.1.4.1840", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-MinimumPasswordAge", "1.2.840.113556.1.4.2013", "2.5.5.16", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-MinimumPasswordLength", "1.2.840.113556.1.4.2014", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-PasswordComplexityEnabled", "1.2.840.113556.1.4.2017", "2.5.5.8", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-PasswordHistoryLength", "1.2.840.113556.1.4.2015", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-PasswordReversibleEncryptionEnabled", "1.2.840.113556.1.4.2016", "2.5.5.8", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-PasswordSettingsPrecedence", "1.2.840.113556.1.4.2021", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-PerUserTrustQuota", "1.2.840.113556.1.4.1789", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-PerUserTrustTombstonesQuota", "1.2.840.113556.1.4.1790", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);

        // ms-DS Phonetic attributes
        yield return Attr("msDS-PhoneticCompanyName", "1.2.840.113556.1.4.1998", "2.5.5.12", singleValued: true, gc: true, systemOnly: false, indexed: true);
        yield return Attr("msDS-PhoneticDepartment", "1.2.840.113556.1.4.1999", "2.5.5.12", singleValued: true, gc: true, systemOnly: false, indexed: true);
        yield return Attr("msDS-PhoneticDisplayName", "1.2.840.113556.1.4.2000", "2.5.5.12", singleValued: true, gc: true, systemOnly: false, indexed: true);
        yield return Attr("msDS-PhoneticFirstName", "1.2.840.113556.1.4.2001", "2.5.5.12", singleValued: true, gc: true, systemOnly: false, indexed: true);
        yield return Attr("msDS-PhoneticLastName", "1.2.840.113556.1.4.2002", "2.5.5.12", singleValued: true, gc: true, systemOnly: false, indexed: true);

        // ms-DS remaining
        yield return Attr("msDS-PortLDAP", "1.2.840.113556.1.4.1860", "2.5.5.9", singleValued: true, gc: false, systemOnly: true, indexed: false);
        yield return Attr("msDS-PortSSL", "1.2.840.113556.1.4.1861", "2.5.5.9", singleValued: true, gc: false, systemOnly: true, indexed: false);
        yield return Attr("msDS-Preferred-GC-Site", "1.2.840.113556.1.4.1444", "2.5.5.1", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-PrimaryComputer", "1.2.840.113556.1.4.2167", "2.5.5.1", singleValued: false, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-PrincipalName", "1.2.840.113556.1.4.1865", "2.5.5.12", singleValued: true, gc: false, systemOnly: true, indexed: false);
        yield return Attr("msDS-PromotionSettings", "1.2.840.113556.1.4.1962", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-PSOApplied", "1.2.840.113556.1.4.2023", "2.5.5.1", singleValued: false, gc: false, systemOnly: true, indexed: false);
        yield return Attr("msDS-PSOAppliesTo", "1.2.840.113556.1.4.2022", "2.5.5.1", singleValued: false, gc: false, systemOnly: false, indexed: false);

        // ms-DS Replication / NC / Quota
        yield return Attr("msDS-QuotaAmount", "1.2.840.113556.1.4.1846", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-QuotaEffective", "1.2.840.113556.1.4.1848", "2.5.5.9", singleValued: true, gc: false, systemOnly: true, indexed: false);
        yield return Attr("msDS-QuotaTrustee", "1.2.840.113556.1.4.1847", "2.5.5.17", singleValued: true, gc: false, systemOnly: false, indexed: true);
        yield return Attr("msDS-QuotaUsed", "1.2.840.113556.1.4.1849", "2.5.5.9", singleValued: true, gc: false, systemOnly: true, indexed: false);
        yield return Attr("msDS-ReplAttributeMetaData", "1.2.840.113556.1.4.1707", "2.5.5.12", singleValued: false, gc: false, systemOnly: true, indexed: false);
        yield return Attr("msDS-ReplAuthenticationMode", "1.2.840.113556.1.4.1965", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("mS-DS-ReplicatesNCReason", "1.2.840.113556.1.4.1408", "2.5.5.7", singleValued: false, gc: false, systemOnly: true, indexed: false);
        yield return Attr("msDS-ReplicationEpoch", "1.2.840.113556.1.4.1720", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-ReplicationNotifyFirstDSADelay", "1.2.840.113556.1.4.1721", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-ReplicationNotifySubsequentDSADelay", "1.2.840.113556.1.4.1722", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-ReplValueMetaData", "1.2.840.113556.1.4.1708", "2.5.5.12", singleValued: false, gc: false, systemOnly: true, indexed: false);
        yield return Attr("msDS-RequiredDomainBehaviorVersion", "1.2.840.113556.1.4.2066", "2.5.5.9", singleValued: true, gc: false, systemOnly: true, indexed: false);
        yield return Attr("msDS-RequiredForestBehaviorVersion", "1.2.840.113556.1.4.2065", "2.5.5.9", singleValued: true, gc: false, systemOnly: true, indexed: false);
        yield return Attr("msDS-ResultantPSO", "1.2.840.113556.1.4.2024", "2.5.5.1", singleValued: true, gc: false, systemOnly: true, indexed: false);
        yield return Attr("msDS-RetiredReplNCSignatures", "1.2.840.113556.1.4.1826", "2.5.5.10", singleValued: true, gc: false, systemOnly: true, indexed: false);
        yield return Attr("msDS-RevealedDSAs", "1.2.840.113556.1.4.1930", "2.5.5.1", singleValued: false, gc: false, systemOnly: true, indexed: false);
        yield return Attr("msDS-RevealedList", "1.2.840.113556.1.4.1940", "2.5.5.7", singleValued: false, gc: false, systemOnly: true, indexed: false);
        yield return Attr("msDS-RevealedListBL", "1.2.840.113556.1.4.1941", "2.5.5.1", singleValued: false, gc: false, systemOnly: true, indexed: false);
        yield return Attr("msDS-RevealedUsers", "1.2.840.113556.1.4.1924", "2.5.5.7", singleValued: false, gc: false, systemOnly: true, indexed: false);
        yield return Attr("msDS-RevealOnDemandGroup", "1.2.840.113556.1.4.1928", "2.5.5.1", singleValued: false, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-NeverRevealGroup", "1.2.840.113556.1.4.1926", "2.5.5.1", singleValued: false, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-NC-Replica-Locations", "1.2.840.113556.1.4.1663", "2.5.5.1", singleValued: false, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-NC-RO-Replica-Locations", "1.2.840.113556.1.4.1967", "2.5.5.1", singleValued: false, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-NC-RO-Replica-LocationsBL", "1.2.840.113556.1.4.1968", "2.5.5.1", singleValued: false, gc: false, systemOnly: true, indexed: false);
        yield return Attr("msDS-NCReplCursors", "1.2.840.113556.1.4.1704", "2.5.5.12", singleValued: false, gc: false, systemOnly: true, indexed: false);
        yield return Attr("msDS-NCReplInboundNeighbors", "1.2.840.113556.1.4.1705", "2.5.5.12", singleValued: false, gc: false, systemOnly: true, indexed: false);
        yield return Attr("msDS-NCReplOutboundNeighbors", "1.2.840.113556.1.4.1706", "2.5.5.12", singleValued: false, gc: false, systemOnly: true, indexed: false);
        yield return Attr("msDS-NCType", "1.2.840.113556.1.4.2024", "2.5.5.9", singleValued: true, gc: false, systemOnly: true, indexed: false);
        yield return Attr("msDS-NonMembers", "1.2.840.113556.1.4.1793", "2.5.5.1", singleValued: false, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-NonMembersBL", "1.2.840.113556.1.4.1794", "2.5.5.1", singleValued: false, gc: false, systemOnly: true, indexed: false);
        yield return Attr("msDS-ObjectReference", "1.2.840.113556.1.4.1841", "2.5.5.1", singleValued: false, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-ObjectReferenceBL", "1.2.840.113556.1.4.1842", "2.5.5.1", singleValued: false, gc: false, systemOnly: true, indexed: false);

        yield return Attr("msDS-SupportedEncryptionTypes", "1.2.840.113556.1.4.1963", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-Settings", "1.2.840.113556.1.4.1464", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-SPNSuffixes", "1.2.840.113556.1.4.1715", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msDS-Site-Affinity", "1.2.840.113556.1.4.1443", "2.5.5.10", singleValued: false, gc: false, systemOnly: true, indexed: false);
        yield return Attr("msDS-SiteName", "1.2.840.113556.1.4.1966", "2.5.5.12", singleValued: true, gc: false, systemOnly: true, indexed: false);
        yield return Attr("msDS-SourceObjectDN", "1.2.840.113556.1.4.1879", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);

        // ms-DS User attributes
        yield return Attr("msDS-User-Account-Control-Computed", "1.2.840.113556.1.4.2380", "2.5.5.9", singleValued: true, gc: false, systemOnly: true, indexed: false);
        yield return Attr("msDS-UserPasswordExpiryTimeComputed", "1.2.840.113556.1.4.2281", "2.5.5.16", singleValued: true, gc: false, systemOnly: true, indexed: false);
        yield return Attr("msDS-USNLastSyncSuccess", "1.2.840.113556.1.4.2055", "2.5.5.16", singleValued: true, gc: false, systemOnly: true, indexed: false);

        // =====================================================================
        // ms-Exch attributes (Exchange schema extensions commonly in AD)
        // =====================================================================
        yield return Attr("msExchAssistantName", "1.2.840.113556.1.2.444", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msExchHouseIdentifier", "1.2.840.113556.1.2.596", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msExchLabeledURI", "1.2.840.113556.1.2.593", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: false);
        yield return Attr("ownerBL", "1.2.840.113556.1.2.104", "2.5.5.1", singleValued: false, gc: false, systemOnly: true, indexed: false);

        // =====================================================================
        // ms-FRS attributes
        // =====================================================================
        yield return Attr("msFRS-Hub-Member", "1.2.840.113556.1.4.1693", "2.5.5.1", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msFRS-Topology-Pref", "1.2.840.113556.1.4.1692", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: false);

        // =====================================================================
        // ms-FVE (BitLocker) attributes
        // =====================================================================
        yield return Attr("msFVE-KeyPackage", "1.2.840.113556.1.4.1999", "2.5.5.10", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msFVE-RecoveryGuid", "1.2.840.113556.1.4.1964", "2.5.5.10", singleValued: true, gc: false, systemOnly: false, indexed: true);
        yield return Attr("msFVE-RecoveryPassword", "1.2.840.113556.1.4.1966", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msFVE-VolumeGuid", "1.2.840.113556.1.4.1998", "2.5.5.10", singleValued: true, gc: false, systemOnly: false, indexed: true);

        // =====================================================================
        // ms-ieee-80211 / ms-Imaging / ms-IIS / Msi attributes
        // =====================================================================
        yield return Attr("msieee80211-Data", "1.2.840.113556.1.4.1821", "2.5.5.10", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msieee80211-DataType", "1.2.840.113556.1.4.1822", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msieee80211-ID", "1.2.840.113556.1.4.1823", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msiFileList", "1.2.840.113556.1.4.823", "2.5.5.10", singleValued: false, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msIIS-FTPDir", "1.2.840.113556.1.4.1786", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msIIS-FTPRoot", "1.2.840.113556.1.4.1785", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msImaging-HashAlgorithm", "1.2.840.113556.1.4.2180", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msImaging-PSPIdentifier", "1.2.840.113556.1.4.2177", "2.5.5.10", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msImaging-PSPString", "1.2.840.113556.1.4.2178", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msImaging-ThumbprintHash", "1.2.840.113556.1.4.2179", "2.5.5.10", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msiScript", "1.2.840.113556.1.4.20", "2.5.5.10", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msiScriptName", "1.2.840.113556.1.4.814", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msiScriptPath", "1.2.840.113556.1.4.339", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msiScriptSize", "1.2.840.113556.1.4.815", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);

        // =====================================================================
        // ms-Kds (Key Distribution Service) attributes
        // =====================================================================
        yield return Attr("msKds-CreateTime", "1.2.840.113556.1.4.2176", "2.5.5.11", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msKds-DomainID", "1.2.840.113556.1.4.2175", "2.5.5.1", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msKds-KDFAlgorithmID", "1.2.840.113556.1.4.2169", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msKds-KDFParam", "1.2.840.113556.1.4.2170", "2.5.5.10", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msKds-PrivateKeyLength", "1.2.840.113556.1.4.2174", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msKds-PublicKeyLength", "1.2.840.113556.1.4.2173", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msKds-RootKeyData", "1.2.840.113556.1.4.2171", "2.5.5.10", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msKds-SecretAgreementAlgorithmID", "1.2.840.113556.1.4.2172", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msKds-SecretAgreementParam", "1.2.840.113556.1.4.2173", "2.5.5.10", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msKds-UseStartTime", "1.2.840.113556.1.4.2174", "2.5.5.16", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msKds-Version", "1.2.840.113556.1.4.2175", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);

        // =====================================================================
        // ms-TPM attributes
        // =====================================================================
        yield return Attr("msTPM-OwnerInformation", "1.2.840.113556.1.4.1966", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msTPM-OwnerInformationTemp", "1.2.840.113556.1.4.2108", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msTPM-SrkPubThumbprint", "1.2.840.113556.1.4.2109", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: true);
        yield return Attr("msTPM-TpmInformationForComputer", "1.2.840.113556.1.4.2107", "2.5.5.1", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msTPM-TpmInformationForComputerBL", "1.2.840.113556.1.4.2110", "2.5.5.1", singleValued: false, gc: false, systemOnly: true, indexed: false);

        // =====================================================================
        // ms-TS (Terminal Services) attributes
        // =====================================================================
        yield return Attr("msTSAllowLogon", "1.2.840.113556.1.4.1979", "2.5.5.8", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msTSBrokenConnectionAction", "1.2.840.113556.1.4.1980", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msTSConnectClientDrives", "1.2.840.113556.1.4.1981", "2.5.5.8", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msTSConnectPrinterDrives", "1.2.840.113556.1.4.1982", "2.5.5.8", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msTSDefaultToMainPrinter", "1.2.840.113556.1.4.1983", "2.5.5.8", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msTSEndpointData", "1.2.840.113556.1.4.2070", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msTSEndpointPlugin", "1.2.840.113556.1.4.2072", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msTSEndpointType", "1.2.840.113556.1.4.2071", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msTSExpireDate", "1.2.840.113556.1.4.1993", "2.5.5.11", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msTSExpireDate2", "1.2.840.113556.1.4.1994", "2.5.5.11", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msTSExpireDate3", "1.2.840.113556.1.4.1995", "2.5.5.11", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msTSExpireDate4", "1.2.840.113556.1.4.1996", "2.5.5.11", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msTSHomeDirectory", "1.2.840.113556.1.4.1984", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msTSHomeDrive", "1.2.840.113556.1.4.1985", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msTSInitialProgram", "1.2.840.113556.1.4.1986", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msTSLicenseVersion", "1.2.840.113556.1.4.1987", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msTSLicenseVersion2", "1.2.840.113556.1.4.1988", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msTSLicenseVersion3", "1.2.840.113556.1.4.1989", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msTSLicenseVersion4", "1.2.840.113556.1.4.1990", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msTSManagingLS", "1.2.840.113556.1.4.2003", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msTSManagingLS2", "1.2.840.113556.1.4.2004", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msTSManagingLS3", "1.2.840.113556.1.4.2005", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msTSManagingLS4", "1.2.840.113556.1.4.2006", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msTSMaxConnectionTime", "1.2.840.113556.1.4.1991", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msTSMaxDisconnectionTime", "1.2.840.113556.1.4.1992", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msTSMaxIdleTime", "1.2.840.113556.1.4.1993", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msTSPrimaryDesktop", "1.2.840.113556.1.4.2073", "2.5.5.1", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msTSPrimaryDesktopBL", "1.2.840.113556.1.4.2074", "2.5.5.1", singleValued: false, gc: false, systemOnly: true, indexed: false);
        yield return Attr("msTSProfilePath", "1.2.840.113556.1.4.1994", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msTSProperty01", "1.2.840.113556.1.4.1995", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msTSProperty02", "1.2.840.113556.1.4.1996", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msTSReconnectionAction", "1.2.840.113556.1.4.1997", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msTSRemoteControl", "1.2.840.113556.1.4.1998", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msTSSecondaryDesktopBL", "1.2.840.113556.1.4.2075", "2.5.5.1", singleValued: false, gc: false, systemOnly: true, indexed: false);
        yield return Attr("msTSSecondaryDesktops", "1.2.840.113556.1.4.2076", "2.5.5.1", singleValued: false, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msTSWorkDirectory", "1.2.840.113556.1.4.1999", "2.5.5.12", singleValued: true, gc: false, systemOnly: false, indexed: false);

        // =====================================================================
        // Remaining miscellaneous attributes (Mscope, ms-net, msNP, etc.)
        // =====================================================================
        yield return Attr("mscopeId", "1.2.840.113556.1.4.718", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msNPAllowDialin", "1.2.840.113556.1.4.1119", "2.5.5.8", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msNPCalledStationID", "1.2.840.113556.1.4.1124", "2.5.5.5", singleValued: false, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msNPCallingStationID", "1.2.840.113556.1.4.1123", "2.5.5.5", singleValued: false, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msNPSavedCallingStationID", "1.2.840.113556.1.4.1130", "2.5.5.5", singleValued: false, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msRADIUSCallbackNumber", "1.2.840.113556.1.4.1145", "2.5.5.5", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msRADIUSFramedIPAddress", "1.2.840.113556.1.4.1153", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msRADIUSFramedRoute", "1.2.840.113556.1.4.1158", "2.5.5.5", singleValued: false, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msRADIUSServiceType", "1.2.840.113556.1.4.1171", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msRASSavedCallbackNumber", "1.2.840.113556.1.4.1189", "2.5.5.5", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msRASSavedFramedIPAddress", "1.2.840.113556.1.4.1190", "2.5.5.9", singleValued: true, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msRASSavedFramedRoute", "1.2.840.113556.1.4.1191", "2.5.5.5", singleValued: false, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msRRASAttribute", "1.2.840.113556.1.4.884", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: false);
        yield return Attr("msRRASVendorAttributeEntry", "1.2.840.113556.1.4.885", "2.5.5.12", singleValued: false, gc: false, systemOnly: false, indexed: false);

        // =====================================================================
        // Locality/other standard attributes that start with L
        // =====================================================================
        // macAddress (starts with 'm' but listed in the L section in some references -- actually 'm')
        yield return Attr("macAddress", "1.3.6.1.1.1.1.22", "2.5.5.5", singleValued: false, gc: false, systemOnly: false, indexed: false);
    }

    private static AttributeSchemaEntry Attr(
        string name, string oid, string syntax,
        bool singleValued = true, bool gc = false, bool systemOnly = false,
        bool indexed = false, string description = null,
        int? rangeLower = null, int? rangeUpper = null)
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
        };
}
