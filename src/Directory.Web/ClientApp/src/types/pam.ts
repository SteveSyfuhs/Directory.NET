export type ActivationStatus =
  | 'PendingApproval'
  | 'Approved'
  | 'Active'
  | 'Expired'
  | 'Denied'
  | 'Cancelled'
  | 'Deactivated'

export interface PrivilegedRole {
  id: string
  name: string
  groupDn: string
  maxActivationHours: number
  requireJustification: boolean
  requireApproval: boolean
  approvers: string[]
  requireMfa: boolean
  isEnabled: boolean
}

export interface RoleActivation {
  id: string
  roleId: string
  roleName: string
  userDn: string
  groupDn: string
  justification: string
  status: ActivationStatus
  requestedAt: string
  activatedAt?: string
  expiresAt?: string
  deactivatedAt?: string
  approvedBy?: string
  deniedBy?: string
  denyReason?: string
  requestedHours: number
}

export interface BreakGlassAccount {
  id: string
  accountDn: string
  description: string
  isSealed: boolean
  lastAccessedAt?: string
  lastAccessedBy?: string
  createdAt: string
}
