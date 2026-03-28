<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import Tree from 'primevue/tree'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import ContextMenu from 'primevue/contextmenu'
import InputText from 'primevue/inputtext'
import ProgressSpinner from 'primevue/progressspinner'
import { useToast } from 'primevue/usetoast'
import { useConfirm } from 'primevue/useconfirm'
import { useDirectoryStore } from '../stores/directory'
import { deleteObject } from '../api/objects'
import { relativeTime, objectClassIcon } from '../utils/format'
import type { ObjectSummary } from '../api/types'
import PropertySheet from '../components/PropertySheet.vue'
import CreateUserDialog from '../components/CreateUserDialog.vue'
import CreateGroupDialog from '../components/CreateGroupDialog.vue'
import DelegateControlDialog from '../components/DelegateControlDialog.vue'

const toast = useToast()
const confirm = useConfirm()
const store = useDirectoryStore()

const selectedKeys = ref<Record<string, boolean>>({})
const selectedRow = ref<ObjectSummary | null>(null)
const filterText = ref('')
const contextMenuRef = ref()
const propertySheetVisible = ref(false)
const propertySheetGuid = ref('')
const createUserVisible = ref(false)
const createGroupVisible = ref(false)
const delegateControlVisible = ref(false)

const contextMenuItems = ref([
  { label: 'New User', icon: 'pi pi-user-plus', command: () => { createUserVisible.value = true } },
  { label: 'New Group', icon: 'pi pi-users', command: () => { createGroupVisible.value = true } },
  { separator: true },
  { label: 'Delegate Control...', icon: 'pi pi-shield', command: () => { delegateControlVisible.value = true } },
  { separator: true },
  { label: 'Refresh', icon: 'pi pi-refresh', command: () => refreshCurrentNode() },
  { separator: true },
  { label: 'Properties', icon: 'pi pi-info-circle', command: () => openSelectedNodeProperties() },
])

const selectedDn = computed(() => store.selectedNodeDn)

onMounted(async () => {
  await store.loadRoots()
})

async function onNodeExpand(node: any) {
  if (!node.children) {
    await store.loadChildren(node)
  }
}

async function onNodeSelect(node: any) {
  const dn = node.data?.dn || node.key
  store.selectedNodeDn = dn
  await store.loadObjects(dn)
}

function onNodeContextMenu(event: { originalEvent: MouseEvent; node: any }) {
  store.selectedNodeDn = event.node.data?.dn || event.node.key
  contextMenuRef.value.show(event.originalEvent)
}

function onRowDoubleClick(event: { data: ObjectSummary }) {
  if (event.data.objectGuid) {
    propertySheetGuid.value = event.data.objectGuid
    propertySheetVisible.value = true
  }
}

function openSelectedNodeProperties() {
  // If the selected tree node has a guid, open property sheet
  const key = Object.keys(selectedKeys.value)[0]
  if (key) {
    const node = findNodeByKey(store.treeNodes, key)
    if (node?.data?.objectGuid) {
      propertySheetGuid.value = node.data.objectGuid
      propertySheetVisible.value = true
    }
  }
}

function findNodeByKey(nodes: any[], key: string): any {
  for (const n of nodes) {
    if (n.key === key) return n
    if (n.children) {
      const found = findNodeByKey(n.children, key)
      if (found) return found
    }
  }
  return null
}

async function refreshCurrentNode() {
  if (selectedDn.value) {
    await store.loadObjects(selectedDn.value)
  }
}

function onDeleteSelected() {
  if (!selectedRow.value?.objectGuid) return
  const objectLabel = selectedRow.value.name || selectedRow.value.dn
  confirm.require({
    message: `Are you sure you want to delete "${objectLabel}"? This action cannot be undone.`,
    header: 'Confirm Delete',
    icon: 'pi pi-exclamation-triangle',
    acceptClass: 'p-button-danger',
    accept: async () => {
      try {
        await deleteObject(selectedRow.value!.objectGuid!)
        toast.add({ severity: 'success', summary: 'Deleted', detail: 'Object deleted successfully', life: 3000 })
        if (selectedDn.value) await store.loadObjects(selectedDn.value)
      } catch (e: any) {
        toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
      }
    },
  })
}

function onObjectCreated() {
  if (selectedDn.value) store.loadObjects(selectedDn.value)
}

const filteredObjects = computed(() => {
  if (!filterText.value) return store.objects
  const q = filterText.value.toLowerCase()
  return store.objects.filter(
    (o) =>
      (o.name?.toLowerCase().includes(q)) ||
      (o.samAccountName?.toLowerCase().includes(q)) ||
      (o.description?.toLowerCase().includes(q))
  )
})
</script>

<template>
  <div class="browse-layout">
    <!-- Tree Panel -->
    <div class="browse-tree-panel">
      <div class="browse-tree-header">
        <i class="pi pi-sitemap"></i>
        <span>Directory Tree</span>
      </div>
      <div class="browse-tree-content">
        <Tree
          :value="store.treeNodes"
          v-model:selectionKeys="selectedKeys"
          selectionMode="single"
          :loading="store.loading"
          @node-expand="onNodeExpand"
          @node-select="onNodeSelect"
          @node-context-menu="onNodeContextMenu"
          class="directory-tree"
        />
      </div>
      <ContextMenu ref="contextMenuRef" :model="contextMenuItems" />
    </div>

    <!-- Content Panel -->
    <div class="browse-content-panel">
      <div class="toolbar">
        <Button label="New User" icon="pi pi-user-plus" size="small" severity="secondary"
                @click="createUserVisible = true" :disabled="!selectedDn"
                v-tooltip="'Create a new user in the selected container'" />
        <Button label="New Group" icon="pi pi-users" size="small" severity="secondary"
                @click="createGroupVisible = true" :disabled="!selectedDn"
                v-tooltip="'Create a new group in the selected container'" />
        <Button icon="pi pi-trash" size="small" severity="danger" text
                @click="onDeleteSelected" :disabled="!selectedRow?.objectGuid"
                v-tooltip="'Delete selected'" />
        <Button icon="pi pi-refresh" size="small" severity="secondary" text
                @click="refreshCurrentNode" :disabled="!selectedDn"
                v-tooltip="'Refresh'" />
        <div class="toolbar-spacer" />
        <InputText v-model="filterText" placeholder="Filter objects..." size="small"
                   style="width: 220px" />
      </div>

      <div v-if="!selectedDn" class="browse-empty-state">
        <i class="pi pi-arrow-left" style="font-size: 2rem; color: var(--p-text-muted-color)"></i>
        <p>Select a container in the directory tree to browse its contents.</p>
        <p style="font-size: 0.875rem; margin: 0">Expand nodes in the tree to navigate OUs and containers. Right-click a node for options like creating users or groups.</p>
      </div>

      <div v-else-if="store.loading" style="text-align: center; padding: 3rem">
        <ProgressSpinner />
      </div>

      <DataTable
        v-else
        :value="filteredObjects"
        v-model:selection="selectedRow"
        selectionMode="single"
        dataKey="dn"
        stripedRows
        size="small"
        scrollable
        scrollHeight="flex"
        @row-dblclick="onRowDoubleClick"
        :paginator="filteredObjects.length > 50"
        :rows="50"
        :rowsPerPageOptions="[25, 50, 100]"
        class="browse-table"
      >
        <Column header="Name" sortable sortField="name" style="min-width: 250px">
          <template #body="{ data }">
            <div style="display: flex; align-items: center; gap: 0.5rem">
              <i :class="objectClassIcon(data.objectClass)" style="color: var(--p-text-muted-color); font-size: 0.875rem"></i>
              <span>{{ data.name || data.dn }}</span>
            </div>
          </template>
        </Column>
        <Column field="objectClass" header="Type" sortable style="width: 160px" />
        <Column field="description" header="Description" style="width: 240px">
          <template #body="{ data }">
            <span style="color: var(--p-text-muted-color)">{{ data.description || '' }}</span>
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
            <p style="margin: 0; font-size: 0.875rem">No objects found in this container. Use the toolbar to create new users or groups, or select a different container in the tree.</p>
          </div>
        </template>
      </DataTable>

      <!-- Status bar -->
      <div v-if="selectedDn" class="browse-statusbar">
        {{ store.totalCount }} object(s) &mdash; {{ selectedDn }}
      </div>
    </div>

    <!-- Dialogs -->
    <PropertySheet
      v-if="propertySheetVisible"
      :objectGuid="propertySheetGuid"
      :visible="propertySheetVisible"
      @update:visible="propertySheetVisible = $event"
    />
    <CreateUserDialog
      :visible="createUserVisible"
      :containerDn="selectedDn || ''"
      @update:visible="createUserVisible = $event"
      @created="onObjectCreated"
    />
    <CreateGroupDialog
      :visible="createGroupVisible"
      :containerDn="selectedDn || ''"
      @update:visible="createGroupVisible = $event"
      @created="onObjectCreated"
    />
    <DelegateControlDialog
      :visible="delegateControlVisible"
      :containerDn="selectedDn || ''"
      @update:visible="delegateControlVisible = $event"
      @delegated="refreshCurrentNode"
    />
  </div>
</template>

<style scoped>
.browse-layout {
  display: flex;
  height: calc(100vh - 3rem);
  margin: -1.5rem -2rem;
  overflow: hidden;
}

.browse-tree-panel {
  width: 320px;
  min-width: 280px;
  border-right: 1px solid var(--p-surface-border);
  background: var(--p-surface-card);
  display: flex;
  flex-direction: column;
}

.browse-tree-header {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.875rem 1rem;
  font-weight: 600;
  font-size: 0.875rem;
  color: var(--p-text-color);
  border-bottom: 1px solid var(--p-surface-border);
  background: var(--p-surface-ground);
}

.browse-tree-content {
  flex: 1;
  overflow-y: auto;
  padding: 0.5rem;
}

.directory-tree {
  border: none !important;
  background: transparent !important;
  padding: 0 !important;
}

.browse-content-panel {
  flex: 1;
  display: flex;
  flex-direction: column;
  overflow: hidden;
  padding: 1rem 1.5rem;
}

.browse-table {
  flex: 1;
}

.browse-empty-state {
  flex: 1;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 1rem;
  color: var(--p-text-muted-color);
  font-size: 0.9375rem;
}

.browse-statusbar {
  padding: 0.5rem 0;
  font-size: 0.75rem;
  color: var(--p-text-muted-color);
  border-top: 1px solid var(--p-surface-border);
  margin-top: 0.5rem;
}
</style>
