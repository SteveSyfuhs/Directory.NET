<script setup lang="ts">
import { ref, onMounted } from 'vue'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import Textarea from 'primevue/textarea'
import ProgressSpinner from 'primevue/progressspinner'
import { useToast } from 'primevue/usetoast'
import { listOUs, createOU, deleteOU } from '../api/admin'

const toast = useToast()
const ous = ref<any[]>([])
const loading = ref(true)
const selectedOu = ref<any>(null)
const createVisible = ref(false)
const newOuName = ref('')
const newOuDescription = ref('')
const newOuParent = ref('')

onMounted(() => loadOUs())

async function loadOUs() {
  loading.value = true
  try {
    ous.value = await listOUs()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    loading.value = false
  }
}

async function onCreate() {
  if (!newOuName.value || !newOuParent.value) return
  try {
    await createOU(newOuName.value, newOuParent.value, newOuDescription.value || undefined)
    toast.add({ severity: 'success', summary: 'Created', detail: `OU ${newOuName.value} created`, life: 3000 })
    createVisible.value = false
    newOuName.value = ''
    newOuDescription.value = ''
    await loadOUs()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

async function onDelete() {
  if (!selectedOu.value?.objectGuid) return
  if (!confirm(`Delete OU ${selectedOu.value.name}? This will also delete all child objects.`)) return
  try {
    await deleteOU(selectedOu.value.objectGuid, true)
    toast.add({ severity: 'success', summary: 'Deleted', detail: 'OU deleted', life: 3000 })
    selectedOu.value = null
    await loadOUs()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}
</script>

<template>
  <div>
    <div class="page-header">
      <h1>Organizational Units</h1>
      <p>Manage organizational units for delegated administration</p>
    </div>

    <div class="toolbar">
      <Button v-permission="'ous:manage'" label="Create OU" icon="pi pi-folder-plus" size="small" @click="createVisible = true" />
      <Button v-permission="'ous:manage'" label="Delete" icon="pi pi-trash" size="small" severity="danger" outlined
              @click="onDelete" :disabled="!selectedOu" />
      <Button label="Refresh" icon="pi pi-refresh" size="small" severity="secondary" outlined
              @click="loadOUs" />
    </div>

    <div v-if="loading" style="text-align: center; padding: 4rem">
      <ProgressSpinner />
    </div>

    <div v-else class="card" style="padding: 0">
      <DataTable :value="ous" v-model:selection="selectedOu" selectionMode="single"
                 dataKey="objectGuid" stripedRows size="small" scrollable scrollHeight="calc(100vh - 260px)">
        <Column header="Name" sortable sortField="name" style="min-width: 250px">
          <template #body="{ data }">
            <div style="display: flex; align-items: center; gap: 0.5rem">
              <i class="pi pi-folder" style="color: var(--app-warn-text)"></i>
              <span>{{ data.name }}</span>
            </div>
          </template>
        </Column>
        <Column field="description" header="Description" style="min-width: 200px" />
        <Column field="distinguishedName" header="Distinguished Name" style="min-width: 350px">
          <template #body="{ data }">
            <span style="color: var(--p-text-muted-color); font-size: 0.85rem">{{ data.distinguishedName }}</span>
          </template>
        </Column>
        <template #empty>
          <div style="text-align: center; padding: 2rem; color: var(--p-text-muted-color)">No OUs found</div>
        </template>
      </DataTable>
    </div>

    <Dialog v-model:visible="createVisible" header="Create Organizational Unit" :style="{ width: '500px' }" modal>
      <div style="display: flex; flex-direction: column; gap: 1rem; padding: 1rem 0">
        <div>
          <label style="display: block; margin-bottom: 0.25rem; font-weight: 500">Name</label>
          <InputText v-model="newOuName" style="width: 100%" placeholder="e.g. Engineering" />
        </div>
        <div>
          <label style="display: block; margin-bottom: 0.25rem; font-weight: 500">Parent DN</label>
          <InputText v-model="newOuParent" style="width: 100%" placeholder="e.g. DC=contoso,DC=com" />
        </div>
        <div>
          <label style="display: block; margin-bottom: 0.25rem; font-weight: 500">Description</label>
          <Textarea v-model="newOuDescription" rows="2" style="width: 100%" />
        </div>
      </div>
      <template #footer>
        <Button label="Cancel" severity="secondary" text @click="createVisible = false" />
        <Button label="Create" icon="pi pi-check" @click="onCreate" :disabled="!newOuName || !newOuParent" />
      </template>
    </Dialog>
  </div>
</template>
