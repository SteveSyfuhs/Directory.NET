<script setup lang="ts">
import { ref, onMounted } from 'vue'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import InputSwitch from 'primevue/inputswitch'
import Textarea from 'primevue/textarea'
import Tag from 'primevue/tag'
import { useToast } from 'primevue/usetoast'
import type { ScimIntegration, ScimOperationLog } from '../types/scim'
import {
  fetchScimIntegrations,
  createScimIntegration,
  updateScimIntegration,
  deleteScimIntegration,
  fetchScimOperationLogs,
  fetchDefaultScimAttributeMapping,
} from '../api/scim'

const toast = useToast()
const loading = ref(false)
const integrations = ref<ScimIntegration[]>([])
const defaultMapping = ref<Record<string, string>>({})

const showEditDialog = ref(false)
const editing = ref<Partial<ScimIntegration>>({})
const isNew = ref(false)
const saving = ref(false)

const showTokenDialog = ref(false)
const visibleToken = ref('')

const showLogsDialog = ref(false)
const logsIntegration = ref<ScimIntegration | null>(null)
const operationLogs = ref<ScimOperationLog[]>([])
const logsLoading = ref(false)

const showMappingDialog = ref(false)
const mappingIntegration = ref<ScimIntegration | null>(null)
const editingMapping = ref<{ scimAttr: string; directoryAttr: string }[]>([])

onMounted(async () => {
  await Promise.all([loadIntegrations(), loadDefaultMapping()])
})

async function loadIntegrations() {
  loading.value = true
  try {
    integrations.value = await fetchScimIntegrations()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    loading.value = false
  }
}

async function loadDefaultMapping() {
  try {
    defaultMapping.value = await fetchDefaultScimAttributeMapping()
  } catch {}
}

function openCreate() {
  isNew.value = true
  editing.value = {
    name: '',
    description: '',
    isEnabled: true,
    attributeMapping: { ...defaultMapping.value },
  }
  showEditDialog.value = true
}

function openEdit(integration: ScimIntegration) {
  isNew.value = false
  editing.value = { ...integration }
  showEditDialog.value = true
}

async function save() {
  saving.value = true
  try {
    if (isNew.value) {
      const created = await createScimIntegration(editing.value)
      toast.add({ severity: 'success', summary: 'Created', detail: `Integration "${created.name}" created`, life: 3000 })
      visibleToken.value = created.bearerToken
      showEditDialog.value = false
      showTokenDialog.value = true
    } else {
      await updateScimIntegration(editing.value.id!, editing.value)
      toast.add({ severity: 'success', summary: 'Updated', detail: 'Integration updated', life: 3000 })
      showEditDialog.value = false
    }
    await loadIntegrations()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    saving.value = false
  }
}

async function remove(integration: ScimIntegration) {
  try {
    await deleteScimIntegration(integration.id)
    toast.add({ severity: 'success', summary: 'Deleted', detail: `Integration "${integration.name}" deleted`, life: 3000 })
    await loadIntegrations()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

async function viewLogs(integration: ScimIntegration) {
  logsIntegration.value = integration
  logsLoading.value = true
  showLogsDialog.value = true
  try {
    operationLogs.value = await fetchScimOperationLogs(integration.id)
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    logsLoading.value = false
  }
}

function openMapping(integration: ScimIntegration) {
  mappingIntegration.value = integration
  editingMapping.value = Object.entries(integration.attributeMapping).map(([k, v]) => ({
    scimAttr: k,
    directoryAttr: v,
  }))
  showMappingDialog.value = true
}

function addMappingRow() {
  editingMapping.value.push({ scimAttr: '', directoryAttr: '' })
}

function removeMappingRow(index: number) {
  editingMapping.value.splice(index, 1)
}

async function saveMapping() {
  if (!mappingIntegration.value) return
  const mapping: Record<string, string> = {}
  for (const row of editingMapping.value) {
    if (row.scimAttr && row.directoryAttr) mapping[row.scimAttr] = row.directoryAttr
  }
  try {
    await updateScimIntegration(mappingIntegration.value.id, { ...mappingIntegration.value, attributeMapping: mapping })
    toast.add({ severity: 'success', summary: 'Saved', detail: 'Attribute mapping updated', life: 3000 })
    showMappingDialog.value = false
    await loadIntegrations()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

function copyToken() {
  navigator.clipboard.writeText(visibleToken.value)
  toast.add({ severity: 'info', summary: 'Copied', detail: 'Bearer token copied to clipboard', life: 2000 })
}

function formatDate(date: string | null) {
  if (!date) return '-'
  return new Date(date).toLocaleString()
}

function getStatusSeverity(status: string | null): "success" | "danger" | "secondary" {
  if (!status) return 'secondary'
  if (status.toLowerCase().includes('success')) return 'success'
  if (status.toLowerCase().includes('fail') || status.toLowerCase().includes('error')) return 'danger'
  return 'secondary'
}
</script>

<template>
  <div>
    <div class="page-header">
      <h1>SCIM Integrations</h1>
      <p>Manage SCIM 2.0 provisioning integrations for SaaS applications</p>
    </div>

    <div class="toolbar">
      <Button label="New Integration" icon="pi pi-plus" @click="openCreate" />
      <span class="toolbar-spacer" />
      <Button label="Refresh" icon="pi pi-refresh" text @click="loadIntegrations" :loading="loading" />
    </div>

    <div class="card">
      <DataTable :value="integrations" :loading="loading" stripedRows>
        <Column field="name" header="Name" sortable />
        <Column header="Status">
          <template #body="{ data }">
            <Tag :value="data.isEnabled ? 'Enabled' : 'Disabled'" :severity="data.isEnabled ? 'success' : 'secondary'" />
          </template>
        </Column>
        <Column header="Base URL">
          <template #body>
            <code>/scim/v2</code>
          </template>
        </Column>
        <Column header="Operations" field="operationCount" sortable />
        <Column header="Last Sync">
          <template #body="{ data }">
            {{ formatDate(data.lastSyncAt) }}
          </template>
        </Column>
        <Column header="Last Status">
          <template #body="{ data }">
            <Tag v-if="data.lastSyncStatus" :value="data.lastSyncStatus" :severity="getStatusSeverity(data.lastSyncStatus)" />
            <span v-else>-</span>
          </template>
        </Column>
        <Column header="Actions" style="width: 280px">
          <template #body="{ data }">
            <div style="display: flex; gap: 0.25rem">
              <Button icon="pi pi-pencil" text size="small" v-tooltip="'Edit'" @click="openEdit(data)" />
              <Button icon="pi pi-list" text size="small" v-tooltip="'Logs'" @click="viewLogs(data)" />
              <Button icon="pi pi-arrows-h" text size="small" v-tooltip="'Attribute Mapping'" @click="openMapping(data)" />
              <Button icon="pi pi-trash" text severity="danger" size="small" v-tooltip="'Delete'" @click="remove(data)" />
            </div>
          </template>
        </Column>
      </DataTable>
    </div>

    <!-- Edit Dialog -->
    <Dialog v-model:visible="showEditDialog" :header="isNew ? 'New SCIM Integration' : 'Edit Integration'" modal style="width: 500px">
      <div style="display: flex; flex-direction: column; gap: 1rem">
        <div>
          <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">Name</label>
          <InputText v-model="editing.name" placeholder="e.g., Okta, Slack" style="width: 100%" />
        </div>
        <div>
          <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">Description</label>
          <Textarea v-model="editing.description" rows="2" style="width: 100%" />
        </div>
        <div style="display: flex; align-items: center; gap: 0.5rem">
          <InputSwitch v-model="editing.isEnabled" />
          <label>Enabled</label>
        </div>
      </div>
      <template #footer>
        <Button label="Cancel" text @click="showEditDialog = false" />
        <Button :label="isNew ? 'Create' : 'Save'" @click="save" :loading="saving" />
      </template>
    </Dialog>

    <!-- Token Dialog -->
    <Dialog v-model:visible="showTokenDialog" header="Bearer Token" modal style="width: 500px">
      <p style="margin-bottom: 0.5rem; color: var(--p-text-muted-color)">
        Copy this bearer token now. It will not be shown again in full.
      </p>
      <div style="display: flex; gap: 0.5rem; align-items: center">
        <InputText :modelValue="visibleToken" readonly style="flex: 1; font-family: monospace" />
        <Button icon="pi pi-copy" @click="copyToken" v-tooltip="'Copy'" />
      </div>
      <template #footer>
        <Button label="Done" @click="showTokenDialog = false" />
      </template>
    </Dialog>

    <!-- Logs Dialog -->
    <Dialog v-model:visible="showLogsDialog" :header="`Logs: ${logsIntegration?.name ?? ''}`" modal style="width: 700px">
      <DataTable :value="operationLogs" :loading="logsLoading" paginator :rows="10" stripedRows>
        <Column field="timestamp" header="Time" sortable>
          <template #body="{ data }">{{ formatDate(data.timestamp) }}</template>
        </Column>
        <Column field="operation" header="Operation" sortable />
        <Column field="resourceType" header="Resource" sortable />
        <Column field="status" header="Status" sortable>
          <template #body="{ data }">
            <Tag :value="data.status" :severity="data.status === 'Success' ? 'success' : 'danger'" />
          </template>
        </Column>
        <Column field="detail" header="Detail" />
      </DataTable>
    </Dialog>

    <!-- Attribute Mapping Dialog -->
    <Dialog v-model:visible="showMappingDialog" :header="`Attribute Mapping: ${mappingIntegration?.name ?? ''}`" modal style="width: 600px">
      <p style="margin-bottom: 1rem; color: var(--p-text-muted-color)">
        Map SCIM attributes to directory attributes.
      </p>
      <div v-for="(row, idx) in editingMapping" :key="idx" style="display: flex; gap: 0.5rem; margin-bottom: 0.5rem; align-items: center">
        <InputText v-model="row.scimAttr" placeholder="SCIM attribute" style="flex: 1" />
        <i class="pi pi-arrow-right" style="color: var(--p-text-muted-color)" />
        <InputText v-model="row.directoryAttr" placeholder="Directory attribute" style="flex: 1" />
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
