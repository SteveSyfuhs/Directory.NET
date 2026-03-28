<script setup lang="ts">
import { ref, onErrorCaptured } from 'vue'
import Button from 'primevue/button'
import Message from 'primevue/message'

const error = ref<Error | null>(null)

onErrorCaptured((err) => {
  error.value = err instanceof Error ? err : new Error(String(err))
  return false
})

function retry() {
  error.value = null
}
</script>

<template>
  <div v-if="error" style="padding: 2rem; max-width: 600px; margin: 2rem auto">
    <Message severity="error" :closable="false">
      <template #default>
        <div style="display: flex; flex-direction: column; gap: 0.75rem">
          <div>
            <strong>Something went wrong</strong>
            <p style="font-size: 0.875rem; margin: 0.5rem 0 0 0; color: var(--p-text-muted-color)">
              {{ error.message }}
            </p>
          </div>
          <div>
            <Button label="Try Again" icon="pi pi-refresh" size="small" @click="retry" />
          </div>
        </div>
      </template>
    </Message>
  </div>
  <slot v-else />
</template>
