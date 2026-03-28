import { get, post } from './client'
import type {
  DemotionRequest,
  DemotionValidationResult,
  DemotionStatus,
  FsmoRolesResponse,
  FsmoRoleTransferRequest,
  FsmoRoleTransferResult,
  FunctionalLevelStatus,
  FunctionalLevelRaiseResult,
} from '../types/dcManagement'

// ── Demotion ────────────────────────────────────────────────────────────────

export function startDemotion(request: DemotionRequest) {
  return post<{ success: boolean; message: string }>('/dc-management/demote', request)
}

export function getDemotionStatus() {
  return get<DemotionStatus>('/dc-management/demote/status')
}

export function validateDemotion(request: DemotionRequest) {
  return post<DemotionValidationResult>('/dc-management/demote/validate', request)
}

// ── FSMO Roles ──────────────────────────────────────────────────────────────

export function getFsmoRoles() {
  return get<FsmoRolesResponse>('/dc-management/fsmo')
}

export function transferFsmoRole(request: FsmoRoleTransferRequest) {
  return post<FsmoRoleTransferResult>('/dc-management/fsmo/transfer', request)
}

export function seizeFsmoRole(request: FsmoRoleTransferRequest) {
  return post<FsmoRoleTransferResult>('/dc-management/fsmo/seize', request)
}

// ── Functional Levels ───────────────────────────────────────────────────────

export function getFunctionalLevels() {
  return get<FunctionalLevelStatus>('/dc-management/functional-levels')
}

export function raiseDomainFunctionalLevel(targetLevel: number) {
  return post<FunctionalLevelRaiseResult>('/dc-management/functional-levels/domain', { targetLevel })
}

export function raiseForestFunctionalLevel(targetLevel: number) {
  return post<FunctionalLevelRaiseResult>('/dc-management/functional-levels/forest', { targetLevel })
}
