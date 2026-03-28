<script setup lang="ts">
import { ref } from 'vue'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Select from 'primevue/select'
import FileUpload from 'primevue/fileupload'
import Checkbox from 'primevue/checkbox'
import { useToast } from 'primevue/usetoast'
import { exportLdif, importLdif, validateLdif } from '../api/ldif'
import type { LdifImportResult } from '../types/ldif'

const toast = useToast()

// Export state
const exportBaseDn = ref('')
const exportFilter = ref('(objectClass=*)')
const exportScope = ref('subtree')
const exportAttributes = ref('')
const includeOperationalAttributes = ref(false)
const exporting = ref(false)

const scopeOptions = [
  { label: 'Subtree (all descendants)', value: 'subtree' },
  { label: 'One Level (direct children)', value: 'oneLevel' },
  { label: 'Base (single object)', value: 'base' },
]

// Import state
const importing = ref(false)
const validating = ref(false)
const importResult = ref<LdifImportResult | null>(null)
const selectedFile = ref<File | null>(null)

async function doExport() {
  exporting.value = true
  try {
    const attrs = exportAttributes.value.trim()
      ? exportAttributes.value.split(',').map(a => a.trim()).filter(Boolean)
      : undefined

    const blob = await exportLdif({
      baseDn: exportBaseDn.value || undefined,
      filter: exportFilter.value || undefined,
      scope: exportScope.value as 'base' | 'oneLevel' | 'subtree',
      attributes: attrs,
      includeOperationalAttributes: includeOperationalAttributes.value,
    })

    downloadBlob(blob, `ldif-export-${formatTimestamp()}.ldif`)
    toast.add({ severity: 'success', summary: 'Export Complete', detail: 'LDIF export downloaded.', life: 3000 })
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Export Failed', detail: e.message, life: 5000 })
  } finally {
    exporting.value = false
  }
}

function onFileSelect(event: any) {
  const file = event.files?.[0]
  if (file) {
    selectedFile.value = file
    importResult.value = null
  }
}

async function doValidate() {
  if (!selectedFile.value) {
    toast.add({ severity: 'warn', summary: 'No File', detail: 'Select an LDIF file first.', life: 3000 })
    return
  }

  validating.value = true
  importResult.value = null
  try {
    importResult.value = await validateLdif(selectedFile.value)
    toast.add({
      severity: importResult.value.failed > 0 ? 'warn' : 'success',
      summary: 'Validation Complete',
      detail: `${importResult.value.totalRecords} records analyzed. ${importResult.value.imported} valid, ${importResult.value.failed} errors.`,
      life: 5000,
    })
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Validation Failed', detail: e.message, life: 5000 })
  } finally {
    validating.value = false
  }
}

async function doImport() {
  if (!selectedFile.value) {
    toast.add({ severity: 'warn', summary: 'No File', detail: 'Select an LDIF file first.', life: 3000 })
    return
  }

  importing.value = true
  importResult.value = null
  try {
    importResult.value = await importLdif(selectedFile.value)
    toast.add({
      severity: importResult.value.failed > 0 ? 'warn' : 'success',
      summary: 'Import Complete',
      detail: `Imported: ${importResult.value.imported}, Skipped: ${importResult.value.skipped}, Failed: ${importResult.value.failed}`,
      life: 5000,
    })
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Import Failed', detail: e.message, life: 5000 })
  } finally {
    importing.value = false
  }
}

function formatTimestamp(): string {
  const d = new Date()
  return d.toISOString().replace(/[T:]/g, '-').slice(0, 19)
}

function downloadBlob(blob: Blob, filename: string) {
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = filename
  document.body.appendChild(a)
  a.click()
  document.body.removeChild(a)
  URL.revokeObjectURL(url)
}
</script>

<template>
  <div>
    <div class="page-header">
      <h1>LDIF Import / Export</h1>
      <p>Import and export directory data using the standard LDIF format (RFC 2849).</p>
    </div>

    <!-- Export Section -->
    <div class="card" style="margin-bottom: 1.5rem">
      <div class="card-title">Export</div>

      <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 1rem; margin-bottom: 1rem">
        <div>
          <label style="display: block; font-size: 0.875rem; font-weight: 600; color: var(--p-text-muted-color); margin-bottom: 0.375rem">Base DN</label>
          <InputText
            v-model="exportBaseDn"
            placeholder="Leave empty for domain root"
            style="width: 100%"
          />
        </div>
        <div>
          <label style="display: block; font-size: 0.875rem; font-weight: 600; color: var(--p-text-muted-color); margin-bottom: 0.375rem">LDAP Filter</label>
          <InputText
            v-model="exportFilter"
            placeholder="(objectClass=*)"
            style="width: 100%"
          />
        </div>
      </div>

      <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 1rem; margin-bottom: 1rem">
        <div>
          <label style="display: block; font-size: 0.875rem; font-weight: 600; color: var(--p-text-muted-color); margin-bottom: 0.375rem">Scope</label>
          <Select
            v-model="exportScope"
            :options="scopeOptions"
            optionLabel="label"
            optionValue="value"
            style="width: 100%"
          />
        </div>
        <div>
          <label style="display: block; font-size: 0.875rem; font-weight: 600; color: var(--p-text-muted-color); margin-bottom: 0.375rem">Attributes (comma-separated, optional)</label>
          <InputText
            v-model="exportAttributes"
            placeholder="cn,sAMAccountName,mail (empty = all)"
            style="width: 100%"
          />
        </div>
      </div>

      <div style="display: flex; align-items: center; gap: 1rem; margin-bottom: 1rem">
        <div style="display: flex; align-items: center; gap: 0.5rem">
          <Checkbox v-model="includeOperationalAttributes" :binary="true" inputId="includeOpsAttrs" />
          <label for="includeOpsAttrs" style="font-size: 0.875rem; color: var(--p-text-color); cursor: pointer">Include operational attributes</label>
        </div>
        <span style="flex: 1"></span>
        <Button
          label="Export LDIF"
          icon="pi pi-download"
          :loading="exporting"
          @click="doExport"
        />
      </div>

      <p style="color: var(--p-text-muted-color); font-size: 0.8125rem; margin: 0">
        Exports matching directory entries as LDIF content records, compatible with ldapmodify and other LDAP tools.
      </p>
    </div>

    <!-- Import Section -->
    <div class="card">
      <div class="card-title">Import</div>

      <p style="color: var(--p-text-muted-color); font-size: 0.875rem; margin-bottom: 1rem">
        Upload an LDIF file to import or modify directory entries. Supports content records (add), change records (add/modify/delete/moddn).
        Use Validate to preview results without making changes.
      </p>

      <div style="display: flex; align-items: center; gap: 0.75rem; margin-bottom: 1rem">
        <FileUpload
          mode="basic"
          accept=".ldif,.ldf"
          :maxFileSize="104857600"
          chooseLabel="Select LDIF File"
          :auto="false"
          customUpload
          @select="onFileSelect"
          :disabled="importing || validating"
        />
        <span v-if="selectedFile" style="font-size: 0.875rem; color: var(--p-text-color)">
          {{ selectedFile.name }} ({{ (selectedFile.size / 1024).toFixed(1) }} KB)
        </span>
      </div>

      <div style="display: flex; gap: 0.5rem; margin-bottom: 1rem">
        <Button
          label="Validate (Dry Run)"
          icon="pi pi-check-circle"
          severity="secondary"
          :loading="validating"
          :disabled="!selectedFile || importing"
          @click="doValidate"
        />
        <Button
          label="Import"
          icon="pi pi-upload"
          :loading="importing"
          :disabled="!selectedFile || validating"
          @click="doImport"
        />
      </div>

      <div v-if="importing || validating" style="color: var(--p-text-muted-color); margin-bottom: 1rem">
        <i class="pi pi-spin pi-spinner" style="margin-right: 0.5rem"></i>
        {{ importing ? 'Importing...' : 'Validating...' }}
      </div>

      <!-- Results -->
      <div v-if="importResult" class="stat-grid" style="margin-bottom: 1rem">
        <div class="stat-card">
          <div class="stat-icon blue"><i class="pi pi-list"></i></div>
          <div>
            <div class="stat-value">{{ importResult.totalRecords }}</div>
            <div class="stat-label">Total Records</div>
          </div>
        </div>
        <div class="stat-card">
          <div class="stat-icon green"><i class="pi pi-check"></i></div>
          <div>
            <div class="stat-value">{{ importResult.imported }}</div>
            <div class="stat-label">Imported</div>
          </div>
        </div>
        <div class="stat-card">
          <div class="stat-icon purple"><i class="pi pi-minus-circle"></i></div>
          <div>
            <div class="stat-value">{{ importResult.skipped }}</div>
            <div class="stat-label">Skipped</div>
          </div>
        </div>
        <div class="stat-card">
          <div class="stat-icon amber"><i class="pi pi-exclamation-triangle"></i></div>
          <div>
            <div class="stat-value">{{ importResult.failed }}</div>
            <div class="stat-label">Failed</div>
          </div>
        </div>
      </div>

      <div v-if="importResult && importResult.errors.length > 0">
        <div class="card-title">Errors</div>
        <div style="max-height: 300px; overflow-y: auto; border: 1px solid var(--p-surface-border); border-radius: 0.5rem; padding: 0.75rem; background: var(--p-surface-ground)">
          <ul style="color: var(--app-danger-text); font-size: 0.8125rem; padding-left: 1.25rem; margin: 0; list-style: none">
            <li v-for="(err, i) in importResult.errors" :key="i" style="margin-bottom: 0.375rem; padding-left: 1rem; position: relative">
              <i class="pi pi-times-circle" style="position: absolute; left: 0; top: 0.125rem; font-size: 0.75rem"></i>
              {{ err }}
            </li>
          </ul>
        </div>
      </div>
    </div>
  </div>
</template>
