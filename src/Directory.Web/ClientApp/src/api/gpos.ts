import { get, post, put, del } from './client'

export interface GpoSummary {
  dn: string
  objectGuid: string
  displayName: string
  cn: string
  flags: number
  isUserEnabled: boolean
  isComputerEnabled: boolean
  versionNumber: number
  linkCount: number
  whenCreated: string
  whenChanged: string
}

export interface GpoLinkInfo {
  targetDn: string
  targetName: string
  isEnforced: boolean
  isDisabled: boolean
}

export interface PasswordPolicySettings {
  minimumLength?: number | null
  complexityEnabled?: boolean | null
  historyCount?: number | null
  maxAgeDays?: number | null
  minAgeDays?: number | null
  reversibleEncryption?: boolean | null
}

export interface AccountLockoutSettings {
  threshold?: number | null
  durationMinutes?: number | null
  observationWindowMinutes?: number | null
}

export interface AuditPolicySettings {
  auditLogonEvents?: number | null
  auditObjectAccess?: number | null
  auditPrivilegeUse?: number | null
  auditPolicyChange?: number | null
  auditAccountManagement?: number | null
  auditProcessTracking?: number | null
  auditDsAccess?: number | null
  auditAccountLogon?: number | null
  auditSystemEvents?: number | null
}

export interface UserRightsSettings {
  allowLogOnLocally?: string[] | null
  denyLogOnLocally?: string[] | null
  allowRemoteDesktop?: string[] | null
  denyRemoteDesktop?: string[] | null
  backupFilesAndDirectories?: string[] | null
  restoreFilesAndDirectories?: string[] | null
  shutdownSystem?: string[] | null
  changeSystemTime?: string[] | null
}

export interface SecurityOptionSettings {
  lanManagerAuthLevel?: number | null
  requireSmbSigning?: boolean | null
  ldapClientSigningRequirement?: number | null
  ldapServerSigningRequirement?: number | null
  minimumSessionSecurity?: number | null
  renameAdministratorAccount?: string | null
  renameGuestAccount?: string | null
  enableGuestAccount?: boolean | null
}

export interface SoftwareRule {
  type: string
  value: string
  securityLevel: number
  description?: string | null
}

export interface SoftwareRestrictionSettings {
  defaultLevel?: number | null
  enforcementScope?: number | null
  rules?: SoftwareRule[] | null
}

export interface ScriptReference {
  path: string
  parameters?: string | null
  order: number
}

export interface DriveMapping {
  driveLetter: string
  uncPath: string
  label?: string | null
  action: string
  reconnect: boolean
}

export interface GpoPolicySettings {
  passwordPolicy?: PasswordPolicySettings | null
  accountLockout?: AccountLockoutSettings | null
  auditPolicy?: AuditPolicySettings | null
  userRights?: UserRightsSettings | null
  securityOptions?: SecurityOptionSettings | null
  softwareRestriction?: SoftwareRestrictionSettings | null
  logonScripts?: ScriptReference[] | null
  logoffScripts?: ScriptReference[] | null
  startupScripts?: ScriptReference[] | null
  shutdownScripts?: ScriptReference[] | null
  driveMappings?: DriveMapping[] | null
}

export interface GpoDetail extends GpoSummary {
  userVersion: number
  computerVersion: number
  gpcFileSysPath: string
  gpcMachineExtensionNames: string
  gpcUserExtensionNames: string
  links: GpoLinkInfo[]
  securityFiltering: string[]
  policySettings: GpoPolicySettings
}

export interface RsopGpoEntry {
  gpoDn: string
  displayName: string
  sourceContainerDn: string
  isEnforced: boolean
  linkOrder: number
  settings: GpoPolicySettings
}

export interface RsopResult {
  userGpos: RsopGpoEntry[]
  computerGpos: RsopGpoEntry[]
  userPolicy: GpoPolicySettings
  computerPolicy: GpoPolicySettings
  mergedPolicy: GpoPolicySettings
}

// API functions
export const listGpos = () => get<GpoSummary[]>('/gpos')

export const getGpo = (id: string) => get<GpoDetail>(`/gpos/${id}`)

export const createGpo = (displayName: string, policySettings?: GpoPolicySettings) =>
  post<GpoDetail>('/gpos', { displayName, policySettings })

export const updateGpo = (id: string, body: { displayName?: string; flags?: number; policySettings?: GpoPolicySettings }) =>
  put<GpoDetail>(`/gpos/${id}`, body)

export const deleteGpo = (id: string) => del(`/gpos/${id}`)

export const linkGpo = (id: string, targetDn: string, enforced = false) =>
  post<void>(`/gpos/${id}/link`, { targetDn, enforced })

export const unlinkGpo = (id: string, targetDn: string) =>
  del(`/gpos/${id}/link/${encodeURIComponent(targetDn)}`)

export function getRsop(userDn?: string, computerDn?: string) {
  const params = new URLSearchParams()
  if (userDn) params.set('user', userDn)
  if (computerDn) params.set('computer', computerDn)
  return get<RsopResult>(`/gpos/rsop?${params.toString()}`)
}

// GPO Settings
export const getGpoSettings = (id: string) => get<GpoPolicySettings>(`/gpos/${id}/settings`)

export const updateGpoSettings = (id: string, settings: GpoPolicySettings) =>
  put<GpoPolicySettings>(`/gpos/${id}/settings`, settings)

export const getGpoComputerSettings = (id: string) => get<Partial<GpoPolicySettings>>(`/gpos/${id}/settings/computer`)

export const getGpoUserSettings = (id: string) => get<Partial<GpoPolicySettings>>(`/gpos/${id}/settings/user`)

// Security Filtering
export interface SecurityFilterEntry {
  dn: string
  name: string
  objectSid: string
  objectClass: string
}

export const getSecurityFiltering = (id: string) => get<SecurityFilterEntry[]>(`/gpos/${id}/security-filtering`)

export const addSecurityFilter = (id: string, principalDn: string) =>
  post<void>(`/gpos/${id}/security-filtering`, { principalDn })

export const removeSecurityFilter = (id: string, sid: string) =>
  del(`/gpos/${id}/security-filtering/${encodeURIComponent(sid)}`)

// Backup / Restore
export interface GpoBackup {
  backupId: string
  gpoGuid: string
  gpoDisplayName: string
  description: string
  createdAt: string
}

export const createGpoBackup = (id: string, description?: string) =>
  post<GpoBackup>(`/gpos/${id}/backup`, { description })

export const listGpoBackups = () => get<GpoBackup[]>('/gpos/backups')

export const restoreGpoBackup = (backupId: string) => post<GpoDetail>(`/gpos/backups/${backupId}/restore`)

// WMI Filters
export interface WmiFilter {
  id: string
  name: string
  description: string
  query: string
  createdAt: string
  modifiedAt: string
}

export const listWmiFilters = () => get<WmiFilter[]>('/gpos/wmi-filters')

export const createWmiFilter = (name: string, description: string, query: string) =>
  post<WmiFilter>('/gpos/wmi-filters', { name, description, query })

export const updateWmiFilter = (id: string, body: { name?: string; description?: string; query?: string }) =>
  put<WmiFilter>(`/gpos/wmi-filters/${id}`, body)

export const deleteWmiFilter = (id: string) => del(`/gpos/wmi-filters/${id}`)

export const setGpoWmiFilter = (gpoId: string, filterId: string | null) =>
  put<void>(`/gpos/${gpoId}/wmi-filter`, { filterId })
