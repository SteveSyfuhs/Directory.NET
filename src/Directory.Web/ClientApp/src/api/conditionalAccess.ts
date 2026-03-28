import { get, post, put, del } from './client'
import type {
  ConditionalAccessPolicy,
  AccessEvaluationRequest,
  AccessEvaluationResult,
  SignInLogEntry,
} from '../types/conditionalAccess'

export const listPolicies = () =>
  get<ConditionalAccessPolicy[]>('/conditional-access/policies')

export const createPolicy = (policy: Partial<ConditionalAccessPolicy>) =>
  post<ConditionalAccessPolicy>('/conditional-access/policies', policy)

export const updatePolicy = (id: string, policy: Partial<ConditionalAccessPolicy>) =>
  put<ConditionalAccessPolicy>(`/conditional-access/policies/${encodeURIComponent(id)}`, policy)

export const deletePolicy = (id: string) =>
  del(`/conditional-access/policies/${encodeURIComponent(id)}`)

export const evaluateAccess = (request: AccessEvaluationRequest) =>
  post<AccessEvaluationResult>('/conditional-access/evaluate', request)

export const getSignInLog = (count = 100) =>
  get<SignInLogEntry[]>(`/conditional-access/sign-in-log?count=${count}`)
