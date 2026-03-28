<script setup lang="ts">
import { ref, computed } from 'vue'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Tag from 'primevue/tag'
import Dialog from 'primevue/dialog'
import Card from 'primevue/card'
import { useToast } from 'primevue/usetoast'
import {
  getMfaStatus,
  beginEnrollment,
  completeEnrollment,
  disableMfa,
  regenerateRecoveryCodes,
  validateMfaCode,
} from '../api/mfa'
import type {
  MfaStatus,
  MfaEnrollmentResult,
} from '../types/mfa'

const toast = useToast()

// Search
const searchQuery = ref('')
const searching = ref(false)
const resolvedDn = ref('')

// Status
const status = ref<MfaStatus | null>(null)
const statusLoading = ref(false)

// Enrollment
const enrollment = ref<MfaEnrollmentResult | null>(null)
const enrolling = ref(false)
const verificationCode = ref('')
const verifying = ref(false)
const recoveryCodes = ref<string[]>([])
const showRecoveryCodes = ref(false)

// Disable
const disableConfirmVisible = ref(false)
const disabling = ref(false)

// Regenerate
const regenConfirmVisible = ref(false)
const regenerating = ref(false)

// Validate
const validateCode = ref('')
const validating = ref(false)
const validateDialogVisible = ref(false)

const hasUser = computed(() => resolvedDn.value !== '')

async function lookupUser() {
  if (!searchQuery.value.trim()) return

  searching.value = true
  resolvedDn.value = searchQuery.value.trim()

  try {
    await loadStatus()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'User Not Found', detail: e.message, life: 5000 })
    resolvedDn.value = ''
    status.value = null
  } finally {
    searching.value = false
  }
}

async function loadStatus() {
  statusLoading.value = true
  try {
    status.value = await getMfaStatus(resolvedDn.value)
  } finally {
    statusLoading.value = false
  }
}

async function startEnrollment() {
  enrolling.value = true
  try {
    enrollment.value = await beginEnrollment(resolvedDn.value)
    verificationCode.value = ''
    toast.add({ severity: 'info', summary: 'Enrollment Started', detail: 'Scan the QR code or enter the secret manually in your authenticator app.', life: 5000 })
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Enrollment Failed', detail: e.message, life: 5000 })
  } finally {
    enrolling.value = false
  }
}

async function verifyEnrollment() {
  if (!verificationCode.value.trim()) return
  verifying.value = true
  try {
    const result = await completeEnrollment(resolvedDn.value, verificationCode.value.trim())
    if (result.success) {
      recoveryCodes.value = result.recoveryCodes
      showRecoveryCodes.value = true
      enrollment.value = null
      verificationCode.value = ''
      await loadStatus()
      toast.add({ severity: 'success', summary: 'MFA Enabled', detail: 'MFA has been successfully enabled. Save your recovery codes.', life: 5000 })
    } else {
      toast.add({ severity: 'warn', summary: 'Invalid Code', detail: 'The verification code was incorrect. Please try again.', life: 5000 })
    }
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Verification Failed', detail: e.message, life: 5000 })
  } finally {
    verifying.value = false
  }
}

async function confirmDisableMfa() {
  disabling.value = true
  try {
    await disableMfa(resolvedDn.value)
    disableConfirmVisible.value = false
    enrollment.value = null
    await loadStatus()
    toast.add({ severity: 'success', summary: 'MFA Disabled', detail: 'MFA has been disabled for this user.', life: 5000 })
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    disabling.value = false
  }
}

async function confirmRegenerateCodes() {
  regenerating.value = true
  try {
    const result = await regenerateRecoveryCodes(resolvedDn.value)
    recoveryCodes.value = result.recoveryCodes
    showRecoveryCodes.value = true
    regenConfirmVisible.value = false
    await loadStatus()
    toast.add({ severity: 'success', summary: 'Recovery Codes Regenerated', detail: 'New recovery codes have been generated. Save them securely.', life: 5000 })
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    regenerating.value = false
  }
}

async function doValidateCode() {
  if (!validateCode.value.trim()) return
  validating.value = true
  try {
    const result = await validateMfaCode(resolvedDn.value, validateCode.value.trim())
    if (result.isValid) {
      const msg = result.usedRecoveryCode ? 'Valid (used a recovery code).' : 'Valid TOTP code.'
      toast.add({ severity: 'success', summary: 'Code Valid', detail: msg, life: 5000 })
      if (result.usedRecoveryCode) await loadStatus()
    } else {
      toast.add({ severity: 'warn', summary: 'Invalid Code', detail: 'The code is not valid.', life: 5000 })
    }
    validateCode.value = ''
    validateDialogVisible.value = false
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    validating.value = false
  }
}

function copyRecoveryCodes() {
  const text = recoveryCodes.value.join('\n')
  navigator.clipboard.writeText(text)
  toast.add({ severity: 'info', summary: 'Copied', detail: 'Recovery codes copied to clipboard.', life: 3000 })
}

function cancelEnrollment() {
  enrollment.value = null
  verificationCode.value = ''
}

function formatDate(dateStr: string | null) {
  if (!dateStr) return '-'
  return new Date(dateStr).toLocaleString()
}
</script>

<template>
  <div>
    <div class="page-header">
      <h1><i class="pi pi-shield" style="margin-right: 0.5rem;"></i>MFA Management</h1>
      <p>Manage Multi-Factor Authentication (TOTP) for directory users.</p>
    </div>

    <!-- User Search -->
    <div class="card" style="margin-bottom: 1.5rem;">
      <div class="card-title">Find User</div>
      <div class="toolbar">
        <InputText
          v-model="searchQuery"
          placeholder="Enter DN, UPN, or sAMAccountName..."
          style="flex: 1; min-width: 300px;"
          @keyup.enter="lookupUser"
        />
        <Button
          label="Lookup"
          icon="pi pi-search"
          :loading="searching"
          @click="lookupUser"
        />
      </div>
    </div>

    <!-- MFA Status -->
    <template v-if="hasUser">
      <div class="card" style="margin-bottom: 1.5rem;">
        <div class="card-title">MFA Status</div>
        <div v-if="statusLoading" style="text-align: center; padding: 1rem;">
          Loading...
        </div>
        <div v-else-if="status" class="mfa-status-grid">
          <div class="mfa-status-item">
            <span class="mfa-status-label">User</span>
            <span class="mfa-status-value">{{ resolvedDn }}</span>
          </div>
          <div class="mfa-status-item">
            <span class="mfa-status-label">Enrollment</span>
            <Tag
              :value="status.isEnrolled ? 'Enrolled' : 'Not Enrolled'"
              :severity="status.isEnrolled ? 'success' : 'secondary'"
            />
          </div>
          <div class="mfa-status-item">
            <span class="mfa-status-label">Status</span>
            <Tag
              :value="status.isEnabled ? 'Enabled' : 'Disabled'"
              :severity="status.isEnabled ? 'success' : 'warn'"
            />
          </div>
          <div class="mfa-status-item">
            <span class="mfa-status-label">Enrolled At</span>
            <span class="mfa-status-value">{{ formatDate(status.enrolledAt) }}</span>
          </div>
          <div class="mfa-status-item">
            <span class="mfa-status-label">Recovery Codes Remaining</span>
            <Tag
              :value="String(status.recoveryCodesRemaining)"
              :severity="status.recoveryCodesRemaining > 2 ? 'success' : status.recoveryCodesRemaining > 0 ? 'warn' : 'danger'"
            />
          </div>
        </div>

        <!-- Actions -->
        <div class="mfa-actions" v-if="status">
          <Button
            v-if="!status.isEnabled"
            label="Enroll in MFA"
            icon="pi pi-plus"
            severity="info"
            :loading="enrolling"
            @click="startEnrollment"
          />
          <Button
            v-if="status.isEnabled"
            label="Validate Code"
            icon="pi pi-check-circle"
            severity="info"
            outlined
            @click="validateDialogVisible = true"
          />
          <Button
            v-if="status.isEnabled"
            label="Regenerate Recovery Codes"
            icon="pi pi-refresh"
            severity="warn"
            outlined
            @click="regenConfirmVisible = true"
          />
          <Button
            v-if="status.isEnabled || status.isEnrolled"
            label="Disable MFA"
            icon="pi pi-ban"
            severity="danger"
            outlined
            @click="disableConfirmVisible = true"
          />
        </div>
      </div>

      <!-- Enrollment Flow -->
      <div v-if="enrollment" class="card" style="margin-bottom: 1.5rem;">
        <div class="card-title">Enrollment Setup</div>
        <div class="enrollment-content">
          <p class="enrollment-instructions">
            Open your authenticator app (Google Authenticator, Authy, etc.) and add a new account using the secret below.
          </p>

          <div class="secret-display">
            <span class="mfa-status-label">Secret Key (manual entry)</span>
            <code class="secret-code">{{ enrollment.secret }}</code>
          </div>

          <div class="secret-display" style="margin-top: 0.75rem;">
            <span class="mfa-status-label">Account</span>
            <span class="mfa-status-value">{{ enrollment.accountName }}</span>
          </div>

          <div class="secret-display" style="margin-top: 0.75rem;">
            <span class="mfa-status-label">Provisioning URI</span>
            <code class="provisioning-uri">{{ enrollment.provisioningUri }}</code>
          </div>

          <div class="verification-section">
            <span class="mfa-status-label">Enter the 6-digit code from your authenticator to verify</span>
            <div class="toolbar" style="margin-top: 0.5rem;">
              <InputText
                v-model="verificationCode"
                placeholder="000000"
                maxlength="6"
                style="width: 160px; font-size: 1.25rem; text-align: center; letter-spacing: 0.3em;"
                @keyup.enter="verifyEnrollment"
              />
              <Button
                label="Verify & Enable"
                icon="pi pi-check"
                severity="success"
                :loading="verifying"
                :disabled="verificationCode.length !== 6"
                @click="verifyEnrollment"
              />
              <Button
                label="Cancel"
                severity="secondary"
                outlined
                @click="cancelEnrollment"
              />
            </div>
          </div>
        </div>
      </div>
    </template>

    <!-- Recovery Codes Dialog -->
    <Dialog
      v-model:visible="showRecoveryCodes"
      header="Recovery Codes"
      :modal="true"
      :closable="true"
      style="width: 480px;"
    >
      <div class="recovery-codes-content">
        <p class="recovery-warning">
          Save these recovery codes in a secure location. Each code can only be used once.
          If you lose access to your authenticator app, you can use these codes to sign in.
        </p>
        <div class="recovery-codes-list">
          <code v-for="code in recoveryCodes" :key="code" class="recovery-code">{{ code }}</code>
        </div>
        <div style="display: flex; gap: 0.5rem; margin-top: 1rem;">
          <Button
            label="Copy All"
            icon="pi pi-copy"
            severity="info"
            outlined
            @click="copyRecoveryCodes"
          />
          <span style="flex: 1;"></span>
          <Button
            label="Done"
            @click="showRecoveryCodes = false"
          />
        </div>
      </div>
    </Dialog>

    <!-- Disable Confirmation Dialog -->
    <Dialog
      v-model:visible="disableConfirmVisible"
      header="Disable MFA"
      :modal="true"
      style="width: 400px;"
    >
      <p>Are you sure you want to disable MFA for this user? They will no longer be required to provide a second factor.</p>
      <div style="display: flex; gap: 0.5rem; justify-content: flex-end; margin-top: 1rem;">
        <Button label="Cancel" severity="secondary" outlined @click="disableConfirmVisible = false" />
        <Button label="Disable MFA" severity="danger" :loading="disabling" @click="confirmDisableMfa" />
      </div>
    </Dialog>

    <!-- Regenerate Confirmation Dialog -->
    <Dialog
      v-model:visible="regenConfirmVisible"
      header="Regenerate Recovery Codes"
      :modal="true"
      style="width: 400px;"
    >
      <p>This will invalidate all existing recovery codes and generate new ones. Continue?</p>
      <div style="display: flex; gap: 0.5rem; justify-content: flex-end; margin-top: 1rem;">
        <Button label="Cancel" severity="secondary" outlined @click="regenConfirmVisible = false" />
        <Button label="Regenerate" severity="warn" :loading="regenerating" @click="confirmRegenerateCodes" />
      </div>
    </Dialog>

    <!-- Validate Code Dialog -->
    <Dialog
      v-model:visible="validateDialogVisible"
      header="Validate TOTP Code"
      :modal="true"
      style="width: 400px;"
    >
      <p>Enter a TOTP code or recovery code to validate.</p>
      <InputText
        v-model="validateCode"
        placeholder="Enter code..."
        style="width: 100%; margin-top: 0.5rem;"
        @keyup.enter="doValidateCode"
      />
      <div style="display: flex; gap: 0.5rem; justify-content: flex-end; margin-top: 1rem;">
        <Button label="Cancel" severity="secondary" outlined @click="validateDialogVisible = false" />
        <Button label="Validate" icon="pi pi-check" :loading="validating" @click="doValidateCode" />
      </div>
    </Dialog>
  </div>
</template>

<style scoped>
.mfa-status-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
  gap: 1rem;
  margin-bottom: 1.5rem;
}

.mfa-status-item {
  display: flex;
  flex-direction: column;
  gap: 0.375rem;
}

.mfa-status-label {
  font-size: 0.8125rem;
  font-weight: 600;
  color: var(--p-text-muted-color);
  text-transform: uppercase;
  letter-spacing: 0.04em;
}

.mfa-status-value {
  font-size: 0.9375rem;
  color: var(--p-text-color);
  word-break: break-all;
}

.mfa-actions {
  display: flex;
  gap: 0.5rem;
  flex-wrap: wrap;
  padding-top: 1rem;
  border-top: 1px solid var(--p-surface-border);
}

.enrollment-content {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.enrollment-instructions {
  color: var(--p-text-muted-color);
  font-size: 0.9375rem;
  margin: 0;
}

.secret-display {
  display: flex;
  flex-direction: column;
  gap: 0.375rem;
}

.secret-code {
  display: inline-block;
  font-family: 'Consolas', 'Courier New', monospace;
  font-size: 1.125rem;
  font-weight: 600;
  letter-spacing: 0.15em;
  color: var(--app-accent-color);
  background: var(--app-accent-bg);
  padding: 0.625rem 1rem;
  border-radius: 0.5rem;
  border: 1px solid var(--p-surface-border);
  word-break: break-all;
  user-select: all;
}

.provisioning-uri {
  display: block;
  font-family: 'Consolas', 'Courier New', monospace;
  font-size: 0.75rem;
  color: var(--p-text-muted-color);
  background: var(--app-neutral-bg);
  padding: 0.5rem 0.75rem;
  border-radius: 0.375rem;
  border: 1px solid var(--p-surface-border);
  word-break: break-all;
  user-select: all;
}

.verification-section {
  margin-top: 0.5rem;
  padding-top: 1rem;
  border-top: 1px solid var(--p-surface-border);
}

.recovery-codes-content {
  display: flex;
  flex-direction: column;
}

.recovery-warning {
  color: var(--app-warn-text);
  background: var(--app-warn-bg);
  border: 1px solid var(--app-warn-border);
  border-radius: 0.5rem;
  padding: 0.75rem 1rem;
  font-size: 0.875rem;
  margin: 0 0 1rem 0;
}

.recovery-codes-list {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 0.5rem;
}

.recovery-code {
  display: block;
  font-family: 'Consolas', 'Courier New', monospace;
  font-size: 1rem;
  font-weight: 600;
  letter-spacing: 0.1em;
  color: var(--p-text-color);
  background: var(--app-neutral-bg);
  padding: 0.5rem 0.75rem;
  border-radius: 0.375rem;
  border: 1px solid var(--p-surface-border);
  text-align: center;
}
</style>
