import { get } from './client'
import type { DashboardSummary, DcHealth, ObjectSummary } from './types'

export const fetchSummary = () => get<DashboardSummary>('/dashboard/summary')
export const fetchDcHealth = () => get<DcHealth[]>('/dashboard/dc-health')
export const fetchRecentChanges = () => get<ObjectSummary[]>('/dashboard/recent-changes')
