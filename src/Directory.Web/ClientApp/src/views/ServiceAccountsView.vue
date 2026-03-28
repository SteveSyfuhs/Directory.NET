<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import InputNumber from 'primevue/inputnumber'
import Tag from 'primevue/tag'
import Dialog from 'primevue/dialog'
import TabView from 'primevue/tabview'
import TabPanel from 'primevue/tabpanel'
import Select from 'primevue/select'
import ProgressSpinner from 'primevue/progressspinner'
import { useToast } from 'primevue/usetoast'
import DnPicker from '../components/DnPicker.vue'
import {
  listServiceAccounts, getServiceAccount, createServiceAccount,
  updateServiceAccount, deleteServiceAccount,
  addPrincipal, removePrincipal,
  enableServiceAccount, disableServiceAccount,
  type ServiceAccountSummary, type ServiceAccountDetail,
} from '../api/serviceAccounts'
import { relativeTime } from '../utils/format'

const toast = useToast()
const accounts = ref<ServiceAccountSummary[]>([])
const loading = ref(true)
const filterText = ref('')
const selectedAccount = ref<ServiceAccountSummary | null>(null)

// Create dialog
const createVisible = ref(false)
const creating = ref(false)
const newName = ref('')
const newType = ref<'msa' | 'gmsa'>('gmsa')
const newDnsHostName = ref('')
const newPasswordInterval = ref(30)
const newSpns = ref<string[]>([])
const newSpnInput = ref('')

// Detail/Properties dialog
const detailVisible = ref(false)
const detail = ref<ServiceAccountDetail | null>(null)
const detailLoading = ref(false)
const saving = ref(false)
const editDnsHostName = ref('')
const editDescription = ref('')
const editPasswordInterval = ref(30)
const editSpns = ref<string[]>([])
const editSpnInput = ref('')

// Principal management
const addPrincipalDn = ref('')

onMounted(() => loadAccounts())

async function loadAccounts() {
  loading.value = true
  try {
    accounts.value = await listServiceAccounts()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    loading.value = false
  }
}

const filteredAccounts = computed(() => {
  if (!filterText.value) return accounts.value
  const q = filterText.value.toLowerCase()
  return accounts.value.filter(a =>
    a.name.toLowerCase().includes(q) || a.dnsHostName.toLowerCase().includes(q)
  )
})

// Create
async function onCreateAccount() {
  if (!newName.value.trim()) return
  creating.value = true
  try {
    await createServiceAccount({
      name: newName.value.trim(),
      type: newType.value,
      dnsHostName: newDnsHostName.value || undefined,
      passwordInterval: newPasswordInterval.value,
      servicePrincipalNames: newSpns.value.length > 0 ? newSpns.value : undefined,
    })
    toast.add({ severity: 'success', summary: 'Created', detail: 'Service account created', life: 3000 })
    createVisible.value = false
    resetCreateForm()
    await loadAccounts()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    creating.value = false
  }
}

function resetCreateForm() {
  newName.value = ''
  newType.value = 'gmsa'
  newDnsHostName.value = ''
  newPasswordInterval.value = 30
  newSpns.value = []
  newSpnInput.value = ''
}

function addNewSpn() {
  if (newSpnInput.value && !newSpns.value.includes(newSpnInput.value)) {
    newSpns.value.push(newSpnInput.value)
    newSpnInput.value = ''
  }
}

// Detail
async function openDetail(account: ServiceAccountSummary) {
  detailLoading.value = true
  detailVisible.value = true
  try {
    detail.value = await getServiceAccount(account.objectGuid)
    editDnsHostName.value = detail.value.dnsHostName
    editDescription.value = detail.value.description
    editPasswordInterval.value = detail.value.passwordInterval
    editSpns.value = [...detail.value.servicePrincipalNames]
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    detailLoading.value = false
  }
}

async function saveAccount() {
  if (!detail.value) return
  saving.value = true
  try {
    detail.value = await updateServiceAccount(detail.value.objectGuid, {
      dnsHostName: editDnsHostName.value,
      description: editDescription.value,
      passwordInterval: editPasswordInterval.value,
      servicePrincipalNames: editSpns.value,
    })
    toast.add({ severity: 'success', summary: 'Saved', detail: 'Service account updated', life: 3000 })
    await loadAccounts()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    saving.value = false
  }
}

function addEditSpn() {
  if (editSpnInput.value && !editSpns.value.includes(editSpnInput.value)) {
    editSpns.value.push(editSpnInput.value)
    editSpnInput.value = ''
  }
}

// Principals
async function onAddPrincipal() {
  if (!detail.value || !addPrincipalDn.value) return
  try {
    await addPrincipal(detail.value.objectGuid, addPrincipalDn.value)
    addPrincipalDn.value = ''
    toast.add({ severity: 'success', summary: 'Added', detail: 'Principal added', life: 3000 })
    detail.value = await getServiceAccount(detail.value.objectGuid)
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

async function onRemovePrincipal(dn: string) {
  if (!detail.value) return
  try {
    await removePrincipal(detail.value.objectGuid, dn)
    toast.add({ severity: 'success', summary: 'Removed', detail: 'Principal removed', life: 3000 })
    detail.value = await getServiceAccount(detail.value.objectGuid)
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

// Actions
async function onEnableAccount() {
  if (!detail.value) return
  try {
    detail.value = await enableServiceAccount(detail.value.objectGuid)
    toast.add({ severity: 'success', summary: 'Enabled', detail: 'Account enabled', life: 3000 })
    await loadAccounts()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

async function onDisableAccount() {
  if (!detail.value) return
  try {
    detail.value = await disableServiceAccount(detail.value.objectGuid)
    toast.add({ severity: 'success', summary: 'Disabled', detail: 'Account disabled', life: 3000 })
    await loadAccounts()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

async function onDeleteAccount() {
  if (!selectedAccount.value) return
  if (!confirm(`Delete service account "${selectedAccount.value.name}"?`)) return
  try {
    await deleteServiceAccount(selectedAccount.value.objectGuid)
    toast.add({ severity: 'success', summary: 'Deleted', detail: 'Service account deleted', life: 3000 })
    selectedAccount.value = null
    detailVisible.value = false
    await loadAccounts()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

function onRowDoubleClick(event: { data: ServiceAccountSummary }) {
  openDetail(event.data)
}
</script>

<template>
  <div>
    <div class="page-header">
      <h1>Managed Service Accounts</h1>
      <p>Manage Managed Service Accounts (MSA) and Group Managed Service Accounts (gMSA)</p>
      <p style="font-size: 0.8125rem; color: var(--p-text-muted-color); margin: 0 0 0.75rem 0">Managed Service Accounts provide automatic password management and SPN handling for services. gMSAs extend this to multiple servers, allowing a single account to be used across a group of hosts. Principals define which computers or groups are allowed to retrieve the managed password.</p>
    </div>

    <div class="toolbar">
      <Button label="Create Service Account" icon="pi pi-plus" size="small" @click="createVisible = true" />
      <Button label="Properties" icon="pi pi-pencil" size="small" severity="secondary" outlined
              @click="selectedAccount && openDetail(selectedAccount)" :disabled="!selectedAccount" />
      <Button icon="pi pi-trash" size="small" severity="danger" text
              @click="onDeleteAccount" :disabled="!selectedAccount" />
      <div class="toolbar-spacer" />
      <InputText v-model="filterText" placeholder="Search service accounts..." size="small" style="width: 260px" />
    </div>

    <div v-if="loading" style="text-align: center; padding: 4rem">
      <ProgressSpinner />
    </div>

    <div v-else class="card" style="padding: 0">
      <DataTable :value="filteredAccounts" v-model:selection="selectedAccount"
                 selectionMode="single" dataKey="objectGuid"
                 stripedRows size="small" scrollable scrollHeight="calc(100vh - 260px)"
                 :paginator="filteredAccounts.length > 50" :rows="50"
                 @row-dblclick="onRowDoubleClick">
        <Column header="Name" sortable sortField="name" style="min-width: 200px">
          <template #body="{ data }">
            <div style="display: flex; align-items: center; gap: 0.5rem">
              <i class="pi pi-key" style="color: var(--p-text-muted-color)"></i>
              <span>{{ data.name }}</span>
            </div>
          </template>
        </Column>
        <Column header="Type" sortable sortField="type" style="width: 100px">
          <template #body="{ data }">
            <Tag :value="data.type" :severity="data.type === 'gMSA' ? 'info' : 'secondary'" />
          </template>
        </Column>
        <Column field="dnsHostName" header="DNS Host Name" sortable style="min-width: 200px" />
        <Column header="Password Interval" sortable sortField="passwordInterval" style="width: 160px">
          <template #body="{ data }">
            {{ data.passwordInterval }} days
          </template>
        </Column>
        <Column header="Principals" sortable sortField="principalCount" style="width: 120px">
          <template #body="{ data }">
            <Tag :value="String(data.principalCount)" severity="secondary" />
          </template>
        </Column>
        <Column header="Status" sortable sortField="enabled" style="width: 100px">
          <template #body="{ data }">
            <Tag :value="data.enabled ? 'Enabled' : 'Disabled'" :severity="data.enabled ? 'success' : 'danger'" />
          </template>
        </Column>
        <Column header="Modified" sortable sortField="whenChanged" style="width: 130px">
          <template #body="{ data }">
            <span style="color: var(--p-text-muted-color)">{{ relativeTime(data.whenChanged) }}</span>
          </template>
        </Column>
        <template #empty>
          <div style="text-align: center; padding: 2rem; color: var(--p-text-muted-color)">No managed service accounts found. Create a gMSA or MSA to provide automatic password management for your services and applications.</div>
        </template>
      </DataTable>
    </div>

    <!-- Create Dialog -->
    <Dialog v-model:visible="createVisible" header="Create Service Account" modal :style="{ width: '550px' }">
      <p style="font-size: 0.8125rem; color: var(--p-text-muted-color); margin: 0 0 0.75rem 0">Create a managed service account for automatic password management. Choose gMSA for multi-server scenarios or MSA for a single server. The DNS host name is used for Kerberos authentication, and SPNs define the services this account represents.</p>
      <div style="display: flex; flex-direction: column; gap: 1rem; padding: 0.5rem 0">
        <div>
          <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">Account Name</label>
          <InputText v-model="newName" placeholder="e.g. svc-webapp" style="width: 100%" size="small"
                     @keyup.enter="onCreateAccount" />
        </div>
        <div>
          <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">Account Type</label>
          <Select v-model="newType" :options="[
            { label: 'Group Managed Service Account (gMSA)', value: 'gmsa' },
            { label: 'Managed Service Account (MSA)', value: 'msa' },
          ]" optionLabel="label" optionValue="value" size="small" style="width: 100%" />
        </div>
        <div>
          <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">DNS Host Name</label>
          <InputText v-model="newDnsHostName" placeholder="svc-webapp.corp.example.com" style="width: 100%" size="small" />
        </div>
        <div>
          <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">Password Change Interval (days)</label>
          <InputNumber v-model="newPasswordInterval" :min="1" :max="365" size="small" style="width: 200px" />
        </div>
        <div>
          <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">Service Principal Names</label>
          <div style="display: flex; gap: 0.5rem; margin-bottom: 0.5rem">
            <InputText v-model="newSpnInput" placeholder="HTTP/webapp.corp.example.com" style="flex: 1" size="small"
                       @keyup.enter="addNewSpn" />
            <Button icon="pi pi-plus" size="small" @click="addNewSpn" :disabled="!newSpnInput" />
          </div>
          <div v-for="(spn, idx) in newSpns" :key="idx"
               style="display: flex; align-items: center; gap: 0.5rem; padding: 0.25rem 0.5rem; margin-bottom: 0.25rem; background: var(--p-surface-100); border-radius: 4px; font-size: 0.875rem">
            <span style="flex: 1; font-family: monospace">{{ spn }}</span>
            <Button icon="pi pi-times" size="small" severity="danger" text @click="newSpns.splice(idx, 1)" />
          </div>
        </div>
      </div>
      <template #footer>
        <Button label="Cancel" severity="secondary" text @click="createVisible = false" />
        <Button label="Create" icon="pi pi-check" @click="onCreateAccount" :loading="creating" :disabled="!newName.trim()" />
      </template>
    </Dialog>

    <!-- Properties Dialog -->
    <Dialog v-model:visible="detailVisible" :header="detail?.name || 'Service Account Properties'" modal
            :style="{ width: '750px', maxHeight: '85vh' }">
      <div v-if="detailLoading" style="text-align: center; padding: 2rem"><ProgressSpinner /></div>
      <template v-else-if="detail">
        <TabView>
          <TabPanel header="General" value="general">
            <div style="display: flex; flex-direction: column; gap: 1rem">
              <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 1rem">
                <div>
                  <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">Name</label>
                  <span>{{ detail.name }}</span>
                </div>
                <div>
                  <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">Type</label>
                  <Tag :value="detail.type" :severity="detail.type === 'gMSA' ? 'info' : 'secondary'" />
                </div>
                <div>
                  <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">SAM Account Name</label>
                  <span style="font-family: monospace">{{ detail.samAccountName }}</span>
                </div>
                <div>
                  <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">SID</label>
                  <span style="font-family: monospace; font-size: 0.85em; color: var(--p-text-muted-color)">{{ detail.objectSid }}</span>
                </div>
              </div>
              <div>
                <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">DNS Host Name</label>
                <InputText v-model="editDnsHostName" size="small" style="width: 100%" />
              </div>
              <div>
                <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">Description</label>
                <InputText v-model="editDescription" size="small" style="width: 100%" />
              </div>
              <div>
                <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">Password Change Interval (days)</label>
                <InputNumber v-model="editPasswordInterval" :min="1" :max="365" size="small" style="width: 200px" />
              </div>
              <div style="display: flex; gap: 0.5rem">
                <Button v-if="detail.enabled" label="Disable" icon="pi pi-ban" size="small" severity="warn" @click="onDisableAccount" />
                <Button v-else label="Enable" icon="pi pi-check" size="small" severity="success" @click="onEnableAccount" />
              </div>
            </div>
          </TabPanel>

          <TabPanel header="Allowed Principals" value="allowed-principals">
            <p style="color: var(--p-text-muted-color); font-size: 0.875rem; margin-top: 0">
              Principals (computers/groups) allowed to retrieve the managed password for this service account.
            </p>
            <div style="display: flex; gap: 0.5rem; margin-bottom: 1rem; align-items: flex-end">
              <DnPicker v-model="addPrincipalDn" label="Add Principal"
                        objectFilter="(|(objectClass=computer)(objectClass=group))"
                        style="width: 400px" />
              <Button label="Add" icon="pi pi-plus" size="small" @click="onAddPrincipal" :disabled="!addPrincipalDn" />
            </div>
            <DataTable :value="detail.principals.map(p => ({ dn: p }))" stripedRows size="small">
              <Column header="Distinguished Name" field="dn" style="min-width: 400px">
                <template #body="{ data }">
                  <span style="font-size: 0.875rem">{{ data.dn }}</span>
                </template>
              </Column>
              <Column style="width: 60px">
                <template #body="{ data }">
                  <Button icon="pi pi-times" size="small" severity="danger" text @click="onRemovePrincipal(data.dn)" />
                </template>
              </Column>
              <template #empty>
                <div style="text-align: center; padding: 1.5rem; color: var(--p-text-muted-color)">No principals configured</div>
              </template>
            </DataTable>
          </TabPanel>

          <TabPanel header="SPNs" value="spns">
            <div style="display: flex; gap: 0.5rem; margin-bottom: 1rem">
              <InputText v-model="editSpnInput" placeholder="HTTP/webapp.corp.example.com" size="small" style="flex: 1"
                         @keyup.enter="addEditSpn" />
              <Button icon="pi pi-plus" size="small" @click="addEditSpn" :disabled="!editSpnInput" />
            </div>
            <DataTable :value="editSpns.map((s, i) => ({ spn: s, idx: i }))" stripedRows size="small">
              <Column header="Service Principal Name" style="min-width: 400px">
                <template #body="{ data }">
                  <span style="font-family: monospace; font-size: 0.875rem">{{ data.spn }}</span>
                </template>
              </Column>
              <Column style="width: 60px">
                <template #body="{ data }">
                  <Button icon="pi pi-times" size="small" severity="danger" text @click="editSpns.splice(data.idx, 1)" />
                </template>
              </Column>
              <template #empty>
                <div style="text-align: center; padding: 1.5rem; color: var(--p-text-muted-color)">No SPNs configured</div>
              </template>
            </DataTable>
          </TabPanel>
        </TabView>
      </template>
      <template #footer>
        <Button label="Cancel" severity="secondary" text @click="detailVisible = false" />
        <Button label="Save" icon="pi pi-check" @click="saveAccount" :loading="saving" />
      </template>
    </Dialog>
  </div>
</template>
