import { get, post, put } from './client'
import type {
  SsprSettings,
  SsprRegistrationStatus,
  SsprRegistrationSummary,
  SsprInitiateResult,
  SsprVerifyResult,
  SsprResetResult,
  SecurityQuestionAnswerInput,
} from '../types/sspr'

// ── Admin endpoints ──

export const getSsprSettings = () =>
  get<SsprSettings>('/sspr/settings')

export const updateSsprSettings = (settings: SsprSettings) =>
  put<SsprSettings>('/sspr/settings', settings)

export const getSsprRegistrations = () =>
  get<SsprRegistrationSummary[]>('/sspr/registrations')

// ── User-facing endpoints ──

export const registerForSspr = (
  userDn: string,
  answers: SecurityQuestionAnswerInput[],
  recoveryEmail?: string,
  recoveryPhone?: string,
) =>
  post<{ message: string; registeredAt: string }>('/sspr/register', {
    userDn,
    answers,
    recoveryEmail,
    recoveryPhone,
  })

export const getSsprStatus = (username: string) =>
  get<SsprRegistrationStatus>(`/sspr/status/${encodeURIComponent(username)}`)

export const initiateReset = (username: string) =>
  post<SsprInitiateResult>('/sspr/initiate', { username })

export const verifySecurityQuestions = (token: string, answers: SecurityQuestionAnswerInput[]) =>
  post<SsprVerifyResult>('/sspr/verify-questions', { token, answers })

export const verifyMfa = (token: string, code: string) =>
  post<SsprVerifyResult>('/sspr/verify-mfa', { token, code })

export const completeReset = (token: string, newPassword: string) =>
  post<SsprResetResult>('/sspr/reset', { token, newPassword })

export const getSecurityQuestions = () =>
  get<string[]>('/sspr/questions')
