<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import InputNumber from 'primevue/inputnumber'
import Textarea from 'primevue/textarea'
import Tag from 'primevue/tag'
import Dialog from 'primevue/dialog'
import Checkbox from 'primevue/checkbox'
import Select from 'primevue/select'
import Tabs from 'primevue/tabs'
import TabList from 'primevue/tablist'
import Tab from 'primevue/tab'
import TabPanels from 'primevue/tabpanels'
import TabPanel from 'primevue/tabpanel'
import Chip from 'primevue/chip'
import { useToast } from 'primevue/usetoast'
import {
  listPolicies,
  createPolicy,
  updatePolicy,
  deletePolicy,
  evaluateAccess,
  getSignInLog,
} from '../api/conditionalAccess'
import type {
  ConditionalAccessPolicy,
  PolicyConditions,
  PolicyActions,
  AccessEvaluationResult,
  SignInLogEntry,
  RiskLevel,
} from '../types/conditionalAccess'

const toast = useToast()

// State
const policies = ref<ConditionalAccessPolicy[]>([])
const signInLog = ref<SignInLogEntry[]>([])
const loading = ref(true)
const filterText = ref('')
const activeTab = ref('0')

// Policy editor
const editVisible = ref(false)
const editSaving = ref(false)
const isNew = ref(false)
const editForm = ref(emptyPolicy())

// Delete
const deleteVisible = ref(false)
const deleteTarget = ref<ConditionalAccessPolicy | null>(null)
const deleting = ref(false)

// Evaluate test panel
const evalRequest = ref({
  userDn: '',
  clientIp: '',
  applicationId: '',
  devicePlatform: '',
  riskLevel: '' as string,
})
const evalResult = ref<AccessEvaluationResult | null>(null)
const evaluating = ref(false)

// Temp input fields for multi-value
const tempIncludeUser = ref('')
const tempExcludeUser = ref('')
const tempIncludeGroup = ref('')
const tempExcludeGroup = ref('')
const tempApp = ref('')
const tempIpRange = ref('')
const tempCountry = ref('')
const tempPlatform = ref('')

const riskLevels = ['Low', 'Medium', 'High']
const mfaMethods = ['totp', 'fido2']
const devicePlatforms = ['Windows', 'macOS', 'Linux', 'iOS', 'Android']

const filteredPolicies = computed(() => {
  if (!filterText.value) return policies.value
  const q = filterText.value.toLowerCase()
  return policies.value.filter(p =>
    p.name.toLowerCase().includes(q) || p.description.toLowerCase().includes(q)
  )
})

function emptyPolicy(): ConditionalAccessPolicy {
  return {
    id: '',
    name: '',
    description: '',
    isEnabled: true,
    priority: 100,
    conditions: {
      includeUsers: [],
      excludeUsers: [],
      includeGroups: [],
      excludeGroups: [],
      includeApplications: [],
      ipRanges: [],
      countries: [],
      minRiskLevel: null,
      devicePlatforms: [],
    },
    actions: {
      requireMfa: false,
      allowedMfaMethods: ['totp', 'fido2'],
      blockAccess: false,
      requirePasswordChange: false,
      sessionLifetimeMinutes: null,
    },
    createdAt: '',
    modifiedAt: '',
  }
}

onMounted(async () => {
  await loadData()
})

async function loadData() {
  loading.value = true
  try {
    const [p, l] = await Promise.all([listPolicies(), getSignInLog()])
    policies.value = p
    signInLog.value = l
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Load Failed', detail: e.message, life: 5000 })
  } finally {
    loading.value = false
  }
}

function openCreate() {
  isNew.value = true
  editForm.value = emptyPolicy()
  editVisible.value = true
}

function openEdit(policy: ConditionalAccessPolicy) {
  isNew.value = false
  editForm.value = JSON.parse(JSON.stringify(policy))
  editVisible.value = true
}

async function savePolicy() {
  editSaving.value = true
  try {
    if (isNew.value) {
      await createPolicy(editForm.value)
      toast.add({ severity: 'success', summary: 'Created', detail: 'Policy created.', life: 3000 })
    } else {
      await updatePolicy(editForm.value.id, editForm.value)
      toast.add({ severity: 'success', summary: 'Updated', detail: 'Policy updated.', life: 3000 })
    }
    editVisible.value = false
    await loadData()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    editSaving.value = false
  }
}

function openDelete(policy: ConditionalAccessPolicy) {
  deleteTarget.value = policy
  deleteVisible.value = true
}

async function confirmDelete() {
  if (!deleteTarget.value) return
  deleting.value = true
  try {
    await deletePolicy(deleteTarget.value.id)
    toast.add({ severity: 'success', summary: 'Deleted', detail: 'Policy deleted.', life: 3000 })
    deleteVisible.value = false
    await loadData()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    deleting.value = false
  }
}

async function doEvaluate() {
  evaluating.value = true
  evalResult.value = null
  try {
    const req: any = { userDn: evalRequest.value.userDn }
    if (evalRequest.value.clientIp) req.clientIp = evalRequest.value.clientIp
    if (evalRequest.value.applicationId) req.applicationId = evalRequest.value.applicationId
    if (evalRequest.value.devicePlatform) req.device = { platform: evalRequest.value.devicePlatform }
    if (evalRequest.value.riskLevel) req.riskLevel = evalRequest.value.riskLevel
    evalResult.value = await evaluateAccess(req)
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Evaluation Failed', detail: e.message, life: 5000 })
  } finally {
    evaluating.value = false
  }
}

function addToList(list: string[], value: string) {
  const val = value.trim()
  if (val && !list.includes(val)) {
    list.push(val)
  }
}

function removeFromList(list: string[], index: number) {
  list.splice(index, 1)
}

function conditionsSummary(p: ConditionalAccessPolicy): string {
  const parts: string[] = []
  if (p.conditions.includeUsers.length > 0)
    parts.push(`Users: ${p.conditions.includeUsers.join(', ')}`)
  if (p.conditions.includeGroups.length > 0)
    parts.push(`Groups: ${p.conditions.includeGroups.length}`)
  if (p.conditions.ipRanges.length > 0)
    parts.push(`IPs: ${p.conditions.ipRanges.length}`)
  if (p.conditions.minRiskLevel)
    parts.push(`Risk >= ${p.conditions.minRiskLevel}`)
  return parts.length > 0 ? parts.join(' | ') : 'None'
}

function actionsSummary(p: ConditionalAccessPolicy): string {
  const parts: string[] = []
  if (p.actions.blockAccess) parts.push('Block')
  if (p.actions.requireMfa) parts.push('Require MFA')
  if (p.actions.requirePasswordChange) parts.push('Password Change')
  if (p.actions.sessionLifetimeMinutes) parts.push(`Session: ${p.actions.sessionLifetimeMinutes}m`)
  return parts.length > 0 ? parts.join(', ') : 'None'
}

function formatDate(d: string | null) {
  if (!d) return '-'
  return new Date(d).toLocaleString()
}
</script>

<template>
  <div>
    <div class="page-header">
      <h1><i class="pi pi-shield" style="margin-right: 0.5rem;"></i>Conditional Access</h1>
      <p>Manage risk-based and conditional MFA policies for sign-in protection.</p>
    </div>

    <Tabs :value="activeTab">
      <TabList>
        <Tab value="0">Policies</Tab>
        <Tab value="1">Sign-In Log</Tab>
        <Tab value="2">Test / Evaluate</Tab>
      </TabList>
      <TabPanels>
        <!-- Policies Tab -->
        <TabPanel value="0">
          <div class="toolbar" style="margin-top: 1rem;">
            <InputText v-model="filterText" placeholder="Filter policies..." style="min-width: 250px;" />
            <span style="flex: 1;"></span>
            <Button label="Refresh" icon="pi pi-refresh" severity="secondary" outlined @click="loadData" />
            <Button label="New Policy" icon="pi pi-plus" @click="openCreate" />
          </div>

          <DataTable :value="filteredPolicies" :loading="loading" stripedRows style="margin-top: 0.5rem;">
            <template #empty>No conditional access policies defined.</template>
            <Column field="priority" header="Priority" sortable style="width: 80px;" />
            <Column field="name" header="Name" sortable />
            <Column field="isEnabled" header="Status" style="width: 100px;">
              <template #body="{ data }">
                <Tag :value="data.isEnabled ? 'Enabled' : 'Disabled'" :severity="data.isEnabled ? 'success' : 'secondary'" />
              </template>
            </Column>
            <Column header="Conditions">
              <template #body="{ data }">
                <span style="font-size: 0.8125rem; color: var(--p-text-muted-color);">{{ conditionsSummary(data) }}</span>
              </template>
            </Column>
            <Column header="Actions">
              <template #body="{ data }">
                <span style="font-size: 0.8125rem;">{{ actionsSummary(data) }}</span>
              </template>
            </Column>
            <Column header="" style="width: 120px;">
              <template #body="{ data }">
                <div style="display: flex; gap: 0.25rem;">
                  <Button icon="pi pi-pencil" text rounded size="small" @click="openEdit(data)" v-tooltip="'Edit'" />
                  <Button icon="pi pi-trash" text rounded size="small" severity="danger" @click="openDelete(data)" v-tooltip="'Delete'" />
                </div>
              </template>
            </Column>
          </DataTable>
        </TabPanel>

        <!-- Sign-In Log Tab -->
        <TabPanel value="1">
          <div class="toolbar" style="margin-top: 1rem;">
            <span style="flex: 1;"></span>
            <Button label="Refresh" icon="pi pi-refresh" severity="secondary" outlined @click="loadData" />
          </div>
          <DataTable :value="signInLog" stripedRows style="margin-top: 0.5rem;">
            <template #empty>No sign-in evaluations recorded.</template>
            <Column field="timestamp" header="Time" sortable>
              <template #body="{ data }">{{ formatDate(data.timestamp) }}</template>
            </Column>
            <Column field="userDn" header="User" />
            <Column field="clientIp" header="IP" />
            <Column field="devicePlatform" header="Platform" />
            <Column header="Result">
              <template #body="{ data }">
                <Tag
                  :value="data.result.accessGranted ? (data.result.mfaRequired ? 'MFA Required' : 'Granted') : 'Blocked'"
                  :severity="data.result.accessGranted ? (data.result.mfaRequired ? 'warn' : 'success') : 'danger'"
                />
              </template>
            </Column>
            <Column header="Policies Matched">
              <template #body="{ data }">
                {{ data.result.evaluatedPolicies.filter((p: any) => p.matched).length }} /
                {{ data.result.evaluatedPolicies.length }}
              </template>
            </Column>
          </DataTable>
        </TabPanel>

        <!-- Test / Evaluate Tab -->
        <TabPanel value="2">
          <div class="card" style="margin-top: 1rem;">
            <div class="card-title">Test Access Evaluation</div>
            <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 1rem;">
              <div>
                <label style="font-size: 0.8125rem; font-weight: 600;">User DN *</label>
                <InputText v-model="evalRequest.userDn" placeholder="CN=user,DC=..." style="width: 100%; margin-top: 0.25rem;" />
              </div>
              <div>
                <label style="font-size: 0.8125rem; font-weight: 600;">Client IP</label>
                <InputText v-model="evalRequest.clientIp" placeholder="192.168.1.1" style="width: 100%; margin-top: 0.25rem;" />
              </div>
              <div>
                <label style="font-size: 0.8125rem; font-weight: 600;">Application ID</label>
                <InputText v-model="evalRequest.applicationId" placeholder="app-client-id" style="width: 100%; margin-top: 0.25rem;" />
              </div>
              <div>
                <label style="font-size: 0.8125rem; font-weight: 600;">Device Platform</label>
                <Select v-model="evalRequest.devicePlatform" :options="['', ...devicePlatforms]" placeholder="Any" style="width: 100%; margin-top: 0.25rem;" />
              </div>
              <div>
                <label style="font-size: 0.8125rem; font-weight: 600;">Risk Level</label>
                <Select v-model="evalRequest.riskLevel" :options="['', ...riskLevels]" placeholder="None" style="width: 100%; margin-top: 0.25rem;" />
              </div>
            </div>
            <div style="margin-top: 1rem;">
              <Button label="Evaluate" icon="pi pi-play" :loading="evaluating" @click="doEvaluate" :disabled="!evalRequest.userDn" />
            </div>

            <div v-if="evalResult" class="eval-result" style="margin-top: 1.5rem;">
              <div class="card-title">Result</div>
              <div class="stat-grid">
                <div class="stat-card">
                  <div class="stat-icon" :class="evalResult.accessGranted ? 'green' : 'amber'">
                    <i :class="evalResult.accessGranted ? 'pi pi-check' : 'pi pi-ban'" />
                  </div>
                  <div>
                    <div class="stat-value">{{ evalResult.accessGranted ? 'Granted' : 'Blocked' }}</div>
                    <div class="stat-label">Access Decision</div>
                  </div>
                </div>
                <div class="stat-card">
                  <div class="stat-icon" :class="evalResult.mfaRequired ? 'amber' : 'green'">
                    <i class="pi pi-shield" />
                  </div>
                  <div>
                    <div class="stat-value">{{ evalResult.mfaRequired ? 'Yes' : 'No' }}</div>
                    <div class="stat-label">MFA Required</div>
                  </div>
                </div>
              </div>
              <div v-if="evalResult.blockReason" style="color: var(--app-danger-text); margin-bottom: 1rem;">
                Block reason: {{ evalResult.blockReason }}
              </div>
              <DataTable :value="evalResult.evaluatedPolicies" size="small">
                <Column field="policyName" header="Policy" />
                <Column field="matched" header="Matched">
                  <template #body="{ data }">
                    <Tag :value="data.matched ? 'Yes' : 'No'" :severity="data.matched ? 'warn' : 'secondary'" />
                  </template>
                </Column>
                <Column field="reason" header="Reason" />
              </DataTable>
            </div>
          </div>
        </TabPanel>
      </TabPanels>
    </Tabs>

    <!-- Policy Edit Dialog -->
    <Dialog
      v-model:visible="editVisible"
      :header="isNew ? 'Create Policy' : 'Edit Policy'"
      :modal="true"
      style="width: 720px;"
    >
      <div style="display: flex; flex-direction: column; gap: 1rem;">
        <div style="display: grid; grid-template-columns: 2fr 1fr 1fr; gap: 1rem;">
          <div>
            <label style="font-size: 0.8125rem; font-weight: 600;">Name *</label>
            <InputText v-model="editForm.name" style="width: 100%; margin-top: 0.25rem;" />
          </div>
          <div>
            <label style="font-size: 0.8125rem; font-weight: 600;">Priority</label>
            <InputNumber v-model="editForm.priority" style="width: 100%; margin-top: 0.25rem;" />
          </div>
          <div style="display: flex; align-items: flex-end; gap: 0.5rem;">
            <Checkbox v-model="editForm.isEnabled" :binary="true" inputId="pol-enabled" />
            <label for="pol-enabled">Enabled</label>
          </div>
        </div>

        <div>
          <label style="font-size: 0.8125rem; font-weight: 600;">Description</label>
          <Textarea v-model="editForm.description" rows="2" style="width: 100%; margin-top: 0.25rem;" />
        </div>

        <!-- Conditions -->
        <div style="border: 1px solid var(--p-surface-border); border-radius: 0.5rem; padding: 1rem;">
          <div class="card-title">Conditions</div>
          <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 1rem;">
            <!-- Include Users -->
            <div>
              <label style="font-size: 0.8125rem; font-weight: 600;">Include Users</label>
              <div style="display: flex; gap: 0.25rem; margin-top: 0.25rem;">
                <InputText v-model="tempIncludeUser" placeholder="DN or 'All'" style="flex: 1;" @keyup.enter="addToList(editForm.conditions.includeUsers, tempIncludeUser); tempIncludeUser = ''" />
                <Button icon="pi pi-plus" size="small" @click="addToList(editForm.conditions.includeUsers, tempIncludeUser); tempIncludeUser = ''" />
              </div>
              <div style="margin-top: 0.25rem; display: flex; flex-wrap: wrap; gap: 0.25rem;">
                <Chip v-for="(u, i) in editForm.conditions.includeUsers" :key="i" :label="u" removable @remove="removeFromList(editForm.conditions.includeUsers, i)" />
              </div>
            </div>
            <!-- Exclude Users -->
            <div>
              <label style="font-size: 0.8125rem; font-weight: 600;">Exclude Users</label>
              <div style="display: flex; gap: 0.25rem; margin-top: 0.25rem;">
                <InputText v-model="tempExcludeUser" placeholder="DN" style="flex: 1;" @keyup.enter="addToList(editForm.conditions.excludeUsers, tempExcludeUser); tempExcludeUser = ''" />
                <Button icon="pi pi-plus" size="small" @click="addToList(editForm.conditions.excludeUsers, tempExcludeUser); tempExcludeUser = ''" />
              </div>
              <div style="margin-top: 0.25rem; display: flex; flex-wrap: wrap; gap: 0.25rem;">
                <Chip v-for="(u, i) in editForm.conditions.excludeUsers" :key="i" :label="u" removable @remove="removeFromList(editForm.conditions.excludeUsers, i)" />
              </div>
            </div>
            <!-- Include Groups -->
            <div>
              <label style="font-size: 0.8125rem; font-weight: 600;">Include Groups</label>
              <div style="display: flex; gap: 0.25rem; margin-top: 0.25rem;">
                <InputText v-model="tempIncludeGroup" placeholder="Group DN" style="flex: 1;" @keyup.enter="addToList(editForm.conditions.includeGroups, tempIncludeGroup); tempIncludeGroup = ''" />
                <Button icon="pi pi-plus" size="small" @click="addToList(editForm.conditions.includeGroups, tempIncludeGroup); tempIncludeGroup = ''" />
              </div>
              <div style="margin-top: 0.25rem; display: flex; flex-wrap: wrap; gap: 0.25rem;">
                <Chip v-for="(g, i) in editForm.conditions.includeGroups" :key="i" :label="g" removable @remove="removeFromList(editForm.conditions.includeGroups, i)" />
              </div>
            </div>
            <!-- IP Ranges -->
            <div>
              <label style="font-size: 0.8125rem; font-weight: 600;">IP Ranges (CIDR)</label>
              <div style="display: flex; gap: 0.25rem; margin-top: 0.25rem;">
                <InputText v-model="tempIpRange" placeholder="10.0.0.0/8" style="flex: 1;" @keyup.enter="addToList(editForm.conditions.ipRanges, tempIpRange); tempIpRange = ''" />
                <Button icon="pi pi-plus" size="small" @click="addToList(editForm.conditions.ipRanges, tempIpRange); tempIpRange = ''" />
              </div>
              <div style="margin-top: 0.25rem; display: flex; flex-wrap: wrap; gap: 0.25rem;">
                <Chip v-for="(r, i) in editForm.conditions.ipRanges" :key="i" :label="r" removable @remove="removeFromList(editForm.conditions.ipRanges, i)" />
              </div>
            </div>
            <!-- Risk Level -->
            <div>
              <label style="font-size: 0.8125rem; font-weight: 600;">Min Risk Level</label>
              <Select v-model="editForm.conditions.minRiskLevel" :options="[null, ...riskLevels]" placeholder="None" style="width: 100%; margin-top: 0.25rem;" />
            </div>
            <!-- Device Platforms -->
            <div>
              <label style="font-size: 0.8125rem; font-weight: 600;">Device Platforms</label>
              <div style="display: flex; gap: 0.25rem; margin-top: 0.25rem;">
                <Select v-model="tempPlatform" :options="devicePlatforms" placeholder="Select..." style="flex: 1;" />
                <Button icon="pi pi-plus" size="small" @click="addToList(editForm.conditions.devicePlatforms, tempPlatform); tempPlatform = ''" />
              </div>
              <div style="margin-top: 0.25rem; display: flex; flex-wrap: wrap; gap: 0.25rem;">
                <Chip v-for="(p, i) in editForm.conditions.devicePlatforms" :key="i" :label="p" removable @remove="removeFromList(editForm.conditions.devicePlatforms, i)" />
              </div>
            </div>
          </div>
        </div>

        <!-- Actions -->
        <div style="border: 1px solid var(--p-surface-border); border-radius: 0.5rem; padding: 1rem;">
          <div class="card-title">Actions</div>
          <div style="display: flex; flex-direction: column; gap: 0.75rem;">
            <div style="display: flex; align-items: center; gap: 0.5rem;">
              <Checkbox v-model="editForm.actions.blockAccess" :binary="true" inputId="act-block" />
              <label for="act-block">Block Access</label>
            </div>
            <div style="display: flex; align-items: center; gap: 0.5rem;">
              <Checkbox v-model="editForm.actions.requireMfa" :binary="true" inputId="act-mfa" />
              <label for="act-mfa">Require MFA</label>
            </div>
            <div v-if="editForm.actions.requireMfa" style="margin-left: 2rem; display: flex; gap: 0.75rem;">
              <div v-for="method in mfaMethods" :key="method" style="display: flex; align-items: center; gap: 0.25rem;">
                <Checkbox
                  :modelValue="editForm.actions.allowedMfaMethods.includes(method)"
                  @update:modelValue="(val: boolean) => { if (val) editForm.actions.allowedMfaMethods.push(method); else editForm.actions.allowedMfaMethods = editForm.actions.allowedMfaMethods.filter(m => m !== method) }"
                  :binary="true"
                  :inputId="'mfa-' + method"
                />
                <label :for="'mfa-' + method">{{ method.toUpperCase() }}</label>
              </div>
            </div>
            <div style="display: flex; align-items: center; gap: 0.5rem;">
              <Checkbox v-model="editForm.actions.requirePasswordChange" :binary="true" inputId="act-pw" />
              <label for="act-pw">Require Password Change</label>
            </div>
            <div style="display: flex; align-items: center; gap: 0.5rem;">
              <label style="font-size: 0.8125rem;">Session Lifetime (minutes):</label>
              <InputNumber v-model="editForm.actions.sessionLifetimeMinutes" :min="1" placeholder="Default" style="width: 120px;" />
            </div>
          </div>
        </div>
      </div>

      <div style="display: flex; gap: 0.5rem; justify-content: flex-end; margin-top: 1rem;">
        <Button label="Cancel" severity="secondary" outlined @click="editVisible = false" />
        <Button :label="isNew ? 'Create' : 'Save'" icon="pi pi-check" :loading="editSaving" @click="savePolicy" :disabled="!editForm.name" />
      </div>
    </Dialog>

    <!-- Delete Confirm -->
    <Dialog v-model:visible="deleteVisible" header="Delete Policy" :modal="true" style="width: 400px;">
      <p>Are you sure you want to delete the policy "{{ deleteTarget?.name }}"?</p>
      <div style="display: flex; gap: 0.5rem; justify-content: flex-end; margin-top: 1rem;">
        <Button label="Cancel" severity="secondary" outlined @click="deleteVisible = false" />
        <Button label="Delete" severity="danger" :loading="deleting" @click="confirmDelete" />
      </div>
    </Dialog>
  </div>
</template>
