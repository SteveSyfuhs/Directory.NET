export interface DomainJoinRequest {
  computerName: string
  dnsHostName: string
  organizationalUnit?: string
  adminUserDn: string
  operatingSystem?: string
  osVersion?: string
  osServicePack?: string
}

export interface DomainJoinResult {
  success: boolean
  computerDn: string
  computerSid: string
  machinePassword: string
  domainDnsName: string
  domainNetBiosName: string
  domainSid: string
  dcName: string
  dcAddress: string
  servicePrincipalNames: string[]
  errorMessage?: string
}

export interface DomainJoinInfo {
  domainDnsName: string
  domainNetBiosName: string
  domainSid: string
  dcName: string
  dcAddress: string
  defaultComputersOu: string
  domainDn: string
}

export interface DomainJoinValidation {
  isValid: boolean
  errors: string[]
  resolvedOu?: string
  resolvedDn?: string
}

export interface DomainJoinHistoryEntry {
  timestamp: string
  operation: string
  computerName: string
  computerDn: string
  success: boolean
  errorMessage?: string
  operator: string
}

export interface RejoinRequest {
  computerName: string
  adminUserDn: string
}

export interface UnjoinRequest {
  computerName: string
  adminUserDn: string
}

// ── Computer Pre-staging ──────────────────────────────────────

export interface PrestagingRequest {
  computerName: string
  dnsHostName?: string
  organizationalUnit?: string
  managedBy?: string
  description?: string
  operatingSystem?: string
  allowedToJoin?: string[]
}

export interface PrestagingResult {
  success: boolean
  computerDn: string
  computerSid: string
  samAccountName: string
  errorMessage?: string
}

export interface PrestagedComputerSummary {
  objectGuid: string
  name: string
  dn: string
  samAccountName: string
  objectSid: string
  managedBy?: string
  description?: string
  whenCreated: string
  enabled: boolean
}

// ── Offline Domain Join (djoin) ────────────────────────────────

export interface DjoinProvisionRequest {
  computerName: string
  organizationalUnit?: string
  machinePasswordOverride?: string
  reuseExistingAccount: boolean
}

export interface DjoinProvisionResult {
  success: boolean
  computerDn: string
  computerSid: string
  djoinBlob: string
  errorMessage?: string
}

export interface DjoinValidationResult {
  valid: boolean
  computerName?: string
  computerDn?: string
  computerSid?: string
  domainDnsName?: string
  provisionedAt?: string
  expiresAt?: string
  accountExists?: boolean
  errorMessage?: string
}
