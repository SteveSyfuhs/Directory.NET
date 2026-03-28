import { get, post, put, del } from './client'
import type { PrivilegedRole, RoleActivation, BreakGlassAccount } from '../types/pam'

// ── Privileged Roles ──────────────────────────────────────────

export const getRoles = () =>
  get<PrivilegedRole[]>('/pam/roles')

export const createRole = (role: Partial<PrivilegedRole>) =>
  post<PrivilegedRole>('/pam/roles', role)

export const updateRole = (id: string, role: Partial<PrivilegedRole>) =>
  put<PrivilegedRole>(`/pam/roles/${encodeURIComponent(id)}`, role)

export const deleteRole = (id: string) =>
  del(`/pam/roles/${encodeURIComponent(id)}`)

// ── Activations ───────────────────────────────────────────────

export const requestActivation = (userDn: string, roleId: string, justification: string, hours: number) =>
  post<RoleActivation>('/pam/activate', { userDn, roleId, justification, hours })

export const approveActivation = (id: string, approverDn: string) =>
  post<RoleActivation>(`/pam/activations/${encodeURIComponent(id)}/approve`, { approverDn })

export const denyActivation = (id: string, denierDn: string, reason: string) =>
  post<RoleActivation>(`/pam/activations/${encodeURIComponent(id)}/deny`, { denierDn, reason })

export const deactivateActivation = (id: string) =>
  post<RoleActivation>(`/pam/activations/${encodeURIComponent(id)}/deactivate`)

export const getActivations = () =>
  get<RoleActivation[]>('/pam/activations')

export const getActiveActivations = () =>
  get<RoleActivation[]>('/pam/activations/active')

export const getPendingActivations = () =>
  get<RoleActivation[]>('/pam/activations/pending')

// ── Break-Glass ───────────────────────────────────────────────

export const getBreakGlassAccounts = () =>
  get<BreakGlassAccount[]>('/pam/break-glass')

export const sealAccount = (accountDn: string, description: string) =>
  post<BreakGlassAccount>('/pam/break-glass', { accountDn, description })

export const breakGlassAccess = (id: string, reason: string, accessedBy?: string) =>
  post<{ password: string }>(`/pam/break-glass/${encodeURIComponent(id)}/access`, { reason, accessedBy })

export const resealAccount = (id: string) =>
  post<BreakGlassAccount>(`/pam/break-glass/${encodeURIComponent(id)}/reseal`)
