<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import Textarea from 'primevue/textarea'
import Checkbox from 'primevue/checkbox'
import Button from 'primevue/button'
import { useToast } from 'primevue/usetoast'
import { createOU } from '../api/admin'
import DnPicker from './DnPicker.vue'

const props = defineProps<{
  visible: boolean
  containerDn: string
}>()

const emit = defineEmits<{
  'update:visible': [val: boolean]
  created: []
}>()

const toast = useToast()
const saving = ref(false)
const showAdvanced = ref(false)

const name = ref('')
const description = ref('')
const containerDnInput = ref('')
const protectFromDeletion = ref(true)

// Advanced: Address fields
const streetAddress = ref('')
const city = ref('')
const state = ref('')
const postalCode = ref('')
const country = ref('')

// Advanced: Other
const managedBy = ref('')

const canSave = computed(() => name.value.trim().length > 0)

watch(() => props.visible, (v) => {
  if (v) {
    name.value = ''
    description.value = ''
    containerDnInput.value = props.containerDn || ''
    protectFromDeletion.value = true
    showAdvanced.value = false
    streetAddress.value = ''
    city.value = ''
    state.value = ''
    postalCode.value = ''
    country.value = ''
    managedBy.value = ''
  }
})

async function onSubmit() {
  if (!canSave.value) return
  saving.value = true
  try {
    await createOU(
      name.value,
      containerDnInput.value || props.containerDn,
      description.value || undefined
    )
    toast.add({ severity: 'success', summary: 'Created', detail: 'Organizational Unit created successfully', life: 3000 })
    emit('created')
    emit('update:visible', false)
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    saving.value = false
  }
}

function close() {
  emit('update:visible', false)
}
</script>

<template>
  <Dialog :visible="visible" @update:visible="close" header="Create Organizational Unit" modal :style="{ width: '520px' }">
    <div class="form-stack">
      <div class="form-row">
        <label>Name *</label>
        <InputText v-model="name" class="form-input" placeholder="Engineering" />
      </div>
      <div class="form-row">
        <label>Description</label>
        <Textarea v-model="description" class="form-input" rows="2" autoResize />
      </div>
      <div class="form-row">
        <label>Parent Container DN</label>
        <InputText v-model="containerDnInput" class="form-input" placeholder="DC=example,DC=com" />
      </div>
      <div class="form-row">
        <div style="display: flex; align-items: center; gap: 0.5rem; margin-top: 0.25rem">
          <Checkbox v-model="protectFromDeletion" :binary="true" inputId="chk-protect-delete" />
          <label for="chk-protect-delete" style="font-weight: 400">Protect container from accidental deletion</label>
        </div>
      </div>
    </div>

    <!-- Advanced Section -->
    <div class="advanced-toggle" @click="showAdvanced = !showAdvanced">
      <i :class="showAdvanced ? 'pi pi-chevron-down' : 'pi pi-chevron-right'" style="font-size: 0.75rem"></i>
      <span>Advanced Attributes</span>
    </div>

    <div v-if="showAdvanced" class="advanced-section">
      <div class="form-stack">
        <div class="form-row">
          <label>Street Address</label>
          <Textarea v-model="streetAddress" class="form-input" rows="2" autoResize />
        </div>
        <div class="form-grid-2col">
          <div class="form-row">
            <label>City</label>
            <InputText v-model="city" class="form-input" />
          </div>
          <div class="form-row">
            <label>State/Province</label>
            <InputText v-model="state" class="form-input" />
          </div>
          <div class="form-row">
            <label>Postal Code</label>
            <InputText v-model="postalCode" class="form-input" />
          </div>
          <div class="form-row">
            <label>Country</label>
            <InputText v-model="country" class="form-input" />
          </div>
        </div>
        <div class="form-row">
          <label>Managed By</label>
          <DnPicker v-model="managedBy" label="Managed By" objectFilter="(|(objectClass=user)(objectClass=group))" />
        </div>
      </div>
    </div>

    <template #footer>
      <Button label="Cancel" severity="secondary" text @click="close" />
      <Button label="Create OU" icon="pi pi-building" @click="onSubmit" :loading="saving" :disabled="!canSave" />
    </template>
  </Dialog>
</template>

<style scoped>
.form-stack {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.form-grid-2col {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 1rem;
}

.form-row {
  display: flex;
  flex-direction: column;
  gap: 0.375rem;
}

.form-row label {
  font-size: 0.8125rem;
  font-weight: 600;
  color: var(--p-text-muted-color);
}

.form-input {
  width: 100%;
}

.advanced-toggle {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin-top: 1.25rem;
  padding: 0.5rem 0;
  cursor: pointer;
  color: var(--app-info-text);
  font-size: 0.875rem;
  font-weight: 500;
  user-select: none;
  border-top: 1px solid var(--app-neutral-border);
}

.advanced-toggle:hover {
  color: var(--app-info-text-strong);
}

.advanced-section {
  margin-top: 0.5rem;
}
</style>
