export interface VerificationCheck {
  name: string
  category: string // "Account" | "DNS" | "SecureChannel" | "SPN" | "Replication"
  passed: boolean
  message: string
  recommendation: string | null
}

export interface JoinVerificationResult {
  computerName: string
  overallHealthy: boolean
  checks: VerificationCheck[]
}

export interface DiagnosticEntry {
  test: string
  status: string // "Pass" | "Fail" | "Warning" | "Skip"
  details: string
  durationMs: number
}

export interface JoinDiagnosticResult {
  computerName: string
  entries: DiagnosticEntry[]
  summary: string
  recommendations: string[]
}

export interface DomainJoinHealthSummary {
  totalJoins: number
  successfulJoins: number
  failedJoins: number
  successRate: number
  recentFailures: DomainJoinHistoryEntry[]
  recentOperations: DomainJoinHistoryEntry[]
  failureReasons: Record<string, number>
}

export interface DomainJoinHistoryEntry {
  timestamp: string
  operation: string
  computerName: string
  computerDn: string
  success: boolean
  errorMessage: string | null
  operator: string
}
