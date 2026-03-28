import { get, post, put, del } from './client'

// ---------------------------------------------------------------------------
// Typed interfaces
// ---------------------------------------------------------------------------

export interface OuInfo {
  guid?: string
  name: string
  dn: string
  description?: string
  parentDn?: string
  whenCreated?: string
  whenChanged?: string
}

export interface DeletedObject {
  guid?: string
  name?: string
  dn: string
  objectClass?: string
  whenDeleted?: string
}

export interface RestoreResult {
  success: boolean
  newDn?: string
  error?: string
}

export interface Site {
  name: string
  dn: string
  description?: string
  location?: string
  siteLinks?: string[]
  servers?: string[]
}

export interface SiteServer {
  name: string
  dn: string
  siteName?: string
  isGc?: boolean
  isDc?: boolean
  dnsHostName?: string
}

export interface Subnet {
  id: string
  subnetAddress: string
  siteDn?: string
  siteName?: string
  description?: string
  location?: string
}

export interface SiteLink {
  name: string
  dn: string
  sites?: string[]
  cost?: number
  replInterval?: number
  description?: string
  schedule?: string
  transportType?: string
}

export interface SiteLinkBridge {
  name: string
  dn: string
  siteLinks?: string[]
}

export interface ReplicationConnection {
  name: string
  dn: string
  fromServer?: string
  toServer?: string
  transportType?: string
  schedule?: string
  options?: number
}

export interface ReplicationStatus {
  partners?: ReplicationPartner[]
  lastSyncTime?: string
  failureCount?: number
}

export interface ReplicationPartner {
  partnerName: string
  partnerDns?: string
  lastAttemptTime?: string
  lastSuccessTime?: string
  consecutiveFailures?: number
  lastError?: string
  isEnabled?: boolean
  transportType?: string
}

export interface Trust {
  id?: string
  trustPartner: string
  flatName?: string
  trustDirection?: number
  trustType?: number
  trustAttributes?: number
  securityIdentifier?: string
  created?: string
  modified?: string
}

export interface HealthStatus {
  status: string
  checks?: HealthCheck[]
  version?: string
}

export interface HealthCheck {
  name: string
  status: string
  description?: string
  duration?: number
}

export interface PasswordPolicyObject {
  id?: string
  name: string
  description?: string
  precedence?: number
  minPasswordLength?: number
  passwordHistoryLength?: number
  complexityEnabled?: boolean
  reversibleEncryptionEnabled?: boolean
  minPasswordAgeDays?: number
  maxPasswordAgeDays?: number
  lockoutThreshold?: number
  lockoutDurationMinutes?: number
  lockoutObservationWindowMinutes?: number
  appliesTo?: string[]
}

export interface EffectivePasswordPolicy extends PasswordPolicyObject {
  source?: string
}

// ---------------------------------------------------------------------------
// OU management
// ---------------------------------------------------------------------------

export function listOUs(parentDn?: string) {
  const params = parentDn ? `?parentDn=${encodeURIComponent(parentDn)}` : ''
  return get<OuInfo[]>(`/ous${params}`)
}

export function createOU(name: string, parentDn: string, description?: string) {
  return post<OuInfo>('/ous', { name, parentDn, description })
}

export function deleteOU(guid: string, recursive?: boolean) {
  return del(`/ous/${guid}?recursive=${recursive ?? false}`)
}

// ---------------------------------------------------------------------------
// Recycle Bin
// ---------------------------------------------------------------------------

export function listDeletedObjects(limit?: number) {
  const params = limit ? `?limit=${limit}` : ''
  return get<DeletedObject[]>(`/recyclebin${params}`)
}

export function restoreObject(guid: string, newParentDn?: string) {
  return post<RestoreResult>(`/recyclebin/${guid}/restore`, { newParentDn })
}

export function purgeObject(guid: string) {
  return del(`/recyclebin/${guid}`)
}

// ---------------------------------------------------------------------------
// Sites
// ---------------------------------------------------------------------------

export function listSites() {
  return get<Site[]>('/sites')
}

export function listSiteServers(siteName: string) {
  return get<SiteServer[]>(`/sites/${encodeURIComponent(siteName)}/servers`)
}

export function listSiteSubnets(siteName: string) {
  return get<Subnet[]>(`/sites/${encodeURIComponent(siteName)}/subnets`)
}

export function createSite(data: { name: string; description?: string }) {
  return post<Site>('/sites', data)
}

export function updateSite(name: string, data: { description?: string }) {
  return put<Site>(`/sites/${encodeURIComponent(name)}`, data)
}

export function deleteSite(name: string) {
  return del(`/sites/${encodeURIComponent(name)}`)
}

export function listAllSubnets() {
  return get<Subnet[]>('/sites/all-subnets')
}

export function createSubnet(data: { subnetAddress: string; siteDn?: string; description?: string; location?: string }) {
  return post<Subnet>('/sites/subnets', data)
}

export function updateSubnet(id: string, data: { siteDn?: string; description?: string; location?: string }) {
  return put<Subnet>(`/sites/subnets/${id}`, data)
}

export function deleteSubnet(id: string) {
  return del(`/sites/subnets/${id}`)
}

export function listSiteLinks() {
  return get<SiteLink[]>('/sites/site-links')
}

export function createSiteLink(data: { name: string; sites: string[]; cost?: number; replInterval?: number; description?: string; schedule?: string }) {
  return post<SiteLink>('/sites/site-links', data)
}

export function updateSiteLink(name: string, data: { sites?: string[]; cost?: number; replInterval?: number; description?: string; schedule?: string }) {
  return put<SiteLink>(`/sites/site-links/${encodeURIComponent(name)}`, data)
}

export function deleteSiteLink(name: string) {
  return del(`/sites/site-links/${encodeURIComponent(name)}`)
}

export function listSiteLinkBridges() {
  return get<SiteLinkBridge[]>('/sites/site-link-bridges')
}

export function createSiteLinkBridge(data: { name: string; siteLinks?: string[] }) {
  return post<SiteLinkBridge>('/sites/site-link-bridges', data)
}

export function deleteSiteLinkBridge(name: string) {
  return del(`/sites/site-link-bridges/${encodeURIComponent(name)}`)
}

export function listReplicationConnections(siteName: string, serverName: string) {
  return get<ReplicationConnection[]>(`/sites/${encodeURIComponent(siteName)}/servers/${encodeURIComponent(serverName)}/connections`)
}

export function createReplicationConnection(siteName: string, serverName: string, data: { fromServer: string; name?: string; transportType?: string; schedule?: string }) {
  return post<ReplicationConnection>(`/sites/${encodeURIComponent(siteName)}/servers/${encodeURIComponent(serverName)}/connections`, data)
}

export function deleteReplicationConnection(siteName: string, serverName: string, connName: string) {
  return del(`/sites/${encodeURIComponent(siteName)}/servers/${encodeURIComponent(serverName)}/connections/${encodeURIComponent(connName)}`)
}

export function triggerKcc(siteName: string, serverName: string) {
  return post<{ success: boolean; message?: string }>(`/sites/${encodeURIComponent(siteName)}/servers/${encodeURIComponent(serverName)}/kcc`)
}

export function moveServer(siteName: string, serverName: string, targetSite: string) {
  return post<{ success: boolean }>(`/sites/${encodeURIComponent(siteName)}/servers/${encodeURIComponent(serverName)}/move`, { targetSite })
}

export function listTransports() {
  return get<{ name: string; dn: string }[]>('/sites/transports')
}

// ---------------------------------------------------------------------------
// Replication
// ---------------------------------------------------------------------------

export function getReplicationStatus() {
  return get<ReplicationStatus>('/replication/status')
}

export function getFsmoRoles() {
  return get<Record<string, string>>('/replication/fsmo')
}

// ---------------------------------------------------------------------------
// Trusts
// ---------------------------------------------------------------------------

export function listTrusts() {
  return get<Trust[]>('/trusts')
}

export function createTrust(data: {
  trustPartner: string
  flatName?: string
  trustDirection?: number
  trustType?: number
  trustAttributes?: number
  securityIdentifier?: string
  sharedSecret?: string
}) {
  return post<Trust>('/trusts', data)
}

export function updateTrust(id: string, data: {
  flatName?: string
  trustDirection?: number
  trustType?: number
  trustAttributes?: number
  securityIdentifier?: string
  sharedSecret?: string
}) {
  return put<Trust>(`/trusts/${id}`, data)
}

export function deleteTrust(id: string) {
  return del(`/trusts/${id}`)
}

export function verifyTrust(id: string) {
  return post<{ verified: boolean; error?: string }>(`/trusts/${id}/verify`)
}

// ---------------------------------------------------------------------------
// Health
// ---------------------------------------------------------------------------

export function getHealth() {
  return get<HealthStatus>('/health')
}

// ---------------------------------------------------------------------------
// Fine-Grained Password Policies (PSOs)
// ---------------------------------------------------------------------------

export function listPasswordPolicies() {
  return get<PasswordPolicyObject[]>('/password-policies')
}

export function createPasswordPolicy(data: {
  name: string
  description?: string
  precedence?: number
  minPasswordLength?: number
  passwordHistoryLength?: number
  complexityEnabled?: boolean
  reversibleEncryptionEnabled?: boolean
  minPasswordAgeDays?: number
  maxPasswordAgeDays?: number
  lockoutThreshold?: number
  lockoutDurationMinutes?: number
  lockoutObservationWindowMinutes?: number
}) {
  return post<PasswordPolicyObject>('/password-policies', data)
}

export function updatePasswordPolicy(id: string, data: {
  name?: string
  description?: string
  precedence?: number
  minPasswordLength?: number
  passwordHistoryLength?: number
  complexityEnabled?: boolean
  reversibleEncryptionEnabled?: boolean
  minPasswordAgeDays?: number
  maxPasswordAgeDays?: number
  lockoutThreshold?: number
  lockoutDurationMinutes?: number
  lockoutObservationWindowMinutes?: number
}) {
  return put<PasswordPolicyObject>(`/password-policies/${id}`, data)
}

export function deletePasswordPolicy(id: string) {
  return del(`/password-policies/${id}`)
}

export function applyPasswordPolicy(id: string, targetDn: string) {
  return post<{ success: boolean }>(`/password-policies/${id}/apply`, { targetDn })
}

export function removePasswordPolicyLink(id: string, dn: string) {
  return del(`/password-policies/${id}/apply/${encodeURIComponent(dn)}`)
}

export function getEffectivePasswordPolicy(userDn: string) {
  return get<EffectivePasswordPolicy>(`/password-policies/effective/${encodeURIComponent(userDn)}`)
}
