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
import { getGroupMembers, addGroupMember, removeGroupMember } from '../api/groups'
import type { ObjectSummary } from '../api/types'
import { relativeTime, cnFromDn } from '../utils/format'
import CreateGroupDialog from '../components/CreateGroupDialog.vue'
import ColumnChooser from '../components/ColumnChooser.vue'
import { exportToCsv } from '../composables/useExport'

const toast = useToast()
const confirm = useConfirm()

// Column chooser
const columnChooserVisible = ref(false)
const GROUP_COLUMN_KEY = 'grouplist-columns'
const allAvailableGroupColumns = [
  { field: 'name', label: 'Name' },
  { field: 'samAccountName', label: 'sAMAccountName' },
  { field: 'description', label: 'Description' },
  { field: 'mail', label: 'Email' },
  { field: 'groupType', label: 'Group Type' },
  { field: 'whenChanged', label: 'Modified' },
  { field: 'whenCreated', label: 'Created' },
  { field: 'dn', label: 'Distinguished Name' },
]
const defaultGroupColumns = ['name', 'samAccountName', 'description', 'whenChanged']
function loadGroupColumns(): string[] {
  try { const s = localStorage.getItem(GROUP_COLUMN_KEY); if (s) return JSON.parse(s) } catch {}
  return defaultGroupColumns
}
const selectedGroupColumns = ref<string[]>(loadGroupColumns())
const groups = ref<ObjectSummary[]>([])
const loading = ref(true)
const filterText = ref('')
const selectedGroup = ref<ObjectSummary | null>(null)
const createGroupVisible = ref(false)

// Member management
const membersDialogVisible = ref(false)
const members = ref<ObjectSummary[]>([])
const membersLoading = ref(false)
const managingGroup = ref<ObjectSummary | null>(null)

// Member search
const memberSearchQuery = ref('')
const memberSearchResults = ref<ObjectSummary[]>([])
const memberSearching = ref(false)

onMounted(() => loadGroups())

async function loadGroups() {
  loading.value = true
  try {
    const result = await searchObjects('', '(objectClass=group)', 500)
    groups.value = result.items
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    loading.value = false
  }
}

const filteredGroups = computed(() => {
  if (!filterText.value) return groups.value
  const q = filterText.value.toLowerCase()
  return groups.value.filter(
    (g) =>
      (g.name?.toLowerCase().includes(q)) ||
      (g.samAccountName?.toLowerCase().includes(q)) ||
      (g.description?.toLowerCase().includes(q))
  )
})

async function openMembers(group: ObjectSummary) {
  if (!group.objectGuid) return
  managingGroup.value = group
  membersDialogVisible.value = true
  membersLoading.value = true
  memberSearchQuery.value = ''
  memberSearchResults.value = []
  try {
    members.value = await getGroupMembers(group.objectGuid)
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    membersLoading.value = false
  }
}

let searchTimeout: ReturnType<typeof setTimeout> | null = null

function onMemberSearchInput() {
  if (searchTimeout) clearTimeout(searchTimeout)
  if (!memberSearchQuery.value || memberSearchQuery.value.length < 2) {
    memberSearchResults.value = []
    return
  }
  searchTimeout = setTimeout(() => searchMembers(), 300)
}

async function searchMembers() {
  if (!memberSearchQuery.value || memberSearchQuery.value.length < 2) return
  memberSearching.value = true
  try {
    const result = await searchObjects('', `(&(|(objectClass=user)(objectClass=computer)(objectClass=group))(cn=*${memberSearchQuery.value}*))`, 20)
    // Filter out objects already in the group
    const currentDns = new Set(members.value.map((m) => m.dn))
    memberSearchResults.value = result.items.filter((item) => !currentDns.has(item.dn))
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    memberSearching.value = false
  }
}

async function onAddMember(member: ObjectSummary) {
  if (!managingGroup.value?.objectGuid || !member.dn) return
  try {
    await addGroupMember(managingGroup.value.objectGuid, member.dn)
    toast.add({ severity: 'success', summary: 'Added', detail: `Added ${member.name} to group`, life: 3000 })
    memberSearchQuery.value = ''
    memberSearchResults.value = []
    members.value = await getGroupMembers(managingGroup.value.objectGuid)
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

async function onRemoveMember(memberDn: string) {
  if (!managingGroup.value?.objectGuid) return
  try {
    await removeGroupMember(managingGroup.value.objectGuid, memberDn)
    toast.add({ severity: 'success', summary: 'Removed', detail: 'Member removed from group', life: 3000 })
    members.value = await getGroupMembers(managingGroup.value.objectGuid)
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

async function onDelete() {
  if (!selectedGroup.value?.objectGuid) return
  const target = selectedGroup.value
  confirm.require({
    message: `Are you sure you want to delete the group "${target.name}"? This action cannot be undone.`,
    header: 'Delete Group',
    icon: 'pi pi-exclamation-triangle',
    rejectLabel: 'Cancel',
    acceptLabel: 'Delete',
    acceptProps: { severity: 'danger' },
    accept: async () => {
      try {
        await deleteObject(target.objectGuid!)
        toast.add({ severity: 'success', summary: 'Deleted', detail: 'Group deleted', life: 3000 })
        selectedGroup.value = null
        await loadGroups()
      } catch (e: any) {
        toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
      }
    },
  })
}

function memberIcon(objectClass: string): string {
  if (objectClass === 'group') return 'pi pi-users'
  if (objectClass === 'computer') return 'pi pi-desktop'
  return 'pi pi-user'
}

function onExport() {
  const columns = selectedGroupColumns.value.map(f => ({
    field: f,
    header: allAvailableGroupColumns.find(c => c.field === f)?.label || f,
  }))
  exportToCsv(filteredGroups.value as Record<string, any>[], columns, 'groups')
}

function onRowDoubleClick(event: { data: ObjectSummary }) {
  openMembers(event.data)
}
</script>

<template>
  <div>
    <div class="page-header">
      <h1>Groups</h1>
      <p>Manage security and distribution groups</p>
    </div>

    <div class="toolbar">
      <Button v-permission="'groups:write'" label="Create Group" icon="pi pi-users" size="small" @click="createGroupVisible = true" />
      <Button label="Members" icon="pi pi-list" size="small" severity="secondary" outlined
              @click="selectedGroup && openMembers(selectedGroup)"
              :disabled="!selectedGroup?.objectGuid"
              v-tooltip="'View and manage group members'" />
      <Button v-permission="'groups:write'" label="Delete" icon="pi pi-trash" size="small" severity="danger" outlined
              @click="onDelete"
              :disabled="!selectedGroup?.objectGuid"
              v-tooltip="'Delete the selected group'" />
      <div class="toolbar-spacer" />
      <Button icon="pi pi-download" label="Export CSV" severity="secondary" outlined size="small"
              @click="onExport" v-tooltip="'Export current list to CSV'" />
      <Button icon="pi pi-th-large" size="small" severity="secondary" text
              @click="columnChooserVisible = true" title="Choose columns" />
      <InputText v-model="filterText" placeholder="Search groups..." size="small" style="width: 260px" />
    </div>

    <div v-if="loading" style="text-align: center; padding: 4rem">
      <ProgressSpinner />
    </div>

    <div v-else class="card" style="padding: 0">
      <DataTable
        :value="filteredGroups"
        v-model:selection="selectedGroup"
        selectionMode="single"
        dataKey="objectGuid"
        stripedRows
        size="small"
        scrollable
        scrollHeight="calc(100vh - 260px)"
        :paginator="filteredGroups.length > 50"
        :rows="50"
        :rowsPerPageOptions="[25, 50, 100, 200]"
        @row-dblclick="onRowDoubleClick"
      >
        <template v-for="col in selectedGroupColumns" :key="col">
          <Column v-if="col === 'name'" header="Name" sortable sortField="name" style="min-width: 250px">
            <template #body="{ data }">
              <div style="display: flex; align-items: center; gap: 0.5rem">
                <i class="pi pi-users" style="color: var(--p-text-muted-color)"></i>
                <span>{{ data.name }}</span>
              </div>
            </template>
          </Column>
          <Column v-else-if="col === 'whenChanged'" header="Modified" sortable sortField="whenChanged" style="width: 130px">
            <template #body="{ data }">
              <span style="color: var(--p-text-muted-color)">{{ relativeTime(data.whenChanged) }}</span>
            </template>
          </Column>
          <Column v-else
            :field="col"
            :header="allAvailableGroupColumns.find(c => c.field === col)?.label || col"
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
            <p style="margin: 0; font-size: 0.875rem">No groups found. Use the <strong>Create Group</strong> button to add a new security or distribution group, or adjust your search filter.</p>
          </div>
        </template>
      </DataTable>
    </div>

    <!-- Members Dialog -->
    <Dialog v-model:visible="membersDialogVisible" :header="'Members of ' + (managingGroup?.name || '')"
            modal :style="{ width: '600px' }">
      <div v-if="membersLoading" style="text-align: center; padding: 2rem">
        <ProgressSpinner />
      </div>
      <template v-else>
        <div style="margin-bottom: 1rem">
          <div style="position: relative">
            <InputText v-model="memberSearchQuery" placeholder="Search for users, groups, or computers to add..."
                       style="width: 100%" size="small" @input="onMemberSearchInput" />
            <div v-if="memberSearchResults.length > 0"
                 style="position: absolute; top: 100%; left: 0; right: 0; z-index: 10; background: var(--p-content-background); border: 1px solid var(--p-content-border-color); border-radius: 0 0 6px 6px; max-height: 200px; overflow-y: auto; box-shadow: 0 4px 6px -1px rgba(0,0,0,0.1)">
              <div v-for="item in memberSearchResults" :key="item.objectGuid"
                   style="display: flex; align-items: center; justify-content: space-between; padding: 0.5rem 0.75rem; cursor: pointer; border-bottom: 1px solid var(--p-content-border-color)"
                   @click="onAddMember(item)">
                <div style="display: flex; align-items: center; gap: 0.5rem">
                  <i :class="memberIcon(item.objectClass)" style="color: var(--p-text-muted-color)"></i>
                  <span>{{ item.name }}</span>
                  <span style="color: var(--p-text-muted-color); font-size: 0.85em">
                    ({{ item.objectClass }})
                  </span>
                </div>
                <i class="pi pi-plus" style="color: var(--p-primary-color)"></i>
              </div>
            </div>
            <div v-if="memberSearching" style="position: absolute; right: 0.5rem; top: 50%; transform: translateY(-50%)">
              <i class="pi pi-spin pi-spinner" style="color: var(--p-text-muted-color)"></i>
            </div>
          </div>
        </div>
        <DataTable :value="members" stripedRows size="small" scrollable scrollHeight="300px">
          <Column header="Member" style="min-width: 300px">
            <template #body="{ data }">
              <div style="display: flex; align-items: center; gap: 0.5rem">
                <i :class="memberIcon(data.objectClass)" style="color: var(--p-text-muted-color)"></i>
                <span>{{ data.name || cnFromDn(data.dn) }}</span>
              </div>
            </template>
          </Column>
          <Column field="objectClass" header="Type" style="width: 120px" />
          <Column style="width: 60px">
            <template #body="{ data }">
              <Button icon="pi pi-times" size="small" severity="danger" text
                      @click="onRemoveMember(data.dn)" />
            </template>
          </Column>
          <template #empty>
            <div style="text-align: center; padding: 1rem; color: var(--p-text-muted-color)">No members</div>
          </template>
        </DataTable>
      </template>
    </Dialog>

    <CreateGroupDialog
      :visible="createGroupVisible"
      containerDn=""
      @update:visible="createGroupVisible = $event"
      @created="loadGroups"
    />
    <ColumnChooser
      :visible="columnChooserVisible"
      :availableColumns="allAvailableGroupColumns"
      :selectedColumns="selectedGroupColumns"
      :storageKey="GROUP_COLUMN_KEY"
      @update:visible="columnChooserVisible = $event"
      @update:selectedColumns="selectedGroupColumns = $event"
    />
  </div>
</template>
