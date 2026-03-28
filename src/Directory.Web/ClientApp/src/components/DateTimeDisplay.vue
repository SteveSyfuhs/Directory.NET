<script setup lang="ts">
import { computed } from 'vue'

const props = defineProps<{
  /** The display value string from the API */
  displayValue: string
  /** The raw FILETIME or generalized time value */
  rawValue: any
}>()

const FILETIME_EPOCH_OFFSET = BigInt('116444736000000000')
const MAX_FILETIME = BigInt('9223372036854775807')
const TICKS_PER_MS = BigInt(10000)

/** Parse FILETIME (100ns ticks since 1601) to JS Date */
function fileTimeToDate(ft: any): Date | null {
  try {
    const val = BigInt(ft)
    if (val <= 0n || val >= MAX_FILETIME) return null
    const epoch = val - FILETIME_EPOCH_OFFSET
    const ms = Number(epoch / TICKS_PER_MS)
    return new Date(ms)
  } catch {
    return null
  }
}

/** Parse generalized time "20060101120000.0Z" to Date */
function generalizedTimeToDate(gt: string): Date | null {
  try {
    // Format: YYYYMMDDHHmmss.fZ
    const match = gt.match(/^(\d{4})(\d{2})(\d{2})(\d{2})(\d{2})(\d{2})/)
    if (!match) return null
    return new Date(`${match[1]}-${match[2]}-${match[3]}T${match[4]}:${match[5]}:${match[6]}Z`)
  } catch {
    return null
  }
}

const parsedDate = computed<Date | null>(() => {
  const raw = props.rawValue
  if (raw == null) return null
  // Try as FILETIME (large integer)
  if (typeof raw === 'number' || typeof raw === 'string') {
    const ft = fileTimeToDate(raw)
    if (ft) return ft
  }
  // Try as generalized time string
  if (typeof raw === 'string') {
    return generalizedTimeToDate(raw)
  }
  return null
})

const isNever = computed(() => {
  if (props.displayValue === 'Never' || props.displayValue === '<not set>') return true
  try {
    const val = BigInt(props.rawValue)
    return val <= 0n || val >= MAX_FILETIME
  } catch {
    return false
  }
})

const localTime = computed(() => {
  if (isNever.value) return 'Never'
  return parsedDate.value?.toLocaleString() ?? props.displayValue
})

const utcTime = computed(() => {
  if (!parsedDate.value) return ''
  return parsedDate.value.toUTCString()
})

const relativeTime = computed(() => {
  if (!parsedDate.value) return ''
  const now = new Date()
  const diff = now.getTime() - parsedDate.value.getTime()
  const absDiff = Math.abs(diff)
  const seconds = Math.floor(absDiff / 1000)
  const minutes = Math.floor(seconds / 60)
  const hours = Math.floor(minutes / 60)
  const days = Math.floor(hours / 24)
  const months = Math.floor(days / 30)
  const years = Math.floor(days / 365)

  const suffix = diff > 0 ? 'ago' : 'from now'
  if (years > 0) return `${years} year${years > 1 ? 's' : ''} ${suffix}`
  if (months > 0) return `${months} month${months > 1 ? 's' : ''} ${suffix}`
  if (days > 0) return `${days} day${days > 1 ? 's' : ''} ${suffix}`
  if (hours > 0) return `${hours} hour${hours > 1 ? 's' : ''} ${suffix}`
  if (minutes > 0) return `${minutes} minute${minutes > 1 ? 's' : ''} ${suffix}`
  return 'just now'
})

const tooltipText = computed(() => {
  if (isNever.value) return `Raw value: ${props.rawValue}`
  const parts = []
  if (utcTime.value) parts.push(`UTC: ${utcTime.value}`)
  if (relativeTime.value) parts.push(relativeTime.value)
  parts.push(`Raw: ${props.rawValue}`)
  return parts.join('\n')
})
</script>

<template>
  <span
    class="datetime-display"
    :class="{ 'is-never': isNever }"
    :title="tooltipText"
  >
    <i v-if="!isNever" class="pi pi-clock" style="font-size: 0.75rem; margin-right: 0.25rem; opacity: 0.5"></i>
    <span>{{ localTime }}</span>
    <span v-if="relativeTime && !isNever" class="relative-time">({{ relativeTime }})</span>
  </span>
</template>

<style scoped>
.datetime-display {
  font-size: 0.8125rem;
  color: var(--p-text-color);
  cursor: default;
  display: inline-flex;
  align-items: center;
  gap: 0.125rem;
}

.datetime-display.is-never {
  color: var(--p-text-muted-color);
  font-style: italic;
}

.relative-time {
  color: var(--p-text-muted-color);
  font-size: 0.75rem;
  margin-left: 0.25rem;
}
</style>
