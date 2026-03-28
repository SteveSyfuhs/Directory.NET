export interface MfaStatus {
  isEnabled: boolean
  isEnrolled: boolean
  enrolledAt: string | null
  recoveryCodesRemaining: number
}

export interface MfaEnrollmentResult {
  secret: string
  provisioningUri: string
  accountName: string
}

export interface MfaEnrollmentCompleteResult {
  success: boolean
  recoveryCodes: string[]
}

export interface MfaValidationResult {
  isValid: boolean
  usedRecoveryCode: boolean
}

export interface MfaRecoveryCodesResult {
  recoveryCodes: string[]
}
