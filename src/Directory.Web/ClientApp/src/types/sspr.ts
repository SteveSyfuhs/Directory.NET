export interface SsprSettings {
  enabled: boolean
  requireMfa: boolean
  requireSecurityQuestions: boolean
  minSecurityQuestions: number
  resetTokenExpiryMinutes: number
  maxResetAttemptsPerHour: number
  securityQuestionOptions: string[]
}

export interface SsprRegistrationStatus {
  isRegistered: boolean
  userDn?: string
  hasSecurityQuestions: boolean
  hasRecoveryEmail: boolean
  hasRecoveryPhone: boolean
  hasMfa: boolean
  registeredAt?: string
}

export interface SsprRegistrationSummary {
  userDn: string
  samAccountName: string
  userPrincipalName: string
  hasSecurityQuestions: boolean
  hasMfa: boolean
  registeredAt: string
}

export interface SsprInitiateResult {
  token: string
  requireSecurityQuestions: boolean
  requireMfa: boolean
  securityQuestions: string[]
  expiresAt: string
}

export interface SsprVerifyResult {
  success: boolean
  message: string
  requireMfa?: boolean
}

export interface SsprResetResult {
  success: boolean
  message: string
}

export interface SecurityQuestionAnswerInput {
  question: string
  answer: string
}
