<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import Card from 'primevue/card'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Tag from 'primevue/tag'
import FileUpload from 'primevue/fileupload'
import Dialog from 'primevue/dialog'
import Message from 'primevue/message'
import ProgressSpinner from 'primevue/progressspinner'
import { useToast } from 'primevue/usetoast'
import {
  getTlsCertificateInfo,
  getTlsStatus,
  uploadTlsCertificate,
  removeTlsCertificate,
  type TlsCertificateInfo,
  type TlsStatus,
} from '../api/tlsCertificate'

const toast = useToast()

const certInfo = ref<TlsCertificateInfo | null>(null)
const tlsStatus = ref<TlsStatus | null>(null)
const loading = ref(true)

// Upload state
const selectedFile = ref<File | null>(null)
const pfxPassword = ref('')
const uploading = ref(false)

// Remove dialog
const removeDialogVisible = ref(false)
const removing = ref(false)

onMounted(() => loadData())

async function loadData() {
  loading.value = true
  try {
    const [info, status] = await Promise.all([
      getTlsCertificateInfo(),
      getTlsStatus(),
    ])
    certInfo.value = info
    tlsStatus.value = status
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    loading.value = false
  }
}

function formatDate(dateStr?: string | null): string {
  if (!dateStr) return 'N/A'
  return new Date(dateStr).toLocaleString()
}

const daysUntilExpiry = computed(() => {
  if (!certInfo.value?.notAfter) return null
  const expiry = new Date(certInfo.value.notAfter)
  const now = new Date()
  return Math.ceil((expiry.getTime() - now.getTime()) / (1000 * 60 * 60 * 24))
})

const expirySeverity = computed<'success' | 'warn' | 'danger'>(() => {
  if (daysUntilExpiry.value === null) return 'success'
  if (daysUntilExpiry.value <= 0) return 'danger'
  if (daysUntilExpiry.value <= 30) return 'warn'
  return 'success'
})

const expiryLabel = computed(() => {
  if (daysUntilExpiry.value === null) return ''
  if (daysUntilExpiry.value <= 0) return 'Expired'
  if (daysUntilExpiry.value === 1) return '1 day remaining'
  return `${daysUntilExpiry.value} days remaining`
})

function onFileSelect(event: any) {
  const files = event.files
  if (files && files.length > 0) {
    selectedFile.value = files[0]
  }
}

function onFileClear() {
  selectedFile.value = null
}

async function handleUpload() {
  if (!selectedFile.value) return
  uploading.value = true
  try {
    await uploadTlsCertificate(selectedFile.value, pfxPassword.value || undefined)
    toast.add({ severity: 'success', summary: 'Certificate Uploaded', detail: 'TLS certificate has been updated successfully', life: 3000 })
    selectedFile.value = null
    pfxPassword.value = ''
    await loadData()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Upload Failed', detail: e.message, life: 5000 })
  } finally {
    uploading.value = false
  }
}

async function handleRemove() {
  removing.value = true
  try {
    await removeTlsCertificate()
    toast.add({ severity: 'success', summary: 'Certificate Removed', detail: 'Custom TLS certificate has been removed', life: 3000 })
    removeDialogVisible.value = false
    await loadData()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    removing.value = false
  }
}
</script>

<template>
  <div>
    <div class="page-header">
      <h1>LDAPS / TLS Certificate</h1>
      <p>Manage the TLS certificate used for secure LDAP (LDAPS) connections</p>
    </div>

    <div v-if="loading" style="text-align: center; padding: 4rem">
      <ProgressSpinner />
    </div>

    <template v-else>
      <!-- TLS Status -->
      <div class="stat-grid" style="margin-bottom: 1.5rem">
        <div class="stat-card">
          <div class="stat-icon" :class="tlsStatus?.enabled ? 'green' : 'amber'">
            <i :class="tlsStatus?.enabled ? 'pi pi-lock' : 'pi pi-lock-open'"></i>
          </div>
          <div>
            <div class="stat-value" style="font-size: 1.25rem">{{ tlsStatus?.enabled ? 'Enabled' : 'Not Configured' }}</div>
            <div class="stat-label">LDAPS Status</div>
          </div>
        </div>
        <div class="stat-card">
          <div class="stat-icon blue">
            <i class="pi pi-server"></i>
          </div>
          <div>
            <div class="stat-value" style="font-size: 1.25rem">{{ tlsStatus?.port ?? 636 }}</div>
            <div class="stat-label">LDAPS Port</div>
          </div>
        </div>
        <div class="stat-card" v-if="tlsStatus?.certificateConfigured">
          <div class="stat-icon" :class="{ green: expirySeverity === 'success', amber: expirySeverity === 'warn', purple: expirySeverity === 'danger' }">
            <i class="pi pi-calendar"></i>
          </div>
          <div>
            <div class="stat-value" style="font-size: 1.25rem">{{ tlsStatus?.daysUntilExpiry ?? 'N/A' }}</div>
            <div class="stat-label">Days Until Expiry</div>
          </div>
        </div>
      </div>

      <!-- Current Certificate Info -->
      <Card v-if="certInfo?.configured" style="margin-bottom: 1.5rem">
        <template #title>
          <div style="display: flex; align-items: center; justify-content: space-between">
            <span>Current Certificate</span>
            <div style="display: flex; gap: 0.5rem; align-items: center">
              <Tag :value="expiryLabel" :severity="expirySeverity" />
              <Button icon="pi pi-trash" size="small" severity="danger" outlined
                      label="Remove" @click="removeDialogVisible = true" />
              <Button icon="pi pi-refresh" size="small" severity="secondary" outlined
                      @click="loadData" v-tooltip="'Refresh'" />
            </div>
          </div>
        </template>
        <template #content>
          <Message v-if="daysUntilExpiry !== null && daysUntilExpiry <= 0" severity="error" :closable="false" style="margin-bottom: 1rem">
            This certificate has expired. LDAPS connections may fail. Upload a new certificate.
          </Message>
          <Message v-else-if="daysUntilExpiry !== null && daysUntilExpiry <= 30" severity="warn" :closable="false" style="margin-bottom: 1rem">
            This certificate expires in {{ daysUntilExpiry }} day(s). Consider renewing it soon.
          </Message>

          <div class="cert-details-grid">
            <div class="cert-detail-row">
              <span class="cert-detail-label">Subject</span>
              <span class="cert-detail-value">{{ certInfo.subject }}</span>
            </div>
            <div class="cert-detail-row">
              <span class="cert-detail-label">Issuer</span>
              <span class="cert-detail-value">{{ certInfo.issuer }}</span>
            </div>
            <div class="cert-detail-row">
              <span class="cert-detail-label">Valid From</span>
              <span class="cert-detail-value">{{ formatDate(certInfo.notBefore) }}</span>
            </div>
            <div class="cert-detail-row">
              <span class="cert-detail-label">Valid To</span>
              <span class="cert-detail-value">{{ formatDate(certInfo.notAfter) }}</span>
            </div>
            <div class="cert-detail-row">
              <span class="cert-detail-label">Thumbprint</span>
              <span class="cert-detail-value" style="font-family: monospace; font-size: 0.85rem">{{ certInfo.thumbprint }}</span>
            </div>
            <div class="cert-detail-row">
              <span class="cert-detail-label">Serial Number</span>
              <span class="cert-detail-value" style="font-family: monospace; font-size: 0.85rem">{{ certInfo.serialNumber }}</span>
            </div>
            <div class="cert-detail-row">
              <span class="cert-detail-label">Key Algorithm</span>
              <span class="cert-detail-value">{{ certInfo.keyAlgorithm }}</span>
            </div>
            <div class="cert-detail-row" v-if="certInfo.keySize">
              <span class="cert-detail-label">Key Size</span>
              <span class="cert-detail-value">{{ certInfo.keySize }} bits</span>
            </div>
            <div class="cert-detail-row" v-if="certInfo.uploadedAt">
              <span class="cert-detail-label">Uploaded</span>
              <span class="cert-detail-value">{{ formatDate(certInfo.uploadedAt) }}</span>
            </div>
          </div>
        </template>
      </Card>

      <!-- No Certificate State -->
      <Card v-else style="margin-bottom: 1.5rem">
        <template #title>Current Certificate</template>
        <template #content>
          <div style="text-align: center; padding: 2rem; color: var(--p-text-muted-color)">
            <i class="pi pi-lock-open" style="font-size: 2.5rem; margin-bottom: 1rem; display: block; opacity: 0.4"></i>
            <p style="font-size: 1.1rem; font-weight: 600; margin-bottom: 0.25rem; color: var(--p-text-color)">No TLS Certificate Configured</p>
            <p>Upload a PFX certificate below to enable LDAPS.</p>
          </div>
        </template>
      </Card>

      <!-- Upload New Certificate -->
      <Card>
        <template #title>Upload New Certificate</template>
        <template #content>
          <p style="color: var(--p-text-muted-color); font-size: 0.875rem; margin-bottom: 1rem">
            Upload a PFX (PKCS#12) file containing the TLS certificate and private key for LDAPS.
          </p>

          <div class="upload-form">
            <div class="upload-field">
              <label style="font-weight: 600; font-size: 0.875rem; margin-bottom: 0.5rem; display: block; color: var(--p-text-color)">
                Certificate File (PFX)
              </label>
              <FileUpload
                mode="basic"
                accept=".pfx,.p12"
                :maxFileSize="10485760"
                chooseLabel="Select PFX File"
                :auto="false"
                @select="onFileSelect"
                @clear="onFileClear"
              />
              <small v-if="selectedFile" style="color: var(--p-text-muted-color); margin-top: 0.25rem; display: block">
                {{ selectedFile.name }} ({{ (selectedFile.size / 1024).toFixed(1) }} KB)
              </small>
            </div>

            <div class="upload-field" style="margin-top: 1rem">
              <label style="font-weight: 600; font-size: 0.875rem; margin-bottom: 0.5rem; display: block; color: var(--p-text-color)">
                PFX Password (optional)
              </label>
              <InputText v-model="pfxPassword" type="password" placeholder="Enter PFX password"
                         style="width: 100%; max-width: 400px" />
            </div>

            <div style="margin-top: 1.5rem">
              <Button label="Upload Certificate" icon="pi pi-upload" severity="info"
                      @click="handleUpload" :loading="uploading" :disabled="!selectedFile" />
            </div>
          </div>
        </template>
      </Card>
    </template>

    <!-- Remove Certificate Dialog -->
    <Dialog v-model:visible="removeDialogVisible" header="Remove TLS Certificate" :modal="true" :style="{ width: '30rem' }">
      <div style="display: flex; align-items: center; gap: 0.75rem">
        <i class="pi pi-exclamation-triangle" style="font-size: 1.5rem; color: var(--app-warn-text)"></i>
        <span>Remove the current TLS certificate? LDAPS connections will use the default certificate.</span>
      </div>
      <template #footer>
        <Button label="Cancel" severity="secondary" text @click="removeDialogVisible = false" />
        <Button label="Remove Certificate" icon="pi pi-trash" severity="danger" @click="handleRemove" :loading="removing" />
      </template>
    </Dialog>
  </div>
</template>

<style scoped>
.cert-details-grid {
  display: grid;
  gap: 0;
}

.cert-detail-row {
  display: flex;
  padding: 0.625rem 0;
  border-bottom: 1px solid var(--p-surface-border);
}

.cert-detail-row:last-child {
  border-bottom: none;
}

.cert-detail-label {
  width: 160px;
  min-width: 160px;
  font-weight: 600;
  font-size: 0.875rem;
  color: var(--p-text-muted-color);
}

.cert-detail-value {
  flex: 1;
  font-size: 0.875rem;
  color: var(--p-text-color);
  word-break: break-all;
}

.upload-form {
  max-width: 600px;
}

.stat-icon.amber {
  background: var(--app-stat-amber-bg);
  color: var(--app-stat-amber-text);
}
</style>
