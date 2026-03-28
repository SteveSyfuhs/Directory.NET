<script setup lang="ts">
import { ref } from 'vue'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import Button from 'primevue/button'
import Listbox from 'primevue/listbox'

const props = defineProps<{
  modelValue: string[]
  label?: string
  placeholder?: string
  validator?: (val: string) => string | null
}>()

const emit = defineEmits<{
  'update:modelValue': [val: string[]]
}>()

const selectedValue = ref<string | null>(null)
const addDialogVisible = ref(false)
const newValue = ref('')
const validationError = ref('')

function onAdd() {
  newValue.value = ''
  validationError.value = ''
  addDialogVisible.value = true
}

function confirmAdd() {
  const val = newValue.value.trim()
  if (!val) return
  if (props.validator) {
    const err = props.validator(val)
    if (err) {
      validationError.value = err
      return
    }
  }
  if (props.modelValue.includes(val)) {
    validationError.value = 'Value already exists'
    return
  }
  emit('update:modelValue', [...props.modelValue, val])
  addDialogVisible.value = false
}

function onRemove() {
  if (selectedValue.value === null) return
  emit('update:modelValue', props.modelValue.filter(v => v !== selectedValue.value))
  selectedValue.value = null
}

const items = () => props.modelValue.map(v => ({ label: v, value: v }))
</script>

<template>
  <div class="multi-value-editor">
    <div v-if="label" class="mve-label">{{ label }}</div>
    <Listbox
      v-model="selectedValue"
      :options="items()"
      optionLabel="label"
      optionValue="value"
      scrollHeight="180px"
      class="mve-list"
    />
    <div class="mve-actions">
      <Button label="Add..." icon="pi pi-plus" size="small" severity="secondary" @click="onAdd" />
      <Button label="Remove" icon="pi pi-minus" size="small" severity="danger" outlined
              :disabled="selectedValue === null" @click="onRemove" />
    </div>

    <Dialog v-model:visible="addDialogVisible" :header="'Add ' + (label || 'Value')" modal :style="{ width: '400px' }">
      <div style="display: flex; flex-direction: column; gap: 0.75rem">
        <InputText v-model="newValue" :placeholder="placeholder || 'Enter value'" style="width: 100%"
                   @keyup.enter="confirmAdd" />
        <small v-if="validationError" style="color: var(--p-red-500)">{{ validationError }}</small>
      </div>
      <template #footer>
        <Button label="Cancel" severity="secondary" text @click="addDialogVisible = false" />
        <Button label="Add" icon="pi pi-plus" @click="confirmAdd" :disabled="!newValue.trim()" />
      </template>
    </Dialog>
  </div>
</template>

<style scoped>
.multi-value-editor {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}
.mve-label {
  font-size: 0.8125rem;
  font-weight: 600;
  color: var(--p-text-muted-color);
}
.mve-list {
  width: 100%;
}
.mve-actions {
  display: flex;
  gap: 0.5rem;
}
</style>
