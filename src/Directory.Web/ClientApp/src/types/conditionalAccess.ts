export type RiskLevel = 'Low' | 'Medium' | 'High'

export interface PolicyConditions {
  includeUsers: string[]
  excludeUsers: string[]
  includeGroups: string[]
  excludeGroups: string[]
  includeApplications: string[]
  ipRanges: string[]
  countries: string[]
  minRiskLevel: RiskLevel | null
  devicePlatforms: string[]
}

export interface PolicyActions {
  requireMfa: boolean
  allowedMfaMethods: string[]
  blockAccess: boolean
  requirePasswordChange: boolean
  sessionLifetimeMinutes: number | null
}

export interface ConditionalAccessPolicy {
  id: string
  name: string
  description: string
  isEnabled: boolean
  priority: number
  conditions: PolicyConditions
  actions: PolicyActions
  createdAt: string
  modifiedAt: string
}

export interface AccessEvaluationRequest {
  userDn: string
  clientIp?: string
  applicationId?: string
  device?: {
    platform?: string
    userAgent?: string
    isCompliant?: boolean
  }
  riskLevel?: RiskLevel
}

export interface PolicyEvaluationEntry {
  policyId: string
  policyName: string
  matched: boolean
  reason?: string
}

export interface AccessEvaluationResult {
  accessGranted: boolean
  mfaRequired: boolean
  allowedMfaMethods: string[]
  passwordChangeRequired: boolean
  sessionLifetimeMinutes: number | null
  evaluatedPolicies: PolicyEvaluationEntry[]
  blockReason?: string
}

export interface SignInLogEntry {
  id: string
  userDn: string
  clientIp?: string
  applicationId?: string
  devicePlatform?: string
  result: AccessEvaluationResult
  timestamp: string
}
