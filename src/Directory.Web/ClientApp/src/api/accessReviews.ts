import { get, post } from './client'

export interface AccessReviewScope {
  type: string
  targetDn: string
}

export interface AccessReview {
  id: string
  name: string
  description: string
  scope: AccessReviewScope
  reviewerDn: string
  frequency: string
  durationDays: number
  autoRemoveOnDeny: boolean
  status: string
  startedAt: string | null
  dueDate: string | null
  completedAt: string | null
  createdAt: string
}

export interface AccessReviewDecision {
  id: string
  reviewId: string
  userDn: string
  userDisplayName: string
  decision: string
  justification: string | null
  reviewerDn: string
  decidedAt: string | null
}

export function fetchAccessReviews() {
  return get<AccessReview[]>('/access-reviews')
}

export function createAccessReview(review: Partial<AccessReview>) {
  return post<AccessReview>('/access-reviews', review)
}

export function fetchAccessReview(id: string) {
  return get<AccessReview>(`/access-reviews/${id}`)
}

export function startAccessReview(id: string) {
  return post<AccessReview>(`/access-reviews/${id}/start`)
}

export function fetchAccessReviewDecisions(id: string) {
  return get<AccessReviewDecision[]>(`/access-reviews/${id}/decisions`)
}

export function submitAccessReviewDecision(reviewId: string, decision: Partial<AccessReviewDecision>) {
  return post<AccessReviewDecision>(`/access-reviews/${reviewId}/decisions`, decision)
}

export function completeAccessReview(id: string) {
  return post<AccessReview>(`/access-reviews/${id}/complete`)
}

export function fetchPendingReviews(reviewerDn?: string) {
  const qs = reviewerDn ? `?reviewerDn=${encodeURIComponent(reviewerDn)}` : ''
  return get<AccessReview[]>(`/access-reviews/pending${qs}`)
}
