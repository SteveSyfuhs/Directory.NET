<script setup lang="ts">
import { ref, onMounted } from 'vue'
import InputText from 'primevue/inputtext'
import InputNumber from 'primevue/inputnumber'
import Button from 'primevue/button'
import Select from 'primevue/select'
import Dialog from 'primevue/dialog'
import Tag from 'primevue/tag'
import ProgressSpinner from 'primevue/progressspinner'
import { useToast } from 'primevue/usetoast'
import { fetchDomainConfig, fetchPasswordPolicy, updatePasswordPolicy, fetchFunctionalLevel, raiseDomainLevel, raiseForestLevel, fetchUpnSuffixes, addUpnSuffix, deleteUpnSuffix } from '../api/domain'
import type { DomainConfig, PasswordPolicy } from '../api/types'
import { functionalLevelName } from '../utils/format'

const toast = useToast()
const config = ref<DomainConfig | null>(null)
const policy = ref<PasswordPolicy | null>(null)
const functionalLevel = ref<any>(null)
const upnData = ref<any>(null)
const loading = ref(true)
const saving = ref(false)

// Functional Level
const showRaiseDomainDialog = ref(false)
const showRaiseForestDialog = ref(false)
const selectedDomainLevel = ref<number | null>(null)
const selectedForestLevel = ref<number | null>(null)
const raising = ref(false)

// UPN Suffixes
const newSuffix = ref('')
const addingSuffix = ref(false)

onMounted(async () => {
  try {
    const [c, p, fl, upn] = await Promise.all([
      fetchDomainConfig(),
      fetchPasswordPolicy(),
      fetchFunctionalLevel(),
      fetchUpnSuffixes(),
    ])
    config.value = c
    policy.value = p
    functionalLevel.value = fl
    upnData.value = upn
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    loading.value = false
  }
})

async function savePasswordPolicy() {
  if (!policy.value) return
  saving.value = true
  try {
    const updated = await updatePasswordPolicy(policy.value)
    policy.value = updated
    toast.add({ severity: 'success', summary: 'Saved', detail: 'Password policy updated successfully', life: 3000 })
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    saving.value = false
  }
}

function openRaiseDomain() {
  selectedDomainLevel.value = null
  showRaiseDomainDialog.value = true
}

function openRaiseForest() {
  selectedForestLevel.value = null
  showRaiseForestDialog.value = true
}

async function submitRaiseDomain() {
  if (selectedDomainLevel.value === null) {
    toast.add({ severity: 'warn', summary: 'Validation', detail: 'Select a target level', life: 3000 })
    return
  }
  if (!confirm('Raising the domain functional level is IRREVERSIBLE. Are you sure?')) return
  raising.value = true
  try {
    const result = await raiseDomainLevel(selectedDomainLevel.value)
    toast.add({ severity: 'success', summary: 'Success', detail: result.message, life: 5000 })
    showRaiseDomainDialog.value = false
    functionalLevel.value = await fetchFunctionalLevel()
    config.value = await fetchDomainConfig()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    raising.value = false
  }
}

async function submitRaiseForest() {
  if (selectedForestLevel.value === null) {
    toast.add({ severity: 'warn', summary: 'Validation', detail: 'Select a target level', life: 3000 })
    return
  }
  if (!confirm('Raising the forest functional level is IRREVERSIBLE. Are you sure?')) return
  raising.value = true
  try {
    const result = await raiseForestLevel(selectedForestLevel.value)
    toast.add({ severity: 'success', summary: 'Success', detail: result.message, life: 5000 })
    showRaiseForestDialog.value = false
    functionalLevel.value = await fetchFunctionalLevel()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    raising.value = false
  }
}

async function onAddSuffix() {
  if (!newSuffix.value.trim()) {
    toast.add({ severity: 'warn', summary: 'Validation', detail: 'Suffix is required', life: 3000 })
    return
  }
  addingSuffix.value = true
  try {
    await addUpnSuffix(newSuffix.value.trim())
    toast.add({ severity: 'success', summary: 'Added', detail: `UPN suffix '${newSuffix.value}' added`, life: 3000 })
    newSuffix.value = ''
    upnData.value = await fetchUpnSuffixes()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    addingSuffix.value = false
  }
}

async function onDeleteSuffix(suffix: string) {
  if (!confirm(`Remove UPN suffix '${suffix}'?`)) return
  try {
    await deleteUpnSuffix(suffix)
    toast.add({ severity: 'success', summary: 'Removed', detail: `UPN suffix '${suffix}' removed`, life: 3000 })
    upnData.value = await fetchUpnSuffixes()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}
</script>

<template>
  <div>
    <div class="page-header">
      <h1>Domain Configuration</h1>
      <p>View domain settings, functional levels, password policies, and UPN suffixes</p>
    </div>

    <div v-if="loading" style="text-align: center; padding: 4rem">
      <ProgressSpinner />
    </div>

    <template v-else>
      <!-- Domain Info -->
      <div v-if="config" class="card" style="margin-bottom: 1.5rem">
        <div class="card-title">Domain Information</div>
        <div class="config-grid">
          <div class="config-row">
            <label>Domain Name</label>
            <InputText :modelValue="config.domainName" disabled class="config-input" />
          </div>
          <div class="config-row">
            <label>NetBIOS Name</label>
            <InputText :modelValue="config.netBiosName" disabled class="config-input" />
          </div>
          <div class="config-row">
            <label>Domain DN</label>
            <InputText :modelValue="config.domainDn" disabled class="config-input" />
          </div>
          <div class="config-row">
            <label>Domain SID</label>
            <InputText :modelValue="config.domainSid" disabled class="config-input" />
          </div>
          <div class="config-row">
            <label>Forest Name</label>
            <InputText :modelValue="config.forestName" disabled class="config-input" />
          </div>
          <div class="config-row">
            <label>Kerberos Realm</label>
            <InputText :modelValue="config.kerberosRealm" disabled class="config-input" />
          </div>
          <div class="config-row">
            <label>Functional Level</label>
            <InputText :modelValue="functionalLevelName(config.functionalLevel)" disabled class="config-input" />
          </div>
        </div>
      </div>

      <!-- Functional Level Management -->
      <div v-if="functionalLevel" class="card" style="margin-bottom: 1.5rem">
        <div class="card-title">Functional Level Management</div>
        <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 1.5rem">
          <div>
            <div style="font-size: 0.875rem; color: var(--p-text-muted-color); margin-bottom: 0.25rem">Domain Functional Level</div>
            <div style="display: flex; align-items: center; gap: 0.75rem; margin-bottom: 0.75rem">
              <Tag :value="functionalLevelName(functionalLevel.domainLevel)" severity="info" style="font-size: 0.875rem" />
            </div>
            <Button label="Raise Domain Level" icon="pi pi-arrow-up" severity="warn" size="small"
                    :disabled="!functionalLevel.possibleDomainLevels?.length"
                    @click="openRaiseDomain" />
            <p v-if="!functionalLevel.possibleDomainLevels?.length" style="font-size: 0.8125rem; color: var(--p-text-muted-color); margin-top: 0.5rem">
              Already at the highest available level.
            </p>
          </div>
          <div>
            <div style="font-size: 0.875rem; color: var(--p-text-muted-color); margin-bottom: 0.25rem">Forest Functional Level</div>
            <div style="display: flex; align-items: center; gap: 0.75rem; margin-bottom: 0.75rem">
              <Tag :value="functionalLevelName(functionalLevel.forestLevel)" severity="info" style="font-size: 0.875rem" />
            </div>
            <Button label="Raise Forest Level" icon="pi pi-arrow-up" severity="warn" size="small"
                    :disabled="!functionalLevel.possibleForestLevels?.length"
                    @click="openRaiseForest" />
            <p v-if="!functionalLevel.possibleForestLevels?.length" style="font-size: 0.8125rem; color: var(--p-text-muted-color); margin-top: 0.5rem">
              Already at the highest available level.
            </p>
          </div>
        </div>
        <div style="margin-top: 1rem; padding: 0.75rem; background: var(--app-warn-bg); border: 1px solid var(--app-warn-border); border-radius: 0.5rem; font-size: 0.8125rem; color: var(--app-warn-text-strong)">
          <i class="pi pi-exclamation-triangle" style="margin-right: 0.375rem"></i>
          Raising functional levels is an irreversible operation. Ensure all domain controllers meet the target level requirements.
        </div>
      </div>

      <!-- UPN Suffixes -->
      <div v-if="upnData" class="card" style="margin-bottom: 1.5rem">
        <div class="card-title">UPN Suffixes</div>
        <div style="margin-bottom: 1rem">
          <div style="display: flex; align-items: center; gap: 0.5rem; margin-bottom: 0.75rem; padding: 0.5rem; background: var(--p-surface-ground); border-radius: 0.375rem">
            <i class="pi pi-lock" style="color: var(--p-text-muted-color)"></i>
            <span style="font-size: 0.875rem; font-weight: 600">{{ upnData.defaultSuffix }}</span>
            <Tag value="Default" severity="secondary" style="margin-left: 0.25rem" />
          </div>
          <div v-for="suffix in upnData.alternativeSuffixes" :key="suffix"
               style="display: flex; align-items: center; gap: 0.5rem; margin-bottom: 0.5rem; padding: 0.5rem; border: 1px solid var(--p-surface-border); border-radius: 0.375rem">
            <i class="pi pi-at" style="color: var(--app-accent-color)"></i>
            <span style="font-size: 0.875rem; flex: 1">{{ suffix }}</span>
            <Button icon="pi pi-trash" severity="danger" text size="small" @click="onDeleteSuffix(suffix)" title="Remove" />
          </div>
          <div v-if="!upnData.alternativeSuffixes?.length" style="font-size: 0.8125rem; color: var(--p-text-muted-color); margin-bottom: 0.75rem">
            No alternative UPN suffixes configured.
          </div>
        </div>
        <div style="display: flex; gap: 0.5rem">
          <InputText v-model="newSuffix" placeholder="e.g. subsidiary.com" style="flex: 1" :disabled="addingSuffix" />
          <Button label="Add Suffix" icon="pi pi-plus" @click="onAddSuffix" :loading="addingSuffix" />
        </div>
      </div>

      <!-- Password Policy -->
      <div v-if="policy" class="card">
        <div class="card-title">Default Password Policy</div>
        <div class="config-grid">
          <div class="config-row">
            <label>Minimum Password Length</label>
            <InputNumber v-model="policy.minPwdLength" :min="0" :max="255" class="config-input" />
          </div>
          <div class="config-row">
            <label>Password History Length</label>
            <InputNumber v-model="policy.pwdHistoryLength" :min="0" :max="24" class="config-input" />
          </div>
          <div class="config-row">
            <label>Maximum Password Age (days)</label>
            <InputNumber v-model="policy.maxPwdAge" class="config-input" />
          </div>
          <div class="config-row">
            <label>Minimum Password Age (days)</label>
            <InputNumber v-model="policy.minPwdAge" class="config-input" />
          </div>
          <div class="config-row">
            <label>Account Lockout Threshold</label>
            <InputNumber v-model="policy.lockoutThreshold" :min="0" class="config-input" />
          </div>
          <div class="config-row">
            <label>Lockout Duration (minutes)</label>
            <InputNumber v-model="policy.lockoutDuration" :min="0" class="config-input" />
          </div>
          <div class="config-row">
            <label>Lockout Observation Window (minutes)</label>
            <InputNumber v-model="policy.lockoutObservationWindow" :min="0" class="config-input" />
          </div>
        </div>
        <div style="margin-top: 1.5rem; display: flex; justify-content: flex-end">
          <Button label="Save Password Policy" icon="pi pi-save" @click="savePasswordPolicy" :loading="saving" />
        </div>
      </div>
    </template>

    <!-- Raise Domain Level Dialog -->
    <Dialog v-model:visible="showRaiseDomainDialog" header="Raise Domain Functional Level" modal
            :style="{ width: '28rem' }" :closable="!raising">
      <div style="padding-top: 0.5rem">
        <div style="margin-bottom: 1rem; padding: 0.75rem; background: var(--app-danger-bg); border: 1px solid var(--app-danger-border); border-radius: 0.5rem; font-size: 0.8125rem; color: var(--app-danger-text-strong)">
          <i class="pi pi-exclamation-triangle" style="margin-right: 0.375rem"></i>
          <strong>Warning:</strong> This operation cannot be reversed.
        </div>
        <label style="display: block; font-weight: 600; margin-bottom: 0.25rem; font-size: 0.875rem">Target Level</label>
        <Select v-model="selectedDomainLevel"
                :options="functionalLevel?.possibleDomainLevels || []"
                optionLabel="name" optionValue="level"
                placeholder="Select a level" style="width: 100%" :disabled="raising" />
      </div>
      <template #footer>
        <Button label="Cancel" severity="secondary" @click="showRaiseDomainDialog = false" :disabled="raising" />
        <Button label="Raise Level" icon="pi pi-arrow-up" severity="warn" @click="submitRaiseDomain" :loading="raising" />
      </template>
    </Dialog>

    <!-- Raise Forest Level Dialog -->
    <Dialog v-model:visible="showRaiseForestDialog" header="Raise Forest Functional Level" modal
            :style="{ width: '28rem' }" :closable="!raising">
      <div style="padding-top: 0.5rem">
        <div style="margin-bottom: 1rem; padding: 0.75rem; background: var(--app-danger-bg); border: 1px solid var(--app-danger-border); border-radius: 0.5rem; font-size: 0.8125rem; color: var(--app-danger-text-strong)">
          <i class="pi pi-exclamation-triangle" style="margin-right: 0.375rem"></i>
          <strong>Warning:</strong> This operation cannot be reversed.
        </div>
        <label style="display: block; font-weight: 600; margin-bottom: 0.25rem; font-size: 0.875rem">Target Level</label>
        <Select v-model="selectedForestLevel"
                :options="functionalLevel?.possibleForestLevels || []"
                optionLabel="name" optionValue="level"
                placeholder="Select a level" style="width: 100%" :disabled="raising" />
      </div>
      <template #footer>
        <Button label="Cancel" severity="secondary" @click="showRaiseForestDialog = false" :disabled="raising" />
        <Button label="Raise Level" icon="pi pi-arrow-up" severity="warn" @click="submitRaiseForest" :loading="raising" />
      </template>
    </Dialog>
  </div>
</template>

<style scoped>
.config-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 1rem;
}

@media (max-width: 768px) {
  .config-grid {
    grid-template-columns: 1fr;
  }
}

.config-row {
  display: flex;
  flex-direction: column;
  gap: 0.375rem;
}

.config-row label {
  font-size: 0.8125rem;
  font-weight: 600;
  color: var(--p-text-muted-color);
}

.config-input {
  width: 100%;
}
</style>
