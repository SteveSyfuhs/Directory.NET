export type WorkflowTrigger =
  | 'UserCreated'
  | 'UserModified'
  | 'UserDisabled'
  | 'UserDeleted'
  | 'GroupMembershipChanged'
  | 'PasswordExpiring'
  | 'AccountExpiring'
  | 'Manual'

export type WorkflowStepType =
  | 'RequireApproval'
  | 'SendEmail'
  | 'AddToGroup'
  | 'RemoveFromGroup'
  | 'SetAttribute'
  | 'MoveToOu'
  | 'EnableAccount'
  | 'DisableAccount'
  | 'AssignRole'
  | 'TriggerWebhook'
  | 'Wait'

export interface WorkflowStep {
  id: string
  order: number
  name: string | null
  type: WorkflowStepType
  parameters: Record<string, string>
}

export interface WorkflowDefinition {
  id: string
  name: string
  description: string | null
  trigger: WorkflowTrigger
  steps: WorkflowStep[]
  isEnabled: boolean
  createdAt: string
  lastModifiedAt: string | null
}

export interface WorkflowStepResult {
  stepId: string
  stepName: string
  stepType: WorkflowStepType
  status: string
  detail: string | null
  startedAt: string | null
  completedAt: string | null
  approvedBy: string | null
}

export interface WorkflowInstance {
  id: string
  workflowDefinitionId: string
  workflowName: string
  targetDn: string
  status: string
  currentStep: number
  totalSteps: number
  stepResults: WorkflowStepResult[]
  startedAt: string
  completedAt: string | null
  initiatedBy: string | null
  approvalPendingFrom: string | null
}
