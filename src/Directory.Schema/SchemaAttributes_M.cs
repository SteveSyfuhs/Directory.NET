using Directory.Core.Interfaces;

namespace Directory.Schema;

public static class SchemaAttributes_M
{
    public static IEnumerable<AttributeSchemaEntry> GetAttributes() => [
        // macAddress
        Attr("macAddress", "1.3.6.1.1.1.1.22", "2.5.5.12", singleValued: false),

        // machineArchitecture
        Attr("machineArchitecture", "1.2.840.113556.1.4.68", "2.5.5.9", singleValued: false),

        // machinePasswordChangeInterval
        Attr("machinePasswordChangeInterval", "1.2.840.113556.1.4.202", "2.5.5.16", singleValued: true),

        // machineRole
        Attr("machineRole", "1.2.840.113556.1.4.71", "2.5.5.9", singleValued: true),

        // machineWidePolicy
        Attr("machineWidePolicy", "1.2.840.113556.1.4.284", "2.5.5.10", singleValued: false),

        // managedBy
        Attr("managedBy", "1.2.840.113556.1.4.653", "2.5.5.1", singleValued: true),

        // managedObjects
        Attr("managedObjects", "1.2.840.113556.1.4.654", "2.5.5.1", singleValued: false, systemOnly: true),

        // manager
        Attr("manager", "0.9.2342.19200300.100.1.10", "2.5.5.1", singleValued: true, gc: true),

        // mAPIID
        Attr("mAPIID", "1.2.840.113556.1.2.49", "2.5.5.9", singleValued: true, systemOnly: true),

        // marshalledInterface
        Attr("marshalledInterface", "1.2.840.113556.1.4.72", "2.5.5.10", singleValued: false),

        // masteredBy
        Attr("masteredBy", "1.2.840.113556.1.4.1409", "2.5.5.1", singleValued: false, systemOnly: true),

        // maxPwdAge
        Attr("maxPwdAge", "1.2.840.113556.1.4.74", "2.5.5.16", singleValued: true),

        // maxRenewAge
        Attr("maxRenewAge", "1.2.840.113556.1.4.75", "2.5.5.16", singleValued: true),

        // maxStorage
        Attr("maxStorage", "1.2.840.113556.1.4.76", "2.5.5.16", singleValued: true),

        // maxTicketAge
        Attr("maxTicketAge", "1.2.840.113556.1.4.77", "2.5.5.16", singleValued: true),

        // mayContain
        Attr("mayContain", "1.2.840.113556.1.2.25", "2.5.5.2", singleValued: false, systemOnly: true),

        // meetingAdvertiseScope
        Attr("meetingAdvertiseScope", "1.2.840.113556.1.4.581", "2.5.5.12", singleValued: true),

        // meetingApplication
        Attr("meetingApplication", "1.2.840.113556.1.4.573", "2.5.5.12", singleValued: false),

        // meetingBandwidth
        Attr("meetingBandwidth", "1.2.840.113556.1.4.580", "2.5.5.9", singleValued: true),

        // meetingBlob
        Attr("meetingBlob", "1.2.840.113556.1.4.574", "2.5.5.10", singleValued: true),

        // meetingContactInfo
        Attr("meetingContactInfo", "1.2.840.113556.1.4.570", "2.5.5.12", singleValued: false),

        // meetingDescription
        Attr("meetingDescription", "1.2.840.113556.1.4.571", "2.5.5.12", singleValued: true),

        // meetingEndTime
        Attr("meetingEndTime", "1.2.840.113556.1.4.578", "2.5.5.11", singleValued: true),

        // meetingID
        Attr("meetingID", "1.2.840.113556.1.4.567", "2.5.5.12", singleValued: true, indexed: true),

        // meetingIP
        Attr("meetingIP", "1.2.840.113556.1.4.575", "2.5.5.12", singleValued: false),

        // meetingIsEncrypted
        Attr("meetingIsEncrypted", "1.2.840.113556.1.4.568", "2.5.5.12", singleValued: true),

        // meetingKeyword
        Attr("meetingKeyword", "1.2.840.113556.1.4.582", "2.5.5.12", singleValued: false),

        // meetingLanguage
        Attr("meetingLanguage", "1.2.840.113556.1.4.569", "2.5.5.12", singleValued: true),

        // meetingLocation
        Attr("meetingLocation", "1.2.840.113556.1.4.583", "2.5.5.12", singleValued: true),

        // meetingMaxParticipants
        Attr("meetingMaxParticipants", "1.2.840.113556.1.4.579", "2.5.5.9", singleValued: true),

        // meetingName
        Attr("meetingName", "1.2.840.113556.1.4.565", "2.5.5.12", singleValued: true, indexed: true),

        // meetingOriginator
        Attr("meetingOriginator", "1.2.840.113556.1.4.572", "2.5.5.12", singleValued: true),

        // meetingOwner
        Attr("meetingOwner", "1.2.840.113556.1.4.576", "2.5.5.12", singleValued: false),

        // meetingProtocol
        Attr("meetingProtocol", "1.2.840.113556.1.4.566", "2.5.5.12", singleValued: true, indexed: true),

        // meetingRating
        Attr("meetingRating", "1.2.840.113556.1.4.584", "2.5.5.12", singleValued: true),

        // meetingRecurrence
        Attr("meetingRecurrence", "1.2.840.113556.1.4.585", "2.5.5.12", singleValued: true),

        // meetingScope
        Attr("meetingScope", "1.2.840.113556.1.4.586", "2.5.5.12", singleValued: true),

        // meetingStartTime
        Attr("meetingStartTime", "1.2.840.113556.1.4.577", "2.5.5.11", singleValued: true),

        // meetingType
        Attr("meetingType", "1.2.840.113556.1.4.587", "2.5.5.12", singleValued: true),

        // meetingURL
        Attr("meetingURL", "1.2.840.113556.1.4.588", "2.5.5.12", singleValued: true),

        // member
        Attr("member", "2.5.4.31", "2.5.5.1", singleValued: false, gc: true),

        // memberNisNetgroup
        Attr("memberNisNetgroup", "1.3.6.1.1.1.1.13", "2.5.5.12", singleValued: false),

        // memberUid
        Attr("memberUid", "1.3.6.1.1.1.1.12", "2.5.5.12", singleValued: false),

        // memberOf (Is-Member-Of-DL)
        Attr("memberOf", "1.2.840.113556.1.2.102", "2.5.5.1", singleValued: false, gc: true, systemOnly: true, indexed: true),

        // mhsORAddress
        Attr("mhsORAddress", "1.2.840.113556.1.4.650", "2.5.5.12", singleValued: false),

        // minPwdAge
        Attr("minPwdAge", "1.2.840.113556.1.4.78", "2.5.5.16", singleValued: true),

        // minPwdLength
        Attr("minPwdLength", "1.2.840.113556.1.4.79", "2.5.5.9", singleValued: true),

        // minTicketAge
        Attr("minTicketAge", "1.2.840.113556.1.4.80", "2.5.5.16", singleValued: true),

        // modifiedCount
        Attr("modifiedCount", "1.2.840.113556.1.4.168", "2.5.5.16", singleValued: true, systemOnly: true),

        // modifiedCountAtLastProm
        Attr("modifiedCountAtLastProm", "1.2.840.113556.1.4.81", "2.5.5.16", singleValued: true, systemOnly: true),

        // modifyTimeStamp
        Attr("modifyTimeStamp", "2.5.18.2", "2.5.5.11", singleValued: true, gc: true, systemOnly: true),

        // moniker
        Attr("moniker", "1.2.840.113556.1.4.82", "2.5.5.10", singleValued: false),

        // monikerDisplayName
        Attr("monikerDisplayName", "1.2.840.113556.1.4.83", "2.5.5.12", singleValued: true),

        // moveTreeState
        Attr("moveTreeState", "1.2.840.113556.1.4.1305", "2.5.5.10", singleValued: true, systemOnly: true),

        // msAuthz-CentralAccessPolicyID
        Attr("msAuthz-CentralAccessPolicyID", "1.2.840.113556.1.4.2155", "2.5.5.12", singleValued: true),

        // msAuthz-EffectiveSecurityPolicy
        Attr("msAuthz-EffectiveSecurityPolicy", "1.2.840.113556.1.4.2152", "2.5.5.12", singleValued: true),

        // msAuthz-LastEffectiveSecurityPolicy
        Attr("msAuthz-LastEffectiveSecurityPolicy", "1.2.840.113556.1.4.2153", "2.5.5.12", singleValued: true),

        // msAuthz-MemberRulesInCentralAccessPolicy
        Attr("msAuthz-MemberRulesInCentralAccessPolicy", "1.2.840.113556.1.4.2156", "2.5.5.1", singleValued: false),

        // msAuthz-MemberRulesInCentralAccessPolicyBL
        Attr("msAuthz-MemberRulesInCentralAccessPolicyBL", "1.2.840.113556.1.4.2157", "2.5.5.1", singleValued: false, systemOnly: true),

        // msAuthz-ProposedSecurityPolicy
        Attr("msAuthz-ProposedSecurityPolicy", "1.2.840.113556.1.4.2154", "2.5.5.12", singleValued: true),

        // msAuthz-ResourceCondition
        Attr("msAuthz-ResourceCondition", "1.2.840.113556.1.4.2151", "2.5.5.12", singleValued: true),

        // msCOM-DefaultPartitionLink
        Attr("msCOM-DefaultPartitionLink", "1.2.840.113556.1.4.1427", "2.5.5.1", singleValued: true),

        // msCOM-ObjectId
        Attr("msCOM-ObjectId", "1.2.840.113556.1.4.1428", "2.5.5.10", singleValued: true),

        // msCOM-PartitionLink
        Attr("msCOM-PartitionLink", "1.2.840.113556.1.4.1423", "2.5.5.1", singleValued: false),

        // msCOM-PartitionSetLink
        Attr("msCOM-PartitionSetLink", "1.2.840.113556.1.4.1424", "2.5.5.1", singleValued: false),

        // msCOM-UserLink
        Attr("msCOM-UserLink", "1.2.840.113556.1.4.1425", "2.5.5.1", singleValued: false),

        // msCOM-UserPartitionSetLink
        Attr("msCOM-UserPartitionSetLink", "1.2.840.113556.1.4.1426", "2.5.5.1", singleValued: false),

        // mscopeId
        Attr("mscopeId", "1.2.840.113556.1.4.711", "2.5.5.12", singleValued: true),

        // ms-DFS-Comment-v2
        Attr("msDFS-Commentv2", "1.2.840.113556.1.4.2038", "2.5.5.12", singleValued: true),

        // ms-DFS-Generation-GUID-v2
        Attr("msDFS-GenerationGUIDv2", "1.2.840.113556.1.4.2039", "2.5.5.10", singleValued: true),

        // ms-DFS-Last-Modified-v2
        Attr("msDFS-LastModifiedv2", "1.2.840.113556.1.4.2040", "2.5.5.11", singleValued: true),

        // ms-DFS-Link-Identity-GUID-v2
        Attr("msDFS-LinkIdentityGUIDv2", "1.2.840.113556.1.4.2041", "2.5.5.10", singleValued: true),

        // ms-DFS-Link-Path-v2
        Attr("msDFS-LinkPathv2", "1.2.840.113556.1.4.2042", "2.5.5.12", singleValued: true),

        // ms-DFS-Link-Security-Descriptor-v2
        Attr("msDFS-LinkSecurityDescriptorv2", "1.2.840.113556.1.4.2043", "2.5.5.15", singleValued: true),

        // ms-DFS-Namespace-Identity-GUID-v2
        Attr("msDFS-NamespaceIdentityGUIDv2", "1.2.840.113556.1.4.2044", "2.5.5.10", singleValued: true),

        // ms-DFS-Properties-v2
        Attr("msDFS-Propertiesv2", "1.2.840.113556.1.4.2045", "2.5.5.10", singleValued: true),

        // ms-DFS-Schema-Major-Version
        Attr("msDFS-SchemaMajorVersion", "1.2.840.113556.1.4.2046", "2.5.5.9", singleValued: true),

        // ms-DFS-Schema-Minor-Version
        Attr("msDFS-SchemaMinorVersion", "1.2.840.113556.1.4.2047", "2.5.5.9", singleValued: true),

        // ms-DFS-Short-Name-Link-Path-v2
        Attr("msDFS-ShortNameLinkPathv2", "1.2.840.113556.1.4.2048", "2.5.5.12", singleValued: true),

        // ms-DFS-Target-List-v2
        Attr("msDFS-TargetListv2", "1.2.840.113556.1.4.2049", "2.5.5.10", singleValued: true),

        // ms-DFS-Ttl-v2
        Attr("msDFS-Ttlv2", "1.2.840.113556.1.4.2050", "2.5.5.9", singleValued: true),

        // ms-DFSR-CachePolicy
        Attr("msDFSR-CachePolicy", "1.2.840.113556.1.6.13.3.35", "2.5.5.9", singleValued: true),

        // ms-DFSR-CommonStagingPath
        Attr("msDFSR-CommonStagingPath", "1.2.840.113556.1.6.13.3.34", "2.5.5.12", singleValued: true),

        // ms-DFSR-CommonStagingSizeInMb
        Attr("msDFSR-CommonStagingSizeInMb", "1.2.840.113556.1.6.13.3.33", "2.5.5.16", singleValued: true),

        // ms-DFSR-ComputerReference
        Attr("msDFSR-ComputerReference", "1.2.840.113556.1.6.13.3.1", "2.5.5.1", singleValued: true),

        // ms-DFSR-ComputerReferenceBL
        Attr("msDFSR-ComputerReferenceBL", "1.2.840.113556.1.6.13.3.2", "2.5.5.1", singleValued: false, systemOnly: true),

        // ms-DFSR-ConflictPath
        Attr("msDFSR-ConflictPath", "1.2.840.113556.1.6.13.3.4", "2.5.5.12", singleValued: true),

        // ms-DFSR-ConflictSizeInMb
        Attr("msDFSR-ConflictSizeInMb", "1.2.840.113556.1.6.13.3.3", "2.5.5.16", singleValued: true),

        // ms-DFSR-ContentSetGuid
        Attr("msDFSR-ContentSetGuid", "1.2.840.113556.1.6.13.3.5", "2.5.5.10", singleValued: true),

        // ms-DFSR-DefaultCompressionExclusionFilter
        Attr("msDFSR-DefaultCompressionExclusionFilter", "1.2.840.113556.1.6.13.3.32", "2.5.5.12", singleValued: true),

        // ms-DFSR-DeletedPath
        Attr("msDFSR-DeletedPath", "1.2.840.113556.1.6.13.3.7", "2.5.5.12", singleValued: true),

        // ms-DFSR-DeletedSizeInMb
        Attr("msDFSR-DeletedSizeInMb", "1.2.840.113556.1.6.13.3.6", "2.5.5.16", singleValued: true),

        // ms-DFSR-DfsLinkTarget
        Attr("msDFSR-DfsLinkTarget", "1.2.840.113556.1.6.13.3.36", "2.5.5.12", singleValued: true),

        // ms-DFSR-DfsPath
        Attr("msDFSR-DfsPath", "1.2.840.113556.1.6.13.3.8", "2.5.5.12", singleValued: true),

        // ms-DFSR-DirectoryFilter
        Attr("msDFSR-DirectoryFilter", "1.2.840.113556.1.6.13.3.10", "2.5.5.12", singleValued: true),

        // ms-DFSR-DisablePacketPrivacy
        Attr("msDFSR-DisablePacketPrivacy", "1.2.840.113556.1.6.13.3.37", "2.5.5.8", singleValued: true),

        // ms-DFSR-Enabled
        Attr("msDFSR-Enabled", "1.2.840.113556.1.6.13.3.9", "2.5.5.8", singleValued: true),

        // ms-DFSR-Extension
        Attr("msDFSR-Extension", "1.2.840.113556.1.6.13.3.31", "2.5.5.10", singleValued: true),

        // ms-DFSR-FileFilter
        Attr("msDFSR-FileFilter", "1.2.840.113556.1.6.13.3.11", "2.5.5.12", singleValued: true),

        // ms-DFSR-Flags
        Attr("msDFSR-Flags", "1.2.840.113556.1.6.13.3.12", "2.5.5.9", singleValued: true),

        // ms-DFSR-Keywords
        Attr("msDFSR-Keywords", "1.2.840.113556.1.6.13.3.13", "2.5.5.12", singleValued: true),

        // ms-DFSR-MaxAgeInCacheInMin
        Attr("msDFSR-MaxAgeInCacheInMin", "1.2.840.113556.1.6.13.3.14", "2.5.5.9", singleValued: true),

        // ms-DFSR-MemberReference
        Attr("msDFSR-MemberReference", "1.2.840.113556.1.6.13.3.15", "2.5.5.1", singleValued: true),

        // ms-DFSR-MemberReferenceBL
        Attr("msDFSR-MemberReferenceBL", "1.2.840.113556.1.6.13.3.16", "2.5.5.1", singleValued: false, systemOnly: true),

        // ms-DFSR-MinDurationCacheInMin
        Attr("msDFSR-MinDurationCacheInMin", "1.2.840.113556.1.6.13.3.17", "2.5.5.9", singleValued: true),

        // ms-DFSR-OnDemandExclusionDirectoryFilter
        Attr("msDFSR-OnDemandExclusionDirectoryFilter", "1.2.840.113556.1.6.13.3.38", "2.5.5.12", singleValued: true),

        // ms-DFSR-OnDemandExclusionFileFilter
        Attr("msDFSR-OnDemandExclusionFileFilter", "1.2.840.113556.1.6.13.3.39", "2.5.5.12", singleValued: true),

        // ms-DFSR-Options
        Attr("msDFSR-Options", "1.2.840.113556.1.6.13.3.18", "2.5.5.9", singleValued: true),

        // ms-DFSR-Options2
        Attr("msDFSR-Options2", "1.2.840.113556.1.6.13.3.40", "2.5.5.9", singleValued: true),

        // ms-DFSR-Priority
        Attr("msDFSR-Priority", "1.2.840.113556.1.6.13.3.19", "2.5.5.9", singleValued: true),

        // ms-DFSR-RdcEnabled
        Attr("msDFSR-RdcEnabled", "1.2.840.113556.1.6.13.3.20", "2.5.5.8", singleValued: true),

        // ms-DFSR-RdcMinFileSizeInKb
        Attr("msDFSR-RdcMinFileSizeInKb", "1.2.840.113556.1.6.13.3.21", "2.5.5.16", singleValued: true),

        // ms-DFSR-ReadOnly
        Attr("msDFSR-ReadOnly", "1.2.840.113556.1.6.13.3.41", "2.5.5.8", singleValued: true),

        // ms-DFSR-ReplicationGroupGuid
        Attr("msDFSR-ReplicationGroupGuid", "1.2.840.113556.1.6.13.3.22", "2.5.5.10", singleValued: true),

        // ms-DFSR-ReplicationGroupType
        Attr("msDFSR-ReplicationGroupType", "1.2.840.113556.1.6.13.3.23", "2.5.5.9", singleValued: true),

        // ms-DFSR-RootFence
        Attr("msDFSR-RootFence", "1.2.840.113556.1.6.13.3.24", "2.5.5.9", singleValued: true),

        // ms-DFSR-RootPath
        Attr("msDFSR-RootPath", "1.2.840.113556.1.6.13.3.25", "2.5.5.12", singleValued: true),

        // ms-DFSR-RootSizeInMb
        Attr("msDFSR-RootSizeInMb", "1.2.840.113556.1.6.13.3.26", "2.5.5.16", singleValued: true),

        // ms-DFSR-Schedule
        Attr("msDFSR-Schedule", "1.2.840.113556.1.6.13.3.27", "2.5.5.10", singleValued: true),

        // ms-DFSR-StagingCleanupTriggerInPercent
        Attr("msDFSR-StagingCleanupTriggerInPercent", "1.2.840.113556.1.6.13.3.28", "2.5.5.9", singleValued: true),

        // ms-DFSR-StagingPath
        Attr("msDFSR-StagingPath", "1.2.840.113556.1.6.13.3.29", "2.5.5.12", singleValued: true),

        // ms-DFSR-StagingSizeInMb
        Attr("msDFSR-StagingSizeInMb", "1.2.840.113556.1.6.13.3.30", "2.5.5.16", singleValued: true),

        // ms-DFSR-TombstoneExpiryInMin
        Attr("msDFSR-TombstoneExpiryInMin", "1.2.840.113556.1.6.13.3.42", "2.5.5.9", singleValued: true),

        // ms-DFSR-Version
        Attr("msDFSR-Version", "1.2.840.113556.1.6.13.3.43", "2.5.5.12", singleValued: true),

        // ms-DNS-DNSKEY-Records
        Attr("msDNS-DNSKEYRecords", "1.2.840.113556.1.4.2143", "2.5.5.10", singleValued: false),

        // ms-DNS-DNSKEY-Record-Set-TTL
        Attr("msDNS-DNSKEYRecordSetTTL", "1.2.840.113556.1.4.2139", "2.5.5.9", singleValued: true),

        // ms-DNS-DS-Record-Algorithms
        Attr("msDNS-DSRecordAlgorithms", "1.2.840.113556.1.4.2140", "2.5.5.9", singleValued: true),

        // ms-DNS-DS-Record-Set-TTL
        Attr("msDNS-DSRecordSetTTL", "1.2.840.113556.1.4.2141", "2.5.5.9", singleValued: true),

        // ms-DNS-Is-Signed
        Attr("msDNS-IsSigned", "1.2.840.113556.1.4.2130", "2.5.5.8", singleValued: true),

        // ms-DNS-Keymaster-Zones
        Attr("msDNS-KeymasterZones", "1.2.840.113556.1.4.2128", "2.5.5.1", singleValued: false),

        // ms-DNS-Maintain-Trust-Anchor
        Attr("msDNS-MaintainTrustAnchor", "1.2.840.113556.1.4.2131", "2.5.5.9", singleValued: true),

        // ms-DNS-NSEC3-Current-Salt
        Attr("msDNS-NSEC3CurrentSalt", "1.2.840.113556.1.4.2146", "2.5.5.12", singleValued: true),

        // ms-DNS-NSEC3-Hash-Algorithm
        Attr("msDNS-NSEC3HashAlgorithm", "1.2.840.113556.1.4.2133", "2.5.5.9", singleValued: true),

        // ms-DNS-NSEC3-Iterations
        Attr("msDNS-NSEC3Iterations", "1.2.840.113556.1.4.2134", "2.5.5.9", singleValued: true),

        // ms-DNS-NSEC3-OptOut
        Attr("msDNS-NSEC3OptOut", "1.2.840.113556.1.4.2132", "2.5.5.8", singleValued: true),

        // ms-DNS-NSEC3-Random-Salt-Length
        Attr("msDNS-NSEC3RandomSaltLength", "1.2.840.113556.1.4.2135", "2.5.5.9", singleValued: true),

        // ms-DNS-NSEC3-User-Salt
        Attr("msDNS-NSEC3UserSalt", "1.2.840.113556.1.4.2147", "2.5.5.12", singleValued: true),

        // ms-DNS-Parent-Has-Secure-Delegation
        Attr("msDNS-ParentHasSecureDelegation", "1.2.840.113556.1.4.2136", "2.5.5.8", singleValued: true),

        // ms-DNS-Propagation-Time
        Attr("msDNS-PropagationTime", "1.2.840.113556.1.4.2137", "2.5.5.9", singleValued: true),

        // ms-DNS-RFC5011-Key-Rollovers
        Attr("msDNS-RFC5011KeyRollovers", "1.2.840.113556.1.4.2138", "2.5.5.8", singleValued: true),

        // ms-DNS-Secure-Delegation-Polling-Period
        Attr("msDNS-SecureDelegationPollingPeriod", "1.2.840.113556.1.4.2148", "2.5.5.9", singleValued: true),

        // ms-DNS-Signature-Inception-Offset
        Attr("msDNS-SignatureInceptionOffset", "1.2.840.113556.1.4.2142", "2.5.5.9", singleValued: true),

        // ms-DNS-Signing-Key-Descriptors
        Attr("msDNS-SigningKeyDescriptors", "1.2.840.113556.1.4.2144", "2.5.5.12", singleValued: false),

        // ms-DNS-Signing-Keys
        Attr("msDNS-SigningKeys", "1.2.840.113556.1.4.2145", "2.5.5.10", singleValued: false),

        // ms-DNS-Sign-With-NSEC3
        Attr("msDNS-SignWithNSEC3", "1.2.840.113556.1.4.2129", "2.5.5.8", singleValued: true),

        // MS-DRM-Identity-Certificate
        Attr("msDRM-IdentityCertificate", "1.2.840.113556.1.4.1843", "2.5.5.10", singleValued: false),

        // ms-DS-Additional-Dns-Host-Name
        Attr("msDS-AdditionalDnsHostName", "1.2.840.113556.1.4.1717", "2.5.5.12", singleValued: false, indexed: true),

        // ms-DS-Additional-Sam-Account-Name
        Attr("msDS-AdditionalSamAccountName", "1.2.840.113556.1.4.1718", "2.5.5.12", singleValued: false, indexed: true),

        // ms-DS-Allowed-DNS-Suffixes
        Attr("msDS-AllowedDNSSuffixes", "1.2.840.113556.1.4.1710", "2.5.5.12", singleValued: false),

        // ms-DS-Allowed-To-Act-On-Behalf-Of-Other-Identity
        Attr("msDS-AllowedToActOnBehalfOfOtherIdentity", "1.2.840.113556.1.4.2182", "2.5.5.15", singleValued: true),

        // ms-DS-Allowed-To-Delegate-To
        Attr("msDS-AllowedToDelegateTo", "1.2.840.113556.1.4.1787", "2.5.5.12", singleValued: false),

        // MS-DS-All-Users-Trust-Quota
        Attr("msDS-AllUsersTrustQuota", "1.2.840.113556.1.4.1789", "2.5.5.9", singleValued: true),

        // ms-DS-Applies-To-Resource-Types
        Attr("msDS-AppliesToResourceTypes", "1.2.840.113556.1.4.2103", "2.5.5.12", singleValued: false),

        // ms-DS-Approx-Immed-Subordinates
        Attr("msDS-Approx-Immed-Subordinates", "1.2.840.113556.1.4.1669", "2.5.5.9", singleValued: true, systemOnly: true),

        // ms-DS-AuthenticatedAt-DC
        Attr("msDS-AuthenticatedAtDC", "1.2.840.113556.1.4.1958", "2.5.5.1", singleValued: false, systemOnly: true),

        // ms-DS-AuthenticatedTo-Accountlist
        Attr("msDS-AuthenticatedToAccountlist", "1.2.840.113556.1.4.1957", "2.5.5.1", singleValued: false, systemOnly: true),

        // ms-DS-Auxiliary-Classes
        Attr("msDS-Auxiliary-Classes", "1.2.840.113556.1.4.1458", "2.5.5.12", singleValued: false, systemOnly: true),

        // ms-DS-Az-Application-Data
        Attr("msDS-AzApplicationData", "1.2.840.113556.1.4.1819", "2.5.5.12", singleValued: true),

        // ms-DS-Az-Application-Name
        Attr("msDS-AzApplicationName", "1.2.840.113556.1.4.1798", "2.5.5.12", singleValued: true),

        // ms-DS-Az-Application-Version
        Attr("msDS-AzApplicationVersion", "1.2.840.113556.1.4.1817", "2.5.5.12", singleValued: true),

        // ms-DS-Az-Biz-Rule
        Attr("msDS-AzBizRule", "1.2.840.113556.1.4.1801", "2.5.5.12", singleValued: true),

        // ms-DS-Az-Biz-Rule-Language
        Attr("msDS-AzBizRuleLanguage", "1.2.840.113556.1.4.1802", "2.5.5.12", singleValued: true),

        // ms-DS-Az-Class-ID
        Attr("msDS-AzClassId", "1.2.840.113556.1.4.1818", "2.5.5.12", singleValued: true),

        // ms-DS-Az-Domain-Timeout
        Attr("msDS-AzDomainTimeout", "1.2.840.113556.1.4.1797", "2.5.5.9", singleValued: true),

        // ms-DS-Az-Generate-Audits
        Attr("msDS-AzGenerateAudits", "1.2.840.113556.1.4.1805", "2.5.5.8", singleValued: true),

        // ms-DS-Az-Generic-Data
        Attr("msDS-AzGenericData", "1.2.840.113556.1.4.1820", "2.5.5.12", singleValued: true),

        // ms-DS-Az-Last-Imported-Biz-Rule-Path
        Attr("msDS-AzLastImportedBizRulePath", "1.2.840.113556.1.4.1803", "2.5.5.12", singleValued: true),

        // ms-DS-Az-LDAP-Query
        Attr("msDS-AzLDAPQuery", "1.2.840.113556.1.4.1792", "2.5.5.12", singleValued: true),

        // ms-DS-Az-Major-Version
        Attr("msDS-AzMajorVersion", "1.2.840.113556.1.4.1824", "2.5.5.9", singleValued: true),

        // ms-DS-Az-Minor-Version
        Attr("msDS-AzMinorVersion", "1.2.840.113556.1.4.1825", "2.5.5.9", singleValued: true),

        // ms-DS-Az-Object-Guid
        Attr("msDS-AzObjectGuid", "1.2.840.113556.1.4.1949", "2.5.5.10", singleValued: true),

        // ms-DS-Az-Operation-ID
        Attr("msDS-AzOperationID", "1.2.840.113556.1.4.1800", "2.5.5.9", singleValued: true),

        // ms-DS-Az-Scope-Name
        Attr("msDS-AzScopeName", "1.2.840.113556.1.4.1799", "2.5.5.12", singleValued: true),

        // ms-DS-Az-Script-Engine-Cache-Max
        Attr("msDS-AzScriptEngineCacheMax", "1.2.840.113556.1.4.1796", "2.5.5.9", singleValued: true),

        // ms-DS-Az-Script-Timeout
        Attr("msDS-AzScriptTimeout", "1.2.840.113556.1.4.1795", "2.5.5.9", singleValued: true),

        // ms-DS-Az-Task-Is-Role-Definition
        Attr("msDS-AzTaskIsRoleDefinition", "1.2.840.113556.1.4.1810", "2.5.5.8", singleValued: true),

        // ms-DS-Behavior-Version
        Attr("msDS-Behavior-Version", "1.2.840.113556.1.4.1459", "2.5.5.9", singleValued: true, systemOnly: true),

        // ms-DS-BridgeHead-Servers-Used
        Attr("msDS-BridgeHeadServersUsed", "1.2.840.113556.1.4.1465", "2.5.5.1", singleValued: false, systemOnly: true),

        // ms-DS-Byte-Array
        Attr("msDS-ByteArray", "1.2.840.113556.1.4.1830", "2.5.5.10", singleValued: true),

        // ms-DS-Cached-Membership
        Attr("msDS-Cached-Membership", "1.2.840.113556.1.4.1441", "2.5.5.10", singleValued: true, systemOnly: true),

        // ms-DS-Cached-Membership-Time-Stamp
        Attr("msDS-Cached-Membership-Time-Stamp", "1.2.840.113556.1.4.1442", "2.5.5.16", singleValued: true, systemOnly: true),

        // ms-DS-Claim-Attribute-Source
        Attr("msDS-ClaimAttributeSource", "1.2.840.113556.1.4.2098", "2.5.5.1", singleValued: true),

        // ms-DS-Claim-Is-Single-Valued
        Attr("msDS-ClaimIsSingleValued", "1.2.840.113556.1.4.2099", "2.5.5.8", singleValued: true),

        // ms-DS-Claim-Is-Value-Space-Restricted
        Attr("msDS-ClaimIsValueSpaceRestricted", "1.2.840.113556.1.4.2100", "2.5.5.8", singleValued: true),

        // ms-DS-Claim-Possible-Values
        Attr("msDS-ClaimPossibleValues", "1.2.840.113556.1.4.2097", "2.5.5.12", singleValued: true),

        // ms-DS-Claim-Shares-Possible-Values-With
        Attr("msDS-ClaimSharesPossibleValuesWith", "1.2.840.113556.1.4.2101", "2.5.5.1", singleValued: true),

        // ms-DS-Claim-Shares-Possible-Values-With-BL
        Attr("msDS-ClaimSharesPossibleValuesWithBL", "1.2.840.113556.1.4.2102", "2.5.5.1", singleValued: false, systemOnly: true),

        // ms-DS-Claim-Source
        Attr("msDS-ClaimSource", "1.2.840.113556.1.4.2157", "2.5.5.12", singleValued: true),

        // ms-DS-Claim-Source-Type
        Attr("msDS-ClaimSourceType", "1.2.840.113556.1.4.2158", "2.5.5.12", singleValued: true),

        // ms-DS-Claim-Type-Applies-To-Class
        Attr("msDS-ClaimTypeAppliesToClass", "1.2.840.113556.1.4.2104", "2.5.5.1", singleValued: false),

        // ms-DS-Claim-Value-Type
        Attr("msDS-ClaimValueType", "1.2.840.113556.1.4.2105", "2.5.5.16", singleValued: true),

        // MS-DS-Consistency-Child-Count
        Attr("ms-DS-ConsistencyChildCount", "1.2.840.113556.1.4.1461", "2.5.5.9", singleValued: true, systemOnly: true),

        // MS-DS-Consistency-Guid
        Attr("ms-DS-ConsistencyGuid", "1.2.840.113556.1.4.1462", "2.5.5.10", singleValued: true),

        // MS-DS-Creator-SID
        Attr("ms-DS-CreatorSID", "1.2.840.113556.1.4.1460", "2.5.5.17", singleValued: true, systemOnly: true),

        // ms-DS-Date-Time
        Attr("msDS-DateTime", "1.2.840.113556.1.4.1831", "2.5.5.11", singleValued: true),

        // ms-DS-Default-Quota
        Attr("msDS-DefaultQuota", "1.2.840.113556.1.4.1845", "2.5.5.9", singleValued: true),

        // ms-DS-Deleted-Object-Lifetime
        Attr("msDS-DeletedObjectLifetime", "1.2.840.113556.1.4.2060", "2.5.5.9", singleValued: true),

        // ms-DS-Disable-For-Instances
        Attr("msDS-DisableForInstances", "1.2.840.113556.1.4.2070", "2.5.5.1", singleValued: false),

        // ms-DS-Disable-For-Instances-BL
        Attr("msDS-DisableForInstancesBL", "1.2.840.113556.1.4.2071", "2.5.5.1", singleValued: false, systemOnly: true),

        // ms-DS-DnsRootAlias
        Attr("msDS-DnsRootAlias", "1.2.840.113556.1.4.1719", "2.5.5.12", singleValued: true),

        // ms-DS-Egress-Claims-Transformation-Policy
        Attr("msDS-EgressClaimsTransformationPolicy", "1.2.840.113556.1.4.2106", "2.5.5.1", singleValued: true),

        // ms-DS-Enabled-Feature
        Attr("msDS-EnabledFeature", "1.2.840.113556.1.4.2061", "2.5.5.1", singleValued: false),

        // ms-DS-Enabled-Feature-BL
        Attr("msDS-EnabledFeatureBL", "1.2.840.113556.1.4.2069", "2.5.5.1", singleValued: false, systemOnly: true),

        // ms-DS-Entry-Time-To-Die
        Attr("msDS-Entry-Time-To-Die", "1.2.840.113556.1.4.1622", "2.5.5.11", singleValued: true, indexed: true),

        // ms-DS-ExecuteScriptPassword
        Attr("msDS-ExecuteScriptPassword", "1.2.840.113556.1.4.1783", "2.5.5.10", singleValued: true, systemOnly: true),

        // ms-DS-External-Key
        Attr("msDS-ExternalKey", "1.2.840.113556.1.4.1711", "2.5.5.10", singleValued: true),

        // ms-DS-External-Store
        Attr("msDS-ExternalStore", "1.2.840.113556.1.4.1712", "2.5.5.12", singleValued: true),

        // ms-DS-ExternalDirectoryObjectId
        Attr("msDS-ExternalDirectoryObjectId", "1.2.840.113556.1.4.2310", "2.5.5.12", singleValued: true, indexed: true),

        // ms-DS-Failed-Interactive-Logon-Count
        Attr("msDS-FailedInteractiveLogonCount", "1.2.840.113556.1.4.1967", "2.5.5.9", singleValued: true),

        // ms-DS-Failed-Interactive-Logon-Count-At-Last-Successful-Logon
        Attr("msDS-FailedInteractiveLogonCountAtLastSuccessfulLogon", "1.2.840.113556.1.4.1968", "2.5.5.9", singleValued: true),

        // ms-DS-Filter-Containers
        Attr("msDS-FilterContainers", "1.2.840.113556.1.4.1713", "2.5.5.12", singleValued: false),

        // ms-DS-Generation-Id
        Attr("msDS-GenerationId", "1.2.840.113556.1.4.2166", "2.5.5.10", singleValued: true),

        // ms-DS-GeoCoordinates-Altitude
        Attr("msDS-GeoCoordinatesAltitude", "1.2.840.113556.1.4.2183", "2.5.5.16", singleValued: true),

        // ms-DS-GeoCoordinates-Latitude
        Attr("msDS-GeoCoordinatesLatitude", "1.2.840.113556.1.4.2184", "2.5.5.16", singleValued: true),

        // ms-DS-GeoCoordinates-Longitude
        Attr("msDS-GeoCoordinatesLongitude", "1.2.840.113556.1.4.2185", "2.5.5.16", singleValued: true),

        // ms-DS-GroupMSAMembership
        Attr("msDS-GroupMSAMembership", "1.2.840.113556.1.4.2196", "2.5.5.15", singleValued: true),

        // ms-DS-HAB-Seniority-Index
        Attr("msDS-HABSeniorityIndex", "1.2.840.113556.1.4.1997", "2.5.5.9", singleValued: true),

        // ms-DS-Has-Domain-NCs
        Attr("msDS-HasDomainNCs", "1.2.840.113556.1.4.1820", "2.5.5.1", singleValued: false, systemOnly: true),

        // ms-DS-Has-Full-Replica-NCs
        Attr("msDS-HasFullReplicaNCs", "1.2.840.113556.1.4.1925", "2.5.5.1", singleValued: false, systemOnly: true),

        // ms-DS-Has-Instantiated-NCs
        Attr("msDS-HasInstantiatedNCs", "1.2.840.113556.1.4.1709", "2.5.5.7", singleValued: false, systemOnly: true),

        // ms-DS-Has-Master-NCs
        Attr("msDS-HasMasterNCs", "1.2.840.113556.1.4.1836", "2.5.5.1", singleValued: false, systemOnly: true),

        // ms-DS-Host-Service-Account
        Attr("msDS-HostServiceAccount", "1.2.840.113556.1.4.2057", "2.5.5.1", singleValued: false),

        // ms-DS-Host-Service-Account-BL
        Attr("msDS-HostServiceAccountBL", "1.2.840.113556.1.4.2058", "2.5.5.1", singleValued: false, systemOnly: true),

        // ms-DS-Ingress-Claims-Transformation-Policy
        Attr("msDS-IngressClaimsTransformationPolicy", "1.2.840.113556.1.4.2107", "2.5.5.1", singleValued: true),

        // ms-DS-Integer
        Attr("msDS-Integer", "1.2.840.113556.1.4.1832", "2.5.5.9", singleValued: true),

        // ms-DS-IntId
        Attr("msDS-IntId", "1.2.840.113556.1.4.1716", "2.5.5.9", singleValued: true, systemOnly: true),

        // ms-DS-Is-Domain-For
        Attr("msDS-IsDomainFor", "1.2.840.113556.1.4.1933", "2.5.5.1", singleValued: false, systemOnly: true),

        // ms-DS-Is-Full-Replica-For
        Attr("msDS-IsFullReplicaFor", "1.2.840.113556.1.4.1932", "2.5.5.1", singleValued: false, systemOnly: true),

        // ms-DS-isGC
        Attr("msDS-isGC", "1.2.840.113556.1.4.1959", "2.5.5.8", singleValued: true, systemOnly: true),

        // ms-DS-Is-Partial-Replica-For
        Attr("msDS-IsPartialReplicaFor", "1.2.840.113556.1.4.1934", "2.5.5.1", singleValued: false, systemOnly: true),

        // ms-DS-Is-Possible-Values-Present
        Attr("msDS-IsPossibleValuesPresent", "1.2.840.113556.1.4.2108", "2.5.5.8", singleValued: true),

        // ms-DS-Is-Primary-Computer-For
        Attr("msDS-IsPrimaryComputerFor", "1.2.840.113556.1.4.2168", "2.5.5.1", singleValued: false, systemOnly: true),

        // ms-DS-isRODC
        Attr("msDS-isRODC", "1.2.840.113556.1.4.1960", "2.5.5.8", singleValued: true, systemOnly: true),

        // ms-DS-Is-Used-As-Resource-Security-Attribute
        Attr("msDS-IsUsedAsResourceSecurityAttribute", "1.2.840.113556.1.4.2095", "2.5.5.8", singleValued: true),

        // ms-DS-Is-User-Cachable-At-Rodc
        Attr("msDS-IsUserCachableAtRodc", "1.2.840.113556.1.4.1961", "2.5.5.9", singleValued: true, systemOnly: true),

        // ms-DS-KeyVersionNumber
        Attr("msDS-KeyVersionNumber", "1.2.840.113556.1.4.1782", "2.5.5.9", singleValued: true, systemOnly: true),

        // ms-DS-KrbTgt-Link
        Attr("msDS-KrbTgtLink", "1.2.840.113556.1.4.1923", "2.5.5.1", singleValued: true),

        // ms-DS-KrbTgt-Link-BL
        Attr("msDS-KrbTgtLinkBL", "1.2.840.113556.1.4.1929", "2.5.5.1", singleValued: false, systemOnly: true),

        // ms-DS-Last-Failed-Interactive-Logon-Time
        Attr("msDS-LastFailedInteractiveLogonTime", "1.2.840.113556.1.4.1966", "2.5.5.16", singleValued: true),

        // ms-DS-Last-Known-RDN
        Attr("msDS-LastKnownRDN", "1.2.840.113556.1.4.2067", "2.5.5.12", singleValued: true, systemOnly: true),

        // ms-DS-Last-Successful-Interactive-Logon-Time
        Attr("msDS-LastSuccessfulInteractiveLogonTime", "1.2.840.113556.1.4.1965", "2.5.5.16", singleValued: true),

        // ms-DS-local-Effective-Deletion-Time
        Attr("msDS-LocalEffectiveDeletionTime", "1.2.840.113556.1.4.2059", "2.5.5.11", singleValued: true, systemOnly: true),

        // ms-DS-local-Effective-Recycle-Time
        Attr("msDS-LocalEffectiveRecycleTime", "1.2.840.113556.1.4.2062", "2.5.5.11", singleValued: true, systemOnly: true),

        // ms-DS-Lockout-Duration
        Attr("msDS-LockoutDuration", "1.2.840.113556.1.4.2018", "2.5.5.16", singleValued: true),

        // ms-DS-Lockout-Observation-Window
        Attr("msDS-LockoutObservationWindow", "1.2.840.113556.1.4.2020", "2.5.5.16", singleValued: true),

        // ms-DS-Lockout-Threshold
        Attr("msDS-LockoutThreshold", "1.2.840.113556.1.4.2019", "2.5.5.9", singleValued: true),

        // ms-DS-Logon-Time-Sync-Interval
        Attr("msDS-LogonTimeSyncInterval", "1.2.840.113556.1.4.1784", "2.5.5.9", singleValued: true),

        // MS-DS-Machine-Account-Quota
        Attr("ms-DS-MachineAccountQuota", "1.2.840.113556.1.4.1411", "2.5.5.9", singleValued: true),

        // ms-DS-ManagedPassword
        Attr("msDS-ManagedPassword", "1.2.840.113556.1.4.2197", "2.5.5.10", singleValued: true, systemOnly: true),

        // ms-DS-ManagedPasswordId
        Attr("msDS-ManagedPasswordId", "1.2.840.113556.1.4.2198", "2.5.5.10", singleValued: true, systemOnly: true),

        // ms-DS-ManagedPasswordInterval
        Attr("msDS-ManagedPasswordInterval", "1.2.840.113556.1.4.2199", "2.5.5.9", singleValued: true),

        // ms-DS-ManagedPasswordPreviousId
        Attr("msDS-ManagedPasswordPreviousId", "1.2.840.113556.1.4.2200", "2.5.5.10", singleValued: true, systemOnly: true),

        // ms-DS-Mastered-By
        Attr("msDS-MasteredBy", "1.2.840.113556.1.4.1837", "2.5.5.1", singleValued: false, systemOnly: true),

        // ms-DS-Maximum-Password-Age
        Attr("msDS-MaximumPasswordAge", "1.2.840.113556.1.4.2012", "2.5.5.16", singleValued: true),

        // ms-DS-Max-Values
        Attr("msDS-MaxValues", "1.2.840.113556.1.4.1714", "2.5.5.9", singleValued: true),

        // ms-DS-Members-For-Az-Role
        Attr("msDS-MembersForAzRole", "1.2.840.113556.1.4.1806", "2.5.5.1", singleValued: false),

        // ms-DS-Members-For-Az-Role-BL
        Attr("msDS-MembersForAzRoleBL", "1.2.840.113556.1.4.1807", "2.5.5.1", singleValued: false, systemOnly: true),

        // ms-DS-Members-Of-Resource-Property-List
        Attr("msDS-MembersOfResourcePropertyList", "1.2.840.113556.1.4.2103", "2.5.5.1", singleValued: false),

        // ms-DS-Members-Of-Resource-Property-List-BL
        Attr("msDS-MembersOfResourcePropertyListBL", "1.2.840.113556.1.4.2104", "2.5.5.1", singleValued: false, systemOnly: true),

        // ms-DS-Member-Transitive
        Attr("msDS-MemberTransitive", "1.2.840.113556.1.4.2111", "2.5.5.1", singleValued: false, systemOnly: true),

        // ms-DS-Minimum-Password-Age
        Attr("msDS-MinimumPasswordAge", "1.2.840.113556.1.4.2013", "2.5.5.16", singleValued: true),

        // ms-DS-Minimum-Password-Length
        Attr("msDS-MinimumPasswordLength", "1.2.840.113556.1.4.2014", "2.5.5.9", singleValued: true),

        // ms-DS-NC-Repl-Cursors
        Attr("msDS-NCReplCursors", "1.2.840.113556.1.4.1704", "2.5.5.12", singleValued: false, systemOnly: true),

        // ms-DS-NC-Replica-Locations
        Attr("msDS-NC-Replica-Locations", "1.2.840.113556.1.4.1663", "2.5.5.1", singleValued: false),

        // ms-DS-NC-Repl-Inbound-Neighbors
        Attr("msDS-NCReplInboundNeighbors", "1.2.840.113556.1.4.1705", "2.5.5.12", singleValued: false, systemOnly: true),

        // ms-DS-NC-Repl-Outbound-Neighbors
        Attr("msDS-NCReplOutboundNeighbors", "1.2.840.113556.1.4.1706", "2.5.5.12", singleValued: false, systemOnly: true),

        // ms-DS-NC-RO-Replica-Locations
        Attr("msDS-NC-RO-Replica-Locations", "1.2.840.113556.1.4.1967", "2.5.5.1", singleValued: false),

        // ms-DS-NC-RO-Replica-Locations-BL
        Attr("msDS-NC-RO-Replica-Locations-BL", "1.2.840.113556.1.4.1968", "2.5.5.1", singleValued: false, systemOnly: true),

        // ms-DS-NC-Type
        Attr("msDS-NCType", "1.2.840.113556.1.4.2024", "2.5.5.9", singleValued: true, systemOnly: true),

        // ms-DS-Never-Reveal-Group
        Attr("msDS-NeverRevealGroup", "1.2.840.113556.1.4.1926", "2.5.5.1", singleValued: false),

        // ms-DS-Non-Members
        Attr("msDS-NonMembers", "1.2.840.113556.1.4.1793", "2.5.5.1", singleValued: false),

        // ms-DS-Non-Members-BL
        Attr("msDS-NonMembersBL", "1.2.840.113556.1.4.1794", "2.5.5.1", singleValued: false, systemOnly: true),

        // ms-DS-Non-Security-Group-Extra-Classes
        Attr("msDS-Non-Security-Group-Extra-Classes", "1.2.840.113556.1.4.2120", "2.5.5.12", singleValued: false),

        // ms-DS-Object-Reference
        Attr("msDS-ObjectReference", "1.2.840.113556.1.4.1840", "2.5.5.1", singleValued: false),

        // ms-DS-Object-Reference-BL
        Attr("msDS-ObjectReferenceBL", "1.2.840.113556.1.4.1841", "2.5.5.1", singleValued: false, systemOnly: true),

        // ms-DS-OIDToGroup-Link
        Attr("msDS-OIDToGroupLink", "1.2.840.113556.1.4.2048", "2.5.5.1", singleValued: true),

        // ms-DS-OIDToGroup-Link-BL
        Attr("msDS-OIDToGroupLinkBL", "1.2.840.113556.1.4.2049", "2.5.5.1", singleValued: false, systemOnly: true),

        // ms-DS-Operations-For-Az-Role
        Attr("msDS-OperationsForAzRole", "1.2.840.113556.1.4.1812", "2.5.5.1", singleValued: false),

        // ms-DS-Operations-For-Az-Role-BL
        Attr("msDS-OperationsForAzRoleBL", "1.2.840.113556.1.4.1813", "2.5.5.1", singleValued: false, systemOnly: true),

        // ms-DS-Operations-For-Az-Task
        Attr("msDS-OperationsForAzTask", "1.2.840.113556.1.4.1808", "2.5.5.1", singleValued: false),

        // ms-DS-Operations-For-Az-Task-BL
        Attr("msDS-OperationsForAzTaskBL", "1.2.840.113556.1.4.1809", "2.5.5.1", singleValued: false, systemOnly: true),

        // ms-DS-Optional-Feature-Flags
        Attr("msDS-OptionalFeatureFlags", "1.2.840.113556.1.4.2063", "2.5.5.9", singleValued: true),

        // ms-DS-Optional-Feature-GUID
        Attr("msDS-OptionalFeatureGUID", "1.2.840.113556.1.4.2064", "2.5.5.10", singleValued: true),

        // ms-DS-Other-Settings
        Attr("msDS-Other-Settings", "1.2.840.113556.1.4.1621", "2.5.5.12", singleValued: false),

        // ms-DS-Password-Complexity-Enabled
        Attr("msDS-PasswordComplexityEnabled", "1.2.840.113556.1.4.2017", "2.5.5.8", singleValued: true),

        // ms-DS-Password-History-Length
        Attr("msDS-PasswordHistoryLength", "1.2.840.113556.1.4.2015", "2.5.5.9", singleValued: true),

        // ms-DS-Password-Reversible-Encryption-Enabled
        Attr("msDS-PasswordReversibleEncryptionEnabled", "1.2.840.113556.1.4.2016", "2.5.5.8", singleValued: true),

        // ms-DS-Password-Settings-Precedence
        Attr("msDS-PasswordSettingsPrecedence", "1.2.840.113556.1.4.2021", "2.5.5.9", singleValued: true),

        // MS-DS-Per-User-Trust-Quota
        Attr("msDS-PerUserTrustQuota", "1.2.840.113556.1.4.1790", "2.5.5.9", singleValued: true),

        // MS-DS-Per-User-Trust-Tombstones-Quota
        Attr("msDS-PerUserTrustTombstonesQuota", "1.2.840.113556.1.4.1791", "2.5.5.9", singleValued: true),

        // ms-DS-Phonetic-Company-Name
        Attr("msDS-PhoneticCompanyName", "1.2.840.113556.1.4.1946", "2.5.5.12", singleValued: true, gc: true),

        // ms-DS-Phonetic-Department
        Attr("msDS-PhoneticDepartment", "1.2.840.113556.1.4.1945", "2.5.5.12", singleValued: true, gc: true),

        // ms-DS-Phonetic-Display-Name
        Attr("msDS-PhoneticDisplayName", "1.2.840.113556.1.4.1948", "2.5.5.12", singleValued: true, gc: true),

        // ms-DS-Phonetic-First-Name
        Attr("msDS-PhoneticFirstName", "1.2.840.113556.1.4.1943", "2.5.5.12", singleValued: true, gc: true),

        // ms-DS-Phonetic-Last-Name
        Attr("msDS-PhoneticLastName", "1.2.840.113556.1.4.1944", "2.5.5.12", singleValued: true, gc: true),

        // ms-DS-Port-LDAP
        Attr("msDS-PortLDAP", "1.2.840.113556.1.4.1860", "2.5.5.9", singleValued: true, systemOnly: true),

        // ms-DS-Port-SSL
        Attr("msDS-PortSSL", "1.2.840.113556.1.4.1861", "2.5.5.9", singleValued: true, systemOnly: true),

        // ms-DS-Preferred-GC-Site
        Attr("msDS-Preferred-GC-Site", "1.2.840.113556.1.4.1444", "2.5.5.1", singleValued: true),

        // ms-DS-Primary-Computer
        Attr("msDS-PrimaryComputer", "1.2.840.113556.1.4.2167", "2.5.5.1", singleValued: false),

        // ms-DS-Principal-Name
        Attr("msDS-PrincipalName", "1.2.840.113556.1.4.1865", "2.5.5.12", singleValued: true, systemOnly: true),

        // ms-DS-Promotion-Settings
        Attr("msDS-PromotionSettings", "1.2.840.113556.1.4.2065", "2.5.5.12", singleValued: true),

        // ms-DS-PSO-Applied
        Attr("msDS-PSOApplied", "1.2.840.113556.1.4.2022", "2.5.5.1", singleValued: false, systemOnly: true),

        // ms-DS-PSO-Applies-To
        Attr("msDS-PSOAppliesTo", "1.2.840.113556.1.4.2023", "2.5.5.1", singleValued: false),

        // ms-DS-Quota-Amount
        Attr("msDS-QuotaAmount", "1.2.840.113556.1.4.1846", "2.5.5.9", singleValued: true),

        // ms-DS-Quota-Effective
        Attr("msDS-QuotaEffective", "1.2.840.113556.1.4.1848", "2.5.5.9", singleValued: true, systemOnly: true),

        // ms-DS-Quota-Trustee
        Attr("msDS-QuotaTrustee", "1.2.840.113556.1.4.1847", "2.5.5.17", singleValued: true),

        // ms-DS-Quota-Used
        Attr("msDS-QuotaUsed", "1.2.840.113556.1.4.1849", "2.5.5.9", singleValued: true, systemOnly: true),

        // ms-DS-Registration-Quota
        Attr("msDS-RegistrationQuota", "1.2.840.113556.1.4.2030", "2.5.5.9", singleValued: true),

        // ms-DS-Repl-Attribute-Meta-Data
        Attr("msDS-ReplAttributeMetaData", "1.2.840.113556.1.4.1707", "2.5.5.12", singleValued: false, systemOnly: true),

        // ms-DS-Repl-Authentication-Mode
        Attr("msDS-ReplAuthenticationMode", "1.2.840.113556.1.4.1852", "2.5.5.9", singleValued: true),

        // MS-DS-Replicates-NC-Reason
        Attr("ms-DS-ReplicatesNCReason", "1.2.840.113556.1.4.1715", "2.5.5.7", singleValued: false, systemOnly: true),

        // ms-DS-ReplicationEpoch
        Attr("msDS-ReplicationEpoch", "1.2.840.113556.1.4.1720", "2.5.5.9", singleValued: true),

        // ms-DS-Replication-Notify-First-DSA-Delay
        Attr("msDS-Replication-Notify-First-DSA-Delay", "1.2.840.113556.1.4.1663", "2.5.5.9", singleValued: true),

        // ms-DS-Replication-Notify-Subsequent-DSA-Delay
        Attr("msDS-Replication-Notify-Subsequent-DSA-Delay", "1.2.840.113556.1.4.1664", "2.5.5.9", singleValued: true),

        // ms-DS-Repl-Value-Meta-Data
        Attr("msDS-ReplValueMetaData", "1.2.840.113556.1.4.1708", "2.5.5.12", singleValued: false, systemOnly: true),

        // ms-DS-Required-Domain-Behavior-Version
        Attr("msDS-RequiredDomainBehaviorVersion", "1.2.840.113556.1.4.2066", "2.5.5.9", singleValued: true),

        // ms-DS-Required-Forest-Behavior-Version
        Attr("msDS-RequiredForestBehaviorVersion", "1.2.840.113556.1.4.2068", "2.5.5.9", singleValued: true),

        // ms-DS-Resultant-PSO
        Attr("msDS-ResultantPSO", "1.2.840.113556.1.4.2025", "2.5.5.1", singleValued: true, systemOnly: true),

        // ms-DS-Retired-Repl-NC-Signatures
        Attr("msDS-RetiredReplNCSignatures", "1.2.840.113556.1.4.1826", "2.5.5.10", singleValued: true, systemOnly: true),

        // ms-DS-Revealed-DSAs
        Attr("msDS-RevealedDSAs", "1.2.840.113556.1.4.1924", "2.5.5.1", singleValued: false, systemOnly: true),

        // ms-DS-Revealed-List
        Attr("msDS-RevealedList", "1.2.840.113556.1.4.1940", "2.5.5.7", singleValued: false, systemOnly: true),

        // ms-DS-Revealed-List-BL
        Attr("msDS-RevealedListBL", "1.2.840.113556.1.4.1941", "2.5.5.7", singleValued: false, systemOnly: true),

        // ms-DS-Revealed-Users
        Attr("msDS-RevealedUsers", "1.2.840.113556.1.4.1928", "2.5.5.7", singleValued: false, systemOnly: true),

        // ms-DS-Reveal-OnDemand-Group
        Attr("msDS-RevealOnDemandGroup", "1.2.840.113556.1.4.1927", "2.5.5.1", singleValued: false),

        // ms-DS-RID-Pool-Allocation-Enabled
        Attr("msDS-RIDPoolAllocationEnabled", "1.2.840.113556.1.4.2216", "2.5.5.8", singleValued: true),

        // ms-ds-Schema-Extensions
        Attr("msDS-Schema-Extensions", "1.2.840.113556.1.4.1440", "2.5.5.10", singleValued: false, systemOnly: true),

        // ms-DS-SCP-Container
        Attr("msDS-SCPContainer", "1.2.840.113556.1.4.2055", "2.5.5.1", singleValued: false),

        // ms-DS-SD-Reference-Domain
        Attr("msDS-SDReferenceDomain", "1.2.840.113556.1.4.1711", "2.5.5.1", singleValued: true),

        // ms-DS-Secondary-KrbTgt-Number
        Attr("msDS-SecondaryKrbTgtNumber", "1.2.840.113556.1.4.1930", "2.5.5.9", singleValued: true, systemOnly: true),

        // ms-DS-Security-Group-Extra-Classes
        Attr("msDS-Security-Group-Extra-Classes", "1.2.840.113556.1.4.2119", "2.5.5.12", singleValued: false),

        // ms-DS-Seniority-Index
        Attr("msDS-SeniorityIndex", "1.2.840.113556.1.4.1998", "2.5.5.9", singleValued: true),

        // ms-DS-Service-Account
        Attr("msDS-ServiceAccount", "1.2.840.113556.1.4.2191", "2.5.5.1", singleValued: false),

        // ms-DS-Service-Account-BL
        Attr("msDS-ServiceAccountBL", "1.2.840.113556.1.4.2192", "2.5.5.1", singleValued: false, systemOnly: true),

        // ms-DS-Service-Account-DNS-Domain
        Attr("msDS-ServiceAccountDNSDomain", "1.2.840.113556.1.4.2193", "2.5.5.12", singleValued: true),

        // ms-DS-Settings
        Attr("msDS-Settings", "1.2.840.113556.1.4.2005", "2.5.5.12", singleValued: true),

        // ms-DS-Site-Affinity
        Attr("msDS-Site-Affinity", "1.2.840.113556.1.4.1443", "2.5.5.10", singleValued: false, systemOnly: true),

        // ms-DS-SiteName
        Attr("msDS-SiteName", "1.2.840.113556.1.4.1962", "2.5.5.12", singleValued: true, systemOnly: true),

        // ms-DS-Source-Object-DN
        Attr("msDS-SourceObjectDN", "1.2.840.113556.1.4.1879", "2.5.5.12", singleValued: true),

        // ms-DS-SPN-Suffixes
        Attr("msDS-SPNSuffixes", "1.2.840.113556.1.4.1713", "2.5.5.12", singleValued: false),

        // ms-DS-Strong-NTLM-Policy
        Attr("msDS-StrongNTLMPolicy", "1.2.840.113556.1.4.2174", "2.5.5.9", singleValued: true),

        // ms-DS-Supported-Encryption-Types
        Attr("msDS-SupportedEncryptionTypes", "1.2.840.113556.1.4.1963", "2.5.5.9", singleValued: true),

        // ms-DS-Tasks-For-Az-Role
        Attr("msDS-TasksForAzRole", "1.2.840.113556.1.4.1814", "2.5.5.1", singleValued: false),

        // ms-DS-Tasks-For-Az-Role-BL
        Attr("msDS-TasksForAzRoleBL", "1.2.840.113556.1.4.1815", "2.5.5.1", singleValued: false, systemOnly: true),

        // ms-DS-Tasks-For-Az-Task
        Attr("msDS-TasksForAzTask", "1.2.840.113556.1.4.1816", "2.5.5.1", singleValued: false),

        // ms-DS-Tasks-For-Az-Task-BL
        Attr("msDS-TasksForAzTaskBL", "1.2.840.113556.1.4.1817", "2.5.5.1", singleValued: false, systemOnly: true),

        // ms-DS-TDO-Egress-BL
        Attr("msDS-TDOEgressBL", "1.2.840.113556.1.4.2113", "2.5.5.1", singleValued: false, systemOnly: true),

        // ms-DS-TDO-Ingress-BL
        Attr("msDS-TDOIngressBL", "1.2.840.113556.1.4.2112", "2.5.5.1", singleValued: false, systemOnly: true),

        // ms-DS-Token-Group-Names
        Attr("msDS-TokenGroupNames", "1.2.840.113556.1.4.2051", "2.5.5.12", singleValued: false, systemOnly: true),

        // ms-DS-Token-Group-Names-Global-And-Universal
        Attr("msDS-TokenGroupNamesGlobalAndUniversal", "1.2.840.113556.1.4.2052", "2.5.5.12", singleValued: false, systemOnly: true),

        // ms-DS-Token-Group-Names-No-GC-Acceptable
        Attr("msDS-TokenGroupNamesNoGCAcceptable", "1.2.840.113556.1.4.2053", "2.5.5.12", singleValued: false, systemOnly: true),

        // ms-DS-Tombstone-Quota-Factor
        Attr("msDS-TombstoneQuotaFactor", "1.2.840.113556.1.4.1850", "2.5.5.9", singleValued: true),

        // ms-DS-Top-Quota-Usage
        Attr("msDS-TopQuotaUsage", "1.2.840.113556.1.4.1851", "2.5.5.12", singleValued: false, systemOnly: true),

        // ms-DS-Transformation-Rules
        Attr("msDS-TransformationRules", "1.2.840.113556.1.4.2109", "2.5.5.12", singleValued: true),

        // ms-DS-Transformation-Rules-Compiled
        Attr("msDS-TransformationRulesCompiled", "1.2.840.113556.1.4.2110", "2.5.5.10", singleValued: true),

        // ms-DS-Trust-Forest-Trust-Info
        Attr("msDS-TrustForestTrustInfo", "1.2.840.113556.1.4.1702", "2.5.5.10", singleValued: true),

        // ms-DS-UpdateScript
        Attr("msDS-UpdateScript", "1.2.840.113556.1.4.1853", "2.5.5.12", singleValued: true, systemOnly: true),

        // ms-DS-User-Account-Auto-Locked
        Attr("ms-DS-UserAccountAutoLocked", "1.2.840.113556.1.4.1856", "2.5.5.8", singleValued: true, systemOnly: true),

        // ms-DS-User-Account-Control-Computed
        Attr("msDS-User-Account-Control-Computed", "1.2.840.113556.1.4.2380", "2.5.5.9", singleValued: true, systemOnly: true),

        // ms-DS-User-Account-Disabled
        Attr("msDS-UserAccountDisabled", "1.2.840.113556.1.4.1854", "2.5.5.8", singleValued: true, systemOnly: true),

        // ms-DS-User-Dont-Expire-Password
        Attr("msDS-UserDontExpirePassword", "1.2.840.113556.1.4.1857", "2.5.5.8", singleValued: true, systemOnly: true),

        // ms-DS-User-Encrypted-Text-Password-Allowed
        Attr("ms-DS-UserEncryptedTextPasswordAllowed", "1.2.840.113556.1.4.1858", "2.5.5.8", singleValued: true, systemOnly: true),

        // ms-DS-User-Password-Expired
        Attr("msDS-UserPasswordExpired", "1.2.840.113556.1.4.1855", "2.5.5.8", singleValued: true, systemOnly: true),

        // ms-DS-User-Password-Expiry-Time-Computed
        Attr("msDS-UserPasswordExpiryTimeComputed", "1.2.840.113556.1.4.2281", "2.5.5.16", singleValued: true, systemOnly: true),

        // ms-DS-User-Password-Not-Required
        Attr("ms-DS-UserPasswordNotRequired", "1.2.840.113556.1.4.1859", "2.5.5.8", singleValued: true, systemOnly: true),

        // ms-DS-USN-Last-Sync-Success
        Attr("msDS-USNLastSyncSuccess", "1.2.840.113556.1.4.2056", "2.5.5.16", singleValued: true, systemOnly: true),

        // ms-DS-Value-Type-Reference
        Attr("msDS-ValueTypeReference", "1.2.840.113556.1.4.2096", "2.5.5.1", singleValued: true),

        // ms-DS-Value-Type-Reference-BL
        Attr("msDS-ValueTypeReferenceBL", "1.2.840.113556.1.4.2097", "2.5.5.1", singleValued: false, systemOnly: true),

        // ms-Exch-Assistant-Name
        Attr("msExchAssistantName", "1.2.840.113556.1.2.444", "2.5.5.12", singleValued: true, gc: true),

        // ms-Exch-House-Identifier
        Attr("msExchHouseIdentifier", "1.2.840.113556.1.2.596", "2.5.5.12", singleValued: true),

        // ms-Exch-LabeledURI
        Attr("msExchLabeledURI", "1.2.840.113556.1.2.593", "2.5.5.12", singleValued: false),

        // ms-Exch-Owner-BL (ownerBL)
        Attr("ownerBL", "1.2.840.113556.1.4.1946", "2.5.5.1", singleValued: false, systemOnly: true),

        // ms-FRS-Hub-Member
        Attr("msFRS-Hub-Member", "1.2.840.113556.1.4.1693", "2.5.5.1", singleValued: false),

        // ms-FRS-Topology-Pref
        Attr("msFRS-Topology-Pref", "1.2.840.113556.1.4.1694", "2.5.5.12", singleValued: true),

        // ms-FVE-KeyPackage
        Attr("msFVE-KeyPackage", "1.2.840.113556.1.4.1999", "2.5.5.10", singleValued: true),

        // ms-FVE-RecoveryGuid
        Attr("msFVE-RecoveryGuid", "1.2.840.113556.1.4.1964", "2.5.5.10", singleValued: true, indexed: true),

        // ms-FVE-RecoveryPassword
        Attr("msFVE-RecoveryPassword", "1.2.840.113556.1.4.1965", "2.5.5.12", singleValued: true),

        // ms-FVE-VolumeGuid
        Attr("msFVE-VolumeGuid", "1.2.840.113556.1.4.1998", "2.5.5.10", singleValued: true, indexed: true),

        // ms-ieee-80211-Data
        Attr("msieee80211-Data", "1.2.840.113556.1.4.1822", "2.5.5.10", singleValued: false),

        // ms-ieee-80211-Data-Type
        Attr("msieee80211-DataType", "1.2.840.113556.1.4.1823", "2.5.5.9", singleValued: true),

        // ms-ieee-80211-ID
        Attr("msieee80211-ID", "1.2.840.113556.1.4.1821", "2.5.5.12", singleValued: true),

        // Msi-File-List
        Attr("msiFileList", "1.2.840.113556.1.4.73", "2.5.5.12", singleValued: false),

        // ms-IIS-FTP-Dir
        Attr("msIIS-FTPDir", "1.2.840.113556.1.4.1786", "2.5.5.12", singleValued: true),

        // ms-IIS-FTP-Root
        Attr("msIIS-FTPRoot", "1.2.840.113556.1.4.1785", "2.5.5.12", singleValued: true),

        // ms-Imaging-Hash-Algorithm
        Attr("msImaging-HashAlgorithm", "1.2.840.113556.1.4.2180", "2.5.5.12", singleValued: true),

        // ms-Imaging-PSP-Identifier
        Attr("msImaging-PSPIdentifier", "1.2.840.113556.1.4.2178", "2.5.5.10", singleValued: true),

        // ms-Imaging-PSP-String
        Attr("msImaging-PSPString", "1.2.840.113556.1.4.2179", "2.5.5.12", singleValued: true),

        // ms-Imaging-Thumbprint-Hash
        Attr("msImaging-ThumbprintHash", "1.2.840.113556.1.4.2181", "2.5.5.10", singleValued: true),

        // Msi-Script
        Attr("msiScript", "1.2.840.113556.1.4.70", "2.5.5.10", singleValued: true),

        // Msi-Script-Name
        Attr("msiScriptName", "1.2.840.113556.1.4.845", "2.5.5.12", singleValued: true),

        // Msi-Script-Path
        Attr("msiScriptPath", "1.2.840.113556.1.4.69", "2.5.5.12", singleValued: true),

        // Msi-Script-Size
        Attr("msiScriptSize", "1.2.840.113556.1.4.846", "2.5.5.9", singleValued: true),

        // ms-Kds-CreateTime
        Attr("msKds-CreateTime", "1.2.840.113556.1.4.2176", "2.5.5.11", singleValued: true),

        // ms-Kds-DomainID
        Attr("msKds-DomainID", "1.2.840.113556.1.4.2177", "2.5.5.1", singleValued: true),

        // ms-Kds-KDF-AlgorithmID
        Attr("msKds-KDFAlgorithmID", "1.2.840.113556.1.4.2169", "2.5.5.12", singleValued: true),

        // ms-Kds-KDF-Param
        Attr("msKds-KDFParam", "1.2.840.113556.1.4.2170", "2.5.5.10", singleValued: true),

        // ms-Kds-PrivateKey-Length
        Attr("msKds-PrivateKeyLength", "1.2.840.113556.1.4.2173", "2.5.5.9", singleValued: true),

        // ms-Kds-PublicKey-Length
        Attr("msKds-PublicKeyLength", "1.2.840.113556.1.4.2174", "2.5.5.9", singleValued: true),

        // ms-Kds-RootKeyData
        Attr("msKds-RootKeyData", "1.2.840.113556.1.4.2175", "2.5.5.10", singleValued: true),

        // ms-Kds-SecretAgreement-AlgorithmID
        Attr("msKds-SecretAgreementAlgorithmID", "1.2.840.113556.1.4.2171", "2.5.5.12", singleValued: true),

        // ms-Kds-SecretAgreement-Param
        Attr("msKds-SecretAgreementParam", "1.2.840.113556.1.4.2172", "2.5.5.10", singleValued: true),

        // ms-Kds-UseStartTime
        Attr("msKds-UseStartTime", "1.2.840.113556.1.4.2194", "2.5.5.11", singleValued: true),

        // ms-Kds-Version
        Attr("msKds-Version", "1.2.840.113556.1.4.2195", "2.5.5.9", singleValued: true),

        // MSMQ-Authenticate
        Attr("mSMQAuthenticate", "1.2.840.113556.1.4.923", "2.5.5.8", singleValued: true),

        // MSMQ-Base-Priority
        Attr("mSMQBasePriority", "1.2.840.113556.1.4.918", "2.5.5.9", singleValued: true),

        // MSMQ-Computer-Type
        Attr("mSMQComputerType", "1.2.840.113556.1.4.933", "2.5.5.12", singleValued: true),

        // MSMQ-Computer-Type-Ex
        Attr("mSMQComputerTypeEx", "1.2.840.113556.1.4.1416", "2.5.5.12", singleValued: true),

        // MSMQ-Cost
        Attr("mSMQCost", "1.2.840.113556.1.4.937", "2.5.5.9", singleValued: true),

        // MSMQ-CSP-Name
        Attr("mSMQCSPName", "1.2.840.113556.1.4.941", "2.5.5.12", singleValued: true),

        // MSMQ-Dependent-Client-Service
        Attr("mSMQDependentClientService", "1.2.840.113556.1.4.1237", "2.5.5.8", singleValued: true),

        // MSMQ-Dependent-Client-Services
        Attr("mSMQDependentClientServices", "1.2.840.113556.1.4.1228", "2.5.5.8", singleValued: true),

        // MSMQ-Digests
        Attr("mSMQDigests", "1.2.840.113556.1.4.947", "2.5.5.10", singleValued: false),

        // MSMQ-Digests-Mig
        Attr("mSMQDigestsMig", "1.2.840.113556.1.4.966", "2.5.5.10", singleValued: false),

        // MSMQ-Ds-Service
        Attr("mSMQDsService", "1.2.840.113556.1.4.1230", "2.5.5.8", singleValued: true),

        // MSMQ-Ds-Services
        Attr("mSMQDsServices", "1.2.840.113556.1.4.1238", "2.5.5.8", singleValued: true),

        // MSMQ-Encrypt-Key
        Attr("mSMQEncryptKey", "1.2.840.113556.1.4.940", "2.5.5.10", singleValued: true),

        // MSMQ-Foreign
        Attr("mSMQForeign", "1.2.840.113556.1.4.934", "2.5.5.8", singleValued: true),

        // MSMQ-In-Routing-Servers
        Attr("mSMQInRoutingServers", "1.2.840.113556.1.4.929", "2.5.5.1", singleValued: false),

        // MSMQ-Interval1
        Attr("mSMQInterval1", "1.2.840.113556.1.4.956", "2.5.5.9", singleValued: true),

        // MSMQ-Interval2
        Attr("mSMQInterval2", "1.2.840.113556.1.4.957", "2.5.5.9", singleValued: true),

        // MSMQ-Journal
        Attr("mSMQJournal", "1.2.840.113556.1.4.919", "2.5.5.8", singleValued: true),

        // MSMQ-Journal-Quota
        Attr("mSMQJournalQuota", "1.2.840.113556.1.4.920", "2.5.5.9", singleValued: true),

        // MSMQ-Label
        Attr("mSMQLabel", "1.2.840.113556.1.4.917", "2.5.5.12", singleValued: true),

        // MSMQ-Label-Ex
        Attr("mSMQLabelEx", "1.2.840.113556.1.4.1412", "2.5.5.12", singleValued: true),

        // MSMQ-Long-Lived
        Attr("mSMQLongLived", "1.2.840.113556.1.4.942", "2.5.5.9", singleValued: true),

        // MSMQ-Migrated
        Attr("mSMQMigrated", "1.2.840.113556.1.4.951", "2.5.5.8", singleValued: true),

        // MSMQ-Multicast-Address
        Attr("mSMQ-MulticastAddress", "1.2.840.113556.1.4.1236", "2.5.5.12", singleValued: true),

        // MSMQ-Name-Style
        Attr("mSMQNameStyle", "1.2.840.113556.1.4.939", "2.5.5.8", singleValued: true),

        // MSMQ-Nt4-Flags
        Attr("mSMQNt4Flags", "1.2.840.113556.1.4.964", "2.5.5.9", singleValued: true),

        // MSMQ-Nt4-Stub
        Attr("mSMQNt4Stub", "1.2.840.113556.1.4.958", "2.5.5.9", singleValued: true),

        // MSMQ-OS-Type
        Attr("mSMQOSType", "1.2.840.113556.1.4.935", "2.5.5.9", singleValued: true),

        // MSMQ-Out-Routing-Servers
        Attr("mSMQOutRoutingServers", "1.2.840.113556.1.4.930", "2.5.5.1", singleValued: false),

        // MSMQ-Owner-ID
        Attr("mSMQOwnerID", "1.2.840.113556.1.4.925", "2.5.5.10", singleValued: true),

        // MSMQ-Prev-Site-Gates
        Attr("mSMQPrevSiteGates", "1.2.840.113556.1.4.1222", "2.5.5.1", singleValued: false),

        // MSMQ-Privacy-Level
        Attr("mSMQPrivacyLevel", "1.2.840.113556.1.4.922", "2.5.5.9", singleValued: true),

        // MSMQ-QM-ID
        Attr("mSMQQMID", "1.2.840.113556.1.4.944", "2.5.5.10", singleValued: true, indexed: true),

        // MSMQ-Queue-Journal-Quota
        Attr("mSMQQueueJournalQuota", "1.2.840.113556.1.4.961", "2.5.5.9", singleValued: true),

        // MSMQ-Queue-Name-Ext
        Attr("mSMQQueueNameExt", "1.2.840.113556.1.4.959", "2.5.5.12", singleValued: true),

        // MSMQ-Queue-Quota
        Attr("mSMQQueueQuota", "1.2.840.113556.1.4.960", "2.5.5.9", singleValued: true),

        // MSMQ-Queue-Type
        Attr("mSMQQueueType", "1.2.840.113556.1.4.916", "2.5.5.10", singleValued: true, indexed: true),

        // MSMQ-Quota
        Attr("mSMQQuota", "1.2.840.113556.1.4.921", "2.5.5.9", singleValued: true),

        // MSMQ-Recipient-FormatName
        Attr("mSMQ-Recipient-FormatName", "1.2.840.113556.1.4.1695", "2.5.5.12", singleValued: true),

        // MSMQ-Routing-Service
        Attr("mSMQRoutingService", "1.2.840.113556.1.4.1231", "2.5.5.8", singleValued: true),

        // MSMQ-Routing-Services
        Attr("mSMQRoutingServices", "1.2.840.113556.1.4.1239", "2.5.5.8", singleValued: true),

        // MSMQ-Secured-Source
        Attr("mSMQ-SecuredSource", "1.2.840.113556.1.4.1696", "2.5.5.8", singleValued: true),

        // MSMQ-Services
        Attr("mSMQServices", "1.2.840.113556.1.4.952", "2.5.5.9", singleValued: true),

        // MSMQ-Service-Type
        Attr("mSMQServiceType", "1.2.840.113556.1.4.943", "2.5.5.9", singleValued: true),

        // MSMQ-Sign-Certificates
        Attr("mSMQSignCertificates", "1.2.840.113556.1.4.945", "2.5.5.10", singleValued: true),

        // MSMQ-Sign-Certificates-Mig
        Attr("mSMQSignCertificatesMig", "1.2.840.113556.1.4.967", "2.5.5.10", singleValued: true),

        // MSMQ-Sign-Key
        Attr("mSMQSignKey", "1.2.840.113556.1.4.946", "2.5.5.10", singleValued: true),

        // MSMQ-Site-1
        Attr("mSMQSite1", "1.2.840.113556.1.4.949", "2.5.5.1", singleValued: true),

        // MSMQ-Site-2
        Attr("mSMQSite2", "1.2.840.113556.1.4.950", "2.5.5.1", singleValued: true),

        // MSMQ-Site-Foreign
        Attr("mSMQSiteForeign", "1.2.840.113556.1.4.1234", "2.5.5.8", singleValued: true),

        // MSMQ-Site-Gates
        Attr("mSMQSiteGates", "1.2.840.113556.1.4.948", "2.5.5.1", singleValued: false),

        // MSMQ-Site-Gates-Mig
        Attr("mSMQSiteGatesMig", "1.2.840.113556.1.4.965", "2.5.5.1", singleValued: false),

        // MSMQ-Site-ID
        Attr("mSMQSiteID", "1.2.840.113556.1.4.953", "2.5.5.10", singleValued: true),

        // MSMQ-Site-Name
        Attr("mSMQSiteName", "1.2.840.113556.1.4.955", "2.5.5.12", singleValued: true),

        // MSMQ-Site-Name-Ex
        Attr("mSMQSiteNameEx", "1.2.840.113556.1.4.1415", "2.5.5.12", singleValued: true),

        // MSMQ-Sites
        Attr("mSMQSites", "1.2.840.113556.1.4.936", "2.5.5.10", singleValued: false),

        // MSMQ-Transactional
        Attr("mSMQTransactional", "1.2.840.113556.1.4.926", "2.5.5.8", singleValued: true),

        // MSMQ-User-Sid
        Attr("mSMQUserSid", "1.2.840.113556.1.4.1233", "2.5.5.10", singleValued: true),

        // MSMQ-Version
        Attr("mSMQVersion", "1.2.840.113556.1.4.928", "2.5.5.9", singleValued: true),

        // ms-net-ieee-80211-GP-PolicyData
        Attr("ms-net-ieee-80211-GP-PolicyData", "1.2.840.113556.1.4.1951", "2.5.5.10", singleValued: true),

        // ms-net-ieee-80211-GP-PolicyGUID
        Attr("ms-net-ieee-80211-GP-PolicyGUID", "1.2.840.113556.1.4.1950", "2.5.5.12", singleValued: true),

        // ms-net-ieee-80211-GP-PolicyReserved
        Attr("ms-net-ieee-80211-GP-PolicyReserved", "1.2.840.113556.1.4.1952", "2.5.5.10", singleValued: false),

        // ms-net-ieee-8023-GP-PolicyData
        Attr("ms-net-ieee-8023-GP-PolicyData", "1.2.840.113556.1.4.1954", "2.5.5.10", singleValued: true),

        // ms-net-ieee-8023-GP-PolicyGUID
        Attr("ms-net-ieee-8023-GP-PolicyGUID", "1.2.840.113556.1.4.1953", "2.5.5.12", singleValued: true),

        // ms-net-ieee-8023-GP-PolicyReserved
        Attr("ms-net-ieee-8023-GP-PolicyReserved", "1.2.840.113556.1.4.1955", "2.5.5.10", singleValued: false),

        // msNPAllowDialin
        Attr("msNPAllowDialin", "1.2.840.113556.1.4.1119", "2.5.5.8", singleValued: true),

        // msNPCalledStationID
        Attr("msNPCalledStationID", "1.2.840.113556.1.4.1123", "2.5.5.12", singleValued: false),

        // msNPCallingStationID
        Attr("msNPCallingStationID", "1.2.840.113556.1.4.1124", "2.5.5.12", singleValued: false),

        // msNPSavedCallingStationID
        Attr("msNPSavedCallingStationID", "1.2.840.113556.1.4.1130", "2.5.5.12", singleValued: false),

        // ms-PKI-AccountCredentials
        Attr("msPKI-AccountCredentials", "1.2.840.113556.1.4.1894", "2.5.5.10", singleValued: false),

        // ms-PKI-Certificate-Application-Policy
        Attr("msPKI-Certificate-Application-Policy", "1.2.840.113556.1.4.1674", "2.5.5.12", singleValued: false),

        // ms-PKI-Certificate-Name-Flag
        Attr("msPKI-Certificate-Name-Flag", "1.2.840.113556.1.4.1432", "2.5.5.9", singleValued: true),

        // ms-PKI-Certificate-Policy
        Attr("msPKI-Certificate-Policy", "1.2.840.113556.1.4.1439", "2.5.5.12", singleValued: false),

        // ms-PKI-Cert-Template-OID
        Attr("msPKI-Cert-Template-OID", "1.2.840.113556.1.4.1436", "2.5.5.12", singleValued: true),

        // ms-PKI-Credential-Roaming-Tokens
        Attr("msPKI-CredentialRoamingTokens", "1.2.840.113556.1.4.1895", "2.5.5.10", singleValued: false),

        // ms-PKI-DPAPIMasterKeys
        Attr("msPKI-DPAPIMasterKeys", "1.2.840.113556.1.4.1893", "2.5.5.10", singleValued: false),

        // ms-PKI-Enrollment-Flag
        Attr("msPKI-Enrollment-Flag", "1.2.840.113556.1.4.1430", "2.5.5.9", singleValued: true),

        // ms-PKI-Enrollment-Servers
        Attr("msPKI-Enrollment-Servers", "1.2.840.113556.1.4.2007", "2.5.5.12", singleValued: false),

        // ms-PKI-Minimal-Key-Size
        Attr("msPKI-Minimal-Key-Size", "1.2.840.113556.1.4.1433", "2.5.5.9", singleValued: true),

        // ms-PKI-OID-Attribute
        Attr("msPKI-OID-Attribute", "1.2.840.113556.1.4.1674", "2.5.5.9", singleValued: true),

        // ms-PKI-OID-CPS
        Attr("msPKI-OID-CPS", "1.2.840.113556.1.4.1672", "2.5.5.12", singleValued: false),

        // ms-PKI-OID-LocalizedName
        Attr("msPKI-OIDLocalizedName", "1.2.840.113556.1.4.1673", "2.5.5.12", singleValued: false),

        // ms-PKI-OID-User-Notice
        Attr("msPKI-OID-User-Notice", "1.2.840.113556.1.4.1675", "2.5.5.12", singleValued: false),

        // ms-PKI-Private-Key-Flag
        Attr("msPKI-Private-Key-Flag", "1.2.840.113556.1.4.1431", "2.5.5.9", singleValued: true),

        // ms-PKI-RA-Application-Policies
        Attr("msPKI-RA-Application-Policies", "1.2.840.113556.1.4.1676", "2.5.5.12", singleValued: false),

        // ms-PKI-RA-Policies
        Attr("msPKI-RA-Policies", "1.2.840.113556.1.4.1438", "2.5.5.12", singleValued: false),

        // ms-PKI-RA-Signature
        Attr("msPKI-RA-Signature", "1.2.840.113556.1.4.1429", "2.5.5.9", singleValued: true),

        // ms-PKI-RoamingTimeStamp
        Attr("msPKI-RoamingTimeStamp", "1.2.840.113556.1.4.1892", "2.5.5.10", singleValued: true),

        // ms-PKI-Site-Name
        Attr("msPKI-Site-Name", "1.2.840.113556.1.4.2008", "2.5.5.12", singleValued: true),

        // ms-PKI-Supersede-Templates
        Attr("msPKI-Supersede-Templates", "1.2.840.113556.1.4.1437", "2.5.5.12", singleValued: false),

        // ms-PKI-Template-Minor-Revision
        Attr("msPKI-Template-Minor-Revision", "1.2.840.113556.1.4.1435", "2.5.5.9", singleValued: true),

        // ms-PKI-Template-Schema-Version
        Attr("msPKI-Template-Schema-Version", "1.2.840.113556.1.4.1434", "2.5.5.9", singleValued: true),

        // msRADIUSCallbackNumber
        Attr("msRADIUSCallbackNumber", "1.2.840.113556.1.4.1145", "2.5.5.12", singleValued: true),

        // ms-RADIUS-FramedInterfaceId
        Attr("msRADIUS-FramedInterfaceId", "1.2.840.113556.1.4.1913", "2.5.5.12", singleValued: true),

        // msRADIUSFramedIPAddress
        Attr("msRADIUSFramedIPAddress", "1.2.840.113556.1.4.1153", "2.5.5.9", singleValued: true),

        // ms-RADIUS-FramedIpv6Prefix
        Attr("msRADIUS-FramedIpv6Prefix", "1.2.840.113556.1.4.1914", "2.5.5.12", singleValued: false),

        // ms-RADIUS-FramedIpv6Route
        Attr("msRADIUS-FramedIpv6Route", "1.2.840.113556.1.4.1916", "2.5.5.12", singleValued: false),

        // msRADIUSFramedRoute
        Attr("msRADIUSFramedRoute", "1.2.840.113556.1.4.1158", "2.5.5.12", singleValued: false),

        // ms-RADIUS-SavedFramedInterfaceId
        Attr("msRADIUS-SavedFramedInterfaceId", "1.2.840.113556.1.4.1915", "2.5.5.12", singleValued: true),

        // ms-RADIUS-SavedFramedIpv6Prefix
        Attr("msRADIUS-SavedFramedIpv6Prefix", "1.2.840.113556.1.4.1917", "2.5.5.12", singleValued: false),

        // ms-RADIUS-SavedFramedIpv6Route
        Attr("msRADIUS-SavedFramedIpv6Route", "1.2.840.113556.1.4.1918", "2.5.5.12", singleValued: false),

        // msRADIUSServiceType
        Attr("msRADIUSServiceType", "1.2.840.113556.1.4.1171", "2.5.5.9", singleValued: true),

        // msRASSavedCallbackNumber
        Attr("msRASSavedCallbackNumber", "1.2.840.113556.1.4.1189", "2.5.5.12", singleValued: true),

        // msRASSavedFramedIPAddress
        Attr("msRASSavedFramedIPAddress", "1.2.840.113556.1.4.1190", "2.5.5.9", singleValued: true),

        // msRASSavedFramedRoute
        Attr("msRASSavedFramedRoute", "1.2.840.113556.1.4.1191", "2.5.5.12", singleValued: false),

        // ms-RRAS-Attribute
        Attr("msRRASAttribute", "1.2.840.113556.1.4.884", "2.5.5.12", singleValued: false),

        // ms-RRAS-Vendor-Attribute-Entry
        Attr("msRRASVendorAttributeEntry", "1.2.840.113556.1.4.883", "2.5.5.12", singleValued: false),

        // msSFU-30-Aliases
        Attr("msSFU30Aliases", "1.2.840.113556.1.6.18.1.339", "2.5.5.12", singleValued: false),

        // msSFU-30-Crypt-Method
        Attr("msSFU30CryptMethod", "1.2.840.113556.1.6.18.1.308", "2.5.5.5", singleValued: true),

        // msSFU-30-Domains
        Attr("msSFU30Domains", "1.2.840.113556.1.6.18.1.311", "2.5.5.12", singleValued: false),

        // msSFU-30-Field-Separator
        Attr("msSFU30FieldSeparator", "1.2.840.113556.1.6.18.1.305", "2.5.5.5", singleValued: true),

        // msSFU-30-Intra-Field-Separator
        Attr("msSFU30IntraFieldSeparator", "1.2.840.113556.1.6.18.1.306", "2.5.5.5", singleValued: true),

        // msSFU-30-Is-Valid-Container
        Attr("msSFU30IsValidContainer", "1.2.840.113556.1.6.18.1.300", "2.5.5.9", singleValued: true),

        // msSFU-30-Key-Attributes
        Attr("msSFU30KeyAttributes", "1.2.840.113556.1.6.18.1.304", "2.5.5.12", singleValued: true),

        // msSFU-30-Key-Values
        Attr("msSFU30KeyValues", "1.2.840.113556.1.6.18.1.340", "2.5.5.12", singleValued: false),

        // msSFU-30-Map-Filter
        Attr("msSFU30MapFilter", "1.2.840.113556.1.6.18.1.303", "2.5.5.12", singleValued: true),

        // msSFU-30-Master-Server-Name
        Attr("msSFU30MasterServerName", "1.2.840.113556.1.6.18.1.307", "2.5.5.12", singleValued: true),

        // msSFU-30-Max-Gid-Number
        Attr("msSFU30MaxGidNumber", "1.2.840.113556.1.6.18.1.310", "2.5.5.9", singleValued: true),

        // msSFU-30-Max-Uid-Number
        Attr("msSFU30MaxUidNumber", "1.2.840.113556.1.6.18.1.309", "2.5.5.9", singleValued: true),

        // msSFU-30-Name
        Attr("msSFU30Name", "1.2.840.113556.1.6.18.1.301", "2.5.5.12", singleValued: true, indexed: true),

        // msSFU-30-Netgroup-Host-At-Domain
        Attr("msSFU30NetgroupHostAtDomain", "1.2.840.113556.1.6.18.1.343", "2.5.5.12", singleValued: false),

        // msSFU-30-Netgroup-User-At-Domain
        Attr("msSFU30NetgroupUserAtDomain", "1.2.840.113556.1.6.18.1.344", "2.5.5.12", singleValued: false),

        // msSFU-30-Nis-Domain
        Attr("msSFU30NisDomain", "1.2.840.113556.1.6.18.1.341", "2.5.5.12", singleValued: true, indexed: true),

        // msSFU-30-NSMAP-Field-Position
        Attr("msSFU30NSMAPFieldPosition", "1.2.840.113556.1.6.18.1.302", "2.5.5.12", singleValued: true),

        // msSFU-30-Order-Number
        Attr("msSFU30OrderNumber", "1.2.840.113556.1.6.18.1.342", "2.5.5.12", singleValued: false),

        // msSFU-30-Posix-Member
        Attr("msSFU30PosixMember", "1.2.840.113556.1.6.18.1.345", "2.5.5.1", singleValued: false),

        // msSFU-30-Posix-Member-Of
        Attr("msSFU30PosixMemberOf", "1.2.840.113556.1.6.18.1.346", "2.5.5.1", singleValued: false, systemOnly: true),

        // msSFU-30-Result-Attributes
        Attr("msSFU30ResultAttributes", "1.2.840.113556.1.6.18.1.312", "2.5.5.12", singleValued: true),

        // msSFU-30-Search-Attributes
        Attr("msSFU30SearchAttributes", "1.2.840.113556.1.6.18.1.313", "2.5.5.12", singleValued: true),

        // msSFU-30-Search-Container
        Attr("msSFU30SearchContainer", "1.2.840.113556.1.6.18.1.314", "2.5.5.12", singleValued: true),

        // msSFU-30-Yp-Servers
        Attr("msSFU30YpServers", "1.2.840.113556.1.6.18.1.315", "2.5.5.12", singleValued: false),

        // ms-SPP-Config-License
        Attr("msSPP-ConfigLicense", "1.2.840.113556.1.4.2081", "2.5.5.10", singleValued: true),

        // ms-SPP-Confirmation-Id
        Attr("msSPP-ConfirmationId", "1.2.840.113556.1.4.2087", "2.5.5.12", singleValued: true),

        // ms-SPP-CSVLK-Partial-Product-Key
        Attr("msSPP-CSVLKPartialProductKey", "1.2.840.113556.1.4.2082", "2.5.5.12", singleValued: true),

        // ms-SPP-CSVLK-Pid
        Attr("msSPP-CSVLKPid", "1.2.840.113556.1.4.2083", "2.5.5.12", singleValued: true),

        // ms-SPP-CSVLK-Sku-Id
        Attr("msSPP-CSVLKSkuId", "1.2.840.113556.1.4.2084", "2.5.5.10", singleValued: true),

        // ms-SPP-Installation-Id
        Attr("msSPP-InstallationId", "1.2.840.113556.1.4.2085", "2.5.5.12", singleValued: true),

        // ms-SPP-Issuance-License
        Attr("msSPP-IssuanceLicense", "1.2.840.113556.1.4.2080", "2.5.5.10", singleValued: true),

        // ms-SPP-KMS-Ids
        Attr("msSPP-KMSIds", "1.2.840.113556.1.4.2079", "2.5.5.10", singleValued: false),

        // ms-SPP-Online-License
        Attr("msSPP-OnlineLicense", "1.2.840.113556.1.4.2086", "2.5.5.10", singleValued: true),

        // ms-SPP-Phone-License
        Attr("msSPP-PhoneLicense", "1.2.840.113556.1.4.2088", "2.5.5.10", singleValued: true),

        // MS-SQL-Alias
        Attr("mS-SQL-Alias", "1.2.840.113556.1.4.1391", "2.5.5.12", singleValued: true),

        // MS-SQL-AllowAnonymousSubscription
        Attr("mS-SQL-AllowAnonymousSubscription", "1.2.840.113556.1.4.1400", "2.5.5.8", singleValued: true),

        // MS-SQL-AllowImmediateUpdatingSubscription
        Attr("mS-SQL-AllowImmediateUpdatingSubscription", "1.2.840.113556.1.4.1404", "2.5.5.8", singleValued: true),

        // MS-SQL-AllowKnownPullSubscription
        Attr("mS-SQL-AllowKnownPullSubscription", "1.2.840.113556.1.4.1401", "2.5.5.8", singleValued: true),

        // MS-SQL-AllowQueuedUpdatingSubscription
        Attr("mS-SQL-AllowQueuedUpdatingSubscription", "1.2.840.113556.1.4.1405", "2.5.5.8", singleValued: true),

        // MS-SQL-AllowSnapshotFilesFTPDownloading
        Attr("mS-SQL-AllowSnapshotFilesFTPDownloading", "1.2.840.113556.1.4.1402", "2.5.5.8", singleValued: true),

        // MS-SQL-AppleTalk
        Attr("mS-SQL-AppleTalk", "1.2.840.113556.1.4.1379", "2.5.5.12", singleValued: true),

        // MS-SQL-Applications
        Attr("mS-SQL-Applications", "1.2.840.113556.1.4.1386", "2.5.5.12", singleValued: false),

        // MS-SQL-Build
        Attr("mS-SQL-Build", "1.2.840.113556.1.4.1369", "2.5.5.9", singleValued: true),

        // MS-SQL-CharacterSet
        Attr("mS-SQL-CharacterSet", "1.2.840.113556.1.4.1371", "2.5.5.9", singleValued: true),

        // MS-SQL-Clustered
        Attr("mS-SQL-Clustered", "1.2.840.113556.1.4.1373", "2.5.5.8", singleValued: true),

        // MS-SQL-ConnectionURL
        Attr("mS-SQL-ConnectionURL", "1.2.840.113556.1.4.1398", "2.5.5.12", singleValued: true),

        // MS-SQL-Contact
        Attr("mS-SQL-Contact", "1.2.840.113556.1.4.1367", "2.5.5.12", singleValued: true),

        // MS-SQL-CreationDate
        Attr("mS-SQL-CreationDate", "1.2.840.113556.1.4.1392", "2.5.5.12", singleValued: true),

        // MS-SQL-Database
        Attr("mS-SQL-Database", "1.2.840.113556.1.4.1382", "2.5.5.12", singleValued: true),

        // MS-SQL-Description
        Attr("mS-SQL-Description", "1.2.840.113556.1.4.1395", "2.5.5.12", singleValued: true),

        // MS-SQL-GPSHeight
        Attr("mS-SQL-GPSHeight", "1.2.840.113556.1.4.1389", "2.5.5.12", singleValued: true),

        // MS-SQL-GPSLatitude
        Attr("mS-SQL-GPSLatitude", "1.2.840.113556.1.4.1387", "2.5.5.12", singleValued: true),

        // MS-SQL-GPSLongitude
        Attr("mS-SQL-GPSLongitude", "1.2.840.113556.1.4.1388", "2.5.5.12", singleValued: true),

        // MS-SQL-InformationDirectory
        Attr("mS-SQL-InformationDirectory", "1.2.840.113556.1.4.1390", "2.5.5.8", singleValued: true),

        // MS-SQL-InformationURL
        Attr("mS-SQL-InformationURL", "1.2.840.113556.1.4.1383", "2.5.5.12", singleValued: true),

        // MS-SQL-Keywords
        Attr("mS-SQL-Keywords", "1.2.840.113556.1.4.1385", "2.5.5.12", singleValued: false),

        // MS-SQL-Language
        Attr("mS-SQL-Language", "1.2.840.113556.1.4.1375", "2.5.5.12", singleValued: true),

        // MS-SQL-LastBackupDate
        Attr("mS-SQL-LastBackupDate", "1.2.840.113556.1.4.1393", "2.5.5.12", singleValued: true),

        // MS-SQL-LastDiagnosticDate
        Attr("mS-SQL-LastDiagnosticDate", "1.2.840.113556.1.4.1394", "2.5.5.12", singleValued: true),

        // MS-SQL-LastUpdatedDate
        Attr("mS-SQL-LastUpdatedDate", "1.2.840.113556.1.4.1399", "2.5.5.12", singleValued: true),

        // MS-SQL-Location
        Attr("mS-SQL-Location", "1.2.840.113556.1.4.1368", "2.5.5.12", singleValued: true),

        // MS-SQL-Memory
        Attr("mS-SQL-Memory", "1.2.840.113556.1.4.1370", "2.5.5.16", singleValued: true),

        // MS-SQL-MultiProtocol
        Attr("mS-SQL-MultiProtocol", "1.2.840.113556.1.4.1378", "2.5.5.12", singleValued: true),

        // MS-SQL-Name
        Attr("mS-SQL-Name", "1.2.840.113556.1.4.1364", "2.5.5.12", singleValued: true, indexed: true),

        // MS-SQL-NamedPipe
        Attr("mS-SQL-NamedPipe", "1.2.840.113556.1.4.1374", "2.5.5.12", singleValued: true),

        // MS-SQL-PublicationURL
        Attr("mS-SQL-PublicationURL", "1.2.840.113556.1.4.1397", "2.5.5.12", singleValued: true),

        // MS-SQL-Publisher
        Attr("mS-SQL-Publisher", "1.2.840.113556.1.4.1396", "2.5.5.12", singleValued: true),

        // MS-SQL-RegisteredOwner
        Attr("mS-SQL-RegisteredOwner", "1.2.840.113556.1.4.1365", "2.5.5.12", singleValued: true),

        // MS-SQL-ServiceAccount
        Attr("mS-SQL-ServiceAccount", "1.2.840.113556.1.4.1366", "2.5.5.12", singleValued: true),

        // MS-SQL-Size
        Attr("mS-SQL-Size", "1.2.840.113556.1.4.1384", "2.5.5.16", singleValued: true),

        // MS-SQL-SortOrder
        Attr("mS-SQL-SortOrder", "1.2.840.113556.1.4.1372", "2.5.5.12", singleValued: true),

        // MS-SQL-SPX
        Attr("mS-SQL-SPX", "1.2.840.113556.1.4.1376", "2.5.5.12", singleValued: true),

        // MS-SQL-Status
        Attr("mS-SQL-Status", "1.2.840.113556.1.4.1381", "2.5.5.9", singleValued: true),

        // MS-SQL-TCPIP
        Attr("mS-SQL-TCPIP", "1.2.840.113556.1.4.1377", "2.5.5.12", singleValued: true),

        // MS-SQL-ThirdParty
        Attr("mS-SQL-ThirdParty", "1.2.840.113556.1.4.1403", "2.5.5.8", singleValued: true),

        // MS-SQL-Type
        Attr("mS-SQL-Type", "1.2.840.113556.1.4.1396", "2.5.5.12", singleValued: true),

        // MS-SQL-UnicodeSortOrder
        Attr("mS-SQL-UnicodeSortOrder", "1.2.840.113556.1.4.1380", "2.5.5.9", singleValued: true),

        // MS-SQL-Version
        Attr("mS-SQL-Version", "1.2.840.113556.1.4.1408", "2.5.5.12", singleValued: true),

        // MS-SQL-Vines
        Attr("mS-SQL-Vines", "1.2.840.113556.1.4.1407", "2.5.5.12", singleValued: true),

        // ms-TAPI-Conference-Blob
        Attr("msTAPI-ConferenceBlob", "1.2.840.113556.1.4.1700", "2.5.5.10", singleValued: true),

        // ms-TAPI-Ip-Address
        Attr("msTAPI-IpAddress", "1.2.840.113556.1.4.1701", "2.5.5.12", singleValued: true),

        // ms-TAPI-Protocol-Id
        Attr("msTAPI-ProtocolId", "1.2.840.113556.1.4.1699", "2.5.5.12", singleValued: true),

        // ms-TAPI-Unique-Identifier
        Attr("msTAPI-uid", "1.2.840.113556.1.4.1698", "2.5.5.12", singleValued: true),

        // ms-TPM-OwnerInformation
        Attr("msTPM-OwnerInformation", "1.2.840.113556.1.4.1966", "2.5.5.12", singleValued: true),

        // ms-TPM-Owner-Information-Temp
        Attr("msTPM-OwnerInformationTemp", "1.2.840.113556.1.4.2108", "2.5.5.12", singleValued: true),

        // ms-TPM-Srk-Pub-Thumbprint
        Attr("msTPM-SrkPubThumbprint", "1.2.840.113556.1.4.2171", "2.5.5.10", singleValued: true, indexed: true),

        // ms-TPM-Tpm-Information-For-Computer
        Attr("msTPM-TpmInformationForComputer", "1.2.840.113556.1.4.2109", "2.5.5.1", singleValued: true),

        // ms-TPM-Tpm-Information-For-Computer-BL
        Attr("msTPM-TpmInformationForComputerBL", "1.2.840.113556.1.4.2110", "2.5.5.1", singleValued: false, systemOnly: true),

        // ms-TS-Allow-Logon
        Attr("msTSAllowLogon", "1.2.840.113556.1.4.1979", "2.5.5.8", singleValued: true),

        // ms-TS-Broken-Connection-Action
        Attr("msTSBrokenConnectionAction", "1.2.840.113556.1.4.1982", "2.5.5.9", singleValued: true),

        // ms-TS-Connect-Client-Drives
        Attr("msTSConnectClientDrives", "1.2.840.113556.1.4.1983", "2.5.5.8", singleValued: true),

        // ms-TS-Connect-Printer-Drives
        Attr("msTSConnectPrinterDrives", "1.2.840.113556.1.4.1984", "2.5.5.8", singleValued: true),

        // ms-TS-Default-To-Main-Printer
        Attr("msTSDefaultToMainPrinter", "1.2.840.113556.1.4.1985", "2.5.5.8", singleValued: true),

        // ms-TS-Endpoint-Data
        Attr("msTSEndpointData", "1.2.840.113556.1.4.2069", "2.5.5.12", singleValued: true),

        // ms-TS-Endpoint-Plugin
        Attr("msTSEndpointPlugin", "1.2.840.113556.1.4.2072", "2.5.5.12", singleValued: true),

        // ms-TS-Endpoint-Type
        Attr("msTSEndpointType", "1.2.840.113556.1.4.2070", "2.5.5.9", singleValued: true),

        // MS-TS-ExpireDate
        Attr("msTSExpireDate", "1.2.840.113556.1.4.1993", "2.5.5.11", singleValued: true),

        // MS-TS-ExpireDate2
        Attr("msTSExpireDate2", "1.2.840.113556.1.4.2001", "2.5.5.11", singleValued: true),

        // MS-TS-ExpireDate3
        Attr("msTSExpireDate3", "1.2.840.113556.1.4.2009", "2.5.5.11", singleValued: true),

        // MS-TS-ExpireDate4
        Attr("msTSExpireDate4", "1.2.840.113556.1.4.2017", "2.5.5.11", singleValued: true),

        // ms-TS-Home-Directory
        Attr("msTSHomeDirectory", "1.2.840.113556.1.4.1977", "2.5.5.12", singleValued: true),

        // ms-TS-Home-Drive
        Attr("msTSHomeDrive", "1.2.840.113556.1.4.1978", "2.5.5.12", singleValued: true),

        // ms-TS-Initial-Program
        Attr("msTSInitialProgram", "1.2.840.113556.1.4.1980", "2.5.5.12", singleValued: true),

        // MS-TS-LicenseVersion
        Attr("msTSLicenseVersion", "1.2.840.113556.1.4.1994", "2.5.5.12", singleValued: true),

        // MS-TS-LicenseVersion2
        Attr("msTSLicenseVersion2", "1.2.840.113556.1.4.2002", "2.5.5.12", singleValued: true),

        // MS-TS-LicenseVersion3
        Attr("msTSLicenseVersion3", "1.2.840.113556.1.4.2010", "2.5.5.12", singleValued: true),

        // MS-TS-LicenseVersion4
        Attr("msTSLicenseVersion4", "1.2.840.113556.1.4.2018", "2.5.5.12", singleValued: true),

        // MS-TSLS-Property01
        Attr("msTSLSProperty01", "1.2.840.113556.1.4.2025", "2.5.5.12", singleValued: true),

        // MS-TSLS-Property02
        Attr("msTSLSProperty02", "1.2.840.113556.1.4.2026", "2.5.5.12", singleValued: true),

        // MS-TS-ManagingLS
        Attr("msTSManagingLS", "1.2.840.113556.1.4.1995", "2.5.5.12", singleValued: true),

        // MS-TS-ManagingLS2
        Attr("msTSManagingLS2", "1.2.840.113556.1.4.2003", "2.5.5.12", singleValued: true),

        // MS-TS-ManagingLS3
        Attr("msTSManagingLS3", "1.2.840.113556.1.4.2011", "2.5.5.12", singleValued: true),

        // MS-TS-ManagingLS4
        Attr("msTSManagingLS4", "1.2.840.113556.1.4.2019", "2.5.5.12", singleValued: true),

        // ms-TS-Max-Connection-Time
        Attr("msTSMaxConnectionTime", "1.2.840.113556.1.4.1986", "2.5.5.9", singleValued: true),

        // ms-TS-Max-Disconnection-Time
        Attr("msTSMaxDisconnectionTime", "1.2.840.113556.1.4.1987", "2.5.5.9", singleValued: true),

        // ms-TS-Max-Idle-Time
        Attr("msTSMaxIdleTime", "1.2.840.113556.1.4.1988", "2.5.5.9", singleValued: true),

        // ms-TS-Primary-Desktop
        Attr("msTSPrimaryDesktop", "1.2.840.113556.1.4.2073", "2.5.5.1", singleValued: true),

        // ms-TS-Primary-Desktop-BL
        Attr("msTSPrimaryDesktopBL", "1.2.840.113556.1.4.2075", "2.5.5.1", singleValued: false, systemOnly: true),

        // ms-TS-Profile-Path
        Attr("msTSProfilePath", "1.2.840.113556.1.4.1976", "2.5.5.12", singleValued: true),

        // MS-TS-Property01
        Attr("msTSProperty01", "1.2.840.113556.1.4.1991", "2.5.5.12", singleValued: true),

        // MS-TS-Property02
        Attr("msTSProperty02", "1.2.840.113556.1.4.1992", "2.5.5.12", singleValued: true),

        // ms-TS-Reconnection-Action
        Attr("msTSReconnectionAction", "1.2.840.113556.1.4.1981", "2.5.5.9", singleValued: true),

        // ms-TS-Remote-Control
        Attr("msTSRemoteControl", "1.2.840.113556.1.4.1989", "2.5.5.9", singleValued: true),

        // ms-TS-Secondary-Desktop-BL
        Attr("msTSSecondaryDesktopBL", "1.2.840.113556.1.4.2076", "2.5.5.1", singleValued: false, systemOnly: true),

        // ms-TS-Secondary-Desktops
        Attr("msTSSecondaryDesktops", "1.2.840.113556.1.4.2074", "2.5.5.1", singleValued: false),

        // ms-TS-Work-Directory
        Attr("msTSWorkDirectory", "1.2.840.113556.1.4.1990", "2.5.5.12", singleValued: true),

        // ms-WMI-Author
        Attr("msWMI-Author", "1.2.840.113556.1.4.1623", "2.5.5.12", singleValued: true),

        // ms-WMI-ChangeDate
        Attr("msWMI-ChangeDate", "1.2.840.113556.1.4.1624", "2.5.5.12", singleValued: true),

        // ms-WMI-Class
        Attr("msWMI-Class", "1.2.840.113556.1.4.1636", "2.5.5.12", singleValued: true),

        // ms-WMI-ClassDefinition
        Attr("msWMI-ClassDefinition", "1.2.840.113556.1.4.1653", "2.5.5.12", singleValued: true),

        // ms-WMI-CreationDate
        Attr("msWMI-CreationDate", "1.2.840.113556.1.4.1625", "2.5.5.12", singleValued: true),

        // ms-WMI-Genus
        Attr("msWMI-Genus", "1.2.840.113556.1.4.1637", "2.5.5.9", singleValued: true),

        // ms-WMI-ID
        Attr("msWMI-ID", "1.2.840.113556.1.4.1626", "2.5.5.12", singleValued: true, indexed: true),

        // ms-WMI-int8Default
        Attr("msWMI-Int8Default", "1.2.840.113556.1.4.1650", "2.5.5.16", singleValued: true),

        // ms-WMI-int8Max
        Attr("msWMI-Int8Max", "1.2.840.113556.1.4.1651", "2.5.5.16", singleValued: true),

        // ms-WMI-int8Min
        Attr("msWMI-Int8Min", "1.2.840.113556.1.4.1652", "2.5.5.16", singleValued: true),

        // ms-WMI-int8ValidValues
        Attr("msWMI-Int8ValidValues", "1.2.840.113556.1.4.1657", "2.5.5.16", singleValued: false),

        // ms-WMI-intDefault
        Attr("msWMI-IntDefault", "1.2.840.113556.1.4.1638", "2.5.5.9", singleValued: true),

        // ms-WMI-intFlags1
        Attr("msWMI-IntFlags1", "1.2.840.113556.1.4.1627", "2.5.5.9", singleValued: true),

        // ms-WMI-intFlags2
        Attr("msWMI-IntFlags2", "1.2.840.113556.1.4.1628", "2.5.5.9", singleValued: true),

        // ms-WMI-intFlags3
        Attr("msWMI-IntFlags3", "1.2.840.113556.1.4.1629", "2.5.5.9", singleValued: true),

        // ms-WMI-intFlags4
        Attr("msWMI-IntFlags4", "1.2.840.113556.1.4.1630", "2.5.5.9", singleValued: true),

        // ms-WMI-intMax
        Attr("msWMI-IntMax", "1.2.840.113556.1.4.1639", "2.5.5.9", singleValued: true),

        // ms-WMI-intMin
        Attr("msWMI-IntMin", "1.2.840.113556.1.4.1640", "2.5.5.9", singleValued: true),

        // ms-WMI-intValidValues
        Attr("msWMI-IntValidValues", "1.2.840.113556.1.4.1641", "2.5.5.9", singleValued: false),

        // ms-WMI-Mof
        Attr("msWMI-Mof", "1.2.840.113556.1.4.1631", "2.5.5.12", singleValued: true),

        // ms-WMI-Name
        Attr("msWMI-Name", "1.2.840.113556.1.4.1632", "2.5.5.12", singleValued: true, indexed: true),

        // ms-WMI-NormalizedClass
        Attr("msWMI-NormalizedClass", "1.2.840.113556.1.4.1633", "2.5.5.12", singleValued: false),

        // ms-WMI-Parm1
        Attr("msWMI-Parm1", "1.2.840.113556.1.4.1634", "2.5.5.12", singleValued: true),

        // ms-WMI-Parm2
        Attr("msWMI-Parm2", "1.2.840.113556.1.4.1635", "2.5.5.12", singleValued: true),

        // ms-WMI-Parm3
        Attr("msWMI-Parm3", "1.2.840.113556.1.4.1658", "2.5.5.12", singleValued: true),

        // ms-WMI-Parm4
        Attr("msWMI-Parm4", "1.2.840.113556.1.4.1659", "2.5.5.12", singleValued: true),

        // ms-WMI-PropertyName
        Attr("msWMI-PropertyName", "1.2.840.113556.1.4.1642", "2.5.5.12", singleValued: true),

        // ms-WMI-Query
        Attr("msWMI-Query", "1.2.840.113556.1.4.1643", "2.5.5.12", singleValued: true),

        // ms-WMI-QueryLanguage
        Attr("msWMI-QueryLanguage", "1.2.840.113556.1.4.1644", "2.5.5.12", singleValued: true),

        // ms-WMI-ScopeGuid
        Attr("msWMI-ScopeGuid", "1.2.840.113556.1.4.1660", "2.5.5.12", singleValued: true),

        // ms-WMI-SourceOrganization
        Attr("msWMI-SourceOrganization", "1.2.840.113556.1.4.1645", "2.5.5.12", singleValued: true),

        // ms-WMI-stringDefault
        Attr("msWMI-StringDefault", "1.2.840.113556.1.4.1646", "2.5.5.12", singleValued: true),

        // ms-WMI-stringValidValues
        Attr("msWMI-StringValidValues", "1.2.840.113556.1.4.1647", "2.5.5.12", singleValued: false),

        // ms-WMI-TargetClass
        Attr("msWMI-TargetClass", "1.2.840.113556.1.4.1648", "2.5.5.12", singleValued: true),

        // ms-WMI-TargetNameSpace
        Attr("msWMI-TargetNameSpace", "1.2.840.113556.1.4.1655", "2.5.5.12", singleValued: true),

        // ms-WMI-TargetObject
        Attr("msWMI-TargetObject", "1.2.840.113556.1.4.1649", "2.5.5.10", singleValued: true),

        // ms-WMI-TargetPath
        Attr("msWMI-TargetPath", "1.2.840.113556.1.4.1654", "2.5.5.12", singleValued: true),

        // ms-WMI-TargetType
        Attr("msWMI-TargetType", "1.2.840.113556.1.4.1656", "2.5.5.12", singleValued: true),

        // mustContain
        Attr("mustContain", "1.2.840.113556.1.2.24", "2.5.5.2", singleValued: false, systemOnly: true),

        // mXRecord
        Attr("mXRecord", "1.2.840.113556.1.4.29", "2.5.5.12", singleValued: false),
    ];

    private static AttributeSchemaEntry Attr(
        string name, string oid, string syntax,
        bool singleValued = true, bool gc = false, bool systemOnly = false,
        bool indexed = false, string description = null,
        int? rangeLower = null, int? rangeUpper = null)
        => new()
        {
            Name = name, LdapDisplayName = name, Oid = oid, Syntax = syntax,
            IsSingleValued = singleValued, IsInGlobalCatalog = gc,
            IsSystemOnly = systemOnly, IsIndexed = indexed,
            Description = description, RangeLower = rangeLower, RangeUpper = rangeUpper,
        };
}
