<script setup lang="ts">
import { ref, onMounted } from 'vue'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import Tag from 'primevue/tag'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import InputNumber from 'primevue/inputnumber'
import Textarea from 'primevue/textarea'
import Select from 'primevue/select'
import Checkbox from 'primevue/checkbox'
import ProgressSpinner from 'primevue/progressspinner'
import { useToast } from 'primevue/usetoast'
import { useConfirm } from 'primevue/useconfirm'
import {
  fetchRetentionPolicies,
  createRetentionPolicy,
  updateRetentionPolicy,
  deleteRetentionPolicy,
  runRetentionPolicy,
  previewRetentionPolicy,
} from '../api/dataRetention'
import type { RetentionPolicy, RetentionPreview, RetentionRunResult } from '../api/dataRetention'

const toast = useToast()

const loading = ref(true)
const policies = ref<RetentionPolicy[]>([])
const showDialog = ref(false)
const showPreviewDialog = ref(false)
const editing = ref(false)
const preview = ref<RetentionPreview | null>(null)
const loadingPreview = ref(false)
const runningPolicyId = ref<string | null>(null)

const form = ref({
  id: '',
  name: '',
  description: '',
  target: 'AuditLogs',
  retentionDays: 90,
  action: 'Delete',
  isEnabled: true,
})

const targetOptions = [
  { label: 'Audit Logs', value: 'AuditLogs' },
  { label: 'Recycle Bin Items', value: 'RecycleBinItems' },
  { label: 'LDAP Audit Entries', value: 'LdapAuditEntries' },
  { label: 'Webhook Delivery Logs', value: 'WebhookDeliveryLogs' },
  { label: 'Scheduled Task History', value: 'ScheduledTaskHistory' },
  { label: 'Password Reset Tokens', value: 'PasswordResetTokens' },
  { label: 'Expired Certificates', value: 'ExpiredCertificates' },
  { label: 'Stale Computers', value: 'StaleComputers' },
]

const actionOptions = [
  { label: 'Delete', value: 'Delete' },
  { label: 'Archive', value: 'Archive' },
  { label: 'Disable', value: 'Disable' },
  { label: 'Report Only', value: 'Report' },
]

onMounted(async () => {
  await loadPolicies()
})

async function loadPolicies() {
  loading.value = true
  try {
    policies.value = await fetchRetentionPolicies()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    loading.value = false
  }
}

function openCreate() {
  editing.value = false
  form.value = { id: '', name: '', description: '', target: 'AuditLogs', retentionDays: 90, action: 'Delete', isEnabled: true }
  showDialog.value = true
}

function openEdit(policy: RetentionPolicy) {
  editing.value = true
  form.value = {
    id: policy.id,
    name: policy.name,
    description: policy.description,
    target: policy.target,
    retentionDays: policy.retentionDays,
    action: policy.action,
    isEnabled: policy.isEnabled,
  }
  showDialog.value = true
}

async function handleSave() {
  try {
    if (editing.value) {
      await updateRetentionPolicy(form.value.id, form.value as any)
      toast.add({ severity: 'success', summary: 'Updated', detail: 'Policy updated', life: 3000 })
    } else {
      await createRetentionPolicy(form.value as any)
      toast.add({ severity: 'success', summary: 'Created', detail: 'Policy created', life: 3000 })
    }
    showDialog.value = false
    await loadPolicies()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

async function handleDelete(policy: RetentionPolicy) {
  if (!confirm(`Delete policy "${policy.name}"?`)) return
  try {
    await deleteRetentionPolicy(policy.id)
    toast.add({ severity: 'success', summary: 'Deleted', detail: 'Policy deleted', life: 3000 })
    await loadPolicies()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

async function handlePreview(policy: RetentionPolicy) {
  loadingPreview.value = true
  showPreviewDialog.value = true
  preview.value = null
  try {
    preview.value = await previewRetentionPolicy(policy.id)
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
    showPreviewDialog.value = false
  } finally {
    loadingPreview.value = false
  }
}

async function handleRun(policy: RetentionPolicy) {
  if (!confirm(`Run retention policy "${policy.name}"? This will ${policy.action.toLowerCase()} matching items older than ${policy.retentionDays} days.`)) return
  runningPolicyId.value = policy.id
  try {
    const result = await runRetentionPolicy(policy.id)
    toast.add({
      severity: result.errorCount > 0 ? 'warn' : 'success',
      summary: 'Policy Applied',
      detail: `Processed: ${result.processedCount}, Purged: ${result.purgedCount}, Errors: ${result.errorCount}`,
      life: 5000,
    })
    await loadPolicies()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    runningPolicyId.value = null
  }
}

function actionSeverity(action: string): string {
  switch (action) {
    case 'Delete': return 'danger'
    case 'Archive': return 'info'
    case 'Disable': return 'warn'
    case 'Report': return 'secondary'
    default: return 'info'
  }
}
</script>

<template>
  <div class="data-retention-view">
    <div class="page-header">
      <h1><i class="pi pi-clock"></i> Data Retention Policies</h1>
      <Button label="Create Policy" icon="pi pi-plus" @click="openCreate" />
    </div>

    <ProgressSpinner v-if="loading" strokeWidth="3" />

    <template v-else>
      <DataTable :value="policies" stripedRows responsiveLayout="scroll" :rowHover="true">
        <Column field="name" header="Name" sortable />
        <Column field="target" header="Target" sortable>
          <template #body="{ data }">
            <Tag :value="data.target" severity="info" />
          </template>
        </Column>
        <Column field="retentionDays" header="Retention (Days)" sortable />
        <Column field="action" header="Action" sortable>
          <template #body="{ data }">
            <Tag :value="data.action" :severity="actionSeverity(data.action)" />
          </template>
        </Column>
        <Column field="isEnabled" header="Enabled" sortable>
          <template #body="{ data }">
            <i :class="data.isEnabled ? 'pi pi-check-circle text-green' : 'pi pi-times-circle text-red'"></i>
          </template>
        </Column>
        <Column field="lastAppliedAt" header="Last Applied" sortable>
          <template #body="{ data }">
            {{ data.lastAppliedAt ? new Date(data.lastAppliedAt).toLocaleString() : '--' }}
          </template>
        </Column>
        <Column field="lastPurgedCount" header="Last Purged" sortable>
          <template #body="{ data }">
            {{ data.lastPurgedCount ?? '--' }}
          </template>
        </Column>
        <Column header="Actions" :style="{ width: '260px' }">
          <template #body="{ data }">
            <div class="action-buttons">
              <Button icon="pi pi-eye" size="small" severity="info" v-tooltip="'Preview'" @click="handlePreview(data)" />
              <Button
                icon="pi pi-play"
                size="small"
                severity="success"
                v-tooltip="'Run Now'"
                :loading="runningPolicyId === data.id"
                @click="handleRun(data)"
              />
              <Button icon="pi pi-pencil" size="small" severity="warn" v-tooltip="'Edit'" @click="openEdit(data)" />
              <Button icon="pi pi-trash" size="small" severity="danger" v-tooltip="'Delete'" @click="handleDelete(data)" />
            </div>
          </template>
        </Column>
      </DataTable>
    </template>

    <!-- Create/Edit Dialog -->
    <Dialog
      v-model:visible="showDialog"
      :header="editing ? 'Edit Retention Policy' : 'Create Retention Policy'"
      :modal="true"
      :style="{ width: '500px' }"
    >
      <div class="form-grid">
        <div class="form-field">
          <label>Name</label>
          <InputText v-model="form.name" class="w-full" />
        </div>
        <div class="form-field">
          <label>Description</label>
          <Textarea v-model="form.description" rows="2" class="w-full" />
        </div>
        <div class="form-field">
          <label>Target</label>
          <Select v-model="form.target" :options="targetOptions" optionLabel="label" optionValue="value" class="w-full" />
        </div>
        <div class="form-row">
          <div class="form-field">
            <label>Retention (Days)</label>
            <InputNumber v-model="form.retentionDays" :min="1" :max="3650" class="w-full" />
          </div>
          <div class="form-field">
            <label>Action</label>
            <Select v-model="form.action" :options="actionOptions" optionLabel="label" optionValue="value" class="w-full" />
          </div>
        </div>
        <div class="form-field checkbox-field">
          <Checkbox v-model="form.isEnabled" :binary="true" inputId="policyEnabled" />
          <label for="policyEnabled">Enabled</label>
        </div>
      </div>
      <template #footer>
        <Button label="Cancel" severity="secondary" @click="showDialog = false" />
        <Button :label="editing ? 'Update' : 'Create'" icon="pi pi-check" :disabled="!form.name" @click="handleSave" />
      </template>
    </Dialog>

    <!-- Preview Dialog -->
    <Dialog
      v-model:visible="showPreviewDialog"
      header="Retention Policy Preview"
      :modal="true"
      :style="{ width: '600px' }"
    >
      <ProgressSpinner v-if="loadingPreview" strokeWidth="3" />
      <template v-else-if="preview">
        <div class="preview-info">
          <p><strong>Policy:</strong> {{ preview.policyName }}</p>
          <p><strong>Target:</strong> <Tag :value="preview.target" severity="info" /></p>
          <p><strong>Affected Items:</strong> <span class="affected-count">{{ preview.affectedCount }}</span></p>
        </div>
        <div v-if="preview.sampleItems.length > 0" class="preview-samples">
          <h4>Sample Items</h4>
          <ul>
            <li v-for="(item, idx) in preview.sampleItems" :key="idx">{{ item }}</li>
          </ul>
        </div>
      </template>
    </Dialog>
  </div>
</template>

<style scoped>
.data-retention-view {
  padding: 1.5rem;
}
.page-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 1.5rem;
}
.page-header h1 {
  margin: 0;
  font-size: 1.5rem;
}
.action-buttons {
  display: flex;
  gap: 0.5rem;
}
.form-grid {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}
.form-row {
  display: flex;
  gap: 1rem;
}
.form-field {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  flex: 1;
}
.form-field label {
  font-weight: 600;
  font-size: 0.85rem;
}
.checkbox-field {
  flex-direction: row;
  align-items: center;
  gap: 0.5rem;
}
.preview-info p {
  margin: 0.5rem 0;
}
.affected-count {
  font-size: 1.25rem;
  font-weight: 700;
  color: var(--p-orange-500);
}
.preview-samples {
  margin-top: 1rem;
}
.preview-samples h4 {
  margin: 0 0 0.5rem 0;
}
.preview-samples ul {
  margin: 0;
  padding-left: 1.5rem;
}
.preview-samples li {
  font-size: 0.85rem;
  margin-bottom: 0.25rem;
  word-break: break-all;
}
.text-green {
  color: var(--p-green-500);
}
.text-red {
  color: var(--p-red-500);
}
</style>
