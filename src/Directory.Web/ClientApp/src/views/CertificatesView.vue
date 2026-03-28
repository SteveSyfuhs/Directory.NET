<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import InputNumber from 'primevue/inputnumber'
import Tag from 'primevue/tag'
import Dialog from 'primevue/dialog'
import TabView from 'primevue/tabview'
import TabPanel from 'primevue/tabpanel'
import Checkbox from 'primevue/checkbox'
import Select from 'primevue/select'
import Textarea from 'primevue/textarea'
import ProgressSpinner from 'primevue/progressspinner'
import { useToast } from 'primevue/usetoast'
import DnPicker from '../components/DnPicker.vue'
import {
  listTemplates, createTemplate, updateTemplate, deleteTemplate,
  getTemplateSecurity, updateTemplateSecurity,
  getCaInfo, initializeCa, listEnrolledCertificates, enrollCertificate,
  revokeCertificate, renewCertificate,
  type CertificateTemplate, type EnrollmentPermission, type EnrolledCertificate,
  type CaInfo, type IssuedCertificateDetail,
} from '../api/certificates'
import { relativeTime } from '../utils/format'

const toast = useToast()

// Templates
const templates = ref<CertificateTemplate[]>([])
const templatesLoading = ref(true)
const selectedTemplate = ref<CertificateTemplate | null>(null)

// Enrolled
const enrolled = ref<EnrolledCertificate[]>([])
const enrolledLoading = ref(true)

// CA
const caInfo = ref<CaInfo | null>(null)
const caLoading = ref(true)

// Create template dialog
const createTemplVisible = ref(false)
const creating = ref(false)
const newTplName = ref('')
const newTplDisplayName = ref('')
const newTplValidity = ref(365)
const newTplRenewal = ref(42)
const newTplKeySize = ref(2048)
const newTplAutoEnroll = ref(false)
const newTplRequireApproval = ref(false)

// Edit template dialog
const editTemplVisible = ref(false)
const editTpl = ref<CertificateTemplate | null>(null)
const editTplDisplayName = ref('')
const editTplValidity = ref(365)
const editTplRenewal = ref(42)
const editTplKeySize = ref(2048)
const editTplAutoEnroll = ref(false)
const editTplRequireApproval = ref(false)
const editTplPublishToDs = ref(false)
const editTplPermissions = ref<EnrollmentPermission[]>([])
const editPermPrincipalDn = ref('')
const savingTpl = ref(false)

// Initialize CA dialog
const initCaVisible = ref(false)
const initCaLoading = ref(false)
const initCaName = ref('Directory.NET Certificate Authority')
const initCaOrg = ref('')
const initCaCountry = ref('')
const initCaValidity = ref(10)
const initCaKeySize = ref(4096)
const initCaHash = ref('SHA256')

// Enroll dialog
const enrollVisible = ref(false)
const enrollTemplate = ref('')
const enrollSubject = ref('')
const enrollSanEntries = ref<string[]>([])
const enrollSanInput = ref('')
const enrollSanType = ref('dns')
const enrollUseCsr = ref(false)
const enrollCsr = ref('')
const enrolling = ref(false)

// Issued certificate detail dialog
const certDetailVisible = ref(false)
const certDetail = ref<IssuedCertificateDetail | null>(null)

// Revoke dialog
const revokeVisible = ref(false)
const revokeSerial = ref('')
const revokeSubject = ref('')
const revokeReason = ref(0)
const revoking = ref(false)

// EKU options
const ekuOptions = [
  { label: 'Server Authentication (1.3.6.1.5.5.7.3.1)', value: '1.3.6.1.5.5.7.3.1' },
  { label: 'Client Authentication (1.3.6.1.5.5.7.3.2)', value: '1.3.6.1.5.5.7.3.2' },
  { label: 'Code Signing (1.3.6.1.5.5.7.3.3)', value: '1.3.6.1.5.5.7.3.3' },
  { label: 'Email Protection (1.3.6.1.5.5.7.3.4)', value: '1.3.6.1.5.5.7.3.4' },
  { label: 'Smart Card Logon (1.3.6.1.4.1.311.20.2.2)', value: '1.3.6.1.4.1.311.20.2.2' },
]

const revocationReasons = [
  { label: 'Unspecified', value: 0 },
  { label: 'Key Compromise', value: 1 },
  { label: 'CA Compromise', value: 2 },
  { label: 'Affiliation Changed', value: 3 },
  { label: 'Superseded', value: 4 },
  { label: 'Cessation of Operation', value: 5 },
]

const hashAlgorithms = [
  { label: 'SHA-256', value: 'SHA256' },
  { label: 'SHA-384', value: 'SHA384' },
  { label: 'SHA-512', value: 'SHA512' },
]

const keySizeOptions = [
  { label: '2048', value: 2048 },
  { label: '4096', value: 4096 },
]

const sanTypeOptions = [
  { label: 'DNS Name', value: 'dns' },
  { label: 'IP Address', value: 'ip' },
  { label: 'Email', value: 'email' },
]

onMounted(async () => {
  await Promise.all([loadTemplates(), loadEnrolled(), loadCaInfo()])
})

async function loadTemplates() {
  templatesLoading.value = true
  try { templates.value = await listTemplates() }
  catch (e: any) { toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 }) }
  finally { templatesLoading.value = false }
}

async function loadEnrolled() {
  enrolledLoading.value = true
  try { enrolled.value = await listEnrolledCertificates() }
  catch { enrolled.value = [] }
  finally { enrolledLoading.value = false }
}

async function loadCaInfo() {
  caLoading.value = true
  try { caInfo.value = await getCaInfo() }
  catch { caInfo.value = null }
  finally { caLoading.value = false }
}

// Create template
async function onCreateTemplate() {
  if (!newTplName.value.trim()) return
  creating.value = true
  try {
    await createTemplate({
      name: newTplName.value.trim(),
      displayName: newTplDisplayName.value || undefined,
      validityPeriodDays: newTplValidity.value,
      renewalPeriodDays: newTplRenewal.value,
      minimumKeySize: newTplKeySize.value,
      autoEnroll: newTplAutoEnroll.value,
      requireApproval: newTplRequireApproval.value,
    })
    toast.add({ severity: 'success', summary: 'Created', detail: 'Certificate template created', life: 3000 })
    createTemplVisible.value = false
    resetCreateForm()
    await loadTemplates()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    creating.value = false
  }
}

function resetCreateForm() {
  newTplName.value = ''
  newTplDisplayName.value = ''
  newTplValidity.value = 365
  newTplRenewal.value = 42
  newTplKeySize.value = 2048
  newTplAutoEnroll.value = false
  newTplRequireApproval.value = false
}

// Edit template
async function openEditTemplate(tpl: CertificateTemplate) {
  editTpl.value = tpl
  editTplDisplayName.value = tpl.displayName
  editTplValidity.value = tpl.validityPeriodDays
  editTplRenewal.value = tpl.renewalPeriodDays
  editTplKeySize.value = tpl.minimumKeySize
  editTplAutoEnroll.value = tpl.autoEnroll
  editTplRequireApproval.value = tpl.requireApproval
  editTplPublishToDs.value = tpl.publishToDs
  editTplPermissions.value = [...tpl.enrollmentPermissions]
  editTemplVisible.value = true
}

async function onSaveTemplate() {
  if (!editTpl.value) return
  savingTpl.value = true
  try {
    await updateTemplate(editTpl.value.name, {
      displayName: editTplDisplayName.value,
      validityPeriodDays: editTplValidity.value,
      renewalPeriodDays: editTplRenewal.value,
      minimumKeySize: editTplKeySize.value,
      autoEnroll: editTplAutoEnroll.value,
      requireApproval: editTplRequireApproval.value,
      publishToDs: editTplPublishToDs.value,
      enrollmentPermissions: editTplPermissions.value,
    })
    toast.add({ severity: 'success', summary: 'Saved', detail: 'Template updated', life: 3000 })
    editTemplVisible.value = false
    await loadTemplates()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    savingTpl.value = false
  }
}

async function onDeleteTemplate(tpl: CertificateTemplate) {
  if (!confirm(`Delete certificate template "${tpl.displayName}"?`)) return
  try {
    await deleteTemplate(tpl.name)
    toast.add({ severity: 'success', summary: 'Deleted', detail: 'Template deleted', life: 3000 })
    await loadTemplates()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

function addPermission() {
  if (!editPermPrincipalDn.value) return
  if (editTplPermissions.value.some(p => p.principalDn === editPermPrincipalDn.value)) return
  editTplPermissions.value.push({
    principalDn: editPermPrincipalDn.value,
    canEnroll: true,
    canAutoEnroll: false,
    canManage: false,
  })
  editPermPrincipalDn.value = ''
}

// Initialize CA
async function onInitializeCa() {
  initCaLoading.value = true
  try {
    const result = await initializeCa({
      commonName: initCaName.value,
      organization: initCaOrg.value || undefined,
      country: initCaCountry.value || undefined,
      validityYears: initCaValidity.value,
      keySizeInBits: initCaKeySize.value,
      hashAlgorithm: initCaHash.value,
    })
    caInfo.value = result
    toast.add({ severity: 'success', summary: 'CA Initialized', detail: 'Certificate Authority has been initialized', life: 5000 })
    initCaVisible.value = false
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    initCaLoading.value = false
  }
}

function downloadCaCert() {
  if (!caInfo.value?.certificatePem) return
  const blob = new Blob([caInfo.value.certificatePem], { type: 'application/x-pem-file' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = 'ca-certificate.pem'
  a.click()
  URL.revokeObjectURL(url)
}

function downloadCrl() {
  window.open('/api/v1/certificates/ca/crl', '_blank')
}

// Enroll
async function onEnroll() {
  if (!enrollTemplate.value || !enrollSubject.value) return
  enrolling.value = true
  try {
    const result = await enrollCertificate(
      enrollTemplate.value,
      enrollSubject.value,
      enrollSanEntries.value.length > 0 ? enrollSanEntries.value : undefined,
      enrollUseCsr.value && enrollCsr.value ? btoa(enrollCsr.value) : undefined
    )
    certDetail.value = result
    certDetailVisible.value = true
    toast.add({ severity: 'success', summary: 'Enrolled', detail: 'Certificate issued successfully', life: 3000 })
    enrollVisible.value = false
    enrollTemplate.value = ''
    enrollSubject.value = ''
    enrollSanEntries.value = []
    enrollUseCsr.value = false
    enrollCsr.value = ''
    await loadEnrolled()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    enrolling.value = false
  }
}

function addSanEntry() {
  if (!enrollSanInput.value) return
  const entry = `${enrollSanType.value}:${enrollSanInput.value}`
  if (!enrollSanEntries.value.includes(entry)) {
    enrollSanEntries.value.push(entry)
    enrollSanInput.value = ''
  }
}

function openRevokeDialog(cert: EnrolledCertificate) {
  revokeSerial.value = cert.serialNumber
  revokeSubject.value = cert.subject
  revokeReason.value = 0
  revokeVisible.value = true
}

async function onRevoke() {
  revoking.value = true
  try {
    await revokeCertificate(revokeSerial.value, revokeReason.value)
    toast.add({ severity: 'success', summary: 'Revoked', detail: 'Certificate revoked', life: 3000 })
    revokeVisible.value = false
    await loadEnrolled()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    revoking.value = false
  }
}

async function onRenew(cert: EnrolledCertificate) {
  if (!confirm(`Renew certificate ${cert.serialNumber}? The current certificate will be superseded.`)) return
  try {
    const result = await renewCertificate(cert.serialNumber)
    certDetail.value = result
    certDetailVisible.value = true
    toast.add({ severity: 'success', summary: 'Renewed', detail: 'Certificate renewed successfully', life: 3000 })
    await loadEnrolled()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

function downloadCert() {
  if (!certDetail.value?.certificatePem) return
  const blob = new Blob([certDetail.value.certificatePem], { type: 'application/x-pem-file' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = `${certDetail.value.serialNumber}.pem`
  a.click()
  URL.revokeObjectURL(url)
}

function downloadPrivateKey() {
  if (!certDetail.value?.privateKeyPem) return
  const blob = new Blob([certDetail.value.privateKeyPem], { type: 'application/x-pem-file' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = `${certDetail.value.serialNumber}-key.pem`
  a.click()
  URL.revokeObjectURL(url)
}

function keyUsageLabel(ku: number): string {
  const parts: string[] = []
  if (ku & 0x80) parts.push('Digital Signature')
  if (ku & 0x20) parts.push('Key Encipherment')
  if (ku & 0x40) parts.push('Non-Repudiation')
  if (ku & 0x10) parts.push('Data Encipherment')
  if (ku & 0x08) parts.push('Key Agreement')
  if (ku & 0x04) parts.push('Key Cert Sign')
  if (ku & 0x02) parts.push('CRL Sign')
  return parts.join(', ') || 'None'
}

function certStatusSeverity(status: string): "success" | "danger" | "warn" | "secondary" | "info" | "contrast" | undefined {
  if (status === 'Active') return 'success'
  if (status === 'Revoked') return 'danger'
  if (status === 'Expired') return 'warn'
  return 'secondary'
}

function templatePurpose(tpl: CertificateTemplate): string {
  if (tpl.enhancedKeyUsage.length === 0) return 'All purposes'
  return tpl.enhancedKeyUsage.map(eku => {
    const opt = ekuOptions.find(o => o.value === eku)
    return opt ? opt.label.split(' (')[0] : eku
  }).join(', ')
}
</script>

<template>
  <div>
    <div class="page-header">
      <h1>Certificate Services</h1>
      <p>Manage certificate templates, enrolled certificates, and the Certificate Authority</p>
    </div>

    <TabView>
      <!-- CA Info Tab -->
      <TabPanel header="Certificate Authority">
        <div v-if="caLoading" style="text-align: center; padding: 2rem"><ProgressSpinner /></div>
        <template v-else-if="caInfo && caInfo.isInitialized">
          <div class="card" style="margin-bottom: 1rem">
            <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 1.5rem">
              <div>
                <h3 style="margin: 0 0 0.25rem 0">{{ caInfo.commonName }}</h3>
                <Tag value="Initialized" severity="success" />
              </div>
              <div style="display: flex; gap: 0.5rem">
                <Button label="Download CA Certificate" icon="pi pi-download" size="small" severity="secondary" @click="downloadCaCert" />
                <Button label="Download CRL" icon="pi pi-download" size="small" severity="secondary" @click="downloadCrl" />
              </div>
            </div>
            <div style="display: grid; grid-template-columns: 200px 1fr; gap: 0.75rem; font-size: 0.9375rem">
              <div style="font-weight: 600; color: var(--p-text-muted-color)">Subject</div>
              <div style="font-family: monospace; font-size: 0.875rem">{{ caInfo.subject }}</div>
              <div style="font-weight: 600; color: var(--p-text-muted-color)">Serial Number</div>
              <div style="font-family: monospace; font-size: 0.875rem">{{ caInfo.serialNumber }}</div>
              <div style="font-weight: 600; color: var(--p-text-muted-color)">Thumbprint</div>
              <div style="font-family: monospace; font-size: 0.875rem">{{ caInfo.thumbprint }}</div>
              <div style="font-weight: 600; color: var(--p-text-muted-color)">Valid From</div>
              <div>{{ new Date(caInfo.notBefore).toLocaleString() }}</div>
              <div style="font-weight: 600; color: var(--p-text-muted-color)">Valid Until</div>
              <div>{{ new Date(caInfo.notAfter).toLocaleString() }}</div>
              <div style="font-weight: 600; color: var(--p-text-muted-color)">Key Algorithm</div>
              <div>{{ caInfo.publicKeyAlgorithm }} {{ caInfo.keySize }}-bit</div>
            </div>
          </div>
        </template>
        <div v-else style="text-align: center; padding: 3rem">
          <i class="pi pi-shield" style="font-size: 3rem; margin-bottom: 1rem; display: block; color: var(--p-text-muted-color)"></i>
          <h3 style="margin: 0 0 0.5rem 0">Certificate Authority Not Initialized</h3>
          <p style="color: var(--p-text-muted-color); margin-bottom: 1.5rem">
            Initialize the Certificate Authority to start issuing certificates. This will generate<br>
            a root CA key pair and self-signed certificate.
          </p>
          <Button label="Initialize Certificate Authority" icon="pi pi-shield" @click="initCaVisible = true" />
        </div>
      </TabPanel>

      <!-- Templates Tab -->
      <TabPanel header="Certificate Templates">
        <p style="font-size: 0.8125rem; color: var(--p-text-muted-color); margin: 0 0 0.75rem 0">Certificate templates define the settings and constraints for issued certificates, including validity period, key size, and intended usage.</p>
        <div class="toolbar">
          <Button label="Create Template" icon="pi pi-plus" size="small" @click="createTemplVisible = true" />
          <div class="toolbar-spacer" />
        </div>

        <div v-if="templatesLoading" style="text-align: center; padding: 2rem"><ProgressSpinner /></div>
        <div v-else class="card" style="padding: 0">
          <DataTable :value="templates" stripedRows size="small" scrollable scrollHeight="calc(100vh - 340px)"
                     :paginator="templates.length > 50" :rows="50">
            <Column field="name" header="Name" sortable style="width: 180px" />
            <Column field="displayName" header="Display Name" sortable style="min-width: 200px" />
            <Column header="Validity" sortable sortField="validityPeriodDays" style="width: 120px">
              <template #body="{ data }">{{ data.validityPeriodDays }} days</template>
            </Column>
            <Column header="Purpose" style="min-width: 200px">
              <template #body="{ data }">
                <span style="font-size: 0.85em">{{ templatePurpose(data) }}</span>
              </template>
            </Column>
            <Column header="Key Size" sortable sortField="minimumKeySize" style="width: 100px">
              <template #body="{ data }">{{ data.minimumKeySize }}</template>
            </Column>
            <Column header="Auto-Enroll" style="width: 120px">
              <template #body="{ data }">
                <Tag v-if="data.autoEnroll" value="Yes" severity="success" />
                <span v-else style="color: var(--p-text-muted-color)">No</span>
              </template>
            </Column>
            <Column header="Modified" sortable sortField="whenChanged" style="width: 130px">
              <template #body="{ data }">
                <span style="color: var(--p-text-muted-color)">{{ relativeTime(data.whenChanged) }}</span>
              </template>
            </Column>
            <Column style="width: 100px">
              <template #body="{ data }">
                <Button icon="pi pi-pencil" size="small" severity="secondary" text @click="openEditTemplate(data)" />
                <Button icon="pi pi-trash" size="small" severity="danger" text @click="onDeleteTemplate(data)" />
              </template>
            </Column>
            <template #empty>
              <div style="text-align: center; padding: 2rem; color: var(--p-text-muted-color)">No certificate templates found.<br><span style="font-size: 0.8125rem">Create a template to define the rules and constraints for certificates issued by this CA.</span></div>
            </template>
          </DataTable>
        </div>
      </TabPanel>

      <!-- Enrolled Certificates Tab -->
      <TabPanel header="Enrolled Certificates">
        <p style="font-size: 0.8125rem; color: var(--p-text-muted-color); margin: 0 0 0.75rem 0">Certificates that have been issued by the Certificate Authority. You can renew or revoke active certificates from here.</p>
        <div class="toolbar">
          <Button label="Request Certificate" icon="pi pi-plus" size="small" @click="enrollVisible = true"
                  :disabled="!caInfo?.isInitialized" />
          <div class="toolbar-spacer" />
          <Button icon="pi pi-refresh" size="small" severity="secondary" text @click="loadEnrolled" />
        </div>

        <div v-if="!caInfo?.isInitialized" style="text-align: center; padding: 2rem; color: var(--p-text-muted-color)">
          <p>Certificate Authority must be initialized before issuing certificates.</p>
        </div>
        <div v-else-if="enrolledLoading" style="text-align: center; padding: 2rem"><ProgressSpinner /></div>
        <div v-else class="card" style="padding: 0">
          <DataTable :value="enrolled" stripedRows size="small" scrollable scrollHeight="calc(100vh - 340px)"
                     :paginator="enrolled.length > 50" :rows="50">
            <Column field="subject" header="Subject" sortable style="min-width: 250px" />
            <Column field="templateName" header="Template" sortable style="width: 180px" />
            <Column header="Issued" sortable sortField="issuedDate" style="width: 150px">
              <template #body="{ data }">
                {{ new Date(data.issuedDate).toLocaleDateString() }}
              </template>
            </Column>
            <Column header="Expires" sortable sortField="expiryDate" style="width: 150px">
              <template #body="{ data }">
                {{ new Date(data.expiryDate).toLocaleDateString() }}
              </template>
            </Column>
            <Column header="Status" sortable sortField="status" style="width: 110px">
              <template #body="{ data }">
                <Tag :value="data.status" :severity="certStatusSeverity(data.status)" />
              </template>
            </Column>
            <Column header="Serial" style="width: 180px">
              <template #body="{ data }">
                <span style="font-family: monospace; font-size: 0.8em; color: var(--p-text-muted-color)">{{ data.serialNumber }}</span>
              </template>
            </Column>
            <Column style="width: 160px">
              <template #body="{ data }">
                <Button v-if="data.status === 'Active'" label="Renew" size="small" severity="secondary" text @click="onRenew(data)" />
                <Button v-if="data.status === 'Active'" label="Revoke" size="small" severity="danger" text @click="openRevokeDialog(data)" />
              </template>
            </Column>
            <template #empty>
              <div style="text-align: center; padding: 2rem; color: var(--p-text-muted-color)">No enrolled certificates.<br><span style="font-size: 0.8125rem">Request a certificate using a template to issue one from the Certificate Authority.</span></div>
            </template>
          </DataTable>
        </div>
      </TabPanel>
    </TabView>

    <!-- Initialize CA Dialog -->
    <Dialog v-model:visible="initCaVisible" header="Initialize Certificate Authority" modal :style="{ width: '600px' }">
      <div style="display: flex; flex-direction: column; gap: 1rem; padding: 0.5rem 0">
        <div style="background: var(--p-surface-100); padding: 1rem; border-radius: 6px; font-size: 0.875rem; color: var(--p-text-muted-color)">
          This will generate a root CA key pair and self-signed certificate. The CA private key will be securely stored in the directory.
        </div>
        <div>
          <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">CA Common Name</label>
          <InputText v-model="initCaName" placeholder="Directory.NET Certificate Authority" style="width: 100%" size="small" />
        </div>
        <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 1rem">
          <div>
            <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">Organization</label>
            <InputText v-model="initCaOrg" placeholder="e.g. Contoso" style="width: 100%" size="small" />
          </div>
          <div>
            <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">Country</label>
            <InputText v-model="initCaCountry" placeholder="e.g. US" style="width: 100%" size="small" maxlength="2" />
          </div>
        </div>
        <div style="display: grid; grid-template-columns: 1fr 1fr 1fr; gap: 1rem">
          <div>
            <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">Validity (years)</label>
            <InputNumber v-model="initCaValidity" :min="1" :max="30" size="small" style="width: 100%" />
          </div>
          <div>
            <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">Key Size</label>
            <Select v-model="initCaKeySize" :options="keySizeOptions"
                    optionLabel="label" optionValue="value" size="small" style="width: 100%" />
          </div>
          <div>
            <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">Hash Algorithm</label>
            <Select v-model="initCaHash" :options="hashAlgorithms"
                    optionLabel="label" optionValue="value" size="small" style="width: 100%" />
          </div>
        </div>
      </div>
      <template #footer>
        <Button label="Cancel" severity="secondary" text @click="initCaVisible = false" />
        <Button label="Initialize CA" icon="pi pi-shield" @click="onInitializeCa" :loading="initCaLoading" />
      </template>
    </Dialog>

    <!-- Create Template Dialog -->
    <Dialog v-model:visible="createTemplVisible" header="Create Certificate Template" modal :style="{ width: '550px' }">
      <div style="display: flex; flex-direction: column; gap: 1rem; padding: 0.5rem 0">
        <div>
          <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">Template Name (CN)</label>
          <InputText v-model="newTplName" placeholder="e.g. WebServer" style="width: 100%" size="small" />
        </div>
        <div>
          <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">Display Name</label>
          <InputText v-model="newTplDisplayName" placeholder="Web Server Certificate" style="width: 100%" size="small" />
        </div>
        <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 1rem">
          <div>
            <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">Validity (days)</label>
            <InputNumber v-model="newTplValidity" :min="1" :max="36500" size="small" style="width: 100%" />
          </div>
          <div>
            <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">Renewal Period (days)</label>
            <InputNumber v-model="newTplRenewal" :min="1" :max="365" size="small" style="width: 100%" />
          </div>
        </div>
        <div>
          <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">Minimum Key Size</label>
          <Select v-model="newTplKeySize" :options="[
            { label: '1024', value: 1024 },
            { label: '2048', value: 2048 },
            { label: '4096', value: 4096 },
          ]" optionLabel="label" optionValue="value" size="small" style="width: 200px" />
        </div>
        <div style="display: flex; gap: 2rem">
          <div style="display: flex; align-items: center; gap: 0.5rem">
            <Checkbox v-model="newTplAutoEnroll" :binary="true" />
            <label>Auto-Enroll</label>
          </div>
          <div style="display: flex; align-items: center; gap: 0.5rem">
            <Checkbox v-model="newTplRequireApproval" :binary="true" />
            <label>Require Approval</label>
          </div>
        </div>
      </div>
      <template #footer>
        <Button label="Cancel" severity="secondary" text @click="createTemplVisible = false" />
        <Button label="Create" icon="pi pi-check" @click="onCreateTemplate" :loading="creating" :disabled="!newTplName.trim()" />
      </template>
    </Dialog>

    <!-- Edit Template Dialog -->
    <Dialog v-model:visible="editTemplVisible" :header="editTpl ? `Edit: ${editTpl.displayName}` : 'Edit Template'"
            modal :style="{ width: '700px', maxHeight: '85vh' }">
      <template v-if="editTpl">
        <TabView>
          <TabPanel header="General">
            <div style="display: flex; flex-direction: column; gap: 1rem">
              <div>
                <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">Display Name</label>
                <InputText v-model="editTplDisplayName" size="small" style="width: 100%" />
              </div>
              <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 1rem">
                <div>
                  <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">Validity (days)</label>
                  <InputNumber v-model="editTplValidity" :min="1" :max="36500" size="small" style="width: 100%" />
                </div>
                <div>
                  <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">Renewal Period (days)</label>
                  <InputNumber v-model="editTplRenewal" :min="1" :max="365" size="small" style="width: 100%" />
                </div>
              </div>
            </div>
          </TabPanel>

          <TabPanel header="Request Handling">
            <div style="display: flex; flex-direction: column; gap: 1rem">
              <div>
                <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">Key Usage</label>
                <span style="color: var(--p-text-muted-color); font-size: 0.875rem">{{ keyUsageLabel(editTpl.keyUsage) }}</span>
              </div>
              <div>
                <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">Minimum Key Size</label>
                <Select v-model="editTplKeySize" :options="[
                  { label: '1024', value: 1024 },
                  { label: '2048', value: 2048 },
                  { label: '4096', value: 4096 },
                ]" optionLabel="label" optionValue="value" size="small" style="width: 200px" />
              </div>
              <div style="display: flex; gap: 2rem">
                <div style="display: flex; align-items: center; gap: 0.5rem">
                  <Checkbox v-model="editTplRequireApproval" :binary="true" />
                  <label>Require CA Manager Approval</label>
                </div>
                <div style="display: flex; align-items: center; gap: 0.5rem">
                  <Checkbox v-model="editTplPublishToDs" :binary="true" />
                  <label>Publish to Active Directory</label>
                </div>
              </div>
            </div>
          </TabPanel>

          <TabPanel header="Extensions">
            <div style="display: flex; flex-direction: column; gap: 1rem">
              <div>
                <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">Key Usage</label>
                <span>{{ keyUsageLabel(editTpl.keyUsage) }}</span>
              </div>
              <div>
                <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">Enhanced Key Usage (EKU)</label>
                <div v-for="eku in editTpl.enhancedKeyUsage" :key="eku"
                     style="padding: 0.25rem 0; font-size: 0.875rem">
                  {{ ekuOptions.find(o => o.value === eku)?.label || eku }}
                </div>
                <span v-if="editTpl.enhancedKeyUsage.length === 0" style="color: var(--p-text-muted-color)">
                  All purposes (no EKU restriction)
                </span>
              </div>
              <div style="display: flex; align-items: center; gap: 0.5rem">
                <Checkbox v-model="editTplAutoEnroll" :binary="true" />
                <label>Enable Auto-Enrollment</label>
              </div>
            </div>
          </TabPanel>

          <TabPanel header="Security">
            <p style="color: var(--p-text-muted-color); font-size: 0.875rem; margin-top: 0">
              Define which principals can enroll, auto-enroll, or manage this template.
            </p>
            <div style="display: flex; gap: 0.5rem; margin-bottom: 1rem; align-items: flex-end">
              <DnPicker v-model="editPermPrincipalDn" label="Add Principal"
                        objectFilter="(|(objectClass=user)(objectClass=group))"
                        style="width: 350px" />
              <Button label="Add" icon="pi pi-plus" size="small" @click="addPermission" :disabled="!editPermPrincipalDn" />
            </div>
            <DataTable :value="editTplPermissions" stripedRows size="small">
              <Column header="Principal" field="principalDn" style="min-width: 250px">
                <template #body="{ data }">
                  <span style="font-size: 0.875rem">{{ data.principalName || data.principalDn }}</span>
                </template>
              </Column>
              <Column header="Enroll" style="width: 80px">
                <template #body="{ data }">
                  <Checkbox v-model="data.canEnroll" :binary="true" />
                </template>
              </Column>
              <Column header="Auto-Enroll" style="width: 110px">
                <template #body="{ data }">
                  <Checkbox v-model="data.canAutoEnroll" :binary="true" />
                </template>
              </Column>
              <Column header="Manage" style="width: 80px">
                <template #body="{ data }">
                  <Checkbox v-model="data.canManage" :binary="true" />
                </template>
              </Column>
              <Column style="width: 60px">
                <template #body="{ index }">
                  <Button icon="pi pi-times" size="small" severity="danger" text
                          @click="editTplPermissions.splice(index, 1)" />
                </template>
              </Column>
              <template #empty>
                <div style="text-align: center; padding: 1.5rem; color: var(--p-text-muted-color)">No enrollment permissions configured</div>
              </template>
            </DataTable>
          </TabPanel>
        </TabView>
      </template>
      <template #footer>
        <Button label="Cancel" severity="secondary" text @click="editTemplVisible = false" />
        <Button label="Save" icon="pi pi-check" @click="onSaveTemplate" :loading="savingTpl" />
      </template>
    </Dialog>

    <!-- Enroll Dialog -->
    <Dialog v-model:visible="enrollVisible" header="Request Certificate" modal :style="{ width: '600px' }">
      <div style="display: flex; flex-direction: column; gap: 1rem; padding: 0.5rem 0">
        <div>
          <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">Certificate Template</label>
          <Select v-model="enrollTemplate"
                  :options="templates.map(t => ({ label: t.displayName, value: t.name }))"
                  optionLabel="label" optionValue="value"
                  placeholder="Select a template" size="small" style="width: 100%" />
        </div>
        <div>
          <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">Subject DN</label>
          <InputText v-model="enrollSubject" placeholder="CN=webapp.corp.example.com" size="small" style="width: 100%" />
        </div>
        <div>
          <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">Subject Alternative Names</label>
          <div style="display: flex; gap: 0.5rem; margin-bottom: 0.5rem">
            <Select v-model="enrollSanType" :options="sanTypeOptions"
                    optionLabel="label" optionValue="value" size="small" style="width: 130px" />
            <InputText v-model="enrollSanInput" placeholder="e.g. webapp.example.com" size="small" style="flex: 1"
                       @keyup.enter="addSanEntry" />
            <Button icon="pi pi-plus" size="small" @click="addSanEntry" :disabled="!enrollSanInput" />
          </div>
          <div v-for="(san, idx) in enrollSanEntries" :key="idx"
               style="display: flex; align-items: center; gap: 0.5rem; padding: 0.25rem 0.5rem; margin-bottom: 0.25rem; background: var(--p-surface-100); border-radius: 4px; font-size: 0.875rem">
            <Tag :value="san.split(':')[0].toUpperCase()" severity="info" style="font-size: 0.75rem" />
            <span style="flex: 1">{{ san.split(':').slice(1).join(':') }}</span>
            <Button icon="pi pi-times" size="small" severity="danger" text @click="enrollSanEntries.splice(idx, 1)" />
          </div>
        </div>
        <div>
          <div style="display: flex; align-items: center; gap: 0.5rem; margin-bottom: 0.5rem">
            <Checkbox v-model="enrollUseCsr" :binary="true" />
            <label>Provide CSR (PKCS#10) instead of server-generated key pair</label>
          </div>
          <Textarea v-if="enrollUseCsr" v-model="enrollCsr" rows="6"
                    placeholder="Paste PEM-encoded CSR here..." style="width: 100%; font-family: monospace; font-size: 0.8rem" />
        </div>
      </div>
      <template #footer>
        <Button label="Cancel" severity="secondary" text @click="enrollVisible = false" />
        <Button label="Submit Request" icon="pi pi-check" @click="onEnroll" :loading="enrolling"
                :disabled="!enrollTemplate || !enrollSubject" />
      </template>
    </Dialog>

    <!-- Certificate Detail Dialog -->
    <Dialog v-model:visible="certDetailVisible" header="Issued Certificate" modal :style="{ width: '650px' }">
      <template v-if="certDetail">
        <div style="display: flex; gap: 0.5rem; margin-bottom: 1.5rem">
          <Button label="Download Certificate" icon="pi pi-download" size="small" @click="downloadCert" />
          <Button v-if="certDetail.privateKeyPem" label="Download Private Key" icon="pi pi-key" size="small"
                  severity="warn" @click="downloadPrivateKey" />
        </div>
        <div v-if="certDetail.privateKeyPem"
             style="background: var(--app-warn-bg); color: var(--app-warn-text-strong); padding: 0.75rem 1rem; border-radius: 6px; margin-bottom: 1rem; font-size: 0.875rem">
          <strong>Important:</strong> Save the private key now. It will not be available again after you close this dialog.
        </div>
        <div style="display: grid; grid-template-columns: 180px 1fr; gap: 0.75rem; font-size: 0.9375rem">
          <div style="font-weight: 600; color: var(--p-text-muted-color)">Subject</div>
          <div style="font-family: monospace; font-size: 0.875rem">{{ certDetail.subject }}</div>
          <div style="font-weight: 600; color: var(--p-text-muted-color)">Issuer</div>
          <div style="font-family: monospace; font-size: 0.875rem">{{ certDetail.issuer }}</div>
          <div style="font-weight: 600; color: var(--p-text-muted-color)">Serial Number</div>
          <div style="font-family: monospace; font-size: 0.875rem">{{ certDetail.serialNumber }}</div>
          <div style="font-weight: 600; color: var(--p-text-muted-color)">Thumbprint</div>
          <div style="font-family: monospace; font-size: 0.875rem">{{ certDetail.thumbprint }}</div>
          <div style="font-weight: 600; color: var(--p-text-muted-color)">Template</div>
          <div>{{ certDetail.templateName }}</div>
          <div style="font-weight: 600; color: var(--p-text-muted-color)">Valid From</div>
          <div>{{ new Date(certDetail.notBefore).toLocaleString() }}</div>
          <div style="font-weight: 600; color: var(--p-text-muted-color)">Valid Until</div>
          <div>{{ new Date(certDetail.notAfter).toLocaleString() }}</div>
          <div style="font-weight: 600; color: var(--p-text-muted-color)">Status</div>
          <div><Tag :value="certDetail.status" :severity="certStatusSeverity(certDetail.status)" /></div>
          <template v-if="certDetail.subjectAlternativeNames.length > 0">
            <div style="font-weight: 600; color: var(--p-text-muted-color)">SANs</div>
            <div>
              <Tag v-for="san in certDetail.subjectAlternativeNames" :key="san" :value="san" severity="info"
                   style="margin-right: 0.25rem; margin-bottom: 0.25rem" />
            </div>
          </template>
          <div style="font-weight: 600; color: var(--p-text-muted-color)">Key Usage</div>
          <div style="font-size: 0.875rem">{{ certDetail.keyUsage }}</div>
          <template v-if="certDetail.enhancedKeyUsage.length > 0">
            <div style="font-weight: 600; color: var(--p-text-muted-color)">Enhanced Key Usage</div>
            <div style="font-size: 0.875rem">
              <div v-for="eku in certDetail.enhancedKeyUsage" :key="eku">
                {{ ekuOptions.find(o => o.value === eku)?.label || eku }}
              </div>
            </div>
          </template>
        </div>
      </template>
      <template #footer>
        <Button label="Close" severity="secondary" @click="certDetailVisible = false" />
      </template>
    </Dialog>

    <!-- Revoke Dialog -->
    <Dialog v-model:visible="revokeVisible" header="Revoke Certificate" modal :style="{ width: '450px' }">
      <div style="display: flex; flex-direction: column; gap: 1rem; padding: 0.5rem 0">
        <p style="margin: 0">
          Are you sure you want to revoke certificate<br>
          <strong style="font-family: monospace">{{ revokeSerial }}</strong><br>
          <span style="color: var(--p-text-muted-color)">{{ revokeSubject }}</span>
        </p>
        <div>
          <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">Revocation Reason</label>
          <Select v-model="revokeReason" :options="revocationReasons"
                  optionLabel="label" optionValue="value" size="small" style="width: 100%" />
        </div>
      </div>
      <template #footer>
        <Button label="Cancel" severity="secondary" text @click="revokeVisible = false" />
        <Button label="Revoke Certificate" icon="pi pi-ban" severity="danger" @click="onRevoke" :loading="revoking" />
      </template>
    </Dialog>
  </div>
</template>
