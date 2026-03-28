import { get, post, del } from './client'
import type { RateLimitStats, RateLimitConfig } from '../types/rateLimits'

export function fetchRateLimitStats() {
  return get<RateLimitStats>('/rate-limits/stats')
}

export function resetRateLimitStats() {
  return post<{ message: string }>('/rate-limits/stats/reset')
}

export function fetchRateLimitConfig() {
  return get<RateLimitConfig>('/rate-limits/limits')
}

export function setDnRateLimit(dn: string, permitLimit: number, windowSeconds: number = 60) {
  return post<{ message: string }>('/rate-limits/limits', { dn, permitLimit, windowSeconds })
}

export function removeDnRateLimit(dn: string) {
  return del(`/rate-limits/limits/${encodeURIComponent(dn)}`)
}

export function setDefaultRateLimit(permitLimit: number, windowSeconds: number) {
  return post<{ defaultPermitLimit: number; defaultWindowSeconds: number }>('/rate-limits/defaults', { permitLimit, windowSeconds })
}

export function addRateLimitExemption(dn: string) {
  return post<{ message: string }>('/rate-limits/exemptions', { dn })
}

export function removeRateLimitExemption(dn: string) {
  return del(`/rate-limits/exemptions/${encodeURIComponent(dn)}`)
}
