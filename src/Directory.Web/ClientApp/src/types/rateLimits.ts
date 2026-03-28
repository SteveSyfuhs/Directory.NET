export interface DnRateLimit {
  dn: string
  permitLimit: number
  window: string
  windowSeconds: number
}

export interface RateLimitStats {
  totalChecks: number
  totalRejections: number
  activeDns: number
  defaultPermitLimit: number
  defaultWindowSeconds: number
  customLimitCount: number
  exemptionCount: number
  topConsumers: DnConsumption[]
  topRejected: DnRejection[]
}

export interface DnConsumption {
  dn: string
  currentWindowOps: number
  lastOperation: string
}

export interface DnRejection {
  dn: string
  rejections: number
}

export interface RateLimitConfig {
  defaultPermitLimit: number
  defaultWindowSeconds: number
  customLimits: DnRateLimit[]
  exemptions: string[]
}
