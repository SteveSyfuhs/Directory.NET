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
import ProgressSpinner from 'primevue/progressspinner'
import { useToast } from 'primevue/usetoast'
import {
  listGmsaAccounts, getGmsaAccount, createGmsaAccount,
  deleteGmsaAccount, rotateGmsaPassword,
  listKdsRootKeys, createKdsRootKey,
} from '../api/gmsa'
import type { GmsaAccount, KdsRootKey } from '../types/gmsa'
import { relativeTime } from '../utils/format'

const toast = useToast()

// ── Data ────────────────────────────────────────────────────────
const accounts = ref<GmsaAccount[]>([])
const kdsKeys = ref<KdsRootKey[]>([])
const loading = ref(true)
const filterText = ref('')
const selectedAccount = ref<GmsaAccount | null>(null)

// Create dialog
const createVisible = ref(false)
const creating = ref(false)
const newName = ref('')
const newDnsHostName = ref('')
const newPasswordInterval = ref(30)
const newSpns = ref<string[]>([])
const newSpnInput = ref('')
const newPrincipals = ref<string[]>([])
const newPrincipalInput = ref('')

// Detail dialog
const detailVisible = ref(false)
const detailAccount = ref<GmsaAccount | null>(null)
const detailLoading = ref(false)

// Rotate confirmation
const rotateVisible = ref(false)
const rotating = ref(false)

// KDS key creation
const creatingKds = ref(false)

onMounted(() => loadAll())

async function loadAll() {
  loading.value = true
  try {
    const [accts, keys] = await Promise.all([
      listGmsaAccounts(),
      listKdsRootKeys(),
    ])
    accounts.value = accts
    kdsKeys.value = keys
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
    a.name.toLowerCase().includes(q) ||
    a.dnsHostName.toLowerCase().includes(q) ||
    a.distinguishedName.toLowerCase().includes(q)
  )
})

// ── Create ──────────────────────────────────────────────────────
async function onCreateAccount() {
  if (!newName.value.trim()) return
  creating.value = true
  try {
    await createGmsaAccount({
      name: newName.value.trim(),
      dnsHostName: newDnsHostName.value || undefined,
      managedPasswordIntervalInDays: newPasswordInterval.value,
      servicePrincipalNames: newSpns.value.length > 0 ? newSpns.value : undefined,
      principalsAllowedToRetrievePassword: newPrincipals.value.length > 0 ? newPrincipals.value : undefined,
    })
    toast.add({ severity: 'success', summary: 'Created', detail: 'gMSA account created', life: 3000 })
    createVisible.value = false
    resetCreateForm()
    await loadAll()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    creating.value = false
  }
}

function resetCreateForm() {
  newName.value = ''
  newDnsHostName.value = ''
  newPasswordInterval.value = 30
  newSpns.value = []
  newSpnInput.value = ''
  newPrincipals.value = []
  newPrincipalInput.value = ''
}

function addNewSpn() {
  if (newSpnInput.value && !newSpns.value.includes(newSpnInput.value)) {
    newSpns.value.push(newSpnInput.value)
    newSpnInput.value = ''
  }
}

function addNewPrincipal() {
  if (newPrincipalInput.value && !newPrincipals.value.includes(newPrincipalInput.value)) {
    newPrincipals.value.push(newPrincipalInput.value)
    newPrincipalInput.value = ''
  }
}

// ── Detail ──────────────────────────────────────────────────────
async function openDetail(account: GmsaAccount) {
  detailLoading.value = true
  detailVisible.value = true
  try {
    detailAccount.value = await getGmsaAccount(account.name)
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    detailLoading.value = false
  }
}

// ── Delete ──────────────────────────────────────────────────────
async function onDeleteAccount() {
  if (!selectedAccount.value) return
  if (!confirm(`Delete gMSA account "${selectedAccount.value.name}"? This cannot be undone.`)) return
  try {
    await deleteGmsaAccount(selectedAccount.value.name)
    toast.add({ severity: 'success', summary: 'Deleted', detail: 'gMSA account deleted', life: 3000 })
    selectedAccount.value = null
    detailVisible.value = false
    await loadAll()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

// ── Rotate Password ─────────────────────────────────────────────
function showRotateConfirm() {
  if (!selectedAccount.value && !detailAccount.value) return
  rotateVisible.value = true
}

async function onRotatePassword() {
  const name = detailAccount.value?.name || selectedAccount.value?.name
  if (!name) return
  rotating.value = true
  try {
    const updated = await rotateGmsaPassword(name)
    toast.add({ severity: 'success', summary: 'Rotated', detail: 'Password rotated successfully', life: 3000 })
    rotateVisible.value = false
    if (detailAccount.value) detailAccount.value = updated
    await loadAll()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    rotating.value = false
  }
}

// ── KDS Root Key ────────────────────────────────────────────────
async function onCreateKdsKey() {
  creatingKds.value = true
  try {
    await createKdsRootKey()
    toast.add({ severity: 'success', summary: 'Created', detail: 'KDS root key created (effective in 10 hours)', life: 5000 })
    kdsKeys.value = await listKdsRootKeys()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    creatingKds.value = false
  }
}

function formatDate(iso: string) {
  if (!iso) return '-'
  return new Date(iso).toLocaleString()
}

function onRowDoubleClick(event: { data: GmsaAccount }) {
  openDetail(event.data)
}
</script>

<template>
  <div>
    <div class="page-header">
      <h1>Group Managed Service Accounts</h1>
      <p>Manage gMSA accounts with automatic password rotation and KDS root keys</p>
      <p style="font-size: 0.8125rem; color: var(--p-text-muted-color); margin: 0 0 0.75rem 0">
        Group Managed Service Accounts (gMSA) provide automatic password management for services running across
        multiple servers. Passwords are derived from KDS root keys using a deterministic algorithm, so all authorized
        hosts compute the same password without synchronization. At least one KDS root key must exist before creating
        gMSA accounts.
      </p>
    </div>

    <TabView>
      <!-- ── gMSA Accounts Tab ──────────────────────────────────── -->
      <TabPanel header="gMSA Accounts">
        <div class="toolbar">
          <Button label="Create gMSA" icon="pi pi-plus" size="small" @click="createVisible = true" />
          <Button label="Properties" icon="pi pi-pencil" size="small" severity="secondary" outlined
                  @click="selectedAccount && openDetail(selectedAccount)" :disabled="!selectedAccount" />
          <Button label="Rotate Password" icon="pi pi-refresh" size="small" severity="warn" outlined
                  @click="showRotateConfirm" :disabled="!selectedAccount" />
          <Button icon="pi pi-trash" size="small" severity="danger" text
                  @click="onDeleteAccount" :disabled="!selectedAccount" />
          <div class="toolbar-spacer" />
          <InputText v-model="filterText" placeholder="Search gMSA accounts..." size="small" style="width: 260px" />
        </div>

        <div v-if="loading" style="text-align: center; padding: 4rem">
          <ProgressSpinner />
        </div>

        <div v-else class="card" style="padding: 0">
          <DataTable :value="filteredAccounts" v-model:selection="selectedAccount"
                     selectionMode="single" dataKey="id"
                     stripedRows size="small" scrollable scrollHeight="calc(100vh - 340px)"
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
            <Column field="dnsHostName" header="DNS Host Name" sortable style="min-width: 200px" />
            <Column header="SPNs" style="min-width: 150px">
              <template #body="{ data }">
                <span v-if="data.servicePrincipalNames.length === 0" style="color: var(--p-text-muted-color)">None</span>
                <Tag v-else :value="data.servicePrincipalNames.length + ' SPN' + (data.servicePrincipalNames.length > 1 ? 's' : '')"
                     severity="secondary" />
              </template>
            </Column>
            <Column header="Password Interval" sortable sortField="managedPasswordIntervalInDays" style="width: 160px">
              <template #body="{ data }">
                {{ data.managedPasswordIntervalInDays }} days
              </template>
            </Column>
            <Column header="Next Rotation" sortable sortField="nextPasswordChange" style="width: 180px">
              <template #body="{ data }">
                <span style="color: var(--p-text-muted-color)">{{ relativeTime(data.nextPasswordChange) }}</span>
              </template>
            </Column>
            <Column header="Status" sortable sortField="isEnabled" style="width: 100px">
              <template #body="{ data }">
                <Tag :value="data.isEnabled ? 'Enabled' : 'Disabled'" :severity="data.isEnabled ? 'success' : 'danger'" />
              </template>
            </Column>
            <template #empty>
              <div style="text-align: center; padding: 2rem; color: var(--p-text-muted-color)">
                No gMSA accounts found. Create a KDS root key first, then create a gMSA account.
              </div>
            </template>
          </DataTable>
        </div>
      </TabPanel>

      <!-- ── KDS Root Keys Tab ──────────────────────────────────── -->
      <TabPanel header="KDS Root Keys">
        <div class="toolbar">
          <Button label="Create KDS Root Key" icon="pi pi-plus" size="small" @click="onCreateKdsKey"
                  :loading="creatingKds" />
        </div>

        <div v-if="loading" style="text-align: center; padding: 4rem">
          <ProgressSpinner />
        </div>

        <div v-else class="card" style="padding: 0">
          <DataTable :value="kdsKeys" stripedRows size="small" scrollable scrollHeight="calc(100vh - 340px)">
            <Column header="Key ID" sortable sortField="id" style="min-width: 300px">
              <template #body="{ data }">
                <span style="font-family: monospace; font-size: 0.875rem">{{ data.id }}</span>
              </template>
            </Column>
            <Column header="Algorithm" sortable sortField="kdfAlgorithm" style="width: 200px">
              <template #body="{ data }">
                <Tag :value="data.kdfAlgorithm" severity="info" />
              </template>
            </Column>
            <Column header="Effective Time" sortable sortField="effectiveTime" style="width: 200px">
              <template #body="{ data }">
                <span>{{ formatDate(data.effectiveTime) }}</span>
              </template>
            </Column>
            <Column header="Status" style="width: 120px">
              <template #body="{ data }">
                <Tag v-if="new Date(data.effectiveTime) <= new Date()" value="Active" severity="success" />
                <Tag v-else value="Pending" severity="warn" />
              </template>
            </Column>
            <Column header="Created" sortable sortField="createdAt" style="width: 180px">
              <template #body="{ data }">
                <span style="color: var(--p-text-muted-color)">{{ relativeTime(data.createdAt) }}</span>
              </template>
            </Column>
            <template #empty>
              <div style="text-align: center; padding: 2rem; color: var(--p-text-muted-color)">
                No KDS root keys found. Create one to enable gMSA password derivation.
                New keys become effective 10 hours after creation to allow replication.
              </div>
            </template>
          </DataTable>
        </div>
      </TabPanel>
    </TabView>

    <!-- ── Create Dialog ────────────────────────────────────────── -->
    <Dialog v-model:visible="createVisible" header="Create gMSA Account" modal :style="{ width: '600px' }">
      <p style="font-size: 0.8125rem; color: var(--p-text-muted-color); margin: 0 0 0.75rem 0">
        Create a Group Managed Service Account with automatic password rotation.
        Authorized principals (computers or groups) can retrieve the managed password.
      </p>
      <div style="display: flex; flex-direction: column; gap: 1rem; padding: 0.5rem 0">
        <div>
          <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">Account Name</label>
          <InputText v-model="newName" placeholder="e.g. svc-webapp" style="width: 100%" size="small"
                     @keyup.enter="onCreateAccount" />
          <small style="color: var(--p-text-muted-color)">A '$' suffix will be added automatically</small>
        </div>
        <div>
          <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">DNS Host Name</label>
          <InputText v-model="newDnsHostName" placeholder="svc-webapp.corp.example.com" style="width: 100%" size="small" />
        </div>
        <div>
          <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">Password Rotation Interval (days)</label>
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
        <div>
          <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">Authorized Principals</label>
          <div style="display: flex; gap: 0.5rem; margin-bottom: 0.5rem">
            <InputText v-model="newPrincipalInput" placeholder="CN=WebServers,OU=Groups,DC=corp,DC=example,DC=com"
                       style="flex: 1" size="small" @keyup.enter="addNewPrincipal" />
            <Button icon="pi pi-plus" size="small" @click="addNewPrincipal" :disabled="!newPrincipalInput" />
          </div>
          <div v-for="(p, idx) in newPrincipals" :key="idx"
               style="display: flex; align-items: center; gap: 0.5rem; padding: 0.25rem 0.5rem; margin-bottom: 0.25rem; background: var(--p-surface-100); border-radius: 4px; font-size: 0.875rem">
            <span style="flex: 1">{{ p }}</span>
            <Button icon="pi pi-times" size="small" severity="danger" text @click="newPrincipals.splice(idx, 1)" />
          </div>
          <small style="color: var(--p-text-muted-color)">DNs of computers or groups allowed to retrieve the password</small>
        </div>
      </div>
      <template #footer>
        <Button label="Cancel" severity="secondary" text @click="createVisible = false" />
        <Button label="Create" icon="pi pi-check" @click="onCreateAccount" :loading="creating"
                :disabled="!newName.trim()" />
      </template>
    </Dialog>

    <!-- ── Detail Dialog ────────────────────────────────────────── -->
    <Dialog v-model:visible="detailVisible" :header="detailAccount?.name || 'gMSA Details'" modal
            :style="{ width: '700px', maxHeight: '85vh' }">
      <div v-if="detailLoading" style="text-align: center; padding: 2rem"><ProgressSpinner /></div>
      <template v-else-if="detailAccount">
        <div style="display: flex; flex-direction: column; gap: 1rem">
          <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 1rem">
            <div>
              <label style="display: block; margin-bottom: 0.25rem; font-weight: 600; color: var(--p-text-muted-color)">Name</label>
              <span style="font-weight: 600">{{ detailAccount.name }}</span>
            </div>
            <div>
              <label style="display: block; margin-bottom: 0.25rem; font-weight: 600; color: var(--p-text-muted-color)">Status</label>
              <Tag :value="detailAccount.isEnabled ? 'Enabled' : 'Disabled'"
                   :severity="detailAccount.isEnabled ? 'success' : 'danger'" />
            </div>
            <div>
              <label style="display: block; margin-bottom: 0.25rem; font-weight: 600; color: var(--p-text-muted-color)">Distinguished Name</label>
              <span style="font-family: monospace; font-size: 0.85em; word-break: break-all">{{ detailAccount.distinguishedName }}</span>
            </div>
            <div>
              <label style="display: block; margin-bottom: 0.25rem; font-weight: 600; color: var(--p-text-muted-color)">DNS Host Name</label>
              <span>{{ detailAccount.dnsHostName || '-' }}</span>
            </div>
            <div>
              <label style="display: block; margin-bottom: 0.25rem; font-weight: 600; color: var(--p-text-muted-color)">Password Interval</label>
              <span>{{ detailAccount.managedPasswordIntervalInDays }} days</span>
            </div>
            <div>
              <label style="display: block; margin-bottom: 0.25rem; font-weight: 600; color: var(--p-text-muted-color)">Password Last Set</label>
              <span>{{ formatDate(detailAccount.passwordLastSet) }}</span>
            </div>
            <div>
              <label style="display: block; margin-bottom: 0.25rem; font-weight: 600; color: var(--p-text-muted-color)">Next Password Change</label>
              <span>{{ formatDate(detailAccount.nextPasswordChange) }}</span>
            </div>
            <div>
              <label style="display: block; margin-bottom: 0.25rem; font-weight: 600; color: var(--p-text-muted-color)">Created</label>
              <span>{{ formatDate(detailAccount.createdAt) }}</span>
            </div>
          </div>

          <!-- SPNs -->
          <div>
            <label style="display: block; margin-bottom: 0.5rem; font-weight: 600">Service Principal Names</label>
            <div v-if="detailAccount.servicePrincipalNames.length === 0" style="color: var(--p-text-muted-color)">No SPNs configured</div>
            <div v-for="spn in detailAccount.servicePrincipalNames" :key="spn"
                 style="padding: 0.25rem 0.5rem; margin-bottom: 0.25rem; background: var(--p-surface-100); border-radius: 4px; font-family: monospace; font-size: 0.875rem">
              {{ spn }}
            </div>
          </div>

          <!-- Authorized Principals -->
          <div>
            <label style="display: block; margin-bottom: 0.5rem; font-weight: 600">Authorized Principals</label>
            <div v-if="detailAccount.principalsAllowedToRetrievePassword.length === 0" style="color: var(--p-text-muted-color)">
              No principals configured
            </div>
            <div v-for="p in detailAccount.principalsAllowedToRetrievePassword" :key="p"
                 style="padding: 0.25rem 0.5rem; margin-bottom: 0.25rem; background: var(--p-surface-100); border-radius: 4px; font-size: 0.875rem">
              {{ p }}
            </div>
          </div>

          <!-- Actions -->
          <div style="display: flex; gap: 0.5rem; margin-top: 0.5rem">
            <Button label="Rotate Password" icon="pi pi-refresh" size="small" severity="warn" @click="showRotateConfirm" />
          </div>
        </div>
      </template>
      <template #footer>
        <Button label="Close" severity="secondary" text @click="detailVisible = false" />
      </template>
    </Dialog>

    <!-- ── Rotate Confirmation Dialog ───────────────────────────── -->
    <Dialog v-model:visible="rotateVisible" header="Rotate Password" modal :style="{ width: '450px' }">
      <p>
        Are you sure you want to force a password rotation for
        <strong>{{ detailAccount?.name || selectedAccount?.name }}</strong>?
      </p>
      <p style="color: var(--p-text-muted-color); font-size: 0.875rem">
        This will generate a new password immediately. Services using the current password
        will need to retrieve the updated credential.
      </p>
      <template #footer>
        <Button label="Cancel" severity="secondary" text @click="rotateVisible = false" />
        <Button label="Rotate" icon="pi pi-refresh" severity="warn" @click="onRotatePassword" :loading="rotating" />
      </template>
    </Dialog>
  </div>
</template>
