export interface WebhookSubscription {
  id: string
  name: string
  url: string
  secret: string
  events: string[]
  isEnabled: boolean
  createdAt: string
  lastDeliveryAt: string | null
  lastDeliveryStatus: string
  failureCount: number
}

export interface WebhookDeliveryRecord {
  id: string
  subscriptionId: string
  eventType: string
  timestamp: string
  statusCode: number
  status: string
  errorMessage: string
  attempt: number
}

export interface WebhookEventTypes {
  [category: string]: string[]
}
