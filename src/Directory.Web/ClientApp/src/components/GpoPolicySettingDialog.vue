<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import Dialog from 'primevue/dialog'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import InputNumber from 'primevue/inputnumber'
import Select from 'primevue/select'
import RadioButton from 'primevue/radiobutton'
import DnPicker from './DnPicker.vue'

export interface PolicySettingDef {
  key: string
  name: string
  description: string
  type: 'boolean' | 'number' | 'string' | 'dropdown' | 'multivalue'
  defaultValue?: any
  min?: number
  max?: number
  options?: { label: string; value: any }[]
}

const props = defineProps<{
  visible: boolean
  setting: PolicySettingDef | null
  currentValue: any
}>()

const emit = defineEmits<{
  'update:visible': [val: boolean]
  'save': [key: string, value: any]
}>()

const definedState = ref<'notdefined' | 'enabled' | 'disabled' | 'defined'>('notdefined')
const numericValue = ref<number | null>(null)
const stringValue = ref('')
const dropdownValue = ref<any>(null)
const multiValues = ref<string[]>([])
const newMultiValue = ref('')

watch(() => props.visible, (vis) => {
  if (vis && props.setting) {
    const val = props.currentValue
    if (val === null || val === undefined) {
      definedState.value = 'notdefined'
      numericValue.value = props.setting.defaultValue ?? null
      stringValue.value = ''
      dropdownValue.value = null
      multiValues.value = []
    } else {
      if (props.setting.type === 'boolean') {
        definedState.value = val ? 'enabled' : 'disabled'
      } else {
        definedState.value = 'defined'
      }
      numericValue.value = typeof val === 'number' ? val : null
      stringValue.value = typeof val === 'string' ? val : ''
      dropdownValue.value = val
      multiValues.value = Array.isArray(val) ? [...val] : []
    }
  }
})

const dialogVisible = computed({
  get: () => props.visible,
  set: (v) => emit('update:visible', v),
})

function onSave() {
  if (!props.setting) return

  if (definedState.value === 'notdefined') {
    emit('save', props.setting.key, null)
  } else if (props.setting.type === 'boolean') {
    emit('save', props.setting.key, definedState.value === 'enabled')
  } else if (props.setting.type === 'number') {
    emit('save', props.setting.key, numericValue.value)
  } else if (props.setting.type === 'string') {
    emit('save', props.setting.key, stringValue.value || null)
  } else if (props.setting.type === 'dropdown') {
    emit('save', props.setting.key, dropdownValue.value)
  } else if (props.setting.type === 'multivalue') {
    emit('save', props.setting.key, multiValues.value.length > 0 ? multiValues.value : null)
  }

  dialogVisible.value = false
}

function addMultiValue() {
  if (newMultiValue.value && !multiValues.value.includes(newMultiValue.value)) {
    multiValues.value.push(newMultiValue.value)
    newMultiValue.value = ''
  }
}

function onPrincipalSelected(dn: string) {
  if (dn && !multiValues.value.includes(dn)) {
    multiValues.value.push(dn)
  }
}

function removeMultiValue(index: number) {
  multiValues.value.splice(index, 1)
}
</script>

<template>
  <Dialog v-model:visible="dialogVisible"
          :header="setting?.name || 'Policy Setting'"
          modal :style="{ width: '550px' }">
    <div v-if="setting" style="display: flex; flex-direction: column; gap: 1rem; padding: 0.5rem 0">
      <p v-if="setting.description" style="color: var(--p-text-muted-color); font-size: 0.875rem; margin: 0">
        {{ setting.description }}
      </p>

      <!-- Boolean type -->
      <template v-if="setting.type === 'boolean'">
        <div style="display: flex; flex-direction: column; gap: 0.75rem">
          <div style="display: flex; align-items: center; gap: 0.5rem">
            <RadioButton v-model="definedState" value="notdefined" inputId="nd" />
            <label for="nd">Not Defined</label>
          </div>
          <div style="display: flex; align-items: center; gap: 0.5rem">
            <RadioButton v-model="definedState" value="enabled" inputId="en" />
            <label for="en">Enabled</label>
          </div>
          <div style="display: flex; align-items: center; gap: 0.5rem">
            <RadioButton v-model="definedState" value="disabled" inputId="dis" />
            <label for="dis">Disabled</label>
          </div>
        </div>
      </template>

      <!-- Number type -->
      <template v-else-if="setting.type === 'number'">
        <div style="display: flex; flex-direction: column; gap: 0.75rem">
          <div style="display: flex; align-items: center; gap: 0.5rem">
            <RadioButton v-model="definedState" value="notdefined" inputId="nd2" />
            <label for="nd2">Not Defined</label>
          </div>
          <div style="display: flex; align-items: center; gap: 0.5rem">
            <RadioButton v-model="definedState" value="defined" inputId="def2" />
            <label for="def2">Define this policy setting</label>
          </div>
          <div v-if="definedState === 'defined'" style="padding-left: 1.5rem">
            <InputNumber v-model="numericValue" :min="setting.min" :max="setting.max"
                         size="small" style="width: 200px" />
            <div v-if="setting.defaultValue !== undefined" style="margin-top: 0.25rem; font-size: 0.8rem; color: var(--p-text-muted-color)">
              Default: {{ setting.defaultValue }}
            </div>
          </div>
        </div>
      </template>

      <!-- String type -->
      <template v-else-if="setting.type === 'string'">
        <div style="display: flex; flex-direction: column; gap: 0.75rem">
          <div style="display: flex; align-items: center; gap: 0.5rem">
            <RadioButton v-model="definedState" value="notdefined" inputId="nd3" />
            <label for="nd3">Not Defined</label>
          </div>
          <div style="display: flex; align-items: center; gap: 0.5rem">
            <RadioButton v-model="definedState" value="defined" inputId="def3" />
            <label for="def3">Define this policy setting</label>
          </div>
          <div v-if="definedState === 'defined'" style="padding-left: 1.5rem">
            <InputText v-model="stringValue" size="small" style="width: 100%" />
          </div>
        </div>
      </template>

      <!-- Dropdown type -->
      <template v-else-if="setting.type === 'dropdown'">
        <div style="display: flex; flex-direction: column; gap: 0.75rem">
          <div style="display: flex; align-items: center; gap: 0.5rem">
            <RadioButton v-model="definedState" value="notdefined" inputId="nd4" />
            <label for="nd4">Not Defined</label>
          </div>
          <div style="display: flex; align-items: center; gap: 0.5rem">
            <RadioButton v-model="definedState" value="defined" inputId="def4" />
            <label for="def4">Define this policy setting</label>
          </div>
          <div v-if="definedState === 'defined'" style="padding-left: 1.5rem">
            <Select v-model="dropdownValue" :options="setting.options || []"
                    optionLabel="label" optionValue="value"
                    size="small" style="width: 100%" placeholder="Select a value" />
          </div>
        </div>
      </template>

      <!-- Multi-value type (user rights) -->
      <template v-else-if="setting.type === 'multivalue'">
        <div style="display: flex; flex-direction: column; gap: 0.75rem">
          <div style="display: flex; align-items: center; gap: 0.5rem">
            <RadioButton v-model="definedState" value="notdefined" inputId="nd5" />
            <label for="nd5">Not Defined</label>
          </div>
          <div style="display: flex; align-items: center; gap: 0.5rem">
            <RadioButton v-model="definedState" value="defined" inputId="def5" />
            <label for="def5">Define this policy setting</label>
          </div>
          <div v-if="definedState === 'defined'" style="padding-left: 1.5rem; display: flex; flex-direction: column; gap: 0.5rem">
            <div style="display: flex; gap: 0.5rem; align-items: flex-end">
              <DnPicker v-model="newMultiValue" label="Add Principal" objectFilter="(|(objectClass=user)(objectClass=group))" style="flex: 1" />
              <Button icon="pi pi-plus" size="small" @click="addMultiValue" :disabled="!newMultiValue" />
            </div>
            <div v-for="(val, idx) in multiValues" :key="idx"
                 style="display: flex; align-items: center; gap: 0.5rem; padding: 0.375rem 0.5rem; background: var(--p-surface-100); border-radius: 4px">
              <span style="flex: 1; font-size: 0.875rem; overflow: hidden; text-overflow: ellipsis; white-space: nowrap">{{ val }}</span>
              <Button icon="pi pi-times" size="small" severity="danger" text @click="removeMultiValue(idx)" />
            </div>
            <div v-if="multiValues.length === 0" style="color: var(--p-text-muted-color); font-size: 0.875rem">
              No principals assigned
            </div>
          </div>
        </div>
      </template>
    </div>

    <template #footer>
      <Button label="Cancel" severity="secondary" text @click="dialogVisible = false" />
      <Button label="OK" icon="pi pi-check" @click="onSave" />
    </template>
  </Dialog>
</template>
