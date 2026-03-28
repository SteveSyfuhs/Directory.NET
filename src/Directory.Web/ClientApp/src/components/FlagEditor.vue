<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import Dialog from 'primevue/dialog'
import Button from 'primevue/button'
import Checkbox from 'primevue/checkbox'
import InputText from 'primevue/inputtext'
import Tag from 'primevue/tag'

const props = defineProps<{
  visible: boolean
  attributeName: string
  /** Current integer value */
  currentValue: number
  /** Map of bit -> flag name */
  flagDefinitions: Record<number, string>
}>()

const emit = defineEmits<{
  'update:visible': [val: boolean]
  'save': [value: number]
}>()

const editValue = ref(0)

watch(() => props.visible, (v) => {
  if (v) {
    editValue.value = props.currentValue ?? 0
  }
})

/** All known flag bits sorted by value */
const knownFlags = computed(() => {
  return Object.entries(props.flagDefinitions)
    .map(([bit, name]) => ({ bit: Number(bit), name }))
    .sort((a, b) => a.bit - b.bit)
})

/** Bits in the current value that are NOT covered by known flags */
const unknownBits = computed(() => {
  let remaining = editValue.value
  for (const { bit } of knownFlags.value) {
    remaining = remaining & ~bit
  }
  if (remaining === 0) return []
  // Decompose into individual bits
  const bits: number[] = []
  for (let i = 0; i < 32; i++) {
    const mask = 1 << i
    if (remaining & mask) {
      bits.push(mask)
    }
  }
  return bits
})

function isFlagSet(bit: number): boolean {
  return (editValue.value & bit) !== 0
}

function toggleFlag(bit: number, checked: boolean) {
  if (checked) {
    editValue.value = editValue.value | bit
  } else {
    editValue.value = editValue.value & ~bit
  }
}

function hexString(val: number): string {
  return '0x' + (val >>> 0).toString(16).toUpperCase().padStart(8, '0')
}

function onSave() {
  emit('save', editValue.value)
  emit('update:visible', false)
}

function onCancel() {
  emit('update:visible', false)
}
</script>

<template>
  <Dialog
    :visible="visible"
    @update:visible="onCancel"
    :header="`Edit Flags: ${attributeName}`"
    modal
    :style="{ width: '520px' }"
    :closable="true"
  >
    <div class="flag-summary">
      <div class="flag-value-row">
        <span class="flag-label">Integer value:</span>
        <InputText :modelValue="String(editValue)" disabled size="small" style="width: 140px; font-family: monospace" />
        <span class="flag-hex">{{ hexString(editValue) }}</span>
      </div>
    </div>

    <div class="flag-list">
      <div
        v-for="flag in knownFlags"
        :key="flag.bit"
        class="flag-item"
      >
        <Checkbox
          :modelValue="isFlagSet(flag.bit)"
          @update:modelValue="(val: boolean) => toggleFlag(flag.bit, val)"
          :binary="true"
          :inputId="`flag-${flag.bit}`"
        />
        <label :for="`flag-${flag.bit}`" class="flag-item-label">
          <span class="flag-name">{{ flag.name }}</span>
          <span class="flag-bit">{{ hexString(flag.bit) }}</span>
        </label>
      </div>
    </div>

    <div v-if="unknownBits.length > 0" class="unknown-flags">
      <div class="unknown-header">Unknown bits set:</div>
      <div class="unknown-tags">
        <Tag v-for="bit in unknownBits" :key="bit" :value="hexString(bit)" severity="warn" />
      </div>
    </div>

    <template #footer>
      <div style="display: flex; justify-content: flex-end; gap: 0.5rem">
        <Button label="Cancel" severity="secondary" text @click="onCancel" />
        <Button label="Save" icon="pi pi-save" @click="onSave" />
      </div>
    </template>
  </Dialog>
</template>

<style scoped>
.flag-summary {
  margin-bottom: 1rem;
  padding-bottom: 0.75rem;
  border-bottom: 1px solid var(--app-neutral-border);
}

.flag-value-row {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}

.flag-label {
  font-size: 0.8125rem;
  font-weight: 600;
  color: var(--p-text-muted-color);
}

.flag-hex {
  font-family: monospace;
  font-size: 0.8125rem;
  color: var(--p-text-muted-color);
}

.flag-list {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  max-height: 400px;
  overflow-y: auto;
  padding-right: 0.25rem;
}

.flag-item {
  display: flex;
  align-items: center;
  gap: 0.625rem;
  padding: 0.375rem 0.5rem;
  border-radius: 0.375rem;
  transition: background 0.15s;
}

.flag-item:hover {
  background: var(--app-neutral-bg);
}

.flag-item-label {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  cursor: pointer;
  flex: 1;
}

.flag-name {
  font-size: 0.8125rem;
  font-weight: 500;
  color: var(--p-text-color);
}

.flag-bit {
  font-family: monospace;
  font-size: 0.75rem;
  color: var(--p-text-muted-color);
  margin-left: auto;
}

.unknown-flags {
  margin-top: 1rem;
  padding-top: 0.75rem;
  border-top: 1px solid var(--app-neutral-border);
}

.unknown-header {
  font-size: 0.8125rem;
  font-weight: 600;
  color: var(--app-warn-text-strong);
  margin-bottom: 0.5rem;
}

.unknown-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 0.375rem;
}
</style>
