<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import Textarea from 'primevue/textarea'
import Select from 'primevue/select'
import InputSwitch from 'primevue/inputswitch'
import Tag from 'primevue/tag'
import { useToast } from 'primevue/usetoast'
import { useConfirm } from 'primevue/useconfirm'
import PageHeader from '../components/PageHeader.vue'
import type { ScheduledTask, ScheduledTaskType, TaskExecutionRecord } from '../types/scheduledTasks'
import {
  fetchScheduledTasks,
  createScheduledTask,
  updateScheduledTask,
  deleteScheduledTask,
  runScheduledTaskNow,
  fetchTaskHistory,
} from '../api/scheduledTasks'

const toast = useToast()
const confirm = useConfirm()
const loading = ref(false)
const tasks = ref<ScheduledTask[]>([])

const showEditDialog = ref(false)
const editingTask = ref<Partial<ScheduledTask>>({})
const isNew = ref(false)
const saving = ref(false)

const showHistoryDialog = ref(false)
const historyTask = ref<ScheduledTask | null>(null)
const historyRecords = ref<TaskExecutionRecord[]>([])
const historyLoading = ref(false)

const taskTypeOptions = [
  { label: 'DNS Scavenging', value: 'DnsScavenging' },
  { label: 'Backup Export', value: 'BackupExport' },
  { label: 'Password Expiry Report', value: 'PasswordExpiryReport' },
  { label: 'Stale Account Cleanup', value: 'StaleAccountCleanup' },
  { label: 'Group Membership Report', value: 'GroupMembershipReport' },
  { label: 'Recycle Bin Purge', value: 'RecycleBinPurge' },
  { label: 'Certificate Expiry Check', value: 'CertificateExpiryCheck' },
  { label: 'Custom', value: 'Custom' },
]

const cronPresets = [
  { label: 'Every day at 2 AM', value: '0 2 * * *' },
  { label: 'Every day at midnight', value: '0 0 * * *' },
  { label: 'Every Monday at 7 AM', value: '0 7 * * 1' },
  { label: 'Every Sunday at 4 AM', value: '0 4 * * 0' },
  { label: 'Every hour', value: '0 * * * *' },
  { label: 'Every 6 hours', value: '0 */6 * * *' },
  { label: 'First of month at 3 AM', value: '0 3 1 * *' },
]

onMounted(() => loadTasks())

async function loadTasks() {
  loading.value = true
  try {
    tasks.value = await fetchScheduledTasks()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    loading.value = false
  }
}

function openCreate() {
  isNew.value = true
  editingTask.value = {
    name: '',
    description: '',
    taskType: 'Custom' as ScheduledTaskType,
    cronExpression: '0 2 * * *',
    isEnabled: false,
    parameters: {},
  }
  showEditDialog.value = true
}

function openEdit(task: ScheduledTask) {
  isNew.value = false
  editingTask.value = { ...task, parameters: { ...task.parameters } }
  showEditDialog.value = true
}

async function saveTask() {
  saving.value = true
  try {
    if (isNew.value) {
      await createScheduledTask(editingTask.value)
      toast.add({ severity: 'success', summary: 'Created', detail: 'Scheduled task created.', life: 3000 })
    } else {
      await updateScheduledTask(editingTask.value.id!, editingTask.value)
      toast.add({ severity: 'success', summary: 'Updated', detail: 'Scheduled task updated.', life: 3000 })
    }
    showEditDialog.value = false
    await loadTasks()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    saving.value = false
  }
}

async function confirmDelete(task: ScheduledTask) {
  confirm.require({
    message: `Are you sure you want to delete the task "${task.name}"?`,
    header: 'Delete Task',
    icon: 'pi pi-exclamation-triangle',
    rejectLabel: 'Cancel',
    acceptLabel: 'Delete',
    acceptProps: { severity: 'danger' },
    accept: async () => {
      try {
        await deleteScheduledTask(task.id)
        toast.add({ severity: 'success', summary: 'Deleted', detail: 'Task deleted.', life: 3000 })
        await loadTasks()
      } catch (e: any) {
        toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
      }
    },
  })
}

async function runNow(task: ScheduledTask) {
  try {
    const record = await runScheduledTaskNow(task.id)
    toast.add({
      severity: record.status === 'Success' ? 'success' : 'warn',
      summary: 'Task Executed',
      detail: record.message || record.status,
      life: 5000,
    })
    await loadTasks()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

async function toggleEnabled(task: ScheduledTask) {
  try {
    await updateScheduledTask(task.id, { ...task, isEnabled: !task.isEnabled })
    await loadTasks()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

async function openHistory(task: ScheduledTask) {
  historyTask.value = task
  historyLoading.value = true
  showHistoryDialog.value = true
  try {
    historyRecords.value = await fetchTaskHistory(task.id)
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    historyLoading.value = false
  }
}

function statusSeverity(status: string): "success" | "danger" | "info" | "secondary" | "warn" | undefined {
  switch (status) {
    case 'Success': return 'success'
    case 'Failed': return 'danger'
    case 'Running': return 'info'
    default: return 'secondary'
  }
}

function formatDate(d: string | null) {
  if (!d) return '-'
  return new Date(d).toLocaleString()
}

function taskTypeLabel(val: string) {
  return taskTypeOptions.find(o => o.value === val)?.label ?? val
}

// Parameters helper for the edit dialog
const parameterKeys = computed(() => {
  const t = editingTask.value.taskType
  switch (t) {
    case 'DnsScavenging': return [{ key: 'maxAgeDays', label: 'Max Age (days)', default: '14' }]
    case 'BackupExport': return [{ key: 'format', label: 'Format (json/ldif)', default: 'json' }]
    case 'PasswordExpiryReport': return [{ key: 'daysAhead', label: 'Days Ahead', default: '14' }]
    case 'StaleAccountCleanup': return [{ key: 'inactiveDays', label: 'Inactive Days', default: '90' }]
    case 'RecycleBinPurge': return [{ key: 'maxAgeDays', label: 'Max Age (days)', default: '180' }]
    case 'CertificateExpiryCheck': return [{ key: 'daysAhead', label: 'Days Ahead', default: '30' }]
    default: return []
  }
})
</script>

<template>
  <div>
    <PageHeader title="Scheduled Tasks" subtitle="Configure and manage recurring maintenance tasks." />

    <div class="card">
      <div class="toolbar">
        <Button label="Refresh" icon="pi pi-refresh" severity="secondary" @click="loadTasks" :loading="loading" />
        <span class="toolbar-spacer" />
        <Button label="New Task" icon="pi pi-plus" @click="openCreate" />
      </div>

      <DataTable :value="tasks" :loading="loading" stripedRows>
        <Column field="name" header="Name" sortable>
          <template #body="{ data }">
            <strong>{{ data.name }}</strong>
            <div style="font-size: 0.8125rem; color: var(--p-text-muted-color)">{{ data.description }}</div>
          </template>
        </Column>
        <Column field="taskType" header="Type" sortable>
          <template #body="{ data }">{{ taskTypeLabel(data.taskType) }}</template>
        </Column>
        <Column field="cronExpression" header="Schedule">
          <template #body="{ data }">
            <code style="font-size: 0.8125rem">{{ data.cronExpression }}</code>
          </template>
        </Column>
        <Column field="isEnabled" header="Enabled" style="width: 6rem">
          <template #body="{ data }">
            <InputSwitch :modelValue="data.isEnabled" @update:modelValue="toggleEnabled(data)" />
          </template>
        </Column>
        <Column field="lastRunAt" header="Last Run" sortable>
          <template #body="{ data }">{{ formatDate(data.lastRunAt) }}</template>
        </Column>
        <Column field="lastRunStatus" header="Status" style="width: 7rem">
          <template #body="{ data }">
            <Tag v-if="data.lastRunStatus" :value="data.lastRunStatus" :severity="statusSeverity(data.lastRunStatus)" />
            <span v-else style="color: var(--p-text-muted-color)">-</span>
          </template>
        </Column>
        <Column field="nextRunAt" header="Next Run" sortable>
          <template #body="{ data }">{{ formatDate(data.nextRunAt) }}</template>
        </Column>
        <Column header="Actions" style="width: 14rem">
          <template #body="{ data }">
            <Button icon="pi pi-play" severity="success" text rounded v-tooltip="'Run Now'" @click="runNow(data)" />
            <Button icon="pi pi-history" severity="info" text rounded v-tooltip="'History'" @click="openHistory(data)" />
            <Button icon="pi pi-pencil" text rounded v-tooltip="'Edit'" @click="openEdit(data)" />
            <Button icon="pi pi-trash" severity="danger" text rounded v-tooltip="'Delete'" @click="confirmDelete(data)" />
          </template>
        </Column>
      </DataTable>
    </div>

    <!-- Create/Edit Dialog -->
    <Dialog v-model:visible="showEditDialog" :header="isNew ? 'New Scheduled Task' : 'Edit Scheduled Task'" modal style="width: 36rem">
      <div style="display: flex; flex-direction: column; gap: 1rem">
        <div>
          <label style="font-weight: 600; font-size: 0.875rem; display: block; margin-bottom: 0.25rem">Name</label>
          <InputText v-model="editingTask.name" style="width: 100%" />
        </div>
        <div>
          <label style="font-weight: 600; font-size: 0.875rem; display: block; margin-bottom: 0.25rem">Description</label>
          <Textarea v-model="editingTask.description" rows="2" style="width: 100%" />
        </div>
        <div>
          <label style="font-weight: 600; font-size: 0.875rem; display: block; margin-bottom: 0.25rem">Task Type</label>
          <Select v-model="editingTask.taskType" :options="taskTypeOptions" optionLabel="label" optionValue="value" style="width: 100%" />
        </div>
        <div>
          <label style="font-weight: 600; font-size: 0.875rem; display: block; margin-bottom: 0.25rem">Cron Expression</label>
          <InputText v-model="editingTask.cronExpression" style="width: 100%" />
          <div style="margin-top: 0.25rem; display: flex; flex-wrap: wrap; gap: 0.25rem">
            <Tag
              v-for="p in cronPresets"
              :key="p.value"
              :value="p.label"
              severity="secondary"
              style="cursor: pointer; font-size: 0.75rem"
              @click="editingTask.cronExpression = p.value"
            />
          </div>
          <div style="font-size: 0.75rem; color: var(--p-text-muted-color); margin-top: 0.25rem">
            Format: minute hour day-of-month month day-of-week (e.g., 0 2 * * * = daily at 2 AM UTC)
          </div>
        </div>

        <!-- Dynamic parameters based on task type -->
        <div v-for="param in parameterKeys" :key="param.key">
          <label style="font-weight: 600; font-size: 0.875rem; display: block; margin-bottom: 0.25rem">{{ param.label }}</label>
          <InputText
            :modelValue="editingTask.parameters?.[param.key] ?? param.default"
            @update:modelValue="(v: string | undefined) => { if (!editingTask.parameters) editingTask.parameters = {}; editingTask.parameters[param.key] = v ?? '' }"
            style="width: 100%"
          />
        </div>

        <div style="display: flex; align-items: center; gap: 0.5rem">
          <InputSwitch v-model="editingTask.isEnabled" />
          <label style="font-size: 0.875rem">Enabled</label>
        </div>
      </div>

      <template #footer>
        <Button label="Cancel" severity="secondary" @click="showEditDialog = false" />
        <Button :label="isNew ? 'Create' : 'Save'" icon="pi pi-check" @click="saveTask" :loading="saving" />
      </template>
    </Dialog>

    <!-- History Dialog -->
    <Dialog v-model:visible="showHistoryDialog" :header="`Task History: ${historyTask?.name ?? ''}`" modal style="width: 50rem">
      <DataTable :value="historyRecords" :loading="historyLoading" stripedRows>
        <Column field="startedAt" header="Started" sortable>
          <template #body="{ data }">{{ formatDate(data.startedAt) }}</template>
        </Column>
        <Column field="completedAt" header="Completed">
          <template #body="{ data }">{{ formatDate(data.completedAt) }}</template>
        </Column>
        <Column field="status" header="Status" style="width: 7rem">
          <template #body="{ data }">
            <Tag :value="data.status" :severity="statusSeverity(data.status)" />
          </template>
        </Column>
        <Column field="message" header="Message" />
      </DataTable>
    </Dialog>
  </div>
</template>
