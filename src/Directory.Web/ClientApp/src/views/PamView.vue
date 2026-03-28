<script setup lang="ts">
import { ref, onMounted, onUnmounted, computed } from 'vue'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import InputNumber from 'primevue/inputnumber'
import Textarea from 'primevue/textarea'
import Tag from 'primevue/tag'
import Dialog from 'primevue/dialog'
import Checkbox from 'primevue/checkbox'
import Slider from 'primevue/slider'
import Chip from 'primevue/chip'
import Tabs from 'primevue/tabs'
import TabList from 'primevue/tablist'
import Tab from 'primevue/tab'
import TabPanels from 'primevue/tabpanels'
import TabPanel from 'primevue/tabpanel'
import Message from 'primevue/message'
import { useToast } from 'primevue/usetoast'
import {
  getRoles,
  createRole,
  updateRole,
  deleteRole,
  requestActivation,
  approveActivation,
  denyActivation,
  deactivateActivation,
  getActiveActivations,
  getPendingActivations,
  getActivations,
  getBreakGlassAccounts,
  sealAccount,
  breakGlassAccess,
  resealAccount,
} from '../api/pam'
import type { PrivilegedRole, RoleActivation, BreakGlassAccount, ActivationStatus } from '../types/pam'

const toast = useToast()

const activeTab = ref('0')
const loading = ref(true)

// ── Roles ─────────────────────────────────────────────────────
const roles = ref<PrivilegedRole[]>([])
const roleEditVisible = ref(false)
const roleEditSaving = ref(false)
const isNewRole = ref(false)
const roleForm = ref<PrivilegedRole>(emptyRole())
const tempApprover = ref('')
const roleDeleteVisible = ref(false)
const roleDeleteTarget = ref<PrivilegedRole | null>(null)
const roleDeleting = ref(false)

function emptyRole(): PrivilegedRole {
  return {
    id: '',
    name: '',
    groupDn: '',
    maxActivationHours: 8,
    requireJustification: true,
    requireApproval: false,
    approvers: [],
    requireMfa: true,
    isEnabled: true,
  }
}

function openNewRole() {
  roleForm.value = emptyRole()
  isNewRole.value = true
  tempApprover.value = ''
  roleEditVisible.value = true
}

function openEditRole(role: PrivilegedRole) {
  roleForm.value = { ...role, approvers: [...role.approvers] }
  isNewRole.value = false
  tempApprover.value = ''
  roleEditVisible.value = true
}

function addApprover() {
  const v = tempApprover.value.trim()
  if (v && !roleForm.value.approvers.includes(v)) {
    roleForm.value.approvers.push(v)
  }
  tempApprover.value = ''
}

function removeApprover(idx: number) {
  roleForm.value.approvers.splice(idx, 1)
}

async function saveRole() {
  roleEditSaving.value = true
  try {
    if (isNewRole.value) {
      await createRole(roleForm.value)
      toast.add({ severity: 'success', summary: 'Role created', life: 3000 })
    } else {
      await updateRole(roleForm.value.id, roleForm.value)
      toast.add({ severity: 'success', summary: 'Role updated', life: 3000 })
    }
    roleEditVisible.value = false
    await loadRoles()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    roleEditSaving.value = false
  }
}

function confirmDeleteRole(role: PrivilegedRole) {
  roleDeleteTarget.value = role
  roleDeleteVisible.value = true
}

async function doDeleteRole() {
  if (!roleDeleteTarget.value) return
  roleDeleting.value = true
  try {
    await deleteRole(roleDeleteTarget.value.id)
    toast.add({ severity: 'success', summary: 'Role deleted', life: 3000 })
    roleDeleteVisible.value = false
    await loadRoles()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    roleDeleting.value = false
  }
}

async function loadRoles() {
  try { roles.value = await getRoles() } catch { /* ignore */ }
}

// ── Activations ───────────────────────────────────────────────
const activateVisible = ref(false)
const activateSaving = ref(false)
const activateForm = ref({ userDn: '', roleId: '', justification: '', hours: 4 })
const activeActivations = ref<RoleActivation[]>([])
const pendingActivations = ref<RoleActivation[]>([])
const historyActivations = ref<RoleActivation[]>([])

// Approve / deny
const approveVisible = ref(false)
const approveSaving = ref(false)
const approveTarget = ref<RoleActivation | null>(null)
const approverDn = ref('')

const denyVisible = ref(false)
const denySaving = ref(false)
const denyTarget = ref<RoleActivation | null>(null)
const denyForm = ref({ denierDn: '', reason: '' })

// Countdown timer
const now = ref(Date.now())
let countdownTimer: ReturnType<typeof setInterval> | null = null

function openActivate() {
  activateForm.value = { userDn: '', roleId: '', justification: '', hours: 4 }
  activateVisible.value = true
}

async function doActivate() {
  activateSaving.value = true
  try {
    await requestActivation(
      activateForm.value.userDn,
      activateForm.value.roleId,
      activateForm.value.justification,
      activateForm.value.hours,
    )
    toast.add({ severity: 'success', summary: 'Activation requested', life: 3000 })
    activateVisible.value = false
    await loadActivations()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    activateSaving.value = false
  }
}

function openApprove(a: RoleActivation) {
  approveTarget.value = a
  approverDn.value = ''
  approveVisible.value = true
}

async function doApprove() {
  if (!approveTarget.value) return
  approveSaving.value = true
  try {
    await approveActivation(approveTarget.value.id, approverDn.value)
    toast.add({ severity: 'success', summary: 'Activation approved', life: 3000 })
    approveVisible.value = false
    await loadActivations()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    approveSaving.value = false
  }
}

function openDeny(a: RoleActivation) {
  denyTarget.value = a
  denyForm.value = { denierDn: '', reason: '' }
  denyVisible.value = true
}

async function doDeny() {
  if (!denyTarget.value) return
  denySaving.value = true
  try {
    await denyActivation(denyTarget.value.id, denyForm.value.denierDn, denyForm.value.reason)
    toast.add({ severity: 'success', summary: 'Activation denied', life: 3000 })
    denyVisible.value = false
    await loadActivations()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    denySaving.value = false
  }
}

async function doDeactivate(a: RoleActivation) {
  try {
    await deactivateActivation(a.id)
    toast.add({ severity: 'success', summary: 'Deactivated', life: 3000 })
    await loadActivations()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

function remainingTime(expiresAt?: string): string {
  if (!expiresAt) return '--'
  const diff = new Date(expiresAt).getTime() - now.value
  if (diff <= 0) return 'Expired'
  const mins = Math.floor(diff / 60000)
  const hrs = Math.floor(mins / 60)
  const m = mins % 60
  return hrs > 0 ? `${hrs}h ${m}m` : `${m}m`
}

function statusSeverity(status: ActivationStatus): 'success' | 'warn' | 'danger' | 'info' | 'secondary' | 'contrast' | undefined {
  switch (status) {
    case 'Active': return 'success'
    case 'PendingApproval': return 'warn'
    case 'Denied': return 'danger'
    case 'Expired': return 'secondary'
    case 'Deactivated': return 'info'
    case 'Cancelled': return 'secondary'
    default: return undefined
  }
}

async function loadActivations() {
  try {
    const [active, pending, history] = await Promise.all([
      getActiveActivations(),
      getPendingActivations(),
      getActivations(),
    ])
    activeActivations.value = active
    pendingActivations.value = pending
    historyActivations.value = history
  } catch { /* ignore */ }
}

// ── Break-Glass ───────────────────────────────────────────────
const breakGlassAccounts = ref<BreakGlassAccount[]>([])
const sealVisible = ref(false)
const sealSaving = ref(false)
const sealForm = ref({ accountDn: '', description: '' })

const breakVisible = ref(false)
const breakSaving = ref(false)
const breakTarget = ref<BreakGlassAccount | null>(null)
const breakReason = ref('')
const revealedPassword = ref('')

function openSeal() {
  sealForm.value = { accountDn: '', description: '' }
  sealVisible.value = true
}

async function doSeal() {
  sealSaving.value = true
  try {
    await sealAccount(sealForm.value.accountDn, sealForm.value.description)
    toast.add({ severity: 'success', summary: 'Account sealed', life: 3000 })
    sealVisible.value = false
    await loadBreakGlass()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    sealSaving.value = false
  }
}

function openBreak(account: BreakGlassAccount) {
  breakTarget.value = account
  breakReason.value = ''
  revealedPassword.value = ''
  breakVisible.value = true
}

async function doBreakGlass() {
  if (!breakTarget.value) return
  breakSaving.value = true
  try {
    const result = await breakGlassAccess(breakTarget.value.id, breakReason.value, 'web-console')
    revealedPassword.value = result.password
    toast.add({ severity: 'warn', summary: 'Break-glass password revealed', life: 5000 })
    await loadBreakGlass()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    breakSaving.value = false
  }
}

async function doReseal(account: BreakGlassAccount) {
  try {
    await resealAccount(account.id)
    toast.add({ severity: 'success', summary: 'Account resealed', life: 3000 })
    await loadBreakGlass()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

async function loadBreakGlass() {
  try { breakGlassAccounts.value = await getBreakGlassAccounts() } catch { /* ignore */ }
}

// ── Lifecycle ─────────────────────────────────────────────────
onMounted(async () => {
  loading.value = true
  await Promise.all([loadRoles(), loadActivations(), loadBreakGlass()])
  loading.value = false
  countdownTimer = setInterval(() => { now.value = Date.now() }, 5000)
})

onUnmounted(() => {
  if (countdownTimer) clearInterval(countdownTimer)
})

const selectedRoleMax = computed(() => {
  const role = roles.value.find(r => r.id === activateForm.value.roleId)
  return role?.maxActivationHours ?? 24
})
</script>

<template>
  <div>
    <div class="page-header">
      <h1>Privileged Access Management</h1>
      <p>Just-in-time privilege elevation, approval workflows, and emergency break-glass access.</p>
    </div>

    <Tabs v-model:value="activeTab">
      <TabList>
        <Tab value="0">Privileged Roles</Tab>
        <Tab value="1">Request Activation</Tab>
        <Tab value="2">Pending Approvals ({{ pendingActivations.length }})</Tab>
        <Tab value="3">Active Activations ({{ activeActivations.length }})</Tab>
        <Tab value="4">Break-Glass Accounts</Tab>
        <Tab value="5">Activation History</Tab>
      </TabList>
      <TabPanels>
        <!-- Privileged Roles -->
        <TabPanel value="0">
          <div class="toolbar">
            <Button label="New Role" icon="pi pi-plus" @click="openNewRole" />
          </div>
          <DataTable :value="roles" :loading="loading" stripedRows>
            <Column field="name" header="Name" sortable />
            <Column field="groupDn" header="Group DN" sortable />
            <Column field="maxActivationHours" header="Max Hours" sortable style="width: 100px" />
            <Column header="Requirements" style="width: 220px">
              <template #body="{ data }">
                <Tag v-if="data.requireJustification" value="Justification" severity="info" class="mr-1" />
                <Tag v-if="data.requireApproval" value="Approval" severity="warn" class="mr-1" />
                <Tag v-if="data.requireMfa" value="MFA" severity="secondary" />
              </template>
            </Column>
            <Column header="Status" style="width: 90px">
              <template #body="{ data }">
                <Tag :value="data.isEnabled ? 'Enabled' : 'Disabled'" :severity="data.isEnabled ? 'success' : 'danger'" />
              </template>
            </Column>
            <Column header="Actions" style="width: 130px">
              <template #body="{ data }">
                <Button icon="pi pi-pencil" text rounded @click="openEditRole(data)" class="mr-1" />
                <Button icon="pi pi-trash" text rounded severity="danger" @click="confirmDeleteRole(data)" />
              </template>
            </Column>
          </DataTable>
        </TabPanel>

        <!-- Request Activation -->
        <TabPanel value="1">
          <div class="card" style="max-width: 600px">
            <h3 style="margin-top: 0">Request Privilege Activation</h3>
            <div class="field" style="margin-bottom: 1rem">
              <label>User DN</label>
              <InputText v-model="activateForm.userDn" style="width: 100%" placeholder="CN=admin,OU=Users,DC=example,DC=com" />
            </div>
            <div class="field" style="margin-bottom: 1rem">
              <label>Privileged Role</label>
              <select v-model="activateForm.roleId" style="width: 100%; padding: 0.5rem; border: 1px solid var(--p-surface-border); border-radius: 6px; background: var(--p-surface-card); color: var(--p-text-color)">
                <option value="">-- Select role --</option>
                <option v-for="r in roles.filter(r => r.isEnabled)" :key="r.id" :value="r.id">{{ r.name }}</option>
              </select>
            </div>
            <div class="field" style="margin-bottom: 1rem">
              <label>Duration: {{ activateForm.hours }} hour(s)</label>
              <Slider v-model="activateForm.hours" :min="1" :max="selectedRoleMax" style="width: 100%" />
            </div>
            <div class="field" style="margin-bottom: 1rem">
              <label>Justification</label>
              <Textarea v-model="activateForm.justification" rows="3" style="width: 100%" placeholder="Explain why you need elevated access..." />
            </div>
            <Button label="Request Activation" icon="pi pi-bolt" :loading="activateSaving" @click="doActivate"
              :disabled="!activateForm.userDn || !activateForm.roleId" />
          </div>
        </TabPanel>

        <!-- Pending Approvals -->
        <TabPanel value="2">
          <DataTable :value="pendingActivations" :loading="loading" stripedRows>
            <Column field="roleName" header="Role" sortable />
            <Column field="userDn" header="User" sortable />
            <Column field="justification" header="Justification" />
            <Column field="requestedHours" header="Hours" style="width: 80px" />
            <Column header="Requested" style="width: 160px">
              <template #body="{ data }">{{ new Date(data.requestedAt).toLocaleString() }}</template>
            </Column>
            <Column header="Actions" style="width: 160px">
              <template #body="{ data }">
                <Button icon="pi pi-check" label="Approve" text size="small" severity="success" @click="openApprove(data)" class="mr-1" />
                <Button icon="pi pi-times" label="Deny" text size="small" severity="danger" @click="openDeny(data)" />
              </template>
            </Column>
          </DataTable>
        </TabPanel>

        <!-- Active Activations -->
        <TabPanel value="3">
          <DataTable :value="activeActivations" :loading="loading" stripedRows>
            <Column field="roleName" header="Role" sortable />
            <Column field="userDn" header="User" sortable />
            <Column header="Time Remaining" style="width: 150px">
              <template #body="{ data }">
                <Tag :value="remainingTime(data.expiresAt)" :severity="remainingTime(data.expiresAt) === 'Expired' ? 'danger' : 'success'" />
              </template>
            </Column>
            <Column header="Activated" style="width: 160px">
              <template #body="{ data }">{{ data.activatedAt ? new Date(data.activatedAt).toLocaleString() : '--' }}</template>
            </Column>
            <Column header="Actions" style="width: 120px">
              <template #body="{ data }">
                <Button icon="pi pi-power-off" label="Deactivate" text size="small" severity="danger" @click="doDeactivate(data)" />
              </template>
            </Column>
          </DataTable>
        </TabPanel>

        <!-- Break-Glass -->
        <TabPanel value="4">
          <Message severity="warn" :closable="false" class="mb-3">
            Break-glass accounts provide emergency access when normal authentication is unavailable.
            All access is logged and audited. Use with extreme caution.
          </Message>
          <div class="toolbar">
            <Button label="Seal New Account" icon="pi pi-lock" @click="openSeal" />
          </div>
          <DataTable :value="breakGlassAccounts" :loading="loading" stripedRows>
            <Column field="accountDn" header="Account DN" sortable />
            <Column field="description" header="Description" />
            <Column header="Status" style="width: 100px">
              <template #body="{ data }">
                <Tag :value="data.isSealed ? 'Sealed' : 'Unsealed'" :severity="data.isSealed ? 'success' : 'danger'" />
              </template>
            </Column>
            <Column header="Last Accessed" style="width: 180px">
              <template #body="{ data }">
                <span v-if="data.lastAccessedAt">{{ new Date(data.lastAccessedAt).toLocaleString() }} by {{ data.lastAccessedBy }}</span>
                <span v-else>Never</span>
              </template>
            </Column>
            <Column header="Actions" style="width: 180px">
              <template #body="{ data }">
                <Button v-if="data.isSealed" icon="pi pi-unlock" label="Break Glass" text size="small" severity="danger" @click="openBreak(data)" />
                <Button v-else icon="pi pi-lock" label="Reseal" text size="small" severity="success" @click="doReseal(data)" />
              </template>
            </Column>
          </DataTable>
        </TabPanel>

        <!-- Activation History -->
        <TabPanel value="5">
          <DataTable :value="historyActivations" :loading="loading" stripedRows paginator :rows="20" sortField="requestedAt" :sortOrder="-1">
            <Column field="roleName" header="Role" sortable />
            <Column field="userDn" header="User" sortable />
            <Column header="Status" style="width: 130px">
              <template #body="{ data }">
                <Tag :value="data.status" :severity="statusSeverity(data.status)" />
              </template>
            </Column>
            <Column field="justification" header="Justification" />
            <Column field="requestedHours" header="Hours" style="width: 80px" />
            <Column header="Requested" field="requestedAt" sortable style="width: 160px">
              <template #body="{ data }">{{ new Date(data.requestedAt).toLocaleString() }}</template>
            </Column>
          </DataTable>
        </TabPanel>
      </TabPanels>
    </Tabs>

    <!-- Role Editor Dialog -->
    <Dialog v-model:visible="roleEditVisible" :header="isNewRole ? 'New Privileged Role' : 'Edit Privileged Role'" :style="{ width: '550px' }" modal>
      <div class="field" style="margin-bottom: 1rem">
        <label>Name</label>
        <InputText v-model="roleForm.name" style="width: 100%" />
      </div>
      <div class="field" style="margin-bottom: 1rem">
        <label>Group DN</label>
        <InputText v-model="roleForm.groupDn" style="width: 100%" placeholder="CN=Domain Admins,CN=Users,DC=example,DC=com" />
      </div>
      <div class="field" style="margin-bottom: 1rem">
        <label>Max Activation Hours</label>
        <InputNumber v-model="roleForm.maxActivationHours" :min="1" :max="720" style="width: 100%" />
      </div>
      <div class="field" style="margin-bottom: 1rem">
        <div style="display: flex; gap: 1.5rem; flex-wrap: wrap">
          <label style="display: flex; align-items: center; gap: 0.5rem">
            <Checkbox v-model="roleForm.requireJustification" :binary="true" /> Require Justification
          </label>
          <label style="display: flex; align-items: center; gap: 0.5rem">
            <Checkbox v-model="roleForm.requireApproval" :binary="true" /> Require Approval
          </label>
          <label style="display: flex; align-items: center; gap: 0.5rem">
            <Checkbox v-model="roleForm.requireMfa" :binary="true" /> Require MFA
          </label>
          <label style="display: flex; align-items: center; gap: 0.5rem">
            <Checkbox v-model="roleForm.isEnabled" :binary="true" /> Enabled
          </label>
        </div>
      </div>
      <div v-if="roleForm.requireApproval" class="field" style="margin-bottom: 1rem">
        <label>Approvers</label>
        <div style="display: flex; gap: 0.5rem; margin-bottom: 0.5rem">
          <InputText v-model="tempApprover" placeholder="Approver DN" style="flex: 1" @keyup.enter="addApprover" />
          <Button icon="pi pi-plus" @click="addApprover" />
        </div>
        <div style="display: flex; flex-wrap: wrap; gap: 0.25rem">
          <Chip v-for="(a, idx) in roleForm.approvers" :key="idx" :label="a" removable @remove="removeApprover(idx)" />
        </div>
      </div>
      <template #footer>
        <Button label="Cancel" text @click="roleEditVisible = false" />
        <Button :label="isNewRole ? 'Create' : 'Save'" icon="pi pi-check" :loading="roleEditSaving" @click="saveRole" />
      </template>
    </Dialog>

    <!-- Delete Role Dialog -->
    <Dialog v-model:visible="roleDeleteVisible" header="Delete Role" :style="{ width: '400px' }" modal>
      <p>Are you sure you want to delete role <strong>{{ roleDeleteTarget?.name }}</strong>? Active activations will be deactivated.</p>
      <template #footer>
        <Button label="Cancel" text @click="roleDeleteVisible = false" />
        <Button label="Delete" icon="pi pi-trash" severity="danger" :loading="roleDeleting" @click="doDeleteRole" />
      </template>
    </Dialog>

    <!-- Approve Dialog -->
    <Dialog v-model:visible="approveVisible" header="Approve Activation" :style="{ width: '400px' }" modal>
      <p>Approve activation for <strong>{{ approveTarget?.userDn }}</strong> to role <strong>{{ approveTarget?.roleName }}</strong>?</p>
      <div class="field" style="margin-bottom: 1rem">
        <label>Your DN (Approver)</label>
        <InputText v-model="approverDn" style="width: 100%" />
      </div>
      <template #footer>
        <Button label="Cancel" text @click="approveVisible = false" />
        <Button label="Approve" icon="pi pi-check" severity="success" :loading="approveSaving" @click="doApprove" />
      </template>
    </Dialog>

    <!-- Deny Dialog -->
    <Dialog v-model:visible="denyVisible" header="Deny Activation" :style="{ width: '400px' }" modal>
      <p>Deny activation for <strong>{{ denyTarget?.userDn }}</strong> to role <strong>{{ denyTarget?.roleName }}</strong>?</p>
      <div class="field" style="margin-bottom: 1rem">
        <label>Your DN</label>
        <InputText v-model="denyForm.denierDn" style="width: 100%" />
      </div>
      <div class="field" style="margin-bottom: 1rem">
        <label>Reason</label>
        <Textarea v-model="denyForm.reason" rows="2" style="width: 100%" />
      </div>
      <template #footer>
        <Button label="Cancel" text @click="denyVisible = false" />
        <Button label="Deny" icon="pi pi-times" severity="danger" :loading="denySaving" @click="doDeny" />
      </template>
    </Dialog>

    <!-- Seal Account Dialog -->
    <Dialog v-model:visible="sealVisible" header="Seal Break-Glass Account" :style="{ width: '450px' }" modal>
      <div class="field" style="margin-bottom: 1rem">
        <label>Account DN</label>
        <InputText v-model="sealForm.accountDn" style="width: 100%" placeholder="CN=BreakGlass,OU=Admin,DC=example,DC=com" />
      </div>
      <div class="field" style="margin-bottom: 1rem">
        <label>Description</label>
        <InputText v-model="sealForm.description" style="width: 100%" placeholder="Emergency admin account" />
      </div>
      <template #footer>
        <Button label="Cancel" text @click="sealVisible = false" />
        <Button label="Seal Account" icon="pi pi-lock" :loading="sealSaving" @click="doSeal" />
      </template>
    </Dialog>

    <!-- Break Glass Dialog -->
    <Dialog v-model:visible="breakVisible" header="Break Glass - Emergency Access" :style="{ width: '500px' }" modal>
      <Message severity="error" :closable="false" class="mb-3">
        This action will reveal the emergency password and is permanently logged. Proceed only in genuine emergencies.
      </Message>
      <p><strong>Account:</strong> {{ breakTarget?.accountDn }}</p>
      <div class="field" style="margin-bottom: 1rem">
        <label>Reason for emergency access</label>
        <Textarea v-model="breakReason" rows="3" style="width: 100%" placeholder="Describe the emergency situation..." />
      </div>
      <div v-if="revealedPassword" class="card" style="background: var(--app-danger-bg); border-color: var(--app-danger-border); margin-top: 1rem">
        <label style="font-weight: 600; color: var(--app-danger-text)">Emergency Password</label>
        <div style="font-family: monospace; font-size: 1.1rem; word-break: break-all; margin-top: 0.5rem; color: var(--p-text-color)">{{ revealedPassword }}</div>
      </div>
      <template #footer>
        <Button label="Cancel" text @click="breakVisible = false" />
        <Button v-if="!revealedPassword" label="Reveal Password" icon="pi pi-unlock" severity="danger" :loading="breakSaving" :disabled="!breakReason" @click="doBreakGlass" />
      </template>
    </Dialog>
  </div>
</template>

<style scoped>
.field label {
  display: block;
  font-weight: 600;
  margin-bottom: 0.375rem;
  font-size: 0.875rem;
}
.mr-1 { margin-right: 0.25rem; }
.mb-3 { margin-bottom: 1rem; }
</style>
