<script setup lang="ts">
import { ref, computed, watch, onMounted } from 'vue'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import Tag from 'primevue/tag'
import Checkbox from 'primevue/checkbox'
import Dialog from 'primevue/dialog'
import Panel from 'primevue/panel'
import Tabs from 'primevue/tabs'
import TabList from 'primevue/tablist'
import Tab from 'primevue/tab'
import TabPanels from 'primevue/tabpanels'
import TabPanel from 'primevue/tabpanel'
import InputText from 'primevue/inputtext'
import ProgressSpinner from 'primevue/progressspinner'
import Message from 'primevue/message'
import ConfirmDialog from 'primevue/confirmdialog'
import { useToast } from 'primevue/usetoast'
import { useConfirm } from 'primevue/useconfirm'
import {
  getObjectSecurity,
  getEffectivePermissions,
  updateOwner,
  addAce,
  removeAce,
  setInheritance,
  propagateInheritance,
} from '../api/security'
import type { SecurityDescriptorInfo, AceInfo, EffectivePermissions } from '../api/types'
import type { AceDto } from '../types/security'
import { ACCESS_MASK } from '../types/security'
import DnPicker from './DnPicker.vue'
import AceEditor from './AceEditor.vue'

const props = defineProps<{
  dn: string
}>()

const toast = useToast()
const confirm = useConfirm()

// State
const securityInfo = ref<SecurityDescriptorInfo | null>(null)
const loading = ref(true)
const selectedPrincipal = ref<string | null>(null)
const advancedDialogVisible = ref(false)
const advancedTab = ref('dacl')

// Owner editing
const changeOwnerDialogVisible = ref(false)
const newOwnerDn = ref('')
const changingOwner = ref(false)

// ACE editing
const aceEditorVisible = ref(false)
const aceEditorMode = ref<'add' | 'edit'>('add')
const editingAce = ref<AceDto | null>(null)
const editingAceIndex = ref(-1)
const selectedAdvancedAceIndex = ref<number | null>(null)

// Inheritance
const inheritanceEnabled = ref(true)
const inheritanceChanging = ref(false)

// Effective permissions state
const effectivePrincipalDn = ref('')
const effectivePermissions = ref<string[]>([])
const effectiveLoading = ref(false)
const effectiveChecked = ref(false)

// Load security descriptor
async function loadSecurity() {
  if (!props.dn) return
  loading.value = true
  try {
    securityInfo.value = await getObjectSecurity(props.dn)
    selectedPrincipal.value = null
    // Check inheritance status from control flags
    const control = securityInfo.value?.control ?? 0
    inheritanceEnabled.value = (control & 0x1000) === 0 // DaclProtected = 0x1000
    // Auto-select first principal
    if (uniquePrincipals.value.length > 0) {
      selectedPrincipal.value = uniquePrincipals.value[0].principal
    }
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error loading security', detail: e.message, life: 5000 })
    securityInfo.value = null
  } finally {
    loading.value = false
  }
}

watch(() => props.dn, () => {
  if (props.dn) loadSecurity()
})

onMounted(() => {
  if (props.dn) loadSecurity()
})

// ── Unique principals from DACL ──
interface PrincipalEntry {
  principal: string
  principalName?: string
  hasAllow: boolean
  hasDeny: boolean
}

const uniquePrincipals = computed<PrincipalEntry[]>(() => {
  if (!securityInfo.value?.dacl) return []
  const map = new Map<string, PrincipalEntry>()
  for (const ace of securityInfo.value.dacl) {
    const key = ace.principal
    if (!map.has(key)) {
      map.set(key, {
        principal: ace.principal,
        principalName: ace.principalName,
        hasAllow: false,
        hasDeny: false,
      })
    }
    const entry = map.get(key)!
    if (!entry.principalName && ace.principalName) {
      entry.principalName = ace.principalName
    }
    if (ace.type.startsWith('Allow')) entry.hasAllow = true
    if (ace.type.startsWith('Deny')) entry.hasDeny = true
  }
  return Array.from(map.values()).sort((a, b) => {
    const aName = a.principalName || a.principal
    const bName = b.principalName || b.principal
    return aName.localeCompare(bName)
  })
})

// ── Permissions for selected principal ──
const ALL_PERMISSIONS = [
  'Full Control',
  'Read Property',
  'Write Property',
  'Create Child',
  'Delete Child',
  'List Contents',
  'Delete',
  'Read Permissions',
  'Modify Permissions',
  'Modify Owner',
  'Delete Tree',
  'List Object',
  'Control Access',
  'Self',
]

const PERMISSION_CATEGORIES: Record<string, string> = {
  'Full Control': 'General',
  'Read Property': 'Read',
  'List Contents': 'Read',
  'Read Permissions': 'Read',
  'List Object': 'Read',
  'Write Property': 'Write',
  'Modify Permissions': 'Write',
  'Modify Owner': 'Write',
  'Create Child': 'Create/Delete',
  'Delete Child': 'Create/Delete',
  'Delete': 'Create/Delete',
  'Delete Tree': 'Create/Delete',
  'Control Access': 'Special',
  'Self': 'Special',
}

interface PermissionRow {
  permission: string
  category: string
  allowed: boolean
  denied: boolean
  allowInherited: boolean
  denyInherited: boolean
}

const permissionsForPrincipal = computed<PermissionRow[]>(() => {
  if (!securityInfo.value?.dacl || !selectedPrincipal.value) return []

  const aces = securityInfo.value.dacl.filter(a => a.principal === selectedPrincipal.value)

  const rows: PermissionRow[] = []

  // Collect all unique permissions from ACEs for this principal, plus standard ones
  const seenPerms = new Set<string>()
  for (const perm of ALL_PERMISSIONS) seenPerms.add(perm)
  for (const ace of aces) {
    for (const perm of ace.permissions) seenPerms.add(perm)
  }

  for (const perm of seenPerms) {
    let allowed = false
    let denied = false
    let allowInherited = false
    let denyInherited = false

    for (const ace of aces) {
      if (ace.permissions.includes(perm) || (perm !== 'Full Control' && ace.permissions.includes('Full Control'))) {
        if (ace.type.startsWith('Allow')) {
          allowed = true
          if (ace.isInherited) allowInherited = true
        }
        if (ace.type.startsWith('Deny')) {
          denied = true
          if (ace.isInherited) denyInherited = true
        }
      }
    }

    rows.push({
      permission: perm,
      category: PERMISSION_CATEGORIES[perm] || 'Special',
      allowed,
      denied,
      allowInherited,
      denyInherited,
    })
  }

  // Sort: General first, then Read, Write, Create/Delete, Special
  const categoryOrder: Record<string, number> = { 'General': 0, 'Read': 1, 'Write': 2, 'Create/Delete': 3, 'Special': 4 }
  return rows.sort((a, b) => {
    const ca = categoryOrder[a.category] ?? 5
    const cb = categoryOrder[b.category] ?? 5
    if (ca !== cb) return ca - cb
    return a.permission.localeCompare(b.permission)
  })
})

// ── Advanced ACE list ──
function appliesTo(ace: AceInfo): string {
  const parts: string[] = []
  if (ace.objectTypeName) {
    parts.push(ace.objectTypeName)
  } else if (ace.objectType) {
    parts.push(ace.objectType)
  } else {
    // Determine from flags
    const hasCI = ace.flags.includes('CONTAINER_INHERIT')
    const hasIO = ace.flags.includes('INHERIT_ONLY')
    if (!hasCI && !hasIO) parts.push('This object only')
    else if (hasCI && !hasIO) parts.push('This object and all descendant objects')
    else if (hasCI && hasIO) parts.push('All descendant objects only')
    else parts.push('This object and all descendant objects')
  }
  if (ace.inheritedObjectTypeName) {
    parts.push(`(Inherited: ${ace.inheritedObjectTypeName})`)
  } else if (ace.inheritedObjectType) {
    parts.push(`(Inherited: ${ace.inheritedObjectType})`)
  }
  return parts.join(' ')
}

function inheritanceSource(ace: AceInfo): string {
  if (!ace.isInherited) return '(not inherited)'
  if (ace.inheritedObjectTypeName) return ace.inheritedObjectTypeName
  if (ace.inheritedObjectType) return ace.inheritedObjectType
  return 'Parent object'
}

function aceSeverity(type: string): string {
  if (type.startsWith('Allow')) return 'success'
  if (type.startsWith('Deny')) return 'danger'
  if (type.startsWith('Audit')) return 'info'
  return 'secondary'
}

// ── Owner editing ──
async function onChangeOwner() {
  if (!newOwnerDn.value || !props.dn) return
  changingOwner.value = true
  try {
    // We need the SID of the selected principal. For simplicity, we pass the DN
    // and the backend resolves it. But our API expects a SID.
    // Use a search to get the SID first.
    const { resolveObject } = await import('../api/objects')
    const resolved = await resolveObject(newOwnerDn.value)
    // The resolve endpoint doesn't return SID. We'll use the DN-based lookup.
    // Actually, let's use the getObjectSecurity approach - search for objectSid
    const { get } = await import('../api/client')
    const detail: any = await get(`/objects/by-dn?dn=${encodeURIComponent(newOwnerDn.value)}`)
    const sid = detail?.objectSid
    if (!sid) {
      toast.add({ severity: 'error', summary: 'Error', detail: 'Could not resolve SID for selected principal', life: 5000 })
      return
    }

    securityInfo.value = await updateOwner(props.dn, sid)
    changeOwnerDialogVisible.value = false
    toast.add({ severity: 'success', summary: 'Owner Changed', detail: 'Security descriptor owner updated', life: 3000 })
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    changingOwner.value = false
  }
}

// ── Inheritance ──
function onInheritanceChange(newValue: boolean) {
  if (!newValue) {
    // Disabling inheritance - show confirm dialog
    confirm.require({
      message: 'Do you want to convert inherited permissions to explicit permissions (Copy), or remove all inherited permissions (Remove)?',
      header: 'Disable Inheritance',
      icon: 'pi pi-exclamation-triangle',
      rejectLabel: 'Cancel',
      acceptLabel: 'Copy',
      accept: async () => {
        await doSetInheritance(false)
      },
      reject: () => {
        // Revert
        inheritanceEnabled.value = true
      },
    })
  } else {
    doSetInheritance(true)
  }
}

async function doSetInheritance(enabled: boolean) {
  inheritanceChanging.value = true
  try {
    securityInfo.value = await setInheritance(props.dn, enabled)
    inheritanceEnabled.value = enabled
    toast.add({
      severity: 'success',
      summary: enabled ? 'Inheritance Enabled' : 'Inheritance Disabled',
      detail: enabled ? 'Inheritable permissions will now propagate' : 'Inherited permissions converted to explicit',
      life: 3000,
    })
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
    inheritanceEnabled.value = !enabled // revert
  } finally {
    inheritanceChanging.value = false
  }
}

async function onPropagateInheritance() {
  try {
    const result = await propagateInheritance(props.dn)
    toast.add({
      severity: 'success',
      summary: 'Inheritance Propagated',
      detail: `Propagated to ${result.propagated} child objects`,
      life: 3000,
    })
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

// ── ACE Add/Edit/Remove ──
function onAddAce() {
  editingAce.value = null
  editingAceIndex.value = -1
  aceEditorMode.value = 'add'
  aceEditorVisible.value = true
}

function onEditAce(ace: AceInfo, index: number) {
  editingAce.value = {
    type: ace.type.startsWith('Deny') ? 'deny' : 'allow',
    principalSid: ace.principal,
    principalName: ace.principalName,
    accessMask: parseInt(ace.accessMask, 16) || 0,
    flags: [...ace.flags],
    objectType: ace.objectType,
    objectTypeName: ace.objectTypeName,
    inheritedObjectType: ace.inheritedObjectType,
    inheritedObjectTypeName: ace.inheritedObjectTypeName,
  }
  editingAceIndex.value = index
  aceEditorMode.value = 'edit'
  aceEditorVisible.value = true
}

async function onAceSave(ace: AceDto) {
  try {
    if (aceEditorMode.value === 'edit' && editingAceIndex.value >= 0) {
      // Remove old ACE then add new one
      await removeAce(props.dn, editingAceIndex.value)
      securityInfo.value = await addAce(props.dn, ace)
    } else {
      securityInfo.value = await addAce(props.dn, ace)
    }
    toast.add({ severity: 'success', summary: 'Saved', detail: 'Permission entry saved', life: 3000 })
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

function onRemoveAce(index: number) {
  const ace = securityInfo.value?.dacl[index]
  if (!ace) return

  confirm.require({
    message: `Remove ${ace.type} permission for ${ace.principalName || ace.principal}?`,
    header: 'Remove Permission',
    icon: 'pi pi-exclamation-triangle',
    rejectLabel: 'Cancel',
    acceptLabel: 'Remove',
    acceptClass: 'p-button-danger',
    accept: async () => {
      try {
        securityInfo.value = await removeAce(props.dn, index)
        toast.add({ severity: 'success', summary: 'Removed', detail: 'Permission entry removed', life: 3000 })
      } catch (e: any) {
        toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
      }
    },
  })
}

// ── Effective Permissions ──
async function checkEffectivePermissions() {
  if (!effectivePrincipalDn.value || !props.dn) return
  effectiveLoading.value = true
  effectiveChecked.value = false
  try {
    const result: EffectivePermissions = await getEffectivePermissions(props.dn, effectivePrincipalDn.value)
    effectivePermissions.value = result.permissions
    effectiveChecked.value = true
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
    effectivePermissions.value = []
  } finally {
    effectiveLoading.value = false
  }
}

const effectivePermissionRows = computed(() => {
  return ALL_PERMISSIONS.map(perm => ({
    permission: perm,
    granted: effectivePermissions.value.includes(perm),
  }))
})

// ── Expanded ACE rows ──
const expandedAceRows = ref<AceInfo[]>([])

// Owner display
const ownerDisplay = computed(() => {
  if (!securityInfo.value) return ''
  const name = securityInfo.value.ownerName || ''
  const sid = securityInfo.value.owner || ''
  if (name && sid) return `${name} (${sid})`
  return name || sid || 'Unknown'
})

function selectPrincipal(principal: string) {
  selectedPrincipal.value = principal
}
</script>

<template>
  <div class="security-tab">
    <ConfirmDialog />

    <!-- Loading -->
    <div v-if="loading" style="text-align: center; padding: 2rem">
      <ProgressSpinner />
    </div>

    <template v-else-if="securityInfo">
      <!-- Owner bar -->
      <div class="owner-bar">
        <span class="owner-label">Owner:</span>
        <span class="owner-value">{{ ownerDisplay }}</span>
        <Button
          label="Change..."
          size="small"
          severity="secondary"
          text
          @click="changeOwnerDialogVisible = true; newOwnerDn = ''"
        />
      </div>

      <!-- Inheritance checkbox -->
      <div class="inheritance-bar">
        <Checkbox
          :modelValue="inheritanceEnabled"
          :binary="true"
          inputId="inherit-check"
          :disabled="inheritanceChanging"
          @update:modelValue="onInheritanceChange"
        />
        <label for="inherit-check" class="inherit-label">
          Include inheritable permissions from this object's parent
        </label>
        <Button
          v-if="inheritanceEnabled"
          label="Propagate..."
          size="small"
          severity="secondary"
          text
          icon="pi pi-share-alt"
          @click="onPropagateInheritance"
          v-tooltip="'Force propagation of inherited ACEs to child objects'"
        />
      </div>

      <p style="font-size: 0.8125rem; color: var(--p-text-muted-color); margin: 0 0 0.5rem 0">Permissions control who can access this object and what actions they can perform. Select a principal above to view and modify their permissions below.</p>

      <!-- Principal list -->
      <div class="section-header">Group or user names:</div>
      <p style="font-size: 0.8125rem; color: var(--p-text-muted-color); margin: 0 0 0.5rem 0">Select a user or group to view their assigned permissions.</p>
      <DataTable
        :value="uniquePrincipals"
        selectionMode="single"
        :selection="uniquePrincipals.find(p => p.principal === selectedPrincipal) || null"
        @rowSelect="(e: any) => selectPrincipal(e.data.principal)"
        dataKey="principal"
        size="small"
        scrollable
        scrollHeight="160px"
        stripedRows
        class="principal-table"
      >
        <Column header="Principal" style="min-width: 200px">
          <template #body="{ data }: { data: PrincipalEntry }">
            <div class="principal-cell">
              <i class="pi pi-users" style="color: var(--p-text-muted-color); font-size: 0.875rem"></i>
              <span class="principal-name">{{ data.principalName || data.principal }}</span>
            </div>
          </template>
        </Column>
        <Column header="Entries" style="width: 180px">
          <template #body="{ data }: { data: PrincipalEntry }">
            <div style="display: flex; gap: 0.25rem">
              <Tag v-if="data.hasAllow" value="Allow" severity="success" class="ace-badge" />
              <Tag v-if="data.hasDeny" value="Deny" severity="danger" class="ace-badge" />
            </div>
          </template>
        </Column>
      </DataTable>

      <!-- Add/Remove buttons for principal-level -->
      <div class="ace-actions">
        <Button label="Add..." icon="pi pi-plus" size="small" severity="secondary" outlined @click="onAddAce" />
        <Button
          label="Remove"
          icon="pi pi-trash"
          size="small"
          severity="danger"
          outlined
          :disabled="!selectedPrincipal"
          @click="() => {
            if (!selectedPrincipal || !securityInfo) return
            const idx = securityInfo.dacl.findIndex(a => a.principal === selectedPrincipal && !a.isInherited)
            if (idx >= 0) onRemoveAce(idx)
          }"
        />
      </div>

      <!-- Permissions for selected principal -->
      <div v-if="selectedPrincipal" class="permissions-section">
        <div class="section-header">
          Permissions for {{ uniquePrincipals.find(p => p.principal === selectedPrincipal)?.principalName || selectedPrincipal }}:
        </div>
        <p style="font-size: 0.8125rem; color: var(--p-text-muted-color); margin: 0 0 0.5rem 0">Check marks indicate the permission is granted. Grayed-out check marks are inherited from parent objects.</p>
        <DataTable
          :value="permissionsForPrincipal"
          size="small"
          scrollable
          scrollHeight="200px"
          stripedRows
          class="permissions-table"
        >
          <Column header="Permission" style="min-width: 200px">
            <template #body="{ data }: { data: PermissionRow }">
              <span :class="{ 'perm-name': true, 'perm-inherited': data.allowInherited || data.denyInherited }">
                {{ data.permission }}
              </span>
              <span v-if="data.allowInherited || data.denyInherited" class="inherited-badge">(Inherited)</span>
            </template>
          </Column>
          <Column header="Allow" style="width: 80px; text-align: center">
            <template #body="{ data }: { data: PermissionRow }">
              <Checkbox
                :modelValue="data.allowed"
                :binary="true"
                :disabled="true"
                :class="{ 'check-inherited': data.allowInherited }"
              />
            </template>
          </Column>
          <Column header="Deny" style="width: 80px; text-align: center">
            <template #body="{ data }: { data: PermissionRow }">
              <Checkbox
                :modelValue="data.denied"
                :binary="true"
                :disabled="true"
                :class="{ 'check-inherited': data.denyInherited }"
              />
            </template>
          </Column>
        </DataTable>
      </div>

      <!-- Bottom actions -->
      <div class="bottom-actions">
        <Button
          label="Advanced..."
          icon="pi pi-cog"
          severity="secondary"
          outlined
          size="small"
          @click="advancedDialogVisible = true"
        />
      </div>

      <!-- Effective Permissions panel -->
      <Panel header="Effective Access" toggleable :collapsed="true" class="effective-panel">
        <div class="effective-content">
          <p style="font-size: 0.8125rem; color: var(--p-text-muted-color); margin: 0 0 0.5rem 0">Effective permissions show the actual access a user or group has after all allow and deny entries are evaluated, including inherited permissions.</p>
          <div class="effective-picker">
            <DnPicker
              v-model="effectivePrincipalDn"
              label="Select a user or group"
              objectFilter="(|(objectClass=user)(objectClass=group))"
            />
            <Button
              label="View Effective Access"
              icon="pi pi-shield"
              size="small"
              :loading="effectiveLoading"
              :disabled="!effectivePrincipalDn"
              @click="checkEffectivePermissions"
            />
          </div>
          <div v-if="effectiveChecked" class="effective-results">
            <div
              v-for="row in effectivePermissionRows"
              :key="row.permission"
              class="effective-row"
            >
              <i
                :class="row.granted ? 'pi pi-check-circle' : 'pi pi-times-circle'"
                :style="{ color: row.granted ? 'var(--app-success-text)' : 'var(--app-danger-text)', fontSize: '0.875rem' }"
              ></i>
              <span :class="{ 'eff-granted': row.granted, 'eff-denied': !row.granted }">
                {{ row.permission }}
              </span>
            </div>
          </div>
        </div>
      </Panel>

      <!-- Change Owner Dialog -->
      <Dialog
        v-model:visible="changeOwnerDialogVisible"
        header="Change Owner"
        modal
        :style="{ width: '500px' }"
      >
        <div style="margin-bottom: 1rem">
          <DnPicker
            v-model="newOwnerDn"
            label="Select new owner"
            objectFilter="(|(objectClass=user)(objectClass=group))"
          />
        </div>
        <template #footer>
          <Button label="Cancel" severity="secondary" text @click="changeOwnerDialogVisible = false" />
          <Button
            label="OK"
            icon="pi pi-check"
            :loading="changingOwner"
            :disabled="!newOwnerDn"
            @click="onChangeOwner"
          />
        </template>
      </Dialog>

      <!-- Advanced Dialog -->
      <Dialog
        v-model:visible="advancedDialogVisible"
        header="Advanced Security Settings"
        modal
        :style="{ width: '900px' }"
        :closable="true"
      >
        <div class="advanced-owner">
          <span class="owner-label">Owner:</span>
          <span class="owner-value">{{ ownerDisplay }}</span>
          <Button label="Change..." size="small" severity="secondary" text @click="changeOwnerDialogVisible = true; newOwnerDn = ''" />
        </div>

        <Tabs :value="advancedTab">
          <TabList>
            <Tab value="dacl">Permissions</Tab>
            <Tab value="sacl">Auditing</Tab>
            <Tab value="owner">Owner</Tab>
          </TabList>
          <TabPanels>
            <!-- DACL Tab -->
            <TabPanel value="dacl">
              <p style="font-size: 0.8125rem; color: var(--p-text-muted-color); margin: 0 0 0.5rem 0">Discretionary Access Control List — explicit and inherited permission entries for this object.</p>
              <DataTable
                :value="securityInfo.dacl"
                stripedRows
                size="small"
                scrollable
                scrollHeight="360px"
                :paginator="securityInfo.dacl.length > 50"
                :rows="50"
                v-model:expandedRows="expandedAceRows"
                dataKey="principal"
                selectionMode="single"
                @rowSelect="(e: any) => selectedAdvancedAceIndex = securityInfo!.dacl.indexOf(e.data)"
                @row-dblclick="(e: any) => onEditAce(e.data, securityInfo!.dacl.indexOf(e.data))"
                class="advanced-ace-table"
              >
                <Column header="Type" style="width: 100px">
                  <template #body="{ data: ace }: { data: AceInfo }">
                    <Tag :value="ace.type" :severity="aceSeverity(ace.type)" />
                  </template>
                </Column>
                <Column header="Principal" style="min-width: 160px">
                  <template #body="{ data: ace }: { data: AceInfo }">
                    <span>{{ ace.principalName || ace.principal }}</span>
                  </template>
                </Column>
                <Column header="Access" style="min-width: 160px">
                  <template #body="{ data: ace }: { data: AceInfo }">
                    <span style="font-size: 0.8125rem">{{ ace.permissions.join(', ') }}</span>
                  </template>
                </Column>
                <Column header="Inherited From" style="width: 130px">
                  <template #body="{ data: ace }: { data: AceInfo }">
                    <span style="font-size: 0.8125rem; color: var(--p-text-muted-color)">{{ inheritanceSource(ace) }}</span>
                  </template>
                </Column>
                <Column header="Applies To" style="min-width: 160px">
                  <template #body="{ data: ace }: { data: AceInfo }">
                    <span style="font-size: 0.8125rem; color: var(--p-text-muted-color)">{{ appliesTo(ace) }}</span>
                  </template>
                </Column>
              </DataTable>

              <!-- Advanced DACL actions -->
              <div class="advanced-dacl-actions">
                <Button label="Add..." icon="pi pi-plus" size="small" severity="secondary" @click="onAddAce" />
                <Button
                  label="Edit..."
                  icon="pi pi-pencil"
                  size="small"
                  severity="secondary"
                  :disabled="selectedAdvancedAceIndex == null"
                  @click="() => { if (selectedAdvancedAceIndex != null && securityInfo) onEditAce(securityInfo.dacl[selectedAdvancedAceIndex], selectedAdvancedAceIndex) }"
                />
                <Button
                  label="Remove"
                  icon="pi pi-trash"
                  size="small"
                  severity="danger"
                  :disabled="selectedAdvancedAceIndex == null"
                  @click="() => { if (selectedAdvancedAceIndex != null) onRemoveAce(selectedAdvancedAceIndex) }"
                />
              </div>
            </TabPanel>

            <!-- SACL Tab -->
            <TabPanel value="sacl">
              <p style="font-size: 0.8125rem; color: var(--p-text-muted-color); margin: 0 0 0.5rem 0">System Access Control List — audit entries that log access attempts to this object.</p>
              <Message severity="info" :closable="false" class="sacl-message">
                Auditing requires SACL write permission. SACL editing is read-only in this view.
              </Message>
              <div v-if="securityInfo.sacl.length === 0" style="text-align: center; padding: 2rem; color: var(--p-text-muted-color)">
                No audit entries configured.
              </div>
              <DataTable
                v-else
                :value="securityInfo.sacl"
                stripedRows
                size="small"
                scrollable
                scrollHeight="360px"
                class="advanced-ace-table"
              >
                <Column header="Type" style="width: 100px">
                  <template #body="{ data: ace }: { data: AceInfo }">
                    <Tag :value="ace.type" severity="info" />
                  </template>
                </Column>
                <Column header="Principal" style="min-width: 180px">
                  <template #body="{ data: ace }: { data: AceInfo }">
                    <span>{{ ace.principalName || ace.principal }}</span>
                  </template>
                </Column>
                <Column header="Access" style="min-width: 180px">
                  <template #body="{ data: ace }: { data: AceInfo }">
                    <span style="font-size: 0.8125rem">{{ ace.permissions.join(', ') }}</span>
                  </template>
                </Column>
                <Column header="Applies To" style="min-width: 180px">
                  <template #body="{ data: ace }: { data: AceInfo }">
                    <span style="font-size: 0.8125rem; color: var(--p-text-muted-color)">{{ appliesTo(ace) }}</span>
                  </template>
                </Column>
                <Column header="Flags" style="min-width: 150px">
                  <template #body="{ data: ace }: { data: AceInfo }">
                    <div style="display: flex; flex-wrap: wrap; gap: 0.25rem">
                      <Tag
                        v-for="flag in ace.flags"
                        :key="flag"
                        :value="flag"
                        severity="secondary"
                        style="font-size: 0.625rem"
                      />
                    </div>
                  </template>
                </Column>
              </DataTable>
            </TabPanel>

            <!-- Owner Tab -->
            <TabPanel value="owner">
              <p style="font-size: 0.8125rem; color: var(--p-text-muted-color); margin: 0 0 0.5rem 0">The owner of an object can always modify its permissions, regardless of other access control settings.</p>
              <div class="owner-tab-content">
                <div class="owner-current">
                  <div class="owner-current-label">Current owner:</div>
                  <div class="owner-current-value">{{ ownerDisplay }}</div>
                </div>
                <Button
                  label="Change Owner..."
                  icon="pi pi-user-edit"
                  severity="secondary"
                  @click="changeOwnerDialogVisible = true; newOwnerDn = ''"
                />
              </div>
            </TabPanel>
          </TabPanels>
        </Tabs>

        <template #footer>
          <Button label="Close" severity="secondary" @click="advancedDialogVisible = false" />
        </template>
      </Dialog>

      <!-- ACE Editor Dialog -->
      <AceEditor
        :visible="aceEditorVisible"
        :mode="aceEditorMode"
        :ace="editingAce"
        @update:visible="aceEditorVisible = $event"
        @save="onAceSave"
      />
    </template>

    <!-- No security descriptor -->
    <div v-else style="text-align: center; padding: 2rem; color: var(--p-text-muted-color)">
      <i class="pi pi-lock" style="font-size: 2rem; display: block; margin-bottom: 0.5rem"></i>
      No security descriptor available for this object.
    </div>
  </div>
</template>

<style scoped>
.security-tab {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.owner-bar {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.5rem 0.75rem;
  background: var(--p-surface-ground);
  border: 1px solid var(--app-neutral-border);
  border-radius: 0.375rem;
  font-size: 0.8125rem;
}

.owner-label {
  font-weight: 600;
  color: var(--p-text-color);
  white-space: nowrap;
}

.owner-value {
  color: var(--p-text-color);
  font-family: 'Cascadia Code', 'Fira Code', Consolas, monospace;
  font-size: 0.8125rem;
  word-break: break-all;
  flex: 1;
}

.inheritance-bar {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.375rem 0.75rem;
  background: var(--p-surface-ground);
  border: 1px solid var(--app-neutral-border);
  border-radius: 0.375rem;
  font-size: 0.8125rem;
}

.inherit-label {
  font-size: 0.8125rem;
  cursor: pointer;
  flex: 1;
}

.section-header {
  font-weight: 600;
  font-size: 0.875rem;
  color: var(--p-text-color);
  margin-bottom: 0.25rem;
}

/* Principal table */
.principal-table :deep(.p-datatable-tbody > tr) {
  cursor: pointer;
}

.principal-table :deep(.p-datatable-tbody > tr.p-highlight) {
  background: var(--app-info-bg) !important;
}

.principal-cell {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.principal-name {
  font-size: 0.8125rem;
  font-weight: 500;
}

.ace-badge {
  font-size: 0.625rem !important;
  padding: 0 0.375rem !important;
  line-height: 1.25rem !important;
}

.ace-actions {
  display: flex;
  gap: 0.5rem;
}

/* Permissions table */
.permissions-section {
  margin-top: 0.5rem;
}

.permissions-table :deep(.p-datatable-tbody > tr) {
  cursor: default;
}

.perm-name {
  font-size: 0.8125rem;
}

.perm-inherited {
  color: var(--p-text-muted-color);
}

.inherited-badge {
  font-size: 0.6875rem;
  color: var(--p-text-muted-color);
  margin-left: 0.375rem;
  font-style: italic;
}

.check-inherited :deep(.p-checkbox-box) {
  opacity: 0.7;
}

/* Bottom actions */
.bottom-actions {
  display: flex;
  justify-content: flex-end;
  gap: 0.5rem;
  margin-top: 0.25rem;
}

/* Effective permissions panel */
.effective-panel {
  margin-top: 0.5rem;
}

.effective-content {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.effective-picker {
  display: flex;
  align-items: flex-end;
  gap: 0.75rem;
  flex-wrap: wrap;
}

.effective-picker > :first-child {
  flex: 1;
  min-width: 200px;
}

.effective-results {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(220px, 1fr));
  gap: 0.375rem;
  padding: 0.75rem;
  background: var(--p-surface-ground);
  border: 1px solid var(--app-neutral-border);
  border-radius: 0.375rem;
}

.effective-row {
  display: flex;
  align-items: center;
  gap: 0.375rem;
  font-size: 0.8125rem;
}

.eff-granted {
  color: var(--app-success-text);
}

.eff-denied {
  color: var(--p-text-muted-color);
}

/* Advanced dialog */
.advanced-owner {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin-bottom: 0.75rem;
  font-size: 0.8125rem;
}

.advanced-ace-table :deep(.p-datatable-tbody > tr) {
  cursor: pointer;
}

.advanced-dacl-actions {
  display: flex;
  gap: 0.5rem;
  margin-top: 0.75rem;
}

.sacl-message {
  margin-bottom: 0.75rem;
}

/* Owner tab */
.owner-tab-content {
  display: flex;
  flex-direction: column;
  gap: 1rem;
  padding: 1rem 0;
}

.owner-current {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.owner-current-label {
  font-weight: 600;
  font-size: 0.875rem;
  color: var(--p-text-color);
}

.owner-current-value {
  font-family: 'Cascadia Code', 'Fira Code', Consolas, monospace;
  font-size: 0.875rem;
  color: var(--p-text-color);
  padding: 0.5rem 0.75rem;
  background: var(--p-surface-ground);
  border: 1px solid var(--app-neutral-border);
  border-radius: 0.375rem;
  word-break: break-all;
}
</style>
