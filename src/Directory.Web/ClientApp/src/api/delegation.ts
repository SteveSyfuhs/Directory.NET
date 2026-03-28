import { get, post, put, del } from './client'
import type { AdminRole, DelegationPermission, EffectivePermissions } from '../types/delegation'

export const listRoles = () => get<AdminRole[]>('/delegation/roles')

export const getRole = (id: string) => get<AdminRole>(`/delegation/roles/${id}`)

export const createRole = (role: Partial<AdminRole>) =>
  post<AdminRole>('/delegation/roles', role)

export const updateRole = (id: string, role: Partial<AdminRole>) =>
  put<AdminRole>(`/delegation/roles/${id}`, role)

export const deleteRole = (id: string) => del(`/delegation/roles/${id}`)

export const listPermissions = () => get<DelegationPermission[]>('/delegation/permissions')

export const getEffectivePermissions = (dn: string) =>
  get<EffectivePermissions>(`/delegation/effective/${encodeURIComponent(dn)}`)

export const assignMember = (roleId: string, memberDn: string) =>
  post<AdminRole>(`/delegation/roles/${roleId}/assign`, { memberDn })

export const removeMember = (roleId: string, memberDn: string) =>
  post<AdminRole>(`/delegation/roles/${roleId}/remove`, { memberDn })
