import { get, post, del } from './client'
import type {
  MfaStatus,
  MfaEnrollmentResult,
  MfaEnrollmentCompleteResult,
  MfaValidationResult,
  MfaRecoveryCodesResult,
} from '../types/mfa'

export const getMfaStatus = (dn: string) =>
  get<MfaStatus>(`/mfa/status/${encodeURIComponent(dn)}`)

export const beginEnrollment = (dn: string) =>
  post<MfaEnrollmentResult>(`/mfa/enroll/${encodeURIComponent(dn)}`)

export const completeEnrollment = (dn: string, code: string) =>
  post<MfaEnrollmentCompleteResult>(`/mfa/enroll/${encodeURIComponent(dn)}/verify`, { code })

export const validateMfaCode = (dn: string, code: string) =>
  post<MfaValidationResult>(`/mfa/validate/${encodeURIComponent(dn)}`, { code })

export const disableMfa = (dn: string) =>
  del(`/mfa/${encodeURIComponent(dn)}`)

export const regenerateRecoveryCodes = (dn: string) =>
  post<MfaRecoveryCodesResult>(`/mfa/recovery-codes/${encodeURIComponent(dn)}`)
