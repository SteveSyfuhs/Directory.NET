<script setup lang="ts">
import { ref, computed } from 'vue'
import Button from 'primevue/button'
import { useToast } from 'primevue/usetoast'

const props = defineProps<{
  /** Hex-encoded string (e.g. "0a1b2c3d...") or base64 string */
  value: string
  /** Maximum lines to show before collapsing (default 4 = 64 bytes) */
  collapsedLines?: number
}>()

const toast = useToast()
const expanded = ref(false)
const maxLines = computed(() => props.collapsedLines ?? 4)

/** Parse hex string to byte array */
function hexToBytes(hex: string): number[] {
  const clean = hex.replace(/[\s-]/g, '')
  const bytes: number[] = []
  for (let i = 0; i < clean.length; i += 2) {
    bytes.push(parseInt(clean.substring(i, i + 2), 16))
  }
  return bytes
}

const bytes = computed(() => hexToBytes(props.value))
const totalLines = computed(() => Math.ceil(bytes.value.length / 16))
const needsCollapse = computed(() => totalLines.value > maxLines.value)

interface HexLine {
  offset: string
  hex: string
  ascii: string
}

const allLines = computed<HexLine[]>(() => {
  const result: HexLine[] = []
  const b = bytes.value
  for (let i = 0; i < b.length; i += 16) {
    const chunk = b.slice(i, i + 16)
    const offset = i.toString(16).padStart(8, '0')
    const hexParts: string[] = []
    for (let j = 0; j < 16; j++) {
      if (j < chunk.length) {
        hexParts.push(chunk[j].toString(16).padStart(2, '0'))
      } else {
        hexParts.push('  ')
      }
    }
    // Group hex bytes in pairs of 8 for readability
    const hexStr = hexParts.slice(0, 8).join(' ') + '  ' + hexParts.slice(8).join(' ')
    const asciiStr = chunk.map(b => (b >= 0x20 && b <= 0x7e) ? String.fromCharCode(b) : '.').join('')
    result.push({ offset, hex: hexStr, ascii: asciiStr })
  }
  return result
})

const visibleLines = computed(() => {
  if (expanded.value || !needsCollapse.value) return allLines.value
  return allLines.value.slice(0, maxLines.value)
})

function copyHex() {
  const clean = props.value.replace(/[\s-]/g, '')
  navigator.clipboard.writeText(clean).then(() => {
    toast.add({ severity: 'info', summary: 'Copied', detail: 'Hex data copied to clipboard', life: 2000 })
  })
}
</script>

<template>
  <div class="hex-viewer">
    <div class="hex-dump">
      <div v-for="line in visibleLines" :key="line.offset" class="hex-line">
        <span class="hex-offset">{{ line.offset }}</span>
        <span class="hex-bytes">{{ line.hex }}</span>
        <span class="hex-ascii">{{ line.ascii }}</span>
      </div>
    </div>
    <div class="hex-controls">
      <Button
        v-if="needsCollapse"
        :label="expanded ? 'Show less' : `Show all (${bytes.length} bytes)`"
        text
        size="small"
        :icon="expanded ? 'pi pi-chevron-up' : 'pi pi-chevron-down'"
        @click="expanded = !expanded"
      />
      <Button
        icon="pi pi-copy"
        text
        size="small"
        label="Copy hex"
        @click="copyHex"
      />
    </div>
  </div>
</template>

<style scoped>
.hex-viewer {
  font-family: 'Cascadia Code', 'Fira Code', 'Consolas', monospace;
  font-size: 0.75rem;
  line-height: 1.4;
}

.hex-dump {
  background: var(--p-surface-ground);
  border: 1px solid var(--app-neutral-border);
  border-radius: 0.375rem;
  padding: 0.5rem;
  overflow-x: auto;
}

.hex-line {
  white-space: pre;
  display: flex;
  gap: 1rem;
}

.hex-offset {
  color: var(--p-text-color);
  user-select: none;
}

.hex-bytes {
  color: var(--p-text-color);
}

.hex-ascii {
  color: var(--app-accent-color);
  border-left: 1px solid var(--app-neutral-border);
  padding-left: 0.75rem;
}

.hex-controls {
  display: flex;
  gap: 0.25rem;
  margin-top: 0.25rem;
}
</style>
