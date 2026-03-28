export interface LockoutPolicy {
  lockoutEnabled: boolean
  lockoutThreshold: number
  lockoutDurationMinutes: number
  lockoutObservationWindowMinutes: number
}

export interface LockoutInfo {
  distinguishedName: string
  failedAttemptCount: number
  lockoutTime: string | null
  lastFailedAttempt: string | null
  isLockedOut: boolean
}
