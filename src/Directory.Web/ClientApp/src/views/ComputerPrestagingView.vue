<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Textarea from 'primevue/textarea'
import Dialog from 'primevue/dialog'
import Tag from 'primevue/tag'
import TabView from 'primevue/tabview'
import TabPanel from 'primevue/tabpanel'
import ProgressBar from 'primevue/progressbar'
import ProgressSpinner from 'primevue/progressspinner'
import { useToast } from 'primevue/usetoast'
import {
  prestageComputer,
  bulkPrestageComputers,
  getPrestagedComputers,
  deletePrestagedComputer,
} from '../api/domainJoin'
import type { PrestagingRequest, PrestagingResult, PrestagedComputerSummary } from '../types/domainJoin'
import { relativeTime } from '../utils/format'

const toast = useToast()

// ── Data ────────────────────────────────────────────────────────
const loading = ref(true)
const prestagedComputers = ref<PrestagedComputerSummary[]>([])
const filterText = ref('')

// Pre-stage form
const formName = ref('')
const formDnsHostName = ref('')
const formOu = ref('')
const formManagedBy = ref('')
const formDescription = ref('')
const formOs = ref('')
const formAllowedToJoin = ref('')
const prestaging = ref(false)

// Bulk pre-stage
const bulkText = ref('')
const bulkOu = ref('')
const bulkManagedBy = ref('')
const bulkDescription = ref('')
const bulkResults = ref<PrestagingResult[]>([])
const bulkResultsVisible = ref(false)
const bulkRunning = ref(false)
const bulkProgress = ref(0)

// Delete
const selectedComputer = ref<PrestagedComputerSummary | null>(null)

// ── Computed ─────────────────────────────────────────────────────
const filteredComputers = computed(() => {
  if (!filterText.value) return prestagedComputers.value
  const q = filterText.value.toLowerCase()
  return prestagedComputers.value.filter(
    (c) =>
      c.name.toLowerCase().includes(q) ||
      c.dn.toLowerCase().includes(q) ||
      (c.managedBy?.toLowerCase().includes(q)) ||
      (c.description?.toLowerCase().includes(q))
  )
})

const canPrestage = computed(() =>
  formName.value.trim() !== '' && !prestaging.value
)

const bulkNames = computed(() => {
  return bulkText.value
    .split('\n')
    .map((l) => l.trim())
    .filter((l) => l.length > 0)
})

// ── Lifecycle ────────────────────────────────────────────────────
onMounted(() => loadComputers())

async function loadComputers() {
  loading.value = true
  try {
    prestagedComputers.value = await getPrestagedComputers()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    loading.value = false
  }
}

// ── Pre-stage single ────────────────────────────────────────────
async function onPrestage() {
  prestaging.value = true
  try {
    const request: PrestagingRequest = {
      computerName: formName.value.trim(),
      dnsHostName: formDnsHostName.value.trim() || undefined,
      organizationalUnit: formOu.value.trim() || undefined,
      managedBy: formManagedBy.value.trim() || undefined,
      description: formDescription.value.trim() || undefined,
      operatingSystem: formOs.value.trim() || undefined,
      allowedToJoin: formAllowedToJoin.value.trim()
        ? formAllowedToJoin.value.split('\n').map((l) => l.trim()).filter((l) => l)
        : undefined,
    }
    const result = await prestageComputer(request)
    if (result.success) {
      toast.add({
        severity: 'success',
        summary: 'Pre-staged',
        detail: `${formName.value} pre-staged at ${result.computerDn}`,
        life: 5000,
      })
      formName.value = ''
      formDnsHostName.value = ''
      formDescription.value = ''
      formOs.value = ''
      formAllowedToJoin.value = ''
      await loadComputers()
    } else {
      toast.add({ severity: 'error', summary: 'Error', detail: result.errorMessage, life: 5000 })
    }
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    prestaging.value = false
  }
}

// ── Bulk pre-stage ──────────────────────────────────────────────
async function onBulkPrestage() {
  if (bulkNames.value.length === 0) return
  bulkRunning.value = true
  bulkProgress.value = 0
  try {
    const requests: PrestagingRequest[] = bulkNames.value.map((name) => ({
      computerName: name,
      organizationalUnit: bulkOu.value.trim() || undefined,
      managedBy: bulkManagedBy.value.trim() || undefined,
      description: bulkDescription.value.trim() || undefined,
    }))
    bulkResults.value = await bulkPrestageComputers(requests)
    bulkProgress.value = 100
    bulkResultsVisible.value = true

    const successCount = bulkResults.value.filter((r) => r.success).length
    const failCount = bulkResults.value.length - successCount
    toast.add({
      severity: failCount === 0 ? 'success' : 'warn',
      summary: 'Bulk Pre-stage Complete',
      detail: `${successCount} succeeded, ${failCount} failed`,
      life: 5000,
    })
    await loadComputers()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    bulkRunning.value = false
  }
}

// ── Delete ──────────────────────────────────────────────────────
async function onDelete(computer: PrestagedComputerSummary) {
  if (!confirm(`Delete pre-staged computer ${computer.name}?`)) return
  try {
    await deletePrestagedComputer(computer.name)
    toast.add({ severity: 'success', summary: 'Deleted', detail: `${computer.name} deleted`, life: 3000 })
    await loadComputers()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}
</script>

<template>
  <div>
    <div class="page-header">
      <h1>Computer Pre-staging</h1>
      <p>Pre-create computer accounts so machines can join the domain with reduced privileges</p>
    </div>

    <div v-if="loading" style="text-align: center; padding: 4rem">
      <ProgressSpinner />
    </div>

    <div v-else>
      <TabView>
        <!-- Single Pre-stage Tab -->
        <TabPanel header="Pre-stage Computer" value="pre-stage-computer">
          <div class="card">
            <h2 style="margin: 0 0 1rem; font-size: 1rem; font-weight: 600">
              <i class="pi pi-plus-circle" style="margin-right: 0.5rem"></i>Pre-stage a Computer Account
            </h2>

            <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 1rem; margin-bottom: 1rem">
              <div>
                <label style="display: block; font-size: 0.8125rem; font-weight: 600; margin-bottom: 0.25rem">
                  Computer Name <span style="color: var(--app-danger-text)">*</span>
                </label>
                <InputText v-model="formName" placeholder="WORKSTATION1" style="width: 100%" maxlength="15" />
                <small style="color: var(--p-text-muted-color)">NetBIOS name, max 15 characters</small>
              </div>
              <div>
                <label style="display: block; font-size: 0.8125rem; font-weight: 600; margin-bottom: 0.25rem">
                  DNS Host Name
                </label>
                <InputText v-model="formDnsHostName" placeholder="workstation1.contoso.com" style="width: 100%" />
              </div>
            </div>

            <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 1rem; margin-bottom: 1rem">
              <div>
                <label style="display: block; font-size: 0.8125rem; font-weight: 600; margin-bottom: 0.25rem">
                  Target OU
                </label>
                <InputText v-model="formOu" placeholder="OU=Workstations,DC=contoso,DC=com" style="width: 100%" />
                <small style="color: var(--p-text-muted-color)">Leave empty for default Computers container</small>
              </div>
              <div>
                <label style="display: block; font-size: 0.8125rem; font-weight: 600; margin-bottom: 0.25rem">
                  Managed By
                </label>
                <InputText v-model="formManagedBy" placeholder="CN=Admin,CN=Users,DC=contoso,DC=com" style="width: 100%" />
                <small style="color: var(--p-text-muted-color)">DN of user/group who manages this computer</small>
              </div>
            </div>

            <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 1rem; margin-bottom: 1rem">
              <div>
                <label style="display: block; font-size: 0.8125rem; font-weight: 600; margin-bottom: 0.25rem">
                  Description
                </label>
                <InputText v-model="formDescription" placeholder="Finance department workstation" style="width: 100%" />
              </div>
              <div>
                <label style="display: block; font-size: 0.8125rem; font-weight: 600; margin-bottom: 0.25rem">
                  Operating System
                </label>
                <InputText v-model="formOs" placeholder="Windows 11 Enterprise" style="width: 100%" />
              </div>
            </div>

            <div style="margin-bottom: 1.5rem">
              <label style="display: block; font-size: 0.8125rem; font-weight: 600; margin-bottom: 0.25rem">
                Allowed to Join (one DN per line)
              </label>
              <Textarea
                v-model="formAllowedToJoin"
                placeholder="CN=HelpDesk,OU=Groups,DC=contoso,DC=com"
                rows="3"
                style="width: 100%"
              />
              <small style="color: var(--p-text-muted-color)">DNs of users/groups authorized to join this specific machine</small>
            </div>

            <Button
              label="Pre-stage Computer"
              icon="pi pi-plus"
              :loading="prestaging"
              :disabled="!canPrestage"
              @click="onPrestage"
            />
          </div>
        </TabPanel>

        <!-- Bulk Pre-stage Tab -->
        <TabPanel header="Bulk Pre-stage" value="bulk-pre-stage">
          <div class="card">
            <h2 style="margin: 0 0 0.5rem; font-size: 1rem; font-weight: 600">
              <i class="pi pi-list" style="margin-right: 0.5rem"></i>Bulk Pre-stage
            </h2>
            <p style="margin: 0 0 1rem; font-size: 0.8125rem; color: var(--p-text-muted-color)">
              Enter one computer name per line or paste from a CSV. All computers will share the same OU and managed-by settings.
            </p>

            <div style="margin-bottom: 1rem">
              <label style="display: block; font-size: 0.8125rem; font-weight: 600; margin-bottom: 0.25rem">
                Computer Names (one per line) <span style="color: var(--app-danger-text)">*</span>
              </label>
              <Textarea
                v-model="bulkText"
                placeholder="WS-001&#10;WS-002&#10;WS-003"
                rows="8"
                style="width: 100%"
              />
              <small style="color: var(--p-text-muted-color)">{{ bulkNames.length }} computer(s) to pre-stage</small>
            </div>

            <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 1rem; margin-bottom: 1rem">
              <div>
                <label style="display: block; font-size: 0.8125rem; font-weight: 600; margin-bottom: 0.25rem">
                  Shared Target OU
                </label>
                <InputText v-model="bulkOu" placeholder="OU=Workstations,DC=contoso,DC=com" style="width: 100%" />
              </div>
              <div>
                <label style="display: block; font-size: 0.8125rem; font-weight: 600; margin-bottom: 0.25rem">
                  Shared Managed By
                </label>
                <InputText v-model="bulkManagedBy" placeholder="CN=Admin,CN=Users,DC=contoso,DC=com" style="width: 100%" />
              </div>
            </div>

            <div style="margin-bottom: 1.5rem">
              <label style="display: block; font-size: 0.8125rem; font-weight: 600; margin-bottom: 0.25rem">
                Shared Description
              </label>
              <InputText v-model="bulkDescription" placeholder="Bulk pre-staged workstations" style="width: 100%" />
            </div>

            <div style="display: flex; align-items: center; gap: 1rem">
              <Button
                label="Bulk Pre-stage"
                icon="pi pi-bolt"
                :loading="bulkRunning"
                :disabled="bulkNames.length === 0 || bulkRunning"
                @click="onBulkPrestage"
              />
              <ProgressBar
                v-if="bulkRunning"
                :value="bulkProgress"
                style="flex: 1; height: 1.5rem"
                :showValue="true"
                mode="indeterminate"
              />
            </div>
          </div>
        </TabPanel>

        <!-- Pre-staged Computers Tab -->
        <TabPanel header="Pre-staged Computers" value="pre-staged-computers">
          <div class="toolbar">
            <Button icon="pi pi-refresh" size="small" severity="secondary" text
                    @click="loadComputers" v-tooltip="'Refresh'" />
            <div class="toolbar-spacer" />
            <InputText v-model="filterText" placeholder="Search pre-staged computers..." size="small" style="width: 280px" />
          </div>

          <div class="card" style="padding: 0">
            <DataTable
              :value="filteredComputers"
              v-model:selection="selectedComputer"
              selectionMode="single"
              dataKey="objectGuid"
              stripedRows
              size="small"
              scrollable
              scrollHeight="calc(100vh - 320px)"
              :paginator="filteredComputers.length > 50"
              :rows="50"
              :rowsPerPageOptions="[25, 50, 100]"
            >
              <Column header="Name" sortable sortField="name" style="min-width: 160px">
                <template #body="{ data }">
                  <div style="display: flex; align-items: center; gap: 0.5rem">
                    <i class="pi pi-desktop" style="color: var(--p-text-muted-color)"></i>
                    <span>{{ data.name }}</span>
                  </div>
                </template>
              </Column>
              <Column field="dn" header="Distinguished Name" sortable style="min-width: 280px">
                <template #body="{ data }">
                  <span style="font-size: 0.8125rem; color: var(--p-text-muted-color); word-break: break-all">{{ data.dn }}</span>
                </template>
              </Column>
              <Column field="managedBy" header="Managed By" style="min-width: 200px">
                <template #body="{ data }">
                  <span style="font-size: 0.8125rem; color: var(--p-text-muted-color)">{{ data.managedBy || '' }}</span>
                </template>
              </Column>
              <Column header="Created" sortable sortField="whenCreated" style="width: 130px">
                <template #body="{ data }">
                  <span style="color: var(--p-text-muted-color)">{{ relativeTime(data.whenCreated) }}</span>
                </template>
              </Column>
              <Column header="Status" style="width: 100px">
                <template #body>
                  <Tag value="Disabled" severity="warn" />
                </template>
              </Column>
              <Column header="Actions" style="width: 80px">
                <template #body="{ data }">
                  <Button
                    icon="pi pi-trash"
                    size="small"
                    severity="danger"
                    text
                    @click="onDelete(data)"
                    v-tooltip="'Delete pre-staged account'"
                  />
                </template>
              </Column>
              <template #empty>
                <div style="text-align: center; padding: 2rem; color: var(--p-text-muted-color)">
                  <i class="pi pi-info-circle" style="font-size: 1.5rem; display: block; margin-bottom: 0.5rem"></i>
                  <p style="margin: 0; font-size: 0.875rem">No pre-staged computers found. Use the Pre-stage tab to create accounts ahead of time.</p>
                </div>
              </template>
            </DataTable>
          </div>
        </TabPanel>
      </TabView>
    </div>

    <!-- Bulk Results Dialog -->
    <Dialog
      v-model:visible="bulkResultsVisible"
      header="Bulk Pre-stage Results"
      :style="{ width: '50rem' }"
      modal
    >
      <DataTable :value="bulkResults" size="small" stripedRows>
        <Column field="samAccountName" header="Account" style="min-width: 160px" />
        <Column header="Status" style="width: 100px">
          <template #body="{ data }">
            <Tag :value="data.success ? 'Success' : 'Failed'" :severity="data.success ? 'success' : 'danger'" />
          </template>
        </Column>
        <Column field="computerDn" header="DN" style="min-width: 250px">
          <template #body="{ data }">
            <span style="font-size: 0.8125rem; word-break: break-all">{{ data.computerDn || '' }}</span>
          </template>
        </Column>
        <Column field="errorMessage" header="Error">
          <template #body="{ data }">
            <span v-if="data.errorMessage" style="color: var(--app-danger-text); font-size: 0.8125rem">{{ data.errorMessage }}</span>
          </template>
        </Column>
      </DataTable>
      <template #footer>
        <Button label="Close" @click="bulkResultsVisible = false" />
      </template>
    </Dialog>
  </div>
</template>
