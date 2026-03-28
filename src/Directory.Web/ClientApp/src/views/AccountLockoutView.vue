<script setup lang="ts">
import { ref, onMounted } from 'vue'
import InputNumber from 'primevue/inputnumber'
import InputSwitch from 'primevue/inputswitch'
import Button from 'primevue/button'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import ProgressSpinner from 'primevue/progressspinner'
import { useToast } from 'primevue/usetoast'
import type { LockoutPolicy, LockoutInfo } from '../types/lockout'
import PageHeader from '../components/PageHeader.vue'
import {
  getLockoutPolicy,
  updateLockoutPolicy,
  getLockedAccounts,
  unlockAccount,
} from '../api/lockout'

const toast = useToast()

const loading = ref(true)
const saving = ref(false)
const lockedLoading = ref(false)

const policy = ref<LockoutPolicy>({
  lockoutEnabled: true,
  lockoutThreshold: 5,
  lockoutDurationMinutes: 30,
  lockoutObservationWindowMinutes: 30,
})

const lockedAccounts = ref<LockoutInfo[]>([])

onMounted(async () => {
  await Promise.all([loadPolicy(), loadLockedAccounts()])
})

async function loadPolicy() {
  loading.value = true
  try {
    policy.value = await getLockoutPolicy()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    loading.value = false
  }
}

async function savePolicy() {
  saving.value = true
  try {
    policy.value = await updateLockoutPolicy(policy.value)
    toast.add({ severity: 'success', summary: 'Saved', detail: 'Lockout policy updated.', life: 3000 })
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    saving.value = false
  }
}

async function loadLockedAccounts() {
  lockedLoading.value = true
  try {
    lockedAccounts.value = await getLockedAccounts()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    lockedLoading.value = false
  }
}

async function doUnlock(dn: string) {
  try {
    await unlockAccount(dn)
    toast.add({ severity: 'success', summary: 'Unlocked', detail: `Account unlocked: ${dn}`, life: 3000 })
    await loadLockedAccounts()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

function formatDate(val: string | null): string {
  if (!val) return '-'
  return new Date(val).toLocaleString()
}
</script>

<template>
  <div>
    <PageHeader title="Account Lockout" subtitle="Configure lockout thresholds and manage locked accounts." />

    <!-- Policy Settings -->
    <div class="card" style="margin-bottom: 1.5rem">
      <div class="card-title">Lockout Policy</div>

      <div v-if="loading" style="text-align: center; padding: 2rem">
        <ProgressSpinner strokeWidth="3" />
      </div>

      <div v-else class="policy-form">
        <div class="form-row">
          <label>Enable Lockout</label>
          <InputSwitch v-model="policy.lockoutEnabled" />
        </div>

        <div class="form-row">
          <label>Lockout Threshold (failed attempts)</label>
          <InputNumber
            v-model="policy.lockoutThreshold"
            :min="0"
            :max="999"
            showButtons
            :disabled="!policy.lockoutEnabled"
          />
          <small class="form-hint">0 = never lock out</small>
        </div>

        <div class="form-row">
          <label>Lockout Duration (minutes)</label>
          <InputNumber
            v-model="policy.lockoutDurationMinutes"
            :min="0"
            :max="99999"
            showButtons
            :disabled="!policy.lockoutEnabled"
          />
          <small class="form-hint">0 = locked until admin unlocks</small>
        </div>

        <div class="form-row">
          <label>Observation Window (minutes)</label>
          <InputNumber
            v-model="policy.lockoutObservationWindowMinutes"
            :min="1"
            :max="99999"
            showButtons
            :disabled="!policy.lockoutEnabled"
          />
          <small class="form-hint">Time window in which failed attempts are counted</small>
        </div>

        <div style="margin-top: 1rem">
          <Button
            label="Save Policy"
            icon="pi pi-save"
            :loading="saving"
            @click="savePolicy"
          />
        </div>
      </div>
    </div>

    <!-- Locked Accounts Table -->
    <div class="card">
      <div class="toolbar">
        <div class="card-title" style="margin-bottom: 0">Locked Accounts</div>
        <div class="toolbar-spacer"></div>
        <Button
          icon="pi pi-refresh"
          label="Refresh"
          text
          @click="loadLockedAccounts"
          :loading="lockedLoading"
        />
      </div>

      <DataTable
        :value="lockedAccounts"
        :loading="lockedLoading"
        stripedRows
        emptyMessage="No accounts are currently locked out."
      >
        <Column field="distinguishedName" header="Distinguished Name" sortable />
        <Column field="failedAttemptCount" header="Failed Attempts" sortable style="width: 140px" />
        <Column header="Locked Since" sortable style="width: 200px">
          <template #body="{ data }">
            {{ formatDate(data.lockoutTime) }}
          </template>
        </Column>
        <Column header="Last Failed Attempt" sortable style="width: 200px">
          <template #body="{ data }">
            {{ formatDate(data.lastFailedAttempt) }}
          </template>
        </Column>
        <Column header="Actions" style="width: 120px">
          <template #body="{ data }">
            <Button
              icon="pi pi-lock-open"
              label="Unlock"
              severity="success"
              size="small"
              @click="doUnlock(data.distinguishedName)"
            />
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
</style>
