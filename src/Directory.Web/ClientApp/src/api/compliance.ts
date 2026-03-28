import { get, post } from './client'

export interface ComplianceReport {
  id: string
  name: string
  category: string
  description: string
  type: string
  isBuiltIn: boolean
  lastRunAt: string | null
  lastRunStatus: string | null
  parameters: Record<string, string>
  customFilter: string | null
}

export interface ReportResult {
  reportId: string
  reportName: string
  generatedAt: string
  totalItems: number
  flaggedItems: number
  complianceStatus: string
  data: Record<string, any>[]
  columns: string[]
  recommendations: ComplianceRecommendation[]
}

export interface ComplianceRecommendation {
  severity: string
  title: string
  description: string
  remediationAction: string | null
}

export interface ComplianceDashboard {
  totalReports: number
  compliantCount: number
  nonCompliantCount: number
  warningCount: number
  notRunCount: number
  complianceScore: number
  reportSummaries: ComplianceSummaryItem[]
  criticalFindings: ComplianceRecommendation[]
}

export interface ComplianceSummaryItem {
  reportId: string
  reportName: string
  category: string
  status: string
  flaggedItems: number
  lastRunAt: string | null
}

export function fetchComplianceReports() {
  return get<ComplianceReport[]>('/compliance/reports')
}

export function runComplianceReport(id: string) {
  return post<ReportResult>(`/compliance/reports/${id}/run`)
}

export function fetchReportResult(id: string) {
  return get<ReportResult>(`/compliance/reports/${id}/result`)
}

export function exportComplianceReport(id: string) {
  return post<string>(`/compliance/reports/${id}/export`)
}

export function fetchComplianceDashboard() {
  return get<ComplianceDashboard>('/compliance/dashboard')
}

export function createCustomReport(report: Partial<ComplianceReport>) {
  return post<ComplianceReport>('/compliance/reports/custom', report)
}
