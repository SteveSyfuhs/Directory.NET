<script setup lang="ts">
import { ref, onMounted, computed, watch } from 'vue'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Textarea from 'primevue/textarea'
import Checkbox from 'primevue/checkbox'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Dialog from 'primevue/dialog'
import Tag from 'primevue/tag'
import ProgressSpinner from 'primevue/progressspinner'
import ConfirmDialog from 'primevue/confirmdialog'
import TabView from 'primevue/tabview'
import TabPanel from 'primevue/tabpanel'
import { useConfirm } from 'primevue/useconfirm'
import { useToast } from 'primevue/usetoast'
import {
  getDomainJoinInfo,
  joinComputer,
  rejoinComputer,
  unjoinComputer,
  getDomainJoinHistory,
  provisionOfflineJoin,
  validateOfflineJoinBlob,
  revokeOfflineJoin,
} from '../api/domainJoin'
import type {
  DomainJoinInfo,
  DomainJoinResult,
  DomainJoinHistoryEntry,
  DjoinProvisionResult,
  DjoinValidationResult,
} from '../types/domainJoin'
import { relativeTime } from '../utils/format'

const toast = useToast()
const confirm = useConfirm()

// ── Data ────────────────────────────────────────────────────────
const loading = ref(true)
const domainInfo = ref<DomainJoinInfo | null>(null)
const history = ref<DomainJoinHistoryEntry[]>([])

// Join form
const joinComputerName = ref('')
const joinDnsHostName = ref('')
const joinOu = ref('')
const joinOs = ref('')
const joinOsVersion = ref('')
const joinOsServicePack = ref('')
const joinAdminDn = ref('CN=Administrator,CN=Users')
const joining = ref(false)

// Join result dialog
const resultVisible = ref(false)
const joinResult = ref<DomainJoinResult | null>(null)
const passwordVisible = ref(false)

// Rejoin form
const rejoinName = ref('')
const rejoinAdminDn = ref('CN=Administrator,CN=Users')
const rejoining = ref(false)

// Unjoin form
const unjoinName = ref('')
const unjoinAdminDn = ref('CN=Administrator,CN=Users')
const unjoining = ref(false)

// Offline Domain Join — Provision
const offlineComputerName = ref('')
const offlineOu = ref('')
const offlineReuseExisting = ref(false)
const provisioning = ref(false)
const provisionResult = ref<DjoinProvisionResult | null>(null)
const provisionResultVisible = ref(false)

// Offline Domain Join — Validate
const validateBlob = ref('')
const validating = ref(false)
const validationResult = ref<DjoinValidationResult | null>(null)

// Offline Domain Join — Revoke
const revokeComputerName = ref('')
const revoking = ref(false)

// ── Computed ─────────────────────────────────────────────────────
const computerNameError = computed(() => {
  const name = joinComputerName.value
  if (!name) return ''
  if (name.length > 15) return 'Computer name must not exceed 15 characters.'
  if (!/^[a-zA-Z0-9]([a-zA-Z0-9\-]{0,13}[a-zA-Z0-9])?$/.test(name))
    return 'Only alphanumeric characters and hyphens allowed. Cannot start or end with a hyphen.'
  return ''
})

const canJoin = computed(() =>
  joinComputerName.value.trim() !== '' &&
  joinDnsHostName.value.trim() !== '' &&
  joinAdminDn.value.trim() !== '' &&
  !computerNameError.value &&
  !joining.value
)

// Auto-derive DNS host name from computer name + domain
watch(joinComputerName, (val) => {
  if (domainInfo.value && val) {
    joinDnsHostName.value = `${val.toLowerCase()}.${domainInfo.value.domainDnsName}`
  } else {
    joinDnsHostName.value = ''
  }
})

// ── Lifecycle ────────────────────────────────────────────────────
onMounted(() => loadAll())

async function loadAll() {
  loading.value = true
  try {
    const [info, hist] = await Promise.all([
      getDomainJoinInfo(),
      getDomainJoinHistory(),
    ])
    domainInfo.value = info
    history.value = hist

    // Pre-fill OU default
    if (info.defaultComputersOu) {
      joinOu.value = info.defaultComputersOu
    }
    // Update admin DN suffix
    if (info.domainDn) {
      joinAdminDn.value = `CN=Administrator,CN=Users,${info.domainDn}`
      rejoinAdminDn.value = `CN=Administrator,CN=Users,${info.domainDn}`
      unjoinAdminDn.value = `CN=Administrator,CN=Users,${info.domainDn}`
    }
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    loading.value = false
  }
}

// ── Join ─────────────────────────────────────────────────────────
async function onJoin() {
  joining.value = true
  try {
    const result = await joinComputer({
      computerName: joinComputerName.value.trim(),
      dnsHostName: joinDnsHostName.value.trim(),
      organizationalUnit: joinOu.value.trim() || undefined,
      adminUserDn: joinAdminDn.value.trim(),
      operatingSystem: joinOs.value.trim() || undefined,
      osVersion: joinOsVersion.value.trim() || undefined,
      osServicePack: joinOsServicePack.value.trim() || undefined,
    })
    joinResult.value = result
    resultVisible.value = true

    if (result.success) {
      toast.add({ severity: 'success', summary: 'Joined', detail: `${joinComputerName.value} joined successfully`, life: 5000 })
      joinComputerName.value = ''
      joinDnsHostName.value = ''
      joinOs.value = ''
      joinOsVersion.value = ''
      joinOsServicePack.value = ''
    } else {
      toast.add({ severity: 'error', summary: 'Join Failed', detail: result.errorMessage, life: 5000 })
    }

    // Refresh history
    history.value = await getDomainJoinHistory()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    joining.value = false
  }
}

// ── Rejoin ───────────────────────────────────────────────────────
async function onRejoin() {
  rejoining.value = true
  try {
    const result = await rejoinComputer({
      computerName: rejoinName.value.trim(),
      adminUserDn: rejoinAdminDn.value.trim(),
    })
    joinResult.value = result
    resultVisible.value = true

    if (result.success) {
      toast.add({ severity: 'success', summary: 'Rejoined', detail: `${rejoinName.value} re-joined successfully`, life: 5000 })
      rejoinName.value = ''
    } else {
      toast.add({ severity: 'error', summary: 'Rejoin Failed', detail: result.errorMessage, life: 5000 })
    }

    history.value = await getDomainJoinHistory()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    rejoining.value = false
  }
}

// ── Unjoin ───────────────────────────────────────────────────────
function onUnjoin() {
  confirm.require({
    message: `Are you sure you want to unjoin "${unjoinName.value}"? This will disable the computer account.`,
    header: 'Confirm Unjoin',
    icon: 'pi pi-exclamation-triangle',
    acceptClass: 'p-button-danger',
    accept: async () => {
      unjoining.value = true
      try {
        const result = await unjoinComputer({
          computerName: unjoinName.value.trim(),
          adminUserDn: unjoinAdminDn.value.trim(),
        })

        if (result.success) {
          toast.add({ severity: 'success', summary: 'Unjoined', detail: `${unjoinName.value} has been unjoined (disabled)`, life: 5000 })
          unjoinName.value = ''
        } else {
          toast.add({ severity: 'error', summary: 'Unjoin Failed', detail: result.errorMessage, life: 5000 })
        }

        history.value = await getDomainJoinHistory()
      } catch (e: any) {
        toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
      } finally {
        unjoining.value = false
      }
    },
  })
}

// ── Offline Domain Join ─────────────────────────────────────────
async function onProvisionOffline() {
  provisioning.value = true
  try {
    const result = await provisionOfflineJoin({
      computerName: offlineComputerName.value.trim(),
      organizationalUnit: offlineOu.value.trim() || undefined,
      reuseExistingAccount: offlineReuseExisting.value,
    })
    provisionResult.value = result
    provisionResultVisible.value = true

    if (result.success) {
      toast.add({ severity: 'success', summary: 'Provisioned', detail: `Offline join blob generated for ${offlineComputerName.value}`, life: 5000 })
      offlineComputerName.value = ''
      offlineReuseExisting.value = false
    } else {
      toast.add({ severity: 'error', summary: 'Provision Failed', detail: result.errorMessage, life: 5000 })
    }
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    provisioning.value = false
  }
}

async function onValidateBlob() {
  validating.value = true
  validationResult.value = null
  try {
    validationResult.value = await validateOfflineJoinBlob(validateBlob.value.trim())
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    validating.value = false
  }
}

async function onRevokeOffline() {
  if (!confirm(`Revoke offline join provision for "${revokeComputerName.value}"? This will invalidate the blob.`)) return
  revoking.value = true
  try {
    await revokeOfflineJoin(revokeComputerName.value.trim())
    toast.add({ severity: 'success', summary: 'Revoked', detail: `Offline join for ${revokeComputerName.value} has been revoked`, life: 5000 })
    revokeComputerName.value = ''
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    revoking.value = false
  }
}

function downloadDjoinBlob(blob: string, computerName: string) {
  const blobData = new Blob([blob], { type: 'application/octet-stream' })
  const url = URL.createObjectURL(blobData)
  const link = document.createElement('a')
  link.href = url
  link.download = `${computerName}.djoin`
  link.click()
  URL.revokeObjectURL(url)
}

// ── Clipboard ────────────────────────────────────────────────────
async function copyToClipboard(text: string) {
  try {
    await navigator.clipboard.writeText(text)
    toast.add({ severity: 'info', summary: 'Copied', detail: 'Copied to clipboard', life: 2000 })
  } catch {
    toast.add({ severity: 'error', summary: 'Error', detail: 'Failed to copy', life: 3000 })
  }
}
</script>

<template>
  <div>
    <div class="page-header">
      <h1>Domain Join</h1>
      <p>Join workstations and member servers to the domain, or manage existing computer accounts</p>
    </div>

    <div v-if="loading" style="text-align: center; padding: 4rem">
      <ProgressSpinner />
    </div>

    <div v-else>
      <!-- Domain Info Card -->
      <div class="stat-grid" v-if="domainInfo">
        <div class="stat-card">
          <div class="stat-icon blue">
            <i class="pi pi-globe"></i>
          </div>
          <div>
            <div class="stat-value" style="font-size: 1rem">{{ domainInfo.domainDnsName || '(not configured)' }}</div>
            <div class="stat-label">Domain DNS Name</div>
          </div>
        </div>
        <div class="stat-card">
          <div class="stat-icon purple">
            <i class="pi pi-tag"></i>
          </div>
          <div>
            <div class="stat-value" style="font-size: 1rem">{{ domainInfo.domainNetBiosName || '(not set)' }}</div>
            <div class="stat-label">NetBIOS Name</div>
          </div>
        </div>
        <div class="stat-card">
          <div class="stat-icon green">
            <i class="pi pi-server"></i>
          </div>
          <div>
            <div class="stat-value" style="font-size: 1rem">{{ domainInfo.dcName }}</div>
            <div class="stat-label">DC Name</div>
          </div>
        </div>
        <div class="stat-card">
          <div class="stat-icon amber">
            <i class="pi pi-folder"></i>
          </div>
          <div>
            <div class="stat-value" style="font-size: 0.85rem; word-break: break-all">{{ domainInfo.defaultComputersOu }}</div>
            <div class="stat-label">Default Computers OU</div>
          </div>
        </div>
      </div>

      <TabView style="margin-top: 1rem">
        <!-- Join Tab -->
        <TabPanel header="Join Computer">
          <div class="card">
            <h2 style="margin: 0 0 1rem; font-size: 1rem; font-weight: 600">
              <i class="pi pi-plus-circle" style="margin-right: 0.5rem"></i>Join a Computer to the Domain
            </h2>

            <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 1rem; margin-bottom: 1rem">
              <div>
                <label style="display: block; font-size: 0.8125rem; font-weight: 600; margin-bottom: 0.25rem">
                  Computer Name <span style="color: var(--app-danger-text)">*</span>
                </label>
                <InputText
                  v-model="joinComputerName"
                  placeholder="WORKSTATION1"
                  style="width: 100%"
                  :class="{ 'p-invalid': computerNameError }"
                  maxlength="15"
                />
                <small v-if="computerNameError" style="color: var(--app-danger-text)">{{ computerNameError }}</small>
                <small v-else style="color: var(--p-text-muted-color)">Max 15 characters, alphanumeric and hyphens</small>
              </div>
              <div>
                <label style="display: block; font-size: 0.8125rem; font-weight: 600; margin-bottom: 0.25rem">
                  DNS Host Name <span style="color: var(--app-danger-text)">*</span>
                </label>
                <InputText
                  v-model="joinDnsHostName"
                  placeholder="workstation1.contoso.com"
                  style="width: 100%"
                />
                <small style="color: var(--p-text-muted-color)">Auto-derived from computer name + domain</small>
              </div>
            </div>

            <div style="margin-bottom: 1rem">
              <label style="display: block; font-size: 0.8125rem; font-weight: 600; margin-bottom: 0.25rem">
                Target OU
              </label>
              <InputText
                v-model="joinOu"
                :placeholder="domainInfo?.defaultComputersOu"
                style="width: 100%"
              />
              <small style="color: var(--p-text-muted-color)">Leave empty to use the default Computers container</small>
            </div>

            <div style="margin-bottom: 1rem">
              <label style="display: block; font-size: 0.8125rem; font-weight: 600; margin-bottom: 0.25rem">
                Admin User DN <span style="color: var(--app-danger-text)">*</span>
              </label>
              <InputText
                v-model="joinAdminDn"
                placeholder="CN=Administrator,CN=Users,DC=contoso,DC=com"
                style="width: 100%"
              />
            </div>

            <div style="display: grid; grid-template-columns: 1fr 1fr 1fr; gap: 1rem; margin-bottom: 1.5rem">
              <div>
                <label style="display: block; font-size: 0.8125rem; font-weight: 600; margin-bottom: 0.25rem">Operating System</label>
                <InputText v-model="joinOs" placeholder="Windows 11 Enterprise" style="width: 100%" />
              </div>
              <div>
                <label style="display: block; font-size: 0.8125rem; font-weight: 600; margin-bottom: 0.25rem">OS Version</label>
                <InputText v-model="joinOsVersion" placeholder="10.0 (22631)" style="width: 100%" />
              </div>
              <div>
                <label style="display: block; font-size: 0.8125rem; font-weight: 600; margin-bottom: 0.25rem">OS Service Pack</label>
                <InputText v-model="joinOsServicePack" placeholder="" style="width: 100%" />
              </div>
            </div>

            <Button
              label="Join Computer"
              icon="pi pi-sign-in"
              :loading="joining"
              :disabled="!canJoin"
              @click="onJoin"
            />
          </div>
        </TabPanel>

        <!-- Rejoin Tab -->
        <TabPanel header="Rejoin Computer">
          <div class="card">
            <h2 style="margin: 0 0 0.5rem; font-size: 1rem; font-weight: 600">
              <i class="pi pi-refresh" style="margin-right: 0.5rem"></i>Rejoin a Computer
            </h2>
            <p style="margin: 0 0 1rem; font-size: 0.8125rem; color: var(--p-text-muted-color)">
              Resets the machine account password for an existing computer to allow it to re-establish its secure channel.
              The computer account will also be re-enabled if it was disabled.
            </p>

            <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 1rem; margin-bottom: 1rem">
              <div>
                <label style="display: block; font-size: 0.8125rem; font-weight: 600; margin-bottom: 0.25rem">
                  Computer Name <span style="color: var(--app-danger-text)">*</span>
                </label>
                <InputText v-model="rejoinName" placeholder="WORKSTATION1" style="width: 100%" />
              </div>
              <div>
                <label style="display: block; font-size: 0.8125rem; font-weight: 600; margin-bottom: 0.25rem">
                  Admin User DN <span style="color: var(--app-danger-text)">*</span>
                </label>
                <InputText v-model="rejoinAdminDn" style="width: 100%" />
              </div>
            </div>

            <Button
              label="Rejoin"
              icon="pi pi-refresh"
              :loading="rejoining"
              :disabled="!rejoinName.trim() || !rejoinAdminDn.trim()"
              @click="onRejoin"
            />
          </div>
        </TabPanel>

        <!-- Unjoin Tab -->
        <TabPanel header="Unjoin Computer">
          <div class="card">
            <h2 style="margin: 0 0 0.5rem; font-size: 1rem; font-weight: 600">
              <i class="pi pi-sign-out" style="margin-right: 0.5rem"></i>Unjoin a Computer
            </h2>
            <p style="margin: 0 0 1rem; font-size: 0.8125rem; color: var(--p-text-muted-color)">
              Disables the computer account, effectively removing the machine from the domain. The account
              is not deleted and can be re-enabled via Rejoin if needed.
            </p>

            <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 1rem; margin-bottom: 1rem">
              <div>
                <label style="display: block; font-size: 0.8125rem; font-weight: 600; margin-bottom: 0.25rem">
                  Computer Name <span style="color: var(--app-danger-text)">*</span>
                </label>
                <InputText v-model="unjoinName" placeholder="WORKSTATION1" style="width: 100%" />
              </div>
              <div>
                <label style="display: block; font-size: 0.8125rem; font-weight: 600; margin-bottom: 0.25rem">
                  Admin User DN <span style="color: var(--app-danger-text)">*</span>
                </label>
                <InputText v-model="unjoinAdminDn" style="width: 100%" />
              </div>
            </div>

            <Button
              label="Unjoin Computer"
              icon="pi pi-sign-out"
              severity="danger"
              :loading="unjoining"
              :disabled="!unjoinName.trim() || !unjoinAdminDn.trim()"
              @click="onUnjoin"
            />
          </div>
        </TabPanel>

        <!-- Offline Domain Join Tab -->
        <TabPanel header="Offline Join (djoin)">
          <div class="card" style="margin-bottom: 1.5rem">
            <h2 style="margin: 0 0 0.5rem; font-size: 1rem; font-weight: 600">
              <i class="pi pi-cloud-download" style="margin-right: 0.5rem"></i>Provision Offline Join
            </h2>
            <p style="margin: 0 0 1rem; font-size: 0.8125rem; color: var(--p-text-muted-color)">
              Generate a djoin blob containing all information a machine needs to join the domain offline.
              The blob includes the machine password and should be transmitted securely.
            </p>

            <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 1rem; margin-bottom: 1rem">
              <div>
                <label style="display: block; font-size: 0.8125rem; font-weight: 600; margin-bottom: 0.25rem">
                  Computer Name <span style="color: var(--app-danger-text)">*</span>
                </label>
                <InputText v-model="offlineComputerName" placeholder="WORKSTATION1" style="width: 100%" maxlength="15" />
              </div>
              <div>
                <label style="display: block; font-size: 0.8125rem; font-weight: 600; margin-bottom: 0.25rem">
                  Target OU
                </label>
                <InputText v-model="offlineOu" :placeholder="domainInfo?.defaultComputersOu" style="width: 100%" />
              </div>
            </div>

            <div style="display: flex; align-items: center; gap: 0.5rem; margin-bottom: 1.5rem">
              <Checkbox v-model="offlineReuseExisting" :binary="true" inputId="reuseExisting" />
              <label for="reuseExisting" style="font-size: 0.8125rem">Re-use existing account (re-provision)</label>
            </div>

            <Button
              label="Generate Blob"
              icon="pi pi-download"
              :loading="provisioning"
              :disabled="!offlineComputerName.trim() || provisioning"
              @click="onProvisionOffline"
            />
          </div>

          <div class="card" style="margin-bottom: 1.5rem">
            <h2 style="margin: 0 0 0.5rem; font-size: 1rem; font-weight: 600">
              <i class="pi pi-check-circle" style="margin-right: 0.5rem"></i>Validate Blob
            </h2>
            <p style="margin: 0 0 1rem; font-size: 0.8125rem; color: var(--p-text-muted-color)">
              Paste a djoin blob to check if it is valid, not expired, and the computer account still exists.
            </p>

            <div style="margin-bottom: 1rem">
              <Textarea
                v-model="validateBlob"
                placeholder="Paste base64-encoded djoin blob here..."
                rows="4"
                style="width: 100%"
              />
            </div>

            <Button
              label="Validate"
              icon="pi pi-check"
              :loading="validating"
              :disabled="!validateBlob.trim() || validating"
              @click="onValidateBlob"
              style="margin-bottom: 1rem"
            />

            <div v-if="validationResult" style="margin-top: 0.5rem">
              <Tag
                :value="validationResult.valid ? 'Valid' : 'Invalid'"
                :severity="validationResult.valid ? 'success' : 'danger'"
                style="margin-bottom: 0.75rem"
              />
              <div style="display: grid; grid-template-columns: auto 1fr; gap: 0.25rem 1rem; font-size: 0.875rem">
                <strong>Computer:</strong>
                <span>{{ validationResult.computerName }}</span>
                <strong>DN:</strong>
                <span style="font-family: monospace; word-break: break-all; font-size: 0.8125rem">{{ validationResult.computerDn }}</span>
                <strong>SID:</strong>
                <span style="font-family: monospace; font-size: 0.8125rem">{{ validationResult.computerSid }}</span>
                <strong>Domain:</strong>
                <span>{{ validationResult.domainDnsName }}</span>
                <strong>Provisioned:</strong>
                <span>{{ validationResult.provisionedAt ? new Date(validationResult.provisionedAt).toLocaleString() : '' }}</span>
                <strong>Expires:</strong>
                <span>{{ validationResult.expiresAt ? new Date(validationResult.expiresAt).toLocaleString() : '' }}</span>
                <strong>Account Exists:</strong>
                <span>{{ validationResult.accountExists ? 'Yes' : 'No' }}</span>
              </div>
              <p v-if="validationResult.errorMessage" style="color: var(--app-danger-text); margin-top: 0.5rem; font-size: 0.8125rem">
                {{ validationResult.errorMessage }}
              </p>
            </div>
          </div>

          <div class="card">
            <h2 style="margin: 0 0 0.5rem; font-size: 1rem; font-weight: 600">
              <i class="pi pi-ban" style="margin-right: 0.5rem"></i>Revoke Offline Join
            </h2>
            <p style="margin: 0 0 1rem; font-size: 0.8125rem; color: var(--p-text-muted-color)">
              Disable the pre-provisioned account and reset its password to invalidate an outstanding djoin blob.
            </p>

            <div style="margin-bottom: 1rem">
              <label style="display: block; font-size: 0.8125rem; font-weight: 600; margin-bottom: 0.25rem">
                Computer Name <span style="color: var(--app-danger-text)">*</span>
              </label>
              <InputText v-model="revokeComputerName" placeholder="WORKSTATION1" style="width: 100%; max-width: 400px" />
            </div>

            <Button
              label="Revoke"
              icon="pi pi-ban"
              severity="danger"
              :loading="revoking"
              :disabled="!revokeComputerName.trim() || revoking"
              @click="onRevokeOffline"
            />
          </div>
        </TabPanel>

        <!-- History Tab -->
        <TabPanel header="Recent Operations">
          <div class="card">
            <h2 style="margin: 0 0 1rem; font-size: 1rem; font-weight: 600">
              <i class="pi pi-history" style="margin-right: 0.5rem"></i>Recent Domain Join Operations
            </h2>
            <DataTable :value="history" size="small" stripedRows paginator :rows="20"
              sortField="timestamp" :sortOrder="-1">
              <Column field="timestamp" header="Time" sortable style="width: 10rem">
                <template #body="{ data }">
                  <span :title="new Date(data.timestamp).toLocaleString()">{{ relativeTime(data.timestamp) }}</span>
                </template>
              </Column>
              <Column field="operation" header="Operation" sortable style="width: 7rem">
                <template #body="{ data }">
                  <Tag
                    :value="data.operation"
                    :severity="data.operation === 'Join' ? 'success' : data.operation === 'Unjoin' ? 'danger' : 'info'"
                  />
                </template>
              </Column>
              <Column field="computerName" header="Computer" sortable />
              <Column field="success" header="Status" sortable style="width: 6rem">
                <template #body="{ data }">
                  <Tag :value="data.success ? 'Success' : 'Failed'" :severity="data.success ? 'success' : 'danger'" />
                </template>
              </Column>
              <Column field="operator" header="Operator" style="max-width: 14rem">
                <template #body="{ data }">
                  <span style="font-size: 0.8125rem; word-break: break-all">{{ data.operator }}</span>
                </template>
              </Column>
              <Column field="errorMessage" header="Error" style="max-width: 16rem">
                <template #body="{ data }">
                  <span v-if="data.errorMessage" style="color: var(--app-danger-text); font-size: 0.8125rem">{{ data.errorMessage }}</span>
                </template>
              </Column>
              <template #empty>
                <div style="text-align: center; padding: 2rem; color: var(--p-text-muted-color)">
                  No domain join operations recorded yet.
                </div>
              </template>
            </DataTable>
          </div>
        </TabPanel>
      </TabView>
    </div>

    <!-- Join Result Dialog -->
    <Dialog
      v-model:visible="resultVisible"
      :header="joinResult?.success ? 'Computer Joined Successfully' : 'Join Result'"
      :style="{ width: '40rem' }"
      modal
      :closable="true"
    >
      <div v-if="joinResult">
        <div v-if="joinResult.success">
          <div style="display: grid; grid-template-columns: auto 1fr; gap: 0.5rem 1rem; font-size: 0.875rem; margin-bottom: 1rem">
            <strong>Computer DN:</strong>
            <span style="font-family: monospace; word-break: break-all">{{ joinResult.computerDn }}</span>

            <strong>Computer SID:</strong>
            <span style="font-family: monospace">{{ joinResult.computerSid }}</span>

            <strong>Domain DNS Name:</strong>
            <span>{{ joinResult.domainDnsName }}</span>

            <strong>Domain NetBIOS:</strong>
            <span>{{ joinResult.domainNetBiosName }}</span>

            <strong>Domain SID:</strong>
            <span style="font-family: monospace">{{ joinResult.domainSid }}</span>

            <strong>DC Name:</strong>
            <span>{{ joinResult.dcName }}</span>

            <strong>DC Address:</strong>
            <span>{{ joinResult.dcAddress }}</span>
          </div>

          <!-- SPNs -->
          <div v-if="joinResult.servicePrincipalNames?.length" style="margin-bottom: 1rem">
            <strong style="font-size: 0.875rem">Registered SPNs:</strong>
            <ul style="margin: 0.25rem 0 0; padding-left: 1.5rem; font-family: monospace; font-size: 0.8125rem">
              <li v-for="spn in joinResult.servicePrincipalNames" :key="spn">{{ spn }}</li>
            </ul>
          </div>

          <!-- Machine Password -->
          <div style="background: var(--p-surface-100); border-radius: 6px; padding: 0.75rem; margin-bottom: 0.5rem">
            <div style="display: flex; align-items: center; gap: 0.5rem; margin-bottom: 0.5rem">
              <strong style="font-size: 0.875rem">Machine Password:</strong>
              <Button
                :icon="passwordVisible ? 'pi pi-eye-slash' : 'pi pi-eye'"
                text
                size="small"
                @click="passwordVisible = !passwordVisible"
                v-tooltip="passwordVisible ? 'Hide password' : 'Reveal password'"
              />
              <Button
                icon="pi pi-copy"
                text
                size="small"
                @click="copyToClipboard(joinResult.machinePassword)"
                v-tooltip="'Copy to clipboard'"
              />
            </div>
            <code v-if="passwordVisible" style="word-break: break-all; font-size: 0.75rem; display: block">{{ joinResult.machinePassword }}</code>
            <span v-else style="font-family: monospace; color: var(--p-text-muted-color); font-size: 0.8125rem">Click the eye icon to reveal the machine password</span>
          </div>
        </div>

        <div v-else>
          <p style="color: var(--app-danger-text)">{{ joinResult.errorMessage }}</p>
        </div>
      </div>

      <template #footer>
        <Button label="Close" @click="resultVisible = false; passwordVisible = false" />
      </template>
    </Dialog>

    <!-- Offline Join Provision Result Dialog -->
    <Dialog
      v-model:visible="provisionResultVisible"
      header="Offline Join Blob Generated"
      :style="{ width: '50rem' }"
      modal
    >
      <div v-if="provisionResult?.success">
        <div style="display: grid; grid-template-columns: auto 1fr; gap: 0.25rem 1rem; font-size: 0.875rem; margin-bottom: 1rem">
          <strong>Computer DN:</strong>
          <span style="font-family: monospace; word-break: break-all">{{ provisionResult.computerDn }}</span>
          <strong>Computer SID:</strong>
          <span style="font-family: monospace">{{ provisionResult.computerSid }}</span>
        </div>

        <div style="margin-bottom: 0.5rem">
          <label style="display: block; font-size: 0.8125rem; font-weight: 600; margin-bottom: 0.25rem">
            Djoin Blob (base64)
          </label>
          <Textarea
            :modelValue="provisionResult.djoinBlob"
            readonly
            rows="6"
            style="width: 100%; font-family: monospace; font-size: 0.75rem"
          />
        </div>

        <div style="display: flex; gap: 0.5rem; margin-bottom: 0.5rem">
          <Button
            label="Copy Blob"
            icon="pi pi-copy"
            size="small"
            severity="secondary"
            outlined
            @click="copyToClipboard(provisionResult.djoinBlob)"
          />
          <Button
            label="Download as .djoin"
            icon="pi pi-download"
            size="small"
            severity="secondary"
            outlined
            @click="downloadDjoinBlob(provisionResult.djoinBlob, provisionResult.computerDn.split(',')[0]?.replace('CN=', '') || 'computer')"
          />
        </div>

        <div style="background: var(--app-warn-bg); border: 1px solid var(--app-warn-border); border-radius: 6px; padding: 0.75rem; font-size: 0.8125rem; color: var(--app-warn-text-strong)">
          <i class="pi pi-exclamation-triangle" style="margin-right: 0.5rem"></i>
          This blob contains the machine password. Transmit it securely and do not store it in plaintext.
          The blob expires in 30 days.
        </div>
      </div>

      <div v-else-if="provisionResult">
        <p style="color: var(--app-danger-text)">{{ provisionResult.errorMessage }}</p>
      </div>

      <template #footer>
        <Button label="Close" @click="provisionResultVisible = false" />
      </template>
    </Dialog>

    <ConfirmDialog />
  </div>
</template>
