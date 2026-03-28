<script setup lang="ts">
import { ref, onMounted, computed, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import Tree from 'primevue/tree'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Tag from 'primevue/tag'
import Dialog from 'primevue/dialog'
import Textarea from 'primevue/textarea'
import Select from 'primevue/select'
import TabView from 'primevue/tabview'
import TabPanel from 'primevue/tabpanel'
import ProgressSpinner from 'primevue/progressspinner'
import { useToast } from 'primevue/usetoast'
import GpoPolicySettingDialog from '../components/GpoPolicySettingDialog.vue'
import type { PolicySettingDef } from '../components/GpoPolicySettingDialog.vue'
import DnPicker from '../components/DnPicker.vue'
import {
  getGpo, updateGpoSettings,
  getSecurityFiltering, addSecurityFilter, removeSecurityFilter,
  createGpoBackup, listGpoBackups, restoreGpoBackup,
  listWmiFilters, createWmiFilter, updateWmiFilter, deleteWmiFilter, setGpoWmiFilter,
  type GpoDetail, type GpoPolicySettings, type SecurityFilterEntry,
  type GpoBackup, type WmiFilter,
} from '../api/gpos'
import { fetchSysvolConfig } from '../api/sysvol'
import type { SysvolConfig } from '../types/sysvol'
import { relativeTime } from '../utils/format'

const route = useRoute()
const router = useRouter()
const toast = useToast()

const gpoId = computed(() => route.params.id as string)
const gpo = ref<GpoDetail | null>(null)
const loading = ref(true)
const saving = ref(false)
const editSettings = ref<GpoPolicySettings>({})

// Policy setting dialog
const settingDialogVisible = ref(false)
const currentSettingDef = ref<PolicySettingDef | null>(null)
const currentSettingValue = ref<any>(null)
const currentSettingSection = ref<string>('')

// Security filtering
const securityFilters = ref<SecurityFilterEntry[]>([])
const secFilterLoading = ref(false)
const addFilterDn = ref('')

// Backups
const backups = ref<GpoBackup[]>([])
const backupsLoading = ref(false)
const backupDesc = ref('')
const creatingBackup = ref(false)

// WMI
const wmiFilters = ref<WmiFilter[]>([])
const wmiLoading = ref(false)
const wmiDialogVisible = ref(false)
const wmiName = ref('')
const wmiDesc = ref('')
const wmiQuery = ref('')
const wmiEditId = ref<string | null>(null)

// SYSVOL config check
const sysvolConfigured = ref(false)

// Tree
const selectedTreeKey = ref<Record<string, boolean>>({})
const expandedTreeKeys = ref<Record<string, boolean>>({
  'computer': true,
  'computer-policies': true,
  'computer-windows': true,
  'computer-security': true,
  'user': true,
  'user-policies': true,
})

const treeNodes = ref([
  {
    key: 'computer',
    label: 'Computer Configuration',
    icon: 'pi pi-desktop',
    children: [
      {
        key: 'computer-policies',
        label: 'Policies',
        icon: 'pi pi-folder',
        children: [
          {
            key: 'computer-windows',
            label: 'Windows Settings',
            icon: 'pi pi-folder',
            children: [
              {
                key: 'computer-security',
                label: 'Security Settings',
                icon: 'pi pi-shield',
                children: [
                  { key: 'account-policies', label: 'Account Policies', icon: 'pi pi-folder', children: [
                    { key: 'password-policy', label: 'Password Policy', icon: 'pi pi-lock' },
                    { key: 'account-lockout', label: 'Account Lockout Policy', icon: 'pi pi-ban' },
                    { key: 'kerberos-policy', label: 'Kerberos Policy', icon: 'pi pi-key' },
                  ]},
                  { key: 'local-policies', label: 'Local Policies', icon: 'pi pi-folder', children: [
                    { key: 'audit-policy', label: 'Audit Policy', icon: 'pi pi-eye' },
                    { key: 'user-rights', label: 'User Rights Assignment', icon: 'pi pi-users' },
                    { key: 'security-options', label: 'Security Options', icon: 'pi pi-cog' },
                  ]},
                  { key: 'event-log', label: 'Event Log', icon: 'pi pi-list' },
                  { key: 'restricted-groups', label: 'Restricted Groups', icon: 'pi pi-users' },
                  { key: 'system-services', label: 'System Services', icon: 'pi pi-server' },
                  { key: 'software-restriction', label: 'Software Restriction Policies', icon: 'pi pi-ban' },
                ],
              },
              { key: 'computer-scripts', label: 'Scripts (Startup/Shutdown)', icon: 'pi pi-code' },
            ],
          },
        ],
      },
    ],
  },
  {
    key: 'user',
    label: 'User Configuration',
    icon: 'pi pi-user',
    children: [
      {
        key: 'user-policies',
        label: 'Policies',
        icon: 'pi pi-folder',
        children: [
          { key: 'user-scripts', label: 'Scripts (Logon/Logoff)', icon: 'pi pi-code' },
          { key: 'drive-mappings', label: 'Drive Mappings', icon: 'pi pi-database' },
        ],
      },
    ],
  },
])

// Policy setting definitions by category
const policyDefinitions: Record<string, PolicySettingDef[]> = {
  'password-policy': [
    { key: 'passwordPolicy.minimumLength', name: 'Minimum password length', description: 'Minimum number of characters a password must contain', type: 'number', min: 0, max: 128, defaultValue: 7 },
    { key: 'passwordPolicy.historyCount', name: 'Enforce password history', description: 'Number of unique passwords before an old one can be reused', type: 'number', min: 0, max: 24, defaultValue: 24 },
    { key: 'passwordPolicy.maxAgeDays', name: 'Maximum password age (days)', description: 'Maximum number of days a password can be used before the user must change it', type: 'number', min: 0, max: 999, defaultValue: 42 },
    { key: 'passwordPolicy.minAgeDays', name: 'Minimum password age (days)', description: 'Minimum number of days before a password can be changed', type: 'number', min: 0, max: 998, defaultValue: 1 },
    { key: 'passwordPolicy.complexityEnabled', name: 'Password must meet complexity requirements', description: 'Require passwords to contain characters from at least 3 of: uppercase, lowercase, digits, special characters', type: 'boolean' },
    { key: 'passwordPolicy.reversibleEncryption', name: 'Store passwords using reversible encryption', description: 'Store passwords in a way that is essentially the same as plaintext. Should be disabled unless required by applications.', type: 'boolean' },
  ],
  'account-lockout': [
    { key: 'accountLockout.threshold', name: 'Account lockout threshold', description: 'Number of failed login attempts before account is locked. 0 means never locked.', type: 'number', min: 0, max: 999, defaultValue: 0 },
    { key: 'accountLockout.durationMinutes', name: 'Account lockout duration (minutes)', description: 'Minutes until a locked account is automatically unlocked. 0 = admin must unlock.', type: 'number', min: 0, max: 99999, defaultValue: 30 },
    { key: 'accountLockout.observationWindowMinutes', name: 'Reset account lockout counter after (minutes)', description: 'Minutes after which the failed logon counter is reset', type: 'number', min: 0, max: 99999, defaultValue: 30 },
  ],
  'kerberos-policy': [],
  'audit-policy': [
    { key: 'auditPolicy.auditAccountLogon', name: 'Audit account logon events', description: 'Audit when a user logs on using a domain account', type: 'dropdown', options: [{ label: 'No auditing', value: 0 }, { label: 'Success', value: 1 }, { label: 'Failure', value: 2 }, { label: 'Success and Failure', value: 3 }] },
    { key: 'auditPolicy.auditLogonEvents', name: 'Audit logon events', description: 'Audit interactive/network/remote logon events', type: 'dropdown', options: [{ label: 'No auditing', value: 0 }, { label: 'Success', value: 1 }, { label: 'Failure', value: 2 }, { label: 'Success and Failure', value: 3 }] },
    { key: 'auditPolicy.auditObjectAccess', name: 'Audit object access', description: 'Audit access to AD objects, files, registry keys', type: 'dropdown', options: [{ label: 'No auditing', value: 0 }, { label: 'Success', value: 1 }, { label: 'Failure', value: 2 }, { label: 'Success and Failure', value: 3 }] },
    { key: 'auditPolicy.auditPrivilegeUse', name: 'Audit privilege use', description: 'Audit when a user exercises a user right', type: 'dropdown', options: [{ label: 'No auditing', value: 0 }, { label: 'Success', value: 1 }, { label: 'Failure', value: 2 }, { label: 'Success and Failure', value: 3 }] },
    { key: 'auditPolicy.auditPolicyChange', name: 'Audit policy change', description: 'Audit changes to audit policies, user rights, trust policies', type: 'dropdown', options: [{ label: 'No auditing', value: 0 }, { label: 'Success', value: 1 }, { label: 'Failure', value: 2 }, { label: 'Success and Failure', value: 3 }] },
    { key: 'auditPolicy.auditAccountManagement', name: 'Audit account management', description: 'Audit creation, modification, deletion of user/group accounts', type: 'dropdown', options: [{ label: 'No auditing', value: 0 }, { label: 'Success', value: 1 }, { label: 'Failure', value: 2 }, { label: 'Success and Failure', value: 3 }] },
    { key: 'auditPolicy.auditProcessTracking', name: 'Audit process tracking', description: 'Audit program execution, process exit, handle duplication', type: 'dropdown', options: [{ label: 'No auditing', value: 0 }, { label: 'Success', value: 1 }, { label: 'Failure', value: 2 }, { label: 'Success and Failure', value: 3 }] },
    { key: 'auditPolicy.auditDsAccess', name: 'Audit directory service access', description: 'Audit access to Active Directory objects', type: 'dropdown', options: [{ label: 'No auditing', value: 0 }, { label: 'Success', value: 1 }, { label: 'Failure', value: 2 }, { label: 'Success and Failure', value: 3 }] },
    { key: 'auditPolicy.auditSystemEvents', name: 'Audit system events', description: 'Audit system startup, shutdown, security log events', type: 'dropdown', options: [{ label: 'No auditing', value: 0 }, { label: 'Success', value: 1 }, { label: 'Failure', value: 2 }, { label: 'Success and Failure', value: 3 }] },
  ],
  'user-rights': [
    { key: 'userRights.allowLogOnLocally', name: 'Allow log on locally', description: 'Determines which users can log on interactively', type: 'multivalue' },
    { key: 'userRights.denyLogOnLocally', name: 'Deny log on locally', description: 'Determines which users are prevented from logging on interactively', type: 'multivalue' },
    { key: 'userRights.allowRemoteDesktop', name: 'Allow log on through Remote Desktop Services', description: 'Determines which users can log on via RDP', type: 'multivalue' },
    { key: 'userRights.denyRemoteDesktop', name: 'Deny log on through Remote Desktop Services', description: 'Prevents specified users from connecting via RDP', type: 'multivalue' },
    { key: 'userRights.backupFilesAndDirectories', name: 'Back up files and directories', description: 'Allows users to bypass NTFS permissions to back up files', type: 'multivalue' },
    { key: 'userRights.restoreFilesAndDirectories', name: 'Restore files and directories', description: 'Allows users to bypass NTFS permissions to restore files', type: 'multivalue' },
    { key: 'userRights.shutdownSystem', name: 'Shut down the system', description: 'Determines which users can shut down the local computer', type: 'multivalue' },
    { key: 'userRights.changeSystemTime', name: 'Change the system time', description: 'Determines which users can change the system clock', type: 'multivalue' },
  ],
  'security-options': [
    { key: 'securityOptions.lanManagerAuthLevel', name: 'Network security: LAN Manager authentication level', description: 'Determines the level of NTLM authentication used for network logons', type: 'dropdown', options: [
      { label: 'Send LM & NTLM responses', value: 0 },
      { label: 'Send LM & NTLM - use NTLMv2 session security if negotiated', value: 1 },
      { label: 'Send NTLM response only', value: 2 },
      { label: 'Send NTLMv2 response only', value: 3 },
      { label: 'Send NTLMv2 response only. Refuse LM', value: 4 },
      { label: 'Send NTLMv2 response only. Refuse LM & NTLM', value: 5 },
    ]},
    { key: 'securityOptions.requireSmbSigning', name: 'Microsoft network server: Digitally sign communications (always)', description: 'Require SMB packet signing', type: 'boolean' },
    { key: 'securityOptions.ldapClientSigningRequirement', name: 'Network security: LDAP client signing requirements', description: 'Level of data signing required for LDAP BIND requests', type: 'dropdown', options: [
      { label: 'None', value: 0 }, { label: 'Negotiate signing', value: 1 }, { label: 'Require signing', value: 2 },
    ]},
    { key: 'securityOptions.ldapServerSigningRequirement', name: 'Domain controller: LDAP server signing requirements', description: 'Level of data signing required for LDAP BIND operations on the DC', type: 'dropdown', options: [
      { label: 'None', value: 0 }, { label: 'Require signing', value: 2 },
    ]},
    { key: 'securityOptions.enableGuestAccount', name: 'Accounts: Guest account status', description: 'Enable or disable the Guest account', type: 'boolean' },
    { key: 'securityOptions.renameAdministratorAccount', name: 'Accounts: Rename administrator account', description: 'Rename the built-in Administrator account', type: 'string' },
    { key: 'securityOptions.renameGuestAccount', name: 'Accounts: Rename guest account', description: 'Rename the built-in Guest account', type: 'string' },
  ],
  'software-restriction': [],
  'event-log': [],
  'restricted-groups': [],
  'system-services': [],
  'computer-scripts': [],
  'user-scripts': [],
  'drive-mappings': [],
}

// Selected category from tree
const activeCategory = ref<string | null>(null)

const activePolicies = computed(() => {
  if (!activeCategory.value) return []
  return policyDefinitions[activeCategory.value] || []
})

function getSettingValue(key: string): any {
  const parts = key.split('.')
  let obj: any = editSettings.value
  for (const p of parts) {
    if (obj == null) return null
    obj = obj[p]
  }
  return obj ?? null
}

function getSettingDisplay(key: string, def: PolicySettingDef): string {
  const val = getSettingValue(key)
  if (val === null || val === undefined) return 'Not Defined'
  if (def.type === 'boolean') return val ? 'Enabled' : 'Disabled'
  if (def.type === 'dropdown' && def.options) {
    const opt = def.options.find(o => o.value === val)
    return opt?.label ?? String(val)
  }
  if (def.type === 'multivalue' && Array.isArray(val)) return `${val.length} principal(s)`
  return String(val)
}

function onTreeSelect(node: any) {
  activeCategory.value = node.key
}

function openSettingEditor(def: PolicySettingDef) {
  currentSettingDef.value = def
  currentSettingValue.value = getSettingValue(def.key)
  settingDialogVisible.value = true
}

function onSettingSaved(key: string, value: any) {
  const parts = key.split('.')
  const section = parts[0]
  const field = parts[1]

  // Initialize section if needed
  if (!(editSettings.value as any)[section]) {
    (editSettings.value as any)[section] = {}
  }

  if (value === null) {
    delete (editSettings.value as any)[section][field]
    // Clean up empty section
    if (Object.keys((editSettings.value as any)[section]).length === 0) {
      delete (editSettings.value as any)[section]
    }
  } else {
    (editSettings.value as any)[section][field] = value
  }
}

onMounted(() => loadGpo())

async function loadGpo() {
  loading.value = true
  try {
    gpo.value = await getGpo(gpoId.value)
    editSettings.value = JSON.parse(JSON.stringify(gpo.value.policySettings || {}))
    await Promise.all([loadSecurityFilters(), loadBackups(), loadWmiFilters(), checkSysvolConfig()])
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    loading.value = false
  }
}

async function checkSysvolConfig() {
  try {
    const config = await fetchSysvolConfig()
    sysvolConfigured.value = !!(config.sysvolSharePath && config.smbServerHostname)
  } catch {
    sysvolConfigured.value = false
  }
}

async function saveSettings() {
  if (!gpo.value) return
  saving.value = true
  try {
    await updateGpoSettings(gpo.value.objectGuid, editSettings.value)
    toast.add({ severity: 'success', summary: 'Saved', detail: 'Policy settings updated', life: 3000 })
    await loadGpo()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    saving.value = false
  }
}

// Security Filtering
async function loadSecurityFilters() {
  if (!gpo.value) return
  secFilterLoading.value = true
  try {
    securityFilters.value = await getSecurityFiltering(gpo.value.objectGuid)
  } catch { securityFilters.value = [] }
  finally { secFilterLoading.value = false }
}

async function onAddFilter() {
  if (!gpo.value || !addFilterDn.value) return
  try {
    await addSecurityFilter(gpo.value.objectGuid, addFilterDn.value)
    addFilterDn.value = ''
    toast.add({ severity: 'success', summary: 'Added', detail: 'Security filter added', life: 3000 })
    await loadSecurityFilters()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

async function onRemoveFilter(entry: SecurityFilterEntry) {
  if (!gpo.value) return
  try {
    await removeSecurityFilter(gpo.value.objectGuid, entry.objectSid || entry.dn)
    toast.add({ severity: 'success', summary: 'Removed', detail: 'Security filter removed', life: 3000 })
    await loadSecurityFilters()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

// Backups
async function loadBackups() {
  backupsLoading.value = true
  try {
    const all = await listGpoBackups()
    backups.value = gpo.value ? all.filter(b => b.gpoGuid === gpo.value!.objectGuid) : all
  } catch { backups.value = [] }
  finally { backupsLoading.value = false }
}

async function onCreateBackup() {
  if (!gpo.value) return
  creatingBackup.value = true
  try {
    await createGpoBackup(gpo.value.objectGuid, backupDesc.value || undefined)
    toast.add({ severity: 'success', summary: 'Backed Up', detail: 'Backup created', life: 3000 })
    backupDesc.value = ''
    await loadBackups()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    creatingBackup.value = false
  }
}

async function onRestoreBackup(backup: GpoBackup) {
  if (!confirm(`Restore GPO from backup "${backup.description || backup.backupId}"? Current settings will be overwritten.`)) return
  try {
    await restoreGpoBackup(backup.backupId)
    toast.add({ severity: 'success', summary: 'Restored', detail: 'GPO restored from backup', life: 3000 })
    await loadGpo()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

// WMI Filters
async function loadWmiFilters() {
  wmiLoading.value = true
  try { wmiFilters.value = await listWmiFilters() }
  catch { wmiFilters.value = [] }
  finally { wmiLoading.value = false }
}

function openCreateWmi() {
  wmiEditId.value = null
  wmiName.value = ''
  wmiDesc.value = ''
  wmiQuery.value = ''
  wmiDialogVisible.value = true
}

function openEditWmi(f: WmiFilter) {
  wmiEditId.value = f.id
  wmiName.value = f.name
  wmiDesc.value = f.description
  wmiQuery.value = f.query
  wmiDialogVisible.value = true
}

async function onSaveWmi() {
  try {
    if (wmiEditId.value) {
      await updateWmiFilter(wmiEditId.value, { name: wmiName.value, description: wmiDesc.value, query: wmiQuery.value })
    } else {
      await createWmiFilter(wmiName.value, wmiDesc.value, wmiQuery.value)
    }
    toast.add({ severity: 'success', summary: 'Saved', detail: 'WMI filter saved', life: 3000 })
    wmiDialogVisible.value = false
    await loadWmiFilters()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

async function onDeleteWmi(f: WmiFilter) {
  if (!confirm(`Delete WMI filter "${f.name}"?`)) return
  try {
    await deleteWmiFilter(f.id)
    toast.add({ severity: 'success', summary: 'Deleted', detail: 'WMI filter deleted', life: 3000 })
    await loadWmiFilters()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

async function onLinkWmi(filterId: string | null) {
  if (!gpo.value) return
  try {
    await setGpoWmiFilter(gpo.value.objectGuid, filterId)
    toast.add({ severity: 'success', summary: 'Updated', detail: filterId ? 'WMI filter linked' : 'WMI filter removed', life: 3000 })
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}
</script>

<template>
  <div>
    <div class="page-header" style="display: flex; align-items: center; gap: 1rem">
      <Button icon="pi pi-arrow-left" severity="secondary" text size="small" @click="router.push('/gpos')" />
      <div>
        <h1 style="margin: 0">{{ gpo?.displayName || 'GPO Editor' }}</h1>
        <p style="margin: 0" v-if="gpo">
          <span style="font-family: monospace; font-size: 0.85em; color: var(--p-text-muted-color)">{{ gpo.cn }}</span>
        </p>
      </div>
      <div class="toolbar-spacer" />
      <Button label="Save Settings" icon="pi pi-check" @click="saveSettings" :loading="saving" />
    </div>

    <!-- SYSVOL warning banner -->
    <div v-if="!loading && !sysvolConfigured" class="card" style="margin-bottom: 1rem; border-color: var(--p-orange-300); background: var(--p-orange-50)">
      <div style="display: flex; align-items: center; gap: 0.75rem; color: var(--p-orange-700)">
        <i class="pi pi-exclamation-triangle" style="font-size: 1.25rem"></i>
        <div>
          <strong>SYSVOL share not configured.</strong> Group Policy files cannot be distributed to domain clients.
          <router-link to="/sysvol" style="margin-left: 0.25rem">Configure SYSVOL in SYSVOL settings.</router-link>
        </div>
      </div>
    </div>

    <!-- SYSVOL file path -->
    <div v-if="!loading && gpo?.gpcFileSysPath" class="card" style="margin-bottom: 1rem; padding: 0.75rem 1rem">
      <div style="display: flex; align-items: center; gap: 0.5rem">
        <i class="pi pi-folder" style="color: var(--p-text-muted-color)"></i>
        <span style="font-weight: 600; font-size: 0.875rem">Policy File Path:</span>
        <span style="font-family: monospace; font-size: 0.85rem; color: var(--p-text-muted-color)">{{ gpo.gpcFileSysPath }}</span>
      </div>
    </div>

    <div v-if="loading" style="text-align: center; padding: 4rem">
      <ProgressSpinner />
    </div>

    <template v-else-if="gpo">
      <TabView>
        <!-- Policy Settings Tab -->
        <TabPanel header="Policy Settings" value="policy-settings">
          <div style="display: flex; gap: 1rem; height: calc(100vh - 280px)">
            <!-- Tree panel -->
            <div class="card" style="width: 340px; min-width: 340px; overflow-y: auto; padding: 0.75rem">
              <Tree :value="treeNodes"
                    v-model:selectionKeys="selectedTreeKey"
                    v-model:expandedKeys="expandedTreeKeys"
                    selectionMode="single"
                    @node-select="onTreeSelect"
                    :pt="{ root: { style: 'border: none; padding: 0; background: transparent' } }" />
            </div>

            <!-- Settings panel -->
            <div class="card" style="flex: 1; overflow-y: auto; padding: 1rem">
              <template v-if="activeCategory && activePolicies.length > 0">
                <DataTable :value="activePolicies" stripedRows size="small" @row-dblclick="(e: any) => openSettingEditor(e.data)">
                  <Column header="Policy" style="min-width: 300px">
                    <template #body="{ data }">
                      <span style="cursor: pointer" @click="openSettingEditor(data)">{{ data.name }}</span>
                    </template>
                  </Column>
                  <Column header="Setting" style="width: 250px">
                    <template #body="{ data }">
                      <Tag v-if="getSettingValue(data.key) !== null"
                           :value="getSettingDisplay(data.key, data)"
                           severity="info" />
                      <span v-else style="color: var(--p-text-muted-color)">Not Defined</span>
                    </template>
                  </Column>
                </DataTable>
              </template>
              <template v-else-if="activeCategory">
                <div style="text-align: center; padding: 3rem; color: var(--p-text-muted-color)">
                  <i class="pi pi-info-circle" style="font-size: 2rem; margin-bottom: 0.5rem; display: block"></i>
                  <p>Select a policy category from the tree to view and edit settings.</p>
                  <p v-if="activeCategory === 'kerberos-policy'">Kerberos policy settings are managed at the domain level.</p>
                </div>
              </template>
              <template v-else>
                <div style="text-align: center; padding: 3rem; color: var(--p-text-muted-color)">
                  <i class="pi pi-arrow-left" style="font-size: 2rem; margin-bottom: 0.5rem; display: block"></i>
                  <p>Select a policy category from the tree on the left to view and configure settings.</p>
                </div>
              </template>
            </div>
          </div>
        </TabPanel>

        <!-- Security Filtering Tab -->
        <TabPanel header="Security Filtering" value="security-filtering">
          <div style="margin-bottom: 1rem; display: flex; gap: 0.5rem; align-items: flex-end">
            <DnPicker v-model="addFilterDn" label="Add Security Principal"
                      objectFilter="(|(objectClass=user)(objectClass=group)(objectClass=computer))"
                      style="width: 400px" />
            <Button label="Add" icon="pi pi-plus" size="small" @click="onAddFilter" :disabled="!addFilterDn" />
          </div>
          <div v-if="secFilterLoading" style="text-align: center; padding: 2rem"><ProgressSpinner /></div>
          <DataTable v-else :value="securityFilters" stripedRows size="small">
            <Column header="Name" field="name" style="min-width: 200px" />
            <Column header="Type" style="width: 120px">
              <template #body="{ data }">
                <Tag :value="data.objectClass" />
              </template>
            </Column>
            <Column header="SID" field="objectSid" style="min-width: 250px">
              <template #body="{ data }">
                <span style="font-family: monospace; font-size: 0.85em; color: var(--p-text-muted-color)">{{ data.objectSid }}</span>
              </template>
            </Column>
            <Column header="DN" field="dn" style="min-width: 300px">
              <template #body="{ data }">
                <span style="font-size: 0.85em; color: var(--p-text-muted-color)">{{ data.dn }}</span>
              </template>
            </Column>
            <Column style="width: 60px">
              <template #body="{ data }">
                <Button icon="pi pi-times" size="small" severity="danger" text @click="onRemoveFilter(data)" />
              </template>
            </Column>
            <template #empty>
              <div style="text-align: center; padding: 2rem; color: var(--p-text-muted-color)">
                No security filtering configured (applies to all authenticated users)
              </div>
            </template>
          </DataTable>
        </TabPanel>

        <!-- WMI Filtering Tab -->
        <TabPanel header="WMI Filtering" value="wmi-filtering">
          <div style="margin-bottom: 1rem; display: flex; gap: 0.5rem; align-items: center">
            <Select :options="[{ id: null, name: '(None)' }, ...wmiFilters]" optionLabel="name" optionValue="id"
                    placeholder="Select WMI filter for this GPO" size="small" style="width: 350px"
                    @change="(e: any) => onLinkWmi(e.value)" />
            <div class="toolbar-spacer" />
            <Button label="New WMI Filter" icon="pi pi-plus" size="small" @click="openCreateWmi" />
          </div>
          <DataTable :value="wmiFilters" stripedRows size="small">
            <Column field="name" header="Name" style="min-width: 200px" />
            <Column field="description" header="Description" style="min-width: 250px" />
            <Column header="Query" style="min-width: 300px">
              <template #body="{ data }">
                <code style="font-size: 0.85em">{{ data.query }}</code>
              </template>
            </Column>
            <Column header="Created" style="width: 130px">
              <template #body="{ data }">
                <span style="color: var(--p-text-muted-color)">{{ relativeTime(data.createdAt) }}</span>
              </template>
            </Column>
            <Column style="width: 100px">
              <template #body="{ data }">
                <Button icon="pi pi-pencil" size="small" severity="secondary" text @click="openEditWmi(data)" />
                <Button icon="pi pi-trash" size="small" severity="danger" text @click="onDeleteWmi(data)" />
              </template>
            </Column>
            <template #empty>
              <div style="text-align: center; padding: 2rem; color: var(--p-text-muted-color)">No WMI filters defined</div>
            </template>
          </DataTable>
        </TabPanel>

        <!-- Backup Tab -->
        <TabPanel header="Backup / Restore" value="backup-restore">
          <div style="margin-bottom: 1rem; display: flex; gap: 0.5rem; align-items: flex-end">
            <div style="flex: 1">
              <label style="display: block; margin-bottom: 0.25rem; font-weight: 600; font-size: 0.875rem">Backup Description</label>
              <InputText v-model="backupDesc" placeholder="Optional description for this backup" size="small" style="width: 100%" />
            </div>
            <Button label="Create Backup" icon="pi pi-save" size="small" @click="onCreateBackup" :loading="creatingBackup" />
          </div>
          <DataTable :value="backups" stripedRows size="small">
            <Column field="backupId" header="Backup ID" style="width: 200px">
              <template #body="{ data }">
                <span style="font-family: monospace; font-size: 0.85em">{{ data.backupId.substring(0, 8) }}...</span>
              </template>
            </Column>
            <Column field="gpoDisplayName" header="GPO" style="min-width: 200px" />
            <Column field="description" header="Description" style="min-width: 250px" />
            <Column header="Created" style="width: 180px">
              <template #body="{ data }">
                {{ new Date(data.createdAt).toLocaleString() }}
              </template>
            </Column>
            <Column style="width: 100px">
              <template #body="{ data }">
                <Button label="Restore" icon="pi pi-replay" size="small" severity="warn" text @click="onRestoreBackup(data)" />
              </template>
            </Column>
            <template #empty>
              <div style="text-align: center; padding: 2rem; color: var(--p-text-muted-color)">No backups found for this GPO</div>
            </template>
          </DataTable>
        </TabPanel>
      </TabView>
    </template>

    <!-- Policy Setting Dialog -->
    <GpoPolicySettingDialog
      v-model:visible="settingDialogVisible"
      :setting="currentSettingDef"
      :currentValue="currentSettingValue"
      @save="onSettingSaved"
    />

    <!-- WMI Filter Dialog -->
    <Dialog v-model:visible="wmiDialogVisible"
            :header="wmiEditId ? 'Edit WMI Filter' : 'Create WMI Filter'"
            modal :style="{ width: '550px' }">
      <div style="display: flex; flex-direction: column; gap: 1rem; padding: 0.5rem 0">
        <div>
          <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">Name</label>
          <InputText v-model="wmiName" size="small" style="width: 100%" />
        </div>
        <div>
          <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">Description</label>
          <InputText v-model="wmiDesc" size="small" style="width: 100%" />
        </div>
        <div>
          <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">WQL Query</label>
          <Textarea v-model="wmiQuery" rows="3" style="width: 100%; font-family: monospace; font-size: 0.875rem"
                    placeholder="SELECT * FROM Win32_OperatingSystem WHERE Version LIKE '10.%'" />
        </div>
      </div>
      <template #footer>
        <Button label="Cancel" severity="secondary" text @click="wmiDialogVisible = false" />
        <Button label="Save" icon="pi pi-check" @click="onSaveWmi" :disabled="!wmiName || !wmiQuery" />
      </template>
    </Dialog>
  </div>
</template>
