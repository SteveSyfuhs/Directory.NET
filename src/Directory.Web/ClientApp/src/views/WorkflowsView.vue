<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import InputSwitch from 'primevue/inputswitch'
import Textarea from 'primevue/textarea'
import Select from 'primevue/select'
import Tag from 'primevue/tag'
import Tabs from 'primevue/tabs'
import TabList from 'primevue/tablist'
import Tab from 'primevue/tab'
import TabPanels from 'primevue/tabpanels'
import TabPanel from 'primevue/tabpanel'
import { useToast } from 'primevue/usetoast'
import type { WorkflowDefinition, WorkflowInstance, WorkflowStep } from '../types/workflows'
import {
  fetchWorkflowDefinitions,
  createWorkflowDefinition,
  updateWorkflowDefinition,
  deleteWorkflowDefinition,
  triggerWorkflow,
  fetchWorkflowInstances,
  approveWorkflowStep,
  rejectWorkflowStep,
  fetchWorkflowTriggerTypes,
  fetchWorkflowStepTypes,
} from '../api/workflows'

const toast = useToast()
const loading = ref(false)
const definitions = ref<WorkflowDefinition[]>([])
const instances = ref<WorkflowInstance[]>([])
const triggerTypes = ref<string[]>([])
const stepTypes = ref<string[]>([])

const activeTab = ref('0')

const showEditDialog = ref(false)
const editing = ref<Partial<WorkflowDefinition>>({})
const isNew = ref(false)
const saving = ref(false)

const showTriggerDialog = ref(false)
const triggerDefId = ref('')
const triggerTargetDn = ref('')

const showInstanceDialog = ref(false)
const selectedInstance = ref<WorkflowInstance | null>(null)

const triggerOptions = computed(() =>
  triggerTypes.value.map((t) => ({ label: t.replace(/([A-Z])/g, ' $1').trim(), value: t }))
)

const stepTypeOptions = computed(() =>
  stepTypes.value.map((t) => ({ label: t.replace(/([A-Z])/g, ' $1').trim(), value: t }))
)

// Parameter definitions per step type
const stepParameterDefs: Record<string, { key: string; label: string; placeholder: string }[]> = {
  RequireApproval: [{ key: 'approver', label: 'Approver (DN or group)', placeholder: 'CN=Admins,DC=...' }],
  SendEmail: [
    { key: 'webhookUrl', label: 'Webhook URL', placeholder: 'https://...' },
    { key: 'to', label: 'To', placeholder: 'admin@example.com' },
    { key: 'subject', label: 'Subject', placeholder: 'Workflow Notification' },
    { key: 'body', label: 'Body', placeholder: 'Message text' },
  ],
  AddToGroup: [{ key: 'groupDn', label: 'Group DN', placeholder: 'CN=GroupName,DC=...' }],
  RemoveFromGroup: [{ key: 'groupDn', label: 'Group DN', placeholder: 'CN=GroupName,DC=...' }],
  SetAttribute: [
    { key: 'attribute', label: 'Attribute Name', placeholder: 'department' },
    { key: 'value', label: 'Value', placeholder: 'Engineering' },
  ],
  MoveToOu: [{ key: 'targetOu', label: 'Target OU', placeholder: 'OU=Disabled,DC=...' }],
  EnableAccount: [],
  DisableAccount: [],
  AssignRole: [{ key: 'role', label: 'Role Name', placeholder: 'HelpDeskAdmin' }],
  TriggerWebhook: [{ key: 'url', label: 'Webhook URL', placeholder: 'https://...' }],
  Wait: [{ key: 'duration', label: 'Duration (minutes)', placeholder: '60' }],
}

onMounted(async () => {
  await Promise.all([loadDefinitions(), loadInstances(), loadMeta()])
})

async function loadDefinitions() {
  loading.value = true
  try {
    definitions.value = await fetchWorkflowDefinitions()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    loading.value = false
  }
}

async function loadInstances() {
  try {
    instances.value = await fetchWorkflowInstances()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

async function loadMeta() {
  try {
    const [tt, st] = await Promise.all([fetchWorkflowTriggerTypes(), fetchWorkflowStepTypes()])
    triggerTypes.value = tt
    stepTypes.value = st
  } catch {}
}

function openCreate() {
  isNew.value = true
  editing.value = {
    name: '',
    description: '',
    trigger: 'Manual',
    steps: [],
    isEnabled: true,
  }
  showEditDialog.value = true
}

function openEdit(def: WorkflowDefinition) {
  isNew.value = false
  editing.value = JSON.parse(JSON.stringify(def))
  showEditDialog.value = true
}

function addStep() {
  if (!editing.value.steps) editing.value.steps = []
  editing.value.steps.push({
    id: crypto.randomUUID(),
    order: editing.value.steps.length,
    name: '',
    type: 'SetAttribute',
    parameters: {},
  })
}

function removeStep(index: number) {
  editing.value.steps?.splice(index, 1)
  editing.value.steps?.forEach((s, i) => (s.order = i))
}

function moveStepUp(index: number) {
  if (index <= 0 || !editing.value.steps) return
  const steps = editing.value.steps
  ;[steps[index - 1], steps[index]] = [steps[index], steps[index - 1]]
  steps.forEach((s, i) => (s.order = i))
}

function moveStepDown(index: number) {
  if (!editing.value.steps || index >= editing.value.steps.length - 1) return
  const steps = editing.value.steps
  ;[steps[index], steps[index + 1]] = [steps[index + 1], steps[index]]
  steps.forEach((s, i) => (s.order = i))
}

async function save() {
  saving.value = true
  try {
    if (isNew.value) {
      await createWorkflowDefinition(editing.value)
      toast.add({ severity: 'success', summary: 'Created', detail: 'Workflow created', life: 3000 })
    } else {
      await updateWorkflowDefinition(editing.value.id!, editing.value)
      toast.add({ severity: 'success', summary: 'Updated', detail: 'Workflow updated', life: 3000 })
    }
    showEditDialog.value = false
    await loadDefinitions()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    saving.value = false
  }
}

async function remove(def: WorkflowDefinition) {
  try {
    await deleteWorkflowDefinition(def.id)
    toast.add({ severity: 'success', summary: 'Deleted', detail: `Workflow "${def.name}" deleted`, life: 3000 })
    await loadDefinitions()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

function openTrigger(def: WorkflowDefinition) {
  triggerDefId.value = def.id
  triggerTargetDn.value = ''
  showTriggerDialog.value = true
}

async function executeTrigger() {
  try {
    await triggerWorkflow(triggerDefId.value, triggerTargetDn.value, 'admin')
    toast.add({ severity: 'success', summary: 'Triggered', detail: 'Workflow instance started', life: 3000 })
    showTriggerDialog.value = false
    await loadInstances()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

function viewInstance(inst: WorkflowInstance) {
  selectedInstance.value = inst
  showInstanceDialog.value = true
}

async function approve(inst: WorkflowInstance) {
  try {
    const updated = await approveWorkflowStep(inst.id, 'admin')
    const idx = instances.value.findIndex((i) => i.id === inst.id)
    if (idx >= 0 && updated) instances.value[idx] = updated
    if (selectedInstance.value?.id === inst.id) selectedInstance.value = updated
    toast.add({ severity: 'success', summary: 'Approved', detail: 'Step approved', life: 3000 })
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

async function reject(inst: WorkflowInstance) {
  try {
    const updated = await rejectWorkflowStep(inst.id, 'admin')
    const idx = instances.value.findIndex((i) => i.id === inst.id)
    if (idx >= 0 && updated) instances.value[idx] = updated
    if (selectedInstance.value?.id === inst.id) selectedInstance.value = updated
    toast.add({ severity: 'warn', summary: 'Rejected', detail: 'Step rejected, workflow cancelled', life: 3000 })
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

function formatDate(date: string | null) {
  if (!date) return '-'
  return new Date(date).toLocaleString()
}

function getStatusSeverity(status: string): "success" | "danger" | "warn" | "info" | "secondary" {
  switch (status) {
    case 'Completed': return 'success'
    case 'Failed': case 'Cancelled': return 'danger'
    case 'AwaitingApproval': return 'warn'
    case 'InProgress': case 'Running': return 'info'
    default: return 'secondary'
  }
}

function getStepStatusSeverity(status: string): "success" | "danger" | "warn" | "info" | "secondary" {
  switch (status) {
    case 'Completed': return 'success'
    case 'Failed': return 'danger'
    case 'AwaitingApproval': return 'warn'
    case 'Running': return 'info'
    case 'Skipped': return 'secondary'
    default: return 'secondary'
  }
}
</script>

<template>
  <div>
    <div class="page-header">
      <h1>User Lifecycle Workflows</h1>
      <p>Automate user provisioning, de-provisioning, and lifecycle management</p>
    </div>

    <Tabs v-model:value="activeTab">
      <TabList>
        <Tab value="0">Workflow Definitions</Tab>
        <Tab value="1">Running Instances</Tab>
      </TabList>
      <TabPanels>
        <!-- Definitions Tab -->
        <TabPanel value="0">
          <div class="toolbar" style="margin-top: 1rem">
            <Button label="New Workflow" icon="pi pi-plus" @click="openCreate" />
            <span class="toolbar-spacer" />
            <Button label="Refresh" icon="pi pi-refresh" text @click="loadDefinitions" :loading="loading" />
          </div>

          <div class="card">
            <DataTable :value="definitions" :loading="loading" stripedRows>
              <Column field="name" header="Name" sortable />
              <Column header="Trigger" sortable>
                <template #body="{ data }">
                  <Tag :value="data.trigger" />
                </template>
              </Column>
              <Column header="Steps">
                <template #body="{ data }">{{ data.steps.length }}</template>
              </Column>
              <Column header="Enabled">
                <template #body="{ data }">
                  <Tag :value="data.isEnabled ? 'Yes' : 'No'" :severity="data.isEnabled ? 'success' : 'secondary'" />
                </template>
              </Column>
              <Column header="Actions" style="width: 240px">
                <template #body="{ data }">
                  <div style="display: flex; gap: 0.25rem">
                    <Button icon="pi pi-play" text size="small" v-tooltip="'Trigger'" @click="openTrigger(data)" />
                    <Button icon="pi pi-pencil" text size="small" v-tooltip="'Edit'" @click="openEdit(data)" />
                    <Button icon="pi pi-trash" text severity="danger" size="small" v-tooltip="'Delete'" @click="remove(data)" />
                  </div>
                </template>
              </Column>
            </DataTable>
          </div>
        </TabPanel>

        <!-- Instances Tab -->
        <TabPanel value="1">
          <div class="toolbar" style="margin-top: 1rem">
            <span class="toolbar-spacer" />
            <Button label="Refresh" icon="pi pi-refresh" text @click="loadInstances" />
          </div>

          <div class="card">
            <DataTable :value="instances" stripedRows paginator :rows="15">
              <Column field="workflowName" header="Workflow" sortable />
              <Column field="targetDn" header="Target" sortable />
              <Column header="Status" sortable>
                <template #body="{ data }">
                  <Tag :value="data.status" :severity="getStatusSeverity(data.status)" />
                </template>
              </Column>
              <Column header="Progress">
                <template #body="{ data }">
                  {{ data.currentStep + 1 }} / {{ data.totalSteps }}
                </template>
              </Column>
              <Column header="Started">
                <template #body="{ data }">{{ formatDate(data.startedAt) }}</template>
              </Column>
              <Column header="Actions" style="width: 200px">
                <template #body="{ data }">
                  <div style="display: flex; gap: 0.25rem">
                    <Button icon="pi pi-eye" text size="small" v-tooltip="'Details'" @click="viewInstance(data)" />
                    <Button
                      v-if="data.status === 'AwaitingApproval'"
                      icon="pi pi-check"
                      text
                      severity="success"
                      size="small"
                      v-tooltip="'Approve'"
                      @click="approve(data)"
                    />
                    <Button
                      v-if="data.status === 'AwaitingApproval'"
                      icon="pi pi-times"
                      text
                      severity="danger"
                      size="small"
                      v-tooltip="'Reject'"
                      @click="reject(data)"
                    />
                  </div>
                </template>
              </Column>
            </DataTable>
          </div>
        </TabPanel>
      </TabPanels>
    </Tabs>

    <!-- Edit/Create Dialog -->
    <Dialog v-model:visible="showEditDialog" :header="isNew ? 'New Workflow' : 'Edit Workflow'" modal style="width: 700px">
      <div style="display: flex; flex-direction: column; gap: 1rem">
        <div>
          <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">Name</label>
          <InputText v-model="editing.name" placeholder="e.g., New Hire Onboarding" style="width: 100%" />
        </div>
        <div>
          <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">Description</label>
          <Textarea v-model="editing.description" rows="2" style="width: 100%" />
        </div>
        <div style="display: flex; gap: 1rem">
          <div style="flex: 1">
            <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">Trigger</label>
            <Select v-model="editing.trigger" :options="triggerOptions" optionLabel="label" optionValue="value" style="width: 100%" />
          </div>
          <div style="display: flex; align-items: flex-end; gap: 0.5rem; padding-bottom: 0.25rem">
            <InputSwitch v-model="editing.isEnabled" />
            <label>Enabled</label>
          </div>
        </div>

        <div>
          <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: 0.5rem">
            <label style="font-weight: 600">Steps</label>
            <Button label="Add Step" icon="pi pi-plus" text size="small" @click="addStep" />
          </div>

          <div v-for="(step, idx) in editing.steps" :key="step.id" class="card" style="margin-bottom: 0.75rem; padding: 1rem">
            <div style="display: flex; align-items: center; gap: 0.5rem; margin-bottom: 0.75rem">
              <Tag :value="`#${idx + 1}`" />
              <InputText v-model="step.name" placeholder="Step name (optional)" style="flex: 1" />
              <Select v-model="step.type" :options="stepTypeOptions" optionLabel="label" optionValue="value" style="width: 200px" />
              <Button icon="pi pi-arrow-up" text size="small" :disabled="idx === 0" @click="moveStepUp(idx)" />
              <Button icon="pi pi-arrow-down" text size="small" :disabled="idx === (editing.steps?.length ?? 0) - 1" @click="moveStepDown(idx)" />
              <Button icon="pi pi-trash" text severity="danger" size="small" @click="removeStep(idx)" />
            </div>
            <div v-if="stepParameterDefs[step.type]?.length" style="display: flex; flex-direction: column; gap: 0.5rem; padding-left: 2.5rem">
              <div v-for="param in stepParameterDefs[step.type]" :key="param.key" style="display: flex; align-items: center; gap: 0.5rem">
                <label style="width: 140px; font-size: 0.875rem">{{ param.label }}</label>
                <InputText
                  :modelValue="step.parameters[param.key] || ''"
                  @update:modelValue="step.parameters[param.key] = $event"
                  :placeholder="param.placeholder"
                  style="flex: 1"
                />
              </div>
            </div>
          </div>
          <div v-if="!editing.steps?.length" style="color: var(--p-text-muted-color); text-align: center; padding: 1rem">
            No steps defined. Click "Add Step" to begin.
          </div>
        </div>
      </div>
      <template #footer>
        <Button label="Cancel" text @click="showEditDialog = false" />
        <Button :label="isNew ? 'Create' : 'Save'" @click="save" :loading="saving" />
      </template>
    </Dialog>

    <!-- Trigger Dialog -->
    <Dialog v-model:visible="showTriggerDialog" header="Trigger Workflow" modal style="width: 450px">
      <div style="display: flex; flex-direction: column; gap: 1rem">
        <div>
          <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">Target DN</label>
          <InputText v-model="triggerTargetDn" placeholder="CN=John Doe,OU=Users,DC=..." style="width: 100%" />
        </div>
      </div>
      <template #footer>
        <Button label="Cancel" text @click="showTriggerDialog = false" />
        <Button label="Trigger" @click="executeTrigger" :disabled="!triggerTargetDn" />
      </template>
    </Dialog>

    <!-- Instance Detail Dialog -->
    <Dialog v-model:visible="showInstanceDialog" header="Workflow Instance" modal style="width: 650px">
      <div v-if="selectedInstance">
        <div style="display: flex; gap: 1rem; margin-bottom: 1rem">
          <div>
            <div style="font-weight: 600; font-size: 0.8125rem; color: var(--p-text-muted-color)">WORKFLOW</div>
            <div>{{ selectedInstance.workflowName }}</div>
          </div>
          <div>
            <div style="font-weight: 600; font-size: 0.8125rem; color: var(--p-text-muted-color)">STATUS</div>
            <Tag :value="selectedInstance.status" :severity="getStatusSeverity(selectedInstance.status)" />
          </div>
          <div>
            <div style="font-weight: 600; font-size: 0.8125rem; color: var(--p-text-muted-color)">TARGET</div>
            <div style="font-size: 0.875rem; word-break: break-all">{{ selectedInstance.targetDn }}</div>
          </div>
        </div>

        <div v-if="selectedInstance.status === 'AwaitingApproval'" style="margin-bottom: 1rem; display: flex; gap: 0.5rem">
          <Button label="Approve" icon="pi pi-check" severity="success" size="small" @click="approve(selectedInstance)" />
          <Button label="Reject" icon="pi pi-times" severity="danger" size="small" @click="reject(selectedInstance)" />
          <span style="color: var(--p-text-muted-color); align-self: center; font-size: 0.875rem">
            Pending approval from: {{ selectedInstance.approvalPendingFrom }}
          </span>
        </div>

        <DataTable :value="selectedInstance.stepResults" stripedRows>
          <Column header="#" style="width: 50px">
            <template #body="{ index }">{{ index + 1 }}</template>
          </Column>
          <Column field="stepName" header="Step" />
          <Column header="Type">
            <template #body="{ data }">
              <Tag :value="data.stepType" />
            </template>
          </Column>
          <Column header="Status">
            <template #body="{ data }">
              <Tag :value="data.status" :severity="getStepStatusSeverity(data.status)" />
            </template>
          </Column>
          <Column field="detail" header="Detail" />
          <Column header="Completed">
            <template #body="{ data }">{{ formatDate(data.completedAt) }}</template>
          </Column>
        </DataTable>
      </div>
    </Dialog>
  </div>
</template>
