import { get, post, put, del } from './client'
import type { WebhookSubscription, WebhookDeliveryRecord, WebhookEventTypes } from '../types/webhooks'

export function fetchWebhooks() {
  return get<WebhookSubscription[]>('/webhooks')
}

export function fetchWebhook(id: string) {
  return get<WebhookSubscription>(`/webhooks/${id}`)
}

export function createWebhook(subscription: Partial<WebhookSubscription>) {
  return post<WebhookSubscription>('/webhooks', subscription)
}

export function updateWebhook(id: string, subscription: Partial<WebhookSubscription>) {
  return put<WebhookSubscription>(`/webhooks/${id}`, subscription)
}

export function deleteWebhook(id: string) {
  return del(`/webhooks/${id}`)
}

export function testWebhook(id: string) {
  return post<WebhookDeliveryRecord>(`/webhooks/${id}/test`)
}

export function fetchWebhookEventTypes() {
  return get<WebhookEventTypes>('/webhooks/events')
}

export function fetchWebhookDeliveries(id: string) {
  return get<WebhookDeliveryRecord[]>(`/webhooks/${id}/deliveries`)
}
