<script setup lang="ts">
import { ref, onMounted, computed, watch } from 'vue'
import Dialog from 'primevue/dialog'
import Tabs from 'primevue/tabs'
import TabList from 'primevue/tablist'
import Tab from 'primevue/tab'
import TabPanels from 'primevue/tabpanels'
import TabPanel from 'primevue/tabpanel'
import InputText from 'primevue/inputtext'
import Textarea from 'primevue/textarea'
import Checkbox from 'primevue/checkbox'
import RadioButton from 'primevue/radiobutton'
import Select from 'primevue/select'
import DatePicker from 'primevue/datepicker'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import Tag from 'primevue/tag'
import ProgressSpinner from 'primevue/progressspinner'
import { useToast } from 'primevue/usetoast'
import { getObject, updateObject } from '../api/objects'
import { getDirectReports, updateDelegation, updateLogonHours, updateLogonWorkstations } from '../api/users'
import type { ObjectDetail, ObjectSummary } from '../api/types'
import { cnFromDn, formatFileTime } from '../utils/format'
import MultiValueEditor from './MultiValueEditor.vue'
import DnPicker from './DnPicker.vue'
import SecurityTab from './SecurityTab.vue'
import AttributeEditor from './AttributeEditor.vue'

const props = defineProps<{
  objectGuid: string
  visible: boolean
}>()

const emit = defineEmits<{
  'update:visible': [val: boolean]
}>()

const toast = useToast()
const obj = ref<ObjectDetail | null>(null)
const loading = ref(true)
const saving = ref(false)

// Security tab is now handled by SecurityTab component

// ── General tab fields ──
const cn = ref('')
const displayName = ref('')
const description = ref('')
const givenName = ref('')
const sn = ref('')
const mail = ref('')
const samAccountName = ref('')
const userPrincipalName = ref('')

// ── Address tab fields ──
const streetAddress = ref('')
const postOfficeBox = ref('')
const city = ref('')
const state = ref('')
const postalCode = ref('')
const country = ref('')

// ── Telephones tab fields ──
const telephoneNumber = ref('')
const homePhone = ref('')
const mobile = ref('')
const fax = ref('')
const ipPhone = ref('')
const pager = ref('')
const info = ref('')
const otherTelephone = ref<string[]>([])
const otherHomePhone = ref<string[]>([])
const otherMobile = ref<string[]>([])
const otherFax = ref<string[]>([])
const otherIpPhone = ref<string[]>([])
const otherPager = ref<string[]>([])

// ── Organization tab fields ──
const title = ref('')
const department = ref('')
const company = ref('')
const manager = ref('')
const directReports = ref<ObjectSummary[]>([])

// ── Account tab fields ──
const uacDisabled = ref(false)
const uacPasswordNeverExpires = ref(false)
const uacSmartcardRequired = ref(false)
const accountExpiresDate = ref<Date | null>(null)
const accountExpiresNever = ref(true)
const servicePrincipalNames = ref<string[]>([])

// ── Delegation tab fields ──
const delegationType = ref('none')
const allowedDelegateServices = ref<string[]>([])

// ── Logon Hours / Workstations dialogs ──
const logonHoursDialogVisible = ref(false)
const logonHoursGrid = ref<boolean[][]>([]) // 7 days x 24 hours
const workstationsDialogVisible = ref(false)
const workstationsList = ref<string[]>([])

// ── Phone "Other" dialogs ──
const otherPhoneDialogVisible = ref(false)
const otherPhoneDialogField = ref<'telephone' | 'homePhone' | 'mobile' | 'fax' | 'ipPhone' | 'pager'>('telephone')

const isUser = computed(() => obj.value?.objectClass?.includes('user') ?? false)
const isComputer = computed(() => obj.value?.objectClass?.includes('computer') ?? false)
const isGroup = computed(() => obj.value?.objectClass?.includes('group') ?? false)
const isContact = computed(() => obj.value?.objectClass?.includes('contact') ?? false)
const showAddressTab = computed(() => isUser.value || isContact.value)
const showTelephonesTab = computed(() => isUser.value || isContact.value)
const showOrganizationTab = computed(() => isUser.value)
const showDelegationTab = computed(() => isComputer.value || (isUser.value && servicePrincipalNames.value.length > 0))

// ── Group info ──
const groupScopeLabel = computed(() => {
  if (!isGroup.value || !obj.value) return ''
  const gt = obj.value.groupType
  if (gt & 0x00000004) return 'Domain Local'
  if (gt & 0x00000002) return 'Global'
  if (gt & 0x00000008) return 'Universal'
  return 'Unknown'
})
const groupTypeLabel = computed(() => {
  if (!isGroup.value || !obj.value) return ''
  return (obj.value.groupType & 0x80000000) ? 'Security' : 'Distribution'
})

// Attribute list is now handled by the AttributeEditor component which provides
// syntax-aware formatting, filtering, and inline editing for all schema attributes.

// Country list (ISO 3166-1)
const countries = [
  { label: '(none)', value: '' },
  { label: 'Afghanistan', value: 'AF' }, { label: 'Albania', value: 'AL' }, { label: 'Algeria', value: 'DZ' },
  { label: 'Argentina', value: 'AR' }, { label: 'Australia', value: 'AU' }, { label: 'Austria', value: 'AT' },
  { label: 'Belgium', value: 'BE' }, { label: 'Brazil', value: 'BR' }, { label: 'Canada', value: 'CA' },
  { label: 'Chile', value: 'CL' }, { label: 'China', value: 'CN' }, { label: 'Colombia', value: 'CO' },
  { label: 'Czech Republic', value: 'CZ' }, { label: 'Denmark', value: 'DK' }, { label: 'Egypt', value: 'EG' },
  { label: 'Finland', value: 'FI' }, { label: 'France', value: 'FR' }, { label: 'Germany', value: 'DE' },
  { label: 'Greece', value: 'GR' }, { label: 'Hong Kong', value: 'HK' }, { label: 'Hungary', value: 'HU' },
  { label: 'Iceland', value: 'IS' }, { label: 'India', value: 'IN' }, { label: 'Indonesia', value: 'ID' },
  { label: 'Ireland', value: 'IE' }, { label: 'Israel', value: 'IL' }, { label: 'Italy', value: 'IT' },
  { label: 'Japan', value: 'JP' }, { label: 'Kenya', value: 'KE' }, { label: 'Korea, South', value: 'KR' },
  { label: 'Luxembourg', value: 'LU' }, { label: 'Malaysia', value: 'MY' }, { label: 'Mexico', value: 'MX' },
  { label: 'Netherlands', value: 'NL' }, { label: 'New Zealand', value: 'NZ' }, { label: 'Nigeria', value: 'NG' },
  { label: 'Norway', value: 'NO' }, { label: 'Pakistan', value: 'PK' }, { label: 'Peru', value: 'PE' },
  { label: 'Philippines', value: 'PH' }, { label: 'Poland', value: 'PL' }, { label: 'Portugal', value: 'PT' },
  { label: 'Romania', value: 'RO' }, { label: 'Russia', value: 'RU' }, { label: 'Saudi Arabia', value: 'SA' },
  { label: 'Singapore', value: 'SG' }, { label: 'South Africa', value: 'ZA' }, { label: 'Spain', value: 'ES' },
  { label: 'Sweden', value: 'SE' }, { label: 'Switzerland', value: 'CH' }, { label: 'Taiwan', value: 'TW' },
  { label: 'Thailand', value: 'TH' }, { label: 'Turkey', value: 'TR' }, { label: 'Ukraine', value: 'UA' },
  { label: 'United Arab Emirates', value: 'AE' }, { label: 'United Kingdom', value: 'GB' },
  { label: 'United States', value: 'US' }, { label: 'Vietnam', value: 'VN' },
]

const dayLabels = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat']

watch(() => props.visible, (v) => {
  if (v && props.objectGuid) loadObject()
})

onMounted(() => {
  if (props.visible && props.objectGuid) loadObject()
})

function getAttrVal(name: string): string {
  return obj.value?.attributes?.[name]?.[0] || ''
}

function getAttrVals(name: string): string[] {
  return obj.value?.attributes?.[name] || []
}

async function loadObject() {
  loading.value = true
  try {
    const data = await getObject(props.objectGuid)
    obj.value = data

    // General
    cn.value = data.cn || ''
    displayName.value = data.displayName || ''
    description.value = data.description || ''
    givenName.value = data.givenName || ''
    sn.value = data.sn || ''
    mail.value = data.mail || ''
    samAccountName.value = data.samAccountName || ''
    userPrincipalName.value = data.userPrincipalName || ''

    // Address
    streetAddress.value = getAttrVal('streetAddress')
    postOfficeBox.value = getAttrVal('postOfficeBox')
    city.value = getAttrVal('l')
    state.value = getAttrVal('st')
    postalCode.value = getAttrVal('postalCode')
    country.value = getAttrVal('c') || getAttrVal('countryCode')

    // Telephones
    telephoneNumber.value = getAttrVal('telephoneNumber')
    homePhone.value = getAttrVal('homePhone')
    mobile.value = getAttrVal('mobile')
    fax.value = getAttrVal('facsimileTelephoneNumber')
    ipPhone.value = getAttrVal('ipPhone')
    pager.value = getAttrVal('pager')
    info.value = getAttrVal('info')
    otherTelephone.value = getAttrVals('otherTelephone')
    otherHomePhone.value = getAttrVals('otherHomePhone')
    otherMobile.value = getAttrVals('otherMobile')
    otherFax.value = getAttrVals('otherFacsimileTelephoneNumber')
    otherIpPhone.value = getAttrVals('otherIpPhone')
    otherPager.value = getAttrVals('otherPager')

    // Organization
    title.value = data.title || ''
    department.value = data.department || ''
    company.value = data.company || ''
    manager.value = data.manager || ''

    // UAC / Account
    const uac = data.userAccountControl || 0
    uacDisabled.value = (uac & 0x2) !== 0
    uacPasswordNeverExpires.value = (uac & 0x10000) !== 0
    uacSmartcardRequired.value = (uac & 0x40000) !== 0

    // Account expires
    if (!data.accountExpires || data.accountExpires === 0 || data.accountExpires === 9223372036854776000) {
      accountExpiresNever.value = true
      accountExpiresDate.value = null
    } else {
      accountExpiresNever.value = false
      const epoch = BigInt(data.accountExpires) - BigInt('116444736000000000')
      const ms = Number(epoch / BigInt(10000))
      accountExpiresDate.value = new Date(ms)
    }

    // SPNs
    servicePrincipalNames.value = [...(data.servicePrincipalNames || [])]

    // Delegation
    const msDsAllowed = data.msDsAllowedToDelegateTo || []
    allowedDelegateServices.value = [...msDsAllowed]
    if (uac & 0x100000) { // NOT_DELEGATED
      delegationType.value = 'none'
    } else if (uac & 0x80) { // TRUSTED_FOR_DELEGATION
      delegationType.value = 'unconstrained'
    } else if (uac & 0x1000000) { // TRUSTED_TO_AUTHENTICATE_FOR_DELEGATION
      delegationType.value = 'protocol_transition'
    } else if (msDsAllowed.length > 0) {
      delegationType.value = 'constrained'
    } else {
      delegationType.value = 'none'
    }

    // Logon hours
    const logonHoursB64 = getAttrVal('logonHours')
    initLogonHoursGrid(logonHoursB64)

    // Workstations
    const ws = getAttrVal('userWorkstations')
    workstationsList.value = ws ? ws.split(',').map(s => s.trim()).filter(Boolean) : []

    // Direct reports
    if (isUser.value && data.objectGuid) {
      try {
        directReports.value = await getDirectReports(data.objectGuid)
      } catch {
        directReports.value = []
      }
    }
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    loading.value = false
  }
}

function initLogonHoursGrid(base64?: string) {
  // 21 bytes = 7 days x 3 bytes (24 bits = 24 hours)
  const grid: boolean[][] = []
  let bytes: number[] = []
  if (base64) {
    try {
      const raw = atob(base64)
      bytes = Array.from(raw, c => c.charCodeAt(0))
    } catch { /* ignore */ }
  }
  // Default: all hours allowed if no logonHours attribute
  const allAllowed = bytes.length !== 21
  for (let day = 0; day < 7; day++) {
    const row: boolean[] = []
    for (let hour = 0; hour < 24; hour++) {
      if (allAllowed) {
        row.push(true)
      } else {
        const byteIndex = day * 3 + Math.floor(hour / 8)
        const bitIndex = hour % 8
        row.push((bytes[byteIndex] & (1 << bitIndex)) !== 0)
      }
    }
    grid.push(row)
  }
  logonHoursGrid.value = grid
}

function logonHoursToBase64(): string {
  const bytes = new Uint8Array(21)
  for (let day = 0; day < 7; day++) {
    for (let hour = 0; hour < 24; hour++) {
      if (logonHoursGrid.value[day]?.[hour]) {
        const byteIndex = day * 3 + Math.floor(hour / 8)
        const bitIndex = hour % 8
        bytes[byteIndex] |= (1 << bitIndex)
      }
    }
  }
  return btoa(String.fromCharCode(...bytes))
}

function toggleLogonHour(day: number, hour: number) {
  logonHoursGrid.value[day][hour] = !logonHoursGrid.value[day][hour]
}

function selectAllHours() {
  for (let d = 0; d < 7; d++)
    for (let h = 0; h < 24; h++)
      logonHoursGrid.value[d][h] = true
}

function clearAllHours() {
  for (let d = 0; d < 7; d++)
    for (let h = 0; h < 24; h++)
      logonHoursGrid.value[d][h] = false
}

function validateSpn(val: string): string | null {
  const pattern = /^[a-zA-Z0-9_\-]+\/[a-zA-Z0-9._\-]+(:\d+)?$/
  if (!pattern.test(val)) return 'SPN must be in format service/host or service/host:port'
  return null
}

function openOtherPhones(field: typeof otherPhoneDialogField.value) {
  otherPhoneDialogField.value = field
  otherPhoneDialogVisible.value = true
}

function getOtherPhoneModel(): string[] {
  switch (otherPhoneDialogField.value) {
    case 'telephone': return otherTelephone.value
    case 'homePhone': return otherHomePhone.value
    case 'mobile': return otherMobile.value
    case 'fax': return otherFax.value
    case 'ipPhone': return otherIpPhone.value
    case 'pager': return otherPager.value
  }
}

function setOtherPhoneModel(val: string[]) {
  switch (otherPhoneDialogField.value) {
    case 'telephone': otherTelephone.value = val; break
    case 'homePhone': otherHomePhone.value = val; break
    case 'mobile': otherMobile.value = val; break
    case 'fax': otherFax.value = val; break
    case 'ipPhone': otherIpPhone.value = val; break
    case 'pager': otherPager.value = val; break
  }
}

const otherPhoneDialogLabel = computed(() => {
  const labels: Record<string, string> = {
    telephone: 'Other Phone Numbers',
    homePhone: 'Other Home Phones',
    mobile: 'Other Mobile Numbers',
    fax: 'Other Fax Numbers',
    ipPhone: 'Other IP Phones',
    pager: 'Other Pagers',
  }
  return labels[otherPhoneDialogField.value] || 'Other'
})

// Security loading is now handled by SecurityTab component
async function onSave() {
  if (!obj.value?.objectGuid) return
  saving.value = true
  try {
    const body: Record<string, unknown> = {
      displayName: displayName.value,
      description: description.value,
      givenName: givenName.value,
      sn: sn.value,
      mail: mail.value,
      title: title.value,
      department: department.value,
      company: company.value,
      manager: manager.value,
      // Address fields
      streetAddress: streetAddress.value,
      postOfficeBox: postOfficeBox.value,
      l: city.value,
      st: state.value,
      postalCode: postalCode.value,
      c: country.value,
      co: country.value ? countries.find(c => c.value === country.value)?.label || '' : '',
      countryCode: country.value,
      // Telephone fields
      telephoneNumber: telephoneNumber.value,
      homePhone: homePhone.value,
      mobile: mobile.value,
      facsimileTelephoneNumber: fax.value,
      ipPhone: ipPhone.value,
      pager: pager.value,
      info: info.value,
      otherTelephone: otherTelephone.value.length > 0 ? otherTelephone.value : null,
      otherHomePhone: otherHomePhone.value.length > 0 ? otherHomePhone.value : null,
      otherMobile: otherMobile.value.length > 0 ? otherMobile.value : null,
      otherFacsimileTelephoneNumber: otherFax.value.length > 0 ? otherFax.value : null,
      otherIpPhone: otherIpPhone.value.length > 0 ? otherIpPhone.value : null,
      otherPager: otherPager.value.length > 0 ? otherPager.value : null,
      // SPNs
      servicePrincipalName: servicePrincipalNames.value.length > 0 ? servicePrincipalNames.value : null,
    }

    // Account expires
    if (isUser.value) {
      if (accountExpiresNever.value) {
        body.accountExpires = '0'
      } else if (accountExpiresDate.value) {
        const ms = accountExpiresDate.value.getTime()
        const filetime = BigInt(ms) * BigInt(10000) + BigInt('116444736000000000')
        body.accountExpires = filetime.toString()
      }
    }

    await updateObject(obj.value.objectGuid, body)

    // Save delegation if applicable
    if (showDelegationTab.value) {
      await updateDelegation(
        obj.value.objectGuid,
        delegationType.value,
        delegationType.value === 'constrained' || delegationType.value === 'protocol_transition'
          ? allowedDelegateServices.value : undefined
      )
    }

    toast.add({ severity: 'success', summary: 'Saved', detail: 'Object updated successfully', life: 3000 })
    emit('update:visible', false)
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    saving.value = false
  }
}

async function saveLogonHours() {
  if (!obj.value?.objectGuid) return
  try {
    await updateLogonHours(obj.value.objectGuid, logonHoursToBase64())
    toast.add({ severity: 'success', summary: 'Saved', detail: 'Logon hours updated', life: 3000 })
    logonHoursDialogVisible.value = false
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

async function saveWorkstations() {
  if (!obj.value?.objectGuid) return
  try {
    await updateLogonWorkstations(obj.value.objectGuid, workstationsList.value)
    toast.add({ severity: 'success', summary: 'Saved', detail: 'Logon workstations updated', life: 3000 })
    workstationsDialogVisible.value = false
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

function close() {
  emit('update:visible', false)
}
</script>

<template>
  <Dialog :visible="visible" @update:visible="close" :header="obj?.cn || obj?.dn || 'Properties'"
          modal :style="{ width: '750px' }" :closable="true" class="property-sheet">
    <div v-if="loading" style="text-align: center; padding: 3rem">
      <ProgressSpinner />
    </div>

    <template v-else-if="obj">
      <p style="font-size: 0.8125rem; color: var(--p-text-muted-color); margin: 0 0 0.75rem 0">View and edit properties for this object. Changes are saved when you click Save.</p>
      <Tabs value="general">
        <TabList>
          <Tab value="general">General</Tab>
          <Tab v-if="showAddressTab" value="address">Address</Tab>
          <Tab v-if="isUser" value="account">Account</Tab>
          <Tab v-if="showTelephonesTab" value="telephones">Telephones</Tab>
          <Tab v-if="showOrganizationTab" value="organization">Organization</Tab>
          <Tab v-if="showDelegationTab" value="delegation">Delegation</Tab>
          <Tab value="memberof">Member Of</Tab>
          <Tab v-if="isGroup" value="members">Members</Tab>
          <Tab value="security">Security</Tab>
          <Tab value="attributes">Attributes</Tab>
        </TabList>
        <TabPanels>

        <!-- General Tab -->
        <TabPanel value="general">
          <div class="prop-grid">
            <!-- Photo for users -->
            <div v-if="isUser && obj.thumbnailPhoto" class="prop-row full-width" style="display: flex; justify-content: center; margin-bottom: 0.5rem">
              <img :src="'data:image/jpeg;base64,' + obj.thumbnailPhoto" alt="Photo"
                   style="width: 96px; height: 96px; border-radius: 8px; object-fit: cover; border: 2px solid var(--p-surface-border)" />
            </div>
            <div class="prop-row">
              <label>Common Name (CN)</label>
              <InputText v-model="cn" disabled class="prop-input" />
            </div>
            <div class="prop-row">
              <label>Display Name</label>
              <InputText v-model="displayName" class="prop-input" />
            </div>
            <div class="prop-row full-width">
              <label>Description</label>
              <InputText v-model="description" class="prop-input" />
            </div>
            <template v-if="isUser">
              <div class="prop-row">
                <label>First Name</label>
                <InputText v-model="givenName" class="prop-input" />
              </div>
              <div class="prop-row">
                <label>Last Name</label>
                <InputText v-model="sn" class="prop-input" />
              </div>
              <div class="prop-row full-width">
                <label>Email</label>
                <InputText v-model="mail" class="prop-input" />
              </div>
            </template>

            <!-- Group scope/type for groups -->
            <template v-if="isGroup">
              <div class="prop-row">
                <label>Group Scope</label>
                <Tag :value="groupScopeLabel" severity="info" />
              </div>
              <div class="prop-row">
                <label>Group Type</label>
                <Tag :value="groupTypeLabel" :severity="groupTypeLabel === 'Security' ? 'warn' : 'secondary'" />
              </div>
            </template>

            <!-- OS info for computers -->
            <template v-if="isComputer">
              <div class="prop-row">
                <label>Operating System</label>
                <InputText :modelValue="obj.operatingSystem || ''" disabled class="prop-input" />
              </div>
              <div class="prop-row">
                <label>OS Version</label>
                <InputText :modelValue="obj.operatingSystemVersion || ''" disabled class="prop-input" />
              </div>
              <div v-if="obj.operatingSystemServicePack" class="prop-row">
                <label>Service Pack</label>
                <InputText :modelValue="obj.operatingSystemServicePack" disabled class="prop-input" />
              </div>
              <div v-if="obj.dnsHostName" class="prop-row">
                <label>DNS Host Name</label>
                <InputText :modelValue="obj.dnsHostName" disabled class="prop-input" />
              </div>
            </template>

            <div class="prop-row full-width">
              <label>Distinguished Name</label>
              <InputText :modelValue="obj.dn" disabled class="prop-input" style="font-family: monospace; font-size: 0.8125rem" />
            </div>
          </div>
        </TabPanel>

        <!-- Address Tab -->
        <TabPanel v-if="showAddressTab" value="address">
          <div class="prop-grid">
            <div class="prop-row full-width">
              <label>Street Address</label>
              <Textarea v-model="streetAddress" rows="3" class="prop-input" />
            </div>
            <div class="prop-row">
              <label>P.O. Box</label>
              <InputText v-model="postOfficeBox" class="prop-input" />
            </div>
            <div class="prop-row">
              <label>City</label>
              <InputText v-model="city" class="prop-input" />
            </div>
            <div class="prop-row">
              <label>State/Province</label>
              <InputText v-model="state" class="prop-input" />
            </div>
            <div class="prop-row">
              <label>Zip/Postal Code</label>
              <InputText v-model="postalCode" class="prop-input" />
            </div>
            <div class="prop-row full-width">
              <label>Country/Region</label>
              <Select v-model="country" :options="countries" optionLabel="label" optionValue="value"
                      placeholder="Select country..." class="prop-input" filter />
            </div>
          </div>
        </TabPanel>

        <!-- Account Tab (users) -->
        <TabPanel v-if="isUser" value="account">
          <div class="prop-grid">
            <div class="prop-row">
              <label>sAMAccountName</label>
              <InputText v-model="samAccountName" disabled class="prop-input" />
            </div>
            <div class="prop-row">
              <label>User Principal Name</label>
              <InputText v-model="userPrincipalName" disabled class="prop-input" />
            </div>
            <div class="prop-row full-width">
              <label>Account Options</label>
              <div style="display: flex; flex-direction: column; gap: 0.75rem; margin-top: 0.25rem">
                <div style="display: flex; align-items: center; gap: 0.5rem">
                  <Checkbox v-model="uacDisabled" :binary="true" inputId="uac-disabled" />
                  <label for="uac-disabled" style="font-weight: 400">Account is disabled</label>
                </div>
                <div style="display: flex; align-items: center; gap: 0.5rem">
                  <Checkbox v-model="uacPasswordNeverExpires" :binary="true" inputId="uac-pwd-never" />
                  <label for="uac-pwd-never" style="font-weight: 400">Password never expires</label>
                </div>
                <div style="display: flex; align-items: center; gap: 0.5rem">
                  <Checkbox v-model="uacSmartcardRequired" :binary="true" inputId="uac-smartcard" />
                  <label for="uac-smartcard" style="font-weight: 400">Smart card required for interactive logon</label>
                </div>
              </div>
            </div>
            <div class="prop-row">
              <label>Account Expires</label>
              <div style="display: flex; flex-direction: column; gap: 0.5rem">
                <div style="display: flex; align-items: center; gap: 0.5rem">
                  <Checkbox v-model="accountExpiresNever" :binary="true" inputId="acc-exp-never" />
                  <label for="acc-exp-never" style="font-weight: 400">Never</label>
                </div>
                <DatePicker v-if="!accountExpiresNever" v-model="accountExpiresDate" dateFormat="yy-mm-dd" showIcon
                            class="prop-input" />
              </div>
            </div>
            <div class="prop-row">
              <label>Password Last Set</label>
              <InputText :modelValue="formatFileTime(obj.pwdLastSet)" disabled class="prop-input" />
            </div>
            <div class="prop-row">
              <label>Last Logon</label>
              <InputText :modelValue="formatFileTime(obj.lastLogon)" disabled class="prop-input" />
            </div>
            <div class="prop-row">
              <label>Bad Password Count</label>
              <InputText :modelValue="String(obj.badPwdCount)" disabled class="prop-input" />
            </div>
            <div class="prop-row full-width" style="display: flex; flex-direction: row; gap: 0.5rem; align-items: flex-start">
              <Button label="Logon Hours..." icon="pi pi-clock" size="small" severity="secondary" outlined
                      @click="logonHoursDialogVisible = true" />
              <Button label="Log On To..." icon="pi pi-desktop" size="small" severity="secondary" outlined
                      @click="workstationsDialogVisible = true" />
            </div>

            <!-- Service Principal Names -->
            <div class="prop-row full-width">
              <MultiValueEditor v-model="servicePrincipalNames" label="Service Principal Names"
                                placeholder="service/hostname or service/hostname:port"
                                :validator="validateSpn" />
            </div>
          </div>
        </TabPanel>

        <!-- Telephones Tab -->
        <TabPanel v-if="showTelephonesTab" value="telephones">
          <div class="prop-grid">
            <div class="prop-row">
              <label>Phone</label>
              <div style="display: flex; gap: 0.25rem">
                <InputText v-model="telephoneNumber" class="prop-input" style="flex: 1" />
                <Button label="Other..." size="small" severity="secondary" text @click="openOtherPhones('telephone')" />
              </div>
            </div>
            <div class="prop-row">
              <label>Home Phone</label>
              <div style="display: flex; gap: 0.25rem">
                <InputText v-model="homePhone" class="prop-input" style="flex: 1" />
                <Button label="Other..." size="small" severity="secondary" text @click="openOtherPhones('homePhone')" />
              </div>
            </div>
            <div class="prop-row">
              <label>Mobile</label>
              <div style="display: flex; gap: 0.25rem">
                <InputText v-model="mobile" class="prop-input" style="flex: 1" />
                <Button label="Other..." size="small" severity="secondary" text @click="openOtherPhones('mobile')" />
              </div>
            </div>
            <div class="prop-row">
              <label>Fax</label>
              <div style="display: flex; gap: 0.25rem">
                <InputText v-model="fax" class="prop-input" style="flex: 1" />
                <Button label="Other..." size="small" severity="secondary" text @click="openOtherPhones('fax')" />
              </div>
            </div>
            <div class="prop-row">
              <label>IP Phone</label>
              <div style="display: flex; gap: 0.25rem">
                <InputText v-model="ipPhone" class="prop-input" style="flex: 1" />
                <Button label="Other..." size="small" severity="secondary" text @click="openOtherPhones('ipPhone')" />
              </div>
            </div>
            <div class="prop-row">
              <label>Pager</label>
              <div style="display: flex; gap: 0.25rem">
                <InputText v-model="pager" class="prop-input" style="flex: 1" />
                <Button label="Other..." size="small" severity="secondary" text @click="openOtherPhones('pager')" />
              </div>
            </div>
            <div class="prop-row full-width">
              <label>Notes</label>
              <Textarea v-model="info" rows="3" class="prop-input" />
            </div>
          </div>

          <!-- Other phones dialog -->
          <Dialog v-model:visible="otherPhoneDialogVisible" :header="otherPhoneDialogLabel" modal :style="{ width: '400px' }">
            <MultiValueEditor :modelValue="getOtherPhoneModel()" @update:modelValue="setOtherPhoneModel" placeholder="Enter phone number" />
            <template #footer>
              <Button label="Close" severity="secondary" @click="otherPhoneDialogVisible = false" />
            </template>
          </Dialog>
        </TabPanel>

        <!-- Organization Tab -->
        <TabPanel v-if="showOrganizationTab" value="organization">
          <div class="prop-grid">
            <div class="prop-row">
              <label>Title</label>
              <InputText v-model="title" class="prop-input" />
            </div>
            <div class="prop-row">
              <label>Department</label>
              <InputText v-model="department" class="prop-input" />
            </div>
            <div class="prop-row full-width">
              <label>Company</label>
              <InputText v-model="company" class="prop-input" />
            </div>
            <div class="prop-row full-width">
              <DnPicker v-model="manager" label="Manager" objectFilter="(|(objectClass=user)(objectClass=contact))" />
            </div>
            <div class="prop-row full-width">
              <label>Direct Reports</label>
              <DataTable :value="directReports" stripedRows size="small" scrollable scrollHeight="200px">
                <Column header="Name" style="min-width: 200px">
                  <template #body="{ data }">
                    <div style="display: flex; align-items: center; gap: 0.5rem">
                      <i class="pi pi-user" style="color: var(--p-text-muted-color)"></i>
                      <span>{{ data.name || cnFromDn(data.dn) }}</span>
                    </div>
                  </template>
                </Column>
                <Column field="description" header="Description" style="width: 200px">
                  <template #body="{ data }">
                    <span style="color: var(--p-text-muted-color)">{{ data.description || '' }}</span>
                  </template>
                </Column>
                <template #empty>
                  <div style="text-align: center; padding: 1rem; color: var(--p-text-muted-color)">No direct reports</div>
                </template>
              </DataTable>
            </div>
          </div>
        </TabPanel>

        <!-- Delegation Tab -->
        <TabPanel v-if="showDelegationTab" value="delegation">
          <div style="display: flex; flex-direction: column; gap: 1rem">
            <div style="display: flex; flex-direction: column; gap: 0.75rem">
              <div style="display: flex; align-items: center; gap: 0.5rem">
                <RadioButton v-model="delegationType" value="none" inputId="del-none" />
                <label for="del-none" style="font-weight: 400">Do not trust this {{ isComputer ? 'computer' : 'user' }} for delegation</label>
              </div>
              <div style="display: flex; align-items: center; gap: 0.5rem">
                <RadioButton v-model="delegationType" value="unconstrained" inputId="del-uncon" />
                <label for="del-uncon" style="font-weight: 400">Trust this {{ isComputer ? 'computer' : 'user' }} for delegation to any service (Kerberos only)</label>
              </div>
              <div style="display: flex; align-items: center; gap: 0.5rem">
                <RadioButton v-model="delegationType" value="constrained" inputId="del-con" />
                <label for="del-con" style="font-weight: 400">Trust for delegation to specified services only - Use Kerberos only</label>
              </div>
              <div style="display: flex; align-items: center; gap: 0.5rem">
                <RadioButton v-model="delegationType" value="protocol_transition" inputId="del-proto" />
                <label for="del-proto" style="font-weight: 400">Trust for delegation to specified services only - Use any authentication protocol</label>
              </div>
            </div>

            <div v-if="delegationType === 'constrained' || delegationType === 'protocol_transition'">
              <MultiValueEditor v-model="allowedDelegateServices" label="Services to which this account can present delegated credentials"
                                placeholder="service/hostname" :validator="validateSpn" />
            </div>
          </div>
        </TabPanel>

        <!-- Member Of Tab -->
        <TabPanel value="memberof">
          <DataTable :value="obj.memberOf?.map(dn => ({ dn }))" stripedRows size="small" scrollable scrollHeight="350px">
            <Column header="Group" style="min-width: 300px">
              <template #body="{ data }">
                <div style="display: flex; align-items: center; gap: 0.5rem">
                  <i class="pi pi-users" style="color: var(--p-text-muted-color)"></i>
                  <span>{{ cnFromDn(data.dn) }}</span>
                </div>
              </template>
            </Column>
            <Column header="Distinguished Name">
              <template #body="{ data }">
                <span style="font-family: monospace; font-size: 0.75rem; color: var(--p-text-muted-color)">{{ data.dn }}</span>
              </template>
            </Column>
            <template #empty>
              <div style="text-align: center; padding: 1rem; color: var(--p-text-muted-color)">Not a member of any groups</div>
            </template>
          </DataTable>
        </TabPanel>

        <!-- Members Tab (groups) -->
        <TabPanel v-if="isGroup" value="members">
          <DataTable :value="obj.member?.map(dn => ({ dn }))" stripedRows size="small" scrollable scrollHeight="350px">
            <Column header="Member" style="min-width: 300px">
              <template #body="{ data }">
                <div style="display: flex; align-items: center; gap: 0.5rem">
                  <i class="pi pi-user" style="color: var(--p-text-muted-color)"></i>
                  <span>{{ cnFromDn(data.dn) }}</span>
                </div>
              </template>
            </Column>
            <Column header="Distinguished Name">
              <template #body="{ data }">
                <span style="font-family: monospace; font-size: 0.75rem; color: var(--p-text-muted-color)">{{ data.dn }}</span>
              </template>
            </Column>
            <template #empty>
              <div style="text-align: center; padding: 1rem; color: var(--p-text-muted-color)">No members</div>
            </template>
          </DataTable>
        </TabPanel>

        <!-- Security Tab -->
        <TabPanel value="security">
          <SecurityTab v-if="obj" :dn="obj.dn" />
        </TabPanel>

        <!-- Attributes Tab (full attribute editor with syntax-aware formatting) -->
        <TabPanel value="attributes">
          <AttributeEditor v-if="obj" :dn="obj.dn" />
        </TabPanel>

        </TabPanels>
      </Tabs>

    </template>

    <template #footer>
      <div style="display: flex; justify-content: flex-end; gap: 0.5rem">
        <Button label="Cancel" severity="secondary" text @click="close" />
        <Button label="Save" icon="pi pi-save" @click="onSave" :loading="saving" />
      </div>
    </template>

    <!-- Logon Hours Dialog -->
    <Dialog v-model:visible="logonHoursDialogVisible" header="Logon Hours" modal :style="{ width: '700px' }">
      <div style="margin-bottom: 0.5rem; display: flex; gap: 0.5rem">
        <Button label="Select All" size="small" severity="secondary" outlined @click="selectAllHours" />
        <Button label="Clear All" size="small" severity="secondary" outlined @click="clearAllHours" />
      </div>
      <div class="logon-hours-grid">
        <div class="lh-header-row">
          <div class="lh-day-label"></div>
          <div v-for="h in 24" :key="h" class="lh-hour-label">{{ h - 1 }}</div>
        </div>
        <div v-for="(day, dayIdx) in dayLabels" :key="dayIdx" class="lh-row">
          <div class="lh-day-label">{{ day }}</div>
          <div v-for="h in 24" :key="h" class="lh-cell"
               :class="{ 'lh-allowed': logonHoursGrid[dayIdx]?.[h-1] }"
               @click="toggleLogonHour(dayIdx, h - 1)">
          </div>
        </div>
      </div>
      <div style="margin-top: 0.75rem; display: flex; align-items: center; gap: 1rem; font-size: 0.8125rem; color: var(--p-text-muted-color)">
        <span><span class="lh-legend-allowed"></span> Logon allowed</span>
        <span><span class="lh-legend-denied"></span> Logon denied</span>
      </div>
      <template #footer>
        <Button label="Cancel" severity="secondary" text @click="logonHoursDialogVisible = false" />
        <Button label="Save" icon="pi pi-save" @click="saveLogonHours" />
      </template>
    </Dialog>

    <!-- Logon Workstations Dialog -->
    <Dialog v-model:visible="workstationsDialogVisible" header="Log On To - Allowed Workstations" modal :style="{ width: '450px' }">
      <MultiValueEditor v-model="workstationsList" label="Allowed workstations" placeholder="Enter computer name (e.g., PC01)" />
      <div style="margin-top: 0.5rem; font-size: 0.8125rem; color: var(--p-text-muted-color)">
        If no workstations are listed, the user can log on to any workstation.
      </div>
      <template #footer>
        <Button label="Cancel" severity="secondary" text @click="workstationsDialogVisible = false" />
        <Button label="Save" icon="pi pi-save" @click="saveWorkstations" />
      </template>
    </Dialog>
  </Dialog>
</template>

<style scoped>
.prop-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 1rem;
}

.prop-row {
  display: flex;
  flex-direction: column;
  gap: 0.375rem;
}

.prop-row.full-width {
  grid-column: 1 / -1;
}

.prop-row label {
  font-size: 0.8125rem;
  font-weight: 600;
  color: var(--p-text-muted-color);
}

.prop-input {
  width: 100%;
}

/* Logon Hours Grid */
.logon-hours-grid {
  display: flex;
  flex-direction: column;
  gap: 1px;
  background: var(--p-surface-border);
  border: 1px solid var(--p-surface-border);
  border-radius: 4px;
  overflow: hidden;
}

.lh-header-row, .lh-row {
  display: flex;
}

.lh-day-label {
  width: 40px;
  min-width: 40px;
  font-size: 0.6875rem;
  font-weight: 600;
  display: flex;
  align-items: center;
  justify-content: center;
  background: var(--p-surface-ground);
  color: var(--p-text-color);
}

.lh-hour-label {
  flex: 1;
  font-size: 0.5625rem;
  text-align: center;
  background: var(--p-surface-ground);
  color: var(--p-text-color);
  padding: 2px 0;
}

.lh-cell {
  flex: 1;
  height: 20px;
  background: var(--app-danger-bg);
  cursor: pointer;
  transition: background 0.1s;
}

.lh-cell.lh-allowed {
  background: var(--app-success-border);
}

.lh-cell:hover {
  opacity: 0.7;
}

.lh-legend-allowed, .lh-legend-denied {
  display: inline-block;
  width: 12px;
  height: 12px;
  border-radius: 2px;
  vertical-align: middle;
  margin-right: 4px;
}

.lh-legend-allowed {
  background: var(--app-success-border);
}

.lh-legend-denied {
  background: var(--app-danger-bg);
}
</style>
