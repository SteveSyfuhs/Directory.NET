<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Textarea from 'primevue/textarea'
import Tag from 'primevue/tag'
import Chip from 'primevue/chip'
import Dialog from 'primevue/dialog'
import Checkbox from 'primevue/checkbox'
import TabView from 'primevue/tabview'
import TabPanel from 'primevue/tabpanel'
import ProgressSpinner from 'primevue/progressspinner'
import { useToast } from 'primevue/usetoast'
import {
  listRoles, getRole, createRole, updateRole, deleteRole,
  listPermissions, getEffectivePermissions,
  assignMember, removeMember,
} from '../api/delegation'
import type { AdminRole, DelegationPermission, EffectivePermissions } from '../types/delegation'
import PageHeader from '../components/PageHeader.vue'

const toast = useToast()

// Data
const roles = ref<AdminRole[]>([])
const permissions = ref<DelegationPermission[]>([])
const loading = ref(true)
const filterText = ref('')

// Role editor dialog
const editVisible = ref(false)
const editSaving = ref(false)
const isNew = ref(false)
const editForm = ref({
  id: '',
  name: '',
  description: '',
  permissions: [] as string[],
  scopeDns: [] as string[],
  assignedMembers: [] as string[],
  isBuiltIn: false,
})
const newScopeDn = ref('')
const newMemberDn = ref('')

// Detail dialog
const detailVisible = ref(false)
const detailRole = ref<AdminRole | null>(null)
const detailLoading = ref(false)

// Delete confirm
const deleteVisible = ref(false)
const deleteTarget = ref<AdminRole | null>(null)
const deleting = ref(false)

// User lookup
const lookupDn = ref('')
const lookupLoading = ref(false)
const lookupResult = ref<EffectivePermissions | null>(null)

// Computed
const filteredRoles = computed(() => {
  if (!filterText.value) return roles.value
  const q = filterText.value.toLowerCase()
  return roles.value.filter(r =>
    r.name.toLowerCase().includes(q) || r.description.toLowerCase().includes(q)
  )
})

const permissionsByCategory = computed(() => {
  const grouped: Record<string, DelegationPermission[]> = {}
  for (const p of permissions.value) {
    if (!grouped[p.category]) grouped[p.category] = []
    grouped[p.category].push(p)
  }
  return grouped
})

onMounted(async () => {
  await Promise.all([loadRoles(), loadPermissions()])
})

async function loadRoles() {
  loading.value = true
  try {
    roles.value = await listRoles()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    loading.value = false
  }
}

async function loadPermissions() {
  try {
    permissions.value = await listPermissions()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

// Create / Edit
function openCreate() {
  isNew.value = true
  editForm.value = {
    id: '',
    name: '',
    description: '',
    permissions: [],
    scopeDns: [],
    assignedMembers: [],
    isBuiltIn: false,
  }
  newScopeDn.value = ''
  newMemberDn.value = ''
  editVisible.value = true
}

function openEdit(role: AdminRole) {
  isNew.value = false
  editForm.value = {
    id: role.id,
    name: role.name,
    description: role.description,
    permissions: [...role.permissions],
    scopeDns: [...role.scopeDns],
    assignedMembers: [...role.assignedMembers],
    isBuiltIn: role.isBuiltIn,
  }
  newScopeDn.value = ''
  newMemberDn.value = ''
  editVisible.value = true
}

async function onSaveRole() {
  if (!editForm.value.name.trim()) return
  editSaving.value = true
  try {
    if (isNew.value) {
      await createRole({
        name: editForm.value.name,
        description: editForm.value.description,
        permissions: editForm.value.permissions,
        scopeDns: editForm.value.scopeDns,
        assignedMembers: editForm.value.assignedMembers,
      })
      toast.add({ severity: 'success', summary: 'Created', detail: `Role "${editForm.value.name}" created`, life: 3000 })
    } else {
      await updateRole(editForm.value.id, {
        name: editForm.value.name,
        description: editForm.value.description,
        permissions: editForm.value.permissions,
        scopeDns: editForm.value.scopeDns,
        assignedMembers: editForm.value.assignedMembers,
      })
      toast.add({ severity: 'success', summary: 'Updated', detail: `Role "${editForm.value.name}" updated`, life: 3000 })
    }
    editVisible.value = false
    await loadRoles()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    editSaving.value = false
  }
}

function addScopeDn() {
  const dn = newScopeDn.value.trim()
  if (dn && !editForm.value.scopeDns.includes(dn)) {
    editForm.value.scopeDns.push(dn)
    newScopeDn.value = ''
  }
}

function removeScopeDn(dn: string) {
  editForm.value.scopeDns = editForm.value.scopeDns.filter(d => d !== dn)
}

function addMemberDn() {
  const dn = newMemberDn.value.trim()
  if (dn && !editForm.value.assignedMembers.includes(dn)) {
    editForm.value.assignedMembers.push(dn)
    newMemberDn.value = ''
  }
}

function removeMemberDn(dn: string) {
  editForm.value.assignedMembers = editForm.value.assignedMembers.filter(d => d !== dn)
}

function isPermissionChecked(key: string): boolean {
  return editForm.value.permissions.includes(key)
}

function togglePermission(key: string) {
  if (editForm.value.isBuiltIn) return
  const idx = editForm.value.permissions.indexOf(key)
  if (idx >= 0) {
    editForm.value.permissions.splice(idx, 1)
  } else {
    editForm.value.permissions.push(key)
  }
}

function toggleCategoryAll(category: string) {
  if (editForm.value.isBuiltIn) return
  const catPerms = permissionsByCategory.value[category] || []
  const allChecked = catPerms.every(p => editForm.value.permissions.includes(p.key))
  if (allChecked) {
    const keys = new Set(catPerms.map(p => p.key))
    editForm.value.permissions = editForm.value.permissions.filter(k => !keys.has(k))
  } else {
    for (const p of catPerms) {
      if (!editForm.value.permissions.includes(p.key)) {
        editForm.value.permissions.push(p.key)
      }
    }
  }
}

function isCategoryAllChecked(category: string): boolean {
  const catPerms = permissionsByCategory.value[category] || []
  return catPerms.length > 0 && catPerms.every(p => editForm.value.permissions.includes(p.key))
}

// Detail
async function openDetail(role: AdminRole) {
  detailLoading.value = true
  detailVisible.value = true
  try {
    detailRole.value = await getRole(role.id)
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
    detailVisible.value = false
  } finally {
    detailLoading.value = false
  }
}

// Delete
function confirmDelete(role: AdminRole) {
  deleteTarget.value = role
  deleteVisible.value = true
}

async function onDelete() {
  if (!deleteTarget.value) return
  deleting.value = true
  try {
    await deleteRole(deleteTarget.value.id)
    toast.add({ severity: 'success', summary: 'Deleted', detail: `Role "${deleteTarget.value.name}" deleted`, life: 3000 })
    deleteVisible.value = false
    await loadRoles()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    deleting.value = false
  }
}

// User lookup
async function onLookup() {
  if (!lookupDn.value.trim()) return
  lookupLoading.value = true
  lookupResult.value = null
  try {
    lookupResult.value = await getEffectivePermissions(lookupDn.value.trim())
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    lookupLoading.value = false
  }
}

function getPermissionDisplay(key: string): string {
  const p = permissions.value.find(perm => perm.key === key)
  return p ? p.displayName : key
}
</script>

<template>
  <PageHeader title="Delegated Administration" subtitle="Manage administrative roles and permissions for delegated access control" />

  <TabView>
    <TabPanel header="Roles" value="roles">
      <div class="toolbar">
        <InputText v-model="filterText" placeholder="Filter roles..." style="width: 280px" />
        <span class="toolbar-spacer"></span>
        <Button label="Create Role" icon="pi pi-plus" @click="openCreate" />
      </div>

      <ProgressSpinner v-if="loading" strokeWidth="3" style="display: block; margin: 2rem auto" />

      <DataTable
        v-else
        :value="filteredRoles"
        stripedRows
        :rows="20"
        :paginator="filteredRoles.length > 20"
        @row-click="(e: any) => openDetail(e.data)"
        class="role-table"
      >
        <Column field="name" header="Name" sortable>
          <template #body="{ data }">
            <div style="display: flex; align-items: center; gap: 0.5rem">
              <span style="font-weight: 600">{{ data.name }}</span>
              <Tag v-if="data.isBuiltIn" value="Built-in" severity="info" />
            </div>
          </template>
        </Column>
        <Column field="description" header="Description" />
        <Column header="Members" style="width: 100px">
          <template #body="{ data }">
            <Tag :value="String(data.assignedMembers.length)" severity="secondary" />
          </template>
        </Column>
        <Column header="Permissions" style="width: 120px">
          <template #body="{ data }">
            <Tag :value="String(data.permissions.length)" severity="secondary" />
          </template>
        </Column>
        <Column header="Scope" style="width: 120px">
          <template #body="{ data }">
            <span v-if="data.scopeDns.length === 0" style="color: var(--p-text-muted-color)">Global</span>
            <Tag v-else :value="`${data.scopeDns.length} OU(s)`" severity="warn" />
          </template>
        </Column>
        <Column header="Actions" style="width: 140px">
          <template #body="{ data }">
            <div style="display: flex; gap: 0.25rem">
              <Button icon="pi pi-pencil" text rounded size="small" @click.stop="openEdit(data)" v-tooltip="'Edit'" />
              <Button
                icon="pi pi-trash"
                text rounded size="small"
                severity="danger"
                :disabled="data.isBuiltIn"
                @click.stop="confirmDelete(data)"
                v-tooltip="data.isBuiltIn ? 'Built-in roles cannot be deleted' : 'Delete'"
              />
            </div>
          </template>
        </Column>
      </DataTable>
    </TabPanel>

    <TabPanel header="User Lookup" value="user-lookup">
      <div class="card" style="max-width: 800px">
        <h3 style="margin-top: 0; margin-bottom: 1rem; color: var(--p-text-color)">Effective Permissions Lookup</h3>
        <p style="color: var(--p-text-muted-color); margin-bottom: 1rem">
          Enter a user DN to see their effective permissions from all assigned roles.
        </p>
        <div style="display: flex; gap: 0.5rem; margin-bottom: 1.5rem">
          <InputText
            v-model="lookupDn"
            placeholder="CN=John Doe,OU=Users,DC=example,DC=com"
            style="flex: 1"
            @keydown.enter="onLookup"
          />
          <Button label="Lookup" icon="pi pi-search" :loading="lookupLoading" @click="onLookup" />
        </div>

        <div v-if="lookupResult">
          <h4 style="margin-bottom: 0.75rem; color: var(--p-text-color)">
            Roles ({{ lookupResult.roles.length }})
          </h4>
          <div v-if="lookupResult.roles.length === 0" style="color: var(--p-text-muted-color); margin-bottom: 1rem">
            No roles assigned to this user.
          </div>
          <div v-else style="display: flex; flex-wrap: wrap; gap: 0.5rem; margin-bottom: 1.5rem">
            <Tag v-for="r in lookupResult.roles" :key="r.roleId" :value="r.roleName" severity="info" />
          </div>

          <h4 style="margin-bottom: 0.75rem; color: var(--p-text-color)">
            Effective Permissions ({{ lookupResult.permissions.length }})
          </h4>
          <div v-if="lookupResult.permissions.length === 0" style="color: var(--p-text-muted-color)">
            No permissions.
          </div>
          <div v-else style="display: flex; flex-wrap: wrap; gap: 0.375rem">
            <Tag
              v-for="perm in lookupResult.permissions"
              :key="perm"
              :value="getPermissionDisplay(perm)"
              severity="success"
            />
          </div>
        </div>
      </div>
    </TabPanel>
  </TabView>

  <!-- Create / Edit Role Dialog -->
  <Dialog
    v-model:visible="editVisible"
    :header="isNew ? 'Create Role' : `Edit Role: ${editForm.name}`"
    :style="{ width: '720px' }"
    modal
  >
    <div style="display: flex; flex-direction: column; gap: 1rem">
      <div>
        <label style="display: block; font-weight: 600; margin-bottom: 0.375rem; color: var(--p-text-color)">Name</label>
        <InputText v-model="editForm.name" :disabled="editForm.isBuiltIn" style="width: 100%" />
      </div>
      <div>
        <label style="display: block; font-weight: 600; margin-bottom: 0.375rem; color: var(--p-text-color)">Description</label>
        <Textarea v-model="editForm.description" :disabled="editForm.isBuiltIn" rows="2" style="width: 100%" />
      </div>

      <!-- Permissions -->
      <div>
        <label style="display: block; font-weight: 600; margin-bottom: 0.5rem; color: var(--p-text-color)">
          Permissions ({{ editForm.permissions.length }} selected)
        </label>
        <div class="permissions-grid">
          <div v-for="(catPerms, category) in permissionsByCategory" :key="category" class="permission-category">
            <div class="category-header" @click="toggleCategoryAll(String(category))">
              <Checkbox
                :modelValue="isCategoryAllChecked(String(category))"
                :disabled="editForm.isBuiltIn"
                binary
                @click.stop="toggleCategoryAll(String(category))"
              />
              <span class="category-label">{{ category }}</span>
            </div>
            <div v-for="p in catPerms" :key="p.key" class="permission-item" @click="togglePermission(p.key)">
              <Checkbox
                :modelValue="isPermissionChecked(p.key)"
                :disabled="editForm.isBuiltIn"
                binary
                @click.stop="togglePermission(p.key)"
              />
              <span :title="p.description">{{ p.displayName }}</span>
            </div>
          </div>
        </div>
      </div>

      <!-- Scope DNs -->
      <div>
        <label style="display: block; font-weight: 600; margin-bottom: 0.375rem; color: var(--p-text-color)">
          Scope (leave empty for global)
        </label>
        <div style="display: flex; gap: 0.5rem; margin-bottom: 0.5rem">
          <InputText v-model="newScopeDn" placeholder="OU=Sales,DC=example,DC=com" style="flex: 1" @keydown.enter="addScopeDn" />
          <Button icon="pi pi-plus" @click="addScopeDn" :disabled="!newScopeDn.trim()" />
        </div>
        <div v-if="editForm.scopeDns.length" style="display: flex; flex-wrap: wrap; gap: 0.375rem">
          <Chip v-for="dn in editForm.scopeDns" :key="dn" :label="dn" removable @remove="removeScopeDn(dn)" />
        </div>
        <div v-else style="color: var(--p-text-muted-color); font-size: 0.875rem">Global scope (all OUs)</div>
      </div>

      <!-- Members -->
      <div>
        <label style="display: block; font-weight: 600; margin-bottom: 0.375rem; color: var(--p-text-color)">
          Assigned Members ({{ editForm.assignedMembers.length }})
        </label>
        <div style="display: flex; gap: 0.5rem; margin-bottom: 0.5rem">
          <InputText v-model="newMemberDn" placeholder="CN=User,OU=Users,DC=example,DC=com" style="flex: 1" @keydown.enter="addMemberDn" />
          <Button icon="pi pi-plus" @click="addMemberDn" :disabled="!newMemberDn.trim()" />
        </div>
        <div v-if="editForm.assignedMembers.length" style="display: flex; flex-wrap: wrap; gap: 0.375rem">
          <Chip v-for="dn in editForm.assignedMembers" :key="dn" :label="dn" removable @remove="removeMemberDn(dn)" />
        </div>
        <div v-else style="color: var(--p-text-muted-color); font-size: 0.875rem">No members assigned</div>
      </div>
    </div>

    <template #footer>
      <Button label="Cancel" text @click="editVisible = false" />
      <Button
        :label="isNew ? 'Create' : 'Save'"
        icon="pi pi-check"
        :loading="editSaving"
        @click="onSaveRole"
        :disabled="!editForm.name.trim()"
      />
    </template>
  </Dialog>

  <!-- Role Detail Dialog -->
  <Dialog
    v-model:visible="detailVisible"
    :header="detailRole ? detailRole.name : 'Role Details'"
    :style="{ width: '600px' }"
    modal
  >
    <ProgressSpinner v-if="detailLoading" strokeWidth="3" style="display: block; margin: 2rem auto" />
    <div v-else-if="detailRole" style="display: flex; flex-direction: column; gap: 1rem">
      <div>
        <span style="font-weight: 600; color: var(--p-text-muted-color); font-size: 0.8125rem; text-transform: uppercase; letter-spacing: 0.04em">Description</span>
        <p style="margin: 0.25rem 0 0; color: var(--p-text-color)">{{ detailRole.description || 'No description' }}</p>
      </div>

      <div>
        <span style="font-weight: 600; color: var(--p-text-muted-color); font-size: 0.8125rem; text-transform: uppercase; letter-spacing: 0.04em">
          Permissions ({{ detailRole.permissions.length }})
        </span>
        <div style="display: flex; flex-wrap: wrap; gap: 0.375rem; margin-top: 0.5rem">
          <Tag v-for="perm in detailRole.permissions" :key="perm" :value="getPermissionDisplay(perm)" severity="success" />
        </div>
      </div>

      <div>
        <span style="font-weight: 600; color: var(--p-text-muted-color); font-size: 0.8125rem; text-transform: uppercase; letter-spacing: 0.04em">Scope</span>
        <div style="margin-top: 0.5rem">
          <span v-if="detailRole.scopeDns.length === 0" style="color: var(--p-text-muted-color)">Global (all OUs)</span>
          <div v-else style="display: flex; flex-wrap: wrap; gap: 0.375rem">
            <Chip v-for="dn in detailRole.scopeDns" :key="dn" :label="dn" />
          </div>
        </div>
      </div>

      <div>
        <span style="font-weight: 600; color: var(--p-text-muted-color); font-size: 0.8125rem; text-transform: uppercase; letter-spacing: 0.04em">
          Members ({{ detailRole.assignedMembers.length }})
        </span>
        <div style="margin-top: 0.5rem">
          <div v-if="detailRole.assignedMembers.length === 0" style="color: var(--p-text-muted-color)">No members assigned</div>
          <div v-else style="display: flex; flex-wrap: wrap; gap: 0.375rem">
            <Chip v-for="dn in detailRole.assignedMembers" :key="dn" :label="dn" />
          </div>
        </div>
      </div>
    </div>

    <template #footer>
      <Button label="Edit" icon="pi pi-pencil" @click="() => { detailVisible = false; if (detailRole) openEdit(detailRole) }" />
      <Button label="Close" text @click="detailVisible = false" />
    </template>
  </Dialog>

  <!-- Delete Confirmation Dialog -->
  <Dialog
    v-model:visible="deleteVisible"
    header="Delete Role"
    :style="{ width: '420px' }"
    modal
  >
    <p style="color: var(--p-text-color)">
      Are you sure you want to delete the role <strong>{{ deleteTarget?.name }}</strong>?
      This action cannot be undone.
    </p>
    <template #footer>
      <Button label="Cancel" text @click="deleteVisible = false" />
      <Button label="Delete" severity="danger" icon="pi pi-trash" :loading="deleting" @click="onDelete" />
    </template>
  </Dialog>
</template>

<style scoped>
.permissions-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(240px, 1fr));
  gap: 1rem;
  max-height: 320px;
  overflow-y: auto;
  border: 1px solid var(--p-surface-border);
  border-radius: 0.5rem;
  padding: 1rem;
  background: var(--p-surface-ground);
}

.permission-category {
  display: flex;
  flex-direction: column;
  gap: 0.375rem;
}

.category-header {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  cursor: pointer;
  padding: 0.25rem 0;
  border-bottom: 1px solid var(--p-surface-border);
  margin-bottom: 0.25rem;
}

.category-label {
  font-weight: 700;
  font-size: 0.8125rem;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  color: var(--p-text-color);
}

.permission-item {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  cursor: pointer;
  padding: 0.125rem 0 0.125rem 1.25rem;
  font-size: 0.875rem;
  color: var(--p-text-color);
}

.permission-item:hover {
  background: var(--p-surface-hover);
  border-radius: 0.25rem;
}

.role-table :deep(.p-datatable-tbody > tr) {
  cursor: pointer;
}
</style>
