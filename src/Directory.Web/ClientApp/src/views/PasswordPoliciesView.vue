<script setup lang="ts">
import { ref, onMounted } from 'vue'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import InputNumber from 'primevue/inputnumber'
import ToggleSwitch from 'primevue/toggleswitch'
import Tag from 'primevue/tag'
import ProgressSpinner from 'primevue/progressspinner'
import { useToast } from 'primevue/usetoast'
import {
  listPasswordPolicies,
  createPasswordPolicy,
  updatePasswordPolicy,
  deletePasswordPolicy,
  applyPasswordPolicy,
  removePasswordPolicyLink,
  getEffectivePasswordPolicy,
} from '../api/admin'
import {
  fetchPasswordFilters,
  enablePasswordFilter,
  disablePasswordFilter,
  testPassword,
} from '../api/passwordFilters'
import type { PasswordFilter, PasswordFilterTestResult } from '../types/passwordFilters'

const toast = useToast()
const policies = ref<any[]>([])
const loading = ref(true)
const showEditDialog = ref(false)
const showApplyDialog = ref(false)
const showEffectiveDialog = ref(false)
const saving = ref(false)
const isNew = ref(false)
const effectiveResult = ref<any>(null)
const effectiveLoading = ref(false)
const effectiveUserDn = ref('')
const applyTargetDn = ref('')
const applyPsoId = ref('')

const form = ref({
  objectGuid: '',
  name: '',
  description: '',
  precedence: 1,
  minPasswordLength: 7,
  passwordHistoryLength: 24,
  complexityEnabled: true,
  reversibleEncryptionEnabled: false,
  minPasswordAgeDays: 1,
  maxPasswordAgeDays: 42,
  lockoutThreshold: 0,
  lockoutDurationMinutes: 30,
  lockoutObservationWindowMinutes: 30,
})

// Password filter state
const filters = ref<PasswordFilter[]>([])
const loadingFilters = ref(true)
const testPasswordInput = ref('')
const testDnInput = ref('')
const testResult = ref<PasswordFilterTestResult | null>(null)
const testingPassword = ref(false)

onMounted(() => {
  loadPolicies()
  loadFilters()
})

async function loadFilters() {
  loadingFilters.value = true
  try {
    filters.value = await fetchPasswordFilters()
  } catch {
    // may not be available
  } finally {
    loadingFilters.value = false
  }
}

async function toggleFilter(filter: PasswordFilter) {
  try {
    if (filter.isEnabled) {
      await disablePasswordFilter(filter.name)
      filter.isEnabled = false
      toast.add({ severity: 'info', summary: 'Disabled', detail: `${filter.name} disabled`, life: 3000 })
    } else {
      await enablePasswordFilter(filter.name)
      filter.isEnabled = true
      toast.add({ severity: 'success', summary: 'Enabled', detail: `${filter.name} enabled`, life: 3000 })
    }
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

async function runPasswordTest() {
  if (!testPasswordInput.value) {
    toast.add({ severity: 'warn', summary: 'Validation', detail: 'Enter a password to test', life: 3000 })
    return
  }
  testingPassword.value = true
  try {
    testResult.value = await testPassword(testPasswordInput.value, testDnInput.value || undefined)
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    testingPassword.value = false
  }
}

async function loadPolicies() {
  loading.value = true
  try {
    policies.value = await listPasswordPolicies()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    loading.value = false
  }
}

function openCreate() {
  isNew.value = true
  form.value = {
    objectGuid: '',
    name: '',
    description: '',
    precedence: 1,
    minPasswordLength: 7,
    passwordHistoryLength: 24,
    complexityEnabled: true,
    reversibleEncryptionEnabled: false,
    minPasswordAgeDays: 1,
    maxPasswordAgeDays: 42,
    lockoutThreshold: 0,
    lockoutDurationMinutes: 30,
    lockoutObservationWindowMinutes: 30,
  }
  showEditDialog.value = true
}

function openEdit(pso: any) {
  isNew.value = false
  form.value = {
    objectGuid: pso.objectGuid,
    name: pso.name,
    description: pso.description || '',
    precedence: pso.precedence,
    minPasswordLength: pso.minPasswordLength,
    passwordHistoryLength: pso.passwordHistoryLength,
    complexityEnabled: pso.complexityEnabled,
    reversibleEncryptionEnabled: pso.reversibleEncryptionEnabled,
    minPasswordAgeDays: pso.minPasswordAgeDays,
    maxPasswordAgeDays: pso.maxPasswordAgeDays,
    lockoutThreshold: pso.lockoutThreshold,
    lockoutDurationMinutes: pso.lockoutDurationMinutes,
    lockoutObservationWindowMinutes: pso.lockoutObservationWindowMinutes,
  }
  showEditDialog.value = true
}

async function submitSave() {
  if (!form.value.name.trim()) {
    toast.add({ severity: 'warn', summary: 'Validation', detail: 'Name is required', life: 3000 })
    return
  }
  saving.value = true
  try {
    if (isNew.value) {
      await createPasswordPolicy({
        name: form.value.name.trim(),
        description: form.value.description || undefined,
        precedence: form.value.precedence,
        minPasswordLength: form.value.minPasswordLength,
        passwordHistoryLength: form.value.passwordHistoryLength,
        complexityEnabled: form.value.complexityEnabled,
        reversibleEncryptionEnabled: form.value.reversibleEncryptionEnabled,
        minPasswordAgeDays: form.value.minPasswordAgeDays,
        maxPasswordAgeDays: form.value.maxPasswordAgeDays,
        lockoutThreshold: form.value.lockoutThreshold,
        lockoutDurationMinutes: form.value.lockoutDurationMinutes,
        lockoutObservationWindowMinutes: form.value.lockoutObservationWindowMinutes,
      })
      toast.add({ severity: 'success', summary: 'Created', detail: `PSO '${form.value.name}' created`, life: 3000 })
    } else {
      await updatePasswordPolicy(form.value.objectGuid, {
        name: form.value.name.trim(),
        description: form.value.description,
        precedence: form.value.precedence,
        minPasswordLength: form.value.minPasswordLength,
        passwordHistoryLength: form.value.passwordHistoryLength,
        complexityEnabled: form.value.complexityEnabled,
        reversibleEncryptionEnabled: form.value.reversibleEncryptionEnabled,
        minPasswordAgeDays: form.value.minPasswordAgeDays,
        maxPasswordAgeDays: form.value.maxPasswordAgeDays,
        lockoutThreshold: form.value.lockoutThreshold,
        lockoutDurationMinutes: form.value.lockoutDurationMinutes,
        lockoutObservationWindowMinutes: form.value.lockoutObservationWindowMinutes,
      })
      toast.add({ severity: 'success', summary: 'Updated', detail: `PSO '${form.value.name}' updated`, life: 3000 })
    }
    showEditDialog.value = false
    await loadPolicies()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    saving.value = false
  }
}

async function confirmDelete(pso: any) {
  if (!confirm(`Delete password policy '${pso.name}'?`)) return
  try {
    await deletePasswordPolicy(pso.objectGuid)
    toast.add({ severity: 'success', summary: 'Deleted', detail: `PSO '${pso.name}' deleted`, life: 3000 })
    await loadPolicies()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

function openApplyDialog(psoId: string) {
  applyPsoId.value = psoId
  applyTargetDn.value = ''
  showApplyDialog.value = true
}

async function submitApply() {
  if (!applyTargetDn.value.trim()) {
    toast.add({ severity: 'warn', summary: 'Validation', detail: 'Target DN is required', life: 3000 })
    return
  }
  saving.value = true
  try {
    await applyPasswordPolicy(applyPsoId.value, applyTargetDn.value.trim())
    toast.add({ severity: 'success', summary: 'Applied', detail: 'PSO linked to target', life: 3000 })
    showApplyDialog.value = false
    await loadPolicies()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    saving.value = false
  }
}

async function removeLink(psoId: string, dn: string) {
  if (!confirm(`Remove link to ${dn}?`)) return
  try {
    await removePasswordPolicyLink(psoId, dn)
    toast.add({ severity: 'success', summary: 'Removed', detail: 'Link removed', life: 3000 })
    await loadPolicies()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

function openEffective() {
  effectiveUserDn.value = ''
  effectiveResult.value = null
  showEffectiveDialog.value = true
}

async function lookupEffective() {
  if (!effectiveUserDn.value.trim()) {
    toast.add({ severity: 'warn', summary: 'Validation', detail: 'User DN is required', life: 3000 })
    return
  }
  effectiveLoading.value = true
  try {
    effectiveResult.value = await getEffectivePasswordPolicy(effectiveUserDn.value.trim())
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    effectiveLoading.value = false
  }
}

function cnFromDn(dn: string): string {
  const match = dn.match(/^(?:CN|OU)=([^,]+)/i)
  return match ? match[1] : dn
}
</script>

<template>
  <div>
    <div class="page-header">
      <h1>Fine-Grained Password Policies</h1>
      <p>Manage Password Settings Objects (PSOs) for granular password policies per user or group</p>
    </div>

    <div class="toolbar">
      <Button label="New PSO" icon="pi pi-plus" @click="openCreate" />
      <Button label="Test Effective Policy" icon="pi pi-search" severity="info" @click="openEffective" />
      <div class="toolbar-spacer" />
      <Button label="Refresh" icon="pi pi-refresh" severity="secondary" @click="loadPolicies" />
    </div>

    <div v-if="loading" style="text-align: center; padding: 4rem">
      <ProgressSpinner />
    </div>

    <div v-else class="card" style="padding: 0">
      <DataTable :value="policies" stripedRows size="small" dataKey="objectGuid">
        <template #header>
          <div style="font-weight: 600; padding: 0.25rem">Password Settings Objects</div>
        </template>

        <Column header="Name" sortable sortField="name">
          <template #body="{ data }">
            <div style="display: flex; align-items: center; gap: 0.5rem">
              <i class="pi pi-shield" style="color: var(--app-accent-color)"></i>
              <div>
                <div style="font-weight: 600">{{ data.name }}</div>
                <div v-if="data.description" style="font-size: 0.8125rem; color: var(--p-text-muted-color)">{{ data.description }}</div>
              </div>
            </div>
          </template>
        </Column>

        <Column header="Precedence" sortable sortField="precedence" style="width: 7rem; text-align: center">
          <template #body="{ data }">
            <Tag :value="String(data.precedence)" severity="info" />
          </template>
        </Column>

        <Column header="Min Length" sortable sortField="minPasswordLength" style="width: 7rem; text-align: center">
          <template #body="{ data }">{{ data.minPasswordLength }}</template>
        </Column>

        <Column header="Max Age (days)" sortable sortField="maxPasswordAgeDays" style="width: 8rem">
          <template #body="{ data }">{{ data.maxPasswordAgeDays }}</template>
        </Column>

        <Column header="Lockout" sortable sortField="lockoutThreshold" style="width: 6rem; text-align: center">
          <template #body="{ data }">{{ data.lockoutThreshold || 'Off' }}</template>
        </Column>

        <Column header="Applied To" style="width: 16rem">
          <template #body="{ data }">
            <div style="display: flex; flex-direction: column; gap: 0.25rem">
              <div v-if="data.appliedTo.length === 0" style="color: var(--p-text-muted-color); font-size: 0.8125rem">
                Not applied
              </div>
              <div v-for="dn in data.appliedTo" :key="dn" style="display: flex; align-items: center; gap: 0.25rem; font-size: 0.8125rem">
                <span style="flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap" :title="dn">{{ cnFromDn(dn) }}</span>
                <Button icon="pi pi-times" severity="danger" text size="small" style="width: 1.5rem; height: 1.5rem"
                        @click="removeLink(data.objectGuid, dn)" title="Remove" />
              </div>
              <Button icon="pi pi-plus" label="Add" severity="secondary" text size="small"
                      @click="openApplyDialog(data.objectGuid)" style="align-self: flex-start" />
            </div>
          </template>
        </Column>

        <Column header="Actions" style="width: 8rem">
          <template #body="{ data }">
            <div style="display: flex; gap: 0.25rem">
              <Button icon="pi pi-pencil" severity="info" text size="small" title="Edit" @click="openEdit(data)" />
              <Button icon="pi pi-trash" severity="danger" text size="small" title="Delete" @click="confirmDelete(data)" />
            </div>
          </template>
        </Column>

        <template #empty>
          <div style="text-align: center; padding: 3rem; color: var(--p-text-muted-color)">
            <i class="pi pi-shield" style="font-size: 2rem; margin-bottom: 0.5rem; display: block; opacity: 0.4"></i>
            No fine-grained password policies configured
            <p style="font-size: 0.8125rem; color: var(--p-text-muted-color); margin: 0.5rem 0 0 0">Create a Password Settings Object (PSO) to define custom password and lockout rules for specific users or groups.</p>
          </div>
        </template>
      </DataTable>
    </div>

    <!-- Create/Edit PSO Dialog -->
    <Dialog v-model:visible="showEditDialog" :header="isNew ? 'Create Password Policy' : 'Edit Password Policy'" modal
            :style="{ width: '38rem' }" :closable="!saving">
      <div style="display: flex; flex-direction: column; gap: 1rem; padding-top: 0.5rem">
        <p style="font-size: 0.8125rem; color: var(--p-text-muted-color); margin: 0 0 0.25rem 0">Configure password and account lockout rules. This PSO can be applied to specific users or groups to override the default domain policy.</p>
        <div>
          <label style="display: block; font-weight: 600; margin-bottom: 0.25rem; font-size: 0.875rem">Name*</label>
          <InputText v-model="form.name" style="width: 100%" :disabled="saving" placeholder="e.g. Admins-StrongPolicy" />
        </div>
        <div>
          <label style="display: block; font-weight: 600; margin-bottom: 0.25rem; font-size: 0.875rem">Description</label>
          <InputText v-model="form.description" style="width: 100%" :disabled="saving" />
        </div>
        <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 1rem">
          <div>
            <label style="display: block; font-weight: 600; margin-bottom: 0.25rem; font-size: 0.875rem">Precedence (lower = higher priority)</label>
            <InputNumber v-model="form.precedence" :min="1" style="width: 100%" :disabled="saving" />
            <small style="font-size: 0.75rem; color: var(--p-text-muted-color)">When multiple PSOs apply, the lowest precedence value wins.</small>
          </div>
          <div>
            <label style="display: block; font-weight: 600; margin-bottom: 0.25rem; font-size: 0.875rem">Minimum Password Length</label>
            <InputNumber v-model="form.minPasswordLength" :min="0" :max="255" style="width: 100%" :disabled="saving" />
            <small style="font-size: 0.75rem; color: var(--p-text-muted-color)">The minimum number of characters required in a password.</small>
          </div>
        </div>
        <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 1rem">
          <div>
            <label style="display: block; font-weight: 600; margin-bottom: 0.25rem; font-size: 0.875rem">Password History Length</label>
            <InputNumber v-model="form.passwordHistoryLength" :min="0" :max="24" style="width: 100%" :disabled="saving" />
            <small style="font-size: 0.75rem; color: var(--p-text-muted-color)">Number of previous passwords remembered to prevent reuse.</small>
          </div>
          <div>
            <label style="display: block; font-weight: 600; margin-bottom: 0.25rem; font-size: 0.875rem">Lockout Threshold (0 = no lockout)</label>
            <InputNumber v-model="form.lockoutThreshold" :min="0" style="width: 100%" :disabled="saving" />
            <small style="font-size: 0.75rem; color: var(--p-text-muted-color)">Number of failed attempts before the account is locked out.</small>
          </div>
        </div>
        <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 1rem">
          <div>
            <label style="display: block; font-weight: 600; margin-bottom: 0.25rem; font-size: 0.875rem">Min Password Age (days)</label>
            <InputNumber v-model="form.minPasswordAgeDays" :min="0" :maxFractionDigits="2" style="width: 100%" :disabled="saving" />
            <small style="font-size: 0.75rem; color: var(--p-text-muted-color)">How long a user must wait before changing their password again.</small>
          </div>
          <div>
            <label style="display: block; font-weight: 600; margin-bottom: 0.25rem; font-size: 0.875rem">Max Password Age (days)</label>
            <InputNumber v-model="form.maxPasswordAgeDays" :min="0" :maxFractionDigits="2" style="width: 100%" :disabled="saving" />
            <small style="font-size: 0.75rem; color: var(--p-text-muted-color)">How long a password can be used before it must be changed. Set to 0 for no expiration.</small>
          </div>
        </div>
        <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 1rem">
          <div>
            <label style="display: block; font-weight: 600; margin-bottom: 0.25rem; font-size: 0.875rem">Lockout Duration (minutes)</label>
            <InputNumber v-model="form.lockoutDurationMinutes" :min="0" :maxFractionDigits="2" style="width: 100%" :disabled="saving" />
            <small style="font-size: 0.75rem; color: var(--p-text-muted-color)">How long the account stays locked after exceeding the threshold.</small>
          </div>
          <div>
            <label style="display: block; font-weight: 600; margin-bottom: 0.25rem; font-size: 0.875rem">Lockout Observation Window (minutes)</label>
            <InputNumber v-model="form.lockoutObservationWindowMinutes" :min="0" :maxFractionDigits="2" style="width: 100%" :disabled="saving" />
            <small style="font-size: 0.75rem; color: var(--p-text-muted-color)">Time window in which failed attempts are counted toward the lockout threshold.</small>
          </div>
        </div>
        <div style="display: flex; gap: 2rem">
          <div style="display: flex; align-items: center; gap: 0.5rem">
            <ToggleSwitch v-model="form.complexityEnabled" :disabled="saving" />
            <label style="font-size: 0.875rem">Complexity Required</label>
          </div>
          <div style="display: flex; align-items: center; gap: 0.5rem">
            <ToggleSwitch v-model="form.reversibleEncryptionEnabled" :disabled="saving" />
            <label style="font-size: 0.875rem">Reversible Encryption</label>
          </div>
        </div>
      </div>
      <template #footer>
        <Button label="Cancel" severity="secondary" @click="showEditDialog = false" :disabled="saving" />
        <Button :label="isNew ? 'Create' : 'Save'" icon="pi pi-save" @click="submitSave" :loading="saving" />
      </template>
    </Dialog>

    <!-- Apply PSO to user/group Dialog -->
    <Dialog v-model:visible="showApplyDialog" header="Apply Policy to User/Group" modal
            :style="{ width: '28rem' }" :closable="!saving">
      <div style="padding-top: 0.5rem">
        <label style="display: block; font-weight: 600; margin-bottom: 0.25rem; font-size: 0.875rem">Target Distinguished Name</label>
        <InputText v-model="applyTargetDn" style="width: 100%" :disabled="saving"
                   placeholder="CN=John Smith,OU=Users,DC=corp,DC=example,DC=com" />
        <p style="font-size: 0.8125rem; color: var(--p-text-muted-color); margin-top: 0.5rem">
          Enter the DN of a user or global security group to apply this password policy to.
        </p>
      </div>
      <template #footer>
        <Button label="Cancel" severity="secondary" @click="showApplyDialog = false" :disabled="saving" />
        <Button label="Apply" icon="pi pi-check" @click="submitApply" :loading="saving" />
      </template>
    </Dialog>

    <!-- Test Effective Policy Dialog -->
    <Dialog v-model:visible="showEffectiveDialog" header="Test Effective Password Policy" modal
            :style="{ width: '32rem' }">
      <div style="padding-top: 0.5rem">
        <div style="margin-bottom: 1rem">
          <label style="display: block; font-weight: 600; margin-bottom: 0.25rem; font-size: 0.875rem">User Distinguished Name</label>
          <div style="display: flex; gap: 0.5rem">
            <InputText v-model="effectiveUserDn" style="flex: 1" :disabled="effectiveLoading"
                       placeholder="CN=John Smith,OU=Users,DC=corp,DC=example,DC=com" />
            <Button label="Lookup" icon="pi pi-search" @click="lookupEffective" :loading="effectiveLoading" />
          </div>
        </div>

        <div v-if="effectiveResult" style="margin-top: 1rem">
          <div style="display: flex; align-items: center; gap: 0.5rem; margin-bottom: 1rem">
            <Tag :value="effectiveResult.source" :severity="effectiveResult.psoDn ? 'success' : 'secondary'" />
            <span v-if="effectiveResult.psoDn" style="font-size: 0.8125rem; color: var(--p-text-muted-color)">
              Precedence: {{ effectiveResult.precedence }}
            </span>
          </div>
          <div v-if="effectiveResult.psoDn" style="font-size: 0.8125rem; color: var(--p-text-muted-color); margin-bottom: 1rem; word-break: break-all">
            {{ effectiveResult.psoDn }}
          </div>
          <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 0.75rem; font-size: 0.875rem">
            <div>
              <div style="color: var(--p-text-muted-color)">Min Length</div>
              <div style="font-weight: 600">{{ effectiveResult.minPasswordLength }}</div>
            </div>
            <div>
              <div style="color: var(--p-text-muted-color)">History Length</div>
              <div style="font-weight: 600">{{ effectiveResult.passwordHistoryLength }}</div>
            </div>
            <div>
              <div style="color: var(--p-text-muted-color)">Complexity</div>
              <div style="font-weight: 600">{{ effectiveResult.complexityEnabled ? 'Yes' : 'No' }}</div>
            </div>
            <div>
              <div style="color: var(--p-text-muted-color)">Max Age (days)</div>
              <div style="font-weight: 600">{{ effectiveResult.maxPasswordAgeDays }}</div>
            </div>
            <div>
              <div style="color: var(--p-text-muted-color)">Min Age (days)</div>
              <div style="font-weight: 600">{{ effectiveResult.minPasswordAgeDays }}</div>
            </div>
            <div>
              <div style="color: var(--p-text-muted-color)">Lockout Threshold</div>
              <div style="font-weight: 600">{{ effectiveResult.lockoutThreshold || 'Off' }}</div>
            </div>
            <div>
              <div style="color: var(--p-text-muted-color)">Lockout Duration (min)</div>
              <div style="font-weight: 600">{{ effectiveResult.lockoutDurationMinutes }}</div>
            </div>
            <div>
              <div style="color: var(--p-text-muted-color)">Observation Window (min)</div>
              <div style="font-weight: 600">{{ effectiveResult.lockoutObservationWindowMinutes }}</div>
            </div>
          </div>
        </div>
      </div>
      <template #footer>
        <Button label="Close" severity="secondary" @click="showEffectiveDialog = false" />
      </template>
    </Dialog>

    <!-- Password Filters Section -->
    <div style="margin-top: 2rem">
      <div class="page-header">
        <h1>Password Filters</h1>
        <p>Pluggable password validation filters applied during password changes</p>
      </div>

      <div v-if="loadingFilters" style="text-align: center; padding: 2rem">
        <ProgressSpinner />
      </div>
      <div v-else>
        <div class="card" style="padding: 0; margin-bottom: 1.5rem">
          <DataTable :value="filters" stripedRows size="small" dataKey="name">
            <template #header>
              <div style="font-weight: 600; padding: 0.25rem">Registered Password Filters</div>
            </template>
            <Column header="Order" sortable sortField="order" style="width: 80px">
              <template #body="{ data }">
                <span style="font-family: monospace; color: var(--p-text-muted-color)">{{ data.order }}</span>
              </template>
            </Column>
            <Column header="Name" sortable sortField="name" style="min-width: 200px">
              <template #body="{ data }">
                <div style="font-weight: 600">{{ data.name }}</div>
              </template>
            </Column>
            <Column header="Description" style="min-width: 300px">
              <template #body="{ data }">
                <span style="font-size: 0.8125rem; color: var(--p-text-muted-color)">{{ data.description }}</span>
              </template>
            </Column>
            <Column header="Enabled" style="width: 100px">
              <template #body="{ data }">
                <ToggleSwitch :modelValue="data.isEnabled" @update:modelValue="toggleFilter(data)" />
              </template>
            </Column>
            <template #empty>
              <div style="text-align: center; padding: 2rem; color: var(--p-text-muted-color)">No password filters registered</div>
            </template>
          </DataTable>
        </div>

        <!-- Password test tool -->
        <div class="card">
          <div class="card-title">Test Password Against Filters</div>
          <div style="display: flex; gap: 0.5rem; margin-bottom: 1rem; flex-wrap: wrap">
            <InputText v-model="testPasswordInput" placeholder="Enter password to test..." style="flex: 1; min-width: 200px" type="password" />
            <InputText v-model="testDnInput" placeholder="User DN (optional)" style="flex: 1; min-width: 200px" />
            <Button label="Test" icon="pi pi-check-circle" @click="runPasswordTest" :loading="testingPassword" />
          </div>

          <div v-if="testResult">
            <div style="display: flex; align-items: center; gap: 0.5rem; margin-bottom: 1rem">
              <Tag :value="testResult.isValid ? 'PASS' : 'FAIL'" :severity="testResult.isValid ? 'success' : 'danger'" />
              <span style="font-size: 0.875rem">{{ testResult.message }}</span>
            </div>

            <DataTable :value="testResult.filterResults" size="small" stripedRows>
              <Column field="filterName" header="Filter" style="min-width: 200px" />
              <Column header="Result" style="width: 100px">
                <template #body="{ data }">
                  <Tag :value="data.isValid ? 'Pass' : 'Fail'" :severity="data.isValid ? 'success' : 'danger'" />
                </template>
              </Column>
              <Column field="message" header="Message" style="min-width: 300px">
                <template #body="{ data }">
                  <span style="font-size: 0.8125rem" :style="data.isValid ? 'color: var(--p-text-muted-color)' : 'color: var(--app-danger-text)'">
                    {{ data.message }}
                  </span>
                </template>
              </Column>
            </DataTable>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>
