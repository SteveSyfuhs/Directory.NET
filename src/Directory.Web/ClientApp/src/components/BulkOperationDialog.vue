<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import Dialog from 'primevue/dialog'
import Select from 'primevue/select'
import InputText from 'primevue/inputtext'
import Button from 'primevue/button'
import Checkbox from 'primevue/checkbox'
import ProgressBar from 'primevue/progressbar'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Tag from 'primevue/tag'
import { useToast } from 'primevue/usetoast'
import { useConfirm } from 'primevue/useconfirm'
import { bulkModify, bulkMove, bulkEnable, bulkDisable, bulkDelete, bulkResetPassword } from '../api/bulk'
import type { BulkOperationResult } from '../api/types'
import type { BulkModification } from '../api/bulk'

const props = defineProps<{
  visible: boolean
  selectedDns: string[]
}>()

const emit = defineEmits<{
  'update:visible': [val: boolean]
  'completed': []
}>()

const toast = useToast()
const confirm = useConfirm()

const operationOptions = [
  { label: 'Modify Attributes', value: 'modify' },
  { label: 'Move', value: 'move' },
  { label: 'Enable', value: 'enable' },
  { label: 'Disable', value: 'disable' },
  { label: 'Delete', value: 'delete' },
  { label: 'Reset Password', value: 'reset-password' },
]

const operation = ref('modify')
const executing = ref(false)
const progress = ref(0)
const results = ref<BulkOperationResult[]>([])
const showResults = ref(false)

// Modify fields
const modAttribute = ref('')
const modOperation = ref<'set' | 'add' | 'remove' | 'clear'>('set')
const modValue = ref('')
const modOperationOptions = [
  { label: 'Set', value: 'set' },
  { label: 'Add', value: 'add' },
  { label: 'Remove', value: 'remove' },
  { label: 'Clear', value: 'clear' },
]

// Move fields
const moveTargetDn = ref('')

// Reset password fields
const password = ref('')
const mustChangeAtNextLogon = ref(true)

const successCount = computed(() => results.value.filter(r => r.success).length)
const failCount = computed(() => results.value.filter(r => !r.success).length)

watch(() => props.visible, (v) => {
  if (v) {
    results.value = []
    showResults.value = false
    progress.value = 0
  }
})

function close() {
  emit('update:visible', false)
}

async function doExecute() {
  executing.value = true
  progress.value = 50
  results.value = []
  showResults.value = false

  try {
    let response: { results: BulkOperationResult[] }

    switch (operation.value) {
      case 'modify': {
        const modifications: BulkModification[] = [{
          attribute: modAttribute.value,
          operation: modOperation.value,
          values: modOperation.value === 'clear' ? undefined : [modValue.value],
        }]
        response = await bulkModify(props.selectedDns, modifications)
        break
      }
      case 'move':
        response = await bulkMove(props.selectedDns, moveTargetDn.value)
        break
      case 'enable':
        response = await bulkEnable(props.selectedDns)
        break
      case 'disable':
        response = await bulkDisable(props.selectedDns)
        break
      case 'delete':
        response = await bulkDelete(props.selectedDns)
        break
      case 'reset-password':
        response = await bulkResetPassword(props.selectedDns, password.value, mustChangeAtNextLogon.value)
        break
      default:
        return
    }

    results.value = response.results
    showResults.value = true
    progress.value = 100

    const sc = response.results.filter(r => r.success).length
    const fc = response.results.filter(r => !r.success).length

    if (fc === 0) {
      toast.add({ severity: 'success', summary: 'Complete', detail: `All ${sc} operations succeeded`, life: 3000 })
    } else {
      toast.add({ severity: 'warn', summary: 'Partial', detail: `${sc} succeeded, ${fc} failed`, life: 5000 })
    }

    emit('completed')
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    executing.value = false
  }
}

function execute() {
  const operationLabel = operationOptions.find(o => o.value === operation.value)?.label ?? operation.value
  confirm.require({
    message: `Execute "${operationLabel}" on ${props.selectedDns.length} object(s)?`,
    header: 'Confirm Bulk Operation',
    icon: operation.value === 'delete' ? 'pi pi-exclamation-triangle' : 'pi pi-question-circle',
    acceptClass: operation.value === 'delete' ? 'p-button-danger' : undefined,
    accept: () => doExecute(),
  })
}

function cnFromDn(dn: string): string {
  const match = dn.match(/^(?:CN|OU)=([^,]+)/i)
  return match ? match[1] : dn
}
</script>

<template>
  <Dialog :visible="visible" @update:visible="close" header="Bulk Operations" modal
          :style="{ width: '700px' }" :closable="!executing">
    <div v-if="!showResults">
      <p style="font-size: 0.8125rem; color: var(--p-text-muted-color); margin: 0 0 0.75rem 0">Apply an operation to all selected objects at once. Choose an operation type and configure its settings below.</p>
      <div style="margin-bottom: 1rem; padding: 0.75rem; background: var(--p-surface-ground); border-radius: 0.5rem; border: 1px solid var(--p-surface-border)">
        <span style="font-weight: 600">{{ selectedDns.length }}</span> object(s) selected
      </div>

      <div style="display: flex; flex-direction: column; gap: 1rem">
        <div>
          <label style="font-size: 0.8125rem; font-weight: 600; display: block; margin-bottom: 0.375rem">Operation</label>
          <Select v-model="operation" :options="operationOptions" optionLabel="label" optionValue="value"
                  style="width: 100%" size="small" />
        </div>

        <!-- Modify form -->
        <template v-if="operation === 'modify'">
          <p style="font-size: 0.8125rem; color: var(--p-text-muted-color); margin: 0 0 0.25rem 0">Set, replace, or clear an attribute value across all selected objects.</p>
          <div style="display: flex; gap: 0.5rem; align-items: flex-end">
            <div style="flex: 1">
              <label style="font-size: 0.8125rem; font-weight: 600; display: block; margin-bottom: 0.375rem">Attribute</label>
              <InputText v-model="modAttribute" size="small" style="width: 100%" placeholder="e.g. department" />
            </div>
            <div style="width: 120px">
              <label style="font-size: 0.8125rem; font-weight: 600; display: block; margin-bottom: 0.375rem">Action</label>
              <Select v-model="modOperation" :options="modOperationOptions" optionLabel="label" optionValue="value"
                      size="small" style="width: 100%" />
            </div>
          </div>
          <div v-if="modOperation !== 'clear'">
            <label style="font-size: 0.8125rem; font-weight: 600; display: block; margin-bottom: 0.375rem">Value</label>
            <InputText v-model="modValue" size="small" style="width: 100%" placeholder="Attribute value" />
          </div>
        </template>

        <!-- Move form -->
        <template v-if="operation === 'move'">
          <p style="font-size: 0.8125rem; color: var(--p-text-muted-color); margin: 0 0 0.25rem 0">Move all selected objects to a different organizational unit or container.</p>
          <div>
            <label style="font-size: 0.8125rem; font-weight: 600; display: block; margin-bottom: 0.375rem">Target Container DN</label>
            <InputText v-model="moveTargetDn" size="small" style="width: 100%"
                       placeholder="e.g. OU=Employees,DC=corp,DC=example,DC=com" />
          </div>
        </template>

        <!-- Reset Password form -->
        <template v-if="operation === 'reset-password'">
          <p style="font-size: 0.8125rem; color: var(--p-text-muted-color); margin: 0 0 0.25rem 0">Set a new password for all selected user accounts.</p>
          <div>
            <label style="font-size: 0.8125rem; font-weight: 600; display: block; margin-bottom: 0.375rem">New Password</label>
            <InputText v-model="password" type="password" size="small" style="width: 100%" />
          </div>
          <div style="display: flex; align-items: center; gap: 0.5rem">
            <Checkbox v-model="mustChangeAtNextLogon" :binary="true" inputId="bulk-must-change" />
            <label for="bulk-must-change" style="font-size: 0.875rem">User must change password at next logon</label>
          </div>
        </template>

        <!-- Enable/Disable/Delete: no extra form needed -->
        <div v-if="operation === 'enable'" style="color: var(--p-text-muted-color); font-size: 0.875rem">
          This will enable all selected accounts by clearing the ACCOUNTDISABLE flag.
        </div>
        <div v-if="operation === 'disable'" style="color: var(--p-text-muted-color); font-size: 0.875rem">
          This will disable all selected accounts by setting the ACCOUNTDISABLE flag.
        </div>
        <div v-if="operation === 'delete'" style="color: var(--app-danger-text); font-size: 0.875rem; font-weight: 500">
          Warning: This will delete all selected objects. This action cannot be undone.
        </div>
      </div>

      <div v-if="executing" style="margin-top: 1rem">
        <ProgressBar :value="progress" />
      </div>

    </div>

    <!-- Results view -->
    <div v-else>
      <div style="display: flex; gap: 1rem; margin-bottom: 1rem">
        <div style="padding: 0.75rem 1rem; background: var(--app-success-bg); border-radius: 0.5rem; flex: 1; text-align: center">
          <div style="font-size: 1.5rem; font-weight: 700; color: var(--app-success-text)">{{ successCount }}</div>
          <div style="font-size: 0.8125rem; color: var(--app-success-text-strong)">Succeeded</div>
        </div>
        <div style="padding: 0.75rem 1rem; background: var(--app-danger-bg); border-radius: 0.5rem; flex: 1; text-align: center">
          <div style="font-size: 1.5rem; font-weight: 700; color: var(--app-danger-text)">{{ failCount }}</div>
          <div style="font-size: 0.8125rem; color: var(--app-danger-text-strong)">Failed</div>
        </div>
      </div>

      <DataTable :value="results" size="small" stripedRows scrollable scrollHeight="300px">
        <Column header="Object" style="min-width: 200px">
          <template #body="{ data }">
            <span style="font-size: 0.8125rem">{{ cnFromDn(data.dn) }}</span>
          </template>
        </Column>
        <Column header="Status" style="width: 100px">
          <template #body="{ data }">
            <Tag :value="data.success ? 'OK' : 'Failed'" :severity="data.success ? 'success' : 'danger'" />
          </template>
        </Column>
        <Column header="Error" style="min-width: 200px">
          <template #body="{ data }">
            <span v-if="data.error" style="font-size: 0.8125rem; color: var(--app-danger-text)">{{ data.error }}</span>
          </template>
        </Column>
      </DataTable>
    </div>

    <template #footer>
      <div style="display: flex; justify-content: flex-end; gap: 0.5rem">
        <template v-if="!showResults">
          <Button label="Cancel" severity="secondary" text @click="close" :disabled="executing" />
          <Button label="Execute" icon="pi pi-play" @click="execute" :loading="executing"
                  :severity="operation === 'delete' ? 'danger' : 'primary'" />
        </template>
        <template v-else>
          <Button label="Close" @click="close" />
        </template>
      </div>
    </template>
  </Dialog>
</template>
