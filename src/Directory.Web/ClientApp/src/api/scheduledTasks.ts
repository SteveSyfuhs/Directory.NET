import { get, post, put, del } from './client'
import type { ScheduledTask, TaskExecutionRecord } from '../types/scheduledTasks'

export function fetchScheduledTasks() {
  return get<ScheduledTask[]>('/scheduled-tasks')
}

export function fetchScheduledTask(id: string) {
  return get<ScheduledTask>(`/scheduled-tasks/${id}`)
}

export function createScheduledTask(task: Partial<ScheduledTask>) {
  return post<ScheduledTask>('/scheduled-tasks', task)
}

export function updateScheduledTask(id: string, task: Partial<ScheduledTask>) {
  return put<ScheduledTask>(`/scheduled-tasks/${id}`, task)
}

export function deleteScheduledTask(id: string) {
  return del(`/scheduled-tasks/${id}`)
}

export function runScheduledTaskNow(id: string) {
  return post<TaskExecutionRecord>(`/scheduled-tasks/${id}/run`)
}

export function fetchTaskHistory(id: string) {
  return get<TaskExecutionRecord[]>(`/scheduled-tasks/${id}/history`)
}
