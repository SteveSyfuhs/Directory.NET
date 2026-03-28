import { get, post, del } from './client'
import type {
  DomainJoinRequest,
  DomainJoinResult,
  DomainJoinInfo,
  DomainJoinValidation,
  DomainJoinHistoryEntry,
  RejoinRequest,
  UnjoinRequest,
  PrestagingRequest,
  PrestagingResult,
  PrestagedComputerSummary,
  DjoinProvisionRequest,
  DjoinProvisionResult,
  DjoinValidationResult,
} from '../types/domainJoin'

export const getDomainJoinInfo = () => get<DomainJoinInfo>('/domain-join/info')

export const joinComputer = (request: DomainJoinRequest) =>
  post<DomainJoinResult>('/domain-join/join', request)

export const rejoinComputer = (request: RejoinRequest) =>
  post<DomainJoinResult>('/domain-join/rejoin', request)

export const unjoinComputer = (request: UnjoinRequest) =>
  post<DomainJoinResult>('/domain-join/unjoin', request)

export const validateJoin = (request: DomainJoinRequest) =>
  post<DomainJoinValidation>('/domain-join/validate', request)

export const getDomainJoinHistory = () =>
  get<DomainJoinHistoryEntry[]>('/domain-join/history')

// ── Computer Pre-staging ──────────────────────────────────────

export const prestageComputer = (request: PrestagingRequest) =>
  post<PrestagingResult>('/domain-join/prestage', request)

export const bulkPrestageComputers = (requests: PrestagingRequest[]) =>
  post<PrestagingResult[]>('/domain-join/prestage/bulk', requests)

export const getPrestagedComputers = () =>
  get<PrestagedComputerSummary[]>('/domain-join/prestage')

export const deletePrestagedComputer = (name: string) =>
  del(`/domain-join/prestage/${encodeURIComponent(name)}`)

// ── Offline Domain Join (djoin) ────────────────────────────────

export const provisionOfflineJoin = (request: DjoinProvisionRequest) =>
  post<DjoinProvisionResult>('/domain-join/offline/provision', request)

export const validateOfflineJoinBlob = (blob: string) =>
  post<DjoinValidationResult>('/domain-join/offline/validate', { blob })

export const revokeOfflineJoin = (computerName: string) =>
  post<{ message: string }>('/domain-join/offline/revoke', { computerName })
