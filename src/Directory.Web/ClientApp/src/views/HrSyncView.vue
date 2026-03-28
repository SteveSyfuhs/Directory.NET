<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import InputSwitch from 'primevue/inputswitch'
import Select from 'primevue/select'
import Tag from 'primevue/tag'
import { useToast } from 'primevue/usetoast'
import type { HrSyncConfiguration, HrSyncHistoryEntry, HrSyncPreviewResult } from '../types/hrSync'
import {
  fetchHrSyncConfigurations,
  createHrSyncConfiguration,
  updateHrSyncConfiguration,
  deleteHrSyncConfiguration,
  triggerHrSync,
  fetchHrSyncHistory,
  previewHrSync,
  fetchHrSyncSourceTypes,
} from '../api/hrSync'

const toast = useToast()
const loading = ref(false)
const configurations = ref<HrSyncConfiguration[]>([])
const sourceTypes = ref<string[]>([])

const showEditDialog = ref(false)
const editing = ref<Partial<HrSyncConfiguration>>({})
const isNew = ref(false)
const saving = ref(false)

const showHistoryDialog = ref(false)
const historyConfig = ref<HrSyncConfiguration | null>(null)
const history = ref<HrSyncHistoryEntry[]>([])
const historyLoading = ref(false)

const showPreviewDialog = ref(false)
const previewConfig = ref<HrSyncConfiguration | null>(null)
const previewResult = ref<HrSyncPreviewResult | null>(null)
const previewLoading = ref(false)

const showMappingDialog = ref(false)
const mappingConfig = ref<HrSyncConfiguration | null>(null)
const editingMapping = ref<{ hrField: string; adAttr: string }[]>([])

const sourceTypeOptions = computed(() =>
  sourceTypes.value.map((t) => ({ label: t, value: t }))
)

onMounted(async () => {
  await Promise.all([loadConfigurations(), loadSourceTypes()])
})

async function loadConfigurations() {
  loading.value = true
  try {
    configurations.value = await fetchHrSyncConfigurations()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    loading.value = false
  }
}

async function loadSourceTypes() {
  try {
    sourceTypes.value = await fetchHrSyncSourceTypes()
  } catch {}
}

function openCreate() {
  isNew.value = true
  editing.value = {
    name: '',
    sourceType: 'GenericApi',
    endpointUrl: '',
    apiKey: '',
    targetOu: '',
    autoCreateUsers: true,
    autoDisableOnTermination: true,
    autoMoveOnDepartmentChange: false,
    cronSchedule: '0 */6 * * *',
    isEnabled: false,
    attributeMapping: {},
  }
  showEditDialog.value = true
}

function openEdit(config: HrSyncConfiguration) {
  isNew.value = false
  editing.value = { ...config }
  showEditDialog.value = true
}

async function save() {
  saving.value = true
  try {
    if (isNew.value) {
      const created = await createHrSyncConfiguration(editing.value)
      toast.add({ severity: 'success', summary: 'Created', detail: `Configuration "${created.name}" created`, life: 3000 })
    } else {
      await updateHrSyncConfiguration(editing.value.id!, editing.value)
      toast.add({ severity: 'success', summary: 'Updated', detail: 'Configuration updated', life: 3000 })
    }
    showEditDialog.value = false
    await loadConfigurations()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    saving.value = false
  }
}

async function remove(config: HrSyncConfiguration) {
  try {
    await deleteHrSyncConfiguration(config.id)
    toast.add({ severity: 'success', summary: 'Deleted', detail: `Configuration "${config.name}" deleted`, life: 3000 })
    await loadConfigurations()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

async function syncNow(config: HrSyncConfiguration) {
  try {
    await triggerHrSync(config.id)
    toast.add({ severity: 'info', summary: 'Sync Started', detail: `Sync triggered for "${config.name}"`, life: 3000 })
    // Reload after a short delay to pick up running status
    setTimeout(() => loadConfigurations(), 2000)
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

async function viewHistory(config: HrSyncConfiguration) {
  historyConfig.value = config
  historyLoading.value = true
  showHistoryDialog.value = true
  try {
    history.value = await fetchHrSyncHistory(config.id)
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    historyLoading.value = false
  }
}

async function runPreview(config: HrSyncConfiguration) {
  previewConfig.value = config
  previewLoading.value = true
  showPreviewDialog.value = true
  try {
    previewResult.value = await previewHrSync(config.id)
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
    previewResult.value = null
  } finally {
    previewLoading.value = false
  }
}

function openMapping(config: HrSyncConfiguration) {
  mappingConfig.value = config
  editingMapping.value = Object.entries(config.attributeMapping).map(([k, v]) => ({
    hrField: k,
    adAttr: v,
  }))
  showMappingDialog.value = true
}

function addMappingRow() {
  editingMapping.value.push({ hrField: '', adAttr: '' })
}

function removeMappingRow(index: number) {
  editingMapping.value.splice(index, 1)
}

async function saveMapping() {
  if (!mappingConfig.value) return
  const mapping: Record<string, string> = {}
  for (const row of editingMapping.value) {
    if (row.hrField && row.adAttr) mapping[row.hrField] = row.adAttr
  }
  try {
    await updateHrSyncConfiguration(mappingConfig.value.id, { ...mappingConfig.value, attributeMapping: mapping })
    toast.add({ severity: 'success', summary: 'Saved', detail: 'Attribute mapping updated', life: 3000 })
    showMappingDialog.value = false
    await loadConfigurations()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

function formatDate(date: string | null) {
  if (!date) return '-'
  return new Date(date).toLocaleString()
}

function getStatusSeverity(status: string | null): "success" | "danger" | "warn" | "secondary" {
  if (!status) return 'secondary'
  const lower = status.toLowerCase()
  if (lower.includes('success') || lower.includes('completed')) return 'success'
  if (lower.includes('fail') || lower.includes('error')) return 'danger'
  if (lower.includes('running')) return 'warn'
  return 'secondary'
}
</script>

<template>
  <div>
    <div class="page-header">
      <h1>HR System Sync</h1>
      <p>Configure inbound user provisioning from HR systems</p>
    </div>

    <div class="toolbar">
      <Button label="New Configuration" icon="pi pi-plus" @click="openCreate" />
      <span class="toolbar-spacer" />
      <Button label="Refresh" icon="pi pi-refresh" text @click="loadConfigurations" :loading="loading" />
    </div>

    <div class="card">
      <DataTable :value="configurations" :loading="loading" stripedRows>
        <Column field="name" header="Name" sortable />
        <Column field="sourceType" header="Source Type" sortable />
        <Column header="Status">
          <template #body="{ data }">
            <Tag :value="data.isEnabled ? 'Enabled' : 'Disabled'" :severity="data.isEnabled ? 'success' : 'secondary'" />
          </template>
        </Column>
        <Column field="cronSchedule" header="Schedule" />
        <Column header="Last Sync">
          <template #body="{ data }">{{ formatDate(data.lastSyncAt) }}</template>
        </Column>
        <Column header="Last Status">
          <template #body="{ data }">
            <Tag v-if="data.lastSyncStatus" :value="data.lastSyncStatus" :severity="getStatusSeverity(data.lastSyncStatus)" />
            <span v-else>-</span>
          </template>
        </Column>
        <Column header="Actions" style="width: 320px">
          <template #body="{ data }">
            <div style="display: flex; gap: 0.25rem">
              <Button icon="pi pi-play" text size="small" v-tooltip="'Sync Now'" @click="syncNow(data)" />
              <Button icon="pi pi-eye" text size="small" v-tooltip="'Preview'" @click="runPreview(data)" />
              <Button icon="pi pi-history" text size="small" v-tooltip="'History'" @click="viewHistory(data)" />
              <Button icon="pi pi-arrows-h" text size="small" v-tooltip="'Mapping'" @click="openMapping(data)" />
              <Button icon="pi pi-pencil" text size="small" v-tooltip="'Edit'" @click="openEdit(data)" />
              <Button icon="pi pi-trash" text severity="danger" size="small" v-tooltip="'Delete'" @click="remove(data)" />
            </div>
          </template>
        </Column>
      </DataTable>
    </div>

    <!-- Edit Dialog -->
    <Dialog v-model:visible="showEditDialog" :header="isNew ? 'New HR Sync Configuration' : 'Edit Configuration'" modal style="width: 550px">
      <div style="display: flex; flex-direction: column; gap: 1rem">
        <div>
          <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">Name</label>
          <InputText v-model="editing.name" placeholder="e.g., Workday Sync" style="width: 100%" />
        </div>
        <div>
          <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">Source Type</label>
          <Select v-model="editing.sourceType" :options="sourceTypeOptions" optionLabel="label" optionValue="value" style="width: 100%" />
        </div>
        <div>
          <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">Endpoint URL</label>
          <InputText v-model="editing.endpointUrl" placeholder="https://api.hr-system.com/v1/employees" style="width: 100%" />
        </div>
        <div>
          <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">API Key</label>
          <InputText v-model="editing.apiKey" type="password" style="width: 100%" />
        </div>
        <div>
          <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">Target OU (DN)</label>
          <InputText v-model="editing.targetOu" placeholder="OU=Employees,DC=corp,DC=example,DC=com" style="width: 100%" />
        </div>
        <div>
          <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">Cron Schedule</label>
          <InputText v-model="editing.cronSchedule" placeholder="0 */6 * * *" style="width: 100%" />
        </div>
        <div style="display: flex; gap: 1.5rem">
          <div style="display: flex; align-items: center; gap: 0.5rem">
            <InputSwitch v-model="editing.autoCreateUsers" />
            <label>Auto-create users</label>
          </div>
          <div style="display: flex; align-items: center; gap: 0.5rem">
            <InputSwitch v-model="editing.autoDisableOnTermination" />
            <label>Auto-disable on termination</label>
          </div>
        </div>
        <div style="display: flex; gap: 1.5rem">
          <div style="display: flex; align-items: center; gap: 0.5rem">
            <InputSwitch v-model="editing.autoMoveOnDepartmentChange" />
            <label>Auto-move on dept change</label>
          </div>
          <div style="display: flex; align-items: center; gap: 0.5rem">
            <InputSwitch v-model="editing.isEnabled" />
            <label>Enabled</label>
          </div>
        </div>
      </div>
      <template #footer>
        <Button label="Cancel" text @click="showEditDialog = false" />
        <Button :label="isNew ? 'Create' : 'Save'" @click="save" :loading="saving" />
      </template>
    </Dialog>

    <!-- History Dialog -->
    <Dialog v-model:visible="showHistoryDialog" :header="`Sync History: ${historyConfig?.name ?? ''}`" modal style="width: 750px">
      <DataTable :value="history" :loading="historyLoading" paginator :rows="10" stripedRows>
        <Column header="Started">
          <template #body="{ data }">{{ formatDate(data.startedAt) }}</template>
        </Column>
        <Column header="Completed">
          <template #body="{ data }">{{ formatDate(data.completedAt) }}</template>
        </Column>
        <Column field="status" header="Status">
          <template #body="{ data }">
            <Tag :value="data.status" :severity="getStatusSeverity(data.status)" />
          </template>
        </Column>
        <Column field="usersCreated" header="Created" />
        <Column field="usersUpdated" header="Updated" />
        <Column field="usersDisabled" header="Disabled" />
        <Column field="errors" header="Errors" />
      </DataTable>
    </Dialog>

    <!-- Preview Dialog -->
    <Dialog v-model:visible="showPreviewDialog" :header="`Sync Preview: ${previewConfig?.name ?? ''}`" modal style="width: 750px">
      <div v-if="previewLoading" style="text-align: center; padding: 2rem">Loading preview...</div>
      <div v-else-if="previewResult">
        <div class="stat-grid" style="margin-bottom: 1rem">
          <div class="stat-card">
            <div class="stat-icon blue"><i class="pi pi-users" /></div>
            <div><div class="stat-value">{{ previewResult.totalRecords }}</div><div class="stat-label">Total Records</div></div>
          </div>
          <div class="stat-card">
            <div class="stat-icon green"><i class="pi pi-user-plus" /></div>
            <div><div class="stat-value">{{ previewResult.newUsers }}</div><div class="stat-label">New Users</div></div>
          </div>
          <div class="stat-card">
            <div class="stat-icon amber"><i class="pi pi-pencil" /></div>
            <div><div class="stat-value">{{ previewResult.updates }}</div><div class="stat-label">Updates</div></div>
          </div>
          <div class="stat-card">
            <div class="stat-icon purple"><i class="pi pi-ban" /></div>
            <div><div class="stat-value">{{ previewResult.terminations }}</div><div class="stat-label">Terminations</div></div>
          </div>
        </div>

        <DataTable :value="previewResult.actions" paginator :rows="10" stripedRows>
          <Column field="action" header="Action">
            <template #body="{ data }">
              <Tag :value="data.action" :severity="data.action === 'Create' ? 'success' : data.action === 'Disable' ? 'danger' : 'warn'" />
            </template>
          </Column>
          <Column field="employeeId" header="Employee ID" />
          <Column field="displayName" header="Name" />
          <Column header="Changes">
            <template #body="{ data }">
              <div v-for="(val, key) in data.changes" :key="key" style="font-size: 0.85rem">
                <strong>{{ key }}:</strong> {{ val }}
              </div>
            </template>
          </Column>
        </DataTable>
      </div>
    </Dialog>

    <!-- Attribute Mapping Dialog -->
    <Dialog v-model:visible="showMappingDialog" :header="`Attribute Mapping: ${mappingConfig?.name ?? ''}`" modal style="width: 600px">
      <p style="margin-bottom: 1rem; color: var(--p-text-muted-color)">
        Map HR system fields to directory attributes.
      </p>
      <div v-for="(row, idx) in editingMapping" :key="idx" style="display: flex; gap: 0.5rem; margin-bottom: 0.5rem; align-items: center">
        <InputText v-model="row.hrField" placeholder="HR field" style="flex: 1" />
        <i class="pi pi-arrow-right" style="color: var(--p-text-muted-color)" />
        <InputText v-model="row.adAttr" placeholder="AD attribute" style="flex: 1" />
        <Button icon="pi pi-times" text severity="danger" size="small" @click="removeMappingRow(idx)" />
      </div>
      <Button label="Add Mapping" icon="pi pi-plus" text size="small" @click="addMappingRow" />
      <template #footer>
        <Button label="Cancel" text @click="showMappingDialog = false" />
        <Button label="Save Mapping" @click="saveMapping" />
      </template>
    </Dialog>
  </div>
</template>
