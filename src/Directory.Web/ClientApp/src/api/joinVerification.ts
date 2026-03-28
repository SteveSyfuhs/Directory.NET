import { get, post } from './client'
import type {
  JoinVerificationResult,
  JoinDiagnosticResult,
  DomainJoinHealthSummary,
} from '../types/joinVerification'

export function verifyDomainJoin(computerName: string) {
  return post<JoinVerificationResult>(`/domain-join/verify/${encodeURIComponent(computerName)}`)
}

export function diagnoseDomainJoin(computerName: string) {
  return post<JoinDiagnosticResult>(`/domain-join/diagnose/${encodeURIComponent(computerName)}`)
}

export function getDomainJoinHealth() {
  return get<DomainJoinHealthSummary>('/domain-join/health')
}

export function repairDomainJoin(computerName: string) {
  return post<JoinVerificationResult>(`/domain-join/repair/${encodeURIComponent(computerName)}`)
}
