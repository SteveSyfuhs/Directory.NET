<script setup lang="ts">
import { ref, onMounted } from 'vue'
import InputNumber from 'primevue/inputnumber'
import InputSwitch from 'primevue/inputswitch'
import InputText from 'primevue/inputtext'
import Button from 'primevue/button'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import ProgressSpinner from 'primevue/progressspinner'
import { useToast } from 'primevue/usetoast'
import type { SsprSettings, SsprRegistrationSummary } from '../types/sspr'
import {
  getSsprSettings,
  updateSsprSettings,
  getSsprRegistrations,
} from '../api/sspr'

const toast = useToast()

const loading = ref(true)
const saving = ref(false)
const regLoading = ref(false)

const settings = ref<SsprSettings>({
  enabled: true,
  requireMfa: true,
  requireSecurityQuestions: false,
  minSecurityQuestions: 3,
  resetTokenExpiryMinutes: 15,
  maxResetAttemptsPerHour: 5,
  securityQuestionOptions: [],
})

const registrations = ref<SsprRegistrationSummary[]>([])

const newQuestion = ref('')

onMounted(async () => {
  await Promise.all([loadSettings(), loadRegistrations()])
})

async function loadSettings() {
  loading.value = true
  try {
    settings.value = await getSsprSettings()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    loading.value = false
  }
}

async function saveSettings() {
  saving.value = true
  try {
    settings.value = await updateSsprSettings(settings.value)
    toast.add({ severity: 'success', summary: 'Saved', detail: 'SSPR settings updated.', life: 3000 })
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    saving.value = false
  }
}

async function loadRegistrations() {
  regLoading.value = true
  try {
    registrations.value = await getSsprRegistrations()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    regLoading.value = false
  }
}

function addQuestion() {
  const q = newQuestion.value.trim()
  if (!q) return
  if (settings.value.securityQuestionOptions.includes(q)) {
    toast.add({ severity: 'warn', summary: 'Duplicate', detail: 'This question already exists.', life: 3000 })
    return
  }
  settings.value.securityQuestionOptions.push(q)
  newQuestion.value = ''
}

function removeQuestion(index: number) {
  settings.value.securityQuestionOptions.splice(index, 1)
}

function moveQuestion(index: number, direction: -1 | 1) {
  const arr = settings.value.securityQuestionOptions
  const newIndex = index + direction
  if (newIndex < 0 || newIndex >= arr.length) return
  const tmp = arr[newIndex]
  arr[newIndex] = arr[index]
  arr[index] = tmp
}

function formatDate(val: string | null): string {
  if (!val) return '-'
  return new Date(val).toLocaleString()
}
</script>

<template>
  <div>
    <div class="page-header">
      <h1>Self-Service Password Reset</h1>
      <p>Configure SSPR settings and manage user registrations.</p>
    </div>

    <!-- SSPR Settings -->
    <div class="card" style="margin-bottom: 1.5rem">
      <div class="card-title">SSPR Settings</div>

      <div v-if="loading" style="text-align: center; padding: 2rem">
        <ProgressSpinner strokeWidth="3" />
      </div>

      <div v-else class="policy-form">
        <div class="form-row">
          <label>Enable SSPR</label>
          <InputSwitch v-model="settings.enabled" />
        </div>

        <div class="form-row">
          <label>Require MFA Verification</label>
          <InputSwitch v-model="settings.requireMfa" :disabled="!settings.enabled" />
          <small class="form-hint">Users with MFA enabled must verify their TOTP code during reset</small>
        </div>

        <div class="form-row">
          <label>Require Security Questions</label>
          <InputSwitch v-model="settings.requireSecurityQuestions" :disabled="!settings.enabled" />
        </div>

        <div class="form-row">
          <label>Minimum Security Questions</label>
          <InputNumber
            v-model="settings.minSecurityQuestions"
            :min="1"
            :max="10"
            showButtons
            :disabled="!settings.enabled || !settings.requireSecurityQuestions"
          />
          <small class="form-hint">Number of security questions users must answer during registration</small>
        </div>

        <div class="form-row">
          <label>Reset Token Expiry (minutes)</label>
          <InputNumber
            v-model="settings.resetTokenExpiryMinutes"
            :min="1"
            :max="1440"
            showButtons
            :disabled="!settings.enabled"
          />
          <small class="form-hint">How long a password reset token remains valid</small>
        </div>

        <div class="form-row">
          <label>Max Reset Attempts Per Hour</label>
          <InputNumber
            v-model="settings.maxResetAttemptsPerHour"
            :min="1"
            :max="100"
            showButtons
            :disabled="!settings.enabled"
          />
          <small class="form-hint">Rate limit: maximum reset initiations per user per hour</small>
        </div>

        <div style="margin-top: 1rem">
          <Button
            label="Save Settings"
            icon="pi pi-save"
            :loading="saving"
            @click="saveSettings"
          />
        </div>
      </div>
    </div>

    <!-- Security Questions Management -->
    <div class="card" style="margin-bottom: 1.5rem">
      <div class="card-title">Security Question Options</div>

      <div v-if="loading" style="text-align: center; padding: 2rem">
        <ProgressSpinner strokeWidth="3" />
      </div>

      <div v-else>
        <div class="question-list">
          <div
            v-for="(q, i) in settings.securityQuestionOptions"
            :key="i"
            class="question-item"
          >
            <span class="question-text">{{ q }}</span>
            <span class="question-actions">
              <Button
                icon="pi pi-arrow-up"
                text
                rounded
                size="small"
                :disabled="i === 0"
                @click="moveQuestion(i, -1)"
                v-tooltip="'Move up'"
              />
              <Button
                icon="pi pi-arrow-down"
                text
                rounded
                size="small"
                :disabled="i === settings.securityQuestionOptions.length - 1"
                @click="moveQuestion(i, 1)"
                v-tooltip="'Move down'"
              />
              <Button
                icon="pi pi-trash"
                text
                rounded
                size="small"
                severity="danger"
                @click="removeQuestion(i)"
                v-tooltip="'Remove'"
              />
            </span>
          </div>
        </div>

        <div v-if="settings.securityQuestionOptions.length === 0" class="empty-state">
          No security questions configured yet.
        </div>

        <div class="add-question-row">
          <InputText
            v-model="newQuestion"
            placeholder="Type a new security question..."
            class="add-question-input"
            @keyup.enter="addQuestion"
          />
          <Button
            icon="pi pi-plus"
            label="Add"
            @click="addQuestion"
            :disabled="!newQuestion.trim()"
          />
        </div>

        <div style="margin-top: 1rem">
          <Button
            label="Save Questions"
            icon="pi pi-save"
            :loading="saving"
            @click="saveSettings"
          />
        </div>
      </div>
    </div>

    <!-- Registered Users Table -->
    <div class="card">
      <div class="toolbar">
        <div class="card-title" style="margin-bottom: 0">Registered Users</div>
        <div class="toolbar-spacer"></div>
        <Button
          icon="pi pi-refresh"
          label="Refresh"
          text
          @click="loadRegistrations"
          :loading="regLoading"
        />
      </div>

      <DataTable
        :value="registrations"
        :loading="regLoading"
        stripedRows
        emptyMessage="No users have registered for SSPR yet."
      >
        <Column field="samAccountName" header="Account Name" sortable />
        <Column field="userPrincipalName" header="UPN" sortable />
        <Column header="Security Questions" sortable style="width: 160px">
          <template #body="{ data }">
            <i
              :class="data.hasSecurityQuestions ? 'pi pi-check-circle' : 'pi pi-times-circle'"
              :style="{ color: data.hasSecurityQuestions ? 'var(--app-success-text)' : 'var(--app-danger-text)' }"
            />
          </template>
        </Column>
        <Column header="MFA" sortable style="width: 100px">
          <template #body="{ data }">
            <i
              :class="data.hasMfa ? 'pi pi-check-circle' : 'pi pi-times-circle'"
              :style="{ color: data.hasMfa ? 'var(--app-success-text)' : 'var(--app-danger-text)' }"
            />
          </template>
        </Column>
        <Column header="Registered" sortable style="width: 200px">
          <template #body="{ data }">
            {{ formatDate(data.registeredAt) }}
          </template>
        </Column>
      </DataTable>
    </div>
  </div>
</template>

<style scoped>
.policy-form {
  display: flex;
  flex-direction: column;
  gap: 1.25rem;
  max-width: 480px;
}

.form-row {
  display: flex;
  flex-direction: column;
  gap: 0.375rem;
}

.form-row label {
  font-weight: 600;
  font-size: 0.875rem;
  color: var(--p-text-color);
}

.form-hint {
  color: var(--p-text-muted-color);
  font-size: 0.8125rem;
}

.question-list {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  margin-bottom: 1rem;
}

.question-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0.625rem 0.875rem;
  background: var(--app-neutral-bg);
  border: 1px solid var(--app-neutral-border);
  border-radius: 0.5rem;
  gap: 0.5rem;
}

.question-text {
  flex: 1;
  font-size: 0.875rem;
  color: var(--p-text-color);
}

.question-actions {
  display: flex;
  gap: 0.125rem;
  flex-shrink: 0;
}

.empty-state {
  text-align: center;
  color: var(--p-text-muted-color);
  font-size: 0.875rem;
  padding: 1.5rem;
}

.add-question-row {
  display: flex;
  gap: 0.5rem;
  align-items: center;
  margin-top: 0.75rem;
}

.add-question-input {
  flex: 1;
}
</style>
