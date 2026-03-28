<script setup lang="ts">
import { ref, onMounted } from 'vue'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import InputSwitch from 'primevue/inputswitch'
import Select from 'primevue/select'
import Tag from 'primevue/tag'
import { useToast } from 'primevue/usetoast'
import type { MdmIntegration, DeviceComplianceStatus, MdmProvider } from '../types/mdm'
import {
  fetchMdmIntegrations,
  createMdmIntegration,
  updateMdmIntegration,
  deleteMdmIntegration,
  fetchMdmDevices,
  syncMdmDevices,
} from '../api/mdmIntegration'

const toast = useToast()
const loading = ref(false)
const integrations = ref<MdmIntegration[]>([])
const devices = ref<DeviceComplianceStatus[]>([])

const showEditDialog = ref(false)
const editingIntegration = ref<Partial<MdmIntegration>>({})
const isNew = ref(false)
const syncing = ref(false)

const providerOptions = [
  { label: 'Microsoft Intune', value: 'Intune' },
  { label: 'Jamf Pro', value: 'JamfPro' },
  { label: 'VMware Workspace ONE', value: 'WorkspaceOne' },
  { label: 'MobileIron', value: 'MobileIron' },
  { label: 'Generic', value: 'Generic' },
]

onMounted(async () => {
  await Promise.all([loadIntegrations(), loadDevices()])
})

async function loadIntegrations() {
  loading.value = true
  try {
    integrations.value = await fetchMdmIntegrations()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    loading.value = false
  }
}

async function loadDevices() {
  try {
    devices.value = await fetchMdmDevices()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

function openCreate() {
  isNew.value = true
  editingIntegration.value = { provider: 'Intune' as MdmProvider, isEnabled: true, syncDeviceCompliance: true }
  showEditDialog.value = true
}

function openEdit(integration: MdmIntegration) {
  isNew.value = false
  editingIntegration.value = { ...integration }
  showEditDialog.value = true
}

async function saveIntegration() {
  try {
    if (isNew.value) {
      await createMdmIntegration(editingIntegration.value)
      toast.add({ severity: 'success', summary: 'Integration Created', life: 3000 })
    } else {
      await updateMdmIntegration(editingIntegration.value.id!, editingIntegration.value)
      toast.add({ severity: 'success', summary: 'Integration Updated', life: 3000 })
    }
    showEditDialog.value = false
    await loadIntegrations()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

async function removeIntegration(integration: MdmIntegration) {
  try {
    await deleteMdmIntegration(integration.id)
    toast.add({ severity: 'success', summary: 'Integration Deleted', life: 3000 })
    await Promise.all([loadIntegrations(), loadDevices()])
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

async function syncAllDevices() {
  syncing.value = true
  try {
    const result = await syncMdmDevices()
    toast.add({
      severity: 'success',
      summary: 'Sync Complete',
      detail: `Synced: ${result.devicesSynced}, New: ${result.newDevices}, Updated: ${result.updatedDevices}`,
      life: 5000,
    })
    await loadDevices()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    syncing.value = false
  }
}

function integrationName(id: string) {
  return integrations.value.find(i => i.id === id)?.name ?? id
}
</script>

<template>
  <div>
    <div class="page-header">
      <h1>MDM Integrations</h1>
      <p>Manage mobile device management integrations and device compliance status.</p>
    </div>

    <!-- Integrations Table -->
    <div class="card" style="margin-bottom: 1.5rem">
      <div class="toolbar">
        <span class="card-title" style="margin-bottom: 0">Integrations</span>
        <span class="toolbar-spacer"></span>
        <Button label="Add Integration" icon="pi pi-plus" size="small" @click="openCreate" />
      </div>
      <DataTable :value="integrations" :loading="loading" size="small" stripedRows>
        <Column field="name" header="Name" />
        <Column field="provider" header="Provider">
          <template #body="{ data }">
            <Tag :value="data.provider" />
          </template>
        </Column>
        <Column field="apiEndpoint" header="API Endpoint" />
        <Column field="isEnabled" header="Enabled">
          <template #body="{ data }">
            <Tag :value="data.isEnabled ? 'Yes' : 'No'" :severity="data.isEnabled ? 'success' : 'secondary'" />
          </template>
        </Column>
        <Column field="lastSyncAt" header="Last Sync">
          <template #body="{ data }">{{ data.lastSyncAt ? new Date(data.lastSyncAt).toLocaleString() : 'Never' }}</template>
        </Column>
        <Column header="Actions" style="width: 150px">
          <template #body="{ data }">
            <Button icon="pi pi-pencil" size="small" text rounded v-tooltip="'Edit'" @click="openEdit(data)" />
            <Button icon="pi pi-trash" size="small" text rounded severity="danger" v-tooltip="'Delete'" @click="removeIntegration(data)" />
          </template>
        </Column>
      </DataTable>
    </div>

    <!-- Device Compliance Table -->
    <div class="card">
      <div class="toolbar">
        <span class="card-title" style="margin-bottom: 0">Device Compliance</span>
        <span class="toolbar-spacer"></span>
        <Button label="Sync Devices" icon="pi pi-sync" size="small" :loading="syncing" @click="syncAllDevices" />
      </div>
      <DataTable :value="devices" size="small" stripedRows paginator :rows="20">
        <Column field="deviceName" header="Device" />
        <Column field="platform" header="Platform" />
        <Column field="userDn" header="User DN" />
        <Column field="isCompliant" header="Compliant">
          <template #body="{ data }">
            <Tag :value="data.isCompliant ? 'Yes' : 'No'" :severity="data.isCompliant ? 'success' : 'danger'" />
          </template>
        </Column>
        <Column field="isManaged" header="Managed">
          <template #body="{ data }">
            <Tag :value="data.isManaged ? 'Yes' : 'No'" :severity="data.isManaged ? 'success' : 'warn'" />
          </template>
        </Column>
        <Column field="integrationId" header="Source">
          <template #body="{ data }">{{ integrationName(data.integrationId) }}</template>
        </Column>
        <Column field="lastCheckIn" header="Last Check-In">
          <template #body="{ data }">{{ new Date(data.lastCheckIn).toLocaleString() }}</template>
        </Column>
        <Column field="complianceIssues" header="Issues">
          <template #body="{ data }">
            <Tag v-for="issue in data.complianceIssues" :key="issue" :value="issue" severity="warn" style="margin: 0.125rem" />
          </template>
        </Column>
      </DataTable>
    </div>

    <!-- Edit Integration Dialog -->
    <Dialog v-model:visible="showEditDialog" :header="isNew ? 'Add Integration' : 'Edit Integration'" :style="{ width: '500px' }" modal>
      <div style="display: flex; flex-direction: column; gap: 1rem">
        <div><label>Name</label><InputText v-model="editingIntegration.name" style="width: 100%" placeholder="e.g., Corporate Intune" /></div>
        <div>
          <label>Provider</label>
          <Select v-model="editingIntegration.provider" :options="providerOptions" optionLabel="label" optionValue="value" style="width: 100%" />
        </div>
        <div><label>API Endpoint</label><InputText v-model="editingIntegration.apiEndpoint" style="width: 100%" placeholder="https://graph.microsoft.com" /></div>
        <div><label>API Key</label><InputText v-model="editingIntegration.apiKey" type="password" style="width: 100%" /></div>
        <div style="display: flex; gap: 1.5rem">
          <label style="display: flex; align-items: center; gap: 0.5rem"><InputSwitch v-model="editingIntegration.isEnabled" /> Enabled</label>
          <label style="display: flex; align-items: center; gap: 0.5rem"><InputSwitch v-model="editingIntegration.syncDeviceCompliance" /> Sync Compliance</label>
        </div>
      </div>
      <template #footer>
        <Button label="Cancel" text @click="showEditDialog = false" />
        <Button label="Save" icon="pi pi-check" @click="saveIntegration" />
      </template>
    </Dialog>
  </div>
</template>
