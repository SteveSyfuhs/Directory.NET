<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Tag from 'primevue/tag'
import Dialog from 'primevue/dialog'
import Toolbar from 'primevue/toolbar'
import ProgressSpinner from 'primevue/progressspinner'
import { useToast } from 'primevue/usetoast'
import { listDeletedObjects, restoreObject, purgeObject } from '../api/admin'
import { relativeTime } from '../utils/format'

const toast = useToast()

const items = ref<any[]>([])
const loading = ref(true)
const selectedItems = ref<any[]>([])
const searchQuery = ref('')

// Dialogs
const restoreDialogVisible = ref(false)
const purgeDialogVisible = ref(false)
const emptyBinDialogVisible = ref(false)
const actionTarget = ref<any>(null)
const actionLoading = ref(false)

onMounted(() => loadItems())

async function loadItems() {
  loading.value = true
  try {
    items.value = await listDeletedObjects(1000)
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    loading.value = false
  }
}

const filteredItems = computed(() => {
  if (!searchQuery.value) return items.value
  const q = searchQuery.value.toLowerCase()
  return items.value.filter(
    (item) =>
      item.name?.toLowerCase().includes(q) ||
      item.objectClass?.toLowerCase().includes(q) ||
      item.lastKnownParent?.toLowerCase().includes(q) ||
      item.distinguishedName?.toLowerCase().includes(q)
  )
})

function classIcon(cls: string) {
  switch (cls) {
    case 'user': return 'pi-user'
    case 'computer': return 'pi-desktop'
    case 'group': return 'pi-users'
    case 'organizationalUnit': return 'pi-folder'
    default: return 'pi-box'
  }
}

// Single restore
function confirmRestore(item: any) {
  actionTarget.value = item
  restoreDialogVisible.value = true
}

async function executeRestore() {
  if (!actionTarget.value) return
  actionLoading.value = true
  try {
    await restoreObject(actionTarget.value.objectGuid)
    toast.add({ severity: 'success', summary: 'Restored', detail: `${actionTarget.value.name} has been restored`, life: 3000 })
    restoreDialogVisible.value = false
    actionTarget.value = null
    selectedItems.value = []
    await loadItems()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Restore Failed', detail: e.message, life: 5000 })
  } finally {
    actionLoading.value = false
  }
}

// Single purge
function confirmPurge(item: any) {
  actionTarget.value = item
  purgeDialogVisible.value = true
}

async function executePurge() {
  if (!actionTarget.value) return
  actionLoading.value = true
  try {
    await purgeObject(actionTarget.value.objectGuid)
    toast.add({ severity: 'success', summary: 'Purged', detail: `${actionTarget.value.name} permanently deleted`, life: 3000 })
    purgeDialogVisible.value = false
    actionTarget.value = null
    selectedItems.value = []
    await loadItems()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Purge Failed', detail: e.message, life: 5000 })
  } finally {
    actionLoading.value = false
  }
}

// Bulk restore
async function bulkRestore() {
  if (!selectedItems.value.length) return
  actionLoading.value = true
  let restored = 0
  let failed = 0
  for (const item of selectedItems.value) {
    try {
      await restoreObject(item.objectGuid)
      restored++
    } catch {
      failed++
    }
  }
  actionLoading.value = false
  if (restored > 0) {
    toast.add({ severity: 'success', summary: 'Bulk Restore', detail: `${restored} object(s) restored${failed > 0 ? `, ${failed} failed` : ''}`, life: 4000 })
  }
  if (failed > 0 && restored === 0) {
    toast.add({ severity: 'error', summary: 'Bulk Restore Failed', detail: `All ${failed} restore(s) failed`, life: 5000 })
  }
  selectedItems.value = []
  await loadItems()
}

// Empty recycle bin
function confirmEmptyBin() {
  emptyBinDialogVisible.value = true
}

async function executeEmptyBin() {
  actionLoading.value = true
  let purged = 0
  let failed = 0
  for (const item of items.value) {
    try {
      await purgeObject(item.objectGuid)
      purged++
    } catch {
      failed++
    }
  }
  actionLoading.value = false
  emptyBinDialogVisible.value = false
  if (purged > 0) {
    toast.add({ severity: 'success', summary: 'Recycle Bin Emptied', detail: `${purged} object(s) permanently deleted${failed > 0 ? `, ${failed} failed` : ''}`, life: 4000 })
  }
  selectedItems.value = []
  await loadItems()
}

const restorableSelected = computed(() =>
  selectedItems.value.filter((i) => !i.isRecycled)
)
</script>

<template>
  <div>
    <div class="page-header">
      <h1>Recycle Bin</h1>
      <p>Recover or permanently remove deleted objects</p>
    </div>

    <Toolbar style="margin-bottom: 1rem; border: 1px solid var(--p-surface-border); border-radius: 0.75rem; background: var(--p-surface-card);">
      <template #start>
        <Button label="Restore Selected" icon="pi pi-undo" size="small" severity="success"
                @click="bulkRestore" :disabled="!restorableSelected.length" :loading="actionLoading"
                style="margin-right: 0.5rem" />
        <Button label="Empty Recycle Bin" icon="pi pi-trash" size="small" severity="danger" outlined
                @click="confirmEmptyBin" :disabled="!items.length" />
      </template>
      <template #end>
        <span class="p-input-icon-left" style="margin-right: 0.5rem">
          <i class="pi pi-search" />
          <InputText v-model="searchQuery" placeholder="Search deleted objects..." size="small"
                     style="width: 260px" />
        </span>
        <Button icon="pi pi-refresh" size="small" severity="secondary" outlined
                @click="loadItems" :loading="loading" v-tooltip="'Refresh'" />
      </template>
    </Toolbar>

    <div v-if="loading && !items.length" style="text-align: center; padding: 4rem">
      <ProgressSpinner />
    </div>

    <div v-else class="card" style="padding: 0">
      <DataTable
        :value="filteredItems"
        v-model:selection="selectedItems"
        dataKey="objectGuid"
        stripedRows
        size="small"
        scrollable
        scrollHeight="calc(100vh - 280px)"
        :loading="loading"
        paginator
        :rows="50"
        :rowsPerPageOptions="[25, 50, 100, 200]"
      >
        <Column selectionMode="multiple" headerStyle="width: 3rem" />

        <Column header="Name" sortable sortField="name" style="min-width: 220px">
          <template #body="{ data }">
            <div style="display: flex; align-items: center; gap: 0.5rem">
              <i :class="'pi ' + classIcon(data.objectClass)" style="color: var(--p-text-muted-color)"></i>
              <span>{{ data.name }}</span>
            </div>
          </template>
        </Column>

        <Column field="objectClass" header="Type" sortable style="width: 150px">
          <template #body="{ data }">
            <Tag :value="data.objectClass" severity="secondary" />
          </template>
        </Column>

        <Column field="lastKnownParent" header="Original Location" style="min-width: 300px">
          <template #body="{ data }">
            <span style="color: var(--p-text-muted-color); font-size: 0.85rem">{{ data.lastKnownParent }}</span>
          </template>
        </Column>

        <Column header="Deleted" sortable sortField="deletedTime" style="width: 140px">
          <template #body="{ data }">
            <span style="color: var(--p-text-muted-color)">{{ relativeTime(data.deletedTime) }}</span>
          </template>
        </Column>

        <Column header="State" style="width: 110px">
          <template #body="{ data }">
            <Tag :value="data.isRecycled ? 'Recycled' : 'Deleted'" :severity="data.isRecycled ? 'danger' : 'warn'" />
          </template>
        </Column>

        <Column header="Actions" style="width: 170px" frozen alignFrozen="right">
          <template #body="{ data }">
            <div style="display: flex; gap: 0.25rem">
              <Button icon="pi pi-undo" size="small" severity="success" text rounded
                      @click="confirmRestore(data)" :disabled="data.isRecycled"
                      v-tooltip="data.isRecycled ? 'Recycled objects cannot be restored' : 'Restore'" />
              <Button icon="pi pi-trash" size="small" severity="danger" text rounded
                      @click="confirmPurge(data)" v-tooltip="'Permanently delete'" />
            </div>
          </template>
        </Column>

        <template #empty>
          <div style="text-align: center; padding: 3rem; color: var(--p-text-muted-color)">
            <i class="pi pi-trash" style="font-size: 2.5rem; margin-bottom: 1rem; display: block; opacity: 0.4"></i>
            <p style="font-size: 1.1rem; font-weight: 600; margin-bottom: 0.25rem; color: var(--p-text-color)">Recycle Bin is Empty</p>
            <p>Deleted objects from the directory will appear here and can be restored within the tombstone lifetime.</p>
          </div>
        </template>
      </DataTable>
    </div>

    <!-- Restore Confirmation Dialog -->
    <Dialog v-model:visible="restoreDialogVisible" header="Restore Object" :modal="true" :style="{ width: '28rem' }">
      <div style="display: flex; align-items: center; gap: 0.75rem">
        <i class="pi pi-undo" style="font-size: 1.5rem; color: var(--app-success-text)"></i>
        <span>
          Restore <strong>{{ actionTarget?.name }}</strong> to its original location?
        </span>
      </div>
      <div v-if="actionTarget?.lastKnownParent" style="margin-top: 0.75rem; font-size: 0.85rem; color: var(--p-text-muted-color)">
        Target: {{ actionTarget.lastKnownParent }}
      </div>
      <template #footer>
        <Button label="Cancel" severity="secondary" text @click="restoreDialogVisible = false" />
        <Button label="Restore" icon="pi pi-undo" severity="success" @click="executeRestore" :loading="actionLoading" />
      </template>
    </Dialog>

    <!-- Purge Confirmation Dialog -->
    <Dialog v-model:visible="purgeDialogVisible" header="Permanently Delete" :modal="true" :style="{ width: '30rem' }">
      <div style="display: flex; align-items: center; gap: 0.75rem">
        <i class="pi pi-exclamation-triangle" style="font-size: 1.5rem; color: var(--app-danger-text)"></i>
        <span>
          Permanently delete <strong>{{ actionTarget?.name }}</strong>?
        </span>
      </div>
      <div style="margin-top: 0.75rem; padding: 0.75rem; background: var(--app-danger-bg); border: 1px solid var(--app-danger-border); border-radius: 0.5rem; font-size: 0.85rem; color: var(--app-danger-text)">
        This cannot be undone. The object and all its attributes will be permanently removed.
      </div>
      <template #footer>
        <Button label="Cancel" severity="secondary" text @click="purgeDialogVisible = false" />
        <Button label="Permanently Delete" icon="pi pi-trash" severity="danger" @click="executePurge" :loading="actionLoading" />
      </template>
    </Dialog>

    <!-- Empty Recycle Bin Confirmation Dialog -->
    <Dialog v-model:visible="emptyBinDialogVisible" header="Empty Recycle Bin" :modal="true" :style="{ width: '32rem' }">
      <div style="display: flex; align-items: center; gap: 0.75rem">
        <i class="pi pi-exclamation-triangle" style="font-size: 1.5rem; color: var(--app-danger-text)"></i>
        <span>
          Permanently delete <strong>all {{ items.length }}</strong> object(s) in the recycle bin?
        </span>
      </div>
      <div style="margin-top: 0.75rem; padding: 0.75rem; background: var(--app-danger-bg); border: 1px solid var(--app-danger-border); border-radius: 0.5rem; font-size: 0.85rem; color: var(--app-danger-text)">
        This cannot be undone. All deleted objects will be permanently removed and cannot be recovered.
      </div>
      <template #footer>
        <Button label="Cancel" severity="secondary" text @click="emptyBinDialogVisible = false" />
        <Button label="Empty Recycle Bin" icon="pi pi-trash" severity="danger" @click="executeEmptyBin" :loading="actionLoading" />
      </template>
    </Dialog>
  </div>
</template>
