<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import InputNumber from 'primevue/inputnumber'
import Select from 'primevue/select'
import Textarea from 'primevue/textarea'
import Button from 'primevue/button'
import { useToast } from 'primevue/usetoast'
import { createRecord, updateRecord } from '../api/dns'
import type { DnsRecord, DnsRecordType } from '../types/dns'
import { DNS_RECORD_TYPES } from '../types/dns'

const props = defineProps<{
  visible: boolean
  zoneName: string
  record: DnsRecord | null
}>()

const emit = defineEmits<{
  'update:visible': [val: boolean]
  saved: []
}>()

const toast = useToast()
const saving = ref(false)

// Form fields
const recordType = ref<DnsRecordType>('A')
const name = ref('')
const ttl = ref(600)

// Type-specific fields
const ipv4Address = ref('')
const ipv6Address = ref('')
const cnameTarget = ref('')
const mxServer = ref('')
const mxPriority = ref(10)
const srvService = ref('')
const srvProtocol = ref('_tcp')
const srvTarget = ref('')
const srvPort = ref(0)
const srvPriority = ref(0)
const srvWeight = ref(100)
const ptrHostname = ref('')
const nsServer = ref('')
const txtValue = ref('')
const soaPrimaryServer = ref('')
const soaAdminEmail = ref('')
const soaSerial = ref(1)
const soaRefresh = ref(900)
const soaRetry = ref(600)
const soaExpire = ref(86400)
const soaMinTtl = ref(60)

const isEdit = computed(() => !!props.record)
const dialogTitle = computed(() => isEdit.value ? 'Edit DNS Record' : 'New DNS Record')

const typeOptions = DNS_RECORD_TYPES.map(t => ({ label: t, value: t }))

watch(() => props.visible, (v) => {
  if (v) {
    if (props.record) {
      recordType.value = props.record.type as DnsRecordType
      name.value = props.record.name
      ttl.value = props.record.ttl
      populateFieldsFromData(props.record.type, props.record.data)
    } else {
      resetForm()
    }
  }
})

function resetForm() {
  recordType.value = 'A'
  name.value = ''
  ttl.value = 600
  ipv4Address.value = ''
  ipv6Address.value = ''
  cnameTarget.value = ''
  mxServer.value = ''
  mxPriority.value = 10
  srvService.value = ''
  srvProtocol.value = '_tcp'
  srvTarget.value = ''
  srvPort.value = 0
  srvPriority.value = 0
  srvWeight.value = 100
  ptrHostname.value = ''
  nsServer.value = ''
  txtValue.value = ''
  soaPrimaryServer.value = ''
  soaAdminEmail.value = ''
  soaSerial.value = 1
  soaRefresh.value = 900
  soaRetry.value = 600
  soaExpire.value = 86400
  soaMinTtl.value = 60
}

function populateFieldsFromData(type: string, data: string) {
  switch (type) {
    case 'A': ipv4Address.value = data; break
    case 'AAAA': ipv6Address.value = data; break
    case 'CNAME': cnameTarget.value = data; break
    case 'MX': {
      const parts = data.split(' ')
      mxPriority.value = parseInt(parts[0]) || 10
      mxServer.value = parts.slice(1).join(' ') || data
      break
    }
    case 'SRV': {
      const parts = data.split(' ')
      srvPriority.value = parseInt(parts[0]) || 0
      srvWeight.value = parseInt(parts[1]) || 100
      srvPort.value = parseInt(parts[2]) || 0
      srvTarget.value = parts.slice(3).join(' ') || data
      break
    }
    case 'PTR': ptrHostname.value = data; break
    case 'NS': nsServer.value = data; break
    case 'TXT': txtValue.value = data; break
    case 'SOA': {
      const parts = data.split(' ')
      soaPrimaryServer.value = parts[0] || ''
      soaAdminEmail.value = parts[1] || ''
      soaSerial.value = parseInt(parts[2]) || 1
      soaRefresh.value = parseInt(parts[3]) || 900
      soaRetry.value = parseInt(parts[4]) || 600
      soaExpire.value = parseInt(parts[5]) || 86400
      soaMinTtl.value = parseInt(parts[6]) || 60
      break
    }
  }
}

function buildData(): string {
  switch (recordType.value) {
    case 'A': return ipv4Address.value
    case 'AAAA': return ipv6Address.value
    case 'CNAME': return cnameTarget.value
    case 'MX': return `${mxPriority.value} ${mxServer.value}`
    case 'SRV': return `${srvPriority.value} ${srvWeight.value} ${srvPort.value} ${srvTarget.value}`
    case 'PTR': return ptrHostname.value
    case 'NS': return nsServer.value
    case 'TXT': return txtValue.value
    case 'SOA': return `${soaPrimaryServer.value} ${soaAdminEmail.value} ${soaSerial.value} ${soaRefresh.value} ${soaRetry.value} ${soaExpire.value} ${soaMinTtl.value}`
    default: return ''
  }
}

// Validation
const ipv4Pattern = /^(\d{1,3}\.){3}\d{1,3}$/
const ipv6Pattern = /^([0-9a-fA-F]{0,4}:){2,7}[0-9a-fA-F]{0,4}$/
const fqdnPattern = /^[a-zA-Z0-9]([a-zA-Z0-9-]*[a-zA-Z0-9])?(\.[a-zA-Z0-9]([a-zA-Z0-9-]*[a-zA-Z0-9])?)*\.?$/

const validationErrors = computed(() => {
  const errors: string[] = []
  if (!name.value.trim() && recordType.value !== 'SOA') errors.push('Name is required')
  if (ttl.value < 0 || ttl.value > 2147483647) errors.push('TTL must be between 0 and 2147483647')

  switch (recordType.value) {
    case 'A':
      if (!ipv4Pattern.test(ipv4Address.value)) errors.push('Invalid IPv4 address format')
      else {
        const octets = ipv4Address.value.split('.').map(Number)
        if (octets.some(o => o > 255)) errors.push('IPv4 octet must be 0-255')
      }
      break
    case 'AAAA':
      if (!ipv6Pattern.test(ipv6Address.value) && ipv6Address.value !== '::1' && ipv6Address.value !== '::')
        errors.push('Invalid IPv6 address format')
      break
    case 'CNAME':
      if (!cnameTarget.value.trim()) errors.push('Target FQDN is required')
      break
    case 'MX':
      if (!mxServer.value.trim()) errors.push('Mail server is required')
      if (mxPriority.value < 0 || mxPriority.value > 65535) errors.push('Priority must be 0-65535')
      break
    case 'SRV':
      if (!srvTarget.value.trim()) errors.push('Target is required')
      if (srvPort.value < 0 || srvPort.value > 65535) errors.push('Port must be 0-65535')
      if (srvPriority.value < 0 || srvPriority.value > 65535) errors.push('Priority must be 0-65535')
      if (srvWeight.value < 0 || srvWeight.value > 65535) errors.push('Weight must be 0-65535')
      break
    case 'PTR':
      if (!ptrHostname.value.trim()) errors.push('Host name is required')
      break
    case 'NS':
      if (!nsServer.value.trim()) errors.push('Name server is required')
      break
    case 'TXT':
      if (!txtValue.value.trim()) errors.push('Text value is required')
      break
    case 'SOA':
      if (!soaPrimaryServer.value.trim()) errors.push('Primary server is required')
      if (!soaAdminEmail.value.trim()) errors.push('Admin email is required')
      break
  }
  return errors
})

const canSave = computed(() => validationErrors.value.length === 0)

async function onSave() {
  if (!canSave.value) return
  saving.value = true
  try {
    const data = buildData()
    const recordName = recordType.value === 'SRV'
      ? `${srvService.value}.${srvProtocol.value}.${name.value || props.zoneName}`
      : name.value || props.zoneName

    if (isEdit.value && props.record) {
      await updateRecord(props.zoneName, props.record.id, {
        name: recordName,
        type: recordType.value,
        data,
        ttl: ttl.value,
      })
      toast.add({ severity: 'success', summary: 'Updated', detail: 'Record updated', life: 3000 })
    } else {
      await createRecord(props.zoneName, {
        name: recordName,
        type: recordType.value,
        data,
        ttl: ttl.value,
      })
      toast.add({ severity: 'success', summary: 'Created', detail: 'Record created', life: 3000 })
    }
    emit('saved')
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
    :header="dialogTitle"
    modal
    :style="{ width: '36rem' }"
    :closable="true"
  >
    <div class="dns-record-form">
      <!-- Record Type -->
      <div class="form-field">
        <label>Record Type</label>
        <Select
          v-model="recordType"
          :options="typeOptions"
          optionLabel="label"
          optionValue="value"
          :disabled="isEdit"
          style="width: 100%"
        />
      </div>

      <!-- Name (common to most) -->
      <div class="form-field" v-if="recordType !== 'SOA'">
        <label>Name</label>
        <InputText v-model="name" placeholder="e.g. www, mail, @" style="width: 100%" />
        <small class="text-muted">Leave empty for zone apex ({{ zoneName }})</small>
      </div>

      <!-- TTL -->
      <div class="form-field">
        <label>TTL (seconds)</label>
        <InputNumber v-model="ttl" :min="0" :max="2147483647" style="width: 100%" />
      </div>

      <!-- A Record Fields -->
      <div v-if="recordType === 'A'" class="form-field">
        <label>IPv4 Address</label>
        <InputText v-model="ipv4Address" placeholder="e.g. 192.168.1.10" style="width: 100%" />
      </div>

      <!-- AAAA Record Fields -->
      <div v-if="recordType === 'AAAA'" class="form-field">
        <label>IPv6 Address</label>
        <InputText v-model="ipv6Address" placeholder="e.g. 2001:db8::1" style="width: 100%" />
      </div>

      <!-- CNAME Record Fields -->
      <div v-if="recordType === 'CNAME'" class="form-field">
        <label>Target FQDN</label>
        <InputText v-model="cnameTarget" placeholder="e.g. www.example.com" style="width: 100%" />
      </div>

      <!-- MX Record Fields -->
      <template v-if="recordType === 'MX'">
        <div class="form-field">
          <label>Mail Server FQDN</label>
          <InputText v-model="mxServer" placeholder="e.g. mail.example.com" style="width: 100%" />
        </div>
        <div class="form-field">
          <label>Priority</label>
          <InputNumber v-model="mxPriority" :min="0" :max="65535" style="width: 100%" />
        </div>
      </template>

      <!-- SRV Record Fields -->
      <template v-if="recordType === 'SRV'">
        <div class="form-row">
          <div class="form-field" style="flex: 1">
            <label>Service</label>
            <InputText v-model="srvService" placeholder="e.g. _ldap" style="width: 100%" />
          </div>
          <div class="form-field" style="flex: 1">
            <label>Protocol</label>
            <Select v-model="srvProtocol" :options="[{label: 'TCP', value: '_tcp'}, {label: 'UDP', value: '_udp'}]" optionLabel="label" optionValue="value" style="width: 100%" />
          </div>
        </div>
        <div class="form-field">
          <label>Target</label>
          <InputText v-model="srvTarget" placeholder="e.g. server.example.com" style="width: 100%" />
        </div>
        <div class="form-row">
          <div class="form-field" style="flex: 1">
            <label>Port</label>
            <InputNumber v-model="srvPort" :min="0" :max="65535" style="width: 100%" />
          </div>
          <div class="form-field" style="flex: 1">
            <label>Priority</label>
            <InputNumber v-model="srvPriority" :min="0" :max="65535" style="width: 100%" />
          </div>
          <div class="form-field" style="flex: 1">
            <label>Weight</label>
            <InputNumber v-model="srvWeight" :min="0" :max="65535" style="width: 100%" />
          </div>
        </div>
      </template>

      <!-- PTR Record Fields -->
      <div v-if="recordType === 'PTR'" class="form-field">
        <label>Host Name</label>
        <InputText v-model="ptrHostname" placeholder="e.g. server.example.com" style="width: 100%" />
      </div>

      <!-- NS Record Fields -->
      <div v-if="recordType === 'NS'" class="form-field">
        <label>Name Server FQDN</label>
        <InputText v-model="nsServer" placeholder="e.g. ns1.example.com" style="width: 100%" />
      </div>

      <!-- TXT Record Fields -->
      <div v-if="recordType === 'TXT'" class="form-field">
        <label>Text Value</label>
        <Textarea v-model="txtValue" rows="3" placeholder="e.g. v=spf1 include:example.com ~all" style="width: 100%" />
      </div>

      <!-- SOA Record Fields -->
      <template v-if="recordType === 'SOA'">
        <div class="form-field">
          <label>Primary Server</label>
          <InputText v-model="soaPrimaryServer" style="width: 100%" />
        </div>
        <div class="form-field">
          <label>Admin Email</label>
          <InputText v-model="soaAdminEmail" placeholder="e.g. hostmaster.example.com" style="width: 100%" />
        </div>
        <div class="form-row">
          <div class="form-field" style="flex: 1">
            <label>Serial</label>
            <InputNumber v-model="soaSerial" :min="0" style="width: 100%" />
          </div>
          <div class="form-field" style="flex: 1">
            <label>Refresh</label>
            <InputNumber v-model="soaRefresh" :min="0" style="width: 100%" />
          </div>
        </div>
        <div class="form-row">
          <div class="form-field" style="flex: 1">
            <label>Retry</label>
            <InputNumber v-model="soaRetry" :min="0" style="width: 100%" />
          </div>
          <div class="form-field" style="flex: 1">
            <label>Expire</label>
            <InputNumber v-model="soaExpire" :min="0" style="width: 100%" />
          </div>
        </div>
        <div class="form-field">
          <label>Minimum TTL</label>
          <InputNumber v-model="soaMinTtl" :min="0" style="width: 100%" />
        </div>
      </template>

      <!-- Validation Errors -->
      <div v-if="validationErrors.length > 0" style="margin-top: 0.5rem">
        <div v-for="err in validationErrors" :key="err" style="color: var(--p-red-500); font-size: 0.8125rem; margin-bottom: 0.25rem">
          {{ err }}
        </div>
      </div>
    </div>

    <template #footer>
      <Button label="Cancel" severity="secondary" @click="emit('update:visible', false)" />
      <Button :label="isEdit ? 'Update' : 'Create'" icon="pi pi-check" :loading="saving" :disabled="!canSave" @click="onSave" />
    </template>
  </Dialog>
</template>

<style scoped>
.dns-record-form {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.form-field {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.form-field label {
  font-size: 0.8125rem;
  font-weight: 600;
  color: var(--p-text-color);
}

.form-row {
  display: flex;
  gap: 0.75rem;
}

.text-muted {
  color: var(--p-text-muted-color);
  font-size: 0.75rem;
}
</style>
