<script setup lang="ts">
import { computed } from 'vue'
import Button from 'primevue/button'
import { useToast } from 'primevue/usetoast'
import { WELL_KNOWN_SIDS } from '../types/attributes'

const props = defineProps<{
  /** The SID string, e.g. "S-1-5-21-..." */
  displayValue: string
  /** Resolved account name, e.g. "DOMAIN\User" */
  resolvedName?: string
}>()

const toast = useToast()

const wellKnownName = computed(() => {
  return WELL_KNOWN_SIDS[props.displayValue] ?? null
})

const effectiveName = computed(() => {
  return props.resolvedName || wellKnownName.value || null
})

function copySid() {
  navigator.clipboard.writeText(props.displayValue).then(() => {
    toast.add({ severity: 'info', summary: 'Copied', detail: 'SID copied to clipboard', life: 2000 })
  })
}
</script>

<template>
  <span class="sid-display">
    <span v-if="effectiveName" class="sid-resolved">{{ effectiveName }}</span>
    <span class="sid-string" :class="{ 'sid-secondary': !!effectiveName }">
      <template v-if="effectiveName">(</template>{{ displayValue }}<template v-if="effectiveName">)</template>
    </span>
    <Button
      icon="pi pi-copy"
      text
      rounded
      size="small"
      class="sid-copy"
      @click.stop="copySid"
      title="Copy SID"
    />
  </span>
</template>

<style scoped>
.sid-display {
  display: inline-flex;
  align-items: center;
  gap: 0.375rem;
  font-size: 0.8125rem;
}

.sid-resolved {
  font-weight: 600;
  color: var(--p-text-color);
}

.sid-string {
  font-family: monospace;
  font-size: 0.75rem;
  color: var(--p-text-color);
}

.sid-string.sid-secondary {
  color: var(--p-text-muted-color);
}

.sid-copy {
  width: 1.5rem !important;
  height: 1.5rem !important;
}

.sid-copy :deep(.pi) {
  font-size: 0.6875rem;
}
</style>
