<script setup lang="ts">
import Button from 'primevue/button'
import { useRouter } from 'vue-router'

const router = useRouter()
</script>

<template>
  <div class="forbidden-page">
    <div class="forbidden-card card">
      <i class="pi pi-lock forbidden-icon"></i>
      <h1>403 &mdash; Forbidden</h1>
      <p>You don't have permission to access this page.</p>
      <p class="forbidden-hint">
        If you believe this is an error, contact your domain administrator to request the
        appropriate role or permissions.
      </p>
      <Button label="Back to Dashboard" icon="pi pi-home" @click="router.push('/')" />
    </div>
  </div>
</template>

<style scoped>
.forbidden-page {
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: 60vh;
}

.forbidden-card {
  text-align: center;
  max-width: 480px;
  padding: 3rem 2rem;
}

.forbidden-icon {
  font-size: 3rem;
  color: var(--app-danger-text);
  margin-bottom: 1rem;
}

.forbidden-card h1 {
  font-size: 1.5rem;
  font-weight: 700;
  color: var(--p-text-color);
  margin: 0 0 0.5rem;
}

.forbidden-card p {
  color: var(--p-text-muted-color);
  margin: 0 0 1rem;
}

.forbidden-hint {
  font-size: 0.875rem;
}
</style>
