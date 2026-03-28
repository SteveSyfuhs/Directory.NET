<script setup lang="ts">
import { ref, computed } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import InputText from 'primevue/inputtext'
import Password from 'primevue/password'
import Button from 'primevue/button'
import Message from 'primevue/message'
import { useAuthStore } from '../stores/auth'

const router = useRouter()
const route = useRoute()
const authStore = useAuthStore()

const username = ref('')
const password = ref('')
const error = ref('')
const loading = ref(false)

/** True when the user was redirected here because their session expired. */
const sessionExpired = computed(() => route.query.expired === '1')

/** The URL to return to after successful login. */
const returnUrl = computed(() => (route.query.returnUrl as string) || '/')

async function handleLogin() {
  error.value = ''
  if (!username.value.trim() || !password.value) {
    error.value = 'Username and password are required.'
    return
  }
  loading.value = true
  try {
    await authStore.login(username.value.trim(), password.value)
    // Redirect to the returnUrl (or fallback to dashboard)
    await router.push(returnUrl.value)
  } catch (e: unknown) {
    error.value = e instanceof Error ? e.message : 'Login failed. Please try again.'
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <div class="login-page">
    <div class="login-card">
      <div class="login-header">
        <i class="pi pi-server login-logo"></i>
        <h1 class="login-title">AD Manager</h1>
        <p class="login-subtitle">Sign in to the admin portal</p>
      </div>

      <Message v-if="sessionExpired" severity="warn" :closable="false" class="session-expired-msg">
        Your session has expired. Please log in again to continue.
      </Message>

      <form class="login-form" @submit.prevent="handleLogin">
        <div class="field">
          <label for="username" class="field-label">Username</label>
          <InputText
            id="username"
            v-model="username"
            placeholder="sAMAccountName"
            autocomplete="username"
            autofocus
            class="w-full"
            :disabled="loading"
          />
        </div>

        <div class="field">
          <label for="password" class="field-label">Password</label>
          <Password
            id="password"
            v-model="password"
            placeholder="Password"
            :feedback="false"
            toggle-mask
            autocomplete="current-password"
            input-class="w-full"
            class="w-full"
            :disabled="loading"
          />
        </div>

        <div v-if="error" class="login-error" role="alert">
          <i class="pi pi-exclamation-triangle"></i>
          <span>{{ error }}</span>
        </div>

        <Button
          type="submit"
          label="Sign In"
          icon="pi pi-sign-in"
          :loading="loading"
          class="w-full login-btn"
        />
      </form>
    </div>
  </div>
</template>

<style scoped>
.login-page {
  min-height: 100vh;
  display: flex;
  align-items: center;
  justify-content: center;
  background: var(--p-surface-ground);
  padding: 1.5rem;
}

.login-card {
  background: var(--p-surface-card);
  border: 1px solid var(--p-surface-border);
  border-radius: 1rem;
  padding: 2.5rem 2rem;
  width: 100%;
  max-width: 420px;
  box-shadow: 0 4px 24px rgba(0, 0, 0, 0.08);
}

.login-header {
  text-align: center;
  margin-bottom: 2rem;
}

.login-logo {
  font-size: 2.5rem;
  color: var(--app-accent-color, #6366f1);
  display: block;
  margin-bottom: 0.75rem;
}

.login-title {
  font-size: 1.5rem;
  font-weight: 700;
  color: var(--p-text-color);
  margin: 0 0 0.25rem;
  letter-spacing: -0.02em;
}

.login-subtitle {
  color: var(--p-text-muted-color);
  font-size: 0.875rem;
  margin: 0;
}

.login-form {
  display: flex;
  flex-direction: column;
  gap: 1.25rem;
}

.field {
  display: flex;
  flex-direction: column;
  gap: 0.375rem;
}

.field-label {
  font-size: 0.875rem;
  font-weight: 500;
  color: var(--p-text-color);
}

.login-error {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  background: var(--app-danger-bg, #fef2f2);
  border: 1px solid var(--app-danger-border, #fecaca);
  color: var(--app-danger-text, #dc2626);
  border-radius: 0.5rem;
  padding: 0.625rem 0.875rem;
  font-size: 0.875rem;
}

.login-error .pi {
  flex-shrink: 0;
}

.login-btn {
  margin-top: 0.25rem;
}

.session-expired-msg {
  margin-bottom: 0.5rem;
}
</style>
