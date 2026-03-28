import { get, post, put, del } from './client'
import type { WorkflowDefinition, WorkflowInstance } from '../types/workflows'

export function fetchWorkflowDefinitions() {
  return get<WorkflowDefinition[]>('/workflows')
}

export function fetchWorkflowDefinition(id: string) {
  return get<WorkflowDefinition>(`/workflows/${id}`)
}

export function createWorkflowDefinition(definition: Partial<WorkflowDefinition>) {
  return post<WorkflowDefinition>('/workflows', definition)
}

export function updateWorkflowDefinition(id: string, definition: Partial<WorkflowDefinition>) {
  return put<WorkflowDefinition>(`/workflows/${id}`, definition)
}

export function deleteWorkflowDefinition(id: string) {
  return del(`/workflows/${id}`)
}

export function triggerWorkflow(id: string, targetDn: string, initiatedBy?: string) {
  return post<WorkflowInstance>(`/workflows/${id}/trigger`, { targetDn, initiatedBy })
}

export function fetchWorkflowInstances(status?: string) {
  const query = status ? `?status=${encodeURIComponent(status)}` : ''
  return get<WorkflowInstance[]>(`/workflows/instances${query}`)
}

export function fetchWorkflowInstance(id: string) {
  return get<WorkflowInstance>(`/workflows/instances/${id}`)
}

export function approveWorkflowStep(instanceId: string, approvedBy?: string) {
  return post<WorkflowInstance>(`/workflows/instances/${instanceId}/approve`, { approvedBy })
}

export function rejectWorkflowStep(instanceId: string, approvedBy?: string) {
  return post<WorkflowInstance>(`/workflows/instances/${instanceId}/reject`, { approvedBy })
}

export function fetchWorkflowTriggerTypes() {
  return get<string[]>('/workflows/triggers')
}

export function fetchWorkflowStepTypes() {
  return get<string[]>('/workflows/step-types')
}
