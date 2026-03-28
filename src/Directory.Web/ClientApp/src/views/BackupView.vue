<script setup lang="ts">
import { ref } from 'vue'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Select from 'primevue/select'
import FileUpload from 'primevue/fileupload'
import { useToast } from 'primevue/usetoast'

const toast = useToast()

const objectClass = ref('')
const baseDn = ref('')
const importing = ref(false)
const exportingJson = ref(false)
const exportingLdif = ref(false)

const importResult = ref<{ imported: number; updated: number; failed: number; errors: string[] } | null>(null)

const objectClassOptions = [
  { label: 'All Objects', value: '' },
  { label: 'Users', value: 'user' },
  { label: 'Groups', value: 'group' },
  { label: 'Computers', value: 'computer' },
  { label: 'OUs', value: 'organizationalUnit' },
  { label: 'Contacts', value: 'contact' },
]

function buildQueryParams() {
  const params = new URLSearchParams()
  if (objectClass.value) params.set('objectClass', objectClass.value)
  if (baseDn.value) params.set('baseDn', baseDn.value)
  const qs = params.toString()
  return qs ? `?${qs}` : ''
}

async function exportJson() {
  exportingJson.value = true
  try {
    const url = `/api/v1/backup/export${buildQueryParams()}`
    const resp = await fetch(url)
    if (!resp.ok) throw new Error(`Export failed: ${resp.statusText}`)
    const blob = await resp.blob()
    downloadBlob(blob, getFilenameFromResponse(resp, 'directory-export.json'))
    toast.add({ severity: 'success', summary: 'Export Complete', detail: 'JSON export downloaded.', life: 3000 })
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Export Failed', detail: e.message, life: 5000 })
  } finally {
    exportingJson.value = false
  }
}

async function exportLdif() {
  exportingLdif.value = true
  try {
    const url = `/api/v1/backup/export/ldif${buildQueryParams()}`
    const resp = await fetch(url)
    if (!resp.ok) throw new Error(`Export failed: ${resp.statusText}`)
    const blob = await resp.blob()
    downloadBlob(blob, getFilenameFromResponse(resp, 'directory-export.ldif'))
    toast.add({ severity: 'success', summary: 'Export Complete', detail: 'LDIF export downloaded.', life: 3000 })
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Export Failed', detail: e.message, life: 5000 })
  } finally {
    exportingLdif.value = false
  }
}

function getFilenameFromResponse(resp: Response, fallback: string): string {
  const disp = resp.headers.get('Content-Disposition')
  if (disp) {
    const match = disp.match(/filename="?(.+?)"?$/i)
    if (match) return match[1]
  }
  return fallback
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

async function onImportUpload(event: any) {
  const file = event.files?.[0]
  if (!file) return

  importing.value = true
  importResult.value = null

  try {
    const text = await file.text()
    const objects = JSON.parse(text)

    if (!Array.isArray(objects)) {
      throw new Error('File must contain a JSON array of directory objects.')
    }

    const resp = await fetch('/api/v1/backup/import', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(objects),
    })

    if (!resp.ok) {
      const problem = await resp.json().catch(() => null)
      throw new Error(problem?.detail || `Import failed: ${resp.statusText}`)
    }

    importResult.value = await resp.json()
    toast.add({
      severity: importResult.value!.failed > 0 ? 'warn' : 'success',
      summary: 'Import Complete',
      detail: `Imported: ${importResult.value!.imported}, Updated: ${importResult.value!.updated}, Failed: ${importResult.value!.failed}`,
      life: 5000,
    })
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Import Failed', detail: e.message, life: 5000 })
  } finally {
    importing.value = false
  }
}
</script>

<template>
  <div>
    <div class="page-header">
      <h1>Backup &amp; Export</h1>
      <p>Export directory data for backup or migration purposes.</p>
    </div>

    <!-- Export Section -->
    <div class="card" style="margin-bottom: 1.5rem">
      <div class="card-title">Export</div>

      <div class="toolbar">
        <Select
          v-model="objectClass"
          :options="objectClassOptions"
          optionLabel="label"
          optionValue="value"
          placeholder="Filter by object class"
          style="min-width: 220px"
        />
        <InputText
          v-model="baseDn"
          placeholder="Base DN (optional)"
          style="min-width: 280px"
        />
        <span class="toolbar-spacer" />
        <Button
          label="Export JSON"
          icon="pi pi-download"
          :loading="exportingJson"
          @click="exportJson"
        />
        <Button
          label="Export LDIF"
          icon="pi pi-download"
          severity="secondary"
          :loading="exportingLdif"
          @click="exportLdif"
        />
      </div>

      <p style="color: var(--p-text-muted-color); font-size: 0.875rem; margin: 0">
        JSON format exports full object data. LDIF is the standard AD interchange format for use with ldapmodify/ldapadd tools.
      </p>
    </div>

    <!-- Import Section -->
    <div class="card">
      <div class="card-title">Import</div>

      <p style="color: var(--p-text-muted-color); font-size: 0.875rem; margin-bottom: 1rem">
        Upload a JSON file previously exported from this tool. Each object will be created or updated in the directory.
      </p>

      <FileUpload
        mode="basic"
        accept=".json"
        :maxFileSize="104857600"
        chooseLabel="Select JSON File"
        :auto="true"
        customUpload
        @uploader="onImportUpload"
        :disabled="importing"
      />

      <div v-if="importing" style="margin-top: 1rem; color: var(--p-text-muted-color)">
        <i class="pi pi-spin pi-spinner" style="margin-right: 0.5rem"></i> Importing...
      </div>

      <div v-if="importResult" class="stat-grid" style="margin-top: 1.5rem">
        <div class="stat-card">
          <div class="stat-icon green"><i class="pi pi-plus"></i></div>
          <div>
            <div class="stat-value">{{ importResult.imported }}</div>
            <div class="stat-label">Created</div>
          </div>
        </div>
        <div class="stat-card">
          <div class="stat-icon blue"><i class="pi pi-refresh"></i></div>
          <div>
            <div class="stat-value">{{ importResult.updated }}</div>
            <div class="stat-label">Updated</div>
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

      <div v-if="importResult && importResult.errors.length > 0" style="margin-top: 1rem">
        <div class="card-title">Errors</div>
        <ul style="color: var(--p-text-muted-color); font-size: 0.875rem; padding-left: 1.25rem; margin: 0">
          <li v-for="(err, i) in importResult.errors" :key="i">{{ err }}</li>
        </ul>
      </div>
    </div>
  </div>
</template>
