<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Tag from 'primevue/tag'
import Dialog from 'primevue/dialog'
import ProgressSpinner from 'primevue/progressspinner'
import { useToast } from 'primevue/usetoast'
import { useConfirm } from 'primevue/useconfirm'
import { searchObjects, deleteObject } from '../api/objects'
import { enableUser, disableUser, unlockUser, getUserGroups } from '../api/users'
import { addGroupMember, removeGroupMember } from '../api/groups'
import type { ObjectSummary } from '../api/types'
import { relativeTime, cnFromDn } from '../utils/format'
import PropertySheet from '../components/PropertySheet.vue'
import CreateUserDialog from '../components/CreateUserDialog.vue'
import ResetPasswordDialog from '../components/ResetPasswordDialog.vue'
import BulkOperationDialog from '../components/BulkOperationDialog.vue'
import ColumnChooser from '../components/ColumnChooser.vue'
import { exportToCsv } from '../composables/useExport'

const toast = useToast()
const confirm = useConfirm()

// Column chooser state
const columnChooserVisible = ref(false)
const COLUMN_STORAGE_KEY = 'userlist-columns'

const allAvailableColumns = [
  { field: 'name', label: 'Name' },
  { field: 'samAccountName', label: 'Login (sAMAccountName)' },
  { field: 'userPrincipalName', label: 'UPN' },
  { field: 'mail', label: 'Email' },
  { field: 'description', label: 'Description' },
  { field: 'displayName', label: 'Display Name' },
  { field: 'givenName', label: 'First Name' },
  { field: 'sn', label: 'Last Name' },
  { field: 'title', label: 'Title' },
  { field: 'department', label: 'Department' },
  { field: 'company', label: 'Company' },
  { field: 'telephoneNumber', label: 'Phone' },
  { field: 'enabled', label: 'Status' },
  { field: 'whenChanged', label: 'Modified' },
  { field: 'whenCreated', label: 'Created' },
  { field: 'lastLogon', label: 'Last Logon' },
  { field: 'dn', label: 'Distinguished Name' },
]

const defaultColumns = ['name', 'samAccountName', 'description', 'enabled', 'whenChanged']

function loadSavedColumns(): string[] {
  try {
    const saved = localStorage.getItem(COLUMN_STORAGE_KEY)
    if (saved) return JSON.parse(saved)
  } catch { /* ignore */ }
  return defaultColumns
}

const selectedColumnFields = ref<string[]>(loadSavedColumns())
const users = ref<ObjectSummary[]>([])
const loading = ref(true)
const filterText = ref('')
const selectedUser = ref<ObjectSummary | null>(null)
const selectedUsers = ref<ObjectSummary[]>([])
const bulkDialogVisible = ref(false)
const propertySheetVisible = ref(false)
const propertySheetGuid = ref('')
const createUserVisible = ref(false)
const resetPasswordVisible = ref(false)
const resetPasswordGuid = ref('')

// Member Of dialog state
const memberOfDialogVisible = ref(false)
const memberOfGroups = ref<ObjectSummary[]>([])
const memberOfLoading = ref(false)
const managingUser = ref<ObjectSummary | null>(null)
const groupSearchQuery = ref('')
const groupSearchResults = ref<ObjectSummary[]>([])
const groupSearching = ref(false)

onMounted(() => loadUsers())

async function loadUsers() {
  loading.value = true
  try {
    const result = await searchObjects('', '(objectClass=user)', 500)
    users.value = result.items
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    loading.value = false
  }
}

const filteredUsers = computed(() => {
  if (!filterText.value) return users.value
  const q = filterText.value.toLowerCase()
  return users.value.filter(
    (u) =>
      (u.name?.toLowerCase().includes(q)) ||
      (u.samAccountName?.toLowerCase().includes(q)) ||
      (u.description?.toLowerCase().includes(q))
  )
})

async function onEnable() {
  if (!selectedUser.value?.objectGuid) return
  try {
    await enableUser(selectedUser.value.objectGuid)
    toast.add({ severity: 'success', summary: 'Enabled', detail: 'User account enabled', life: 3000 })
    await loadUsers()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

async function onDisable() {
  if (!selectedUser.value?.objectGuid) return
  try {
    await disableUser(selectedUser.value.objectGuid)
    toast.add({ severity: 'success', summary: 'Disabled', detail: 'User account disabled', life: 3000 })
    await loadUsers()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

async function onUnlock() {
  if (!selectedUser.value?.objectGuid) return
  try {
    await unlockUser(selectedUser.value.objectGuid)
    toast.add({ severity: 'success', summary: 'Unlocked', detail: 'User account unlocked', life: 3000 })
    await loadUsers()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

function onResetPassword() {
  if (!selectedUser.value?.objectGuid) return
  resetPasswordGuid.value = selectedUser.value.objectGuid
  resetPasswordVisible.value = true
}

async function onDelete() {
  if (!selectedUser.value?.objectGuid) return
  const target = selectedUser.value
  confirm.require({
    message: `Are you sure you want to delete the user "${target.name}"? This action cannot be undone.`,
    header: 'Delete User',
    icon: 'pi pi-exclamation-triangle',
    rejectLabel: 'Cancel',
    acceptLabel: 'Delete',
    acceptProps: { severity: 'danger' },
    accept: async () => {
      try {
        await deleteObject(target.objectGuid!)
        toast.add({ severity: 'success', summary: 'Deleted', detail: 'User deleted', life: 3000 })
        selectedUser.value = null
        await loadUsers()
      } catch (e: any) {
        toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
      }
    },
  })
}

function onExport() {
  const columns = selectedColumnFields.value.map(f => ({
    field: f,
    header: allAvailableColumns.find(c => c.field === f)?.label || f,
  }))
  exportToCsv(filteredUsers.value as Record<string, any>[], columns, 'users')
}

function onRowDoubleClick(event: { data: ObjectSummary }) {
  if (event.data.objectGuid) {
    propertySheetGuid.value = event.data.objectGuid
    propertySheetVisible.value = true
  }
}

// Member Of dialog functions
async function openMemberOf() {
  if (!selectedUser.value?.objectGuid) return
  managingUser.value = selectedUser.value
  memberOfDialogVisible.value = true
  memberOfLoading.value = true
  groupSearchQuery.value = ''
  groupSearchResults.value = []
  try {
    memberOfGroups.value = await getUserGroups(selectedUser.value.objectGuid)
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    memberOfLoading.value = false
  }
}

let searchTimeout: ReturnType<typeof setTimeout> | null = null

function onGroupSearchInput() {
  if (searchTimeout) clearTimeout(searchTimeout)
  if (!groupSearchQuery.value || groupSearchQuery.value.length < 2) {
    groupSearchResults.value = []
    return
  }
  searchTimeout = setTimeout(() => searchGroups(), 300)
}

async function searchGroups() {
  if (!groupSearchQuery.value || groupSearchQuery.value.length < 2) return
  groupSearching.value = true
  try {
    const result = await searchObjects('', `(&(objectClass=group)(cn=*${groupSearchQuery.value}*))`, 20)
    // Filter out groups the user is already a member of
    const currentDns = new Set(memberOfGroups.value.map((g) => g.dn))
    groupSearchResults.value = result.items.filter((g) => !currentDns.has(g.dn))
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    groupSearching.value = false
  }
}

async function onAddToGroup(group: ObjectSummary) {
  if (!managingUser.value?.dn || !group.objectGuid) return
  try {
    await addGroupMember(group.objectGuid, managingUser.value.dn)
    toast.add({ severity: 'success', summary: 'Added', detail: `Added to ${group.name}`, life: 3000 })
    groupSearchQuery.value = ''
    groupSearchResults.value = []
    memberOfGroups.value = await getUserGroups(managingUser.value.objectGuid!)
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

async function onRemoveFromGroup(group: ObjectSummary) {
  if (!managingUser.value?.dn || !group.objectGuid) return
  try {
    await removeGroupMember(group.objectGuid, managingUser.value.dn)
    toast.add({ severity: 'success', summary: 'Removed', detail: `Removed from ${group.name}`, life: 3000 })
    memberOfGroups.value = await getUserGroups(managingUser.value.objectGuid!)
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}
</script>

<template>
  <div>
    <div class="page-header">
      <h1>Users</h1>
      <p>Manage user accounts</p>
    </div>

    <div class="toolbar">
      <Button v-permission="'users:write'" label="Create User" icon="pi pi-user-plus" size="small" @click="createUserVisible = true" />
      <Button v-permission="'users:write'" label="Enable" icon="pi pi-check" size="small" severity="success" outlined
              @click="onEnable" :disabled="!selectedUser?.objectGuid"
              v-tooltip="'Enable the selected user account'" />
      <Button v-permission="'users:write'" label="Disable" icon="pi pi-ban" size="small" severity="warn" outlined
              @click="onDisable" :disabled="!selectedUser?.objectGuid"
              v-tooltip="'Disable the selected user account'" />
      <Button v-permission="'users:write'" label="Reset Password" icon="pi pi-key" size="small" severity="secondary" outlined
              @click="onResetPassword" :disabled="!selectedUser?.objectGuid"
              v-tooltip="'Reset the password for the selected user'" />
      <Button v-permission="'users:write'" label="Unlock" icon="pi pi-lock-open" size="small" severity="secondary" outlined
              @click="onUnlock" :disabled="!selectedUser?.objectGuid"
              v-tooltip="'Unlock a locked-out user account'" />
      <Button label="Member Of" icon="pi pi-users" size="small" severity="secondary" outlined
              @click="openMemberOf" :disabled="!selectedUser?.objectGuid"
              v-tooltip="'View and manage group memberships'" />
      <Button v-permission="'users:write'" icon="pi pi-trash" size="small" severity="danger" text
              @click="onDelete" :disabled="!selectedUser?.objectGuid"
              v-tooltip="'Delete the selected user'" />
      <template v-if="selectedUsers.length > 1">
        <span style="font-size: 0.8125rem; color: var(--p-text-muted-color); margin-left: 0.5rem">{{ selectedUsers.length }} selected</span>
        <Button v-permission="'users:write'" label="Bulk Actions" icon="pi pi-bolt" size="small" severity="warn"
                @click="bulkDialogVisible = true"
                v-tooltip="'Perform operations on all selected users at once'" />
      </template>
      <div class="toolbar-spacer" />
      <Button icon="pi pi-download" label="Export CSV" severity="secondary" outlined size="small"
              @click="onExport" v-tooltip="'Export current list to CSV'" />
      <Button icon="pi pi-th-large" size="small" severity="secondary" text
              @click="columnChooserVisible = true" title="Choose columns" />
      <InputText v-model="filterText" placeholder="Search users..." size="small" style="width: 260px" />
    </div>

    <div v-if="loading" style="text-align: center; padding: 4rem">
      <ProgressSpinner />
    </div>

    <div v-else class="card" style="padding: 0">
      <DataTable
        :value="filteredUsers"
        v-model:selection="selectedUsers"
        selectionMode="multiple"
        dataKey="objectGuid"
        stripedRows
        size="small"
        scrollable
        scrollHeight="calc(100vh - 260px)"
        :paginator="filteredUsers.length > 50"
        :rows="50"
        :rowsPerPageOptions="[25, 50, 100, 200]"
        @row-dblclick="onRowDoubleClick"
        @row-select="(e: any) => { selectedUser = e.data }"
      >
        <Column selectionMode="multiple" headerStyle="width: 3rem" />
        <template v-for="col in selectedColumnFields" :key="col">
          <Column v-if="col === 'name'" header="Name" sortable sortField="name" style="min-width: 220px">
            <template #body="{ data }">
              <div style="display: flex; align-items: center; gap: 0.5rem">
                <i class="pi pi-user" style="color: var(--p-text-muted-color)"></i>
                <span>{{ data.name }}</span>
              </div>
            </template>
          </Column>
          <Column v-else-if="col === 'enabled'" header="Status" sortable sortField="enabled" style="width: 120px">
            <template #body="{ data }">
              <Tag v-if="data.enabled !== undefined"
                   :value="data.enabled ? 'Enabled' : 'Disabled'"
                   :severity="data.enabled ? 'success' : 'danger'" />
            </template>
          </Column>
          <Column v-else-if="col === 'whenChanged'" header="Modified" sortable sortField="whenChanged" style="width: 130px">
            <template #body="{ data }">
              <span style="color: var(--p-text-muted-color)">{{ relativeTime(data.whenChanged) }}</span>
            </template>
          </Column>
          <Column v-else-if="col === 'whenCreated'" header="Created" sortable sortField="whenCreated" style="width: 130px">
            <template #body="{ data }">
              <span style="color: var(--p-text-muted-color)">{{ relativeTime(data.whenCreated) }}</span>
            </template>
          </Column>
          <Column v-else
            :field="col"
            :header="allAvailableColumns.find(c => c.field === col)?.label || col"
            sortable
            style="min-width: 150px"
          >
            <template #body="{ data }">
              <span style="color: var(--p-text-muted-color)">{{ (data as any)[col] || '' }}</span>
            </template>
          </Column>
        </template>
        <template #empty>
          <div style="text-align: center; padding: 2rem; color: var(--p-text-muted-color)">
            <i class="pi pi-info-circle" style="font-size: 1.5rem; display: block; margin-bottom: 0.5rem"></i>
            <p style="margin: 0; font-size: 0.875rem">No users found. Use the <strong>Create User</strong> button to add a new account, or adjust your search filter.</p>
          </div>
        </template>
      </DataTable>
    </div>

    <!-- Member Of Dialog -->
    <Dialog v-model:visible="memberOfDialogVisible" :header="'Group memberships for ' + (managingUser?.name || '')"
            modal :style="{ width: '600px' }">
      <div v-if="memberOfLoading" style="text-align: center; padding: 2rem">
        <ProgressSpinner />
      </div>
      <template v-else>
        <div style="margin-bottom: 1rem">
          <div style="position: relative">
            <InputText v-model="groupSearchQuery" placeholder="Search for a group to add..."
                       style="width: 100%" size="small" @input="onGroupSearchInput" />
            <div v-if="groupSearchResults.length > 0"
                 style="position: absolute; top: 100%; left: 0; right: 0; z-index: 10; background: var(--p-content-background); border: 1px solid var(--p-content-border-color); border-radius: 0 0 6px 6px; max-height: 200px; overflow-y: auto; box-shadow: 0 4px 6px -1px rgba(0,0,0,0.1)">
              <div v-for="group in groupSearchResults" :key="group.objectGuid"
                   style="display: flex; align-items: center; justify-content: space-between; padding: 0.5rem 0.75rem; cursor: pointer; border-bottom: 1px solid var(--p-content-border-color)"
                   @click="onAddToGroup(group)">
                <div style="display: flex; align-items: center; gap: 0.5rem">
                  <i class="pi pi-users" style="color: var(--p-text-muted-color)"></i>
                  <span>{{ group.name }}</span>
                  <span v-if="group.description" style="color: var(--p-text-muted-color); font-size: 0.85em">
                    - {{ group.description }}
                  </span>
                </div>
                <i class="pi pi-plus" style="color: var(--p-primary-color)"></i>
              </div>
            </div>
            <div v-if="groupSearching" style="position: absolute; right: 0.5rem; top: 50%; transform: translateY(-50%)">
              <i class="pi pi-spin pi-spinner" style="color: var(--p-text-muted-color)"></i>
            </div>
          </div>
        </div>
        <DataTable :value="memberOfGroups" stripedRows size="small" scrollable scrollHeight="300px">
          <Column header="Group" style="min-width: 300px">
            <template #body="{ data }">
              <div style="display: flex; align-items: center; gap: 0.5rem">
                <i class="pi pi-users" style="color: var(--p-text-muted-color)"></i>
                <span>{{ data.name || cnFromDn(data.dn) }}</span>
              </div>
            </template>
          </Column>
          <Column field="description" header="Description" style="width: 200px">
            <template #body="{ data }">
              <span style="color: var(--p-text-muted-color)">{{ data.description || '' }}</span>
            </template>
          </Column>
          <Column style="width: 60px">
            <template #body="{ data }">
              <Button icon="pi pi-times" size="small" severity="danger" text
                      @click="onRemoveFromGroup(data)" />
            </template>
          </Column>
          <template #empty>
            <div style="text-align: center; padding: 1rem; color: var(--p-text-muted-color)">Not a member of any groups</div>
          </template>
        </DataTable>
      </template>
    </Dialog>

    <PropertySheet
      v-if="propertySheetVisible"
      :objectGuid="propertySheetGuid"
      :visible="propertySheetVisible"
      @update:visible="propertySheetVisible = $event"
    />
    <CreateUserDialog
      :visible="createUserVisible"
      containerDn=""
      @update:visible="createUserVisible = $event"
      @created="loadUsers"
    />
    <ResetPasswordDialog
      :visible="resetPasswordVisible"
      :userGuid="resetPasswordGuid"
      @update:visible="resetPasswordVisible = $event"
      @reset="loadUsers"
    />
    <BulkOperationDialog
      :visible="bulkDialogVisible"
      :selectedDns="selectedUsers.map(u => u.dn)"
      @update:visible="bulkDialogVisible = $event"
      @completed="loadUsers"
    />
    <ColumnChooser
      :visible="columnChooserVisible"
      :availableColumns="allAvailableColumns"
      :selectedColumns="selectedColumnFields"
      :storageKey="COLUMN_STORAGE_KEY"
      @update:visible="columnChooserVisible = $event"
      @update:selectedColumns="selectedColumnFields = $event"
    />
  </div>
</template>
