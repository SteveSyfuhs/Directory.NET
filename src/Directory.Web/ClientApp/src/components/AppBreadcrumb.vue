<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import Breadcrumb from 'primevue/breadcrumb'

const route = useRoute()
const router = useRouter()

/** Map route path segments to human-readable labels */
const segmentLabels: Record<string, string> = {
  '': 'Dashboard',
  browse: 'Browse Directory',
  search: 'Advanced Search',
  users: 'Users',
  groups: 'Groups',
  computers: 'Computers',
  ous: 'OUs',
  recyclebin: 'Recycle Bin',
  audit: 'Audit Log',
  'ldap-audit': 'LDAP Protocol Audit',
  sites: 'Sites & Services',
  trusts: 'Trusts',
  replication: 'Replication',
  schema: 'Schema Browser',
  config: 'Domain Config',
  'password-policies': 'Password Policies',
  'account-lockout': 'Account Lockout',
  gpos: 'Group Policy',
  edit: 'Edit',
  'service-accounts': 'Service Accounts',
  gmsa: 'gMSA Management',
  certificates: 'Certificate Services',
  tls: 'TLS / LDAPS Certificate',
  dns: 'DNS Manager',
  mfa: 'MFA Management',
  settings: 'Settings',
  cluster: 'Cluster',
  nodes: 'Nodes',
  status: 'Status',
  'service-settings': 'Service Settings',
  backup: 'Backup & Export',
  ldif: 'LDIF Import/Export',
  delegation: 'Delegated Admin',
  'scheduled-tasks': 'Scheduled Tasks',
  webhooks: 'Webhooks',
  sysvol: 'SYSVOL',
  rodc: 'RODC Settings',
  'domain-join': 'Domain Join',
  'computer-prestaging': 'Computer Pre-staging',
  'join-verification': 'Join Verification',
  'dc-management': 'DC Management',
  'sspr-settings': 'SSPR Settings',
  'oauth-clients': 'OAuth / OIDC Clients',
  'saml-providers': 'SAML Providers',
  'scim-integrations': 'SCIM Integrations',
  'hr-sync': 'HR System Sync',
  workflows: 'Lifecycle Workflows',
  fido2: 'FIDO2 / WebAuthn',
  'conditional-access': 'Conditional Access',
  smartcard: 'Smart Card / PIV',
  migration: 'Migration Wizard',
  'ldap-proxy': 'LDAP Proxy',
  mdm: 'MDM Integrations',
  'multi-region': 'Multi-Region',
  pam: 'Privileged Access (PAM)',
  radius: 'RADIUS Server',
  'ssh-keys': 'SSH Key Management',
  compliance: 'Compliance Reports',
  'access-reviews': 'Access Reviews',
  'data-retention': 'Data Retention',
}

const homeItem = {
  label: 'Dashboard',
  icon: 'pi pi-home',
  command: () => router.push('/'),
}

const breadcrumbItems = computed(() => {
  const path = route.path

  // Dashboard: no extra items
  if (path === '/') return []

  const segments = path.split('/').filter(Boolean)
  const items: { label: string; command?: () => void }[] = []
  let builtPath = ''

  for (let i = 0; i < segments.length; i++) {
    const seg = segments[i]
    builtPath += '/' + seg

    // Check if this segment looks like a UUID / GUID / numeric ID — label it as an ID breadcrumb
    const isId = /^[0-9a-f-]{8,}$/i.test(seg) || /^\d+$/.test(seg)
    const label = isId ? `#${seg.substring(0, 8)}…` : (segmentLabels[seg] ?? seg)

    const capturedPath = builtPath
    // Last segment is not clickable (current page)
    if (i < segments.length - 1) {
      items.push({
        label,
        command: () => router.push(capturedPath),
      })
    } else {
      items.push({ label })
    }
  }

  return items
})

// Only show breadcrumb when not on the dashboard
const showBreadcrumb = computed(() => route.path !== '/')
</script>

<template>
  <Breadcrumb
    v-if="showBreadcrumb"
    :home="homeItem"
    :model="breadcrumbItems"
    class="app-breadcrumb"
  />
</template>

<style scoped>
.app-breadcrumb {
  background: transparent !important;
  border: none !important;
  padding: 0 0 0.75rem 0 !important;
  font-size: 0.875rem;
}

.app-breadcrumb :deep(.p-breadcrumb-list) {
  flex-wrap: wrap;
}

.app-breadcrumb :deep(.p-breadcrumb-item-link) {
  color: var(--p-text-muted-color);
  font-size: 0.875rem;
}

.app-breadcrumb :deep(.p-breadcrumb-item-link:hover) {
  color: var(--app-accent-color);
}

.app-breadcrumb :deep(.p-breadcrumb-item:last-child .p-breadcrumb-item-link) {
  color: var(--p-text-color);
  font-weight: 600;
  pointer-events: none;
}
</style>
