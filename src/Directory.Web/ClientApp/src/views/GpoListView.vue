<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Tag from 'primevue/tag'
import Dialog from 'primevue/dialog'
import TabView from 'primevue/tabview'
import TabPanel from 'primevue/tabpanel'
import InputNumber from 'primevue/inputnumber'
import Checkbox from 'primevue/checkbox'
import Select from 'primevue/select'
import Textarea from 'primevue/textarea'
import ProgressSpinner from 'primevue/progressspinner'
import { useToast } from 'primevue/usetoast'
import {
  listGpos, getGpo, createGpo, updateGpo, deleteGpo,
  linkGpo, unlinkGpo, getRsop,
  type GpoSummary, type GpoDetail, type GpoPolicySettings, type RsopResult
} from '../api/gpos'
import { fetchSysvolConfig } from '../api/sysvol'
import { searchObjects } from '../api/objects'
import { relativeTime } from '../utils/format'

const toast = useToast()
const gpos = ref<GpoSummary[]>([])
const loading = ref(true)
const filterText = ref('')
const selectedGpo = ref<GpoSummary | null>(null)

// Create dialog
const createDialogVisible = ref(false)
const newGpoName = ref('')
const creating = ref(false)

// Detail dialog
const detailVisible = ref(false)
const detail = ref<GpoDetail | null>(null)
const detailLoading = ref(false)
const saving = ref(false)
const editDisplayName = ref('')
const editFlags = ref(0)
const editSettings = ref<GpoPolicySettings>({})

// Link dialog
const linkDialogVisible = ref(false)
const linkTargetDn = ref('')
const linkEnforced = ref(false)
const linkSearchQuery = ref('')
const linkSearchResults = ref<{ dn: string; name?: string; objectClass: string }[]>([])
const linkSearching = ref(false)

// RSoP dialog
const rsopDialogVisible = ref(false)
const rsopUserDn = ref('')
const rsopComputerDn = ref('')
const rsopResult = ref<RsopResult | null>(null)
const rsopLoading = ref(false)

// SYSVOL config check
const sysvolConfigured = ref(false)

onMounted(() => {
  loadGpos()
  checkSysvolConfig()
})

async function checkSysvolConfig() {
  try {
    const config = await fetchSysvolConfig()
    sysvolConfigured.value = !!(config.sysvolSharePath && config.smbServerHostname)
  } catch {
    sysvolConfigured.value = false
  }
}

async function loadGpos() {
  loading.value = true
  try {
    gpos.value = await listGpos()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    loading.value = false
  }
}

const filteredGpos = computed(() => {
  if (!filterText.value) return gpos.value
  const q = filterText.value.toLowerCase()
  return gpos.value.filter(
    (g) => g.displayName.toLowerCase().includes(q) || g.cn.toLowerCase().includes(q)
  )
})

// Create GPO
async function onCreateGpo() {
  if (!newGpoName.value.trim()) return
  creating.value = true
  try {
    await createGpo(newGpoName.value.trim())
    toast.add({ severity: 'success', summary: 'Created', detail: 'GPO created successfully', life: 3000 })
    createDialogVisible.value = false
    newGpoName.value = ''
    await loadGpos()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    creating.value = false
  }
}

// Open detail
async function openDetail(gpo: GpoSummary) {
  detailLoading.value = true
  detailVisible.value = true
  try {
    detail.value = await getGpo(gpo.objectGuid)
    editDisplayName.value = detail.value.displayName
    editFlags.value = detail.value.flags
    editSettings.value = JSON.parse(JSON.stringify(detail.value.policySettings || {}))
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    detailLoading.value = false
  }
}

function onRowDoubleClick(event: { data: GpoSummary }) {
  openDetail(event.data)
}

// Save GPO settings
async function saveGpo() {
  if (!detail.value) return
  saving.value = true
  try {
    detail.value = await updateGpo(detail.value.objectGuid, {
      displayName: editDisplayName.value,
      flags: editFlags.value,
      policySettings: editSettings.value,
    })
    toast.add({ severity: 'success', summary: 'Saved', detail: 'GPO updated successfully', life: 3000 })
    await loadGpos()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    saving.value = false
  }
}

// Delete GPO
async function onDeleteGpo() {
  if (!selectedGpo.value) return
  if (!confirm(`Delete GPO "${selectedGpo.value.displayName}"? This will remove all links.`)) return
  try {
    await deleteGpo(selectedGpo.value.objectGuid)
    toast.add({ severity: 'success', summary: 'Deleted', detail: 'GPO deleted', life: 3000 })
    selectedGpo.value = null
    detailVisible.value = false
    await loadGpos()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

// Link GPO
function openLinkDialog() {
  if (!detail.value) return
  linkDialogVisible.value = true
  linkTargetDn.value = ''
  linkEnforced.value = false
  linkSearchQuery.value = ''
  linkSearchResults.value = []
}

let linkSearchTimeout: ReturnType<typeof setTimeout> | null = null

function onLinkSearchInput() {
  if (linkSearchTimeout) clearTimeout(linkSearchTimeout)
  if (!linkSearchQuery.value || linkSearchQuery.value.length < 2) {
    linkSearchResults.value = []
    return
  }
  linkSearchTimeout = setTimeout(() => searchLinkTargets(), 300)
}

async function searchLinkTargets() {
  if (!linkSearchQuery.value || linkSearchQuery.value.length < 2) return
  linkSearching.value = true
  try {
    const result = await searchObjects('',
      `(|(objectClass=organizationalUnit)(objectClass=domainDNS)(objectClass=site))`, 50)
    const q = linkSearchQuery.value.toLowerCase()
    linkSearchResults.value = result.items
      .filter((r) => (r.name?.toLowerCase().includes(q)) || r.dn.toLowerCase().includes(q))
      .map((r) => ({ dn: r.dn, name: r.name, objectClass: r.objectClass }))
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    linkSearching.value = false
  }
}

async function onSelectLinkTarget(target: { dn: string; name?: string }) {
  linkTargetDn.value = target.dn
  linkSearchResults.value = []
  linkSearchQuery.value = target.name || target.dn
}

async function onLinkGpo() {
  if (!detail.value || !linkTargetDn.value) return
  try {
    await linkGpo(detail.value.objectGuid, linkTargetDn.value, linkEnforced.value)
    toast.add({ severity: 'success', summary: 'Linked', detail: 'GPO linked to target', life: 3000 })
    linkDialogVisible.value = false
    // Refresh detail
    detail.value = await getGpo(detail.value.objectGuid)
    await loadGpos()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

async function onUnlinkGpo(targetDn: string) {
  if (!detail.value) return
  try {
    await unlinkGpo(detail.value.objectGuid, targetDn)
    toast.add({ severity: 'success', summary: 'Unlinked', detail: 'GPO unlinked', life: 3000 })
    detail.value = await getGpo(detail.value.objectGuid)
    await loadGpos()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

// RSoP
async function computeRsop() {
  if (!rsopUserDn.value && !rsopComputerDn.value) {
    toast.add({ severity: 'warn', summary: 'Input required', detail: 'Enter at least a user or computer DN', life: 3000 })
    return
  }
  rsopLoading.value = true
  try {
    rsopResult.value = await getRsop(rsopUserDn.value || undefined, rsopComputerDn.value || undefined)
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    rsopLoading.value = false
  }
}

function gpoStatusSeverity(gpo: GpoSummary): string {
  if (gpo.isUserEnabled && gpo.isComputerEnabled) return 'success'
  if (!gpo.isUserEnabled && !gpo.isComputerEnabled) return 'danger'
  return 'warn'
}

function gpoStatusLabel(gpo: GpoSummary): string {
  if (gpo.isUserEnabled && gpo.isComputerEnabled) return 'Enabled'
  if (!gpo.isUserEnabled && !gpo.isComputerEnabled) return 'All Disabled'
  if (!gpo.isUserEnabled) return 'User Disabled'
  return 'Computer Disabled'
}

// Audit policy options
const auditOptions = [
  { label: 'No auditing', value: 0 },
  { label: 'Success', value: 1 },
  { label: 'Failure', value: 2 },
  { label: 'Success + Failure', value: 3 },
]

function ensurePasswordPolicy() {
  if (!editSettings.value.passwordPolicy) editSettings.value.passwordPolicy = {}
}
function ensureAccountLockout() {
  if (!editSettings.value.accountLockout) editSettings.value.accountLockout = {}
}
function ensureAuditPolicy() {
  if (!editSettings.value.auditPolicy) editSettings.value.auditPolicy = {}
}
function ensureSecurityOptions() {
  if (!editSettings.value.securityOptions) editSettings.value.securityOptions = {}
}
</script>

<template>
  <div>
    <div class="page-header">
      <h1>Group Policy Objects</h1>
      <p>Manage Group Policy Objects and their settings</p>
    </div>

    <!-- SYSVOL warning banner -->
    <div v-if="!loading && !sysvolConfigured" class="card" style="margin-bottom: 1rem; border-color: var(--p-orange-300); background: var(--p-orange-50)">
      <div style="display: flex; align-items: center; gap: 0.75rem; color: var(--p-orange-700)">
        <i class="pi pi-exclamation-triangle" style="font-size: 1.25rem"></i>
        <div>
          <strong>SYSVOL share not configured.</strong> Group Policy files cannot be distributed.
          <router-link to="/sysvol" style="margin-left: 0.25rem">Configure SYSVOL in Domain Settings.</router-link>
        </div>
      </div>
    </div>

    <div class="toolbar">
      <Button label="Create GPO" icon="pi pi-plus" size="small" @click="createDialogVisible = true" />
      <Button label="Edit" icon="pi pi-pencil" size="small" severity="secondary" outlined
              @click="selectedGpo && openDetail(selectedGpo)" :disabled="!selectedGpo"
              v-tooltip="'Edit settings for the selected GPO'" />
      <Button icon="pi pi-trash" size="small" severity="danger" text
              @click="onDeleteGpo" :disabled="!selectedGpo"
              v-tooltip="'Delete the selected GPO and remove all its links'" />
      <div class="toolbar-spacer" />
      <Button label="RSoP Calculator" icon="pi pi-calculator" size="small" severity="secondary" outlined
              @click="rsopDialogVisible = true"
              v-tooltip="'Calculate the Resultant Set of Policy for a user or computer'" />
      <InputText v-model="filterText" placeholder="Search GPOs..." size="small" style="width: 260px" />
    </div>

    <div v-if="loading" style="text-align: center; padding: 4rem">
      <ProgressSpinner />
    </div>

    <div v-else class="card" style="padding: 0">
      <DataTable
        :value="filteredGpos"
        v-model:selection="selectedGpo"
        selectionMode="single"
        dataKey="objectGuid"
        stripedRows
        size="small"
        scrollable
        scrollHeight="calc(100vh - 260px)"
        :paginator="filteredGpos.length > 50"
        :rows="50"
        :rowsPerPageOptions="[25, 50, 100]"
        @row-dblclick="onRowDoubleClick"
      >
        <Column header="Name" sortable sortField="displayName" style="min-width: 280px">
          <template #body="{ data }">
            <div style="display: flex; align-items: center; gap: 0.5rem">
              <i class="pi pi-file-edit" style="color: var(--p-text-muted-color)"></i>
              <span>{{ data.displayName }}</span>
            </div>
          </template>
        </Column>
        <Column header="Status" sortable sortField="flags" style="width: 160px">
          <template #body="{ data }">
            <Tag :value="gpoStatusLabel(data)" :severity="gpoStatusSeverity(data)" />
          </template>
        </Column>
        <Column field="linkCount" header="Links" sortable style="width: 80px" />
        <Column field="versionNumber" header="Version" sortable style="width: 100px" />
        <Column header="GUID" style="width: 300px">
          <template #body="{ data }">
            <span style="color: var(--p-text-muted-color); font-family: monospace; font-size: 0.85em">{{ data.cn }}</span>
          </template>
        </Column>
        <Column header="Modified" sortable sortField="whenChanged" style="width: 130px">
          <template #body="{ data }">
            <span style="color: var(--p-text-muted-color)">{{ relativeTime(data.whenChanged) }}</span>
          </template>
        </Column>
        <template #empty>
          <div style="text-align: center; padding: 2rem; color: var(--p-text-muted-color)">
            <i class="pi pi-info-circle" style="font-size: 1.5rem; display: block; margin-bottom: 0.5rem"></i>
            <p style="margin: 0; font-size: 0.875rem">No Group Policy Objects found. Use the <strong>Create GPO</strong> button to add a new policy, or adjust your search filter.</p>
          </div>
        </template>
      </DataTable>
    </div>

    <!-- Create GPO Dialog -->
    <Dialog v-model:visible="createDialogVisible" header="Create Group Policy Object" modal :style="{ width: '450px' }">
      <div style="display: flex; flex-direction: column; gap: 1rem; padding: 0.5rem 0">
        <div>
          <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">Display Name</label>
          <InputText v-model="newGpoName" placeholder="e.g. Default Domain Policy" style="width: 100%" size="small"
                     @keyup.enter="onCreateGpo" />
        </div>
      </div>
      <template #footer>
        <Button label="Cancel" severity="secondary" text @click="createDialogVisible = false" />
        <Button label="Create" icon="pi pi-check" @click="onCreateGpo" :loading="creating"
                :disabled="!newGpoName.trim()" />
      </template>
    </Dialog>

    <!-- GPO Detail Dialog -->
    <Dialog v-model:visible="detailVisible" :header="detail?.displayName || 'GPO Details'" modal
            :style="{ width: '900px', maxHeight: '85vh' }">
      <div v-if="detailLoading" style="text-align: center; padding: 2rem">
        <ProgressSpinner />
      </div>
      <template v-else-if="detail">
        <TabView>
          <!-- General Tab -->
          <TabPanel header="General">
            <div style="display: flex; flex-direction: column; gap: 1rem">
              <div>
                <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">Display Name</label>
                <InputText v-model="editDisplayName" style="width: 100%" size="small" />
              </div>
              <div>
                <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">GUID</label>
                <span style="font-family: monospace; color: var(--p-text-muted-color)">{{ detail.cn }}</span>
              </div>
              <div>
                <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">SYSVOL File Path</label>
                <span v-if="detail.gpcFileSysPath" style="font-family: monospace; font-size: 0.85em; color: var(--p-text-muted-color)">{{ detail.gpcFileSysPath }}</span>
                <span v-else style="color: var(--p-text-muted-color); font-style: italic">Not configured</span>
              </div>
              <div style="display: flex; gap: 2rem">
                <div>
                  <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">Status</label>
                  <Select v-model="editFlags" :options="[
                    { label: 'All settings enabled', value: 0 },
                    { label: 'User settings disabled', value: 1 },
                    { label: 'Computer settings disabled', value: 2 },
                    { label: 'All settings disabled', value: 3 },
                  ]" optionLabel="label" optionValue="value" size="small" style="width: 250px" />
                </div>
                <div>
                  <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">Version</label>
                  <span>{{ detail.versionNumber }} (User: {{ detail.userVersion }}, Computer: {{ detail.computerVersion }})</span>
                </div>
              </div>
            </div>
          </TabPanel>

          <!-- Links Tab -->
          <TabPanel header="Links">
            <div style="margin-bottom: 1rem">
              <Button label="Link to OU/Domain" icon="pi pi-link" size="small" @click="openLinkDialog" />
            </div>
            <DataTable :value="detail.links" stripedRows size="small" scrollable scrollHeight="300px">
              <Column header="Target" style="min-width: 300px">
                <template #body="{ data }">
                  <div style="display: flex; align-items: center; gap: 0.5rem">
                    <i class="pi pi-folder" style="color: var(--p-text-muted-color)"></i>
                    <span>{{ data.targetName }}</span>
                  </div>
                </template>
              </Column>
              <Column header="DN" style="min-width: 300px">
                <template #body="{ data }">
                  <span style="color: var(--p-text-muted-color); font-size: 0.85em">{{ data.targetDn }}</span>
                </template>
              </Column>
              <Column header="Enforced" style="width: 100px">
                <template #body="{ data }">
                  <Tag v-if="data.isEnforced" value="Enforced" severity="warn" />
                </template>
              </Column>
              <Column style="width: 60px">
                <template #body="{ data }">
                  <Button icon="pi pi-times" size="small" severity="danger" text
                          @click="onUnlinkGpo(data.targetDn)" />
                </template>
              </Column>
              <template #empty>
                <div style="text-align: center; padding: 1rem; color: var(--p-text-muted-color)">Not linked to any container</div>
              </template>
            </DataTable>
          </TabPanel>

          <!-- Security Filtering Tab -->
          <TabPanel header="Security Filtering">
            <div v-if="detail.securityFiltering.length === 0"
                 style="text-align: center; padding: 2rem; color: var(--p-text-muted-color)">
              <i class="pi pi-info-circle" style="font-size: 1.5rem; display: block; margin-bottom: 0.5rem"></i>
              <p style="margin: 0; font-size: 0.875rem">No security filtering configured. This GPO applies to all authenticated users by default.</p>
            </div>
            <DataTable v-else :value="detail.securityFiltering.map((s) => ({ dn: s }))" stripedRows size="small">
              <Column header="Security Principal" field="dn" />
            </DataTable>
          </TabPanel>

          <!-- Password Policy Tab -->
          <TabPanel header="Password Policy">
            <div style="display: flex; flex-direction: column; gap: 1rem" @focus.capture="ensurePasswordPolicy">
              <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 1rem">
                <div>
                  <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">Minimum Password Length</label>
                  <InputNumber v-model="(editSettings.passwordPolicy ??= {}).minimumLength" :min="0" :max="128"
                               size="small" style="width: 100%" @focus="ensurePasswordPolicy" />
                </div>
                <div>
                  <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">Password History Count</label>
                  <InputNumber v-model="(editSettings.passwordPolicy ??= {}).historyCount" :min="0" :max="24"
                               size="small" style="width: 100%" @focus="ensurePasswordPolicy" />
                </div>
                <div>
                  <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">Max Password Age (days)</label>
                  <InputNumber v-model="(editSettings.passwordPolicy ??= {}).maxAgeDays" :min="0" :max="999"
                               size="small" style="width: 100%" @focus="ensurePasswordPolicy" />
                </div>
                <div>
                  <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">Min Password Age (days)</label>
                  <InputNumber v-model="(editSettings.passwordPolicy ??= {}).minAgeDays" :min="0" :max="998"
                               size="small" style="width: 100%" @focus="ensurePasswordPolicy" />
                </div>
              </div>
              <div style="display: flex; gap: 2rem">
                <div style="display: flex; align-items: center; gap: 0.5rem">
                  <Checkbox v-model="(editSettings.passwordPolicy ??= {}).complexityEnabled" :binary="true"
                            @change="ensurePasswordPolicy" />
                  <label>Password must meet complexity requirements</label>
                </div>
                <div style="display: flex; align-items: center; gap: 0.5rem">
                  <Checkbox v-model="(editSettings.passwordPolicy ??= {}).reversibleEncryption" :binary="true"
                            @change="ensurePasswordPolicy" />
                  <label>Store passwords using reversible encryption</label>
                </div>
              </div>
            </div>
          </TabPanel>

          <!-- Account Lockout Tab -->
          <TabPanel header="Account Lockout">
            <div style="display: grid; grid-template-columns: 1fr 1fr 1fr; gap: 1rem" @focus.capture="ensureAccountLockout">
              <div>
                <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">Lockout Threshold</label>
                <InputNumber v-model="(editSettings.accountLockout ??= {}).threshold" :min="0" :max="999"
                             size="small" style="width: 100%" @focus="ensureAccountLockout" />
                <small style="color: var(--p-text-muted-color)">0 = never locked out</small>
              </div>
              <div>
                <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">Duration (minutes)</label>
                <InputNumber v-model="(editSettings.accountLockout ??= {}).durationMinutes" :min="0" :max="99999"
                             size="small" style="width: 100%" @focus="ensureAccountLockout" />
                <small style="color: var(--p-text-muted-color)">0 = until admin unlocks</small>
              </div>
              <div>
                <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">Observation Window (minutes)</label>
                <InputNumber v-model="(editSettings.accountLockout ??= {}).observationWindowMinutes" :min="0" :max="99999"
                             size="small" style="width: 100%" @focus="ensureAccountLockout" />
              </div>
            </div>
          </TabPanel>

          <!-- Audit Policy Tab -->
          <TabPanel header="Audit Policy">
            <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 1rem" @focus.capture="ensureAuditPolicy">
              <div v-for="field in [
                { key: 'auditLogonEvents', label: 'Audit Logon Events' },
                { key: 'auditObjectAccess', label: 'Audit Object Access' },
                { key: 'auditPrivilegeUse', label: 'Audit Privilege Use' },
                { key: 'auditPolicyChange', label: 'Audit Policy Change' },
                { key: 'auditAccountManagement', label: 'Audit Account Management' },
                { key: 'auditProcessTracking', label: 'Audit Process Tracking' },
                { key: 'auditDsAccess', label: 'Audit Directory Service Access' },
                { key: 'auditAccountLogon', label: 'Audit Account Logon Events' },
                { key: 'auditSystemEvents', label: 'Audit System Events' },
              ]" :key="field.key">
                <div>
                  <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">{{ field.label }}</label>
                  <Select v-model="(editSettings.auditPolicy ??= {} as any)[field.key]" :options="auditOptions"
                          optionLabel="label" optionValue="value" size="small" style="width: 100%"
                          placeholder="Not configured" @focus="ensureAuditPolicy" />
                </div>
              </div>
            </div>
          </TabPanel>

          <!-- Security Options Tab -->
          <TabPanel header="Security Options">
            <div style="display: flex; flex-direction: column; gap: 1rem" @focus.capture="ensureSecurityOptions">
              <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 1rem">
                <div>
                  <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">LAN Manager Auth Level</label>
                  <Select v-model="(editSettings.securityOptions ??= {}).lanManagerAuthLevel" :options="[
                    { label: 'Send LM & NTLM responses', value: 0 },
                    { label: 'Send LM & NTLM - use NTLMv2 if negotiated', value: 1 },
                    { label: 'Send NTLM response only', value: 2 },
                    { label: 'Send NTLMv2 response only', value: 3 },
                    { label: 'Send NTLMv2 - refuse LM', value: 4 },
                    { label: 'Send NTLMv2 - refuse LM & NTLM', value: 5 },
                  ]" optionLabel="label" optionValue="value" size="small" style="width: 100%"
                     placeholder="Not configured" @focus="ensureSecurityOptions" />
                </div>
                <div>
                  <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">LDAP Client Signing</label>
                  <Select v-model="(editSettings.securityOptions ??= {}).ldapClientSigningRequirement" :options="[
                    { label: 'None', value: 0 },
                    { label: 'Negotiate signing', value: 1 },
                    { label: 'Require signing', value: 2 },
                  ]" optionLabel="label" optionValue="value" size="small" style="width: 100%"
                     placeholder="Not configured" @focus="ensureSecurityOptions" />
                </div>
              </div>
              <div style="display: flex; gap: 2rem">
                <div style="display: flex; align-items: center; gap: 0.5rem">
                  <Checkbox v-model="(editSettings.securityOptions ??= {}).requireSmbSigning" :binary="true"
                            @change="ensureSecurityOptions" />
                  <label>Require SMB Signing</label>
                </div>
                <div style="display: flex; align-items: center; gap: 0.5rem">
                  <Checkbox v-model="(editSettings.securityOptions ??= {}).enableGuestAccount" :binary="true"
                            @change="ensureSecurityOptions" />
                  <label>Enable Guest Account</label>
                </div>
              </div>
              <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 1rem">
                <div>
                  <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">Rename Administrator Account</label>
                  <InputText v-model="(editSettings.securityOptions ??= {}).renameAdministratorAccount"
                             size="small" style="width: 100%" placeholder="Not configured" @focus="ensureSecurityOptions" />
                </div>
                <div>
                  <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">Rename Guest Account</label>
                  <InputText v-model="(editSettings.securityOptions ??= {}).renameGuestAccount"
                             size="small" style="width: 100%" placeholder="Not configured" @focus="ensureSecurityOptions" />
                </div>
              </div>
            </div>
          </TabPanel>

          <!-- Drive Mappings Tab -->
          <TabPanel header="Drive Mappings & Scripts">
            <div style="margin-bottom: 1rem">
              <h4 style="margin: 0 0 0.5rem 0">Drive Mappings</h4>
              <Button label="Add Drive Mapping" icon="pi pi-plus" size="small"
                      @click="(editSettings.driveMappings ??= []).push({ driveLetter: '', uncPath: '', action: 'Create', reconnect: true })" />
            </div>
            <DataTable v-if="editSettings.driveMappings && editSettings.driveMappings.length > 0"
                       :value="editSettings.driveMappings" stripedRows size="small">
              <Column header="Drive" style="width: 80px">
                <template #body="{ data }">
                  <InputText v-model="data.driveLetter" size="small" style="width: 60px" placeholder="H:" />
                </template>
              </Column>
              <Column header="UNC Path" style="min-width: 250px">
                <template #body="{ data }">
                  <InputText v-model="data.uncPath" size="small" style="width: 100%" placeholder="\\server\share" />
                </template>
              </Column>
              <Column header="Label" style="width: 150px">
                <template #body="{ data }">
                  <InputText v-model="data.label" size="small" style="width: 100%" />
                </template>
              </Column>
              <Column header="Action" style="width: 120px">
                <template #body="{ data }">
                  <Select v-model="data.action" :options="['Create', 'Replace', 'Update', 'Delete']"
                          size="small" style="width: 100%" />
                </template>
              </Column>
              <Column style="width: 50px">
                <template #body="{ index }">
                  <Button icon="pi pi-times" size="small" severity="danger" text
                          @click="editSettings.driveMappings?.splice(index, 1)" />
                </template>
              </Column>
            </DataTable>

            <div style="margin-top: 1.5rem; margin-bottom: 1rem">
              <h4 style="margin: 0 0 0.5rem 0">Logon Scripts</h4>
              <Button label="Add Script" icon="pi pi-plus" size="small"
                      @click="(editSettings.logonScripts ??= []).push({ path: '', order: (editSettings.logonScripts?.length ?? 0) + 1 })" />
            </div>
            <DataTable v-if="editSettings.logonScripts && editSettings.logonScripts.length > 0"
                       :value="editSettings.logonScripts" stripedRows size="small">
              <Column header="Path" style="min-width: 300px">
                <template #body="{ data }">
                  <InputText v-model="data.path" size="small" style="width: 100%" placeholder="\\server\scripts\logon.bat" />
                </template>
              </Column>
              <Column header="Parameters" style="width: 200px">
                <template #body="{ data }">
                  <InputText v-model="data.parameters" size="small" style="width: 100%" />
                </template>
              </Column>
              <Column style="width: 50px">
                <template #body="{ index }">
                  <Button icon="pi pi-times" size="small" severity="danger" text
                          @click="editSettings.logonScripts?.splice(index, 1)" />
                </template>
              </Column>
            </DataTable>
          </TabPanel>
        </TabView>
      </template>
      <template #footer>
        <Button label="Cancel" severity="secondary" text @click="detailVisible = false" />
        <Button label="Save" icon="pi pi-check" @click="saveGpo" :loading="saving" />
      </template>
    </Dialog>

    <!-- Link GPO Dialog -->
    <Dialog v-model:visible="linkDialogVisible" header="Link GPO to Container" modal :style="{ width: '500px' }">
      <div style="display: flex; flex-direction: column; gap: 1rem; padding: 0.5rem 0">
        <div>
          <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">Target (OU, Domain, or Site)</label>
          <div style="position: relative">
            <InputText v-model="linkSearchQuery" placeholder="Search for an OU or domain..."
                       style="width: 100%" size="small" @input="onLinkSearchInput" />
            <div v-if="linkSearchResults.length > 0"
                 style="position: absolute; top: 100%; left: 0; right: 0; z-index: 10; background: var(--p-content-background); border: 1px solid var(--p-content-border-color); border-radius: 0 0 6px 6px; max-height: 200px; overflow-y: auto; box-shadow: 0 4px 6px -1px rgba(0,0,0,0.1)">
              <div v-for="item in linkSearchResults" :key="item.dn"
                   style="padding: 0.5rem 0.75rem; cursor: pointer; border-bottom: 1px solid var(--p-content-border-color)"
                   @click="onSelectLinkTarget(item)">
                <div style="display: flex; align-items: center; gap: 0.5rem">
                  <i class="pi pi-folder" style="color: var(--p-text-muted-color)"></i>
                  <span>{{ item.name || item.dn }}</span>
                </div>
                <div style="font-size: 0.8em; color: var(--p-text-muted-color)">{{ item.dn }}</div>
              </div>
            </div>
          </div>
          <div v-if="linkTargetDn" style="margin-top: 0.5rem; font-size: 0.85em; color: var(--p-text-muted-color)">
            Selected: {{ linkTargetDn }}
          </div>
        </div>
        <div style="display: flex; align-items: center; gap: 0.5rem">
          <Checkbox v-model="linkEnforced" :binary="true" />
          <label>Enforced (cannot be blocked by child OUs)</label>
        </div>
      </div>
      <template #footer>
        <Button label="Cancel" severity="secondary" text @click="linkDialogVisible = false" />
        <Button label="Link" icon="pi pi-link" @click="onLinkGpo" :disabled="!linkTargetDn" />
      </template>
    </Dialog>

    <!-- RSoP Dialog -->
    <Dialog v-model:visible="rsopDialogVisible" header="Resultant Set of Policy (RSoP)" modal
            :style="{ width: '800px', maxHeight: '85vh' }">
      <div style="display: flex; flex-direction: column; gap: 1rem; padding: 0.5rem 0">
        <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 1rem">
          <div>
            <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">User DN</label>
            <InputText v-model="rsopUserDn" placeholder="CN=jsmith,CN=Users,DC=..." style="width: 100%" size="small" />
          </div>
          <div>
            <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">Computer DN</label>
            <InputText v-model="rsopComputerDn" placeholder="CN=PC01,CN=Computers,DC=..." style="width: 100%" size="small" />
          </div>
        </div>
        <Button label="Compute RSoP" icon="pi pi-calculator" @click="computeRsop" :loading="rsopLoading" />

        <template v-if="rsopResult">
          <TabView>
            <TabPanel header="Applied GPOs">
              <h4 v-if="rsopResult.userGpos.length > 0" style="margin: 0 0 0.5rem 0">User GPOs</h4>
              <DataTable v-if="rsopResult.userGpos.length > 0" :value="rsopResult.userGpos" stripedRows size="small">
                <Column field="displayName" header="GPO" style="min-width: 200px" />
                <Column field="sourceContainerDn" header="Source" style="min-width: 200px" />
                <Column header="Enforced" style="width: 100px">
                  <template #body="{ data }">
                    <Tag v-if="data.isEnforced" value="Enforced" severity="warn" />
                  </template>
                </Column>
                <Column field="linkOrder" header="Order" style="width: 80px" />
              </DataTable>

              <h4 v-if="rsopResult.computerGpos.length > 0" style="margin: 1rem 0 0.5rem 0">Computer GPOs</h4>
              <DataTable v-if="rsopResult.computerGpos.length > 0" :value="rsopResult.computerGpos" stripedRows size="small">
                <Column field="displayName" header="GPO" style="min-width: 200px" />
                <Column field="sourceContainerDn" header="Source" style="min-width: 200px" />
                <Column header="Enforced" style="width: 100px">
                  <template #body="{ data }">
                    <Tag v-if="data.isEnforced" value="Enforced" severity="warn" />
                  </template>
                </Column>
                <Column field="linkOrder" header="Order" style="width: 80px" />
              </DataTable>

              <div v-if="rsopResult.userGpos.length === 0 && rsopResult.computerGpos.length === 0"
                   style="text-align: center; padding: 2rem; color: var(--p-text-muted-color)">
                <i class="pi pi-info-circle" style="font-size: 1.5rem; display: block; margin-bottom: 0.5rem"></i>
                <p style="margin: 0; font-size: 0.875rem">No GPOs apply to the specified targets. Verify the distinguished names are correct and that GPOs are linked to the relevant OUs.</p>
              </div>
            </TabPanel>

            <TabPanel header="Merged Policy">
              <pre style="background: var(--p-surface-100); border-radius: 6px; padding: 1rem; overflow: auto; max-height: 400px; font-size: 0.85em">{{ JSON.stringify(rsopResult.mergedPolicy, null, 2) }}</pre>
            </TabPanel>
          </TabView>
        </template>
      </div>
    </Dialog>
  </div>
</template>
