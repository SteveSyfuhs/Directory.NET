export interface TreeNode {
  dn: string
  name: string
  objectClass: string
  objectGuid?: string
  hasChildren: boolean
  icon: string
}

export interface ObjectSummary {
  dn: string
  objectGuid?: string
  name?: string
  objectClass: string
  description?: string
  samAccountName?: string
  enabled?: boolean
  whenCreated?: string
  whenChanged?: string
}

export interface ObjectDetail {
  dn: string
  objectGuid?: string
  objectSid?: string
  objectClass: string[]
  cn?: string
  displayName?: string
  description?: string
  samAccountName?: string
  userPrincipalName?: string
  userAccountControl: number
  givenName?: string
  sn?: string
  mail?: string
  title?: string
  department?: string
  company?: string
  manager?: string
  dnsHostName?: string
  operatingSystem?: string
  operatingSystemVersion?: string
  operatingSystemServicePack?: string
  memberOf: string[]
  member: string[]
  servicePrincipalNames: string[]
  msDsAllowedToDelegateTo: string[]
  thumbnailPhoto?: string
  primaryGroupId: number
  pwdLastSet: number
  lastLogon: number
  accountExpires?: number
  badPwdCount: number
  groupType: number
  whenCreated?: string
  whenChanged?: string
  attributes: Record<string, string[]>
  eTag?: string
}

export interface DashboardSummary {
  userCount: number
  computerCount: number
  groupCount: number
  ouCount: number
  totalObjects: number
  domainName: string
  domainDn: string
  domainSid: string
  functionalLevel: number
}

export interface DcHealth {
  hostname: string
  siteName: string
  serverDn: string
  lastHeartbeat?: string
  isHealthy: boolean
}

export interface SearchResult {
  items: ObjectSummary[]
  totalCount: number
  continuationToken?: string
}

export interface PaginatedResponse<T> {
  items: T[]
  continuationToken: string | null
  totalCount: number
  pageSize: number
  hasMore: boolean
}

export interface PasswordPolicy {
  minPwdLength: number
  pwdHistoryLength: number
  maxPwdAge: number
  minPwdAge: number
  pwdProperties: number
  lockoutThreshold: number
  lockoutDuration: number
  lockoutObservationWindow: number
}

export interface SchemaAttribute {
  name: string
  oid: string
  syntax: string
  isSingleValued: boolean
  isIndexed: boolean
  isInGlobalCatalog: boolean
  rangeLower?: number
  rangeUpper?: number
  isSystemOnly: boolean
}

export interface SchemaClass {
  name: string
  oid: string
  superiorClass?: string
  classType: string
  mustContain: string[]
  mayContain: string[]
  auxiliaryClasses: string[]
  possibleSuperiors: string[]
}

export interface ResolveResult {
  dn: string
  displayName?: string
  objectClass: string
  thumbnailPhoto?: string
}

export interface DomainConfig {
  domainName: string
  netBiosName: string
  domainDn: string
  domainSid: string
  forestName: string
  kerberosRealm: string
  functionalLevel: number
}

// Security/ACL types
export interface SecurityDescriptorInfo {
  owner: string
  ownerName?: string
  group: string
  groupName?: string
  dacl: AceInfo[]
  sacl: AceInfo[]
  control: number
}

export interface AceInfo {
  type: string
  principal: string
  principalName?: string
  accessMask: string
  permissions: string[]
  objectType?: string
  objectTypeName?: string
  inheritedObjectType?: string
  inheritedObjectTypeName?: string
  flags: string[]
  isInherited: boolean
}

export interface EffectivePermissions {
  permissions: string[]
}

// Advanced Search types
export interface AdvancedSearchResult {
  items: AdvancedSearchResultItem[]
  totalCount: number
}

export interface AdvancedSearchResultItem {
  dn: string
  objectGuid?: string
  objectClass: string
  name?: string
  attributes: Record<string, string[]>
}

export interface SavedSearch {
  id: string
  name: string
  baseDn: string
  scope: string
  filter: string
  attributes?: string[]
  createdAt?: string
}

// Bulk operations types
export interface BulkResponse {
  results: BulkOperationResult[]
}

export interface BulkOperationResult {
  dn: string
  success: boolean
  error?: string
}
