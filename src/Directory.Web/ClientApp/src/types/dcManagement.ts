// ── Demotion ────────────────────────────────────────────────────────────────

export interface DemotionRequest {
  adminCredentialDn?: string
  isLastDcInDomain: boolean
  removeDnsRecords: boolean
  forceRemoval: boolean
  newAdminPassword?: string
}

export interface DemotionValidationResult {
  isValid: boolean
  errors: string[]
  warnings: string[]
  heldFsmoRoles: string[]
}

export interface DemotionStatus {
  isInProgress: boolean
  progress: number
  phase: string | null
  error: string | null
  isComplete: boolean
}

// ── FSMO Roles ──────────────────────────────────────────────────────────────

export type FsmoRoleName =
  | 'SchemaMaster'
  | 'DomainNamingMaster'
  | 'PdcEmulator'
  | 'RidMaster'
  | 'InfrastructureMaster'

export interface FsmoRoleInfo {
  role: FsmoRoleName
  holderDn: string
  holderServerName: string
  scope: string
  description: string
}

export interface DcInfo {
  ntdsSettingsDn: string
  serverName: string
  siteName: string
  isCurrentDc: boolean
}

export interface FsmoRolesResponse {
  roles: FsmoRoleInfo[]
  domainControllers: DcInfo[]
}

export interface FsmoRoleTransferRequest {
  role: string
  targetNtdsSettingsDn: string
}

export interface FsmoRoleTransferResult {
  success: boolean
  previousHolder?: string
  newHolder?: string
  errorMessage?: string
}

// ── Functional Levels ───────────────────────────────────────────────────────

export enum DomainFunctionalLevel {
  Windows2000 = 0,
  Windows2003 = 2,
  Windows2008 = 3,
  Windows2008R2 = 4,
  Windows2012 = 5,
  Windows2012R2 = 6,
  Windows2016 = 7,
  DirectoryNET_v1 = 100,
}

export enum ForestFunctionalLevel {
  Windows2000 = 0,
  Windows2003 = 2,
  Windows2008 = 3,
  Windows2008R2 = 4,
  Windows2012 = 5,
  Windows2012R2 = 6,
  Windows2016 = 7,
  DirectoryNET_v1 = 100,
}

export interface FunctionalLevelFeature {
  name: string
  description: string
  requiredDomainLevel: number
  isEnabled: boolean
}

export interface FunctionalLevelStatus {
  currentDomainLevel: number
  currentForestLevel: number
  maxDomainLevel: number
  maxForestLevel: number
  blockingDcs: string[]
  availableFeatures: FunctionalLevelFeature[]
}

export interface FunctionalLevelRaiseResult {
  success: boolean
  previousLevel?: number
  newLevel?: number
  errorMessage?: string
}

// ── Helpers ─────────────────────────────────────────────────────────────────

export const functionalLevelLabels: Record<number, string> = {
  0: 'Windows 2000',
  2: 'Windows Server 2003',
  3: 'Windows Server 2008',
  4: 'Windows Server 2008 R2',
  5: 'Windows Server 2012',
  6: 'Windows Server 2012 R2',
  7: 'Windows Server 2016',
  100: 'Directory.NET v1',
}

export const functionalLevelOptions = [
  { label: 'Windows 2000', value: 0 },
  { label: 'Windows Server 2003', value: 2 },
  { label: 'Windows Server 2008', value: 3 },
  { label: 'Windows Server 2008 R2', value: 4 },
  { label: 'Windows Server 2012', value: 5 },
  { label: 'Windows Server 2012 R2', value: 6 },
  { label: 'Windows Server 2016', value: 7 },
  { label: 'Directory.NET v1', value: 100 },
]
