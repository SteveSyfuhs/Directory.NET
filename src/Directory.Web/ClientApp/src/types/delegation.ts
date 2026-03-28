export interface AdminRole {
  id: string
  name: string
  description: string
  permissions: string[]
  scopeDns: string[]
  assignedMembers: string[]
  isBuiltIn: boolean
  createdAt: string
}

export interface DelegationPermission {
  key: string
  category: string
  displayName: string
  description: string
}

export interface EffectivePermissions {
  userDn: string
  permissions: string[]
  roles: EffectiveRoleSummary[]
}

export interface EffectiveRoleSummary {
  roleId: string
  roleName: string
  assignedVia: string
  scopeDns: string[]
}
