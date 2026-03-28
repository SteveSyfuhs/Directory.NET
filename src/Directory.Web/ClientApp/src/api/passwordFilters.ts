import { get, put, post } from './client'
import type { PasswordFilter, PasswordFilterTestResult } from '../types/passwordFilters'

export const fetchPasswordFilters = () => get<PasswordFilter[]>('/password-filters')

export const enablePasswordFilter = (name: string) =>
  put<{ name: string; isEnabled: boolean }>(`/password-filters/${encodeURIComponent(name)}/enable`)

export const disablePasswordFilter = (name: string) =>
  put<{ name: string; isEnabled: boolean }>(`/password-filters/${encodeURIComponent(name)}/disable`)

export const testPassword = (password: string, dn?: string, oldPassword?: string) =>
  post<PasswordFilterTestResult>('/password-filters/test', { password, dn, oldPassword })
