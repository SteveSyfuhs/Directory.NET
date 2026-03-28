<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import RadioButton from 'primevue/radiobutton'
import Checkbox from 'primevue/checkbox'
import Select from 'primevue/select'
import Button from 'primevue/button'
import { useToast } from 'primevue/usetoast'
import { createZone } from '../api/dns'

const props = defineProps<{
  visible: boolean
}>()

const emit = defineEmits<{
  'update:visible': [val: boolean]
  created: []
}>()

const toast = useToast()
const saving = ref(false)

const zoneName = ref('')
const zoneType = ref('primary')
const isReverseZone = ref(false)
const dynamicUpdate = ref('Secure')

const dynamicUpdateOptions = [
  { label: 'None', value: 'None' },
  { label: 'Nonsecure and Secure', value: 'NonsecureAndSecure' },
  { label: 'Secure Only', value: 'Secure' },
]

watch(() => props.visible, (v) => {
  if (v) {
    zoneName.value = ''
    zoneType.value = 'primary'
    isReverseZone.value = false
    dynamicUpdate.value = 'Secure'
  }
})

const displayZoneName = computed(() => {
  if (!zoneName.value) return ''
  if (isReverseZone.value && !zoneName.value.endsWith('.in-addr.arpa')) {
    const parts = zoneName.value.split('.')
    const reversed = [...parts].reverse()
    return reversed.join('.') + '.in-addr.arpa'
  }
  return zoneName.value
})

const canSave = computed(() => zoneName.value.trim().length > 0)

async function onSave() {
  if (!canSave.value) return
  saving.value = true
  try {
    await createZone({
      name: zoneName.value,
      type: zoneType.value,
      reverseZone: isReverseZone.value,
      dynamicUpdate: dynamicUpdate.value,
    })
    toast.add({ severity: 'success', summary: 'Created', detail: `Zone created`, life: 3000 })
    emit('created')
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <Dialog
    :visible="visible"
    @update:visible="emit('update:visible', $event)"
    header="New DNS Zone"
    modal
    :style="{ width: '30rem' }"
    :closable="true"
  >
    <div class="zone-form">
      <div class="form-field">
        <label>Zone Name</label>
        <InputText
          v-model="zoneName"
          :placeholder="isReverseZone ? 'e.g. 168.192 (network ID)' : 'e.g. example.com'"
          style="width: 100%"
        />
        <small v-if="isReverseZone && displayZoneName" class="text-muted">
          Will be stored as: {{ displayZoneName }}
        </small>
      </div>

      <div class="form-field">
        <label>Zone Type</label>
        <div class="radio-group">
          <div class="radio-item">
            <RadioButton v-model="zoneType" value="primary" inputId="zt-primary" />
            <label for="zt-primary">
              <strong>Primary</strong>
              <span>Read/write copy of the zone</span>
            </label>
          </div>
          <div class="radio-item">
            <RadioButton v-model="zoneType" value="secondary" inputId="zt-secondary" />
            <label for="zt-secondary">
              <strong>Secondary</strong>
              <span>Read-only copy from a primary server</span>
            </label>
          </div>
          <div class="radio-item">
            <RadioButton v-model="zoneType" value="stub" inputId="zt-stub" />
            <label for="zt-stub">
              <strong>Stub</strong>
              <span>Contains NS and SOA records only</span>
            </label>
          </div>
        </div>
      </div>

      <div class="form-field">
        <div style="display: flex; align-items: center; gap: 0.5rem">
          <Checkbox v-model="isReverseZone" :binary="true" inputId="reverse-zone" />
          <label for="reverse-zone">Reverse lookup zone</label>
        </div>
      </div>

      <div class="form-field">
        <label>Dynamic Updates</label>
        <Select
          v-model="dynamicUpdate"
          :options="dynamicUpdateOptions"
          optionLabel="label"
          optionValue="value"
          style="width: 100%"
        />
      </div>
    </div>

    <template #footer>
      <Button label="Cancel" severity="secondary" @click="emit('update:visible', false)" />
      <Button label="Create" icon="pi pi-check" :loading="saving" :disabled="!canSave" @click="onSave" />
    </template>
  </Dialog>
</template>

<style scoped>
.zone-form {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.form-field {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.form-field > label {
  font-size: 0.8125rem;
  font-weight: 600;
  color: var(--p-text-color);
}

.radio-group {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  margin-top: 0.25rem;
}

.radio-item {
  display: flex;
  align-items: flex-start;
  gap: 0.5rem;
}

.radio-item label {
  display: flex;
  flex-direction: column;
  font-size: 0.875rem;
  cursor: pointer;
}

.radio-item label span {
  font-size: 0.75rem;
  color: var(--p-text-muted-color);
}

.text-muted {
  color: var(--p-text-muted-color);
  font-size: 0.75rem;
}
</style>
